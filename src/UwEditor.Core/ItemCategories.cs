using System;
using System.Collections.Generic;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>Item categories by item id, following the class ranges of include/items.h
    /// (copied from the Unity editor's Uw1ItemFilter, which has them for both games).</summary>
    [Flags]
    public enum ItemCategory : long
    {
        None = 0,
        MeleeWeapon = 1L << 0, Ammunition = 1L << 1, Launcher = 1L << 2, Armour = 1L << 3, Shield = 1L << 4,
        Ring = 1L << 5, Crown = 1L << 6, Creature = 1L << 7, Container = 1L << 8, Light = 1L << 9,
        Wand = 1L << 10, Treasure = 1L << 11, Food = 1L << 12, Potion = 1L << 13, Scenery = 1L << 14,
        QuestKey = 1L << 15, Runestone = 1L << 16, Key = 1L << 17, Lock = 1L << 18, Quest = 1L << 19,
        MagicItem = 1L << 20, SpellObject = 1L << 21, Book = 1L << 22, Scroll = 1L << 23, Door = 1L << 24,
        Furniture = 1L << 25, Decal = 1L << 26, Switch = 1L << 27, Trap = 1L << 28, Trigger = 1L << 29,
        Animated = 1L << 30,
        Weapon = MeleeWeapon | Ammunition | Launcher,
        Wearable = Armour | Shield | Ring | Crown,
        Keys = Key | Lock | QuestKey,
        Model3D = Door | Furniture | Decal | Switch,
        Event = Trap | Trigger,
        All = (1L << 31) - 1,
    }

    public static class ItemCategories
    {
        public static ItemCategory Of(int item, GameKind game)
        {
            return game == GameKind.UW2 ? OfUw2(item) : OfUw1(item);
        }

        static ItemCategory OfUw1(int item)
        {
            if (item < 0x10) return ItemCategory.MeleeWeapon;
            if (item < 0x18) return ItemCategory.Ammunition;
            if (item < 0x20) return ItemCategory.Launcher;
            if (item < 0x30) return ItemCategory.Armour;
            if (item < 0x33) return ItemCategory.Crown;
            if (item < 0x36) return ItemCategory.Armour;      // unused armour slots
            if (item == 0x36 || (item >= 0x38 && item <= 0x3A)) return ItemCategory.Ring;
            if (item < 0x40) return ItemCategory.Shield;
            if (item < 0x80) return ItemCategory.Creature;
            if (item < 0x90) return ItemCategory.Container;
            if (item < 0x98) return ItemCategory.Light;
            if (item < 0xA0) return ItemCategory.Wand;
            if (item < 0xB0) return ItemCategory.Treasure;
            if (item == 0xBB || item == 0xBC) return ItemCategory.Potion;   // the red and green potions
            if (item < 0xC0) return ItemCategory.Food;
            if (item < 0xE1) return ItemCategory.Scenery;
            if (item < 0xE8) return ItemCategory.QuestKey;    // keys of Truth, Love, Courage, the key parts, Key of Infinity
            if (item < 0x100) return ItemCategory.Runestone;
            if (item == 0x10F) return ItemCategory.Lock;
            if (item < 0x110) return ItemCategory.Key;
            if (item < 0x120) return ItemCategory.Quest;
            if (item < 0x130) return item == 0x120 ? ItemCategory.SpellObject : ItemCategory.MagicItem;
            if (item < 0x138) return ItemCategory.Book;
            if (item < 0x140) return ItemCategory.Scroll;
            if (item < 0x150) return ItemCategory.Door;
            if (item < 0x160) return ItemCategory.Furniture;
            if (item < 0x170) return ItemCategory.Decal;
            if (item < 0x180) return ItemCategory.Switch;
            if (item < 0x1A0) return ItemCategory.Trap;
            if (item < 0x1C0) return ItemCategory.Trigger;
            return ItemCategory.Animated;
        }

        /// <summary>UW2's classes (uw2 include/items.h).</summary>
        static ItemCategory OfUw2(int item)
        {
            if (item < 0x10) return ItemCategory.MeleeWeapon;
            if (item < 0x18) return ItemCategory.Ammunition;
            if (item < 0x1B || item == 0x1F) return ItemCategory.Launcher;
            if (item < 0x20) return ItemCategory.Ammunition;   // homing dart, snowball, fireball, satellite
            if (item < 0x30 || item == 0x33) return ItemCategory.Armour;
            if (item < 0x33 || item == 0x34) return ItemCategory.Crown;
            if (item == 0x36) return ItemCategory.Quest;      // strange artifact
            if (item < 0x3B) return ItemCategory.Ring;
            if (item < 0x40) return ItemCategory.Shield;
            if (item < 0x80) return ItemCategory.Creature;
            if (item < 0x90) return ItemCategory.Container;
            if (item < 0x98) return ItemCategory.Light;
            if (item < 0xA0) return ItemCategory.Wand;
            if (item < 0xB0) return ItemCategory.Treasure;
            if (item < 0xC0) return ItemCategory.Food;
            if (item < 0xE0) return ItemCategory.Scenery;
            if (item >= 0xE1 && item <= 0xE7) return ItemCategory.Potion;
            if (item < 0x100) return ItemCategory.Runestone;
            if (item < 0x102) return ItemCategory.MagicItem;  // curious implement, lockpick
            if (item == 0x10F) return ItemCategory.Lock;
            if (item < 0x110) return ItemCategory.Key;
            if (item < 0x120) return ItemCategory.Quest;
            if (item < 0x130) return item == 0x120 ? ItemCategory.SpellObject : ItemCategory.MagicItem;
            if (item < 0x134 || item == 0x138) return ItemCategory.Book;
            if (item < 0x13B) return ItemCategory.Scroll;     // scrolls and maps
            if (item < 0x140) return ItemCategory.Scenery;
            if (item < 0x150) return ItemCategory.Door;
            if (item < 0x160) return ItemCategory.Furniture;
            if (item < 0x170) return ItemCategory.Decal;
            if (item < 0x180) return ItemCategory.Switch;
            if (item < 0x1A0) return ItemCategory.Trap;
            if (item < 0x1C0) return ItemCategory.Trigger;
            return ItemCategory.Animated;
        }

        /// <summary>The quick filters of the object list.</summary>
        public static readonly KeyValuePair<ItemCategory, string>[] Quick =
        {
            new KeyValuePair<ItemCategory, string>(ItemCategory.Weapon, "Weapons"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Food, "Food"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Creature, "Monsters"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Keys, "Keys"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Trap, "Traps"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Trigger, "Triggers"),
        };

        /// <summary>Every category offered in the find window, with labels (the Unity editor's list).</summary>
        public static readonly KeyValuePair<ItemCategory, string>[] Choices =
        {
            new KeyValuePair<ItemCategory, string>(ItemCategory.All, "Any kind"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Weapon, "Weapons (all)"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.MeleeWeapon, "Melee weapons"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Ammunition | ItemCategory.Launcher, "Missiles and launchers"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Wearable, "Armour, shields, rings, crowns"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Creature, "Monsters and NPCs"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Container, "Containers"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Light | ItemCategory.Wand, "Lights and wands"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Treasure, "Treasure"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Food, "Food"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Potion, "Potions"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Scenery, "Scenery and junk"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Runestone, "Runestones"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Keys, "Keys and locks"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Quest, "Unique and quest items"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.MagicItem | ItemCategory.SpellObject, "Tools and spell objects"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Book | ItemCategory.Scroll, "Books and scrolls"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Door, "Doors"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Furniture | ItemCategory.Decal | ItemCategory.Switch, "Furniture, decals, switches"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Trap, "Traps"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Trigger, "Triggers"),
            new KeyValuePair<ItemCategory, string>(ItemCategory.Animated, "Animated objects"),
        };
    }

    /// <summary>
    /// What the object list and the find window look for. Text matches the name, the creature's
    /// NPC name, the object's text (book, scroll, writing, gravestone, text trap), "#index" and
    /// "0x" item ids. Categories are alternatives; Enchanted narrows them down.
    /// </summary>
    public sealed class ObjectQuery
    {
        public string Text = "";
        public ItemCategory Categories = ItemCategory.All;
        public bool EnchantedOnly;

        public bool IsEmpty { get { return string.IsNullOrWhiteSpace(Text) && Categories == ItemCategory.All && !EnchantedOnly; } }

        /// <summary>What matched in the object's text or NPC name, for showing beside a result; null if nothing there did.</summary>
        public string MatchedText;

        public bool Matches(GameSession s, ObjectInfo o)
        {
            MatchedText = null;
            if (Categories != ItemCategory.All && (ItemCategories.Of(o.Item, s.Kind) & Categories) == 0) return false;
            if (EnchantedOnly && !o.Enchanted) return false;
            string f = (Text ?? "").Trim().ToLowerInvariant();
            if (f.Length == 0) return true;
            int v;
            if (f.StartsWith("0x")) return int.TryParse(f.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out v) && v == o.Item;
            if (f.StartsWith("#")) return int.TryParse(f.Substring(1), out v) && v == o.Index;
            if (o.Name.ToLowerInvariant().Contains(f)) return true;
            string npc = ObjectTexts.NpcName(s, o);
            if (npc != null && npc.ToLowerInvariant().Contains(f)) { MatchedText = npc; return true; }
            string text = ObjectTexts.TextOf(s, o);
            if (text != null && text.ToLowerInvariant().Contains(f)) { MatchedText = Snippet(text, f); return true; }
            return false;
        }

        /// <summary>A short piece of text round the match, on one line.</summary>
        public static string Snippet(string text, string f)
        {
            string flat = text.Replace('\n', ' ').Replace('\r', ' ');
            int i = flat.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal);
            if (i < 0) return flat.Length > 60 ? flat.Substring(0, 60) + "…" : flat;
            int start = Math.Max(0, i - 20), end = Math.Min(flat.Length, i + f.Length + 30);
            return (start > 0 ? "…" : "") + flat.Substring(start, end - start) + (end < flat.Length ? "…" : "");
        }
    }

    /// <summary>One object found by <see cref="LevelSearch"/>.</summary>
    public sealed class SearchHit
    {
        public int Level { get; set; }
        public string LevelName { get; set; }
        public int Index { get; set; }
        public int Item { get; set; }
        public string Name { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        /// <summary>The object it is inside, or empty.</summary>
        public string Inside { get; set; }
        public string Matched { get; set; }
    }

    /// <summary>The find window's search over every level of the open game.</summary>
    public static class LevelSearch
    {
        public static List<SearchHit> Run(GameSession s, ObjectQuery q, int max = 2000)
        {
            var hits = new List<SearchHit>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                var level = s.LoadLevel(slot.Level);
                foreach (var o in level.Objects)
                {
                    if (o.TileX < 0 || o.Free || !q.Matches(s, o)) continue;
                    hits.Add(new SearchHit
                    {
                        Level = slot.Level,
                        LevelName = s.IsUw2 ? s.Profile.LevelName(slot.Level) : "Level " + slot.Level,
                        Index = o.Index, Item = o.Item, Name = o.Name, TileX = o.TileX, TileY = o.TileY,
                        Inside = o.Parent >= 0 ? level.Objects[o.Parent].ToString() : "",
                        Matched = q.MatchedText ?? "",
                    });
                    if (hits.Count >= max) return hits;
                }
            }
            return hits;
        }
    }
}
