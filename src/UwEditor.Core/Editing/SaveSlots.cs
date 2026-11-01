using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>One of the game's four save slots, SAVE1 to SAVE4 next to DATA.</summary>
    public sealed class SaveSlot
    {
        public int Number { get; internal set; }
        /// <summary>SAVEn's path (it may not exist).</summary>
        public string Folder { get; internal set; }
        public bool Exists { get; internal set; }
        /// <summary>The game lists a slot whose DESC exists (GAMEWRAP.C get_save_descs).</summary>
        public bool Used { get; internal set; }
        /// <summary>The description, or "&lt;not used yet&gt;" as the game shows an unused slot.</summary>
        public string Description { get; internal set; }
        public bool HasLevels { get; internal set; }
        public DateTime Written { get; internal set; }
        public string Name { get { return "SAVE" + Number; } }
        public override string ToString() { return Name + ": " + Description; }
    }

    /// <summary>
    /// The save slots of a game folder (game/GAMEWRAP.C, both games). A save is a copy of the
    /// working folder SAVE0: LEV.ARK, PLAYER.DAT, BGLOBALS.DAT, DESC and (UW2) SCD.ARK.
    /// DESC is the description as raw text with no terminator, up to 30 characters (SaveGame:
    /// wdialog with 0x1E, blttodrive); the menu reads its first line, at most 38 characters
    /// (get_save_descs, fgets), and shows "&lt;not used yet&gt;" for a slot without one.
    /// UW2 writes a blank description as one space.
    /// </summary>
    public static class SaveSlots
    {
        public const int Count = 4;
        public const int DescMax = 30;
        public const string NotUsed = "<not used yet>";
        public const string BackupSuffix = ".BKP";

        /// <summary>The slots of the game folder (the folder holding DATA).</summary>
        public static List<SaveSlot> List(string gameFolder)
        {
            var list = new List<SaveSlot>();
            for (int n = 1; n <= Count; n++)
            {
                string folder = GameFiles.Resolve(Path.Combine(gameFolder, "SAVE" + n));
                var s = new SaveSlot { Number = n, Folder = folder, Exists = Directory.Exists(folder), Description = NotUsed };
                if (s.Exists)
                {
                    string desc = GameProfile.Find(folder, "DESC");
                    if (desc != null) { s.Used = true; s.Description = ReadDesc(desc); }
                    string lev = GameProfile.Find(folder, "LEV.ARK");
                    s.HasLevels = lev != null;
                    s.Written = Directory.GetFiles(folder).Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
                }
                list.Add(s);
            }
            return list;
        }

        /// <summary>DESC as the menu shows it: the first line, at most 38 characters.</summary>
        public static string ReadDesc(string path)
        {
            var b = File.ReadAllBytes(path);
            var sb = new StringBuilder();
            foreach (var x in b)
            {
                if (x == 0 || x == '\n' || x == '\r' || sb.Length >= 38) break;
                sb.Append((char)x);
            }
            return sb.ToString();
        }

        /// <summary>The bytes the game would write for a description: printable ASCII, at most 30, a blank one as one space.</summary>
        public static byte[] DescBytes(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
                if (c >= 0x20 && c < 0x7F && sb.Length < DescMax) sb.Append(c);
            if (sb.Length == 0) sb.Append(' ');
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        /// <summary>Writes a slot's DESC (SafeFile: DESC.BKP keeps the old one). The slot folder must exist.</summary>
        public static void Rename(SaveSlot slot, string text)
        {
            if (!Directory.Exists(slot.Folder)) throw new DirectoryNotFoundException(slot.Name + " does not exist: copy a slot there first.");
            string path = GameProfile.Find(slot.Folder, "DESC") ?? Path.Combine(slot.Folder, "DESC");
            Conversations.SafeFile.Write(path, DescBytes(text));
        }

        /// <summary>Where a replaced slot is kept: SAVEn.BKP next to it.</summary>
        public static string BackupOf(string folder) { return folder.TrimEnd('/', '\\') + BackupSuffix; }

        /// <summary>
        /// Copies every file of one slot to another, as SaveGame and RestoreGame copy a whole
        /// folder (GAMEWRAP.C copy_dir after clear_dir). The copy is made beside the target
        /// first; a target that existed is then kept as SAVEn.BKP (replacing an older one)
        /// and the copy takes its name, so a failure leaves the old slot as it was.
        /// Returns the backup folder, or null when the target did not exist.
        /// </summary>
        public static string Copy(SaveSlot from, SaveSlot to)
        {
            if (from.Number == to.Number) throw new InvalidOperationException("A slot cannot be copied onto itself.");
            if (!Directory.Exists(from.Folder)) throw new DirectoryNotFoundException(from.Name + " does not exist.");
            string target = to.Folder.TrimEnd('/', '\\');
            string tmp = target + ".tmp";
            if (Directory.Exists(tmp)) DeleteFolder(tmp);
            Directory.CreateDirectory(tmp);
            try
            {
                foreach (var f in Directory.GetFiles(from.Folder))
                {
                    string dest = Path.Combine(tmp, Path.GetFileName(f));
                    File.Copy(f, dest);
                    File.SetAttributes(dest, File.GetAttributes(dest) & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
                DeleteFolder(tmp);
                throw;
            }
            string backup = null;
            if (Directory.Exists(target))
            {
                backup = BackupOf(target);
                if (Directory.Exists(backup)) DeleteFolder(backup);
                Directory.Move(target, backup);
            }
            Directory.Move(tmp, target);
            return backup;
        }

        static void DeleteFolder(string folder)
        {
            foreach (var f in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }
    }
}
