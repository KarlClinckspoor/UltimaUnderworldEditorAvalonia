using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>An item kind the key item scatter can take in besides the key items, with its class and how many the levels hold.</summary>
    public sealed class ExtraItem
    {
        public int Item;
        public string Name = "", Class = "";
        public int Count;
        public override string ToString() { return string.Format("{0} (0x{1:X3}) ×{2}", Name, Item, Count); }
    }

    /// <summary>
    /// The key item scatter's additional items: every kind of item the player can pick up (COMOBJ.DAT's
    /// pickable bit, byte 3 bit 5) that lies in the levels, by class, for the picker. Items the scatter
    /// moves as key items (keys, runes, ability items, ingredients, plot items, talismans) move by
    /// their category whatever is picked here. Containers move with what they hold, and only within
    /// their own level: their contents list is the level's (a link to the first object inside).
    /// </summary>
    public static class ExtraItems
    {
        /// <summary>The classes in the picker's order, with their headings.</summary>
        public static readonly string[][] Classes =
        {
            new[] { "weapons", "Weapons and ammunition" },
            new[] { "armour", "Armour, shields and crowns" },
            new[] { "rings", "Rings" },
            new[] { "wands", "Wands" },
            new[] { "potions", "Potions" },
            new[] { "food", "Food and drink" },
            new[] { "lights", "Lights" },
            new[] { "treasure", "Coins, gems and treasure" },
            new[] { "books", "Books, scrolls and maps" },
            new[] { "containers", "Containers (they take what they hold along, within their level)" },
            new[] { "bones", "Skulls and bones" },
            new[] { "debris", "Broken weapons, wood, plants and debris" },
            new[] { "other", "Other things to pick up (bedrolls, instruments, tools…)" },
        };

        /// <summary>The kinds the item swap moves (the old "Everything else as filler").</summary>
        public static readonly string[] SwapClasses = { "weapons", "armour", "rings", "wands", "potions", "food", "lights" };

        /// <summary>Objects with the pickable bit that are fixed in fact (Uw1Schemas "Pick up": urn, orb, campfire, fountain, cauldron; UW2's skull 0x13 too) and the spell objects inside wands.</summary>
        static bool Fixed(GameSession s, int item)
        {
            if (item == 0x120) return true;
            string n = (s.Names.Get(item) ?? "").Trim().ToLowerInvariant();
            return n == "urn" || n == "orb" || n == "campfire" || n == "fountain" || n == "cauldron" || (s.IsUw2 && item == 0x13);
        }

        /// <summary>The class of an item kind, or null when the picker does not offer it (not pickable, fixed, a creature or a fixed object).</summary>
        public static string ClassOf(GameSession s, int item)
        {
            if (item < 0 || item >= 0x140 || (item >= 0x40 && item < 0x80)) return null;
            if ((Races.ComObjByte(s, item, 3) & 0x20) == 0 || Fixed(s, item)) return null;
            if (item < 0x20) return "weapons";
            if (item < 0x40) return ItemTables.Rings(s).Contains(item) ? "rings" : "armour";
            if (item >= 0x80 && item < 0x90) return "containers";
            if (ItemTables.LightKinds(s).Any(k => k == item || k + 4 == item)) return "lights";
            if (item >= 0x98 && item < 0xA0) return "wands";
            if (item >= 0xA0 && item < 0xB0) return "treasure";
            if (ItemShuffle.IsPotionItem(s, item)) return "potions";
            if (ItemTables.Food(s).Contains(item) || (item >= 0xB0 && item < 0xC0)) return "food";
            if (item >= 0xC2 && item <= 0xC7) return "bones";
            if (item >= 0xC0 && item < 0xE0) return "debris";
            if (item >= 0x130) return "books";
            return "other";
        }

        /// <summary>Whether an item kind is a container: its link is a list of what it holds.</summary>
        public static bool IsContainer(GameSession s, int item) { return ClassOf(s, item) == "containers"; }

        /// <summary>Every kind the picker offers that lies in the levels (on the floor, in containers or carried), with its count.</summary>
        public static List<ExtraItem> Offered(GameSession s, Func<int, LevelModel> level)
        {
            var counts = new Dictionary<int, int>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    if (ClassOf(s, o.Item) == null) continue;
                    int n; counts.TryGetValue(o.Item, out n); counts[o.Item] = n + 1;
                }
            }
            var order = Classes.Select(c => c[0]).ToList();
            return counts.Select(kv => new ExtraItem { Item = kv.Key, Name = s.Names.Get(kv.Key) ?? "", Class = ClassOf(s, kv.Key), Count = kv.Value })
                .OrderBy(x => order.IndexOf(x.Class)).ThenBy(x => x.Item).ToList();
        }
    }
}
