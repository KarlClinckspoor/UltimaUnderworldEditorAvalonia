using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.Core.Randomizer
{
    public static partial class Challenges
    {
        /// <summary>The run's state shared by the challenges.</summary>
        sealed class Ctx
        {
            public ChallengeWorld W;
            public GameSession S;
            public ChallengeOptions Opt;
            public Random Rng;
            public ChallengeSection Sec;
            /// <summary>Item kinds the game looks for by kind (ItemCleaner's rule): what inventory traps ask for, the puzzle pieces.</summary>
            public HashSet<int> Guarded;
            public HashSet<long> Special;

            public Ctx(ChallengeWorld w, ChallengeOptions opt)
            {
                W = w; S = w.Session; Opt = opt;
                Guarded = new HashSet<int>();
                // ItemCleaner.GuardedKinds: UW1's emeralds (the emerald puzzle, event/TRIGGER.C emerald_trap), UW2's
                // storage crystals (obj/OBJUSE.C), its curious implement and lockpick (UseKey), and every kind an
                // inventory trap (0x18C: item quality << 5 | owner, event/TRIGGER.C UseTrap) asks for
                Guarded.Add(S.IsUw2 ? 0xA1 : 0xA7);
                if (S.IsUw2) { Guarded.Add(0x100); Guarded.Add(0x101); }
                foreach (int l in w.Levels)
                    foreach (var o in w.Model(l).Objects)
                        if (!o.Free && o.TileX >= 0 && o.Item == 0x18C) Guarded.Add((o.Quality << 5 | o.Owner) & 0x1FF);
                Special = new HashSet<long>((opt.Special ?? new List<IgnoredObject>()).Select(c => (long)c.Level << 32 | (uint)c.Index));
            }
        }

        /// <summary>An item the challenges may touch: an object of a level, or an object of the player's inventory.</summary>
        sealed class Thing
        {
            /// <summary>The level, or -1 for the player's inventory.</summary>
            public int Level;
            public ObjectInfo O;
            public InventoryItem Inv;
            public int Item;
            /// <summary>Inside a create-object trap (a template: the trap would make it again).</summary>
            public bool Template;
            /// <summary>The creature whose inventory holds it (however deep), or -1.</summary>
            public int Carrier = -1;
            /// <summary>Why it is never touched, or null.</summary>
            public string Protected;
            public string Label;
            public int Depth;
        }

        // ---- item kinds, by game (include/items.h of each game)

        /// <summary>
        /// Food that is eaten, not drunk (Starvation, Fasting, Merchant, Hoarding): UW1 0xB0-0xB7 (meat,
        /// bread, cheese, apple, corn, bread, fish, popcorn); UW2 also the pastry 0xB8, honeycomb 0xBA,
        /// meat-on-a-stick 0xBE and nutritious wafer 0xBF. Not the mushrooms and toadstools, the
        /// drinks, UW1's potions (0xBB-0xBC) or the Wine of Compassion; not rotworms, leeches or plants.
        /// </summary>
        public static bool IsFood(GameSession s, int item)
        {
            if (s.IsUw2) return (item >= 0xB0 && item <= 0xB8) || item == 0xBA || item == 0xBE || item == 0xBF;
            return item >= 0xB0 && item <= 0xB7;
        }

        /// <summary>Meat and fish: 0xB0 and the fish 0xB6; UW2 also its second piece of meat 0xB1 and the meat-on-a-stick 0xBE.</summary>
        public static bool IsMeatOrFish(GameSession s, int item)
        {
            return item == 0xB0 || item == 0xB6 || (s.IsUw2 && (item == 0xB1 || item == 0xBE));
        }

        /// <summary>The weapons the player fights with: melee 0x00-0x0E (not the fist), sling stones, bolts and arrows (0x10-0x12) and the launchers (0x18-0x1A, 0x1F). The magic missiles (fireballs and the like) are not items to carry.</summary>
        public static bool IsWeapon(int item)
        {
            return item <= 0x0E || (item >= 0x10 && item <= 0x12) || ItemTables.Launchers.Contains(item);
        }

        /// <summary>Armour, shields, rings and crowns (0x20-0x3F, what the game wears).</summary>
        public static bool IsWearable(GameSession s, int item)
        {
            return item >= 0x20 && item < 0x40 && (ItemCategories.Of(item, s.Kind) & ItemCategory.Wearable) != 0;
        }

        /// <summary>Lights 0x90-0x97, lit or not; UW1's taper (0x93, lit 0x97) is the Taper of Sacrifice and stays.</summary>
        public static bool IsLight(GameSession s, int item)
        {
            return item >= 0x90 && item <= 0x97 && (s.IsUw2 || (item != 0x93 && item != 0x97));
        }

        /// <summary>Coins (0xA0; UW1 also gold coins 0xA1) and gems (0xA2-0xA7). UW2's 0xA1 is the storage crystal.</summary>
        public static bool IsCoinOrGem(GameSession s, int item)
        {
            return item == 0xA0 || (!s.IsUw2 && item == 0xA1) || (item >= 0xA2 && item <= 0xA7);
        }

        /// <summary>Books, scrolls and maps (UW1 0x130-0x13F; UW2 0x130-0x13A, its 0x13B-0x13F being scenery).</summary>
        public static bool IsText(GameSession s, int item)
        {
            return item >= 0x130 && item < 0x140 && (ItemCategories.Of(item, s.Kind) & (ItemCategory.Book | ItemCategory.Scroll)) != 0;
        }

        /// <summary>The map item (UseBook opens the map screen with it): UW1 0x13B, UW2 0x13A.</summary>
        public static int MapItem(GameSession s) { return s.IsUw2 ? 0x13A : 0x13B; }

        public static bool IsRune(int item) { return item >= 0xE8 && item <= 0xFF; }
        public const int RuneBag = 0x8F, Ylem = 0xFF, LockItem = 0x10F, SpellObject = 0x120, CreateTrap = 0x187, DoorTrap = 0x188;
        public static bool IsWand(int item) { return item >= 0x98 && item <= 0x9B; }

        /// <summary>UW2 wearables the plot needs: the fraznium gauntlets and circlet lift force fields (event/WORLDEV.C check_fraznium), the Guardian signet rings (conv/BABLHACK.C, combat/SPELLS2.C); the strange artifact is a quest item by class.</summary>
        static readonly int[] Uw2QuestWearables = { 0x33, 0x34, 0x35, 0x36 };

        // ---- what may be touched

        /// <summary>Objects a trap or trigger names by its link (a delete-object trap's target and the like), not the templates inside create-object traps.</summary>
        static HashSet<int> Named(LevelModel m)
        {
            var set = new HashSet<int>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || !o.IsTrapOrTrigger || o.Link <= 0 || o.Link >= m.Objects.Length) continue;
                if (o.Item == CreateTrap && !o.IsQuantity) continue;
                set.Add(o.Link);
            }
            return set;
        }

        /// <summary>Why an item is never touched, or null: quest items by the editor's lists (KeyItems, NamedItems, the quest classes, Caliburn and the jewelled dagger, UW2's quest wearables and the book the plot watches), kinds the game looks for, objects a trap names or that hold a trap or trigger.</summary>
        static string Protection(Ctx x, LevelModel m, ObjectInfo o, HashSet<int> named)
        {
            var s = x.S;
            string kind;
            if (KeyItems.Classify(s, o, out kind) != null && kind != "runestone") return kind == "key" ? "keys" : kind == "talisman" ? "talismans" : "quest items";
            if (NamedItems.Of(s.Kind, o.Item) != null) return "quest items";
            var c = ItemCategories.Of(o.Item, s.Kind);
            if ((c & (ItemCategory.Key | ItemCategory.Lock | ItemCategory.QuestKey | ItemCategory.Quest)) != 0) return "keys, locks and quest items";
            if (ItemTables.KeptWeapons.Contains(o.Item)) return "quest items";
            if (s.IsUw2 && Uw2QuestWearables.Contains(o.Item)) return "quest items";
            // ui/INTERACT.C: picking up a tenacious book 0x138 sets a quest bit and is a crime
            if (s.IsUw2 && o.Item == 0x138 && o.Record.Length > 1 && (o.Record[1] & 0x20) != 0) return "quest items";
            if (ItemShuffle.IsMarkedBones(o)) return "quest items";
            if (x.Guarded.Contains(o.Item)) return "kinds the game looks for (puzzles, inventory traps)";
            if (m != null && named != null && named.Contains(o.Index)) return "objects a trap names";
            if (m != null && o.Children.Any(ch => m.Objects[ch].IsTrapOrTrigger)) return "objects holding a trap or trigger";
            return null;
        }

        /// <summary>The items of a level as it is now (not creatures, nothing from 0x140 on).</summary>
        static List<Thing> Things(Ctx x, int level)
        {
            var m = x.W.Model(level);
            var named = Named(m);
            var l = new List<Thing>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || o.Index < 2 || o.IsCritter || o.Item >= 0x140) continue;
                var t = new Thing { Level = level, O = o, Item = o.Item, Label = ItemShuffle.Describe(x.S, m, o) };
                bool otherTrap = false;
                for (int up = o.Parent, guard = 0; up >= 0 && guard < 64; up = m.Objects[up].Parent, guard++)
                {
                    var u = m.Objects[up];
                    t.Depth++;
                    if (u.IsTrapOrTrigger) { if (u.Item == CreateTrap) t.Template = true; else otherTrap = true; }
                    if (u.IsCritter && t.Carrier < 0) t.Carrier = up;
                }
                t.Protected = otherTrap ? "objects inside a trap (a door trap's lock and the like)" : Protection(x, m, o, named);
                l.Add(t);
            }
            return l;
        }

        /// <summary>The player's inventory in a save, as items.</summary>
        static List<Thing> PlayerThings(Ctx x)
        {
            var l = new List<Thing>();
            var p = x.W.Player;
            if (p == null) return l;
            foreach (var it in p.Inventory.All())
            {
                var o = Stand(it);
                var t = new Thing { Level = -1, Inv = it, O = o, Item = it.ItemId, Label = "player: " + x.S.Names.Get(it.ItemId) };
                for (var up = it.Parent; up != null; up = up.Parent) t.Depth++;
                t.Protected = Protection(x, null, o, null);
                l.Add(t);
            }
            return l;
        }

        /// <summary>An inventory object seen as a level object (for the quest lists).</summary>
        static ObjectInfo Stand(InventoryItem it)
        {
            return new ObjectInfo
            {
                Index = -1, Record = (byte[])it.Record.Clone(), Item = it.ItemId, Quality = it.Quality, Owner = it.Owner, Link = it.Link,
                IsQuantity = it.IsQuantity, Enchanted = it.Enchanted, Name = "",
            };
        }

        /// <summary>Every item of every level, and the player's.</summary>
        static List<Thing> AllThings(Ctx x)
        {
            var l = new List<Thing>();
            foreach (int level in x.W.Levels) l.AddRange(Things(x, level));
            l.AddRange(PlayerThings(x));
            return l;
        }

        /// <summary>The items a predicate picks, those protected counted as kept.</summary>
        static List<Thing> Pick(Ctx x, Func<Thing, bool> f)
        {
            var l = new List<Thing>();
            foreach (var t in AllThings(x))
            {
                if (!f(t)) continue;
                if (t.Protected != null) { x.Sec.Keep(t.Protected); continue; }
                l.Add(t);
            }
            return l;
        }

        // ---- changing items

        static bool InInventory(PlayerSheet p, InventoryItem it)
        {
            var top = it;
            while (top.Parent != null) { if (!top.Parent.Contents.Contains(top)) return false; top = top.Parent; }
            return p.Inventory.Top.Contains(top);
        }

        /// <summary>Deletes the items (LevelEditor.DeleteObject: from tiles, containers, inventories and traps, with what they hold), deepest first.</summary>
        static void Remove(Ctx x, IEnumerable<Thing> list, string what = null)
        {
            foreach (var g in list.Where(t => t.Level > 0).GroupBy(t => t.Level).OrderBy(g => g.Key))
            {
                var e = x.W.Editor(g.Key);
                foreach (var t in g.OrderByDescending(t => t.Depth).ThenByDescending(t => t.O.Index))
                {
                    var o = e.Model.Objects[t.O.Index];
                    if (o.Free || o.Item != t.Item || (o.TileX < 0 && o.Parent < 0)) continue;
                    e.DeleteObject(t.O.Index);
                    x.Sec.Add(g.Key, t.Label + (what ?? ""));
                }
            }
            var p = x.W.Player;
            foreach (var t in list.Where(t => t.Level < 0))
            {
                if (p == null || !InInventory(p, t.Inv)) continue;
                p.Inventory.Remove(t.Inv);
                x.W.PlayerChanged = true;
                x.Sec.Add(-1, t.Label + (what ?? ""));
            }
        }

        static ItemRewrite R(ObjectInfo o)
        {
            return new ItemRewrite { Index = o.Index, Item = o.Item, Quality = o.Quality, Flags = (o.Record[1] >> 1) & 7, Link = o.Link, Owner = o.Owner, Enchanted = o.Enchanted, IsQuantity = o.IsQuantity, Exact = true };
        }

        static ItemRewrite R(InventoryItem it)
        {
            return new ItemRewrite { Index = -1, Item = it.ItemId, Quality = it.Quality, Flags = it.Flags, Link = it.Link, Owner = it.Owner, Enchanted = it.Enchanted, IsQuantity = it.IsQuantity, Exact = true };
        }

        static bool Same(ItemRewrite a, ItemRewrite b)
        {
            return a.Item == b.Item && a.Quality == b.Quality && a.Flags == b.Flags && a.Link == b.Link && a.Enchanted == b.Enchanted && a.IsQuantity == b.IsQuantity;
        }

        /// <summary>
        /// Rewrites items (item, quality, flags 9-11, enchanted, is-quantity, link; the owner, place
        /// and next stay): level objects through LevelEditor.RewriteItems, one call a level;
        /// inventory objects in the player sheet. change returns the new words (null: no change)
        /// and a few words for the report.
        /// </summary>
        static HashSet<Thing> Rewrite(Ctx x, IEnumerable<Thing> list, Func<Thing, ItemRewrite, Tuple<ItemRewrite, string>> change, string label)
        {
            var done = new HashSet<Thing>();
            var byLevel = new SortedDictionary<int, List<ItemRewrite>>();
            foreach (var t in list)
            {
                var old = t.Level > 0 ? R(x.W.Model(t.Level).Objects[t.O.Index]) : R(t.Inv);
                var c = change(t, old);
                if (c == null || Same(c.Item1, old)) continue;
                var n = c.Item1;
                if (t.Level > 0)
                {
                    List<ItemRewrite> l;
                    if (!byLevel.TryGetValue(t.Level, out l)) byLevel[t.Level] = l = new List<ItemRewrite>();
                    n.Index = t.O.Index; n.Owner = old.Owner; n.Exact = true;
                    l.Add(n);
                }
                else
                {
                    var it = t.Inv;
                    if (n.IsQuantity && it.Contents.Count > 0) continue;
                    it.ItemId = n.Item; it.Flags = n.Flags & 7; it.Enchanted = n.Enchanted; it.Quality = n.Quality & 0x3F;
                    if (it.IsQuantity != n.IsQuantity) it.IsQuantity = n.IsQuantity;
                    if (n.IsQuantity) it.Link = n.Link & 0x3FF;
                    x.W.Player.Inventory.Changed = true;
                    x.W.PlayerChanged = true;
                }
                x.Sec.Add(t.Level, t.Label + (c.Item2 == null ? "" : " → " + c.Item2));
                done.Add(t);
            }
            foreach (var kv in byLevel) x.W.Editor(kv.Key).RewriteItems(kv.Value, label);
            return done;
        }

        /// <summary>The spell objects (0x120) inside an item (decode_obj_spell's list form, obj/USEITEMS.C).</summary>
        static List<int> SpellChildren(LevelModel m, ObjectInfo o)
        {
            if (o.IsQuantity) return new List<int>();
            return o.Children.Where(c => m.Objects[c].Item == SpellObject).ToList();
        }

        /// <summary>Deletes the spell objects inside the items (level and inventory).</summary>
        static void DropSpellObjects(Ctx x, IEnumerable<Thing> list)
        {
            foreach (var g in list.Where(t => t.Level > 0).GroupBy(t => t.Level))
            {
                var e = x.W.Editor(g.Key);
                foreach (var t in g)
                    foreach (int c in SpellChildren(e.Model, e.Model.Objects[t.O.Index]))
                        if (!e.Model.Objects[c].Free) e.DeleteObject(c);
            }
            foreach (var t in list.Where(t => t.Level < 0))
            {
                int n = t.Inv.Contents.RemoveAll(c => c.ItemId == SpellObject);
                if (n > 0) { x.W.Player.Inventory.Changed = true; x.W.PlayerChanged = true; }
            }
        }

        static bool HasSpellObject(Ctx x, Thing t)
        {
            if (t.Level < 0) return !t.Inv.IsQuantity && t.Inv.Contents.Any(c => c.ItemId == SpellObject);
            var m = x.W.Model(t.Level);
            return SpellChildren(m, m.Objects[t.O.Index]).Count > 0;
        }

        /// <summary>An enchantment held in the link (is-quantity, enchanted, link 0x200 and up: decode_obj_spell).</summary>
        static bool LinkEnchanted(ItemRewrite r) { return r.IsQuantity && r.Enchanted && r.Link >= 0x200; }

        // ---- OBJECTS.DAT's creature table (include/critter.h struct Creature, obj/TREASURE.C)

        static readonly RecordSchema CS = Uw1Schemas.Creature;
        const int CreaturesAt = 0x132, CreatureSize = 0x30;
        static int Rows(byte[] d) { return d.Length < CreaturesAt ? 0 : Math.Min(64, (d.Length - CreaturesAt) / CreatureSize); }
        static int TGet(byte[] d, int row, string f) { return CS[f].Get(d, CreaturesAt + CreatureSize * row); }
        static void TSet(byte[] d, int row, string f, int v) { CS[f].Set(d, v, CreaturesAt + CreatureSize * row); }
        /// <summary>A weapon slot's item (generate_weapons): ((item >> 4) &amp; 3) &lt;&lt; 4 | item &amp; 0xF, a weapon, missile or armour.</summary>
        static int ArmsId(int item) { return ((item >> 4) & 3) << 4 | (item & 0xF); }

        static string Kind(Ctx x, int row) { return x.S.Names.Get(0x40 + row); }

        /// <summary>Sets the chance of every loot entry (other[0], other[1]: generate_equipment) whose item matches to 0.</summary>
        static void ClearLoot(Ctx x, Func<int, bool> match, string what)
        {
            var d = x.W.Objects;
            for (int row = 0; row < Rows(d); row++)
                for (int k = 1; k <= 2; k++)
                {
                    int chance = TGet(d, row, "Loot " + k + " chance"), item = TGet(d, row, "Loot " + k + " item");
                    if (chance == 0 || item >= 0x200 || !match(item)) continue;
                    TSet(d, row, "Loot " + k + " chance", 0);
                    x.Sec.Add(0, string.Format("{0} no longer carries {1}{2}", Kind(x, row), x.S.Names.Get(item), what ?? ""));
                }
        }

        /// <summary>Takes away the weapon slots (arms[0], arms[1]: generate_weapons) whose item matches.</summary>
        static void ClearArms(Ctx x, Func<int, bool> match)
        {
            var d = x.W.Objects;
            for (int row = 0; row < Rows(d); row++)
                for (int k = 1; k <= 2; k++)
                {
                    if (TGet(d, row, "Arms " + k + " present") == 0) continue;
                    int id = ArmsId(TGet(d, row, "Arms " + k + " item"));
                    if (!match(id)) continue;
                    TSet(d, row, "Arms " + k + " present", 0);
                    x.Sec.Add(0, string.Format("{0} no longer gets {1}", Kind(x, row), x.S.Names.Get(id)));
                }
        }

        /// <summary>Scales the food chance (generate_food: food_prob in 16, item 0xB0 + food_item) of the kinds whose food matches; keep 0 takes it away.</summary>
        static void ScaleFood(Ctx x, Func<int, bool> match, double keep)
        {
            var d = x.W.Objects;
            for (int row = 0; row < Rows(d); row++)
            {
                int chance = TGet(d, row, "Food chance"), item = 0xB0 + TGet(d, row, "Food item");
                if (chance == 0 || !match(item)) continue;
                int n = (int)Math.Round(chance * keep);
                if (n == chance) continue;
                TSet(d, row, "Food chance", n);
                x.Sec.Add(0, string.Format("{0}: {1} chance {2} → {3} in 16", Kind(x, row), x.S.Names.Get(item), chance, n));
            }
        }

        /// <summary>The creature table's changes, field by field.</summary>
        static void DiffTable(ChallengeWorld w, List<TableChange> into)
        {
            var a = w.ObjectsBefore; var b = w.Objects;
            for (int row = 0; row < Math.Min(Rows(a), Rows(b)); row++)
                foreach (var f in CS.Fields)
                {
                    int o = f.Get(a, CreaturesAt + CreatureSize * row), n = f.Get(b, CreaturesAt + CreatureSize * row);
                    if (o != n) into.Add(new TableChange { Row = row, Field = f.Name, Old = o, New = n });
                }
        }

        // ---- creatures

        /// <summary>The creatures of a level (on the map, in inventories or traps' templates).</summary>
        static IEnumerable<ObjectInfo> Creatures(LevelModel m)
        {
            return m.Objects.Where(o => !o.Free && o.TileX >= 0 && o.IsCritter && o.IsMobile);
        }

        static string CreatureName(Ctx x, LevelModel m, ObjectInfo o)
        {
            string n = ObjectTexts.NpcName(x.S, o);
            return string.Format("#{0} {1}{2} ({3}, {4}){5}", o.Index, x.S.Names.Get(o.Item), n != null ? " \"" + n + "\"" : "", o.TileX, o.TileY, o.Parent >= 0 && m.Objects[o.Parent].IsTrapOrTrigger ? " template" : "");
        }

        /// <summary>Peaceful: every creature (templates too) friendly, attitude 3 (word 0x0D bits 14-15, include/object.h OBJ_ATTITUDE).</summary>
        static void Peaceful(Ctx x)
        {
            foreach (int level in x.W.Levels)
            {
                var m = x.W.Model(level);
                var recs = new List<KeyValuePair<int, byte[]>>();
                foreach (var o in Creatures(m))
                {
                    int w = Word(o.Record, 0x0D);
                    if (w >> 14 == 3) continue;
                    var r = (byte[])o.Record.Clone();
                    w = (w & 0x3FFF) | 3 << 14;
                    r[0x0D] = (byte)w; r[0x0E] = (byte)(w >> 8);
                    recs.Add(new KeyValuePair<int, byte[]>(o.Index, r));
                    x.Sec.Add(level, CreatureName(x, m, o) + ": " + AttitudeName(Word(o.Record, 0x0D) >> 14) + " → friendly");
                }
                if (recs.Count > 0) x.W.Editor(level).RewriteRecords(recs, "Peaceful");
            }
            x.Sec.Notes.Add("Conversations and the game still change attitudes in play.");
        }

        /// <summary>
        /// DMD mode: every creature becomes powerful (word 0x0D bit 10: +7..12 to hit, +4..15
        /// damage, combat/COMBAT.C critter_attack), and those without a conversation (whoami 0)
        /// that are not hostile become hostile (attitude 0). Templates too. The special list is left alone.
        /// </summary>
        static void Dmd(Ctx x)
        {
            int special = 0;
            foreach (int level in x.W.Levels)
            {
                var m = x.W.Model(level);
                var recs = new List<KeyValuePair<int, byte[]>>();
                foreach (var o in Creatures(m))
                {
                    if (x.Special.Contains((long)level << 32 | (uint)o.Index)) { special++; continue; }
                    int w = Word(o.Record, 0x0D), n = w | 0x400;
                    bool talker = o.Record[0x1A] > 0;
                    if (!talker && (w >> 14) != 0) n &= 0x3FFF;
                    if (n == w) continue;
                    var r = (byte[])o.Record.Clone();
                    r[0x0D] = (byte)n; r[0x0E] = (byte)(n >> 8);
                    recs.Add(new KeyValuePair<int, byte[]>(o.Index, r));
                    var what = new List<string>();
                    if ((n >> 14) != (w >> 14)) what.Add(AttitudeName(w >> 14) + " → hostile");
                    if ((w & 0x400) == 0) what.Add("powerful");
                    x.Sec.Add(level, CreatureName(x, m, o) + ": " + string.Join(", ", what));
                }
                if (recs.Count > 0) x.W.Editor(level).RewriteRecords(recs, "DMD mode");
            }
            x.Sec.Notes.Add(string.Format("Creatures with a conversation keep their attitude; {0} special creature{1} left as {2} are.", special, special == 1 ? "" : "s", special == 1 ? "it" : "they"));
        }

        // ---- removals

        static void Poverty(Ctx x)
        {
            Remove(x, Pick(x, t => IsCoinOrGem(x.S, t.Item)));
            var d = x.W.Objects;
            for (int row = 0; row < Rows(d); row++)
            {
                if (TGet(d, row, "Treasure chance") == 0 && TGet(d, row, "Treasure rate") == 0) continue;
                // generate_treasure: rand() % 16 >= treasure_rate gives nothing, and treasure_prob sets the amount; both 0
                TSet(d, row, "Treasure chance", 0);
                TSet(d, row, "Treasure rate", 0);
                x.Sec.Add(0, Kind(x, row) + ": no treasure");
            }
            ClearLoot(x, i => IsCoinOrGem(x.S, i), null);
        }

        /// <summary>The wand spells the plot needs (decode_obj_spell's reading of the spell's link): UW1's class 13 effect 3, the bullfrog (combat/SPELLS.C work_bullfrog_tiles, the level 4 puzzle); UW2's spells 39 Telekinesis and 5 Light (its Academy wands).</summary>
        static bool KeptWandSpell(GameSession s, byte[] rec)
        {
            int w0 = Word(rec, 0), link = Word(rec, 6) >> 6;
            if (((w0 >> 11) & 1) == 0) return false;
            int major = (link & 0x1FF) >> 6, effect = link & 0x3F;
            if (s.IsUw2) return major == 0 && (effect == 5 || effect == 39);
            return major == 1 && effect == 3;
        }

        static bool KeptWand(Ctx x, Thing t)
        {
            if (t.Level < 0)
            {
                if (t.Inv.IsQuantity) return KeptWandSpell(x.S, t.Inv.Record);
                return t.Inv.Contents.Any(c => c.ItemId == SpellObject && KeptWandSpell(x.S, c.Record));
            }
            var m = x.W.Model(t.Level);
            var o = m.Objects[t.O.Index];
            if (o.IsQuantity) return KeptWandSpell(x.S, o.Record);
            return SpellChildren(m, o).Any(c => KeptWandSpell(x.S, m.Objects[c].Record));
        }

        /// <summary>
        /// Allergic to magic: weapons, armour, rings and crowns lose their enchantment (made plain
        /// as the shipped ones are: not is-quantity, link 0); spell scrolls become blank (link
        /// 0x200: text 0 of block 3, "The pages are blank."; obj/USEITEMS.C UseBook); wands lose
        /// their spell except the plot's (<see cref="KeptWandSpell"/>); the runestones and the rune
        /// bag go; a save's rune bag bits, shelf and rune count (struct Player 0x44-0x49 and nrunes,
        /// combat/RUNES.C clear_runes) are cleared. A spell object (0x120) inside an item goes too.
        /// </summary>
        static void Allergic(Ctx x)
        {
            var s = x.S;
            var gear = Pick(x, t => t.Item < 0x40 || IsText(s, t.Item));
            var withSpell = gear.Where(t => HasSpellObject(x, t)).ToList();
            DropSpellObjects(x, withSpell);
            var plain = Rewrite(x, gear, (t, r) =>
            {
                bool listSpell = withSpell.Contains(t);
                if (!LinkEnchanted(r) && !(listSpell || r.Enchanted)) return null;
                var n = r;
                if (IsText(s, t.Item))
                {
                    if (!LinkEnchanted(r)) return null;
                    n.Enchanted = false; n.Flags &= ~6; n.IsQuantity = true; n.Link = 0x200;
                    return Tuple.Create(n, "blank");
                }
                n.Enchanted = false; n.Flags &= ~4;
                if (LinkEnchanted(r)) { if (t.Item >= 0x10 && t.Item <= 0x17) { n.IsQuantity = true; n.Link = 1; } else { n.IsQuantity = false; n.Link = 0; } }
                return Tuple.Create(n, "plain");
            }, "Allergic to magic");
            foreach (var t in withSpell) if (!plain.Contains(t)) x.Sec.Add(t.Level, t.Label + " → its spell object removed");

            var wands = Pick(x, t => IsWand(t.Item));
            var keep = wands.Where(t => KeptWand(x, t)).ToList();
            foreach (var t in keep) x.Sec.Keep("the plot's wands");
            wands = wands.Except(keep).ToList();
            var charged = wands.Where(t => HasSpellObject(x, t)).ToList();
            DropSpellObjects(x, charged);
            var cleared = Rewrite(x, wands, (t, r) =>
            {
                if (!r.Enchanted && !LinkEnchanted(r)) return null;
                var n = r;
                n.Enchanted = false; n.Flags &= ~4;
                if (n.IsQuantity) { n.IsQuantity = false; n.Link = 0; }
                return Tuple.Create(n, "no spell");
            }, "Allergic to magic");
            foreach (var t in charged) if (!cleared.Contains(t)) x.Sec.Add(t.Level, t.Label + " → no spell");

            Remove(x, Pick(x, t => IsRune(t.Item)));
            // the rune bag holds bits, not objects (add_rune); anything else in it would go with it
            Remove(x, Pick(x, t => t.Item == RuneBag && (t.Level < 0 ? t.Inv.Contents.Count == 0 : t.O.Children.Count == 0)));
            ClearLoot(x, i => IsRune(i) || i == RuneBag || IsWand(i), null);
            var p = x.W.Player;
            if (p != null)
            {
                var rec = p.Dat.Record;
                int nr = s.IsUw2 ? 0x61 : 0x60, mask = s.IsUw2 ? 0x06 : 0x0C;
                bool any = false;
                for (int i = 0x44; i < 0x4A; i++) if (rec[i] != 0) { rec[i] = 0; any = true; }
                if ((rec[nr] & mask) != 0) { rec[nr] = (byte)(rec[nr] & ~mask); any = true; }
                if (any) { x.W.PlayerChanged = true; x.Sec.Add(-1, "the rune bag's runes and the shelf"); }
            }
        }

        static void Illiterate(Ctx x)
        {
            Remove(x, Pick(x, t => IsText(x.S, t.Item)));
            ClearLoot(x, i => IsText(x.S, i), null);
        }

        static void OldSchool(Ctx x)
        {
            int map = MapItem(x.S);
            Remove(x, Pick(x, t => t.Item == map));
            ClearLoot(x, i => i == map, null);
        }

        static void Darkness(Ctx x)
        {
            Remove(x, Pick(x, t => IsLight(x.S, t.Item)));
            ClearLoot(x, i => IsLight(x.S, i), null);
            x.Sec.Notes.Add("The tiles' own light (their light flags) is not touched.");
        }

        static void Naked(Ctx x)
        {
            Remove(x, Pick(x, t => IsWearable(x.S, t.Item)));
            ClearArms(x, id => id >= 0x20 && id < 0x40);
            ClearLoot(x, i => IsWearable(x.S, i), null);
        }

        static void Monk(Ctx x)
        {
            Remove(x, Pick(x, t => IsWeapon(t.Item)));
            ClearArms(x, IsWeapon);
            ClearLoot(x, IsWeapon, null);
        }

        // ---- Ranger

        /// <summary>The launchers from the least to the most damaging (OBJECTS.DAT's missile table, 3 bytes from 0x82, damage first; obj/HACK.C hack_init).</summary>
        public static int[] LaunchersByDamage(GameSession s, byte[] objects)
        {
            Func<int, int> dmg = id => objects != null && 0x82 + 3 * (id - 0x10) < objects.Length ? objects[0x82 + 3 * (id - 0x10)] : 0;
            return ItemTables.Launchers.OrderBy(dmg).ThenBy(id => id).ToArray();
        }

        static double Normal(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>Ranger's ammunition count: round(N(7, 3)), drawn again until it is 1-15.</summary>
        public static int AmmoCount(Random rng)
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                int n = (int)Math.Round(7 + 3 * Normal(rng));
                if (n >= 1 && n <= 15) return n;
            }
            return 7;
        }

        /// <summary>One Ranger draw: a launcher or ammunition with equal chance; the launcher by the weights (launchers least damaging first), the ammunition uniform among sling stones, bolts and arrows with <see cref="AmmoCount"/>. Returns the item and the count (0 for a launcher).</summary>
        public static Tuple<int, int> RangerDraw(Random rng, int[] launchers, double[] weights)
        {
            if (rng.NextDouble() < 0.5)
            {
                int i = Distributions.Pick(rng, weights);
                return Tuple.Create(launchers[Math.Max(0, i)], 0);
            }
            int ammo = ItemTables.Ammo[rng.Next(ItemTables.Ammo.Length)];
            return Tuple.Create(ammo, AmmoCount(rng));
        }

        /// <summary>
        /// Ranger: each melee weapon (0x00-0x0E) becomes a launcher or ammunition with equal
        /// chance. A launcher keeps the weapon's quality and enchantment; ammunition is a stack
        /// (is-quantity, link = count 1-15). The creatures' melee arms and loot follow the same draw.
        /// </summary>
        static void Ranger(Ctx x)
        {
            var launchers = LaunchersByDamage(x.S, x.W.Objects);
            var weights = (x.Opt.RangerWeights ?? new double[] { 10, 3, 2, 1 }).Concat(Enumerable.Repeat(1.0, 4)).Take(4).Select(v => Math.Max(0, v)).ToArray();
            if (weights.Sum() <= 0) weights = new double[] { 1, 1, 1, 1 };
            x.Sec.Notes.Add("Launchers from least to most damaging: " + string.Join(", ", launchers.Select((l, i) => x.S.Names.Get(l) + " " + weights[i])) + ".");
            var melee = Pick(x, t => t.Item <= 0x0E);
            var draws = melee.ToDictionary(t => t, t => RangerDraw(x.Rng, launchers, weights));
            DropSpellObjects(x, melee.Where(t => draws[t].Item2 > 0 && HasSpellObject(x, t)).ToList());
            Rewrite(x, melee, (t, r) =>
            {
                var d = draws[t];
                var n = r;
                n.Item = d.Item1;
                if (d.Item2 > 0)
                {
                    if (t.Level > 0 ? x.W.Model(t.Level).Objects[t.O.Index].Children.Count > 0 : t.Inv.Contents.Count > 0) return null;
                    n.IsQuantity = true; n.Link = d.Item2; n.Enchanted = false; n.Flags &= ~4;
                    return Tuple.Create(n, d.Item2 + " " + x.S.Names.Get(d.Item1));
                }
                return Tuple.Create(n, x.S.Names.Get(d.Item1));
            }, "Ranger");
            var obj = x.W.Objects;
            for (int row = 0; row < Rows(obj); row++)
            {
                for (int k = 1; k <= 2; k++)
                {
                    if (TGet(obj, row, "Arms " + k + " present") == 0) continue;
                    int id = ArmsId(TGet(obj, row, "Arms " + k + " item"));
                    if (id > 0x0E) continue;
                    var d = RangerDraw(x.Rng, launchers, weights);
                    TSet(obj, row, "Arms " + k + " item", d.Item1);
                    x.Sec.Add(0, string.Format("{0}: {1} → {2}", Kind(x, row), x.S.Names.Get(id), x.S.Names.Get(d.Item1)));
                }
                for (int k = 1; k <= 2; k++)
                {
                    int chance = TGet(obj, row, "Loot " + k + " chance"), item = TGet(obj, row, "Loot " + k + " item");
                    if (chance == 0 || item > 0x0E) continue;
                    var d = RangerDraw(x.Rng, launchers, weights);
                    TSet(obj, row, "Loot " + k + " item", d.Item1);
                    x.Sec.Add(0, string.Format("{0}: {1} → {2}", Kind(x, row), x.S.Names.Get(item), x.S.Names.Get(d.Item1)));
                }
            }
        }

        // ---- food

        static void Vegetarian(Ctx x)
        {
            var s = x.S;
            Remove(x, Pick(x, t => IsMeatOrFish(s, t.Item) || t.Item == ItemTables.FishingPole));
            ScaleFood(x, i => IsMeatOrFish(s, i), 0);
            ClearLoot(x, i => IsMeatOrFish(s, i) || i == ItemTables.FishingPole, null);
        }

        /// <summary>Starvation: no food and no fishing poles; the Ylem runestones (and a save's Ylem rune) become another rune, so In Mani Ylem (Create Food) cannot be cast.</summary>
        static void Starvation(Ctx x)
        {
            var s = x.S;
            Remove(x, Pick(x, t => IsFood(s, t.Item) || t.Item == ItemTables.FishingPole));
            var ylem = Pick(x, t => t.Item == Ylem);
            Rewrite(x, ylem, (t, r) => { var n = r; n.Item = 0xE8 + x.Rng.Next(23); return Tuple.Create(n, s.Names.Get(n.Item)); }, "Starvation");
            ScaleFood(x, i => IsFood(s, i), 0);
            ClearLoot(x, i => IsFood(s, i) || i == ItemTables.FishingPole || i == Ylem, null);
            var p = x.W.Player;
            if (p != null && (p.Dat.Record[0x46] & 1) != 0)
            {
                // rune 23 (Ylem) is bit 7 - (23 & 7) of runebag[23 >> 3] (combat/RUNES.C add_rune)
                int other = x.Rng.Next(23);
                p.Dat.Record[0x46] &= 0xFE;
                p.Dat.Record[0x44 + (other >> 3)] |= (byte)(1 << (7 - (other & 7)));
                for (int i = 0x47; i < 0x4A; i++) if (p.Dat.Record[i] == 23) p.Dat.Record[i] = (byte)other;
                x.W.PlayerChanged = true;
                x.Sec.Add(-1, "the Ylem rune → " + s.Names.Get(0xE8 + other));
            }
        }

        /// <summary>Fasting: of the food (as Starvation counts it), the given share stays, picked at random; the creatures' food chances are scaled by it.</summary>
        static void Fasting(Ctx x)
        {
            double keep = Math.Max(0, Math.Min(100, x.Opt.FastingKeep)) / 100.0;
            var food = Pick(x, t => IsFood(x.S, t.Item));
            int stay = (int)Math.Round(food.Count * keep);
            for (int i = food.Count - 1; i > 0; i--) { int j = x.Rng.Next(i + 1); var t = food[i]; food[i] = food[j]; food[j] = t; }
            x.Sec.Notes.Add(string.Format("{0} of {1} pieces of food stay ({2:0}%).", stay, food.Count, keep * 100));
            Remove(x, food.Skip(stay));
            ScaleFood(x, i => IsFood(x.S, i), keep);
            var d = x.W.Objects;
            for (int row = 0; row < Rows(d); row++)
                for (int k = 1; k <= 2; k++)
                {
                    int chance = TGet(d, row, "Loot " + k + " chance"), item = TGet(d, row, "Loot " + k + " item");
                    if (chance == 0 || !IsFood(x.S, item)) continue;
                    int n = (int)Math.Round(chance * keep);
                    if (n == chance) continue;
                    TSet(d, row, "Loot " + k + " chance", n);
                    x.Sec.Add(0, string.Format("{0}: {1} chance {2} → {3} in 16", Kind(x, row), x.S.Names.Get(item), chance, n));
                }
        }

        // ---- quality

        /// <summary>
        /// Whether an item has the weapon and armour quality words: COMOBJ.DAT quality type 1 or 13
        /// (block 5: "ruined, badly worn, worn, serviceable, excellent"), which every weapon,
        /// launcher, piece of armour and shield has in both games.
        /// </summary>
        static bool WornWords(GameSession s, int item)
        {
            if (item >= 0x40) return false;
            int qt = Races.ComObjByte(s, item, 10) & 0xF;
            return qt == 1 || qt == 13;
        }

        /// <summary>
        /// Utopia: quality 63 (word 4, "excellent"); Dystopia: quality 1-15 at random, the band
        /// obj/LOOK.C LookAt calls "badly worn" (word (quality >> 4) + 1 = 1 of quality types 1
        /// and 13, both games). The loot creatures get on death keeps the game's own roll.
        /// </summary>
        static void Quality(Ctx x, bool utopia)
        {
            var list = Pick(x, t => WornWords(x.S, t.Item));
            Rewrite(x, list, (t, r) =>
            {
                var n = r;
                n.Quality = utopia ? 63 : 1 + x.Rng.Next(15);
                return Tuple.Create(n, "quality " + r.Quality + " → " + n.Quality);
            }, utopia ? "Utopia" : "Dystopia");
            x.Sec.Notes.Add(utopia ? "Quality 63: \"excellent\"." : "Quality 1-15: \"badly worn\" (obj/LOOK.C, quality / 16 + 1 = 1).");
            x.Sec.Notes.Add("Loot made when a creature dies keeps the game's own quality roll (obj/TREASURE.C).");
        }
    }
}
