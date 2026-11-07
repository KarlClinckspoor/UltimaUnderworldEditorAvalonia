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
    /// The structured Dialogue view (Decompiler, StructuredBuild.cs): every shipped
    /// conversation comes out as if / while / menu / switch with every statement once, the
    /// flat view's says, responds, menus, calls and assignments are all in it, and running
    /// the structured form says what the simulator running the bytecode says.
    /// </summary>
    public class ConversationStructureTests
    {
        readonly ITestOutputHelper output;
        public ConversationStructureTests(ITestOutputHelper output) { this.output = output; }

        /// <summary>The gotos the structure keeps (the scripts' own jumps between labelled parts): at most these.</summary>
        static int MaxGotos(string game) { return game == "UW2" ? 895 : 277; }

        [Theory]
        [InlineData("UW", 97)]
        [InlineData("UW2", 102)]
        public void Every_conversation_is_structured_with_every_statement_once_in_order(string game, int count)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int n = 0, gotos = 0, menus = 0, loops = 0, switches = 0;
            var problems = new List<string>();
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var d = lib.Decompile(c);
                n++;
                var r = d.Structure;
                Assert.NotNull(r);
                problems.AddRange(r.Problems.Select(p => slot + ": " + p));
                gotos += r.Gotos; menus += r.Menus; loops += r.Loops; switches += r.Switches;
                // the menus are all menu { } blocks
                Assert.DoesNotContain(d.Lines, l => l.Text.Contains("babl_menu(") || l.Text.Contains("babl_fmenu("));
                Assert.Equal(0, r.MenuFallbacks);
            }
            output.WriteLine("{0}: {1} conversations, {2} gotos, {3} menus, {4} loops, {5} switches", game, n, gotos, menus, loops, switches);
            foreach (var p in problems.Take(20)) output.WriteLine(p);
            Assert.Equal(count, n);
            Assert.Empty(problems);
            Assert.InRange(gotos, 0, MaxGotos(game));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_flat_views_says_menus_calls_and_assignments_are_all_in_the_structured_view(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var flat = lib.Decompile(c, false);
                var st = lib.Decompile(c);
                // say and respond: the same instructions, each once
                Func<DecompileResult, PseudoKind, List<int>> at = (d, k) => d.Lines.Where(l => l.Kind == k).Select(l => l.Address).OrderBy(a => a).ToList();
                Assert.Equal(at(flat, PseudoKind.Say), at(st, PseudoKind.Say));
                Assert.Equal(at(flat, PseudoKind.Respond), at(st, PseudoKind.Respond));
                // menus: the call's line in both
                var fm = flat.Lines.Where(l => l.Indent < 2 && (l.Text.Contains("babl_menu(") || l.Text.Contains("babl_fmenu("))).Select(l => l.Address).OrderBy(a => a).ToList();
                var sm = st.Lines.Where(l => l.Kind == PseudoKind.Menu && (l.Text.EndsWith("menu {"))).Select(l => l.Address).OrderBy(a => a).ToList();
                Assert.Equal(fm, sm);
                // every call and assignment the flat view shows is a line of the structured view, or inside one's instructions
                var lines = st.Lines.Where(l => l.Kind != PseudoKind.Comment && l.Kind != PseudoKind.Label && l.Kind != PseudoKind.Function).ToList();
                foreach (var l in flat.Lines)
                {
                    if (l.Kind != PseudoKind.Statement && !(l.Kind == PseudoKind.Menu && l.Indent < 2)) continue;
                    if (l.Text.StartsWith("if ") || l.Text.StartsWith("goto ") || l.Text.StartsWith("return") || l.Text == "end" || l.Text.StartsWith("word ")) continue;
                    Assert.True(lines.Any(x => x.Address == l.Address || x.Start >= 0 && x.Start <= l.Address && l.Address < x.End),
                        string.Format("{0} slot {1}: {2:X4} {3} is not in the structured view", game, slot, l.Address, l.Text));
                }
            }
        }

        static IEnumerable<Func<SimMenu, int, int>> Paths()
        {
            // the first choice, then the last when a menu comes round again (often "goodbye")
            yield return (m, seen) => seen == 0 ? 1 : m.Ids.Count;
            yield return (m, seen) => m.Ids.Count;
            yield return (m, seen) => seen < 2 ? Math.Min(m.Ids.Count, 2) : m.Ids.Count;
            yield return (m, seen) => seen < 3 ? (seen % m.Ids.Count) + 1 : m.Ids.Count;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_structured_form_says_what_the_bytecode_says_on_the_same_choices(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int runs = 0, lines = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var strings = lib.StringsOf(c);
                var world = SimWorld.FromGame(s, slot, null);
                var saved = SimWorld.SavedGlobals(s, slot);
                Func<int, int, string> other = (b, i) => s.Strings.Get(b, i);
                int p = 0;
                foreach (var path in Paths())
                {
                    p++;
                    var sim = new ConversationSimulator(c, strings, world, other, saved) { LogInstructions = false };
                    sim.RunToEnd(path, () => "yes", 300000);
                    var run = Decompiler.RunStructured(c, strings, world, path, () => "yes", other, saved, 300000);
                    string where = string.Format("{0} slot {1} path {2}", game, slot, p);
                    if (sim.State == SimState.Error || sim.State != SimState.Ended) continue;   // the bytecode itself stops: nothing to compare
                    Assert.True(run.State == SimState.Ended, where + ": the structured run did not end: " + run.Error);
                    var a = sim.Output.Select(o => o.Kind + " " + o.StringId + " " + o.Text).ToList();
                    var b = run.Output.Select(o => o.Kind + " " + o.StringId + " " + o.Text).ToList();
                    Assert.True(a.SequenceEqual(b), where + ": the outputs differ at line " + Enumerable.Range(0, Math.Min(a.Count, b.Count)).FirstOrDefault(k => a[k] != b[k])
                        + " (bytecode " + a.Count + " lines, structured " + b.Count + ")");
                    Assert.Equal(sim.EndReport, run.EndReport);
                    runs++;
                    lines += a.Count;
                }
            }
            output.WriteLine("{0}: {1} runs compared, {2} lines of output", game, runs, lines);
            Assert.True(runs > 0);
        }

        [Fact]
        public void Corbys_conversation_reads_as_if_else_and_a_menu_with_its_choices()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var lib = ConversationLibrary.Open(GameSession.Open(g));
            var d = lib.Decompile(lib.Archive.Read(1));
            var text = d.Text;
            Assert.Contains("if (met_before == 1) {", text);
            Assert.Contains("} else {", text);
            Assert.Contains("        menu {\n", text.Replace("\r", ""));
            Assert.Contains(d.Lines, l => l.Kind == PseudoKind.Menu && l.Text == "\"Of what torment dost thou speak?\" #2:");
            // a function that returns a value: return e, not the result local and a goto
            Assert.Contains("return 0;", text);
            Assert.Contains("return 1;", text);
            Assert.DoesNotContain("goto ", text);
            // every line keeps the instructions it shows
            var say = d.Lines.First(l => l.Kind == PseudoKind.Say);
            Assert.True(say.Start >= 0 && say.Start < say.Address && say.End == say.Address + 1);
        }
    }
}
