using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The item swap's own options: owners by spot, scopes, scenery, the starting equipment and the enchantment swap.</summary>
    public class ItemSwapOptionsTests
    {
        static ShuffleOptions Swap(GameSession s, int seed, string mode = "swap-any")
        {
            var o = ShuffleOptions.Defaults(s);
            o.Seed = seed; o.Mode = mode; o.Ignored = new List<IgnoredObject>();
            return o;
        }

        static List<int> Levels(GameSession s) { return s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList(); }

        static void AllLevelsOk(GameSession s, LevelEdits e)
        {
            foreach (int l in Levels(s)) { bool ok; e.For(l).Check(out ok); Assert.True(ok, "level " + l); }
        }

        static Dictionary<int, int> Counts(LevelModel m)
        {
            var d = new Dictionary<int, int>();
            foreach (var x in m.Objects) if (!x.Free && x.TileX >= 0) { d.TryGetValue(x.Item, out int n); d[x.Item] = n + 1; }
            return d;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_spot_keeps_its_owner_only_where_both_items_can_be_owned(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var on = new LevelEdits(s); var off = new LevelEdits(s);
            var o = Swap(s, 21); o.SwapBones = true;
            var r1 = ItemShuffle.Run(s, on.Model, o);
            o.SpotOwnership = false;
            var r0 = ItemShuffle.Run(s, off.Model, o);
            ItemShuffle.Apply(r1, on, 21); ItemShuffle.Apply(r0, off, 21);
            int took = 0, kept = 0;
            foreach (int l in Levels(s))
            {
                var was = s.LoadLevel(l); var a = on.Model(l); var b = off.Model(l);
                for (int i = 0; i < was.Objects.Length; i++)
                {
                    var x = was.Objects[i]; var y1 = a.Objects[i]; var y0 = b.Objects[i];
                    if (x.Free) continue;
                    // the same objects went to the same spots: only the owner may differ
                    Assert.Equal(y0.Item, y1.Item); Assert.Equal(y0.Quality, y1.Quality); Assert.Equal(y0.Link, y1.Link);
                    if (y1.Item == x.Item && y1.Owner == x.Owner && y0.Owner == x.Owner) continue;
                    if (ItemShuffle.Ownable(s, x.Item) && ItemShuffle.Ownable(s, y1.Item)) { Assert.Equal(x.Owner, y1.Owner); took++; }
                    else { Assert.Equal(y0.Owner, y1.Owner); kept++; }
                }
            }
            Assert.True(took > 50);
            Assert.True(kept > 0);    // bones, scrolls, thread: their owner fields travel with them
            AllLevelsOk(s, on);
        }

        [Fact]
        public void The_rats_food_on_level_1_makes_a_light_put_there_the_rats()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var l1 = s.LoadLevel(1);
            // the piece of cheese and the piece of meat at (22, 8) belong to the giant rats: owner 36 & 0x1F = race 4 (LOOK.C)
            Assert.Equal(0xB2, l1.Objects[945].Item); Assert.Equal(36, l1.Objects[945].Owner);
            Assert.Equal(0xB0, l1.Objects[946].Item); Assert.Equal(36, l1.Objects[946].Owner);
            for (int seed = 1; seed < 400; seed++)
            {
                var o = Swap(s, seed);
                o.SwapScope = "level";
                o.ShuffleWeapons = o.ShuffleArmour = o.SwapTools = o.ShuffleRunes = o.ShuffleWands = o.ShuffleScrolls = o.ShufflePotions = o.ShuffleRings = false;
                var e = new LevelEdits(s);
                ItemShuffle.Apply(ItemShuffle.Run(s, e.Model, o), e, seed);
                int spot = new[] { 945, 946 }.FirstOrDefault(i => e.Model(1).Objects[i].Item >= 0x90 && e.Model(1).Objects[i].Item < 0x98);
                if (spot == 0) continue;
                Assert.Equal(36, e.Model(1).Objects[spot].Owner);
                // without the option the light keeps its own owner (nobody's, here)
                o.SpotOwnership = false;
                var e0 = new LevelEdits(s);
                ItemShuffle.Apply(ItemShuffle.Run(s, e0.Model, o), e0, seed);
                Assert.Equal(e.Model(1).Objects[spot].Item, e0.Model(1).Objects[spot].Item);
                Assert.NotEqual(36, e0.Model(1).Objects[spot].Owner);
                return;
            }
            Assert.Fail("no seed put a light where the rats' food was");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Permutations_keep_to_their_scope(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var rnd = new Random(5);
            var levels = Levels(s).SelectMany(l => Enumerable.Repeat(l, 10 + rnd.Next(30))).OrderBy(x => rnd.Next()).ToList();
            foreach (var scope in new[] { "level", "world", "near", "game" })
                foreach (int range in new[] { 1, 2 })
                {
                    var o = Swap(s, 9); o.SwapScope = scope; o.SwapRange = range;
                    var p = ItemShuffle.Permutation(new Random(9), s, levels, o);
                    Assert.Equal(p, ItemShuffle.Permutation(new Random(9), s, levels, o));     // same seed, same permutation
                    Assert.Equal(Enumerable.Range(0, levels.Count), p.OrderBy(x => x));          // a permutation
                    int moved = 0, across = 0;
                    for (int i = 0; i < p.Length; i++)
                    {
                        int from = levels[p[i]], to = levels[i];
                        if (p[i] != i) moved++;
                        if (from != to) across++;
                        if (scope == "level") Assert.Equal(from, to);
                        if (scope == "world") Assert.Equal(s.Profile.WorldOf(from), s.Profile.WorldOf(to));
                        if (scope == "near") { Assert.True(Math.Abs(from - to) <= range); Assert.Equal(s.Profile.WorldOf(from), s.Profile.WorldOf(to)); }
                    }
                    Assert.True(moved > levels.Count / 2, scope);
                    if (scope != "level") Assert.True(across > levels.Count / 4, scope);
                }
        }

        [Theory]
        [InlineData("UW", "level")]
        [InlineData("UW", "near")]
        [InlineData("UW2", "world")]
        [InlineData("UW2", "near")]
        public void Scoped_swaps_keep_counts_where_they_must_and_undo_exactly(string game, string scope)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var o = Swap(s, 4); o.SwapScope = scope; o.SwapRange = 1;
            var before = Levels(s).ToDictionary(l => l, l => (byte[])e.For(l).Data.Clone());
            var c0 = Levels(s).ToDictionary(l => l, l => Counts(e.Model(l)));
            var r = ItemShuffle.Run(s, e.Model, o);
            Assert.True(r.Changed > 50);
            ItemShuffle.Apply(r, e, 4);
            AllLevelsOk(s, e);
            var c1 = Levels(s).ToDictionary(l => l, l => Counts(e.Model(l)));
            Func<Dictionary<int, Dictionary<int, int>>, Func<int, int>, Dictionary<(int, int), int>> by = (c, key) =>
            {
                var d = new Dictionary<(int, int), int>();
                foreach (var kv in c) foreach (var it in kv.Value) { d.TryGetValue((key(kv.Key), it.Key), out int n); d[(key(kv.Key), it.Key)] = n + it.Value; }
                return d;
            };
            Func<int, int> k = scope == "level" ? (Func<int, int>)(l => l) : l => s.Profile.WorldOf(l);
            Assert.Equal(by(c0, k).OrderBy(x => x.Key), by(c1, k).OrderBy(x => x.Key));
            if (scope == "near")
            {
                // an item can only have come from a level at most 1 away
                foreach (int l in Levels(s))
                    foreach (var it in c1[l])
                    {
                        int near = Levels(s).Where(x => Math.Abs(x - l) <= 1 && s.Profile.WorldOf(x) == s.Profile.WorldOf(l)).Sum(x => c0[x].TryGetValue(it.Key, out int n) ? n : 0);
                        Assert.True(it.Value <= near, "level " + l + " item " + it.Key);
                    }
                Assert.Contains(Levels(s), l => !c0[l].OrderBy(x => x.Key).SequenceEqual(c1[l].OrderBy(x => x.Key)));
            }
            ItemShuffle.Revert(r, e);
            foreach (var kv in before) Assert.Equal(kv.Value, e.For(kv.Key).Data);
        }

        [Theory]
        [InlineData("UW", 8, 820)]
        [InlineData("UW2", 49, 959)]
        public void Bones_and_scenery_swap_but_Garamons_bones_never_move(string game, int level, int bones)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var marked = s.LoadLevel(level).Objects[bones];
            Assert.True(ItemShuffle.IsMarkedBones(marked));
            Assert.Equal(0x3E, marked.Owner);
            Assert.DoesNotContain(ItemShuffle.Candidates(s, s.LoadLevel, true), c => c.Level == level && c.Index == bones);
            var rec = (byte[])marked.Record.Clone();
            int bonesMoved = 0, sceneryMoved = 0;
            foreach (int seed in new[] { 1, 2, 3 })
                foreach (var mode in new[] { "swap-kind", "swap-any" })
                {
                    var e = new LevelEdits(s);
                    var o = Swap(s, seed, mode); o.SwapBones = true; o.SwapScenery = true; o.SwapScope = seed == 3 ? "level" : "game";
                    var r = ItemShuffle.Run(s, e.Model, o);
                    ItemShuffle.Apply(r, e, seed);
                    Assert.Equal(rec, e.Model(level).Objects[bones].Record);
                    foreach (var kv in r.ByLevel)
                        foreach (var w in kv.Value)
                        {
                            int was = s.LoadLevel(kv.Key).Objects[w.Index].Item;
                            if (w.Item != was && ItemShuffle.IsBones(w.Item)) bonesMoved++;
                            if (w.Item != was && w.Item >= 0xC0 && w.Item < 0xE0 && !ItemShuffle.IsBones(w.Item) && w.Item != ItemTables.Pole) sceneryMoved++;
                            if (mode == "swap-kind" && ItemShuffle.IsBones(was)) Assert.True(ItemShuffle.IsBones(w.Item));
                        }
                    if (seed == 1) AllLevelsOk(s, e);
                }
            Assert.True(bonesMoved > 10);
            Assert.True(sceneryMoved > 10);
            // off (the default): no scenery moves
            var e2 = new LevelEdits(s);
            var r2 = ItemShuffle.Run(s, e2.Model, Swap(s, 1));
            Assert.DoesNotContain(r2.ByLevel.SelectMany(kv => kv.Value), w => w.Item >= 0xC0 && w.Item < 0xE0 && w.Item != ItemTables.Pole);
        }

        [Theory]
        [InlineData("UW", new[] { 934, 935, 936, 940, 959 })]
        [InlineData("UW2", new[] { 513, 528, 533, 581, 836, 978, 979, 983, 984, 985, 986, 989, 990, 991, 992, 995, 1012 })]
        public void The_starting_equipment_stays_when_kept(string game, int[] expected)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var start = ItemShuffle.StartingEquipment(s, s.LoadLevel);
            Assert.Equal(expected, start.Select(x => x.Index).OrderBy(x => x).ToArray());
            Assert.All(start, x => Assert.Equal(1, x.Level));
            var l1 = s.LoadLevel(1);
            var e = new LevelEdits(s);
            var o = Swap(s, 2); o.SwapScope = "level"; o.Kept = start;
            ItemShuffle.Apply(ItemShuffle.Run(s, e.Model, o), e, 2);
            foreach (var x in start) Assert.Equal(l1.Objects[x.Index].Record, e.Model(1).Objects[x.Index].Record);
            // without it, some of it moves
            var e0 = new LevelEdits(s);
            o.Kept = null;
            ItemShuffle.Apply(ItemShuffle.Run(s, e0.Model, o), e0, 2);
            Assert.Contains(start, x => !l1.Objects[x.Index].Record.SequenceEqual(e0.Model(1).Objects[x.Index].Record));
        }

        static string ClassOf(GameSession s, int item)
        {
            // melee weapons and launchers apart in the enchantment swap, but a main swap of weapons mixes them
            if (ItemTables.Axes.Contains(item) || ItemTables.Swords(s.IsUw2).Contains(item) || ItemTables.Maces.Contains(item) || ItemTables.Launchers.Contains(item)) return "weapon";
            if (ItemTables.Slot(item) >= 0 || ItemTables.ShieldIds.Contains(item)) return "armour";
            if (item >= 0x130 && item < 0x140) return "scroll";
            return null;
        }

        static Dictionary<string, List<int>> Enchantments(GameSession s, LevelEdits e)
        {
            var d = new Dictionary<string, List<int>>();
            foreach (int l in Levels(s))
                foreach (var x in e.Model(l).Objects)
                {
                    if (x.Free || x.TileX < 0 || !x.Enchanted || !x.IsQuantity || x.Link < 0x200) continue;
                    string c = ClassOf(s, x.Item);
                    if (c == null) continue;
                    if (!d.ContainsKey(c)) d[c] = new List<int>();
                    d[c].Add(x.Link | ((x.Record[1] >> 3) & 1) << 12);
                }
            foreach (var l in d.Values) l.Sort();
            return d;
        }

        [Theory]
        [InlineData("UW", "swap-kind")]
        [InlineData("UW", "")]
        [InlineData("UW2", "swap-kind")]
        [InlineData("UW2", "")]
        public void Enchantments_swap_within_their_class_after_the_main_swap(string game, string main)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var before = Levels(s).ToDictionary(l => l, l => (byte[])e.For(l).Data.Clone());
            var ench0 = Enchantments(s, e);
            var o = Swap(s, 13, "swap-kind");
            if (main == "") o.ShuffleWeapons = o.ShuffleArmour = o.ShuffleFood = o.ShuffleLights = o.SwapTools = o.ShuffleRunes = o.ShuffleWands = o.ShuffleScrolls = o.ShufflePotions = o.ShuffleRings = false;
            o.SwapEnchantments = true;
            var r = ItemShuffle.Run(s, e.Model, o);
            Assert.Equal(r.Report.ToString(), ItemShuffle.Run(s, e.Model, o).Report.ToString());
            Assert.Contains("enchantments trade places", r.Report.ToString());
            ItemShuffle.Apply(r, e, 13);
            AllLevelsOk(s, e);
            var ench1 = Enchantments(s, e);
            // every class keeps exactly its own enchantments (Caliburn and quest armour are outside the classes and stay)
            Assert.Equal(ench0.Keys.OrderBy(x => x), ench1.Keys.OrderBy(x => x));
            foreach (var k in ench0.Keys) Assert.Equal(ench0[k], ench1[k]);
            int moved = 0;
            foreach (int l in Levels(s))
            {
                var was = s.LoadLevel(l);
                for (int i = 0; i < was.Objects.Length; i++)
                {
                    var x = was.Objects[i]; var y = e.Model(l).Objects[i];
                    if (x.Free) continue;
                    if (main == "") Assert.Equal(x.Item, y.Item);      // only enchantments moved
                    if (x.Link != y.Link && ClassOf(s, y.Item) != null) moved++;
                }
            }
            Assert.True(moved > 10);
            ItemShuffle.Revert(r, e);
            foreach (var kv in before) Assert.Equal(kv.Value, e.For(kv.Key).Data);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_formulas_follow_the_settings(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var o = Swap(s, 77, "swap-kind");
            string f = ItemShuffle.SwapFormula(s, o);
            Assert.Contains("Seed 77", f);
            Assert.Contains("uniform random permutation", f);
            Assert.Contains("owner'(i) = owner(i)", f);
            o.SwapScope = "near"; o.SwapRange = 2; o.SpotOwnership = false; o.SwapEnchantments = true;
            f = ItemShuffle.SwapFormula(s, o);
            Assert.Contains("L(a) − 2 .. L(a) + 2", f);
            Assert.DoesNotContain("owner'(i)", f);
            Assert.Contains("e'(i) = e(σ(i))", f);

            var sh = ShuffleOptions.Defaults(s);
            sh.WeaponQualitySpread = 6; sh.WeaponEnchants[0].Probability = 12.5; sh.WeaponDepth = 40;
            string t = ItemShuffle.ShuffleFormula(s, sh);
            Assert.Contains("clamp(q + U{−6..6}, 1, 63)", t);
            Assert.Contains("12.5/100 × (1 + 40/100 × d)", t);
            Assert.Contains(s.IsUw2 ? "d = (level within its world − 1) / 7" : "d = min(1, (level − 1) / 7)", t);
            sh.CustomWeaponQuality = true; sh.ShuffleArmour = false;
            t = ItemShuffle.ShuffleFormula(s, sh);
            Assert.Contains("Cat(the custom curve at 1..63)", t);
            Assert.DoesNotContain("Armour (", t);
        }
    }
}
