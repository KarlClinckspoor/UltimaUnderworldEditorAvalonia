using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// The conversation graph hung from its start, like a mobile: the start is pinned at the top
    /// and every other node hangs from it. Each node has a weight (more for a bigger box) that
    /// gravity pulls straight down; each edge is a spring from the bottom of its upper end to the
    /// top of its lower end, stiffest for plain flow, softer for conditions and answers, softer
    /// still for calls and weak for loops, at rest at the gap the layered layout gave it; the
    /// layout's points on long edges are light beads on the spring, so a long edge can bend
    /// round boxes. A box an edge passes through is pushed sideways off it (and the edge the
    /// other way). Each step moves the nodes by their damped speed, capped by a limit that cools
    /// step by step so the layout comes to rest; boxes are then moved apart sideways wherever
    /// they come closer than the gaps (as rectangles; two of one layer keep their order), and an
    /// edge's lower end down wherever it rises above its upper end. Nodes nothing leads to
    /// (functions only called from many places) hang from the top: they move sideways only.
    /// Started from the layered layout; edges are drawn straight.
    /// Measured over both games' conversations (GraphLayoutQualityTests), it crosses about twice
    /// as many edges as the layered layout and draws some over boxes: it is for moving nodes by
    /// hand, not for the tidiest picture.
    /// </summary>
    public sealed class HangingLayout
    {
        /// <summary>The downward pull on a unit of weight.</summary>
        public double Gravity = 0.02;
        /// <summary>The spring of a plain edge (the others are fractions of it).</summary>
        public double Stiffness = 0.08;
        /// <summary>How hard an edge and a box it passes through push each other sideways, per unit.</summary>
        public double EdgePush = 0.15;
        /// <summary>The share of its speed a node keeps from one step to the next.</summary>
        public double Damping = 0.8;
        /// <summary>The most a node moves in a step, at the start; the limit cools by Cooling a step.</summary>
        public double MaxSpeed = 30;
        public double Cooling = 0.993;
        /// <summary>The springs' length at rest is the gap between the ends in the layered layout it started from (else the vertical gap).</summary>
        public bool RestFromStart = true;
        /// <summary>Boxes too close are always moved apart sideways (so they keep their rows' order), unless one is an edge's end below the other.</summary>
        public bool SidewaysOnly = true;
        /// <summary>Two boxes of one layer of the layered layout it started from part in their order there (so they never pass each other).</summary>
        public bool KeepOrder = true;

        sealed class Spring { public int A, B; public double K, Rest, MinGap; }

        readonly ConversationGraph g;
        readonly GraphNode[] nodes;
        readonly Dictionary<GraphNode, int> index = new Dictionary<GraphNode, int>();
        readonly double[] vx, vy, mass, padX, padY;
        readonly bool[] hook;
        readonly List<Spring> springs = new List<Spring>();
        readonly HashSet<GraphNode> pinned = new HashSet<GraphNode>();
        readonly double vgap;
        double limit;
        bool built;

        /// <summary>The steps taken, and the largest move of the last one.</summary>
        public int Steps { get; private set; }
        public double LastMove { get; private set; }
        /// <summary>Nothing moves much any more.</summary>
        public bool Settled { get { return Steps > 0 && LastMove < 0.1; } }
        public ConversationGraph Graph { get { return g; } }
        /// <summary>The nodes held where they are: the start, and the ones dragged.</summary>
        public IEnumerable<GraphNode> Pinned { get { return pinned; } }

        /// <param name="g">A graph laid out in layers (ConversationGraph.Layout), whose places it starts from.</param>
        /// <param name="hgap">The gap kept between boxes side by side.</param>
        /// <param name="vgap">The gap between an edge's ends (the springs' rest length unless RestFromStart).</param>
        public HangingLayout(ConversationGraph g, double hgap = 36, double vgap = 64)
        {
            this.g = g;
            this.vgap = vgap;
            g.LeaveLayers();
            g.StraightEdges = true;
            nodes = g.Nodes.ToArray();
            int n = nodes.Length;
            vx = new double[n]; vy = new double[n]; mass = new double[n]; padX = new double[n]; padY = new double[n]; hook = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var v = nodes[i];
                index[v] = i;
                if (v.Dummy)
                {
                    // a bead: a point in the middle of where the layer had it
                    v.Y += v.Height / 2; v.Height = 0;
                    mass[i] = 0.4; padX[i] = 6; padY[i] = 10;
                }
                else
                {
                    mass[i] = 1 + v.Width * v.Height / (270.0 * 120.0);
                    padX[i] = hgap / 2; padY[i] = vgap / 4;
                }
            }
            var start = g.Visible.FirstOrDefault();
            if (start != null) pinned.Add(start);
            limit = MaxSpeed;
        }

        /// <summary>The springs, from the settings (on the first step).</summary>
        void Build()
        {
            built = true;
            var fed = new HashSet<GraphNode>();
            foreach (var e in g.Edges)
            {
                if (e.Shared || e.From == e.To) continue;
                var chain = new List<GraphNode>();
                chain.Add(e.Back ? e.To : e.From);
                chain.AddRange(e.Bends);
                chain.Add(e.Back ? e.From : e.To);
                double k = Stiffness * (e.Back ? 0.15 : e.Kind == GraphEdgeKind.Call ? 0.4 : e.Kind == GraphEdgeKind.Branch || e.Kind == GraphEdgeKind.Choice ? 0.8 : 1);
                for (int c = 1; c < chain.Count; c++)
                {
                    var a = chain[c - 1]; var b = chain[c];
                    bool bead = a.Dummy || b.Dummy;
                    double rest = bead ? vgap / 2 : vgap;
                    if (RestFromStart) rest = Math.Max(rest, b.Y - (a.Y + a.Height));
                    springs.Add(new Spring { A = index[a], B = index[b], K = k, Rest = rest, MinGap = bead ? vgap / 4 : vgap / 2 });
                }
                fed.Add(chain[chain.Count - 1]);
                foreach (var d in e.Bends) fed.Add(d);
            }
            for (int i = 0; i < nodes.Length; i++) hook[i] = !fed.Contains(nodes[i]);
        }

        public bool IsPinned(GraphNode n) { return pinned.Contains(n); }

        /// <summary>Starts moving again after a change (a node dragged or let go).</summary>
        public void Wake()
        {
            limit = Math.Max(limit, MaxSpeed / 2);
            LastMove = double.MaxValue;
        }

        /// <summary>Holds a node where it is (or lets it go; the start stays pinned).</summary>
        public void Pin(GraphNode n, bool on)
        {
            if (!index.ContainsKey(n)) return;
            if (on) pinned.Add(n);
            else if (n != g.Visible.FirstOrDefault()) pinned.Remove(n);
            vx[index[n]] = vy[index[n]] = 0;
            Wake();
        }

        /// <summary>Lets go of every node but the start.</summary>
        public void Unpin()
        {
            var start = g.Visible.FirstOrDefault();
            pinned.RemoveWhere(x => x != start);
            Wake();
        }

        /// <summary>Moves a node and holds it there, its edges following.</summary>
        public void MoveTo(GraphNode n, double x, double y)
        {
            if (!index.ContainsKey(n)) return;
            n.X = x; n.Y = y;
            pinned.Add(n);
            vx[index[n]] = vy[index[n]] = 0;
            Wake();
            g.Route();
        }

        /// <summary>Steps until nothing moves much, or at most this many. Returns the steps taken.</summary>
        public int Settle(int most = 3000)
        {
            int s = 0;
            while (s < most && !Settled) { One(); s++; }
            g.Route();
            return s;
        }

        /// <summary>Some steps, then the edges routed again. Returns the largest move of the last.</summary>
        public double Step(int count = 1)
        {
            if (nodes.Length == 0) return 0;
            for (int s = 0; s < count; s++) One();
            g.Route();
            return LastMove;
        }

        double[] fx, fy;
        int[] order;
        GraphNode[] boxes;
        double widest;

        void One()
        {
            int n = nodes.Length;
            if (n == 0) return;
            if (!built) Build();
            if (fx == null) { fx = new double[n]; fy = new double[n]; order = Enumerable.Range(0, n).ToArray(); }
            Array.Clear(fx, 0, n); Array.Clear(fy, 0, n);
            for (int i = 0; i < n; i++) fy[i] += mass[i] * Gravity;
            foreach (var sp in springs)
            {
                var a = nodes[sp.A]; var b = nodes[sp.B];
                double ax = a.CenterX, ay = a.Y + a.Height, bx = b.CenterX, by = b.Y;
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                double f = sp.K * (len - sp.Rest);
                double ux = dx / len * f, uy = dy / len * f;
                fx[sp.A] += ux; fy[sp.A] += uy; fx[sp.B] -= ux; fy[sp.B] -= uy;
            }
            EdgesOffBoxes();

            double most = 0;
            var oldX = new double[n]; var oldY = new double[n];
            for (int i = 0; i < n; i++)
            {
                var v = nodes[i];
                oldX[i] = v.X; oldY[i] = v.Y;
                if (pinned.Contains(v)) { vx[i] = vy[i] = 0; continue; }
                vx[i] = (vx[i] + fx[i] / mass[i]) * Damping;
                vy[i] = hook[i] ? 0 : (vy[i] + fy[i] / mass[i]) * Damping;
                double sp = Math.Sqrt(vx[i] * vx[i] + vy[i] * vy[i]);
                if (double.IsNaN(sp)) { vx[i] = vy[i] = 0; continue; }
                if (sp > limit) { vx[i] *= limit / sp; vy[i] *= limit / sp; }
                v.X += vx[i]; v.Y += vy[i];
            }
            for (int round = 0; round < 3; round++) { Separate(); KeepBelow(); }
            for (int i = 0; i < n; i++)
            {
                double mx = nodes[i].X - oldX[i], my = nodes[i].Y - oldY[i];
                most = Math.Max(most, Math.Sqrt(mx * mx + my * my));
            }
            limit = Math.Max(0.05, limit * Cooling);
            LastMove = most;
            Steps++;
        }

        /// <summary>How much of a move a node takes: none when pinned (or, up and down, hung from the top).</summary>
        double Share(int i, bool vertical)
        {
            if (pinned.Contains(nodes[i]) || vertical && hook[i]) return 0;
            return 1 / mass[i];
        }

        /// <summary>Moves two nodes apart by d (x or y), each by its share (the lighter more).</summary>
        void Part(int i, int j, double d, bool vertical)
        {
            double si = Share(i, vertical), sj = Share(j, vertical);
            if (si + sj <= 0) return;
            double mi = d * si / (si + sj), mj = d * sj / (si + sj);
            if (vertical) { nodes[i].Y -= mi; nodes[j].Y += mj; }
            else { nodes[i].X -= mi; nodes[j].X += mj; }
        }

        /// <summary>Boxes closer than the gaps are moved apart, sideways unless they mostly lie one over the other.</summary>
        void Separate()
        {
            int n = nodes.Length;
            Array.Sort(order, (p, q) => (nodes[p].X - padX[p]).CompareTo(nodes[q].X - padX[q]));
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                var a = nodes[i];
                double ar = a.X + a.Width + padX[i];
                for (int oj = oi + 1; oj < n; oj++)
                {
                    int j = order[oj];
                    var b = nodes[j];
                    if (b.X - padX[j] >= ar) break;
                    bool beads = a.Dummy && b.Dummy;
                    double px = beads ? 3 : Math.Min(padX[i], padX[j]), py = beads ? 5 : Math.Min(padY[i], padY[j]);
                    double ox = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X) + 2 * px;
                    double oy = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y) + 2 * py;
                    if (ox <= 0 || oy <= 0) continue;
                    if (SidewaysOnly || ox < oy * 1.5)
                    {
                        bool left = KeepOrder && a.Layer == b.Layer ? a.Order < b.Order : a.CenterX < b.CenterX || a.CenterX == b.CenterX && i < j;
                        if (left) Part(i, j, ox, false); else Part(j, i, ox, false);
                    }
                    else
                    {
                        double ca = a.Y + a.Height / 2, cb = b.Y + b.Height / 2;
                        bool above = ca < cb || ca == cb && i < j;
                        if (above) Part(i, j, oy, true); else Part(j, i, oy, true);
                    }
                }
            }
        }

        /// <summary>An edge's lower end goes below its upper end's bottom (by half the gap; a bead by a quarter).</summary>
        void KeepBelow()
        {
            foreach (var sp in springs)
            {
                var a = nodes[sp.A]; var b = nodes[sp.B];
                double over = a.Y + a.Height + sp.MinGap - b.Y;
                if (over > 0) Part(sp.A, sp.B, over, true);
            }
        }

        /// <summary>A box an edge's stretch passes through is pushed sideways off it, and the stretch's ends the other way.</summary>
        void EdgesOffBoxes()
        {
            const double pad = 10;
            if (boxes == null) { boxes = nodes.Where(x => !x.Dummy).ToArray(); widest = boxes.Select(x => x.Width).DefaultIfEmpty(0).Max(); }
            Array.Sort(boxes, (p, q) => p.X.CompareTo(q.X));
            foreach (var sp in springs)
            {
                var a = nodes[sp.A]; var b = nodes[sp.B];
                double ax = a.CenterX, ay = a.Y + a.Height, bx = b.CenterX, by = b.Y;
                if (by <= ay) continue;
                double x0 = Math.Min(ax, bx), x1 = Math.Max(ax, bx);
                // the first box that may reach x0
                int lo = 0, hi = boxes.Length;
                while (lo < hi) { int m = (lo + hi) / 2; if (boxes[m].X < x0 - pad - widest) lo = m + 1; else hi = m; }
                for (int bi = lo; bi < boxes.Length; bi++)
                {
                    var c = boxes[bi];
                    if (c.X - pad > x1) break;
                    if (c == a || c == b) continue;
                    double top = Math.Max(ay, c.Y), bottom = Math.Min(by, c.Y + c.Height);
                    if (bottom <= top || c.X - pad > x1 || c.X + c.Width + pad < x0) continue;
                    // where the stretch is beside the box's middle
                    double my = (top + bottom) / 2, t = (my - ay) / (by - ay);
                    double sx = ax + (bx - ax) * t;
                    double l = c.X - pad, r = c.X + c.Width + pad;
                    if (sx <= l || sx >= r) continue;
                    // off the nearer side
                    double d = sx - l < r - sx ? -(sx - l) : r - sx;  // the box moves by -d, the edge by +d
                    int ci = index[c];
                    double f = EdgePush * Math.Abs(d) * (bottom - top) / Math.Max(1, c.Height);
                    double sgn = Math.Sign(d);
                    fx[ci] -= sgn * f;
                    fx[sp.A] += sgn * f * (1 - t);
                    fx[sp.B] += sgn * f * t;
                }
            }
        }
    }
}
