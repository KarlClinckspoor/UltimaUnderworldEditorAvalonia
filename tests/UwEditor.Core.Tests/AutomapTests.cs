using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class AutomapTests
    {
        [Fact]
        public void The_seen_byte_matches_the_maps_the_game_shipped_and_notes_round_trip()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            var a = Automap.Load(s, 1);
            var m = s.LoadLevel(1);
            int seen = 0, same = 0;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int b = a.Map[y * 64 + x], t = b & 0xF;
                    if (t == 0 || t >= 10) continue;
                    seen++;
                    if (Automap.Seen(s, m, x, y) == b) same++;
                }
            Assert.True(seen > 300);
            Assert.True(same >= seen * 0.98, same + " of " + seen);
            Assert.Equal(14, a.Notes.Count);
            Assert.Contains(a.Notes, n => n.Text.Contains("THRONE ROOM"));
            var back = Automap.ReadNotes(Automap.WriteNotes(a.Notes));
            Assert.Equal(a.Notes.Select(n => (n.Text, n.X, n.Y)), back.Select(n => (n.Text, n.X, n.Y)));
            // erased notes (x = -1) are left out; text is cut to 49 printable characters
            var odd = Automap.ReadNotes(Automap.WriteNotes(new System.Collections.Generic.List<MapNote> { new MapNote { Text = new string('x', 80) + "é", X = 5, Y = 6 }, new MapNote { Text = "gone", X = -1, Y = 0 } }));
            Assert.Single(odd);
            Assert.Equal(49, odd[0].Text.Length);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Painting_marking_all_and_undo_then_saving_writes_the_blocks(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var c = new EditingTests.Copy(g))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                var a = edits.AutomapFor(2);
                var m = s.LoadLevel(2);
                var before = (byte[])a.Map.Clone();
                a.Snapshot("all");
                a.All(s, m, true);
                int open = 0; for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (!m.Tiles[x, y].IsSolid) open++;
                Assert.Equal(open, a.SeenCount);
                a.Snapshot("unseen");
                Assert.True(a.Paint(s, m, 32, 32, 2, false) >= 0);
                a.Undo();
                Assert.Equal(open, a.SeenCount);
                a.Undo();
                Assert.Equal(before, a.Map);
                a.Snapshot("again");
                a.All(s, m, true);
                a.Notes.Add(new MapNote { Text = "HERE BE DRAGONS", X = 100, Y = 120 });
                Assert.True(edits.HasChanges);
                Assert.Contains(2, edits.DirtyLevels);
                Assert.Equal(new[] { 2 }, edits.Save());
                Assert.False(edits.HasChanges);
                var b = Automap.Load(GameSession.Open(c.Folder), 2);
                Assert.Equal(a.Map, b.Map);
                Assert.Contains(b.Notes, n => n.Text == "HERE BE DRAGONS" && n.X == 100 && n.Y == 120);
                // the level itself is untouched
                var s2 = GameSession.Open(c.Folder);
                Assert.Equal(s.LevArk.Read(s.Profile.LevelBlock(2)), s2.LevArk.Read(s2.Profile.LevelBlock(2)));
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_monster_world_report_says_what_the_levels_hold(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var r = MonsterSwap.WorldReport(s, s.LoadLevel);
            foreach (var w in new[] { "creatures in the world", "By kind", "By level", "Creatures with a conversation", "never moved, has a conversation" }) Assert.Contains(w, r);
            Assert.Contains("L1 ", r);
        }
    }
}
