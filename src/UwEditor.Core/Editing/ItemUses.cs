using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UwEditor.Core.Conversations;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Editing
{
    /// <summary>One place an item id is used.</summary>
    public sealed class ItemUse
    {
        /// <summary>"code" (the game's code names it), "conversation", "combination", "creature" (loot or arms in OBJECTS.DAT), "level" (objects of it), "named" (a quest item by name).</summary>
        public string Kind;
        public string Text;
        /// <summary>Where to go: a level object, or a conversation slot and line.</summary>
        public int Level = -1, Index = -1, TileX = -1, TileY = -1, Slot = -1, Line = -1;
        public override string ToString() { return Text; }
    }

    /// <summary>What an item id is used for across the game: a whole report for one id, or a short census of every id.</summary>
    public sealed class ItemUseReport
    {
        public int Item;
        public string Name;
        public readonly List<ItemUse> Uses = new List<ItemUse>();
        public int Count(string kind) { return Uses.Count(u => u.Kind == kind); }
        /// <summary>Nothing uses it: no objects, no conversation, no code, no loot or combination.</summary>
        public bool Free { get { return Uses.Count == 0; } }
        /// <summary>Its behaviour is not only data: the game's code or a conversation names it.</summary>
        public bool Bound { get { return Uses.Any(u => u.Kind == "code" || u.Kind == "conversation" || u.Kind == "named"); } }
        public string Summary
        {
            get
            {
                if (Free) return "free";
                var parts = new List<string>();
                int lv = Count("level"); if (lv > 0) parts.Add(lv + " placed");
                if (Count("conversation") > 0) parts.Add("conversations");
                if (Count("code") > 0) parts.Add("game code");
                if (Count("named") > 0) parts.Add("quest item");
                if (Count("creature") > 0) parts.Add("creature loot");
                if (Count("combination") > 0) parts.Add("combination");
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>
    /// Finds where an item id is used, so a slot can be given a new identity safely: the objects
    /// of it in every level (on the floor, in containers, carried, as trap templates); the
    /// conversations that create, take, look for or delete it (conv/CONVERSE.C: do_inv_create,
    /// do_inv_delete, take_from_npc, take_id_from_npc, find_inv, find_barter, find_barter_total
    /// take an item id, or BARTER_CLASS 1000 + its class, include/conv.h); item combinations
    /// (CMB.DAT); creature loot and arms (OBJECTS.DAT); the game's own code
    /// (<see cref="HardcodedItems"/>); and UW1's quest items by name.
    /// </summary>
    public static class ItemUses
    {
        static readonly string[] ItemFunctions = { "do_inv_create", "do_inv_delete", "take_from_npc", "take_id_from_npc", "take_from_npc_inv", "find_inv", "find_barter", "find_barter_total" };
        const int BarterClass = 1000;

        /// <summary>The conversations' item calls with a literal item argument: (slot, line number, line, item ids or class codes named).</summary>
        public sealed class ConversationIndex
        {
            public readonly List<(int Slot, string Name, int Line, string Text, int[] Values)> Calls = new List<(int, string, int, string, int[])>();
        }

        static readonly Regex Call = new Regex(@"\b(" + string.Join("|", ItemFunctions) + @")\(([^()]*)\)", RegexOptions.Compiled);

        /// <summary>Reads every conversation once (decompiling them takes a moment).</summary>
        public static ConversationIndex IndexConversations(ConversationLibrary lib)
        {
            var ix = new ConversationIndex();
            if (lib == null) return ix;
            foreach (int slot in lib.Archive.Slots())
            {
                Conversation c;
                try { c = lib.Archive.Read(slot); } catch (Exception) { continue; }
                if (c == null) continue;
                string text;
                try { text = lib.Decompile(c).Text; } catch (Exception) { continue; }
                var lines = text.Split('\n');
                string name = lib.Name(slot);
                for (int i = 0; i < lines.Length; i++)
                    foreach (Match m in Call.Matches(lines[i]))
                    {
                        // the item (or class) is the first argument of each of these (find_inv(item, where), find_barter_total(item, …))
                        string first = m.Groups[2].Value.Split(',')[0].Trim();
                        if (Regex.IsMatch(first, @"^\d+$")) ix.Calls.Add((slot, name, i + 1, lines[i].Trim(), new[] { int.Parse(first) }));
                    }
            }
            return ix;
        }

        /// <summary>Everything that uses item id; level gives the levels as edited, lib and conversations may be null.</summary>
        public static ItemUseReport Find(GameSession s, int item, Func<int, LevelModel> level, ConversationIndex conversations, ObjectTables tables = null, CombinationTable cmb = null)
        {
            var r = new ItemUseReport { Item = item, Name = s.Names.Get(item) };
            var hc = HardcodedItems.Of(s.Kind, item);
            if (hc != null)
                foreach (var f in hc.Value.Files)
                {
                    string what = HardcodedItems.Describe(f);
                    r.Uses.Add(new ItemUse { Kind = "code", Text = string.Format("The game's code names it ({0}, {1}.C){2}: that behaviour stays with the id.", hc.Value.Name, f, what.Length > 0 ? ", " + what : "") });
                }
            string proper = NamedItems.Of(s.Kind, item);
            if (proper != null) r.Uses.Add(new ItemUse { Kind = "named", Text = "A quest item: " + proper + "." });
            else if (!s.IsUw2 && KeyItems.Uw1Notable.ContainsKey(item)) r.Uses.Add(new ItemUse { Kind = "named", Text = "A quest item (" + KeyItems.Uw1Notable[item].Replace("item:", "").Replace('_', ' ') + ")." });
            if (conversations != null)
                foreach (var c in conversations.Calls)
                {
                    bool exact = c.Values.Contains(item), cls = c.Values.Contains(BarterClass + (item >> 4));
                    if (!exact && !cls) continue;
                    r.Uses.Add(new ItemUse
                    {
                        Kind = "conversation", Slot = c.Slot, Line = c.Line,
                        Text = string.Format("Conversation {0} ({1}), line {2}: {3}{4}", c.Slot, c.Name, c.Line, c.Text, exact ? "" : " (its whole class, " + (BarterClass + (item >> 4)) + ")"),
                    });
                }
            if (cmb != null && cmb.Exists)
                for (int i = 0; i < cmb.Dat.Entries.Length; i++)
                {
                    if (cmb.IsEmpty(i)) continue;
                    int a = cmb.Item(i, 0), b = cmb.Item(i, 1), o = cmb.Item(i, 2);
                    if (a == item || b == item || o == item)
                        r.Uses.Add(new ItemUse { Kind = "combination", Text = string.Format("Combination {0}: {1} + {2} → {3} (CMB.DAT).", i + 1, s.Names.Get(a), s.Names.Get(b), s.Names.Get(o)) });
                }
            if (tables != null)
            {
                var t = ObjectTables.Table("Creatures");
                for (int row = 0; row < t.Count; row++)
                {
                    int critter = t.ItemOf(row);
                    var why = new List<string>();
                    for (int k = 1; k <= 2; k++)
                    {
                        if (tables.Get(t, row, t.Schema["Loot " + k + " chance"]) > 0 && tables.Get(t, row, t.Schema["Loot " + k + " item"]) == item) why.Add("loot " + k);
                        if (tables.Get(t, row, t.Schema["Arms " + k + " present"]) != 0 && tables.Get(t, row, t.Schema["Arms " + k + " item"]) == item) why.Add("arms " + k);
                    }
                    if (item >= 0xB0 && item <= 0xBF && tables.Get(t, row, t.Schema["Food chance"]) > 0 && 0xB0 + tables.Get(t, row, t.Schema["Food item"]) == item) why.Add("food");
                    if (why.Count > 0) r.Uses.Add(new ItemUse { Kind = "creature", Text = string.Format("{0} carries it when made ({1}, OBJECTS.DAT).", s.Names.Get(critter), string.Join(", ", why)) });
                }
            }
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.Item != item || (o.TileX < 0 && o.Parent < 0) || o.Index == 0) continue;
                    string where = "";
                    for (int up = o.Parent, g = 0; up >= 0 && g < 64; up = m.Objects[up].Parent, g++)
                    {
                        var p = m.Objects[up];
                        where = p.IsTrapOrTrigger ? ", made by trap #" + p.Index : p.IsCritter ? ", carried by #" + p.Index + " " + p.Name : ", in #" + p.Index + " " + p.Name;
                        if (p.IsTrapOrTrigger || p.IsCritter) break;
                    }
                    string lname = s.IsUw2 && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : "Level " + slot.Level;
                    r.Uses.Add(new ItemUse { Kind = "level", Level = slot.Level, Index = o.Index, TileX = o.TileX, TileY = o.TileY, Text = string.Format("{0} ({1}, {2}): #{3}{4}", lname, o.TileX, o.TileY, o.Index, where) });
                }
            }
            return r;
        }

        /// <summary>A report for every item (not creatures, doors or traps: ids 0x000-0x03F and 0x080-0x13F).</summary>
        public static List<ItemUseReport> Census(GameSession s, Func<int, LevelModel> level, ConversationIndex conversations, ObjectTables tables = null, CombinationTable cmb = null)
        {
            // the levels once, not once an item
            var cache = new Dictionary<int, LevelModel>();
            Func<int, LevelModel> once = l => { LevelModel m; if (!cache.TryGetValue(l, out m)) cache[l] = m = level(l); return m; };
            return SlotItems().Select(i => Find(s, i, once, conversations, tables, cmb)).ToList();
        }

        /// <summary>The item ids an item slot can be: weapons, missiles, armour, and the misc, scenery and special classes.</summary>
        public static IEnumerable<int> SlotItems() { return Enumerable.Range(0, 0x40).Concat(Enumerable.Range(0x80, 0xC0)); }

        /// <summary>What kind of thing an id is, by its class (include/items.h): the game decides how it acts by that.</summary>
        public static string ClassOf(int item, bool uw2)
        {
            switch (item >> 4)
            {
                case 0x0: return "melee weapon (OBJECTS.DAT weapons)";
                case 0x1: return item >= 0x18 ? "missile weapon (OBJECTS.DAT ranged)" : "ammunition or missile (OBJECTS.DAT ranged)";
                case 0x2: return "armour (OBJECTS.DAT armour)";
                case 0x3: return "crown, ring or shield (OBJECTS.DAT armour)";
                case 0x8: return "container (OBJECTS.DAT containers)";
                case 0x9: return (item & 15) < 8 ? "light (OBJECTS.DAT lights)" : "wand";
                case 0xA: return "treasure";
                case 0xB: return "food or drink (OBJECTS.DAT food: nutrition)";
                case 0xC: case 0xD: return "scenery or junk";
                case 0xE: return uw2 ? "potion or runestone" : "key piece or runestone";
                case 0xF: return "runestone";
                case 0x10: return "key or lockpick";
                case 0x11: case 0x12: return "quest or magic item";
                case 0x13: return "book, scroll or map";
                default: return "other";
            }
        }
    }
}
