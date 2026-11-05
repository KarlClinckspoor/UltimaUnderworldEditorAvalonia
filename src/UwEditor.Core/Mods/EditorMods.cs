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
