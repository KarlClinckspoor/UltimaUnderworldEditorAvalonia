using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Player
{
    public enum WarningLevel
    {
        /// <summary>The game itself notices: UW2's end screen says the character cheated.</summary>
        Detected,
        /// <summary>Play cannot produce it (character creation and the game's rules never give it).</summary>
        Unreachable,
        /// <summary>Allowed, but the game will change it or it disagrees with another value.</summary>
        Note,
    }

    public sealed class PlayerWarning
    {
        public WarningLevel Level;
        public string Field;
        public string Message;
        public override string ToString() { return Level + ": " + Message; }
    }

    /// <summary>A character class's start from DATA\SKILLS.DAT: base attributes and bonus points (game/CHARGEN.C).</summary>
    public sealed class ClassStart
    {
        public int Strength, Dexterity, Intelligence, Bonus;
        public int Total { get { return Strength + Dexterity + Intelligence + Bonus; } }
    }

    /// <summary>
    /// The player record of a saved game's PLAYER.DAT, field by field, with checks against what
    /// the game can produce. Offsets are struct Player's (include/player.h of both games): the
    /// two agree up to 0x5D; the appearance byte (lefty bit 0, female bit 1, body bits 2-4,
    /// class bits 5-7) is at 0x64 in UW1 and 0x65 in UW2. The current vitality lives twice, in
    /// the record and in the workspace's player object, and the game keeps the latter.
    /// </summary>
    public sealed class PlayerSheet
    {
        public GameKind Kind { get; private set; }
        public PlayerDat Dat { get; private set; }
        public string Path { get; private set; }
        public ClassStart[] Classes { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        bool dirty;
        /// <summary>Unsaved edits: the record, or the inventory (whose edits mark only it).</summary>
        public bool Dirty { get { return dirty || (Inventory != null && Inventory.Changed); } set { dirty = value; } }
        bool uw2 { get { return Kind == GameKind.UW2; } }
        byte[] R { get { return Dat.Record; } }

        public static readonly string[] ClassNames = { "Fighter", "Mage", "Bard", "Tinker", "Druid", "Paladin", "Ranger", "Shepherd" };

        /// <summary>The experience levels: level n + 2 needs exp / 500 (in tenths) of at least table[n] (game/SKILLCHK.C level_table); levels stop at 16.</summary>
        public static readonly int[] LevelTable = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192 };

        public static PlayerSheet Load(string path, GameKind kind, string dataFolder)
        {
            var bytes = File.ReadAllBytes(path);
            var s = FromBytes(bytes, kind, dataFolder);
            s.Path = path;
            return s;
        }

        /// <summary>A sheet from bytes that belong to a file (a checkpoint restored): unsaved until written there.</summary>
        public static PlayerSheet FromBytes(byte[] bytes, GameKind kind, string dataFolder, string path)
        {
            var s = FromBytes(bytes, kind, dataFolder);
            s.Path = path;
            s.Dirty = true;
            return s;
        }

        public static PlayerSheet FromBytes(byte[] bytes, GameKind kind, string dataFolder)
        {
            var detected = PlayerDat.Detect(bytes);
            if (detected == null) throw new InvalidDataException("This PLAYER.DAT has neither game's length (1 + record + 2 + 0x5B + 8 × objects); DATA's 301-byte template is not a saved player.");
            if (detected.Value != kind) throw new InvalidDataException("This PLAYER.DAT is " + detected.Value + "'s, not " + kind + "'s.");
            var s = new PlayerSheet { Kind = kind, Dat = PlayerDat.Parse(bytes, kind) };
            s.Classes = ReadClasses(dataFolder == null ? null : GameProfile.Find(dataFolder, "SKILLS.DAT"));
            s.Inventory = PlayerInventory.Parse(s.Dat.Rest);
            return s;
        }

        static ClassStart[] ReadClasses(string path)
        {
            var c = new ClassStart[8];
            byte[] d = path != null ? File.ReadAllBytes(path) : null;
            for (int i = 0; i < 8; i++)
                c[i] = d != null && d.Length >= 32
                    ? new ClassStart { Strength = d[4 * i], Dexterity = d[4 * i + 1], Intelligence = d[4 * i + 2], Bonus = d[4 * i + 3] }
                    : null;
            return c;
        }

        // ---- fields

        public string Name { get { return Dat.Name; } set { Dat.Name = value; Dirty = true; } }
        public int Strength { get { return R[0x1E]; } set { Set(0x1E, value); } }
        public int Dexterity { get { return R[0x1F]; } set { Set(0x1F, value); } }
        public int Intelligence { get { return R[0x20]; } set { Set(0x20, value); } }
        public int Skill(int i) { return R[0x21 + i]; }
        public void SetSkill(int i, int v) { Set(0x21 + i, v); }
        public int Vitality { get { return Dat.Vitality; } set { Dat.Vitality = value; Dirty = true; } }
        public int MaxVitality { get { return R[0x36]; } set { Set(0x36, value); } }
        public int Mana { get { return R[0x37]; } set { Set(0x37, value); } }
        public int MaxMana { get { return R[0x38]; } set { Set(0x38, value); } }
        public int Hunger { get { return R[0x39]; } set { Set(0x39, value); } }
        public int Fatigue { get { return R[0x3A]; } set { Set(0x3A, value); } }
        public int Level { get { return R[0x3D]; } set { Set(0x3D, value); } }
        public int Weight { get { return R[0x4A] | R[0x4B] << 8; } }
        public int MaxWeight { get { return R[0x4C] | R[0x4D] << 8; } set { Word(0x4C, value); } }
        /// <summary>Experience as the game shows it (the record keeps tenths).</summary>
        public long Experience { get { return Dat.ExperienceTenths / 10; } set { Dat.ExperienceTenths = value * 10; Dirty = true; } }
        public long ExperienceTenths { get { return Dat.ExperienceTenths; } set { Dat.ExperienceTenths = value; Dirty = true; } }
        public int SkillPoints { get { return R[0x52]; } set { Set(0x52, value); } }
        public int SkillPointsEarned { get { return R[0x53]; } set { Set(0x53, value); } }

        // ---- UW1's story bits (include/player.h: byte 0x62 and 0x6D), not quest flags

        /// <summary>The story bits below are known (UW1; Underworld II keeps other things there).</summary>
        public bool HasStoryBits { get { return !uw2; } }

        /// <summary>
        /// UW1: talismans thrown into the lava of the Slasher's chamber are destroyed (byte 0x62 bit 2,
        /// player->talisman_ok; motion/OBJPHYS.C mob_to_static). Set with GaramonBuried when Garamon's
        /// bones are used on his grave (obj/USEITEMS.C UseBonesOn), before his ghost speaks; cleared
        /// when the ghost (whoami 0x1B) is killed (event/WORLDEV.C death_check). No quest flag stands
        /// for it: quest 5 is only read, by Garamon's conversation, and nothing in the game sets it.
        /// </summary>
        public bool TalismansCanBurn { get { return !uw2 && (R[0x62] & 4) != 0; } set { StoryBit(0x62, 4, value); } }

        /// <summary>UW1: Garamon is buried (byte 0x62 bit 3, player->garamon), set with TalismansCanBurn.</summary>
        public bool GaramonBuried { get { return !uw2 && (R[0x62] & 8) != 0; } set { StoryBit(0x62, 8, value); } }

        /// <summary>UW1: the talismans still to destroy (byte 0x6D: 8 at the start, 0xFF once the last is gone and the moongate opened; game/SKILLS.C check_victory).</summary>
        public int TalismansLeft { get { return uw2 ? 0 : R[0x6D]; } set { if (uw2) throw new InvalidOperationException("Ultima Underworld 1 only."); Set(0x6D, value); } }

        /// <summary>UW1: Tybal's orb on level 7 is destroyed (byte 0x60 bit 5, player->orb). While it is not, level 7 takes the mana away and puts the maze back (game/GAMEWRAP.C do_level_hacks), and Tybal's guards act otherwise (critter/AI.C). Set when the orb is broken (obj/USEITEMS.C).</summary>
        public bool OrbDestroyed { get { return !uw2 && (R[0x60] & 0x20) != 0; } set { StoryBit(0x60, 0x20, value); } }

        /// <summary>UW1: the Key of Truth was given (byte 0x60 bit 6, player->key): the shrine mantra gives it only once (game/SKILLS.C).</summary>
        public bool KeyOfTruthGiven { get { return !uw2 && (R[0x60] & 0x40) != 0; } set { StoryBit(0x60, 0x40, value); } }

        /// <summary>UW1: the Cup of Wonder was found (byte 0x60 bit 7, player->cup): the flute's tune makes it only while clear (sound/SOUND.C play_cup_tune), and the shrine mantra pointing the way to it stops (game/SKILLS.C).</summary>
        public bool CupFound { get { return !uw2 && (R[0x60] & 0x80) != 0; } set { StoryBit(0x60, 0x80, value); } }

        /// <summary>UW1: Armageddon was cast (byte 0x60 bit 4, player->armageddon): every level arrived on is cleared of objects (game/GAMEWRAP.C do_level_hacks).</summary>
        public bool ArmageddonCast { get { return !uw2 && (R[0x60] & 0x10) != 0; } set { StoryBit(0x60, 0x10, value); } }

        /// <summary>UW1: the maze navigation spell (byte 0x62 bit 4, player->maze; game/PLAYDATA.C), put back on level 7 while the orb stands.</summary>
        public bool MazeSpell { get { return !uw2 && (R[0x62] & 0x10) != 0; } set { StoryBit(0x62, 0x10, value); } }

        /// <summary>UW1: the level the moonstone lies on, where Gate Travel goes (byte 0x5E bits 0-3, player->moonstone; combat/SPELLS.C): 2 at the start, 0 while carried (ui/INTERACT.C), the level it is put down on (inv/BAGS.C).</summary>
        public int MoonstoneLevel { get { return uw2 ? 0 : R[0x5E] & 0xF; } set { StoryNibble(0x5E, 0, value); } }

        /// <summary>UW1: the level the silver tree grows on, where a dead player comes back (byte 0x5E bits 4-7, player->tree; game/SKILLS.C): 0 for none; Armageddon clears it.</summary>
        public int SilverTreeLevel { get { return uw2 ? 0 : R[0x5E] >> 4; } set { StoryNibble(0x5E, 4, value); } }

        /// <summary>
        /// UW1: the dreams, a bit each (word 0x6E, player->dreams; game/SKILLS.C dream). Bits 0 and 1
        /// are set once the first two dreams have played; bits 2 and 3 are set by Tybal's death and by
        /// a talisman thrown before Garamon is buried, and cleared once that dream has played; 4-9 the
        /// random dreams seen. No dream plays once Garamon is buried.
        /// </summary>
        public int Dreams { get { return uw2 ? 0 : R[0x6E] | R[0x6F] << 8; } set { if (uw2) throw new InvalidOperationException("Ultima Underworld 1 only."); Word(0x6E, value); } }

        void StoryBit(int at, int mask, bool on)
        {
            if (uw2) throw new InvalidOperationException("Ultima Underworld 1 only.");
            R[at] = (byte)(on ? R[at] | mask : R[at] & ~mask);
            Dirty = true;
        }

        void StoryNibble(int at, int shift, int v)
        {
            if (uw2) throw new InvalidOperationException("Ultima Underworld 1 only.");
            if (v < 0 || v > 15) throw new ArgumentOutOfRangeException("value", "0-15");
            R[at] = (byte)((R[at] & ~(0xF << shift)) | (v << shift));
            Dirty = true;
        }

        int AppearanceAt { get { return uw2 ? 0x65 : 0x64; } }
        /// <summary>
        /// The hand bit (struct Player's "lefty"). Character creation stores the menu choice in it,
        /// and the two games list the choices the other way round (CHRGEN.DAT option 1, strings
        /// 11 and 12 of block 2): in UW1 it is set for a right-handed character, in UW2 for a
        /// left-handed one. HandNames reads the labels from the game.
        /// </summary>
        public bool HandBit { get { return (R[AppearanceAt] & 1) != 0; } set { Bits(0, 1, value ? 1 : 0); } }

        /// <summary>The character creation labels for the hand bit clear and set, from CHRGEN.DAT and STRINGS.PAK block 2.</summary>
        public static string[] HandNames(string dataFolder, GameStrings strings)
        {
            var names = new[] { "hand bit clear", "hand bit set" };
            try
            {
                string path = dataFolder == null ? null : GameProfile.Find(dataFolder, "CHRGEN.DAT");
                if (path == null || strings == null) return names;
                var d = File.ReadAllBytes(path);
                // 8 records of 0x12 bytes, then each option's zero-ended list of string numbers
                int p = 8 * 0x12, option = 0;
                var list = new List<int>();
                while (p + 1 < d.Length && option <= 1)
                {
                    int w = d[p] | d[p + 1] << 8;
                    p += 2;
                    if (w == 0) { if (option == 1) break; option++; list.Clear(); continue; }
                    if (option == 1) list.Add(w);
                }
                if (list.Count >= 2)
                {
                    string a = strings.Get(2, list[0]), b = strings.Get(2, list[1]);
                    if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)) names = new[] { a.Trim() + "-handed", b.Trim() + "-handed" };
                }
            }
            catch (Exception) { }
            return names;
        }
        public bool Female { get { return (R[AppearanceAt] & 2) != 0; } set { Bits(1, 1, value ? 1 : 0); } }
        public int Body { get { return (R[AppearanceAt] >> 2) & 7; } set { Bits(2, 3, value); } }
        public int Class { get { return (R[AppearanceAt] >> 5) & 7; } set { Bits(5, 3, value); } }
        /// <summary>The portrait: HEADS.GR picture female × 5 + body (conv/CONVERSE.C).</summary>
        public int Portrait { get { return (Female ? 5 : 0) + Body; } }

        void Set(int at, int v)
        {
            if (v < 0 || v > 255) throw new ArgumentOutOfRangeException("value", "a byte is 0-255");
            R[at] = (byte)v; Dirty = true;
        }

        void Word(int at, int v)
        {
            if (v < 0 || v > 0xFFFF) throw new ArgumentOutOfRangeException("value", "a word is 0-65535");
            R[at] = (byte)v; R[at + 1] = (byte)(v >> 8); Dirty = true;
        }

        void Bits(int shift, int width, int v)
        {
            int mask = (1 << width) - 1;
            if (v < 0 || v > mask) throw new ArgumentOutOfRangeException("value", "0-" + mask);
            int at = AppearanceAt;
            R[at] = (byte)((R[at] & ~(mask << shift)) | (v << shift));
            Dirty = true;
        }

        // ---- what the game derives

        /// <summary>player_compute (game/SKILLS.C): HP 30 + level × strength / 5.</summary>
        public int ExpectedMaxVitality { get { return 30 + Level * Strength / 5; } }
        /// <summary>player_compute: (mana skill + 1) × intelligence / 8.</summary>
        public int ExpectedMaxMana { get { return (Skill(7) + 1) * Intelligence >> 3; } }
        /// <summary>player_compute: carrying weight, UW1 strength × 20, UW2 strength × 13 + 300.</summary>
        public int ExpectedMaxWeight { get { return uw2 ? Strength * 13 + 300 : Strength * 20; } }
        /// <summary>The level the experience has earned (player_get_exp).</summary>
        public int ExpectedLevel
        {
            get
            {
                long points = ExperienceTenths / 500;
                int level = 1;
                while (level < 16 && LevelTable[level - 1] <= points) level++;
                return level;
            }
        }
        /// <summary>A skill point for every 300 experience (3000 tenths).</summary>
        public int ExpectedPointsEarned { get { return (int)Math.Min(255, ExperienceTenths / 3000); } }

        /// <summary>Sets the maxima the game derives (as player_compute would) from the attributes, level and mana skill.</summary>
        public void Recompute()
        {
            MaxVitality = Math.Min(255, ExpectedMaxVitality);
            MaxMana = Math.Min(255, ExpectedMaxMana);
            MaxWeight = ExpectedMaxWeight;
        }

        // ---- checks

        /// <summary>Everything about the record the game would notice, or could not have produced.</summary>
        public List<PlayerWarning> Check()
        {
            var w = new List<PlayerWarning>();
            Action<WarningLevel, string, string> add = (l, f, m) => w.Add(new PlayerWarning { Level = l, Field = f, Message = m });
            int sum = Strength + Dexterity + Intelligence;
            if (uw2 && sum > 64)
                add(WarningLevel.Detected, "Attributes", string.Format("Strength + dexterity + intelligence is {0}, over 64: the end-of-game screen adds \"AND CHEATED ON THEIR CHARACTER\" (game/SKILLS.C game_stats).", sum));
            else if (!uw2 && sum > 64)
                add(WarningLevel.Note, "Attributes", string.Format("Strength + dexterity + intelligence is {0}. UW1 does not check, but UW2 calls more than 64 cheating.", sum));
            foreach (var a in new[] { Tuple.Create("Strength", Strength), Tuple.Create("Dexterity", Dexterity), Tuple.Create("Intelligence", Intelligence) })
                if (a.Item2 > 30) add(WarningLevel.Unreachable, a.Item1, string.Format("{0} is {1}, but character creation stops attributes at 30, and nothing in the game raises them.", a.Item1, a.Item2));
            var cs = Class < Classes.Length ? Classes[Class] : null;
            if (cs != null)
            {
                string cname = ClassNames[Class];
                if (sum > cs.Total)
                    add(WarningLevel.Unreachable, "Attributes", string.Format("A {0} starts with attributes adding up to at most {1} ({2}/{3}/{4} and {5} bonus points, SKILLS.DAT); these add up to {6}.", cname, cs.Total, cs.Strength, cs.Dexterity, cs.Intelligence, cs.Bonus, sum));
                if (Strength < cs.Strength || Dexterity < cs.Dexterity || Intelligence < cs.Intelligence)
                    add(WarningLevel.Unreachable, "Attributes", string.Format("A {0} starts with at least {1} strength, {2} dexterity and {3} intelligence.", cname, cs.Strength, cs.Dexterity, cs.Intelligence));
            }
            for (int i = 0; i < 20; i++)
                if (Skill(i) > 30) add(WarningLevel.Unreachable, "Skills", string.Format("{0} is {1}: skills stop at 30.", PlayerDat.SkillNames[i], Skill(i)));
            if (Body > 4) add(WarningLevel.Unreachable, "Body", "Body is " + Body + ", but there are only five portraits (0-4) for each sex; HEADS.GR has no picture for this one.");
            if (Level < 1 || Level > 16) add(WarningLevel.Unreachable, "Level", "Levels run from 1 to 16.");
            else if (Level != ExpectedLevel)
                add(WarningLevel.Note, "Level", string.Format("Level is {0}, but the experience has earned level {1}. The game only raises the level as experience comes in, and never lowers it.", Level, ExpectedLevel));
            if (ExperienceTenths > 0x17700 + 0x7FFF)
                add(WarningLevel.Unreachable, "Experience", "Experience stops growing once it passes 9600 (player_get_exp).");
            if (SkillPointsEarned > ExpectedPointsEarned + 1)
                add(WarningLevel.Note, "Skill points", string.Format("{0} skill points are recorded as earned, but the experience gives {1}; the game gives no more until the experience catches up.", SkillPointsEarned, ExpectedPointsEarned));
            if (MaxVitality != Math.Min(255, ExpectedMaxVitality))
                add(WarningLevel.Note, "Max vitality", string.Format("Maximum vitality is {0}, but the game computes {1} (30 + level × strength / 5) at the next level gain.", MaxVitality, ExpectedMaxVitality));
            if (MaxMana != Math.Min(255, ExpectedMaxMana))
                add(WarningLevel.Note, "Max mana", string.Format("Maximum mana is {0}, but the game computes {1} ((mana skill + 1) × intelligence / 8) at the next level gain or mana skill increase.", MaxMana, ExpectedMaxMana));
            if (MaxWeight != ExpectedMaxWeight)
                add(WarningLevel.Note, "Max weight", string.Format("Carrying capacity is {0}, but the game computes {1} from strength.", MaxWeight, ExpectedMaxWeight));
            if (Vitality > MaxVitality) add(WarningLevel.Note, "Vitality", "Vitality is above its maximum; the game lets it stand until the player heals or is hurt.");
            if (Mana > MaxMana) add(WarningLevel.Note, "Mana", "Mana is above its maximum; it stays there until it is spent.");
            if (Vitality == 0) add(WarningLevel.Note, "Vitality", "Vitality is 0: the player dies at the first check.");
            string n = Name;
            if (n.Length == 0) add(WarningLevel.Unreachable, "Name", "The name cannot be empty: its first letter is the record's cipher key.");
            if (n.Length > 29) add(WarningLevel.Unreachable, "Name", "Character creation takes at most 29 letters.");
            foreach (var p in Inventory.Problems()) add(WarningLevel.Note, "Inventory", p);
            return w;
        }

        /// <summary>The file with the changes.</summary>
        public byte[] ToBytes()
        {
            Dat = Dat.WithRest(Inventory.Serialize(Dat.Rest));
            return Dat.ToBytes();
        }

        /// <summary>Writes PLAYER.DAT (the old file kept as PLAYER.DAT.BKP).</summary>
        public void Save(string path = null)
        {
            path = path ?? Path;
            var bytes = ToBytes();
            Conversations.SafeFile.Write(path, bytes);
            // read back what was written, so the open sheet is the file
            Dat = PlayerDat.Parse(bytes, Kind);
            Inventory = PlayerInventory.Parse(Dat.Rest);
            Dirty = false;
            Path = path;
        }
    }
}
