using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>
    /// Which files a mod may change (docs/mod-format.md, rule 1): the game's executable and the
    /// files under DATA, CRIT, CUTS and SOUND. Paths are upper case and '/'-separated, relative
    /// to the game folder.
    /// </summary>
    public static class GamePaths
    {
        static readonly string[] Folders = { "DATA", "CRIT", "CUTS", "SOUND" };

        public static string ExeName(GameKind game) { return game == GameKind.UW2 ? "UW2.EXE" : "UW.EXE"; }

        /// <summary>A path as a mod stores it (upper case, '/'), or null when it is not a plain relative path.</summary>
        public static string Normalize(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            string p = rel.Replace('\\', '/');
            if (p.StartsWith("/") || p.Contains(':')) return null;
            var parts = p.Split('/');
            foreach (var s in parts)
                if (s.Length == 0 || s == "." || s == ".." || s.Trim() != s || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            return string.Join("/", parts).ToUpperInvariant();
        }

        /// <summary>Whether a mod may change this file of this game.</summary>
        public static bool IsGameFile(string rel, GameKind game)
        {
            string p = Normalize(rel);
            if (p == null) return false;
            if (p == ExeName(game)) return true;
            int slash = p.IndexOf('/');
            if (slash < 0) return false;
            string top = p.Substring(0, slash);
            if (!Folders.Contains(top)) return false;
            // the editor's and the manager's own files beside the game's
            return !IsWorkFile(p);
        }

        /// <summary>Backups, first versions and temporary files the editor and the manager keep beside the game's.</summary>
        public static bool IsWorkFile(string rel)
        {
            string up = rel.ToUpperInvariant();
            return up.EndsWith(".BKP") || up.EndsWith(".ORIG") || up.EndsWith(".TMP") || up.EndsWith(".UWMM-TMP") || up.EndsWith(".UWMOD");
        }

        /// <summary>The game files present in a folder, as a mod names them.</summary>
        public static List<string> List(string folder, GameKind game)
        {
            var list = new List<string>();
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder))
                if (string.Equals(Path.GetFileName(f), ExeName(game), StringComparison.OrdinalIgnoreCase)) list.Add(ExeName(game));
            foreach (var d in Directory.GetDirectories(folder))
            {
                if (!Folders.Contains(Path.GetFileName(d).ToUpperInvariant())) continue;
                foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                {
                    string rel = Normalize(Path.GetRelativePath(folder, f));
                    if (rel != null && IsGameFile(rel, game)) list.Add(rel);
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>The file at a mod's path under root, whatever the case of its parts on disk, or null.</summary>
        public static string Find(string root, string rel)
        {
            string cur = root;
            foreach (var part in rel.Split('/'))
            {
                if (!Directory.Exists(cur)) return null;
                string next = Directory.GetFileSystemEntries(cur).FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
                if (next == null) return null;
                cur = next;
            }
            return File.Exists(cur) ? cur : null;
        }
    }
}
