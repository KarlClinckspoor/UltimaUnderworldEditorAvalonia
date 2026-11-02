using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The monster swap's terrain guard, the special-kinds option and the formula.</summary>
    public class MonsterTerrainTests
    {
        static MonsterSwapOptions Options(int seed, string scope)
        {
            return new MonsterSwapOptions { Seed = seed, Scope = scope, Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() };
        }

        [Fact]
        public void The_games_creatures_move_as_their_data_says()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2");
            if (g1 == null || g2 == null) return;
            var s1 = GameSession.Open(g1);
            var k1 = CreatureKind.Read(s1);
            Assert.Contains("fire elemental", s1.Names.Get(0x78));
            Assert.True(k1[0x78].Fireproof && !k1[0x78].Flier && !k1[0x78].Swims);
            Assert.True(k1[0x42].Flier);                                // cave bat
            Assert.True(k1[0x57].Swims && k1[0x74].Swims);              // lurker, deep lurker
            Assert.False(k1[0x52].Flier || k1[0x52].Swims || k1[0x52].Fireproof);   // bloodworm
            var s2 = GameSession.Open(g2);
            var k2 = CreatureKind.Read(s2);
            Assert.Contains("brain creature", s2.Names.Get(0x58));
            Assert.True(k2[0x58].Flier);
            Assert.True(k2[0x5E].Fireproof && !k2[0x5E].Flier);         // hordling
            Assert.True(CreatureTerrain.CanStand(k1[0x42], CreatureTerrain.Water) && CreatureTerrain.CanStand(k1[0x42], CreatureTerrain.Lava));
            Assert.True(CreatureTerrain.CanStand(k1[0x57], CreatureTerrain.Water) && !CreatureTerrain.CanStand(k1[0x57], CreatureTerrain.Lava));
            Assert.True(CreatureTerrain.CanStand(k1[0x78], CreatureTerrain.Lava) && !CreatureTerrain.CanStand(k1[0x78], CreatureTerrain.Water));
            Assert.False(CreatureTerrain.CanStand(k1[0x52], CreatureTerrain.Water) || CreatureTerrain.CanStand(k1[0x52], CreatureTerrain.Lava));
            Assert.True(CreatureTerrain.CanStand(k1[0x52], CreatureTerrain.Plain) && CreatureTerrain.CanStand(k1[0x52], CreatureTerrain.Ice));
        }

        /// <summary>
        /// Before the guard, seeds 1-3 put 15-18 walkers on water or lava in UW1 within each level
        /// (20-26 across all levels) and 9-11 in UW2 (18-21 across all levels).
        /// </summary>
        [Theory]
        [InlineData("UW", "level", 0, 20)]
        [InlineData("UW", "all", 0, 20)]
        [InlineData("UW", "all", 100, 100)]
        [InlineData("UW2", "level", 0, 20)]
        [InlineData("UW2", "world", 50, 60)]
        [InlineData("UW2", "all", 0, 100)]
        public void No_swap_puts_a_creature_on_water_or_lava_it_cannot_stand_on(string game, string scope, double depth, double spread)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var kinds = CreatureKind.Read(s);
            var terrain = CreatureTerrain.TryLoad(s);
            Assert.NotNull(terrain);
            int onWaterOrLava = 0;
            foreach (int seed in new[] { 1, 2, 3, 77 })
            {
                var e = new LevelEdits(s);
                var o = Options(seed, scope);
                o.DepthBlend = depth; o.Spread = spread; o.LeaveSpecialKinds = seed % 2 == 0;
                var survey = MonsterSwap.Survey(s, e.Model, o);
                var r = MonsterSwap.Run(s, e.Model, o);
                MonsterSwap.Apply(r, e, seed);
                foreach (var x in survey)
                {
                    var now = e.Model(x.Level).Objects[x.Index];
                    int cls = CreatureTerrain.FloorClass(s, terrain, e.Model(x.Level), now.TileX, now.TileY);
                    Assert.Equal(x.Floor, cls);
                    if (CreatureTerrain.Restricted(cls)) onWaterOrLava++;
                    if (now.Record.SequenceEqual(x.O.Record)) continue;         // as the game had it
                    Assert.True(CreatureTerrain.CanStand(kinds[now.Item], cls),
                        string.Format("seed {0}: {1} put on {2} at L{3} ({4}, {5})", seed, s.Names.Get(now.Item), CreatureTerrain.Name(cls), x.Level, now.TileX, now.TileY));
                }
                // every kind keeps its count
                Assert.Equal(survey.Select(x => x.O.Item).OrderBy(i => i), survey.Select(x => e.Model(x.Level).Objects[x.Index].Item).OrderBy(i => i));
            }
            Assert.True(onWaterOrLava > 20);                                    // the test sees such spots at all
        }

        [Fact]
        public void Water_and_lava_spots_still_swap_among_the_creatures_that_may_go_there()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var e = new LevelEdits(s);
            var o = Options(9, "all");
            o.Spread = 100;
            o.LeaveSpecialKinds = false;
            var survey = MonsterSwap.Survey(s, e.Model, o);
            MonsterSwap.Apply(MonsterSwap.Run(s, e.Model, o), e, 9);
            var lava = survey.Where(x => x.Blocked == null && x.Floor == CreatureTerrain.Lava && CreatureTerrain.CanStand(x.Kind, x.Floor)).ToList();
            Assert.True(lava.Count > 10);
            Assert.Contains(lava, x => e.Model(x.Level).Objects[x.Index].Item != x.O.Item);
        }

        [Theory]
        [InlineData("UW", 0x78)]
        [InlineData("UW2", 0x58)]
        public void The_special_kinds_neither_move_nor_replace_others_unless_the_option_is_off(string game, int item)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            Assert.Equal(item, MonsterSwap.SpecialKind(s));
            Assert.True(new MonsterSwapOptions().LeaveSpecialKinds);
            Assert.True(MonsterSwapOptions.FromJson("{\"Seed\": 3}").LeaveSpecialKinds);     // settings saved before the option
            Func<LevelEdits, List<long>> where = ed => s.LevelSlots().Where(x => x.Present)
                .SelectMany(l => ed.Model(l.Level).Objects.Where(x => !x.Free && x.IsMobile && x.Item == item).Select(x => (long)l.Level << 32 | (uint)x.Index)).OrderBy(x => x).ToList();
            var e = new LevelEdits(s);
            var before = where(e);
            Assert.NotEmpty(before);
            var o = Options(5, "all");
            o.Spread = 100;
            var survey = MonsterSwap.Survey(s, e.Model, o);
            Assert.All(survey.Where(x => x.O.Item == item && x.O.Parent < 0), x => Assert.Contains("left alone", x.Blocked ?? "left alone"));
            MonsterSwap.Apply(MonsterSwap.Run(s, e.Model, o), e, 5);
            Assert.Equal(before, where(e));

            o.LeaveSpecialKinds = false;
            var e2 = new LevelEdits(s);
            MonsterSwap.Apply(MonsterSwap.Run(s, e2.Model, o), e2, 5);
            Assert.NotEqual(before, where(e2));
        }

        [Fact]
        public void The_formula_shows_the_settings_values()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var o = Options(1, "world");
            o.Spread = 30; o.DepthBlend = 25; o.Measure = "combined"; o.RerollPowerful = true; o.PowerfulChance = 10; o.PowerfulDepth = 40;
            string f = MonsterSwap.Formula(s, o);
            Assert.Contains("σ = 0.5 × spread/100 = 0.15", f);
            Assert.Contains("t = 0.75·p₀ + 0.25·d + N(0, σ)", f);
            Assert.Contains("√(h × (1 + A·(P ? 5/3 : 1)/8) × D)", f);
            Assert.Contains("each world", f);
            Assert.Contains("P(powerful) = 0.1 × (1 + 0.4 × depth)", f);
            Assert.Contains("((level − 1) mod 8) / 7", f);
            o.Measure = "none";
            Assert.Contains("U(0, 1)", MonsterSwap.Formula(s, o));
            o.Measure = "xp"; o.Spread = 0; o.DepthBlend = 0; o.RerollPowerful = false;
            f = MonsterSwap.Formula(s, o);
            Assert.Contains("S = max(0, XP) × (P ? 2.25 : 1)", f);
            Assert.Contains("t = p₀ + N(0, σ)", f);
            Assert.DoesNotContain("P(powerful)", f);
        }
    }
}
