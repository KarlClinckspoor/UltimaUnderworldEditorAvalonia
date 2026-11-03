using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core
{
    /// <summary>One level the check sends the player to, and the square.</summary>
    public sealed class ExhumedVisit
    {
        public int Level, X, Y;
        /// <summary>When the cheat runs in the script (milliseconds of the game's clock).</summary>
        public int At;
    }

    /// <summary>A check set up: the folders, the script and how to run the port.</summary>
    public sealed class ExhumedPlan
    {
        public GameKind Kind;
        /// <summary>The check's folder: game (a copy holding the edits), home (the port's home, with SAVE1), before (SAVE1 as written).</summary>
        public string Folder, Game, Home, Before, Script, Screenshot;
        public readonly List<ExhumedVisit> Visits = new List<ExhumedVisit>();
        /// <summary>A new character is made (no saved game to start from), else saved game I is loaded.</summary>
        public bool NewCharacter;
        /// <summary>The script ends by saving in slot I, so the game writes every level it visited.</summary>
        public bool SavesAtEnd;
        /// <summary>The player is kept alive: strength 99 and defense 30 at the start, hit points filled before each level (the port has no god mode; its set cheat does this).</summary>
        public bool KeepAlive;
        /// <summary>The hit points set before each level: the maximum with strength 99 (SKILLS.C player_compute: 30 + level × strength / 5, at most 255).</summary>
        public int AliveHp;
        public int ExitAfterMs;
        /// <summary>When the files were ready: a save written after it is the game's.</summary>
        public DateTime ReadyUtc;
        /// <summary>The port's arguments (after the program).</summary>
        public readonly List<string> Arguments = new List<string>();
        /// <summary>Environment variables the port needs (the test cheats).</summary>
        public readonly Dictionary<string, string> Environment = new Dictionary<string, string>();
        public string Note;
    }

    /// <summary>Levels a check cannot send the player to: none of their squares is open, flat, dry and free with room around it.</summary>
    public sealed class NowhereToStandException : InvalidOperationException
    {
        public List<int> Levels { get; private set; }
        public NowhereToStandException(List<int> levels, bool uw2)
            : base(string.Format("{0} {1} no open, flat, dry square with room around it to send the player to (the game kills a player it cannot place). Untick {2}: {3}.",
                uw2 ? (levels.Count == 1 ? "Level slot" : "Level slots") : (levels.Count == 1 ? "Level" : "Levels"),
                levels.Count == 1 ? "has" : "have", levels.Count == 1 ? "it" : "them", string.Join(", ", levels)))
        { Levels = levels; }
    }

    /// <summary>What a check found.</summary>
    public sealed class ExhumedResult
    {
        public int ExitCode;
        public bool TimedOut;
        public string Output = "";
        /// <summary>The levels the player reached (the cheat ran); the ones refused, with the game's reason.</summary>
        public readonly List<int> Reached = new List<int>();
        public readonly List<string> Refused = new List<string>();
        /// <summary>Problems: a crash, a halt, the game's fatal error, a cheat refused, findings in the levels the game saved.</summary>
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool Resaved;
        public bool Passed { get { return Problems.Count == 0; } }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Passed ? "PASSED: nothing went wrong." : "PROBLEMS: " + Problems.Count + ".");
            foreach (var p in Problems) sb.AppendLine("  ✗ " + p);
            foreach (var n in Notes) sb.AppendLine("  · " + n);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Checks edits in Underworld Exhumed, the native port of the original games built from their
    /// decompiled source (github.com/abedegno/underworld-exhumed): the edited game is copied with
    /// a saved game holding the edits, and the port is run hidden from an input script that loads
    /// the save (or makes a new character), sends the player to each level to check with the
    /// port's test cheat `goto` (so the game's own code loads each one), and, in UW1, saves in slot
    /// I so that the game writes every level it visited. The port's output is read for crashes,
    /// halts and the game's fatal errors, and the levels the game saved are checked again here.
    /// The port's switches and cheats are from its docs/BUILDING.md (Input scripts, Cheats for tests).
    /// </summary>
    public static class ExhumedCheck
    {
        public const string Marker = ".uwedit-exhumed";
        /// <summary>Time for a level change (the cheat waits for the 3D view itself; this is the spacing).</summary>
        public const int VisitMs = 5000;

        /// <summary>The port's program for the game: uw1port or uw2port (with .exe on Windows).</summary>
        public static string ProgramName(GameKind kind) { return (kind == GameKind.UW2 ? "uw2port" : "uw1port") + (OperatingSystem.IsWindows() ? ".exe" : ""); }

        /// <summary>Where the port may be: next to the editor, in the user's Downloads or a build of the repository.</summary>
        public static string FindPort(GameKind kind, IEnumerable<string> extraFolders = null)
        {
            string name = ProgramName(kind), game = kind == GameKind.UW2 ? "uw2" : "uw1";
            var dirs = new List<string>();
            if (extraFolders != null) dirs.AddRange(extraFolders.Where(d => !string.IsNullOrEmpty(d)));
            dirs.Add(AppContext.BaseDirectory);
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, "underworld-exhumed", game, "build", "port"));
                dirs.Add(Path.Combine(home, "Downloads"));
            }
            foreach (var d in dirs)
            {
                try
                {
                    string p = Path.Combine(d, name);
                    if (File.Exists(p)) return p;
                    if (Directory.Exists(d))
                        foreach (var sub in Directory.GetDirectories(d).Where(x => Path.GetFileName(x).IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0).Take(20))
                        {
                            p = Path.Combine(sub, name);
                            if (File.Exists(p)) return p;
                        }
                }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>
        /// A square to send the player to: open and flat, its eight neighbours open at the same
        /// height, not water or lava, and with nothing on it; the start square on the start level
        /// when it is one. Null when the level has none.
        /// </summary>
        public static (int X, int Y)? GotoSquare(GameSession s, LevelModel m, TerrainTable terrain = null)
        {
            terrain = terrain ?? TryTerrain(s);
            Func<int, int, bool> good = (x, y) =>
            {
                if (x < 1 || y < 1 || x > 62 || y > 62) return false;
                var t = m.Tiles[x, y];
                if (t.Type != TileType.Open || t.FirstObject != 0) return false;
                if (terrain != null && t.FloorIndex >= 0 && t.FloorIndex < m.FloorMap.Length)
                {
                    int word = terrain.Get(terrain.EntryOf(m.FloorMap[t.FloorIndex], true));
                    bool wet = s.IsUw2 ? ((word >> 6) & 3) == 1 || ((word >> 6) & 3) == 2 : (word & 0x30) != 0;
                    if (wet) return false;
                }
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = m.Tiles[x + dx, y + dy];
                        if (n.Type != TileType.Open || n.Height != t.Height) return false;
                    }
                return true;
            };
            var start = s.Start;
            if (start != null && start.Level == m.Level && good((int)start.X, (int)start.Y)) return ((int)start.X, (int)start.Y);
            // nearest the middle first: away from the map's edge, likely in the level's heart
            var all = new List<(int X, int Y)>();
            for (int y = 2; y < 62; y++) for (int x = 2; x < 62; x++) if (good(x, y)) all.Add((x, y));
            if (all.Count == 0) return null;
            return all.OrderBy(t => Math.Abs(t.X - 32) + Math.Abs(t.Y - 32)).First();
        }

        static TerrainTable TryTerrain(GameSession s)
        {
            try { return TerrainTable.Load(s); } catch (Exception) { return null; }
        }

        /// <summary>The levels among these with no square to send the player to (all of them, not just the first).</summary>
        public static List<int> LevelsWithNowhereToStand(GameSession s, LevelEdits edits, IEnumerable<int> levels)
        {
            var terrain = TryTerrain(s);
            var list = new List<int>();
            foreach (int level in levels.Distinct())
            {
                var m = edits != null ? edits.Model(level) : s.LoadLevel(level);
                if (m == null || !m.Present || GotoSquare(s, m, terrain) == null) list.Add(level);
            }
            return list;
        }

        /// <summary>The player's experience level in the save a check starts from (1 for a new character).</summary>
        static int PlayerLevelOf(GameSession s, byte[] playerDat)
        {
            try
            {
                string from = SaveSource(s);
                byte[] file = playerDat ?? (from == null ? null : File.ReadAllBytes(GameProfile.Find(from, "PLAYER.DAT")));
                if (file == null) return 1;
                int l = PlayerDat.Parse(file, s.Kind).Level;
                return l < 1 || l > 16 ? 1 : l;
            }
            catch (Exception) { return 1; }
        }

        /// <summary>The levels changed since they were opened (the default to check).</summary>
        public static List<int> EditedLevels(LevelEdits edits)
        {
            return edits == null ? new List<int>() : edits.DirtyLevels.ToList();
        }

        /// <summary>
        /// Sets a check up in folder (made afresh; only a folder made here is ever cleared): the
        /// copied game with the edits, the port's home with saved game I holding them, the script.
        /// </summary>
        public static ExhumedPlan Prepare(GameSession s, LevelEdits edits, byte[] playerDat, IList<int> levels, string folder, bool saveAtEnd = true, bool keepAlive = true)
        {
            if (s == null) throw new InvalidOperationException("Open a game first.");
            levels = (levels ?? new List<int>()).Distinct().ToList();
            if (levels.Count == 0) throw new InvalidOperationException("Choose at least one level to check.");
            var nowhere = LevelsWithNowhereToStand(s, edits, levels);
            if (nowhere.Count > 0) throw new NowhereToStandException(nowhere, s.IsUw2);
            string game = Path.GetFullPath(s.GameFolder);
            folder = Path.GetFullPath(folder);
            if (folder.TrimEnd(Path.DirectorySeparatorChar).Equals(game.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || game.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The check runs in a copy, never in the game folder itself.");
            if (Directory.Exists(folder))
            {
                if (!File.Exists(Path.Combine(folder, Marker)) && Directory.EnumerateFileSystemEntries(folder).Any())
                    throw new InvalidOperationException(folder + " exists and was not made by a check: choose another folder.");
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, Marker), "A check of " + game + " in Underworld Exhumed; the editor clears it each time.");

            var plan = new ExhumedPlan { Kind = s.Kind, Folder = folder, Game = Path.Combine(folder, "game"), Home = Path.Combine(folder, "home"), Before = Path.Combine(folder, "before"), Script = Path.Combine(folder, "script.txt"), Screenshot = Path.Combine(folder, "last-screen.png") };
            Directory.CreateDirectory(plan.Game);
            PlayTest.CopyGame(game, plan.Game, folder);
            // the copy keeps no saved games of its own: the port reads saves from its home
            foreach (var d in Directory.GetDirectories(plan.Game).Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^SAVE\d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                Directory.Delete(d, true);

            string dataName = Path.GetFileName(s.DataFolder.TrimEnd(Path.DirectorySeparatorChar));
            string levName = Path.GetFileName(s.LevArkPath);
            byte[] lev = edits != null ? edits.ToBytes() : s.LevArk.ToBytes();
            Directory.CreateDirectory(plan.Home);

            // the saved game to start from: the open one, or with DATA open the game's first, carrying DATA's levels as edited
            if (!s.IsSave) File.WriteAllBytes(Path.Combine(plan.Game, dataName, levName), lev);
            if (SaveSource(s) != null)
            {
                string slot = Path.Combine(plan.Home, "SAVE1");
                WriteSave(s, edits, playerDat, slot, null, lev);
                CopyFlat(slot, plan.Before);
            }
            else if (s.IsUw2) throw new InvalidOperationException("UW2 is checked from a saved game: save a game once (any slot), or open a SAVEn folder.");
            else plan.NewCharacter = true;

            // the script: start, then a cheat a level, then (UW1) a save
            var sc = new StringBuilder();
            sc.AppendLine("# Made by the Underworld Editor: check " + (s.IsSave ? s.SaveFolder : game) + " in Underworld Exhumed.");
            int t;
            if (plan.NewCharacter)
            {
                // as the port's own setup sessions make one (uw1/tests/replay/setup/headless): every default, the name "avatar"
                sc.AppendLine("9000 key esc\n12000 key esc\n15000 key enter        # Create Character");
                for (int at = 18000; at <= 32000; at += 2000) sc.AppendLine(at + " key enter");
                int nameAt = 34000;
                foreach (char c in "avatar") { sc.AppendLine(nameAt + " key " + c); nameAt += 200; }
                sc.AppendLine("36000 key enter\n38000 key enter        # Yes");
                t = 44000;
            }
            else
            {
                plan.Arguments.Add("--enhance"); plan.Arguments.Add("skip-intro");
                sc.AppendLine("3000 key enter         # Journey Onward\n4500 key enter         # saved game I");
                t = 12000;
            }
            var terrain = TryTerrain(s);
            plan.KeepAlive = keepAlive;
            if (keepAlive)
            {
                // no god mode in the port: its set cheat (port/sys/cheats.c c_set) raises the maxima as a level gained does, then the hit points are filled before each level
                plan.AliveHp = Math.Min(255, 30 + PlayerLevelOf(s, plan.NewCharacter ? null : playerDat) * 99 / 5);
                sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat set str 99        # keep the player alive\n{1} cheat set defense 30", t, t + 200));
                t += 500;
            }
            foreach (int level in levels)
            {
                var m = edits != null ? edits.Model(level) : s.LoadLevel(level);
                var sq = GotoSquare(s, m, terrain).Value;
                if (keepAlive) { sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat set hp {1}", t, plan.AliveHp)); t += 300; }
                plan.Visits.Add(new ExhumedVisit { Level = level, X = sq.X, Y = sq.Y, At = t });
                sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat goto {1} {2} {3}", t, level, sq.X, sq.Y));
                t += VisitMs;
            }
            plan.SavesAtEnd = saveAtEnd && !s.IsUw2;
            if (plan.SavesAtEnd)
            {
                // UW1: Ctrl+S, slot I, Enter keeps the description (GAMEWRAP.C SaveGame); a new character's slot has none, so one is typed
                sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} down ctrl\n{1} key s\n{2} up ctrl\n{3} key enter        # slot I", t, t + 100, t + 300, t + 2000));
                int at = t + 3500;
                if (plan.NewCharacter) foreach (char c in "check") { sc.AppendLine(at + " key " + c); at += 200; }
                sc.AppendLine((at + 500) + " key enter        # the description");
                t = at + 500 + VisitMs;
            }
            plan.ExitAfterMs = t + 3000;
            File.WriteAllText(plan.Script, sc.ToString());

            plan.Arguments.InsertRange(0, new[] { "--data", plan.Game, "--home", plan.Home, "--hidden", "--no-audio", "--no-recording", "--exit-on-halt",
                "--input-script", plan.Script, "--screenshot-after", (plan.ExitAfterMs - 1000).ToString(CultureInfo.InvariantCulture), "--screenshot", plan.Screenshot,
                "--exit-after", plan.ExitAfterMs.ToString(CultureInfo.InvariantCulture) });
            plan.Environment["EXHUME_TEST_CHEATS"] = "1";
            plan.ReadyUtc = DateTime.UtcNow;
            plan.Note = string.Format("{0}{1}, then {2}{3}.", plan.NewCharacter ? "A new character" : "Saved game I", keepAlive ? " kept alive (strength 99, defense 30, " + plan.AliveHp + " hit points before each level)" : "", string.Join(", ", plan.Visits.Select(v => (s.IsUw2 ? "slot " : "level ") + v.Level + " (" + v.X + ", " + v.Y + ")")), plan.SavesAtEnd ? ", then a save in slot I" : "");
            return plan;
        }

        /// <summary>The saved game an export starts from: the open one, or with DATA open the game's first saved game (null when there is none).</summary>
        public static string SaveSource(GameSession s)
        {
            if (s.IsSave) return s.SaveFolder;
            return Enumerable.Range(1, 4).Select(i => Path.Combine(s.GameFolder, "SAVE" + i)).FirstOrDefault(d => GameProfile.Find(d, "PLAYER.DAT") != null);
        }

        /// <summary>Underworld Exhumed's home folder, where it keeps its saved games: %APPDATA%\uw1port on Windows, ~/.uw1port elsewhere (uw2port for UW2).</summary>
        public static string PortHome(GameKind kind)
        {
            string name = kind == GameKind.UW2 ? "uw2port" : "uw1port";
            if (OperatingSystem.IsWindows()) return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), name);
            return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "." + name);
        }

        /// <summary>
        /// Writes the edits as a saved game in folder (a SAVEn folder of the original game or of
        /// the port's home): the starting save's files with LEV.ARK as edited, the player as edited
        /// when a save is open, and the description given (when the save has one). The folder must
        /// be new, empty or a saved game already.
        /// </summary>
        public static string WriteSave(GameSession s, LevelEdits edits, byte[] playerDat, string folder, string description, byte[] lev = null)
        {
            string from = SaveSource(s);
            if (from == null) throw new InvalidOperationException("A saved game holds the player as well as the levels, and this game has none to start from: save a game once (any slot) in the game, or open a SAVEn folder.");
            folder = Path.GetFullPath(folder);
            if (Path.GetFullPath(from).TrimEnd(Path.DirectorySeparatorChar).Equals(folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That is the saved game open now: Save writes to it; export to another folder.");
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && GameProfile.Find(folder, "PLAYER.DAT") == null)
                throw new InvalidOperationException(folder + " holds files and is not a saved game: choose an empty folder or a SAVEn folder.");
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder)) File.Delete(f);
            CopyFlat(from, folder);
            lev = lev ?? (edits != null ? edits.ToBytes() : s.LevArk.ToBytes());
            File.WriteAllBytes(Path.Combine(folder, Path.GetFileName(s.LevArkPath)), lev);
            if (playerDat != null && s.IsSave) File.WriteAllBytes(GameProfile.Find(folder, "PLAYER.DAT") ?? Path.Combine(folder, "PLAYER.DAT"), playerDat);
            string desc = GameProfile.Find(folder, "DESC");
            if (desc != null && !string.IsNullOrEmpty(description))
                File.WriteAllBytes(desc, Encoding.Latin1.GetBytes(description.Length > 0x1E ? description.Substring(0, 0x1E) : description));
            return folder;
        }

        static void CopyFlat(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string up = f.ToUpperInvariant();
                if (up.EndsWith(".BKP") || up.EndsWith(".ORIG") || up.EndsWith(".TMP")) continue;
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
        }

        /// <summary>Exit codes the port and the game give (main.c: 1 a port error, 3 a halt; ERROR.C: −24 the game's fatal error, −1 a start-up error).</summary>
        public static string DescribeExit(int code)
        {
            switch (code)
            {
                case 0: return null;
                case 1: return "the port stopped with an error (exit 1)";
                case 3: return "the port halted where the game did something it cannot carry out (exit 3)";
                case -24: case 232: return "the game stopped with its own fatal error, \"Underworld can no longer run\" (exit −24)";
                case -1: case 255: return "the game stopped while starting (exit −1)";
                case -1073741819: return "the port crashed: an access violation (0xC0000005)";
                case -1073741571: return "the port crashed: a stack overflow (0xC00000FD)";
                case 134: return "the port aborted (SIGABRT)";
                case 136: return "the port crashed: an arithmetic fault (SIGFPE)";
                case 139: return "the port crashed: a bad memory access (SIGSEGV)";
                case 138: return "the port crashed: a bus error (SIGBUS)";
                default: return "the port ended with exit code " + code;
            }
        }

        /// <summary>Reads what the port printed and what it left: the cheats that ran, errors, and the levels the game saved.</summary>
        public static ExhumedResult Analyze(GameSession s, ExhumedPlan plan, int exitCode, string output, bool timedOut)
        {
            var r = new ExhumedResult { ExitCode = exitCode, TimedOut = timedOut, Output = output ?? "" };
            var lines = r.Output.Replace("\r", "").Split('\n');
            string why = timedOut ? "the port did not finish in time (it hung, or waits at a prompt)" : DescribeExit(exitCode);
            if (why != null) r.Problems.Add(why);
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                var m = System.Text.RegularExpressions.Regex.Match(line, @"cheat: goto (\d+) (\d+) (\d+)(: (.*))? \(not as DOS\)");
                if (m.Success)
                {
                    int lv = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (m.Groups[4].Success) { r.Refused.Add("level " + lv + ": " + m.Groups[5].Value); r.Problems.Add("going to " + (s.IsUw2 ? "slot " : "level ") + lv + " (" + m.Groups[2].Value + ", " + m.Groups[3].Value + ") was refused: " + m.Groups[5].Value); }
                    else r.Reached.Add(lv);
                    continue;
                }
                var sm = System.Text.RegularExpressions.Regex.Match(line, @"cheat: (set \S+ \S+): (.*) \(not as DOS\)");
                if (sm.Success) { r.Notes.Add("Keeping the player alive: \"" + sm.Groups[1].Value + "\" was refused: " + sm.Groups[2].Value + "."); continue; }
                var nm = System.Text.RegularExpressions.Regex.Match(line, @"far null pointer read at (\S+)");
                if (nm.Success)
                {
                    string after = r.Reached.Count > 0 ? " after going to " + (s.IsUw2 ? "slot " : "level ") + r.Reached[r.Reached.Count - 1] : " before the first level";
                    r.Notes.Add("The game read through a null pointer at " + nm.Groups[1].Value + after + ". The port reads what DOS would have, so the game goes on; it can mean an object link that points nowhere, or a path the game itself gets wrong (a death, say).");
                    continue;
                }
                if (line.Contains("host fault")) r.Problems.Add("the port crashed (" + line + ")");
                else if (line.Contains("the game stops here")) r.Problems.Add("the port halted: " + line);
                else if (line.IndexOf("can no longer run", StringComparison.OrdinalIgnoreCase) >= 0) r.Problems.Add("the game's fatal error: " + line);
                else if (line.IndexOf("cheat:", StringComparison.Ordinal) >= 0 && line.IndexOf("refused", StringComparison.OrdinalIgnoreCase) >= 0) r.Problems.Add("the test cheats were refused: " + line);
            }
            foreach (var v in plan.Visits)
                if (!r.Reached.Contains(v.Level) && !r.Refused.Any(x => x.StartsWith("level " + v.Level + ":", StringComparison.Ordinal)))
                    r.Problems.Add((s.IsUw2 ? "Slot " : "Level ") + v.Level + " was never reached (the run ended or stalled before its cheat ran)");
            if (r.Reached.Count > 0) r.Notes.Add("The game loaded " + (s.IsUw2 ? "slot" : "level") + (r.Reached.Count == 1 ? " " : "s ") + string.Join(", ", r.Reached) + ".");

            // the levels the game wrote back: checked as the editor checks its own
            string saved = Path.Combine(plan.Home, "SAVE1", Path.GetFileName(s.LevArkPath));
            string before = Path.Combine(plan.Before, Path.GetFileName(s.LevArkPath));
            if (plan.SavesAtEnd && File.Exists(saved))
            {
                bool same = File.Exists(before) && File.ReadAllBytes(saved).AsSpan().SequenceEqual(File.ReadAllBytes(before));
                bool written = File.GetLastWriteTimeUtc(saved) > plan.ReadyUtc;
                if (plan.NewCharacter || !same || written)
                {
                    r.Resaved = true;
                    try
                    {
                        var ark = Archive.Open(File.ReadAllBytes(saved), s.Kind);
                        foreach (int lv in r.Reached.Distinct())
                        {
                            int block = s.Profile.LevelBlock(lv);
                            if (!ark.Has(block)) { r.Problems.Add("The game's save has no level " + lv + "."); continue; }
                            var data = ark.Read(block);
                            bool ok;
                            string check = new LevelBlock(data, s.Kind).Check(out ok);
                            if (!ok) r.Problems.Add("Level " + lv + " as the game saved it fails the object-list check: " + check);
                            var found = LevelFixes.Find(s, lv, data).Where(f => f.Severity == FixSeverity.ListError).ToList();
                            foreach (var f in found.Take(5)) r.Problems.Add("Level " + lv + " as the game saved it: " + f.Text);
                            if (found.Count > 5) r.Problems.Add("Level " + lv + ": " + (found.Count - 5) + " more list errors.");
                        }
                        r.Notes.Add("The game saved slot I; the levels it wrote were checked again here.");
                    }
                    catch (Exception ex) { r.Problems.Add("The game's save could not be read: " + ex.Message); }
                }
                else r.Problems.Add("The game did not save in slot I at the end (the save's levels are unchanged).");
            }
            else if (plan.SavesAtEnd) r.Problems.Add("The game wrote no saved game in slot I at the end.");
            if (File.Exists(plan.Screenshot)) r.Notes.Add("The last screen: " + plan.Screenshot);
            return r;
        }
    }
}
