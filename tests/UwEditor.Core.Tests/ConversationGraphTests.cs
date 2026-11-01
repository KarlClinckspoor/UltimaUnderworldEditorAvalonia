using System;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class ConversationGraphTests
    {
        readonly ITestOutputHelper output;
        public ConversationGraphTests(ITestOutputHelper output) { this.output = output; }

        internal static ConversationGraph Graph(ConversationLibrary lib, Conversation c)
        {
            var d = Decompiler.Decompile(c, lib.StringsOf(c).ToArray(), lib.Kind, false);
            var g = ConversationGraph.Build(c, d);
            foreach (var n in g.Nodes) { n.Width = 240; n.Height = 24 + 16 * n.Lines.Count; }
            g.Layout();
            return g;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_graph_is_built_and_laid_out_for_every_conversation(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int nodes = 0, edges = 0, choices = 0, backs = 0, says = 0, convs = 0, biggest = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var graph = Graph(lib, c);
                var visible = graph.Visible.ToList();
                Assert.NotEmpty(visible);
                Assert.NotNull(graph.NodeAt(0));
                Assert.Equal(0, visible[0].Address);
                convs++;
                nodes += visible.Count;
                biggest = Math.Max(biggest, visible.Count);
                edges += graph.Edges.Count;
                choices += graph.Edges.Count(e => e.Kind == GraphEdgeKind.Choice);
                backs += graph.Edges.Count(e => e.Back);
                says += visible.Count(n => n.Kind == GraphNodeKind.Say || n.Kind == GraphNodeKind.Menu);
                // nodes of a layer do not overlap, and every edge has points
                foreach (var layer in visible.GroupBy(n => n.Layer))
                {
                    var row = layer.OrderBy(n => n.X).ToList();
                    for (int i = 1; i < row.Count; i++) Assert.True(row[i].X >= row[i - 1].X + row[i - 1].Width - 0.01, "overlap in conversation " + slot);
                }
                Assert.All(graph.Edges, e => Assert.True(e.Points.Count >= 2));
                // every string a node shows is one of the conversation's
                int count = lib.StringsOf(c).Count;
                Assert.All(visible.SelectMany(n => n.Lines).Where(l => l.StringId >= 0), l => Assert.InRange(l.StringId, 0, count - 1));
            }
            output.WriteLine("{0}: {1} conversations, {2} nodes (largest {3}), {4} edges, {5} answers, {6} loops, {7} say or menu nodes", game, convs, nodes, biggest, edges, choices, backs, says);
            Assert.True(choices > 100);
        }

        [Fact]
        public void Corby_starts_with_his_line_and_a_menu_whose_answers_label_the_edges()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var graph = Graph(lib, lib.Archive.Read(1));
            var first = graph.Visible.First(n => n.Lines.Any(l => l.Kind == PseudoKind.Say));
            Assert.Contains(first.Lines, l => l.StringId == 1);
            var menu = graph.Visible.First(n => n.Kind == GraphNodeKind.Menu && n.Lines.Any(l => l.StringId == 2));
            Assert.Contains(graph.Edges, e => e.From == menu && e.Kind == GraphEdgeKind.Choice && e.Choice.StartsWith("1: \"Of what torment") && e.Label == "local22 == 1");
            Assert.Same(menu, graph.NodeAt(menu.Address));
        }
    }
}
