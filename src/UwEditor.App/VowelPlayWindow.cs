using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Vowel play: the names to change (one per line, saved per game), a seed,
    /// "Keep a first vowel", Preview, Apply and Undo. The vowels of each name are drawn again
    /// (VowelPlay) and every string naming it follows, in the shared STRINGS.PAK edits. Nothing is
    /// written to the game until you save.
    /// </summary>
    public sealed class VowelPlayWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly StringsEditor strings;
        readonly ConversationLibrary conversations;
        readonly VowelPlayOptions opt;
        readonly TextBox report = ItemToolLayout.TextArea(false);
        readonly TextBox names = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 12 };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly CheckBox keepFirst = new CheckBox { Content = "Keep a first vowel (“Illomo” stays I…)" };
        readonly Button undoButton = new Button { Content = "Undo vowel play", IsEnabled = false };
        readonly List<VowelPlayResult> applied = new List<VowelPlayResult>();
        HashSet<string> protectedWords;

        public VowelPlayWindow(MainWindow main, GameSession s, StringsEditor strings, ConversationLibrary conversations)
        {
            this.main = main;
            session = s;
            this.strings = strings;
            this.conversations = conversations;
            Title = "Vowel play";
            Width = 980; Height = 760; MinWidth = 640; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt = Load();

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Vowel play", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Each name gets its vowels drawn again (“Garamon” might become “Gorumen”): same length, same consonants, same capitals. A name gets one new spelling, used in every string: the names creatures talk under, conversations, books, scrolls and cutscenes. " +
                       "The mantras, the passwords and every word a conversation compares typed text against (“Deco Morono”, “Folanae”) never change, and a new spelling is never a word the game already uses. The same seed gives the same names.",
            });

            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); Save(); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            keepFirst.IsChecked = opt.KeepFirstLetter;
            keepFirst.IsCheckedChanged += (o, e) => { opt.KeepFirstLetter = keepFirst.IsChecked == true; Save(); };
            var seedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            seedRow.Children.Add(new TextBlock { Text = "Seed", Width = 40, VerticalAlignment = VerticalAlignment.Center });
            seedRow.Children.Add(seed); seedRow.Children.Add(newSeed); seedRow.Children.Add(keepFirst);
            head.Children.Add(seedRow);

            // the names, one per line
            names.Text = string.Join("\n", opt.Names ?? VowelPlay.DefaultNames(strings));
            names.TextChanged += (o, e) => { opt.Names = NameList(); Save(); };
            var defaults = new Button { Content = "The game's names" };
            defaults.Click += (o, e) => { names.Text = string.Join("\n", VowelPlay.DefaultNames(strings)); opt.Names = null; Save(); };
            var left = new DockPanel { Width = 230, Margin = new Thickness(0, 0, 12, 0) };
            var leftHead = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            leftHead.Children.Add(new TextBlock { Text = "Names (one per line)", FontWeight = FontWeight.SemiBold });
            leftHead.Children.Add(new TextBlock { Text = "Add or remove names; capitals count.", FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            DockPanel.SetDock(leftHead, Dock.Top); left.Children.Add(leftHead);
            DockPanel.SetDock(defaults, Dock.Bottom); defaults.Margin = new Thickness(0, 6, 0, 0); left.Children.Add(defaults);
            left.Children.Add(names);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => { var p = Protected(); if (p != null) Show(VowelPlay.Preview(strings, p, opt).Report(false)); };
            var apply = new Button { Content = "Apply", FontWeight = FontWeight.SemiBold };
            apply.Click += (o, e) => ApplyNames();
            undoButton.Click += (o, e) => UndoNames();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            bar.Children.Add(preview); bar.Children.Add(apply); bar.Children.Add(undoButton);
            bar.Children.Add(new TextBlock { Text = "The strings change in the editor; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            report.Text = "Press Preview to see the new names and the texts that would change, or Apply to change them.";
            var right = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top); right.Children.Add(bar);
            right.Children.Add(report);

            var body = new DockPanel();
            DockPanel.SetDock(left, Dock.Left); body.Children.Add(left);
            body.Children.Add(right);

            var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,*") };
            Grid.SetRow(head, 0); grid.Children.Add(head);
            Grid.SetRow(body, 1); grid.Children.Add(body);
            Content = grid;
            ManualWindow.Attach(this, "Vowel play");
            Closed += (o, e) => Save();
        }

        public VowelPlayOptions Options { get { return opt; } }
        public string ReportText { get { return report.Text; } }

        List<string> NameList()
        {
            return (names.Text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        /// <summary>The words that never change: read once (the conversations are decompiled for their compare and contains texts).</summary>
        HashSet<string> Protected()
        {
            if (protectedWords != null) return protectedWords;
            try { protectedWords = VowelPlay.ProtectedWords(strings, conversations); }
            catch (Exception ex) { Show("The protected words could not be read: " + ex.Message); return null; }
            return protectedWords;
        }

        void Show(string text) { report.Text = text; }

        void ApplyNames()
        {
            var p = Protected();
            if (p == null) return;
            VowelPlayResult r;
            try { r = VowelPlay.Apply(strings, p, opt); }
            catch (Exception ex) { Show("The names could not be changed: " + ex.Message); return; }
            if (r.Edits.Count == 0) { Show(r.Report(false)); return; }
            applied.Add(r);
            undoButton.IsEnabled = true;
            Show(r.Report(true));
            main.MantrasChanged("Vowel play applied (seed " + r.Seed + "). Undo vowel play in its window puts the names back; File ▸ Save writes STRINGS.PAK.");
        }

        void UndoNames()
        {
            if (applied.Count == 0) return;
            var r = applied[applied.Count - 1];
            applied.RemoveAt(applied.Count - 1);
            int left = VowelPlay.Undo(strings, r);
            undoButton.IsEnabled = applied.Count > 0;
            Show("Names put back (seed " + r.Seed + ")." + (left > 0 ? " " + left + " string" + (left == 1 ? " was" : "s were") + " changed since and stay as they are." : ""));
            main.MantrasChanged("Vowel play undone.");
        }

        string PathOf() { return Path.Combine(Settings.Folder, "vowel-play-" + (session.IsUw2 ? "uw2" : "uw1") + ".json"); }

        VowelPlayOptions Load()
        {
            try { if (File.Exists(PathOf())) { var o = VowelPlayOptions.FromJson(File.ReadAllText(PathOf())); if (o != null) return o; } } catch (Exception) { }
            return new VowelPlayOptions { Seed = new Random().Next(1, int.MaxValue) };
        }

        void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(PathOf(), opt.ToJson()); } catch (Exception) { }
        }
    }
}
