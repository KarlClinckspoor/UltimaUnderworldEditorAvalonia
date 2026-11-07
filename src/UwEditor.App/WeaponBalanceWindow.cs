using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using UwEditor.Core;
using UwEditor.Core.Balance;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Balance ▸ Weapons: every melee weapon and launcher, how many the levels hold (plain,
    /// enchanted, special, other), and the damage a player of the chosen skills expects from each
    /// swing kind, per swing and per second, against a chosen target (WeaponBalance).
    /// </summary>
    public sealed class WeaponBalanceWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly PlayerStats stats;
        readonly BalanceTarget target = new BalanceTarget();
        readonly SortableTable<WeaponRow> melee, ranged;
        readonly TextBlock summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        readonly StatSlider defence, armour, aim;
        readonly CheckBox powerful = new CheckBox { Content = "Powerful (armour × 5/3)" };
        readonly ComboBox creature = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        readonly TabControl tabs = new TabControl();
        Func<int, LevelModel> levels;
        ObjectTables own;
        bool filling;

        public WeaponBalanceWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Balance weapons";
            Width = 1320; Height = 820; MinWidth = 900; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            stats = PlayerStats.From(session.Player);
            levels = BalanceUi.Levels(main);

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var mcols = new List<TableColumn<WeaponRow>>
            {
                new TableColumn<WeaponRow> { Header = "Weapon", Width = 150, Text = r => r.Enchantment.Length > 0 ? "  " + r.Name : r.Name, Key = r => r.Item * 1000 + (r.Enchantment.Length > 0 ? 1 : 0), Tip = "The weapon; its own line counts every copy, the indented lines below it the copies with one enchantment." },
                new TableColumn<WeaponRow> { Header = "Enchantment", Width = 150, Text = r => r.Enchantment, Tip = "The game's name for it (STRINGS.PAK block 6). UW2's names for accuracy and damage are swapped: see Effect." },
                new TableColumn<WeaponRow> { Header = "Effect", Width = 170, Text = r => r.Effect, Tip = "What the enchantment does in a fight (combat/COMBAT.C DoPlayerWeapon)." },
                new TableColumn<WeaponRow> { Header = "Skill", Width = 64, Text = r => r.Skill },
                new TableColumn<WeaponRow> { Header = "Base", Width = 74, Right = true, Text = r => string.Join("/", r.Base), Key = r => r.Base.Max(), Tip = "Slash / bash / stab damage of the weapon table (bare hands: Unarmed × 2 / 5 + Strength / 6 + 4); Strength / 9 and a damage enchantment are added." },
                new TableColumn<WeaponRow> { Header = "Charge", Width = 60, Right = true, Text = r => BalanceUi.F2(r.MaxCharge / 128.0), Key = r => r.MaxCharge, Tip = "A full charge multiplies the rolled damage by Max charge / 128." },
                new TableColumn<WeaponRow> { Header = "Speed", Width = 54, Right = true, Text = r => r.Speed.ToString(ci), Key = r => r.Speed, Tip = "The charge grows by Speed every 16 ticks (1/16 s) up to 100." },
                new TableColumn<WeaponRow> { Header = "Swing s", Width = 70, Right = true, Text = r => Math.Abs(r.Seconds[2] - r.Seconds[0]) < 0.001 ? BalanceUi.F2(r.Seconds[0]) : BalanceUi.F2(r.Seconds[0]) + "/" + BalanceUi.F2(r.Seconds[2]), Key = r => r.Seconds[0], Tip = "Seconds from pressing to the next swing, released at full charge (UW2: a stab's is shorter)." },
                new TableColumn<WeaponRow> { Header = "Slash", Width = 58, Right = true, Text = r => BalanceUi.F(r.PerSwing[0]), Key = r => r.PerSwing[0], Tip = "Expected damage of a slash, misses and criticals counted." },
                new TableColumn<WeaponRow> { Header = "Bash", Width = 58, Right = true, Text = r => BalanceUi.F(r.PerSwing[1]), Key = r => r.PerSwing[1], Tip = "Expected damage of a bash." },
                new TableColumn<WeaponRow> { Header = "Stab", Width = 58, Right = true, Text = r => BalanceUi.F(r.PerSwing[2]), Key = r => r.PerSwing[2], Tip = "Expected damage of a stab." },
                new TableColumn<WeaponRow> { Header = "Slash/s", Width = 66, Right = true, Text = r => BalanceUi.F2(r.PerSecond[0]), Key = r => r.PerSecond[0], Tip = "Expected damage per second, slashing over and over." },
                new TableColumn<WeaponRow> { Header = "Bash/s", Width = 66, Right = true, Text = r => BalanceUi.F2(r.PerSecond[1]), Key = r => r.PerSecond[1] },
                new TableColumn<WeaponRow> { Header = "Stab/s", Width = 66, Right = true, Text = r => BalanceUi.F2(r.PerSecond[2]), Key = r => r.PerSecond[2] },
                new TableColumn<WeaponRow> { Header = "Best/s", Width = 66, Right = true, Text = r => BalanceUi.F2(r.BestPerSecond), Key = r => r.BestPerSecond, Tip = "The best of the three." },
                new TableColumn<WeaponRow> { Header = "In world", Width = 70, Right = true, Text = r => r.Total.ToString(ci), Key = r => r.Total, Tip = "Copies in the levels (on the floor, in containers, carried by creatures, made by traps)." },
                new TableColumn<WeaponRow> { Header = "Plain", Width = 54, Right = true, Text = r => r.Plain.ToString(ci), Key = r => r.Plain },
                new TableColumn<WeaponRow> { Header = "Ench.", Width = 54, Right = true, Text = r => r.Enchanted.ToString(ci), Key = r => r.Enchanted, Tip = "With an accuracy or damage enchantment." },
                new TableColumn<WeaponRow> { Header = "Special", Width = 62, Right = true, Text = r => r.Special.ToString(ci), Key = r => r.Special, Tip = "UW2's special weapons (class 12, effects 8-15)." },
                new TableColumn<WeaponRow> { Header = "Other", Width = 54, Right = true, Text = r => r.Other.ToString(ci), Key = r => r.Other, Tip = "Another enchantment, which does nothing in a fight (a spell cast on use, say)." },
            };
            var rcols = new List<TableColumn<WeaponRow>>
            {
                new TableColumn<WeaponRow> { Header = "Launcher", Width = 150, Text = r => r.Enchantment.Length > 0 ? "  " + r.Name : r.Name, Key = r => r.Item * 1000 + (r.Enchantment.Length > 0 ? 1 : 0) },
                new TableColumn<WeaponRow> { Header = "Enchantment", Width = 150, Text = r => r.Enchantment },
                new TableColumn<WeaponRow> { Header = "Effect", Width = 220, Text = r => r.Effect },
                new TableColumn<WeaponRow> { Header = "Ammunition", Width = 130, Text = r => r.AmmoName, Tip = "The launcher's Ammo byte names it (0 stones, 1 bolts, 2 arrows)." },
                new TableColumn<WeaponRow> { Header = "Damage", Width = 66, Right = true, Text = r => r.Base[0].ToString(ci), Key = r => r.Base[0], Tip = "The ammunition's damage: the launcher's own damage is never used." },
                new TableColumn<WeaponRow> { Header = "Per shot", Width = 70, Right = true, Text = r => BalanceUi.F(r.PerSwing[0]), Key = r => r.PerSwing[0], Tip = "Expected damage of a shot that hits (Missile skill, the dice, the target's armour). Missiles have no to-hit roll: a shot that reaches the target hits." },
                new TableColumn<WeaponRow> { Header = "Shot s", Width = 62, Right = true, Text = r => BalanceUi.F2(r.Seconds[0]), Key = r => r.Seconds[0], Tip = "About 5 picture steps (0.625 s) plus the aiming time." },
                new TableColumn<WeaponRow> { Header = "Per s", Width = 62, Right = true, Text = r => BalanceUi.F2(r.PerSecond[0]), Key = r => r.PerSecond[0] },
                new TableColumn<WeaponRow> { Header = "In world", Width = 70, Right = true, Text = r => r.Total.ToString(ci), Key = r => r.Total },
                new TableColumn<WeaponRow> { Header = "Plain", Width = 54, Right = true, Text = r => r.Plain.ToString(ci), Key = r => r.Plain },
                new TableColumn<WeaponRow> { Header = "Ench.", Width = 54, Right = true, Text = r => (r.Enchanted + r.Special + r.Other).ToString(ci), Key = r => r.Enchanted + r.Special + r.Other, Tip = "Enchanted launchers: the enchantment does nothing." },
                new TableColumn<WeaponRow> { Header = "Ammo in world", Width = 110, Right = true, Text = r => r.AmmoCount.ToString(ci), Key = r => r.AmmoCount, Tip = "The ammunition's stacks added up." },
            };
            melee = new SortableTable<WeaponRow>(mcols, new WeaponRow[0]);
            ranged = new SortableTable<WeaponRow>(rcols, new WeaponRow[0]);
            tabs.Items.Add(new TabItem { Header = "Melee", Content = melee });
            tabs.Items.Add(new TabItem { Header = "Launchers", Content = ranged });

            // left: the player, the target, the aim
            var left = new StackPanel { Spacing = 2, Width = 300, Margin = new Thickness(0, 0, 18, 0) };
            var panel = new PlayerStatsPanel(session, stats, new[] { -1, -2, PlayerStats.Attack, PlayerStats.Unarmed, PlayerStats.Sword, PlayerStats.Axe, PlayerStats.Mace, PlayerStats.Missile }, true);
            panel.Changed += Fill;
            left.Children.Add(panel);
            left.Children.Add(BalanceUi.Heading("TARGET"));
            var names = new List<string> { "Custom" };
            for (int c = 0; c < 64; c++) names.Add(string.Format("{0} (0x{1:X2})", session.Names.Get(0x40 + c), 0x40 + c));
            creature.ItemsSource = names;
            creature.SelectedIndex = 0;
            ToolTip.SetTip(creature, "Take the defence and armour (the four hit zones' average) of a creature (OBJECTS.DAT).");
            left.Children.Add(creature);
            defence = new StatSlider("Defence", -10, 40, target.Defence, "The target's defence (creature byte 0x12): taken from the player's to-hit.");
            armour = new StatSlider("Armour", 0, 30, target.Armour, "Taken from the damage of each blow that hits (a creature's armour at the zone hit).");
            left.Children.Add(defence); left.Children.Add(armour); left.Children.Add(powerful);
            ToolTip.SetTip(powerful, "A powerful creature (mobile object flag): its armour counts 5/3 times against the player.");
            left.Children.Add(BalanceUi.Heading("LAUNCHERS"));
            aim = new StatSlider("Aim (1/10 s)", 0, 30, 3, "Time spent aiming between shots, added to the shot's 0.625 s.");
            left.Children.Add(aim);
            var reread = new Button { Content = "Read the levels again", Margin = new Thickness(0, 10, 0, 0) };
            ToolTip.SetTip(reread, "Counts the weapons again after the levels changed. The tables are read again whenever the window is brought up (with the object tables window's unsaved edits).");
            reread.Click += (o, e) => { levels = BalanceUi.Levels(main); Fill(); };
            left.Children.Add(reread);
            left.Children.Add(new TextBlock
            {
                Text = "Melee: to hit is Attack / 2 + the weapon's skill + Dexterity / 7 against the target's defence (a roll 0-30 above 15 hits, above 28 is critical); damage is the swing's base + Strength / 9, doubled on half the criticals, rolled as dice, × the full charge / 128, less the armour. Quality changes nothing. Missiles: the ammunition's damage, scaled by the Missile skill; no charge, strength or critical; launchers and ammunition ignore enchantments.",
                FontSize = 11, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            });

            creature.SelectionChanged += (o, e) =>
            {
                if (filling || creature.SelectedIndex <= 0) return;
                var t = WeaponBalance.TargetOf(Tables(), creature.SelectedIndex - 1);
                filling = true;
                defence.SetQuietly(t.Defence); armour.SetQuietly(t.Armour);
                filling = false;
                Fill();
            };
            defence.ValueChanged += v => { if (!filling) { creature.SelectedIndex = 0; Fill(); } };
            armour.ValueChanged += v => { if (!filling) { creature.SelectedIndex = 0; Fill(); } };
            powerful.IsCheckedChanged += (o, e) => Fill();
            aim.ValueChanged += v => Fill();

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var leftScroll = new ScrollViewer { Content = left, Margin = new Thickness(0, 0, 14, 0) };
            grid.Children.Add(leftScroll);
            var right = new DockPanel();
            DockPanel.SetDock(summary, Dock.Bottom);
            right.Children.Add(summary);
            right.Children.Add(tabs);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Content = new Border { Padding = new Thickness(14), Child = grid };
            ManualWindow.Attach(this, "Balance weapons");
            Activated += (o, e) => Fill();
            Fill();
        }

        ObjectTables Tables() { return BalanceUi.Tables(main, ref own); }

        public SortableTable<WeaponRow> Melee { get { return melee; } }
        public SortableTable<WeaponRow> Ranged { get { return ranged; } }
        public PlayerStats Stats { get { return stats; } }
        public BalanceTarget Target { get { return target; } }
        public string Summary { get { return summary.Text; } }
        public StatSlider DefenceSlider { get { return defence; } }
        public StatSlider ArmourSlider { get { return armour; } }
        public ComboBox CreatureBox { get { return creature; } }

        /// <summary>Works the rows out again.</summary>
        public void Fill()
        {
            if (main.ViewModel.Session != session) { Close(); return; }
            target.Defence = defence.Value; target.Armour = armour.Value; target.Powerful = powerful.IsChecked == true;
            List<WeaponRow> rows;
            try { rows = WeaponBalance.Rows(session, Tables(), levels, stats, target, aim.Value / 10.0); }
            catch (Exception ex) { summary.Text = "Could not work the weapons out: " + ex.Message; return; }
            melee.SetRows(rows.Where(r => !r.Ranged));
            ranged.SetRows(rows.Where(r => r.Ranged));
            var heads = rows.Where(r => r.Enchantment.Length == 0).ToList();
            var m = heads.Where(r => !r.Ranged).ToList();
            var l = heads.Where(r => r.Ranged).ToList();
            var bestM = m.Where(r => r.Total > 0).OrderByDescending(r => r.BestPerSecond).FirstOrDefault();
            var bestL = l.Where(r => r.Total > 0).OrderByDescending(r => r.BestPerSecond).FirstOrDefault();
            summary.Text = string.Format("In the levels: {0} melee weapons ({1} plain, {2} enchanted, {3} special, {4} other) and {5} launchers. Best plain melee weapon found in the world: {6}; best launcher: {7}.",
                m.Sum(r => r.Total), m.Sum(r => r.Plain), m.Sum(r => r.Enchanted), m.Sum(r => r.Special), m.Sum(r => r.Other), l.Sum(r => r.Total),
                bestM == null ? "none" : bestM.Name + " (" + BalanceUi.F2(bestM.BestPerSecond) + " a second)",
                bestL == null ? "none" : bestL.Name + " (" + BalanceUi.F2(bestL.BestPerSecond) + " a second)");
        }
    }
}
