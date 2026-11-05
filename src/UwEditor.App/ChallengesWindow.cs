using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Named challenges: the challenges in alphabetical order, each with a check box,
    /// a line saying what it does and its options (Fasting's food remaining, Ranger's launcher
    /// weights), a seed, the special creatures, Preview, Apply and Undo. Apply puts the levels in
    /// one Edit ▸ Undo step each; OBJECTS.DAT's creature table changes go into the object tables
    /// window as its unsaved edits; a save's player changes go into the player sheet. Nothing is
    /// written to the game until you save.
    /// </summary>
    public sealed class ChallengesWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly ChallengeOptions opt;
        readonly TextBox report = ItemToolLayout.TextArea(false);
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo challenges", IsEnabled = false };
        readonly TextBlock conflict = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x50, 0x3C)) };
        readonly List<ChallengeApplied> applied = new List<ChallengeApplied>();
        readonly Dictionary<Challenge, CheckBox> boxes = new Dictionary<Challenge, CheckBox>();
        readonly TabControl tabs = new TabControl();
        readonly StackPanel settings = new StackPanel { Spacing = 6 };
        HashSet<int> traders;
        Control header, buttons;
        TabItem specialTab;
        const double MinReport = 140;

        public ChallengesWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Named challenges";
            Width = 980; Height = 860; MinWidth = 700; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt = Load();
            if (opt.Special == null) opt.Special = Challenges.DefaultSpecial(s, Level);
            if (opt.RangerWeights == null || opt.RangerWeights.Length != 4) opt.RangerWeights = new double[] { 10, 3, 2, 1 };

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(new TextBlock { Text = "Named challenges", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Each challenge changes every level: objects on the floor, in containers, carried by creatures and kept as templates in traps; the creatures' loot in OBJECTS.DAT; and, in a saved game, the player's inventory. " +
                       "Quest items and conversations are never touched. Some pairs cannot go together; the others are applied one after another in a fixed order. The same seed gives the same result.",
            });
            header = head;

            BuildSettings();
            tabs.Items.Add(new TabItem { Header = "Challenges", Content = new ScrollViewer { Content = settings, Padding = new Thickness(4, 8, 16, 8) } });
            specialTab = new TabItem { Header = "Special creatures (" + opt.Special.Count + ")" };
            SpecialPanel = new IgnoreListPanel(main, s, opt.Special, Challenges.SpecialCandidates(s, Level), () => { Save(); specialTab.Header = "Special creatures (" + opt.Special.Count + ")"; },
                "Ghost town keeps these creatures, DMD mode neither turns them hostile nor makes them powerful, and Hoarding gives them nothing to carry. " + Challenges.SpecialDefaultsText(s) + " Only creatures without a conversation are listed.",
                "Creatures without a conversation", "Special", "Add →", "← Remove", "Add the creature selected in the editor", null);
            specialTab.Content = SpecialPanel;
            tabs.Items.Add(specialTab);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => Preview();
            var apply = new Button { Content = "Apply", FontWeight = FontWeight.SemiBold };
            apply.Click += (o, e) => ApplyChallenges();
            undoButton.Click += (o, e) => UndoAll();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            bar.Children.Add(preview); bar.Children.Add(apply); bar.Children.Add(undoButton);
            bar.Children.Add(new TextBlock { Text = "Each run is also one Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            buttons = bar;
            report.MinHeight = MinReport;
            report.Text = "Tick challenges, then press Preview to see what would change, or Apply to change it.";

            var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
            Grid.SetRow(header, 0); grid.Children.Add(header);
            Grid.SetRow(tabs, 1); grid.Children.Add(tabs);
            Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
            Grid.SetRow(report, 3); grid.Children.Add(report);
            Content = grid;
            tabs.Height = 470;
            SizeChanged += (o, e) => Fit();
            head.SizeChanged += (o, e) => Fit();
            Opened += (o, e) => Fit();
            UpdateConflict();
            ManualWindow.Attach(this, "Named challenges");
            Closed += (o, e) => Save();
        }

        /// <summary>The settings' height: up to 470, leaving the report its minimum when the window is short; the report takes the rest.</summary>
        void Fit()
        {
            double h = ClientSize.Height - 32 - header.Bounds.Height - header.Margin.Bottom - buttons.Bounds.Height - buttons.Margin.Top - buttons.Margin.Bottom - MinReport;
            double target = Math.Floor(Math.Max(180, Math.Min(470, h)));
            if (Math.Abs(tabs.Height - target) > 0.5) tabs.Height = target;
        }

        void BuildSettings()
        {
            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); Save(); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var none = new Button { Content = "Untick all" };
            none.Click += (o, e) => { foreach (var b in boxes.Values) b.IsChecked = false; };
            settings.Children.Add(Row(new TextBlock { Text = "Seed", Width = 40, VerticalAlignment = VerticalAlignment.Center }, seed, newSeed, none));
            settings.Children.Add(conflict);

            byte[] objects = ObjectsNow();
            var launchers = Challenges.LaunchersByDamage(session, objects);
            foreach (var info in Challenges.All)
            {
                var c = info.Id;
                var box = new CheckBox { Content = info.Name, IsChecked = opt.IsOn(c), FontWeight = FontWeight.SemiBold, MinWidth = 170, VerticalAlignment = VerticalAlignment.Top };
                box.IsCheckedChanged += (o, e) => { opt.Set(c, box.IsChecked == true); Save(); UpdateConflict(); };
                boxes[c] = box;
                var text = new StackPanel { Spacing = 4 };
                text.Children.Add(new TextBlock { Text = info.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 6, 0, 0) });
                if (c == Challenge.Fasting) text.Children.Add(FastingRow());
                if (c == Challenge.Ranger) text.Children.Add(RangerRow(launchers));
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 2) };
                Grid.SetColumn(text, 1);
                row.Children.Add(box); row.Children.Add(text);
                settings.Children.Add(row);
            }
            settings.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0),
                Text = "The challenges are applied in this order: Ghost town, Bones rise, Peaceful, DMD mode, Vow of poverty, Allergic to magic, Illiterate, Old school, Eternal darkness, Naked, Monk, Ranger, Vegetarian, Starvation, Fasting, Utopia, Dystopia, Merchant, Hoarding, Lockdown, Precognition. " +
                       "These pairs cannot go together: Monk and Ranger, Monk and Hoarding, Utopia and Dystopia, Peaceful and DMD mode, Merchant and Hoarding.",
            });
        }

        Control FastingRow()
        {
            var v = new TextBlock { Width = 90, VerticalAlignment = VerticalAlignment.Center, Text = opt.FastingKeep.ToString("0") + " % stays" };
            var sl = new Slider { Minimum = 0, Maximum = 100, Value = opt.FastingKeep, Width = 240, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Height = 34, Margin = new Thickness(0, -10, 0, 0) };
            sl.ValueChanged += (o, e) => { opt.FastingKeep = sl.Value; v.Text = sl.Value.ToString("0") + " % stays"; Save(); };
            FastingSlider = sl;
            return Row(new TextBlock { Text = "Food remaining", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }, sl, v);
        }

        Control RangerRow(int[] launchers)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(new TextBlock { Text = "Launcher weights, least to most damaging:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            RangerBoxes = new List<NumericUpDown>();
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                p.Children.Add(new TextBlock { Text = session.Names.Get(launchers[i]), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) });
                var n = new NumericUpDown { Minimum = 0, Maximum = 1000, Increment = 1, FormatString = "0.##", Width = 110, Value = (decimal)opt.RangerWeights[k] };
                n.ValueChanged += (o, e) => { opt.RangerWeights[k] = (double)(n.Value ?? 0); Save(); };
                RangerBoxes.Add(n);
                p.Children.Add(n);
            }
            var wrap = new StackPanel { Spacing = 2 };
            wrap.Children.Add(p);
            wrap.Children.Add(new TextBlock { Text = "Half the melee weapons become launchers and half become ammunition: sling stones, bolts or arrows, 1–15 of them (drawn from N(7, 3), and drawn again when outside that range).", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
            return wrap;
        }

        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foreach (var x in c) p.Children.Add(x); return p; }

        void UpdateConflict()
        {
            string why = Challenges.Conflicts(opt.On());
            conflict.Text = why ?? "";
            conflict.IsVisible = why != null;
        }

        LevelModel Level(int l) { return main.ViewModel.Edits.Model(l); }

        /// <summary>OBJECTS.DAT as edited: the object tables window's, when it is open.</summary>
        byte[] ObjectsNow()
        {
            var t = main.ChallengeTables(false);
            return t != null ? t.ObjectsBytes : null;
        }

        string PathOf() { return Path.Combine(Settings.Folder, "challenges-" + (session.IsUw2 ? "uw2" : "uw1") + ".json"); }

        ChallengeOptions Load()
        {
            try { if (File.Exists(PathOf())) { var o = ChallengeOptions.FromJson(File.ReadAllText(PathOf())); if (o != null) return o; } } catch (Exception) { }
            return new ChallengeOptions { Seed = Environment.TickCount & 0x7FFFFFFF };
        }

        void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(PathOf(), opt.ToJson()); } catch (Exception) { }
        }

        public ChallengeOptions Options { get { return opt; } }
        public string ReportText { get { return report.Text; } }
        public bool CanUndo { get { return undoButton.IsEnabled; } }
        public string ConflictText { get { return conflict.IsVisible ? conflict.Text : null; } }
        public CheckBox Box(Challenge c) { return boxes[c]; }
        public Slider FastingSlider { get; private set; }
        public List<NumericUpDown> RangerBoxes { get; private set; }
        public IgnoreListPanel SpecialPanel { get; private set; }
        public TabControl Tabs { get { return tabs; } }

        ChallengeResult Run()
        {
            if (traders == null) traders = Challenges.TraderSlots(session);
            var player = session.IsSave ? main.ChallengePlayer() : null;
            var w = ChallengeWorld.Copy(session, main.ViewModel.Edits, ObjectsNow(), player, traders);
            return Challenges.Run(w, opt);
        }

        /// <summary>The Preview button: what the ticked challenges would change, nothing applied.</summary>
        public void Preview()
        {
            ChallengeResult r;
            try { r = Run(); }
            catch (Exception ex) { report.Text = "The preview failed: " + ex.Message; return; }
            report.Text = r.Refused != null ? r.Refused : "Preview — nothing changed yet.\n\n" + r.Report;
        }

        /// <summary>The Apply button.</summary>
        public void ApplyChallenges()
        {
            ChallengeResult r;
            try { r = Run(); }
            catch (Exception ex) { report.Text = "Nothing was changed: " + ex.Message; return; }
            if (r.Refused != null) { report.Text = r.Refused; return; }
            if (r.Total == 0) { report.Text = "Nothing to change.\n\n" + r.Report; return; }
            var tables = r.TableChanges.Count > 0 ? main.ChallengeTables(true) : null;
            var player = session.IsSave && r.World.PlayerChanged ? main.ChallengePlayer() : null;
            if (player != null) main.ChallengePlayerChanging();
            var a = Challenges.Apply(r, main.ViewModel.Edits, Setter(tables), player, "Named challenges (seed " + opt.Seed + ")");
            applied.Add(a);
            undoButton.IsEnabled = true;
            Save();
            var notes = new List<string>();
            if (r.TableChanges.Count > 0)
                notes.Add(tables == null ? "OBJECTS.DAT could not be opened, so its creature table is unchanged." : a.Table.Count + " field(s) of OBJECTS.DAT's creature table changed in Tools ▸ Object tables. They are unsaved there until File ▸ Save all writes them; DATA's copy of the file serves every save.");
            if (player != null) notes.Add("The player's inventory changed (unsaved).");
            report.Text = "Applied. " + (applied.Count > 1 ? applied.Count + " runs so far; Undo challenges puts back all of them." : "Undo challenges puts everything back.") + string.Concat(notes.Select(n => "\n" + n)) + "\n\n" + r.Report;
            main.ViewModel.RaiseEditState();
            main.RandomizerApplied("Named challenges applied. Edit ▸ Undo (per level) or Undo challenges in the Named challenges window puts them back.");
        }

        static Func<int, string, int, string> Setter(ObjectTables t)
        {
            if (t == null) return null;
            return (row, field, value) => t.Set("Creatures", row, field, value);
        }

        /// <summary>The Undo challenges button: every run from this window, put back as it was.</summary>
        public void UndoAll()
        {
            var skipped = new SortedSet<int>();
            bool table = applied.Any(a => a.Table.Count > 0), player = applied.Any(a => a.ChangedPlayer);
            var tables = table ? main.ChallengeTables(true) : null;
            var sheet = player ? main.ChallengePlayer() : null;
            if (sheet != null) main.ChallengePlayerChanging();
            for (int i = applied.Count - 1; i >= 0; i--) skipped.UnionWith(Challenges.Undo(applied[i], main.ViewModel.Edits, Setter(tables), sheet));
            report.Text = string.Format("Undone: {0} run(s) put back as they were.", applied.Count)
                + (skipped.Count == 0 ? "" : "\n\nLeft as they are because they were edited since: level" + (skipped.Count == 1 ? " " : "s ") + string.Join(", ", skipped) + ". Use Edit ▸ Undo to step back there.");
            applied.Clear();
            undoButton.IsEnabled = false;
            main.ViewModel.RaiseEditState();
            main.RandomizerApplied("Named challenges undone.");
        }
    }
}
