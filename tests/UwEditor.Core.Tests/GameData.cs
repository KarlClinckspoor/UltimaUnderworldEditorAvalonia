using System;
using System.IO;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The game files: UW_GAMEDATA (a folder holding UW and/or UW2), or else the repository's
    /// git-ignored gamedata folder. Tests needing a game that is not there pass without checking
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
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorFree.sln"))) dir = dir.Parent;
                return dir == null ? null : Path.Combine(dir.FullName, "gamedata");
            }
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
