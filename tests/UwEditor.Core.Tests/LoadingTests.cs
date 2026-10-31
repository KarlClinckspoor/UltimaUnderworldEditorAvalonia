using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class LoadingTests
    {
        readonly ITestOutputHelper output;
        public LoadingTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Uw1_every_level_loads_and_passes_the_games_check()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            Assert.Equal(GameKind.UW1, s.Kind);
            Assert.False(s.IsSave);
            var slots = s.LevelSlots();
            Assert.Equal(9, slots.Count);
            Assert.All(slots, sl => Assert.True(sl.Present));
            for (int l = 1; l <= 9; l++)
            {
                var m = s.LoadLevel(l);
                output.WriteLine("level {0}: {1} open tiles, {2} objects; {3}", l, m.OpenTileCount, m.UsedObjectCount, m.CheckReport);
                Assert.True(m.CheckOk, m.CheckReport);
                Assert.Equal(48, m.WallMap.Length);
                Assert.Equal(10, m.FloorMap.Length);
                // every object on a tile list lies on an open tile's list it came from
                foreach (var o in m.Objects.Where(o => o.OnTile))
                    Assert.Contains(o, m.TileObjects(o.TileX, o.TileY));
            }
        }

        [Fact]
        public void Uw2_slots_load_and_slot_73_is_the_extra_level()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(Path.Combine(g, "DATA"));
            Assert.Equal(GameKind.UW2, s.Kind);
            var slots = s.LevelSlots();
            Assert.Equal(80, slots.Count);
            Assert.Equal(42, slots.Count(x => x.Present));   // docs/FORMATS.md: 42 level blocks
            foreach (var sl in slots.Where(x => x.Present))
            {
                var m = s.LoadLevel(sl.Level);
                Assert.True(m.CheckOk, "level " + sl.Level + ": " + m.CheckReport);
                Assert.Equal(64, m.WallMap.Length);
            }
            var extra = s.LoadLevel(73);
            Assert.Equal(1046, extra.OpenTileCount);   // claude/uw2-levels-graphics.md
            Assert.Equal(427, extra.UsedObjectCount);
        }

        [Fact]
        public void New_game_starts_on_the_executables_square()
        {
            string g1 = GameData.Game("UW");
            if (g1 != null)
            {
                var s = GameSession.Open(g1);
                Assert.Equal(1, s.Start.Level);
                Assert.Equal(0x20 + 0.5, s.Start.X);
                Assert.Equal(2 + 0.5, s.Start.Y);
                Assert.False(s.LoadLevel(1).Tiles[0x20, 2].IsSolid);
            }
            string g2 = GameData.Game("UW2");
            if (g2 != null)
            {
                var s = GameSession.Open(Path.Combine(g2, "UW2.EXE"));
                Assert.Equal(0x13 + 0.5, s.Start.X);
                Assert.Equal(0x30 + 0.5, s.Start.Y);
                Assert.False(s.LoadLevel(1).Tiles[0x13, 0x30].IsSolid);
            }
        }

        [Fact]
        public void A_saved_game_opens_with_DATA_and_starts_at_the_player()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            string save = Path.Combine(g, "SAVE1");
            if (!Directory.Exists(save)) return;
            var s = GameSession.Open(save);
            Assert.True(s.IsSave);
            Assert.EndsWith("DATA", s.DataFolder);
            Assert.True(s.Start.FromSave);
            output.WriteLine("{0}: level {1} at {2:0.00}, {3:0.00}, z {4:0.00}, facing {5:0.0}", s.Start.Description, s.Start.Level, s.Start.X, s.Start.Y, s.Start.Z, s.Start.FacingDegrees);
            var m = s.LoadLevel(s.Start.Level);
            var t = m.Tiles[(int)s.Start.X, (int)s.Start.Y];
            Assert.False(t.IsSolid);
            // the player stands on the floor of that tile (z within a step of it)
            Assert.InRange(s.Start.Z, m.FloorAt(t.X, t.Y, s.Start.X - t.X, s.Start.Y - t.Y) - 0.26, m.FloorAt(t.X, t.Y, s.Start.X - t.X, s.Start.Y - t.Y) + 1.0);
        }

        [Fact]
        public void Names_come_from_strings_block_4()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            output.WriteLine(string.Join(", ", Enumerable.Range(0, 16).Select(i => s.Names.Raw(i))));
            Assert.True(s.Names.Count >= 0x200);
            Assert.Equal("hand axe", s.Names.Get(0));
            Assert.Equal("dagger", s.Names.Get(3));
        }

        [Fact]
        public void Graphics_load_for_both_games()
        {
            string g1 = GameData.Game("UW");
            if (g1 != null)
            {
                var gr = GameSession.Open(g1).Graphics;
                Assert.Equal(64, gr.WallEdge);
                Assert.Equal(32, gr.FloorEdge);
                Assert.Equal(210, gr.WallTextures.Count);
                Assert.Equal(464, gr.ObjectPictures.Count);
            }
            string g2 = GameData.Game("UW2");
            if (g2 != null)
            {
                var gr = GameSession.Open(g2).Graphics;
                Assert.Equal(264, gr.WallTextures.Count);
                Assert.Equal(464, gr.ObjectPictures.Count);
            }
        }
    }
}
