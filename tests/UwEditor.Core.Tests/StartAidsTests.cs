using System;
using System.Linq;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The Apply window's helps (UW1): a full torch and the map beside the start, every map explored, and Undo apply taking them back.</summary>
    public class StartAidsTests
    {
        static readonly StartPoint Shipped = new StartPoint { Level = 1, X = 32.5, Y = 2.5 };

        [Fact]
        public void A_full_torch_and_the_map_lie_beside_the_start()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            // the shipped sack at (33, 3) holds a torch of quality 40 and the map: the torch is filled up, the map kept
            var said = StartAids.PutKit(s, edits, Shipped);
            Assert.Equal(63, edits.Model(1).Objects[936].Quality);
            Assert.Contains(said, l => l.Contains("#936") && l.Contains("quality 40 to 63"));
            Assert.Contains(said, l => l.Contains("map (#941) already lies"));
            // again: nothing more to do
            int count = edits.Model(1).Objects.Count(o => !o.Free);
            said = StartAids.PutKit(s, edits, Shipped);
            Assert.Equal(count, edits.Model(1).Objects.Count(o => !o.Free));
            // with the sack gone, both are put on the floor beside the start
            edits.For(1).DeleteObject(942);
            said = StartAids.PutKit(s, edits, Shipped);
            var m = edits.Model(1);
            // beside the start (32, 2): the first open square around it, (33, 3) being a diagonal tile
            Func<ObjectInfo, bool> beside = o => o.OnTile && Math.Abs(o.TileX - 32) <= 1 && Math.Abs(o.TileY - 2) <= 1;
            var torch = m.Objects.Single(o => !o.Free && o.Item == StartAids.Torch && beside(o));
            var map = m.Objects.Single(o => !o.Free && o.Item == StartAids.Map && beside(o));
            Assert.Equal(new[] { 33, 2 }, new[] { torch.TileX, torch.TileY });
            Assert.Equal(new[] { torch.TileX, torch.TileY }, new[] { map.TileX, map.TileY });
            Assert.Equal(63, torch.Quality);
            Assert.True(torch.OnTile && map.OnTile);
            Assert.Equal(0x200, map.Link);
            bool ok;
            edits.For(1).Check(out ok);
            Assert.True(ok);
        }

        [Fact]
        public void Apply_explores_the_maps_and_puts_the_torch_and_map_and_undo_apply_takes_them_back()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var level1 = (byte[])edits.For(1).Data.Clone();
            var maps = s.LevelSlots().Where(x => x.Present).ToDictionary(x => x.Level, x => (byte[])edits.AutomapFor(x.Level).Map.Clone());
            var r = KeyShuffle.Run(s, edits, new KeyShuffleOptions { StartKit = true, ExploreMaps = true });
            Assert.True(r.Success);
            KeyShuffle.Apply(r, edits);
            Assert.Contains(r.AppliedAids, l => l.Contains("torch"));
            Assert.Contains(r.AppliedAids, l => l.StartsWith("The maps of levels"));
            // every open square of a level is seen, rock is empty
            var a = edits.AutomapFor(1);
            var m = edits.Model(1);
            Assert.True(a.Dirty);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) Assert.Equal(Automap.Seen(s, m, x, y), a.Map[y * 64 + x]);
            Assert.True(UltimateEditor.Games.AutomapLevel.Seen(a.Map[2 * 64 + 32]));
            Assert.Contains(m.Objects, o => !o.Free && (o.Item == StartAids.Torch || o.Item == StartAids.LitTorch) && o.Quality == 63);
            // Undo apply: the items, the torch and map, and the maps go back
            Assert.Empty(KeyShuffle.Undo(r, edits));
            Assert.True(edits.For(1).Data.AsSpan().SequenceEqual(level1));
            foreach (var kv in maps) Assert.True(edits.AutomapFor(kv.Key).Map.AsSpan().SequenceEqual(kv.Value), "map " + kv.Key);
        }
    }
}
