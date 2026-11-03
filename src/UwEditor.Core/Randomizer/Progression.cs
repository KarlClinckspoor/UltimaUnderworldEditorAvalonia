using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// The Uniform progression rule of the item swap and the monster swap: everything taking part
    /// is sorted by strength, the levels are walked in the game's order, and each level takes the
    /// next of the sorted list for its number of spots (so the weakest end up on the first level and
    /// the strongest on the last). Within a level the spots are filled at random.
    ///
    /// The walk: UW1 levels 1 to 9; UW2 world by world in the order the player meets them,
    /// Britannia, the Prison Tower, Killorn Keep, the Ice Caverns, Talorus, the Scintillus Academy,
    /// the Pits of Carnage, the Tomb of Praecor Loth, the Ethereal Void (levels in order within
    /// each). The worlds are numbered (level − 1) / 8 (uw2 include/map.h LEVELS_PER_WORLD); world 6
    /// is the Tomb (levels 49-56, uw2 event/TRIGGER.C, Praecor Loth's quest 7) and world 7 the Pits
    /// (uw2 critter/PATHFIND.C "the Pits of Carnage (world 7)"), so the walk takes world 7 before
    /// world 6. Slots after the nine worlds (UW2's map-scrap level 73, which the player never
    /// walks) are left out: what is there stays.
    /// </summary>
    public static class Progression
    {
        /// <summary>UW2's worlds in the order of the walk (GameProfile.Uw2Worlds numbering).</summary>
        public static readonly int[] Uw2WorldOrder = { 0, 1, 2, 3, 4, 5, 7, 6, 8 };

        /// <summary>A curse (class 9) or an enchantment without degrees counts as the middle of 1-8 (Karl: "consider curse a mid-range strength enchantment").</summary>
        public const double MidLevel = 4.5;

        /// <summary>The position of a level in the walk (smaller first).</summary>
        public static int OrderKey(GameSession s, int level)
        {
            if (!s.IsUw2) return level;
            int w = s.Profile.WorldOf(level);
            int at = Array.IndexOf(Uw2WorldOrder, w);
            if (at < 0) at = Uw2WorldOrder.Length + w;
            return at * 1000 + s.Profile.PlaceInWorld(level);
        }

        /// <summary>Whether the walk takes a level: every UW1 level, UW2's levels of the nine worlds (not the slots after them).</summary>
        public static bool InWalk(GameSession s, int level)
        {
            return !s.IsUw2 || Array.IndexOf(Uw2WorldOrder, s.Profile.WorldOf(level)) >= 0;
        }

        /// <summary>The levels the walk takes, in its order.</summary>
        public static List<int> LevelOrder(GameSession s, IEnumerable<int> levels)
        {
            return levels.Distinct().Where(l => InWalk(s, l)).OrderBy(l => OrderKey(s, l)).ToList();
        }

        /// <summary>The walk in words, for the formula and the report.</summary>
        public static string OrderText(GameSession s)
        {
            if (!s.IsUw2) return "levels 1 → 2 → … → 9";
            return string.Join(" → ", Uw2WorldOrder.Select(w => s.Profile.WorldName(w))) + ", levels in order within each world (the slots after the worlds keep theirs)";
        }

        /// <summary>A level's name for the reports: "level 3" or "Talorus 2 (34)".</summary>
        public static string LevelName(GameSession s, int level)
        {
            return s.IsUw2 ? s.Profile.LevelName(level) : "level " + level;
        }

        /// <summary>OBJECTS.DAT's bytes, or null.</summary>
        public static byte[] ReadObjects(GameSession s)
        {
            string p = GameProfile.Find(s.DataFolder, "OBJECTS.DAT");
            try { return p == null ? null : File.ReadAllBytes(p); } catch (Exception) { return null; }
        }

        /// <summary>
        /// A weapon's damage from OBJECTS.DAT: a melee weapon (0x00-0x0F) its highest of slash,
        /// bash and stab (struct Weapon at 2 + 8 × item, damage[3]; combat/COMBAT.C
        /// DoPlayerWeapon uses the swing's one); ammunition (0x10-0x17) its missile damage (struct
        /// MissileInfo at 0x82 + 3 × (item − 0x10), motion/OBJPHYS.C missile_newhit); a launcher
        /// (0x18-0x1F) the damage of the ammunition it fires (its MissileInfo at 0x9A, ammo byte =
        /// ammunition − 0x10, COMBAT.C GetPlayerWeapon / check_ammo), since the missile does the
        /// damage; its own byte when it has no ammunition. 0 for anything else.
        /// </summary>
        public static double WeaponDamage(byte[] objects, int item)
        {
            if (objects == null) return 0;
            if (item >= 0 && item < 0x10 && 4 + 8 * item < objects.Length)
                return Math.Max(objects[2 + 8 * item], Math.Max(objects[3 + 8 * item], objects[4 + 8 * item]));
            if (item >= 0x10 && item < 0x18 && 0x82 + 3 * (item - 0x10) < objects.Length) return objects[0x82 + 3 * (item - 0x10)];
            if (item >= 0x18 && item < 0x20 && 0x9C + 3 * (item - 0x18) < objects.Length)
            {
                int ammo = (sbyte)objects[0x9C + 3 * (item - 0x18)];
                if (ammo >= 0 && ammo < 8) return objects[0x82 + 3 * ammo];
                return objects[0x9A + 3 * (item - 0x18)];
            }
            return 0;
        }

        /// <summary>An armour piece's protection from OBJECTS.DAT (struct Armour at 0xB2 + 4 × (item − 0x20), byte 0); 0 for anything else.</summary>
        public static double ArmourProtection(byte[] objects, int item)
        {
            if (objects == null || item < 0x20 || item >= 0x40 || 0xB2 + 4 * (item - 0x20) >= objects.Length) return 0;
            return objects[0xB2 + 4 * (item - 0x20)];
        }

        /// <summary>
        /// The circle of one of the 64 spells objects carry: UW1 spell / 6 + 1 (48 and up have no
        /// runes: 9), UW2 spell / 8 + 1 (combat/RUNES.C player_cast, both games: "the circle is
        /// idx / 6 + 1", "idx / 8 + 1"; objects cast the same index, obj/USEITEMS.C
        /// decode_obj_spell, combat/SPELLS.C cast). SpellTables.Circle.
        /// </summary>
        public static int SpellCircle(bool uw2, int spell) { return SpellTables.Circle(uw2, spell & 0x3F); }

        /// <summary>
        /// The level of an object's enchantment (obj/USEITEMS.C decode_obj_spell): none 0; a spell
        /// (flag 11, major 0) its circle; class 12 on a weapon: UW1 (effect &amp; 7) + 1 (Accuracy
        /// and Damage 1-8), UW2 effects 0-7 add 1, 3, 5, 7 (EnchantmentEffects: 2 × (effect &amp; 3)
        /// + 1) and 8-15 are the special weapons (counted mid-range); class 12 on armour, shields
        /// and rings (effect &amp; 7) + 1 (Protection and Toughness 1-8); a curse (class 9) and every
        /// other effect without degrees <see cref="MidLevel"/>. A list link (contents, a spell
        /// object) is not an enchantment of the object itself: 0.
        /// </summary>
        public static double EnchantLevel(GameSession s, int item, bool enchanted, bool isQuantity, int link, bool flag11)
        {
            if (!enchanted || !isQuantity || link < 0x200) return 0;
            if (flag11)
            {
                if (((link & 0x1FF) >> 6) == 0) return SpellCircle(s.IsUw2, link & 0x3F);
                return MidLevel;
            }
            int major = (link & 0x1FF) >> 4, effect = link & 0xF;
            if (major != 12) return MidLevel;
            bool weapon = item < 0x20;
            if (weapon && s.IsUw2) return effect < 8 ? 2 * (effect & 3) + 1 : MidLevel;
            return (effect & 7) + 1;
        }

        /// <summary>
        /// Assigns sources to spots by the rule. levels[i] is spot i's level and strength[i] the
        /// strength of what spot i holds now; the result p says spot i takes what spot p[i] held.
        /// The holders are sorted by strength (ties broken by a random key, one draw each in
        /// index order); the levels are walked in <see cref="LevelOrder"/>; a level with n spots
        /// takes the next n of the sorted list. Without fit, the n are shuffled onto the level's
        /// spots. With fit(spot, holder) (2 fits, 1 fits but for its height, 0 may not go there),
        /// the level's spots are filled one at a time, the spot with the fewest fitting holders
        /// first (ties at random), each taking one of the level's share that fits, at random; a spot
        /// none of the share fits takes the lowest-ranked holder after them that does (pulled from
        /// the next levels, while the one it leaves spills into the next level): a fallback. With
        /// none left that fits, the height is relaxed; when nothing left may even stand there, the
        /// spot trades with an earlier spot of the group whose holder may stand on both.
        /// </summary>
        public static int[] Assign(Random rng, GameSession s, IList<int> levels, IList<double> strength, Func<int, int, int> fit, out int fallbacks, out int relaxed)
        {
            int n = levels.Count;
            fallbacks = relaxed = 0;
            var p = new int[n];
            if (n == 0) return p;
            var tie = new double[n];
            for (int i = 0; i < n; i++) tie[i] = rng.NextDouble();
            // levels outside the walk keep what they hold
            for (int i = 0; i < n; i++) p[i] = i;
            var walked = Enumerable.Range(0, n).Where(i => InWalk(s, levels[i])).ToList();
            var remaining = walked.OrderBy(i => strength[i]).ThenBy(i => tie[i]).ToList();
            var byLevel = walked.GroupBy(i => levels[i]).ToDictionary(g => g.Key, g => g.ToList());
            var done = new List<int>();
            foreach (int level in LevelOrder(s, levels))
            {
                var spots = byLevel[level];
                int k = spots.Count;
                var share = remaining.Take(k).ToList();
                if (fit == null)
                {
                    for (int i = share.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int t = share[i]; share[i] = share[j]; share[j] = t; }
                    for (int i = 0; i < k; i++) p[spots[i]] = share[i];
                    remaining.RemoveRange(0, k);
                    continue;
                }
                var key = spots.ToDictionary(x => x, x => rng.NextDouble());
                var open = spots.ToList();
                while (open.Count > 0)
                {
                    int spot = open.OrderBy(x => share.Count(h => fit(x, h) == 2)).ThenBy(x => key[x]).First();
                    open.Remove(spot);
                    var good = share.Where(h => fit(spot, h) == 2).ToList();
                    int pick;
                    if (good.Count > 0) pick = good[rng.Next(good.Count)];
                    else
                    {
                        pick = remaining.FirstOrDefault(h => fit(spot, h) == 2, -1);
                        if (pick < 0) { pick = remaining.FirstOrDefault(h => fit(spot, h) >= 1, -1); if (pick >= 0) relaxed++; }
                        if (pick < 0)
                        {
                            // nothing left may stand here: trade with an earlier spot whose holder may stand on both
                            int q = done.FirstOrDefault(d => fit(spot, p[d]) >= 1 && remaining.Any(h => fit(d, h) >= 1), -1);
                            if (q >= 0)
                            {
                                int h2 = remaining.First(h => fit(q, h) >= 1);
                                p[spot] = p[q]; p[q] = h2;
                                remaining.Remove(h2); share.Remove(h2);
                                done.Add(spot);
                                fallbacks++;
                                continue;
                            }
                            pick = remaining[0];
                            relaxed++;
                        }
                        if (!share.Contains(pick)) fallbacks++;
                    }
                    p[spot] = pick;
                    remaining.Remove(pick); share.Remove(pick);
                    done.Add(spot);
                }
            }
            return p;
        }

        static string Num(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        /// <summary>A range of strengths, "a–b", or "a" when they are all one.</summary>
        public static string Range(IEnumerable<double> v)
        {
            var l = v.ToList();
            if (l.Count == 0) return "–";
            double lo = l.Min(), hi = l.Max();
            return lo == hi ? Num(lo) : Num(lo) + "–" + Num(hi);
        }

        /// <summary>The median of a list (the mean of the middle two for an even count); 0 when empty.</summary>
        public static double Median(IEnumerable<double> v)
        {
            var l = v.OrderBy(x => x).ToList();
            if (l.Count == 0) return 0;
            return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
        }
    }
}
