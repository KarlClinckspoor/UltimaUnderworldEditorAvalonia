using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.Core.Randomizer
{
    public static partial class Challenges
    {
        /// <summary>
        /// At most this many objects in a trader's inventory: barter fills its 4 (UW1) or 6 (UW2)
        /// slots from the first 40 objects of the trader's inventory (conv/BARTER.C setup_to_barter).
        /// </summary>
        public const int TraderCap = 40;
        /// <summary>An ordinary monster takes more than this only when no level of its world has a monster with fewer: goods spread to the next levels first.</summary>
        public const int MonsterCap = 16;
        /// <summary>Free static objects every level keeps after goods are moved there (the game needs room for loot, missiles and the like).</summary>
        public const int FreeReserve = 32;

        /// <summary>A creature that can be given goods.</summary>
        sealed class Recipient
        {
            public int Level, Index, X, Y, Load;
            public bool Trader;
            public double Tie;
            public string Name;
        }

        static int TreeSize(LevelModel m, int i, int depth = 0)
        {
            var o = m.Objects[i];
            int n = 1;
            if (!o.IsQuantity && depth < 64) foreach (int c in o.Children) n += TreeSize(m, c, depth + 1);
            return n;
        }

        static int TreeSize(InventoryItem it) { return 1 + it.Contents.Sum(TreeSize); }

        static ClipNode Node(InventoryItem it)
        {
            var n = new ClipNode { Record = (byte[])it.Record.Clone(), Mobile = false };
            foreach (var c in it.Contents) n.Children.Add(Node(c));
            return n;
        }

        /// <summary>The level the player stands on in the open save, or 0.</summary>
        static int PlayerLevel(Ctx x)
        {
            var p = x.W.Player;
            if (p == null) return 0;
            int l = p.Dat.SavedLevel;
            return x.W.Levels.Contains(l) ? l : 0;
        }

        /// <summary>
        /// Merchant (food only, to traders) and Hoarding (food, weapons and armour, to traders and
        /// ordinary monsters). Traders are creatures whose conversation calls setup_to_barter
        /// (<see cref="TraderSlots"/>); ordinary monsters have no conversation (whoami 0), hit
        /// points, are not summoned, have a name and are not on the special list. Goods a recipient
        /// already carries stay with it; the others are taken out (LevelEditor.Copy, then
        /// DeleteObject) and put into the least-loaded recipient of their own level (ties at
        /// random; traders at most <see cref="TraderCap"/> objects, ordinary monsters
        /// <see cref="MonsterCap"/> while another level has room), or, when their level has none
        /// or no room (<see cref="FreeReserve"/>), of the nearest level that has (in UW2 within the
        /// same world). Goods nobody can carry are removed. A save's player's goods count as lying
        /// on the player's level.
        /// </summary>
        static void Move(Ctx x, bool merchant)
        {
            var s = x.S;
            Func<int, bool> isGood = merchant ? (Func<int, bool>)(i => IsFood(s, i)) : (i => IsFood(s, i) || IsWeapon(i) || IsWearable(s, i));
            var byLevel = new Dictionary<int, List<Recipient>>();
            var byKey = new Dictionary<long, Recipient>();
            foreach (int level in x.W.Levels)
            {
                var m = x.W.Model(level);
                var l = new List<Recipient>();
                foreach (var o in m.Objects)
                {
                    if (o.Free || !o.OnTile || !o.IsCritter || !o.IsMobile || o.Record[8] == 0) continue;
                    if (((Word(o.Record, 0x0D) >> 8) & 1) != 0) continue;           // summoned (OBJ_TEMP)
                    int who = o.Record[0x1A];
                    long key = (long)level << 32 | (uint)o.Index;
                    bool trader = who > 0 && x.W.TraderSlots.Contains(who);
                    bool ordinary = who == 0 && !x.Special.Contains(key) && !MonsterSwap.Unnamed(s, o.Item);
                    if (merchant ? !trader : !(trader || ordinary)) continue;
                    var r = new Recipient { Level = level, Index = o.Index, X = o.TileX, Y = o.TileY, Load = o.Children.Count, Trader = trader, Tie = x.Rng.NextDouble(), Name = CreatureName(x, m, o) };
                    l.Add(r);
                    byKey[key] = r;
                }
                byLevel[level] = l;
            }

            int playerLevel = PlayerLevel(x);
            var goods = new List<Thing>();
            foreach (var t in Pick(x, t => isGood(t.Item)))
            {
                if (t.Level > 0 && t.Carrier >= 0 && byKey.ContainsKey((long)t.Level << 32 | (uint)t.Carrier))
                {
                    x.Sec.Keep(merchant ? "food a trader carries already" : "goods a monster or trader carries already");
                    continue;
                }
                if (t.Level < 0 && playerLevel == 0) { x.Sec.Keep("the player's goods (no level to put them on)"); continue; }
                goods.Add(t);
            }
            Func<Thing, int> levelOf = t => t.Level > 0 ? t.Level : playerLevel;
            Func<Thing, int> sizeOf = t => t.Level > 0 ? TreeSize(x.W.Model(t.Level), t.O.Index) : TreeSize(t.Inv);

            // room: what the level has free, plus what leaves it, less the reserve
            var room = new Dictionary<int, int>();
            foreach (int level in x.W.Levels) room[level] = new LevelBlock(x.W.Editor(level).Data, s.Kind).StaticTop + 1 - FreeReserve;
            foreach (var t in goods.Where(t => t.Level > 0)) room[t.Level] += sizeOf(t);

            var plan = new List<Tuple<Thing, Recipient>>();
            var lost = new List<Thing>();
            foreach (var t in goods.OrderBy(levelOf).ThenBy(t => t.Level < 0 ? 1 : 0).ThenBy(t => t.O.Index))
            {
                int from = levelOf(t), size = sizeOf(t);
                Recipient pick = null;
                var near = x.W.Levels.Where(l => s.Profile.WorldOf(l) == s.Profile.WorldOf(from)).OrderBy(l => Math.Abs(l - from)).ThenBy(l => l).ToList();
                // first within the soft cap of ordinary monsters, then past it (traders never past theirs)
                for (int pass = 0; pass < 2 && pick == null; pass++)
                    foreach (int level in near)
                    {
                        if (room[level] < size) continue;
                        pick = byLevel[level].Where(r => r.Trader ? r.Load < TraderCap : pass == 1 || r.Load < MonsterCap).OrderBy(r => r.Load).ThenBy(r => r.Tie).FirstOrDefault();
                        if (pick != null) break;
                    }
                if (pick == null) { lost.Add(t); continue; }
                pick.Load++;
                room[pick.Level] -= size;
                plan.Add(Tuple.Create(t, pick));
            }

            // copy what moves, take every good out, then put the copies in
            var clips = new Dictionary<Thing, ObjectClip>();
            foreach (var p in plan)
            {
                var t = p.Item1;
                if (t.Level > 0) clips[t] = x.W.Editor(t.Level).Copy(new[] { t.O.Index });
                else { var c = new ObjectClip { Kind = s.Kind, FromLevel = -1 }; c.Roots.Add(Node(t.Inv)); clips[t] = c; }
            }
            foreach (var g in goods.Where(t => t.Level > 0).GroupBy(t => t.Level))
            {
                var e = x.W.Editor(g.Key);
                foreach (var t in g.OrderByDescending(t => t.Depth).ThenByDescending(t => t.O.Index))
                {
                    var o = e.Model.Objects[t.O.Index];
                    if (!o.Free && o.Item == t.Item && (o.TileX >= 0 || o.Parent >= 0)) e.DeleteObject(t.O.Index);
                }
            }
            var player = x.W.Player;
            foreach (var t in goods.Where(t => t.Level < 0))
                if (player != null && InInventory(player, t.Inv)) { player.Inventory.Remove(t.Inv); x.W.PlayerChanged = true; }
            int moved = 0, away = 0;
            foreach (var p in plan)
            {
                var t = p.Item1; var r = p.Item2;
                x.W.Editor(r.Level).Paste(clips[t], r.X, r.Y, r.Index);
                moved++;
                if (levelOf(t) != r.Level) away++;
                x.Sec.Add(t.Level, t.Label + " → " + r.Name + (levelOf(t) != r.Level ? " on " + LevelName(s, r.Level) : ""));
            }
            foreach (var t in lost) x.Sec.Add(t.Level, t.Label + " → removed (nobody " + (s.IsUw2 ? "in its world " : "") + "to carry it)");

            int traders = byKey.Values.Count(r => r.Trader), ordinaryN = byKey.Values.Count(r => !r.Trader);
            x.Sec.Notes.Add(merchant
                ? string.Format("{0} traders (their conversation calls setup_to_barter) take the food; {1} pieces moved, {2} to another level, {3} removed with no trader to take them.", traders, moved, away, lost.Count)
                : string.Format("{0} ordinary monsters and {1} traders take the goods; {2} moved, {3} to another level, {4} removed with nobody to take them.", ordinaryN, traders, moved, away, lost.Count));
            if (byKey.Count > 0)
            {
                var full = byKey.Values.Where(r => r.Trader).OrderByDescending(r => r.Load).FirstOrDefault();
                if (full != null) x.Sec.Notes.Add(string.Format("The most loaded trader carries {0} objects (barter fills its slots from the first {1}).", full.Load, TraderCap));
                var most = byKey.Values.Where(r => !r.Trader).OrderByDescending(r => r.Load).FirstOrDefault();
                if (most != null) x.Sec.Notes.Add(string.Format("The most loaded monster carries {0} objects.", most.Load));
            }
            if (merchant)
            {
                ScaleFood(x, i => IsFood(s, i), 0);
                ClearLoot(x, i => IsFood(s, i), null);
                x.Sec.Notes.Add("The creatures' own food loot (OBJECTS.DAT) is taken away, so no food turns up outside the traders.");
            }
        }

        // ---- Lockdown

        /// <summary>
        /// The lock Lockdown puts on a door, shaped as the shipped levels' locks (word 0 0xC70F in
        /// nearly all of them): item 0x10F, flags 9 (locked) and 10 (it can be locked again;
        /// obj/USEITEMS.C checkLock), invisible, is-quantity, link
        /// 0x200 + key 0 (no key fits: checkLock fails a key when link &amp; 0x1FF is 0), quality 40,
        /// owner 0, heading 0, fine position (3, 3), and z the picking difficulty (skill_check against
        /// z × 3; z 14 needs skill 31 in UW1, 32 in UW2, z 15 cannot be picked).
        /// </summary>
        public static ObjectSpec LockSpec(int z)
        {
            return new ObjectSpec { Item = LockItem, Flags = 3, IsQuantity = true, Invisible = true, Link = 0x200, Quality = 40, Owner = 0, Heading = 0, FineX = 3, FineY = 3, Z = z };
        }

        /// <summary>The picking difficulty by depth: 2 on the first level (of each UW2 world) to 13 on the deepest, always below 14.</summary>
        public static int LockZ(GameSession s, int level) { return 2 + (int)Math.Round(ItemShuffle.DepthOf(s.IsUw2, level) * 11); }

        /// <summary>The squares a door trap acts on: where it lies, and the target squares of the triggers whose chains reach it (event/TRIGGER.C UseTrap, TRAP_DOOR).</summary>
        static HashSet<(int, int)> DoorTrapSquares(LevelModel m)
        {
            var set = new HashSet<(int, int)>();
            foreach (var o in m.Objects)
            {
                if (o.Free || o.TileX < 0 || o.Item != DoorTrap) continue;
                set.Add((o.TileX, o.TileY));
                foreach (var t in Triggers.TriggersReaching(m, o)) set.Add((t.Quality, t.Owner));
            }
            foreach (var c in Triggers.Chains(m))
                if (c.Steps.Any(st => st.Item == DoorTrap)) set.Add((c.TargetX, c.TargetY));
            return set;
        }

        /// <summary>Why Lockdown leaves a door alone, or null.</summary>
        public static string DoorSkip(GameSession s, LevelModel m, ObjectInfo d, HashSet<(int, int)> trapSquares)
        {
            if (d.Item == 0x146) return "portcullises";
            if (d.Item == 0x147) return "secret doors";
            if (d.IsQuantity) return "doors that cannot hold a lock";
            if (d.Children.Any(c => m.Objects[c].Item == LockItem)) return "doors with a lock already";
            // combat/DAMAGE.C damage_object: quality class 3 or the door's bit 13 (doordir) and it takes no damage
            if (((Races.ComObjByte(s, d.Item, 6) >> 2) & 3) == 3) return "doors that cannot be broken (quality class 3)";
            if ((d.Record[1] & 0x20) != 0) return "doors that cannot be broken (bit 13)";
            if (trapSquares.Contains((d.TileX, d.TileY))) return "doors a lever or trigger works (door traps)";
            return null;
        }

        /// <summary>
        /// Lockdown: every closed door (0x140-0x145) without a lock that can be broken and that no
        /// door trap works gets <see cref="LockSpec"/>, with <see cref="LockZ"/>. Open doors,
        /// portcullises and secret doors are left alone.
        /// </summary>
        static void Lockdown(Ctx x)
        {
            var s = x.S;
            foreach (int level in x.W.Levels)
            {
                var e = x.W.Editor(level);
                var m = e.Model;
                var squares = DoorTrapSquares(m);
                int z = LockZ(s, level);
                foreach (var d in m.Objects.Where(o => !o.Free && o.OnTile && o.Item >= 0x140 && o.Item <= 0x147).ToList())
                {
                    string why = DoorSkip(s, m, d, squares);
                    if (why != null) { x.Sec.Keep(why); continue; }
                    if (new LevelBlock(e.Data, s.Kind).StaticTop < 0) { x.Sec.Keep("doors on a level with no free object"); continue; }
                    e.AddObject(LockSpec(z), d.TileX, d.TileY, d.Index);
                    x.Sec.Add(level, string.Format("#{0} {1} ({2}, {3}): difficulty {4}", d.Index, s.Names.Get(d.Item), d.TileX, d.TileY, z));
                }
            }
            x.Sec.Notes.Add("The locks are shaped as the shipped ones (lock 0x10F, locked, key 0: no key fits). Pick them (difficulty 2 to 13 by depth) or break the door.");
        }

        // ---- Precognition

        /// <summary>
        /// Precognition: every level's automap fully drawn, each open tile with the byte the game
        /// writes when the player sees it (<see cref="Automap.Seen"/>). In DATA these blocks reach
        /// new games: a new game copies the whole of DATA's LEV.ARK into SAVE0 (game/UWEDIT.C,
        /// ui/MAINMENU.C).
        /// </summary>
        static void Precognition(Ctx x)
        {
            foreach (int level in x.W.Levels)
            {
                var a = x.W.Map(level);
                var before = (byte[])a.Map.Clone();
                var m = x.W.Model(level);
                a.Snapshot("Precognition");
                a.All(x.S, m, true);
                int n = 0;
                for (int i = 0; i < before.Length; i++) if (before[i] != a.Map[i]) n++;
                if (n > 0) x.Sec.Add(level, n + " tiles drawn", n);
            }
            x.Sec.Notes.Add(x.S.IsSave ? "The save's maps are drawn." : "DATA's maps are drawn: a new game copies them.");
        }
    }
}
