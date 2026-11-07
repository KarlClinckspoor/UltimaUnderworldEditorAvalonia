using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using MapNote = UwEditor.Core.Editing.MapNote;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// The key item scatter's spoiler log in the game's own map notes (UW1). The map screen's up
    /// arrow goes past the last level to page 99, showing a blank map with that page's notes
    /// (Automap.NotesOnly), so the log can travel inside the mod: page 9 (the Ethereal Void's map)
    /// gets a warning; from page 10 come the hints (in the order the logic reaches the items, by
    /// the name of the hand-made zone they lie in, or the level and the part of it), on as many
    /// pages as they need; then a page that says the solutions come next and nothing else; then
    /// the solutions (level, square and what holds it). Only the items worth a hint are given:
    /// the artifacts (talismans, the Key of Infinity's parts, plot items) and the key items the
    /// shuffled game cannot be finished without (KeyShuffleResult.WorthAHint), so not a key to a
    /// door the logic lets the player bash, pick or go round. The map screen draws notes in FONT4X5P.SYS
    /// (ui/AUTOMAP.C RedisplayStrings), which has capitals only: every line is in capitals
    /// (Automap.NoteChars; a lowercase letter hangs the game on that page), at most 246 pixels wide
    /// from x 12, so it ends on the parchment before the buttons, 25 lines a page from y 186 down
    /// to 12 (y counts up from the bottom; a note's y is its baseline, the text above it).
    /// Page 9 is safe for notes like any other: its only differences are that no map is loaded
    /// for it unless the player is on level 9 (ChangeAutoMapLevel) and no player marker
    /// (ShowAutoMapLevel). Pages 9 to 99 read blocks 0x2C to 0x86, inside LEV.ARK's 135
    /// (sys/ARC.C get_arc), each at most 100 notes (ATM_Strings), and an empty page is no block.
    /// </summary>
    public static class SpoilerNotes
    {
        public const int WarningPage = 9, HintsPage = 10;
        const int Left = 12, Top = 186, Bottom = 12, LineStep = 7, MaxWidth = 246;
        public static int LinesPerPage { get { return (Top - Bottom) / LineStep + 1; } }
        /// <summary>The first words of the notes this writes, to find them again (and replace them) on a later run.</summary>
        public const string WarningMark = "SPOILERS", HintsMark = "HINTS", AnswersMark = "SOLUTIONS", DividerMark = "SOLUTIONS NEXT";

        // ------------------------------------------------------------ the font

        /// <summary>Each character's width in pixels, spacing included, in the notes' font (FONT4X5P.SYS: six header words, then each character's bitmap and its width byte); 6 for all when the file is missing.</summary>
        public static int[] FontWidths(GameSession s)
        {
            var w = Enumerable.Repeat(6, 256).ToArray();
            try
            {
                string p = s == null ? null : GameProfile.Find(s.DataFolder, "FONT4X5P.SYS");
                if (p == null) return w;
                var d = File.ReadAllBytes(p);
                int size = d[2] | d[3] << 8;
                int n = size <= 0 ? 0 : (d.Length - 12) / (size + 1);
                for (int i = 0; i < n && i < 256; i++) w[i] = d[12 + i * (size + 1) + size];
            }
            catch (Exception) { }
            return w;
        }

        public static int Width(string text, int[] widths) { return (text ?? "").Sum(c => c < widths.Length ? widths[c] : 6); }

        /// <summary>Plain ASCII the font has: accents dropped, typographic quotes made plain, anything else a '?'.</summary>
        public static string Ascii(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in (text ?? "").Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c == '‘' || c == '’') sb.Append('\'');
                else if (c == '“' || c == '”') sb.Append('"');
                else if (c == '–' || c == '—') sb.Append('-');
                else if (c >= 32 && c < 127) sb.Append(c);
                else if (!char.IsControl(c)) sb.Append('?');
            }
            return sb.ToString();
        }

        /// <summary>A line in capitals (Automap.NoteChars) split into notes that fit the width and the 49 characters a note holds; the parts after the first indented.</summary>
        public static List<string> Wrap(string line, int[] widths)
        {
            var parts = new List<string>();
            string cur = "";
            foreach (var word in Automap.NoteChars(line).Split(' '))
            {
                string next = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && (Width(next, widths) > MaxWidth || next.Length > Automap.TextSize - 1))
                {
                    parts.Add(cur);
                    cur = "  " + word;
                }
                else cur = next;
            }
            if (cur.Trim().Length > 0) parts.Add(cur);
            // a single word longer than a note: cut
            for (int i = 0; i < parts.Count; i++)
                while (parts[i].Length > 1 && (Width(parts[i], widths) > MaxWidth || parts[i].Length > Automap.TextSize - 1)) parts[i] = parts[i].Substring(0, parts[i].Length - 1);
            return parts;
        }

        // ------------------------------------------------------------ the lines

        /// <summary>The first lock of each lock number (keys open locks whose link &amp; 0x1FF is their owner: obj/USEITEMS.C checkLock), to name keys by.</summary>
        public static Dictionary<int, KeyPlace> Locks(GameSession s, Func<int, LevelModel> level)
        {
            var d = new Dictionary<int, KeyPlace>();
            try { foreach (var g in KeyIndex.Build(s, level)) if (g.Locks.Count > 0) d[g.Id] = g.Locks.OrderBy(l => l.Level).ThenBy(l => l.Index).First(); }
            catch (Exception) { }
            return d;
        }

        /// <summary>The name a player knows an item by: its proper name; a key by the first lock it opens (its look text is a sentence: STRINGS.PAK block 5, 0x64 + its lock).</summary>
        static string ItemName(GameSession s, ShuffleObject o, Dictionary<int, KeyPlace> locks)
        {
            string n = NamedItems.Of(s.Kind, o.Item) ?? o.Name ?? "";
            KeyPlace lk;
            if (KeyIndex.IsKey(o.Item, s.Kind) && locks != null && locks.TryGetValue(o.Record.Owner, out lk))
                n = string.Format("key to {0}, L{1} ({2}, {3})", string.IsNullOrEmpty(lk.HolderName) ? "a lock" : lk.HolderName, lk.Level, lk.TileX, lk.TileY);
            n = n.Trim();
            return n.Length == 0 ? "item 0x" + o.Item.ToString("X") : char.ToUpperInvariant(n[0]) + n.Substring(1);
        }

        /// <summary>The part of the level a square is in: a third of the open tiles' extent each way (northwest … center … southeast).</summary>
        public static string PartOfLevel(LevelModel m, int x, int y)
        {
            int x0 = 63, x1 = 0, y0 = 63, y1 = 0;
            if (m != null)
                for (int ty = 0; ty < 64; ty++)
                    for (int tx = 0; tx < 64; tx++)
                        if (!m.Tiles[tx, ty].IsSolid) { x0 = Math.Min(x0, tx); x1 = Math.Max(x1, tx); y0 = Math.Min(y0, ty); y1 = Math.Max(y1, ty); }
            if (x1 < x0) { x0 = y0 = 0; x1 = y1 = 63; }
            int col = Third(x, x0, x1), row = Third(y, y0, y1);
            string ns = row == 2 ? "north" : row == 0 ? "south" : "", ew = col == 2 ? "east" : col == 0 ? "west" : "";
            return ns.Length + ew.Length == 0 ? "center" : ns + ew;
        }

        static int Third(int v, int lo, int hi)
        {
            double f = hi <= lo ? 0.5 : (v - lo + 0.5) / (hi - lo + 1);
            return f < 1 / 3.0 ? 0 : f < 2 / 3.0 ? 1 : 2;
        }

        static string Holder(ShuffleObject o)
        {
            if (string.IsNullOrEmpty(o.Holder)) return "";
            // "in #12 sack", "carried by #200 goblin (locked: …)": without the record numbers and the lock token
            string h = System.Text.RegularExpressions.Regex.Replace(o.Holder, @"#\d+ ", "");
            int p = h.IndexOf(" (locked", StringComparison.Ordinal);
            if (p >= 0) h = h.Substring(0, p) + " (locked)";
            return h.Trim();
        }

        static IEnumerable<KeyShuffleMove> KeyMoves(KeyShuffleResult r)
        {
            return r.Moves.Where(r.WorthAHint).OrderBy(m => m.Sphere < 0 ? int.MaxValue : m.Sphere).ThenBy(m => m.Place.Level).ThenBy(m => m.Item.Name).ThenBy(m => m.Item.Index);
        }

        /// <summary>One hint a key item: where by the hand-made zone's name (and the level), or by the level and its part.</summary>
        public static List<string> Hints(GameSession s, KeyShuffleResult r, Func<int, LevelModel> level)
        {
            var list = new List<string>();
            var locks = Locks(s, level);
            foreach (var m in KeyMoves(r))
            {
                var p = m.Place;
                string zone = KeyShuffle.PlaceName(r.Options.Names, p.Level, p.X, p.Y).Trim('“', '”', '"');
                LevelModel lm = null;
                if (zone.Length == 0) { try { lm = level(p.Level); } catch (Exception) { } }
                string where = zone.Length > 0 ? zone + " (L" + p.Level + ")" : "level " + p.Level + ", " + PartOfLevel(lm, p.X, p.Y);
                list.Add(ItemName(s, m.Item, locks) + ": " + where + (string.IsNullOrEmpty(p.Holder) ? "" : ", hidden"));
            }
            return list;
        }

        /// <summary>One answer a key item: the level, the square, and what holds it.</summary>
        public static List<string> Answers(GameSession s, KeyShuffleResult r, Func<int, LevelModel> level)
        {
            var list = new List<string>();
            var locks = Locks(s, level);
            foreach (var m in KeyMoves(r))
            {
                var p = m.Place;
                string h = Holder(p);
                list.Add(string.Format("{0}: L{1} ({2}, {3}){4}", ItemName(s, m.Item, locks), p.Level, p.X, p.Y, h.Length > 0 ? " " + h : ""));
            }
            return list;
        }

        // ------------------------------------------------------------ the pages

        /// <summary>The notes of each page this writes: 9 (the warning, added to the level's own notes elsewhere), the hint pages and the answer pages.</summary>
        public static Dictionary<int, List<MapNote>> Pages(GameSession s, KeyShuffleResult r, Func<int, LevelModel> level)
        {
            var widths = FontWidths(s);
            var pages = new Dictionary<int, List<MapNote>>();
            var hints = Hints(s, r, level).SelectMany(l => Wrap(l, widths)).ToList();
            var answers = Answers(s, r, level).SelectMany(l => Wrap(l, widths)).ToList();
            int body = LinesPerPage - 1;
            int hintPages = Math.Max(1, (hints.Count + body - 1) / body), answerPages = Math.Max(1, (answers.Count + body - 1) / body);
            int divider = HintsPage + hintPages, firstAnswer = divider + 1;
            for (int k = 0; k < hintPages; k++)
            {
                int page = HintsPage + k;
                string head = string.Format("{0} {1}/{2} ({3})", HintsMark, k + 1, hintPages, k + 1 < hintPages ? "more on page " + (page + 1) : "solutions after page " + divider);
                pages[page] = Lay(new[] { head }.Concat(hints.Skip(k * body).Take(body)));
            }
            pages[divider] = Lay(new[] { DividerMark, "The pages after this one give every", "solution: where each item lies.", "Turn back now if you want to search." }.SelectMany(l => Wrap(l, widths)));
            for (int k = 0; k < answerPages; k++)
            {
                int page = firstAnswer + k;
                string head = string.Format("{0} {1}/{2}{3}", AnswersMark, k + 1, answerPages, k + 1 < answerPages ? " (more on page " + (page + 1) + ")" : "");
                pages[page] = Lay(new[] { head }.Concat(answers.Skip(k * body).Take(body)));
            }
            pages[WarningPage] = Lay(Warning(r).SelectMany(l => Wrap(l, widths)));
            return pages;
        }

        /// <summary>The warning on page 9.</summary>
        public static List<string> Warning(KeyShuffleResult r)
        {
            return new List<string>
            {
                WarningMark + " AHEAD: the next map pages hold the",
                "key item scatter's log (seed " + r.UsedSeed + ").",
                "From page " + HintsPage + ": hints, in the order you",
                "can reach the items; then a page saying",
                "solutions next, and the solutions.",
                "Do not turn the page if you want to search!",
            };
        }

        /// <summary>Lines (each fitting a note) as notes from the top of the page down, as many as the page holds, in capitals.</summary>
        static List<MapNote> Lay(IEnumerable<string> lines)
        {
            var notes = new List<MapNote>();
            int y = Top;
            foreach (var l in lines)
            {
                if (notes.Count >= LinesPerPage) break;
                notes.Add(new MapNote { Text = Automap.Clean(l), X = Left, Y = y });
                y -= LineStep;
            }
            return notes;
        }

        /// <summary>A line of the warning, as this writes it (capitals) or as version 0.1.8 did (lowercase, which hung the game).</summary>
        static bool Ours(MapNote n)
        {
            string t = n.Text ?? "";
            var ic = StringComparison.OrdinalIgnoreCase;
            return t.StartsWith(WarningMark + " AHEAD", ic) || t.StartsWith("key item scatter's log", ic)
                || t.StartsWith("Page " + HintsPage + ": hints", ic) || t.StartsWith("the items. Page 11", ic)
                || t.StartsWith("From page " + HintsPage + ": hints", ic) || t.StartsWith("can reach the items; then", ic) || t.StartsWith("solutions next, and the", ic)
                || t.StartsWith("Do not turn the page", ic);
        }

        static bool IsLogPage(List<MapNote> notes)
        {
            // this version's pages, and 0.1.8's answer pages (ANSWERS)
            return notes.Count > 0 && (notes[0].Text.StartsWith(HintsMark + " ", StringComparison.OrdinalIgnoreCase) || notes[0].Text.StartsWith(AnswersMark + " ", StringComparison.OrdinalIgnoreCase)
                || notes[0].Text.StartsWith("ANSWERS ", StringComparison.OrdinalIgnoreCase) || notes[0].Text.Equals(DividerMark, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Writes the pages into the editor's map notes (UW1): page 9 keeps its own notes and gets
        /// the warning, the log's pages are replaced, and pages an earlier log left (they start
        /// with HINTS or ANSWERS) are emptied. Returns each page changed with its map and notes as
        /// they were, for Undo.
        /// </summary>
        public static Dictionary<int, Tuple<byte[], List<MapNote>>> Write(GameSession s, LevelEdits edits, KeyShuffleResult r, string label)
        {
            var before = new Dictionary<int, Tuple<byte[], List<MapNote>>>();
            if (s.Kind != GameKind.UW1 || !r.Success) return before;
            var pages = Pages(s, r, edits.Model);
            Action<int, List<MapNote>> put = (page, notes) =>
            {
                var a = edits.AutomapFor(page);
                if (a.Notes.Count == notes.Count && a.Notes.Zip(notes, (x, y) => x.Text == y.Text && x.X == y.X && x.Y == y.Y).All(b => b)) return;
                before[page] = Tuple.Create((byte[])a.Map.Clone(), a.Notes.Select(n => new MapNote { Text = n.Text, X = n.X, Y = n.Y }).ToList());
                a.Replace(a.Map, notes, label);
                edits.AutomapEdited(a);
            };
            // page 9: the level's own notes, without an earlier warning, then the warning
            var nine = edits.AutomapFor(WarningPage);
            put(WarningPage, nine.Notes.Where(n => !Ours(n)).Concat(pages[WarningPage]).Take(Automap.MaxNotes).ToList());
            for (int page = HintsPage; page <= Automap.LastNotesPage; page++)
            {
                List<MapNote> mine;
                if (pages.TryGetValue(page, out mine)) { put(page, mine); continue; }
                if (!s.LevArk.Has(Automap.NotesBlock(s.Kind, page)) && edits.Automaps.All(a => a.Level != page)) continue;
                var a = edits.AutomapFor(page);
                if (IsLogPage(a.Notes)) put(page, new List<MapNote>());
            }
            return before;
        }
    }
}
