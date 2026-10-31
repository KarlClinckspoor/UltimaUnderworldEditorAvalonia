using System;
using System.Collections.Generic;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// A Glasbey palette (Glasbey, Heijden, Toh and Gray, "Colour displays for categorical images",
    /// 2007): each next colour is the one farthest in CIELAB from all chosen so far, over a grid of
    /// candidate RGB colours. Black, white and the magenta of unassigned tiles are taken from the
    /// start so no zone looks like rock, a highlight or a gap; very dark colours are left out.
    /// </summary>
    public static class Glasbey
    {
        static uint[] colors;

        /// <summary>254 colours (zones 2-255), as 0xFFRRGGBB.</summary>
        public static uint[] Colors { get { return colors ?? (colors = Make(254)); } }

        public static uint[] Make(int count)
        {
            const int steps = 32;
            var cand = new List<(uint rgb, double l, double a, double b)>();
            for (int r = 0; r < steps; r++)
                for (int g = 0; g < steps; g++)
                    for (int bl = 0; bl < steps; bl++)
                    {
                        int R = r * 255 / (steps - 1), G = g * 255 / (steps - 1), B = bl * 255 / (steps - 1);
                        double L, A, Bb;
                        Lab(R, G, B, out L, out A, out Bb);
                        if (L < 22) continue;   // too close to the rock's black
                        cand.Add(((uint)(R << 16 | G << 8 | B), L, A, Bb));
                    }
            var dist = new double[cand.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = double.MaxValue;
            Action<double, double, double> take = (l, a, b) =>
            {
                for (int i = 0; i < cand.Count; i++)
                {
                    var c = cand[i];
                    double d = (c.l - l) * (c.l - l) + (c.a - a) * (c.a - a) + (c.b - b) * (c.b - b);
                    if (d < dist[i]) dist[i] = d;
                }
            };
            foreach (var seed in new[] { (0, 0, 0), (255, 255, 255), (255, 0, 255) })
            {
                double L, A, B;
                Lab(seed.Item1, seed.Item2, seed.Item3, out L, out A, out B);
                take(L, A, B);
            }
            var result = new uint[count];
            for (int k = 0; k < count; k++)
            {
                int best = 0;
                for (int i = 1; i < cand.Count; i++) if (dist[i] > dist[best]) best = i;
                var c = cand[best];
                result[k] = 0xFF000000u | c.rgb;
                take(c.l, c.a, c.b);
            }
            return result;
        }

        static double Lin(int c) { double v = c / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double F(double t) { return t > 0.008856 ? Math.Pow(t, 1.0 / 3) : 7.787 * t + 16.0 / 116; }

        public static void Lab(int r, int g, int b, out double L, out double A, out double B)
        {
            double R = Lin(r), G = Lin(g), Bl = Lin(b);
            double x = (0.4124 * R + 0.3576 * G + 0.1805 * Bl) / 0.95047;
            double y = 0.2126 * R + 0.7152 * G + 0.0722 * Bl;
            double z = (0.0193 * R + 0.1192 * G + 0.9505 * Bl) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            L = 116 * fy - 16; A = 500 * (fx - fy); B = 200 * (fy - fz);
        }
    }
}
