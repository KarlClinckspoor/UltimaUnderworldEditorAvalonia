using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class PlayTestAndWorldTests
    {
        static string Temp() { return Path.Combine(Path.GetTempPath(), "uwedit-play-" + Guid.NewGuid().ToString("N")); }

        [Fact]
        public void A_new_game_play_test_patches_the_copys_executable_and_carries_the_edits()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            using (var c = new EditingTests.Copy(g, "SAVE1"))
            {
                var s = GameSession.Open(c.Folder);
                if (s.ExePath == null) return;
                var edits = new LevelEdits(s);
                var e = edits.For(1);
                var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => t.x > 1 && t.y > 1 && !e.Model.Tiles[t.x, t.y].IsSolid);
                e.SetTileField(open.x, open.y, "Floor height", (e.TileField(open.x, open.y, "Floor height") + 1) % 15);
                var exeBefore = File.ReadAllBytes(s.ExePath);
                string to = Temp();
                try
                {
                    var plan = PlayTest.Prepare(s, edits, null, 1, open.x, open.y, to);
                    Assert.Null(plan.Slot);
                    Assert.Equal(exeBefore, File.ReadAllBytes(s.ExePath));                 // the game itself untouched
                    var copy = GameSession.Open(to);
                    Assert.Equal(open.x, (int)copy.NewGameStart.X);
                    Assert.Equal(open.y, (int)copy.NewGameStart.Y);
                    Assert.Equal(e.Data, copy.LevArk.Read(copy.Profile.LevelBlock(1)));
                    Assert.False(Directory.EnumerateFiles(to, "*.BKP", SearchOption.AllDirectories).Any());
                    // again: the marked folder is cleared and made again
                    PlayTest.Prepare(s, edits, null, 1, open.x, open.y, to);
                    // a folder the editor did not make is refused
                    string other = Temp();
                    Directory.CreateDirectory(other);
                    File.WriteAllText(Path.Combine(other, "mine.txt"), "x");
                    Assert.Throws<InvalidOperationException>(() => PlayTest.Prepare(s, edits, null, 1, open.x, open.y, other));
                    Assert.True(File.Exists(Path.Combine(other, "mine.txt")));
                    Directory.Delete(other, true);
                    var args = PlayTest.DosBoxArguments(plan, "-conf \"my conf.conf\"");
                    Assert.Equal(new[] { "-conf", "my conf.conf", "-c", "mount c \"" + to + "\"", "-c", "c:", "-c", "UW" }, args);
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Fact]
        public void A_play_test_elsewhere_puts_the_player_in_a_saved_game_of_the_copy()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            using (var c = new EditingTests.Copy(g, "SAVE1"))
            {
                var s = GameSession.Open(c.Folder);
                var edits = new LevelEdits(s);
                int level = 3;
                var m = edits.Model(level);
                var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => t.x > 1 && t.y > 1 && t.x < 62 && t.y < 62 && !m.Tiles[t.x, t.y].IsSolid);
                string to = Temp();
                try
                {
                    var plan = PlayTest.Prepare(s, edits, null, level, open.x, open.y, to);
                    Assert.Equal("SAVE1", plan.Slot);
                    var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(to, "SAVE1", "PLAYER.DAT")), GameKind.UW2);
                    Assert.Equal(level, pd.SavedLevel);
                    Assert.Equal(open.x, pd.TileX);
                    Assert.Equal(open.y, pd.TileY);
                    // the save's levels are DATA's, as edited
                    Assert.Equal(File.ReadAllBytes(Path.Combine(to, "DATA", "LEV.ARK")), File.ReadAllBytes(Path.Combine(to, "SAVE1", "LEV.ARK")));
                    // the folder's own save is untouched
                    Assert.Equal(File.ReadAllBytes(Path.Combine(g, "SAVE1", "PLAYER.DAT")), File.ReadAllBytes(Path.Combine(c.Folder, "SAVE1", "PLAYER.DAT")));
                }
                finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_world_check_finds_a_lock_whose_keys_are_gone_and_full_levels(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var before = WorldCheck.Run(s, edits.Model);
            Assert.Equal(s.LevelSlots().Count(x => x.Present), before.Count(f => f.Kind == "objects"));
            // delete every key of a lock that has some, on every level: its locked locks are reported
            var groups = KeyIndex.Build(s, edits.Model);
            var gr = groups.First(x => x.Id > 0 && x.Keys.Count > 0 && x.Locks.Any(l => l.Locked));
            foreach (var k in gr.Keys.OrderByDescending(k => k.Index)) { var ed = edits.For(k.Level); if (!ed.Model.Objects[k.Index].Free) ed.DeleteObject(k.Index); }
            var after = WorldCheck.Run(s, edits.Model);
            Assert.Contains(after, f => f.Kind == "no key" && f.Text.Contains("lock " + gr.Id + ","));
            Assert.DoesNotContain(before, f => f.Kind == "no key" && f.Text.Contains("lock " + gr.Id + ","));
            // the player carrying one clears it
            Assert.DoesNotContain(WorldCheck.Run(s, edits.Model, new[] { gr.Id }), f => f.Kind == "no key" && f.Text.Contains("lock " + gr.Id + ","));
            // fill a level up: it is reported full
            var e1 = edits.For(1);
            var open = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).First(t => !e1.Model.Tiles[t.x, t.y].IsSolid);
            while (new LevelBlock(e1.Data, s.Kind).StaticTop >= 0) e1.AddObject(new ObjectSpec { Item = 0xC4 }, open.x, open.y);
            Assert.Contains(WorldCheck.Run(s, edits.Model), f => f.Kind == "objects" && f.Level == 1 && f.Severity == 2);
        }
    }
}
