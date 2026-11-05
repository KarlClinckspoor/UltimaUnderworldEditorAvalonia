using System;
using System.Linq;
using UwEditor.Core;
using UwEditor.Core.Editing;
using Xunit;

namespace ModRoundTrip.Tests
{
    /// <summary>LEV.ARK edited through the level editor (UW1's plain archive, UW2's compressed one).</summary>
    public class LevelTests
    {
        static int OpenTile(LevelModel m, out int y, int from = 10)
        {
            for (y = from; y < 63; y++) for (int x = from; x < 63; x++) if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0) return x;
            throw new Exception("no open tile");
        }

        static int ATileWithObjects(LevelModel m, out int y)
        {
            for (y = 1; y < 63; y++) for (int x = 1; x < 63; x++) if (m.TileObjects(x, y).Count > 0) return x;
            throw new Exception("no objects");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_few_objects_and_tiles(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            int y, x = OpenTile(e.Model, out y);
            e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
            int y2, x2 = OpenTile(e.Model, out y2, 20);
            e.SetTileField(x2, y2, "Floor height", (e.Model.Tiles[x2, y2].Height + 1) % 16);
            var e2 = edits.For(2);
            int y3, x3 = ATileWithObjects(e2.Model, out y3);
            e2.DeleteObject(e2.Model.TileObjects(x3, y3)[0].Index);
            edits.Save();
            rt.Check("level.few", "DATA/LEV.ARK");
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Many_tiles_on_every_level_with_objects(string game)
        {
            using var rt = RoundTrip.Start(game);
            if (rt == null) return;
            var s = GameSession.Open(rt.Edited);
            var edits = new LevelEdits(s);
            int levels = Math.Min(s.Kind == UltimateEditor.Games.GameKind.UW2 ? 8 : 9, 9);
            for (int level = 1; level <= levels; level++)
            {
                var e = edits.For(level);
                int n = 0;
                for (int ty = 5; ty < 59 && n < 40; ty += 3)
                    for (int tx = 5; tx < 59 && n < 40; tx += 7)
                        if (e.Model.Tiles[tx, ty].Type == TileType.Open) { e.SetTileField(tx, ty, "Floor height", (e.Model.Tiles[tx, ty].Height + 2) % 16); n++; }
                if (n == 0) continue;   // an empty level slot
                int y, x = OpenTile(e.Model, out y, 1);
                e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
                e.AddObject(ObjectSpec.Default(0x95, s.Kind), x, y);
            }
            edits.Save();
            rt.Check("level.many", "DATA/LEV.ARK");
        }
    }
}
