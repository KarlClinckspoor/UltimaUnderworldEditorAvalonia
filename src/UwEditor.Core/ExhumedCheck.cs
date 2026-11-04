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
        /// <summary>Why the player goes there: "level" (the level's own square), "edit" (beside an edit), "sweep" (one square of an area of the level).</summary>
        public string Why = "level";
        public string Key { get { return Level + " " + X + " " + Y; } }
    }

    /// <summary>How thorough a check is: where on each level the player goes, and what he does there.</summary>
    public sealed class ExhumedOptions
    {
        /// <summary>Also go beside every edited spot (tiles and objects changed), so the creatures and objects near the edits are drawn and run.</summary>
        public bool VisitEdits;
        /// <summary>Also go to one square in each 16 by 16 area of the level that has room for the player.</summary>
        public bool Sweep;
        /// <summary>Turn round once at each stop (the turn-left key held), so everything in range is drawn.</summary>
        public bool LookAround;
        /// <summary>At most this many stops beside edits on one level.</summary>
        public int MaxEditStops = 12;
        /// <summary>Stops closer than this many squares to another stop are left out.</summary>
        public int Spacing = 5;
        public ExhumedOptions Clone() { return (ExhumedOptions)MemberwiseClone(); }
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
        /// <summary>Where the player went on each level and what he did there.</summary>
        public ExhumedOptions Options = new ExhumedOptions();
        /// <summary>The player is kept alive: experience level 16 in the saved game, then strength (AliveStrength) and defense 30 at the start, hit points filled before each level (the port has no god mode; its set cheat does this).</summary>
        public bool KeepAlive;
        /// <summary>The player's experience level in the check: 16 from a saved game, 1 for a new character (the port has no cheat for the level).</summary>
        public int AliveLevel;
        /// <summary>The strength set at the start (AliveStrengthAt of AliveLevel).</summary>
        public int AliveStrength;
        /// <summary>The hit points set before each level: the maximum at AliveLevel and AliveStrength (SKILLS.C player_compute: 30 + level × strength / 5).</summary>
        public int AliveHp;
        /// <summary>The port runs with its window shown (no --hidden), to watch what happens.</summary>
        public bool Shown;
        /// <summary>The level the port crashes on (ExhumedCheck.PortCrashLevel) was chosen: it comes last, after the save.</summary>
        public bool CrashLevelLast;
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
        /// <summary>The stops the player reached, as "level x y".</summary>
        public readonly HashSet<string> StopsReached = new HashSet<string>();
        public readonly List<string> Refused = new List<string>();
        /// <summary>Problems: a crash, a halt, the game's fatal error, a cheat refused, findings in the levels the game saved.</summary>
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool Resaved;
        /// <summary>The level the port crashed on arriving at, the one it always crashes on (ExhumedCheck.PortCrashLevel): said in the notes, not a problem; 0 when it did not.</summary>
        public int PortCrashedOn;
        public bool Passed { get { return Problems.Count == 0; } }

        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine((Passed ? (PortCrashedOn > 0 ? "PASSED for the rest; level " + PortCrashedOn + " could not be checked (Underworld Exhumed crashes there, see below)." : "PASSED: nothing went wrong.") : "PROBLEMS: " + Problems.Count + "."));
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
        /// <summary>How long the turn-left key is held to look round once (a turn takes about 3.1 s; a little more for the start of the turn).</summary>
        public const int LookMs = 3600;
        /// <summary>The experience level a kept-alive player is given in the check's saved game: the games' highest (SKILLCHK.C: level 16 at 96000 experience).</summary>
        public const int TopLevel = 16;

        /// <summary>
        /// The level Underworld Exhumed crashes on arriving at, whatever the levels hold: UW1's level 9
        /// (the void). Checked with UW1 v1.0.2 and a build of 2026-11-04: a host fault once the level is
        /// loaded, with a new character on the shipped game, after the goto cheat and after the talisman
        /// ritual's moongate (game/SKILLS.C check_victory's do_teleport to level 9 (27, 23)). 0 for none (UW2).
        /// </summary>
        public static int PortCrashLevel(GameKind kind) { return kind == GameKind.UW2 ? 0 : 9; }

        /// <summary>What the report says about the level the port crashes on.</summary>
        public static string PortCrashNote(int level)
        {
            return "Underworld Exhumed crashed on arriving at level " + level + ", as it does on the shipped game (with a new character, and after the talisman ritual's moongate): a bug of the port, not of the edits, so level " + level + " could not be checked. The DOSBox spot test can check it in the original game.";
        }

        /// <summary>
        /// The strength the keep-alive cheat sets at an experience level: the most, up to 99, whose
        /// maximum hit points (30 + level × strength / 5, SKILLS.C player_compute) still fit the byte
        /// the game keeps them in (playerdat->avghit is an unsigned char: more wraps round to a few
        /// hit points). Level 1: 99 (49 hit points); level 16: 70 (254).
        /// </summary>
        public static int AliveStrengthAt(int level) { level = Math.Max(1, level); return Math.Min(99, (225 * 5 + 4) / level); }

        /// <summary>The maximum hit points at an experience level and strength (SKILLS.C player_compute).</summary>
        public static int MaxHpAt(int level, int strength) { return 30 + level * strength / 5; }

        /// <summary>
        /// Makes the player in a saved game's PLAYER.DAT experience level 16 (with at least that
        /// level's experience, 96000) and fills the hit points to that level's maximum at the
        /// keep-alive strength; the script's cheat sets the strength again and works the maxima out.
        /// </summary>
        static void RaiseToTopLevel(string saveFolder, GameKind kind)
        {
            string path = GameProfile.Find(saveFolder, "PLAYER.DAT");
            if (path == null) return;
            var pd = PlayerDat.Parse(File.ReadAllBytes(path), kind);
            int str = AliveStrengthAt(TopLevel), hp = MaxHpAt(TopLevel, str);
            pd.Level = TopLevel;
            if (pd.ExperienceTenths < 960000) pd.ExperienceTenths = 960000;
            pd.Strength = str;
            pd.MaxVitality = hp;
            pd.Vitality = hp;
            File.WriteAllBytes(path, pd.ToBytes());
        }

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
            Func<int, int, bool> good = (x, y) => Standable(s, m, terrain, x, y);
            var start = s.Start;
            if (start != null && start.Level == m.Level && good((int)start.X, (int)start.Y)) return ((int)start.X, (int)start.Y);
            // nearest the middle first: away from the map's edge, likely in the level's heart
            var all = new List<(int X, int Y)>();
            for (int y = 2; y < 62; y++) for (int x = 2; x < 62; x++) if (good(x, y)) all.Add((x, y));
            if (all.Count == 0) return null;
            return all.OrderBy(t => Math.Abs(t.X - 32) + Math.Abs(t.Y - 32)).First();
        }

        /// <summary>The standable square nearest (x, y), at most r squares away each way (nearest first by distance), or null.</summary>
        public static (int X, int Y)? NearestStandable(GameSession s, LevelModel m, TerrainTable terrain, int x, int y, int r, bool loose = false)
        {
            (int X, int Y)? best = null;
            int bestD = int.MaxValue;
            for (int yy = Math.Max(1, y - r); yy <= Math.Min(62, y + r); yy++)
                for (int xx = Math.Max(1, x - r); xx <= Math.Min(62, x + r); xx++)
                {
                    int d = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                    if (d < bestD && Standable(s, m, terrain, xx, yy, loose)) { best = (xx, yy); bestD = d; }
                }
            return best;
        }

        /// <summary>
        /// The squares a check sends the player to on a level: its own square (GotoSquare), then
        /// with the options a square beside each edited spot and one in each 16 by 16 area with
        /// room, each at least Spacing squares from the others.
        /// </summary>
        public static List<ExhumedVisit> Stops(GameSession s, LevelEdits edits, int level, ExhumedOptions o, TerrainTable terrain = null)
        {
            o = o ?? new ExhumedOptions();
            terrain = terrain ?? TryTerrain(s);
            var m = edits != null ? edits.Model(level) : s.LoadLevel(level);
            var list = new List<ExhumedVisit>();
            var first = GotoSquare(s, m, terrain);
            if (first == null) return list;
            list.Add(new ExhumedVisit { Level = level, X = first.Value.X, Y = first.Value.Y, Why = "level" });
            Func<int, int, bool> far = (x, y) => list.All(v => Math.Max(Math.Abs(v.X - x), Math.Abs(v.Y - y)) >= o.Spacing);
            if (o.VisitEdits && edits != null)
            {
                int n = 0;
                foreach (var spot in EditedSpots(s, edits, level))
                {
                    if (n >= o.MaxEditStops) break;
                    // an edit often makes the squares round it uneven: fall back to a plain open, dry, empty square
                    var sq = NearestStandable(s, m, terrain, spot.X, spot.Y, 4) ?? NearestStandable(s, m, terrain, spot.X, spot.Y, 4, true);
                    if (sq == null || !far(sq.Value.X, sq.Value.Y)) continue;
                    list.Add(new ExhumedVisit { Level = level, X = sq.Value.X, Y = sq.Value.Y, Why = "edit" });
                    n++;
                }
            }
            if (o.Sweep)
                for (int by = 0; by < 4; by++)
                    for (int bx = 0; bx < 4; bx++)
                    {
                        var sq = NearestStandable(s, m, terrain, bx * 16 + 8, by * 16 + 8, 7);
                        if (sq == null || !far(sq.Value.X, sq.Value.Y)) continue;
                        list.Add(new ExhumedVisit { Level = level, X = sq.Value.X, Y = sq.Value.Y, Why = "sweep" });
                    }
            return list;
        }

        /// <summary>
        /// The squares of a level the edits changed: tiles whose record differs from the level as
        /// saved, and the squares of objects added, removed, moved or changed (both where they
        /// were and where they are; an object inside another counts at its holder's square).
        /// </summary>
        public static List<(int X, int Y)> EditedSpots(GameSession s, LevelEdits edits, int level)
        {
            var spots = new List<(int X, int Y)>();
            var seen = new HashSet<int>();
            Action<int, int> add = (x, y) => { if (x >= 0 && y >= 0 && x < 64 && y < 64 && seen.Add(x + 64 * y)) spots.Add((x, y)); };
            LevelModel before, now;
            try { before = s.LoadLevel(level); now = edits.Model(level); }
            catch (Exception) { return spots; }
            if (before == null || now == null || !now.Present) return spots;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    if (!before.Tiles[x, y].Record.AsSpan().SequenceEqual(now.Tiles[x, y].Record)) add(x, y);
            int n = Math.Min(before.Objects.Length, now.Objects.Length);
            for (int i = 1; i < n; i++)
            {
                var a = before.Objects[i]; var b = now.Objects[i];
                bool same = a.Free == b.Free && a.TileX == b.TileX && a.TileY == b.TileY && a.Record.AsSpan().SequenceEqual(b.Record);
                if (same) continue;
                if (!a.Free) add(a.TileX, a.TileY);
                if (!b.Free) add(b.TileX, b.TileY);
            }
            return spots;
        }

        /// <summary>A square the player can be sent to: open and flat, its eight neighbours open at the same height, not water or lava, and nothing lying on it.</summary>
        public static bool Standable(GameSession s, LevelModel m, TerrainTable terrain, int x, int y, bool loose = false)
        {
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
                if (loose)
                {
                    // open, dry and empty, with at least three open neighbours of its four, so there is room to be put down
                    int open = 0;
                    if (m.Tiles[x + 1, y].Type != TileType.Solid) open++;
                    if (m.Tiles[x - 1, y].Type != TileType.Solid) open++;
                    if (m.Tiles[x, y + 1].Type != TileType.Solid) open++;
                    if (m.Tiles[x, y - 1].Type != TileType.Solid) open++;
                    return open >= 3;
                }
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = m.Tiles[x + dx, y + dy];
                        if (n.Type != TileType.Open || n.Height != t.Height) return false;
                    }
                return true;
            };
            return good(x, y);
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

        /// <summary>The levels changed since they were opened (the default to check).</summary>
        public static List<int> EditedLevels(LevelEdits edits)
        {
            return edits == null ? new List<int>() : edits.DirtyLevels.ToList();
        }

        /// <summary>
        /// Sets a check up in folder (made afresh; only a folder made here is ever cleared): the
        /// copied game with the edits, the port's home with saved game I holding them, the script.
        /// </summary>
        public static ExhumedPlan Prepare(GameSession s, LevelEdits edits, byte[] playerDat, IList<int> levels, string folder, bool saveAtEnd = true, bool keepAlive = true, ExhumedOptions options = null, bool showWindow = false)
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
                if (keepAlive) RaiseToTopLevel(slot, s.Kind);
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
                // no god mode in the port: the saved game's player is made level 16 (no cheat sets the level; a new character stays 1), then its set cheat
                // (port/sys/cheats.c c_set) raises the strength and works the maxima out as a level gained does, and the hit points are filled before each level
                plan.AliveLevel = plan.NewCharacter ? 1 : TopLevel;
                plan.AliveStrength = AliveStrengthAt(plan.AliveLevel);
                plan.AliveHp = MaxHpAt(plan.AliveLevel, plan.AliveStrength);
                sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat set str {1}        # keep the player alive\n{2} cheat set defense 30", t, plan.AliveStrength, t + 200));
                t += 500;
            }
            options = options ?? new ExhumedOptions();
            plan.Options = options.Clone();
            plan.SavesAtEnd = saveAtEnd && !s.IsUw2;
            // the level the port crashes on goes last, after the save, so that the others are checked and saved first
            int crashLevel = PortCrashLevel(s.Kind);
            levels = levels.OrderBy(l => l == crashLevel ? 1 : 0).ToList();
            bool saved = false;
            Action save = () =>
            {
                if (!plan.SavesAtEnd || saved) return;
                saved = true;
                // UW1: Ctrl+S, slot I, Enter keeps the description (GAMEWRAP.C SaveGame); a new character's slot has none, so one is typed
                sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} down ctrl\n{1} key s\n{2} up ctrl\n{3} key enter        # slot I", t, t + 100, t + 300, t + 2000));
                int at = t + 3500;
                if (plan.NewCharacter) foreach (char c in "check") { sc.AppendLine(at + " key " + c); at += 200; }
                sc.AppendLine((at + 500) + " key enter        # the description");
                t = at + 500 + VisitMs;
            };
            foreach (int level in levels)
            {
                if (level == crashLevel)
                {
                    save();
                    plan.CrashLevelLast = true;
                    sc.AppendLine("# level " + level + " last: Underworld Exhumed crashes on arriving there");
                }
                foreach (var v in Stops(s, edits, level, options, terrain))
                {
                    if (keepAlive) { sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat set hp {1}", t, plan.AliveHp)); t += 300; }
                    v.At = t;
                    plan.Visits.Add(v);
                    sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} cheat goto {1} {2} {3}        # {4}", t, level, v.X, v.Y, v.Why == "level" ? "the level's square" : v.Why == "edit" ? "beside an edit" : "sweep"));
                    t += VisitMs;
                    if (options.LookAround)
                    {
                        // a full turn: the turn-left key turns 15 * (0x5A / 4) / 4 a 1/256 s, about 3.1 s a turn (motion/PHYSICS.C, PLAYMOVE.C)
                        sc.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} down a        # look around\n{1} up a", t - 1500, t - 1500 + LookMs));
                        t += LookMs;
                    }
                }
            }
            save();
            plan.ExitAfterMs = t + 3000;
            File.WriteAllText(plan.Script, sc.ToString());

            plan.Shown = showWindow;
            var args = new List<string> { "--data", plan.Game, "--home", plan.Home };
            if (!showWindow) args.Add("--hidden");
            args.AddRange(new[] { "--no-audio", "--no-recording", "--exit-on-halt",
                "--input-script", plan.Script, "--screenshot-after", (plan.ExitAfterMs - 1000).ToString(CultureInfo.InvariantCulture), "--screenshot", plan.Screenshot,
                "--exit-after", plan.ExitAfterMs.ToString(CultureInfo.InvariantCulture) });
            plan.Arguments.InsertRange(0, args);
            plan.Environment["EXHUME_TEST_CHEATS"] = "1";
            plan.ReadyUtc = DateTime.UtcNow;
            string stops = string.Join("; ", plan.Visits.GroupBy(v => v.Level).Select(g => (s.IsUw2 ? "slot " : "level ") + g.Key + " " + string.Join(", ", g.Select(v => "(" + v.X + ", " + v.Y + ")" + (v.Why == "edit" ? " by an edit" : v.Why == "sweep" ? " sweep" : "")))));
            plan.Note = string.Format("{0}{1}, then {2} stop{3}: {4}{5}{6}.", plan.NewCharacter ? "A new character" : "Saved game I", keepAlive ? string.Format(" kept alive (experience level {0}{1}, strength {2}, defense 30, {3} hit points before each stop)", plan.AliveLevel, plan.NewCharacter ? " as a new character: no cheat sets the level" : "", plan.AliveStrength, plan.AliveHp) : "",
                plan.Visits.Count, plan.Visits.Count == 1 ? "" : "s", stops, options.LookAround ? "; looking round at each" : "",
                (plan.SavesAtEnd ? (plan.CrashLevelLast ? "; a save in slot I before level " + crashLevel + ", which comes last (Underworld Exhumed crashes there)" : "; then a save in slot I") : plan.CrashLevelLast ? "; level " + crashLevel + " last (Underworld Exhumed crashes there)" : "")
                + (showWindow ? "; the game's window shown" : ""));
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
                    else
                    {
                        if (!r.Reached.Contains(lv)) r.Reached.Add(lv);
                        r.StopsReached.Add(lv + " " + m.Groups[2].Value + " " + m.Groups[3].Value);
                    }
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
            // the port's own crash on arriving at the level it always crashes on: a note, not a problem
            int crashLevel = PortCrashLevel(s.Kind);
            int lastGoto = 0;
            foreach (var raw in lines)
            {
                var gm = System.Text.RegularExpressions.Regex.Match(raw, @"cheat: goto (\d+) \d+ \d+ \(not as DOS\)");
                if (gm.Success) lastGoto = int.Parse(gm.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            bool knownCrash = crashLevel > 0 && lastGoto == crashLevel && !timedOut && exitCode != 0;
            if (knownCrash)
            {
                r.Problems.RemoveAll(p => p == why || p.StartsWith("the port crashed (", StringComparison.Ordinal) || p.StartsWith("the port halted: ", StringComparison.Ordinal));
                r.Notes.Add(PortCrashNote(crashLevel));
                r.PortCrashedOn = crashLevel;
            }
            foreach (var v in plan.Visits)
                if (!(knownCrash && v.Level == crashLevel) && !r.StopsReached.Contains(v.Key) && !(r.Reached.Contains(v.Level) && plan.Visits.Count(x => x.Level == v.Level) == 1) && !r.Refused.Any(x => x.StartsWith("level " + v.Level + ":", StringComparison.Ordinal)))
                    r.Problems.Add((plan.Visits.Count(x => x.Level == v.Level) > 1 ? "(" + v.X + ", " + v.Y + ") on " + (s.IsUw2 ? "slot " : "level ") : (s.IsUw2 ? "Slot " : "Level ")) + v.Level + " was never reached (the run ended or stalled before its cheat ran)");
            if (plan.Visits.Count > plan.Visits.Select(v => v.Level).Distinct().Count())
                r.Notes.Add("The player stopped at " + r.StopsReached.Count + " of " + plan.Visits.Count + " squares.");
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
                        foreach (int lv in r.Reached.Distinct().Where(l => !(knownCrash && l == crashLevel)))
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
