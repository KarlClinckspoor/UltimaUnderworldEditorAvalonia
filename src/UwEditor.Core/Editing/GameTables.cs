using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Conversations;
using UwEditor.Core.Player;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// A small table of the game's DATA folder (TERRAIN.DAT, SKILLS.DAT, CMB.DAT, SHADES.DAT,
    /// DL.DAT), edited in memory and written with SafeFile. The file as read (or last saved)
    /// is kept, so each value can say whether it changed and Revert can put it back. These
    /// files are the game's own: a saved game has no copy, so the DATA folder's is edited.
    /// </summary>
    public abstract class DataTable
    {
        public GameSession Session { get; private set; }
        public string FileName { get; private set; }
        /// <summary>The file in DATA, or null when the game has none.</summary>
        public string Path { get; private set; }
        public bool Exists { get { return Original != null; } }
        /// <summary>The file as read or last saved.</summary>
        protected byte[] Original { get; private set; }

        /// <summary>Reads the file; present false: the game has no such file (UW1's DL.DAT).</summary>
        protected void Init(GameSession s, string name, bool present = true)
        {
            Session = s;
            FileName = name;
            Path = present ? GameProfile.Find(s.DataFolder, name) : null;
            Original = Path != null ? File.ReadAllBytes(Path) : null;
            if (Original != null) Read(Original);
        }

        /// <summary>Sets the values from the file's bytes.</summary>
        protected abstract void Read(byte[] bytes);

        /// <summary>The file with the values as they now are.</summary>
        public abstract byte[] ToBytes();

        /// <summary>The bytes as read or last saved (a copy).</summary>
        public byte[] OriginalBytes { get { return Original == null ? null : (byte[])Original.Clone(); } }

        public bool HasChanges
        {
            get
            {
                if (Original == null) return false;
                try { return !ToBytes().SequenceEqual(Original); }
                catch (InvalidDataException) { return true; }
            }
        }

        /// <summary>Back to the file as read or last saved.</summary>
        public void Revert() { if (Original != null) Read(Original); }

        /// <summary>Writes the file (SafeFile: NAME.EXT.ORIG once, NAME.EXT.BKP each time).</summary>
        public void Save()
        {
            if (Path == null) throw new InvalidOperationException(FileName + " is not in the DATA folder.");
            var b = ToBytes();
            SafeFile.Write(Path, b);
            Original = b;
            Read(b);
        }

        protected static int Word(byte[] b, int i) { return 2 * i + 1 < b.Length ? b[2 * i] | b[2 * i + 1] << 8 : 0; }
    }

    /// <summary>
    /// DATA\TERRAIN.DAT: a word for each texture (map/TEXTMAPS.C Load_Terrains). UW1: walls
    /// (W64.TR) from offset 0 and floors (F32.TR) from 0x200, 1024 bytes; the values are the
    /// types of uw1 include/map.h (TERR_*): wall 2 ankh to 0xB textured door, floor 0x10
    /// water and 0x20 lava; a floor's class is the word shifted right 4 (critter/PATHFIND.C,
    /// motion/COLLIDE.C), so 0x30 would be ice, which UW1's floors never use. UW2: one word for
    /// each of T64.TR's first 256 textures (it has 264; the game reads past the file for the
    /// rest and leaves them 0): bits 6-7 the class, 1 water, 2 lava, 3 ice (uw2 include/map.h
    /// TERR_CLASS); bits 3-5 a water current or the ice's grip (motion/PHYSICS.C); bits 0-2
    /// the wall's kind, 3 and 4 stairs (3d/DRAWOBJ.C, ui/AUTOMAP.C), 5 a shaft (obj/LOOK.C).
    /// </summary>
    public sealed class TerrainTable : DataTable
    {
        ushort[] words = new ushort[0];

        public static TerrainTable Load(GameSession s)
        {
            var t = new TerrainTable();
            t.Init(s, "TERRAIN.DAT");
            return t;
        }

        protected override void Read(byte[] b)
        {
            words = new ushort[b.Length / 2];
            for (int i = 0; i < words.Length; i++) words[i] = (ushort)Word(b, i);
        }

        public override byte[] ToBytes()
        {
            var b = (byte[])Original.Clone();
            for (int i = 0; i < words.Length; i++) { b[2 * i] = (byte)words[i]; b[2 * i + 1] = (byte)(words[i] >> 8); }
            return b;
        }

        /// <summary>The words in the file.</summary>
        public int Count { get { return words.Length; } }

        /// <summary>The word of a texture: a wall or floor of the texture file (UW2 has one file and one table).</summary>
        public int EntryOf(int texture, bool floor) { return (Session.IsUw2 || !floor ? 0 : 0x100) + texture; }

        /// <summary>How many textures of the wall or floor file have a word in TERRAIN.DAT.</summary>
        public int Textures(bool floor)
        {
            var g = Session.Graphics;
            int n = floor ? g.FloorTextures.Count : g.WallTextures.Count;
            int room = Session.IsUw2 ? Count : Math.Max(0, Math.Min(0x100, Count - (floor ? 0x100 : 0)));
            return Math.Min(n, room);
        }

        public int Get(int entry) { return entry >= 0 && entry < words.Length ? words[entry] : 0; }

        public void Set(int entry, int value)
        {
            if (entry < 0 || entry >= words.Length) throw new ArgumentOutOfRangeException(nameof(entry), "TERRAIN.DAT has words 0-" + (words.Length - 1) + ".");
            if (value < 0 || value > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(value), "A terrain word is 0-0xFFFF.");
            words[entry] = (ushort)value;
        }

        public bool IsChanged(int entry) { return Original != null && Word(Original, entry) != Get(entry); }
        public int OriginalValue(int entry) { return Original == null ? 0 : Word(Original, entry); }

        /// <summary>UW1's wall types (uw1 include/map.h TERR_*; CONVERSE.C, LOOK.C and USEITEMS.C test 8, 9 and 0xB).</summary>
        public static readonly KeyValuePair<int, string>[] Uw1Walls =
        {
            Pair(0x00, "normal"), Pair(0x02, "ankh (shrine)"), Pair(0x03, "stairs up"), Pair(0x04, "stairs down"), Pair(0x05, "pipe"),
            Pair(0x06, "grating"), Pair(0x07, "drain"), Pair(0x08, "chained princess"), Pair(0x09, "window"), Pair(0x0A, "tapestry"), Pair(0x0B, "textured door"),
        };

        /// <summary>UW1's floor types: the class is the word shifted right 4 (PATHFIND.C hyp_move, WORLDEV.C, COLLIDE.C TILE_TERR).</summary>
        public static readonly KeyValuePair<int, string>[] Uw1Floors =
        {
            Pair(0x00, "normal"), Pair(0x10, "water"), Pair(0x20, "lava"), Pair(0x30, "ice (the engine knows it; UW1 never uses it)"),
        };

        /// <summary>UW2's classes, bits 6-7 (uw2 include/map.h TERR_CLASS; Underworld Adventures' format document 4.7).</summary>
        public static readonly string[] Uw2Classes = { "normal", "water", "lava", "ice" };

        /// <summary>UW2's bits 0-2, the wall's kind: 3 and 4 draw and map as stairs (3d/DRAWOBJ.C, ui/AUTOMAP.C), 5 is a shaft to look down (obj/LOOK.C).</summary>
        public static string Uw2KindName(int k)
        {
            switch (k)
            {
                case 0: return "plain";
                case 3: return "stairs up";
                case 4: return "stairs down";
                case 5: return "shaft";
                default: return "kind " + k + " (not used by the game)";
            }
        }

        /// <summary>
        /// UW2's bits 3-5. Swimming (motion/PHYSICS.C, motion state 1): a current, value - 1 being
        /// the slope it flows like (0 slope north: to the south; 1 to the north; 2 to the west;
        /// 3 to the east). On ice (motion state 4): 7 grips, 0-6 slide (PN.bounce = 15 - value).
        /// </summary>
        public static string Uw2SubName(int cls, int sub)
        {
            if (cls == 1)
            {
                switch (sub)
                {
                    case 0: return "no current";
                    case 1: return "current to the south";
                    case 2: return "current to the north";
                    case 3: return "current to the west";
                    case 4: return "current to the east";
                    default: return "current " + sub + " (none)";
                }
            }
            if (cls == 3) return sub == 7 ? "grips (not slippery)" : "slippery " + sub + " (0-6)";
            return sub == 0 ? "-" : "extra bits " + sub + " (unused here)";
        }

        public static int Uw2Class(int v) { return (v >> 6) & 3; }
        public static int Uw2Sub(int v) { return (v >> 3) & 7; }
        public static int Uw2Kind(int v) { return v & 7; }
        public static int Uw2Compose(int keep, int cls, int sub, int kind) { return (keep & ~0xFF) | (cls & 3) << 6 | (sub & 7) << 3 | (kind & 7); }

        /// <summary>A terrain word in words.</summary>
        public static string Describe(GameKind kind, bool floor, int v)
        {
            if (kind == GameKind.UW2)
            {
                int c = Uw2Class(v), s = Uw2Sub(v), k = Uw2Kind(v);
                var parts = new List<string> { Uw2Classes[c] };
                if (c == 1 || c == 3 || s != 0) parts.Add(Uw2SubName(c, s));
                if (k != 0) parts.Add(Uw2KindName(k));
                if ((v & ~0xFF) != 0) parts.Add(string.Format("high byte 0x{0:X2}", v >> 8));
                return string.Join(", ", parts);
            }
            foreach (var p in floor ? Uw1Floors : Uw1Walls) if (p.Key == v) return p.Value;
            return string.Format("0x{0:X} (not a known type)", v);
        }

        static KeyValuePair<int, string> Pair(int k, string v) { return new KeyValuePair<int, string>(k, v); }
    }

    /// <summary>
    /// DATA\SKILLS.DAT (Formats' SkillsDat): each class's attributes and bonus points, then
    /// five skill entries each, a count and that many skills (game/CHARGEN.C, both games): an
    /// empty entry gives nothing, a single skill is given, several make a menu to pick one
    /// from. The skills' names are block 2 strings 0x1F on (CHARGEN.C set_sklmnu).
    /// </summary>
    public sealed class SkillsTable : DataTable
    {
        public SkillsDat Dat { get; private set; }

        public static SkillsTable Load(GameSession s)
        {
            var t = new SkillsTable();
            t.Init(s, "SKILLS.DAT");
            return t;
        }

        protected override void Read(byte[] b) { Dat = SkillsDat.Parse(b); }
        public override byte[] ToBytes() { return Dat.ToBytes(); }

        SkillsDat Orig { get { return SkillsDat.Parse(Original); } }

        public static string ClassName(int c) { return c >= 0 && c < PlayerSheet.ClassNames.Length ? PlayerSheet.ClassNames[c] : "class " + c; }

        /// <summary>The skill's name as the game shows it (block 2, 0x1F + skill), or the usual English one.</summary>
        public string SkillName(int skill)
        {
            string s = Session.Strings.Get(2, 0x1F + skill);
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            return skill >= 0 && skill < PlayerDat.SkillNames.Length ? PlayerDat.SkillNames[skill] : "skill " + skill;
        }

        /// <summary>Attribute a: 0 strength, 1 dexterity, 2 intelligence, 3 bonus points.</summary>
        public int Attribute(int cls, int a) { return Pick(Dat.Entries[cls], a); }
        public bool AttributeChanged(int cls, int a) { return Pick(Orig.Entries[cls], a) != Attribute(cls, a); }

        public void SetAttribute(int cls, int a, int v)
        {
            if (v < 0 || v > 255) throw new ArgumentOutOfRangeException(nameof(v), "0-255.");
            var e = Dat.Entries[cls];
            switch (a) { case 0: e.Strength = v; break; case 1: e.Dexterity = v; break; case 2: e.Intelligence = v; break; default: e.BonusPoints = v; break; }
        }

        static int Pick(SkillsDat.ClassEntry e, int a) { return a == 0 ? e.Strength : a == 1 ? e.Dexterity : a == 2 ? e.Intelligence : e.BonusPoints; }

        public IReadOnlyList<int> Choice(int cls, int entry) { return Dat.Entries[cls].Choices[entry]; }
        public bool ChoiceChanged(int cls, int entry) { return !Orig.Entries[cls].Choices[entry].SequenceEqual(Choice(cls, entry)); }

        /// <summary>Adds or removes a skill of an entry (kept in the file's order).</summary>
        public void Toggle(int cls, int entry, int skill, bool on)
        {
            if (skill < 0 || skill >= SkillsDat.Skills) throw new ArgumentOutOfRangeException(nameof(skill), "Skills are 0-19.");
            var c = Dat.Entries[cls].Choices[entry];
            if (on && !c.Contains(skill)) c.Add(skill);
            if (!on) c.Remove(skill);
        }

        public void SetChoice(int cls, int entry, IEnumerable<int> skills)
        {
            var c = Dat.Entries[cls].Choices[entry];
            c.Clear();
            foreach (int s in skills) if (s >= 0 && s < SkillsDat.Skills && !c.Contains(s)) c.Add(s);
        }

        /// <summary>What the game makes of an entry.</summary>
        public string Explain(int cls, int entry)
        {
            var c = Choice(cls, entry);
            if (c.Count == 0) return "nothing";
            if (c.Count == 1) return "gives " + SkillName(c[0]);
            return "pick one of " + string.Join(", ", c.Select(SkillName));
        }

        /// <summary>Things the game would do differently from what the table seems to say.</summary>
        public List<string> Check()
        {
            var l = new List<string>();
            for (int c = 0; c < SkillsDat.Classes; c++)
            {
                for (int a = 0; a < 3; a++)
                    if (Attribute(c, a) > 30) l.Add(string.Format("{0}: {1} is {2}; the bonus points never take an attribute above 30, but the base is used as it is.", ClassName(c), new[] { "strength", "dexterity", "intelligence" }[a], Attribute(c, a)));
                // set_sklmnu does not step past an empty entry, so the entries after it in the same pass read it again
                for (int k = 0; k < SkillsDat.EntriesPerClass - 1; k++)
                    if (Choice(c, k).Count == 0 && Enumerable.Range(k + 1, SkillsDat.EntriesPerClass - k - 1).Any(j => Choice(c, j).Count > 0))
                    {
                        l.Add(string.Format("{0}: entry {1} is empty, so the entries after it give nothing (CHARGEN.C set_sklmnu does not step past an empty entry).", ClassName(c), k + 1));
                        break;
                    }
            }
            int size = 0;
            try { size = ToBytes().Length; } catch (InvalidDataException e) { l.Add(e.Message); }
            if (size > 0x348) l.Add("The file is " + size + " bytes; the game reads at most 0x348.");
            return l;
        }
    }

    /// <summary>
    /// DATA\CMB.DAT (Formats' CmbDat): ten rules of three words, the first and second source
    /// and the output (obj/COMBINE.C, both games). The low nine bits name an item; bit 15 of a
    /// source says it is used up. UW1 only takes a rule with a source used up and has nine rules
    /// and a tenth of zeros; UW2 takes any rule and fills its five unused ones with 0xFFFF.
    /// The first rule that names both items, in either order, is the one used.
    /// </summary>
    public sealed class CombinationTable : DataTable
    {
        public CmbDat Dat { get; private set; }
        byte[] tail = new byte[0];

        public static CombinationTable Load(GameSession s)
        {
            var t = new CombinationTable();
            t.Init(s, "CMB.DAT");
            return t;
        }

        protected override void Read(byte[] b)
        {
            Dat = CmbDat.Parse(b);
            tail = b.Length > 60 ? b.Skip(60).ToArray() : new byte[0];
        }

        public override byte[] ToBytes()
        {
            var b = Dat.ToBytes();
            // a shorter file is read as far as it goes (bltfromdrive); keep its length
            if (Original != null && Original.Length < b.Length) return b.Take(Original.Length).ToArray();
            return b.Concat(tail).ToArray();
        }

        /// <summary>The raw word: which 0 first source, 1 second, 2 output.</summary>
        public int Raw(int rule, int which)
        {
            var r = Dat.Entries[rule];
            return which == 0 ? r.First : which == 1 ? r.Second : r.Output;
        }

        public bool Changed(int rule, int which) { return Original != null && Word(Original, rule * 3 + which) != Raw(rule, which); }
        public bool RuleChanged(int rule) { return Changed(rule, 0) || Changed(rule, 1) || Changed(rule, 2); }

        void SetRaw(int rule, int which, int v)
        {
            var r = Dat.Entries[rule];
            if (which == 0) r.First = v & 0xFFFF; else if (which == 1) r.Second = v & 0xFFFF; else r.Output = v & 0xFFFF;
            Dat.Entries[rule] = r;
        }

        public int Item(int rule, int which) { return Raw(rule, which) & 0x1FF; }
        public bool UsedUp(int rule, int which) { return which < 2 && (Raw(rule, which) & CmbDat.UsedUp) != 0; }

        /// <summary>Sets the item of a word, its other bits kept.</summary>
        public void SetItem(int rule, int which, int item)
        {
            if (item < 0 || item > 0x1FF) throw new ArgumentOutOfRangeException(nameof(item), "Items are 0-0x1FF.");
            SetRaw(rule, which, (Raw(rule, which) & ~0x1FF) | item);
        }

        public void SetUsedUp(int rule, int which, bool on)
        {
            if (which > 1) throw new ArgumentOutOfRangeException(nameof(which), "Only a source is used up.");
            SetRaw(rule, which, on ? Raw(rule, which) | CmbDat.UsedUp : Raw(rule, which) & ~CmbDat.UsedUp);
        }

        /// <summary>The unused value of a word: UW2's 0xFFFF, UW1's 0.</summary>
        public int EmptyWord { get { return Session.IsUw2 ? 0xFFFF : 0; } }

        public bool IsEmpty(int rule)
        {
            int e = EmptyWord;
            return Raw(rule, 0) == e && Raw(rule, 1) == e && Raw(rule, 2) == e;
        }

        /// <summary>Makes the rule unused (UW2 0xFFFF, UW1 zeros).</summary>
        public void Clear(int rule) { for (int w = 0; w < 3; w++) SetRaw(rule, w, EmptyWord); }

        /// <summary>A new rule in an empty slot: both sources used up, the output a hand axe until chosen.</summary>
        public void MakeRule(int rule, int first, int second, int output)
        {
            SetRaw(rule, 0, (first & 0x1FF) | CmbDat.UsedUp);
            SetRaw(rule, 1, (second & 0x1FF) | CmbDat.UsedUp);
            SetRaw(rule, 2, output & 0x1FF);
        }

        /// <summary>Whether the game takes the rule at all (UW1 ObjsBeCombinable needs a source used up).</summary>
        public bool Active(int rule)
        {
            if (IsEmpty(rule)) return false;
            return Session.IsUw2 || UsedUp(rule, 0) || UsedUp(rule, 1);
        }

        public List<string> Check()
        {
            var l = new List<string>();
            for (int i = 0; i < CmbDat.Rules; i++)
            {
                if (IsEmpty(i)) continue;
                if (!Session.IsUw2 && !UsedUp(i, 0) && !UsedUp(i, 1)) l.Add(string.Format("Rule {0}: neither source is used up, so the game ignores it.", i + 1));
                if (Session.IsUw2 && !UsedUp(i, 0) && !UsedUp(i, 1)) l.Add(string.Format("Rule {0}: neither source is used up, so it can be repeated without end.", i + 1));
                for (int j = 0; j < i; j++)
                {
                    if (!Active(j)) continue;
                    if ((Item(j, 0) == Item(i, 0) && Item(j, 1) == Item(i, 1)) || (Item(j, 0) == Item(i, 1) && Item(j, 1) == Item(i, 0)))
                    {
                        l.Add(string.Format("Rule {0} names the same items as rule {1}; only rule {1} is used.", i + 1, j + 1));
                        break;
                    }
                }
            }
            return l;
        }
    }

    /// <summary>
    /// DATA\SHADES.DAT (Formats' ShadesDat; map/LIGHTING.C, 3d/VIEW3D.C preset_grid): for each
    /// of the eight light levels six int16, smooth_div, smooth_base, smooth_lowpass, curvrad
    /// (the range in squares), distpoly and dist8. The same 96 bytes in both games.
    /// </summary>
    public sealed class ShadesTable : DataTable
    {
        public ShadesDat Dat { get; private set; }
        byte[] tail = new byte[0];

        /// <summary>Each column's range in the editor: what the game can use (preset_grid needs a range under 16; shades are 0-15).</summary>
        public static readonly int[] Min = { 1, 0, -15, 0, 0, 0 }, Max = { 255, 15, 15, 15, 16, 16 };

        public static ShadesTable Load(GameSession s)
        {
            var t = new ShadesTable();
            t.Init(s, "SHADES.DAT");
            return t;
        }

        protected override void Read(byte[] b)
        {
            Dat = ShadesDat.Parse(b);
            tail = b.Length > 96 ? b.Skip(96).ToArray() : new byte[0];
        }

        public override byte[] ToBytes() { return Dat.ToBytes().Concat(tail).ToArray(); }

        public int Get(int row, int col) { return Dat.Rows[row, col]; }

        public void Set(int row, int col, int v)
        {
            if (v < Min[col] || v > Max[col]) throw new ArgumentOutOfRangeException(nameof(v), ShadesDat.Columns[col] + " is " + Min[col] + "-" + Max[col] + ".");
            Dat.Rows[row, col] = (short)v;
        }

        public bool IsChanged(int row, int col) { return Original != null && (short)Word(Original, row * 6 + col) != Get(row, col); }
    }

    /// <summary>
    /// UW2's DATA\DL.DAT: a byte for each of the 80 levels (game/PLAYDATA.C load_dl). The value
    /// mod 10 is a light level, the level's minimum light; it applies on the squares whose
    /// light flag (bit 8 of the tile) differs from (value ≥ 10): value 10 and up on squares
    /// without the flag, 1-9 on squares with it (motion/PHYSICS.C change_GrSq:
    /// (flag ^ light_hi) ? light_mod : none). A minimum of 0 does nothing.
    /// </summary>
    public sealed class MinimumLightTable : DataTable
    {
        byte[] values = new byte[0];

        public static MinimumLightTable Load(GameSession s)
        {
            var t = new MinimumLightTable();
            t.Init(s, "DL.DAT", s.IsUw2);
            return t;
        }

        protected override void Read(byte[] b) { values = (byte[])b.Clone(); }
        public override byte[] ToBytes() { return (byte[])values.Clone(); }

        public int Count { get { return values.Length; } }
        public int Raw(int level) { return level >= 1 && level <= values.Length ? values[level - 1] : 0; }
        public int Minimum(int level) { return Raw(level) % 10; }
        /// <summary>True: on squares without the light flag (value 10 and up); false: on flagged squares.</summary>
        public bool OnUnflagged(int level) { return Raw(level) >= 10; }
        public bool IsChanged(int level) { return Original != null && level >= 1 && level <= Original.Length && Original[level - 1] != Raw(level); }

        /// <summary>Sets the minimum (0-9) and where it applies; a value of 20 and up becomes 10 + minimum.</summary>
        public void Set(int level, int minimum, bool onUnflagged)
        {
            if (level < 1 || level > values.Length) throw new ArgumentOutOfRangeException(nameof(level), "DL.DAT has levels 1-" + values.Length + ".");
            if (minimum < 0 || minimum > 9) throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum light is 0-9.");
            int v = minimum + (onUnflagged ? 10 : 0);
            if (Raw(level) % 10 == minimum && (Raw(level) >= 10) == onUnflagged) return;      // unchanged: keep the byte as it is
            values[level - 1] = (byte)v;
        }

        /// <summary>What the game does on the level.</summary>
        public string Explain(int level)
        {
            int min = Minimum(level);
            if (min == 0) return "no minimum: only carried light counts";
            return OnUnflagged(level)
                ? string.Format("light {0} at least on squares without the light flag; flagged squares are dark without a light", min)
                : string.Format("light {0} at least on squares with the light flag; elsewhere only carried light counts", min);
        }
    }

    /// <summary>
    /// A level's texture map (map/TEXTMAPS.C; LevelModel.ReadTextureMap): UW1 block 0x11 +
    /// level, 48 wall words (W64.TR), 10 floor words (F32.TR) and the six door bytes at 0x74
    /// (DOORS.GR); UW2 block 0x4F + level, 64 words of T64.TR (the first 16 are the floors too:
    /// a tile's floor index is 4 bits) and the six door bytes at 0x80. Changes go through
    /// LevelEditor.EditBytes, so they undo and are saved with the levels.
    /// </summary>
    public static class TextureMap
    {
        public enum Kind { Wall, Floor, Door }

        /// <summary>The slots of a kind: UW1 48 walls, 10 floors; UW2 64 textures (no separate floors); 6 doors.</summary>
        public static int Slots(GameSession s, Kind k)
        {
            if (k == Kind.Door) return 6;
            if (s.IsUw2) return k == Kind.Wall ? 64 : 0;
            return k == Kind.Wall ? 48 : 10;
        }

        /// <summary>The byte offset of a slot in the texture map block.</summary>
        public static int Offset(GameSession s, Kind k, int slot)
        {
            if (k == Kind.Door) return (s.IsUw2 ? 0x80 : 0x74) + slot;
            return 2 * ((k == Kind.Floor ? 48 : 0) + slot);
        }

        /// <summary>The textures there are to choose from: the wall or floor file, or DOORS.GR.</summary>
        public static int Choices(GameSession s, Kind k)
        {
            var g = s.Graphics;
            return k == Kind.Door ? g.Doors.Count : k == Kind.Floor ? g.FloorTextures.Count : g.WallTextures.Count;
        }

        public static int Get(GameSession s, LevelModel m, Kind k, int slot)
        {
            if (k == Kind.Door) return m.DoorMap[slot];
            return k == Kind.Floor ? m.FloorMap[slot] : m.WallMap[slot];
        }

        /// <summary>The block a level with none would have (each index its own texture, init_txtlib).</summary>
        public static byte[] Identity(GameSession s)
        {
            var b = new byte[s.Profile.TextureBlockSize];
            int words = s.IsUw2 ? 64 : 58;
            for (int i = 0; i < words; i++) { int v = s.IsUw2 || i < 48 ? i : i - 48; b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(v >> 8); }
            for (int i = 0; i < 6; i++) b[(s.IsUw2 ? 0x80 : 0x74) + i] = (byte)i;
            return b;
        }

        /// <summary>Points a slot at another texture, as one undoable step of the level.</summary>
        public static void Set(LevelEditor e, Kind k, int slot, int texture)
        {
            var s = e.Session;
            if (slot < 0 || slot >= Slots(s, k)) throw new ArgumentOutOfRangeException(nameof(slot), "The level has " + k.ToString().ToLowerInvariant() + " slots 0-" + (Slots(s, k) - 1) + ".");
            int n = Choices(s, k);
            if (texture < 0 || texture >= n) throw new ArgumentOutOfRangeException(nameof(texture), "There are textures 0-" + (n - 1) + ".");
            if (Get(s, e.Model, k, slot) == texture) return;
            string label = string.Format("{0} {1}: texture {2}", k == Kind.Door ? "Door" : k == Kind.Floor ? "Floor" : s.IsUw2 && slot < 16 ? "Wall/floor" : "Wall", slot, texture);
            int at = Offset(s, k, slot);
            if (e.TextureBytes == null || e.TextureBytes.Length < s.Profile.TextureBlockSize)
            {
                // no block yet (or a short one): start from the identity map, the old bytes kept
                var tex = Identity(s);
                if (e.TextureBytes != null) Array.Copy(e.TextureBytes, tex, e.TextureBytes.Length);
                Put(tex, k, at, texture);
                e.Replace(e.Data, tex, label);
                return;
            }
            e.EditBytes(label, (data, tex) => Put(tex, k, at, texture));
        }

        static void Put(byte[] tex, Kind k, int at, int texture)
        {
            if (k == Kind.Door) { tex[at] = (byte)texture; return; }
            tex[at] = (byte)texture; tex[at + 1] = (byte)(texture >> 8);
        }

        /// <summary>How many open tiles of the level use a slot: for their walls (the editor and the game draw a tile's walls with its own wall index) or their floor.</summary>
        public static int Uses(LevelModel m, Kind k, int slot)
        {
            if (k == Kind.Door || !m.Present) return 0;
            int n = 0;
            bool uw2 = m.Session.IsUw2;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = m.Tiles[x, y];
                    if (t.IsSolid) continue;
                    if ((k == Kind.Floor || uw2) && t.FloorIndex == slot) n++;
                    else if (k == Kind.Wall && t.WallIndex == slot) n++;
                }
            return n;
        }
    }
}
