using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>The conversations' names: the shipped names files, carrying names over by body, renames, and the screening that made the files.</summary>
    public class ConversationNamesTests
    {
        readonly ITestOutputHelper output;
        public ConversationNamesTests(ITestOutputHelper output) { this.output = output; }

        static GameKind KindOf(string game) { return game == "UW2" ? GameKind.UW2 : GameKind.UW1; }

        static ConversationArchive Cnv(string game)
        {
            string g = GameData.Game(game);
            return g == null ? null : ConversationArchive.Load(GameProfile.Find(Path.Combine(g, "DATA"), "CNV.ARK"), KindOf(game));
        }

        static StringsPakFile Pak(string game)
        {
            return StringsPakFile.Load(GameProfile.Find(Path.Combine(GameData.Game(game), "DATA"), "STRINGS.PAK"));
        }

        /// <summary>The conversation with one NOP put in after main's START: every address after it moves by one.</summary>
        static Conversation Shifted(Conversation c)
        {
            var lines = Disassembler.Disassemble(c).Split('\n').ToList();
            int start = lines.FindIndex(l => l.TrimStart().StartsWith("START"));
            lines.Insert(start + 1, "    NOP");
            var e = Assembler.Assemble(string.Join("\n", lines));
            e.Slot = c.Slot;
            return e;
        }

        static string TempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uwnames-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "conversation-names-uw1.json");
        }

        [Fact]
        public void Names_apply_only_to_the_unchanged_block_and_follow_function_bodies_after_an_edit()
        {
            var cnv = Cnv("UW"); if (cnv == null) return;
            var c = cnv.Read(146);
            var lay = BablLayout.Of(c);
            int f = lay.Functions.First(x => x > 0x300);
            int label = lay.Labels.First(a => lay.FunctionOf(a) == f);
            var file = new NameFile();
            var s = new SlotNames { Sha256 = NameBook.BlockHash(c) };
            var withBody = new FunctionNames { Name = "followed", Body = lay.Body(f) };
            withBody.Labels[label - f] = "a_label";
            withBody.Locals[22] = "answer";
            s.Functions[f] = withBody;
            int g = lay.Functions.First(x => x > f);
            s.Functions[g] = new FunctionNames { Name = "only_here" };   // no body: cannot follow
            s.Globals[34] = "quest_flag";
            file.Slots[146] = s;
            var book = NameBook.Load(GameKind.UW1, null, file);

            var n = book.Resolve(c);
            Assert.True(n.Exact);
            Assert.Equal("followed", n.Function(f));
            Assert.Equal("only_here", n.Function(g));
            Assert.Equal("a_label", n.Label(label));
            Assert.Equal("answer", n.Local(f, 22));
            Assert.Equal("quest_flag", n.Global(34));
            var d = Decompiler.Decompile(c, Pak("UW").Get(c.StringBlock).ToArray(), GameKind.UW1, true, n);
            Assert.Contains(d.Lines, l => l.Kind == PseudoKind.Function && l.Text.StartsWith("followed()"));
            Assert.Contains(d.Lines, l => l.Text.Contains("answer = babl_menu("));
            Assert.Contains(d.Lines, l => l.Text.Contains("quest_flag = get_quest(32)"));

            var e = Shifted(c);
            var m = book.Resolve(e);
            Assert.False(m.Exact);
            Assert.Equal("followed", m.Function(f + 1));
            Assert.Equal("a_label", m.Label(label + 1));
            Assert.Equal("answer", m.Local(f + 1, 22));
            Assert.Equal(ConversationNames.FunctionPlaceholder(g + 1), m.Function(g + 1));
            Assert.Equal("quest_flag", m.Global(34));
            Assert.True(m.Carried >= 1);
            // another slot's block takes nothing of slot 146's
            Assert.Equal(0, book.Resolve(cnv.Read(1)).NamedFunctions);
        }

        [Fact]
        public void The_shipped_names_apply_to_the_shipped_blocks_and_most_follow_an_edit()
        {
            foreach (var game in new[] { "UW", "UW2" })
            {
                var cnv = Cnv(game); if (cnv == null) continue;
                var book = NameBook.Load(KindOf(game), null);
                Assert.Null(book.LoadError);
                int exact = 0, slots = 0;
                foreach (int slot in cnv.Slots())
                {
                    slots++;
                    var c = cnv.Read(slot);
                    var n = book.Resolve(c);
                    if (n.Exact) exact++;
                    var lay = n.Layout;
                    // every function that speaks has a name, and every label
                    foreach (int f in lay.Functions)
                        if (f != 0 && NameScreening.Speaks(c, lay, f)) Assert.True(n.IsNamed(NameTarget.Function(f)), game + " slot " + slot + " function " + f.ToString("X4"));
                    Assert.Equal(lay.Labels.Count, n.NamedLabels);
                    // names are unique among functions and labels
                    var all = lay.Functions.Select(n.Function).Concat(lay.Labels.Select(n.Label)).ToList();
                    Assert.Equal(all.Count, all.Distinct().Count());
                }
                Assert.Equal(slots, exact);
                // after an edit that moves everything, the functions keep their names
                var c0 = cnv.Read(cnv.Slots().First(s => BablLayout.Of(cnv.Read(s)).Functions.Count > 20));
                var before = book.Resolve(c0);
                var after = book.Resolve(Shifted(c0));
                int kept = before.Layout.Functions.Count(f => f != 0 && before.Function(f) == after.Function(f + 1));
                output.WriteLine("{0}: {1} slots named exactly; slot {2}: {3} of {4} function names kept after the edit", game, exact, c0.Slot, kept, before.Layout.Functions.Count - 1);
                Assert.True(kept >= before.Layout.Functions.Count - 2);
            }
        }

        [Fact]
        public void A_rename_round_trips_and_the_script_assembles_to_the_same_bytes()
        {
            var cnv = Cnv("UW"); if (cnv == null) return;
            string path = TempFile();
            var book = NameBook.Load(GameKind.UW1, path);
            var c = cnv.Read(146);
            var n = book.Resolve(c);
            var lay = n.Layout;
            int f = lay.Functions.First(x => x > 0x300 && lay.Called.Contains(x));
            int label = lay.Labels.First(a => a > 0x300);
            int global = Enumerable.Range(0, c.Variables).First(a => c.VariableAt(a) == null);

            Assert.NotNull(n.Check(c, NameTarget.Function(f), "2bad"));
            Assert.NotNull(n.Check(c, NameTarget.Function(f), "goto"));
            Assert.NotNull(n.Check(c, NameTarget.Function(f), "PUSHI"));
            Assert.NotNull(n.Check(c, NameTarget.Function(f), "npc_attitude"));
            Assert.NotNull(n.Check(c, NameTarget.Function(f), n.Label(label)));
            Assert.NotNull(n.Check(c, NameTarget.Function(0), "begin"));
            Assert.NotNull(n.Check(c, NameTarget.Function(f), "func_1234"));
            Assert.Null(n.Check(c, NameTarget.Function(f), "ask_about_dorna"));

            book.Rename(c, NameTarget.Function(f), "ask_about_dorna");
            book.Rename(c, NameTarget.Label(label), "after_hail");
            book.Rename(c, NameTarget.Global(global), "my_flag");
            book.Rename(c, NameTarget.Local(f, 22), "answer");
            Assert.True(File.Exists(path));

            var again = NameBook.Load(GameKind.UW1, path);
            var m = again.Resolve(c);
            Assert.Equal("ask_about_dorna", m.Function(f));
            Assert.Equal("after_hail", m.Label(label));
            Assert.Equal("my_flag", m.Global(global));
            Assert.Equal("answer", m.Local(f, 22));
            Assert.Equal(NameTarget.Function(f), m.Find(c, "ask_about_dorna", 0));
            Assert.Equal(NameTarget.Local(f, 22), m.Find(c, "answer", f));
            Assert.Equal(NameTarget.Local(f, 21), m.Find(c, "local21", f));

            string text = Disassembler.Disassemble(c, null, null, m);
            Assert.Contains("CALL      ask_about_dorna", text);
            Assert.Contains("after_hail:", text);
            Assert.Equal(c.ToBytes(), Assembler.Assemble(text).ToBytes());

            // an empty name goes back to the shipped one
            again.Rename(c, NameTarget.Function(f), "");
            Assert.Equal(NameBook.Load(GameKind.UW1, null).Resolve(c).Function(f), again.Resolve(c).Function(f));

            // a rename made in an edited block is exported onto the shipped block's function with that body
            var e = Shifted(c);
            again.Rename(e, NameTarget.Function(f + 1), "edited_name");
            Assert.Equal("edited_name", NameBook.Load(GameKind.UW1, path).Resolve(e).Function(f + 1));
            Assert.Equal("after_hail", NameBook.Load(GameKind.UW1, path).Resolve(e).Label(label + 1));
            var export = NameFile.Parse(again.Export(cnv.Read).ToJson());
            Assert.Equal("edited_name", export.Slots[146].Functions[f].Name);
            Assert.Equal("after_hail", NameBook.Load(GameKind.UW1, null, export).Resolve(c).Label(label));
            Assert.Equal("my_flag", export.Slots[146].Globals[global]);
            Assert.Equal(NameBook.LoadShipped(GameKind.UW1).Slots[146].Sha256, export.Slots[146].Sha256);
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_conversation_with_its_names_assembles_to_the_same_bytes(string game)
        {
            var cnv = Cnv(game); if (cnv == null) return;
            var book = NameBook.Load(KindOf(game), null);
            foreach (int slot in cnv.Slots())
            {
                var c = cnv.Read(slot);
                string text = Disassembler.Disassemble(c, null, null, book.Resolve(c));
                Assert.Equal(c.ToBytes(), Assembler.Assemble(text).ToBytes());
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_screening_names_every_library_function(string game)
        {
            var cnv = Cnv(game); if (cnv == null) return;
            var kind = KindOf(game);
            var pak = Pak(game);
            var screened = NameScreening.Screen(cnv, pak, kind, NameScreening.Talkers(pak));
            var shipped = NameBook.LoadShipped(kind);
            var lib = NameScreening.LibraryBodies(cnv);
            int functions = lib.Sum(kv => kv.Value.Count);
            output.WriteLine("{0}: {1} library bodies cover {2} functions", game, lib.Count, functions);
            Assert.True(lib.Count >= 20 && functions > 1000);
            foreach (var kv in lib)
            {
                Assert.True(screened.Library.ContainsKey(kv.Key) && screened.Library[kv.Key].Name != null, game + ": no name for the body of slot " + kv.Value[0].Key + " " + kv.Value[0].Value.ToString("X4"));
                Assert.True(shipped.Library.ContainsKey(kv.Key) && shipped.Library[kv.Key].Name != null, game + ": the shipped file lacks the body of slot " + kv.Value[0].Key + " " + kv.Value[0].Value.ToString("X4"));
            }
            // the shipped file names every slot's block
            foreach (int slot in cnv.Slots()) Assert.Equal(screened.Slots[slot].Sha256, shipped.Slots[slot].Sha256);
            // and reads back as it writes
            Assert.Equal(shipped.ToJson(), NameFile.Parse(shipped.ToJson()).ToJson());
        }

        /// <summary>
        /// Writes docs/conversations/names-uw1.json and names-uw2.json from the game data when
        /// UW_WRITE_NAMES=1: a fresh screening, with the names already in the files kept.
        /// </summary>
        [Fact]
        public void Write_the_shipped_names_files_when_asked() { WriteNames(); }

        void WriteNames([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        {
            if (Environment.GetEnvironmentVariable("UW_WRITE_NAMES") != "1") return;
            // this file's folder, wherever the build went
            var dir = new DirectoryInfo(Path.GetDirectoryName(here));
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorAvalonia.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            foreach (var game in new[] { "UW", "UW2" })
            {
                var cnv = Cnv(game); if (cnv == null) continue;
                var kind = KindOf(game);
                var pak = Pak(game);
                string path = Path.Combine(dir.FullName, "docs", "conversations", NameBook.ShippedResource(kind));
                var existing = File.Exists(path) ? NameFile.Parse(File.ReadAllText(path)) : null;
                var file = NameScreening.Merge(NameScreening.Screen(cnv, pak, kind, NameScreening.Talkers(pak)), existing);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, file.ToJson());
                output.WriteLine("Wrote " + path);
            }
        }
    }
}
