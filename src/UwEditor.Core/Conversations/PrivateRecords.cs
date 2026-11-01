using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// Keeps the private globals' files in step with CNV.ARK. The game reads a conversation's
    /// private globals from its record in the save's BGLOBALS.DAT, at most the script's
    /// babl_nvars words of it, and writes them back to the same record (uw1/uw2 conv/BABL.C
    /// bab_get_globals, bab_put_globals); a conversation without a record keeps nothing, and
    /// words past the record's end are neither read nor kept. A new game makes BGLOBALS.DAT
    /// from DATA's BABGLOBS.DAT, the same records with every word 0 (init_babl). The shipped
    /// files have a record of exactly babl_nvars words for every conversation, in slot order.
    /// So when a conversation's variable count grows, or an empty slot gets a conversation,
    /// its record must grow or be added, in BABGLOBS.DAT and in the open save's BGLOBALS.DAT
    /// (new words 0, the others kept). A record is never shrunk: the game reads only its
    /// first babl_nvars words anyway.
    /// </summary>
    public static class PrivateRecords
    {
        /// <summary>Resizes or adds the records of the given slots to their conversations' variable counts. Returns the files written.</summary>
        public static List<string> Update(GameSession s, ConversationArchive cnv, IEnumerable<int> slots)
        {
            var written = new List<string>();
            var list = new List<int>(slots);
            if (list.Count == 0) return written;
            string data = GameProfile.Find(s.DataFolder, "BABGLOBS.DAT");
            if (data != null && Update(data, false, cnv, list)) written.Add(Path.GetFileName(data));
            string save = s.IsSave ? GameProfile.Find(s.SaveFolder, "BGLOBALS.DAT") : null;
            if (save != null && Update(save, true, cnv, list)) written.Add(Path.GetFileName(save) + " in " + Path.GetFileName(s.SaveFolder));
            return written;
        }

        /// <summary>One file: true when it had to change (and was written).</summary>
        public static bool Update(string path, bool withWords, ConversationArchive cnv, IEnumerable<int> slots)
        {
            var g = BGlobals.Parse(File.ReadAllBytes(path), withWords);
            if (!Fit(g, cnv, slots)) return false;
            SafeFile.Write(path, g.ToBytes(withWords));
            return true;
        }

        /// <summary>Grows or adds the records in memory; true when one changed.</summary>
        public static bool Fit(BGlobals g, ConversationArchive cnv, IEnumerable<int> slots)
        {
            bool changed = false;
            foreach (int slot in slots)
            {
                Conversation c;
                try { c = cnv.Read(slot); } catch (Exception) { continue; }
                if (c == null) continue;
                var r = g.Find(slot);
                if (r != null && r.Words.Length >= c.Variables) continue;
                g.Resize(slot, c.Variables);
                changed = true;
            }
            return changed;
        }
    }
}
