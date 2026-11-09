using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One string a text randomizer changes: where, what it said, what it says now.</summary>
    public sealed class TextEdit
    {
        public int Block;
        public int Index;
        public string Before;
        public string After;
    }

    /// <summary>
    /// What the text randomizers (Randomize mantras, Vowel play) share: whole words found in every
    /// string of STRINGS.PAK, the changed strings worked out without touching anything, put in the
    /// strings (unsaved until File ▸ Save) and put back.
    /// </summary>
    public static class TextRewrite
    {
        /// <summary>
        /// A regex for any of the words as a whole word: no letter right before or after (an
        /// apostrophe may follow, so "Garamon's" holds "Garamon"). Longer words first, so "SUMM RA"
        /// wins over "RA"; a space inside a word matches any run of spaces.
        /// </summary>
        public static Regex Finder(IEnumerable<string> words, bool ignoreCase)
        {
            var alts = words.Where(w => !string.IsNullOrEmpty(w)).Distinct().OrderByDescending(w => w.Length).ThenBy(w => w, StringComparer.Ordinal)
                .Select(w => Regex.Escape(w).Replace("\\ ", "\\s+")).ToList();
            if (alts.Count == 0) return null;
            var o = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            return new Regex(@"(?<![A-Za-z])(" + string.Join("|", alts) + @")(?![A-Za-z])", o);
        }

        /// <summary>Every string (as shown) that the rewrite changes, in every block but the skipped ones. Nothing is changed.</summary>
        public static List<TextEdit> Plan(StringsEditor strings, Func<string, string> rewrite, ICollection<int> skipBlocks = null)
        {
            var list = new List<TextEdit>();
            foreach (int block in strings.Pak.Blocks)
            {
                if (skipBlocks != null && skipBlocks.Contains(block)) continue;
                var l = strings.Get(block);
                for (int i = 0; i < l.Count; i++)
                {
                    string before = l[i] ?? "";
                    string after = rewrite(before);
                    if (after != before) list.Add(new TextEdit { Block = block, Index = i, Before = before, After = after });
                }
            }
            return list;
        }

        /// <summary>Puts the edits in the strings (unsaved until File ▸ Save).</summary>
        public static void Apply(StringsEditor strings, IEnumerable<TextEdit> edits)
        {
            foreach (var e in edits) strings.Set(e.Block, e.Index, e.After);
        }

        /// <summary>Puts back each string that still says what the edit made it say, last edit first. Returns how many were left because they changed since.</summary>
        public static int Undo(StringsEditor strings, IList<TextEdit> edits)
        {
            int left = 0;
            for (int k = edits.Count - 1; k >= 0; k--)
            {
                var e = edits[k];
                if (strings.Get(e.Block, e.Index) == e.After) strings.Set(e.Block, e.Index, e.Before);
                else left++;
            }
            return left;
        }

        /// <summary>"SUMM RA" -> "Summ Ra": each word's first letter up, the rest down.</summary>
        public static string Title(string w)
        {
            var sb = new StringBuilder(w.Length);
            bool start = true;
            foreach (char c in w)
            {
                sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                start = !char.IsLetter(c);
            }
            return sb.ToString();
        }

        /// <summary>The report's list of changed texts.</summary>
        public static void Report(StringBuilder sb, IEnumerable<TextEdit> edits, bool applied, string what)
        {
            var l = edits.ToList();
            sb.AppendLine(l.Count + " text" + (l.Count == 1 ? "" : "s") + " " + what + " " + (applied ? "changed" : "would change") + ":");
            foreach (var e in l) sb.AppendFormat("  block {0} (0x{0:X4}) string {1}: {2}\n", e.Block, e.Index, e.After.Replace("\n", " "));
        }
    }
}
