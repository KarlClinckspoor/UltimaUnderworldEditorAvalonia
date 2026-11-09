using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UwEditor.Core.Conversations;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>Vowel play's settings, saved per game.</summary>
    public sealed class VowelPlayOptions
    {
        public int Seed { get; set; } = 1;
        /// <summary>A name starting with a vowel keeps it ("Illomo" stays "I…").</summary>
        public bool KeepFirstLetter { get; set; }
        /// <summary>The names to change; null for <see cref="VowelPlay.DefaultNames"/>.</summary>
        public List<string> Names { get; set; }

        public string ToJson() { return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }); }
        public static VowelPlayOptions FromJson(string json) { return JsonSerializer.Deserialize<VowelPlayOptions>(json); }
    }

    /// <summary>One name and what it becomes.</summary>
    public sealed class VowelName
    {
        public string Was;
        public string Now;
        /// <summary>Why it stays, when it does.</summary>
        public string Stays;
    }

    /// <summary>What vowel play changes (or would change).</summary>
    public sealed class VowelPlayResult
    {
        public int Seed;
        public readonly List<VowelName> Names = new List<VowelName>();
        public readonly List<TextEdit> Edits = new List<TextEdit>();
        public readonly List<string> Protected = new List<string>();
        public readonly List<string> Notes = new List<string>();

        public string Report(bool applied)
        {
            var sb = new StringBuilder();
            sb.AppendLine((applied ? "Vowel play applied" : "Preview: vowel play") + " (seed " + Seed + ").");
            sb.AppendLine();
            foreach (var n in Names.Where(n => n.Stays == null)) sb.AppendFormat("  {0,-14} -> {1}\n", n.Was, n.Now);
            var kept = Names.Where(n => n.Stays != null).ToList();
            if (kept.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Staying:");
                foreach (var n in kept) sb.AppendFormat("  {0}: {1}\n", n.Was, n.Stays);
            }
            if (Protected.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Never changed (mantras, passwords and the words conversations compare against): " + string.Join(", ", Protected) + ".");
            }
            sb.AppendLine();
            TextRewrite.Report(sb, Edits, applied, "naming someone");
            foreach (var n in Notes) { sb.AppendLine(); sb.AppendLine(n); }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Randomizer ▸ Vowel play: proper names get their vowels shuffled, as Merrow's "Vowel play"
    /// does to Quest 64's monsters. Each vowel becomes a random vowel (a, e, i, o, u, keeping
    /// its case; y is a consonant), so a name keeps its length, consonants and look ("Garamon" ->
    /// "Gorumen"). Each name gets one new spelling from the seed and the name, used in every
    /// string of STRINGS.PAK: the names creatures talk under (block 7), conversations, books,
    /// scrolls and cutscenes; the name in capitals ("GARAMON") follows too. Words stay that the
    /// game or a conversation compares typed text against (the mantras, passwords like "Deco
    /// Morono", "Folanae"), and a new spelling is never a word the game's text already has or
    /// another name.
    /// </summary>
    public static class VowelPlay
    {
        const string Vowels = "aeiou";
        /// <summary>Words before a name that are not names.</summary>
        static readonly HashSet<string> Titles = new HashSet<string>(StringComparer.Ordinal)
            { "The", "A", "An", "Of", "Sir", "Lord", "Lady", "King", "Queen", "Dr", "Mr", "Mrs", "Master", "Baron", "Brother", "Sister" };
        /// <summary>Names that are not the name of a creature that talks (block 7), by game.</summary>
        static readonly string[] Uw1Extra = { "Cabirus", "Almric", "Arial", "Corwin", "Avatar", "British", "Britannia", "Caliburn", "Flamebeard", "Mardin", "Stygian" };
        static readonly string[] Uw2Extra = { "Avatar", "British", "Britannia", "Guardian", "Blackrock", "Killorn", "Shamino" };
        /// <summary>Folanae: Gurstang's word, which Illomo waits for.</summary>
        static readonly string[] AlwaysProtected = { "Folanae" };

        /// <summary>Every whole word of the game's text, as written (case kept).</summary>
        static HashSet<string> Words(StringsEditor strings)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (int b in strings.Pak.Blocks)
                foreach (var s in strings.Get(b))
                    foreach (Match m in Regex.Matches(s ?? "", @"[A-Za-z][A-Za-z']*[A-Za-z]|[A-Za-z]")) set.Add(m.Value.EndsWith("'s") ? m.Value.Substring(0, m.Value.Length - 2) : m.Value);
            return set;
        }

        static bool HasVowel(string w) { return w.Any(c => Vowels.IndexOf(char.ToLowerInvariant(c)) >= 0); }

        /// <summary>
        /// The names to change at first: every capitalized word of the names creatures talk under
        /// (block 7 from string 16, titles left out) that has a vowel and never appears in lower
        /// case in the game's text ("Door", "Rat", "Data" do), then the game's other names that the
        /// text uses (Cabirus, the Avatar, Lord British...).
        /// </summary>
        public static List<string> DefaultNames(StringsEditor strings)
        {
            var words = Words(strings);
            var list = new List<string>();
            var b7 = strings.Get(7);
            for (int i = 16; i < b7.Count; i++)
                foreach (Match m in Regex.Matches(b7[i] ?? "", @"[A-Za-z][A-Za-z']*[A-Za-z]"))
                {
                    string t = m.Value;
                    if (!char.IsUpper(t[0]) || t.Length < 3 || !HasVowel(t) || Titles.Contains(t) || t.ToUpperInvariant() == t) continue;
                    if (words.Contains(t.ToLowerInvariant())) continue;
                    if (!list.Contains(t)) list.Add(t);
                }
            foreach (var t in strings.Session.IsUw2 ? Uw2Extra : Uw1Extra)
                if (words.Contains(t) && !list.Contains(t)) list.Add(t);
            return list;
        }

        /// <summary>
        /// The words never changed: the shrine mantras (UW1, block 2 0x33-0x4C), Folanae, and every
        /// word of a text a conversation compares typed text against (compare, contains: the
        /// passwords and the answers NPCs wait for). Upper case.
        /// </summary>
        public static HashSet<string> ProtectedWords(StringsEditor strings, ConversationLibrary conversations)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            void Add(string text) { foreach (Match m in Regex.Matches(text ?? "", @"[A-Za-z][A-Za-z']*")) set.Add(m.Value.ToUpperInvariant()); }
            foreach (var w in AlwaysProtected) Add(w);
            if (!strings.Session.IsUw2)
                for (int i = MantraShuffle.First; i < MantraShuffle.First + MantraShuffle.Count; i++) Add(strings.Get(MantraShuffle.Block, i));
            if (conversations != null)
                foreach (string literal in ComparedTexts(conversations)) Add(literal);
            return set;
        }

        /// <summary>The string literals the conversations pass to compare and contains (read from their decompiled view).</summary>
        public static List<string> ComparedTexts(ConversationLibrary conversations)
        {
            var list = new List<string>();
            var call = new Regex(@"\b(compare|contains)\s*\(");
            var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (int slot in conversations.Archive.Slots())
            {
                DecompileResult d;
                try
                {
                    var c = conversations.Archive.Read(slot);
                    if (c == null) continue;
                    d = conversations.Decompile(c);
                }
                catch (Exception) { continue; }
                foreach (var line in d.Lines)
                {
                    string text = line.Text ?? "";
                    foreach (Match m in call.Matches(text))
                    {
                        // the call's arguments: up to its closing parenthesis
                        int depth = 0, end = m.Index + m.Length - 1;
                        for (int k = end; k < text.Length; k++)
                        {
                            if (text[k] == '(') depth++;
                            else if (text[k] == ')' && --depth == 0) { end = k; break; }
                        }
                        foreach (Match q in literal.Matches(text.Substring(m.Index, end - m.Index + 1)))
                            list.Add(Regex.Unescape(q.Groups[1].Value));
                    }
                }
            }
            return list;
        }

        /// <summary>A stable number for a name (FNV-1a), so a name's new spelling depends only on the seed and the name.</summary>
        static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        /// <summary>The name with each vowel drawn again (case kept; with keepFirst, a first vowel stays).</summary>
        public static string Shuffle(string name, Random rng, bool keepFirst)
        {
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool vowel = Vowels.IndexOf(char.ToLowerInvariant(c)) >= 0;
                if (!vowel || (keepFirst && i == 0)) { sb.Append(c); continue; }
                char v = Vowels[rng.Next(Vowels.Length)];
                sb.Append(char.IsUpper(c) ? char.ToUpperInvariant(v) : v);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Each name's new spelling: drawn again (up to 64 times) while it is the same name, a word
        /// the text already has (any case), another name, or another name's new spelling.
        /// </summary>
        public static List<VowelName> Plan(IEnumerable<string> names, HashSet<string> protectedWords, HashSet<string> words, int seed, bool keepFirst)
        {
            var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal).ToList();
            var all = new HashSet<string>(list.Select(n => n.ToUpperInvariant()), StringComparer.Ordinal);
            var vocabulary = new HashSet<string>(words.Select(w => w.ToUpperInvariant()), StringComparer.Ordinal);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var res = new List<VowelName>();
            foreach (var n in list)
            {
                var v = new VowelName { Was = n, Now = n };
                res.Add(v);
                if (protectedWords.Contains(n.ToUpperInvariant())) { v.Stays = "a mantra, a password or a word a conversation compares against"; continue; }
                if (!HasVowel(keepFirst ? n.Substring(1) : n)) { v.Stays = "no vowel to change"; continue; }
                var rng = new Random(Hash(n) ^ seed);
                string got = null;
                for (int tries = 0; tries < 64 && got == null; tries++)
                {
                    string c = Shuffle(n, rng, keepFirst);
                    string u = c.ToUpperInvariant();
                    if (c == n || all.Contains(u) || vocabulary.Contains(u) || taken.Contains(u) || protectedWords.Contains(u)) continue;
                    got = c;
                }
                if (got == null) { v.Stays = "every new spelling tried was already a word or a name"; continue; }
                v.Now = got;
                taken.Add(got.ToUpperInvariant());
            }
            return res;
        }

        /// <summary>A text with each name changed: as written ("Garamon", "Garamon's") and in capitals ("GARAMON"). Case-sensitive, so "Owl" changes and "owl" does not.</summary>
        public static string Rewrite(string text, Regex finder, Dictionary<string, string> swap)
        {
            if (finder == null) return text;
            return finder.Replace(text, m => { string now; return swap.TryGetValue(m.Value, out now) ? now : m.Value; });
        }

        /// <summary>The words to look for and what each becomes (the name and its capitals).</summary>
        public static Dictionary<string, string> Swaps(IEnumerable<VowelName> names)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var n in names.Where(n => n.Stays == null))
            {
                d[n.Was] = n.Now;
                string up = n.Was.ToUpperInvariant();
                if (up != n.Was && !d.ContainsKey(up)) d[up] = n.Now.ToUpperInvariant();
            }
            return d;
        }

        /// <summary>Works out every change on the strings as they are now (unsaved edits too). Nothing is changed.</summary>
        public static VowelPlayResult Preview(StringsEditor strings, HashSet<string> protectedWords, VowelPlayOptions opt)
        {
            var r = new VowelPlayResult { Seed = opt.Seed };
            var names = opt.Names ?? DefaultNames(strings);
            r.Names.AddRange(Plan(names, protectedWords, Words(strings), opt.Seed, opt.KeepFirstLetter));
            r.Protected.AddRange(protectedWords.OrderBy(w => w, StringComparer.Ordinal));
            var swap = Swaps(r.Names);
            if (swap.Count == 0) { r.Notes.Add("No name to change."); return r; }
            var finder = TextRewrite.Finder(swap.Keys, false);
            r.Edits.AddRange(TextRewrite.Plan(strings, text => Rewrite(text, finder, swap)));
            return r;
        }

        /// <summary>Puts the changes in the strings (unsaved until File ▸ Save).</summary>
        public static VowelPlayResult Apply(StringsEditor strings, HashSet<string> protectedWords, VowelPlayOptions opt)
        {
            var r = Preview(strings, protectedWords, opt);
            TextRewrite.Apply(strings, r.Edits);
            return r;
        }

        /// <summary>Puts back what Apply changed (strings changed since stay). Returns how many stayed.</summary>
        public static int Undo(StringsEditor strings, VowelPlayResult r) { return TextRewrite.Undo(strings, r.Edits); }
    }
}
