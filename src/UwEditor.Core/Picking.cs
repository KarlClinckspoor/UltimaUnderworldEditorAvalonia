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
        /// (each wall belongs to the open tile in front of it, and rock has no top), so the ray is
        /// walked through the grid: the first solid tile it enters between the floor and the
        /// ceiling wins, unless a drawn surface (a floor, or an object) is nearer. That is the rock
        /// behind a wall, or the rock under the mouse seen from above. With neither, the tile hit,
        /// or else the tile at floor level under the mouse. -1 when there is none.
        /// </summary>
        public static int TileForBrush(PickResult hit, Func<int, int, bool> solid)
        {
            var o = hit.Origin; var d = hit.Direction;
            if (d.LengthSquared() > 1e-12f)
            {
                int rock; float tRock;
                float maxT = hit.Hit ? hit.Distance + 0.02f : 1e6f;
                if (FirstSolid(o, d, solid, maxT, out rock, out tRock)) return rock;
            }
            if (hit.Hit) return hit.Tile;
            if (Math.Abs(d.Y) < 1e-6f) return -1;
            float t = -o.Y / d.Y;
            if (t <= 0) return -1;
            var p = o + d * t;
            int tx = (int)Math.Floor(p.X), ty = (int)Math.Floor(p.Z);
            return tx < 0 || ty < 0 || tx > 63 || ty > 63 ? -1 : tx + 64 * ty;
        }

        /// <summary>
        /// The first solid tile the ray enters while it is between the floor (0) and the ceiling
        /// (LevelGeometry.Ceiling), no farther than maxT (a grid walk, Amanatides and Woo). A ray
        /// that starts inside rock (the camera behind a wall) skips that rock first.
        /// </summary>
        public static bool FirstSolid(Vector3 o, Vector3 d, Func<int, int, bool> solid, float maxT, out int tile, out float tHit)
        {
            tile = -1; tHit = 0;
            float t0 = 0, t1 = maxT;
            if (!Clip(o.X, d.X, 0, 64, ref t0, ref t1) || !Clip(o.Z, d.Z, 0, 64, ref t0, ref t1) || !Clip(o.Y, d.Y, 0, LevelGeometry.Ceiling, ref t0, ref t1)) return false;
            var p = o + d * (t0 + 1e-5f);
            int cx = Math.Clamp((int)Math.Floor(p.X), 0, 63), cz = Math.Clamp((int)Math.Floor(p.Z), 0, 63);
            int sx = d.X > 0 ? 1 : -1, sz = d.Z > 0 ? 1 : -1;
            float tMaxX = Math.Abs(d.X) < 1e-9f ? float.MaxValue : ((cx + (d.X > 0 ? 1 : 0)) - o.X) / d.X;
            float tMaxZ = Math.Abs(d.Z) < 1e-9f ? float.MaxValue : ((cz + (d.Z > 0 ? 1 : 0)) - o.Z) / d.Z;
            float dX = Math.Abs(d.X) < 1e-9f ? float.MaxValue : 1 / Math.Abs(d.X), dZ = Math.Abs(d.Z) < 1e-9f ? float.MaxValue : 1 / Math.Abs(d.Z);
            float enter = t0;
            bool skipping = t0 <= 0 && solid(cx, cz);
            for (int guard = 0; guard < 200 && enter <= t1; guard++)
            {
                if (cx < 0 || cz < 0 || cx > 63 || cz > 63) break;
                bool s = solid(cx, cz);
                if (skipping) { if (!s) skipping = false; }
                else if (s) { tile = cx + 64 * cz; tHit = enter; return true; }
                if (tMaxX < tMaxZ) { enter = tMaxX; tMaxX += dX; cx += sx; }
                else { enter = tMaxZ; tMaxZ += dZ; cz += sz; }
            }
            return false;
        }

        /// <summary>Narrows [t0, t1] to where o + d t lies between lo and hi on one axis; false when nothing is left.</summary>
        static bool Clip(float o, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-9f) return o >= lo && o <= hi;
            float a = (lo - o) / d, b = (hi - o) / d;
            if (a > b) { var x = a; a = b; b = x; }
            t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
            return t0 <= t1;
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
