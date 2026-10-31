using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class ConversationTests
    {
        readonly ITestOutputHelper output;
        public ConversationTests(ITestOutputHelper output) { this.output = output; }

        static ConversationArchive Cnv(string game, out GameKind kind)
        {
            kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            string g = GameData.Game(game);
            if (g == null) return null;
            return ConversationArchive.Load(GameProfile.Find(Path.Combine(g, "DATA"), "CNV.ARK"), kind);
        }

        [Theory]
        [InlineData("UW", 97)]
        [InlineData("UW2", 102)]
        public void Every_shipped_conversation_parses_and_writes_back_the_same_bytes(string game, int expected)
        {
            GameKind kind;
            var cnv = Cnv(game, out kind); if (cnv == null) return;
            int n = 0;
            foreach (int slot in cnv.Slots())
            {
                var raw = cnv.RawBlock(slot);
                var c = cnv.Read(slot);
                Assert.Equal(raw, c.ToBytes());
                Assert.Equal((ushort)BablOp.START, c.Code[0]);
                n++;
            }
            output.WriteLine("{0}: {1} conversations", game, n);
            if (expected > 0) Assert.Equal(expected, n);
            Assert.True(n > 50);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_shipped_conversation_disassembles_and_assembles_to_the_same_bytes(string game)
        {
            GameKind kind;
            var cnv = Cnv(game, out kind); if (cnv == null) return;
            int unknown = 0;
            foreach (int slot in cnv.Slots())
            {
                var c = cnv.Read(slot);
                string text = Disassembler.Disassemble(c);
                unknown += text.Split('\n').Count(l => l.TrimStart().StartsWith(".word"));
                var back = Assembler.Assemble(text);
                Assert.Equal(c.ToBytes(), back.ToBytes());
            }
            output.WriteLine("{0}: {1} words that are not instructions", game, unknown);
        }

        [Fact]
        public void Unchanged_archive_saves_to_the_same_bytes_and_a_change_is_kept()
        {
            foreach (var game in new[] { "UW", "UW2" })
            {
                GameKind kind;
                var cnv = Cnv(game, out kind); if (cnv == null) continue;
                var original = File.ReadAllBytes(cnv.Path);
                Assert.Equal(original, cnv.ToBytes());
                int slot = cnv.Slots().First();
                var c = cnv.Read(slot);
                cnv.Write(slot, c);   // the same bytes: nothing written
                var written = new System.Collections.Generic.List<int>();
                Assert.Equal(original, cnv.ToBytes(written));
                Assert.Empty(written);
                c.Code = c.Code.Concat(new ushort[] { (ushort)BablOp.NOP }).ToArray();
                cnv.Write(slot, c);
                var again = ConversationArchive.FromBytes(cnv.ToBytes(written), kind);
                Assert.Equal(new[] { slot }, written);
                Assert.Equal(c.ToBytes(), again.Read(slot).ToBytes());
                foreach (int other in again.Slots().Where(s => s != slot))
                    Assert.Equal(cnv.RawBlock(other), again.RawBlock(other));
            }
        }

        [Fact]
        public void Assembler_reports_the_line_of_a_mistake()
        {
            var e = Assert.Throws<AssemblyError>(() => Assembler.Assemble(".slot 1\n.strings 0xE01\n.variables 0\n    START\n    JMP nowhere\n"));
            Assert.Equal(5, e.Line);
            e = Assert.Throws<AssemblyError>(() => Assembler.Assemble(".slot 1\n.strings 0xE01\n.variables 0\n    START\n    PUSHI\n"));
            Assert.Equal(5, e.Line);
            var c = Assembler.Assemble(".slot 1\n.strings 0xE01\n.variables 0\n    START\nback:\n    BRA back\n    EXIT\n");
            Assert.Equal(new ushort[] { 0x22, 0x12, 0xFFFF, 0x26 }, c.Code);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Strings_pak_saves_unchanged_and_edits_one_block_leaving_the_rest(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            string path = GameProfile.Find(Path.Combine(g, "DATA"), "STRINGS.PAK");
            var original = File.ReadAllBytes(path);
            var pak = StringsPakFile.Load(path);
            Assert.Equal(original, pak.ToBytes());
            // every conversation block encodes back to the bytes it came from
            int same = 0, blocks = 0;
            foreach (int b in pak.Blocks.Where(b => b >= 0xE00 && b < 0xF00).ToList())
            {
                var copy = StringsPakFile.FromBytes(original);
                var list = copy.Get(b);
                list.Add("x");
                copy.Set(b, list);
                list.RemoveAt(list.Count - 1);
                copy.Set(b, list);     // back to the original: no longer a change
                Assert.False(copy.IsChanged(b));
                blocks++;
            }
            int block = pak.Blocks.First(b => b >= 0xE00);
            var strings = pak.Get(block);
            strings[1] = "Edited by a test, with \"quotes\" and 123.";
            strings.Add("A new string.");
            pak.Set(block, strings);
            var back = StringsPakFile.FromBytes(pak.ToBytes());
            Assert.Equal(strings, back.Get(block));
            var orig = StringsPakFile.FromBytes(original);
            foreach (int b in orig.Blocks.Where(b => b != block)) { Assert.Equal(orig.Get(b), back.Get(b)); same++; }
            output.WriteLine("{0}: {1} conversation blocks, {2} other blocks unchanged", game, blocks, same);
            Assert.Throws<InvalidDataException>(() => pak.Set(block, new[] { "a | b" }));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Decompiled_view_shows_say_menus_and_finds_the_quests_conversations_use(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var lib = ConversationLibrary.Open(s);
            int says = 0, menus = 0;
            foreach (int slot in lib.Archive.Slots())
            {
                var c = lib.Archive.Read(slot);
                var d = lib.Decompile(c);
                Assert.NotEmpty(d.Lines);
                says += d.Lines.Count(l => l.Kind == PseudoKind.Say);
                menus += d.Lines.Count(l => l.Text.Contains("babl_menu(…)") || l.Text.Contains("babl_fmenu(…"));
                // the stack always balances, except after UW1 conversation 6's two words that are not instructions
                if (!d.Lines.Any(l => l.Text.StartsWith("word 0x"))) Assert.DoesNotContain(d.Lines, l => l.Text.Contains("<empty>"));
                // every string a say or respond names is in the block
                var strs = lib.StringsOf(c);
                Assert.All(d.Strings, u => Assert.InRange(u.Id, 0, strs.Count - 1));
            }
            var uses = GlobalUsage.Collect(lib.Archive, s.Strings, s.Kind);
            var globals = GlobalVariables.Load(s, lib.Archive, uses, lib.Name);
            int quests = globals.All.Count(v => v.Kind == GlobalKind.Quest && v.Uses.Count > 0);
            int priv = globals.All.Count(v => v.Kind == GlobalKind.Private && v.Uses.Count > 0);
            output.WriteLine("{0}: {1} says, {2} menus, {3} uses, {4} quests used, {5} private globals used", game, says, menus, uses.Count, quests, priv);
            Assert.True(says > 1000);
            Assert.True(menus > 300);
            Assert.True(quests > 5);
            Assert.True(priv > 50);
            if (s.Kind == GameKind.UW2) Assert.Contains(globals.All, v => v.Kind == GlobalKind.XClock && v.Uses.Count > 0);
            else
            {
                // Murgo (quest 0) and the talismans quest are set by conversations
                Assert.NotEmpty(globals.Find(GlobalKind.Quest, 0).Writers);
                Assert.Contains(globals.All, v => v.Kind == GlobalKind.GameVariable && v.Uses.Count > 0);
            }
        }

        [Fact]
        public void Uw2_save_values_are_read_and_written_back_through_a_copy()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            string tmp = Path.Combine(Path.GetTempPath(), "uwedit-globals-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDir(g, tmp, "DATA");
                CopyDir(g, tmp, "SAVE1");
                File.Copy(Path.Combine(g, "UW2.EXE"), Path.Combine(tmp, "UW2.EXE"));
                var s = GameSession.Open(Path.Combine(tmp, "SAVE1"));
                var lib = ConversationLibrary.Open(s);
                var globals = GlobalVariables.Load(s, lib.Archive, null);
                Assert.True(globals.CanEdit);
                var clock = globals.Find(GlobalKind.XClock, 1);
                var q = globals.Find(GlobalKind.Quest, 77);
                var v = globals.Find(GlobalKind.GameVariable, 200);
                var p = globals.All.First(x => x.Kind == GlobalKind.Private && x.Editable);
                Assert.Null(globals.Set(clock, 9));
                Assert.Null(globals.Set(q, q.Value == 1 ? 0 : 1));
                Assert.Null(globals.Set(v, 1234));
                Assert.Null(globals.Set(p, -5));
                Assert.NotNull(globals.Set(clock, 300));
                Assert.Equal(2, globals.Save().Count);
                Assert.True(File.Exists(Path.Combine(tmp, "SAVE1", "PLAYER.BKP")));
                var again = GlobalVariables.Load(GameSession.Open(Path.Combine(tmp, "SAVE1")), lib.Archive, null);
                Assert.Equal(9, again.Find(GlobalKind.XClock, 1).Value);
                Assert.Equal(q.Value, again.Find(GlobalKind.Quest, 77).Value);
                Assert.Equal(1234, again.Find(GlobalKind.GameVariable, 200).Value);
                Assert.Equal(-5, again.Find(GlobalKind.Private, p.Number, p.Slot).Value);
                // a quest flag shares its byte with others: only its bit changed
                var before = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(tmp, "SAVE1", "PLAYER.BKP")), GameKind.UW2).Record;
                var after = PlayerDat.Parse(File.ReadAllBytes(Path.Combine(tmp, "SAVE1", "PLAYER.DAT")), GameKind.UW2).Record;
                int diff = Enumerable.Range(0, before.Length).Count(i => before[i] != after[i]);
                Assert.InRange(diff, 2, 4);
            }
            finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }
        }

        static void CopyDir(string from, string to, string name)
        {
            string src = Path.Combine(from, name), dst = Path.Combine(to, name);
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        }
    }
}
