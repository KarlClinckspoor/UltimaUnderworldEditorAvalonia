using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Randomizer;

namespace UwEditor.Core.Editing
{
    /// <summary>What the item cleaner may remove (several at once).</summary>
    [Flags]
    public enum CleanKind
    {
        None = 0,
        /// <summary>Piles of debris, rubble, wood chips, pieces of wood.</summary>
        Debris = 1 << 0,
        /// <summary>Bones, skulls, piles of bones, the dead rotworm.</summary>
        Bones = 1 << 1,
        /// <summary>Broken weapons and shields, blood stains, wax.</summary>
        Junk = 1 << 2,
        /// <summary>Plants, grass and vines that lie as objects.</summary>
        Plants = 1 << 3,
        Food = 1 << 4,
        /// <summary>Weapons, armour, shields, rings and crowns.</summary>
        Gear = 1 << 5,
        /// <summary>Gems, coins and other treasure.</summary>
        Treasure = 1 << 6,
        Potions = 1 << 7,
        /// <summary>Torches, candles and lanterns.</summary>
        Lights = 1 << 8,
        /// <summary>Every item that can be picked up (what the other kinds name, and the rest).</summary>
        AllLoose = 1 << 9,
    }

    /// <summary>The cleaner's settings.</summary>
    public sealed class CleanOptions
    {
        public CleanKind Kinds = CleanKind.Debris | CleanKind.Bones;
        /// <summary>Also what lies inside containers (bags, chests, barrels) on the map.</summary>
        public bool InsideContainers;
        /// <summary>Also what creatures without a conversation carry.</summary>
        public bool CreatureInventories;
        /// <summary>Only the tiles from (X0, Y0) to (X1, Y1).</summary>
        public int X0 = 0, Y0 = 0, X1 = 63, Y1 = 63;
        /// <summary>Only items whose name holds this (empty: any).</summary>
        public string Name = "";
    }

    /// <summary>One object the cleaner would remove.</summary>
    public sealed class CleanCandidate
    {
        public int Level { get; set; }
        public int Index { get; set; }
        public int Item { get; set; }
        public string Name { get; set; }
        public int TileX { get; set; }
        public int TileY { get; set; }
        /// <summary>The object it lies in, or empty.</summary>
        public string Inside { get; set; }
        public override string ToString() { return string.Format("L{0} #{1} {2} ({3}, {4}){5}", Level, Index, Name, TileX, TileY, Inside.Length > 0 ? " in " + Inside : ""); }
    }

    /// <summary>What a cleaner run would remove, and what of the same kinds it keeps and why.</summary>
    public sealed class CleanPlan
    {
        public List<CleanCandidate> Remove { get; } = new List<CleanCandidate>();
        /// <summary>Kept objects of the chosen kinds, counted by reason.</summary>
        public SortedDictionary<string, int> Kept { get; } = new SortedDictionary<string, int>();
        /// <summary>Removed objects counted by name.</summary>
        public SortedDictionary<string, int> ByName { get; } = new SortedDictionary<string, int>();

        internal void Keep(string why) { int n; Kept.TryGetValue(why, out n); Kept[why] = n + 1; }
    }

    /// <summary>
    /// Removes objects by kind from levels (the Unity editor's item cleaner, Extensions/UI/FixesWindow.cs),
    /// never touching what the game or a quest needs: keys, locks and quest items (KeyItems,
    /// ItemCategories), talismans, runestones, books and scrolls with a text, maps, anything a trap
    /// or trigger names and what lies inside it (create-object templates, delete targets), the item
    /// kinds an inventory trap asks for (0x18C, item quality &lt;&lt; 5 | owner; event/TRIGGER.C UseTrap),
    /// the puzzle pieces the game finds by kind (UW1's emeralds 0xA7, UW2's storage crystals 0xA1),
    /// UW2's curious implement and lockpick (obj/OBJUSE.C UseKey), Garamon's bones (UW1 bones with
    /// owner 0x3E; obj/USEITEMS.C UseBonesOn), objects that hold something (contents, triggers, a
    /// lock), what characters with a conversation carry, and creatures, doors, furniture, decals,
    /// switches, traps and triggers. Removing goes through <see cref="LevelEditor.DeleteObject"/>,
    /// one undoable step a level.
    /// </summary>
    public static class ItemCleaner
    {
        /// <summary>The kinds with their labels, for the window.</summary>
        public static readonly KeyValuePair<CleanKind, string>[] Choices =
        {
            new KeyValuePair<CleanKind, string>(CleanKind.Debris, "Debris (rubble, piles of debris, wood chips, pieces of wood)"),
            new KeyValuePair<CleanKind, string>(CleanKind.Bones, "Bones and skulls"),
            new KeyValuePair<CleanKind, string>(CleanKind.Junk, "Junk (broken weapons, blood stains, wax)"),
            new KeyValuePair<CleanKind, string>(CleanKind.Plants, "Plants and grass"),
            new KeyValuePair<CleanKind, string>(CleanKind.Food, "Food and drink"),
            new KeyValuePair<CleanKind, string>(CleanKind.Gear, "Weapons and armour"),
            new KeyValuePair<CleanKind, string>(CleanKind.Treasure, "Treasure"),
            new KeyValuePair<CleanKind, string>(CleanKind.Potions, "Potions"),
            new KeyValuePair<CleanKind, string>(CleanKind.Lights, "Lights"),
            new KeyValuePair<CleanKind, string>(CleanKind.AllLoose, "Every item that can be picked up"),
        };

