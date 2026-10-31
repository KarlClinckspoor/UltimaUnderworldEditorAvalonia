using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>An object that matters to the randomizer, where it is and what it would grant.</summary>
    public sealed class KeyItem
    {
        public int Level, Index, Item;
        public string Name;
        /// <summary>key, talisman, quest item, runestone, NPC, quest creature, marked.</summary>
        public string Kind;
        /// <summary>What a placement of it grants (key:5, talisman:cup, item:orb, npc:corby).</summary>
        public string Token;
        public int TileX, TileY;
        public int Zone;
        /// <summary>The container or creature holding it, or null.</summary>
        public string Holder;
        public bool Marked;
        public override string ToString()
        {
            return string.Format("{0}  #{1} {2}{3} · L{4} ({5}, {6}){7}", Token, Index, Name, Holder != null ? " in " + Holder : "", Level, TileX, TileY, Marked ? " · marked" : "");
        }
        public string Key { get { return Level + ":" + Index; } }
    }

    /// <summary>
    /// Which objects of the levels are key items for the randomizer: keys (by the lock they open),
    /// the talismans (COMOBJ fate 10), quest and unique items, runestones, creatures that talk
    /// (whoami with a name) and the creatures whose death the game checks (WORLDEV.C death_check);
    /// plus whatever the zone file marks by hand. Plants, bridges, furniture and the like are left
    /// out.
    /// </summary>
    public static class KeyItems
    {
        /// <summary>UW1 items worth listing besides keys and talismans (the Unity editor's audit list).</summary>
        public static readonly Dictionary<int, string> Uw1Notable = new Dictionary<int, string>
        {
            { 0x0E1, "item:key_of_truth" }, { 0x0E2, "item:key_of_love" }, { 0x0E3, "item:key_of_courage" },
            { 0x0E4, "item:two_part_key" }, { 0x0E5, "item:two_part_key" }, { 0x0E6, "item:two_part_key" }, { 0x0E7, "item:key_of_infinity" },
            { 0x110, "item:picture_tom" }, { 0x111, "item:crystal" }, { 0x112, "item:orb_rock" }, { 0x113, "item:gem_cutter" },
            { 0x115, "item:burning_incense" }, { 0x116, "item:incense" }, { 0x117, "item:orb" }, { 0x118, "item:broken_blade" },
            { 0x119, "item:broken_hilt" }, { 0x11A, "item:figurine" }, { 0x11C, "item:strong_thread" }, { 0x11D, "item:dragon_scales" },
            { 0x11E, "item:resilient_sphere" }, { 0x12C, "item:medallion" }, { 0x124, "item:flute" }, { 0x126, "item:moonstone" },
            { 0x0D8, "item:pole" }, { 0x02F, "item:lava_boots" }, { 0x128, "item:rock_hammer" }, { 0x122, "item:silver_seed" },
        };

        public static readonly Dictionary<int, string> Uw1Talismans = new Dictionary<int, string>
        {
            { 0x00A, "sword" }, { 0x036, "ring" }, { 0x037, "shield" }, { 0x093, "taper" }, { 0x097, "taper" },
            { 0x0AE, "cup" }, { 0x0BF, "wine" }, { 0x11F, "standard" }, { 0x136, "book" },
        };

        /// <summary>UW2 items beyond the quest range worth listing: the large blackrock gem, the curious implement, tools.</summary>
        static readonly HashSet<int> Uw2Extra = new HashSet<int> { 0x168, 0x100, 0x122, 0x126, 0x129, 0x13F, 0x036 };

        /// <summary>UW1's creatures whose death the game checks (event/WORLDEV.C death_check), by whoami.</summary>
        public static readonly Dictionary<int, string> Uw1QuestCreatures = new Dictionary<int, string>
        {
            { 0x6E, "gazer_dead" }, { 0x8E, "rodrick_dead" }, { 0x18, "prisoner_dead" }, { 0xE7, "tyball_dead" },
            { 0x1B, "garamon" }, { 0x16, "golem" }, { 0x0B, "thorlson" },
        };

        static string Slug(string s) { return new string((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); }

        /// <summary>The token and kind of an object, or null when it is not a key item (without hand marks).</summary>
        public static string Classify(GameSession s, ObjectInfo o, out string kind)
        {
            kind = null;
            bool uw2 = s.IsUw2;
            if (KeyIndex.IsKey(o.Item, s.Kind)) { kind = "key"; return "key:" + o.Owner; }
            string tn;
            if (!uw2 && Uw1Talismans.TryGetValue(o.Item, out tn)) { kind = "talisman"; return "talisman:" + tn; }
            if (o.Item < 0x140 && ((Races.ComObjByte(s, o.Item, 7) >> 1) & 15) == 10) { kind = "talisman"; return "talisman:" + Slug(s.Names.Get(o.Item)); }
            string tok;
            if (!uw2 && Uw1Notable.TryGetValue(o.Item, out tok)) { kind = "quest item"; return tok; }
            if (o.IsCritter)
            {
                int who = o.Record.Length > 0x1A ? o.Record[0x1A] : 0;
                string fl;
                if (!uw2 && Uw1QuestCreatures.TryGetValue(who, out fl)) { kind = "quest creature"; return "flag:" + fl; }
                string npc = ObjectTexts.NpcName(s, o);
                if (npc != null) { kind = "NPC"; return "npc:" + Slug(npc); }
                return null;
            }
            var c = ItemCategories.Of(o.Item, s.Kind);
            if ((c & (ItemCategory.Quest | ItemCategory.QuestKey)) != 0 || (uw2 && Uw2Extra.Contains(o.Item))) { kind = "quest item"; return "item:" + Slug(s.Names.Get(o.Item)); }
            if ((c & ItemCategory.Runestone) != 0) { kind = "runestone"; return "rune:" + Slug(s.Names.Get(o.Item).Replace(" stone", "")); }
            return null;
        }

        /// <summary>Every key item of the levels (with their unsaved edits), with the zone it lies in.</summary>
        public static List<KeyItem> Find(GameSession s, Func<int, LevelModel> level, ZoneSet zs, IEnumerable<int> levels = null)
        {
            var list = new List<KeyItem>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || (levels != null && !levels.Contains(slot.Level))) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0) continue;
                    string key = slot.Level + ":" + o.Index;
                    bool mark;
                    bool marked = zs.Importance.TryGetValue(key, out mark);
                    if (marked && !mark) continue;
                    string kind;
                    string token = Classify(s, o, out kind);
                    if (token == null && !(marked && mark)) continue;
                    if (token == null) { kind = "marked"; token = "item:" + Slug(s.Names.Get(o.Item)); }
                    var k = new KeyItem
                    {
                        Level = slot.Level, Index = o.Index, Item = o.Item, Name = ObjectTexts.NpcName(s, o) ?? NamedItems.Of(s.Kind, o.Item) ?? o.Name,
                        Kind = kind, Token = token, TileX = o.TileX, TileY = o.TileY, Marked = marked && mark,
                        Zone = zs.NearestZone(slot.Level, o.TileX, o.TileY),
                    };
                    if (o.Parent >= 0) k.Holder = m.Objects[o.Parent].ToString();
                    list.Add(k);
                }
            }
            return list;
        }

        /// <summary>Placements a zoning would get from its key items (one per token and zone), not already in it.</summary>
        public static List<Placement> SuggestPlacements(ZoneSet zs, IEnumerable<KeyItem> items)
        {
            var have = new HashSet<string>(zs.Placements.Select(p => p.Zone + "|" + Requirement.Normalize(p.Grants)));
            var add = new List<Placement>();
            foreach (var k in items)
            {
                if (k.Zone <= ZoneSet.Unassigned) continue;
                string id = k.Zone + "|" + Requirement.Normalize(k.Token);
                if (!have.Add(id)) continue;
                add.Add(new Placement { Zone = k.Zone, Grants = k.Token, Notes = string.Format("#{0} {1}, level {2} ({3}, {4})", k.Index, k.Name, k.Level, k.TileX, k.TileY), Source = "levels" });
            }
            return add;
        }
    }
}
