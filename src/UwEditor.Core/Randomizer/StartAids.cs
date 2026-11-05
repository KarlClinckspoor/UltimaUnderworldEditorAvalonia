using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Uw1;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>
    /// Helps for a shuffled game (UW1), chosen in the Apply window: a full torch and the map
    /// beside the start, and every level's map explored.
    /// </summary>
    public static class StartAids
    {
        public const int Torch = 0x91, LitTorch = 0x95, Map = 0x13B, FullTorch = 63;

        /// <summary>The start square and the eight around it, the start first.</summary>
        static IEnumerable<(int X, int Y)> Around(int sx, int sy)
        {
            yield return (sx, sy);
            foreach (var d in new[] { (1, 1), (1, 0), (0, 1), (-1, 0), (0, -1), (-1, -1), (1, -1), (-1, 1) }) yield return (sx + d.Item1, sy + d.Item2);
        }

        /// <summary>The objects lying on those squares, and what containers there hold.</summary>
        static List<ObjectInfo> Near(LevelModel m, int sx, int sy)
        {
            var list = new List<ObjectInfo>();
            var stack = new Stack<ObjectInfo>();
            foreach (var (x, y) in Around(sx, sy))
            {
                if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                foreach (var o in m.TileObjects(x, y)) stack.Push(o);
            }
            while (stack.Count > 0)
            {
                var o = stack.Pop();
                list.Add(o);
                if (o.IsCritter) continue;
                foreach (int c in o.Children) if (c > 0 && c < m.Objects.Length && !m.Objects[c].Free) stack.Push(m.Objects[c]);
            }
            return list;
        }

        /// <summary>Where new things go: the first open square around the start without a door (the shipped kit lies at the start + (1, 1)), else the start.</summary>
        static (int X, int Y) KitSquare(LevelModel m, int sx, int sy)
        {
            foreach (var (x, y) in Around(sx, sy).Skip(1).Concat(new[] { (sx, sy) }))
            {
                if (x < 1 || y < 1 || x > 62 || y > 62) continue;
                if (m.Tiles[x, y].IsSolid || m.Tiles[x, y].Type != TileType.Open) continue;
                if (m.TileObjects(x, y).Any(o => o.Item >= 0x140 && o.Item <= 0x14F)) continue;
                return (x, y);
            }
            return (sx, sy);
        }

        /// <summary>
        /// A torch of quality 63 and the map beside the start square (on the start or the eight
        /// squares around it, on the floor or in a container there): a torch found there is
        /// filled up, a missing one or a missing map is added on the floor beside the start
        /// (the map as the shipped one: quality 40, link 0x200). Returns what was done.
        /// </summary>
        public static List<string> PutKit(GameSession s, LevelEdits edits, StartPoint start)
        {
            var said = new List<string>();
            if (s.IsUw2) { said.Add("The torch and the map are for Ultima Underworld 1 only."); return said; }
            if (start == null) { said.Add("The torch and the map were not placed: no start square is known."); return said; }
            int level = start.Level, sx = (int)start.X, sy = (int)start.Y;
            var e = edits.For(level);
            var near = Near(e.Model, sx, sy);
            var torch = near.FirstOrDefault(o => o.Item == Torch || o.Item == LitTorch);
            var (kx, ky) = KitSquare(e.Model, sx, sy);
            if (torch != null)
            {
                if (torch.Quality < FullTorch)
                {
                    e.SetObjectField(torch.Index, (torch.IsMobile ? Uw1Schemas.MobileObject : Uw1Schemas.StaticObject)["Quality"], FullTorch);
                    said.Add(string.Format("The torch #{0} beside the start was filled up (quality {1} to 63).", torch.Index, torch.Quality));
                }
                else said.Add(string.Format("A full torch (#{0}) already lies beside the start.", torch.Index));
            }
            else
            {
                int i = e.AddObject(new ObjectSpec { Item = Torch, Quality = FullTorch, IsQuantity = true, Link = 1 }, kx, ky);
                said.Add(string.Format("A torch of quality 63 (#{0}) was put on level {1} ({2}, {3}), beside the start.", i, level, kx, ky));
            }
            var map = Near(e.Model, sx, sy).FirstOrDefault(o => o.Item == Map);
            if (map != null) said.Add(string.Format("The map (#{0}) already lies beside the start.", map.Index));
            else
            {
                int i = e.AddObject(new ObjectSpec { Item = Map, Quality = 40, IsQuantity = true, Link = 0x200 }, kx, ky);
                said.Add(string.Format("A map (#{0}) was put on level {1} ({2}, {3}), beside the start.", i, level, kx, ky));
            }
            return said;
        }

        /// <summary>
        /// Every level's map explored: each open square takes the code the game writes when the
        /// player sees it (Automap.Seen: the tile type, the floor's terrain, a door), rock stays
        /// empty; the notes are kept. Returns the levels changed, with their maps as they were.
        /// </summary>
        public static Dictionary<int, Tuple<byte[], List<MapNote>>> ExploreMaps(GameSession s, LevelEdits edits, string label)
        {
            var before = new Dictionary<int, Tuple<byte[], List<MapNote>>>();
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m = edits.Model(slot.Level);
                if (m == null || !m.Present) continue;
                var a = edits.AutomapFor(slot.Level);
                var map = (byte[])a.Map.Clone();
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        map[y * 64 + x] = Automap.Seen(s, m, x, y);
                if (map.AsSpan().SequenceEqual(a.Map)) continue;
                before[slot.Level] = Tuple.Create((byte[])a.Map.Clone(), a.Notes.Select(n => new MapNote { Text = n.Text, X = n.X, Y = n.Y }).ToList());
                a.Replace(map, a.Notes, label);
                edits.AutomapEdited(a);
            }
            return before;
        }
    }
}
