using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>The Uniform progression rule of the monster swap and the item swap.</summary>
    public class ProgressionTests
    {
        readonly ITestOutputHelper output;
        public ProgressionTests(ITestOutputHelper output) { this.output = output; }

        static List<int> Levels(GameSession s) { return s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList(); }

        static double Xp(CreatureKind k, ObjectInfo o) { return Math.Max(0, k.Exp) * ((((o.Record[0x0D] | o.Record[0x0E] << 8) >> 10) & 1) != 0 ? 2.25 : 1); }

        [Fact]
        public void The_walk_takes_the_worlds_in_the_order_the_player_meets_them()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2");
            if (g1 == null || g2 == null) return;
            var s1 = GameSession.Open(g1);
            Assert.Equal(Enumerable.Range(1, 9), Progression.LevelOrder(s1, new[] { 9, 3, 1, 2, 8, 4, 5, 7, 6 }));
            var s2 = GameSession.Open(g2);
            var order = Progression.LevelOrder(s2, Enumerable.Range(1, 73));
            // Britannia 1-8 first, the Pits of Carnage (world 7, 57-64) before the Tomb (world 6, 49-56), then the Void
            Assert.Equal(Enumerable.Range(1, 48), order.Take(48));
            Assert.Equal(Enumerable.Range(57, 8), order.Skip(48).Take(8));
            Assert.Equal(Enumerable.Range(49, 8), order.Skip(56).Take(8));
            Assert.Equal(Enumerable.Range(65, 8), order.Skip(64).Take(8));
            // slot 73, outside the worlds, is not walked: what is there stays
            Assert.Equal(72, order.Count);
            Assert.False(Progression.InWalk(s2, 73));
            Assert.True(Progression.InWalk(s1, 9));
            Assert.Equal("Pits of Carnage", s2.Profile.WorldName(s2.Profile.WorldOf(57)));
            Assert.Equal("Tomb of Praecor Loth", s2.Profile.WorldName(s2.Profile.WorldOf(49)));
            Assert.StartsWith("Britannia → Prison Tower → Killorn Keep → Ice Caverns → Talorus → Scintillus Academy → Pits of Carnage → Tomb of Praecor Loth → Ethereal Void", Progression.OrderText(s2));
        }

        [Fact]
        public void Strengths_follow_the_data()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2");
            if (g1 == null || g2 == null) return;
            var s1 = GameSession.Open(g1);
            var d = Progression.ReadObjects(s1);
            Assert.Equal(14, Progression.WeaponDamage(d, 0x01));      // axe: slash 14
            Assert.Equal(17, Progression.WeaponDamage(d, 0x0E));      // bash 17
            Assert.Equal(8, Progression.WeaponDamage(d, 0x19));       // bow: arrows 8
            Assert.Equal(10, Progression.WeaponDamage(d, 0x1A));      // crossbow: bolts 10
            Assert.Equal(5, Progression.WeaponDamage(d, 0x10));       // sling stones
            Assert.Equal(6, Progression.ArmourProtection(d, 0x22));   // plate
            // UW1 weapon class 12: Accuracy 0-7 → 1-8, Damage 8-15 → 1-8; armour the same; a curse mid-range; a spell its circle
            Assert.Equal(1, Progression.EnchantLevel(s1, 0x04, true, true, 0x200 | 0xC0, false));
            Assert.Equal(8, Progression.EnchantLevel(s1, 0x04, true, true, 0x200 | 0xCF, false));
            Assert.Equal(3, Progression.EnchantLevel(s1, 0x22, true, true, 0x200 | 0xCA, false));
            Assert.Equal(Progression.MidLevel, Progression.EnchantLevel(s1, 0x38, true, true, 0x200 | 0x93, false));
            Assert.Equal(3, Progression.EnchantLevel(s1, 0x130, true, true, 0x200 | 13, true));   // spell 13: circle 3
            Assert.Equal(0, Progression.EnchantLevel(s1, 0x04, false, true, 0x200 | 0xC5, false));
            var s2 = GameSession.Open(g2);
            Assert.Equal(7, Progression.EnchantLevel(s2, 0x04, true, true, 0x200 | 0xC3, false));  // UW2 "Unsurpassed Accuracy": 7 damage
            Assert.Equal(5, Progression.EnchantLevel(s2, 0x04, true, true, 0x200 | 0xC6, false));  // UW2 "Great Damage": 5 to hit
            Assert.Equal(Progression.MidLevel, Progression.EnchantLevel(s2, 0x04, true, true, 0x200 | 0xC9, false));  // a special weapon
            Assert.Equal(8, Progression.SpellCircle(true, 63));
            Assert.Equal(9, Progression.SpellCircle(false, 50));
        }

        [Fact]
        public void A_spot_none_of_its_share_fits_pulls_from_the_next_level_and_the_rest_spills()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            // four spots, two on level 1 and two on level 2, holding strengths 0..3; spot 0 takes neither 0 nor 1
            int fb, rl;
            var p = Progression.Assign(new Random(3), s, new[] { 1, 1, 2, 2 }, new double[] { 0, 1, 2, 3 }, (i, j) => i == 0 && j < 2 ? 0 : 2, out fb, out rl);
            Assert.Equal(2, p[0]);                                  // pulled the nearest-ranked one that fits, from level 2's share
            Assert.Contains(p[1], new[] { 0, 1 });
            Assert.Contains(1 - p[1], new[] { p[2], p[3] });        // the share's other one spilled to level 2
            Assert.Contains(3, new[] { p[2], p[3] });
            Assert.Equal(1, fb); Assert.Equal(0, rl);
            // nothing left may stand on the last spot: it trades with an earlier one
            p = Progression.Assign(new Random(3), s, new[] { 1, 2 }, new double[] { 0, 1 }, (i, j) => i == 1 && j == 1 ? 0 : 2, out fb, out rl);
            Assert.Equal(new[] { 1, 0 }, p);
            Assert.Equal(1, fb);
            // a height that fits nowhere is relaxed
            p = Progression.Assign(new Random(3), s, new[] { 1, 1 }, new double[] { 0, 1 }, (i, j) => 1, out fb, out rl);
            Assert.Equal(new[] { 0, 1 }, p.OrderBy(x => x));
            Assert.Equal(2, rl);
            // without a fit, each level takes its consecutive ranks
            p = Progression.Assign(new Random(9), s, new[] { 2, 1, 2, 1, 3 }, new double[] { 5, 4, 3, 2, 1 }, null, out fb, out rl);
            Assert.Equal(new[] { 3, 4 }, new[] { p[1], p[3] }.OrderBy(x => x));     // level 1: strengths 1 and 2 (spots 4 and 3)
            Assert.Equal(new[] { 1, 2 }, new[] { p[0], p[2] }.OrderBy(x => x));     // level 2: strengths 3 and 4
            Assert.Equal(0, p[4]);                                                  // level 3: the strongest
        }

        static MonsterSwapOptions Monsters(GameSession s, LevelEdits e, int seed)
        {
            return new MonsterSwapOptions { Seed = seed, Rule = "progression", Whitelist = MonsterSwap.DefaultWhitelist(s, e.Model), Ignored = new List<IgnoredObject>() };
        }

        [Theory]
        [InlineData("UW", 1)]
        [InlineData("UW", 7)]
        [InlineData("UW2", 1)]
        [InlineData("UW2", 7)]
        public void Monsters_progress_level_by_level_within_each_movement_class(string game, int seed)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var o = Monsters(s, e, seed);
            var survey = MonsterSwap.Survey(s, e.Model, o);
            var levels = Levels(s);
            var before = levels.ToDictionary(l => l, l => (byte[])e.For(l).Data.Clone());
            var r = MonsterSwap.Run(s, e.Model, o);
            Assert.Equal(r.Report.ToString(), MonsterSwap.Run(s, e.Model, o).Report.ToString());
            Assert.True(r.Changed > 100);
            MonsterSwap.Apply(r, e, seed);
            var kinds = CreatureKind.Read(s);
            var terrain = CreatureTerrain.TryLoad(s);
            var after = survey.ToDictionary(x => x, x => e.Model(x.Level).Objects[x.Index]);

            // ranked: moving, not level-bound, not kept on a floor its creature could not be put on
            var ranked = survey.Where(x => x.Blocked == null && x.LevelBound == null && !(CreatureTerrain.Restricted(x.Floor) && !CreatureTerrain.CanStand(x.Kind, x.Floor))).ToList();
            for (int c = 0; c < 4; c++)
            {
                var cls = ranked.Where(x => MonsterSwap.MoveClass(x.Kind) == c).ToList();
                // every spot keeps its class: each level keeps its number of creatures of the class
                foreach (var x in cls) Assert.Equal(c, MonsterSwap.MoveClass(kinds[after[x].Item]));
                double last = -1;
                var medians = new List<string>();
                foreach (int l in Progression.LevelOrder(s, cls.Select(x => x.Level)))
                {
                    double m = Progression.Median(cls.Where(x => x.Level == l).Select(x => Xp(kinds[after[x].Item], after[x])));
                    medians.Add(l + ":" + m);
                    Assert.True(m >= last, string.Format("{0} seed {1}, {2}: level {3} median {4} below the previous {5} ({6})", game, seed, MonsterSwap.MoveClassName(c), l, m, last, string.Join(" ", medians)));
                    last = m;
                }
                // and across the class the strengths are those that were there
                Assert.Equal(cls.Select(x => Xp(x.Kind, x.O)).OrderBy(v => v), cls.Select(x => Xp(kinds[after[x].Item], after[x])).OrderBy(v => v));
            }
            // talkers and other blocked ones unmoved; level-bound kinds stay on their level
            foreach (var x in survey.Where(x => x.Blocked != null)) Assert.Equal(x.O.Record, after[x].Record);
            foreach (var lv in survey.Where(x => x.Blocked == null && x.LevelBound != null).GroupBy(x => x.Level))
                Assert.Equal(lv.Select(x => x.O.Item).OrderBy(i => i), lv.Select(x => after[x].Item).OrderBy(i => i));
            // nobody on water or lava it cannot stand on; every kind keeps its count
            foreach (var x in survey)
            {
                if (after[x].Record.SequenceEqual(x.O.Record)) continue;
                Assert.True(CreatureTerrain.CanStand(kinds[after[x].Item], x.Floor), s.Names.Get(after[x].Item) + " on " + CreatureTerrain.Name(x.Floor) + " at L" + x.Level);
            }
            Assert.Equal(survey.Select(x => x.O.Item).OrderBy(i => i), after.Values.Select(x => x.Item).OrderBy(i => i));
            foreach (int l in levels) { bool ok; e.For(l).Check(out ok); Assert.True(ok, "level " + l); }
            string rep = r.Report.ToString();
            Assert.Contains("Uniform progression", rep);
            Assert.Contains("Nearest fit:", rep);
            output.WriteLine(rep.Substring(0, Math.Min(rep.Length, 6000)));

            MonsterSwap.Revert(r, e);
            foreach (var kv in before) Assert.Equal(kv.Value, e.For(kv.Key).Data);
        }

        [Fact]
        public void The_monster_formula_tells_the_rule()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var o = new MonsterSwapOptions { Rule = "progression" };
            string f = MonsterSwap.Formula(s, o);
            Assert.Contains("Uniform progression", f);
            Assert.Contains("r_ℓ … r_ℓ + n_ℓ − 1", f);
            Assert.Contains("Pits of Carnage → Tomb of Praecor Loth", f);
            Assert.DoesNotContain("spread", f);
            Assert.DoesNotContain("Uniform progression", MonsterSwap.Formula(s, new MonsterSwapOptions()));
            Assert.Equal("progression", MonsterSwapOptions.FromJson(o.ToJson()).Rule);
        }

        static ShuffleOptions Items(GameSession s, int seed, string mode = "swap-kind")
        {
            var o = ShuffleOptions.Defaults(s);
            o.Seed = seed; o.Mode = mode; o.Ignored = new List<IgnoredObject>(); o.SwapRule = "progression";
            return o;
        }

        static string RankedKind(GameSession s, ObjectInfo o)
        {
            int it = o.Item;
            if (!o.IsQuantity && o.Link != 0 && !(it >= 0x98 && it <= 0x9B)) return null;
            if (it < 0x20 && !ItemTables.KeptWeapons.Contains(it) && (ItemTables.Axes.Contains(it) || ItemTables.Swords(s.IsUw2).Contains(it) || ItemTables.Maces.Contains(it) || ItemTables.Launchers.Contains(it) || ItemTables.Ammo.Contains(it))) return "weapon";
            if (ItemTables.Slot(it) >= 0 || ItemTables.ShieldIds.Contains(it)) return "armour";
            bool f11 = ((o.Record[1] >> 3) & 1) != 0;
            if (it >= 0x130 && it <= 0x13F && o.Enchanted && o.IsQuantity && f11 && o.Link >= 0x200 && ((o.Link & 0x1FF) >> 6) == 0) return "scroll";
            if (it >= 0x98 && it <= 0x9B) return o.IsQuantity ? "wand" : "charged wand";
            return null;
        }

        /// <summary>The ranked objects by kind: (level, index) of each, as the swap takes them (nothing ignored, inventories in, no templates).</summary>
        static Dictionary<string, List<(int Level, int Index)>> Ranked(GameSession s, LevelEdits e)
        {
            var d = new Dictionary<string, List<(int, int)>>();
            foreach (var c in ItemShuffle.Candidates(s, e.Model, true))
            {
                var m = e.Model(c.Level);
                var o = m.Objects[c.Index];
                // objects holding triggers keep their kind (ItemShuffle.Run)
                if (!o.IsQuantity && o.Children.Any(x => m.Objects[x].IsTrapOrTrigger)) continue;
                bool inTrap = false;
                for (int up = o.Parent; up >= 0; up = m.Objects[up].Parent) if (m.Objects[up].IsTrapOrTrigger) inTrap = true;
                if (inTrap) continue;
                string k = RankedKind(s, o);
                if (k == null) continue;
                // a charged wand moves with its spell object only when that holds one of the 64 spells (ItemShuffle IsWandSpell)
                if (k == "charged wand")
                {
                    if (!(o.Link > 0 && o.Link < m.Objects.Length && m.Objects[o.Link].Item == 0x120)) continue;
                    var sp = m.Objects[o.Link];
                    if (!(sp.IsQuantity && ((sp.Record[1] >> 3) & 1) != 0 && sp.Link >= 0x200 && ((sp.Link & 0x1FF) >> 6) == 0)) continue;
                }
                if (!d.TryGetValue(k, out var l)) d[k] = l = new List<(int, int)>();
                l.Add((c.Level, c.Index));
            }
            return d;
        }

        [Theory]
        [InlineData("UW", 3, "swap-kind")]
        [InlineData("UW", 11, "swap-any")]
        [InlineData("UW2", 3, "swap-kind")]
        [InlineData("UW2", 11, "swap-any")]
        public void Items_are_handed_out_in_strength_order_level_by_level(string game, int seed, string mode)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var objects = Progression.ReadObjects(s);
            var o = Items(s, seed, mode);
            var ranked = Ranked(s, e);
            Assert.True(ranked["weapon"].Count > 50 && ranked["armour"].Count > 20 && ranked["scroll"].Count > 5);
            var levels = Levels(s);
            var before = levels.ToDictionary(l => l, l => (byte[])e.For(l).Data.Clone());
            var strengthBefore = ranked.ToDictionary(k => k.Key, k => k.Value.Select(x => ItemShuffle.ItemStrength(s, objects, e.Model(x.Level), e.Model(x.Level).Objects[x.Index])).OrderBy(v => v).ToList());
            var r = ItemShuffle.Run(s, e.Model, o);
            Assert.Equal(r.Report.ToString(), ItemShuffle.Run(s, e.Model, o).Report.ToString());
            ItemShuffle.Apply(r, e, seed);
            foreach (int l in levels) { bool ok; e.For(l).Check(out ok); Assert.True(ok, "level " + l); }
            foreach (var kv in ranked)
            {
                // the same strengths, now in order along the walk: no level holds one stronger than any later level's
                var now = kv.Value.Select(x => (x.Level, S: ItemShuffle.ItemStrength(s, objects, e.Model(x.Level), e.Model(x.Level).Objects[x.Index]))).ToList();
                Assert.Equal(strengthBefore[kv.Key], now.Select(x => x.S).OrderBy(v => v));
                double max = double.NegativeInfinity;
                foreach (int l in Progression.LevelOrder(s, now.Select(x => x.Level)))
                {
                    var here = now.Where(x => x.Level == l).Select(x => x.S).ToList();
                    Assert.True(here.Min() >= max, string.Format("{0} {1}: level {2} has {3} after {4}", game, kv.Key, l, here.Min(), max));
                    max = here.Max();
                }
                // and every level keeps its number of objects of the kind
                Assert.Equal(kv.Value.GroupBy(x => x.Level).ToDictionary(x => x.Key, x => x.Count()).OrderBy(x => x.Key),
                    kv.Value.Where(x => RankedKind(s, e.Model(x.Level).Objects[x.Index]) == kv.Key).GroupBy(x => x.Level).ToDictionary(x => x.Key, x => x.Count()).OrderBy(x => x.Key));
            }
            string rep = r.Report.ToString();
            Assert.Contains("Uniform progression", rep);
            Assert.Contains("weapons by level", rep);
            if (mode == "swap-any") Assert.Contains("does not apply", rep);
            output.WriteLine(rep.Substring(0, Math.Min(4000, rep.Length)));
            ItemShuffle.Revert(r, e);
            foreach (var kv in before) Assert.Equal(kv.Value, e.For(kv.Key).Data);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Enchantments_follow_the_progression_too_and_the_start_and_ignored_stay(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var o = Items(s, 5);
            o.SwapEnchantments = true;
            o.Kept = ItemShuffle.StartingEquipment(s, e.Model);
            o.Ignored = ItemShuffle.DefaultIgnored(s, e.Model);
            Assert.NotEmpty(o.Kept);
            var keep = o.Kept.Concat(o.Ignored).Select(x => (x.Level, x.Index)).ToList();
            var was = keep.ToDictionary(x => x, x => (byte[])e.Model(x.Level).Objects[x.Index].Record.Clone());
            var levels = Levels(s);
            var before = levels.ToDictionary(l => l, l => (byte[])e.For(l).Data.Clone());
            var r = ItemShuffle.Run(s, e.Model, o);
            ItemShuffle.Apply(r, e, 5);
            foreach (var x in keep) Assert.Equal(was[x], e.Model(x.Level).Objects[x.Index].Record);
            // melee weapons' enchantment levels are in order along the walk
            var melee = new HashSet<int>(ItemTables.Axes.Concat(ItemTables.Swords(s.IsUw2)).Concat(ItemTables.Maces));
            var keepSet = new HashSet<(int, int)>(keep);
            var now = new List<(int Level, double E)>();
            foreach (var c in ItemShuffle.Candidates(s, e.Model, true))
            {
                var ob = e.Model(c.Level).Objects[c.Index];
                if (!melee.Contains(ob.Item) || keepSet.Contains((c.Level, c.Index)) || (!ob.IsQuantity && ob.Link != 0)) continue;
                if (!ob.IsQuantity && ob.Children.Count > 0) continue;
                now.Add((c.Level, Progression.EnchantLevel(s, ob.Item, ob.Enchanted, ob.IsQuantity, ob.Link, ((ob.Record[1] >> 3) & 1) != 0)));
            }
            Assert.Contains(now, x => x.E > 0);
            double max = double.NegativeInfinity;
            foreach (int l in Progression.LevelOrder(s, now.Select(x => x.Level)))
            {
                var here = now.Where(x => x.Level == l).Select(x => x.E).ToList();
                Assert.True(here.Min() >= max, "level " + l);
                max = here.Max();
            }
            Assert.Contains("by Uniform progression", r.Report.ToString());
            ItemShuffle.Revert(r, e);
            foreach (var kv in before) Assert.Equal(kv.Value, e.For(kv.Key).Data);
        }

        [Fact]
        public void The_item_formula_tells_the_rule_and_random_swaps_are_unchanged()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var o = Items(s, 2);
            string f = ItemShuffle.SwapFormula(s, o);
            Assert.Contains("across the whole game (Uniform progression fixes both)", f);
            Assert.Contains("r_ℓ … r_ℓ + n_ℓ − 1", f);
            Assert.Contains("max(slash, bash, stab) + e", f);
            o.SwapRule = "random";
            Assert.DoesNotContain("progression", ItemShuffle.SwapFormula(s, o));
            // the random rule draws exactly as before the rule existed: the same seed, the same records
            var e1 = new LevelEdits(s);
            var a = ItemShuffle.Run(s, e1.Model, o);
            var b = ItemShuffle.Run(s, e1.Model, ShuffleOptions.FromJson(o.ToJson()));
            Assert.Equal(a.Report.ToString(), b.Report.ToString());
            Assert.DoesNotContain("IsProgression", o.ToJson());
        }
    }
}
