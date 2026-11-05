using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Crossings and overlaps of the conversation graphs' layouts, over every conversation of
    /// both games. Node sizes are near the graph window's: 270 wide (230 for code), a line of
    /// about 42 characters 15 high.
    /// </summary>
    public class GraphLayoutQualityTests
    {
        readonly ITestOutputHelper output;
        public GraphLayoutQualityTests(ITestOutputHelper output) { this.output = output; }

        internal static ConversationGraph Build(ConversationLibrary lib, Conversation c)
        {
            var d = Decompiler.Decompile(c, lib.StringsOf(c).ToArray(), lib.Kind, false);
            var g = ConversationGraph.Build(c, d);
            var strings = lib.StringsOf(c);
            foreach (var n in g.Nodes)
            {
                n.Width = n.Kind == GraphNodeKind.Code ? 230 : 270;
                double h = 22;
                int statements = 0;
                foreach (var l in n.Lines)
                {
                    bool code = l.Kind == PseudoKind.Statement || l.Kind == PseudoKind.Menu && l.Indent < 2;
                    if (code && ++statements > 10) continue;
                    string t = l.StringId >= 0 && l.StringId < strings.Count ? strings[l.StringId] : l.Text;
                    int chars = code ? 36 : 40;
                    h += 15 * Math.Max(1, Math.Ceiling(Math.Min(t.Length, 320) / (double)chars)) + 3;
                }
                n.Height = Math.Max(30, h + 6);
            }
            return g;
        }

        static IEnumerable<Tuple<string, ConversationGraph>> All(string game)
        {
            string g = GameData.Game(game);
            if (g == null) yield break;
            var lib = ConversationLibrary.Open(GameSession.Open(g));
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                yield return Tuple.Create(slot + " " + (lib.Name(slot) ?? "?"), Build(lib, c));
            }
        }

        static readonly Tuple<string, LayeredOptions>[] Variants =
        {
            Tuple.Create("before", LayeredOptions.Before),
            Tuple.Create("+ 24 sweeps, best kept", new LayeredOptions { KeepBest = true, Transpose = false, TallBends = false, TurnLoops = false }),
            Tuple.Create("+ transpose", new LayeredOptions { TallBends = false, TurnLoops = false }),
            Tuple.Create("+ tall bends", new LayeredOptions { TurnLoops = false }),
            Tuple.Create("+ loops turned (now)", new LayeredOptions()),
        };

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Layered_layout_quality(string game)
        {
            var graphs = All(game).ToList();
            if (graphs.Count == 0) return;
            var totals = new GraphQuality[Variants.Length];
            var rows = new List<Tuple<string, int, GraphQuality, GraphQuality>>();
            foreach (var t in graphs)
            {
                GraphQuality first = new GraphQuality(), last = new GraphQuality();
                for (int v = 0; v < Variants.Length; v++)
                {
                    t.Item2.Layout(36, 64, Variants[v].Item2);
                    var q = GraphMetrics.Measure(t.Item2);
                    totals[v] += q;
                    if (v == 0) first = q;
                    last = q;
                    Assert.Equal(0, q.Overlaps);
                    Assert.All(t.Item2.Edges, e => Assert.True(e.Points.Count >= 2));
                }
                rows.Add(Tuple.Create(t.Item1, t.Item2.Visible.Count(), first, last));
            }
            for (int v = 0; v < Variants.Length; v++)
                output.WriteLine("{0} {1,-24} {2}  (drawing {3:0} x {4:0} in all)", game, Variants[v].Item1, totals[v], totals[v].Width, totals[v].Height);
            output.WriteLine("Most crossings before, before -> now:");
            foreach (var r in rows.OrderByDescending(r => r.Item3.Crossings).Take(12))
                output.WriteLine("  {0} ({1} nodes): {2} -> {3}", r.Item1, r.Item2, r.Item3, r.Item4);
            // the layout now crosses less than before
            Assert.True(totals[Variants.Length - 1].Crossings < totals[0].Crossings);
            Assert.True(totals[Variants.Length - 1].EdgesOverNodes < totals[0].EdgesOverNodes);
        }

        /// <summary>The hanging layout against the layered one, over every conversation (slow: only with UW_GRAPH_QUALITY=1).</summary>
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Hanging_layout_quality(string game)
        {
            if (Environment.GetEnvironmentVariable("UW_GRAPH_QUALITY") != "1") return;
            var graphs = All(game).ToList();
            if (graphs.Count == 0) return;
            var layered = new GraphQuality();
            string[] names = { "pushed either way", "pushed sideways, order kept", "+ springs at rest as laid out (now)" };
            var hanging = new GraphQuality[names.Length];
            int[] unsettled = new int[names.Length], steps = new int[names.Length];
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var rows = new List<Tuple<string, int, GraphQuality, GraphQuality, int>>();
            foreach (var t in graphs)
            {
                var g = t.Item2;
                for (int v = 0; v < names.Length; v++)
                {
                    g.Layout(36, 64);
                    var a = GraphMetrics.Measure(g);
                    if (v == 0) layered += a;
                    var h = new HangingLayout(g) { SidewaysOnly = v > 0, KeepOrder = v > 0, RestFromStart = v == 2 };
                    int s = h.Settle(3000);
                    steps[v] += s;
                    if (!h.Settled) unsettled[v]++;
                    var b = GraphMetrics.Measure(g);
                    hanging[v] += b;
                    if (v == names.Length - 1) rows.Add(Tuple.Create(t.Item1, g.Visible.Count(), a, b, s));
                }
            }
            output.WriteLine("{0} layered: {1}  (drawing {2:0} x {3:0} in all)", game, layered, layered.Width, layered.Height);
            for (int v = 0; v < names.Length; v++)
                output.WriteLine("{0} hanging, {1}: {2}  (drawing {3:0} x {4:0} in all); {5} steps, {6} not settled",
                    game, names[v], hanging[v], hanging[v].Width, hanging[v].Height, steps[v], unsettled[v]);
            output.WriteLine("{0:0.0} s", watch.Elapsed.TotalSeconds);
            foreach (var r in rows.OrderByDescending(r => r.Item2).Take(10))
                output.WriteLine("  {0} ({1} nodes, {4} steps): layered {2}; hanging {3}", r.Item1, r.Item2, r.Item3, r.Item4, r.Item5);
        }

        [Fact]
        public void Hanging_keeps_the_start_pinned_boxes_apart_and_a_moved_node_where_it_was_put()
        {
            string game = GameData.Game("UW"); if (game == null) return;
            var lib = ConversationLibrary.Open(GameSession.Open(game));
            foreach (int slot in new[] { 1, 2, 3 })   // Corby, Shak, Goldthirst
            {
                var g = Build(lib, lib.Archive.Read(slot));
                g.Layout(36, 64);
                var start = g.Visible.First();
                double sx = start.X, sy = start.Y;
                var h = new HangingLayout(g);
                Assert.True(g.StraightEdges);
                h.Settle();
                Assert.True(h.Settled, "conversation " + slot + " did not settle");
                Assert.Equal(sx, start.X); Assert.Equal(sy, start.Y);
                var q = GraphMetrics.Measure(g);
                Assert.Equal(0, q.Overlaps);
                Assert.All(g.Edges, e => Assert.True(e.Points.Count >= 2));
                // every edge's lower end hangs below its upper end
                foreach (var e in g.Edges.Where(e => !e.Shared && e.From != e.To && !e.Back && e.Bends.Count == 0))
                    Assert.True(e.To.Y >= e.From.Y + e.From.Height, e + " goes up in conversation " + slot);
                // a node moved by hand stays there, the rest settles round it
                var moved = g.Visible.Last();
                h.MoveTo(moved, moved.X + 400, moved.Y + 100);
                double mx = moved.X, my = moved.Y;
                Assert.True(h.IsPinned(moved));
                h.Settle();
                Assert.Equal(mx, moved.X); Assert.Equal(my, moved.Y);
                h.Unpin();
                Assert.False(h.IsPinned(moved));
                Assert.True(h.IsPinned(start));
                output.WriteLine("{0}: {1}", lib.Name(slot), q);
            }
            // back to layers: curves again
            var again = Build(lib, lib.Archive.Read(1));
            again.Layout(36, 64);
            new HangingLayout(again).Settle(50);
            again.Layout(36, 64);
            Assert.False(again.StraightEdges);
            Assert.DoesNotContain(again.Edges, e => e.Straight && e.From != e.To);
        }
    }
}
