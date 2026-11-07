using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Graphics
{
    /// <summary>One character of a font: its rows (pixel x is bit RowBytes * 8 - 1 - x, as the
    /// file has them, the first byte the highest), and its width, the pixels the text moves on.</summary>
    public sealed class FontGlyph
    {
        public int Width;
        public uint[] Rows;
        /// <summary>The bytes after the width when a font's widthsize is over 1 (none shipped).</summary>
        public byte[] Extra = new byte[0];
        /// <summary>False for a code the file has no glyph for (it is shorter than 128 glyphs).</summary>
        public bool Present = true;

        public FontGlyph Clone() { return new FontGlyph { Width = Width, Rows = (uint[])Rows.Clone(), Extra = (byte[])Extra.Clone(), Present = Present }; }

        public bool IsBlank { get { return Rows.All(r => r == 0); } }

        public bool SameAs(FontGlyph o)
        {
            return o != null && Width == o.Width && Present == o.Present && Rows.SequenceEqual(o.Rows) && Extra.SequenceEqual(o.Extra);
        }
    }

    /// <summary>The six fonts, the same files in both games, and where the game uses them.</summary>
    public static class FontFiles
    {
        public static readonly string[] Names = { "FONT5X6P.SYS", "FONT5X6I.SYS", "FONTBIG.SYS", "FONTCHAR.SYS", "FONT4X5P.SYS", "FONTBUTN.SYS" };

        public static string Use(string name)
        {
            switch ((name ?? "").ToUpperInvariant())
            {
                case "FONT5X6P.SYS": return "The scroll: messages, conversations, books, the inventory, cutscene subtitles";
                case "FONT5X6I.SYS": return "Stats and skills";
                case "FONTBIG.SYS": return "Menus and cutscene titles";
                case "FONTCHAR.SYS": return "Character creation and skills";
                case "FONT4X5P.SYS": return "Map notes and labels (capitals and symbols only)";
                case "FONTBUTN.SYS": return "Buttons (the 26 capitals)";
                default: return "";
            }
        }
    }

    /// <summary>
    /// A font, DATA\FONT*.SYS (both games): a 12-byte header of six words (widthsize, the bytes
    /// after each glyph's rows; charsize, the bytes of a glyph's rows; the space width; the
    /// height; the bytes a row; the widest glyph), then each glyph's rows (1 bit a pixel, the
    /// highest bit leftmost) and its width byte.
    ///
    /// The game (uw1 gfx/GRFX.C grfx_load_font) always reads (charsize + widthsize) × 128 bytes,
    /// so every font has 128 codes, 0-127; gfx/GRLIBI.ASM setup_font points the 128 entries of the
    /// glyph table (4D16) into the buffer at DS:1C56 (UW2 1C54) in steps of
    /// (height &lt;&lt; (bytes a row − 1)) + widthsize. The shipped files are shorter (FONT4X5P has 97
    /// glyphs, FONTBUTN 94, the others 127): the codes past the end read whatever an earlier font
    /// left in the buffer. This model keeps the file's length (Count) so a file is written back byte
    /// for byte; the editor writes all 128 when it saves.
    ///
    /// Limits found in the game: the glyphs' buffer ends where the shadow and text colours start
    /// (GRCORE.ASM's _DATA lists bytefont and the colours): UW1 1C56..2D33, 0x10DE bytes (GRLIBI.ASM
    /// reads the shadow colour at 2D34); UW2 1C54..2D35, 0x10E2 bytes (its GRLIBI.ASM reads it at
    /// 2D36; its C pointers are 8 lower, 1C4C and 2D2E). So in both a glyph is at most 33 bytes
    /// (128 × 34 = 0x1100 would run into the colours). setup_font's shift only works out for 1
    /// or 2 bytes a row, and the rasteriser (GRLIBI.ASM 4AA5) has unrolled loops for heights up
    /// to 16. The rasteriser draws any code outside 20h..7Ah as '?' (4B2C: cmp al,20h / jl,
    /// cmp al,7Ah / jle), signed, so bytes of 128 and up too, while string_width (4C45) still
    /// adds the code's own width.
    /// </summary>
    public sealed class UwFont
    {
        public const int Codes = 128;
        /// <summary>The most bytes a glyph (rows and width) may have: 0x10DE (UW2 0x10E2) / 128, rounded down.</summary>
        public const int MaxGlyphBytes = 33;
        /// <summary>The codes the game draws (others come out as '?').</summary>
        public const int FirstDrawn = 0x20, LastDrawn = 0x7A;

        public string Name = "";
        public int WidthSize = 1;
        public int SpaceWidth;
        public int Height;
        public int RowBytes;
        public int MaxWidth;
        /// <summary>How many glyphs the file holds (the shipped files have fewer than 128).</summary>
        public int Count;
        /// <summary>Bytes after the last whole glyph, kept as they were.</summary>
        public byte[] Tail = new byte[0];
        public readonly FontGlyph[] Glyphs = new FontGlyph[Codes];

        public int CharSize { get { return Height * RowBytes; } }
        public int GlyphBytes { get { return CharSize + WidthSize; } }
        /// <summary>Pixels a row holds.</summary>
        public int Columns { get { return RowBytes * 8; } }

        public static bool IsDrawn(int code) { return code >= FirstDrawn && code <= LastDrawn; }

        public static UwFont Load(string path)
        {
            var f = Read(File.ReadAllBytes(path));
            f.Name = Path.GetFileName(path).ToUpperInvariant();
            return f;
        }

        public static UwFont Read(byte[] b)
        {
            if (b == null || b.Length < 12) throw new InvalidDataException("A font file has a 12-byte header; this one is shorter.");
            int W(int i) { return b[i] | b[i + 1] << 8; }
            var f = new UwFont { WidthSize = W(0), SpaceWidth = W(4), Height = W(6), RowBytes = W(8), MaxWidth = W(10) };
            int charSize = W(2);
            if (f.WidthSize < 1 || f.WidthSize > 4) throw new InvalidDataException("The font's widthsize is " + f.WidthSize + "; the game's fonts have 1.");
            if (f.RowBytes < 1 || f.RowBytes > 2) throw new InvalidDataException("The font has " + f.RowBytes + " bytes a row; the game handles 1 or 2.");
            if (f.Height < 1 || f.Height > 16) throw new InvalidDataException("The font is " + f.Height + " rows high; the game handles 1 to 16.");
            if (charSize != f.Height * f.RowBytes) throw new InvalidDataException(string.Format("The font's charsize is {0}, not height × bytes a row ({1}).", charSize, f.Height * f.RowBytes));
            int gb = f.GlyphBytes;
            f.Count = Math.Min(Codes, (b.Length - 12) / gb);
            int end = 12 + f.Count * gb;
            f.Tail = b.Skip(end).ToArray();
            for (int c = 0; c < Codes; c++)
            {
                var g = new FontGlyph { Rows = new uint[f.Height], Extra = new byte[f.WidthSize - 1], Present = c < f.Count };
                if (g.Present)
                {
                    int o = 12 + c * gb;
                    for (int y = 0; y < f.Height; y++)
                    {
                        uint r = 0;
                        for (int k = 0; k < f.RowBytes; k++) r = r << 8 | b[o + y * f.RowBytes + k];
                        g.Rows[y] = r;
                    }
                    g.Width = b[o + charSize];
                    for (int k = 1; k < f.WidthSize; k++) g.Extra[k - 1] = b[o + charSize + k];
                }
                f.Glyphs[c] = g;
            }
            return f;
        }

        /// <summary>A blank font of this shape, all 128 glyphs present.</summary>
        public static UwFont Blank(int height, int rowBytes, int spaceWidth, int maxWidth)
        {
            var f = new UwFont { Height = height, RowBytes = rowBytes, SpaceWidth = spaceWidth, MaxWidth = maxWidth, Count = Codes };
            for (int c = 0; c < Codes; c++) f.Glyphs[c] = new FontGlyph { Rows = new uint[height] };
            return f;
        }

        public UwFont Clone()
        {
            var f = new UwFont { Name = Name, WidthSize = WidthSize, SpaceWidth = SpaceWidth, Height = Height, RowBytes = RowBytes, MaxWidth = MaxWidth, Count = Count, Tail = (byte[])Tail.Clone() };
            for (int c = 0; c < Codes; c++) f.Glyphs[c] = Glyphs[c].Clone();
            return f;
        }

        /// <summary>The problems that would stop the game using this font (empty when none).</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            if (GlyphBytes > MaxGlyphBytes)
                p.Add(string.Format("A glyph takes {0} bytes; the game's buffer holds 128 glyphs of at most {1} (gfx/GRLIBI.ASM: the glyphs at DS:1C56 end at the text colours, DS:2D34).", GlyphBytes, MaxGlyphBytes));
            if (RowBytes < 1 || RowBytes > 2) p.Add("The game handles 1 or 2 bytes a row.");
            if (Height < 1 || Height > 16) p.Add("The game handles heights of 1 to 16 rows.");
            for (int c = 0; c < Codes; c++)
                if (Glyphs[c].Present && (Glyphs[c].Width < 0 || Glyphs[c].Width > 255)) p.Add("Code " + c + " has a width outside 0-255.");
            if (SpaceWidth < 0 || SpaceWidth > 0xFFFF || MaxWidth < 0 || MaxWidth > 0xFFFF) p.Add("The space width and the widest glyph are words (0-65535).");
            return p;
        }

        /// <summary>The file: the header, then Count glyphs, then the tail. Throws when the game could not use it.</summary>
        public byte[] ToBytes()
        {
            var problems = Problems();
            if (problems.Count > 0) throw new InvalidDataException(string.Join("\n", problems));
            var o = new MemoryStream();
            void W(int v) { o.WriteByte((byte)v); o.WriteByte((byte)(v >> 8)); }
            W(WidthSize); W(CharSize); W(SpaceWidth); W(Height); W(RowBytes); W(MaxWidth);
            for (int c = 0; c < Count; c++)
            {
                var g = Glyphs[c];
                for (int y = 0; y < Height; y++)
                    for (int k = RowBytes - 1; k >= 0; k--) o.WriteByte((byte)(g.Rows[y] >> (8 * k)));
                o.WriteByte((byte)g.Width);
                for (int k = 0; k < WidthSize - 1; k++) o.WriteByte(k < g.Extra.Length ? g.Extra[k] : (byte)0);
            }
            o.Write(Tail, 0, Tail.Length);
            return o.ToArray();
        }

        /// <summary>Makes every code present (blank, width 0, where the file had none) so the
        /// game never reads glyphs another font left in its buffer.</summary>
        public void FillTo128()
        {
            for (int c = 0; c < Codes; c++)
                if (!Glyphs[c].Present) { Glyphs[c] = new FontGlyph { Rows = new uint[Height], Extra = new byte[WidthSize - 1], Present = true }; }
            Count = Codes;
            Tail = new byte[0];
        }

        public bool Pixel(int code, int x, int y)
        {
            if (x < 0 || x >= Columns || y < 0 || y >= Height) return false;
            return (Glyphs[code].Rows[y] >> (Columns - 1 - x) & 1) != 0;
        }

        public void SetPixel(int code, int x, int y, bool on)
        {
            if (x < 0 || x >= Columns || y < 0 || y >= Height) return;
            var g = Glyphs[code];
            uint bit = 1u << (Columns - 1 - x);
            g.Rows[y] = on ? g.Rows[y] | bit : g.Rows[y] & ~bit;
            if (!g.Present) Grow(code);
            g.Present = true;
        }

        /// <summary>Puts a glyph at a code (a copy, fitted to this font's rows: cut or padded at the
        /// bottom and right). Codes up to it become present.</summary>
        public void SetGlyph(int code, FontGlyph g, int columnsOfSource)
        {
            var n = new FontGlyph { Rows = new uint[Height], Extra = new byte[WidthSize - 1], Present = true, Width = Math.Min(g.Width, Columns) };
            for (int y = 0; y < Height && y < g.Rows.Length; y++)
                for (int x = 0; x < Columns && x < columnsOfSource; x++)
                    if ((g.Rows[y] >> (columnsOfSource - 1 - x) & 1) != 0) n.Rows[y] |= 1u << (Columns - 1 - x);
            Glyphs[code] = n;
            Grow(code);
        }

        /// <summary>Makes the codes up to this one present (the file grows to hold it; the bytes after the old end go).</summary>
        void Grow(int code)
        {
            if (code < Count) return;
            for (int c = Count; c <= code; c++) Glyphs[c].Present = true;
            Count = code + 1;
            Tail = new byte[0];
        }

        /// <summary>The widest glyph's width (for the header's max width).</summary>
        public int WidestGlyph() { return Glyphs.Where(g => g.Present).Select(g => g.Width).DefaultIfEmpty(0).Max(); }

        /// <summary>The code the game actually draws for a byte (the rasteriser's '?').</summary>
        public static int DrawnCode(int b) { return b >= FirstDrawn && b <= LastDrawn ? b : '?'; }

        /// <summary>A line's width in pixels as string_width counts it: the codes' own widths, at most 54 characters.</summary>
        public int StringWidth(IEnumerable<int> codes)
        {
            int w = 0, n = 0;
            foreach (int c in codes)
            {
                if (c == 0 || n++ >= 54) break;
                int k = c & 0x7F;
                w += Glyphs[k].Width;
            }
            return w;
        }

        /// <summary>
        /// A line as the game draws it, one bool a pixel (width × Height): each glyph's rows ORed
        /// in at the running position (pixels past a glyph's width overlap the next one), codes
        /// the game does not draw shown as its '?'. Widths are the drawn glyph's, so this shows
        /// what is on screen; StringWidth is what the game measures.
        /// </summary>
        public bool[,] Render(IList<int> codes, out int width)
        {
            width = 0;
            foreach (int c in codes) width += Glyphs[DrawnCode(c)].Width;
            int total = width + Columns;
            var px = new bool[total, Height];
            int at = 0;
            foreach (int c in codes)
            {
                int d = DrawnCode(c);
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Columns; x++)
                        if (Pixel(d, x, y)) px[at + x, y] = true;
                at += Glyphs[d].Width;
            }
            return px;
        }
    }

    /// <summary>The marks "Make accented letter" adds, each drawn for small, middle and big fonts.</summary>
    public enum AccentMark { Grave, Acute, Circumflex, Tilde, Diaeresis, Cedilla, Ring }

    /// <summary>
    /// Builds an accented letter from a base glyph: the mark is put over the letter's ink (under
    /// it for the cedilla), centred on its columns, in the tallest drawing of the mark that fits
    /// the free rows. A capital in a small font has no free row above: then the smallest drawing
    /// replaces the top row, to be touched up by hand.
    /// </summary>
    public static class AccentMaker
    {
        // Each mark: drawings from 1 row up, rows of '#' and '.', for three sizes.
        static readonly Dictionary<AccentMark, string[][]> marks = new Dictionary<AccentMark, string[][]>
        {
            // small (height up to 7), middle (up to 11), big
            [AccentMark.Grave] = new[] { new[] { "#.", "#.|.#" }, new[] { "#.", "#.|.#", "#..|.#.|..#" }, new[] { "##.", "##..|..##", "##..|.##.|..##" } },
            [AccentMark.Acute] = new[] { new[] { ".#", ".#|#." }, new[] { ".#", ".#|#.", "..#|.#.|#.." }, new[] { ".##", "..##|##..", "..##|.##.|##.." } },
            [AccentMark.Circumflex] = new[] { new[] { ".#.", ".#.|#.#" }, new[] { ".#.", ".#.|#.#", "..#..|.#.#.|#...#" }, new[] { ".##.", ".##.|#..#", "..##..|.#..#.|#....#" } },
            [AccentMark.Tilde] = new[] { new[] { "##", ".#.#|#.#." }, new[] { "##", ".#.#|#.#.", ".##.#|#.##." }, new[] { "###", ".##..#|#..##.", ".##...|#..#.#|....#." } },
            [AccentMark.Diaeresis] = new[] { new[] { "#.#" }, new[] { "#.#", "#..#" }, new[] { "##.##", "##..##|##..##" } },
            [AccentMark.Cedilla] = new[] { new[] { "#", ".#|#." }, new[] { "#", ".#|#.", ".#.|..#|##." }, new[] { "##", ".##|##.", ".##|..#|##." } },
            [AccentMark.Ring] = new[] { new[] { "#", "##|##" }, new[] { "#", "##|##", ".#.|#.#|.#." }, new[] { "##", ".##.|.##.", ".##.|#..#|.##." } },
        };

        public static string Label(AccentMark m)
        {
            switch (m)
            {
                case AccentMark.Grave: return "Grave (à)";
                case AccentMark.Acute: return "Acute (á)";
                case AccentMark.Circumflex: return "Circumflex (â)";
                case AccentMark.Tilde: return "Tilde (ã)";
                case AccentMark.Diaeresis: return "Diaeresis (ä)";
                case AccentMark.Cedilla: return "Cedilla (ç)";
                default: return "Ring (å)";
            }
        }

        static int SizeClass(int height) { return height <= 7 ? 0 : height <= 11 ? 1 : 2; }

        static bool[,] Parse(string s)
        {
            var rows = s.Split('|');
            int w = rows.Max(r => r.Length);
            var m = new bool[w, rows.Length];
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++) m[x, y] = rows[y][x] == '#';
            return m;
        }

        /// <summary>The base glyph with the mark added (a new glyph; the font is not changed).</summary>
        public static FontGlyph Make(UwFont font, int baseCode, AccentMark mark)
        {
            var g = font.Glyphs[baseCode].Clone();
            g.Present = true;
            int cols = font.Columns, h = font.Height;
            int top = -1, bottom = -1, left = cols, right = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < cols; x++)
                    if ((g.Rows[y] >> (cols - 1 - x) & 1) != 0)
                    {
                        if (top < 0) top = y;
                        bottom = y;
                        left = Math.Min(left, x); right = Math.Max(right, x);
                    }
            if (top < 0) { top = h; bottom = -1; left = 0; right = Math.Max(0, g.Width - 2); }
            var drawings = marks[mark][SizeClass(h)];
            bool below = mark == AccentMark.Cedilla;
            int room = below ? h - 1 - bottom : top;
            // the tallest drawing that fits with a gap row, else without one, else the smallest over the letter
            int pick = -1, gap = 0;
            for (int i = drawings.Length - 1; i >= 0 && pick < 0; i--)
                if (Parse(drawings[i]).GetLength(1) + 1 <= room) { pick = i; gap = 1; }
            for (int i = drawings.Length - 1; i >= 0 && pick < 0; i--)
                if (Parse(drawings[i]).GetLength(1) <= room) { pick = i; gap = 0; }
            bool over = pick < 0;
            if (over) pick = 0;
            var m = Parse(drawings[pick]);
            int mw = m.GetLength(0), mh = m.GetLength(1);
            // a cedilla hangs straight from the letter; marks above keep the gap row when there is one
            int y0 = below ? (over ? h - mh : bottom + 1) : (over ? 0 : top - gap - mh);
            int centre2 = left + right;            // twice the ink's centre
            int x0 = (centre2 - (mw - 1) + 1) / 2;
            if (x0 < 0) x0 = 0;
            if (x0 + mw > cols) x0 = Math.Max(0, cols - mw);
            if (over)
                for (int y = y0; y < y0 + mh && y < h; y++)
                    if (y >= 0) g.Rows[y] = 0;
            for (int y = 0; y < mh; y++)
                for (int x = 0; x < mw; x++)
                    if (m[x, y] && y0 + y >= 0 && y0 + y < h && x0 + x < cols) g.Rows[y0 + y] |= 1u << (cols - 1 - (x0 + x));
            if (x0 + mw + 1 > g.Width) g.Width = Math.Min(cols, Math.Max(g.Width, x0 + mw + 1));
            return g;
        }

        /// <summary>The base letter and mark of a Unicode letter, when it is one of the marks here
        /// ('é' → 'e', Acute). False for anything else.</summary>
        public static bool Decompose(char letter, out char baseLetter, out AccentMark mark)
        {
            baseLetter = letter; mark = AccentMark.Acute;
            string d = letter.ToString().Normalize(System.Text.NormalizationForm.FormD);
            if (d.Length != 2 || d[0] > 0x7F) return false;
            baseLetter = d[0];
            switch (d[1])
            {
                case '̀': mark = AccentMark.Grave; return true;
                case '́': mark = AccentMark.Acute; return true;
                case '̂': mark = AccentMark.Circumflex; return true;
                case '̃': mark = AccentMark.Tilde; return true;
                case '̈': mark = AccentMark.Diaeresis; return true;
                case '̧': mark = AccentMark.Cedilla; return true;
                case '̊': mark = AccentMark.Ring; return true;
                default: return false;
            }
        }
    }

    /// <summary>The six fonts of a game folder, loaded once.</summary>
    public sealed class GameFonts
    {
        public readonly Dictionary<string, UwFont> ByName = new Dictionary<string, UwFont>(StringComparer.OrdinalIgnoreCase);

        public static GameFonts Load(string dataFolder)
        {
            var g = new GameFonts();
            foreach (var n in FontFiles.Names)
            {
                string p = GameProfile.Find(dataFolder, n);
                if (p == null) continue;
                try { var f = UwFont.Load(p); f.Name = n; g.ByName[n] = f; }
                catch (InvalidDataException) { }
            }
            return g;
        }

        public UwFont this[string name] { get { UwFont f; return ByName.TryGetValue(name, out f) ? f : null; } }
    }
}
