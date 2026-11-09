using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>One string the mantra shuffle changes: where, what it said, what it says now.</summary>
    public sealed class MantraEdit
    {
        public int Block;
        public int Index;
        public string Before;
        public string After;
    }

    /// <summary>One mantra: its slot (string block 2), what the shrine does with it, its word before and after.</summary>
    public sealed class MantraMove
    {
        public int Index;
        public string Meaning;
        public string Was;
        public string Now;
    }

    /// <summary>What the mantra shuffle changes (or would change).</summary>
    public sealed class MantraResult
    {
        public int Seed;
        public readonly List<MantraMove> Mantras = new List<MantraMove>();
        /// <summary>Every changed string: the mantras themselves (block 2) first, then the texts that name them.</summary>
        public readonly List<MantraEdit> Edits = new List<MantraEdit>();
        public readonly List<string> Notes = new List<string>();

        /// <summary>The report: the mantras' new words, then every text changed.</summary>
        public string Report(bool applied)
        {
            var sb = new StringBuilder();
            sb.AppendLine((applied ? "Mantras swapped" : "Preview: the mantras would swap") + " (seed " + Seed + ").");
            sb.AppendLine();
            foreach (var m in Mantras) sb.AppendFormat("  {0,-8} -> {1,-8} {2}\n", m.Was, m.Now, m.Meaning);
            var texts = Edits.Where(e => e.Block != MantraShuffle.Block).ToList();
            sb.AppendLine();
            sb.AppendLine(texts.Count + " text" + (texts.Count == 1 ? "" : "s") + " naming a mantra " + (applied ? "changed" : "would change") + ":");
            foreach (var e in texts)
                sb.AppendFormat("  block {0} (0x{0:X4}) string {1}: {2}\n", e.Block, e.Index, e.After.Replace("\n", " "));
            foreach (var n in Notes) { sb.AppendLine(); sb.AppendLine(n); }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Randomizer ▸ Randomize mantras (UW1): the shrines' mantras trade words. The game looks the
    /// typed mantra up among string block 2's strings 0x33 to 0x4C (game/SKILLS.C mantra_advance,
    /// upper case, the whole text), so the words move between those strings and the shrine's
    /// effects stay with the slots: the skill whose mantra was RA may now answer to SOL. Every
    /// text of STRINGS.PAK naming a mantra (books, scrolls, wall writing, conversations) is changed
    /// to the slot's new word, keeping its capitals.
    /// Two slots stay: INSAHN (the Cup of Wonder's way), whose three parts are pictures in the
    /// incense visions (cutscenes 11 to 13, obj/USEITEMS.C), not text; and NO, which only prints
    /// an empty string and no text names. Every other mantra gets another one's word.
    /// </summary>
    public static class MantraShuffle
    {
        public const int Block = 2;
        public const int First = 0x33;
        public const int Count = 26;
        /// <summary>The skills' own mantras come first; their skill names are block 2's strings 0x1F on.</summary>
        public const int Skills = 20;
        const int SkillNames = 0x1F;
        /// <summary>INSAHN: the incense visions show its parts as pictures.</summary>
        public const int Cup = First + 20;
        /// <summary>NO: prints string 0x1F of block 1 (empty).</summary>
        public const int Nothing = First + 22;

        /// <summary>What the shrine does with a mantra slot (string block 2's index).</summary>
        public static string Meaning(GameSession s, int index)
        {
            int k = index - First;
            if (k >= 0 && k < Skills)
            {
                string n = s?.Strings?.Get(Block, SkillNames + k);
                return string.IsNullOrWhiteSpace(n) ? "skill " + k : n.Trim();
            }
            switch (k)
            {
                case 20: return "the way to the Cup of Wonder (stays: its parts are pictures)";
                case 21: return "the Key of Truth";
                case 22: return "nothing (stays)";
                case 23: return "combat skills (Courage)";
                case 24: return "magic skills (Truth)";
                case 25: return "other skills (Love)";
            }
            return "";
        }

        /// <summary>Whether a slot takes part.</summary>
        public static bool Moves(int index) { return index >= First && index < First + Count && index != Cup && index != Nothing; }

        /// <summary>
        /// The new words: every slot that takes part gets another one's word (a random
        /// derangement by the seed). Returns the slot's index to its new word.
        /// </summary>
        public static Dictionary<int, string> Plan(IList<string> block2, int seed)
        {
            var slots = Enumerable.Range(First, Count).Where(i => Moves(i) && i < block2.Count && !string.IsNullOrWhiteSpace(block2[i])).ToList();
            var words = slots.Select(i => block2[i]).ToList();
            var rng = new Random(seed);
            var order = Enumerable.Range(0, slots.Count).ToList();
            // Sattolo's shuffle: one cycle through every slot, so no word stays where it was
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i); var t = order[i]; order[i] = order[j]; order[j] = t; }
            var map = new Dictionary<int, string>();
            for (int k = 0; k < slots.Count; k++) map[slots[k]] = words[order[k]];
            return map;
        }

        /// <summary>Works out every change on the strings as they are now (unsaved edits too). Nothing is changed.</summary>
        public static MantraResult Preview(StringsEditor strings, int seed)
        {
            var r = new MantraResult { Seed = seed };
            if (strings.Session.IsUw2) { r.Notes.Add("UW2 has no shrine mantras: nothing to do."); return r; }
            var b2 = strings.Get(Block);
            if (b2.Count < First + Count) { r.Notes.Add("String block 2 has no mantras (fewer than " + (First + Count) + " strings)."); return r; }
            var plan = Plan(b2, seed);
            if (plan.Count < 2) { r.Notes.Add("Fewer than two mantras to swap."); return r; }

            // old word -> new word, by the old (upper case) word
            var swap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in plan.OrderBy(k => k.Key))
            {
                string was = b2[kv.Key];
                r.Mantras.Add(new MantraMove { Index = kv.Key, Meaning = Meaning(strings.Session, kv.Key), Was = was, Now = kv.Value });
                swap[was.ToUpperInvariant()] = kv.Value.ToUpperInvariant();
                r.Edits.Add(new MantraEdit { Block = Block, Index = kv.Key, Before = was, After = kv.Value });
            }
            foreach (int i in new[] { Cup, Nothing })
                r.Mantras.Add(new MantraMove { Index = i, Meaning = Meaning(strings.Session, i), Was = b2[i], Now = b2[i] });

            var finder = Finder(swap.Keys);
            foreach (int block in strings.Pak.Blocks)
            {
                if (block == Block) continue;
                var l = strings.Get(block);
                for (int i = 0; i < l.Count; i++)
                {
                    string before = l[i] ?? "";
                    string after = Rewrite(before, finder, swap);
                    if (after != before) r.Edits.Add(new MantraEdit { Block = block, Index = i, Before = before, After = after });
                }
            }
            r.Notes.Add("INSAHN stays: the incense visions show its three parts as pictures (cutscenes 11 to 13). NO stays: it does nothing and no text names it.");
            return r;
        }

        /// <summary>Puts the changes in the strings (unsaved until File ▸ Save). Returns the result.</summary>
        public static MantraResult Apply(StringsEditor strings, int seed)
        {
            var r = Preview(strings, seed);
            foreach (var e in r.Edits) strings.Set(e.Block, e.Index, e.After);
            return r;
        }

        /// <summary>Puts back what Apply changed, where a string still says what Apply made it say. Returns how many strings were left because they changed since.</summary>
        public static int Undo(StringsEditor strings, MantraResult r)
        {
            int left = 0;
            foreach (var e in r.Edits)
            {
                if (strings.Get(e.Block, e.Index) == e.After) strings.Set(e.Block, e.Index, e.Before);
                else left++;
            }
            return left;
        }

        /// <summary>The mantras as whole words, longest first (so "SUMM RA" wins over "RA"), any case.</summary>
        public static Regex Finder(IEnumerable<string> words)
        {
            var alts = words.OrderByDescending(w => w.Length).Select(w => Regex.Escape(w).Replace("\\ ", "\\s+"));
            return new Regex(@"(?<![A-Za-z])(" + string.Join("|", alts) + @")(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// A text with each mantra it names changed to the new word. A mantra counts in capitals
        /// ("RA", "SUMM RA"); in title case ("Summ Ra", "Fanlo") only when it has more than one word
        /// or five letters or more, so that common words like "Lo" or "Un" are never taken for one.
        /// </summary>
        public static string Rewrite(string text, Regex finder, Dictionary<string, string> swap)
        {
            return finder.Replace(text, m =>
            {
                string found = m.Value;
                string key = Regex.Replace(found, @"\s+", " ").ToUpperInvariant();
                string now;
                if (!swap.TryGetValue(key, out now)) return found;
                if (found == found.ToUpperInvariant()) return now;
                bool long_ = key.Contains(' ') || key.Length >= 5;
                if (long_ && found == Title(found)) return Title(now);
                return found;
            });
        }

        /// <summary>"SUMM RA" -> "Summ Ra".</summary>
        static string Title(string w)
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
    }
}
