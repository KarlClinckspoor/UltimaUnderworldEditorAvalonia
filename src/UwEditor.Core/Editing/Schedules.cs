using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// UW2's SCD.ARK as the editor edits it: the saved game's copy when a save is open (the
    /// game runs the schedules from the save directory, event/SCHEDULE.C Sched_Load), else
    /// DATA's, which a new game copies into SAVE0 (game/GAMEWRAP.C). Rows are edited in place,
    /// each block kept in time order; Save writes the changed blocks through the archive
    /// layer (put_arc) and SafeFile.
    /// </summary>
    public sealed class Schedules
    {
        public GameSession Session { get; private set; }
        /// <summary>The SCD.ARK edited.</summary>
        public string Path { get; private set; }
        /// <summary>"SAVE1" or "DATA": where the file is.</summary>
        public string Where { get; private set; }
        public Uw2Archive Archive { get; private set; }
        public List<ScdBlock> Blocks { get; private set; }
        readonly Dictionary<int, string> races = new Dictionary<int, string>();

        /// <summary>The SCD.ARK of the open game: the save's when it has one, else DATA's; null when there is none.</summary>
        public static string Locate(GameSession s)
        {
            if (s.IsSave)
            {
                string p = GameProfile.Find(s.SaveFolder, "SCD.ARK");
                if (p != null) return p;
            }
            return GameProfile.Find(s.DataFolder, "SCD.ARK");
        }

        /// <summary>Reads the open game's schedules. Throws for UW1 (it has none) or a missing file.</summary>
        public static Schedules Load(GameSession s)
        {
            if (!s.IsUw2) throw new InvalidOperationException("Schedules (SCD.ARK) belong to Ultima Underworld II.");
            string p = Locate(s);
            if (p == null) throw new FileNotFoundException("There is no SCD.ARK in " + (s.IsSave ? s.SaveFolder + " or " : "") + s.DataFolder + ".");
            var sc = new Schedules { Session = s, Path = p };
            sc.Where = s.IsSave && string.Equals(System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(p)), System.IO.Path.GetFullPath(s.SaveFolder), StringComparison.OrdinalIgnoreCase)
                ? System.IO.Path.GetFileName(s.SaveFolder) : "DATA";
            foreach (var c in Races.Of(s)) if (c.Value > 0) sc.races[c.Value] = c.Label.Substring(c.Label.IndexOf(':') + 1).Trim();
            sc.Reload();
            return sc;
        }

        void Reload()
        {
            Archive = (Uw2Archive)UltimateEditor.Games.Archive.Load(Path, GameKind.UW2);
            Blocks = ScdArk.Read(Archive);
            while (Blocks.Count < ScdArk.Blocks) Blocks.Add(new ScdBlock());
        }

        /// <summary>Some block differs from the file.</summary>
        public bool HasChanges
        {
            get
            {
                for (int b = 0; b < Blocks.Count && b < Archive.Count; b++) if (ScdArk.Changed(Archive, Blocks, b)) return true;
                return false;
            }
        }

        /// <summary>Forgets the edits: the file as it is on disk.</summary>
        public void Revert() { Reload(); }

        /// <summary>The blocks out of time order, as messages (empty when all are in order).</summary>
        public List<string> Problems()
        {
            var list = new List<string>();
            for (int b = 0; b < Blocks.Count; b++)
            {
                int i = Blocks[b].FirstOutOfOrder();
                if (i >= 0) list.Add(string.Format("Block {0} row {1} is earlier than the row before it.", b, i));
            }
            return list;
        }

        /// <summary>Writes the changed blocks (SCD.ARK.BKP keeps the old file). Returns the blocks written.</summary>
        public List<int> Save()
        {
            var p = Problems();
            if (p.Count > 0) throw new InvalidOperationException("Not saved: " + string.Join(" ", p));
            var written = ScdArk.Write(Archive, Blocks);
            if (written.Count > 0) Conversations.SafeFile.Write(Path, Archive.ToBytes());
            return written;
        }

        // ---- names

        /// <summary>The NPC of a conversation slot: string whoami + 16 of block 7, or null.</summary>
        public string NpcName(int whoami)
        {
            if (whoami <= 0) return null;
            string n = Session.Strings.Get(7, whoami + 16);
            return string.IsNullOrWhiteSpace(n) ? null : n.Trim();
        }

        /// <summary>The creatures of a race (OBJECTS.DAT), or null.</summary>
        public string RaceName(int race) { string n; return races.TryGetValue(race, out n) ? n : null; }

        public string Describe(ScdRow r) { return r.Describe(NpcName, RaceName); }

        public string Meaning(ScdRow r, ScdField f) { return r.Meaning(f, NpcName, RaceName); }

        /// <summary>The fields of a row: time, level, once and event, then the event's parameters.</summary>
        public List<ScdField> Fields(int block, ScdRow r) { return ScdRow.HeaderFields(block).Concat(r.ParameterFields()).ToList(); }

        // ---- editing

        /// <summary>
        /// Sets a field of row index of a block. A new time moves the row to its place (rows of
        /// the same time keep their order; a test row takes its disabled rows along, and they
        /// take its time). Returns the row's index afterwards.
        /// </summary>
        public int SetField(int block, int index, ScdField f, int value)
        {
            var blk = Blocks[block];
            var r = blk.Rows[index];
            if (f.Get(r) == value) return index;
            f.Set(r, value);
            if (f.Kind == "time")
            {
                // a test row runs the disabled rows right after it (SCDEVENT.C ev_checkvar): they move with it
                for (int j = index + 1; r.Event == 10 && j < blk.Rows.Count && blk.Rows[j].Event < 0; j++) blk.Rows[j].Time = value;
                // SCHEDULE.C stops at the first row past the clock, so the rows stay in time order
                blk.SortStable();
            }
            return blk.Rows.IndexOf(r);
        }

        /// <summary>
        /// Adds a row after row index (or at the start when index is -1, or the end of an empty
        /// block): a copy of that row, or an empty one (event 0, any level) with its time.
        /// Returns the new row's index.
        /// </summary>
        public int AddRow(int block, int index, bool copy)
        {
            var blk = Blocks[block];
            ScdRow r;
            if (index >= 0 && index < blk.Rows.Count && copy) r = blk.Rows[index].Clone();
            else
            {
                r = new ScdRow { Level = 0xFF, Event = 0 };
                if (index >= 0 && index < blk.Rows.Count) r.Time = blk.Rows[index].Time;
            }
            int at = Math.Min(blk.Rows.Count, index + 1);
            blk.Rows.Insert(at, r);
            return at;
        }

        public void DeleteRow(int block, int index)
        {
            Blocks[block].Rows.RemoveAt(index);
        }

        /// <summary>Swaps a row with its neighbour (d -1 up, +1 down) when both have the same time; false otherwise.</summary>
        public bool MoveRow(int block, int index, int d)
        {
            var rows = Blocks[block].Rows;
            int j = index + d;
            if (j < 0 || j >= rows.Count || rows[j].Time != rows[index].Time) return false;
            var t = rows[index]; rows[index] = rows[j]; rows[j] = t;
            return true;
        }
    }
}
