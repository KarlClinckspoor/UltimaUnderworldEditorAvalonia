using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// UW1's level 5 Pac-Man maze: every randomizer leaves its glowing rocks alone (MazeRocks).
    /// Randomizer ▸ Randomize mantras (UW1): the words swap, INSAHN and NO stay, every text follows, Undo.
    /// </summary>
    public class MazeRocksAndMantrasTests
    {
        /// <summary>The rocks of level 5 as they lie: square, quantity, quality, owner (not the next link, which a neighbour's change may move).</summary>
        static List<string> Rocks(LevelModel m)
        {
            return m.Objects.Where(o => !o.Free && o.TileX >= 0 && o.Item == MazeRocks.Item)
                .Select(o => string.Format("{0},{1} #{2} q{3} o{4} l{5}", o.TileX, o.TileY, o.Index, o.Quality, o.Owner, o.Link)).OrderBy(x => x).ToList();
        }

        static GameSession Uw1() { string g = GameData.Game("UW"); return g == null ? null : GameSession.Open(g); }

        [Fact]
        public void The_maze_rocks_are_all_on_level_5_and_no_list_offers_them()
        {
            var s = Uw1(); if (s == null) return;
            var rocks = Rocks(s.LoadLevel(MazeRocks.Level));
            // 83 lie in the maze, one to a square; the 84th is carried by creature #227 at (17, 36)
            Assert.True(rocks.Count == 84, string.Join(" | ", rocks));
            foreach (var slot in s.LevelSlots().Where(x => x.Present && x.Level != MazeRocks.Level)) Assert.Empty(Rocks(s.LoadLevel(slot.Level)));
            int inMaze = rocks.Count(r =>
            {
                var xy = r.Split(' ')[0].Split(',').Select(int.Parse).ToArray();
                return MazeRocks.InMaze(s, MazeRocks.Level, xy[0], xy[1]);
            });
            Assert.Equal(83, inMaze);
            System.Func<int, LevelModel> level = s.LoadLevel;
            Assert.DoesNotContain(ItemShuffle.Candidates(s, level), c => c.Level == MazeRocks.Level && s.LoadLevel(c.Level).Objects[c.Index].Item == MazeRocks.Item);
            Assert.DoesNotContain(ItemShuffle.Candidates(s, level, true), c => c.Level == MazeRocks.Level && s.LoadLevel(c.Level).Objects[c.Index].Item == MazeRocks.Item);
            // Place anywhere drops nothing in the maze
            var m = s.LoadLevel(MazeRocks.Level);
            var terrain = CreatureTerrain.TryLoad(s);
            Assert.DoesNotContain(Anywhere.Squares(s, m, terrain, null), q => MazeRocks.InMaze(s, MazeRocks.Level, q.X, q.Y));
            // UW2's 0x129 is another item, and other levels are not the maze
            Assert.False(MazeRocks.InMaze(s, 4, 5, 5));
        }

        [Theory]
        [InlineData("shuffle")]
        [InlineData("swap-kind")]
        [InlineData("swap-any")]
        public void Item_shuffles_and_swaps_leave_the_maze_rocks_alone(string mode)
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            var before = Rocks(edits.Model(MazeRocks.Level));
            var o = ShuffleOptions.Defaults(s, edits.Model);
            o.Seed = 5; o.Mode = mode; o.SwapScenery = true; o.SwapBones = true; o.PlaceAnywhere = true;
            var r = ItemShuffle.Run(s, edits.Model, o);
            ItemShuffle.Apply(r, edits, o.Seed);
            Assert.Equal(before, Rocks(edits.Model(MazeRocks.Level)));
        }

        [Fact]
        public void The_key_item_scatter_with_filler_leaves_the_maze_rocks_alone()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            var before = Rocks(edits.Model(MazeRocks.Level));
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 11, Filler = true });
            Assert.True(r.Success);
            KeyShuffle.Apply(r, edits);
            Assert.Equal(before, Rocks(edits.Model(MazeRocks.Level)));
        }

        [Fact]
        public void No_named_challenge_touches_the_maze_rocks()
        {
            var s = Uw1(); if (s == null) return;
            var edits = new LevelEdits(s);
            var before = Rocks(edits.Model(MazeRocks.Level));
            var traders = Challenges.TraderSlots(s);
            foreach (var info in Challenges.All)
            {
                var w = ChallengeWorld.Copy(s, edits, null, null, traders);
                var o = new ChallengeOptions { Seed = 7, Special = Challenges.DefaultSpecial(s, edits.Model) };
                o.Set(info.Id, true);
                Challenges.Run(w, o);
                var after = Rocks(w.Model(MazeRocks.Level));
                Assert.True(before.SequenceEqual(after), info.Name + " touched the maze rocks: before " + string.Join(" | ", before.Except(after)) + "; after " + string.Join(" | ", after.Except(before)));
            }
        }

        // ---- mantras

        [Fact]
        public void Every_mantra_but_INSAHN_and_NO_gets_another_ones_word()
        {
            var s = Uw1(); if (s == null) return;
            var b2 = s.Strings.Block(2);
            for (int seed = 1; seed < 40; seed++)
            {
                var plan = MantraShuffle.Plan(b2, seed);
                Assert.Equal(24, plan.Count);
                Assert.DoesNotContain(MantraShuffle.Cup, plan.Keys);
                Assert.DoesNotContain(MantraShuffle.Nothing, plan.Keys);
                foreach (var kv in plan) Assert.NotEqual(b2[kv.Key], kv.Value);
                Assert.Equal(plan.Keys.Select(k => b2[k]).OrderBy(x => x), plan.Values.OrderBy(x => x));
            }
            Assert.Equal(MantraShuffle.Plan(b2, 9), MantraShuffle.Plan(b2, 9));
        }

        [Fact]
        public void Rewrite_takes_capitals_and_long_title_case_words_only()
        {
            var swap = new Dictionary<string, string> { { "RA", "SOL" }, { "SUMM RA", "MU AHM" }, { "FANLO", "LU" }, { "UN", "ORA" } };
            var f = MantraShuffle.Finder(swap.Keys);
            Assert.Equal("Speak the mantra SOL now.", MantraShuffle.Rewrite("Speak the mantra RA now.", f, swap));
            Assert.Equal("speak Mu Ahm, the mantra of Courage", MantraShuffle.Rewrite("speak Summ Ra, the mantra of Courage", f, swap));
            Assert.Equal("entitled \"Folanae Lu\"", MantraShuffle.Rewrite("entitled \"Folanae Fanlo\"", f, swap));
            // short words in title or lower case are ordinary words; letters around a mantra make another word
            Assert.Equal("Un, ra, Ra, RAIN, BRA", MantraShuffle.Rewrite("Un, ra, Ra, RAIN, BRA", f, swap));
            // one pass: a new word is never changed again
            Assert.Equal("ORA SOL", MantraShuffle.Rewrite("UN RA", f, swap));
        }

        [Fact]
        public void The_mantras_and_every_text_naming_them_change_and_undo_puts_them_back()
        {
            var s = Uw1(); if (s == null) return;
            var st = StringsEditor.Open(s);
            var shipped = st.Pak.Blocks.ToDictionary(b => b, b => st.Pak.Get(b).ToList());
            var r = MantraShuffle.Apply(st, 3);
            var plan = r.Mantras.Where(m => m.Was != m.Now).ToDictionary(m => m.Was, m => m.Now);
            Assert.Equal(24, plan.Count);
            Assert.Equal("INSAHN", st.Get(2, MantraShuffle.Cup));
            Assert.Equal("NO", st.Get(2, MantraShuffle.Nothing));
            Assert.Equal(plan["RA"], st.Get(2, MantraShuffle.First));
            // the shrine still finds every word once
            var words = Enumerable.Range(MantraShuffle.First, MantraShuffle.Count).Select(i => st.Get(2, i)).ToList();
            Assert.Equal(words.Count, words.Distinct().Count());

            Assert.Contains(plan["RA"], st.Get(3, 0x21));                                   // a scroll
            Assert.Contains(plan["LON"], st.Get(0x0E02, 8));                                // Shak's conversation
            Assert.Contains(plan["AMO"], st.Get(0x0E92, 0x21));                             // a generic conversation
            // "speak Summ Ra, the mantra of Courage": in title case, as it was
            string courage = string.Join(" ", plan["SUMM RA"].Split(' ').Select(w => w.Substring(0, 1) + w.Substring(1).ToLowerInvariant()));
            Assert.Contains("speak " + courage + ",", st.Get(8, 0x46));
            Assert.DoesNotContain("Fanlo", st.Get(3, 0xAB));
            // nothing but mentions changed: the texts without a mantra are as shipped
            var changed = r.Edits.Select(e => (e.Block, e.Index)).ToHashSet();
            foreach (int b in st.Pak.Blocks)
                for (int i = 0; i < shipped[b].Count; i++)
                    if (!changed.Contains((b, i))) Assert.Equal(shipped[b][i], st.Pak.Get(b)[i]);
            Assert.True(r.Edits.Count(e => e.Block != 2) >= 30, r.Report(true));
            Assert.Contains(r.Edits, e => e.Block >= 0x0E00);

            Assert.Equal(0, MantraShuffle.Undo(st, r));
            foreach (int b in st.Pak.Blocks) Assert.Equal(shipped[b], st.Pak.Get(b));
        }

        [Fact]
        public void UW2_has_no_mantras_to_swap()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var st = StringsEditor.Open(GameSession.Open(g));
            var r = MantraShuffle.Preview(st, 1);
            Assert.Empty(r.Edits);
            Assert.NotEmpty(r.Notes);
        }
    }
}
