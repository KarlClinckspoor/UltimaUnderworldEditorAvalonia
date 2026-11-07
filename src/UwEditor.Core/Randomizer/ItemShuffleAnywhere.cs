using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Randomizer
{
    public sealed partial class ShuffleOptions
    {
        /// <summary>
        /// Property shuffle: the items it rolls that lie on the floor go to random squares of their
        /// level (Anywhere). The item swap never moves them: it keeps items where they were.
        /// </summary>
        public bool PlaceAnywhere { get; set; } = true;
    }

    /// <summary>An object put on another square of its level (Place anywhere).</summary>
    public sealed class ObjectMove
    {
        public int Index, FromX, FromY;
        public AnywhereSpot To;
        public string Name = "";
        public override string ToString() { return string.Format("#{0} {1}: ({2}, {3}) -> {4}", Index, Name, FromX, FromY, To); }
    }

    public static partial class ItemShuffle
    {
        /// <summary>
        /// Place anywhere for the property shuffle: each object a group rolled (Touched) that lies
        /// on the floor, but the starting equipment's, goes to a random square of its level, one
        /// object a square while there are squares. The squares qualify as Anywhere.Qualifies says,
        /// anywhere on the level: these items carry no logic, so where they land does not matter to
        /// finishing the game (Karl). Its own random numbers, so turning it on or off leaves the
        /// rolls alone.
        /// </summary>
        static void PlaceAnywhere(GameSession s, Func<int, LevelModel> level, ShuffleOptions opt, List<Cand> all, ShuffleResult res)
        {
            var start = new HashSet<(int, int)>(StartTiles(s));
            var rolled = all.Where(c => c.Touched).ToList();
            var moving = rolled.Where(c => c.O.OnTile && !(c.Level == 1 && start.Contains((c.O.TileX, c.O.TileY)))).ToList();
            if (moving.Count == 0) return;
            var terrain = CreatureTerrain.TryLoad(s);
            var rng = new Random(unchecked(opt.Seed * 31 + 0x51A7));
            int moved = 0;
            var none = new List<int>();
            foreach (var g in moving.GroupBy(c => c.Level).OrderBy(g => g.Key))
            {
                var m = level(g.Key);
                var squares = Anywhere.Squares(s, m, terrain, null);
                var list = g.OrderBy(c => c.Index).ToList();
                var spots = Anywhere.Spots(rng, m, squares, list.Count);
                if (spots.Count == 0) { none.Add(g.Key); continue; }
                var moves = new List<ObjectMove>();
                for (int k = 0; k < list.Count; k++)
                    moves.Add(new ObjectMove { Index = list[k].Index, FromX = list[k].O.TileX, FromY = list[k].O.TileY, To = spots[k], Name = s.Names.Get(list[k].R.Item) });
                res.Moves[g.Key] = moves;
                moved += moves.Count;
            }
            res.Report.AppendFormat("Place anywhere: {0} rolled object(s) lying on the floor go to random squares of their level ({1} level(s)); the starting equipment stays.{2}\n", moved, res.Moves.Count,
                none.Count == 0 ? "" : " No square qualifies on level(s) " + string.Join(", ", none) + ": their objects stay.");
        }
    }
}
