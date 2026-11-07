using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Balance;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Balance: the barter simulator, the weapons and the armour windows; Tools ▸ Conversations.</summary>
    public class BalanceWindowsTests
    {
        /// <summary>A picture of the window, when BALANCE_SHOTS names a folder.</summary>
        static void Shot(Window w, string name)
        {
            string dir = System.Environment.GetEnvironmentVariable("BALANCE_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            System.IO.Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), System.IO.Path.Combine(dir, name + ".png"));
        }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_weapons_window_lists_the_weapons_and_follows_the_sliders(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var bw = w.ShowWeaponBalance();
            Ui.Pump();
            var longsword = bw.Melee.Shown.First(r => r.Item == 0x05 && r.Enchantment.Length == 0);
            Assert.True(longsword.Total > 0);
            Assert.Contains(bw.Ranged.Shown, r => r.AmmoCount > 0);
            Assert.Contains("melee weapons", bw.Summary);
            double before = longsword.PerSwing[0];
            // a tougher target: less damage
            bw.ArmourSlider.Value = 10;
            Ui.Pump();
            var after = bw.Melee.Shown.First(r => r.Item == 0x05 && r.Enchantment.Length == 0);
            Assert.True(after.PerSwing[0] < before);
            // a creature as the target fills defence and armour
            bw.CreatureBox.SelectedIndex = 1 + 0x0A;
            Ui.Pump();
            Assert.Equal(bw.Target.Defence, bw.DefenceSlider.Value);
            // the table sorts by a header and keeps the order after a change
            bw.Melee.SortBy(14);
            bw.ArmourSlider.Value = 0;
            Ui.Pump();
            Assert.Equal(14, bw.Melee.SortColumn);
            var best = bw.Melee.Shown.Select(r => r.BestPerSecond).ToList();
            Assert.Equal(best.OrderBy(x => x).ToList(), best);
            Shot(bw, "weapons-" + game);
            bw.Close();
            w.Close();
        }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_armour_window_lists_armour_and_rings(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowArmourBalance();
            Ui.Pump();
            var plate = aw.Table.Shown.First(r => r.Item == 0x22 && r.Enchantment.Length == 0);
            Assert.Equal(6, plate.Armour);
            Assert.Contains(aw.Table.Shown, r => r.Slot == "Ring" && r.Total > 0);
            aw.QualitySlider.Value = 0;
            Ui.Pump();
            Assert.Equal(1, aw.Table.Shown.First(r => r.Item == 0x22 && r.Enchantment.Length == 0).Armour);
            aw.AttackerBox.SelectedIndex = 1 + 0x0A;
            Ui.Pump();
            Assert.Contains("Defence", aw.Summary);
            Shot(aw, "armour-" + game);
            aw.Close();
            w.Close();
        }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_barter_simulator_values_a_trade_and_balances_it_with_coins(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var bs = w.ShowBarterSimulator();
            Ui.Pump();
            // a talking creature of the levels comes first, with its exact temper
            Assert.True(bs.Traders.Count > 64);
            Assert.True(bs.Traders[0].ObjectIndex > 0);
            int any = bs.Traders.ToList().FindIndex(t => t.ObjectIndex < 0 && t.Class == 0x0A);
            bs.TraderBox.SelectedIndex = any;
            Ui.Pump();
            Assert.False(bs.Temper.Exact);
            bs.Add(new BarterItem { Item = 0x05, Quality = 63 }, false);
            Ui.Pump();
            Assert.False(bs.Result.Accepted);
            Assert.Contains("refuses", bs.ResultText);
            bs.Balance();
            Ui.Pump();
            Assert.Single(bs.PlayerSide);
            Assert.Equal(0xA0, bs.PlayerSide[0].Item);
            Assert.True(bs.Result.Accepted);
            Assert.Contains("accepts", bs.ResultText);
            Shot(bs, "barter-" + game);
            // the player's Charm lowers the greed
            int greed = bs.Temper.Greed;
            bs.Stats[PlayerStats.Charm] = bs.Stats[PlayerStats.Charm] + 5;
            bs.Recompute();
            Assert.Equal(greed - 10, bs.Temper.Greed);
            bs.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_tools_menu_groups_the_conversation_and_balance_tools()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            var items = w.GetLogicalDescendants().OfType<MenuItem>().ToList();
            var tools = items.First(m => (m.Header as string) == "_Tools");
            var subs = tools.Items.OfType<MenuItem>().ToList();
            var conv = subs.First(m => (m.Header as string) == "_Conversations");
            Assert.Equal(new[] { "_All conversations…", "_Simulator…", "_Graph…" }, conv.Items.OfType<MenuItem>().Select(m => m.Header as string).ToArray());
            var bal = subs.First(m => (m.Header as string) == "Ba_lance");
            Assert.Equal(3, bal.Items.OfType<MenuItem>().Count());
            Assert.DoesNotContain(subs, m => (m.Header as string ?? "").StartsWith("Conversation "));
            w.Close();
        }
    }
}
