using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using MapNote = UwEditor.Core.Editing.MapNote;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// What stops the accent table's letters showing in the game: a letter used in text whose
    /// font has no glyph at its code (or still the shipped symbol), a code the DOS game draws as
    /// '?', and a mapped code in a map note (FONT4X5P, whose notes the game forces to capitals).
    /// </summary>
    public static class FontChecks
    {
        /// <summary>
        /// The fonts a block's strings are shown in. Every string can reach the scroll
        /// (FONT5X6P: messages, conversations, books, the inventory, cutscene subtitles); block 1,
        /// the game's own strings, also has the menus' (FONTBIG) and character creation's
        /// (FONTCHAR); block 2, attributes and skills, the stats' (FONT5X6I) and character
        /// creation's; cutscenes (3072 up) also their titles' (FONTBIG). An approximation on the
        /// safe side: a block's string may need a letter in every one of its fonts.
        /// </summary>
        public static string[] FontsFor(int block)
        {
            if (block == 1) return new[] { "FONT5X6P.SYS", "FONTBIG.SYS", "FONTCHAR.SYS" };
            if (block == 2) return new[] { "FONT5X6P.SYS", "FONT5X6I.SYS", "FONTCHAR.SYS" };
            if (block >= 0x0C00 && block < 0x0E00) return new[] { "FONT5X6P.SYS", "FONTBIG.SYS" };
            return new[] { "FONT5X6P.SYS" };
        }

        /// <summary>The fonts as the game shipped them: each FONT*.SYS.ORIG the editor kept, else the file.</summary>
        public static GameFonts Originals(string dataFolder)
        {
            var g = new GameFonts();
            foreach (var n in FontFiles.Names)
            {
                string p = GameProfile.Find(dataFolder, n);
                if (p == null) continue;
                string o = Conversations.SafeFile.OriginalOf(p);
                try
                {
                    var f = File.Exists(o) && new FileInfo(o).Length > 0 ? UwFont.Read(File.ReadAllBytes(o)) : UwFont.Read(File.ReadAllBytes(p));
                    f.Name = n;
                    g.ByName[n] = f;
                }
                catch (Exception) { }
            }
            return g;
        }

        /// <summary>
        /// The problems, one line each (none: an empty list). strings: every block's strings as
        /// the game has them (codes, not letters). originals: the shipped fonts, for telling a
        /// glyph still the shipped symbol (null: not checked). notes: the map notes, by page.
        /// </summary>
        public static List<string> Run(AccentTable table, GameFonts fonts, IEnumerable<(int Block, IList<string> Strings)> strings,
                                       GameFonts originals = null, IEnumerable<(int Page, IList<MapNote> Notes)> notes = null)
        {
            var r = new List<string>();
            if (table == null || table.IsEmpty) return r;
            // where each mapped code is used, by font: (count, first place)
            var uses = new Dictionary<(int Code, string Font), (int N, string First)>();
            var any = new Dictionary<int, int>();
            foreach (var b in strings ?? Enumerable.Empty<(int, IList<string>)>())
                for (int i = 0; i < b.Strings.Count; i++)
                    foreach (var code in (b.Strings[i] ?? "").Where(ch => table.LetterOf(ch) != null).Distinct())
                    {
                        int n0; any.TryGetValue(code, out n0); any[code] = n0 + 1;
                        foreach (var f in FontsFor(b.Block))
                        {
                            (int N, string First) u;
                            uses[(code, f)] = uses.TryGetValue((code, f), out u) ? (u.N + 1, u.First) : (1, "block " + b.Block + " string " + i);
                        }
                    }
            foreach (var kv in table.Map)
            {
                int code = kv.Key; char letter = kv.Value;
                string who = "'" + letter + "' (code " + code + (code >= 0x21 && code < 0x7F ? ", '" + (char)code + "'" : "") + ")";
                if (!AccentTable.GameDraws(code))
                    r.Add(who + ": the stock game draws code " + code + " as '?' (gfx/GRLIBI.ASM draws only 20h-7Ah). The game's executable would have to be changed for it to show (MANUAL.md, \"The accent table\").");
                foreach (var f in FontFiles.Names)
                {
                    (int N, string First) u;
                    if (!uses.TryGetValue((code, f), out u)) continue;
                    var font = fonts == null ? null : fonts[f];
                    if (font == null) continue;
                    var g = font.Glyphs[code];
                    string where = u.N + " string" + (u.N == 1 ? "" : "s") + " shown in " + f + " (first " + u.First + ")";
                    if (!g.Present || g.IsBlank)
                        r.Add(who + " is used in " + where + ", which has no glyph at code " + code + ".");
                    else if (originals != null && originals[f] != null && originals[f].Glyphs[code].Present && originals[f].Glyphs[code].SameAs(g) && code >= 0x21)
                        r.Add(who + " is used in " + where + ", whose glyph at code " + code + " is still the shipped '" + (char)code + "'.");
                }
            }
            if (notes != null)
                foreach (var p in notes)
                    for (int i = 0; i < p.Notes.Count; i++)
                    {
                        var codes = (p.Notes[i].Text ?? "").Where(ch => table.LetterOf(ch) != null).Distinct().ToList();
                        if (codes.Count > 0)
                            r.Add("Map page " + p.Page + ", note " + (i + 1) + " (\"" + p.Notes[i].Text + "\") has " + string.Join(", ", codes.Select(c => "code " + (int)c + " ('" + c + "', the table's '" + table.LetterOf(c) + "')")) + ": map notes are drawn in FONT4X5P and typed by the player.");
                    }
            return r;
        }

        /// <summary>The map notes of every page a game's LEV.ARK holds notes for.</summary>
        public static List<(int Page, IList<MapNote> Notes)> MapNotes(GameSession s)
        {
            var list = new List<(int, IList<MapNote>)>();
            if (s?.LevArk == null) return list;
            int last = s.Kind == GameKind.UW1 ? Automap.LastNotesPage : 79;
            for (int page = 0; page <= last; page++)
            {
                int nb = Automap.NotesBlock(s.Kind, page);
                try
                {
                    if (!s.LevArk.Has(nb)) continue;
                    var n = Automap.ReadNotes(s.LevArk.Read(nb));
                    if (n.Count > 0) list.Add((page, n));
                }
                catch (Exception) { }
            }
            return list;
        }
    }
}
