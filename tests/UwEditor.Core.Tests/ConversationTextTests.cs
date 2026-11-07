using System;
using System.Linq;
using UwEditor.Core.Conversations;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The conversation language as editor text (ConversationText), the built-ins' descriptions, and the library's undo states and shipped copies.</summary>
    public class ConversationTextTests
    {
        const string Sample =
            "// 3 variables\n" +
            "\n" +
            "main() {\n" +
            "    greet();\n" +
            "    end;\n" +
            "}\n" +
            "\n" +
            "greet(arg1) {    // 2 locals\n" +
            "    int answer, count;\n" +
            "    say \"Hi, \\\"friend\\\" // not a comment\" #3;\n" +
            "again:\n" +
            "    answer = menu { \"a\" #4; \"b\"; };\n" +
            "    if (answer == 1) goto again;\n" +
            "    count = get_quest(4) + 0x1F - @02B5();\n" +
            "}\n" +
            "\n" +
            "unused() {    // never called\n" +
            "    int answer;\n" +
            "    answer = 2;\n" +
            "}\n";

        [Fact]
        public void A_line_scans_into_the_lexers_tokens_with_comments()
        {
            var t = ConversationText.ScanLine("    say \"Hi, \\\"friend\\\" // not a comment\" #3; // yes");
            Assert.Equal(new[] { SpanKind.Keyword, SpanKind.String, SpanKind.StringId, SpanKind.Punct, SpanKind.Comment }, t.Select(x => x.Kind));
            Assert.Equal("Hi, \"friend\" // not a comment", ConversationText.Unquote(t[1].Text));
            Assert.Equal(4, t[0].Start);
            Assert.Equal("// yes", t[4].Text);
            var u = ConversationText.ScanLine("x = 0x1F - @02B5 + 9a <= café;");
            Assert.Equal(new[] { SpanKind.Name, SpanKind.Punct, SpanKind.Number, SpanKind.Punct, SpanKind.Address, SpanKind.Punct, SpanKind.Bad, SpanKind.Punct, SpanKind.Name, SpanKind.Punct }, u.Select(x => x.Kind));
            Assert.Equal("<=", u[7].Text);
            // the sample is the language: the parser reads it
            var errors = new System.Collections.Generic.List<SourceError>();
            var prog = ConversationSource.Parse(Sample, errors);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            Assert.Equal(3, prog.Functions.Count);
            Assert.Equal(10, ConversationText.LineOf(Sample, Sample.IndexOf("say")));
        }

        [Fact]
        public void Functions_definitions_and_uses_are_found_in_the_text()
        {
            var fns = ConversationText.Functions(Sample);
            Assert.Equal(new[] { "main", "greet", "unused" }, fns.Select(f => f.Name));
            Assert.Equal(new[] { 3, 8, 17 }, fns.Select(f => f.Line));
            Assert.Equal(new[] { false, false, true }, fns.Select(f => f.NeverCalled));
            Assert.Equal(fns[1].Start, fns[0].End);
            Assert.Equal(Sample.Length, fns[2].End);
            Assert.Equal("greet", ConversationText.FunctionAt(Sample, Sample.IndexOf("again;")).Name);

            // a call goes to the function, a goto to its label, a local to its declaration in that function
            int call = Sample.IndexOf("greet();");
            Assert.Equal(fns[1].Start, ConversationText.DefinitionOf(Sample, "greet", call));
            Assert.Equal(Sample.IndexOf("again:"), ConversationText.DefinitionOf(Sample, "again", Sample.IndexOf("goto again") + 6));
            Assert.Equal(Sample.IndexOf("answer, count"), ConversationText.DefinitionOf(Sample, "answer", Sample.IndexOf("answer == 1")));
            Assert.Equal(Sample.IndexOf("arg1"), ConversationText.DefinitionOf(Sample, "arg1", Sample.IndexOf("answer == 1")));
            int unusedLocal = Sample.IndexOf("int answer;") + 4;
            Assert.Equal(unusedLocal, ConversationText.DefinitionOf(Sample, "answer", Sample.IndexOf("answer = 2")));
            Assert.Equal(-1, ConversationText.DefinitionOf(Sample, "get_quest", Sample.IndexOf("get_quest")));

            // uses: names only, not inside strings or comments; a function's own
            Assert.Equal(5, ConversationText.Uses(Sample, "answer").Count);
            Assert.Equal(3, ConversationText.Uses(Sample, "answer", fns[1]).Count);
            Assert.Empty(ConversationText.Uses(Sample, "comment"));
            Assert.Equal(2, ConversationText.Uses(Sample, "greet").Count);

            var at = ConversationText.SpanAt(Sample, Sample.IndexOf("get_quest") + 3);
            Assert.Equal("get_quest", at.Value.Text);
            Assert.Equal(SpanKind.String, ConversationText.SpanAt(Sample, Sample.IndexOf("not a comment")).Value.Kind);
        }

        [Fact]
        public void Every_built_in_the_compiler_checks_has_a_description()
        {
            foreach (var name in new[] { "babl_menu", "get_quest", "set_quest", "sex", "x_skills", "do_offer", "compare", "random", "val", "teleport_player", "babl_hack" })
                Assert.NotNull(ConversationBuiltins.Describe(name));
            foreach (var name in ConversationBuiltins.Names.Where(n => n != "say" && n != "respond"))
                Assert.True(ConversationCompiler.Arity(name) != null, name + " has no argument count");
            Assert.Contains("written back", ConversationBuiltins.DescribeVariable("npc_attitude"));
            Assert.Null(ConversationBuiltins.Describe("no_such"));
        }

        [Fact]
        public void A_snapshot_puts_a_slot_back_and_the_shipped_copy_reverts_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var lib = ConversationLibrary.Open(GameSession.Open(g));
            var c = lib.Archive.Read(1);
            byte[] raw = lib.Archive.RawBlock(1);
            var strings = lib.StringsOf(c);
            var before = lib.Snapshot(1);
            Assert.False(before.Edited);
            Assert.False(before.StringsEdited);

            string text = lib.Decompile(c).Text.Replace("\"Hast thou come to add to my torment?\" #1", "\"Art thou here to mock me?\"");
            var r = lib.Compile(c, text);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            lib.Apply(r);
            var after = lib.Snapshot(1);
            Assert.True(after.Edited && after.StringsEdited);
            Assert.Equal(strings.Count + 1, after.Strings.Count);

            lib.Restore(before);
            Assert.False(lib.HasChanges);
            Assert.Equal(raw, lib.Archive.Read(1).ToBytes());
            lib.Restore(after);
            Assert.Contains("Art thou here to mock me?", lib.StringsOf(lib.Archive.Read(1)));

            // the shipped copy: the files as they are (there is no .ORIG beside them)
            var shipped = lib.Shipped(1);
            Assert.Equal(raw, shipped.Conversation.ToBytes());
            Assert.Equal(strings, shipped.Strings);
            Assert.True(lib.RevertToShipped(1));
            Assert.False(lib.HasChanges);
            Assert.Null(lib.Shipped(500));
        }

        [Fact]
        public void An_edited_function_keeps_the_names_its_source_gave_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var lib = ConversationLibrary.Open(GameSession.Open(g));
            var c = lib.Archive.Read(1);
            string text = lib.Decompile(c).Text;
            // an edited function's body is new, so names that follow bodies lose it
            string edited = text.Replace("\"Hast thou come to add to my torment?\" #1", "\"Art thou here to mock me?\"")
                                .Replace("hast_thou_come_to() {", "hast_thou_come_to() {\n    int mood;\n    mood = 1;");
            var r = lib.Compile(c, edited);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            var placed = r.Placed.Single(p => p.Name == "hast_thou_come_to");
            Assert.True(placed.Compiled);
            Assert.Equal(r.Placed.Count, BablLayout.Of(r.Conversation).Functions.Count);
            Assert.Equal("mood", placed.Locals[1]);
            Assert.False(r.Placed.Single(p => p.Name == "main").Compiled);
            lib.Apply(r);
            var now = lib.Archive.Read(1);
            Assert.NotEqual("hast_thou_come_to", lib.Names.Resolve(now).Function(placed.Address));
            Assert.True(lib.Names.Adopt(now, r.Placed) >= 2);
            var names = lib.Names.Resolve(now);
            Assert.Equal("hast_thou_come_to", names.Function(placed.Address));
            Assert.Equal("mood", names.Local(placed.Address, 1));
            // the text after applying is the source as typed, but for the numbers of new strings
            string after = lib.Decompile(now).Text;
            Assert.Contains("hast_thou_come_to() {", after);
            Assert.Contains("mood = 1;", after);
            Assert.Equal(0, lib.Names.Adopt(now, r.Placed));
        }
    }
}
