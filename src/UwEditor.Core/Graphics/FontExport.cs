using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// A font as a PNG sheet: 16 × 8 cells, code 0 at the top left, code 16 starting the second
    /// row. Each cell is the glyph's columns (the font's, or more when a glyph is wider) by its
    /// rows plus one, black ink on white, and the row under the glyph marks its width in red
    /// (a red pixel for each column the text moves on). Cells are parted by grey lines one pixel
    /// wide, the sheet starting and ending with one.
    /// </summary>
    public static class FontSheet
    {
        const uint Paper = 0xFFFFFFFF, Black = 0xFF000000, Red = 0xFFE02020, Grey = 0xFFB0B0B0;

        public static int CellWidth(UwFont f) { return Math.Max(f.Columns, f.WidestGlyph()); }

        public static byte[] Export(UwFont f)
        {
            int cw = CellWidth(f), ch = f.Height + 1;
            int w = 1 + 16 * (cw + 1), h = 1 + 8 * (ch + 1);
            var px = new uint[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Grey;
            for (int c = 0; c < UwFont.Codes; c++)
            {
                int ox = 1 + (c % 16) * (cw + 1), oy = 1 + (c / 16) * (ch + 1);
                var g = f.Glyphs[c];
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        uint v = Paper;
                        if (y < f.Height) { if (f.Pixel(c, x, y)) v = Black; }
                        else if (g.Present && x < g.Width) v = Red;
                        px[(oy + y) * w + ox + x] = v;
                    }
            }
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < px.Length; i++)
            {
                rgba[4 * i] = (byte)(px[i] >> 16); rgba[4 * i + 1] = (byte)(px[i] >> 8); rgba[4 * i + 2] = (byte)px[i]; rgba[4 * i + 3] = 255;
            }
            return Png.EncodeRgba(w, h, rgba);
        }

        /// <summary>Reads a sheet into a copy of the font (its shape kept): ink is any dark pixel,
        /// a width mark any pixel of the width row that is not white. Throws when the sheet's size
        /// does not fit the font. warnings gets ink cut off at the font's columns.</summary>
        public static UwFont Import(UwFont shape, byte[] png, List<string> warnings = null)
        {
            var img = Png.Decode(png);
            int ch = shape.Height + 1;
            if (img.Height != 1 + 8 * (ch + 1))
                throw new InvalidDataException(string.Format("The sheet is {0} pixels high; for a font {1} rows high it is {2} (8 rows of cells of {1} + 1 rows, with lines between).", img.Height, shape.Height, 1 + 8 * (ch + 1)));
            if ((img.Width - 1) % 16 != 0 || (img.Width - 1) / 16 - 1 < shape.Columns)
                throw new InvalidDataException(string.Format("The sheet is {0} pixels wide; it should be 16 cells of at least {1} columns, with lines between ({2} or more).", img.Width, shape.Columns, 1 + 16 * (shape.Columns + 1)));
            int cw = (img.Width - 1) / 16 - 1;
            var f = shape.Clone();
            f.FillTo128();
            int cut = 0;
            for (int c = 0; c < UwFont.Codes; c++)
            {
                int ox = 1 + (c % 16) * (cw + 1), oy = 1 + (c / 16) * (ch + 1);
                var g = f.Glyphs[c];
                g.Present = true;
                Array.Clear(g.Rows, 0, g.Rows.Length);
                int width = 0;
                for (int x = 0; x < cw; x++)
                {
                    for (int y = 0; y < shape.Height; y++)
                        if (Dark(img, ox + x, oy + y))
                        {
                            if (x < f.Columns) g.Rows[y] |= 1u << (f.Columns - 1 - x);
                            else cut++;
                        }
                    if (!White(img, ox + x, oy + shape.Height)) width = x + 1;
                }
                g.Width = width;
            }
            if (cut > 0 && warnings != null) warnings.Add(cut + " pixels past the font's " + f.Columns + " columns were left out.");
            return f;
        }

        static void Rgb(PngImage img, int x, int y, out int r, out int g, out int b)
        {
            int i = 4 * (y * img.Width + x);
            int a = img.Rgba[i + 3];
            // transparent counts as white
            r = 255 - (255 - img.Rgba[i]) * a / 255; g = 255 - (255 - img.Rgba[i + 1]) * a / 255; b = 255 - (255 - img.Rgba[i + 2]) * a / 255;
        }

        static bool Dark(PngImage img, int x, int y) { int r, g, b; Rgb(img, x, y, out r, out g, out b); return r * 299 + g * 587 + b * 114 < 128000; }
        static bool White(PngImage img, int x, int y) { int r, g, b; Rgb(img, x, y, out r, out g, out b); return r > 200 && g > 200 && b > 200; }
    }

    /// <summary>
    /// A font as BDF (Adobe's Glyph Bitmap Distribution Format 2.1, the text format X11 and
    /// font editors read). Each glyph is named uwNNN by its code and encoded at its character
    /// from the accent table (ASCII for 20h..7Eh; other codes with ENCODING -1 and the code),
    /// with its width as DWIDTH. The game's header (space width, max width, widthsize, glyph
    /// count, the bytes after the glyphs) goes into UW_ properties, so a font exported and read
    /// back is the same file byte for byte.
    /// </summary>
    public static class FontBdf
    {
        /// <summary>Rows under the baseline: under the bottom of 'x' (or 'a', 'A'), as the shipped fonts are drawn.</summary>
        public static int Descent(UwFont f)
        {
            foreach (int c in new[] { 'x', 'a', 'o', 'A', 'H' })
            {
                if (!f.Glyphs[c].Present || f.Glyphs[c].IsBlank) continue;
                int bottom = -1;
                for (int y = 0; y < f.Height; y++) if (f.Glyphs[c].Rows[y] != 0) bottom = y;
                return Math.Max(0, f.Height - 1 - bottom);
            }
            return 0;
        }

        static string Family(UwFont f) { return Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(f.Name) ? "FONT" : f.Name); }

        public static string Export(UwFont f, AccentTable accents)
        {
            accents = accents ?? new AccentTable();
            int descent = Descent(f), ascent = f.Height - descent;
            var sb = new StringBuilder();
            void L(string s) { sb.Append(s).Append('\n'); }
            string fam = Family(f);
            L("STARTFONT 2.1");
            L("COMMENT Ultima Underworld font " + f.Name + ", exported by the Ultima Underworld editor");
            L(string.Format(CultureInfo.InvariantCulture, "FONT -UW-{0}-Medium-R-Normal--{1}-{2}-75-75-P-{3}-ISO10646-1", fam, f.Height, f.Height * 10, f.Columns * 10));
            L(string.Format(CultureInfo.InvariantCulture, "SIZE {0} 75 75", f.Height));
            L(string.Format(CultureInfo.InvariantCulture, "FONTBOUNDINGBOX {0} {1} 0 {2}", f.Columns, f.Height, -descent));
            var props = new List<string>
            {
                "FOUNDRY \"UW\"", "FAMILY_NAME \"" + fam + "\"", "WEIGHT_NAME \"Medium\"", "SLANT \"R\"", "SETWIDTH_NAME \"Normal\"",
                "PIXEL_SIZE " + f.Height, "POINT_SIZE " + f.Height * 10, "RESOLUTION_X 75", "RESOLUTION_Y 75", "SPACING \"P\"",
                "CHARSET_REGISTRY \"ISO10646\"", "CHARSET_ENCODING \"1\"",
                "FONT_ASCENT " + ascent, "FONT_DESCENT " + descent, "DEFAULT_CHAR " + (int)'?',
                "UW_SPACE_WIDTH " + f.SpaceWidth, "UW_MAX_WIDTH " + f.MaxWidth, "UW_WIDTH_SIZE " + f.WidthSize,
                "UW_ROW_BYTES " + f.RowBytes, "UW_GLYPHS " + f.Count, "UW_TAIL \"" + Hex(f.Tail) + "\"",
            };
            L("STARTPROPERTIES " + props.Count);
            foreach (var p in props) L(p);
            L("ENDPROPERTIES");
            var codes = Enumerable.Range(0, UwFont.Codes).Where(c => f.Glyphs[c].Present).ToList();
            L("CHARS " + codes.Count);
            foreach (int c in codes)
            {
                var g = f.Glyphs[c];
                L("STARTCHAR uw" + c.ToString("D3", CultureInfo.InvariantCulture));
                int u = accents.UnicodeOf(c);
                L(u >= 0 ? "ENCODING " + u : "ENCODING -1 " + c);
                L("SWIDTH " + (f.Height == 0 ? 0 : g.Width * 1000 / f.Height) + " 0");
                L("DWIDTH " + g.Width + " 0");
                L(string.Format(CultureInfo.InvariantCulture, "BBX {0} {1} 0 {2}", f.Columns, f.Height, -descent));
                if (g.Extra.Length > 0) L("COMMENT UW_EXTRA " + Hex(g.Extra));
                L("BITMAP");
                foreach (var r in g.Rows) L(r.ToString(f.RowBytes == 1 ? "X2" : "X4", CultureInfo.InvariantCulture));
                L("ENDCHAR");
            }
            L("ENDFONT");
            return sb.ToString();
        }

        static string Hex(byte[] b) { return string.Concat(b.Select(x => x.ToString("X2", CultureInfo.InvariantCulture))); }
        static byte[] Unhex(string s)
        {
            s = (s ?? "").Trim().Trim('"');
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(s.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return b;
        }

        /// <summary>
        /// Reads a BDF. With the UW_ properties (a file this editor wrote) the result is the font
        /// as it was. Without them, the font is fitted into shape (the game file's height and
        /// bytes a row): glyphs are placed by their bounding boxes on the baseline, cut at the
        /// bottom and right; a glyph's code is its uwNNN name, else its ENCODING -1 code, else its
        /// character through the accent table. warnings gets what was left out.
        /// </summary>
        public static UwFont Import(string text, AccentTable accents, UwFont shape = null, List<string> warnings = null)
        {
            accents = accents ?? new AccentTable();
            warnings = warnings ?? new List<string>();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int bbW = 0, bbH = 0, bbX = 0, bbY = 0;
            int i = 0;
            string[] T(string s) { return s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries); }
            int N(string s) { return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture); }
            if (lines.Length == 0 || !lines[0].StartsWith("STARTFONT")) throw new InvalidDataException("Not a BDF file (it starts with STARTFONT).");
            var glyphs = new List<(string Name, int Enc, int Enc2, int Dw, int W, int H, int X, int Y, uint[] Rows, byte[] Extra)>();
            for (; i < lines.Length; i++)
            {
                var t = T(lines[i]);
                if (t.Length == 0) continue;
                switch (t[0])
                {
                    case "FONTBOUNDINGBOX": bbW = N(t[1]); bbH = N(t[2]); bbX = N(t[3]); bbY = N(t[4]); break;
                    case "STARTPROPERTIES":
                        for (i++; i < lines.Length && !lines[i].StartsWith("ENDPROPERTIES"); i++)
                        {
                            var l = lines[i].Trim();
                            int sp = l.IndexOf(' ');
                            if (sp > 0) props[l.Substring(0, sp)] = l.Substring(sp + 1).Trim();
                        }
                        break;
                    case "STARTCHAR":
                        {
                            string name = lines[i].Substring(9).Trim();
                            int enc = -1, enc2 = -1, dw = 0, w = bbW, h = bbH, x = bbX, y = bbY;
                            var rows = new List<uint>();
                            byte[] extra = new byte[0];
                            for (i++; i < lines.Length && !lines[i].StartsWith("ENDCHAR"); i++)
                            {
                                var u = T(lines[i]);
                                if (u.Length == 0) continue;
                                if (u[0] == "ENCODING") { enc = N(u[1]); if (u.Length > 2) enc2 = N(u[2]); }
                                else if (u[0] == "DWIDTH") dw = N(u[1]);
                                else if (u[0] == "BBX") { w = N(u[1]); h = N(u[2]); x = N(u[3]); y = N(u[4]); }
                                else if (u[0] == "COMMENT" && u.Length > 2 && u[1] == "UW_EXTRA") extra = Unhex(u[2]);
                                else if (u[0] == "BITMAP")
                                {
                                    for (i++; i < lines.Length && !lines[i].StartsWith("ENDCHAR"); i++)
                                    {
                                        string hx = lines[i].Trim();
                                        if (hx.Length == 0) continue;
                                        // left-aligned bits: keep the leftmost 32 columns
                                        if (hx.Length > 8) hx = hx.Substring(0, 8);
                                        uint v = uint.Parse(hx, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                        v <<= 4 * (8 - hx.Length);
                                        rows.Add(v);
                                    }
                                    i--;
                                }
                            }
                            glyphs.Add((name, enc, enc2, dw, w, h, x, y, rows.ToArray(), extra));
                            break;
                        }
                }
            }
            string P(string k) { string v; return props.TryGetValue(k, out v) ? v.Trim('"') : null; }
            int descent = P("FONT_DESCENT") != null ? N(P("FONT_DESCENT")) : -bbY;
            UwFont f;
            bool ours = P("UW_ROW_BYTES") != null && P("UW_GLYPHS") != null;
            if (ours)
            {
                f = UwFont.Blank(bbH, N(P("UW_ROW_BYTES")), N(P("UW_SPACE_WIDTH") ?? "0"), N(P("UW_MAX_WIDTH") ?? "0"));
                f.WidthSize = N(P("UW_WIDTH_SIZE") ?? "1");
                for (int c = 0; c < UwFont.Codes; c++) { f.Glyphs[c].Present = false; f.Glyphs[c].Extra = new byte[f.WidthSize - 1]; }
                f.Count = N(P("UW_GLYPHS"));
                f.Tail = Unhex(P("UW_TAIL"));
            }
            else
            {
                if (shape == null) throw new InvalidDataException("This BDF was not written by the editor: it can only be read into one of the game's fonts.");
                f = shape.Clone();
                for (int c = 0; c < UwFont.Codes; c++) { f.Glyphs[c] = new FontGlyph { Rows = new uint[f.Height], Extra = new byte[f.WidthSize - 1], Present = false }; }
                f.Count = 0; f.Tail = new byte[0];
                if (bbH != f.Height) warnings.Add(string.Format("The BDF is {0} rows high; the game's font is {1}: glyphs were placed on its baseline and cut to fit.", bbH, f.Height));
            }
            int fDescent = ours ? descent : FontBdf.Descent(shape);
            int baseline = f.Height - fDescent;   // the row just under the baseline
            int skipped = 0, cut = 0;
            foreach (var g in glyphs)
            {
                int code = -1;
                if (g.Name.StartsWith("uw") && int.TryParse(g.Name.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out code) && code >= 0 && code < UwFont.Codes) { }
                else if (g.Enc == -1 && g.Enc2 >= 0 && g.Enc2 < UwFont.Codes) code = g.Enc2;
                else code = accents.CodeOfUnicode(g.Enc);
                if (code < 0 || code >= UwFont.Codes) { skipped++; continue; }
                var n = f.Glyphs[code];
                n.Present = true;
                n.Width = Math.Max(0, Math.Min(255, g.Dw));
                if (g.Extra.Length == n.Extra.Length) n.Extra = g.Extra;
                Array.Clear(n.Rows, 0, n.Rows.Length);
                // glyph row r is at height g.Y + g.H - 1 - r above the baseline
                for (int r = 0; r < g.Rows.Length && r < g.H; r++)
                {
                    int row = baseline - (g.Y + g.H - r);
                    for (int x = 0; x < g.W && x < 32; x++)
                    {
                        if ((g.Rows[r] >> (31 - x) & 1) == 0) continue;
                        int col = g.X + x;
                        if (row < 0 || row >= f.Height || col < 0 || col >= f.Columns) { cut++; continue; }
                        n.Rows[row] |= 1u << (f.Columns - 1 - col);
                    }
                }
                if (!ours && code >= f.Count) f.Count = code + 1;
            }
            if (!ours)
            {
                for (int c = 0; c < f.Count; c++) f.Glyphs[c].Present = true;
                if (skipped > 0) warnings.Add(skipped + " glyphs had no code (no uwNNN name, and their characters are not ASCII or in the accent table) and were left out.");
            }
            if (cut > 0) warnings.Add(cut + " pixels outside the font's " + f.Columns + " × " + f.Height + " cells were left out.");
            return f;
        }
    }

    /// <summary>
    /// A font as TrueType, for installing in Windows, macOS or Linux: each pixel is a square
    /// (runs of pixels in a row joined into one rectangle), no hinting; 128 font units a pixel.
    /// Characters are at their letters through the accent table, ASCII for 20h..7Eh, and the
    /// other codes at U+E000 + code (the private use area). Tables: cmap (formats 4), glyf,
    /// head, hhea, hmtx, loca, maxp, name, OS/2, post (format 3).
    /// </summary>
    public static class FontTtf
    {
        public const int Unit = 128;

        public static int UnicodeOf(int code, AccentTable accents)
        {
            int u = (accents ?? new AccentTable()).UnicodeOf(code);
            return u >= 0 ? u : 0xE000 + code;
        }

        public static string FamilyName(UwFont f, string game)
        {
            string n = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(f.Name) ? "FONT" : f.Name);
            return (string.IsNullOrEmpty(game) ? "Underworld " : game + " ") + n;
        }

        sealed class Glyph { public int Advance; public List<int[]> Rects = new List<int[]>(); }

        public static byte[] Export(UwFont f, AccentTable accents, string family)
        {
            int descent = FontBdf.Descent(f), ascent = f.Height - descent;
            // glyph 0 .notdef (a box), then each present code
            var glyphs = new List<Glyph>();
            var nd = new Glyph { Advance = Math.Max(2, f.Columns / 2 + 1) * Unit };
            int bw = nd.Advance / Unit - 1;
            nd.Rects.Add(new[] { 0, 0, bw, 1 }); nd.Rects.Add(new[] { 0, ascent - 1, bw, ascent });
            nd.Rects.Add(new[] { 0, 1, 1, ascent - 1 }); nd.Rects.Add(new[] { bw - 1, 1, bw, ascent - 1 });
            glyphs.Add(nd);
            var map = new SortedDictionary<int, int>();
            for (int c = 0; c < UwFont.Codes; c++)
            {
                var g = f.Glyphs[c];
                if (!g.Present) continue;
                int u = UnicodeOf(c, accents);
                if (map.ContainsKey(u) || u > 0xFFFF) continue;
                var t = new Glyph { Advance = g.Width * Unit };
                for (int y = 0; y < f.Height; y++)
                {
                    int top = ascent - y;      // in pixels, y up
                    int x = 0;
                    while (x < f.Columns)
                    {
                        if (!f.Pixel(c, x, y)) { x++; continue; }
                        int s = x;
                        while (x < f.Columns && f.Pixel(c, x, y)) x++;
                        t.Rects.Add(new[] { s, top - 1, x, top });   // x0, y0, x1, y1 in pixels
                    }
                }
                map[u] = glyphs.Count;
                glyphs.Add(t);
            }

            // glyf and loca
            var glyf = new Bin();
            var loca = new List<int>();
            int xMin = 0, yMin = 0, xMax = 0, yMax = 0, maxPoints = 0, maxContours = 0;
            bool any = false;
            var lsb = new List<int>();
            foreach (var g in glyphs)
            {
                loca.Add(glyf.Length);
                if (g.Rects.Count == 0) { lsb.Add(0); continue; }
                int gx0 = g.Rects.Min(r => r[0]) * Unit, gy0 = g.Rects.Min(r => r[1]) * Unit, gx1 = g.Rects.Max(r => r[2]) * Unit, gy1 = g.Rects.Max(r => r[3]) * Unit;
                lsb.Add(gx0);
                if (!any) { xMin = gx0; yMin = gy0; xMax = gx1; yMax = gy1; any = true; }
                else { xMin = Math.Min(xMin, gx0); yMin = Math.Min(yMin, gy0); xMax = Math.Max(xMax, gx1); yMax = Math.Max(yMax, gy1); }
                maxPoints = Math.Max(maxPoints, g.Rects.Count * 4);
                maxContours = Math.Max(maxContours, g.Rects.Count);
                glyf.I16(g.Rects.Count); glyf.I16(gx0); glyf.I16(gy0); glyf.I16(gx1); glyf.I16(gy1);
                for (int k = 0; k < g.Rects.Count; k++) glyf.U16(k * 4 + 3);
                glyf.U16(0);   // no instructions
                var pts = new List<int[]>();
                foreach (var r in g.Rects)
                {
                    // clockwise (TrueType's outer contours): up the left side, along the top, down the right
                    int x0 = r[0] * Unit, y0 = r[1] * Unit, x1 = r[2] * Unit, y1 = r[3] * Unit;
                    pts.Add(new[] { x0, y0 }); pts.Add(new[] { x0, y1 }); pts.Add(new[] { x1, y1 }); pts.Add(new[] { x1, y0 });
                }
                foreach (var p in pts) glyf.U8(0x01);   // on the curve, x and y as 16-bit deltas
                int px = 0;
                foreach (var p in pts) { glyf.I16(p[0] - px); px = p[0]; }
                int py = 0;
                foreach (var p in pts) { glyf.I16(p[1] - py); py = p[1]; }
                glyf.Pad4();
            }
            loca.Add(glyf.Length);
            var locaB = new Bin();
            foreach (int o in loca) locaB.U32((uint)o);

            int upem = f.Height * Unit;
            int advMax = glyphs.Max(g => g.Advance);
            var head = new Bin();
            head.U32(0x00010000); head.U32(0x00010000);
            head.U32(0);                 // checkSumAdjustment, filled in at the end
            head.U32(0x5F0F3CF5);
            head.U16(0x000B);            // baseline at y=0, lsb at x=0, integer scaling
            head.U16(upem);
            head.I64(Seconds1904()); head.I64(Seconds1904());
            head.I16(xMin); head.I16(yMin); head.I16(xMax); head.I16(yMax);
            head.U16(0);                 // macStyle: regular
            head.U16(Math.Max(1, f.Height)); // lowestRecPPEM
            head.I16(2);                 // fontDirectionHint
            head.I16(1);                 // long loca
            head.I16(0);

            var hhea = new Bin();
            hhea.U32(0x00010000);
            hhea.I16(ascent * Unit); hhea.I16(-descent * Unit); hhea.I16(0);
            hhea.U16(advMax);
            hhea.I16(any ? lsb.Min() : 0);
            hhea.I16(glyphs.Select((g, k) => g.Rects.Count == 0 ? 0 : g.Advance - g.Rects.Max(r => r[2]) * Unit).Min());
            hhea.I16(xMax);
            hhea.I16(1); hhea.I16(0); hhea.I16(0);
            for (int k = 0; k < 4; k++) hhea.I16(0);
            hhea.I16(0);
            hhea.U16(glyphs.Count);

            var hmtx = new Bin();
            for (int k = 0; k < glyphs.Count; k++) { hmtx.U16(glyphs[k].Advance); hmtx.I16(lsb[k]); }

            var maxp = new Bin();
            maxp.U32(0x00010000); maxp.U16(glyphs.Count);
            maxp.U16(maxPoints); maxp.U16(maxContours); maxp.U16(0); maxp.U16(0);
            maxp.U16(1); maxp.U16(0); maxp.U16(0); maxp.U16(0); maxp.U16(0); maxp.U16(0); maxp.U16(0); maxp.U16(0); maxp.U16(0);

            // cmap: format 4, one segment per character, for (3,1) and (0,3)
            var seg = map.Keys.ToList();
            var f4 = new Bin();
            int segs = seg.Count + 1;
            // searchRange = 2 × 2^floor(log2(segs))
            int p2 = 1, e2 = 0; while (p2 * 2 <= segs) { p2 *= 2; e2++; }
            f4.U16(4); f4.U16(16 + 8 * segs); f4.U16(0);
            f4.U16(segs * 2); f4.U16(p2 * 2); f4.U16(e2); f4.U16(segs * 2 - p2 * 2);
            foreach (int u in seg) f4.U16(u); f4.U16(0xFFFF);
            f4.U16(0);
            foreach (int u in seg) f4.U16(u); f4.U16(0xFFFF);
            foreach (int u in seg) f4.U16((map[u] - u) & 0xFFFF); f4.U16(1);
            foreach (int u in seg) f4.U16(0); f4.U16(0);
            var cmap = new Bin();
            cmap.U16(0); cmap.U16(2);
            cmap.U16(0); cmap.U16(3); cmap.U32(4 + 16);
            cmap.U16(3); cmap.U16(1); cmap.U32(4 + 16);
            cmap.Bytes(f4.ToArray());

            // name
            string version = "Version 1.000";
            string ps = new string(family.Where(ch => ch > 32 && ch < 127 && "[](){}<>/%".IndexOf(ch) < 0).ToArray());
            var names = new (int Id, string Text)[]
            {
                (0, "Glyphs from Ultima Underworld's " + f.Name + "; exported by the Ultima Underworld editor"),
                (1, family), (2, "Regular"), (3, family + " Regular"), (4, family), (5, version), (6, ps),
            };
            var name = new Bin();
            var strData = new Bin();
            var recs = new List<(int Pid, int Eid, int Lid, int Nid, byte[] Data)>();
            foreach (var n in names) recs.Add((1, 0, 0, n.Id, Encoding.ASCII.GetBytes(Ascii(n.Text))));
            foreach (var n in names) recs.Add((3, 1, 0x409, n.Id, Encoding.BigEndianUnicode.GetBytes(n.Text)));
            name.U16(0); name.U16(recs.Count); name.U16(6 + 12 * recs.Count);
            foreach (var r in recs)
            {
                name.U16(r.Pid); name.U16(r.Eid); name.U16(r.Lid); name.U16(r.Nid); name.U16(r.Data.Length); name.U16(strData.Length);
                strData.Bytes(r.Data);
            }
            name.Bytes(strData.ToArray());

            // OS/2, version 4
            var os2 = new Bin();
            int avg = (int)glyphs.Where(g => g.Advance > 0).Select(g => g.Advance).DefaultIfEmpty(Unit).Average();
            os2.U16(4); os2.I16(avg); os2.U16(400); os2.U16(5); os2.U16(0);   // fsType 0: installable
            int sub = upem * 2 / 3;
            os2.I16(sub); os2.I16(sub); os2.I16(0); os2.I16(Unit);
            os2.I16(sub); os2.I16(sub); os2.I16(0); os2.I16(upem / 2);
            os2.I16(Unit); os2.I16(ascent * Unit / 2);
            os2.I16(0);
            for (int k = 0; k < 10; k++) os2.U8(0);    // PANOSE: any
            uint ur1 = 1;                                // Basic Latin
            if (map.Keys.Any(u => u >= 0xA0 && u <= 0xFF)) ur1 |= 2;     // Latin-1 Supplement
            if (map.Keys.Any(u => u >= 0x100 && u <= 0x17F)) ur1 |= 4;   // Latin Extended-A
            // bit 60 (Private Use Area) is in ulUnicodeRange2 (bits 32-63)
            os2.U32(ur1); os2.U32(map.Keys.Any(u => u >= 0xE000 && u <= 0xF8FF) ? 1u << (60 - 32) : 0); os2.U32(0); os2.U32(0);
            os2.Bytes(Encoding.ASCII.GetBytes("UWED"));
            os2.U16(0x40);                               // fsSelection: regular
            os2.U16(seg.Count > 0 ? seg.First() : 0x20); os2.U16(seg.Count > 0 ? seg.Last() : 0x20);
            os2.I16(ascent * Unit); os2.I16(-descent * Unit); os2.I16(0);
            os2.U16(Math.Max(ascent * Unit, yMax)); os2.U16(Math.Max(descent * Unit, -yMin));
            os2.U32(1); os2.U32(0);                      // code page: Latin 1
            int xh = HeightOf(f, 'x', ascent), cap = HeightOf(f, 'H', ascent);
            os2.I16(xh * Unit); os2.I16(cap * Unit);
            os2.U16(0); os2.U16(0x20); os2.U16(1);

            // format 3 (no glyph names), no italic angle, the underline a pixel under the baseline, not fixed pitch
            var post = new Bin();
            post.U32(0x00030000); post.U32(0); post.I16(-Unit); post.I16(Unit); post.U32(0);
            post.U32(0); post.U32(0); post.U32(0); post.U32(0);

            var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["OS/2"] = os2.ToArray(), ["cmap"] = cmap.ToArray(), ["glyf"] = glyf.ToArray(), ["head"] = head.ToArray(),
                ["hhea"] = hhea.ToArray(), ["hmtx"] = hmtx.ToArray(), ["loca"] = locaB.ToArray(), ["maxp"] = maxp.ToArray(),
                ["name"] = name.ToArray(), ["post"] = post.ToArray(),
            };
            var font = new Bin();
            int nt = tables.Count;
            int pw = 1, ew = 0; while (pw * 2 <= nt) { pw *= 2; ew++; }
            font.U32(0x00010000); font.U16(nt); font.U16(pw * 16); font.U16(ew); font.U16(nt * 16 - pw * 16);
            int offset = 12 + 16 * nt;
            int headAt = 0;
            var body = new Bin();
            foreach (var kv in tables)
            {
                font.Bytes(Encoding.ASCII.GetBytes(kv.Key));
                font.U32(Checksum(kv.Value));
                if (kv.Key == "head") headAt = offset + body.Length;
                font.U32((uint)(offset + body.Length));
                font.U32((uint)kv.Value.Length);
                body.Bytes(kv.Value);
                body.Pad4();
            }
            font.Bytes(body.ToArray());
            var all = font.ToArray();
            uint adj = 0xB1B0AFBA - Checksum(all);
            all[headAt + 8] = (byte)(adj >> 24); all[headAt + 9] = (byte)(adj >> 16); all[headAt + 10] = (byte)(adj >> 8); all[headAt + 11] = (byte)adj;
            return all;
        }

        static int HeightOf(UwFont f, char c, int ascent)
        {
            var g = f.Glyphs[c];
            if (!g.Present) return ascent;
            for (int y = 0; y < f.Height; y++) if (g.Rows[y] != 0) return ascent - y;
            return ascent;
        }

        static string Ascii(string s) { return new string(s.Select(ch => ch < 128 ? ch : '?').ToArray()); }

        static long Seconds1904() { return (long)(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        static uint Checksum(byte[] b)
        {
            uint sum = 0;
            for (int i = 0; i < b.Length; i += 4)
            {
                uint v = 0;
                for (int k = 0; k < 4; k++) v = v << 8 | (i + k < b.Length ? b[i + k] : 0u);
                sum += v;
            }
            return sum;
        }

        sealed class Bin
        {
            readonly MemoryStream m = new MemoryStream();
            public int Length { get { return (int)m.Length; } }
            public void U8(int v) { m.WriteByte((byte)v); }
            public void U16(int v) { m.WriteByte((byte)(v >> 8)); m.WriteByte((byte)v); }
            public void I16(int v) { U16(v & 0xFFFF); }
            public void U32(uint v) { U16((int)(v >> 16)); U16((int)(v & 0xFFFF)); }
            public void I64(long v) { U32((uint)(v >> 32)); U32((uint)v); }
            public void Bytes(byte[] b) { m.Write(b, 0, b.Length); }
            public void Pad4() { while (m.Length % 4 != 0) m.WriteByte(0); }
            public byte[] ToArray() { return m.ToArray(); }
        }
    }
}
