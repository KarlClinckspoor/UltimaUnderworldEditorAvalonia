using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Player
{
    /// <summary>
    /// An object of the player's inventory: an 8-byte static object record (word 0 item id
    /// bits 0-8, flags 9-11, enchanted 12, door direction 13, invisible 14, quantity 15;
    /// word 1 position; word 2 quality 0-5 and next 6-15; word 3 owner 0-5 and link or special
    /// 6-15), with the objects inside it when its link is a list.
    /// </summary>
    public sealed class InventoryItem
    {
        public readonly byte[] Record = new byte[8];
        public readonly List<InventoryItem> Contents = new List<InventoryItem>();
        public InventoryItem Parent;
        /// <summary>The inventory slot it is in (0-18), or -1.</summary>
        public int Slot = -1;
        /// <summary>Its number in the file it was read from (0 for a new one).</summary>
        public int Number;

        int W(int i) { return Record[2 * i] | Record[2 * i + 1] << 8; }
        void SetW(int i, int v) { Record[2 * i] = (byte)v; Record[2 * i + 1] = (byte)(v >> 8); }

        public int ItemId { get { return W(0) & 0x1FF; } set { Check(value, 0x1FF); SetW(0, (W(0) & ~0x1FF) | value); } }
        public int Flags { get { return (W(0) >> 9) & 7; } set { Check(value, 7); SetW(0, (W(0) & ~(7 << 9)) | value << 9); } }
        public bool Enchanted { get { return (W(0) & 0x1000) != 0; } set { SetW(0, value ? W(0) | 0x1000 : W(0) & ~0x1000); } }
        public bool Invisible { get { return (W(0) & 0x4000) != 0; } set { SetW(0, value ? W(0) | 0x4000 : W(0) & ~0x4000); } }
        /// <summary>The link is a quantity or a special value, not a list of objects.</summary>
        public bool IsQuantity
        {
            get { return (W(0) & 0x8000) != 0; }
            set
            {
                if (!value == !IsQuantity) return;
                if (value && Contents.Count > 0) throw new InvalidOperationException("Empty the container first: its link holds its contents.");
                SetW(0, value ? W(0) | 0x8000 : W(0) & ~0x8000);
                if (!value) Link = 0;
            }
        }
        public int Quality { get { return W(2) & 0x3F; } set { Check(value, 0x3F); SetW(2, (W(2) & ~0x3F) | value); } }
        public int Owner { get { return W(3) & 0x3F; } set { Check(value, 0x3F); SetW(3, (W(3) & ~0x3F) | value); } }
        /// <summary>With IsQuantity: a quantity below 0x200, or 0x200 + a special number (a spell, a text). Otherwise the contents, written on saving.</summary>
        public int Link
        {
            get { return (W(3) >> 6) & 0x3FF; }
            set { Check(value, 0x3FF); SetW(3, (W(3) & 0x3F) | value << 6); }
        }
        internal int Next { get { return (W(2) >> 6) & 0x3FF; } set { SetW(2, (W(2) & 0x3F) | value << 6); } }

        /// <summary>A quantity, when the link is one (below 0x200).</summary>
        public int? Quantity { get { return IsQuantity && Link < 0x200 ? Link : (int?)null; } }

        static void Check(int v, int max)
        {
            if (v < 0 || v > max) throw new ArgumentOutOfRangeException("value", "0-" + max);
        }

        public IEnumerable<InventoryItem> All()
        {
            yield return this;
            foreach (var c in Contents) foreach (var x in c.All()) yield return x;
        }
    }

    /// <summary>
    /// The inventory part of PLAYER.DAT (inv/INVSAVE.C, docs/FORMATS.md "PLAYER.DAT"): a count
    /// word (objects + 1), then the workspace: the player object (0x1B bytes, its link at
    /// word 3 the head of the inventory list), the object on the cursor (8, ignored on
    /// loading), 0x1C slot words (0-18 used: 0-4 armour, 5-6 shoulders, 7-8 hands, 9-10 rings,
    /// 11-18 the pack; a slot holds an object number in bits 6-15) and the objects, 8 bytes
    /// each, numbered from 1, with links renumbered to those numbers. Saving numbers them the
    /// way InvSaveNexts does: depth first, an object's contents right after it.
    /// </summary>
    public sealed class PlayerInventory
    {
        public const int Slots = 19, PackFirst = 11, PackLast = 18;
        public static readonly string[] SlotNames =
        {
            "head", "body", "hands (gloves)", "legs", "feet", "shoulder 1", "shoulder 2", "hand 1", "hand 2", "ring 1", "ring 2",
            "pack 1", "pack 2", "pack 3", "pack 4", "pack 5", "pack 6", "pack 7", "pack 8",
        };

        public readonly List<InventoryItem> Top = new List<InventoryItem>();
        /// <summary>Objects in the file that no list reaches (the cursor object's contents, say); dropped when the inventory is rewritten.</summary>
        public int Unreached { get; private set; }
        public bool Changed { get; set; }
        byte[] slotWords = new byte[0x38];
        byte[] object0 = new byte[8];

        public static PlayerInventory Parse(byte[] rest)
        {
            var inv = new PlayerInventory();
            if (rest == null || rest.Length < 2 + 0x5B) return inv;
            int count = rest[0] | rest[1] << 8;
            const int ws = 2;
            Array.Copy(rest, ws + 0x23, inv.slotWords, 0, 0x38);
            Array.Copy(rest, ws + 0x5B, inv.object0, 0, Math.Min(8, rest.Length - ws - 0x5B));
            int objects = Math.Max(0, Math.Min(count - 1, (rest.Length - ws - 0x5B) / 8 - 1));
            var byNumber = new Dictionary<int, InventoryItem>();
            var seen = new HashSet<int>();
            int head = (rest[ws + 6] | rest[ws + 7] << 8) >> 6;
            inv.ReadList(rest, head, objects, null, inv.Top, byNumber, seen);
            for (int s = 0; s < Slots; s++)
            {
                int n = (inv.slotWords[2 * s] | inv.slotWords[2 * s + 1] << 8) >> 6;
                InventoryItem it;
                if (n != 0 && byNumber.TryGetValue(n, out it)) it.Slot = s;
            }
            inv.Unreached = objects - seen.Count;
            return inv;
        }

        void ReadList(byte[] rest, int n, int objects, InventoryItem parent, List<InventoryItem> into, Dictionary<int, InventoryItem> byNumber, HashSet<int> seen)
        {
            while (n > 0 && n <= objects && seen.Add(n))
            {
                var it = new InventoryItem { Number = n, Parent = parent };
                Array.Copy(rest, 2 + 0x5B + 8 * n, it.Record, 0, 8);
                byNumber[n] = it;
                into.Add(it);
                if (!it.IsQuantity && it.Link != 0) ReadList(rest, it.Link, objects, it, it.Contents, byNumber, seen);
                n = it.Next;
            }
        }

        public IEnumerable<InventoryItem> All() { return Top.SelectMany(t => t.All()); }

        public InventoryItem InSlot(int slot) { return Top.FirstOrDefault(t => t.Slot == slot); }

        public int FreePackSlot()
        {
            for (int s = PackFirst; s <= PackLast; s++) if (InSlot(s) == null) return s;
            return -1;
        }

        /// <summary>
        /// A new object: in a container (null: the top of the inventory, in a free pack slot).
        /// Containers (item ids 0x80-0x8F) get an empty list; anything else a quantity of 1.
        /// </summary>
        public InventoryItem Add(int itemId, InventoryItem container, bool isContainer)
        {
            var it = new InventoryItem();
            it.ItemId = itemId;
            it.Quality = 0x28;                 // what the game's own saves have for new objects
            if (!isContainer) { it.IsQuantity = true; it.Link = 1; }
            if (container == null)
            {
                int s = FreePackSlot();
                if (s < 0) throw new InvalidOperationException("The pack is full (slots 11-18): put it in a container, or free a slot.");
                it.Slot = s;
                Top.Add(it);
            }
            else
            {
                if (container.IsQuantity) throw new InvalidOperationException("That object is not a container: its link is a quantity or special value.");
                it.Parent = container;
                container.Contents.Add(it);
            }
            Changed = true;
            return it;
        }

        /// <summary>Takes an object (and whatever is inside it) out of the inventory.</summary>
        public void Remove(InventoryItem it)
        {
            var list = it.Parent == null ? Top : it.Parent.Contents;
            if (!list.Remove(it)) return;
            it.Parent = null;
            it.Slot = -1;
            Changed = true;
        }

        /// <summary>Puts a top-level object in a slot (or -1 for none); whatever was there loses it.</summary>
        public void SetSlot(InventoryItem it, int slot)
        {
            if (it.Parent != null) throw new InvalidOperationException("Only objects at the top of the inventory sit in slots.");
            if (slot < -1 || slot >= Slots) throw new ArgumentOutOfRangeException("slot");
            if (slot >= 0) { var other = InSlot(slot); if (other != null && other != it) other.Slot = -1; }
            it.Slot = slot;
            Changed = true;
        }

        public List<string> Problems()
        {
            var p = new List<string>();
            foreach (var t in Top)
                if (t.Slot < 0) p.Add(string.Format("Object 0x{0:X3} is at the top of the inventory but in no slot: the game shows it nowhere.", t.ItemId));
            if (Unreached > 0) p.Add(Unreached + " object(s) in the file are on no list (the cursor's, say); saving the inventory drops them.");
            int count = All().Count();
            if (count > 0x3FE) p.Add("More objects than a link can number (1023).");
            return p;
        }

        /// <summary>
        /// The count word and workspace, renumbered (InvSaveNexts' order). Unchanged, it is the
        /// bytes given. The player and cursor objects come from current, so a vitality set in the
        /// player object stays.
        /// </summary>
        public byte[] Serialize(byte[] current)
        {
            if (!Changed) return current;
            var objs = new List<InventoryItem>();
            int first = Number(Top, objs);
            var o = new byte[2 + 0x5B + 8 * (objs.Count + 1)];
            int count = objs.Count + 1;
            o[0] = (byte)count; o[1] = (byte)(count >> 8);
            Array.Copy(current, 2, o, 2, 0x23);                 // the player object and the cursor object
            int link = (o[2 + 6] | o[2 + 7] << 8) & 0x3F | first << 6;
            o[2 + 6] = (byte)link; o[2 + 7] = (byte)(link >> 8);
            // slots: 0-18 from the objects; 19-27 (an open bag's view, rebuilt by the game) cleared
            foreach (var t in Top)
                if (t.Slot >= 0)
                {
                    int w = t.Number << 6;
                    o[2 + 0x23 + 2 * t.Slot] = (byte)w; o[2 + 0x23 + 2 * t.Slot + 1] = (byte)(w >> 8);
                }
            Array.Copy(object0, 0, o, 2 + 0x5B, 8);
            foreach (var it in objs) Array.Copy(it.Record, 0, o, 2 + 0x5B + 8 * it.Number, 8);
            return o;
        }

        /// <summary>Numbers a list depth first, sets the next and link fields, returns the first number (0 for none).</summary>
        static int Number(List<InventoryItem> list, List<InventoryItem> into)
        {
            InventoryItem prev = null;
            int first = 0;
            foreach (var it in list)
            {
                into.Add(it);
                it.Number = into.Count;
                if (prev == null) first = it.Number; else prev.Next = it.Number;
                if (!it.IsQuantity) it.Link = Number(it.Contents, into);
                prev = it;
            }
            if (prev != null) prev.Next = 0;
            return first;
        }
    }
}
