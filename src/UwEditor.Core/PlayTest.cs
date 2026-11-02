using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core
{
    /// <summary>What a play test was set up as: the copy, the program to run, and how to get to the square.</summary>
    public sealed class PlayTestPlan
    {
        /// <summary>The copy of the game the test runs in (the game folder itself is never touched).</summary>
        public string Folder;
        /// <summary>The executable's file name in the copy (UW.EXE or UW2.EXE).</summary>
        public string Exe;
        /// <summary>The saved game holding the player on the square (SAVE1…), or null when a new game starts there.</summary>
        public string Slot;
        public int Level, X, Y;
        /// <summary>What to do in the game, in words.</summary>
        public string Note;
    }

    /// <summary>
    /// Sets up a play test: a copy of the game folder with the unsaved levels in it and the player
    /// put on a square. A new game starts on level 1's square by patching the copy's executable
    /// (as Move the new-game start does); anywhere else needs a saved game, whose PLAYER.DAT gets the
    /// position (an open save's own, or with DATA open the first save of the folder, given DATA's
    /// levels as edited). The copy is made afresh each time in its own folder, marked so that only a
    /// folder made here is ever cleared.
    /// </summary>
    public static class PlayTest
    {
        public const string Marker = ".uwedit-playtest";

        public static PlayTestPlan Prepare(GameSession s, LevelEdits edits, byte[] playerDat, int level, int x, int y, string folder)
        {
            if (s == null) throw new InvalidOperationException("Open a game first.");
            var lm = edits != null ? edits.Model(level) : s.LoadLevel(level);
            if (x < 1 || y < 1 || x > 62 || y > 62 || lm.Tiles[x, y].IsSolid) throw new InvalidOperationException(string.Format("({0}, {1}) is not a square to stand on.", x, y));
            string game = Path.GetFullPath(s.GameFolder);
            folder = Path.GetFullPath(folder);
            if (folder.TrimEnd(Path.DirectorySeparatorChar).Equals(game.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The play test runs in a copy, never in the game folder itself.");
            if (Directory.Exists(folder))
            {
                if (!File.Exists(Path.Combine(folder, Marker)) && Directory.EnumerateFileSystemEntries(folder).Any())
                    throw new InvalidOperationException(folder + " exists and was not made by a play test: choose another folder.");
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, Marker), "A copy of " + game + " for a play test; the editor clears it each time.");
            CopyGame(game, folder, folder);

            string dataName = Path.GetFileName(s.DataFolder.TrimEnd(Path.DirectorySeparatorChar));
            var plan = new PlayTestPlan { Folder = folder, Level = level, X = x, Y = y };
            string exe = s.ExePath ?? StartPosition.ExePath(s.DataFolder, s.Kind);
            plan.Exe = exe != null ? Path.GetFileName(exe) : (s.IsUw2 ? "UW2.EXE" : "UW.EXE");
            byte[] levels = edits != null ? edits.ToBytes() : s.LevArk.ToBytes();

            string slot = s.IsSave ? Path.GetFileName(s.SaveFolder.TrimEnd(Path.DirectorySeparatorChar)) : null;
            if (slot == null && level == 1 && exe != null)
            {
                // a new game: the edited levels in DATA, the start square patched into the copy's executable
                File.WriteAllBytes(Path.Combine(folder, dataName, Path.GetFileName(s.LevArkPath)), levels);
                string copyExe = Path.Combine(folder, Path.GetFileName(exe));
                var bytes = File.ReadAllBytes(copyExe);
                if (!StartPosition.Patch(bytes, s.Kind, x, y)) throw new InvalidOperationException("The start call was not found in " + plan.Exe + " (another version?).");
                File.WriteAllBytes(copyExe, bytes);
                plan.Note = string.Format("Start a new game: you begin on ({0}, {1}) of level 1.", x, y);
                return plan;
            }
            if (slot == null)
            {
                // somewhere else from DATA: the first saved game carries the player into DATA's levels as edited
                slot = Enumerable.Range(1, 4).Select(i => "SAVE" + i).FirstOrDefault(n => GameProfile.Find(Path.Combine(game, n), "PLAYER.DAT") != null);
                if (slot == null) throw new InvalidOperationException("Only a new game starts without a saved game, and it starts on level 1. Save a game once (any slot), or test from level 1.");
                File.WriteAllBytes(Path.Combine(folder, dataName, Path.GetFileName(s.LevArkPath)), levels);
                playerDat = null;   // that save's own player
            }
            string slotDir = Path.Combine(folder, slot);
            if (s.IsSave && !Directory.Exists(slotDir))
            {
                // a saved game kept outside the game folder, or under another name: it plays as slot 1 of the copy
                slot = "SAVE1";
                slotDir = Path.Combine(folder, slot);
                if (Directory.Exists(slotDir)) Directory.Delete(slotDir, true);
                Directory.CreateDirectory(slotDir);
                foreach (var f in Directory.GetFiles(s.SaveFolder)) File.Copy(f, Path.Combine(slotDir, Path.GetFileName(f)), true);
            }
            File.WriteAllBytes(Path.Combine(slotDir, Path.GetFileName(s.LevArkPath)), levels);
            string playerPath = GameProfile.Find(slotDir, "PLAYER.DAT") ?? Path.Combine(slotDir, "PLAYER.DAT");
            var pd = PlayerDat.Parse(playerDat ?? File.ReadAllBytes(playerPath), s.Kind);
            var t = lm.Tiles[x, y];
            bool slope = t.Type >= TileType.SlopeUpN;
            pd.SetPosition(level, x, y, t.Height, slope, pd.Heading);
            File.WriteAllBytes(playerPath, pd.ToBytes());
            plan.Slot = slot;
            plan.Note = string.Format("Restore saved game {0} ({1}): you stand on ({2}, {3}) of level {4}.", slot.Substring(4), slot, x, y, level);
            return plan;
        }

        /// <summary>The game folder copied file by file, leaving out the editor's own backups, checkpoints and copies.</summary>
        public static void CopyGame(string from, string to, string playFolder)
        {
            foreach (var d in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(d);
                if (Path.GetFullPath(d).Equals(playFolder, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("Editor ", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(d, Marker)) || File.Exists(Path.Combine(d, ExhumedCheck.Marker))) continue;
                Directory.CreateDirectory(Path.Combine(to, name));
                CopyGame(d, Path.Combine(to, name), playFolder);
            }
            foreach (var f in Directory.GetFiles(from))
            {
                string up = f.ToUpperInvariant();
                if (up.EndsWith(".BKP") || up.EndsWith(".ORIG") || up.EndsWith(".TMP") || up.EndsWith(".UWMOD")) continue;
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
        }

        /// <summary>DOSBox's arguments: mount the copy as C:, go there and run the game.</summary>
        public static List<string> DosBoxArguments(PlayTestPlan plan, string extra)
        {
            var a = new List<string>();
            if (!string.IsNullOrWhiteSpace(extra)) a.AddRange(SplitArgs(extra));
            a.Add("-c"); a.Add("mount c \"" + plan.Folder + "\"");
            a.Add("-c"); a.Add("c:");
            a.Add("-c"); a.Add(Path.GetFileNameWithoutExtension(plan.Exe));
            return a;
        }

        /// <summary>Splits a command line into arguments, double quotes grouping.</summary>
        public static IEnumerable<string> SplitArgs(string s)
        {
            var cur = new System.Text.StringBuilder();
            bool q = false;
            foreach (char c in s)
            {
                if (c == '"') { q = !q; continue; }
                if (char.IsWhiteSpace(c) && !q) { if (cur.Length > 0) { yield return cur.ToString(); cur.Clear(); } continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) yield return cur.ToString();
        }

        /// <summary>Where DOSBox may be: next to the game (GOG's DOSBOX folder), or installed.</summary>
        public static string FindDosBox(string gameFolder)
        {
            var c = new List<string>();
            if (gameFolder != null)
            {
                foreach (var dir in new[] { gameFolder, Path.GetDirectoryName(gameFolder) ?? gameFolder })
                {
                    c.Add(Path.Combine(dir, "DOSBOX", "DOSBox.exe"));
                    c.Add(Path.Combine(dir, "DOSBox", "DOSBox.exe"));
                    c.Add(Path.Combine(dir, "dosbox", "dosbox.exe"));
                }
            }
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                if (!string.IsNullOrEmpty(pf))
                {
                    c.Add(Path.Combine(pf, "DOSBox-0.74-3", "DOSBox.exe"));
                    c.Add(Path.Combine(pf, "DOSBox-0.74", "DOSBox.exe"));
                    c.Add(Path.Combine(pf, "DOSBox-X", "dosbox-x.exe"));
                    c.Add(Path.Combine(pf, "DOSBox Staging", "dosbox.exe"));
                }
            c.Add("/usr/bin/dosbox"); c.Add("/usr/local/bin/dosbox"); c.Add("/usr/bin/dosbox-x"); c.Add("/opt/homebrew/bin/dosbox");
            c.Add("/Applications/DOSBox.app/Contents/MacOS/DOSBox"); c.Add("/Applications/dosbox-staging.app/Contents/MacOS/dosbox");
            return c.FirstOrDefault(File.Exists);
        }
    }
}
