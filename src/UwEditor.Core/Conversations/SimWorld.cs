using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// An object the simulator knows: one of the talker's or the player's items, or one a
    /// script made. The fields are the ones the conversation built-ins read and write
    /// (conv/CONVERSE.C, conv/BABLHACK.C, conv/BARTER.C).
    /// </summary>
    public sealed class SimObject
    {
        /// <summary>The object index the script sees (a level object's index when it came from the level).</summary>
        public int Index;
        public int Item;
        public int Quality = 40;
        public bool IsQuantity;
        /// <summary>The link field: a quantity for a quantity object (below 0x200), else a link.</summary>
        public int Link;
        public int Owner;
        public int Flags;
        public int Heading;
        public int FineX, FineY, Z;
        public bool Flag9, Flag10;
        /// <summary>COMOBJ.DAT's value of the item, for identify_inv.</summary>
        public int Value;
        /// <summary>What it holds (a container's contents), by index.</summary>
        public List<int> Contents = new List<int>();

        /// <summary>How many: the link of a quantity object, else 1 (count_inv).</summary>
        public int Count { get { return IsQuantity && Link < 0x200 ? Link : 1; } }
        public int Major { get { return Item >> 6; } }
        public int Minor { get { return (Item >> 4) & 3; } }
        /// <summary>OBJ_CLASS: the item's group of 16.</summary>
        public int Class { get { return Item >> 4; } }

        public SimObject Clone()
        {
            var o = (SimObject)MemberwiseClone();
            o.Contents = new List<int>(Contents);
            return o;
        }

        public override string ToString() { return "#" + Index + " item " + Item + (Count > 1 ? " x" + Count : "") + " q" + Quality; }
    }

    /// <summary>
    /// What only the game knows when a conversation runs, for the simulator: the player
    /// (name, sex, skills, experience), the quest flags and bytes, the game variables and
    /// UW2's X clocks, the talker's and the player's state that the game copies into the
    /// imported variables (conv/CONVVARS.C setup_converse_data), the inventories and trade
    /// slots, how random rolls come out and how trades end. Filled in from the open game
    /// (PLAYER.DAT of a save, the talker's level object) or from defaults. A plain copy: the
    /// simulator never writes it back to the game.
    /// </summary>
    public sealed class SimWorld
    {
        public GameKind Kind;
        public bool Uw2 { get { return Kind == GameKind.UW2; } }
        public string PlayerName = "Avatar";
        public bool PlayerFemale;
        /// <summary>The quest flags: UW1 one long (bits 0-31), UW2 32 longs of four flags each (player.h quests).</summary>
        public uint[] QuestWords;
        /// <summary>The quest bytes: UW1 quests 32-35, UW2 quests 128-143.</summary>
        public int[] QuestBytes;
        /// <summary>What get_quest returns above the last quest: UW1 the talismans byte (0x6D), UW2 record byte 0xF6.</summary>
        public int QuestTop;
        /// <summary>The game variables traps and x_traps share: UW1 64 bytes, UW2 256 numbered variables.</summary>
        public int[] GameVariables;
        /// <summary>UW2's X clocks (x_clock); unused in UW1.</summary>
        public int[] XClocks = new int[16];
        public int[] Skills = new int[20];
        public int SkillPoints;
        public long Experience;
        /// <summary>The values the game copies into the imported variables, by name (ImportedVariables).</summary>
        public Dictionary<string, int> Imported = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The seed of the simulator's rand().</summary>
        public int RandomSeed = 1;
        /// <summary>When set, random(n) always gives this (kept within 1..n).</summary>
        public int? FixedRandom;
        /// <summary>do_offer: the talker accepts an offer (else it refuses).</summary>
        public bool OffersAccepted = true;
        /// <summary>do_demand: the talker gives in to a demand (else it refuses and attacks).</summary>
        public bool DemandsGivenIn = true;
        /// <summary>The talker's personal name string id (npc_name), and its item.</summary>
        public int TalkerItem = 0x40;
        public string TalkerDescription = "no talker (defaults)";
        public Dictionary<int, SimObject> Objects = new Dictionary<int, SimObject>();
        /// <summary>The talker's inventory, by object index.</summary>
        public List<int> NpcInventory = new List<int>();
        /// <summary>The player's inventory (top level), by object index.</summary>
        public List<int> PlayerInventory = new List<int>();
        /// <summary>The trade slots, an object index or 0, and whether each is selected (UW1 four a side, UW2 six).</summary>
        public int[] PlayerSlots, NpcSlots;
        public bool[] PlayerSelected, NpcSelected;
        /// <summary>The object in the player's hand (a take_from_npc), or 0.</summary>
        public int InHand;
        public Func<int, string> ItemName = i => "item " + i;
        public int NextIndex = 1000;

        public int TradeSlots { get { return Uw2 ? 6 : 4; } }
        public int QuestFlags { get { return Uw2 ? 128 : 32; } }

        public static SimWorld Defaults(GameKind kind)
        {
            var w = new SimWorld { Kind = kind };
            bool uw2 = kind == GameKind.UW2;
            w.QuestWords = new uint[uw2 ? 32 : 1];
            w.QuestBytes = new int[uw2 ? 16 : 4];
            w.GameVariables = new int[uw2 ? 256 : 64];
            w.PlayerSlots = new int[w.TradeSlots]; w.NpcSlots = new int[w.TradeSlots];
            w.PlayerSelected = new bool[w.TradeSlots]; w.NpcSelected = new bool[w.TradeSlots];
            for (int i = 0; i < 20; i++) w.Skills[i] = 5;
            foreach (var kv in DefaultImports) w.Imported[kv.Key] = kv.Value;
            return w;
        }

        /// <summary>The imported variables' values when nothing better is known (a friendly talker and a new player).</summary>
        static readonly Dictionary<string, int> DefaultImports = new Dictionary<string, int>
        {
            { "npc_whoami", 0 }, { "npc_hunger", 0x10 }, { "npc_health", 0x80 }, { "npc_hp", 30 }, { "npc_arms", 10 },
            { "npc_power", 10 }, { "npc_goal", 0 }, { "npc_gtarg", 0 }, { "npc_talkedto", 0 }, { "npc_level", 2 },
            { "npc_xhome", 0 }, { "npc_yhome", 0 }, { "npc_name", 0 }, { "npc_attitude", 2 },
            { "play_hunger", 0x40 }, { "play_health", 0x80 }, { "play_hp", 30 }, { "play_arms", 15 }, { "play_power", 20 },
            { "play_mana", 10 }, { "play_level", 1 }, { "play_sex", 0 }, { "play_poison", 0 }, { "play_drawn", 0 },
            { "play_name", ConversationSimulator.PlayerNameId }, { "dungeon_level", 1 }, { "game_time", 0 }, { "game_mins", 0 },
            { "game_days", 0 }, { "new_player_exp", 0 },
        };

        public SimWorld Clone()
        {
            var w = (SimWorld)MemberwiseClone();
            w.QuestWords = (uint[])QuestWords.Clone();
            w.QuestBytes = (int[])QuestBytes.Clone();
            w.GameVariables = (int[])GameVariables.Clone();
            w.XClocks = (int[])XClocks.Clone();
            w.Skills = (int[])Skills.Clone();
            w.PlayerSlots = (int[])PlayerSlots.Clone(); w.NpcSlots = (int[])NpcSlots.Clone();
            w.PlayerSelected = (bool[])PlayerSelected.Clone(); w.NpcSelected = (bool[])NpcSelected.Clone();
            w.Imported = new Dictionary<string, int>(Imported, StringComparer.Ordinal);
            w.Objects = new Dictionary<int, SimObject>();
            foreach (var kv in Objects) w.Objects[kv.Key] = kv.Value.Clone();
            w.NpcInventory = new List<int>(NpcInventory);
            w.PlayerInventory = new List<int>(PlayerInventory);
            return w;
        }

        /// <summary>A new object (for the start-up list, or one a script makes), returned with its index.</summary>
        public SimObject Add(int item, int quantity = 1, int quality = 40)
        {
            var o = new SimObject { Index = NextIndex++, Item = item, Quality = quality, IsQuantity = quantity > 1, Link = quantity > 1 ? quantity : 0 };
            Objects[o.Index] = o;
            return o;
        }

        public SimObject Get(int index) { SimObject o; return Objects.TryGetValue(index, out o) ? o : null; }

        // ----- quests, both games (conv/BABLHACK.C get_quest, set_quest) -----

        /// <summary>A quest flag's bit as the player record has it (for the editors: not the 16-bit mask UW1's set_quest uses).</summary>
        public int QuestFlag(int q)
        {
            if (q < 0 || q >= QuestFlags) return 0;
            return Uw2 ? (int)((QuestWords[q >> 2] >> (q & 3)) & 1) : (int)((QuestWords[0] >> q) & 1);
        }

        public void SetQuestFlag(int q, int v)
        {
            if (q < 0 || q >= QuestFlags) return;
            int w = Uw2 ? q >> 2 : 0, bit = Uw2 ? q & 3 : q;
            QuestWords[w] = v != 0 ? QuestWords[w] | 1u << bit : QuestWords[w] & ~(1u << bit);
        }

        // ----- filling in from the open game -----

        /// <summary>
        /// The world of an open game: with a save open, the player record of its PLAYER.DAT
        /// (name, sex, skills, quests, game variables, X clocks, the player's imported values and
        /// inventory); the talker's level object when one is given (its imported values and
        /// inventory). DATA has no player record, so a new game's zeros are used.
        /// </summary>
        public static SimWorld FromGame(GameSession s, int slot, ConversationUser talker)
        {
            var w = Defaults(s.Kind);
            w.ItemName = i => { string n = s.Names.Get(i); return string.IsNullOrWhiteSpace(n) ? "item " + i : n; };
            w.Imported["npc_whoami"] = slot < 0x100 ? slot : 0;
            w.TalkerItem = slot >= 0x100 ? 0x40 + (slot & 0x3F) : 0x40;
            w.Imported["npc_name"] = slot < 0x100 ? (slot + 0x10) | 0xE00 : w.TalkerItem | 0x800;
            string path = s.IsSave ? GameProfile.Find(s.SaveFolder, "PLAYER.DAT") : null;
            if (path != null)
            {
                try
                {
                    var sheet = UwEditor.Core.Player.PlayerSheet.Load(path, s.Kind, s.DataFolder);
                    w.FromPlayer(sheet, s);
                }
                catch (Exception) { }
            }
            if (talker != null)
            {
                try { w.FromTalker(s, talker); } catch (Exception) { }
            }
            return w;
        }

        void FromPlayer(UwEditor.Core.Player.PlayerSheet p, GameSession s)
        {
            var r = p.Dat.Record;
            PlayerName = p.Name ?? PlayerName;
            PlayerFemale = p.Female;
            for (int k = 0; k < 20; k++) Skills[k] = p.Skill(k);
            SkillPoints = p.SkillPoints;
            Experience = p.Experience;
            // player.h: UW1 quests long 0x65, bytes 0x69-0x6C, talismans 0x6D, game_vars 0x70;
            // UW2 quests longs 0x66, bytes 0xE6-0xF5, bF6, vars words 0xF9, X clocks 0x36D
            if (Uw2)
            {
                for (int i = 0; i < 32; i++) QuestWords[i] = U32(r, 0x66 + 4 * i);
                for (int i = 0; i < 16; i++) QuestBytes[i] = r[0xE6 + i];
                QuestTop = r[0xF6];
                for (int i = 0; i < 256; i++) GameVariables[i] = r[0xF9 + 2 * i] | r[0xFA + 2 * i] << 8;
                for (int i = 0; i < 16; i++) XClocks[i] = r[0x36D + i];
            }
            else
            {
                QuestWords[0] = U32(r, 0x65);
                for (int i = 0; i < 4; i++) QuestBytes[i] = r[0x69 + i];
                QuestTop = r[0x6D];
                for (int i = 0; i < 64; i++) GameVariables[i] = r[0x70 + i];
            }
            // conv/CONVVARS.C setup_converse_data: the player's half
            Imported["play_hunger"] = p.Hunger;
            Imported["play_hp"] = p.Vitality;
            Imported["play_health"] = p.MaxVitality > 0 ? (short)(p.Vitality << 8) / p.MaxVitality : 0x80;
            Imported["play_arms"] = p.Skill(0) + p.Strength;
            Imported["play_power"] = p.Dexterity + p.Mana + p.Skill(6);
            Imported["play_mana"] = p.Mana;
            Imported["play_level"] = p.Level;
            Imported["play_sex"] = p.Female ? 1 : 0;
            if (p.Inventory != null)
                foreach (var it in p.Inventory.Top) PlayerInventory.Add(FromItem(it, s));
        }

        int FromItem(UwEditor.Core.Player.InventoryItem it, GameSession s)
        {
            var o = new SimObject
            {
                Index = NextIndex++, Item = it.ItemId, Quality = it.Quality, Owner = it.Owner, Flags = it.Flags,
                IsQuantity = it.IsQuantity, Link = it.Link, Value = ItemValue(s, it.ItemId),
            };
            Objects[o.Index] = o;
            foreach (var c in it.Contents) o.Contents.Add(FromItem(c, s));
            return o.Index;
        }

        void FromTalker(GameSession s, ConversationUser u)
        {
            var level = s.LoadLevel(u.Level);
            var o = level.Objects[u.Index];
            if (o == null || o.Record == null || o.Record.Length < 0x1B) return;
            var r = o.Record;
            int w0b = r[0xB] | r[0xC] << 8, w0d = r[0xD] | r[0xE] << 8;
            int goal = w0b & 0xF, gtarg = (w0b >> 4) & 0xFF, attitude = (w0d >> 14) & 3;
            TalkerItem = o.Item;
            TalkerDescription = u.ToString();
            int who = r[0x1A];
            // conv/CONVVARS.C setup_converse_data: the talker's half (the class figures it uses are not read here)
            Imported["npc_whoami"] = who;
            Imported["npc_hp"] = r[8];
            Imported["npc_goal"] = goal;
            Imported["npc_gtarg"] = gtarg;
            Imported["npc_talkedto"] = (w0d >> 13) & 1;
            Imported["npc_xhome"] = o.Quality;
            Imported["npc_yhome"] = o.Owner;
            Imported["npc_name"] = who != 0 ? (who + 0x10) | 0xE00 : o.Item | 0x800;
            Imported["npc_attitude"] = goal == 5 && gtarg == 1 ? 0 : attitude;
            Imported["dungeon_level"] = u.Level;
            foreach (int c in o.Children) { int i = FromLevel(level, c, s); if (i > 0) NpcInventory.Add(i); }
        }

        int FromLevel(LevelModel level, int index, GameSession s)
        {
            var lo = index > 0 && index < level.Objects.Length ? level.Objects[index] : null;
            if (lo == null || lo.Free) return 0;
            var o = new SimObject
            {
                Index = lo.Index, Item = lo.Item, Quality = lo.Quality, Owner = lo.Owner, IsQuantity = lo.IsQuantity, Link = lo.Link,
                Heading = lo.Heading, FineX = lo.FineX, FineY = lo.FineY, Z = lo.Z, Value = ItemValue(s, lo.Item),
                Flags = lo.Record != null && lo.Record.Length > 1 ? (lo.Record[1] >> 1) & 7 : 0,
            };
            if (Objects.ContainsKey(o.Index)) return 0;
            Objects[o.Index] = o;
            if (o.Index >= NextIndex) NextIndex = o.Index + 1;
            foreach (int c in lo.Children) { int i = FromLevel(level, c, s); if (i > 0) o.Contents.Add(i); }
            return o.Index;
        }

        /// <summary>COMOBJ.DAT's value word of an item (bytes 4-5 of its record), 0 when unknown.</summary>
        public static int ItemValue(GameSession s, int item)
        {
            if (s == null || item < 0 || item >= 0x200) return 0;
            int lo = UwEditor.Core.Editing.Races.ComObjByte(s, item, 4), hi = UwEditor.Core.Editing.Races.ComObjByte(s, item, 5);
            return lo < 0 || hi < 0 ? 0 : lo | hi << 8;
        }

        static uint U32(byte[] r, int at) { return (uint)(r[at] | r[at + 1] << 8 | r[at + 2] << 16 | r[at + 3] << 24); }

        /// <summary>The private globals a conversation starts with: its record in the save's BGLOBALS.DAT (none in DATA, where a new game has zeros).</summary>
        public static int[] SavedGlobals(GameSession s, int slot)
        {
            if (!s.IsSave) return null;
            string p = GameProfile.Find(s.SaveFolder, "BGLOBALS.DAT");
            if (p == null) return null;
            try
            {
                var r = BGlobals.Parse(File.ReadAllBytes(p), true).Find(slot);
                return r == null ? null : r.Words;
            }
            catch (Exception) { return null; }
        }
    }
}
