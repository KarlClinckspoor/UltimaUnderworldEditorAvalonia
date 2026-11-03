using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    public static partial class Challenges
    {
        /// <summary>Free mobile records every level keeps after Bones rise (the game needs room for missiles and summoned creatures).</summary>
        public const int MobileReserve = 8;

        /// <summary>Skulls (0xC2, 0xC3) in both games (include/items.h).</summary>
        public static bool IsSkull(int item) { return item == 0xC2 || item == 0xC3; }

        /// <summary>Bones and piles of bones: 0xC4, 0xC5, 0xC6 in both games, and the second pile of bones (UW1 0xDC, UW2 0xDD; the item cleaner's bones).</summary>
        public static bool IsBone(GameSession s, int item) { return (item >= 0xC4 && item <= 0xC6) || item == (s.IsUw2 ? 0xDD : 0xDC); }

        /// <summary>The skeleton creature: the kind named "skeleton" in 0x40-0x7F (UW1 0x4A, UW2 0x48).</summary>
        public static int SkeletonItem(GameSession s)
        {
            for (int i = 0x40; i < 0x80; i++) if ((s.Names.Get(i) ?? "").Trim().ToLowerInvariant() == "skeleton") return i;
            return s.IsUw2 ? 0x48 : 0x4A;
        }

        /// <summary>
        /// Ghost town: every creature without a conversation (whoami 0) that is not on the special
        /// list goes: on the map (what it carried is put on the floor where it stood,
        /// LevelEditor.MoveObjectAnywhere, then the creature is deleted: off the active list and the
        /// animation list, LevelEditor.FreeIndex) and as a template in a create-object trap, so the
        /// trap makes none (an empty trap makes nothing: event/TRIGGER.C UseTrap, TRAP_CREATE_OBJECT).
        /// A creature a trap names otherwise (by its link) stays.
        /// </summary>
        static void GhostTown(Ctx x)
        {
            int dropped = 0, templates = 0;
            foreach (int level in x.W.Levels)
            {
                var e = x.W.Editor(level);
                var m = e.Model;
                var named = Named(m);
                var gone = new List<ObjectInfo>();
                foreach (var o in Creatures(m))
                {
                    if (o.Record[0x1A] > 0) { x.Sec.Keep("creatures with a conversation"); continue; }
                    if (x.Special.Contains((long)level << 32 | (uint)o.Index)) { x.Sec.Keep("special creatures"); continue; }
                    if (named.Contains(o.Index)) { x.Sec.Keep("creatures a trap names"); continue; }
                    // a template's contents would go with it: one carrying a quest object stays
                    if (o.Parent >= 0 && Descendants(m, o.Index).Any(d => !m.Objects[d].IsCritter && Protection(x, m, m.Objects[d], named) != null)) { x.Sec.Keep("templates carrying a quest object"); continue; }
                    gone.Add(o);
                }
                foreach (var o in gone.OrderByDescending(o => o.Parent >= 0 ? 1 : 0).ThenByDescending(o => o.Index))
                {
                    var c = e.Model.Objects[o.Index];
                    if (c.Free || c.Item != o.Item) continue;
                    bool template = c.Parent >= 0;
                    if (!template)
                    {
                        // the loot stays: what it carried lies on its tile
                        foreach (int ch in c.Children.ToList())
                        {
                            e.MoveObjectAnywhere(ch, c.TileX, c.TileY, c.FineX, c.FineY);
                            dropped++;
                        }
                    }
                    else templates++;
                    e.DeleteObject(o.Index);
                    x.Sec.Add(level, CreatureName(x, m, o));
                }
            }
            x.Sec.Notes.Add(string.Format("{0} carried objects put on the floor; {1} templates taken out of traps.", dropped, templates));
        }

        /// <summary>
        /// Bones rise: a 2 × 2 window slides over each level in reading order; in each, while there
        /// are an unused skull and two unused bones lying on its tiles (not in containers; never
        /// Garamon's bones, owner 0x3E, obj/USEITEMS.C UseBonesOn, nor quest objects or ones a trap
        /// names), the skull with the two bones nearest it go and a hostile skeleton stands on the
        /// skull's tile (another open tile of the window if that one is rock), with its kind's
        /// average hit points (OBJECTS.DAT) and its home there. Every object is used once; a level
        /// keeps <see cref="MobileReserve"/> free mobile records.
        /// </summary>
        static void BonesRise(Ctx x)
        {
            var s = x.S;
            int skeleton = SkeletonItem(s);
            int hp = Rows(x.W.Objects) > skeleton - 0x40 ? TGet(x.W.Objects, skeleton - 0x40, "Average HP") : 0;
            int noRoom = 0;
            foreach (int level in x.W.Levels)
            {
                var e = x.W.Editor(level);
                var m = e.Model;
                var named = Named(m);
                var lying = new Dictionary<(int, int), List<ObjectInfo>>();
                foreach (var o in m.Objects)
                {
                    if (o.Free || !o.OnTile || !(IsSkull(o.Item) || IsBone(s, o.Item))) continue;
                    // Garamon's bones (UW1) and UW2's marked pile carry owner 0x3E
                    if (o.Owner == 0x3E) { x.Sec.Keep("Garamon's bones and the like (owner 0x3E)"); continue; }
                    string why = Protection(x, m, o, named);
                    if (why != null) { x.Sec.Keep(why); continue; }
                    List<ObjectInfo> l;
                    if (!lying.TryGetValue((o.TileX, o.TileY), out l)) lying[(o.TileX, o.TileY)] = l = new List<ObjectInfo>();
                    l.Add(o);
                }
                if (lying.Count == 0) continue;
                var used = new HashSet<int>();
                var rises = new List<Tuple<ObjectInfo, ObjectInfo, ObjectInfo, int, int>>();
                int free = new LevelBlock(e.Data, s.Kind).MobileTop + 1 - MobileReserve;
                for (int y = 0; y < 63; y++)
                    for (int xx = 0; xx < 63; xx++)
                    {
                        var here = new List<ObjectInfo>();
                        foreach (var t in new[] { (xx, y), (xx + 1, y), (xx, y + 1), (xx + 1, y + 1) })
                        {
                            List<ObjectInfo> l;
                            if (lying.TryGetValue(t, out l)) here.AddRange(l.Where(o => !used.Contains(o.Index)));
                        }
                        while (true)
                        {
                            var skulls = here.Where(o => IsSkull(o.Item) && !used.Contains(o.Index)).ToList();
                            var bones = here.Where(o => IsBone(s, o.Item) && !used.Contains(o.Index)).ToList();
                            if (skulls.Count == 0 || bones.Count < 2) break;
                            if (free <= 0) { noRoom++; break; }
                            // the skull whose two nearest bones lie nearest, ties by index
                            var best = skulls.Select(k => new { k, b = bones.OrderBy(b => Dist(k, b)).ThenBy(b => b.Index).Take(2).ToList() })
                                .OrderBy(c => Dist(c.k, c.b[0]) + Dist(c.k, c.b[1])).ThenBy(c => c.k.Index).First();
                            int tx = best.k.TileX, ty = best.k.TileY;
                            if (m.Tiles[tx, ty].IsSolid)
                            {
                                var open = new[] { (xx, y), (xx + 1, y), (xx, y + 1), (xx + 1, y + 1) }.Where(t => !m.Tiles[t.Item1, t.Item2].IsSolid).ToList();
                                if (open.Count == 0) { used.Add(best.k.Index); continue; }
                                tx = open[0].Item1; ty = open[0].Item2;
                            }
                            used.Add(best.k.Index); used.Add(best.b[0].Index); used.Add(best.b[1].Index);
                            rises.Add(Tuple.Create(best.k, best.b[0], best.b[1], tx, ty));
                            free--;
                        }
                    }
                foreach (var r in rises)
                {
                    foreach (var o in new[] { r.Item1, r.Item2, r.Item3 }) e.DeleteObject(o.Index);
                    var spec = new ObjectSpec { Item = skeleton, IsQuantity = false, Link = 0, Attitude = 0, Hp = hp, FineX = r.Item1.FineX, FineY = r.Item1.FineY };
                    int i = e.AddObject(spec, r.Item4, r.Item5);
                    x.Sec.Add(level, string.Format("#{0} {1} ({2}, {3}) from #{4} {5}, #{6} {7}, #{8} {9}", i, s.Names.Get(skeleton), r.Item4, r.Item5,
                        r.Item1.Index, s.Names.Get(r.Item1.Item), r.Item2.Index, s.Names.Get(r.Item2.Item), r.Item3.Index, s.Names.Get(r.Item3.Item)));
                }
            }
            x.Sec.Notes.Add(string.Format("A skull and two bones (0xC4-0xC6, {0}) make one {1} (0x{2:X2}, {3} hit points), hostile.", s.IsUw2 ? "0xDD" : "0xDC", s.Names.Get(skeleton), skeleton, hp));
            if (noRoom > 0) x.Sec.Notes.Add(noRoom + " skeleton(s) could not rise: no free mobile record left.");
        }

        static IEnumerable<int> Descendants(LevelModel m, int i, int depth = 0)
        {
            if (depth > 64) yield break;
            foreach (int c in m.Objects[i].Children)
            {
                yield return c;
                foreach (int d in Descendants(m, c, depth + 1)) yield return d;
            }
        }

        static double Dist(ObjectInfo a, ObjectInfo b)
        {
            double dx = a.WorldX - b.WorldX, dy = a.WorldY - b.WorldY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
