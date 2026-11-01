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
    /// Tools ▸ Randomizer ▸ Shuffle item properties: weights, enchantments and minimums in four
    /// tabs (weapons, armour, food / lights / tools, runestones), a preview, Shuffle, and Undo
    /// shuffle, which puts back every object the shuffles from this window changed.
    /// </summary>
    public sealed class ItemShuffleWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        ShuffleOptions opt;
        readonly TextBox report = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Increment = 1, FormatString = "0", Width = 150 };
        readonly Button undoButton = new Button { Content = "Undo shuffle", IsEnabled = false };
        readonly TabControl tabs = new TabControl();
        readonly List<ShuffleResult> applied = new List<ShuffleResult>();

        public ItemShuffleWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Shuffle item properties";
            Width = 980; Height = 760; MinWidth = 700; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt = LoadOptions(s, Level);

            var shuffle = new Button { Content = "Shuffle", FontWeight = FontWeight.SemiBold };
            shuffle.Click += (o, e) => Shuffle();
            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => { var r = Run(); report.Text = "Preview — nothing changed yet.\n\n" + r.Report; };
            undoButton.Click += (o, e) => UndoAll();
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var defaults = new Button { Content = "Defaults" };
            defaults.Click += (o, e) => { int sd = opt.Seed; opt = ShuffleOptions.Defaults(session, Level); opt.Seed = sd; Build(); };
            seed.ValueChanged += (o, e) => opt.Seed = (int)(seed.Value ?? 1);

            var inv = new CheckBox { Content = "Include creatures' inventories" };
            var keep = new CheckBox { Content = "Keep spells and other enchantments" };
            inv.IsCheckedChanged += (o, e) => opt.IncludeInventories = inv.IsChecked == true;
            keep.IsCheckedChanged += (o, e) => opt.KeepOtherEnchantments = keep.IsChecked == true;
            this.inv = inv; this.keep = keep;

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Shuffle item properties", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Every weapon, armour piece, food, light, runestone, wand and spell scroll stays where it lies, but may become another item of its kind by the weights below, " +
                       "with its quality spread and its enchantment rolled again. Weights are relative: 0 removes a kind. Caliburn" + (s.IsUw2 ? ", the jewelled dagger" : "") +
                       ", talismans and quest items are never touched. Unlike the zone shuffle, nothing moves. The same seed gives the same result.",
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Label("Seed")); row.Children.Add(seed); row.Children.Add(newSeed); row.Children.Add(defaults);
            row.Children.Add(inv); row.Children.Add(keep);
            top.Children.Add(row);
            mode.ItemsSource = new[] { "Shuffle properties (the tabs' weights, enchantments and curves)", "Swap existing objects, within their kind", "Swap existing objects, across all kinds taking part" };
            mode.SelectionChanged += (o, e) => { opt.Mode = Modes[Math.Max(0, mode.SelectedIndex)]; modeNote.IsVisible = opt.Mode != "shuffle"; };
            var mrow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            mrow.Children.Add(Label("Mode")); mrow.Children.Add(mode); mrow.Children.Add(modeNote);
            top.Children.Add(mrow);
            DockPanel.SetDock(top, Dock.Top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6) };
            buttons.Children.Add(preview); buttons.Children.Add(shuffle); buttons.Children.Add(undoButton);
            buttons.Children.Add(new TextBlock { Text = "Each shuffle is also one Edit ▸ Undo step per level; nothing is written to the game until you save.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
            var foot = new DockPanel { Height = 180 };
            DockPanel.SetDock(buttons, Dock.Top);
            foot.Children.Add(buttons); foot.Children.Add(report);
            DockPanel.SetDock(foot, Dock.Bottom);

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(tabs);
            Content = dock;
            ManualWindow.Attach(this, "Shuffling item properties");
            Build();
            Closed += (o, e) => SaveOptions();
        }

        readonly CheckBox inv, keep;
        TabItem ignoredTab;
        static readonly string[] Modes = { "shuffle", "swap-kind", "swap-any" };
        readonly ComboBox mode = new ComboBox { MinWidth = 420 };
        readonly TextBlock modeNote = new TextBlock
        {
            Text = "Only the tabs' Shuffle boxes count: nothing is made or removed, the objects trade places whole.",
            FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
        };
        public ShuffleOptions Options { get { return opt; } }
        public bool CanUndoShuffle { get { return undoButton.IsEnabled; } }
        public string ReportText { get { return report.Text; } }
        public void ShowTab(int i) { tabs.SelectedIndex = i; }
        /// <summary>Opens the custom distribution window of a weapon enchantment kind (screenshots, tests).</summary>
        public void OpenWeaponCurves(int i) { var g = opt.WeaponEnchants[i]; OpenEnchantCurves(g, opt.WeaponDepth, () => { }); }
        public void OpenWeaponQuality() { OpenQualityCurve(false, () => { }); }

        // ---- options file

        static string OptionsPath(GameSession s) { return Path.Combine(Settings.Folder, "item-shuffle-" + (s.IsUw2 ? "uw2" : "uw1") + ".json"); }

        LevelModel Level(int l) { return main.ViewModel.Edits.Model(l); }

        static ShuffleOptions LoadOptions(GameSession s, Func<int, LevelModel> level)
        {
            try
            {
                string p = OptionsPath(s);
                if (File.Exists(p))
                {
                    var o = ShuffleOptions.FromJson(File.ReadAllText(p));
                    if (o != null && o.WeaponEnchants.Count > 0 && o.RuneWeights.Count > 0) { o.Complete(s, level); return o; }
                }
            }
            catch (Exception) { }
            return ShuffleOptions.Defaults(s, level);
        }

        void SaveOptions()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(OptionsPath(session), opt.ToJson()); } catch (Exception) { }
        }

        // ---- running

        ShuffleResult Run() { return ItemShuffle.Run(session, Level, opt); }

        /// <summary>The Shuffle button.</summary>
        public void Shuffle()
        {
            var r = Run();
            if (r.Changed == 0) { report.Text = "Nothing to change.\n\n" + r.Report; return; }
            ItemShuffle.Apply(r, main.ViewModel.Edits, opt.Seed);
            applied.Add(r);
            undoButton.IsEnabled = true;
            SaveOptions();
            report.Text = "Shuffled. " + (applied.Count > 1 ? applied.Count + " shuffles so far; Undo shuffle puts back all of them.\n\n" : "Undo shuffle puts everything back.\n\n") + r.Report;
            main.ShuffleApplied();
        }

        /// <summary>The Undo shuffle button: every shuffle from this window, put back.</summary>
        public void UndoAll()
        {
            int n = 0;
            for (int i = applied.Count - 1; i >= 0; i--) { ItemShuffle.Revert(applied[i], main.ViewModel.Edits); n += applied[i].Changed; }
            report.Text = string.Format("Undone: {0} shuffle(s), {1} object changes put back as they were.", applied.Count, n);
            applied.Clear();
            undoButton.IsEnabled = false;
            main.ShuffleApplied();
        }

        // ---- the tabs

        void Build()
        {
            seed.Value = opt.Seed;
            inv.IsChecked = opt.IncludeInventories;
            keep.IsChecked = opt.KeepOtherEnchantments;
            mode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, opt.Mode));
            modeNote.IsVisible = opt.Mode != "shuffle";
            int sel = Math.Max(0, tabs.SelectedIndex);
            tabs.Items.Clear();
            tabs.Items.Add(new TabItem { Header = "Weapons", Content = Scroll(WeaponsTab()) });
            tabs.Items.Add(new TabItem { Header = "Armour", Content = Scroll(ArmourTab()) });
            tabs.Items.Add(new TabItem { Header = "Food, lights, tools", Content = Scroll(FoodTab()) });
            tabs.Items.Add(new TabItem { Header = "Runestones", Content = Scroll(RunesTab()) });
            tabs.Items.Add(new TabItem { Header = "Wands", Content = Scroll(WandsTab()) });
            tabs.Items.Add(new TabItem { Header = "Scrolls, potions", Content = Scroll(ScrollsTab()) });
            tabs.Items.Add(new TabItem { Header = "Rings, gloves, boots", Content = Scroll(RingsTab()) });
            ignoredTab = new TabItem { Header = "Ignored (" + opt.Ignored.Count + ")" };
            tabs.Items.Add(ignoredTab);
            ignoredTab.Content = IgnoredTab();
            tabs.SelectedIndex = sel;
        }

        static Control Scroll(Control c) { return new ScrollViewer { Content = c, Padding = new Thickness(4, 8, 16, 8) }; }

        static TextBlock Label(string t, double width = double.NaN)
        {
            return new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center, Width = width, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        static TextBlock Heading(string t)
        {
            return new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 4) };
        }

        static TextBlock Note(string t)
        {
            return new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) };
        }

        /// <summary>A slider with its value beside it.</summary>
        static Control Bar(double min, double max, double value, double step, Action<double> set, double width = 220, string format = "0")
        {
            var v = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Text = value.ToString(format) };
            var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5, VerticalAlignment = VerticalAlignment.Center, Height = 34, Margin = new Thickness(0, -10, 0, 0) };
            s.ValueChanged += (o, e) => { v.Text = s.Value.ToString(format); set(s.Value); };
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(s); p.Children.Add(v);
            return p;
        }

        static Control Line(string label, params Control[] parts)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 1) };
            p.Children.Add(Label(label, 200));
            foreach (var c in parts) p.Children.Add(c);
            return p;
        }

        static NumericUpDown Count(int min, int max, int value, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = 1, FormatString = "0", Width = 120 };
            n.ValueChanged += (o, e) => set((int)(n.Value ?? min));
            return n;
        }

        static CheckBox Check(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value };
            c.IsCheckedChanged += (o, e) => set(c.IsChecked == true);
            return c;
        }

        Control EnchantRows(List<EnchantGroup> groups, Func<double> depth)
        {
            var p = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(Label("", 200));
            foreach (var t in new[] { "chance %", "mean strength", "deviation" }) head.Children.Add(new TextBlock { Text = t, Width = 196, Opacity = 0.7, FontSize = 12 });
            p.Children.Add(head);
            foreach (var g in groups)
            {
                int n = g.Effects.Length;
                var chance = Bar(0, 100, g.Probability, 1, x => g.Probability = x, 150);
                var mean = Bar(1, n, Math.Max(1, Math.Min(n, g.Mean)), 0.1, x => g.Mean = x, 150, "0.0");
                var dev = Bar(0, n, Math.Min(n, g.Deviation), 0.1, x => g.Deviation = x, 150, "0.0");
                var state = new TextBlock { FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
                Action update = () =>
                {
                    chance.IsEnabled = !g.CustomDepth;
                    mean.IsEnabled = dev.IsEnabled = !g.CustomStrength;
                    state.Text = g.CustomStrength && g.CustomDepth ? "custom strength and depth" : g.CustomStrength ? "custom strength" : g.CustomDepth ? "custom depth" : "";
                };
                var custom = new Button { Content = "Custom…", Padding = new Thickness(8, 2) };
                custom.Click += (o, e) => OpenEnchantCurves(g, depth(), update);
                update();
                p.Children.Add(Line(g.Name + " (1–" + n + ")", chance, mean, dev, custom, state));
            }
            return p;
        }

        static TextBlock Small(string t)
        {
            return new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 6) };
        }

        static Window Dialog(string title, Control content, double height)
        {
            return new DistributionDialog(title, content, height);
        }

        /// <summary>The strength bars and the depth bars (UW1) or curve (UW2) of one enchantment kind, each replacing its sliders when ticked.</summary>
        void OpenEnchantCurves(EnchantGroup g, double depthAdj, Action update)
        {
            int n = g.Effects.Length, levels = session.Profile.LevelSlots;
            if (g.StrengthBars == null || g.StrengthBars.Length != n) g.StrengthBars = g.StrengthFromSliders();
            if (session.IsUw2) { if (g.DepthCurve == null || g.DepthCurve.Count < 2) g.DepthCurve = g.DepthCurveFromSliders(depthAdj); }
            else if (g.DepthBars == null || g.DepthBars.Length != levels) g.DepthBars = g.DepthBarsFromSliders(depthAdj, levels);

            var p = new StackPanel { Spacing = 4 };
            p.Children.Add(new TextBlock { Text = g.Name, FontSize = 16, FontWeight = FontWeight.SemiBold });
            p.Children.Add(Small("Each part replaces its sliders while ticked; editing it ticks it. \"From the sliders\" sets it to what the sliders give now."));

            var useStrength = new CheckBox { Content = "Strength from these bars instead of the mean and deviation", IsChecked = g.CustomStrength };
            var names = Enumerable.Range(0, n).Select(i => (i + 1).ToString()).ToArray();
            var bars = new BarsEditor(g.StrengthBars, names) { Caption = "weight by strength (1 = Minor)" };
            useStrength.IsCheckedChanged += (o, e) => { g.CustomStrength = useStrength.IsChecked == true; update(); };
            bars.Changed += () => useStrength.IsChecked = true;
            var fromSliders1 = new Button { Content = "From the sliders" };
            fromSliders1.Click += (o, e) => { Array.Copy(g.StrengthFromSliders(), g.StrengthBars, n); bars.InvalidateVisual(); };
            p.Children.Add(Heading("Strength"));
            p.Children.Add(useStrength);
            p.Children.Add(bars);
            p.Children.Add(Small(string.Join(", ", g.Effects.Select((e, i) => (i + 1) + " " + (session.Strings.Get(6, 0x1C0 + (opt.ArmourEnchants.Contains(g) ? 0x10 : 0) + e) ?? "").Trim()))));
            p.Children.Add(Row(fromSliders1));

            var useDepth = new CheckBox { Content = "Chance by depth from this instead of the chance and depth sliders", IsChecked = g.CustomDepth };
            useDepth.IsCheckedChanged += (o, e) => { g.CustomDepth = useDepth.IsChecked == true; update(); };
            var fromSliders2 = new Button { Content = "From the sliders" };
            p.Children.Add(Heading("Chance by depth"));
            p.Children.Add(useDepth);
            if (session.IsUw2)
            {
                var curve = new CurveEditor(g.DepthCurve, 1, 8)
                {
                    YCaption = "chance % by level within its world", XCaption = "level", XLabel = x => "L" + x.ToString("0"),
                    Marks = Enumerable.Range(1, 8).Select(x => (double)x).ToArray(),
                };
                curve.Changed += () => useDepth.IsChecked = true;
                fromSliders2.Click += (o, e) => { g.DepthCurve.Clear(); g.DepthCurve.AddRange(g.DepthCurveFromSliders(depthAdj)); curve.Points = g.DepthCurve; };
                p.Children.Add(curve);
                p.Children.Add(Row(fromSliders2, Small("Click to add a point, drag to move one, right-click to remove one. The dots are where each level reads it (Underworld II's worlds have up to 8 levels).")));
            }
            else
            {
                var dbars = new BarsEditor(g.DepthBars, Enumerable.Range(1, levels).Select(l => "L" + l).ToArray()) { Relative = false, Caption = "chance % by level" };
                dbars.Changed += () => useDepth.IsChecked = true;
                fromSliders2.Click += (o, e) => { Array.Copy(g.DepthBarsFromSliders(depthAdj, levels), g.DepthBars, levels); dbars.InvalidateVisual(); };
                p.Children.Add(dbars);
                p.Children.Add(Row(fromSliders2));
            }
            var w = Dialog("Custom distribution: " + g.Name, p, 640);
            ManualWindow.Attach(w, "Shuffling item properties");
            w.Show(this);
        }

        /// <summary>The quality curve of weapons or armour, replacing the spread while ticked.</summary>
        void OpenQualityCurve(bool armour, Action update)
        {
            var p = new StackPanel { Spacing = 4 };
            string what = armour ? "Armour" : "Weapon";
            p.Children.Add(new TextBlock { Text = what + " quality", FontSize = 16, FontWeight = FontWeight.SemiBold });
            p.Children.Add(Small("While ticked, each " + (armour ? "piece" : "weapon") + " gets a quality drawn from this curve (its height at each quality 1-63 is that quality's weight) instead of its own quality ± the spread. Most objects lie at 40."));
            var use = new CheckBox { Content = "Quality from this curve instead of the spread", IsChecked = armour ? opt.CustomArmourQuality : opt.CustomWeaponQuality };
            use.IsCheckedChanged += (o, e) => { if (armour) opt.CustomArmourQuality = use.IsChecked == true; else opt.CustomWeaponQuality = use.IsChecked == true; update(); };
            var pts = armour ? opt.ArmourQualityCurve : opt.WeaponQualityCurve;
            var curve = new CurveEditor(pts, 1, 63) { YCaption = "weight by quality", XCaption = "quality", XTickStep = 8 };
            curve.Changed += () => use.IsChecked = true;
            var fromGame = new Button { Content = "From the game" };
            fromGame.Click += (o, e) =>
            {
                var census = ItemShuffle.Census(session, Level);
                pts.Clear(); pts.AddRange(Distributions.CurveOf(armour ? census.ArmourQuality : census.WeaponQuality, 1, 8));
                curve.Points = pts;
            };
            p.Children.Add(use);
            p.Children.Add(curve);
            p.Children.Add(Row(fromGame, Small("Click to add a point, drag to move one, right-click to remove one. \"From the game\" follows the qualities in the levels now.")));
            var w = Dialog(what + " quality", p, 420);
            ManualWindow.Attach(w, "Shuffling item properties");
            w.Show(this);
        }

        static StackPanel Row(params Control[] parts)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4) };
            foreach (var c in parts) if (c != null) r.Children.Add(c);
            return r;
        }

        Control QualityLine(bool armour)
        {
            var bar = Bar(0, 63, armour ? opt.ArmourQualitySpread : opt.WeaponQualitySpread, 1, x => { if (armour) opt.ArmourQualitySpread = (int)x; else opt.WeaponQualitySpread = (int)x; });
            var state = new TextBlock { FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
            Action update = () =>
            {
                bool c = armour ? opt.CustomArmourQuality : opt.CustomWeaponQuality;
                bar.IsEnabled = !c;
                state.Text = c ? "custom curve" : "";
            };
            var custom = new Button { Content = "Custom curve…", Padding = new Thickness(8, 2) };
            custom.Click += (o, e) => OpenQualityCurve(armour, update);
            update();
            return Line("Quality spread ±", bar, custom, state);
        }

        static Control SpecialRows(List<SpecialChoice> list, int max)
        {
            var p = new StackPanel();
            foreach (var sp in list)
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 1) };
                var c = Check(sp.Name, sp.On, x => sp.On = x);
                c.Width = 200;
                r.Children.Add(c);
                r.Children.Add(Count(0, max, sp.Count, x => sp.Count = x));
                r.Children.Add(new TextBlock { Text = sp.Note, Opacity = 0.7, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                p.Children.Add(r);
            }
            return p;
        }

        Control WeaponsTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle weapons", opt.ShuffleWeapons, x => opt.ShuffleWeapons = x));
            p.Children.Add(Heading("Weapon types (relative weights)"));
            p.Children.Add(Line("Axes", Bar(0, 100, opt.Axes, 1, x => opt.Axes = x)));
            p.Children.Add(Line("Swords and daggers", Bar(0, 100, opt.Swords, 1, x => opt.Swords = x)));
            p.Children.Add(Line("Maces and clubs", Bar(0, 100, opt.Maces, 1, x => opt.Maces = x)));
            p.Children.Add(Line("Ranged and ammunition", Bar(0, 100, opt.Ranged, 1, x => opt.Ranged = x)));
            p.Children.Add(Line("Launchers among ranged %", Bar(0, 100, opt.LauncherShare, 1, x => opt.LauncherShare = x)));
            p.Children.Add(Note("Of the ranged objects, this share are slings, bows and crossbows; the rest are ammunition (never enchanted, keeping its count or 3–12)."));
            p.Children.Add(QualityLine(false));
            p.Children.Add(Heading("Enchantments (strength 1 = Minor)"));
            p.Children.Add(Note("Each kind is tried in a random order; the first that succeeds is the weapon's. The strength is a normal draw around the mean, kept in range."));
            p.Children.Add(EnchantRows(opt.WeaponEnchants, () => opt.WeaponDepth));
            p.Children.Add(Line("Depth adjustment", Bar(0, 100, opt.WeaponDepth, 1, x => opt.WeaponDepth = x)));
            p.Children.Add(Note("At 100 the deepest level doubles every chance and moves each mean up to the strongest; level 1 is never changed."));
            p.Children.Add(Heading("Special weapons"));
            if (opt.WeaponSpecialList.Count == 0)
                p.Children.Add(Note("Ultima Underworld has no special weapon enchantments (LifeStealer, Fire Doom, Stone Strike, Entry… are Underworld II's)."));
            else
            {
                p.Children.Add(Check("Create special weapons on random melee weapons", opt.WeaponSpecials, x => opt.WeaponSpecials = x));
                p.Children.Add(SpecialRows(opt.WeaponSpecialList, 20));
            }
            return p;
        }

        Control ArmourTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle armour", opt.ShuffleArmour, x => opt.ShuffleArmour = x));
            p.Children.Add(Heading("Armour types (relative weights)"));
            p.Children.Add(Note("A piece keeps its slot (helm, body, leggings, gloves, boots) and changes material; shields become another shield."));
            p.Children.Add(Line("Leather", Bar(0, 100, opt.Leather, 1, x => opt.Leather = x)));
            p.Children.Add(Line("Mail", Bar(0, 100, opt.Mail, 1, x => opt.Mail = x)));
            p.Children.Add(Line("Plate", Bar(0, 100, opt.Plate, 1, x => opt.Plate = x)));
            p.Children.Add(Line("Shields", Bar(0, 100, opt.Shields, 1, x => opt.Shields = x)));
            p.Children.Add(QualityLine(true));
            p.Children.Add(Heading("Enchantments (strength 1 = Minor)"));
            p.Children.Add(EnchantRows(opt.ArmourEnchants, () => opt.ArmourDepth));
            p.Children.Add(Line("Depth adjustment", Bar(0, 100, opt.ArmourDepth, 1, x => opt.ArmourDepth = x)));
            p.Children.Add(Heading("Special armour"));
            p.Children.Add(Check("Create special armour on random unenchanted pieces", opt.ArmourSpecials, x => opt.ArmourSpecials = x));
            p.Children.Add(SpecialRows(opt.ArmourSpecialList, 20));
            return p;
        }

        Control FoodTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle food", opt.ShuffleFood, x => opt.ShuffleFood = x));
            p.Children.Add(Heading("Food (relative weights)"));
            foreach (var k in opt.FoodWeights.Keys.OrderBy(k => k).ToList())
                p.Children.Add(Line(session.Names.Get(k), Bar(0, 100, opt.FoodWeights[k], 1, x => opt.FoodWeights[k] = x)));
            p.Children.Add(Check("Shuffle light sources", opt.ShuffleLights, x => opt.ShuffleLights = x));
            p.Children.Add(Heading("Light sources (relative weights; lit ones stay lit)"));
            foreach (var k in opt.LightWeights.Keys.OrderBy(k => k).ToList())
                p.Children.Add(Line(session.Names.Get(k), Bar(0, 100, opt.LightWeights[k], 1, x => opt.LightWeights[k] = x)));
            p.Children.Add(Heading("Tools (at least this many)"));
            p.Children.Add(Check("Tools take part in swaps (swap modes)", opt.SwapTools, x => opt.SwapTools = x));
            p.Children.Add(Note("When there are fewer, random food and lights lying on the floor become them."));
            p.Children.Add(Line(session.Names.Get(ItemTables.Thread(session.IsUw2)), Count(0, 50, opt.MinThread, x => opt.MinThread = x)));
            p.Children.Add(Line(session.Names.Get(ItemTables.Pole), Count(0, 50, opt.MinPole, x => opt.MinPole = x)));
            p.Children.Add(Line(session.Names.Get(ItemTables.FishingPole), Count(0, 50, opt.MinFishingPole, x => opt.MinFishingPole = x)));
            return p;
        }

        Control RunesTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle runestones", opt.ShuffleRunes, x => opt.ShuffleRunes = x));
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 4) };
            head.Children.Add(new TextBlock { Text = "Rune", Width = 200, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock { Text = "weight", Width = 266, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock { Text = "at least", FontWeight = FontWeight.SemiBold });
            p.Children.Add(head);
            foreach (int r in ItemTables.Runes)
            {
                double w; int m;
                opt.RuneWeights.TryGetValue(r, out w); opt.RuneMinimum.TryGetValue(r, out m);
                p.Children.Add(Line(session.Names.Get(r), Bar(0, 100, w, 1, x => opt.RuneWeights[r] = x), Count(0, 20, m, x => opt.RuneMinimum[r] = x)));
            }
            p.Children.Add(Note("Minimums are met by changing runestones of the kinds with the most to spare; if there are too few runestones the report says which could not be met."));
            return p;
        }

        string[] CircleLabels() { return Enumerable.Range(1, SpellTables.Circles(session.IsUw2)).Select(c => SpellTables.CircleLabel(session.IsUw2, c)).ToArray(); }

        Control SpellList()
        {
            var lines = Enumerable.Range(1, SpellTables.Circles(session.IsUw2))
                .Select(c => (session.IsUw2 || c < 9 ? "Circle " + c : "No runes") + ": " + string.Join(", ", SpellTables.InCircle(session, c).Select(sp => SpellTables.Name(session, sp))));
            return Small("A spell is picked by circle, then evenly among the spells of that circle:\n" + string.Join("\n", lines));
        }

        Control WandsTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle wands", opt.ShuffleWands, x => opt.ShuffleWands = x));
            p.Children.Add(Small("A wand keeps its spell in a spell object inside it: its quality is the charges (USEITEMS.C useNSpellCharges). Broken wands and wands with other effects are kept. Drag the bars; sweeping sideways sets several at once."));
            p.Children.Add(Heading("Charges (relative weights)"));
            var charges = new BarsEditor(opt.WandCharges, Enumerable.Range(1, ShuffleOptions.MaxCharges).Select(i => i.ToString()).ToArray()) { Caption = "weight by charges" };
            p.Children.Add(charges);
            p.Children.Add(Heading("Spell circle (relative weights)"));
            var circles = new BarsEditor(opt.WandCircles, CircleLabels()) { Caption = "weight by circle", Height = 130 };
            p.Children.Add(circles);
            var fromGame = new Button { Content = "From the game" };
            fromGame.Click += (o, e) =>
            {
                var c = ItemShuffle.Census(session, Level);
                Array.Copy(Distributions.Scaled(c.Charges), opt.WandCharges, opt.WandCharges.Length);
                Array.Copy(Distributions.Scaled(c.WandCircles), opt.WandCircles, opt.WandCircles.Length);
                charges.InvalidateVisual(); circles.InvalidateVisual();
            };
            p.Children.Add(Row(fromGame, Small("Sets both to what the wands in the levels have now.")));
            p.Children.Add(SpellList());
            return p;
        }

        Control ScrollsTab()
        {
            var p = new StackPanel();
            p.Children.Add(Check("Shuffle spell scrolls", opt.ShuffleScrolls, x => opt.ShuffleScrolls = x));
            p.Children.Add(Small("Scrolls enchanted with a spell (read to cast it once). Readable scrolls, books and maps are kept."));
            p.Children.Add(Heading("Spell circle (relative weights)"));
            var circles = new BarsEditor(opt.ScrollCircles, CircleLabels()) { Caption = "weight by circle", Height = 130 };
            p.Children.Add(circles);
            var fromGame = new Button { Content = "From the game" };
            fromGame.Click += (o, e) =>
            {
                var c = ItemShuffle.Census(session, Level);
                Array.Copy(Distributions.Scaled(c.ScrollCircles), opt.ScrollCircles, opt.ScrollCircles.Length);
                circles.InvalidateVisual();
            };
            p.Children.Add(Row(fromGame, Small("Sets the bars to what the scrolls in the levels have now.")));

            p.Children.Add(Heading("Potions"));
            p.Children.Add(Check("Shuffle potions", opt.ShufflePotions, x => opt.ShufflePotions = x));
            p.Children.Add(Small(session.IsUw2
                ? "Potions holding one of the 64 spells get a new one; a potion then takes the colour the game uses most for that spell (Underworld II's colours tell the effect). Potions with special effects are kept."
                : "Potions holding one of the 64 spells get a new one; red and green tell nothing in Ultima Underworld. Potions with special effects are kept."));
            p.Children.Add(Check("Only spells some potion already holds", opt.PotionsKnownOnly, x => opt.PotionsKnownOnly = x));
            var pc = new BarsEditor(opt.PotionCircles, CircleLabels()) { Caption = "weight by circle", Height = 130 };
            p.Children.Add(pc);
            var pFromGame = new Button { Content = "From the game" };
            pFromGame.Click += (o, e) =>
            {
                var c = ItemShuffle.Census(session, Level);
                Array.Copy(Distributions.Scaled(c.PotionCircles), opt.PotionCircles, opt.PotionCircles.Length);
                pc.InvalidateVisual();
            };
            p.Children.Add(Row(pFromGame, Small("Sets the bars to what the potions in the levels have now.")));
            p.Children.Add(SpellList());
            return p;
        }

        Control RingsTab()
        {
            var p = new StackPanel();
            p.Children.Add(Small("Worn things apply their effect while worn (PLAYDATA.C player_affected_by). A curse does its strength in d8 of damage, never below 3 hit points, every time what is worn changes (SPELLS.C backfire)."));
            p.Children.Add(Check("Shuffle rings", opt.ShuffleRings, x => opt.ShuffleRings = x));
            p.Children.Add(Heading("Rings (relative weights)"));
            foreach (var k in opt.RingWeights.Keys.OrderBy(k => k).ToList())
                p.Children.Add(Line(session.Names.Get(k), Bar(0, 100, opt.RingWeights[k], 1, x => opt.RingWeights[k] = x)));
            p.Children.Add(Line("Rings with an effect %", Bar(0, 100, opt.RingMagic, 1, x => opt.RingMagic = x)));
            if (session.IsUw2) p.Children.Add(Small("The Guardian signet rings are quest items and stay."));
            p.Children.Add(Heading("Gloves and boots"));
            p.Children.Add(Check("Give gloves and boots worn effects", opt.ShuffleWorn, x => opt.ShuffleWorn = x));
            p.Children.Add(Line("Chance of a worn effect %", Bar(0, 100, opt.WornChance, 1, x => opt.WornChance = x)));
            p.Children.Add(Small("Instead of the Protection or Toughness the armour tab rolled (or the one they have, when armour is not shuffled)."));
            p.Children.Add(Heading("Curses"));
            p.Children.Add(Line("Cursed instead %", Bar(0, 100, opt.CurseShare, 1, x => opt.CurseShare = x)));
            p.Children.Add(Small("Of the rings, gloves and boots that get an effect, this share are cursed instead."));
            p.Children.Add(new BarsEditor(opt.CurseStrength, Enumerable.Range(1, 8).Select(i => i + "d8").ToArray()) { Caption = "weight by curse strength", Height = 120, MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left });
            p.Children.Add(Heading("Worn effects (relative weights)"));
            var effects = ItemTables.WornEffects(session);
            foreach (var grp in effects.GroupBy(e => e.Group))
            {
                p.Children.Add(new TextBlock { Text = grp.Key, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2) });
                foreach (var e in grp)
                {
                    int code = e.Code;
                    double w; opt.WornEffects.TryGetValue(code, out w);
                    p.Children.Add(Line(e.Name, Bar(0, 100, w, 1, x => opt.WornEffects[code] = x)));
                }
            }
            return p;
        }

        Control IgnoredTab()
        {
            var all = ItemShuffle.Candidates(session, Level);
            var filter = new TextBox { Watermark = "Filter: a name, a spell, L4…", Width = 300 };
            var left = new ListBox { Height = double.NaN };
            var right = new ListBox();
            left.Bind(ListBox.BackgroundProperty, left.GetResourceObservable("Panel"));
            right.Bind(ListBox.BackgroundProperty, right.GetResourceObservable("Panel"));
            var status = new TextBlock { FontSize = 12, Opacity = 0.75 };
            Action refresh = () =>
            {
                string f = (filter.Text ?? "").Trim();
                var ign = new HashSet<long>(opt.Ignored.Select(x => (long)x.Level << 32 | (uint)x.Index));
                left.ItemsSource = all.Where(c => !ign.Contains((long)c.Level << 32 | (uint)c.Index) && (f.Length == 0 || c.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                right.ItemsSource = opt.Ignored.ToList();
                if (ignoredTab != null) ignoredTab.Header = "Ignored (" + opt.Ignored.Count + ")";
            };
            filter.TextChanged += (o, e) => refresh();
            Action<IgnoredObject> add = c =>
            {
                if (c == null || opt.Ignored.Any(x => x.Level == c.Level && x.Index == c.Index)) return;
                opt.Ignored.Add(c);
                status.Text = "Ignored: " + c.Label;
                refresh();
            };
            var addB = new Button { Content = "Ignore →" };
            addB.Click += (o, e) => add(left.SelectedItem as IgnoredObject);
            left.DoubleTapped += (o, e) => add(left.SelectedItem as IgnoredObject);
            var remB = new Button { Content = "← Stop ignoring" };
            remB.Click += (o, e) => { if (right.SelectedItem is IgnoredObject c) { opt.Ignored.RemoveAll(x => x.Level == c.Level && x.Index == c.Index); refresh(); } };
            var selB = new Button { Content = "Ignore the object selected in the editor" };
            selB.Click += (o, e) =>
            {
                var vm = main.ViewModel;
                if (vm.Level == null || vm.SelectedObject < 0) { status.Text = "Select an object in the editor first."; return; }
                var m = vm.Edits.Model(vm.Level.Level);
                var ob = m.Objects[vm.SelectedObject];
                add(new IgnoredObject { Level = vm.Level.Level, Index = ob.Index, Label = ItemShuffle.Describe(session, m, ob) });
            };
            var goB = new Button { Content = "Go to" };
            goB.Click += (o, e) => { var c = (right.SelectedItem ?? left.SelectedItem) as IgnoredObject; if (c != null) main.GoToObject(c.Level, c.Index); };
            refresh();

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
            var intro = Small("Both modes leave these objects alone (a wand's spell object goes with its wand). Wands enchanted directly are ignored at first: Ultima Underworld's bullfrog wand, Underworld II's slightly worn wand of Telekinesis and wand of Light.");
            Grid.SetColumnSpan(intro, 3); grid.Children.Add(intro);
            var lh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4) };
            lh.Children.Add(new TextBlock { Text = "Objects the shuffles touch", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }); lh.Children.Add(filter);
            Grid.SetRow(lh, 1); grid.Children.Add(lh);
            var rh = new TextBlock { Text = "Ignored", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8) };
            Grid.SetRow(rh, 1); Grid.SetColumn(rh, 2); grid.Children.Add(rh);
            Grid.SetRow(left, 2); grid.Children.Add(left);
            var mid = new StackPanel { Spacing = 8, Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(addB); mid.Children.Add(remB); mid.Children.Add(goB);
            Grid.SetRow(mid, 2); Grid.SetColumn(mid, 1); grid.Children.Add(mid);
            Grid.SetRow(right, 2); Grid.SetColumn(right, 2); grid.Children.Add(right);
            var foot = Row(selB, status);
            Grid.SetRow(foot, 3); Grid.SetColumnSpan(foot, 3); grid.Children.Add(foot);
            grid.Margin = new Thickness(4, 8);
            return grid;
        }
    }

    /// <summary>A small window for one hand-made distribution.</summary>
    public sealed class DistributionDialog : Window
    {
        public DistributionDialog(string title, Control content, double height)
        {
            Title = title;
            Width = 760; Height = height; MinWidth = 520; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new ScrollViewer { Content = content, Padding = new Thickness(16) };
        }
    }
}
