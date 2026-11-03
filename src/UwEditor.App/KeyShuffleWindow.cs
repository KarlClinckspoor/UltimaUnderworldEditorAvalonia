using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Key item shuffle: the key items (keys, the Key of Infinity's parts, the
    /// talismans lying as objects, runestones, ability items, the boots' ingredients, plot items)
    /// move to new places so that the game stays beatable under the chosen logic (assumed fill on
    /// the automatic zones' time-aware solver), written into the levels as whole records, one
    /// undoable step a level; Undo shuffle puts every byte back. Preview shows the spoiler log
    /// without changing anything. UW1 only.
    /// </summary>
    public sealed class KeyShuffleWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly KeyShuffleOptions opt = new KeyShuffleOptions();
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly NumericUpDown retries = new NumericUpDown { Minimum = 0, Maximum = 200, Increment = 1, FormatString = "0", Width = 110 };
        readonly Button previewButton = new Button { Content = "Preview" };
        readonly Button shuffleButton = new Button { Content = "Shuffle", FontWeight = FontWeight.SemiBold };
        readonly Button undoButton = new Button { Content = "Undo shuffle", IsEnabled = false };
        readonly Button exportButton = new Button { Content = "Export spoiler log…", IsEnabled = false };
        readonly List<KeyShuffleResult> applied = new List<KeyShuffleResult>();
        bool busy;

        /// <summary>The last preview or shuffle, or null.</summary>
        public KeyShuffleResult Result { get; private set; }
        /// <summary>The options of the next run.</summary>
        public KeyShuffleOptions Options { get { return opt; } }
        /// <summary>The spoiler log on show.</summary>
        public string Spoiler { get { return report.Text ?? ""; } }

        public KeyShuffleWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Key item shuffle";
            Width = 1000; Height = 900; MinWidth = 680; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt.Nav.MaxClimb = main.ZoneClimb;
            opt.Nav.JumpSpellClimb = 2 * opt.Nav.MaxClimb;

            var top = new StackPanel { Spacing = 6 };
            top.Children.Add(new TextBlock { Text = "Key item shuffle", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Moves the key items to new places so that the game stays beatable with the logic chosen below. The automatic zones and their time-aware solver decide what is reachable: " +
                       "the key items are placed one by one, each where the solver reaches while the ones not placed yet are counted as held (assumed fill), and the other items fill the rest. " +
                       "A fresh solve of the shuffled levels must reach level 9 with no one-way trap; otherwise the next seed is tried. Items trade places whole (keys keep their lock numbers). " +
                       "Nothing is written until Shuffle, one undoable step a level, saved with the levels." + (s.IsUw2 ? " Ultima Underworld 1 only: its facts and solver." : ""),
            });
            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            retries.Value = opt.Retries;
            retries.ValueChanged += (o, e) => { opt.Retries = (int)(retries.Value ?? 20); };
            top.Children.Add(LogicOptionsPanel.Row(LogicOptionsPanel.Label("Seed"), seed, newSeed, LogicOptionsPanel.Label("Retries", 60), retries, LogicOptionsPanel.Note("further seeds tried when a fill fails the check")));
            // the logic options, as the Automatic zones window has them
            top.Children.Add(new LogicOptionsPanel(opt.Nav, opt.Logic, true));
            var cats = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in KeyShuffle.Categories)
            {
                string cat = c;
                var box = new CheckBox { Content = KeyShuffle.CategoryName(c), IsChecked = opt.Move.Contains(c), Margin = new Thickness(0, 0, 12, 0) };
                box.IsCheckedChanged += (o, e) => { if (box.IsChecked == true) opt.Move.Add(cat); else opt.Move.Remove(cat); };
                cats.Children.Add(box);
            }
            var moveRow = new DockPanel();
            var moveLabel = LogicOptionsPanel.Label("Move");
            moveLabel.VerticalAlignment = VerticalAlignment.Top;
            moveLabel.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(moveLabel, Dock.Left);
            moveRow.Children.Add(moveLabel);
            moveRow.Children.Add(cats);
            top.Children.Add(moveRow);
            var creatures = new CheckBox { Content = "Creature inventories (creatures without a conversation)", IsChecked = opt.CreatureInventories };
            creatures.IsCheckedChanged += (o, e) => opt.CreatureInventories = creatures.IsChecked == true;
            var filler = new CheckBox { Content = "Everything else as filler (weapons, armour, food, coins, lights …)", IsChecked = opt.Filler };
            filler.IsCheckedChanged += (o, e) => opt.Filler = filler.IsChecked == true;
            top.Children.Add(LogicOptionsPanel.Row(LogicOptionsPanel.Label(""), creatures, filler));

            previewButton.Click += async (o, e) => await RunAsync(false);
            shuffleButton.Click += async (o, e) => await RunAsync(true);
            undoButton.Click += (o, e) => UndoNow();
            exportButton.Click += (o, e) => Export();
            ToolTip.SetTip(previewButton, "Computes a shuffle and shows its spoiler log; nothing changes.");
            ToolTip.SetTip(shuffleButton, "Writes the shuffle shown (or a new one for these settings) into the levels.");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 4) };
            buttons.Children.Add(previewButton); buttons.Children.Add(shuffleButton); buttons.Children.Add(undoButton); buttons.Children.Add(exportButton);
            var actions = new StackPanel();
            actions.Children.Add(buttons);
            actions.Children.Add(summary);
            if (s.IsUw2) { previewButton.IsEnabled = shuffleButton.IsEnabled = false; summary.Text = "Ultima Underworld 1 only."; }

            // the settings scroll when the window is short; the buttons stay in sight, the log takes the rest
            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            var scroll = new ScrollViewer { Content = top, MaxHeight = 560, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            Grid.SetRow(actions, 1);
            Grid.SetRow(report, 2);
            root.Children.Add(scroll); root.Children.Add(actions); root.Children.Add(report);
            Content = root;
            ManualWindow.Attach(this, "Key item shuffle");
        }

        KeyShuffleResult Compute()
        {
            return KeyShuffle.Run(session, main.ViewModel.Edits, opt);
        }

        /// <summary>The last result still fits: computed with these settings from the levels as they are now.</summary>
        bool Fits(KeyShuffleResult r)
        {
            if (r == null || !r.Success || r.IsApplied) return false;
            var o = r.Options;
            if (o.Seed != opt.Seed || o.Retries != opt.Retries || o.Filler != opt.Filler || o.CreatureInventories != opt.CreatureInventories || !o.Move.SetEquals(opt.Move)) return false;
            if (o.Logic.ToString() != opt.Logic.ToString() || o.Nav.MaxClimb != opt.Nav.MaxClimb || o.Nav.JumpSpellClimb != opt.Nav.JumpSpellClimb || o.Nav.GapJumps != opt.Nav.GapJumps
                || o.Nav.GapTiles != opt.Nav.GapTiles || o.Nav.JumpSpellGapTiles != opt.Nav.JumpSpellGapTiles || o.Nav.DiagonalJumps != opt.Nav.DiagonalJumps
                || o.Nav.CutAtLevelChanges != opt.Nav.CutAtLevelChanges || o.Nav.FoldEmptyDeadEnds != opt.Nav.FoldEmptyDeadEnds) return false;
            var edits = main.ViewModel.Edits;
            foreach (var kv in r.Before)
            {
                var e = edits.Existing(kv.Key);
                byte[] now = e != null ? e.Data : session.LoadLevel(kv.Key).Block.Data;
                if (!now.AsSpan().SequenceEqual(kv.Value)) return false;
            }
            return true;
        }

        void Show(KeyShuffleResult r, string prefix)
        {
            Result = r;
            report.Text = r.Log.ToString();
            exportButton.IsEnabled = r.Log.Length > 0;
            if (r.Refused != null) { summary.Text = r.Refused; return; }
            summary.Text = prefix + (r.Success
                ? string.Format("Seed {0}{1}: {2} items move ({3} key items), {4} levels change; the shuffled game reaches level 9 in {5} spheres (the original in {6}).{7}",
                    r.UsedSeed, r.UsedSeed != r.Options.Seed ? " (after " + (r.Attempts - 1) + " failed)" : "", r.Moves.Count(m => m.Item != m.Place), r.Moves.Count(m => m.ByFill && m.Item != m.Place), r.After.Count,
                    r.SpheresAfter, r.SpheresBefore, r.Unlocked.Count > 0 ? " " + r.Unlocked.Count + " door(s) unlocked." : "")
                : "No shuffle passed the check in " + r.Attempts + " attempts: see the log.");
        }

        /// <summary>Preview: computes a shuffle and shows its spoiler log; nothing changes.</summary>
        public KeyShuffleResult PreviewNow()
        {
            try { var r = Compute(); Show(r, "Preview. "); return r; }
            catch (Exception ex) { summary.Text = "Could not shuffle: " + ex.Message; return null; }
        }

        /// <summary>Shuffle: writes the result on show when it still fits, or a new one, into the levels (one undoable step a level).</summary>
        public bool ShuffleNow()
        {
            try
            {
                var r = Fits(Result) ? Result : Compute();
                return ApplyResult(r);
            }
            catch (Exception ex) { summary.Text = "Could not shuffle: " + ex.Message; return false; }
        }

        bool ApplyResult(KeyShuffleResult r)
        {
            if (r == null || !r.Success) { if (r != null) Show(r, ""); return false; }
            KeyShuffle.Apply(r, main.ViewModel.Edits);
            applied.Add(r);
            undoButton.IsEnabled = true;
            Show(r, "Shuffled (not saved yet). ");
            return true;
        }

        async Task RunAsync(bool apply)
        {
            if (busy) return;
            if (apply && Fits(Result)) { ApplyResult(Result); return; }
            busy = true;
            previewButton.IsEnabled = shuffleButton.IsEnabled = false;
            summary.Text = "Shuffling… (the solver runs once per key item, then checks the result)";
            try
            {
                KeyShuffleResult r = null;
                string error = null;
                var snap = KeyShuffle.Snapshot(session, main.ViewModel.Edits);
                var o = opt.Clone();
                await Task.Run(() => { try { r = KeyShuffle.RunOn(session, snap, o); } catch (Exception ex) { error = ex.Message; } });
                if (error != null) { summary.Text = "Could not shuffle: " + error; return; }
                if (apply) ApplyResult(r); else Show(r, "Preview. ");
            }
            finally
            {
                busy = false;
                previewButton.IsEnabled = shuffleButton.IsEnabled = !session.IsUw2;
            }
        }

        /// <summary>Undo shuffle: puts the levels of the last shuffle back byte for byte (a level edited since is left alone; Edit ▸ Undo still steps back there).</summary>
        public bool UndoNow()
        {
            if (applied.Count == 0) return false;
            var r = applied[applied.Count - 1];
            applied.RemoveAt(applied.Count - 1);
            var skipped = KeyShuffle.Undo(r, main.ViewModel.Edits);
            undoButton.IsEnabled = applied.Count > 0;
            summary.Text = skipped.Count == 0 ? "The shuffle of seed " + r.UsedSeed + " is undone." :
                "Undone, except level(s) " + string.Join(", ", skipped) + ", edited since (use Edit ▸ Undo there).";
            Result = null;
            return skipped.Count == 0;
        }

        async void Export()
        {
            if (Result == null || Result.Log.Length == 0) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export the spoiler log", SuggestedFileName = "uw1-key-shuffle-" + (Result.Success ? Result.UsedSeed : Result.Options.Seed) + ".txt", DefaultExtension = "txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } } },
            });
            string path = f?.TryGetLocalPath();
            if (path == null) return;
            try { ExportTo(path); summary.Text = "Exported " + path + "."; }
            catch (Exception ex) { summary.Text = "Could not export: " + ex.Message; }
        }

        /// <summary>Writes the spoiler log on show to a text file.</summary>
        public void ExportTo(string path) { File.WriteAllText(path, Result?.Log.ToString() ?? ""); }
    }
}
