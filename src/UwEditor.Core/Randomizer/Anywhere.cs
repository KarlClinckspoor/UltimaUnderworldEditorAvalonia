using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A square an item can be put on, and where on it.</summary>
    public struct AnywhereSpot
    {
        public int X, Y, FineX, FineY;
        public override string ToString() { return string.Format("({0}, {1})", X, Y); }
    }

    /// <summary>
    /// Place anywhere: the key item scatter and the item property shuffle put the items lying on
    /// the floor on random squares of their level instead of where items lay. A square qualifies
    /// when it is open (flat, a slope, or the open half of a diagonal), not on the level's edge,
    /// its floor neither water nor lava (TERRAIN.DAT, as creatures are kept off them), and nothing
    /// on it is a door, a trap or a trigger; the key item scatter adds the zones its logic reaches. The item lies at a
    /// fine position inside the open part of the tile, at the floor's height there (a slope rises
    /// across the tile, LevelModel.FloorAt; LevelEditor.FloorZ).
    /// </summary>
    public static class Anywhere
    {
        /// <summary>Whether a square can take an item, before the caller's own test of reach.</summary>
        public static bool Qualifies(GameSession s, LevelModel m, TerrainTable terrain, int x, int y)
        {
            if (x < 1 || y < 1 || x > 62 || y > 62) return false;
            var t = m.Tiles[x, y];
            if (t == null || t.IsSolid) return false;
            if (CreatureTerrain.Restricted(CreatureTerrain.FloorClass(s, terrain, m, x, y))) return false;
            foreach (var o in m.TileObjects(x, y))
                if ((o.Item >= 0x140 && o.Item <= 0x14F) || o.IsTrapOrTrigger) return false;
            return true;
        }

        /// <summary>The squares of a level that qualify and that reach allows (null: all), row by row.</summary>
        public static List<(int X, int Y)> Squares(GameSession s, LevelModel m, TerrainTable terrain, Func<int, int, bool> reach)
        {
            var list = new List<(int, int)>();
            for (int y = 1; y < 63; y++)
                for (int x = 1; x < 63; x++)
                    if (Qualifies(s, m, terrain, x, y) && (reach == null || reach(x, y))) list.Add((x, y));
            return list;
        }

        /// <summary>
        /// Whether a fine position (the middle of eighth fx, fy; y north) lies inside the open part
        /// of a tile, with a margin from a diagonal wall. OpenSE is the triangle below the line from
        /// the south-west corner to the north-east one, and so on (LevelGeometry.FloorCorners).
        /// </summary>
        public static bool InOpenPart(TileType type, int fx, int fy)
        {
            double px = (fx + 0.5) / 8, py = (fy + 0.5) / 8, margin = 0.15;
            switch (type)
            {
                case TileType.OpenSE: return px - py > margin;
                case TileType.OpenNW: return py - px > margin;
                case TileType.OpenSW: return px + py < 1 - margin;
                case TileType.OpenNE: return px + py > 1 + margin;
                case TileType.Solid: return false;
                default: return true;
            }
        }

        /// <summary>A random fine position inside the open part of the tile, away from its edges (1-6).</summary>
        public static (int FineX, int FineY) FinePosition(Random rng, TileType type)
        {
            for (int k = 0; k < 64; k++)
            {
                int fx = 1 + rng.Next(6), fy = 1 + rng.Next(6);
                if (InOpenPart(type, fx, fy)) return (fx, fy);
            }
            switch (type)
            {
                case TileType.OpenSE: return (5, 2);
                case TileType.OpenNW: return (2, 5);
                case TileType.OpenSW: return (2, 2);
                case TileType.OpenNE: return (5, 5);
                default: return (3, 3);
            }
        }

        /// <summary>
        /// Spots for count items on one level: the squares in a random order, one item a square
        /// while they last (then round again), each with a random fine position. Empty when the
        /// level has no square.
        /// </summary>
        public static List<AnywhereSpot> Spots(Random rng, LevelModel m, IList<(int X, int Y)> squares, int count)
        {
            var spots = new List<AnywhereSpot>();
            if (squares.Count == 0 || count <= 0) return spots;
            var order = squares.ToList();
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var x = order[i]; order[i] = order[j]; order[j] = x; }
            for (int k = 0; k < count; k++)
            {
                var sq = order[k % order.Count];
                var (fx, fy) = FinePosition(rng, m.Tiles[sq.X, sq.Y].Type);
                spots.Add(new AnywhereSpot { X = sq.X, Y = sq.Y, FineX = fx, FineY = fy });
            }
            return spots;
        }

        /// <summary>Moves an object lying on a tile to a spot, on the floor there, in a level's bytes (changed in place).</summary>
        public static void Move(GameSession s, int level, byte[] data, byte[] tex, int index, AnywhereSpot to)
        {
            var e = new LevelEditor(s, level, data, tex);
            e.MoveObject(index, to.X, to.Y, to.FineX, to.FineY, onFloor: true);
        }
    }
}
