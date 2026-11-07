using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Balance;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The balance tools' rules: trading, weapon damage and timing, armour.</summary>
    public class BalanceTests
    {
        [Fact]
        public void Borland_rand_gives_the_turbo_c_sequence()
        {
            var r = new BorlandRandom(1);
            Assert.Equal(new[] { 346, 130, 10982, 1090, 11656 }, Enumerable.Range(0, 5).Select(i => r.Next()).ToArray());
        }

        [Fact]
        public void An_items_worth_follows_assess_value_with_its_16_bit_products()
        {
            // UW2 counts a coin at quality 63, UW1 at its own
            Assert.Equal(10 * 3 * 63 >> 6, Barter.Worth(GameKind.UW2, 0xA0, 3, 10, 5, 0));
            Assert.Equal(10 * 3 * 40 >> 6, Barter.Worth(GameKind.UW1, 0xA0, 3, 10, 40, 0));
            // UW1: 300 coins of quality 40 pass 32767 (900 × 40) and come out below 0
            Assert.True(Barter.Worth(GameKind.UW1, 0xA0, 3, 300, 40, 0) < 0);
            Assert.True(Barter.Wraps(GameKind.UW1, 3, 300, 40, false));
            Assert.False(Barter.Wraps(GameKind.UW2, 3, 300, 40, false));
            // liked: × 1.5; disliked or quality 0: nothing; at least 1 otherwise
            Assert.Equal(15 * 63 >> 6, Barter.Worth(GameKind.UW1, 5, 10, 1, 63, 1));
            Assert.Equal(0, Barter.Worth(GameKind.UW1, 5, 10, 1, 63, -1));
            Assert.Equal(0, Barter.Worth(GameKind.UW1, 5, 10, 1, 0, 0));
            Assert.Equal(1, Barter.Worth(GameKind.UW1, 5, 1, 1, 1, 0));
            // the evaluation, and a side worth nothing counting 100 %
            Assert.Equal(10, Barter.Evaluate(110, 100));
            Assert.Equal(100, Barter.Evaluate(5, 0));
        }

        [Fact]
        public void A_traders_temper_is_drawn_from_its_object_index()
        {
            var c = new TraderClass { Haggle = 8, Patience = 9, Shrewd = 10, Level = 5 };
            var a = Barter.Temper(c, 123, 0);
            var b = Barter.Temper(c, 123, 0);
            Assert.True(a.Exact);
            Assert.Equal(a.Greed, b.Greed);
            Assert.InRange(a.Greed, 48 * 75 / 100, 48 * 125 / 100);
            // Charm: greed − 2 a point, patience + half a point
            var charmed = Barter.Temper(c, 123, 10);
            Assert.Equal(a.Greed - 20, charmed.Greed);
            Assert.Equal(a.Patience + 5, charmed.Patience);
            var any = Barter.Temper(c, -1, 0);
            Assert.False(any.Exact);
            Assert.Equal(48, any.Greed);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Likes_and_dislikes_are_read_from_the_conversations(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var all = lib.Archive.Slots().Select(slot => Barter.LikesOf(lib, slot)).Where(l => l != null).ToList();
            Assert.NotEmpty(all);
            if (game == "UW")
            {
                var two = Barter.LikesOf(lib, 2);
                Assert.NotNull(two);
                Assert.Contains(1000, two.Likes);                 // every melee weapon
                Assert.Contains(0x123, two.Dislikes);             // the mandolin
                Assert.Equal(1, two.Of(0x05, 10));                // a longsword: liked
                Assert.Equal(-1, two.Of(0x123, 10));
                Assert.Equal(-1, two.Of(0x05, 0));                // worthless
            }
            else Assert.Contains(all, l => l.Likes.Contains(0xBD));   // someone likes a bottle of wine
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Balance_trade_adds_the_fewest_coins_the_trader_accepts(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var t = ObjectTables.Load(s);
            Func<int, int> value = i => t.Get("Common properties", i, "Value");
            var c = Barter.Classes(s, t).First(x => x.Haggle > 0);
            var temper = Barter.Temper(c, 77, 5);
            var trader = new List<BarterItem> { new BarterItem { Item = 0x05, Quality = 63 } };      // a longsword
            var player = new List<BarterItem>();
            int q = s.IsUw2 ? 63 : 40;
            bool toPlayer; string note;
            var coins = Barter.Settle(s.Kind, player, trader, value, null, temper, q, false, out toPlayer, out note);
            Assert.True(toPlayer);
            Assert.Single(coins);
            int n = coins[0].Quantity;
            var r = Barter.Offer(s.Kind, coins, trader, value, null, temper, 10, s.Strings);
            Assert.True(r.Accepted);
            var fewer = new List<BarterItem> { new BarterItem { Item = 0xA0, Quantity = n - 1, Quality = q } };
            Assert.False(Barter.Offer(s.Kind, fewer, trader, value, null, temper, 10, s.Strings).Accepted);
            Assert.NotNull(r.Judgement);
            // equal worth needs fewer coins than a greedy trader's price
            var even = Barter.Settle(s.Kind, player, trader, value, null, temper, q, true, out toPlayer, out note);
            Assert.True(even[0].Quantity <= n);
            // the player gives far more: the trader pays coins back, the most it still accepts
            var rich = new List<BarterItem> { new BarterItem { Item = 0x05, Quality = 63 }, new BarterItem { Item = 0x05, Quality = 63 }, new BarterItem { Item = 0x05, Quality = 63 } };
            var back = Barter.Settle(s.Kind, rich, trader, value, null, temper, q, false, out toPlayer, out note);
            Assert.False(toPlayer);
            Assert.NotEmpty(back);
            var withBack = trader.Concat(back).ToList();
            Assert.True(Barter.Offer(s.Kind, rich, withBack, value, null, temper, 10, s.Strings).Accepted);
            withBack[withBack.Count - 1] = new BarterItem { Item = 0xA0, Quantity = back.Last().Quantity + 1, Quality = q };
            Assert.False(Barter.Offer(s.Kind, rich, withBack, value, null, temper, 10, s.Strings).Accepted);
        }

        [Fact]
        public void The_dice_and_the_swing_timing_are_the_games()
        {
            // 7 damage: 1d6 + 1d1, 4.5 on average
            Assert.Equal(4.5, Dice.Expected(7, 128, 0, 0), 6);
            // armour takes from each roll, not below 0
            Assert.Equal(10 / 6.0, Dice.Expected(7, 128, 0, 3), 6);       // 2-7 less 3: 0, 0, 1, 2, 3, 4
            Assert.Equal(0, Dice.Expected(7, 128, 0, 20));
            // to hit: 15 + d in 31, critical d + 2 in 31
            double hit, crit;
            Dice.HitChances(0, out hit, out crit);
            Assert.Equal(15 / 31.0, hit, 6); Assert.Equal(2 / 31.0, crit, 6);
            // UW1 longsword (speed 15): about 400 ticks; UW2's stab two steps shorter
            Assert.Equal(400, WeaponBalance.SwingTicks(GameKind.UW1, 15, 0, false));
            Assert.Equal(400, WeaponBalance.SwingTicks(GameKind.UW1, 15, 2, false));
            Assert.Equal(400 - 64, WeaponBalance.SwingTicks(GameKind.UW2, 15, 2, false));
            Assert.Equal(6, ArmourBalance.ArmourValue(63, 6));
            Assert.Equal(1, ArmourBalance.ArmourValue(0, 6));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_overviews_count_the_worlds_weapons_and_armour(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var t = ObjectTables.Load(s);
            Func<int, LevelModel> lev = n => s.LoadLevel(n);
            var p = new PlayerStats();
            var rows = WeaponBalance.Rows(s, t, lev, p, new BalanceTarget(), 0.3);
            var longsword = rows.First(r => r.Item == 0x05 && r.Enchantment.Length == 0);
            Assert.True(longsword.Total > 0);
            Assert.Equal(longsword.Total, longsword.Plain + longsword.Enchanted + longsword.Special + longsword.Other);
            Assert.Equal(longsword.Total - longsword.Plain, rows.Where(r => r.Item == 0x05 && r.Enchantment.Length > 0).Sum(r => r.Total));
            Assert.True(longsword.PerSecond[0] > 0);
            // stronger: more damage
            p.Strength = 40;
            var strong = WeaponBalance.Rows(s, t, lev, p, new BalanceTarget(), 0.3).First(r => r.Item == 0x05 && r.Enchantment.Length == 0);
            Assert.True(strong.PerSwing[0] > longsword.PerSwing[0]);
            // a launcher fires its ammunition, whose damage alone counts
            var crossbow = rows.First(r => r.Ranged && r.Enchantment.Length == 0 && r.AmmoName.Contains("bolt"));
            Assert.Equal(t.Get("Ranged", crossbow.Ammo - 0x10, "Damage"), crossbow.Base[0]);
            Assert.True(crossbow.AmmoCount > 0);
            Assert.DoesNotContain(rows, r => r.Ranged && r.Ammo < 0);

            var arm = ArmourBalance.Rows(s, t, lev, p, PlayerStats.Sword, new BalanceBlow(), 63);
            var plate = arm.First(r => r.Item == 0x22 && r.Enchantment.Length == 0);     // breastplate: protection 6
            Assert.Equal(6, plate.Armour);
            Assert.True(plate.Worn < plate.Bare);
            Assert.Contains(arm, r => r.Slot == "Ring" && r.Total > 0);
            var low = ArmourBalance.Rows(s, t, lev, p, PlayerStats.Sword, new BalanceBlow(), 0).First(r => r.Item == 0x22 && r.Enchantment.Length == 0);
            Assert.Equal(1, low.Armour);
        }
    }
}
