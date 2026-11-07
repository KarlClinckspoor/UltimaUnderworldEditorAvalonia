using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;
using UwEditor.Core.Graphics;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Fonts (UwFont: DATA\FONT*.SYS), their exports (PNG sheet, BDF, TrueType), the accent
    /// table, and STRINGS.PAK's new Huffman tree for characters the shipped tree has no code for.
    /// </summary>
    public class FontTests
    {
        readonly ITestOutputHelper output;
        public FontTests(ITestOutputHelper output) { this.output = output; }

        public static IEnumerable<object[]> Fonts()
        {
            foreach (var g in new[] { "UW", "UW2" })
                foreach (var n in FontFiles.Names) yield return new object[] { g, n };
        }

        static UwFont Load(string game, string name, out byte[] bytes)
        {
            bytes = null;
            string g = GameData.Game(game); if (g == null) return null;
            string p = GameProfile.Find(Path.Combine(g, "DATA"), name);
            Assert.NotNull(p);
            bytes = File.ReadAllBytes(p);
            var f = UwFont.Read(bytes);
            f.Name = name;
            return f;
        }

        [Theory]
        [MemberData(nameof(Fonts))]
        public void Every_shipped_font_round_trips_byte_for_byte(string game, string name)
        {
            byte[] bytes;
            var f = Load(game, name, out bytes); if (f == null) return;
            Assert.Equal(bytes, f.ToBytes());
            Assert.Empty(f.Problems());
            Assert.True(f.GlyphBytes <= UwFont.MaxGlyphBytes);
            // filled to 128: 12 + 128 glyphs, the shipped glyphs unchanged
            var full = f.Clone();
            full.FillTo128();
            var b = full.ToBytes();
            Assert.Equal(12 + 128 * f.GlyphBytes, b.Length);
            Assert.Equal(bytes.Take(12 + f.Count * f.GlyphBytes), b.Take(12 + f.Count * f.GlyphBytes));
            var back = UwFont.Read(b);
            Assert.Equal(128, back.Count);
            Assert.True(back.Glyphs[127].Present);
        }

        [Fact]
        public void The_shipped_fonts_hold_fewer_than_128_glyphs()
        {
            byte[] b;
            var counts = FontFiles.Names.Select(n => { var f = Load("UW", n, out b); return f == null ? -1 : f.Count; }).ToList();
            if (counts.Contains(-1)) return;
            output.WriteLine(string.Join(", ", FontFiles.Names.Zip(counts, (n, c) => n + " " + c)));
            Assert.Equal(new[] { 127, 127, 127, 127, 97, 94 }, counts);
        }

        [Fact]
        public void A_glyph_over_33_bytes_is_refused()
        {
            var f = UwFont.Blank(16, 2, 4, 16);
            Assert.Equal(33, f.GlyphBytes);
            Assert.Empty(f.Problems());
            Assert.Equal(12 + 128 * 33, f.ToBytes().Length);
            f.WidthSize = 2;
            foreach (var g in f.Glyphs) g.Extra = new byte[1];
            Assert.NotEmpty(f.Problems());
            Assert.Throws<InvalidDataException>(() => f.ToBytes());
        }

        [Theory]
        [MemberData(nameof(Fonts))]
        public void Bdf_round_trips_exactly(string game, string name)
        {
            byte[] bytes;
            var f = Load(game, name, out bytes); if (f == null) return;
            var accents = new AccentTable();
            accents.Set('$', 'é'); accents.Set(1, 'ç');
            string bdf = FontBdf.Export(f, accents);
            Assert.Contains("ENCODING 233", bdf);      // é at '$'
            Assert.Contains("STARTCHAR uw001", bdf);
            var back = FontBdf.Import(bdf, accents);
            Assert.Equal(bytes, back.ToBytes());
            // without its names, codes come back from the characters
            string anonymous = System.Text.RegularExpressions.Regex.Replace(bdf, "STARTCHAR uw[0-9]+", "STARTCHAR glyph");
            var again = FontBdf.Import(anonymous, accents);
            for (int c = 0x20; c < 0x7F; c++)
                if (f.Glyphs[c].Present) Assert.True(f.Glyphs[c].SameAs(again.Glyphs[c]), name + " code " + c);
        }

        [Fact]
        public void A_bdf_from_elsewhere_is_fitted_into_the_games_font()
        {
            byte[] bytes;
            var f = Load("UW", "FONT5X6P.SYS", out bytes); if (f == null) return;
            // an 'é' 5 wide, 7 high with its top 1 pixel above the font's top (cut), on the baseline
            string bdf = "STARTFONT 2.1\nFONT x\nSIZE 7 75 75\nFONTBOUNDINGBOX 5 7 0 -1\nSTARTPROPERTIES 2\nFONT_ASCENT 6\nFONT_DESCENT 1\nENDPROPERTIES\nCHARS 1\n"
                + "STARTCHAR eacute\nENCODING 233\nSWIDTH 500 0\nDWIDTH 5 0\nBBX 4 6 0 0\nBITMAP\n20\n40\n60\n90\nF0\n70\nENDCHAR\nENDFONT\n";
            var accents = new AccentTable();
            accents.Set('$', 'é');
            var warnings = new List<string>();
            var g = FontBdf.Import(bdf, accents, f, warnings);
            Assert.True(g.Glyphs['$'].Present);
            Assert.Equal(5, g.Glyphs['$'].Width);
            // the baseline is under row 4 (the bottom of 'a'): the glyph's last row lands on row 4
            Assert.True(g.Pixel('$', 1, 4) && g.Pixel('$', 2, 4) && g.Pixel('$', 3, 4));
            Assert.Equal(f.Height, g.Height);
            Assert.NotEmpty(warnings);   // the top row was cut
        }

        [Theory]
        [InlineData("UW", "FONT5X6P.SYS")]
        [InlineData("UW", "FONTBIG.SYS")]
        [InlineData("UW", "FONT4X5P.SYS")]
        public void The_png_sheet_round_trips_glyphs_and_widths(string game, string name)
        {
            byte[] bytes;
            var f = Load(game, name, out bytes); if (f == null) return;
            var png = FontSheet.Export(f);
            var img = Png.Decode(png);
            Assert.Equal(1 + 16 * (FontSheet.CellWidth(f) + 1), img.Width);
            Assert.Equal(1 + 8 * (f.Height + 2), img.Height);
            var back = FontSheet.Import(f, png);
            var full = f.Clone(); full.FillTo128();
            Assert.Equal(full.ToBytes(), back.ToBytes());
        }

        [Fact]
        public void A_sheet_of_the_wrong_size_is_refused()
        {
            byte[] bytes;
            var f = Load("UW", "FONT5X6P.SYS", out bytes); if (f == null) return;
            var big = Load("UW", "FONTBIG.SYS", out bytes);
            Assert.Throws<InvalidDataException>(() => FontSheet.Import(f, FontSheet.Export(big)));
        }

        [Fact]
        public void The_truetype_font_has_a_valid_table_directory_and_checksum()
        {
            byte[] bytes;
            var f = Load("UW", "FONT5X6P.SYS", out bytes); if (f == null) return;
            var accents = new AccentTable(); accents.Set('$', 'é');
            var ttf = FontTtf.Export(f, accents, "Underworld FONT5X6P");
            uint U32(int at) { return (uint)(ttf[at] << 24 | ttf[at + 1] << 16 | ttf[at + 2] << 8 | ttf[at + 3]); }
            int U16(int at) { return ttf[at] << 8 | ttf[at + 1]; }
            Assert.Equal(0x00010000u, U32(0));
            int n = U16(4);
            Assert.Equal(10, n);
            var tags = new List<string>();
            for (int i = 0; i < n; i++)
            {
                int rec = 12 + 16 * i;
                string tag = System.Text.Encoding.ASCII.GetString(ttf, rec, 4);
                tags.Add(tag);
                int off = (int)U32(rec + 8), len = (int)U32(rec + 12);
                Assert.True(off % 4 == 0 && off + len <= ttf.Length, tag);
                if (tag != "head")
                {
                    uint sum = 0;
                    for (int k = 0; k < len; k += 4) { uint v = 0; for (int j = 0; j < 4; j++) v = v << 8 | (k + j < len ? ttf[off + k + j] : 0u); sum += v; }
                    Assert.Equal(U32(rec + 4), sum);
                }
            }
            Assert.Equal(tags.OrderBy(t => t, StringComparer.Ordinal), tags);
            uint whole = 0;
            for (int k = 0; k < ttf.Length; k += 4) { uint v = 0; for (int j = 0; j < 4; j++) v = v << 8 | (k + j < ttf.Length ? ttf[k + j] : 0u); whole += v; }
            Assert.Equal(0xB1B0AFBAu, whole);
        }

        [Fact]
        public void Make_accented_letter_puts_the_mark_over_or_under_the_letter()
        {
            byte[] bytes;
            var f = Load("UW", "FONT5X6P.SYS", out bytes); if (f == null) return;
            // 'e' has its top at row 1: an acute fits in row 0, the letter unchanged
            var e = AccentMaker.Make(f, 'e', AccentMark.Acute);
            Assert.NotEqual(0u, e.Rows[0]);
            for (int y = 1; y < f.Height; y++) Assert.Equal(f.Glyphs['e'].Rows[y], e.Rows[y]);
            // a cedilla hangs under 'c' (its bottom at row 4)
            var c = AccentMaker.Make(f, 'c', AccentMark.Cedilla);
            Assert.NotEqual(0u, c.Rows[5]);
            // a capital has no room: the top row is replaced by the mark
            var E = AccentMaker.Make(f, 'E', AccentMark.Acute);
            Assert.NotEqual(f.Glyphs['E'].Rows[0], E.Rows[0]);
            // big fonts get taller marks above the letter
            var big = Load("UW", "FONTBIG.SYS", out bytes);
            var a = AccentMaker.Make(big, 'a', AccentMark.Circumflex);
            Assert.True(Enumerable.Range(0, 4).Count(y => a.Rows[y] != 0) >= 2);
            char b; AccentMark m;
            Assert.True(AccentMaker.Decompose('ã', out b, out m));
            Assert.Equal('a', b); Assert.Equal(AccentMark.Tilde, m);
            Assert.True(AccentMaker.Decompose('Ç', out b, out m));
            Assert.Equal('C', b); Assert.Equal(AccentMark.Cedilla, m);
            Assert.False(AccentMaker.Decompose('ß', out b, out m));
        }

        [Fact]
        public void The_game_draws_only_codes_20h_to_7Ah()
        {
            var f = UwFont.Blank(6, 1, 4, 5);
            for (int c = 0; c < 128; c++) f.Glyphs[c].Width = c == '?' ? 4 : 5;
            Assert.Equal('?', UwFont.DrawnCode(1));
            Assert.Equal('?', UwFont.DrawnCode(127));
            Assert.Equal('?', UwFont.DrawnCode(0x7B));
            Assert.Equal('$', UwFont.DrawnCode('$'));
            int w;
            f.Render(new[] { 1, (int)'a' }, out w);
            Assert.Equal(9, w);   // drawn as '?' (4) then 'a' (5)
            Assert.Equal(10, f.StringWidth(new[] { 1, (int)'a' }));   // string_width adds code 1's own width
            Assert.Equal(54 * 5, f.StringWidth(Enumerable.Repeat((int)'a', 60)));
        }

        // ----- the accent table -----

        [Fact]
        public void The_accent_table_shows_letters_and_writes_codes()
        {
            var t = new AccentTable();
            t.Set('$', 'é'); t.Set('[', 'ç');
            Assert.Equal("café ça", t.ToDisplay("caf$ [a"));
            Assert.Equal("caf$ [a", t.ToGame("café ça"));
            Assert.Contains("no code", t.Problem("pão"));
            Assert.Contains("gives to", t.Problem("10$"));
            Assert.Throws<FormatException>(() => t.ToGame("pão"));
            Assert.Throws<ArgumentException>(() => t.Set('|', 'ã'));
            Assert.Throws<ArgumentException>(() => t.Set('\\', 'ã'));
            Assert.Throws<ArgumentException>(() => t.Set('@', 'ã'));
            Assert.Throws<ArgumentException>(() => t.Set(10, 'ã'));
            Assert.Throws<ArgumentException>(() => t.Set('a', 'ã'));
            Assert.Throws<ArgumentException>(() => t.Set('%', 'é'));   // é has a code
            var back = AccentTable.FromDictionary(t.ToDictionary());
            Assert.Equal(t.Map, back.Map);
            Assert.Equal('é', FontTtf.UnicodeOf('$', t));
            Assert.Equal(0xE001, FontTtf.UnicodeOf(1, t));
        }

        static CodeUse Scan(string game)
        {
            string g = GameData.Game(game); if (g == null) return null;
            var pak = StringsPakFile.Load(GameProfile.Find(Path.Combine(g, "DATA"), "STRINGS.PAK"));
            return CodeUse.Scan(pak.Blocks.SelectMany(b => pak.Get(b).Select(s => (b, s))));
        }

        [Theory]
        [InlineData("UW", "$%[]", "#$%*/[]")]
        [InlineData("UW2", "$[]^`", "$*=[]^`")]
        public void The_free_codes_are_the_symbols_no_string_uses(string game, string free, string withDebug)
        {
            var use = Scan(game); if (use == null) return;
            Assert.Equal(free, new string(use.FreeCodes(null, false, false).Select(c => (char)c).ToArray()));
            Assert.Equal(withDebug, new string(use.FreeCodes(null, true, false).Select(c => (char)c).ToArray()));
            var undrawn = use.FreeCodes(null, false, true);
            Assert.Contains(1, undrawn); Assert.Contains(127, undrawn);
            Assert.DoesNotContain(10, undrawn); Assert.DoesNotContain(9, undrawn); Assert.DoesNotContain('|', undrawn);
            // a preset fills the free codes as far as they go
            var t = new AccentTable();
            string left = t.Fill(AccentTable.Presets.First(p => p.Name == "Spanish"), use.FreeCodes(t, false, false));
            Assert.Equal(free.Length, t.Count);
            Assert.Equal("éóáíñúü¿¡ÉÓÁÍÑÚÜ".Substring(free.Length), left);
            // the mapped codes stay free for the table once text uses them
            Assert.Equal(free, new string(use.FreeCodes(t, false, false).Select(c => (char)c).ToArray()));
        }

        // ----- STRINGS.PAK with a new tree -----

        static StringsPakFile Pak(string game)
        {
            string g = GameData.Game(game); if (g == null) return null;
            return StringsPakFile.Load(GameProfile.Find(Path.Combine(g, "DATA"), "STRINGS.PAK"));
        }

        /// <summary>Every string of every block of a file, read with the game's walk of the tree.</summary>
        static Dictionary<int, List<string>> DecodeLikeTheGame(byte[] file)
        {
            int nodeCount = file[0] | file[1] << 8;
            var nodes = new HuffmanNode[nodeCount];
            for (int i = 0; i < nodeCount; i++) nodes[i] = new HuffmanNode(file[2 + 4 * i], i, file[3 + 4 * i], file[4 + 4 * i], file[5 + 4 * i]);
            int t = 2 + 4 * nodeCount;
            int blocks = file[t] | file[t + 1] << 8;
            var r = new Dictionary<int, List<string>>();
            for (int b = 0; b < blocks; b++)
            {
                int rec = t + 2 + 6 * b;
                int num = file[rec] | file[rec + 1] << 8;
                int at = BitConverter.ToInt32(file, rec + 2);
                int count = file[at] | file[at + 1] << 8;
                var list = new List<string>();
                for (int s = 0; s < count; s++)
                {
                    int rel = file[at + 2 + 2 * s] | file[at + 3 + 2 * s] << 8;
                    list.Add(StringsTree.DecodeLikeTheGame(nodes, file, at + 2 + 2 * count + rel));
                }
                r[num] = list;
            }
            return r;
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_new_character_rebuilds_the_tree_and_every_string_reads_back_in_the_game(string game)
        {
            var pak = Pak(game); if (pak == null) return;
            var before = pak.Blocks.ToDictionary(b => b, b => pak.Get(b));
            var shipped = DecodeLikeTheGame(pak.ToBytes());
            foreach (var kv in before) Assert.Equal(kv.Value, shipped[kv.Key]);

            var list = pak.Get(1);
            list[0] = "Caf$ [] na p\u0001o";   // codes the shipped trees have no leaf for
            pak.Set(1, list);
            Assert.True(pak.NeedsNewTree);
            var bytes = pak.ToBytes();
            int nodes = bytes[0] | bytes[1] << 8;
            Assert.True(nodes <= 255);
            Assert.Equal(1, nodes % 2);
            Assert.Equal((nodes - 1) / 2, Enumerable.Range(0, nodes).Count(i => bytes[2 + 4 * i + 2] != 0xFF));   // n leaves, n-1 inner nodes
            var decoded = DecodeLikeTheGame(bytes);
            before[1] = list;
            Assert.Equal(before.Keys.OrderBy(k => k), decoded.Keys.OrderBy(k => k));
            foreach (var kv in before) Assert.Equal(kv.Value, decoded[kv.Key]);
            // and the editor's reader agrees
            var again = StringsPakFile.FromBytes(bytes);
            foreach (var kv in before) Assert.Equal(kv.Value, again.Get(kv.Key));
            output.WriteLine(game + ": " + pak.ToBytes().Length + " bytes with the new tree; " + nodes + " nodes");
        }

        [Fact]
        public void Edits_the_tree_can_write_keep_the_tree_and_the_other_blocks_bytes()
        {
            var pak = Pak("UW"); if (pak == null) return;
            var orig = pak.ToBytes();
            var list = pak.Get(1);
            list[0] = "Hello";
            pak.Set(1, list);
            Assert.False(pak.NeedsNewTree);
            var b = pak.ToBytes();
            int treeBytes = 2 + 4 * (orig[0] | orig[1] << 8);
            Assert.Equal(orig.Take(treeBytes), b.Take(treeBytes));
        }

        [Fact]
        public void Every_code_fits_in_a_tree_and_unwritable_characters_are_refused()
        {
            // codes 1-127 are 127 characters with the terminator: a tree of 253 nodes, under the 255 links reach
            var pak = Pak("UW"); if (pak == null) return;
            var list = pak.Get(1);
            list[0] = new string(Enumerable.Range(1, 127).Where(c => c != '|').Select(c => (char)c).ToArray());
            pak.Set(1, list);
            var bytes = pak.ToBytes();
            Assert.Equal(253, bytes[0] | bytes[1] << 8);
            Assert.Equal(list[0], DecodeLikeTheGame(bytes)[1][0]);
            var tooMany = Enumerable.Range(1, 129).ToDictionary(c => (char)c, c => 1L);
            Assert.Throws<InvalidOperationException>(() => StringsTree.Build(tooMany));
            list[0] = "café";
            Assert.Throws<InvalidDataException>(() => pak.Set(1, list));   // é itself (233) is past 127
            list[0] = "a|b";
            Assert.Throws<InvalidDataException>(() => pak.Set(1, list));
            list[0] = new string('a', 512);
            Assert.Throws<InvalidDataException>(() => pak.Set(1, list));
        }

        [Fact]
        public void A_tree_from_counts_is_optimal_and_laid_out_as_the_shipped_ones()
        {
            var counts = new Dictionary<char, long> { ['a'] = 45, ['b'] = 13, ['c'] = 12, ['d'] = 16, ['e'] = 9, ['f'] = 5 };
            var t = StringsTree.Build(counts);
            Assert.Equal(13, t.Length);                 // 7 leaves ('|' added), 6 inner
            Assert.Equal(255, t[t.Length - 1].Parent);  // the root last
            Assert.Equal('|', (char)t[6].Char);         // leaves sorted by character
            Assert.True(Enumerable.Range(0, 7).All(i => t[i].Left == 255 && t[i].Right == 255));
        }

        // ----- the editors -----

        [Fact]
        public void The_strings_editor_shows_and_takes_mapped_letters()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            s.Accents.Set('$', 'é');
            var e = StringsEditor.Open(s);
            e.Set(1, 0, "Café");
            Assert.Equal("Café", e.Get(1, 0));
            Assert.Equal("Caf$", e.Pak.Get(1)[0]);
            var ex = Assert.Throws<FormatException>(() => e.Set(1, 0, "Pão"));
            Assert.Contains("accent table", ex.Message);
            Assert.True(e.Pak.NeedsNewTree);
        }

        [Fact]
        public void The_compiler_takes_mapped_letters_and_refuses_others_at_their_line()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            s.Accents.Set('$', 'é');
            var lib = ConversationLibrary.Open(s);
            var c = lib.Archive.Read(1);
            string text = lib.Decompile(c).Text;
            string ok = text.Replace("\"Hast thou come to add to my torment?\" #1", "\"Café?\" #1");
            var r = lib.Compile(c, ok);
            Assert.True(r.Ok, string.Join("; ", r.Errors));
            Assert.Equal("Café?", r.AddedStrings.Single().Text);
            lib.Apply(r);
            Assert.Contains("Caf$?", lib.StringsOf(c));
            Assert.Contains("Café?", lib.ShownStringsOf(c));

            string bad = text.Replace("\"Hast thou come to add to my torment?\" #1", "\"Pão?\" #1");
            var r2 = lib.Compile(lib.Archive.Read(1), bad);
            Assert.False(r2.Ok);
            Assert.Contains(r2.Errors, err => err.Message.Contains("accent table") && err.Line > 1);
        }
    }
}
