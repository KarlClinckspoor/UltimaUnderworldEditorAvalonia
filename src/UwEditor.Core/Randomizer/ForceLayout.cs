using System;
using System.Collections.Generic;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The force-directed layout's settings: how hard links pull and nodes push.</summary>
    public sealed class ForceOptions
    {
        /// <summary>How hard a link pulls its two nodes together (1 the usual).</summary>
        public double Attraction = 1;
        /// <summary>How hard every two nodes push each other apart (1 the usual).</summary>
        public double Repulsion = 1;
        /// <summary>The pull of every node towards the middle of them all (per unit of distance), so the layout stays compact and parts with no links stay near.</summary>
        public double Gravity = 1.5;
        /// <summary>The ideal distance between linked nodes (canvas units).</summary>
        public double Length = 150;
        /// <summary>The most a node moves in one step.</summary>
        public double MaxStep = 30;
    }

    /// <summary>The layout's running state: the step length, which grows while the layout settles and shrinks when it overshoots.</summary>
    public sealed class ForceState
    {
        public double StepLength = -1;
        public double Energy = double.MaxValue;
        public int Progress;
        /// <summary>Starts over at full step (after a setting changed or a node was dragged).</summary>
        public void Reset() { StepLength = -1; Energy = double.MaxValue; Progress = 0; }
    }

    /// <summary>
    /// A force-directed layout in the manner of Fruchterman and Reingold: every two nodes push
    /// apart with Repulsion × k² / d, each link pulls its ends together with Attraction × d² / k
    /// (k the ideal length), and a gravity of Gravity × distance pulls everything to the middle of
    /// all the nodes (so moving them all together changes nothing); each step moves a
    /// node along the sum of its forces by the step length, which adapts as in Yifan Hu's method:
    /// it grows a little after five steps in a row that lowered the total force, and shrinks after
    /// one that did not, so the layout settles instead of shaking. Pinned nodes (one being
    /// dragged) stay.
    /// </summary>
    public static class ForceLayout
    {
        /// <summary>One step on the centres (x, y) inside width × height. Returns the largest move made.</summary>
        public static double Step(double[] x, double[] y, IList<(int A, int B)> links, bool[] pinned, ForceOptions o, double width, double height, ForceState state = null)
        {
            int n = x.Length;
            if (n == 0) return 0;
            o = o ?? new ForceOptions();
            double k = Math.Max(10, o.Length), k2 = k * k;
            var dx = new double[n]; var dy = new double[n];
            // repulsion between every two nodes
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double ex = x[i] - x[j], ey = y[i] - y[j];
                    double d2 = ex * ex + ey * ey;
                    if (d2 < 0.01)
                    {
                        // on top of each other: apart in a direction fixed by their numbers
                        double a = (i * 7919 + j * 104729) % 628 / 100.0;
                        ex = Math.Cos(a) * 0.1; ey = Math.Sin(a) * 0.1; d2 = 0.01;
                    }
                    double d = Math.Sqrt(d2);
                    double f = o.Repulsion * k2 / d;
                    double fx = ex / d * f, fy = ey / d * f;
                    dx[i] += fx; dy[i] += fy; dx[j] -= fx; dy[j] -= fy;
                }
            // attraction along the links
            foreach (var l in links)
            {
                if (l.A == l.B || l.A < 0 || l.B < 0 || l.A >= n || l.B >= n) continue;
                double ex = x[l.A] - x[l.B], ey = y[l.A] - y[l.B];
                double d = Math.Sqrt(ex * ex + ey * ey);
                if (d < 0.01) continue;
                double f = o.Attraction * d * d / k;
                double fx = ex / d * f, fy = ey / d * f;
                dx[l.A] -= fx; dy[l.A] -= fy; dx[l.B] += fx; dy[l.B] += fy;
            }
            // gravity towards the middle of them all
            double cx = 0, cy = 0, most = 0, energy = 0;
            for (int i = 0; i < n; i++) { cx += x[i]; cy += y[i]; }
            cx /= n; cy /= n;
            for (int i = 0; i < n; i++)
            {
                dx[i] += o.Gravity * (cx - x[i]);
                dy[i] += o.Gravity * (cy - y[i]);
                if (pinned == null || i >= pinned.Length || !pinned[i]) energy += dx[i] * dx[i] + dy[i] * dy[i];
            }
            // the step length (Hu's adaptive cooling)
            state = state ?? new ForceState();
            if (state.StepLength < 0) state.StepLength = o.MaxStep;
            else if (energy < state.Energy)
            {
                if (++state.Progress >= 5) { state.Progress = 0; state.StepLength = Math.Min(o.MaxStep, state.StepLength / 0.9); }
            }
            else { state.Progress = 0; state.StepLength *= 0.9; }
            state.Energy = energy;
            for (int i = 0; i < n; i++)
            {
                if (pinned != null && i < pinned.Length && pinned[i]) continue;
                double len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (len < 1e-9 || double.IsNaN(len)) continue;
                // a node whose forces nearly balance moves less than the full step
                double step = Math.Min(state.StepLength, len / 10);
                x[i] = Math.Max(0, Math.Min(width, x[i] + dx[i] / len * step));
                y[i] = Math.Max(0, Math.Min(height, y[i] + dy[i] / len * step));
                most = Math.Max(most, step);
            }
            return most;
        }

        /// <summary>A colour for a level's outline: distinct hues round the wheel (the golden angle), bright and saturated.</summary>
        public static uint LevelColor(int level)
        {
            if (level <= 0) return 0x808080;
            double h = ((level - 1) * 137.508) % 360;
            return Hsv(h, 0.85, 1.0);
        }

        static uint Hsv(double h, double s, double v)
        {
            double c = v * s, xx = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = xx; } else if (h < 120) { r = xx; g = c; } else if (h < 180) { g = c; b = xx; }
            else if (h < 240) { g = xx; b = c; } else if (h < 300) { r = xx; b = c; } else { r = c; b = xx; }
            return (uint)((byte)Math.Round((r + m) * 255) << 16 | (byte)Math.Round((g + m) * 255) << 8 | (byte)Math.Round((b + m) * 255));
        }
    }
}
