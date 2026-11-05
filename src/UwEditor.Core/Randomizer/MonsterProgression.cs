using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The monster swap's Uniform progression rule (see <see cref="Progression"/>).</summary>
    public static partial class MonsterSwap
    {
        /// <summary>The movement classes Uniform progression ranks apart: 0 fliers, 1 swimmers, 2 fire-resistant walkers, 3 other walkers.</summary>
        public static int MoveClass(CreatureKind k)
        {
            if (k.Flier) return 0;
            if (k.Swims) return 1;
            if (k.Fireproof) return 2;
            return 3;
        }

        /// <summary>A movement class in words.</summary>
        public static string MoveClassName(int c)
        {
            switch (c)
            {
                case 0: return "fliers";
                case 1: return "swimmers";
                case 2: return "fire-resistant";
                default: return "walkers";
            }
        }

        /// <summary>
        /// Uniform progression: each movement class (<see cref="MoveClass"/>) ranked on its own over
        /// the whole game by the strength measure, and handed out level by level in the walk
        /// (<see cref="Progression.Assign"/>): each level keeps its number of creatures of the class
        /// and takes the next ones. A spot takes only a creature that may stand on its floor
        /// (CreatureTerrain) and, if one does, whose height fits; one whose own creature cannot stand
        /// there (the game put it there) keeps it. Level-bound creatures are not ranked: they trade
        /// at random among the level-bound spots of their level and class. Report lines go to
        /// report: per class and level, the count and the strengths before → after, and how many
        /// spots took the nearest-ranked creature that fits instead of one of their level's share.
        /// </summary>
        static void Progress(GameSession s, MonsterSwapOptions opt, Random rng, List<MonsterSpot> moving, Func<MonsterSpot, int> headroom, Action<MonsterSpot, MonsterSpot, bool> put, StringBuilder report)
        {
            var kept = moving.Where(x => CreatureTerrain.Restricted(x.Floor) && !CreatureTerrain.CanStand(x.Kind, x.Floor)).ToList();
            var keptSet = new HashSet<MonsterSpot>(kept);
            var free = moving.Where(x => !keptSet.Contains(x)).ToList();
            int fallbacks = 0, relaxed = 0;
            var groups = new List<(string Name, List<MonsterSpot> Spots, bool Ranked)>();
            for (int c = 0; c < 4; c++) groups.Add((MoveClassName(c), free.Where(x => x.LevelBound == null && MoveClass(x.Kind) == c).ToList(), true));
            foreach (var g in free.Where(x => x.LevelBound != null).GroupBy(x => (x.Level, MoveClass(x.Kind))).OrderBy(g => Progression.OrderKey(s, g.Key.Level)).ThenBy(g => g.Key.Item2))
                groups.Add((MoveClassName(g.Key.Item2) + ", level-bound on " + Progression.LevelName(s, g.Key.Level), g.ToList(), false));

            report.AppendLine("Uniform progression: each movement class sorted by strength (ties at random) and handed out level by level in the order " + Progression.OrderText(s) + "; each level keeps its number of creatures of the class.");
            foreach (var g in groups)
            {
                var spots = g.Spots;
                if (spots.Count == 0) continue;
                var room = spots.Select(headroom).ToArray();
                Func<int, int, int> fit = (i, j) =>
                {
                    if (!CreatureTerrain.CanStand(spots[j].Kind, spots[i].Floor)) return 0;
                    return spots[j].Kind.Height <= room[i] ? 2 : 1;
                };
                int fb, rl;
                var levels = spots.Select(x => x.Level).ToList();
                var p = Progression.Assign(rng, s, levels, spots.Select(x => x.Strength).ToList(), fit, out fb, out rl);
                bool loot = opt.LootWithMonster || spots.Any(x => x.Whoami > 0 && x.O.Children.Count > 0);
                for (int i = 0; i < spots.Count; i++) put(spots[i], spots[p[i]], loot);
                fallbacks += fb; relaxed += rl;
                if (!g.Ranked) continue;
                report.AppendFormat("  {0}, {1} creatures (count: strength before → after):\n", g.Name, spots.Count);
                foreach (int l in Progression.LevelOrder(s, levels))
                {
                    var idx = Enumerable.Range(0, spots.Count).Where(i => levels[i] == l).ToList();
                    report.AppendFormat("    {0}: {1}: {2} → {3}\n", Progression.LevelName(s, l), idx.Count, Progression.Range(idx.Select(i => spots[i].Strength)), Progression.Range(idx.Select(i => spots[p[i]].Strength)));
                }
            }
            int bound = free.Count(x => x.LevelBound != null);
            if (bound > 0) report.AppendFormat("  {0} level-bound creatures are not ranked: they trade at random within their level and class.\n", bound);
            if (kept.Count > 0) report.AppendFormat("  {0} creatures stand on water or lava that they could not be put on (the game put them there), so they stay.\n", kept.Count);
            report.AppendFormat("  Nearest fit: {0} spot(s) took the nearest-ranked creature that fits instead of one of their level's share; {1} took one whose height does not fit (nothing left fit).\n", fallbacks, relaxed);
        }

        /// <summary>The formula's lines for Uniform progression.</summary>
        static void ProgressionFormula(GameSession s, StringBuilder b)
        {
            b.AppendLine("Uniform progression, each movement class on its own (fliers; swimmers; fire-resistant; other walkers), over the whole game:");
            b.AppendLine("  sort its n creatures by S, ties at random; walk the levels in the order " + Progression.OrderText(s) + ";");
            b.AppendLine("  level ℓ with n_ℓ creatures of the class takes ranks r_ℓ … r_ℓ + n_ℓ − 1,  r_ℓ = 1 + Σ n_k over the levels k before it;");
            b.AppendLine("  within ℓ the spots take those creatures at random, the spot with the fewest that fit first; a creature fits a spot");
            b.AppendLine("  when it may stand on its floor (fliers anywhere; swimmers on water; fire-resistant on lava) and its height fits.");
            b.AppendLine("  A spot none of its share fits takes the next-ranked creature that does (pulled from the next levels; the one");
            b.AppendLine("  left spills into the next level); with none left, the height is relaxed.");
            b.AppendLine("  Level-bound creatures trade at random within their level and class; one on water or lava it could not be put on stays.");
        }
    }
}
