using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.Core
{
    /// <summary>The tester the spot test's saved games hold, and which spots it visits.</summary>
    public sealed class SpotTestOptions
    {
        /// <summary>Which squares, as the Underworld Exhumed check picks them (beside edits, the sweep).</summary>
        public ExhumedOptions Stops = new ExhumedOptions();
        public int Strength = 30, Dexterity = 30, Intelligence = 30, Defense = 30;
        /// <summary>Wear the best armour the object tables have, one piece a body slot and a shield.</summary>
        public bool BestArmour = true;
        /// <summary>Saved games a round (the game has four slots).</summary>
        public int PerRound = 4;
        public SpotTestOptions Clone() { var o = (SpotTestOptions)MemberwiseClone(); o.Stops = Stops.Clone(); return o; }
    }

    /// <summary>A spot test under way: the copy of the game, the spots, and how far it got.</summary>
    public sealed class SpotTestRun
    {
        public string Folder, Exe;
        public readonly List<ExhumedVisit> Spots = new List<ExhumedVisit>();
        /// <summary>Each spot's floor: the tile's height, and whether it is a slope (taken when the test starts).</summary>
        public readonly List<(int Height, bool Slope)> Footing = new List<(int Height, bool Slope)>();
        /// <summary>The spots before this one have been tested (the next round starts here).</summary>
        public int Next;
        /// <summary>The first spot of the round written last, and how many it held.</summary>
        public int RoundStart, RoundCount;
        public SpotTestOptions Options;
        /// <summary>LEV.ARK as it was when the test started: every saved game gets the same.</summary>
        public byte[] Levels;
        /// <summary>The tester's PLAYER.DAT (anywhere: each saved game moves it to its spot).</summary>
        public byte[] Player;
        /// <summary>BGLOBALS.DAT of a new game (DATA's BABGLOBS.DAT with every global 0).</summary>
        public byte[] Globals;
        /// <summary>What the tester wears, in words.</summary>
        public string Wearing = "";
        public string LevArkName = "LEV.ARK";
        /// <summary>The saved game whose player object the tester has ("" when made from nothing).</summary>
        public string PlayerFrom = "";
        public int Tested { get { return Math.Min(Next, Spots.Count); } }
        public int Remaining { get { return Spots.Count - Tested; } }
        public bool Finished { get { return Next >= Spots.Count; } }
        /// <summary>The spots of the round written last.</summary>
        public List<ExhumedVisit> Round { get { return Spots.Skip(RoundStart).Take(RoundCount).ToList(); } }
    }

    /// <summary>
    /// The DOSBox spot test: the Underworld Exhumed check's stops, played by hand in the original
    /// game. Four at a time, each becomes a saved game (SAVE1-SAVE4) holding the same levels and a
    /// fresh tester standing on the spot; the person restores them in turn, then the next four
    /// are written. Everything happens in a copy of the game folder, marked so that only a
    /// folder made here is ever cleared. UW1.
    /// </summary>
    public static class SpotTest
    {
        public const string Marker = ".uwedit-spottest";
        public const string LogName = "spot-test-log.txt";

        /// <summary>The spots on the levels given, in level order, each level's as the Exhumed check would stop at them.</summary>
        public static List<ExhumedVisit> Spots(GameSession s, LevelEdits edits, IEnumerable<int> levels, ExhumedOptions o)
        {
            var list = new List<ExhumedVisit>();
            foreach (int level in levels.Distinct()) list.AddRange(ExhumedCheck.Stops(s, edits, level, o));
            return list;
        }

        /// <summary>Sets a test up in folder: the copy of the game, the spots, the tester and the levels. Writes no saved game yet.</summary>
        public static SpotTestRun Start(GameSession s, LevelEdits edits, IList<int> levels, SpotTestOptions o, string folder)
        {
            if (s == null) throw new InvalidOperationException("Open a game first.");
            if (s.IsUw2) throw new InvalidOperationException("The spot test is for UW1 for now: UW2's player record is laid out differently.");
            o = (o ?? new SpotTestOptions()).Clone();
            if (o.PerRound < 1 || o.PerRound > 4) throw new ArgumentOutOfRangeException("PerRound", "1-4 saved games a round");
            levels = (levels ?? new List<int>()).Distinct().ToList();
            if (levels.Count == 0) throw new InvalidOperationException("Choose at least one level.");
            var nowhere = ExhumedCheck.LevelsWithNowhereToStand(s, edits, levels);
            if (nowhere.Count > 0) throw new NowhereToStandException(nowhere, s.IsUw2);
            var run = new SpotTestRun { Options = o };
            run.Spots.AddRange(Spots(s, edits, levels, o.Stops));
            foreach (var v in run.Spots)
            {
                var t = (edits != null ? edits.Model(v.Level) : s.LoadLevel(v.Level)).Tiles[v.X, v.Y];
                run.Footing.Add((t.Height, t.Type >= TileType.SlopeUpN));
            }
            if (run.Spots.Count == 0) throw new InvalidOperationException("None of those levels has a square to stand on.");
            run.Player = TesterPlayer(s, o, out run.Wearing, out run.PlayerFrom);
            run.Globals = NewGameGlobals(s);
            run.Levels = edits != null ? edits.ToBytes() : s.LevArk.ToBytes();
            run.LevArkName = Path.GetFileName(s.LevArkPath);

            string game = Path.GetFullPath(s.GameFolder);
            folder = Path.GetFullPath(folder);
            if (folder.TrimEnd(Path.DirectorySeparatorChar).Equals(game.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || game.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The spot test runs in a copy, never in the game folder itself.");
            if (Directory.Exists(folder))
            {
                if (!File.Exists(Path.Combine(folder, Marker)) && Directory.EnumerateFileSystemEntries(folder).Any())
                    throw new InvalidOperationException(folder + " exists and was not made by a spot test: choose another folder.");
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, Marker), "A copy of " + game + " for a DOSBox spot test; the editor clears it each time.");
            PlayTest.CopyGame(game, folder, folder);
            // the copy's own saved games would only confuse: the test's are written a round at a time
            foreach (var d in SaveDirs(folder)) Directory.Delete(d, true);
            // a new game in the copy starts in the same levels
            string dataName = Path.GetFileName(s.DataFolder.TrimEnd(Path.DirectorySeparatorChar));
            File.WriteAllBytes(Path.Combine(folder, dataName, run.LevArkName), run.Levels);
            run.Folder = folder;
            string exe = s.ExePath ?? StartPosition.ExePath(s.DataFolder, s.Kind);
            run.Exe = exe != null ? Path.GetFileName(exe) : "UW.EXE";
            File.WriteAllText(Path.Combine(folder, LogName), string.Format(CultureInfo.InvariantCulture,
                "Spot test of {0}, started {1:yyyy-MM-dd HH:mm}: {2} spot{3}. The tester: {4} (the player object from {5}).{6}",
                s.IsSave ? s.SaveFolder : game, DateTime.Now, run.Spots.Count, run.Spots.Count == 1 ? "" : "s", Tester(o, run.Wearing),
                run.PlayerFrom == "" ? "nothing: no saved game" : run.PlayerFrom, Environment.NewLine));
            return run;
        }

        /// <summary>The tester in words.</summary>
        public static string Tester(SpotTestOptions o, string wearing)
        {
            return string.Format("strength {0}, dexterity {1}, intelligence {2}, defense {3}{4}", o.Strength, o.Dexterity, o.Intelligence, o.Defense,
                string.IsNullOrEmpty(wearing) ? "" : ", wearing " + wearing);
        }

        static IEnumerable<string> SaveDirs(string folder)
        {
            return Directory.GetDirectories(folder).Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^SAVE\d$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        }

        /// <summary>
        /// Writes the next round: SAVE1… for the spots from run.Next on (as many as a round holds),
        /// the slots it does not fill removed. Returns the spots written (none when the test is done).
        /// </summary>
        public static List<ExhumedVisit> WriteRound(GameSession s, SpotTestRun run) { return WriteRound(s, run, run.Next); }

        /// <summary>Writes the round starting at spot first (an earlier round again, say).</summary>
        public static List<ExhumedVisit> WriteRound(GameSession s, SpotTestRun run, int first)
        {
            if (run == null || run.Folder == null) throw new InvalidOperationException("Start the test first.");
            first = Math.Max(0, Math.Min(first, run.Spots.Count));
            int n = Math.Min(run.Options.PerRound, run.Spots.Count - first);
            run.RoundStart = first; run.RoundCount = n;
            foreach (var d in SaveDirs(run.Folder)) Directory.Delete(d, true);
            for (int i = 0; i < n; i++)
            {
                var v = run.Spots[first + i];
                string dir = Path.Combine(run.Folder, "SAVE" + (i + 1));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, run.LevArkName), run.Levels);
                File.WriteAllBytes(Path.Combine(dir, "BGLOBALS.DAT"), run.Globals);
                var pd = PlayerDat.Parse((byte[])run.Player.Clone(), s.Kind);
                var f = run.Footing[first + i];
                pd.SetPosition(v.Level, v.X, v.Y, f.Height, f.Slope, 0);
                File.WriteAllBytes(Path.Combine(dir, "PLAYER.DAT"), pd.ToBytes());
                File.WriteAllBytes(Path.Combine(dir, "DESC"), Encoding.Latin1.GetBytes(Description(run, first + i)));
            }
            return run.Round;
        }

        /// <summary>The saved game's description (at most 30 letters): which spot, where.</summary>
        public static string Description(SpotTestRun run, int i)
        {
            var v = run.Spots[i];
            string d = string.Format(CultureInfo.InvariantCulture, "{0}/{1} L{2} {3},{4} {5}", i + 1, run.Spots.Count, v.Level, v.X, v.Y, v.Why);
            return d.Length > 0x1E ? d.Substring(0, 0x1E) : d;
        }

        /// <summary>One line a spot, for the prompt and the log.</summary>
        public static string Describe(SpotTestRun run, int i, int slot)
        {
            var v = run.Spots[i];
            return string.Format(CultureInfo.InvariantCulture, "SAVE{0}: spot {1} of {2}, level {3} ({4}, {5}){6}", slot, i + 1, run.Spots.Count, v.Level, v.X, v.Y,
                v.Why == "edit" ? ", beside an edit" : v.Why == "sweep" ? ", sweep" : ", the level's square");
        }

        /// <summary>Marks the round written last as tested, with the person's notes in the log; the next round starts after it.</summary>
        public static void FinishRound(SpotTestRun run, string notes)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < run.RoundCount; i++) sb.AppendLine(Describe(run, run.RoundStart + i, i + 1));
            if (!string.IsNullOrWhiteSpace(notes)) sb.AppendLine("  Notes: " + notes.Trim().Replace("\r\n", "\n").Replace("\n", Environment.NewLine + "  "));
            try { File.AppendAllText(Path.Combine(run.Folder, LogName), sb.ToString()); } catch (Exception) { }
            run.Next = Math.Max(run.Next, run.RoundStart + run.RoundCount);
        }

        /// <summary>A note in the log (the test stopped, say).</summary>
        public static void Log(SpotTestRun run, string line)
        {
            try { File.AppendAllText(Path.Combine(run.Folder, LogName), line + Environment.NewLine); } catch (Exception) { }
        }

        /// <summary>A new game's BGLOBALS.DAT: DATA's BABGLOBS.DAT with every global 0 (conv/BABL.C init_babl).</summary>
        public static byte[] NewGameGlobals(GameSession s)
        {
            string bab = GameProfile.Find(s.DataFolder, "BABGLOBS.DAT");
            if (bab == null) throw new InvalidOperationException("DATA has no BABGLOBS.DAT: a saved game needs the conversations' globals.");
            return BGlobals.Parse(File.ReadAllBytes(bab), false).ToBytes(true);
        }

        /// <summary>
        /// The PLAYER.DAT a test starts from: the open saved game's, or the game's first saved
        /// game's (the game's own player object, which the tester keeps), or with none a blank one
        /// (BlankPlayer). Whose it was, in from ("" for the blank one).
        /// </summary>
        public static byte[] TemplatePlayer(GameSession s, out string from)
        {
            string dir = ExhumedCheck.SaveSource(s);
            string p = dir == null ? null : GameProfile.Find(dir, "PLAYER.DAT");
            if (p != null)
            {
                try
                {
                    var bytes = File.ReadAllBytes(p);
                    PlayerDat.Parse(bytes, s.Kind);
                    from = dir;
                    return bytes;
                }
                catch (Exception) { }
            }
            from = "";
            return BlankPlayer();
        }

        /// <summary>
        /// A UW1 PLAYER.DAT made from nothing, for a game with no saved game: an empty record (the
        /// tester's fields are set after), sound and music on and detail 3 (init_char), and a
        /// workspace with the player object as InitPlayerRec makes it (game/PLAYER.C: the
        /// adventurer 0x7F, door direction set, whoami 0xFD) with the bytes every UW1 save has in
        /// it (0x13 = 5, 0x14 = 0x80, 0x15 = 0xAC), no cursor object, no slots and no objects.
        /// </summary>
        public static byte[] BlankPlayer()
        {
            var rec = new byte[PlayerDat.RecordSizeFor(GameKind.UW1)];
            rec[0] = (byte)'T';
            rec[0xB5] = 0x35;                    // sound 1, music 1, detail 3
            var rest = new byte[2 + 0x5B + 8];
            rest[0] = 1;                          // the count word: no objects
            const int o = 2;
            rest[o + 0] = 0x7F; rest[o + 1] = 0x20;
            rest[o + 0x13] = 0x05; rest[o + 0x14] = 0x80; rest[o + 0x15] = 0xAC;
            rest[o + 0x1A] = 0xFD;
            byte key = (byte)(rec[0] ^ 0xAA);
            var enc = PlayerDat.Cipher(rec, key, GameKind.UW1, true);
            var f = new byte[1 + enc.Length + rest.Length];
            f[0] = key;
            Array.Copy(enc, 0, f, 1, enc.Length);
            Array.Copy(rest, 0, f, 1 + enc.Length, rest.Length);
            return f;
        }

        /// <summary>The tester's PLAYER.DAT: a saved game's player reset to a new character, with the options' attributes, defense and armour.</summary>
        public static byte[] TesterPlayer(GameSession s, SpotTestOptions o, out string wearing)
        {
            string from;
            return TesterPlayer(s, o, out wearing, out from);
        }

        public static byte[] TesterPlayer(GameSession s, SpotTestOptions o, out string wearing, out string from)
        {
            var pd = PlayerDat.Parse(TemplatePlayer(s, out from), s.Kind);
            ObjectNames names = null;
            try { names = ObjectNames.Load(s.DataFolder); } catch (Exception) { }
            return Tester(pd, o, s.DataFolder, out wearing, names).ToBytes();
        }

        /// <summary>
        /// A new character from a player (UW1): the record as init_char(1) leaves it (game/CHARGEN.C:
        /// level 1, no experience, one skill point, the game clock at 0x10B3000, the moonstone on
        /// level 2, eight talismans, no quests, dreams, runes, spells or game variables but
        /// variable 0x1A at 0x35, hunger 0xC0, fatigue and food 0x40, every skill 0, right-handed),
        /// the attributes and defense given, the maxima as player_compute works them out
        /// (game/SKILLS.C: hit points 30 + level × strength / 5, mana (mana skill + 1) × intelligence
        /// / 8, carrying 20 × strength), full hit points and mana, and an inventory of the best
        /// armour only (or nothing). The player object of the workspace (the game's own, as the save
        /// had it) is kept; its hit points follow. The name becomes "Tester".
        /// </summary>
        public static PlayerDat Tester(PlayerDat pd, SpotTestOptions o, string dataFolder, out string wearing, ObjectNames names = null)
        {
            if (pd.Game != GameKind.UW1) throw new InvalidOperationException("UW1 only.");
            var r = pd.Record;
            Action<int, int> b = (at, v) => r[at] = (byte)Math.Max(0, Math.Min(255, v));
            Action<int, int> w = (at, v) => { r[at] = (byte)v; r[at + 1] = (byte)(v >> 8); };
            // name
            for (int i = 0; i < 0x1E; i++) r[i] = 0;
            var name = Encoding.ASCII.GetBytes("Tester");
            Array.Copy(name, r, name.Length);
            b(0x1E, o.Strength); b(0x1F, o.Dexterity); b(0x20, o.Intelligence);
            for (int i = 0; i < 20; i++) r[0x21 + i] = 0;
            b(0x21 + 1, o.Defense);
            r[0x39] = 0xC0; r[0x3A] = 0x40; r[0x3B] = 0x40; r[0x3C] = 0;   // hunger, fatigue, food_heal, b3C
            r[0x3D] = 1;                                                    // level
            for (int i = 0x3E; i < 0x44; i++) r[i] = 0;                     // the active spells
            for (int i = 0x44; i < 0x47; i++) r[i] = 0;                     // the rune bag
            for (int i = 0x47; i < 0x4A; i++) r[i] = 0x18;                  // the shelf: no runes
            for (int i = 0x4E; i < 0x52; i++) r[i] = 0;                     // experience
            r[0x52] = 1; r[0x53] = 0;                                       // skill points
            r[0x5E] = 2;                                                    // moonstone on level 2, no silver tree
            w(0x5F, 0);                                                     // drawn, poison, active spells, runes, armageddon, orb, key, cup
            w(0x61, 0);                                                     // incense, mushrooms, drink, talisman_ok, garamon, maze
            r[0x63] = 0;                                                    // light
            r[0x64] = (byte)((r[0x64] & 0xFE) | 1);                         // right-handed (lefty 1, as init_char); sex, body and class kept
            for (int i = 0x65; i < 0x6D; i++) r[i] = 0;                     // quests 0-35
            r[0x6D] = 8;                                                    // talismans left
            w(0x6E, 0);                                                     // dreams
            for (int i = 0x70; i < 0xB0; i++) r[i] = 0;                     // game variables
            r[0x70 + 0x1A] = 0x35;
            r[0xB0] = 0;                                                    // the maximum mana kept on level 7
            w(0xB6, 0);                                                     // fps 0, terrain 0 (worked out again on arrival)
            r[0xB8] = 0; r[0xB9] = 0; r[0xBA] = 0; r[0xBB] = 0;              // motion state, swimming, the last critical hit
            for (int i = 0xBC; i < 0xC2; i++) r[i] = 0;
            for (int i = 0xC2; i < 0xCA; i++) r[i] = 0;                     // lore by level
            r[0xCE] = 0x00; r[0xCF] = 0x30; r[0xD0] = 0x0B; r[0xD1] = 0x01; // the game clock, 0x10B3000
            int hp = 30 + 1 * o.Strength / 5;
            int mana = (0 + 1) * o.Intelligence >> 3;
            b(0x36, hp); b(0x37, mana); b(0x38, mana);
            w(0x4C, o.Strength * 2 * 10);

            // the inventory: the best armour only
            var inv = PlayerInventory.Parse(pd.Rest);
            foreach (var t in inv.Top.ToList()) inv.Remove(t);
            inv.Changed = true;
            int weight = 0;
            var worn = new List<string>();
            if (o.BestArmour)
                foreach (var piece in BestArmour(dataFolder))
                {
                    var it = inv.Add(piece.Item, null, false);
                    it.Quality = 0x3F;
                    int slot = piece.Slot < 0 ? 7 + (r[0x64] & 1) : piece.Slot;   // the shield in the shield hand: 7 + lefty (combat/COMBAT.C)
                    inv.SetSlot(it, slot);
                    weight += piece.Mass;
                    worn.Add(names != null ? names.Get(piece.Item).ToLowerInvariant() : piece.Name);
                }
            w(0x4A, weight);
            wearing = string.Join(", ", worn);
            var tester = pd.WithRest(inv.Serialize(pd.Rest));
            tester.Vitality = hp;
            return tester;
        }

        /// <summary>A piece of armour: its item, the slot it goes in (-1 the shield hand), its mass in tenths of a stone.</summary>
        public sealed class Piece
        {
            public int Item, Slot, Mass, Protection;
            public string Name;
        }

        static readonly Dictionary<int, int> SlotOfCategory = new Dictionary<int, int> { { 8, 0 }, { 1, 1 }, { 4, 2 }, { 3, 3 }, { 5, 4 } };
        static readonly string[] SlotWords = { "helm", "body armour", "gloves", "leggings", "boots" };

        /// <summary>
        /// The best armour by OBJECTS.DAT (UW1, the armour table at 0xB2, 4 bytes an item from
        /// 0x20: protection, durability, -, category): for each body slot the most protection, then
        /// durability, leaving out the unbreakable specials (durability 255: the dragon skin boots,
        /// the crowns); and the best shield of those the game counts as worn (0x3B-0x3F,
        /// PLAYDATA.C). Masses from COMOBJ.DAT.
        /// </summary>
        public static List<Piece> BestArmour(string dataFolder)
        {
            string objects = GameProfile.Find(dataFolder, "OBJECTS.DAT"), comobj = GameProfile.Find(dataFolder, "COMOBJ.DAT");
            if (objects == null || comobj == null) return new List<Piece>();
            var od = File.ReadAllBytes(objects); var cd = File.ReadAllBytes(comobj);
            var all = new List<(int Item, int Prot, int Dur, int Cat, int Mass)>();
            for (int i = 0; i < 32; i++)
            {
                int at = 0xB2 + 4 * i, item = 0x20 + i;
                if (at + 4 > od.Length) break;
                int c = 2 + 11 * item;
                int mass = c + 3 <= cd.Length ? (cd[c + 1] >> 4) | (cd[c + 2] << 4) : 0;
                all.Add((item, od[at], od[at + 1], od[at + 3], mass));
            }
            var list = new List<Piece>();
            foreach (var kv in SlotOfCategory.OrderBy(k => k.Value))
            {
                var best = all.Where(a => a.Cat == kv.Key && a.Dur != 255 && a.Prot > 0).OrderByDescending(a => a.Prot).ThenByDescending(a => a.Dur).FirstOrDefault();
                if (best.Item != 0) list.Add(new Piece { Item = best.Item, Slot = kv.Value, Mass = best.Mass, Protection = best.Prot, Name = SlotWords[kv.Value] + " 0x" + best.Item.ToString("X2") });
            }
            var shield = all.Where(a => a.Cat == 0 && a.Item >= 0x3B && a.Item <= 0x3F && a.Prot > 0).OrderByDescending(a => a.Prot).ThenByDescending(a => a.Dur).FirstOrDefault();
            if (shield.Item != 0) list.Add(new Piece { Item = shield.Item, Slot = -1, Mass = shield.Mass, Protection = shield.Prot, Name = "shield 0x" + shield.Item.ToString("X2") });
            return list;
        }
    }
}
