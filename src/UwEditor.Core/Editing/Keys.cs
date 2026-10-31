using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>A key or a lock somewhere in the levels.</summary>
    public sealed class KeyPlace
    {
        public int Level;
        public int Index;
        public int Item;
        public string Name;
        public int TileX, TileY;
        /// <summary>For a lock: the door or chest it is in (its index), or -1. For a key: the container it is in, or -1.</summary>
        public int Holder = -1;
        public string HolderName;
        /// <summary>A lock's flag 9: locked.</summary>
        public bool Locked;
        /// <summary>A lock's flag 10: stays after unlocking (can be locked again).</summary>
        public bool Relockable;
        public override string ToString()
        {
            string where = string.Format("level {0}, ({1}, {2})", Level, TileX, TileY);
            return Holder >= 0 ? string.Format("#{0} {1} in #{2} {3}, {4}", Index, Name, Holder, HolderName, where) : string.Format("#{0} {1}, {2}", Index, Name, where);
        }
    }

    /// <summary>The keys and locks that share one lock number.</summary>
    public sealed class KeyGroup
    {
        public int Id { get; set; }
        public List<KeyPlace> Keys { get; } = new List<KeyPlace>();
        public List<KeyPlace> Locks { get; } = new List<KeyPlace>();
        /// <summary>The keys' description: STRINGS.PAK block 5, string 0x64 + the number (obj/LOOK.C KeyLook).</summary>
        public string Text { get; set; }
        public int TextIndex { get { return 0x64 + Id; } }
        public string Summary
        {
            get
            {
                string s = string.Format("Lock {0}: {1} key{2}, {3} lock{4}", Id, Keys.Count, Keys.Count == 1 ? "" : "s", Locks.Count, Locks.Count == 1 ? "" : "s");
                if (Keys.Count == 0 && Locks.Count > 0) s += " (no key in the levels)";
                if (Locks.Count == 0 && Keys.Count > 0) s += " (opens nothing in the levels)";
                return s;
            }
        }
    }

    /// <summary>
    /// The keys and their locks across the levels. A key fits a lock when its owner field (6 bits)
    /// equals the lock's link &amp; 0x1FF (obj/USEITEMS.C checkLock); the lock (0x10F) sits in its door's
    /// or chest's contents, flag 9 locked, flag 10 left in place after unlocking. Real keys are
    /// 0x100 and 0x102-0x10E in UW1, 0x102-0x10E in UW2 (0x101 is the lockpick, UW2's 0x100 the
    /// curious implement).
    /// </summary>
    public static class KeyIndex
    {
        public static bool IsKey(int item, GameKind kind)
        {
            if (item == 0x100) return kind == GameKind.UW1;
            return item >= 0x102 && item <= 0x10E;
        }

        public static List<KeyGroup> Build(GameSession s, Func<int, LevelModel> level, GameStrings strings = null)
        {
            var groups = new SortedDictionary<int, KeyGroup>();
            Func<int, KeyGroup> g = id => { KeyGroup k; if (!groups.TryGetValue(id, out k)) groups[id] = k = new KeyGroup { Id = id }; return k; };
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0) continue;
                    if (IsKey(o.Item, s.Kind))
                    {
                        var p = Place(m, o);
                        g(o.Owner).Keys.Add(p);
                    }
                    else if (o.Item == 0x10F && o.IsQuantity)
                    {
                        var p = Place(m, o);
                        int flags = (o.Record[1] >> 1) & 7;
                        p.Locked = (flags & 1) != 0;
                        p.Relockable = (flags & 2) != 0;
                        g(o.Link & 0x1FF).Locks.Add(p);
                    }
                }
            }
            strings = strings ?? s.Strings;
            foreach (var k in groups.Values) k.Text = strings.Get(5, k.TextIndex);
            return groups.Values.ToList();
        }

        static KeyPlace Place(LevelModel m, ObjectInfo o)
        {
            var p = new KeyPlace { Level = m.Level, Index = o.Index, Item = o.Item, Name = o.Name, TileX = o.TileX, TileY = o.TileY };
            if (o.Parent >= 0) { p.Holder = o.Parent; p.HolderName = m.Objects[o.Parent].Name; }
            return p;
        }
    }
}
