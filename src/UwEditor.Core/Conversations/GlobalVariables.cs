using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>One global a conversation can reach, with its value in the open game and who uses it.</summary>
    public sealed class GlobalVariable
    {
        public GlobalKind Kind;
        public int Number;
        /// <summary>For private globals, the conversation slot; -1 otherwise.</summary>
        public int Slot = -1;
        public string Name;
        public string Description;
        /// <summary>The value in PLAYER.DAT or BGLOBALS.DAT, or null when there is none (imported variables are the talker's and the player's live state).</summary>
        public int? Value;
        public int Min, Max;
        public bool Editable;
        public readonly List<GlobalUse> Uses = new List<GlobalUse>();

        public string KindName { get { return GlobalVariables.KindName(Kind); } }
        public string Label { get { return string.IsNullOrEmpty(Name) ? Number.ToString() : Number + "  " + Name; } }
        public IEnumerable<int> Readers { get { return Uses.Where(u => u.Access != GlobalAccess.Write).Select(u => u.Slot).Distinct().OrderBy(x => x); } }
        public IEnumerable<int> Writers { get { return Uses.Where(u => u.Access != GlobalAccess.Read).Select(u => u.Slot).Distinct().OrderBy(x => x); } }
        public override string ToString() { return KindName + " " + Label; }
    }

    /// <summary>
    /// The game state conversations read and write, both games: quest flags and quest
    /// bytes (get_quest, set_quest), the game variables traps share (x_traps), UW2's X clocks
    /// (x_clock), the skills (x_skills), the imported talker and player variables, and each
    /// conversation's private globals. Values come from the save's PLAYER.DAT and BGLOBALS.DAT
    /// (record offsets from include/player.h); with DATA open there is no player record (a new
    /// game starts with them at 0) and BABGLOBS.DAT gives the private globals' sizes, read only.
    /// </summary>
    public sealed class GlobalVariables
    {
        public GameKind Kind { get; private set; }
        PlayerDat player;
        UwEditor.Core.Player.PlayerSheet sheet;
        /// <summary>The player record: the shared sheet's when one was given (one copy of PLAYER.DAT for the whole editor), else this window's own.</summary>
        public PlayerDat Player { get { return sheet != null ? sheet.Dat : player; } }
        public string PlayerPath { get; private set; }
        public BGlobals Private { get; private set; }
        public string PrivatePath { get; private set; }
        /// <summary>Values can be changed and saved (a saved game is open).</summary>
        public bool CanEdit { get; private set; }
        public readonly List<GlobalVariable> All = new List<GlobalVariable>();
        public bool Dirty { get; private set; }
        bool uw2 { get { return Kind == GameKind.UW2; } }

        static readonly Dictionary<int, string> Uw1Quests = new Dictionary<int, string>
        {
            { 0, "Murgo freed" }, { 1, "talked to Hagbard" }, { 2, "met Dr. Owl" }, { 3, "may speak to King Ketchaval" },
            { 4, "Goldthirst's gazer killed" }, { 5, "Garamon: find the talismans" }, { 6, "friend of the lizardmen" },
            { 7, "conversation 24, Murgo" }, { 8, "Bronus's book for Morlock" }, { 9, "find Gurstang" },
            { 10, "where Zak is, for Delanrey" }, { 11, "Rodrick killed" },
            { 32, "Knight of the Crux (1 seek Dorna, 2 the writ of Lorne, 3 writ found, 4 armoury open)" },
            { 36, "talismans left (get_quest above 35 reads it)" },
        };
        static readonly Dictionary<int, string> Uw2Quests = new Dictionary<int, string>
        {
            { 128, "lines of power cut (a bit each)" }, { 129, "pit fights won-lost" }, { 130, "blackrock gems used (a bit each)" },
            { 133, "Jospur's debt (pit fights unpaid)" }, { 134, "Killorn's countdown" }, { 135, "bloodworms killed (sewers)" },
            { 141, "worlds visited (a bit each)" }, { 143, "cutscene to play after a conversation, plus one" },
            { 144, "player record byte 0xF6 (get_quest above 143 reads it)" },
        };
        static readonly string[] Uw2Clocks = { "time of day (0-71)", "castle plot", "Nystul and the gems", "djinn capture", "", "", "", "", "", "", "", "", "", "", "most kills in the pits", "changed (counts X clock events)" };
        static readonly Dictionary<int, string> Uw2Vars = new Dictionary<int, string>
        {
            { 0, "vending machine selection" }, { 1, "vending machine selection" }, { 2, "vending machine selection" },
            { 3, "vending machine selection" }, { 4, "vending machine selection" }, { 5, "vending machine selection" },
            { 6, "the last gem" }, { 100, "pyramid colour sequence" }, { 101, "pyramid colour sequence" }, { 102, "pyramid colour sequence" },
            { 103, "pyramid colour sequence" }, { 104, "pyramid colour sequence" }, { 105, "pyramid: done" }, { 106, "pyramid: last trigger tile" },
        };

        public static string KindName(GlobalKind k)
        {
            switch (k)
            {
                case GlobalKind.Quest: return "Quest";
                case GlobalKind.GameVariable: return "Game variable";
                case GlobalKind.XClock: return "X clock";
                case GlobalKind.Skill: return "Skill";
                case GlobalKind.Imported: return "Talker/player";
                default: return "Private";
            }
        }

        /// <summary>The variables of an open game, with their uses in the conversations (from GlobalUsage).</summary>
        public static GlobalVariables Load(GameSession s, ConversationArchive cnv, IEnumerable<GlobalUse> uses, Func<int, string> conversationName = null, UwEditor.Core.Player.PlayerSheet sheet = null)
        {
            var g = new GlobalVariables { Kind = s.Kind, sheet = sheet };
            // DATA's PLAYER.DAT is a 301-byte template, not a player record: a new game starts with these at 0
            g.PlayerPath = s.IsSave ? GameProfile.Find(s.SaveFolder, "PLAYER.DAT") : null;
            if (g.PlayerPath != null && sheet == null)
            {
                try { g.player = PlayerDat.Parse(File.ReadAllBytes(g.PlayerPath), s.Kind); } catch (Exception) { g.player = null; }
            }
            g.PrivatePath = s.IsSave ? GameProfile.Find(s.SaveFolder, "BGLOBALS.DAT") : GameProfile.Find(s.DataFolder, "BABGLOBS.DAT");
            if (g.PrivatePath != null)
            {
                try { g.Private = BGlobals.Parse(File.ReadAllBytes(g.PrivatePath), s.IsSave); } catch (Exception) { g.Private = null; }
            }
            g.CanEdit = s.IsSave;
            g.Build(s, cnv, conversationName);
            if (uses != null) g.Attach(uses);
            return g;
        }

        void Build(GameSession s, ConversationArchive cnv, Func<int, string> conversationName)
        {
            bool edit = CanEdit && Player != null;
            int quests = uw2 ? 128 : 32, bytes = uw2 ? 16 : 4;
            var qnames = uw2 ? Uw2Quests : Uw1Quests;
            for (int q = 0; q < quests + bytes; q++)
            {
                string n;
                qnames.TryGetValue(q, out n);
                bool bit = q < quests;
                All.Add(new GlobalVariable
                {
                    Kind = GlobalKind.Quest, Number = q, Name = n,
                    Description = bit ? (uw2 ? string.Format("Quest flag: bit {0} of the long at record 0x{1:X2} (numbered variable 0x{2:X3}).", q & 3, 0x66 + (q >> 2) * 4, 0x100 + q)
                                             : string.Format("Quest flag: bit {0} of the long at record 0x65.", q))
                                      : (uw2 ? string.Format("Quest byte at record 0x{0:X2} (numbered variable 0x{1:X3}).", 0xE6 + q - quests, 0x180 + q - quests)
                                             : string.Format("Quest byte at record 0x{0:X2}.", 0x69 + q - quests)),
                    Min = 0, Max = bit ? 1 : 255, Editable = edit,
                });
            }
            All.Add(new GlobalVariable
            {
                Kind = GlobalKind.Quest, Number = quests + bytes, Name = qnames[quests + bytes],
                Description = uw2 ? "get_quest of any number above 143 returns record byte 0xF6." : "get_quest of any number above 35 returns record byte 0x6D, the talismans left (0xFF once the last is gone).",
                Min = 0, Max = 255, Editable = edit,
            });
            int vars = uw2 ? 256 : 64;
            for (int v = 0; v < vars; v++)
            {
                string n = null;
                if (uw2) Uw2Vars.TryGetValue(v, out n);
                All.Add(new GlobalVariable
                {
                    Kind = GlobalKind.GameVariable, Number = v, Name = n,
                    Description = uw2 ? string.Format("Numbered variable {0} (record word 0x{1:X3}): set and check variable traps, schedules and x_traps.", v, 0xF9 + 2 * v)
                                      : string.Format("Game variable {0} (record byte 0x{1:X2}): set and check variable traps, and x_traps (6 bits, 0-63).", v, 0x70 + v),
                    Min = 0, Max = uw2 ? 65535 : 63, Editable = edit,
                });
            }
            if (uw2)
                for (int x = 0; x < 16; x++)
                    All.Add(new GlobalVariable
                    {
                        Kind = GlobalKind.XClock, Number = x, Name = Uw2Clocks[x].Length > 0 ? Uw2Clocks[x] : null,
                        Description = string.Format("X clock {0} (record byte 0x{1:X3}, numbered variable 0x{2:X3}); x_clock(n, v) sets it for v up to 0x100 and reads it above. Setting clock 0 moves the game clock.", x, 0x36D + x, 0x190 + x),
                        Min = 0, Max = 255, Editable = edit,
                    });
            for (int k = 0; k < 20; k++)
                All.Add(new GlobalVariable
                {
                    Kind = GlobalKind.Skill, Number = k, Name = k < PlayerDat.SkillNames.Length ? PlayerDat.SkillNames[k] : null,
                    Description = string.Format("Skill {0} (record byte 0x{1:X2}); x_skills(n, v) sets it for v 0-30, makes a skill check for 10000{2}, and reads it otherwise.", k, 0x21 + k, uw2 ? ", spends a skill point above 10000" : ""),
                    Min = 0, Max = 30, Editable = edit,
                });
            foreach (var iv in ImportedVariables.All)
                All.Add(new GlobalVariable
                {
                    Kind = GlobalKind.Imported, Number = 0, Name = iv.Name,
                    Description = "Copied in when a conversation starts: " + iv.Description + (iv.WrittenBack ? "; copied back when it ends." : "; not copied back."),
                });
            // private globals: each conversation's memory below babl_nvars that the game does not fill in
            if (cnv != null)
                foreach (int slot in cnv.Slots())
                {
                    Conversation c;
                    try { c = cnv.Read(slot); } catch (Exception) { continue; }
                    var rec = Private == null ? null : Private.Find(slot);
                    string cname = conversationName != null ? conversationName(slot) : null;
                    for (int a = 0; a < c.Variables; a++)
                    {
                        var im = c.VariableAt(a);
                        if (im != null && ImportedVariables.IsBound(im.Name, Kind)) continue;
                        All.Add(new GlobalVariable
                        {
                            Kind = GlobalKind.Private, Number = a, Slot = slot, Name = im != null ? im.Name : null,
                            Description = string.Format("Conversation {0}{1}'s own variable at address {2}, kept in BGLOBALS.DAT{3}.", slot, cname == null ? "" : " (" + cname + ")", a,
                                rec == null ? " (no record there)" : a < rec.Words.Length ? "" : " (beyond its record of " + rec.Words.Length + " words, so not kept)"),
                            Min = -32768, Max = 32767,
                            Editable = CanEdit && rec != null && a < rec.Words.Length,
                        });
                    }
                }
            foreach (var v in All) v.Value = Read(v);
        }

        void Attach(IEnumerable<GlobalUse> uses)
        {
            var map = new Dictionary<string, GlobalVariable>();
            foreach (var v in All) map[Key(v.Kind, v.Number, v.Slot, v.Name)] = v;
            int top = uw2 ? 144 : 36;
            foreach (var u in uses)
            {
                GlobalVariable v;
                int n = u.Kind == GlobalKind.Quest && u.Number > top ? top : u.Number;
                string key = u.Kind == GlobalKind.Imported ? Key(u.Kind, 0, -1, u.Name)
                    : u.Kind == GlobalKind.Private ? Key(u.Kind, u.Number, u.Slot, null)
                    : Key(u.Kind, n, -1, null);
                if (map.TryGetValue(key, out v)) v.Uses.Add(u);
            }
        }

        static string Key(GlobalKind k, int n, int slot, string name)
        {
            if (k == GlobalKind.Imported) return "I:" + name;
            if (k == GlobalKind.Private) return "P:" + slot + ":" + n;
            return k + ":" + n;
        }

        public GlobalVariable Find(GlobalKind k, int number, int slot = -1)
        {
            return All.FirstOrDefault(v => v.Kind == k && v.Number == number && (k != GlobalKind.Private || v.Slot == slot));
        }

        int? Read(GlobalVariable v)
        {
            if (v.Kind == GlobalKind.Private)
            {
                var rec = Private == null ? null : Private.Find(v.Slot);
                return rec != null && v.Number < rec.Words.Length ? rec.Words[v.Number] : (int?)null;
            }
            if (Player == null || v.Kind == GlobalKind.Imported) return null;
            int at, bit;
            if (!Locate(v, out at, out bit)) return null;
            var r = Player.Record;
            if (at < 0 || at >= r.Length) return null;
            if (bit >= 0) return (r[at] >> bit) & 1;
            if (v.Kind == GlobalKind.GameVariable && uw2) return r[at] | r[at + 1] << 8;
            return r[at];
        }

        /// <summary>Where a value lives in the player record: a byte (bit -1), or a bit of one.</summary>
        bool Locate(GlobalVariable v, out int at, out int bit)
        {
            at = -1; bit = -1;
            int quests = uw2 ? 128 : 32, bytes = uw2 ? 16 : 4;
            switch (v.Kind)
            {
                case GlobalKind.Quest:
                    if (v.Number < quests)
                    {
                        if (uw2) { at = 0x66 + (v.Number >> 2) * 4; bit = v.Number & 3; }
                        else { at = 0x65 + v.Number / 8; bit = v.Number % 8; }
                    }
                    else if (v.Number < quests + bytes) at = (uw2 ? 0xE6 : 0x69) + v.Number - quests;
                    else at = uw2 ? 0xF6 : 0x6D;
                    return true;
                case GlobalKind.GameVariable: at = uw2 ? 0xF9 + 2 * v.Number : 0x70 + v.Number; return true;
                case GlobalKind.XClock: at = 0x36D + v.Number; return uw2;
                case GlobalKind.Skill: at = 0x21 + v.Number; return true;
            }
            return false;
        }

        /// <summary>Changes a value (in memory until Save). Returns an error, or null.</summary>
        public string Set(GlobalVariable v, int value)
        {
            if (!v.Editable) return CanEdit ? v + " cannot be changed." : "Open a saved game (a SAVE folder) to change values; DATA holds a new game's.";
            if (value < v.Min || value > v.Max) return string.Format("{0} is {1}-{2}.", v, v.Min, v.Max);
            if (v.Kind == GlobalKind.Private)
            {
                var rec = Private.Find(v.Slot);
                rec.Words[v.Number] = value;
            }
            else
            {
                int at, bit;
                if (!Locate(v, out at, out bit)) return v + " is not in the player record.";
                var r = Player.Record;
                if (bit >= 0) r[at] = (byte)(value != 0 ? r[at] | 1 << bit : r[at] & ~(1 << bit));
                else if (v.Kind == GlobalKind.GameVariable && uw2) { r[at] = (byte)value; r[at + 1] = (byte)(value >> 8); }
                else r[at] = (byte)value;
                if (sheet != null) sheet.Dirty = true;
            }
            v.Value = value;
            Dirty = true;
            return null;
        }

        /// <summary>Writes PLAYER.DAT and BGLOBALS.DAT (each old file kept as .BKP).</summary>
        public List<string> Save()
        {
            var written = new List<string>();
            if (!CanEdit || !Dirty) return written;
            if (sheet != null) { if (sheet.Dirty) { sheet.Save(); written.Add(sheet.Path); } }
            else if (Player != null && PlayerPath != null) { SafeFile.Write(PlayerPath, Player.ToBytes()); written.Add(PlayerPath); }
            if (Private != null && PrivatePath != null) { SafeFile.Write(PrivatePath, Private.ToBytes(true)); written.Add(PrivatePath); }
            Dirty = false;
            return written;
        }
    }

    /// <summary>Every conversation's global uses, from decompiling each one.</summary>
    public static class GlobalUsage
    {
        public static List<GlobalUse> Collect(ConversationArchive cnv, GameStrings strings, GameKind kind)
        {
            var all = new List<GlobalUse>();
            foreach (int slot in cnv.Slots())
            {
                try
                {
                    var c = cnv.Read(slot);
                    var d = Decompiler.Decompile(c, strings == null ? null : strings.Block(c.StringBlock), kind);
                    all.AddRange(d.Globals);
                }
                catch (Exception) { }
            }
            return all;
        }
    }
}
