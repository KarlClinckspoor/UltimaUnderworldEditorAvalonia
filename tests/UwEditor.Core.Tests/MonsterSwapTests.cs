using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class MonsterSwapTests
    {
        static int Item(byte[] r) { return (r[0] | r[1] << 8) & 0x1FF; }

        [Theory]
        [InlineData("UW", "level")]
        [InlineData("UW", "all")]
        [InlineData("UW2", "world")]
        public void Swaps_keep_the_kinds_honour_the_rules_and_undo_exactly(string game, string scope)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var o = new MonsterSwapOptions { Seed = 4, Scope = scope, Whitelist = MonsterSwap.DefaultWhitelist(s, edits.Model), Ignored = new List<IgnoredObject>() };
            var survey = MonsterSwap.Survey(s, edits.Model, o);
            var someone = survey.First(x => x.Blocked == null && x.LevelBound == null && x.Whoami == 0);
            o.Ignored.Add(new IgnoredObject { Level = someone.Level, Index = someone.Index });
            survey = MonsterSwap.Survey(s, edits.Model, o);
            var levels = s.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var before = levels.ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());

            var r = MonsterSwap.Run(s, edits.Model, o);
            Assert.Equal(r.Report.ToString(), MonsterSwap.Run(s, edits.Model, o).Report.ToString());
            Assert.True(r.Changed > 100);
            MonsterSwap.Apply(r, edits, o.Seed);

            var after = survey.ToDictionary(x => x, x => edits.Model(x.Level).Objects[x.Index]);
            // never moved: conversations (but the whitelist), templates, ignored
            foreach (var x in survey.Where(x => x.Blocked != null)) Assert.Equal(x.O.Record, after[x].Record);
            Assert.Equal(someone.O.Record, after[survey.First(x => x.Level == someone.Level && x.Index == someone.Index)].Record);
            foreach (var x in survey.Where(x => x.Blocked == null)) { Assert.False(MonsterSwap.Unnamed(s, after[x].Item)); Assert.True(after[x].Record[8] > 0); }
            // every kind keeps its count, swimmers stay swimmers, level-bound kinds stay on their level
            Assert.Equal(survey.Select(x => x.O.Item).OrderBy(i => i), after.Values.Select(x => x.Item).OrderBy(i => i));
            var kinds = CreatureKind.Read(s);
            foreach (var x in survey.Where(x => x.Blocked == null)) Assert.Equal(x.Kind.Swims, kinds[after[x].Item].Swims);
            foreach (var lv in survey.Where(x => x.LevelBound != null).GroupBy(x => x.Level))
                Assert.Equal(lv.Select(x => x.O.Item).OrderBy(i => i), lv.Select(x => after[x].Item).OrderBy(i => i));
            // each spot keeps its place and home
            foreach (var x in survey) { Assert.Equal(x.O.TileX, after[x].TileX); Assert.Equal(x.O.Record[0x16], after[x].Record[0x16]); Assert.Equal(x.O.Record[0x17], after[x].Record[0x17]); }
            if (scope == "level")
                foreach (var lv in survey.GroupBy(x => x.Level)) Assert.Equal(lv.Select(x => x.O.Item).OrderBy(i => i), lv.Select(x => after[x].Item).OrderBy(i => i));
            foreach (int l in levels) { bool ok; edits.For(l).Check(out ok); Assert.True(ok); }

            MonsterSwap.Revert(r, edits);
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
        }

        [Fact]
        public void Strict_matching_keeps_each_spots_strength_and_keep_hp_keeps_the_hit_points()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var kinds = CreatureKind.Read(s);
            var o = new MonsterSwapOptions { Seed = 5, Scope = "all", Spread = 0, DepthBlend = 0, KeepHp = true, Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() };
            var survey = MonsterSwap.Survey(s, s.LoadLevel, o);
            var e = new LevelEdits(s);
            MonsterSwap.Apply(MonsterSwap.Run(s, e.Model, o), e, 5);
            int same = 0, n = 0;
            foreach (var x in survey.Where(x => x.Blocked == null && x.LevelBound == null))
            {
                n++;
                var now = e.Model(x.Level).Objects[x.Index];
                Assert.Equal(x.O.Record[8], now.Record[8]);                        // hit points stay with the spot
                bool pw = ((now.Record[0x0D] | now.Record[0x0E] << 8) & 0x400) != 0, pw0 = ((x.O.Record[0x0D] | x.O.Record[0x0E] << 8) & 0x400) != 0;
                double xp = kinds[now.Item].Exp * (pw ? 2.25 : 1), xp0 = kinds[x.O.Item].Exp * (pw0 ? 2.25 : 1);
                if (Math.Abs(xp - xp0) < 1e-9) same++;
            }
            // a few may differ where a monster's height did not fit and the next one in line took the spot
            Assert.True(same >= n * 0.95, same + " of " + n + " spots kept their experience");
        }

        [Fact]
        public void Following_depth_puts_stronger_monsters_deeper()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var kinds = CreatureKind.Read(s);
            var o = new MonsterSwapOptions { Seed = 6, Scope = "all", Spread = 0, DepthBlend = 100, Whitelist = new List<IgnoredObject>(), Ignored = new List<IgnoredObject>() };
            var e = new LevelEdits(s);
            var survey = MonsterSwap.Survey(s, e.Model, o).Where(x => x.Blocked == null && x.LevelBound == null && !x.Kind.Swims).ToList();
            Func<IEnumerable<(int level, double xp)>, double> corr = v =>
            {
                var a = v.ToList(); double mx = a.Average(t => t.level), my = a.Average(t => t.xp);
                double c = a.Sum(t => (t.level - mx) * (t.xp - my)), sx = Math.Sqrt(a.Sum(t => (t.level - mx) * (t.level - mx))), sy = Math.Sqrt(a.Sum(t => (t.xp - my) * (t.xp - my)));
                return c / (sx * sy);
            };
            double r0 = corr(survey.Select(x => (x.Level, (double)x.Kind.Exp)));
            MonsterSwap.Apply(MonsterSwap.Run(s, e.Model, o), e, 6);
            double r1 = corr(survey.Select(x => (x.Level, (double)kinds[e.Model(x.Level).Objects[x.Index].Item].Exp)));
            Assert.True(r1 > r0 && r1 > 0.5, string.Format("depth correlation {0:0.00} → {1:0.00}", r0, r1));
        }

        [Fact]
        public void The_whitelist_lets_the_gazer_move_with_its_conversation()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var w = MonsterSwap.DefaultWhitelist(s, s.LoadLevel);
            Assert.Single(w);
            var o = new MonsterSwapOptions { Seed = 1, Scope = "level", Spread = 100, Whitelist = w, Ignored = new List<IgnoredObject>() };
            var e = new LevelEdits(s);
            MonsterSwap.Apply(MonsterSwap.Run(s, e.Model, o), e, 1);
            // the gazer's conversation number 0x6E now sits on some spot of level 2, with the gazer kind
            var m = e.Model(2);
            var gazer = m.Objects.Where(x => !x.Free && x.IsCritter && x.Record[0x1A] == 0x6E).ToList();
            Assert.Single(gazer);
            Assert.Contains("gazer", s.Names.Get(gazer[0].Item));
        }
    }
}
