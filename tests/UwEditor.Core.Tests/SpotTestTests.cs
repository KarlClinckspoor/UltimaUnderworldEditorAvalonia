using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The DOSBox spot test: the tester's PLAYER.DAT and the rounds of four saved games.</summary>
    public class SpotTestTests
    {
        static string Temp(string name) { return Path.Combine(Path.GetTempPath(), "uwedit-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); }

        static void CheckTester(PlayerDat pd, string data)
        {
            var r = pd.Record;
            Assert.Equal("Tester", pd.Name);
            Assert.Equal(30, pd.Strength); Assert.Equal(30, pd.Dexterity); Assert.Equal(30, pd.Intelligence);
            Assert.Equal(30, pd.Skill(1));
            for (int i = 0; i < 20; i++) if (i != 1) Assert.Equal(0, pd.Skill(i));
            Assert.Equal(1, pd.Level);
            Assert.Equal(0, pd.ExperienceTenths);
            Assert.Equal(36, pd.MaxVitality);                // 30 + 1 × 30 / 5
            Assert.Equal(36, pd.Vitality);
            Assert.Equal(3, pd.MaxMana);                     // (0 + 1) × 30 / 8
            Assert.Equal(600, r[0x4C] | r[0x4D] << 8);       // 20 × strength
            Assert.Equal(8, r[0x6D]);                        // talismans
            Assert.Equal(0x35, r[0x70 + 0x1A]);
            Assert.Equal(0x010B3000, BitConverter.ToInt32(r, 0xCE));
            Assert.Equal(2, r[0x5E]);                        // the moonstone on level 2
            Assert.All(Enumerable.Range(0x65, 8), i => Assert.Equal(0, r[i]));   // no quests
            Assert.Equal(1, r[0x64] & 1);                    // right-handed
            var inv = PlayerInventory.Parse(pd.Rest);
            var pieces = SpotTest.BestArmour(data);
            Assert.Equal(new[] { 0x2E, 0x22, 0x28, 0x25, 0x2B, 0x3B }, pieces.Select(p => p.Item));   // helmet, breastplate, plate gauntlets, leggings, boots, tower shield
            Assert.Equal(6, inv.Top.Count);
            for (int s = 0; s < 5; s++) Assert.Equal(pieces[s].Item, inv.InSlot(s).ItemId);
            Assert.Equal(0x3B, inv.InSlot(8).ItemId);         // the shield hand: 7 + lefty
            Assert.All(inv.Top, t => Assert.Equal(0x3F, t.Quality));
            Assert.Equal(pieces.Sum(p => p.Mass), r[0x4A] | r[0x4B] << 8);
            Assert.Empty(inv.Problems());
            // the workspace player object: the adventurer, its hit points the record's
            Assert.Equal(0x7F, pd.Rest[2] | (pd.Rest[3] & 1) << 8);
            Assert.Equal(0xFD, pd.Rest[2 + 0x1A]);
            Assert.Equal(36, pd.Rest[2 + 8]);
        }

        [Fact]
        public void With_no_saved_game_the_tester_is_made_from_nothing()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            string from, wearing;
            var bytes = SpotTest.TesterPlayer(s, new SpotTestOptions(), out wearing, out from);
            Assert.Equal("", from);
            Assert.Equal(GameKind.UW1, PlayerDat.Detect(bytes));
            Assert.True(PlayerDat.KeyMatches(bytes, GameKind.UW1));
            CheckTester(PlayerDat.Parse(bytes, GameKind.UW1), s.DataFolder);
            Assert.Contains("tower shield", wearing, StringComparison.OrdinalIgnoreCase);
            // the editor's own player sheet reads it
            var sheet = PlayerSheet.FromBytes(bytes, GameKind.UW1, s.DataFolder);
            Assert.Equal(30, sheet.Strength);
        }

        [Fact]
        public void A_saved_games_player_becomes_a_new_character_keeping_its_player_object()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            foreach (var save in GameData.Saves("UW"))
            {
                var before = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(save, "PLAYER.DAT")), GameKind.UW1);
                string wearing;
                var t = SpotTest.Tester(PlayerDat.Parse(File.ReadAllBytes(Path.Combine(save, "PLAYER.DAT")), GameKind.UW1), new SpotTestOptions(), s.DataFolder, out wearing);
                var round = PlayerDat.Parse(t.ToBytes(), GameKind.UW1);
                CheckTester(round, s.DataFolder);
                for (int i = 9; i < 0x1B; i++) Assert.Equal(before.Rest[2 + i], round.Rest[2 + i]);
                Assert.Equal(before.Record[0x64] & 0xFE, round.Record[0x64] & 0xFE);   // sex, body, class kept
            }
        }

        [Fact]
        public void Rounds_of_four_saved_games_hold_the_same_levels_and_the_tester_on_each_spot()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(2);
            var tile = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !e.Model.Tiles[k.X, k.Y].IsSolid && e.Model.Tiles[k.X, k.Y].Height < 10);
            e.SetTileField(tile.X, tile.Y, "Floor height", e.Model.Tiles[tile.X, tile.Y].Height + 1);
            var o = new SpotTestOptions();
            o.Stops.Sweep = true;
            string dir = Temp("spottest");
            try
            {
                var run = SpotTest.Start(s, edits, new[] { 2, 3 }, o, dir);
                var expected = ExhumedCheck.Stops(s, edits, 2, o.Stops).Concat(ExhumedCheck.Stops(s, edits, 3, o.Stops)).Select(v => v.Key).ToList();
                Assert.Equal(expected, run.Spots.Select(v => v.Key));
                Assert.True(run.Spots.Count > 8);
                Assert.True(File.Exists(Path.Combine(dir, SpotTest.Marker)));
                Assert.True(File.Exists(Path.Combine(dir, run.Exe)));
                Assert.Equal(edits.ToBytes(), File.ReadAllBytes(Path.Combine(dir, "DATA", "LEV.ARK")));
                var bab = BGlobals.Parse(File.ReadAllBytes(Path.Combine(g, "DATA", "BABGLOBS.DAT")), false);
                int seen = 0;
                while (!run.Finished)
                {
                    var round = SpotTest.WriteRound(s, run);
                    Assert.Equal(Math.Min(4, run.Remaining), round.Count);
                    for (int i = 0; i < round.Count; i++)
                    {
                        string slot = Path.Combine(dir, "SAVE" + (i + 1));
                        Assert.Equal(edits.ToBytes(), File.ReadAllBytes(Path.Combine(slot, "LEV.ARK")));
                        var pd = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(slot, "PLAYER.DAT")), GameKind.UW1);
                        Assert.Equal(round[i].Level, pd.SavedLevel);
                        Assert.Equal(round[i].X, pd.TileX); Assert.Equal(round[i].Y, pd.TileY);
                        Assert.Equal(30, pd.Strength);
                        var gl = BGlobals.Parse(File.ReadAllBytes(Path.Combine(slot, "BGLOBALS.DAT")), true);
                        Assert.Equal(bab.Records.Select(r => r.Slot), gl.Records.Select(r => r.Slot));
                        Assert.All(gl.Records, r => Assert.All(r.Words, w => Assert.Equal(0, w)));
                        string desc = File.ReadAllText(Path.Combine(slot, "DESC"));
                        Assert.StartsWith((run.Tested + i + 1) + "/" + run.Spots.Count + " L" + round[i].Level, desc);
                        Assert.True(desc.Length <= 30);
                    }
                    for (int i = round.Count; i < 4; i++) Assert.False(Directory.Exists(Path.Combine(dir, "SAVE" + (i + 1))));
                    seen += round.Count;
                    SpotTest.FinishRound(run, seen == 4 ? "a wall looked wrong" : null);
                    Assert.Equal(seen, run.Tested);
                }
                Assert.Equal(run.Spots.Count, seen);
                Assert.Equal(0, run.Remaining);
                string log = File.ReadAllText(Path.Combine(dir, SpotTest.LogName));
                Assert.Contains("a wall looked wrong", log);
                Assert.Contains("SAVE1: spot 1 of", log);
                // an earlier round again
                var again = SpotTest.WriteRound(s, run, 0);
                Assert.Equal(run.Spots.Take(4).Select(v => v.Key), again.Select(v => v.Key));
                // a second start clears the first; a folder it did not make, or the game itself, is refused
                SpotTest.Start(s, edits, new[] { 2 }, o, dir);
                string other = Temp("other");
                Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "mine.txt"), "x");
                Assert.Throws<InvalidOperationException>(() => SpotTest.Start(s, edits, new[] { 2 }, o, other));
                Directory.Delete(other, true);
                Assert.Throws<InvalidOperationException>(() => SpotTest.Start(s, edits, new[] { 2 }, o, g));
                // the game folder is untouched
                Assert.False(Directory.Exists(Path.Combine(g, "SAVE1")));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        [Fact]
        public void Uw2_is_refused_for_now()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            Assert.Throws<InvalidOperationException>(() => SpotTest.Start(s, null, new[] { 1 }, new SpotTestOptions(), Temp("spot2")));
        }
    }
}
