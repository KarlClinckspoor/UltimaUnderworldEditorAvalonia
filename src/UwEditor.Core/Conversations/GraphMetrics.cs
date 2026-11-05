using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Conversations
{
    /// <summary>How tidy a laid-out graph is: the fewer of each, the better.</summary>
    public struct GraphQuality
    {
        /// <summary>Pairs of drawn edges that cross (once or more).</summary>
        public int Crossings;
        /// <summary>Pairs of nodes whose boxes overlap.</summary>
        public int Overlaps;
        /// <summary>Edges drawn across a node that is neither of their ends (counted per edge and node).</summary>
        public int EdgesOverNodes;
        /// <summary>The drawing's size.</summary>
        public double Width, Height;

        public static GraphQuality operator +(GraphQuality a, GraphQuality b)
        {
            return new GraphQuality
            {
                Crossings = a.Crossings + b.Crossings, Overlaps = a.Overlaps + b.Overlaps, EdgesOverNodes = a.EdgesOverNodes + b.EdgesOverNodes,
                Width = a.Width + b.Width, Height = a.Height + b.Height,
            };
        }

        public override string ToString()
        {
            return Crossings + " crossings, " + Overlaps + " overlaps, " + EdgesOverNodes + " edges over nodes";
        }
    }

    /// <summary>
    /// Measures a laid-out ConversationGraph as the graph window draws it: an edge is a curve
    /// through its points (a cubic leaving and entering each point straight down), a loop goes
    /// round the side in straight lines, and the calls of a much-called function are not drawn.
    /// </summary>
    public static class GraphMetrics
    {
        /// <summary>The edge as the window draws it, flattened into a line through these points.</summary>
        public static List<GraphPoint> Path(GraphEdge e, int steps = 10)
        {
            var r = new List<GraphPoint>();
            if (e.Points.Count == 0) return r;
            r.Add(e.Points[0]);
            bool straight = e.Straight;
            for (int i = 1; i < e.Points.Count; i++)
            {
                var p0 = e.Points[i - 1]; var p1 = e.Points[i];
                if (straight) { r.Add(p1); continue; }
                double dy = (p1.Y - p0.Y) / 2;
                double c1x = p0.X, c1y = p0.Y + dy, c2x = p1.X, c2y = p1.Y - dy;
                for (int s = 1; s <= steps; s++)
                {
                    double t = (double)s / steps, u = 1 - t;
                    double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                    r.Add(new GraphPoint(a * p0.X + b * c1x + c * c2x + d * p1.X, a * p0.Y + b * c1y + c * c2y + d * p1.Y));
                }
            }
            return r;
        }

        public static GraphQuality Measure(ConversationGraph g)
        {
            var q = new GraphQuality { Width = g.Width - g.Left, Height = g.Height - g.Top };
            var nodes = g.Visible.ToList();
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var a = nodes[i]; var b = nodes[j];
                    if (a.X < b.X + b.Width - 0.5 && b.X < a.X + a.Width - 0.5 && a.Y < b.Y + b.Height - 0.5 && b.Y < a.Y + a.Height - 0.5) q.Overlaps++;
                }
            var edges = g.Edges.Where(e => !e.Shared && e.Points.Count >= 2).ToList();
            var paths = edges.Select(e => Path(e)).ToList();
            var boxes = paths.Select(Box).ToList();
            for (int i = 0; i < edges.Count; i++)
                for (int j = i + 1; j < edges.Count; j++)
                    if (Touch(boxes[i], boxes[j]) && Cross(paths[i], paths[j])) q.Crossings++;
            for (int i = 0; i < edges.Count; i++)
                foreach (var n in nodes)
                {
                    if (n == edges[i].From || n == edges[i].To) continue;
                    // a little inside the box, so an edge that only grazes it is not counted
                    double[] r = { n.X + 2, n.Y + 2, n.X + n.Width - 2, n.Y + n.Height - 2 };
                    if (r[2] <= r[0] || r[3] <= r[1] || !Touch(boxes[i], r)) continue;
                    if (Through(paths[i], r)) q.EdgesOverNodes++;
                }
            return q;
        }

        static double[] Box(List<GraphPoint> p)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var v in p) { x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y); }
            return new[] { x0, y0, x1, y1 };
        }

        static bool Touch(double[] a, double[] b) { return a[0] <= b[2] && b[0] <= a[2] && a[1] <= b[3] && b[1] <= a[3]; }

        static bool Cross(List<GraphPoint> a, List<GraphPoint> b)
        {
            for (int i = 1; i < a.Count; i++)
                for (int j = 1; j < b.Count; j++)
                    if (Proper(a[i - 1], a[i], b[j - 1], b[j])) return true;
            return false;
        }

        static double Side(GraphPoint a, GraphPoint b, GraphPoint c) { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }

        /// <summary>The two segments cross at a point inside both (touching or lying along each other is not crossing).</summary>
        static bool Proper(GraphPoint p1, GraphPoint p2, GraphPoint q1, GraphPoint q2)
        {
            if (Math.Max(p1.X, p2.X) < Math.Min(q1.X, q2.X) || Math.Max(q1.X, q2.X) < Math.Min(p1.X, p2.X)
                || Math.Max(p1.Y, p2.Y) < Math.Min(q1.Y, q2.Y) || Math.Max(q1.Y, q2.Y) < Math.Min(p1.Y, p2.Y)) return false;
            const double eps = 1e-6;
            double d1 = Side(q1, q2, p1), d2 = Side(q1, q2, p2), d3 = Side(p1, p2, q1), d4 = Side(p1, p2, q2);
            return (d1 > eps && d2 < -eps || d1 < -eps && d2 > eps) && (d3 > eps && d4 < -eps || d3 < -eps && d4 > eps);
        }

        /// <summary>The line goes into the rectangle (x0, y0, x1, y1).</summary>
        static bool Through(List<GraphPoint> p, double[] r)
        {
            foreach (var v in p) if (v.X > r[0] && v.X < r[2] && v.Y > r[1] && v.Y < r[3]) return true;
            var c = new[] { new GraphPoint(r[0], r[1]), new GraphPoint(r[2], r[1]), new GraphPoint(r[2], r[3]), new GraphPoint(r[0], r[3]) };
            for (int i = 1; i < p.Count; i++)
                for (int k = 0; k < 4; k++)
                    if (Proper(p[i - 1], p[i], c[k], c[(k + 1) % 4])) return true;
            return false;
        }
    }
}
