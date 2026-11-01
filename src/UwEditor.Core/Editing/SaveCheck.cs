using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>One problem a saved game has, and its repair.</summary>
    public sealed class SaveFinding
    {
        public int Level;
        /// <summary>"notes", "automap" or "objects".</summary>
        public string Kind;
        public string Text;
        /// <summary>What the repair does; null when there is none.</summary>
        public string FixText;
        /// <summary>Problems crash or break the game; warnings do not.</summary>
        public bool Problem = true;
        internal Func<LevelEdits, bool> Repair;
        public bool CanFix { get { return Repair != null; } }
        public override string ToString() { return (Problem ? "✖ " : "▲ ") + "Level " + Level + ", " + Kind + ": " + Text; }
    }

    /// <summary>
    /// Checks a saved game for what makes the original game crash or complain on loading it, and
    /// repairs it as unsaved edits:
    /// - map notes read from another block (UW1's LEV.ARK has no lengths, and the game writes a
    ///   level's emptied notes as a block of length 0 that then shares the next block's offset:
    ///   Underworld Exhumed's issue 7), notes with no end (the map screen copies each into a
    ///   50-byte buffer: AUTOMAP.C RedisplayStrings), notes blocks that are not whole notes or
    ///   hold more than 100 (GetTheWords reads the block into ATM_Strings[100] unchecked), notes
    ///   off the map screen;
    /// - automap blocks of another length than 0 or 0x1000 (GetAutoMapLevel reads into the
    ///   0x1000-byte map before it checks the length) or read from another block;
    /// - the object lists: the list errors of <see cref="LevelFixes"/>, among them the objects
    ///   UW1 loses when part of a stack proves too heavy to pick up, which make the game say
    ///   "Problems in object list" (Underworld Exhumed's issue 9 and its tidy-objects).
    /// </summary>
    public static class SaveCheck
    {
        const int ScreenW = 320, ScreenH = 200;

        public static List<SaveFinding> Find(GameSession s, LevelEdits edits)
        {
            var list = new List<SaveFinding>();
            var ark = s.LevArk;
            var uw1 = ark as Uw1Archive;
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                int lv = slot.Level;
                var edited = edits?.Automaps.FirstOrDefault(a => a.Level == lv && a.Dirty);
                // ---- notes (unless already edited here: then they are written whole on save)
                int nb = Automap.NotesBlock(s.Kind, lv);
                if (edited == null && ark.Has(nb))
                {
                    var b = ark.Read(nb) ?? new byte[0];
                    int alias = uw1 != null ? uw1.AliasOf(nb) : -1;
                    if (alias >= 0)
                        list.Add(new SaveFinding
                        {
                            Level = lv, Kind = "notes",
                            Text = string.Format("the map notes are read from {0} (all of this level's notes were erased once, and the game wrote an empty block that shares the next one's place); the {1} \"notes\" there have no end, and opening the map can crash the game", Describe(s, alias), b.Length / Automap.NoteSize),
                            FixText = "Drop the notes block (the level has no notes)",
                            Repair = e => { var a = e.AutomapFor(lv); a.Replace(a.Map, new List<MapNote>(), "Repair: drop the notes read from another block"); ForceDirty(e, a); return true; },
                        });
                    else
                    {
                        var why = new List<string>();
                        if (b.Length % Automap.NoteSize != 0) why.Add(string.Format("the block is {0} bytes, not whole notes of {1}", b.Length, Automap.NoteSize));
                        int count = b.Length / Automap.NoteSize;
                        if (count > Automap.MaxNotes) why.Add(string.Format("it holds {0} notes, more than the game's 100 (it reads them all into room for 100)", count));
                        int noEnd = 0, offScreen = 0;
                        for (int k = 0; k < Math.Min(count, Automap.MaxNotes); k++)
                        {
                            int at = k * Automap.NoteSize;
                            bool end = false;
                            for (int i = 0; i < Automap.TextSize; i++) if (b[at + i] == 0) { end = true; break; }
                            int x = (short)(b[at + 0x32] | b[at + 0x33] << 8), y = (short)(b[at + 0x34] | b[at + 0x35] << 8);
                            if (!end && x >= 0) noEnd++;
                            if (x >= 0 && (x >= ScreenW || y < 0 || y >= ScreenH)) offScreen++;
                        }
                        if (noEnd > 0) why.Add(noEnd + " note(s) with no end within 50 bytes (the map screen copies a note into 50 bytes)");
                        bool bad = why.Count > 0;
                        if (offScreen > 0) why.Add(offScreen + " note(s) off the map screen");
                        if (why.Count > 0)
                            list.Add(new SaveFinding
                            {
                                Level = lv, Kind = "notes", Problem = bad, Text = string.Join("; ", why),
                                FixText = "Keep the whole notes that end and lie on the screen (at most 100); drop the rest",
                                Repair = e => { var a = e.AutomapFor(lv); a.Replace(a.Map, CleanNotes(b), "Repair: map notes"); ForceDirty(e, a); return true; },
                            });
                    }
                }
                // ---- the automap
                int mb = Automap.MapBlock(s.Kind, lv);
                if (edited == null && ark.Has(mb))
                {
                    var b = ark.Read(mb) ?? new byte[0];
                    int alias = uw1 != null ? uw1.AliasOf(mb) : -1;
                    // a block that shares its offset with notes is the automap the notes are read from: that is the notes' problem
                    bool aliasIsNotes = alias >= 0 && alias >= Automap.NotesBlock(s.Kind, 1) && alias <= Automap.NotesBlock(s.Kind, s.Profile.LevelSlots);
                    if (b.Length != 0 && b.Length != 64 * 64)
                        list.Add(new SaveFinding
                        {
                            Level = lv, Kind = "automap", Text = string.Format("the automap block is {0} bytes, not 4096: the game copies it into its 4096-byte map before it checks the length", b.Length),
                            FixText = "Keep the first 4096 bytes (or pad with unexplored tiles)",
                            Repair = e => { var a = e.AutomapFor(lv); a.Replace(a.Map, a.Notes, "Repair: automap length"); ForceDirty(e, a); return true; },
                        });
                    else if (alias >= 0 && !aliasIsNotes)
                        list.Add(new SaveFinding
                        {
                            Level = lv, Kind = "automap", Problem = false, Text = "the automap is read from " + Describe(s, alias),
                            FixText = "Write it as a block of its own",
                            Repair = e => { var a = e.AutomapFor(lv); a.Replace(a.Map, a.Notes, "Repair: automap block"); ForceDirty(e, a); return true; },
                        });
                }
                // ---- the object lists
                LevelModel m;
                try { m = edits != null ? edits.Model(lv) : s.LoadLevel(lv); } catch (Exception ex) { list.Add(new SaveFinding { Level = lv, Kind = "objects", Text = "the level does not load: " + ex.Message }); continue; }
                bool ok;
                string check = new LevelBlock(m.Block.Data, s.Kind).Check(out ok);
                var errors = LevelFixes.Find(s, lv, m.Block.Data).Where(f => f.Severity == FixSeverity.ListError).ToList();
                if (!ok || errors.Count > 0)
                {
                    string text = errors.Count > 0 ? string.Join(" ", errors.Take(4).Select(f => f.Text)) + (errors.Count > 4 ? " (" + (errors.Count - 4) + " more)" : "") : check;
                    bool lost = errors.Count > 0 && errors.All(f => f.Check == LevelFixes.Unreferenced);
                    list.Add(new SaveFinding
                    {
                        Level = lv, Kind = "objects",
                        Text = (lost ? "objects lost from every list (as UW1 loses part of a stack that proves too heavy to pick up): the game says \"Problems in object list\" here. " : "the object lists fail the game's check. ") + text,
                        FixText = errors.Any(f => f.CanFix) ? "Repair the lists (Level ▸ Fixes' repairs: free what is lost, rebuild the free stacks…)" : null,
                        Repair = errors.Any(f => f.CanFix) ? (Func<LevelEdits, bool>)(e =>
                        {
                            var ed = e.For(lv);
                            int n = 0;
                            ed.Batch("Repair the object lists", () =>
                            {
                                for (int pass = 0; pass < 4; pass++)
                                {
                                    var now = LevelFixes.Find(ed).Where(f => f.Severity == FixSeverity.ListError && f.CanFix).ToList();
                                    if (now.Count == 0) break;
                                    foreach (var f in now) if (LevelFixes.Fix(ed, f)) n++;
                                }
                            });
                            return n > 0;
                        }) : null,
                    });
                }
            }
            return list;
        }

        static string Describe(GameSession s, int block)
        {
            for (int lv = 1; lv <= s.Profile.LevelSlots; lv++)
            {
                if (block == Automap.MapBlock(s.Kind, lv)) return "level " + lv + "'s automap (block " + block + ")";
                if (block == Automap.NotesBlock(s.Kind, lv)) return "level " + lv + "'s notes (block " + block + ")";
                if (block == s.Profile.LevelBlock(lv)) return "level " + lv + "'s map (block " + block + ")";
            }
            return "block " + block;
        }

        /// <summary>The notes worth keeping from a block: whole, ending within their text, on the screen, not erased; at most 100.</summary>
        public static List<MapNote> CleanNotes(byte[] b)
        {
            var keep = new List<MapNote>();
            for (int at = 0; at + Automap.NoteSize <= b.Length && keep.Count < Automap.MaxNotes; at += Automap.NoteSize)
            {
                int len = -1;
                for (int i = 0; i < Automap.TextSize; i++) if (b[at + i] == 0) { len = i; break; }
                int x = (short)(b[at + 0x32] | b[at + 0x33] << 8), y = (short)(b[at + 0x34] | b[at + 0x35] << 8);
                if (len < 0 || x < 0 || x >= ScreenW || y < 0 || y >= ScreenH) continue;
                keep.Add(new MapNote { Text = System.Text.Encoding.Latin1.GetString(b, at, len), X = (short)x, Y = (short)y });
            }
            return keep;
        }

        /// <summary>Tells the editor the automap changed (Replace marked it so; it is written whole on save).</summary>
        static void ForceDirty(LevelEdits e, Automap a) { e.AutomapEdited(a); }

        /// <summary>Repairs every finding that has a repair, as unsaved edits; returns how many.</summary>
        public static int RepairAll(List<SaveFinding> findings, LevelEdits edits)
        {
            int n = 0;
            foreach (var f in findings.Where(f => f.CanFix)) if (f.Repair(edits)) n++;
            return n;
        }

        public static bool Repair(SaveFinding f, LevelEdits edits) { return f.CanFix && f.Repair(edits); }
    }
}