        // the scenery range 0xC0-0xDF by kind (include/items.h; string block 4 names)
        static readonly int[] Uw1Debris = { 0xCC, 0xCD, 0xD0, 0xD1, 0xD2, 0xD5, 0xD6, 0xDA, 0xDB };
        static readonly int[] Uw1Bones = { 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xD9, 0xDC };
        static readonly int[] Uw1Junk = { 0xC8, 0xC9, 0xCA, 0xCB, 0xDD, 0xDE, 0xDF };
        static readonly int[] Uw1Plants = { 0xC0, 0xC1, 0xC7, 0xCE, 0xCF, 0xD4 };
        static readonly int[] Uw2Debris = { 0xCC, 0xCD, 0xD0, 0xD1, 0xD6, 0xDB, 0xDC };
        static readonly int[] Uw2Bones = { 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xDD };
        static readonly int[] Uw2Junk = { 0xC7, 0xC8, 0xC9, 0xCA, 0xCB, 0xD2, 0xDE, 0xDF };
        static readonly int[] Uw2Plants = { 0xC0, 0xC1, 0xCE, 0xCF, 0xD4, 0xD9, 0xDA };

        /// <summary>The cleaner kind of an item id (None when it is in none but All loose items).</summary>
        public static CleanKind KindOf(int item, GameKind game)
        {
            bool uw2 = game == GameKind.UW2;
            if ((uw2 ? Uw2Debris : Uw1Debris).Contains(item)) return CleanKind.Debris;
            if ((uw2 ? Uw2Bones : Uw1Bones).Contains(item)) return CleanKind.Bones;
            if ((uw2 ? Uw2Junk : Uw1Junk).Contains(item)) return CleanKind.Junk;
            if ((uw2 ? Uw2Plants : Uw1Plants).Contains(item)) return CleanKind.Plants;
            var c = ItemCategories.Of(item, game);
            if ((c & ItemCategory.Food) != 0) return CleanKind.Food;
            if ((c & (ItemCategory.Weapon | ItemCategory.Wearable)) != 0) return CleanKind.Gear;
            if ((c & ItemCategory.Treasure) != 0) return CleanKind.Treasure;
            if ((c & ItemCategory.Potion) != 0) return CleanKind.Potions;
            if ((c & ItemCategory.Light) != 0) return CleanKind.Lights;
            return CleanKind.None;
        }

        /// <summary>The item kinds kept on every level: what inventory traps ask for, the puzzle pieces found by kind, UW2's lock tools.</summary>
        static HashSet<int> GuardedKinds(GameSession s, Func<int, LevelModel> level)
        {
            var set = new HashSet<int>();
            set.Add(s.IsUw2 ? 0xA1 : 0xA7);
            if (s.IsUw2) { set.Add(0x100); set.Add(0x101); }
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                    if (!o.Free && o.TileX >= 0 && o.Item == 0x18C) set.Add((o.Quality << 5 | o.Owner) & 0x1FF);
            }
            return set;
        }

