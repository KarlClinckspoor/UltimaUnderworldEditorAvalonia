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
    /// Tools ▸ Swap monsters: the monsters trade places by strength (experience by default,
    /// hit points, damage or both), keeping each spot's difficulty or following the depth, with
    /// a spread from strict to uniform. Creatures with a conversation stay unless whitelisted.
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
            seed.ValueChanged += (o, e) => opt.Seed = (int)(seed.Value ?? 1);
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);

            var scope = new ComboBox { ItemsSource = new[] { "Within each level", s.IsUw2 ? "Within each world" : "Across all levels (no worlds in UW1)", "Across all levels" }, SelectedIndex = Math.Max(0, Array.IndexOf(Scopes, opt.Scope)), Width = 300 };
            scope.SelectionChanged += (o, e) => opt.Scope = Scopes[Math.Max(0, scope.SelectedIndex)];
            var measure = new ComboBox { ItemsSource = new[] { "Experience value (the game's own rating)", "Hit points", "Damage per round (attacks and spells)", "Combined: √(toughness × damage)", "None: uniform" }, SelectedIndex = Math.Max(0, Array.IndexOf(Measures, opt.Measure)), Width = 300 };
            measure.SelectionChanged += (o, e) => opt.Measure = Measures[Math.Max(0, measure.SelectedIndex)];

            var settings = new StackPanel { Spacing = 4 };
            settings.Children.Add(Row(Label("Seed"), seed, newSeed));
            settings.Children.Add(Row(Label("Swap"), scope));
            settings.Children.Add(Row(Label("Strength"), measure));
            settings.Children.Add(Row(Label("Target"), Bar(opt.DepthBlend, x => opt.DepthBlend = x), Note("0: each spot keeps its difficulty (the strength of the monster that was there). 100: stronger deeper. Between: a blend.")));
            settings.Children.Add(Row(Label("Spread"), Bar(opt.Spread, x => opt.Spread = x), Note("0: strongest to hardest, strictly. 100: uniform.")));
            settings.Children.Add(Row(Label(""), Check("Keep the hit points at the spot (a golem in a worm's place gets the worm's hit points)", opt.KeepHp, x => opt.KeepHp = x)));
            settings.Children.Add(Row(Label(""), Check("Carried items go with the monster (those monsters then only swap within their level)", opt.LootWithMonster, x => opt.LootWithMonster = x)));
            settings.Children.Add(Row(Label(""), Check("Creatures with a conversation may move within their own level", opt.TalkersInLevel, x => opt.TalkersInLevel = x)));
            settings.Children.Add(Row(Label("Powerful"), Check("Roll it again", opt.RerollPowerful, x => opt.RerollPowerful = x),
                Label("chance %", 60), Bar(opt.PowerfulChance, x => opt.PowerfulChance = x, 140), Label("depth", 40), Bar(opt.PowerfulDepth, x => opt.PowerfulDepth = x, 140)));
            settings.Children.Add(Note("A powerful monster has armour × 5/3, +7..12 attack skill, +4..15 damage and × 1.5..3 experience; every strength measure counts it. Depth at 100 doubles the chance on the deepest level."));

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Swap monsters", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The monsters trade places: each spot keeps its position, home, goal and (by default) what lies in its inventory, and takes another monster's kind, hit points, powerful flag and attitude. " +
                       "Creatures with a conversation stay (the plot finds them by their conversation, on their own level) unless whitelisted, and so do templates inside traps and ignored ones. " +
                       "Race-bound creatures (liches, guards, Tybal's men...) only swap within their level; swimmers swap with swimmers; a monster only goes where its height fits.",
            });
            settingsView = new ScrollViewer { Content = settings, Padding = new Thickness(4, 8, 16, 8) };
            DockPanel.SetDock(top, Dock.Top);

            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => report.Text = "Preview — nothing changed yet.\n\n" + Run().Report;
            var swap = new Button { Content = "Swap", FontWeight = FontWeight.SemiBold };
            swap.Click += (o, e) => Swap();
            undoButton.Click += (o, e) => UndoAll();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            buttons.Children.Add(preview); buttons.Children.Add(swap); buttons.Children.Add(undoButton);
            buttons.Children.Add(new TextBlock { Text = "One Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
            var foot = new DockPanel { Height = 190 };
            DockPanel.SetDock(buttons, Dock.Top);
            foot.Children.Add(buttons); foot.Children.Add(report);
            DockPanel.SetDock(foot, Dock.Bottom);

            BuildTabs();
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(tabs);
            Content = dock;
            ManualWindow.Attach(this, "Swapping monsters");
            Closed += (o, e) => Save();
        }

        ScrollViewer settingsView;

        void BuildTabs()
        {
            tabs.Items.Add(new TabItem { Header = "Settings", Content = settingsView });
            var survey = MonsterSwap.Survey(session, Level, new MonsterSwapOptions());
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
            var table = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12, Text = MonsterSwap.StrengthTable(session) };
            tabs.Items.Add(wt);
            tabs.Items.Add(it);
            tabs.Items.Add(new TabItem { Header = "Strength table", Content = table });
        }

        static TextBlock Label(string t, double w = 80) { return new TextBlock { Text = t, Width = w, VerticalAlignment = VerticalAlignment.Center }; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, VerticalAlignment = VerticalAlignment.Center }; }
        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foreach (var x in c) p.Children.Add(x); return p; }

        static CheckBox Check(string text, bool value, Action<bool> a)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (o, e) => a(c.IsChecked == true);
            return c;
        }

        static Control Bar(double value, Action<double> set, double width = 200)
        {
            var v = new TextBlock { Width = 34, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Text = value.ToString("0") };
            var s = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = width, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Height = 34, Margin = new Thickness(0, -10, 0, 0) };
            s.ValueChanged += (o, e) => { v.Text = s.Value.ToString("0"); set(s.Value); };
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
            for (int i = applied.Count - 1; i >= 0; i--) { MonsterSwap.Revert(applied[i], main.ViewModel.Edits); n += applied[i].Changed; }
            report.Text = string.Format("Undone: {0} swap(s), {1} creature records put back as they were.", applied.Count, n);
            applied.Clear();
            undoButton.IsEnabled = false;
            main.ShuffleApplied();
        }
    }
}
