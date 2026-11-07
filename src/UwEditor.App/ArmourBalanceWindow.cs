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
    /// Tools ▸ Balance ▸ Armour and rings: every wearable item, how many the levels hold (plain,
    /// enchanted, other), its armour at a chosen quality, and the damage a reference creature
    /// blow does at its hit location with and without it (ArmourBalance).
    /// </summary>
    public sealed class ArmourBalanceWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly PlayerStats stats;
        readonly BalanceBlow blow = new BalanceBlow();
        readonly SortableTable<ArmourRow> table;
        readonly TextBlock summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        readonly StatSlider quality, toHit, damage, weaponSkill;
        readonly ComboBox attacker = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        Func<int, LevelModel> levels;
        ObjectTables own;
        bool filling;

        public ArmourBalanceWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Balance armour and rings";
            Width = 1280; Height = 820; MinWidth = 900; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            stats = PlayerStats.From(session.Player);
            levels = BalanceUi.Levels(main);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var cols = new List<TableColumn<ArmourRow>>
            {
                new TableColumn<ArmourRow> { Header = "Item", Width = 170, Text = r => r.Enchantment.Length > 0 ? "  " + r.Name : r.Name, Key = r => r.Item * 1000 + (r.Enchantment.Length > 0 ? 1 : 0), Tip = "The item; its own line counts every copy, the indented lines below it the copies with one enchantment." },
                new TableColumn<ArmourRow> { Header = "Enchantment", Width = 160, Text = r => r.Enchantment, Tip = "The game's name for it (STRINGS.PAK block 6)." },
                new TableColumn<ArmourRow> { Header = "Effect", Width = 250, Text = r => r.Effect, Tip = "What it does while worn (game/PLAYDATA.C player_affected_by)." },
                new TableColumn<ArmourRow> { Header = "Slot", Width = 90, Text = r => r.Slot, Tip = "From the armour table's Category. A shield counts in the shield hand only for items 0x3B-0x3F." },
                new TableColumn<ArmourRow> { Header = "Covers", Width = 96, Text = r => string.Join("+", r.Locations.Select(l => ArmourBalance.LocationNames[l])), Tip = "The hit locations it adds armour to. Leggings and boots add up on the legs." },
                new TableColumn<ArmourRow> { Header = "Prot.", Width = 50, Right = true, Text = r => r.Protection.ToString(ci), Key = r => r.Protection, Tip = "The armour table's Protection." },
                new TableColumn<ArmourRow> { Header = "Armour", Width = 62, Right = true, Text = r => r.Armour.ToString(ci), Key = r => r.Armour, Tip = "At the quality chosen: quality × protection / 64 + 1, plus Toughness (a shield or ring: twice, on the torso)." },
                new TableColumn<ArmourRow> { Header = "To-hit", Width = 56, Right = true, Text = r => r.ToHitPenalty == 0 ? "" : "−" + r.ToHitPenalty, Key = r => r.ToHitPenalty, Tip = "Taken from a creature's to-hit at its locations by a Protection enchantment (melee only)." },
                new TableColumn<ArmourRow> { Header = "Blow", Width = 56, Right = true, Text = r => BalanceUi.F(r.Worn), Key = r => r.Worn, Tip = "Expected damage of the reference blow at the item's location with the item worn (misses and criticals counted)." },
                new TableColumn<ArmourRow> { Header = "Saved", Width = 60, Right = true, Text = r => BalanceUi.F(r.Saved), Key = r => r.Saved, Tip = "Less than with nothing worn there." },
                new TableColumn<ArmourRow> { Header = "Saved %", Width = 66, Right = true, Text = r => r.SavedPercent.ToString("0", ci) + "%", Key = r => r.SavedPercent },
                new TableColumn<ArmourRow> { Header = "Stones", Width = 60, Right = true, Text = r => r.MassStones.ToString("0.0", ci), Key = r => r.MassStones, Tip = "COMOBJ mass. The player carries Strength × 20 tenths (UW2: × 13 + 300) and slows down past half of it." },
                new TableColumn<ArmourRow> { Header = "Wear", Width = 74, Text = r => r.QualityClass >= 3 ? "never" : "2d4 >> " + r.QualityClass, Key = r => r.QualityClass, Tip = "A critical hit takes 2d4 >> quality class off the quality of the helm, the leggings (boots one time in five) or the shield; class 3 never wears." },
                new TableColumn<ArmourRow> { Header = "In world", Width = 70, Right = true, Text = r => r.Total.ToString(ci), Key = r => r.Total },
                new TableColumn<ArmourRow> { Header = "Plain", Width = 54, Right = true, Text = r => r.Plain.ToString(ci), Key = r => r.Plain },
                new TableColumn<ArmourRow> { Header = "Ench.", Width = 54, Right = true, Text = r => r.Enchanted.ToString(ci), Key = r => r.Enchanted, Tip = "With a Protection or Toughness enchantment." },
                new TableColumn<ArmourRow> { Header = "Other", Width = 54, Right = true, Text = r => r.Other.ToString(ci), Key = r => r.Other, Tip = "With another enchantment (resistances, regeneration, a curse, a spell cast on use...)." },
            };
            table = new SortableTable<ArmourRow>(cols, new ArmourRow[0]);

            var left = new StackPanel { Spacing = 2, Width = 300, Margin = new Thickness(0, 0, 18, 0) };
            var panel = new PlayerStatsPanel(session, stats, new[] { PlayerStats.Defense }, true);
            panel.Changed += Fill;
            left.Children.Add(panel);
            weaponSkill = new StatSlider("Weapon skill", 0, 30, stats[PlayerStats.Sword], "The skill of the weapon in hand (Sword, Axe, Mace, or Unarmed with none): half of it adds to the player's Defence.");
            weaponSkill.ValueChanged += v => Fill();
            left.Children.Add(weaponSkill);
            left.Children.Add(BalanceUi.Heading("ITEMS"));
            quality = new StatSlider("Quality", 0, 63, 63, "The items' quality: armour is quality × protection / 64 + 1.");
            quality.ValueChanged += v => Fill();
            left.Children.Add(quality);
            left.Children.Add(BalanceUi.Heading("THE BLOW"));
            var names = new List<string> { "Custom" };
            for (int c = 0; c < 64; c++) names.Add(string.Format("{0} (0x{1:X2})", session.Names.Get(0x40 + c), 0x40 + c));
            attacker.ItemsSource = names;
            attacker.SelectedIndex = 0;
            ToolTip.SetTip(attacker, "Take a creature's strongest attack: its to-hit and its damage + Strength / 5 (OBJECTS.DAT).");
            left.Children.Add(attacker);
            toHit = new StatSlider("To-hit", 0, 40, blow.ToHit, "The attack's to-hit (creature attack to-hit + equip / 2; a powerful creature 7-12 more).");
            damage = new StatSlider("Damage", 1, 60, blow.Damage, "The attack's damage + the creature's Strength / 5 (a powerful creature 4-15 more), rolled as dice.");
            left.Children.Add(toHit); left.Children.Add(damage);
            var reread = new Button { Content = "Read the levels again", Margin = new Thickness(0, 10, 0, 0) };
            reread.Click += (o, e) => { levels = BalanceUi.Levels(main); Fill(); };
            left.Children.Add(reread);
            left.Children.Add(new TextBlock
            {
                Text = "A creature's blow hits one location (torso, hands, legs or head, by the height of the swing). It needs its to-hit + a roll 0-30 − the player's Defence above 15; its damage, doubled on half the criticals, is rolled as dice and loses that location's armour. Missiles never miss and are not slowed by Protection. Rings only work through their enchantment.",
                FontSize = 11, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            });
            attacker.SelectionChanged += (o, e) =>
            {
                if (filling || attacker.SelectedIndex <= 0) return;
                var t = Tables();
                int c = attacker.SelectedIndex - 1, best = 1, bd = -1;
                for (int a = 1; a <= 3; a++) { int d = t.Get("Creatures", c, "Attack " + a + " damage"); if (d > bd && t.Get("Creatures", c, "Attack " + a + " probability") > 0) { bd = d; best = a; } }
                if (bd < 0) bd = t.Get("Creatures", c, "Attack 1 damage");
                filling = true;
                toHit.SetQuietly(t.Get("Creatures", c, "Attack " + best + " to-hit") + t.Get("Creatures", c, "Equip") / 2);
                damage.SetQuietly(bd + t.Get("Creatures", c, "Strength") / 5);
                filling = false;
                Fill();
            };
            toHit.ValueChanged += v => { if (!filling) { attacker.SelectedIndex = 0; Fill(); } };
            damage.ValueChanged += v => { if (!filling) { attacker.SelectedIndex = 0; Fill(); } };

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new ScrollViewer { Content = left, Margin = new Thickness(0, 0, 14, 0) });
            var right = new DockPanel();
            DockPanel.SetDock(summary, Dock.Bottom);
            right.Children.Add(summary);
            right.Children.Add(table);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Content = new Border { Padding = new Thickness(14), Child = grid };
            ManualWindow.Attach(this, "Balance armour and rings");
            Activated += (o, e) => Fill();
            Fill();
        }

        ObjectTables Tables() { return BalanceUi.Tables(main, ref own); }

        public SortableTable<ArmourRow> Table { get { return table; } }
        public string Summary { get { return summary.Text; } }
        public StatSlider QualitySlider { get { return quality; } }
        public StatSlider DamageSlider { get { return damage; } }
        public ComboBox AttackerBox { get { return attacker; } }

        public void Fill()
        {
            if (main.ViewModel.Session != session) { Close(); return; }
            blow.ToHit = toHit.Value; blow.Damage = damage.Value;
            List<ArmourRow> rows;
            // the weapon skill slider stands for whichever skill the wielded weapon uses
            var p = new PlayerStats { Strength = stats.Strength, Dexterity = stats.Dexterity, Easy = stats.Easy };
            for (int i = 0; i < p.Skills.Length; i++) p.Skills[i] = stats.Skills[i];
            p[PlayerStats.Sword] = weaponSkill.Value;
            try { rows = ArmourBalance.Rows(session, Tables(), levels, p, PlayerStats.Sword, blow, quality.Value); }
            catch (Exception ex) { summary.Text = "Could not work the armour out: " + ex.Message; return; }
            table.SetRows(rows);
            var heads = rows.Where(r => r.Enchantment.Length == 0).ToList();
            int defence = p[PlayerStats.Defense] + weaponSkill.Value / 2;
            double bare = heads.Count > 0 ? heads[0].Bare : 0;
            // the best set the world offers: per location the item with the most armour found there
            var bestBy = new string[4];
            var bestArmour = new int[4];
            foreach (var r in heads.Where(r => r.Total > 0 && r.Slot != "Ring"))
                foreach (int l in r.Locations) if (r.Armour > bestArmour[l]) { bestArmour[l] = r.Armour; bestBy[l] = r.Name; }
            summary.Text = string.Format("Player's Defence {0} (Defense + weapon skill / 2). The blow does {1} on average at a bare location. In the levels: {2} pieces of armour and shields ({3} enchanted, {4} other) and {5} rings ({6} enchanted). Most armour found per location (plain, at this quality): {7}.",
                defence, BalanceUi.F(bare), heads.Where(r => r.Slot != "Ring").Sum(r => r.Total), heads.Where(r => r.Slot != "Ring").Sum(r => r.Enchanted), heads.Where(r => r.Slot != "Ring").Sum(r => r.Other),
                heads.Where(r => r.Slot == "Ring").Sum(r => r.Total), heads.Where(r => r.Slot == "Ring").Sum(r => r.Enchanted + r.Other),
                string.Join(", ", Enumerable.Range(0, 4).Select(l => ArmourBalance.LocationNames[l] + " " + (bestBy[l] == null ? "nothing" : bestBy[l] + " (" + bestArmour[l] + ")"))));
        }
    }
}
