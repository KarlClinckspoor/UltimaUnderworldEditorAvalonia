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

        /// <summary>
        /// The lines through the middles of tiles k·step (0 ≤ k·step &lt; 64), x = k + 0.5 and
        /// y = k + 0.5, so the line labelled 50 runs through tile 50. Each is drawn across the open
        /// tiles it crosses, on their floors (slopes included).
        /// </summary>
        public static List<float> Lines(LevelModel level, int step)
        {
            var f = new List<float>();
            if (step < 1) return f;
            for (int k = 0; k < 64; k += step)
                for (int i = 0; i < 64; i++)
                {
                    // x = k + 0.5 across tile (k, i), south to north
                    var t = level.Tiles[k, i];
                    if (!t.IsSolid)
                        LevelScene.Line(f,
                            new Vector3(k + 0.5f, (float)level.FloorAt(k, i, 0.5, 0.0) + Lift, i),
                            new Vector3(k + 0.5f, (float)level.FloorAt(k, i, 0.5, 1.0) + Lift, i + 1), Colour);
                    // y = k + 0.5 across tile (i, k), west to east
                    t = level.Tiles[i, k];
                    if (!t.IsSolid)
                        LevelScene.Line(f,
                            new Vector3(i, (float)level.FloorAt(i, k, 0.0, 0.5) + Lift, k + 0.5f),
                            new Vector3(i + 1, (float)level.FloorAt(i, k, 1.0, 0.5) + Lift, k + 0.5f), Colour);
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
            for (int k = 0; k < 64; k += step)
            {
                Vector2 a, b;
                float c = k + 0.5f;
                if (Visible(vp, new Vector3(c, y, -far), new Vector3(c, y, 64 + far), width, height, out a, out b))
                {
                    var p = a.Y >= b.Y ? a : b;
                    list.Add(new GridLabel { Text = "X: " + k, X = p.X, Y = p.Y, IsX = true, Distance = Math.Abs(c - tg.X) });
                }
                if (Visible(vp, new Vector3(-far, y, c), new Vector3(64 + far, y, c), width, height, out a, out b))
                {
                    var p = a.X <= b.X ? a : b;
                    list.Add(new GridLabel { Text = "Y: " + k, X = p.X, Y = p.Y, IsX = false, Distance = Math.Abs(c - tg.Z) });
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
