using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Editing
{
    /// <summary>One thing the world check found, with where to look.</summary>
    public sealed class WorldFinding
    {
        /// <summary>"no key", "opens nothing", "objects", "note".</summary>
        public string Kind;
        /// <summary>2 a problem, 1 a warning, 0 for information.</summary>
        public int Severity;
        public string Text;
        public int Level, X = -1, Y = -1, Index = -1;
        public override string ToString() { return Text; }
    }

    /// <summary>
    /// Checks across the whole world: locked locks no key in the levels (or the player's
    /// inventory) opens, keys whose lock is nowhere, and levels close to the game's object limits
    /// (255 mobile and 768 static records a level, obj/OBJECTS.C; the active list holds 0x100 + 4).
    /// Keys count wherever they lie: on the floor, in containers, in creatures' inventories, and
    /// as templates inside create-object traps (the trap makes them).
    /// </summary>
    public static class WorldCheck
    {
        public const int MobileLimit = 255, StaticLimit = 768;

        /// <summary>The check. inventoryKeys: lock numbers of the keys the player carries (a saved game), or null.</summary>
        public static List<WorldFinding> Run(GameSession s, Func<int, LevelModel> level, IEnumerable<int> inventoryKeys = null, double warnShare = 0.9)
        {
            var found = new List<WorldFinding>();
            var groups = KeyIndex.Build(s, level);
            var carried = new HashSet<int>(inventoryKeys ?? Enumerable.Empty<int>());
            foreach (var g in groups)
            {
                bool anyKey = g.Keys.Count > 0 || carried.Contains(g.Id);
                foreach (var l in g.Locks.Where(x => x.Locked))
                {
                    if (anyKey) continue;
                    if (g.Id == 0)
                    {
                        found.Add(new WorldFinding { Kind = "note", Severity = 0, Level = l.Level, X = l.TileX, Y = l.TileY, Index = l.Holder >= 0 ? l.Holder : l.Index,
                            Text = string.Format("Level {0} ({1}, {2}): {3} is locked with lock 0, which no key opens (a lockpick, a spell or force).", l.Level, l.TileX, l.TileY, l.Holder >= 0 ? "#" + l.Holder + " " + l.HolderName : "a lock") });
                        continue;
                    }
                    found.Add(new WorldFinding { Kind = "no key", Severity = 2, Level = l.Level, X = l.TileX, Y = l.TileY, Index = l.Holder >= 0 ? l.Holder : l.Index,
                        Text = string.Format("Level {0} ({1}, {2}): {3} is locked with lock {4}, and no key for it is in the levels{5}.", l.Level, l.TileX, l.TileY,
                            l.Holder >= 0 ? "#" + l.Holder + " " + l.HolderName : "#" + l.Index + " a lock", g.Id, inventoryKeys != null ? " or the player's inventory" : "") });
                }
                if (g.Locks.Count == 0)
                    foreach (var k in g.Keys)
                        found.Add(new WorldFinding { Kind = "opens nothing", Severity = 1, Level = k.Level, X = k.TileX, Y = k.TileY, Index = k.Index,
                            Text = string.Format("Level {0} ({1}, {2}): #{3} {4} has lock number {5}, but no lock in the levels has it{6}.", k.Level, k.TileX, k.TileY, k.Index, k.Name, g.Id,
                                string.IsNullOrWhiteSpace(g.Text) ? "" : " (\"" + g.Text.Trim() + "\")") });
            }
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                var b = m.Block;
                int freeM = b.MobileTop + 1, freeS = b.StaticTop + 1;
                int usedM = MobileLimit - freeM, usedS = StaticLimit - freeS;
                string name = s.IsUw2 && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : "Level " + slot.Level;
                int sev = usedM >= MobileLimit || usedS >= StaticLimit ? 2 : (usedM >= MobileLimit * warnShare || usedS >= StaticLimit * warnShare ? 1 : 0);
                found.Add(new WorldFinding { Kind = "objects", Severity = sev, Level = slot.Level,
                    Text = string.Format("{0}: {1} of {2} mobile objects, {3} of {4} static{5}.", name, usedM, MobileLimit, usedS, StaticLimit,
                        sev == 2 ? ": full, nothing more can be added (the game cannot drop or create objects there either)" : sev == 1 ? ": nearly full (the game needs room to drop, throw and create objects)" : "") });
            }
            return found.OrderByDescending(f => f.Severity).ThenBy(f => f.Kind == "objects" ? 1 : 0).ThenBy(f => f.Level).ToList();
        }
    }
}
