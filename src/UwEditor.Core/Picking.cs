using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    public struct PickResult
    {
        /// <summary>The object hit, or -1.</summary>
        public int Object;
        /// <summary>The tile hit (x + 64 y), or -1.</summary>
        public int Tile;
        public float Distance;
        public Vector3 Point;
        /// <summary>What the level triangle hit is (0 floor, 1 wall, 2 ceiling), or -1 for an object or nothing.</summary>
        public int Kind;
        /// <summary>The ray: where it starts and its direction (also when nothing was hit).</summary>
        public Vector3 Origin, Direction;
        public bool Hit { get { return Object >= 0 || Tile >= 0; } }
        public static readonly PickResult None = new PickResult { Object = -1, Tile = -1, Distance = float.MaxValue, Kind = -1 };
    }

    /// <summary>What lies under the mouse: the nearest level triangle or object sprite along the ray.</summary>
    public static class Picking
    {
        public static PickResult Pick(Vector3 origin, Vector3 dir, LevelMesh mesh, IList<SpriteInstance> sprites, Vector3 cameraRight, Vector3 cameraUp, ModelMesh models = null, float spriteSlack = 0.02f)
        {
            var best = PickResult.None;
            best.Origin = origin; best.Direction = dir;
            if (mesh != null)
            {
                var v = mesh.Vertices;
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    float d;
                    if (RayTriangle(origin, dir, v[3 * t].Position, v[3 * t + 1].Position, v[3 * t + 2].Position, out d) && d < best.Distance)
                    {
                        best.Distance = d;
                        best.Tile = mesh.TriangleTile[t];
                        best.Object = -1;
                        best.Kind = mesh.TriangleKind[t];
                    }
                }
            }
            if (models != null)
            {
                var v = models.Vertices;
                for (int t = 0; t < models.TriangleCount; t++)
                {
                    float d;
                    if (RayTriangle(origin, dir, v[3 * t].Position, v[3 * t + 1].Position, v[3 * t + 2].Position, out d) && d < best.Distance + 0.02f)
                    {
                        best.Distance = Math.Min(d, best.Distance);
                        best.Object = models.TriangleObject[t];
                        best.Tile = models.TriangleTile[t];
                        best.Kind = -1;
                    }
                }
            }
            if (sprites != null)
            {
                foreach (var s in sprites)
                {
                    // the quad as the shader draws it: facing the camera round its middle
                    var r = cameraRight * (s.Width / 2 + spriteSlack);
                    var u = cameraUp * (s.Height / 2 + spriteSlack);
                    var c0 = s.Centre;
                    Vector3 a = c0 - r - u, b = c0 + r - u, c = c0 + r + u, dd = c0 - r + u;
                    float dist;
                    // a sprite in front of its wall wins over the wall a hair behind it
                    if ((RayTriangle(origin, dir, a, b, c, out dist) || RayTriangle(origin, dir, a, c, dd, out dist)) && dist < best.Distance + 0.05f)
                    {
                        best.Distance = Math.Min(dist, best.Distance);
                        best.Object = s.Object;
                        best.Kind = -1;
                        int tx = (int)Math.Floor(s.Base.X), ty = (int)Math.Floor(s.Base.Z);
                        best.Tile = tx >= 0 && ty >= 0 && tx < 64 && ty < 64 ? tx + 64 * ty : -1;
                    }
                }
            }
            if (best.Hit) best.Point = origin + dir * best.Distance;
            return best;
        }

        /// <summary>
        /// The tile a tile brush works on, solid rock too. Solid tiles have no surface of their own
        /// (a wall belongs to the open tile in front of it), so a wall hit gives the solid tile just
        /// behind it, and a ray that hits nothing (rock seen from above, in the 2D map or over the
        /// walls) gives the solid tile at the top of the rock under it, or else the tile at floor
        /// level. Otherwise the tile hit. -1 when there is none.
        /// </summary>
        public static int TileForBrush(PickResult hit, Func<int, int, bool> solid)
        {
            if (hit.Object < 0 && hit.Kind == 1 && hit.Tile >= 0)
            {
                var q = hit.Point + hit.Direction * 0.02f;
                int tx = (int)Math.Floor(q.X), ty = (int)Math.Floor(q.Z);
                if (tx >= 0 && ty >= 0 && tx < 64 && ty < 64 && solid(tx, ty)) return tx + 64 * ty;
            }
            if (hit.Hit) return hit.Tile;
            var o = hit.Origin; var d = hit.Direction;
            if (Math.Abs(d.Y) < 1e-6f) return -1;
            foreach (float h in new[] { LevelGeometry.Ceiling, 0f })
            {
                float t = (h - o.Y) / d.Y;
                if (t <= 0) continue;
                var p = o + d * t;
                int tx = (int)Math.Floor(p.X), ty = (int)Math.Floor(p.Z);
                if (tx < 0 || ty < 0 || tx > 63 || ty > 63) continue;
                if (h == 0f || solid(tx, ty)) return tx + 64 * ty;
            }
            return -1;
        }

        /// <summary>Whether a level surface lies between two points (a little short of each end).</summary>
        public static bool Blocked(LevelMesh mesh, Vector3 from, Vector3 to)
        {
            var d = to - from;
            float len = d.Length();
            if (len < 1e-4f || mesh == null) return false;
            d /= len;
            var v = mesh.Vertices;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                if (mesh.TriangleKind[t] == 2) continue;   // ceilings are not drawn from above
                float hit;
                if (RayTriangle(from, d, v[3 * t].Position, v[3 * t + 1].Position, v[3 * t + 2].Position, out hit) && hit > 0.05f && hit < len - 0.05f)
                    return true;
            }
            return false;
        }

        /// <summary>Möller–Trumbore, both sides.</summary>
        public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Math.Abs(det) < 1e-9f) return false;
            float inv = 1 / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-4f;
        }
    }
}
