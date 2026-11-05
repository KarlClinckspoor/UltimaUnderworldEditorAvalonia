using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>What an edited game folder changed against its originals, and what keeps it from being a mod.</summary>
    public sealed class FolderChanges
    {
        public GameKind Game;
        /// <summary>Changed files: the mod's path, the original's bytes (null for a file added), the edited bytes.</summary>
        public readonly List<(string Path, byte[] Original, byte[] Edited)> Changed = new List<(string, byte[], byte[])>();
        /// <summary>Why no mod can be made (files removed, or added where the game would not read them). Empty when one can.</summary>
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>Makes a mod from an edited game folder (docs/mod-format.md).</summary>
    public static class ModMaker
    {
        /// <summary>
        /// Compares an edited folder with a folder of the original files. A file missing from
        /// the edited game is a problem (a mod never removes one), and so is a new file, unless
        /// it is one the game opens by number (GamePaths.CanAdd).
        /// </summary>
        public static FolderChanges Compare(string originalFolder, string editedFolder, GameKind game)
        {
            var r = new FolderChanges { Game = game };
            var a = GamePaths.List(originalFolder, game);
            var b = GamePaths.List(editedFolder, game);
            foreach (var p in a.Except(b)) r.Problems.Add(p + " is missing from the edited game: a mod cannot remove files.");
            foreach (var p in b)
            {
                byte[] e = File.ReadAllBytes(GamePaths.Find(editedFolder, p));
                if (!a.Contains(p))
                {
                    if (GamePaths.CanAdd(p, game)) r.Changed.Add((p, null, e));
                    else r.Problems.Add(NotReadable(p));
                    continue;
                }
                byte[] o = File.ReadAllBytes(GamePaths.Find(originalFolder, p));
                if (!o.AsSpan().SequenceEqual(e)) r.Changed.Add((p, o, e));
            }
            return r;
        }

        static string NotReadable(string p) { return p + " is not one of the game's files, and the game would never open it: a mod cannot add it."; }

        /// <summary>
        /// Compares a game folder the editor wrote with the first versions it kept
        /// (NAME.EXT.ORIG beside each file it wrote). An empty .ORIG marks a file the editor
        /// added (carried when the game opens it by number), and a .ORIG without its file one
        /// it removed (a problem).
        /// </summary>
        public static FolderChanges FromOriginals(string folder, GameKind game)
        {
            var r = new FolderChanges { Game = game };
            var present = new HashSet<string>(GamePaths.List(folder, game), StringComparer.Ordinal);
            foreach (var orig in Directory.GetFiles(folder, "*.ORIG", SearchOption.AllDirectories))
            {
                string rel = GamePaths.Normalize(Path.GetRelativePath(folder, orig.Substring(0, orig.Length - ".ORIG".Length)));
                if (rel == null || !GamePaths.IsGameFile(rel, game)) continue;
                byte[] o = File.ReadAllBytes(orig);
                if (!present.Contains(rel))
                {
                    if (o.Length > 0) r.Problems.Add(rel + " was removed: a mod cannot remove files.");
                    continue;
                }
                byte[] e = File.ReadAllBytes(GamePaths.Find(folder, rel));
                if (o.Length == 0)
                {
                    if (GamePaths.CanAdd(rel, game)) r.Changed.Add((rel, null, e));
                    else r.Problems.Add(NotReadable(rel));
                    continue;
                }
                if (!o.AsSpan().SequenceEqual(e)) r.Changed.Add((rel, o, e));
            }
            r.Changed.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
            return r;
        }

        /// <summary>The mod for these changes, with deltas made and checked. Throws ModException when there are problems or no changes.</summary>
        public static ModPackage Make(FolderChanges changes, string id, string name, string version, string description, IEnumerable<string> authors, string madeWith)
        {
            if (changes.Problems.Count > 0) throw new ModException("No mod can be made:\n" + string.Join("\n", changes.Problems));
            if (changes.Changed.Count == 0) throw new ModException("Nothing has changed: there is nothing to put in a mod.");
            var m = new ModPackage { Game = changes.Game, Id = id, Name = name, Version = version, Description = description ?? "", MadeWith = madeWith ?? "" };
            if (authors != null) m.Authors.AddRange(authors.Where(a => !string.IsNullOrWhiteSpace(a)));
            foreach (var c in changes.Changed) m.Add(c.Path, c.Original, c.Edited);
            return m;
        }
    }
}
