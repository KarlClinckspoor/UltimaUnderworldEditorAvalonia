using System.Collections.Generic;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// The movement rules of the zone fill: steps left, right, up and down (no diagonals), never
    /// into rock or through the wall side of a diagonal tile, never into a tile holding a door
    /// (any door, portcullis or secret door, open or closed: that tile is left out), and never
    /// up more than <see cref="MaxClimb"/> floor steps between the touching edges of two tiles.
    /// Going down any height is fine, so a fill from the top of a drop reaches the bottom, but not
    /// the other way.
    /// </summary>
    public sealed class ZoneFlood
    {
        /// <summary>The highest climb a jump makes, in floor steps (a walk climbs 1: the player's step height is 8 z units, MOTION.H).</summary>
        public const int DefaultMaxClimb = 2;

        readonly LevelModel m;
        readonly bool[,] door = new bool[64, 64];
        public int MaxClimb { get; private set; }

        public ZoneFlood(LevelModel m, int maxClimb = DefaultMaxClimb)
        {
            this.m = m;
            MaxClimb = maxClimb;
            foreach (var o in m.Objects)
                if (!o.Free && o.OnTile && o.Item >= 0x140 && o.Item <= 0x14F && o.TileX >= 0 && o.TileX < 64 && o.TileY >= 0 && o.TileY < 64)
                    door[o.TileX, o.TileY] = true;
        }

        public bool HasDoor(int x, int y) { return door[x, y]; }

        /// <summary>A tile the fill may colour: open floor with no door.</summary>
        public bool Enterable(int x, int y)
        {
            return x >= 0 && y >= 0 && x < 64 && y < 64 && !m.Tiles[x, y].IsSolid && !door[x, y];
        }

        // directions: 0 north (+y), 1 east (+x), 2 south (-y), 3 west (-x)
        static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };
        // the corners of each edge (LevelGeometry: 0 (0,0), 1 (1,0), 2 (1,1), 3 (0,1))
        static readonly int[,] EdgeCorners = { { 2, 3 }, { 1, 2 }, { 0, 1 }, { 0, 3 } };

        static bool SideOpen(TileType t, int dir)
        {
            switch (t)
            {
                case TileType.Solid: return false;
                case TileType.OpenSE: return dir == 2 || dir == 1;
                case TileType.OpenSW: return dir == 2 || dir == 3;
                case TileType.OpenNE: return dir == 0 || dir == 1;
                case TileType.OpenNW: return dir == 0 || dir == 3;
                default: return true;
            }
        }

        static void Edge(TileInfo t, int dir, out int lo, out int hi)
        {
            var h = LevelGeometry.CornerHeights(t);
            int a = h[EdgeCorners[dir, 0]], b = h[EdgeCorners[dir, 1]];
            lo = a < b ? a : b; hi = a < b ? b : a;
        }

        /// <summary>Whether a step from (x, y) in direction dir (0 N, 1 E, 2 S, 3 W) is allowed.</summary>
        public bool CanStep(int x, int y, int dir)
        {
            int nx = x + Dx[dir], ny = y + Dy[dir];
            if (!Enterable(nx, ny)) return false;
            var a = m.Tiles[x, y]; var b = m.Tiles[nx, ny];
            if (!SideOpen(a.Type, dir) || !SideOpen(b.Type, (dir + 2) & 3)) return false;
            int alo, ahi, blo, bhi;
            Edge(a, dir, out alo, out ahi);
            Edge(b, (dir + 2) & 3, out blo, out bhi);
            return blo - ahi <= MaxClimb;
        }

        /// <summary>
        /// Every tile reachable from (x, y) by these rules whose zone is the start tile's (tiles of
        /// other zones stop the fill as walls do). Empty when the start is rock or holds a door.
        /// </summary>
        public List<int> Reach(byte[] zones, int x, int y)
        {
            var found = new List<int>();
            if (!Enterable(x, y)) return found;
            byte from = zones[y * 64 + x];
            var seen = new bool[64 * 64];
            var stack = new Stack<int>();
            stack.Push(y * 64 + x);
            seen[y * 64 + x] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                found.Add(k);
                int kx = k % 64, ky = k / 64;
                for (int d = 0; d < 4; d++)
                {
                    int nx = kx + Dx[d], ny = ky + Dy[d];
                    if (nx < 0 || ny < 0 || nx >= 64 || ny >= 64) continue;
                    int nk = ny * 64 + nx;
                    if (seen[nk] || zones[nk] != from || !CanStep(kx, ky, d)) continue;
                    seen[nk] = true;
                    stack.Push(nk);
                }
            }
            return found;
        }
    }
}
