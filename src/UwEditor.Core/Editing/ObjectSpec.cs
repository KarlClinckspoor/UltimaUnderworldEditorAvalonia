using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>What kind of form an item gets when it is added: what can be said about it.</summary>
    public enum ItemForm
    {
        /// <summary>Quality, owner, quantity: boulders, food, treasure, scenery.</summary>
        Basic,
        /// <summary>Weapons, armour, shields, rings: can carry a weapon or armour enchantment, or a spell.</summary>
        Enchantable,
        /// <summary>Wands and staffs: a spell object inside, with charges.</summary>
        Wand,
        /// <summary>Books and scrolls: a text of STRINGS.PAK block 3.</summary>
        Text,
        /// <summary>Keys: the lock they open, and their description (block 5, 0x64 + lock).</summary>
        Key,
        /// <summary>A lock (0x10F): its number and whether it is locked.</summary>
        Lock,
        /// <summary>Bags, boxes, chests: they hold objects.</summary>
        Container,
        /// <summary>Critters: hit points, attitude, conversation.</summary>
        Creature,
    }

    /// <summary>
    /// A new object, before it is placed in a level or an inventory: its record fields, and for
    /// a wand the spell object that goes inside it.
    /// </summary>
    public sealed class ObjectSpec
    {
        public int Item;
        public int Quality = 0x28;
        public int Owner;
        /// <summary>With IsQuantity: the quantity (below 0x200) or 0x200 + a special value (an enchantment, a text).</summary>
        public int Link = 1;
        public bool IsQuantity = true;
        public bool Enchanted;
        public bool Invisible;
        /// <summary>Flags 9-11 (bits 0-2 here): flag 9 locked (a lock), flag 11 a spell from the 64 (an enchantment).</summary>
        public int Flags;
        public int Heading;
        public int FineX = 3, FineY = 3;
        /// <summary>Height in the tile (8 a floor step); null: on the floor.</summary>
        public int? Z;
        // critters
        public int Hp;
        public int Attitude = 2;
        public int Whoami;
        public int Goal;
        /// <summary>Objects that go inside it (a wand's spell object).</summary>
        public readonly List<ObjectSpec> Contents = new List<ObjectSpec>();

        public bool IsMobile { get { return Item >= 0x40 && Item < 0x80; } }

        public static ItemForm FormOf(int item, GameKind kind)
        {
            var c = ItemCategories.Of(item, kind);
            if (item == 0x10F) return ItemForm.Lock;
            if ((c & ItemCategory.Key) != 0 && item != 0x101) return ItemForm.Key;
            if ((c & ItemCategory.Creature) != 0) return ItemForm.Creature;
            if ((c & (ItemCategory.Weapon | ItemCategory.Wearable)) != 0) return ItemForm.Enchantable;
            if ((c & ItemCategory.Wand) != 0) return ItemForm.Wand;
            if ((c & (ItemCategory.Book | ItemCategory.Scroll)) != 0) return ItemForm.Text;
            if ((c & ItemCategory.Container) != 0) return ItemForm.Container;
            return ItemForm.Basic;
        }

        /// <summary>A spec with the defaults for the item: containers hold a list, everything else a quantity of 1.</summary>
        public static ObjectSpec Default(int item, GameKind kind)
        {
            var s = new ObjectSpec { Item = item };
            var form = FormOf(item, kind);
            if (form == ItemForm.Container || form == ItemForm.Wand) { s.IsQuantity = false; s.Link = 0; }
            if (form == ItemForm.Creature) { s.IsQuantity = false; s.Link = 0; }
            if (form == ItemForm.Lock) { s.IsQuantity = true; s.Link = 0x200 + 1; s.Flags = 1; }
            return s;
        }

        /// <summary>The static part of the record (8 bytes) as the game lays it out (include/object.h).</summary>
        public void Write(byte[] rec, int z)
        {
            int w0 = (Item & 0x1FF) | (Flags & 7) << 9 | (Enchanted ? 0x1000 : 0) | (Invisible ? 0x4000 : 0) | (IsQuantity ? 0x8000 : 0);
            int w1 = (Math.Max(0, Math.Min(127, z)) & 0x7F) | (Heading & 7) << 7 | (FineY & 7) << 10 | (FineX & 7) << 13;
            int w2 = (Quality & 0x3F);
            int w3 = (Owner & 0x3F) | (IsQuantity ? (Link & 0x3FF) << 6 : 0);
            Put(rec, 0, w0); Put(rec, 2, w1); Put(rec, 4, w2); Put(rec, 6, w3);
        }

        static void Put(byte[] b, int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }

        public string Describe(ObjectNames names)
        {
            string n = names == null ? null : names.Get(Item);
            string q = IsQuantity && Link < 0x200 && Link > 1 ? " × " + Link : "";
            return string.Format("0x{0:X3} {1}{2}{3}", Item, n ?? "item", q, Enchanted ? " (enchanted)" : "");
        }
    }

    /// <summary>One enchantment an item can carry, and how it is stored.</summary>
    public sealed class EnchantmentChoice
    {
        public string Name;
        /// <summary>The link value (0x200 + special) with Enchanted set and IsQuantity.</summary>
        public int Link;
        /// <summary>Flag 11: the special is one of the 64 player spells (major class -1).</summary>
        public bool Flag11;
        /// <summary>What it really does, where the name says otherwise (EnchantmentEffects).</summary>
        public string Effect;
        public string Group;
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// The enchantments of is-quantity objects, as decode_obj_spell (obj/USEITEMS.C) reads them:
    /// with flag 11 clear the special's major class is (link &amp; 0x1FF) &gt;&gt; 4 and the effect its low 4
    /// bits; class 12 is the weapon and armour enchantments, named block 6 0x1C0 + effect (+ 0x10
    /// for armour, item minor class above 1); other classes are named block 6 class × 16 + effect.
    /// With flag 11 set the special is one of the 64 spells, named block 6 0x100 + spell. A wand's
    /// spell is a spell object (0x120) inside it, with flag 11, link 0x200 + spell, quality the charges.
    /// </summary>
    public static class Enchantments
    {
        public static bool IsArmourLike(int item) { return ((item >> 4) & 3) > 1 && item < 0x40; }

        public static List<EnchantmentChoice> WeaponOrArmour(GameKind kind, int item, GameStrings strings)
        {
            var list = new List<EnchantmentChoice>();
            bool armour = IsArmourLike(item);
            var c = ItemCategories.Of(item, kind);
            bool shieldOrRing = (c & (ItemCategory.Shield | ItemCategory.Ring)) != 0;
            for (int e = 0; e < 16; e++)
            {
                int nameIndex = 0x1C0 + (armour ? 0x10 : 0) + e;
                string name = strings.Get(6, nameIndex);
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new EnchantmentChoice
                {
                    Name = name.Trim(), Link = 0x200 | 0xC0 | e, Group = armour ? "Armour enchantments" : "Weapon enchantments",
                    Effect = EnchantmentEffects.Describe(kind, nameIndex, shieldOrRing),
                });
            }
            return list;
        }

        /// <summary>The spells an object can hold (block 6 0x100-0x13F), for an enchanted object (flag 11) or a wand's spell object.</summary>
        public static List<EnchantmentChoice> Spells(GameStrings strings)
        {
            var list = new List<EnchantmentChoice>();
            for (int s = 0; s < 64; s++)
            {
                string name = strings.Get(6, 0x100 + s);
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new EnchantmentChoice { Name = name.Trim(), Link = 0x200 | s, Flag11 = true, Group = "Spells (circle " + (s / 8 + 1) + ")" });
            }
            return list;
        }

        /// <summary>Effects by major class (block 6 class × 16 + effect, classes 0-11 and 13-15 but 9, the curses): what rings and some items carry.</summary>
        public static List<EnchantmentChoice> ClassEffects(GameStrings strings)
        {
            var list = new List<EnchantmentChoice>();
            for (int i = 0; i < 0x100; i++)
            {
                int major = i >> 4;
                if (major == 9 || major == 12) continue;
                string name = strings.Get(6, i);
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new EnchantmentChoice { Name = name.Trim(), Link = 0x200 | i, Group = "Effects (class " + major + ")" });
            }
            return list;
        }

        /// <summary>The name of the enchantment an object's fields describe, or null.</summary>
        public static string NameOf(int item, bool enchanted, bool isQuantity, int link, bool flag11, GameStrings strings)
        {
            if (!enchanted || !isQuantity || link < 0x200) return null;
            int major, effect;
            if (flag11) { major = (link & 0x1FF) >> 6; effect = link & 0x3F; return major == 0 ? strings.Get(6, 0x100 + effect) : null; }
            major = (link & 0x1FF) >> 4; effect = link & 0xF;
            if (major == 12) return strings.Get(6, 0x1C0 + (IsArmourLike(item) ? 0x10 : 0) + effect);
            if (major == 9) return "a curse";
            return strings.Get(6, (major << 4) + effect);
        }

        public static string Uw2Warning
        {
            get
            {
                return "UW2 names its enchantments differently from UW1 (its own STRINGS.PAK block 6), and some names do not match what the game does: " +
                    "the four weapon \"Accuracy\" enchantments add damage and the four \"Damage\" ones add to hit (COMBAT.C DoPlayerWeapon swaps them). " +
                    "The names shown here are the game's; the line under each says what it really does.";
            }
        }
    }
}
