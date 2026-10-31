using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>A number at the edge of the view naming a grid line ("X: 5").</summary>
    public struct GridLabel
    {
        public string Text;
        /// <summary>Where the line meets the edge, in view pixels (y down).</summary>
        public float X, Y;
        /// <summary>True for a line of constant x (labelled at the bottom or top edge).</summary>
        public bool IsX;
        /// <summary>How far the line is from the point looked at, in tiles: nearer lines get their label first.</summary>
        public float Distance;
    }

    /// <summary>
    /// Yellow lines every N tiles, laid on the floors so they follow heights and slopes, and the
    /// numbers at the view's edges that say which line is which.
    /// </summary>
    public static class GridOverlay
    {
        public static readonly Vector4 Colour = new Vector4(1f, 0.84f, 0.15f, 0.85f);
        const float Lift = 0.012f;

        /// <summary>The lines x = k·step and y = k·step (0 &lt; k·step &lt; 64) on the open side of each tile edge they cross.</summary>
        public static List<float> Lines(LevelModel level, int step)
        {
            var f = new List<float>();
            if (step < 1) return f;
            var c = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            Action<int, int, int, int, int> edge = (tx, ty, e, a, b) =>
            {
                if (tx < 0 || ty < 0 || tx >= 64 || ty >= 64) return;
                var t = level.Tiles[tx, ty];
                if (t.IsSolid || !LevelGeometry.EdgeOpen(t.Type, e)) return;
                int[] h = LevelGeometry.CornerHeights(t);
                LevelScene.Line(f,
                    new Vector3(tx + c[a].X, h[a] * LevelGeometry.Step + Lift, ty + c[a].Y),
                    new Vector3(tx + c[b].X, h[b] * LevelGeometry.Step + Lift, ty + c[b].Y), Colour);
            };
            for (int k = step; k < 64; k += step)
                for (int i = 0; i < 64; i++)
                {
                    // x = k: the east edge of (k-1, i) and the west edge of (k, i); corners 0 SW, 1 SE, 2 NE, 3 NW
                    edge(k - 1, i, 1, 1, 2);
                    edge(k, i, 0, 0, 3);
                    // y = k: the north edge of (i, k-1) and the south edge of (i, k)
                    edge(i, k - 1, 3, 3, 2);
                    edge(i, k, 2, 0, 1);
                }
            return f;
        }

        /// <summary>
        /// The labels: each line, taken on the plane of the floor looked at (planeY) and drawn
        /// on far past the level, is cut to the view; an x line is labelled where it leaves the
        /// view nearest the bottom, a y line nearest the left.
        /// </summary>
        public static List<GridLabel> Labels(OrbitCamera cam, float width, float height, int step, float planeY)
        {
            var list = new List<GridLabel>();
            if (step < 1 || width < 2 || height < 2) return list;
            var vp = cam.View * cam.Projection(width / height);
            float y = planeY;
            // a little past the level, so a line is labelled at the view's edge when the level's
            // edge is not in sight, without the lines past the horizon crowding the edges
            const float far = 12f;
            var tg = cam.Target;
            for (int k = step; k < 64; k += step)
            {
                Vector2 a, b;
                if (Visible(vp, new Vector3(k, y, -far), new Vector3(k, y, 64 + far), width, height, out a, out b))
                {
                    var p = a.Y >= b.Y ? a : b;
                    list.Add(new GridLabel { Text = "X: " + k, X = p.X, Y = p.Y, IsX = true, Distance = Math.Abs(k - tg.X) });
                }
                if (Visible(vp, new Vector3(-far, y, k), new Vector3(64 + far, y, k), width, height, out a, out b))
                {
                    var p = a.X <= b.X ? a : b;
                    list.Add(new GridLabel { Text = "Y: " + k, X = p.X, Y = p.Y, IsX = false, Distance = Math.Abs(k - tg.Z) });
                }
            }
            list.Sort((p, q) => p.Distance.CompareTo(q.Distance));
            return list;
        }

        /// <summary>The part of a world segment that shows in the view, in pixels; false if none.</summary>
        public static bool Visible(Matrix4x4 vp, Vector3 p0, Vector3 p1, float width, float height, out Vector2 a, out Vector2 b)
        {
            a = b = Vector2.Zero;
            var c0 = Vector4.Transform(new Vector4(p0, 1), vp);
            var c1 = Vector4.Transform(new Vector4(p1, 1), vp);
            // cut at a plane just in front of the eye, so nothing behind it is projected
            const float near = 0.05f;
            if (c0.W < near && c1.W < near) return false;
            if (c0.W < near) c0 = Vector4.Lerp(c0, c1, (near - c0.W) / (c1.W - c0.W));
            else if (c1.W < near) c1 = Vector4.Lerp(c1, c0, (near - c1.W) / (c0.W - c1.W));
            Func<Vector4, Vector2> px = c => new Vector2((c.X / c.W * 0.5f + 0.5f) * width, (0.5f - c.Y / c.W * 0.5f) * height);
            Vector2 s0 = px(c0), s1 = px(c1);
            // Liang–Barsky against the view rectangle
            float t0 = 0, t1 = 1;
            var d = s1 - s0;
            float[] p = { -d.X, d.X, -d.Y, d.Y };
            float[] q = { s0.X, width - s0.X, s0.Y, height - s0.Y };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-6f) { if (q[i] < 0) return false; continue; }
                float r = q[i] / p[i];
                if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
            }
            a = s0 + d * t0;
            b = s0 + d * t1;
            return (b - a).LengthSquared() > 1f;
        }
    }
}
