using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The conversation compiler (ConversationCompiler.cs): every shipped conversation's
    /// Dialogue source compiles back to its bytes; compiled from scratch (no function copied)
    /// it gives the same bytes, or where the view hides a jump, a conversation the simulator
    /// runs the same; edits change only what was edited; mistakes are reported at their line.
    /// </summary>
    public class ConversationCompilerTests
    {
        readonly ITestOutputHelper output;
        public ConversationCompilerTests(ITestOutputHelper output) { this.output = output; }

        static IEnumerable<Func<SimMenu, int, int>> Paths()
        {
            // as ConversationStructureTests: the first choice, then the last when a menu comes round again
            yield return (m, seen) => seen == 0 ? 1 : m.Ids.Count;
            yield return (m, seen) => m.Ids.Count;
            yield return (m, seen) => seen < 2 ? Math.Min(m.Ids.Count, 2) : m.Ids.Count;
            yield return (m, seen) => seen < 3 ? (seen % m.Ids.Count) + 1 : m.Ids.Count;
        }

        static ConversationSimulator Run(GameSession s, Conversation c, IList<string> strings, Func<SimMenu, int, int> path)
        {
            var sim = new ConversationSimulator(c, strings, SimWorld.FromGame(s, c.Slot, null), (b, i) => s.Strings.Get(b, i), SimWorld.SavedGlobals(s, c.Slot)) { LogInstructions = false };
            sim.RunToEnd(path, () => "yes", 300000);
            return sim;
        }

        static List<string> Trace(ConversationSimulator sim) { return sim.Output.Select(o => o.Kind + " " + o.StringId + " " + o.Text).ToList(); }

        [Theory]
        [InlineData("UW", 97)]
        [InlineData("UW2", 102)]
        public void Every_conversation_compiles_back_from_its_source_to_the_same_bytes(string game, int count)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int n = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var r = lib.Compile(c, lib.Decompile(c).Text);
                Assert.True(r.Ok, game + " slot " + slot + ": " + string.Join("; ", r.Errors));
                Assert.True(r.Conversation.ToBytes().SequenceEqual(c.ToBytes()), game + " slot " + slot + ": the bytes differ");
                Assert.Empty(r.CompiledFunctions);
                Assert.Empty(r.AddedStrings);
                Assert.Empty(r.UnusedStrings);
                n++;
            }
            Assert.Equal(count, n);
        }

        /// <summary>
        /// The conversations whose source the compiler cannot compile from scratch, and why
        /// (they still compile back to their bytes, copied, while unchanged).
        /// </summary>
        static readonly Dictionary<string, Dictionary<int, string>> NotFromScratch = new Dictionary<string, Dictionary<int, string>>
        {
            { "UW", new Dictionary<int, string>
                {
                    { 6, "two words that are not instructions (0x0544 0x0033, likely a corrupted PUSHI 51) before a SAY: babl_run stops there, the view shows say <empty>" },
                    { 139, "the switch's case ends jump into the middle of the third case's compare (to its TSTEQ): the view shows a while on a temporary" },
                } },
            { "UW2", new Dictionary<int, string>() },
        };

        [Theory]
        [InlineData("UW", 26)]
        [InlineData("UW2", 38)]
        public void Compiled_from_scratch_every_conversation_gives_its_bytes_or_runs_the_same(string game, int minIdentical)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int identical = 0, equivalent = 0, functions = 0, sameFunctions = 0;
            var notCompiled = new Dictionary<int, string>();
            var reasons = new SortedDictionary<string, int>();
            var differ = new List<string>();
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var strings = lib.StringsOf(c);
                var r = lib.Compile(c, lib.Decompile(c).Text, new CompileOptions { CompileAll = true });
                if (!r.Ok) { notCompiled[slot] = r.Errors[0].ToString(); continue; }
                Assert.Empty(r.CopiedFunctions);
                Assert.Empty(r.AddedStrings);
                var a = FunctionCode(c); var b = FunctionCode(r.Conversation);
                Assert.Equal(a.Count, b.Count);
                for (int k = 0; k < a.Count; k++)
                {
                    functions++;
                    if (a[k] == b[k]) { sameFunctions++; continue; }
                    string why = Reason(a[k], b[k]);
                    int x; reasons.TryGetValue(why, out x); reasons[why] = x + 1;
                }
                if (r.Conversation.ToBytes().SequenceEqual(c.ToBytes())) { identical++; continue; }
                // not the same bytes: the same say / respond / menu trace on every path
                int p = 0;
                foreach (var path in Paths())
                {
                    p++;
                    var before = Run(s, c, strings, path);
                    var after = Run(s, r.Conversation, r.Strings, path);
                    var ta = Trace(before); var tb = Trace(after);
                    string where = string.Format("{0} slot {1} path {2}", game, slot, p);
                    if (before.State == SimState.Ended || before.State == SimState.Error)
                    {
                        if (before.State != after.State || !ta.SequenceEqual(tb) || !before.EndReport.SequenceEqual(after.EndReport)) differ.Add(where);
                    }
                    else
                    {
                        // the old code does not end within the steps (it loops): the new one neither, saying the same so far
                        int common = Math.Min(ta.Count, tb.Count);
                        if (after.State != before.State || !ta.Take(common).SequenceEqual(tb.Take(common))) differ.Add(where);
                    }
                }
                equivalent++;
            }
            output.WriteLine("{0}: {1} conversations with the same bytes, {2} running the same, {3} not compiled from scratch; functions {4} of {5} the same", game, identical, equivalent, notCompiled.Count, sameFunctions, functions);
            foreach (var kv in reasons) output.WriteLine("  {0,4}  {1}", kv.Value, kv.Key);
            foreach (var kv in notCompiled) output.WriteLine("  not compiled: slot {0}: {1} ({2})", kv.Key, NotFromScratch[game].TryGetValue(kv.Key, out var why2) ? why2 : "unexpected", kv.Value);
            Assert.Empty(differ);
            Assert.Equal(NotFromScratch[game].Keys.OrderBy(x => x), notCompiled.Keys.OrderBy(x => x));
            Assert.InRange(identical, minIdentical, 1000);
        }

        /// <summary>Each function's code with its jumps and calls made independent of where it sits.</summary>
        static List<string> FunctionCode(Conversation c)
        {
            var lay = BablLayout.Of(c);
            var starts = lay.Functions.ToList();
            var list = new List<string>();
            for (int k = 0; k < starts.Count; k++)
            {
                int st = starts[k], end = k + 1 < starts.Count ? starts[k + 1] : c.Code.Length;
                var sb = new StringBuilder();
                for (int i = lay.At[st]; i < lay.Code.Count && lay.Code[i].Address < end; i++)
                {
                    var ins = lay.Code[i];
                    if (!ins.Known) { sb.Append(".word ").Append(ins.Word).Append('\n'); continue; }
                    sb.Append(ins.Op);
                    if (ins.HasOperand)
                        sb.Append(' ').Append(ins.Op == BablOp.CALL ? "f" + starts.IndexOf(ins.Target) : ins.Op == BablOp.JMP ? "+" + (ins.Target - st) : ((short)ins.Operand).ToString());
                    sb.Append('\n');
                }
                list.Add(sb.ToString());
            }
            return list;
        }

        /// <summary>Why a function compiled from its source differs from the shipped one: at the first instruction (jump targets aside) that differs.</summary>
        static string Reason(string a, string b)
        {
            Func<string, string> op = x => x.StartsWith("JMP ") || x.StartsWith("BRA ") || x.StartsWith("BEQ ") || x.StartsWith("BNE ") ? x.Substring(0, 3) : x;
            var la = a.Split('\n'); var lb = b.Split('\n');
            int d = 0;
            while (d < la.Length && d < lb.Length && op(la[d]) == op(lb[d])) d++;
            string x0 = d < la.Length ? op(la[d]) : "", y0 = d < lb.Length ? op(lb[d]) : "";
            if (x0 == "JMP" && y0 == "BRA") return "if / else or while made with JMP where the view's construct is made with BRA";
            if (x0 == "BRA" && y0 == "JMP") return "a jump made with BRA where the view's construct is made with JMP";
            if (x0 == "JMP") return "a jump to where control goes anyway, or one that cannot run (the view does not show it)";
            if (x0 == "BRA") return "a goto or return made with BRA (the language's are JMPs)";
            if (x0 == "BEQ" && y0 == "NOT" || Opposite(x0, y0)) return "an if whose block is a goto, shown as if (!c) around what follows (the condition turned over)";
            if (x0.StartsWith("PUSHI_EFF") && y0.StartsWith("PUSHI_EFF")) return "a declared local after a temporary (the language puts the temporaries after the locals)";
            return "other: " + x0 + " / " + y0;
        }

        static bool Opposite(string a, string b)
        {
            var pairs = new[] { "TSTEQ TSTNE", "TSTLT TSTGE", "TSTGT TSTLE" };
            return pairs.Any(p => p == a + " " + b || p == b + " " + a);
        }

        // ----- edits -----

        static string Replace(string text, string what, string with)
        {
            Assert.Contains(what, text);
            int at = text.IndexOf(what, StringComparison.Ordinal);
            return text.Substring(0, at) + with + text.Substring(at + what.Length);
        }

        [Fact]
        public void Changing_a_line_of_text_adds_a_string_and_changes_only_its_function()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var c = lib.Archive.Read(1);
            var strings = lib.StringsOf(c);
            string text = Replace(lib.Decompile(c).Text, "say \"Hast thou come to add to my torment?\" #1;", "say \"Hast thou come to add to my sorrow?\" #1;");
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal(new[] { "hast_thou_come_to" }, r.CompiledFunctions);
            var added = Assert.Single(r.AddedStrings);
            Assert.Equal(strings.Count, added.Id);
            Assert.Equal("Hast thou come to add to my sorrow?", added.Text);
            Assert.Equal(strings.Count + 1, r.Strings.Count);
            // string 1 is no longer used: reported, kept
            Assert.Contains(r.UnusedStrings, u => u.Id == 1);
            Assert.Equal(strings[1], r.Strings[1]);
            // the same code but for the string's number
            Assert.Equal(c.Code.Length, r.Conversation.Code.Length);
            var changed = Enumerable.Range(0, c.Code.Length).Where(i => c.Code[i] != r.Conversation.Code[i]).ToList();
            Assert.Single(changed);
            Assert.Equal(added.Id, r.Conversation.Code[changed[0]]);
            // the simulator says the new line
            var sim = Run(s, r.Conversation, r.Strings, (m, seen) => m.Ids.Count);
            Assert.Contains(sim.Output, o => o.Kind == SimOutputKind.Say && o.Text == "Hast thou come to add to my sorrow?");
            Assert.DoesNotContain(sim.Output, o => o.Text == "Hast thou come to add to my torment?");

            // applied: the archive and STRINGS.PAK hold it (not saved)
            lib.Apply(r);
            Assert.True(lib.Archive.IsChanged(1));
            Assert.Equal("Hast thou come to add to my sorrow?", lib.StringsOf(lib.Archive.Read(1)).Last());
        }

        [Fact]
        public void Adding_a_menu_choice_shows_it_and_runs_its_statements()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var c = lib.Archive.Read(1);
            string text = Replace(lib.Decompile(c).Text,
                "            \"Indeed I have, thou mewling child!\" #5:\n                flee_and_end();\n",
                "            \"Indeed I have, thou mewling child!\" #5:\n                flee_and_end();\n            \"Who art thou?\":\n                say \"I am Corby, and my torment is my own.\";\n");
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal(new[] { "hast_thou_come_to" }, r.CompiledFunctions);
            Assert.Equal(2, r.AddedStrings.Count);
            int who = r.AddedStrings.Single(x => x.Text == "Who art thou?").Id;
            // the fifth choice, picked
            var sim = Run(s, r.Conversation, r.Strings, (m, seen) => m.Ids.Count);
            Assert.True(sim.State == SimState.Ended, sim.Error);
            Assert.Contains(sim.Output, o => o.Kind == SimOutputKind.Say && o.Text == "I am Corby, and my torment is my own.");
            Assert.Contains(sim.Output, o => o.StringId == who);
            // the old choices still do what they did
            var before = Run(s, c, lib.StringsOf(c), (m, seen) => 1);
            var after = Run(s, r.Conversation, r.Strings, (m, seen) => 1);
            Assert.Equal(Trace(before).Where(x => !x.StartsWith("Choice")), Trace(after).Where(x => !x.StartsWith("Choice")));
        }

        [Fact]
        public void Adding_an_if_runs_it_and_moves_the_functions_after_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            var c = lib.Archive.Read(1);
            string text = Replace(lib.Decompile(c).Text, "main() {\n    global33 = 0;\n",
                "main() {\n    global33 = 0;\n    if (global33 == 0 && play_hp > -1) {\n        say \"Thou hast found me.\";\n    } else {\n        exit;\n    }\n");
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal(new[] { "main" }, r.CompiledFunctions);
            Assert.True(r.Conversation.Code.Length > c.Code.Length);
            // main first, then every other function as it was, moved
            var a = FunctionCode(c); var b = FunctionCode(r.Conversation);
            Assert.Equal(a.Count, b.Count);
            for (int k = 1; k < a.Count; k++) Assert.Equal(a[k], b[k]);
            var sim = Run(s, r.Conversation, r.Strings, (m, seen) => seen == 0 ? 1 : m.Ids.Count);
            Assert.True(sim.State == SimState.Ended, sim.Error);
            Assert.Equal("Thou hast found me.", sim.Output.First(o => o.Kind == SimOutputKind.Say).Text);
            // after it, the conversation as before
            var before = Trace(Run(s, c, lib.StringsOf(c), (m, seen) => seen == 0 ? 1 : m.Ids.Count));
            Assert.Equal(before, Trace(sim).Skip(1));
        }

        [Fact]
        public void Expressions_follow_the_precedence_and_code_as_Origins_compiler()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var lib = ConversationLibrary.Open(GameSession.Open(g));
            var c = lib.Archive.Read(1);
            string text = Replace(lib.Decompile(c).Text, "main() {\n    global33 = 0;\n", "main() {\n    global33 = 2 + 3 * 4 - -5;\n    global0 = !(global33 < 1) || get_quest(4) == 1 && 0;\n");
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            string asm = string.Join(" ", r.Assembly.Split('\n').Select(l => l.Split(';')[0].Trim()).Where(l => l.Length > 0 && !l.StartsWith(".") && !l.EndsWith(":")).Select(l => string.Join(" ", l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))));
            // UW1 writes -5 as PUSHI 5 NEG
            Assert.Contains("PUSHI 33 PUSHI 2 PUSHI 3 PUSHI 4 MUL ADD PUSHI 5 NEG SUB STO", asm);
            // a constant argument goes into a temporary; && binds tighter than ||
            Assert.Contains("PUSHI 0 PUSHI 33 FETCHM PUSHI 1 TSTLT NOT PUSHI 4 PUSHI_EFF 1 SWAP STO PUSHI_EFF 1 PUSHI 1 CALLI 15 POP POP PUSH_REG PUSHI 1 TSTEQ PUSHI 0 AND OR STO", asm);
        }

        // ----- mistakes -----

        CompileResult CompileCorby(Func<string, string> edit, out ConversationLibrary lib)
        {
            lib = null;
            string g = GameData.Game("UW"); if (g == null) return null;
            lib = ConversationLibrary.Open(GameSession.Open(g));
            var c = lib.Archive.Read(1);
            return lib.Compile(c, edit(lib.Decompile(c).Text));
        }

        static SourceError Only(CompileResult r)
        {
            Assert.False(r.Ok);
            Assert.Null(r.Conversation);
            return Assert.Single(r.Errors);
        }

        static int LineOf(string text, string what)
        {
            int at = text.IndexOf(what, StringComparison.Ordinal);
            return text.Substring(0, at).Count(ch => ch == '\n') + 1;
        }

        [Fact]
        public void An_unknown_name_is_an_error_at_its_line_and_column()
        {
            string text = null;
            var r = CompileCorby(t => text = Replace(t, "    met_before = 1;\n", "    met_befor = 1;\n"), out _);
            if (r == null) return;
            var e = Only(r);
            Assert.Equal(LineOf(text, "met_befor = 1;"), e.Line);
            Assert.Equal(5, e.Column);
            Assert.Contains("unknown name met_befor", e.Message);
        }

        [Fact]
        public void A_syntax_error_is_reported_where_it_is()
        {
            string text = null;
            var r = CompileCorby(t => text = Replace(t, "    met_before = 1;\n", "    met_before = (1;\n"), out _);
            if (r == null) return;
            var e = Only(r);
            Assert.Equal(LineOf(text, "met_before = (1;"), e.Line);
            Assert.Equal(20, e.Column);
            Assert.Contains("expected ')'", e.Message);
        }

        [Fact]
        public void A_built_in_with_the_wrong_number_of_arguments_is_an_error()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    met_before = get_quest(4, 5, 6);\n"), out _);
            if (r == null) return;
            Assert.Contains("get_quest takes 1 to 2 arguments; this call passes 3", Only(r).Message);
            r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    met_before = sex(\"lad\");\n"), out _);
            Assert.Contains("sex takes 2 arguments; this call passes 1", Only(r).Message);
        }

        [Fact]
        public void Assigning_to_an_imported_function_is_an_error()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    get_quest = 1;\n"), out _);
            if (r == null) return;
            Assert.Contains("cannot assign to get_quest: it is an imported function", Only(r).Message);
        }

        [Fact]
        public void A_global_beyond_the_variable_count_is_an_error()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    global34 = 1;\n"), out _);
            if (r == null) return;
            Assert.Contains("beyond the conversation's 34 variables", Only(r).Message);
        }

        [Fact]
        public void A_menu_with_too_many_choices_is_an_error()
        {
            var sb = new StringBuilder("    menu {\n");
            for (int k = 1; k <= 10; k++) sb.Append("        \"Choice ").Append(k).Append("\": {}\n");
            sb.Append("    }\n");
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", sb.ToString()), out _);
            if (r == null) return;
            Assert.Contains("a menu shows at most 9 choices", Only(r).Message);
        }

        [Fact]
        public void Break_outside_a_loop_and_a_goto_to_no_label_are_errors()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    break;\n    goto nowhere;\n"), out _);
            if (r == null) return;
            Assert.Equal(2, r.Errors.Count);
            Assert.Contains("break outside a loop", r.Errors[0].Message);
            Assert.Contains("there is no label nowhere", r.Errors[1].Message);
        }

        [Fact]
        public void A_conversation_too_big_for_the_games_buffer_is_an_error()
        {
            var sb = new StringBuilder();
            for (int k = 0; k < 3000; k++) sb.Append("    say \"Torment.\";\n");
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", sb.ToString()), out _);
            if (r == null) return;
            Assert.Contains("the game reads at most 16384 (0x4000)", Only(r).Message);
        }

        [Fact]
        public void An_unchanged_function_calling_one_that_was_removed_is_an_error()
        {
            string text = null;
            var r = CompileCorby(t =>
            {
                int a = t.IndexOf("\nflee_and_end() {", StringComparison.Ordinal), b = t.IndexOf("\n}\n", a, StringComparison.Ordinal);
                return text = t.Substring(0, a) + t.Substring(b + 2);
            }, out _);
            if (r == null) return;
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Message.Contains("flee_and_end") && e.Message.Contains("no longer has"));
        }

        [Fact]
        public void A_string_number_beyond_the_block_is_an_error()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    say #9999;\n"), out _);
            if (r == null) return;
            Assert.Contains("there is no string #9999", Only(r).Message);
        }

        [Fact]
        public void A_negative_number_below_a_word_is_an_error()
        {
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    met_before = -65535;\n"), out _);
            if (r == null) return;
            Assert.Contains("-65535 does not fit in a word", Only(r).Message);
            r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    met_before = -32768;\n"), out _);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
        }

        [Fact]
        public void Labels_of_different_functions_never_share_an_assembly_name()
        {
            // "L_" + function + "_" + label once made zz_a's b_c and zz_a_b's c the same word
            var r = CompileCorby(t => t + "\nzz_a() {\nb_c:\n    return;\n}\n\nzz_a_b() {\nc:\n    return;\n}\n", out _);
            if (r == null) return;
            Assert.True(r.Ok, string.Join("; ", r.Errors));
        }

        [Fact]
        public void An_answered_menu_reads_back_from_its_own_view()
        {
            var r = CompileCorby(t => Replace(t, "main() {\n", "main() {\n    int pick;\n    pick = menu { \"One.\"; \"Two.\"; };\n    global33 = pick;\n"), out var lib);
            if (r == null) return;
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            lib.Apply(r);
            var c = lib.Archive.Read(1);
            string view = lib.Decompile(c).Text;
            Assert.Contains("= menu {", view);
            var again = lib.Compile(c, view);
            Assert.True(again.Ok, string.Join("; ", again.Errors) + "\n" + view.Substring(0, Math.Min(view.Length, 600)));
            Assert.True(again.Conversation.ToBytes().SequenceEqual(c.ToBytes()));
        }

        [Fact]
        public void A_bare_return_in_a_function_returning_a_value_reads_back_as_return()
        {
            // return; jumps past the exit block to the epilogue: once read as "goto", with the "return 5" lost
            var r = CompileCorby(t => Replace(t, "    met_before = 1;\n", "    met_before = zz_pick(2);\n")
                + "\nzz_pick(a) {\n    if (a == 1) {\n        return;\n    }\n    return 5;\n}\n", out var lib);
            if (r == null) return;
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            lib.Apply(r);
            var c = lib.Archive.Read(1);
            string view = lib.Decompile(c).Text;
            // lib.Apply keeps no names: the function is called what its address makes it
            string name = System.Text.RegularExpressions.Regex.Match(view, @"met_before = (\w+)\(2\);").Groups[1].Value;
            int at = view.IndexOf("\n" + name + "(", StringComparison.Ordinal);
            Assert.True(name.Length > 0 && at >= 0, view);
            string fn = view.Substring(at + 1);
            Assert.Contains("return;", fn);
            Assert.Contains("return 5;", fn);
            Assert.DoesNotContain("goto", fn.Substring(0, fn.IndexOf("\n}", StringComparison.Ordinal)));
            var again = lib.Compile(c, view);
            Assert.True(again.Ok, string.Join("; ", again.Errors));
            Assert.True(again.Conversation.ToBytes().SequenceEqual(c.ToBytes()));
        }

        [Fact]
        public void The_parser_reads_every_construct_of_the_language()
        {
            var errors = new List<SourceError>();
            var p = ConversationSource.Parse(
                "f(arg2, arg1) {    // 3 locals\n" +
                "    int a, b[2];\n" +
                "    a = -arg1[2] + *arg2 % 3;\n" +
                "    b[1] = streq(\"x\" #4, #5) != 0;\n" +
                "    while (true) {\n        if (a) goto out; else_label:\n        continue;\n    }\n" +
                "out:\n" +
                "    switch (a) {\n    case -1: {}\n    case 2:\n        exit;\n    default:\n        a = 1;\n    }\n" +
                "    fmenu {\n        \"one\" #1 if (a):\n            say \"x\";\n        \"two\" #2;\n        default:\n            respond 3;\n    }\n" +
                "    a = menu { \"a\"; \"b\"; };\n" +
                "    g(&a, b, ptr(a), @FFFF());\n" +
                "    word 0x0544;\n" +
                "    return a;\n" +
                "}\n", errors);
            Assert.Empty(errors);
            var f = Assert.Single(p.Functions);
            Assert.Equal(new[] { "arg2", "arg1" }, f.Params.Select(x => x.Name));
            Assert.Equal(2, f.Locals[1].Size);
            Assert.Equal(10, f.Body.Count);
            var sw = (SwitchS)f.Body[4];
            Assert.Equal(0xFFFF, sw.Cases[0].Value);
            var fm = (MenuS)f.Body[5];
            Assert.True(fm.Filtered);
            Assert.NotNull(fm.Choices[0].Flag);
            Assert.Null(fm.Choices[1].Body);
            Assert.NotNull(fm.Default);
        }
    }
}
