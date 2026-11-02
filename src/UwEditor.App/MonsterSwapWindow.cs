using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// Randomizer ▸ Monster swap: the monsters trade places by strength (experience by default,
    /// hit points, damage or both), keeping each spot's difficulty or following the depth, with
    /// a spread from strict to uniform. Creatures with a conversation stay unless whitelisted.
    /// The settings and lists take their natural height (scrolling when the window is short);
    /// the report below takes the rest.
    /// </summary>
    public sealed class MonsterSwapWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly MonsterSwapOptions opt;
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo swap", IsEnabled = false };
        readonly List<MonsterSwapResult> applied = new List<MonsterSwapResult>();
        readonly TabControl tabs = new TabControl();
        readonly TextBox formula = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly StackPanel settings = new StackPanel { Spacing = 4 };
        StackPanel top, buttons;
        Grid root;
        double tabChrome = 48;
        const double MinReport = 120;
        static readonly string[] Scopes = { "level", "world", "all" };
        static readonly string[] Measures = { "xp", "hp", "damage", "combined", "none" };

        public MonsterSwapWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Swap monsters";
            Width = 960; Height = 800; MinWidth = 680; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt = Load();
            if (opt.Whitelist == null) opt.Whitelist = MonsterSwap.DefaultWhitelist(s, Level);
            if (opt.Ignored == null) opt.Ignored = new List<IgnoredObject>();

            seed.Value = opt.Seed;
            seed.ValueChanged += (o, e) => { opt.Seed = (int)(seed.Value ?? 1); Changed(); };
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);

            var scope = new ComboBox { ItemsSource = new[] { "Within each level", s.IsUw2 ? "Within each world" : "Across all levels (no worlds in UW1)", "Across all levels" }, SelectedIndex = Math.Max(0, Array.IndexOf(Scopes, opt.Scope)), Width = 300 };
            scope.SelectionChanged += (o, e) => { opt.Scope = Scopes[Math.Max(0, scope.SelectedIndex)]; Changed(); };
            var measure = new ComboBox { ItemsSource = new[] { "Experience value (the game's own rating)", "Hit points", "Damage per round (attacks and spells)", "Combined: √(toughness × damage)", "None: uniform" }, SelectedIndex = Math.Max(0, Array.IndexOf(Measures, opt.Measure)), Width = 300 };
            measure.SelectionChanged += (o, e) => { opt.Measure = Measures[Math.Max(0, measure.SelectedIndex)]; Changed(); };

            settings.Children.Add(Row(Label("Seed"), seed, newSeed));
            settings.Children.Add(Row(Label("Swap"), scope));
            settings.Children.Add(Row(Label("Strength"), measure));
            settings.Children.Add(Row(Label("Target"), Bar(opt.DepthBlend, x => opt.DepthBlend = x), Note("0: each spot keeps its difficulty (the strength of the monster that was there). 100: stronger deeper. Between: a blend.")));
            settings.Children.Add(Row(Label("Spread"), Bar(opt.Spread, x => opt.Spread = x), Note("0: strongest to hardest, strictly. 100: uniform.")));
            settings.Children.Add(Row(Label(""), Check("Keep the hit points at the spot (a golem in a worm's place gets the worm's hit points)", opt.KeepHp, x => opt.KeepHp = x)));
            settings.Children.Add(Row(Label(""), Check("Carried items go with the monster (those monsters then only swap within their level)", opt.LootWithMonster, x => opt.LootWithMonster = x)));
            settings.Children.Add(Row(Label(""), Check("Creatures with a conversation may move within their own level", opt.TalkersInLevel, x => opt.TalkersInLevel = x)));
            specialCheck = Check("Leave the " + MonsterSwap.SpecialKindName(s) + " alone: they neither move nor take another's place", opt.LeaveSpecialKinds, x => opt.LeaveSpecialKinds = x);
            settings.Children.Add(Row(Label(""), specialCheck));
            settings.Children.Add(Row(Label("Powerful"), Check("Roll it again", opt.RerollPowerful, x => opt.RerollPowerful = x),
                Label("chance %", 60), Bar(opt.PowerfulChance, x => opt.PowerfulChance = x, 140), Label("depth", 40), Bar(opt.PowerfulDepth, x => opt.PowerfulDepth = x, 140)));
            settings.Children.Add(Note("A powerful monster has armour × 5/3, +7..12 attack skill, +4..15 damage and × 1.5..3 experience; every strength measure counts it. Depth at 100 doubles the chance on the deepest level."));
            settings.Children.Add(new TextBlock { Text = "Formula: N(μ, σ) is a normal distribution of mean μ and standard deviation σ, U(0, 1) uniform", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            settings.Children.Add(formula);
            UpdateFormula();

            top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Swap monsters", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The monsters trade places: each spot keeps its position, home, goal and (by default) what lies in its inventory, and takes another monster's kind, hit points, powerful flag and attitude. " +
                       "Creatures with a conversation stay (the plot finds them by their conversation, on their own level) unless whitelisted, and so do templates inside traps and ignored ones. " +
                       "Race-bound creatures (liches, guards, Tybal's men...) only swap within their level; swimmers swap with swimmers; a monster only goes where its height fits, and only fliers, swimmers and fire-resistant ones go on water or lava.",
            });
            settingsView = new ScrollViewer { Content = settings, Padding = new Thickness(4, 8, 16, 8) };

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => report.Text = "Preview — nothing changed yet.\n\n" + Run().Report;
            var swap = new Button { Content = "Swap", FontWeight = FontWeight.SemiBold };
            swap.Click += (o, e) => Swap();
            undoButton.Click += (o, e) => UndoAll();
            buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            var world = new Button { Content = "World report" };
            ToolTip.SetTip(world, "The creatures of the game's levels as saved on disk (before any edits)");
            world.Click += (o, e) => report.Text = MonsterSwap.WorldReport(session, session.LoadLevel);
            buttons.Children.Add(preview); buttons.Children.Add(swap); buttons.Children.Add(undoButton); buttons.Children.Add(world);
            buttons.Children.Add(new TextBlock { Text = "One Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
            report.MinHeight = MinReport;

            BuildTabs();
            // the tabs keep the settings' natural height (FitTabs), the report takes what is left
            root = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
            Grid.SetRow(tabs, 1); Grid.SetRow(buttons, 2); Grid.SetRow(report, 3);
            root.Children.Add(top); root.Children.Add(tabs); root.Children.Add(buttons); root.Children.Add(report);
            Content = root;
            LayoutUpdated += (o, e) => FitTabs();
            ManualWindow.Attach(this, "Swapping monsters");
            Closed += (o, e) => Save();
        }

        ScrollViewer settingsView;
        CheckBox specialCheck;
        SortableTable<MonsterSwap.StrengthRow> strengthTable;

        /// <summary>
        /// Sizes the tabs: the settings' natural height (what the scroll viewer measured them at, plus
        /// the tab strip), but no more than leaves the report its minimum; the lists and the strength
        /// table get the same height and scroll inside it.
        /// </summary>
        void FitTabs()
        {
            if (root == null || root.Bounds.Height <= 0) return;
            if (tabs.SelectedIndex == 0 && settingsView.Bounds.Height > 0 && !double.IsNaN(tabs.Height) && tabs.Bounds.Height > settingsView.Bounds.Height)
                tabChrome = tabs.Bounds.Height - settingsView.Bounds.Height;
            double natural = settings.DesiredSize.Height + settingsView.Padding.Top + settingsView.Padding.Bottom + tabChrome;
            if (natural <= tabChrome) return;
            double avail = root.Bounds.Height - top.Bounds.Height - top.Margin.Top - top.Margin.Bottom - buttons.Bounds.Height - buttons.Margin.Top - buttons.Margin.Bottom - MinReport;
            double h = Math.Floor(Math.Max(160, Math.Min(natural, avail)));
            if (double.IsNaN(tabs.Height) || Math.Abs(tabs.Height - h) > 0.5) tabs.Height = h;
        }

        void Changed() { UpdateFormula(); }

        void UpdateFormula() { formula.Text = MonsterSwap.Formula(session, opt); }

        void BuildTabs()
        {
            tabs.Items.Add(new TabItem { Header = "Settings", Content = settingsView });
            var survey = MonsterSwap.Survey(session, Level, new MonsterSwapOptions { LeaveSpecialKinds = false });
            var talkers = survey.Where(x => x.Blocked == "has a conversation").Select(x => new IgnoredObject { Level = x.Level, Index = x.Index, Label = x.Label }).ToList();
            var live = survey.Where(x => x.Blocked == null || x.Blocked == "has a conversation").Select(x => new IgnoredObject { Level = x.Level, Index = x.Index, Label = x.Label }).ToList();
            TabItem wt = null, it = null;
            var white = new IgnoreListPanel(main, session, opt.Whitelist, talkers, () => { Save(); if (wt != null) wt.Header = "Whitelist (" + opt.Whitelist.Count + ")"; },
                "Creatures with a conversation that may move anyway: ones whose plot only needs them alive somewhere, so the directions to them just stop being right. The level 2 gazer (Goldthirst's quest) in Ultima Underworld and the Listener (Altara's dagger) in Underworld II are whitelisted at first. Their conversation and their special death go with them.",
                "Creatures with a conversation", "Allowed to move", "Allow →", "← Keep in place", "Allow the creature selected in the editor",
                c => talkers.Any(t => t.Level == c.Level && t.Index == c.Index) ? null : "Only creatures with a conversation need the whitelist.");
            var ign = new IgnoreListPanel(main, session, opt.Ignored, live, () => { Save(); if (it != null) it.Header = "Ignored (" + opt.Ignored.Count + ")"; },
                "Monsters that never move (the Slasher of Veils, a guard you like where it is...).",
                "Monsters", "Ignored", "Ignore →", "← Stop ignoring", "Ignore the creature selected in the editor",
                c => live.Any(t => t.Level == c.Level && t.Index == c.Index) ? null : "That is not a creature in the world.");
            wt = new TabItem { Header = "Whitelist (" + opt.Whitelist.Count + ")", Content = white };
            it = new TabItem { Header = "Ignored (" + opt.Ignored.Count + ")", Content = ign };
            WhitelistPanel = white;
            IgnoredPanel = ign;
            tabs.Items.Add(wt);
            tabs.Items.Add(it);
            tabs.Items.Add(new TabItem { Header = "Strength table", Content = BuildStrengthTable() });
        }

        /// <summary>The strength table: every named kind with its measures; a header click sorts by it, a second click reverses.</summary>
        Control BuildStrengthTable()
        {
            var ci = System.Globalization.CultureInfo.CurrentCulture;
            var cols = new List<TableColumn<MonsterSwap.StrengthRow>>
            {
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Kind", Width = 150, Text = r => r.Name },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Item", Width = 58, Text = r => "0x" + r.Item.ToString("X2"), Key = r => r.Item },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "XP", Width = 58, Right = true, Text = r => r.Xp.ToString(ci), Key = r => r.Xp, Tip = "The game's experience value for the kind" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "HP", Width = 54, Right = true, Text = r => r.Hp.ToString(ci), Key = r => r.Hp, Tip = "Average hit points" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Armour", Width = 76, Right = true, Text = r => r.Armour.ToString("0.0", ci), Key = r => r.Armour, Tip = "The mean armour over the hit locations" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Damage", Width = 80, Right = true, Text = r => r.Damage.ToString("0.0", ci), Key = r => r.Damage, Tip = "Damage per round (attacks and spells)" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Combined", Width = 94, Right = true, Text = r => r.Combined.ToString("0.0", ci), Key = r => r.Combined, Tip = "√(hp × (1 + armour / 8) × damage)" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Moves", Width = 68, Text = r => r.Moves, Tip = "Fliers may go anywhere, swimmers on water" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Fire", Width = 72, Text = r => r.Fireproof ? "resists" : "", Key = r => r.Fireproof ? 1 : 0, Tip = "Resists fire: may go on lava" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Height", Width = 70, Right = true, Text = r => r.Height.ToString(ci), Key = r => r.Height, Tip = "COMOBJ.DAT height: it must fit under the ceiling" },
                new TableColumn<MonsterSwap.StrengthRow> { Header = "Spells", Width = 110, Fill = true, Text = r => r.Spells },
            };
            strengthTable = new SortableTable<MonsterSwap.StrengthRow>(cols, MonsterSwap.StrengthRows(session));
            strengthTable.SortBy(2);
            var note = new TextBlock { Text = "Click a header to sort by it, again to reverse. " + MonsterSwap.StrengthNote, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) };
            var d = new DockPanel { Margin = new Thickness(4, 8) };
            DockPanel.SetDock(note, Dock.Bottom);
            d.Children.Add(note); d.Children.Add(strengthTable);
            return d;
        }

        static TextBlock Label(string t, double w = 80) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, VerticalAlignment = VerticalAlignment.Center }; }
        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foreach (var x in c) p.Children.Add(x); return p; }

        CheckBox Check(string text, bool value, Action<bool> a)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (o, e) => { a(c.IsChecked == true); Changed(); };
            return c;
        }

        Control Bar(double value, Action<double> set, double width = 200)
        {
            var v = new TextBlock { Width = 34, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Text = value.ToString("0") };
            var s = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = width, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Height = 34, Margin = new Thickness(0, -10, 0, 0) };
            s.ValueChanged += (o, e) => { v.Text = s.Value.ToString("0"); set(s.Value); Changed(); };
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(s); p.Children.Add(v);
            return p;
        }

        LevelModel Level(int l) { return main.ViewModel.Edits.Model(l); }

        string PathOf() { return Path.Combine(Settings.Folder, "monster-swap-" + (session.IsUw2 ? "uw2" : "uw1") + ".json"); }

        MonsterSwapOptions Load()
        {
            try { if (File.Exists(PathOf())) { var o = MonsterSwapOptions.FromJson(File.ReadAllText(PathOf())); if (o != null) return o; } } catch (Exception) { }
            return new MonsterSwapOptions { Seed = Environment.TickCount & 0x7FFFFFFF };
        }

        void Save()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(PathOf(), opt.ToJson()); } catch (Exception) { }
        }

        public MonsterSwapOptions Options { get { return opt; } }
        public bool CanUndoSwap { get { return undoButton.IsEnabled; } }
        public string ReportText { get { return report.Text; } }
        /// <summary>The formula box's text (live with the settings).</summary>
        public string FormulaText { get { return formula.Text; } }
        public CheckBox SpecialKindsCheck { get { return specialCheck; } }
        public IgnoreListPanel WhitelistPanel { get; private set; }
        public IgnoreListPanel IgnoredPanel { get; private set; }
        public SortableTable<MonsterSwap.StrengthRow> StrengthTableView { get { return strengthTable; } }
        public TabControl Tabs { get { return tabs; } }
        public TextBox ReportBox { get { return report; } }

        MonsterSwapResult Run() { return MonsterSwap.Run(session, Level, opt); }

        /// <summary>The Swap button.</summary>
        public void Swap()
        {
            var r = Run();
            if (r.Changed == 0) { report.Text = "Nothing to change.\n\n" + r.Report; return; }
            MonsterSwap.Apply(r, main.ViewModel.Edits, opt.Seed);
            applied.Add(r);
            undoButton.IsEnabled = true;
            Save();
            report.Text = "Swapped. " + (applied.Count > 1 ? applied.Count + " swaps so far; Undo swap puts back all of them.\n\n" : "Undo swap puts everything back.\n\n") + r.Report;
            main.ShuffleApplied();
        }

        /// <summary>The Undo swap button: every swap from this window, put back.</summary>
        public void UndoAll()
        {
            int n = 0;
            var skipped = new SortedSet<int>();
            for (int i = applied.Count - 1; i >= 0; i--) { skipped.UnionWith(MonsterSwap.Revert(applied[i], main.ViewModel.Edits)); n += applied[i].Changed; }
            report.Text = string.Format("Undone: {0} swap(s), {1} creature records put back as they were.", applied.Count, n)
                + (skipped.Count == 0 ? "" : "\n\nLeft as they are, because they were edited since: level" + (skipped.Count == 1 ? " " : "s ") + string.Join(", ", skipped) + ". Edit ▸ Undo steps back there.");
            applied.Clear();
            undoButton.IsEnabled = false;
            main.ShuffleApplied();
        }
    }
}
