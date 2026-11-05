using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UltimateEditor.Games;
using Underworld.Mods;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Mods
{
    /// <summary>
    /// The editor's side of mods (format 2, docs/mod-format.md, built by Underworld.Mods): making
    /// one from the saved game folder, and applying one to it through SafeFile, so every file
    /// written keeps its .BKP and its first version (.ORIG).
    /// </summary>
    public static class EditorMods
    {
        /// <summary>"UwEditorAvalonia" and the program's version, for a mod's made_with.</summary>
        public static string MadeWith
        {
            get
            {
                var asm = typeof(EditorMods).Assembly;
                string v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "";
                int plus = v.IndexOf('+');
                return "UwEditorAvalonia " + (plus > 0 ? v.Substring(0, plus) : v);
            }
        }

        /// <summary>A file's bytes by a mod's path, or null when the folder lacks it.</summary>
        public static Func<string, byte[]> Reader(string folder)
        {
            return rel => { string p = GamePaths.Find(folder, rel); return p == null ? null : File.ReadAllBytes(p); };
        }

        /// <summary>The saved changes of a game folder: against a folder of originals, or (null) the .ORIG copies the editor kept.</summary>
        public static FolderChanges Collect(string gameFolder, GameKind kind, string referenceFolder)
        {
            return referenceFolder != null ? ModMaker.Compare(referenceFolder, gameFolder, kind) : ModMaker.FromOriginals(gameFolder, kind);
        }

        public static List<FileCheck> Check(ModPackage mod, string gameFolder) { return mod.Check(Reader(gameFolder)); }

        /// <summary>The note inside a .zip of changed files (README-CHANGED-FILES.txt), and the one the Mods window shows.</summary>
        public const string ZipWarning =
            "This .zip holds whole game files as the editor saved them: they are the game's data, so it is not for sharing in public " +
            "(share a .uwmod instead: it holds only the differences). It is not for the mod manager either, which takes .uwmod files. " +
            "Unzip it over your own copy of the game, keeping the folders, to get your changes there.";

        public const string ZipReadmeName = "README-CHANGED-FILES.txt";

        /// <summary>
        /// Writes the changed files of <paramref name="changes"/> (as saved, whole) into a .zip at
        /// their paths in the game folder (DATA/LEV.ARK, SOUND/12.VOC...), with a note saying
        /// what it is and is not for. Returns the paths written. Problems that keep the changes
        /// from being a mod do not stop the .zip: the files that did change go in.
        /// </summary>
        public static List<string> WriteZip(FolderChanges changes, string zipPath, string gameName)
        {
            if (changes.Changed.Count == 0) throw new InvalidOperationException("No changed files to put in the .zip: save your edits first.");
            string tmp = zipPath + ".tmp";
            var written = new List<string>();
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                var note = new System.Text.StringBuilder();
                note.Append("Changed files of ").Append(gameName).Append(", saved by ").Append(MadeWith).Append(" on ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(".\r\n\r\n");
                note.Append(ZipWarning).Append("\r\n\r\nFiles:\r\n");
                foreach (var c in changes.Changed.OrderBy(c => c.Path, StringComparer.Ordinal))
                {
                    var e = zip.CreateEntry(c.Path, System.IO.Compression.CompressionLevel.Optimal);
                    using (var s = e.Open()) s.Write(c.Edited, 0, c.Edited.Length);
                    written.Add(c.Path);
                    note.Append("  ").Append(c.Path.Replace('/', '\\')).Append(c.Original == null ? "  (new)" : "").Append("\r\n");
                }
                var r = zip.CreateEntry(ZipReadmeName);
                using (var s = r.Open()) { var b = System.Text.Encoding.UTF8.GetBytes(note.ToString()); s.Write(b, 0, b.Length); }
            }
            File.Move(tmp, zipPath, true);
            return written;
        }

        /// <summary>
        /// Applies a mod to a game folder: every file must be the original the mod was made from
        /// (or already the mod's result, which is left alone). All results are built and checked
        /// in memory before the first is written. Returns the mod's paths written. Throws
        /// ModException when the mod does not fit.
        /// </summary>
        public static List<string> Apply(ModPackage mod, string gameFolder, GameKind kind)
        {
            if (mod.Game != kind) throw new ModException("This mod is for " + mod.GameName + ", not the game open here.");
            var read = Reader(gameFolder);
            var checks = mod.Check(read);
            var bad = checks.Where(c => c.State == FileState.Different || c.State == FileState.Missing).ToList();
            if (bad.Count > 0)
                throw new ModException("The mod does not fit this game: " + string.Join("; ", bad.Select(b => b.File.Path + (b.State == FileState.Missing ? " is missing" : b.File.Added ? " already exists" : " is not the file the mod was made from"))) + ".");
            var outputs = new List<(ModFile File, byte[] Bytes)>();
            foreach (var c in checks.Where(c => c.State == FileState.Original)) outputs.Add((c.File, mod.BuildOne(c.File, read(c.File.Path))));
            var written = new List<string>();
            foreach (var o in outputs)
            {
                string path = GamePaths.Find(gameFolder, o.File.Path) ?? NewPath(gameFolder, o.File.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // the mark of a file the editor added, so a mod made from this folder carries it too
                if (o.File.Added && !File.Exists(path + SafeFile.OriginalSuffix)) File.WriteAllBytes(path + SafeFile.OriginalSuffix, new byte[0]);
                SafeFile.Write(path, o.Bytes);
                written.Add(o.File.Path);
            }
            return written;
        }

        /// <summary>Where a file the mod adds goes: its folder as the game has it (whatever its case), else as the mod names it.</summary>
        static string NewPath(string gameFolder, string rel)
        {
            int slash = rel.LastIndexOf('/');
            string dir = gameFolder;
            if (slash > 0)
            {
                string cur = gameFolder;
                foreach (var part in rel.Substring(0, slash).Split('/'))
                {
                    string next = Directory.Exists(cur) ? Directory.GetDirectories(cur).FirstOrDefault(d => string.Equals(Path.GetFileName(d), part, StringComparison.OrdinalIgnoreCase)) : null;
                    cur = next ?? Path.Combine(cur, part);
                }
                dir = cur;
            }
            return Path.Combine(dir, rel.Substring(slash + 1));
        }
    }
}
