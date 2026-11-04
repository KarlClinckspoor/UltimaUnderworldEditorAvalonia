using System;
using System.IO;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The game files: UW_GAMEDATA (a folder holding UW and/or UW2), or else the git-ignored
    /// tests/gamedata folder (or gamedata at the top, if only that one is there). Tests needing a game that is not there pass without checking
    /// anything, unless UW_REQUIRE_GAMEDATA=1.
    /// </summary>
    public static class GameData
    {
        public static string Root
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("UW_GAMEDATA");
                if (!string.IsNullOrEmpty(env)) return env;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorAvalonia.sln"))) dir = dir.Parent;
                if (dir == null) return null;
                // tests/gamedata; the repository's own gamedata folder was the place before
                string tests = Path.Combine(dir.FullName, "tests", "gamedata"), old = Path.Combine(dir.FullName, "gamedata");
                return !Directory.Exists(tests) && Directory.Exists(old) ? old : tests;
            }
        }

        /// <summary>
        /// Saved games that had problems in play, kept under saves/uw1 or saves/uw2 of the game
        /// data (one folder each, any name, with LEV.ARK and PLAYER.DAT); read with the game's DATA.
        /// None when there are none.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<string> Saves(string game)
        {
            string root = Root;
            string dir = root == null ? null : Path.Combine(root, "saves", game == "UW2" ? "uw2" : "uw1");
            if (dir == null || !Directory.Exists(dir)) yield break;
            foreach (var d in Directory.GetDirectories(dir))
                if (File.Exists(Path.Combine(d, "LEV.ARK")) && File.Exists(Path.Combine(d, "PLAYER.DAT"))) yield return d;
        }

        /// <summary>The game folder ("UW" or "UW2"), or null when it is missing.</summary>
        public static string Game(string name)
        {
            string root = Root;
            string p = root == null ? null : Path.Combine(root, name);
            bool there = p != null && Directory.Exists(Path.Combine(p, "DATA"));
            if (!there && Environment.GetEnvironmentVariable("UW_REQUIRE_GAMEDATA") == "1")
                Assert.Fail("Game files for " + name + " not found under " + root);
            return there ? p : null;
        }
    }
}
