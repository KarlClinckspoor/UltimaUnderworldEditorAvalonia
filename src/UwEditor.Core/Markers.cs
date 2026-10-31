using System;
using System.Collections.Generic;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>
    /// Solid markers drawn in the 3D view as coloured triangles (x, y, z, r, g, b, a per
    /// vertex, like the lines): arrows standing over the start and the player, and bullseyes on
    /// the squares traps and triggers act on.
    /// </summary>
    public static class Markers
    {
        public static readonly Vector4 Red = new Vector4(0.92f, 0.12f, 0.1f, 0.95f);
        public static readonly Vector4 Green = new Vector4(0.15f, 0.85f, 0.25f, 0.95f);
        public static readonly Vector4 White = new Vector4(0.97f, 0.97f, 0.95f, 0.95f);

        /// <summary>The arrow's height over the floor: its tip touches the floor, its tail is this high.</summary>
        public const float ArrowHeight = 1.5f;

        static void V(List<float> f, Vector3 p, Vector4 c)
        {
            f.Add(p.X); f.Add(p.Y); f.Add(p.Z); f.Add(c.X); f.Add(c.Y); f.Add(c.Z); f.Add(c.W);
        }

        static void Tri(List<float> f, Vector3 a, Vector3 b, Vector3 c, Vector4 col)
        {
            V(f, a, col); V(f, b, col); V(f, c, col);
        }

        static Vector4 Shade(Vector4 c, float k) { return new Vector4(c.X * k, c.Y * k, c.Z * k, c.W); }

        /// <summary>A large arrow pointing down at a point on the floor: an eight-sided shaft and a cone.</summary>
        public static void ArrowDown(List<float> f, Vector3 tip, Vector4 colour, float scale = 1f)
        {
            const int sides = 8;
            float headLen = 0.55f * scale, headR = 0.32f * scale, shaftR = 0.12f * scale, len = ArrowHeight * scale;
            var headBase = tip + new Vector3(0, headLen, 0);
            var top = tip + new Vector3(0, len, 0);
            for (int i = 0; i < sides; i++)
            {
                double a0 = 2 * Math.PI * i / sides, a1 = 2 * Math.PI * (i + 1) / sides;
                var d0 = new Vector3((float)Math.Cos(a0), 0, (float)Math.Sin(a0));
                var d1 = new Vector3((float)Math.Cos(a1), 0, (float)Math.Sin(a1));
                // light from the north-west so the sides read as round
                float k = 0.6f + 0.4f * (float)Math.Max(0, Math.Cos((a0 + a1) / 2 - 2.4));
                var c = Shade(colour, k);
                Tri(f, tip, headBase + d0 * headR, headBase + d1 * headR, c);
                Tri(f, headBase, headBase + d1 * headR, headBase + d0 * headR, Shade(colour, 0.45f));
                var b0 = headBase + d0 * shaftR; var b1 = headBase + d1 * shaftR;
                var t0 = top + d0 * shaftR; var t1 = top + d1 * shaftR;
                Tri(f, b0, b1, t1, c); Tri(f, b0, t1, t0, c);
                Tri(f, top, t0, t1, Shade(colour, 1.1f));
            }
        }

        /// <summary>A bullseye lying on the floor: rings of red and white round a point.</summary>
        public static void Bullseye(List<float> f, Vector3 centre, float radius = 0.48f, Vector4? colour = null)
        {
            var red = colour ?? Red;
            const int rings = 5, seg = 32;
            var p = centre + new Vector3(0, 0.03f, 0);
            for (int r = 0; r < rings; r++)
            {
                float r0 = radius * r / rings, r1 = radius * (r + 1) / rings;
                var c = (rings - 1 - r) % 2 == 0 ? red : White;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    var d0 = new Vector3((float)Math.Cos(a0), 0, (float)Math.Sin(a0));
                    var d1 = new Vector3((float)Math.Cos(a1), 0, (float)Math.Sin(a1));
                    if (r == 0) Tri(f, p, p + d0 * r1, p + d1 * r1, c);
                    else
                    {
                        Tri(f, p + d0 * r0, p + d0 * r1, p + d1 * r1, c);
                        Tri(f, p + d0 * r0, p + d1 * r1, p + d1 * r0, c);
                    }
                }
            }
        }

        /// <summary>A coloured sheet over a tile's floor (corner heights given), for the zone overlay.</summary>
        public static void TileSheet(List<float> f, int x, int y, float h00, float h10, float h01, float h11, Vector4 colour)
        {
            var a = new Vector3(x, h00, y); var b = new Vector3(x + 1, h10, y); var c = new Vector3(x, h01, y + 1); var d = new Vector3(x + 1, h11, y + 1);
            Tri(f, a, b, d, colour); Tri(f, a, d, c, colour);
        }

        /// <summary>A bullseye standing up, facing the camera's way, over a point: seen from any angle.</summary>
        public static void BullseyeStanding(List<float> f, Vector3 centre, Vector3 right, Vector3 up, float radius = 0.3f)
        {
            const int rings = 4, seg = 28;
            for (int r = 0; r < rings; r++)
            {
                float r0 = radius * r / rings, r1 = radius * (r + 1) / rings;
                var c = (rings - 1 - r) % 2 == 0 ? Red : White;
                for (int i = 0; i < seg; i++)
                {
                    double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
                    var d0 = right * (float)Math.Cos(a0) + up * (float)Math.Sin(a0);
                    var d1 = right * (float)Math.Cos(a1) + up * (float)Math.Sin(a1);
                    Tri(f, centre + d0 * r0, centre + d0 * r1, centre + d1 * r1, c);
                    if (r > 0) Tri(f, centre + d0 * r0, centre + d1 * r1, centre + d1 * r0, c);
                }
            }
        }
    }
}
