using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A point of a hand-drawn curve.</summary>
    public sealed class CurvePoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public CurvePoint() { }
        public CurvePoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// The shuffle's hand-made distributions: bars (one weight or chance per value) and curves
    /// through points. Curves are monotone cubic (Fritsch–Carlson), so they never swing above or
    /// below their points: a curve between two points of 0 and 10 stays within 0 and 10, and
    /// no chance can turn negative.
    /// </summary>
    public static class Distributions
    {
        /// <summary>Picks an index by weight; -1 when every weight is 0 or less.</summary>
        public static int Pick(Random rng, IList<double> weights)
        {
            double sum = 0;
            foreach (var w in weights) if (w > 0) sum += w;
            if (sum <= 0) return -1;
            double r = rng.NextDouble() * sum;
            for (int i = 0; i < weights.Count; i++) { if (weights[i] <= 0) continue; r -= weights[i]; if (r < 0) return i; }
            for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0) return i;
            return -1;
        }

        static double Phi(double z)
        {
            // Abramowitz and Stegun 7.1.26, |error| < 1.5e-7
            double t = 1 / (1 + 0.3275911 * Math.Abs(z) / Math.Sqrt(2));
            double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z / 2);
            return z >= 0 ? (1 + y) / 2 : (1 - y) / 2;
        }

        /// <summary>
        /// The chances of strengths 1..n that the sliders give (round(Normal(mean, sd)), kept
        /// within 1..n), as percentages: what the bars start from.
        /// </summary>
        public static double[] NormalBars(int n, double mean, double sd)
        {
            var b = new double[n];
            for (int k = 1; k <= n; k++)
            {
                double lo = k == 1 ? double.NegativeInfinity : k - 0.5, hi = k == n ? double.PositiveInfinity : k + 0.5;
                double p;
                if (sd <= 1e-9) p = (mean >= lo && mean < hi) ? 1 : 0;
                else p = (double.IsPositiveInfinity(hi) ? 1 : Phi((hi - mean) / sd)) - (double.IsNegativeInfinity(lo) ? 0 : Phi((lo - mean) / sd));
                b[k - 1] = Math.Round(p * 1000) / 10;
            }
            return b;
        }

        /// <summary>The curve's value at x (monotone cubic through the points, flat past the ends).</summary>
        public static double Eval(IList<CurvePoint> pts, double x)
        {
            if (pts == null || pts.Count == 0) return 0;
            var p = pts.OrderBy(q => q.X).ToList();
            if (p.Count == 1 || x <= p[0].X) return p[0].Y;
            if (x >= p[p.Count - 1].X) return p[p.Count - 1].Y;
            int n = p.Count;
            var d = new double[n - 1];
            for (int i = 0; i < n - 1; i++) { double h = p[i + 1].X - p[i].X; d[i] = h <= 0 ? 0 : (p[i + 1].Y - p[i].Y) / h; }
            var m = new double[n];
            m[0] = d[0]; m[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0 ? 0 : (d[i - 1] + d[i]) / 2;
            for (int i = 0; i < n - 1; i++)
            {
                if (d[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
                double a = m[i] / d[i], b = m[i + 1] / d[i], s = a * a + b * b;
                if (s > 9) { double t = 3 / Math.Sqrt(s); m[i] = t * a * d[i]; m[i + 1] = t * b * d[i]; }
            }
            int k = 0;
            while (k < n - 2 && x > p[k + 1].X) k++;
            double hk = p[k + 1].X - p[k].X;
            if (hk <= 0) return p[k].Y;
            double u = (x - p[k].X) / hk, u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * p[k].Y + (u3 - 2 * u2 + u) * hk * m[k] + (-2 * u3 + 3 * u2) * p[k + 1].Y + (u3 - u2) * hk * m[k + 1];
        }

        /// <summary>The curve read at each whole x from lo to hi, never below 0.</summary>
        public static double[] Sample(IList<CurvePoint> pts, int lo, int hi)
        {
            var r = new double[hi - lo + 1];
            for (int x = lo; x <= hi; x++) r[x - lo] = Math.Max(0, Eval(pts, x));
            return r;
        }

        /// <summary>A histogram as percentages of its largest bar (all 0 when empty).</summary>
        public static double[] Scaled(int[] counts)
        {
            int max = counts.Length == 0 ? 0 : counts.Max();
            return counts.Select(c => max == 0 ? 0 : Math.Round(100.0 * c / max, 1)).ToArray();
        }

        /// <summary>A curve following a histogram over lo..hi: points every step, each the mean of the bins around it, as percentages of the largest.</summary>
        public static List<CurvePoint> CurveOf(int[] counts, int lo, int step)
        {
            int hi = lo + counts.Length - 1;
            var ys = new List<(double x, double y)>();
            for (int x = lo; ; x += step)
            {
                if (x > hi) x = hi;
                int a = Math.Max(lo, x - step / 2), b = Math.Min(hi, x + step / 2);
                double s = 0; for (int i = a; i <= b; i++) s += counts[i - lo];
                ys.Add((x, s / (b - a + 1)));
                if (x == hi) break;
            }
            double max = ys.Max(t => t.y);
            return ys.Select(t => new CurvePoint(t.x, max <= 0 ? 50 : Math.Round(100 * t.y / max, 1))).ToList();
        }
    }

    /// <summary>The 64 spells objects can carry (STRINGS.PAK block 6 0x100 + spell) and their circles.</summary>
    public static class SpellTables
    {
        /// <summary>UW1: spells 0-47 six to a circle, 48-52 (Mass Paralyze, Acid, Local Teleport, Mana Boost, Restore Mana) have no runes (circle 9). UW2: eight to a circle (RUNES.C player_cast).</summary>
        public static int Circle(bool uw2, int spell) { return uw2 ? spell / 8 + 1 : spell < 48 ? spell / 6 + 1 : 9; }

        /// <summary>The bars of the circle widgets: UW1 has a ninth for the spells without runes.</summary>
        public static int Circles(bool uw2) { return uw2 ? 8 : 9; }

        public static string CircleLabel(bool uw2, int circle) { return !uw2 && circle == 9 ? "no runes" : circle.ToString(); }

        /// <summary>The named spells of a circle.</summary>
        public static List<int> InCircle(GameSession s, int circle)
        {
            var l = new List<int>();
            for (int sp = 0; sp < 64; sp++)
                if (Circle(s.IsUw2, sp) == circle && !string.IsNullOrWhiteSpace(s.Strings.Get(6, 0x100 + sp))) l.Add(sp);
            return l;
        }

        public static string Name(GameSession s, int spell) { return (s.Strings.Get(6, 0x100 + spell) ?? "spell " + spell).Trim(); }
    }
}
