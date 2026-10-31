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
        public bool Hit { get { return Object >= 0 || Tile >= 0; } }
        public static readonly PickResult None = new PickResult { Object = -1, Tile = -1, Distance = float.MaxValue };
    }

    /// <summary>What lies under the mouse: the nearest level triangle or object sprite along the ray.</summary>
    public static class Picking
    {
        public static PickResult Pick(Vector3 origin, Vector3 dir, LevelMesh mesh, IList<SpriteInstance> sprites, Vector3 cameraRight, float spriteSlack = 0.02f)
        {
            var best = PickResult.None;
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
                    }
                }
            }
            if (sprites != null)
            {
                var right = cameraRight;
                foreach (var s in sprites)
                {
                    float hw = s.Width / 2 + spriteSlack;
                    var a = s.Base - right * hw + new Vector3(0, -spriteSlack, 0);
                    var b = s.Base + right * hw + new Vector3(0, -spriteSlack, 0);
                    var c = b + new Vector3(0, s.Height + 2 * spriteSlack, 0);
                    var dd = a + new Vector3(0, s.Height + 2 * spriteSlack, 0);
                    float dist;
                    // a sprite in front of its wall wins over the wall a hair behind it
                    if ((RayTriangle(origin, dir, a, b, c, out dist) || RayTriangle(origin, dir, a, c, dd, out dist)) && dist < best.Distance + 0.05f)
                    {
                        best.Distance = Math.Min(dist, best.Distance);
                        best.Object = s.Object;
                        int tx = (int)Math.Floor(s.Base.X), ty = (int)Math.Floor(s.Base.Z);
                        best.Tile = tx >= 0 && ty >= 0 && tx < 64 && ty < 64 ? tx + 64 * ty : -1;
                    }
                }
            }
            if (best.Hit) best.Point = origin + dir * best.Distance;
            return best;
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
