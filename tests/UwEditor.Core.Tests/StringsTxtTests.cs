using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>STRINGS.PAK as the older tools' text dump (StringsTable, format txt): the form older translations are kept in.</summary>
    public class StringsTxtTests
    {
        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_text_dump_goes_out_and_comes_back_the_same(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var st = StringsEditor.Open(GameSession.Open(g));
            var before = st.Blocks().ToDictionary(b => b.Number, b => st.Get(b.Number));
            string dump = StringsTable.Export(st, "txt");
            Assert.StartsWith("strings.pak: " + before.Count + " string blocks.", dump);
            Assert.Contains("\r\nblock: 0001; " + before[1].Count + " strings.\r\n0: ", dump);

            var problems = new List<string>();
            var rows = StringsTable.Read(dump, "txt", problems);
            Assert.Empty(problems);
            Assert.Equal(before.Values.Sum(l => l.Count), rows.Count);
            // spaces at the end of a text are part of it (the game joins strings)
            foreach (var r in rows) Assert.Equal((before[r.Block][r.Index] ?? "").Replace("\r\n", "\n"), r.Text);
            var same = StringsTable.Import(st, dump, "txt");
            Assert.Equal(0, same.Changed);
            Assert.Empty(same.Problems);
        }

        [Fact]
        public void A_dump_in_Latin_1_with_hexadecimal_blocks_is_read_and_applied()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var st = StringsEditor.Open(s);
            int block = st.Blocks().Select(b => b.Number).First(n => n >= 0xC00);
            // the letters need codes before they can be written
            s.Accents = new AccentTable();
            s.Accents.Set('$', 'ã');
            s.Accents.Set('%', 'ç');
            string text = "strings.pak: 2 string blocks.\r\n\r\nblock: 0001; 3 strings.\r\n0: Não serve.\\n\r\n1: \r\n2: Fim \r\n\r\nblock: "
                + block.ToString("x4") + "; 1 strings.\r\n0: Coração\r\nnot a string\r\n";
            string path = Path.Combine(Path.GetTempPath(), "uw-txt-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(path, Encoding.Latin1.GetBytes(text));
            try
            {
                string read = StringsTable.ReadFile(path);
                Assert.Contains("Não", read);
                var res = StringsTable.Import(st, read, "txt");
                Assert.Equal(3, res.Changed);
                Assert.Equal(1, res.Empty);
                Assert.Single(res.Problems);
                Assert.Equal("Não serve.\n", st.Get(1, 0));
                Assert.Equal("Fim ", st.Get(1, 2));
                Assert.Equal("Coração", st.Get(block, 0));
                // the same text as UTF-8 reads the same
                File.WriteAllText(path, text, new UTF8Encoding(true));
                Assert.Equal(read, StringsTable.ReadFile(path));
            }
            finally { File.Delete(path); }
            Assert.Equal("txt", StringsTable.FormatOf("strings_pt-br.txt"));
        }
    }
}
