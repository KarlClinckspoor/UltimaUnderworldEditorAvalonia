using System;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class ItemShuffleTests
    {
        static int CountOf(LevelEdits e, GameSession s, System.Func<ObjectInfo, bool> f)
        {
            int n = 0;
            foreach (var sl in s.LevelSlots()) if (sl.Present) n += e.Model(sl.Level).Objects.Count(o => !o.Free && o.TileX >= 0 && f(o));
            return n;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Weights_minimums_and_specials_are_honoured_and_undo_puts_everything_back(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s);
            o.Seed = 7; o.Axes = 0; o.MinThread = 6; o.MinPole = 6; o.MinFishingPole = 8; o.WeaponSpecials = true;
            o.RuneMinimum[0xE8] = 4;
            o.ArmourEffectsOn = false;
            o.ArmourSpecials = true; o.ArmourSpecialList[0].On = true; o.ArmourSpecialList[0].Count = 1;
            var before = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])edits.For(x.Level).Data.Clone());
            int kept = CountOf(edits, s, x => x.Item == 0x0A);

            var r = ItemShuffle.Run(s, edits.Model, o);
            Assert.Equal(r.Report.ToString(), ItemShuffle.Run(s, edits.Model, o).Report.ToString());   // same seed, same result
            ItemShuffle.Apply(r, edits, o.Seed);

            Assert.Equal(0, CountOf(edits, s, x => ItemTables.Axes.Contains(x.Item)));
            Assert.Equal(kept, CountOf(edits, s, x => x.Item == 0x0A));          // Caliburn / the jewelled dagger
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.Thread(s.IsUw2)) >= 6);
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.Pole) >= 6);
            Assert.True(CountOf(edits, s, x => x.Item == ItemTables.FishingPole) >= 8);
            Assert.True(CountOf(edits, s, x => x.Item == 0xE8) >= 4);
            foreach (int rune in ItemTables.Runes) Assert.True(CountOf(edits, s, x => x.Item == rune) >= 1);
            if (s.IsUw2)
                for (int e = 8; e <= 13; e++)
                    Assert.Equal(1, CountOf(edits, s, x => x.Item < 0x10 && x.Enchanted && x.IsQuantity && x.Link == (0x200 | 0xC0 | e)));
            // Flameproof is an armour effect the tab rolls again: without other effects, only the one special is left
            Assert.Equal(1, CountOf(edits, s, x => (ItemTables.Slot(x.Item) >= 0 || ItemTables.ShieldIds.Contains(x.Item)) && x.Enchanted && x.IsQuantity && x.Link == 0x236));

            ItemShuffle.Revert(r, edits);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
        }

        static int CountBefore(GameSession s, int link)
        {
            int n = 0;
            foreach (var sl in s.LevelSlots()) if (sl.Present) n += s.LoadLevel(sl.Level).Objects.Count(o => !o.Free && o.TileX >= 0 && o.Item >= 0x20 && o.Item < 0x40 && o.Enchanted && o.IsQuantity && o.Link == link);
            return n;
        }

        [Fact]
        public void Options_round_trip_through_json()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var o = ShuffleOptions.Defaults(GameSession.Open(g));
            o.Axes = 3; o.RuneMinimum[0xF0] = 5; o.WeaponEnchants[0].Mean = 4.5;
            var b = ShuffleOptions.FromJson(o.ToJson());
            Assert.Equal(3, b.Axes);
            Assert.Equal(5, b.RuneMinimum[0xF0]);
            Assert.Equal(4.5, b.WeaponEnchants[0].Mean);
            Assert.Equal(o.FoodWeights.Count, b.FoodWeights.Count);
        }

        [Theory]
        [InlineData("UW", "swap-kind")]
        [InlineData("UW", "swap-any")]
        [InlineData("UW2", "swap-any")]
        public void Swaps_keep_every_count_and_valid_lists_ignore_listed_objects_and_undo_exactly(string game, string mode)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s, edits.Model);
            o.Seed = 3; o.Mode = mode;
            Assert.NotEmpty(o.Ignored);                                  // the quest wands
            if (!s.IsUw2) Assert.Contains(o.Ignored, x => x.Level == 4 && x.Label.Contains("bullfrog"));
            else Assert.Contains(o.Ignored, x => x.Level == 43 && x.Label.Contains("Telekinesis"));
            var levels = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var before = levels.ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            System.Func<System.Collections.Generic.Dictionary<int, int>> counts = () =>
            {
                var d = new System.Collections.Generic.Dictionary<int, int>();
                foreach (int l in levels) foreach (var x in edits.Model(l).Objects) if (!x.Free && x.TileX >= 0) { d.TryGetValue(x.Item, out int n); d[x.Item] = n + 1; }
                return d;
            };
            var c0 = counts();
            var ign = o.Ignored.Select(x => (x.Level, x.Index, (byte[])edits.Model(x.Level).Objects[x.Index].Record.Clone())).ToList();

            var r = ItemShuffle.Run(s, edits.Model, o);
            Assert.True(r.Changed > 50);
            ItemShuffle.Apply(r, edits, o.Seed);
            var c1 = counts();
            Assert.Equal(c0.OrderBy(k => k.Key), c1.OrderBy(k => k.Key));    // nothing made, nothing lost
            foreach (int l in levels) { bool ok; edits.For(l).Check(out ok); Assert.True(ok, "level " + l); }
            foreach (var (l, idx, rec) in ign) Assert.Equal(rec, edits.Model(l).Objects[idx].Record);
            if (mode == "swap-kind")
                foreach (var w in r.ByLevel.SelectMany(kv => kv.Value.Select(x => (kv.Key, x))))
                {
                    var was = s.LoadLevel(w.Key).Objects[w.x.Index].Item;
                    bool weapon(int it) => it < 0x20;
                    bool armour(int it) => it >= 0x20 && it < 0x40;
                    if (weapon(was)) Assert.True(weapon(w.x.Item));
                    if (armour(was)) Assert.True(armour(w.x.Item));
                }

            ItemShuffle.Revert(r, edits);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
        }

        [Fact]
        public void Curves_never_overshoot_and_slider_bars_match_the_normal_draw()
        {
            var pts = new System.Collections.Generic.List<CurvePoint> { new CurvePoint(1, 0), new CurvePoint(3, 100), new CurvePoint(4, 100), new CurvePoint(8, 10) };
            for (double x = 1; x <= 8; x += 0.05) { double y = Distributions.Eval(pts, x); Assert.InRange(y, -1e-9, 100 + 1e-9); }
            Assert.Equal(100, Distributions.Eval(pts, 3.5), 6);       // flat between equal points
            var b = Distributions.NormalBars(8, 2, 1);
            Assert.InRange(b.Sum(), 99, 101);
            Assert.True(b[1] > b[0] && b[1] > b[2] && b[7] < 1);
            var one = Distributions.NormalBars(4, 3, 0);
            Assert.Equal(new double[] { 0, 0, 100, 0 }, one);
        }

        static System.Collections.Generic.List<(LevelModel m, ObjectInfo o)> Objects(LevelEdits e, GameSession s)
        {
            var l = new System.Collections.Generic.List<(LevelModel, ObjectInfo)>();
            foreach (var sl in s.LevelSlots()) if (sl.Present) { var m = e.Model(sl.Level); foreach (var o in m.Objects) if (!o.Free && o.TileX >= 0) l.Add((m, o)); }
            return l;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Custom_bars_curves_wands_scrolls_potions_rings_and_curses_do_what_they_say(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s, edits.Model);
            o.Seed = 9;
            // strength: only the strongest; chance: always (custom depth), only for the first kind
            var acc = o.WeaponEnchants[0];
            acc.CustomStrength = true; acc.StrengthBars = new double[acc.Effects.Length]; acc.StrengthBars[acc.Effects.Length - 1] = 100;
            acc.CustomDepth = true;
            if (s.IsUw2) acc.DepthCurve = new System.Collections.Generic.List<CurvePoint> { new CurvePoint(1, 100), new CurvePoint(8, 100) };
            else acc.DepthBars = Enumerable.Repeat(100.0, s.Profile.LevelSlots).ToArray();
            foreach (var other in o.WeaponEnchants.Skip(1)) { other.CustomDepth = true; other.DepthBars = new double[s.Profile.LevelSlots]; other.DepthCurve = new System.Collections.Generic.List<CurvePoint> { new CurvePoint(1, 0), new CurvePoint(8, 0) }; }
            o.WeaponSpecials = false;
            o.CustomWeaponQuality = true; o.WeaponQualityCurve = new System.Collections.Generic.List<CurvePoint> { new CurvePoint(1, 0), new CurvePoint(19, 0), new CurvePoint(20, 100), new CurvePoint(21, 0), new CurvePoint(63, 0) };
            o.WandCharges = new double[ShuffleOptions.MaxCharges]; o.WandCharges[4] = 100;
            o.WandCircles = new double[o.WandCircles.Length]; o.WandCircles[2] = 100;
            o.ScrollCircles = new double[o.ScrollCircles.Length]; o.ScrollCircles[7] = 100;
            o.RingMagic = 100; o.RingCurseShare = 100; o.CurseStrength = new double[] { 0, 100, 0, 0, 0, 0, 0, 0 };
            o.ArmourEffectsOn = false;
            var r = ItemShuffle.Run(s, edits.Model, o);
            ItemShuffle.Apply(r, edits, o.Seed);
            var objs = Objects(edits, s);
            var meleeIds = ItemTables.Axes.Concat(ItemTables.Swords(s.IsUw2)).Concat(ItemTables.Maces).ToList();
            var ignored = new System.Collections.Generic.HashSet<(int, int)>(o.Ignored.Select(x => (x.Level, x.Index)));
            int melee = 0;
            foreach (var (m, x) in objs)
            {
                bool f11 = (x.Record[1] & 8) != 0;
                if (x.Parent >= 0 && m.Objects[x.Parent].IsTrapOrTrigger) continue;     // templates inside traps are left alone
                bool otherEffect = x.Enchanted && x.IsQuantity && x.Link >= 0x200 && (f11 || ((x.Link & 0x1FF) >> 4) != 12);     // kept: not what the tab rolls
                if (meleeIds.Contains(x.Item) && (x.IsQuantity || x.Link == 0) && !otherEffect)
                {
                    melee++;
                    Assert.InRange(x.Quality, 20, 20);
                    Assert.True(x.Enchanted && x.Link == (0x200 | 0xC0 | acc.Effects.Last()), s.Names.Get(x.Item) + " " + x.Link.ToString("X"));
                }
                if (x.Item == 0x120 && x.Parent >= 0 && m.Objects[x.Parent].Item >= 0x98 && m.Objects[x.Parent].Item <= 0x9B && f11 && ((x.Link & 0x1FF) >> 6) == 0)
                {
                    Assert.Equal(5, x.Quality);
                    Assert.Equal(3, SpellTables.Circle(s.IsUw2, x.Link & 0x3F));
                }
                if (x.Item >= 0x130 && x.Item <= 0x13F && x.Enchanted && x.IsQuantity && f11 && ((x.Link & 0x1FF) >> 6) == 0)
                    Assert.Equal(8, SpellTables.Circle(s.IsUw2, x.Link & 0x3F));
                if (ItemTables.Rings(s).Contains(x.Item) && (x.IsQuantity || x.Link == 0) && !ignored.Contains((m.Level, x.Index)) && !(x.Parent >= 0 && m.Objects[x.Parent].IsTrapOrTrigger))
                    Assert.Equal(0x292, x.Link);
            }
            Assert.True(melee > 20);
            // potions: only spells some potion held, and in UW2 the colour the game used for that spell
            var known = Objects(new LevelEdits(s), s).Where(t => ItemShuffle.IsPotionItem(s, t.o.Item) && t.o.Enchanted && t.o.IsQuantity && (t.o.Record[1] & 8) != 0 && ((t.o.Link & 0x1FF) >> 6) == 0)
                .Select(t => (spell: t.o.Link & 0x3F, item: t.o.Item)).ToList();
            foreach (var (m, x) in objs)
                if (ItemShuffle.IsPotionItem(s, x.Item) && x.Enchanted && x.IsQuantity && (x.Record[1] & 8) != 0 && ((x.Link & 0x1FF) >> 6) == 0)
                {
                    Assert.Contains(known, k => k.spell == (x.Link & 0x3F));
                    if (s.IsUw2) Assert.Contains(known, k => k.spell == (x.Link & 0x3F) && k.item == x.Item);
                }
            foreach (int l in s.LevelSlots().Where(x => x.Present).Select(x => x.Level)) { bool ok; edits.For(l).Check(out ok); Assert.True(ok); }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Defaults_follow_the_world_and_the_report_says_what_it_holds(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var o = ShuffleOptions.Defaults(s, s.LoadLevel);
            var c = ItemShuffle.Census(s, s.LoadLevel);
            int axes = ItemTables.Axes.Sum(i => c.Items.TryGetValue(i, out int n) ? n : 0);
            int swords = ItemTables.Swords(s.IsUw2).Sum(i => c.Items.TryGetValue(i, out int n) ? n : 0);
            Assert.Equal(Math.Round(100.0 * Math.Min(axes, swords) / Math.Max(axes, swords), 1), Math.Min(o.Axes, o.Swords), 1);
            Assert.Equal(ShuffleOptions.CurrentVersion, o.Version);
            Assert.InRange(o.LauncherShare, 1, 99);
            Assert.True(o.WeaponEnchants.All(e => e.Probability >= 0 && e.Mean >= 1 && e.Mean <= e.Effects.Length));
            Assert.True(o.ArmourEffects.Values.Max() == 100);                 // some armour carries other effects
            // the world's effect shares come back when shuffling with these settings
            o.Seed = 1;
            var e2 = new LevelEdits(s);
            ItemShuffle.Apply(ItemShuffle.Run(s, e2.Model, o), e2, 1);
            var c2 = ItemShuffle.Census(s, e2.Model);
            Assert.InRange(c2.ArmourEffects.Values.Sum() + c2.ArmourCurses, 0, c.ArmourTotal / 4);
            var rep = ItemShuffle.WorldReport(s, s.LoadLevel);
            foreach (var w in new[] { "Axes", "Weapons enchanted", "Armour with other effects", "Wand spells", "Potion spells", "Rings with an effect" }) Assert.Contains(w, rep);
        }

        [Fact]
        public void Armour_other_effects_and_curses_follow_their_settings()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var o = ShuffleOptions.Defaults(s, e.Model);
            o.Seed = 2; o.ArmourEffectChance = 100; o.ArmourCurseShare = 0;
            foreach (var k in o.ArmourEffects.Keys.ToList()) o.ArmourEffects[k] = k == 0x3A ? 100 : 0;     // Valor
            o.ArmourEffectSlots = new[] { false, false, true, true, false, false };                       // gloves and boots only
            ItemShuffle.Apply(ItemShuffle.Run(s, e.Model, o), e, 2);
            foreach (var (m, x) in Objects(e, s))
            {
                if (x.Parent >= 0 && m.Objects[x.Parent].IsTrapOrTrigger) continue;
                if (!(x.IsQuantity || x.Link == 0)) continue;
                if (ItemTables.Gloves.Contains(x.Item) || ItemTables.Boots.Contains(x.Item)) Assert.Equal(0x23A, x.Link);
                else if (ItemTables.Slot(x.Item) >= 0) Assert.NotEqual(0x23A, x.Link);
            }
        }
    }
}
