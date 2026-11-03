using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    public sealed partial class ShuffleOptions
    {
        // ---- the item swap's own settings (unused by the property shuffle)

        /// <summary>
        /// Swap: an object put into a spot takes the spot's owner, where the owner field is an
        /// ownership on both the object that was there and the one put there (see
        /// <see cref="ItemShuffle.Ownable"/>); otherwise the object keeps its own owner field.
        /// </summary>
        public bool SpotOwnership { get; set; } = true;
        /// <summary>Swap: where objects may go. "game" (anywhere, the default), "world" (UW2: the same world; UW1 has one world, the whole game), "level" (the same level), "near" (within <see cref="SwapRange"/> levels, and in UW2 the same world).</summary>
        public string SwapScope { get; set; } = "game";
        /// <summary>Swap, scope "near": how many levels up or down an object may go.</summary>
        public int SwapRange { get; set; } = 1;
        /// <summary>Swap: skulls, bones and piles of bones you can pick up take part (never Garamon's bones).</summary>
        public bool SwapBones { get; set; }
        /// <summary>Swap: the other scenery you can pick up takes part (broken weapons, wood, debris, plants, the dead rotworm…).</summary>
        public bool SwapScenery { get; set; }
        /// <summary>Swap: after the main swap, the enchantments trade places among the objects of each class.</summary>
        public bool SwapEnchantments { get; set; }
        public bool EnchantWeapons { get; set; } = true;
        public bool EnchantArmour { get; set; } = true;
        public bool EnchantScrolls { get; set; } = true;
        /// <summary>Swap: objects kept where they are, like the ignored ones (the starting equipment). Null: none.</summary>
        public List<IgnoredObject> Kept { get; set; }
    }

    public static partial class ItemShuffle
    {
        /// <summary>The bones, skulls and piles of bones the player can pick up (include/items.h 0xC2-0xC6, both games).</summary>
        public static bool IsBones(int item) { return item >= 0xC2 && item <= 0xC6; }

        /// <summary>
        /// Garamon's bones: bones whose owner field is 0x3E. Used on his grave they bury him and
        /// end his part of the plot (uw1 obj/USEITEMS.C UseBonesOn tests OBJ_OWNER == 0x3E);
        /// in the shipped data the pile of bones #820 on level 8 (2, 15). Underworld II's
        /// UseBonesOn has no such test, but its one pile of bones with owner 0x3E (the Tomb of
        /// Praecor Loth, level 49 #959) carries the same mark and is kept as well.
        /// </summary>
        public static bool IsMarkedBones(ObjectInfo o) { return IsBones(o.Item) && o.Owner == 0x3E; }

        /// <summary>
        /// Scenery the swap can move: items 0xC0-0xDF that can be picked up (COMOBJ.DAT byte 3
        /// bit 5, as Clean up items reads it), "bones" or "scenery"; the pole is a tool, and
        /// Garamon's bones never move. Null when it is none of them.
        /// </summary>
        static string SceneryKind(GameSession s, ObjectInfo o)
        {
            int it = o.Item;
            if (it < 0xC0 || it >= 0xE0 || it == ItemTables.Pole) return null;
            if ((Races.ComObjByte(s, it, 3) & 0x20) == 0) return null;
            if (IsMarkedBones(o)) return null;
            return IsBones(it) ? "bones" : "scenery";
        }

        /// <summary>
        /// Whether an item's owner field is an ownership: the race it belongs to, which makes
        /// taking it stealing. The games read it so only for items whose COMOBJ.DAT can-own bit
        /// is set (obj/LOOK.C "belonging to", critter/CRITTIME.C player_grabbed, both games). For
        /// other objects the 6 bits mean something else: a key's lock number (obj/USEITEMS.C
        /// checkLock; keys are excluded by name too), the creature whose bones they are and
        /// Garamon's mark (LOOK.C BonesLook, USEITEMS.C UseBonesOn), a gravestone's or writing's
        /// text (LOOK.C), a trap's or trigger's target tile y and text number (event/TRIGGER.C),
        /// a creature's home y (critter/AI.C), a door's spike bit. Fixed objects (0x180 and up)
        /// never count.
        /// </summary>
        public static bool Ownable(GameSession s, int item)
        {
            return item < 0x180 && !KeyIndex.IsKey(item, s.Kind) && Races.CanBeOwned(s, item);
        }

        /// <summary>
        /// The tiles of the starting equipment on level 1, by game, as the shipped levels hold
        /// it. UW1: the sack on (33, 3), beside the new-game start square (32, 2), with a dagger,
        /// a map, bread, a fish, a torch and an apple. UW2: in the Avatar's quarters (start
        /// square (19, 48)) the pile on (20, 49), a sack with a map, thread, a torch and a
        /// dagger, a leather vest and leather boots; and the closet behind the secret door at
        /// (19, 50), tiles (18-19, 51-52): the pack (dagger, bedroll, water, bread, torch,
        /// lockpick), the box with the sling and its stones, the rune bag and the Ort, Bet, In,
        /// Jux and Sanct stones, a hand axe and a flute.
        /// </summary>
        static (int X, int Y)[] StartTiles(GameSession s)
        {
            return s.IsUw2 ? new[] { (20, 49), (18, 51), (19, 51), (18, 52), (19, 52) } : new[] { (33, 3) };
        }

        /// <summary>The starting equipment the swap can move: the swap's candidates (with scenery) on the start tiles of level 1, contents included.</summary>
        public static List<IgnoredObject> StartingEquipment(GameSession s, Func<int, LevelModel> level)
        {
            var tiles = new HashSet<(int, int)>(StartTiles(s));
            return Candidates(s, level, true).Where(c => c.Level == 1 && OnTiles(level(1), c.Index, tiles)).ToList();
        }

        static bool OnTiles(LevelModel m, int index, HashSet<(int, int)> tiles)
        {
            var o = m.Objects[index];
            return tiles.Contains((o.TileX, o.TileY));
        }

        /// <summary>The scope in words, for the report and the formula.</summary>
        public static string ScopeText(GameSession s, ShuffleOptions opt)
        {
            switch (opt.SwapScope)
            {
                case "level": return "within each level";
                case "world": return s.IsUw2 ? "within each world" : "within the world (Ultima Underworld has one: every level)";
                case "near": return string.Format("within ±{0} level{1}{2}", opt.SwapRange, opt.SwapRange == 1 ? "" : "s", s.IsUw2 ? " of the same world" : "");
                default: return "across the whole game";
            }
        }

        /// <summary>Passes of the ±N swap (see <see cref="Permutation"/>).</summary>
        public const int NearPasses = 6;

        /// <summary>
        /// A permutation p of the spots (levels[i] is spot i's level): the object of spot p[i]
        /// goes to spot i. "game": one Fisher-Yates shuffle of every spot. "level" / "world":
        /// a Fisher-Yates shuffle within each level / world, in order. "near": within each world
        /// (UW1: the whole game), <see cref="NearPasses"/> passes; in each, every spot a (in a
        /// random order) picks a spot b uniformly among the spots at most N levels from it, and a
        /// and b trade their objects when both objects then lie at most N levels from where they
        /// started. Every object keeps to its range; the same seed gives the same permutation.
        /// Public for the tests.
        /// </summary>
        public static int[] Permutation(Random rng, GameSession s, IList<int> levels, ShuffleOptions opt)
        {
            int n = levels.Count;
            var p = Enumerable.Range(0, n).ToArray();
            string scope = opt.SwapScope ?? "game";
            if (scope == "game" || n < 2) { Shuffle(rng, p); return p; }
            Func<int, int> bucket = l => scope == "level" ? l : s.Profile.WorldOf(l);
            foreach (var b in Enumerable.Range(0, n).GroupBy(i => bucket(levels[i])).OrderBy(g => g.Key))
            {
                var idx = b.ToArray();
                if (scope != "near")
                {
                    var sub = idx.ToArray();
                    Shuffle(rng, sub);
                    for (int k = 0; k < idx.Length; k++) p[idx[k]] = sub[k];
                    continue;
                }
                int range = Math.Max(0, opt.SwapRange);
                // the spots of the world sorted by level, and where each level starts in that order
                var ord = idx.OrderBy(i => levels[i]).ThenBy(i => i).ToArray();
                Func<int, int> lowerBound = lv => { int lo = 0, hi = ord.Length; while (lo < hi) { int mid = (lo + hi) / 2; if (levels[ord[mid]] < lv) lo = mid + 1; else hi = mid; } return lo; };
                for (int pass = 0; pass < NearPasses; pass++)
                {
                    var order = idx.ToArray();
                    Shuffle(rng, order);
                    foreach (int a in order)
                    {
                        int from = lowerBound(levels[a] - range), to = lowerBound(levels[a] + range + 1);
                        if (to - from < 2) continue;
                        int bb = ord[from + rng.Next(to - from)];
                        if (bb == a) continue;
                        // the object now at a (from spot p[a]) would go to bb, and the one at bb to a
                        if (Math.Abs(levels[p[a]] - levels[bb]) > range || Math.Abs(levels[p[bb]] - levels[a]) > range) continue;
                        int t = p[a]; p[a] = p[bb]; p[bb] = t;
                    }
                }
            }
            return p;
        }

        /// <summary>The enchantment classes of the enchantment swap, by the item a spot holds after the main swap, or null.</summary>
        static string EnchantClass(GameSession s, ShuffleOptions opt, ItemRewrite r)
        {
            int it = r.Item;
            if (r.IsQuantity == false && r.Link != 0) return null;     // a list: contents, a spell object
            if (opt.EnchantWeapons)
            {
                if (ItemTables.KeptWeapons.Contains(it)) return null;
                if (ItemTables.Axes.Contains(it) || ItemTables.Swords(s.IsUw2).Contains(it) || ItemTables.Maces.Contains(it)) return "melee weapons";
                if (ItemTables.Launchers.Contains(it)) return "launchers";
            }
            if (opt.EnchantArmour && (ItemTables.Slot(it) >= 0 || ItemTables.ShieldIds.Contains(it))) return "armour";
            if (opt.EnchantScrolls && it >= 0x130 && it <= 0x13F && r.Enchanted && r.IsQuantity && (r.Flags & 4) != 0 && r.Link >= 0x200 && ((r.Link & 0x1FF) >> 6) == 0) return "spell scrolls";
            return null;
        }

        /// <summary>
        /// After the main swap: the enchantments (the enchanted bit, is-quantity, the link and
        /// flag 11, which together are the enchantment or its absence, obj/USEITEMS.C
        /// decode_obj_spell) trade places among the objects of each class, by the same scope:
        /// melee weapons, launchers (ammunition never), armour with shields, spell scrolls (which
        /// only trade spells, since every one has one). The objects themselves stay.
        /// </summary>
        static void SwapEnchantmentsAfter(GameSession s, ShuffleOptions opt, Random rng, List<Cand> pool, ShuffleResult res)
        {
            var classes = new Dictionary<string, List<Cand>>();
            foreach (var c in pool)
            {
                string k = EnchantClass(s, opt, c.R);
                if (k == null) continue;
                List<Cand> l;
                if (!classes.TryGetValue(k, out l)) classes[k] = l = new List<Cand>();
                l.Add(c);
            }
            res.Report.AppendLine(opt.IsProgression ? "Then the enchantments trade places within each class, by Uniform progression (curses and effects without degrees count " + Progression.MidLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) + "):" : "Then the enchantments trade places within each class:");
            if (classes.Count == 0) res.Report.AppendLine("  no weapons, armour or spell scrolls to swap.");
            foreach (var g in classes.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var list = g.Value;
                var units = list.Select(c => (c.R.Enchanted, c.R.IsQuantity, c.R.Link, F11: c.R.Flags & 4)).ToList();
                // Uniform progression: the enchantments sorted by their level (Progression.EnchantLevel), handed out level by level
                var p = opt.IsProgression
                    ? ProgressionOf(s, rng, res, g.Key + "' enchantments (level)", list.Select(c => c.Level).ToList(), list.Select((c, i) => Progression.EnchantLevel(s, c.R.Item, units[i].Enchanted, units[i].IsQuantity, units[i].Link, units[i].F11 != 0)).ToList())
                    : Permutation(rng, s, list.Select(c => c.Level).ToList(), opt);
                int moved = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var u = units[p[i]];
                    var c = list[i];
                    if (u.Enchanted != c.R.Enchanted || u.Link != c.R.Link || u.F11 != (c.R.Flags & 4) || u.IsQuantity != c.R.IsQuantity) moved++;
                    c.R.Enchanted = u.Enchanted; c.R.IsQuantity = u.IsQuantity; c.R.Link = u.Link; c.R.Flags = (c.R.Flags & ~4) | u.F11;
                    c.Touched = true;
                }
                res.Report.AppendFormat("  {0}: {1} objects, {2} enchanted, {3} changed.\n", g.Key, list.Count, units.Count(u => u.Enchanted && u.Link >= 0x200), moved);
            }
        }

        /// <summary>The objects the item swap can touch, scenery included when asked.</summary>
        public static List<IgnoredObject> Candidates(GameSession s, Func<int, LevelModel> level, bool scenery)
        {
            if (!scenery) return Candidates(s, level);
            var l = new List<IgnoredObject>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || o.TileX < 0 || o.IsMobile) continue;
                    if (o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger) continue;
                    if (KindOf(s, o, ((o.Record[1] >> 3) & 1) != 0, true) == null) continue;
                    l.Add(new IgnoredObject { Level = slot.Level, Index = o.Index, Label = Describe(s, m, o) });
                }
            }
            return l;
        }
    }
}
