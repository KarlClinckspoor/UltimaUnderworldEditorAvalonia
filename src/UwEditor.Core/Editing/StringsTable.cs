using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UwEditor.Core.Editing
{
    /// <summary>One string of a translation table: where it goes and its text (null: left as it is).</summary>
    public sealed class StringsTableRow
    {
        public int Block, Index;
        public string Text;
    }

    /// <summary>What importing a translation table did.</summary>
    public sealed class StringsImportResult
    {
        public int Rows, Changed, Same, Empty;
        /// <summary>Rows that were not applied, each with its reason (a block or string that is not there, a letter the file cannot write).</summary>
        public readonly List<string> Problems = new List<string>();

        public string Describe()
        {
            var s = string.Format("{0} string{1} changed, {2} already the same", Changed, Changed == 1 ? "" : "s", Same);
            if (Empty > 0) s += ", " + Empty + " left empty in the table kept as they are";
            if (Problems.Count > 0) s += "; " + Problems.Count + " not applied: " + string.Join("; ", Problems.Take(3)) + (Problems.Count > 3 ? "; …" : "");
            return s + ".";
        }
    }

    /// <summary>
    /// Every string of STRINGS.PAK as a translation table, out and in again, in three formats:
    /// - TSV, as Underworld Revisited's Languages/&lt;code&gt;.strings.tsv: one line a string, the
    ///   block, a TAB, the index, a TAB, the text; lines starting with # are comments; in a text
    ///   \n is a line break and \t a TAB. One translation serves both programs.
    /// - CSV for spreadsheets: block, index, source, translation (quoted as RFC 4180, UTF-8
    ///   with a byte order mark so that Excel reads the accents).
    /// - PO for Poedit and Weblate: msgctxt "block:index", msgid the source, msgstr the
    ///   translation.
    /// Strings go out as the editor shows them (the accent table's codes as their letters) and
    /// come in the same way. On import a row whose text is empty leaves its string as it is,
    /// and a string is never added: the numbers are what the game's code asks for.
    /// </summary>
    public static class StringsTable
    {
        public static readonly string[] Formats = { "tsv", "csv", "po", "txt" };

        /// <summary>The format a file name asks for by its extension, or null.</summary>
        public static string FormatOf(string path)
        {
            string e = (System.IO.Path.GetExtension(path ?? "") ?? "").TrimStart('.').ToLowerInvariant();
            return e == "pot" ? "po" : Formats.Contains(e) ? e : null;
        }

        static IEnumerable<StringsTableRow> All(StringsEditor st)
        {
            foreach (var b in st.Blocks().OrderBy(x => x.Number))
            {
                var l = st.Get(b.Number);
                for (int i = 0; i < l.Count; i++) yield return new StringsTableRow { Block = b.Number, Index = i, Text = l[i] ?? "" };
            }
        }

        public static string Export(StringsEditor st, string format)
        {
            switch (format)
            {
                case "tsv": return ExportTsv(st);
                case "csv": return ExportCsv(st);
                case "po": return ExportPo(st);
                case "txt": return ExportTxt(st);
            }
            throw new ArgumentException("The formats are tsv, csv, po and txt.");
        }

        /// <summary>
        /// A table file's text. UTF-8 when the bytes are UTF-8; else Latin-1, which is what the
        /// older tools' text dumps (and the translations made from them) are written in.
        /// </summary>
        public static string ReadFile(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            try
            {
                string s = new UTF8Encoding(false, true).GetString(bytes);
                return s.Length > 0 && s[0] == (char)0xFEFF ? s.Substring(1) : s;
            }
            catch (ArgumentException) { return Encoding.Latin1.GetString(bytes); }
        }

        // ---- TXT: the text dump of the older tools (Underworld Adventures' strpak and the translations made from it)
        //   strings.pak: 122 string blocks.
        //
        //   block: 0c00; 42 strings.
        //   0: text, with \n for a line break
        // The block number is hexadecimal; what follows "N: " is the text to the end of the line, spaces at its end included.

        public static string ExportTxt(StringsEditor st)
        {
            var sb = new StringBuilder();
            var blocks = st.Blocks().OrderBy(x => x.Number).ToList();
            sb.Append("strings.pak: ").Append(blocks.Count).Append(" string blocks.\r\n");
            foreach (var b in blocks)
            {
                var l = st.Get(b.Number);
                sb.Append("\r\nblock: ").Append(b.Number.ToString("x4", CultureInfo.InvariantCulture)).Append("; ").Append(l.Count).Append(" strings.\r\n");
                for (int i = 0; i < l.Count; i++) sb.Append(i).Append(": ").Append(TsvEscape(l[i])).Append("\r\n");
            }
            return sb.ToString();
        }

        static List<StringsTableRow> ReadTxt(string text, List<string> problems)
        {
            var rows = new List<StringsTableRow>();
            int n = 0, block = -1;
            foreach (var raw in text.Replace("\r\n", "\n").Split((char)10))
            {
                n++;
                string line = raw.TrimEnd((char)13);
                if (line.Trim().Length == 0 || line.StartsWith("strings.pak:", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("block:", StringComparison.OrdinalIgnoreCase))
                {
                    string id = line.Substring(6).Split(';')[0].Trim();
                    if (!int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out block)) { problems.Add("line " + n + ": \"" + id + "\" is no block number (hexadecimal)"); block = -1; }
                    continue;
                }
                int colon = line.IndexOf(':');
                int i;
                if (colon <= 0 || !int.TryParse(line.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out i))
                { problems.Add("line " + n + ": not \"index: text\" (a string must stay on one line)"); continue; }
                if (block < 0) { problems.Add("line " + n + ": a string before any \"block:\" line"); continue; }
                string t = line.Substring(colon + 1);
                if (t.StartsWith(" ")) t = t.Substring(1);
                rows.Add(new StringsTableRow { Block = block, Index = i, Text = TsvUnescape(t) });
            }
            return rows;
        }

        // ---- TSV

        static string TsvEscape(string s) { return (s ?? "").Replace("\r\n", "\n").Replace("\n", "\\n").Replace("\t", "\\t"); }

        static string TsvUnescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length && (s[i + 1] == 'n' || s[i + 1] == 't')) { sb.Append(s[i + 1] == 'n' ? '\n' : '\t'); i++; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string ExportTsv(StringsEditor st)
        {
            var sb = new StringBuilder();
            sb.Append("# STRINGS.PAK of ").Append(st.Session.IsUw2 ? "Ultima Underworld II" : "Ultima Underworld").Append(", written by the Underworld Editor.\n");
            sb.Append("# One line a string: the block, a TAB, the index, a TAB, the text. \\n is a line break, \\t a TAB.\n");
            sb.Append("# The format of Underworld Revisited's Languages/<code>.strings.tsv. An empty text keeps the string as it is.\n");
            int block = -1;
            foreach (var r in All(st))
            {
                if (r.Block != block) { block = r.Block; sb.Append("# block ").Append(block).Append(": ").Append(StringsEditor.Describe(st.Session, block)).Append('\n'); }
                sb.Append(r.Block.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(r.Index.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(TsvEscape(r.Text)).Append('\n');
            }
            return sb.ToString();
        }

        static List<StringsTableRow> ReadTsv(string text, List<string> problems)
        {
            var rows = new List<StringsTableRow>();
            int n = 0;
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                n++;
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(new[] { '\t' }, 3);
                int b, i;
                if (p.Length < 2 || !int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b) || !int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                { problems.Add("line " + n + ": not \"block TAB index TAB text\""); continue; }
                rows.Add(new StringsTableRow { Block = b, Index = i, Text = p.Length > 2 ? TsvUnescape(p[2]) : "" });
            }
            return rows;
        }

        // ---- CSV

        static string CsvField(string s)
        {
            s = (s ?? "").Replace("\r\n", "\n");
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith(" ") || s.EndsWith(" ") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static string ExportCsv(StringsEditor st)
        {
            var sb = new StringBuilder("\uFEFFblock,index,source,translation\r\n");
            foreach (var r in All(st))
                sb.Append(r.Block.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Index.ToString(CultureInfo.InvariantCulture)).Append(',').Append(CsvField(r.Text)).Append(",\r\n");
            return sb.ToString();
        }

        /// <summary>The records of a CSV text (RFC 4180: quoted fields may hold commas, quotes doubled, and line breaks).</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var f = new StringBuilder();
            bool quoted = false, any = false;
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; } else quoted = false; }
                    else if (c != '\r') f.Append(c);
                    continue;
                }
                if (c == '"' && f.Length == 0) { quoted = true; any = true; }
                else if (c == ',') { row.Add(f.ToString()); f.Clear(); any = true; }
                else if (c == '\n') { if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); } row = new List<string>(); f.Clear(); any = false; }
                else if (c != '\r') { f.Append(c); any = true; }
            }
            if (any || f.Length > 0) { row.Add(f.ToString()); rows.Add(row); }
            return rows;
        }

        static List<StringsTableRow> ReadCsv(string text, List<string> problems)
        {
            var rows = new List<StringsTableRow>();
            var recs = ParseCsv(text);
            int n = 0;
            foreach (var r in recs)
            {
                n++;
                int b, i;
                if (r.Count < 3 || !int.TryParse(r[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b) || !int.TryParse(r[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                {
                    if (n > 1 || (r.Count > 0 && r[0].Trim().ToLowerInvariant() != "block")) problems.Add("record " + n + ": not \"block,index,source,translation\"");
                    continue;
                }
                // the translation column; a table of three columns holds the text itself in the third
                rows.Add(new StringsTableRow { Block = b, Index = i, Text = r.Count > 3 ? r[3] : r[2] });
            }
            return rows;
        }

        // ---- PO

        static string PoQuote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in (s ?? "").Replace("\r\n", "\n"))
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string PoUnquote(string s)
        {
            s = s.Trim();
            if (s.Length < 2 || s[0] != '"' || s[s.Length - 1] != '"') return "";
            s = s.Substring(1, s.Length - 2);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c == 'r' ? '\r' : c);
            }
            return sb.ToString();
        }

        public static string ExportPo(StringsEditor st)
        {
            var sb = new StringBuilder();
            sb.Append("msgid \"\"\nmsgstr \"\"\n\"Project-Id-Version: ").Append(st.Session.IsUw2 ? "Ultima Underworld II" : "Ultima Underworld").Append(" STRINGS.PAK\\n\"\n");
            sb.Append("\"MIME-Version: 1.0\\n\"\n\"Content-Type: text/plain; charset=UTF-8\\n\"\n\"Content-Transfer-Encoding: 8bit\\n\"\n\n");
            foreach (var r in All(st))
            {
                // gettext has no entry for an empty source: such a string has nothing to translate
                if (r.Text.Length == 0) continue;
                sb.Append("#. block ").Append(r.Block).Append(" (").Append(StringsEditor.Describe(st.Session, r.Block)).Append("), string ").Append(r.Index).Append('\n');
                sb.Append("msgctxt \"").Append(r.Block).Append(':').Append(r.Index).Append("\"\n");
                sb.Append("msgid ").Append(PoQuote(r.Text)).Append('\n');
                sb.Append("msgstr \"\"\n\n");
            }
            return sb.ToString();
        }

        static List<StringsTableRow> ReadPo(string text, List<string> problems)
        {
            var rows = new List<StringsTableRow>();
            string ctxt = null, field = null;
            var str = new StringBuilder();
            bool fuzzy = false, thisFuzzy = false;
            Action flush = () =>
            {
                if (ctxt != null)
                {
                    var p = ctxt.Split(':');
                    int b, i;
                    if (p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out b) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                        rows.Add(new StringsTableRow { Block = b, Index = i, Text = thisFuzzy ? "" : str.ToString() });       // a fuzzy entry is not a translation yet
                    else problems.Add("msgctxt \"" + ctxt + "\": not \"block:index\"");
                }
                ctxt = null; str.Clear(); thisFuzzy = false;
            };
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#,") && line.Contains("fuzzy")) { fuzzy = true; continue; }
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("msgctxt ")) { flush(); ctxt = PoUnquote(line.Substring(8)); thisFuzzy = fuzzy; fuzzy = false; field = "ctxt"; }
                else if (line.StartsWith("msgid ")) { if (field == "str") { flush(); fuzzy = false; } field = "id"; }
                else if (line.StartsWith("msgstr ")) { field = "str"; str.Clear(); str.Append(PoUnquote(line.Substring(7))); }
                else if (line[0] == '"' && field == "str") str.Append(PoUnquote(line));
            }
            flush();
            return rows;
        }

        // ---- import

        /// <summary>The rows of a table text in a format (tsv, csv or po), with what could not be read.</summary>
        public static List<StringsTableRow> Read(string text, string format, List<string> problems)
        {
            switch (format)
            {
                case "tsv": return ReadTsv(text ?? "", problems);
                case "csv": return ReadCsv(text ?? "", problems);
                case "po": return ReadPo(text ?? "", problems);
                case "txt": return ReadTxt(text ?? "", problems);
            }
            throw new ArgumentException("The formats are tsv, csv, po and txt.");
        }

        /// <summary>
        /// Puts a table's texts in the strings (until the strings are saved). A row with an empty
        /// text, and one whose text is what the string already holds, change nothing; a row for a
        /// block or string that is not there, or with a letter the file cannot write, is reported
        /// and left out. Nothing is changed when dryRun is set: the result says what would be.
        /// </summary>
        public static StringsImportResult Import(StringsEditor st, string text, string format, bool dryRun = false)
        {
            var res = new StringsImportResult();
            var rows = Read(text, format, res.Problems);
            res.Rows = rows.Count;
            var blocks = new Dictionary<int, List<string>>();
            var done = new HashSet<long>();
            foreach (var r in rows)
            {
                if (string.IsNullOrEmpty(r.Text)) { res.Empty++; continue; }
                List<string> l;
                if (!blocks.TryGetValue(r.Block, out l)) blocks[r.Block] = l = st.Pak.Has(r.Block) ? st.Get(r.Block) : null;
                if (l == null) { res.Problems.Add("block " + r.Block + " is not in STRINGS.PAK"); continue; }
                if (r.Index < 0 || r.Index >= l.Count) { res.Problems.Add("block " + r.Block + " has no string " + r.Index + " (it has " + l.Count + ")"); continue; }
                if (!done.Add((long)r.Block << 32 | (uint)r.Index)) { res.Problems.Add("block " + r.Block + " string " + r.Index + " is in the table twice: the first is taken"); continue; }
                string now = (l[r.Index] ?? "").Replace("\r\n", "\n"), want = r.Text.Replace("\r\n", "\n");
                if (now == want) { res.Same++; continue; }
                try
                {
                    st.ToGame(want);
                    if (!dryRun) st.Set(r.Block, r.Index, want);
                    res.Changed++;
                }
                catch (Exception ex) { res.Problems.Add("block " + r.Block + " string " + r.Index + ": " + ex.Message); }
            }
            return res;
        }
    }
}
