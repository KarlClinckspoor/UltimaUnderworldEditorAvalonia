using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    public static partial class Challenges
    {
        /// <summary>UW1's Shak (conversation 2), who reforges the sword for 20 coins.</summary>
        public const int ShakWhoami = 2;
        /// <summary>UW1's Zak (conversation 28), who trades the Taper of Sacrifice for food.</summary>
        public const int ZakWhoami = 28;
        /// <summary>The coins Shak asks for the sword (conversation 2: global1 = 20, func_11F7 counts coins 0xA0 and 0xA1 by count_inv).</summary>
        public const int ShakGold = 20;
        /// <summary>
        /// The food left beside Zak: one piece of meat (0xB0, quality 40). Zak likes food (class 1011)
        /// and takes an offer once (offered - asked) * 100 / asked reaches his greed (conv/BARTER.C
        /// do_offer). The taper is worth 1 (COMOBJ value 1 at quality 40); a liked piece of meat
        /// 4 * 3 / 2 * 40 / 64 = 3, at worst 2 after his appraisal's blur (npc_assess at most 44 %),
        /// and his greed (haggle 9: 54 % ± 25 %) is at most 67 %: one piece is enough.
        /// </summary>
        public const int ZakFood = 0xB0;

        /// <summary>
        /// The only exceptions to the Vow of poverty and Starvation (Karl): the sword needs 20 coins
        /// for Shak and the taper needs food for Zak, so Vow of poverty puts one pile of 20 coins
        /// right beside Shak and Starvation the least food Zak takes (one piece of meat) right
        /// beside Zak. The pile is protected from the later challenges (Fasting, Merchant, Hoarding).
        /// UW1 only. Returns where it went ("L2 (61, 3)"), or null.
        /// </summary>
        static string PlaceException(Ctx x, int whoami, ObjectSpec spec, string what)
        {
            var s = x.S;
            if (s.IsUw2) return null;
            foreach (int level in x.W.Levels)
            {
                var m = x.W.Model(level);
                var npc = m.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.IsCritter && o.Record != null && o.Record.Length > 0x1A && o.Record[0x1A] == whoami);
                if (npc == null) continue;
                var spot = FreeSpotBeside(s, m, npc.TileX, npc.TileY);
                if (spot == null) { x.Sec.Notes.Add("No free square beside " + what + " on level " + level + ": nothing was put there."); return null; }
                var e = x.W.Editor(level);
                if (new LevelBlock(e.Data, s.Kind).StaticTop < 0) { x.Sec.Notes.Add("Level " + level + " has no free object for " + what + "'s pile."); return null; }
                int i = e.AddObject(spec, spot.Item1, spot.Item2);
                x.Exceptions.Add((long)level << 32 | (uint)i);
                string where = string.Format("L{0} ({1}, {2})", level, spot.Item1, spot.Item2);
                x.Sec.Add(level, string.Format("#{0} {1} × {2} put beside {3} at ({4}, {5})", i, s.Names.Get(spec.Item), spec.IsQuantity ? spec.Link : 1, what, spot.Item1, spot.Item2));
                return where;
            }
            x.Sec.Notes.Add(what + " is not in the levels: nothing was put beside him.");
            return null;
        }

        /// <summary>
        /// A square next to (x, y) to drop something on: open (not rock, slope or diagonal), no door,
        /// no water or lava (TERRAIN.DAT), within one floor step of the creature's square; the four
        /// sides first, then the corners, and an empty square before one with objects.
        /// </summary>
        public static Tuple<int, int> FreeSpotBeside(GameSession s, LevelModel m, int x, int y)
        {
            TerrainTable terrain = null;
            try { terrain = TerrainTable.Load(s); } catch (Exception) { }
            var order = new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };
            Tuple<int, int> busy = null;
            int h0 = m.Tiles[x, y].Height;
            foreach (var d in order)
            {
                int nx = x + d.Item1, ny = y + d.Item2;
                if (nx < 0 || ny < 0 || nx > 63 || ny > 63) continue;
                var t = m.Tiles[nx, ny];
                if (t.Type != TileType.Open || Math.Abs(t.Height - h0) > 1) continue;
                var here = m.TileObjects(nx, ny).ToList();
                if (here.Any(o => o.Item >= 0x140 && o.Item <= 0x14F)) continue;
                if (terrain != null && t.FloorIndex >= 0 && t.FloorIndex < m.FloorMap.Length)
                {
                    // UW1: the floor's class is its TERRAIN.DAT word >> 4 (1 water, 2 lava; map/TEXTMAPS.C)
                    int c = (terrain.Get(terrain.EntryOf(m.FloorMap[t.FloorIndex], true)) >> 4) & 3;
                    if (c == 1 || c == 2) continue;
                }
                if (here.Count == 0) return Tuple.Create(nx, ny);
                if (busy == null) busy = Tuple.Create(nx, ny);
            }
            return busy;
        }

        static void PovertyException(Ctx x)
        {
            var spec = new ObjectSpec { Item = 0xA0, Quality = 40, Link = ShakGold, IsQuantity = true };
            string at = PlaceException(x, ShakWhoami, spec, "Shak");
            if (at != null) x.Sec.Notes.Add(string.Format("The only exception: a pile of {0} coins at {1}, beside Shak, who reforges the sword for them (conversation 2).", ShakGold, at));
        }

        static void StarvationException(Ctx x)
        {
            var spec = new ObjectSpec { Item = ZakFood, Quality = 40, Link = 1, IsQuantity = true };
            string at = PlaceException(x, ZakWhoami, spec, "Zak");
            if (at != null) x.Sec.Notes.Add(string.Format("The only exception: one {0} at {1}, beside Zak, the least food he takes for the Taper of Sacrifice (conversation 28; conv/BARTER.C do_offer).", x.S.Names.Get(ZakFood), at));
        }
    }
}
