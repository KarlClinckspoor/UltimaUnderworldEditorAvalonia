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
            opt = LoadOptions(s);

            var shuffle = new Button { Content = "Shuffle", FontWeight = FontWeight.SemiBold };
            shuffle.Click += (o, e) => Shuffle();
            var preview = new Button { Content = "Preview" };
            preview.Click += (o, e) => { var r = Run(); report.Text = "Preview — nothing changed yet.\n\n" + r.Report; };
            undoButton.Click += (o, e) => UndoAll();
            var newSeed = new Button { Content = "New seed" };
            newSeed.Click += (o, e) => seed.Value = new Random().Next(1, int.MaxValue);
            var defaults = new Button { Content = "Defaults" };
            defaults.Click += (o, e) => { int sd = opt.Seed; opt = ShuffleOptions.Defaults(session); opt.Seed = sd; Build(); };
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
                Text = "Every weapon, armour piece, food, light and runestone stays where it lies, but may become another item of its kind by the weights below, " +
                       "with its quality spread and its enchantment rolled again. Weights are relative: 0 removes a kind. Caliburn" + (s.IsUw2 ? ", the jewelled dagger" : "") +
                       ", talismans and quest items are never touched. Unlike the zone shuffle, nothing moves. The same seed gives the same result.",
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Label("Seed")); row.Children.Add(seed); row.Children.Add(newSeed); row.Children.Add(defaults);
            row.Children.Add(inv); row.Children.Add(keep);
            top.Children.Add(row);
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
        public ShuffleOptions Options { get { return opt; } }
        public bool CanUndoShuffle { get { return undoButton.IsEnabled; } }
        public string ReportText { get { return report.Text; } }

        // ---- options file

        static string OptionsPath(GameSession s) { return Path.Combine(Settings.Folder, "item-shuffle-" + (s.IsUw2 ? "uw2" : "uw1") + ".json"); }

        static ShuffleOptions LoadOptions(GameSession s)
        {
            try
            {
                string p = OptionsPath(s);
                if (File.Exists(p))
                {
                    var o = ShuffleOptions.FromJson(File.ReadAllText(p));
                    if (o != null && o.WeaponEnchants.Count > 0 && o.RuneWeights.Count > 0) return o;
                }
            }
            catch (Exception) { }
            return ShuffleOptions.Defaults(s);
        }

        void SaveOptions()
        {
            try { Directory.CreateDirectory(Settings.Folder); File.WriteAllText(OptionsPath(session), opt.ToJson()); } catch (Exception) { }
        }

        // ---- running

        ShuffleResult Run() { return ItemShuffle.Run(session, l => main.ViewModel.Edits.Model(l), opt); }

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
            int sel = Math.Max(0, tabs.SelectedIndex);
            tabs.Items.Clear();
            tabs.Items.Add(new TabItem { Header = "Weapons", Content = Scroll(WeaponsTab()) });
            tabs.Items.Add(new TabItem { Header = "Armour", Content = Scroll(ArmourTab()) });
            tabs.Items.Add(new TabItem { Header = "Food, lights, tools", Content = Scroll(FoodTab()) });
            tabs.Items.Add(new TabItem { Header = "Runestones", Content = Scroll(RunesTab()) });
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

        static Control EnchantRows(List<EnchantGroup> groups)
        {
            var p = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(Label("", 200));
            foreach (var t in new[] { "chance %", "mean strength", "deviation" }) head.Children.Add(new TextBlock { Text = t, Width = 196, Opacity = 0.7, FontSize = 12 });
            p.Children.Add(head);
            foreach (var g in groups)
            {
                int n = g.Effects.Length;
                p.Children.Add(Line(g.Name + " (1–" + n + ")",
                    Bar(0, 100, g.Probability, 1, x => g.Probability = x, 150),
                    Bar(1, n, Math.Max(1, Math.Min(n, g.Mean)), 0.1, x => g.Mean = x, 150, "0.0"),
                    Bar(0, n, Math.Min(n, g.Deviation), 0.1, x => g.Deviation = x, 150, "0.0")));
            }
            return p;
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
            p.Children.Add(Line("Quality spread ±", Bar(0, 63, opt.WeaponQualitySpread, 1, x => opt.WeaponQualitySpread = (int)x)));
            p.Children.Add(Heading("Enchantments (strength 1 = Minor)"));
            p.Children.Add(Note("Each kind is tried in a random order; the first that succeeds is the weapon's. The strength is a normal draw around the mean, kept in range."));
            p.Children.Add(EnchantRows(opt.WeaponEnchants));
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
            p.Children.Add(Line("Quality spread ±", Bar(0, 63, opt.ArmourQualitySpread, 1, x => opt.ArmourQualitySpread = (int)x)));
            p.Children.Add(Heading("Enchantments (strength 1 = Minor)"));
            p.Children.Add(EnchantRows(opt.ArmourEnchants));
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
    }
}
