using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using UwEditor.Core.Conversations;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The conversation graph's two layouts, with the window's own node sizes.</summary>
    public class ConversationGraphWindowTests
    {
        readonly ITestOutputHelper output;
        public ConversationGraphWindowTests(ITestOutputHelper output) { this.output = output; }

        static string Uw1 { get { return GameData.Game("UW"); } }

        /// <summary>Where a test may leave a picture of a window (UWEDITOR_SHOTS), or null.</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        void Pictures(ConversationGraphWindow gw, string name)
        {
            gw.Canvas.Fit();
            Shot(gw, name + "-fit");
            gw.Canvas.Home();
            Shot(gw, name + "-start");
        }

        [AvaloniaFact]
        public void Shak_and_Goldthirst_in_layers_and_hanging_without_overlaps()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            foreach (var c in new[] { Tuple.Create(2, "shak"), Tuple.Create(3, "goldthirst") })
            {
                var gw = w.ShowConversationGraph(c.Item1);
                gw.Width = 1300; gw.Height = 860;
                gw.LayoutChoice = 0;
                Ui.Pump();
                Assert.NotNull(gw.Graph);
                // the layout before 2026-11-05, for comparison
                gw.Canvas.Options = LayeredOptions.Before;
                gw.Rebuild(false);
                Ui.Pump();
                var before = GraphMetrics.Measure(gw.Graph);
                Pictures(gw, c.Item2 + "-before");
                gw.Canvas.Options = null;
                gw.Rebuild(false);
                Ui.Pump();
                var layers = GraphMetrics.Measure(gw.Graph);
                Pictures(gw, c.Item2 + "-layers");
                Assert.Equal(0, layers.Overlaps);
                Assert.True(layers.Crossings < before.Crossings);
                Assert.True(layers.EdgesOverNodes <= before.EdgesOverNodes);

                gw.LayoutChoice = 1;
                Ui.Pump();
                var h = gw.Canvas.Hanging;
                Assert.NotNull(h);
                Assert.True(gw.Canvas.Moving);
                var start = gw.Graph.Visible.First();
                Assert.True(h.IsPinned(start));
                h.Settle();
                gw.Canvas.InvalidateVisual();
                var hanging = GraphMetrics.Measure(gw.Graph);
                Pictures(gw, c.Item2 + "-hanging");
                Assert.Equal(0, hanging.Overlaps);
                output.WriteLine("{0} ({1} nodes): before {2}; in layers {3}; hanging {4}", c.Item2, gw.Graph.Visible.Count(), before, layers, hanging);
            }
            w.Close();
        }

        [AvaloniaFact]
        public void A_dragged_node_stays_where_it_is_put_until_let_go_and_through_an_edit()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var gw = w.ShowConversationGraph(1);
            gw.Width = 1300; gw.Height = 860;
            gw.LayoutChoice = 1;
            Ui.Pump();
            var canvas = gw.Canvas;
            canvas.Hanging.Settle();
            var n = gw.Graph.Visible.Skip(1).First(x => x.Kind == GraphNodeKind.Menu);
            canvas.Select(n, true);
            Ui.Pump();
            double x0 = n.X, y0 = n.Y;
            var p = canvas.TranslatePoint(canvas.PointOf(n), gw).Value;
            gw.MouseMove(p);
            gw.MouseDown(p, MouseButton.Left);
            gw.MouseMove(p + new Vector(40, 20));
            gw.MouseMove(p + new Vector(120, 60));
            gw.MouseUp(p + new Vector(120, 60), MouseButton.Left);
            Ui.Pump();
            Assert.True(canvas.Hanging.IsPinned(n));
            Assert.Equal(x0 + 120 / canvas.Scale, n.X, 3);
            Assert.Equal(y0 + 60 / canvas.Scale, n.Y, 3);
            // the rest hangs round it; it stays
            canvas.Hanging.Settle();
            Assert.Equal(x0 + 120 / canvas.Scale, n.X, 3);
            // an edit builds the graph again: the node keeps its place and stays held
            gw.Rebuild(true);
            Ui.Pump();
            var again = gw.Graph.Visible.First(x => x.Address == n.Address);
            Assert.NotSame(n, again);
            Assert.True(gw.Canvas.Hanging.IsPinned(again));
            Assert.Equal(x0 + 120 / canvas.Scale, again.X, 3);
            // a right-click lets it go
            var q = canvas.TranslatePoint(canvas.PointOf(again), gw).Value;
            gw.MouseMove(q);
            gw.MouseDown(q, MouseButton.Right);
            gw.MouseUp(q, MouseButton.Right);
            Ui.Pump();
            Assert.False(gw.Canvas.Hanging.IsPinned(again));
            // back to layers: the drawing is the layered one again
            gw.LayoutChoice = 0;
            Ui.Pump();
            Assert.Null(gw.Canvas.Hanging);
            Assert.False(gw.Graph.StraightEdges);
            gw.Close();
            w.Close();
        }
    }
}
