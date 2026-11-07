using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// CNV.ARK, both games: one conversation per block. A creature talks through block whoami,
    /// or, with whoami 0, through the generic block 0x100 + (item &amp; 0x3F) (conv/CONVERSE.C).
    /// UW2's blocks are compressed; the archive code reads and writes both formats. Saving
    /// writes only the blocks whose bytes changed, so the others stay exactly as they were.
    /// </summary>
    public sealed class ConversationArchive
    {
        public GameKind Kind { get; private set; }
        public string Path { get; private set; }
        Archive archive;
        readonly Dictionary<int, Conversation> changed = new Dictionary<int, Conversation>();

        public static ConversationArchive Load(string path, GameKind kind)
        {
            return new ConversationArchive { Kind = kind, Path = path, archive = Archive.Load(path, kind) };
        }

        public static ConversationArchive FromBytes(byte[] data, GameKind kind)
        {
            return new ConversationArchive { Kind = kind, archive = Archive.Open(data, kind) };
        }

        public int Count { get { return archive.Count; } }

        /// <summary>The biggest block the game's buffer takes (load_script: 0x4000; UW2 0x5000, from the Unity editor's converter).</summary>
        public int MaxBlockBytes { get { return MaxBlockBytesFor(Kind); } }

        public static int MaxBlockBytesFor(GameKind kind) { return kind == GameKind.UW2 ? 0x5000 : 0x4000; }

        public bool Has(int slot)
        {
            if (changed.ContainsKey(slot)) return changed[slot] != null;
            if (!archive.Has(slot)) return false;
            var b = archive.Read(slot);
            return b != null && b.Length >= 0x10;
        }

        public IEnumerable<int> Slots()
        {
            for (int s = 0; s < Count; s++) if (Has(s)) yield return s;
        }

        public byte[] RawBlock(int slot) { return archive.Has(slot) ? archive.Read(slot) : null; }

        /// <summary>A fresh copy of the conversation in a slot (the edited one, if there is one), or null.</summary>
        public Conversation Read(int slot)
        {
            Conversation c;
            if (changed.TryGetValue(slot, out c)) return c == null ? null : c.Clone();
            if (!Has(slot)) return null;
            return Conversation.Parse(archive.Read(slot), slot);
        }

        /// <summary>Replaces a slot's conversation (null clears it) until Save.</summary>
        public void Write(int slot, Conversation c)
        {
            if (slot < 0 || slot >= Count) throw new ArgumentOutOfRangeException("slot", "CNV.ARK has slots 0-" + (Count - 1) + ".");
            if (c != null)
            {
                int bytes = c.ToBytes().Length;
                if (bytes > MaxBlockBytes)
                    throw new InvalidDataException(string.Format("Conversation {0} is {1} bytes; the game reads at most {2} (0x{2:X}).", slot, bytes, MaxBlockBytes));
            }
            changed[slot] = c == null ? null : c.Clone();
        }

        public bool IsChanged(int slot) { return changed.ContainsKey(slot); }
        public bool HasChanges { get { return changed.Count > 0; } }
        public IEnumerable<int> ChangedSlots() { return changed.Keys; }
        public void Revert(int slot) { changed.Remove(slot); }

        /// <summary>The archive with the changes in it. Blocks whose bytes did not change are left alone.</summary>
        public byte[] ToBytes(List<int> written = null)
        {
            var copy = Archive.Open(archive.ToBytes(), Kind);
            foreach (var kv in changed)
            {
                byte[] old = copy.Has(kv.Key) ? copy.Read(kv.Key) : null;
                if (kv.Value == null)
                {
                    if (old != null) { copy.Delete(kv.Key); if (written != null) written.Add(kv.Key); }
                    continue;
                }
                byte[] now = kv.Value.ToBytes();
                if (old != null && Same(old, now)) continue;
                copy.Write(kv.Key, now);
                if (written != null) written.Add(kv.Key);
            }
            return copy.ToBytes();
        }

        /// <summary>Writes CNV.ARK (keeping the old file as CNV.ARK.BKP) and makes the saved state the new base.</summary>
        public List<int> Save(string path = null)
        {
            path = path ?? Path;
            var written = new List<int>();
            byte[] bytes = ToBytes(written);
            SafeFile.Write(path, bytes);
            archive = Archive.Open(bytes, Kind);
            changed.Clear();
            Path = path;
            return written;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// Writing a game file safely: to a temporary file first, the old one kept as NAME.EXT.BKP.
    /// The first time the editor writes a file, the file as it was is also kept as
    /// NAME.EXT.ORIG and never overwritten: the reference mods are made against.
    /// </summary>
    public static class SafeFile
    {
        public const string OriginalSuffix = ".ORIG";

        /// <summary>The untouched copy kept beside a file the editor has written, or null.</summary>
        public static string OriginalOf(string path)
        {
            string o = path + OriginalSuffix;
            return File.Exists(o) ? o : null;
        }

        /// <summary>The copy of the previous version kept when a file is written.</summary>
        public static string BackupOf(string path) { return path + ".BKP"; }

        public static void Write(string path, byte[] bytes)
        {
            if (File.Exists(path) && !File.Exists(path + OriginalSuffix)) File.Copy(path, path + OriginalSuffix);
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            string tmp = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path) + ".tmp");
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                // NAME.EXT.BKP: CS000.N01 and CS000.N02, or OBJECTS.DAT and OBJECTS.GR, keep backups of their own
                string bkp = BackupOf(path);
                try { File.Replace(tmp, path, bkp, true); }       // a rename: the game file is never half written
                catch (PlatformNotSupportedException) { File.Copy(path, bkp, true); File.Move(tmp, path, true); }
                catch (IOException) { File.Copy(path, bkp, true); File.Move(tmp, path, true); }
            }
            else File.Move(tmp, path);
        }
    }
}
