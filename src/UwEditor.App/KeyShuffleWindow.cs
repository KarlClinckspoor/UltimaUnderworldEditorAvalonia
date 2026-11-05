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
    /// Randomizer ▸ Scatter ▸ Apply (the key item shuffle): the key items (keys, the Key of Infinity's parts, the
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
        readonly Button shuffleButton = new Button { Content = "Apply", FontWeight = FontWeight.SemiBold };
        readonly Button undoButton = new Button { Content = "Undo apply", IsEnabled = false };
        readonly Button exportButton = new Button { Content = "Export spoiler log…", IsEnabled = false };
        readonly List<KeyShuffleResult> applied = new List<KeyShuffleResult>();
        bool busy;
        // the zoning the solver uses: the Zones window's
        readonly ZoningState zoning;
        readonly TextBlock zoningText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        readonly Button quickButton = new Button { Content = "Quick autozone" };
        bool quickRunning, namesFromZoning;
        readonly CheckBox kitBox = new CheckBox { Content = "A full torch (quality 63) and the map beside the start" };
        readonly CheckBox mapsBox = new CheckBox { Content = "Every map square explored" };

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
            Title = "Apply";
            Width = 1000; Height = 960; MinWidth = 680; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt.Nav.MaxClimb = main.ZoneClimb;
            opt.Nav.JumpSpellClimb = 2 * opt.Nav.MaxClimb;

            var top = new StackPanel { Spacing = 6 };
            top.Children.Add(new TextBlock { Text = "Apply", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Moves the key items to new places so that the game stays beatable with the logic chosen below. The zones (the Zones window's zoning, or automatic zones when there is none) and the time-aware solver decide what is reachable. " +
                       "The key items are placed one by one, each somewhere the solver can reach while it counts the items not yet placed as held (assumed fill); the other items fill the rest. " +
                       "A fresh solve of the shuffled levels must then reach level 9 without a one-way trap, or the next seed is tried. Items trade places whole (keys keep their lock numbers). " +
                       "Nothing is written until you press Apply, which makes one undoable step per level; the changes are saved with the levels." + (s.IsUw2 ? " This works for Ultima Underworld 1 only, whose facts and solver it uses." : ""),
            });
            // the zoning: the Zones window's, or Quick autozone when there is none
            zoning = main.Zoning();
            quickButton.Click += async (o, e) => await QuickAutozoneAsync();
            ToolTip.SetTip(quickButton, "Makes automatic zones with the movement options below and puts them in the Zones window, where you can look at and change them; Apply then uses them. Available only when there is no zoning yet.");
            var zonesButton = new Button { Content = "Zones window…" };
            zonesButton.Click += (o, e) => main.ShowZones();
            var head = new OptionGrid(1);
            head.AddRow("Zones", LogicOptionsPanel.Row(zoningText, quickButton, zonesButton));
            top.Children.Add(head);
            if (zoning != null)
            {
                zoning.Changed += OnZoningChanged;
                Closed += (o, e) => zoning.Changed -= OnZoningChanged;
            }
            RefreshZoning();
            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            retries.Value = opt.Retries;
            retries.ValueChanged += (o, e) => { opt.Retries = (int)(retries.Value ?? 20); };
            head.AddRow("Seed", LogicOptionsPanel.Row(seed, newSeed, LogicOptionsPanel.Label("Retries", 60), retries, LogicOptionsPanel.Note("How many further seeds to try when a fill fails the check.")));
            // the logic options, as the Automatic zones window has them
            top.Children.Add(new LogicOptionsPanel(opt.Nav, opt.Logic, true));
            // what moves, three to a row, lined up with the options above
            top.Children.Add(LogicOptionsPanel.Heading("THE SCATTER", "what moves, and what Apply adds"));
            var moves = new OptionGrid(3);
            var boxes = KeyShuffle.Categories.Select(c =>
            {
                string cat = c;
                var box = new CheckBox { Content = KeyShuffle.CategoryName(c), IsChecked = opt.Move.Contains(c) };
                box.IsCheckedChanged += (o, e) => { if (box.IsChecked == true) opt.Move.Add(cat); else opt.Move.Remove(cat); };
                return (Control)box;
            }).ToList();
            for (int i = 0; i < boxes.Count; i += 3) moves.AddRow(i == 0 ? "Move" : "", boxes.Skip(i).Take(3).Concat(Enumerable.Repeat<Control>(null, Math.Max(0, 3 - (boxes.Count - i)))).ToArray());
            var creatures = new CheckBox { Content = "Creature inventories (creatures without a conversation)", IsChecked = opt.CreatureInventories };
            creatures.IsCheckedChanged += (o, e) => opt.CreatureInventories = creatures.IsChecked == true;
            var filler = new CheckBox { Content = "Everything else as filler (weapons, armour, food, coins, lights…)", IsChecked = opt.Filler };
            filler.IsCheckedChanged += (o, e) => opt.Filler = filler.IsChecked == true;
            top.Children.Add(moves);
            // the longer choices in a grid of their own, so that the categories' columns stay close
            var more = new OptionGrid(2);
            more.AddRow("Also", creatures, filler);
            kitBox.IsChecked = opt.StartKit;
            kitBox.IsCheckedChanged += (o, e) => opt.StartKit = kitBox.IsChecked == true;
            ToolTip.SetTip(kitBox, "At Apply, a torch of quality 63 and the map are placed beside the start square (the editor's, or the game's own when \"Consider the current starting square\" is off). A torch or map already there is filled up or kept; a missing one is put on the floor beside the start.");
            mapsBox.IsChecked = opt.ExploreMaps;
            mapsBox.IsCheckedChanged += (o, e) => opt.ExploreMaps = mapsBox.IsChecked == true;
            ToolTip.SetTip(mapsBox, "At Apply, every level's map shows every square, as if the player had seen it all; the map notes are kept.");
            more.AddRow("At the start", kitBox, mapsBox);
            top.Children.Add(more);

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
            if (s.IsUw2) { previewButton.IsEnabled = shuffleButton.IsEnabled = false; summary.Text = "This works for Ultima Underworld 1 only."; }

            // the settings scroll when the window is short; the buttons stay in sight, the log takes the rest
            var root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,*") };
            var scroll = new ScrollViewer { Content = top, MaxHeight = 560, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            // the settings take what the window can spare, the log keeps at least 220 pixels
            SizeChanged += (o, e) => scroll.MaxHeight = Math.Max(240, e.NewSize.Height - 300);
            Grid.SetRow(actions, 1);
            Grid.SetRow(report, 2);
            root.Children.Add(scroll); root.Children.Add(actions); root.Children.Add(report);
            Content = root;
            ManualWindow.Attach(this, "Apply");
        }

        KeyShuffleResult Compute()
        {
            PrepareZoning();
            return KeyShuffle.Run(session, main.ViewModel.Edits, opt);
        }

        // ---- the zoning

        /// <summary>Whether the next run uses the Zones window's zoning (it has zones), rather than automatic zones.</summary>
        public bool UsesZoning { get { return zoning != null && zoning.HasZones; } }

        public Button QuickAutozoneButton { get { return quickButton; } }

        void OnZoningChanged(string what) { if (what != "notes") RefreshZoning(); }

        void RefreshZoning()
        {
            if (UsesZoning)
            {
                zoningText.Text = string.Format("The Zones window's zoning: {0}{1}, {2} zones{3}.", Path.GetFileName(zoning.Path), zoning.Dirty ? " (its unsaved changes included)" : "", zoning.ZoneCount,
                    (AutoZones.IsAutomatic(zoning.Set) ? ", made by the automatic zones" : "") + (zoning.SplitByLevel ? "; multi-level zones split by level (the Zones window's option)" : ""));
                quickButton.IsEnabled = false;
            }
            else
            {
                zoningText.Text = "There is no zoning yet, so Apply makes automatic zones from the movement options below. Quick autozone makes them now and puts them in the Zones window.";
                quickButton.IsEnabled = !quickRunning && !session.IsUw2 && zoning != null;
            }
        }

        /// <summary>Sets the options' zoning (a copy of the Zones window's, or none) and the names of the places in the log.</summary>
        void PrepareZoning()
        {
            if (UsesZoning)
            {
                opt.Zoning = zoning.ForSolver();
                opt.ZoningFrom = Path.GetFileName(zoning.Path) + (zoning.Dirty ? " with its unsaved changes" : "") + (zoning.SplitByLevel ? ", multi-level zones split by level" : "");
                // a painted zoning names the places itself; an automatic one by the hand-made zones, as before
                if (!AutoZones.IsAutomatic(opt.Zoning)) { opt.Names = opt.Zoning; opt.NamesFrom = "the zoning (" + opt.ZoningFrom + ")"; namesFromZoning = true; return; }
            }
            else { opt.Zoning = null; opt.ZoningFrom = ""; }
            if (opt.Names == null || namesFromZoning) { namesFromZoning = false; LoadNames(); }
        }

        /// <summary>Quick autozone: the automatic zones with the movement options here, put in the Zones window (only when it has no zoning).</summary>
        public async Task<bool> QuickAutozoneAsync()
        {
            if (quickRunning || zoning == null || UsesZoning || session.IsUw2) return false;
            quickRunning = true;
            quickButton.IsEnabled = false;
            summary.Text = "Making the automatic zones…";
            ZoneSet made = null;
            string error = null;
            var snap = KeyShuffle.Snapshot(session, main.ViewModel.Edits);
            var nav = opt.Nav.Clone();
            var logic = opt.Logic.Clone();
            await Task.Run(() => { try { made = Timeline.AutoZoning(session, snap.Level, nav, logic); } catch (Exception ex) { error = ex.Message; } });
            quickRunning = false;
            if (error != null) { summary.Text = "Could not make the zones: " + error; RefreshZoning(); return false; }
            // a zoning opened or painted in the Zones window while these were made is not replaced without asking
            if (UsesZoning)
            {
                string a = await MessageWindow.Ask(this, "Quick autozone", "The Zones window got a zoning while the automatic zones were being made (" + Path.GetFileName(zoning.Path) + "). Replace it with the automatic zones?", "Keep it", "Replace");
                if (a != "Replace") { summary.Text = "The automatic zones were made, but the Zones window's zoning was kept."; RefreshZoning(); return false; }
            }
            zoning.Adopt(made,Path.Combine(session.DataFolder, AutoZones.DefaultFileName(session.IsUw2)));
            summary.Text = string.Format("{0} automatic zones made and put in the Zones window. They are not saved yet; saving there asks for a name. Apply now uses them.", zoning.ZoneCount);
            RefreshZoning();
            return true;
        }

        /// <summary>The hand-made zones whose names label the spoiler log: the zone file in DATA, else Karl's painted UW1 map shipped with the editor.</summary>
        void LoadNames()
        {
            try
            {
                string path = System.IO.Path.Combine(session.DataFolder, ZoneSet.DefaultFileName(session.IsUw2));
                if (System.IO.File.Exists(path)) { opt.Names = ZoneSet.Load(path, session.Profile.LevelSlots); opt.NamesFrom = "the zone file in DATA (" + System.IO.Path.GetFileName(path) + ")"; return; }
                if (session.IsUw2) return;
                using (var st = typeof(ZoningState).Assembly.GetManifestResourceStream("uw1-zones.example.json"))
                using (var r = new System.IO.StreamReader(st))
                {
                    opt.Names = ZoneSet.FromJson(r.ReadToEnd(), session.Profile.LevelSlots);
                    opt.NamesFrom = "Karl's painted UW1 map";
                }
            }
            catch (Exception) { opt.Names = null; }
        }

        /// <summary>The last result still fits: computed with these settings from the levels as they are now.</summary>
        bool Fits(KeyShuffleResult r)
        {
            if (r == null || !r.Success || r.IsApplied) return false;
            var o = r.Options;
            if (o.Seed != opt.Seed || o.Retries != opt.Retries || o.Filler != opt.Filler || o.CreatureInventories != opt.CreatureInventories || !o.Move.SetEquals(opt.Move)) return false;
            if (o.Logic.ToString() != opt.Logic.ToString() || o.Nav.MaxClimb != opt.Nav.MaxClimb || o.Nav.JumpSpellClimb != opt.Nav.JumpSpellClimb || o.Nav.GapJumps != opt.Nav.GapJumps
                || o.Nav.GapTiles != opt.Nav.GapTiles || o.Nav.JumpSpellGapTiles != opt.Nav.JumpSpellGapTiles || o.Nav.DiagonalJumps != opt.Nav.DiagonalJumps
                || o.Nav.CutAtLevelChanges != opt.Nav.CutAtLevelChanges || o.Nav.FoldEmptyDeadEnds != opt.Nav.FoldEmptyDeadEnds || o.Nav.ShippedStart != opt.Nav.ShippedStart) return false;
            // the zoning as it is now
            if ((o.Zoning != null) != UsesZoning || (o.Zoning != null && o.Zoning.ToJson() != zoning.ForSolver().ToJson())) return false;
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
                : "No shuffle passed the check in " + r.Attempts + " attempts; see the log.");
        }

        /// <summary>Preview: computes a shuffle and shows its spoiler log; nothing changes.</summary>
        public KeyShuffleResult PreviewNow()
        {
            try { var r = Compute(); Show(r, "Preview. "); return r; }
            catch (Exception ex) { summary.Text = "Could not apply: " + ex.Message; return null; }
        }

        /// <summary>Shuffle: writes the result on show when it still fits, or a new one, into the levels (one undoable step a level).</summary>
        public bool ShuffleNow()
        {
            try
            {
                var r = Fits(Result) ? Result : Compute();
                return ApplyResult(r);
            }
            catch (Exception ex) { summary.Text = "Could not apply: " + ex.Message; return false; }
        }

        bool ApplyResult(KeyShuffleResult r)
        {
            if (r == null || !r.Success) { if (r != null) Show(r, ""); return false; }
            // the helps are done at Apply, with the choices as they are now
            r.Options.StartKit = opt.StartKit;
            r.Options.ExploreMaps = opt.ExploreMaps;
            KeyShuffle.Apply(r, main.ViewModel.Edits);
            if (r.AppliedAids.Count > 0)
            {
                r.Log.AppendLine();
                r.Log.AppendLine("AT APPLY");
                foreach (var l in r.AppliedAids) r.Log.AppendLine("  " + l);
            }
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
            summary.Text = "Applying… (the solver runs once per key item, then checks the result)";
            try
            {
                KeyShuffleResult r = null;
                string error = null;
                var snap = KeyShuffle.Snapshot(session, main.ViewModel.Edits);
                PrepareZoning();
                var o = opt.Clone();
                await Task.Run(() => { try { r = KeyShuffle.RunOn(session, snap, o); } catch (Exception ex) { error = ex.Message; } });
                if (error != null) { summary.Text = "Could not apply: " + error; return; }
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
                "Undone, except level(s) " + string.Join(", ", skipped) + ", which were edited since; use Edit ▸ Undo there.";
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
