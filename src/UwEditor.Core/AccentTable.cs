using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core
{
    /// <summary>A set of letters a language needs, most used first, for filling the accent table.</summary>
    public sealed class AccentPreset
    {
        public string Name;
        public string Letters;
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// The accent table: which of the game's codes stand for which letters, so translated text
    /// can have à, é, ç, ü, ñ… without changing the game's program. The game's text is bytes
    /// below 128 (a font has 128 glyphs, uw1 gfx/GRLIBI.ASM), and the strings editor shows a
    /// mapped code as its letter and writes the letter back as the code. The fonts then need a
    /// glyph for the letter at that code (Tools ▸ Graphics ▸ Fonts).
    ///
    /// Which codes can be used: the game draws only codes 20h..7Ah (GRLIBI.ASM 4B2C draws any
    /// other as '?', in both games), so a letter needs a printable symbol no string uses: UW1 has
    /// $ % [ ], UW2 $ [ ] ^ ` (FreeCodes scans STRINGS.PAK). Codes 1-31 and 7Bh-7Fh hold glyphs
    /// but are drawn as '?' by the DOS game; they are offered only when asked for. Never '|'
    /// (ends a string, ui/GAMESTRN.C), '\' (the scroll's escapes, ui/SCROLLIO.C) or '@'
    /// (conversations' substitutions, conv/BABL.C), nor 9, 10, 13 (tab, the line break,
    /// carriage return), letters or digits.
    /// </summary>
    public sealed class AccentTable
    {
        readonly SortedDictionary<int, char> byCode = new SortedDictionary<int, char>();
        readonly Dictionary<char, int> byLetter = new Dictionary<char, int>();

        public static readonly char[] Never = { '|', '\\', '@' };

        public IReadOnlyDictionary<int, char> Map { get { return byCode; } }
        public int Count { get { return byCode.Count; } }
        public bool IsEmpty { get { return byCode.Count == 0; } }

        /// <summary>Why a code cannot carry a letter (null when it can).</summary>
        public static string CodeProblem(int code)
        {
            if (code < 1 || code > 127) return "Only codes 1-127 are in the fonts.";
            char c = (char)code;
            if (Array.IndexOf(Never, c) >= 0) return "'" + c + "' has a meaning in the game's text (" + (c == '|' ? "it ends a string" : c == '\\' ? "the scroll's escapes" : "conversations' substitutions") + ").";
            if (code == 9 || code == 10 || code == 13) return "Code " + code + " is a tab, line break or carriage return.";
            if (char.IsLetterOrDigit(c) || c == ' ') return "'" + c + "' is used by every text.";
            return null;
        }

        /// <summary>Whether the DOS game draws the code (GRLIBI.ASM: 20h..7Ah, others come out as '?').</summary>
        public static bool GameDraws(int code) { return code >= 0x20 && code <= 0x7A; }

        /// <summary>Gives a code a letter. Throws when the code cannot carry one or the letter has another code.</summary>
        public void Set(int code, char letter)
        {
            string p = CodeProblem(code);
            if (p != null) throw new ArgumentException(p);
            if (letter < 0x80) throw new ArgumentException("'" + letter + "' is a plain ASCII character: the game writes it as it is.");
            int other;
            if (byLetter.TryGetValue(letter, out other) && other != code) throw new ArgumentException("'" + letter + "' already has code " + other + ".");
            Remove(code);
            byCode[code] = letter;
            byLetter[letter] = code;
        }

        public void Remove(int code)
        {
            char old;
            if (byCode.TryGetValue(code, out old)) { byCode.Remove(code); byLetter.Remove(old); }
        }

        public void Clear() { byCode.Clear(); byLetter.Clear(); }

        public char? LetterOf(int code) { char c; return byCode.TryGetValue(code, out c) ? c : (char?)null; }
        public int CodeOf(char letter) { int c; return byLetter.TryGetValue(letter, out c) ? c : -1; }

        /// <summary>A game string as the editor shows it: mapped codes as their letters.</summary>
        public string ToDisplay(string raw)
        {
            if (string.IsNullOrEmpty(raw) || byCode.Count == 0) return raw;
            var a = raw.ToCharArray();
            for (int i = 0; i < a.Length; i++) { char l; if (byCode.TryGetValue(a[i], out l)) a[i] = l; }
            return new string(a);
        }

        /// <summary>Why a shown text cannot be written (null when it can): a letter with no code,
        /// or a character whose code the table gives to a letter.</summary>
        public string Problem(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (char ch in text)
            {
                if (byLetter.ContainsKey(ch)) continue;
                char l;
                if (byCode.TryGetValue(ch, out l)) return "'" + ch + "' is code " + (int)ch + ", which the accent table gives to '" + l + "': it cannot be written as itself.";
                if (ch >= 0x80) return "'" + ch + "' has no code: give it one in the accent table (Tools ▸ Graphics ▸ Fonts), or write it without the accent.";
            }
            return null;
        }

        /// <summary>A shown text as the game's bytes: letters as their codes. Throws with the reason when it cannot be.</summary>
        public string ToGame(string text)
        {
            string p = Problem(text);
            if (p != null) throw new FormatException(p);
            if (string.IsNullOrEmpty(text) || byLetter.Count == 0) return text;
            var a = text.ToCharArray();
            for (int i = 0; i < a.Length; i++) { int c; if (byLetter.TryGetValue(a[i], out c)) a[i] = (char)c; }
            return new string(a);
        }

        /// <summary>The character a code stands for in exports (BDF, TrueType): its letter, the
        /// ASCII character for 20h..7Eh, else -1.</summary>
        public int UnicodeOf(int code)
        {
            char l;
            if (byCode.TryGetValue(code, out l)) return l;
            if (code >= 0x20 && code <= 0x7E) return code;
            return -1;
        }

        /// <summary>The code an exported character came from (UnicodeOf the other way), or -1.</summary>
        public int CodeOfUnicode(int u)
        {
            if (u >= 0 && u <= 0xFFFF) { int c; if (byLetter.TryGetValue((char)u, out c)) return c; }
            if (u >= 0x20 && u <= 0x7E && !byCode.ContainsKey(u)) return u;
            return -1;
        }

        public AccentTable Clone()
        {
            var t = new AccentTable();
            foreach (var kv in byCode) t.Set(kv.Key, kv.Value);
            return t;
        }

        /// <summary>For the settings: code → letter.</summary>
        public Dictionary<int, string> ToDictionary() { return byCode.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()); }

        /// <summary>From the settings; entries that are no longer valid are dropped.</summary>
        public static AccentTable FromDictionary(IDictionary<int, string> d)
        {
            var t = new AccentTable();
            if (d == null) return t;
            foreach (var kv in d.OrderBy(k => k.Key))
                if (!string.IsNullOrEmpty(kv.Value))
                    try { t.Set(kv.Key, kv.Value[0]); } catch (ArgumentException) { }
            return t;
        }

        public static readonly AccentPreset[] Presets =
        {
            new AccentPreset { Name = "Portuguese", Letters = "ãçéáíóêõôâúàÉÁÇÃÓÊÍÚÕÔÂÀü" },
            new AccentPreset { Name = "Spanish", Letters = "éóáíñúü¿¡ÉÓÁÍÑÚÜ" },
            new AccentPreset { Name = "French", Letters = "éèàçêùâîôûëïœüÿÉÈÀÇÊÙÂÎÔÛËÏŒ" },
            new AccentPreset { Name = "German", Letters = "äöüßÄÖÜ" },
            new AccentPreset { Name = "Italian", Letters = "àèéìòùÀÈÉÌÒÙ" },
            new AccentPreset { Name = "Common Western", Letters = "éáíóúàèçñüöäâêôãõëïîûùòìßÉÁÍÓÚÀÈÇÑÜÖÄ¿¡" },
        };

        /// <summary>Gives the preset's letters that have no code yet the free codes in order, as
        /// far as they go. Returns the letters left without a code.</summary>
        public string Fill(AccentPreset preset, IEnumerable<int> freeCodes)
        {
            var free = new Queue<int>(freeCodes.Where(c => !byCode.ContainsKey(c)));
            var left = new System.Text.StringBuilder();
            foreach (char l in preset.Letters)
            {
                if (byLetter.ContainsKey(l)) continue;
                if (free.Count == 0) { left.Append(l); continue; }
                Set(free.Dequeue(), l);
            }
            return left.ToString();
        }
    }

    /// <summary>What the strings use of the codes an accent table could take.</summary>
    public sealed class CodeUse
    {
        /// <summary>How many times each code appears, in all blocks and outside the debug block (24).</summary>
        public readonly int[] All = new int[128], OutsideDebug = new int[128];

        public const int DebugBlock = 24;

        public static CodeUse Scan(IEnumerable<(int Block, string Text)> strings)
        {
            var u = new CodeUse();
            foreach (var s in strings)
                foreach (char c in s.Text ?? "")
                    if (c < 128) { u.All[c]++; if (s.Block != DebugBlock) u.OutsideDebug[c]++; }
            return u;
        }

        /// <summary>
        /// The codes a letter can take, in code order: printable symbols no string uses (or,
        /// with debugToo, used only by the debug strings, which play never shows), and, with
        /// undrawnToo, the codes the DOS game draws as '?' (1-31, 7Bh-7Fh). A code the table
        /// already maps counts as free (its uses are the letter's).
        /// </summary>
        public List<int> FreeCodes(AccentTable table, bool debugToo, bool undrawnToo)
        {
            var list = new List<int>();
            for (int c = 1; c < 128; c++)
            {
                if (AccentTable.CodeProblem(c) != null) continue;
                bool mapped = table != null && table.LetterOf(c) != null;
                if (!AccentTable.GameDraws(c) && !undrawnToo && !mapped) continue;
                int uses = debugToo ? OutsideDebug[c] : All[c];
                if (uses == 0 || mapped) list.Add(c);
            }
            return list;
        }
    }
}