        /// <summary>Objects a trap or trigger links to, what lies inside them, and what lies inside traps and triggers (templates, targets).</summary>
        static HashSet<int> Named(LevelModel m)
        {
            var set = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || !o.IsTrapOrTrigger) continue;
                if (o.Link > 0) stack.Push(o.Link);
                foreach (int c in o.Children) stack.Push(c);
            }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (i <= 0 || i >= m.Objects.Length || !set.Add(i)) continue;
                foreach (int c in m.Objects[i].Children) stack.Push(c);
            }
            return set;
        }

        /// <summary>Why an object is never removed, or null when it may be.</summary>
        public static string Protection(GameSession s, LevelModel m, ObjectInfo o, HashSet<int> named, HashSet<int> guarded, CleanOptions opt)
        {
            if (o.Item >= 0x140) return "fixed object (door, furniture, decal, switch, trap, trigger)";
            if (o.IsCritter) return "creature";
            string kind;
            if (KeyItems.Classify(s, o, out kind) != null) return kind == "key" ? "key" : kind;
            var c = ItemCategories.Of(o.Item, s.Kind);
            if ((c & (ItemCategory.Key | ItemCategory.Lock | ItemCategory.QuestKey | ItemCategory.Quest)) != 0) return "key, lock or quest item";
            if ((c & (ItemCategory.Book | ItemCategory.Scroll)) != 0 && (ObjectTexts.TextOf(s, o) != null || (s.IsUw2 && o.Item >= 0x138 && o.Item <= 0x13A))) return "book, scroll or map with a text";
            if (!s.IsUw2 && Uw1Bones.Contains(o.Item) && o.Owner == 0x3E) return "Garamon's bones";
            if (guarded.Contains(o.Item)) return "a kind the game looks for (inventory trap, puzzle)";
            if (named.Contains(o.Index)) return "named by a trap or trigger";
            if (!o.IsQuantity && o.Link > 0) return "holds something (contents, a trigger, a lock)";
            if (o.Parent >= 0)
            {
                // what a character with a conversation carries may be asked for or given in it
                for (int p = o.Parent, guard = 0; p >= 0 && guard++ < 64; p = m.Objects[p].Parent)
                {
                    var h = m.Objects[p];
                    if (h.IsCritter) return ObjectTexts.NpcName(s, h) != null || (h.Record.Length > 0x1A && h.Record[0x1A] > 0) ? "carried by a character who talks" : opt.CreatureInventories ? null : "carried by a creature";
                }
                if (!opt.InsideContainers) return "inside a container";
            }
            return null;
        }

        /// <summary>What the cleaner would remove from the given levels (as edited: pass LevelEdits.Model).</summary>
        public static CleanPlan Preview(GameSession s, Func<int, LevelModel> level, IEnumerable<int> levels, CleanOptions opt)
        {
            var plan = new CleanPlan();
            var guarded = GuardedKinds(s, level);
            string name = (opt.Name ?? "").Trim();
            foreach (int l in levels)
            {
                LevelModel m;
                try { m = level(l); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                var named = Named(m);
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.Index < 2) continue;
                    if (o.TileX < opt.X0 || o.TileX > opt.X1 || o.TileY < opt.Y0 || o.TileY > opt.Y1) continue;
                    if (name.Length > 0 && o.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var kind = KindOf(o.Item, s.Kind);
                    bool loose = (opt.Kinds & CleanKind.AllLoose) != 0 && o.Item < 0x140 && !o.IsCritter && (Races.ComObjByte(s, o.Item, 3) & 0x20) != 0;
                    if ((kind & opt.Kinds) == 0 && !loose) continue;
                    string why = Protection(s, m, o, named, guarded, opt);
                    if (why != null) { plan.Keep(why); continue; }
                    plan.Remove.Add(new CleanCandidate
                    {
                        Level = l, Index = o.Index, Item = o.Item, Name = o.Name, TileX = o.TileX, TileY = o.TileY,
                        Inside = o.Parent >= 0 ? m.Objects[o.Parent].ToString() : "",
                    });
                    int n; plan.ByName.TryGetValue(o.Name, out n); plan.ByName[o.Name] = n + 1;
                }
            }
            return plan;
        }

        /// <summary>
        /// Removes the plan's objects, one undoable step a level (a batch of deletes). An object that
        /// changed since the preview (freed, or another item there now) is left alone. Returns the
        /// number removed.
        /// </summary>
        public static int Apply(LevelEdits edits, CleanPlan plan)
        {
            int removed = 0;
            foreach (var g in plan.Remove.GroupBy(c => c.Level))
            {
                var e = edits.For(g.Key);
                e.BeginBatch("Item cleaner: " + g.Count() + " objects");
                try
                {
                    // deepest first, so a container's contents go before anything they lie in
                    foreach (var c in g.OrderByDescending(c => c.Inside.Length > 0).ThenByDescending(c => c.Index))
                    {
                        var o = e.Model.Objects[c.Index];
                        if (o.Free || o.TileX < 0 || o.Item != c.Item) continue;
                        e.DeleteObject(c.Index);
                        removed++;
                    }
                }
                finally { e.EndBatch(); }
            }
            return removed;
        }
    }
}
