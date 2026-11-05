using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Uw1;

namespace UwEditor.Core.Editing
{
    /// <summary>A square a trap or trigger acts on.</summary>
    public sealed class TrapTarget
    {
        public int Level, X, Y;
        public string What;
        /// <summary>The fields that hold it, for "click and choose": quality = x, owner = y, and for a teleport z = the level.</summary>
        public bool Editable;
        public override string ToString() { return string.Format("({0}, {1}): {2}", X, Y, What); }
    }

    /// <summary>A trigger and the traps its link reaches, in order.</summary>
    public sealed class TrapChain
    {
        public ObjectInfo Trigger;
        public readonly List<ObjectInfo> Steps = new List<ObjectInfo>();
        public int TargetX { get { return Trigger.Quality; } }
        public int TargetY { get { return Trigger.Owner; } }
    }

    /// <summary>
    /// Triggers, traps and their chains on a level (both games' event/TRIGGER.C): a trigger
    /// (0x1A0-0x1BF) fires the trap its link names, and the whole chain runs at the trigger's
    /// target square (quality, owner), not where the objects lie. Each trap passes on along its
    /// own link. Traps and triggers follow the link whatever is_quant says.
    /// </summary>
    public static class Triggers
    {
        public static bool IsTrigger(int item) { return item >= 0x1A0 && item < 0x1C0; }
        public static bool IsTrap(int item) { return item >= 0x180 && item < 0x1A0; }
        public static bool IsTeleport(int item) { return item == 0x181; }
        public static bool IsDeleteTrap(int item) { return item == 0x18B; }

        /// <summary>Whether this object's quality and owner are a square on the map (triggers, teleports, delete object traps).</summary>
        public static bool HasTargetSquare(int item) { return IsTrigger(item) || IsTeleport(item) || IsDeleteTrap(item); }

        static bool HasLink(ObjectInfo o) { return o.IsTrapOrTrigger || !o.IsQuantity; }

        public static ObjectInfo LinksTo(LevelModel m, ObjectInfo o)
        {
            if (o == null || !HasLink(o) || o.Link <= 0 || o.Link >= m.Objects.Length) return null;
            var t = m.Objects[o.Link];
            return t.Free ? null : t;
        }

        /// <summary>The objects whose link names o (triggers, earlier traps, levers and doors holding it).</summary>
        public static List<ObjectInfo> LinkedFrom(LevelModel m, ObjectInfo o)
        {
            var list = new List<ObjectInfo>();
            foreach (var x in m.Objects)
            {
                if (x.Free || x == o || x.Link != o.Index || !HasLink(x)) continue;
                if (!x.IsTrapOrTrigger && (x.IsCritter || (ItemCategories.Of(x.Item, m.Session.Kind) & ItemCategory.Container) != 0)) continue;
                list.Add(x);
            }
            return list;
        }

        /// <summary>The trigger, then each trap in turn (a loop is cut where it comes back).</summary>
        public static TrapChain Chain(LevelModel m, ObjectInfo trigger)
        {
            var c = new TrapChain { Trigger = trigger };
            var seen = new HashSet<int> { trigger.Index };
            var next = LinksTo(m, trigger);
            while (next != null && seen.Add(next.Index) && c.Steps.Count < 256)
            {
                c.Steps.Add(next);
                // a trigger reached in a chain runs its own chain; a non-trap ends it
                if (!next.IsTrapOrTrigger || IsTrigger(next.Item)) break;
                next = LinksTo(m, next);
            }
            return c;
        }

        /// <summary>The triggers placed on the level (on a tile or inside something there).</summary>
        public static List<ObjectInfo> TriggersOf(LevelModel m)
        {
            return m.Objects.Where(o => !o.Free && o.TileX >= 0 && IsTrigger(o.Item)).OrderBy(o => o.Index).ToList();
        }

        public static List<TrapChain> Chains(LevelModel m) { return TriggersOf(m).Select(t => Chain(m, t)).ToList(); }

        /// <summary>Traps on the level no trigger's chain reaches (fired by a door, a hack, a conversation, a schedule, or nothing).</summary>
        public static List<ObjectInfo> Unreached(LevelModel m, List<TrapChain> chains)
        {
            var reached = new HashSet<int>(chains.SelectMany(c => c.Steps).Select(o => o.Index));
            return m.Objects.Where(o => !o.Free && o.TileX >= 0 && IsTrap(o.Item) && !reached.Contains(o.Index)).OrderBy(o => o.Index).ToList();
        }

        /// <summary>The item a create-object trap copies as a random monster in UW2 (include/items.h ITEM_ADVENTURER).</summary>
        public const int Adventurer = 0x7F;

        /// <summary>
        /// A wandering-monster spawner: a create-object trap on a tile whose flags are 0 (no
        /// trigger counts itself on it) and whose link holds a mobile object. The game runs these
        /// itself, without a trigger (event/TRIGGER.C DoWanderingMonsters, both games).
        /// </summary>
        public static bool IsSpawner(LevelModel m, ObjectInfo o)
        {
            if (o == null || o.Free || o.Item != 0x187 || o.TileX < 0 || o.IsQuantity) return false;
            int flags = (o.Record[1] >> 1) & 7;
            return flags == 0 && o.Link > 0 && o.Link < LevelBlock.Mobiles && o.Link < m.Objects.Length && !m.Objects[o.Link].Free;
        }

        /// <summary>
        /// The first create-object trap on o's square, searching the tile's list and the lists
        /// inside its objects in order (obj/OBJECTS.C Obj_FindInMap with recursion): the game's
        /// wandering-monster scan takes only that one on each square, so a spawner behind another
        /// create-object trap there never runs.
        /// </summary>
        public static ObjectInfo FirstCreateTrapOn(LevelModel m, int x, int y)
        {
            foreach (var o in m.TileObjects(x, y))
            {
                var f = FirstCreateTrapIn(m, o, 0);
                if (f != null) return f;
            }
            return null;
        }

        static ObjectInfo FirstCreateTrapIn(LevelModel m, ObjectInfo o, int depth)
        {
            if (o.Item == 0x187) return o;
            if (depth > 16) return null;
            foreach (int c in o.Children)
            {
                var f = FirstCreateTrapIn(m, m.Objects[c], depth + 1);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>A spawner the game never runs, because another create-object trap comes first on its square.</summary>
        public static bool SpawnerHidden(LevelModel m, ObjectInfo o)
        {
            if (!IsSpawner(m, o)) return false;
            var first = FirstCreateTrapOn(m, o.TileX, o.TileY);
            return first != null && first.Index != o.Index;
        }

        /// <summary>The chance, in percent, that a spawner makes its creature each time it runs: (63 − quality) in 63 (UseTrap TRAP_CREATE_OBJECT).</summary>
        public static double SpawnChance(int quality) { return Math.Max(0, 63 - quality) * 100.0 / 63; }

        /// <summary>The quality that gives a spawn chance of about percent.</summary>
        public static int SpawnQuality(double percent) { return Math.Max(0, Math.Min(63, (int)Math.Round(63 - percent * 63 / 100))); }

        /// <summary>What a wandering-monster spawner does, in a sentence or two (null when o is not one).</summary>
        public static string SpawnerText(GameSession s, LevelModel m, ObjectInfo o)
        {
            if (!IsSpawner(m, o)) return null;
            var t = m.Objects[o.Link];
            string what = s.IsUw2 && t.Item == Adventurer
                ? "a random monster (the \"adventurer\" template: the creature is chosen each time from the level's place in its world, the castle plot's progress and the player's level)"
                : (s.Names.Get(t.Item) ?? "item 0x" + t.Item.ToString("X3"));
            if (SpawnerHidden(m, o))
                return string.Format("wandering-monster spawner that never runs: create-object trap #{0} comes first on its square, and the game's scan takes only the first one there.", FirstCreateTrapOn(m, o.TileX, o.TileY).Index);
            return string.Format("wandering-monster spawner: makes {0}, {1:0} % each time it runs. The game runs it every {2} game minutes with a 1 in 4 chance while the player is more than 8 squares away, and when he sleeps, but not while a creature it made is within 4 squares. The creature goes when the player sleeps.",
                what, SpawnChance(o.Quality), s.IsUw2 ? 10 : 8);
        }

        /// <summary>
        /// UW2's random monster for a spawner whose template is the adventurer (UseTrap
        /// TRAP_CREATE_OBJECT): creature 0x40 + row × 16 + column, rows 0 to
        /// lo − 1 with lo = (place in world × 3 + castle clock) / 9 (at least 1), columns 0 to
        /// range − 1 with range = player level + castle clock + 1 (at most 16); a pick is kept
        /// with chance 1 in its strength (eligible_castle_monster), never for strength 0.
        /// </summary>
        public static void AdventurerRange(int placeInWorld, int castleClock, int playerLevel, out int rows, out int columns)
        {
            rows = Math.Max(1, (placeInWorld * 3 + castleClock) / 9);
            columns = Math.Min(16, playerLevel + castleClock + 1);
        }

        /// <summary>The level's wandering-monster spawners.</summary>
        public static List<ObjectInfo> Spawners(LevelModel m) { return m.Objects.Where(o => IsSpawner(m, o)).OrderBy(o => o.Index).ToList(); }

        /// <summary>The triggers whose chain reaches o.</summary>
        public static List<ObjectInfo> TriggersReaching(LevelModel m, ObjectInfo o)
        {
            var found = new List<ObjectInfo>();
            var seen = new HashSet<int> { o.Index };
            var todo = new Queue<ObjectInfo>();
            todo.Enqueue(o);
            while (todo.Count > 0 && seen.Count < 1024)
                foreach (var x in LinkedFrom(m, todo.Dequeue()))
                {
                    if (!seen.Add(x.Index)) continue;
                    if (IsTrigger(x.Item)) found.Add(x);
                    else if (IsTrap(x.Item)) todo.Enqueue(x);
                }
            return found;
        }

        /// <summary>The squares o acts on: a trigger's target; a teleport's destination; a delete trap's square; for other traps, where the chains that reach them run.</summary>
        public static List<TrapTarget> Targets(LevelModel m, ObjectInfo o)
        {
            var list = new List<TrapTarget>();
            int level = m.Level;
            if (IsTrigger(o.Item))
                list.Add(new TrapTarget { Level = level, X = o.Quality, Y = o.Owner, What = "its chain runs here", Editable = true });
            else if (IsTeleport(o.Item))
            {
                int to = o.Z == 0 ? level : o.Z;
                list.Add(new TrapTarget { Level = to, X = o.Quality, Y = o.Owner, What = "teleports the player here" + (to != level ? " (level " + to + ")" : ""), Editable = true });
            }
            else if (IsDeleteTrap(o.Item))
                list.Add(new TrapTarget { Level = level, X = o.Quality, Y = o.Owner, What = "deletes its linked object from here", Editable = true });
            if (IsTrap(o.Item))
                foreach (var t in TriggersReaching(m, o))
                    list.Add(new TrapTarget { Level = level, X = t.Quality, Y = t.Owner, What = "runs here (from " + t + ")" });
            var unique = new List<TrapTarget>();
            foreach (var t in list)
            {
                if (t.X < 0 || t.X > 63 || t.Y < 0 || t.Y > 63) continue;
                if (unique.Exists(u => u.Level == t.Level && u.X == t.X && u.Y == t.Y)) continue;
                unique.Add(t);
            }
            return unique;
        }

        /// <summary>What the kind of trap or trigger does, and what each field means, for the help text.</summary>
        public static string Explain(GameSession s, ObjectInfo o)
        {
            if (!o.IsTrapOrTrigger) return null;
            var sb = new System.Text.StringBuilder();
            if (s.IsUw2)
            {
                Uw2Traps.ItemNames = i => s.Names.Get(i);
                string d = Uw2Traps.Describe(o.Item);
                if (d != null) sb.AppendLine(d);
                int flags = (o.Record[1] >> 1) & 7;
                string now = null;
                try { now = Uw2Traps.Decode(o.Item, o.Quality, o.Owner, o.Z, o.Heading, o.FineX, o.FineY, o.IsQuantity, o.Link, i => s.Strings.Get(9, i)); }
                catch (Exception) { }
                if (!string.IsNullOrEmpty(now)) { sb.AppendLine(); sb.Append("With these values: ").AppendLine(now); }
                if (sb.Length == 0) sb.Append("There is no description of this trap for UW2.");
            }
            else
            {
                if (Uw1Traps.IsTrigger(o.Item)) sb.AppendLine(Uw1Traps.TriggerNames[o.Item - Uw1Traps.FirstTrigger] + ".");
                else if (Uw1Traps.IsTrap(o.Item)) sb.AppendLine(Uw1Traps.TrapNames[o.Item - Uw1Traps.FirstTrap] + ".");
                if (o.Item == 0x183) sb.AppendLine("This one (quality " + o.Quality + "): " + Uw1Traps.HackTrapName(o.Quality) + ".");
                foreach (var r in Uw1Traps.Roles(o.Item)) sb.Append("• ").Append(r.Field).Append(": ").AppendLine(r.Meaning);
                if (o.Item == 0x190) { string t = s.Strings.Get(9, o.Quality << 5 | o.Owner); if (!string.IsNullOrEmpty(t)) sb.AppendLine("Text now: " + t.Trim()); }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>The numbered variable a set or check variable trap uses, for the global variables window (or -1).</summary>
        public static int VariableOf(GameSession s, ObjectInfo o)
        {
            if (o.Item != 0x18D && o.Item != 0x18E) return -1;
            if (s.IsUw2) return o.Item == 0x18D ? o.Z + (o.FineX << 7) : o.Z | (o.FineX & 3) << 7;
            return o.Z;   // UW1: 0 a quest bit, 1-63 a variable
        }

        /// <summary>A short summary of what a trap or trigger does, for the chains list.</summary>
        public static string Short(GameSession s, ObjectInfo o)
        {
            if (s.IsUw2)
            {
                try
                {
                    Uw2Traps.ItemNames = i => s.Names.Get(i);
                    string d = Uw2Traps.Decode(o.Item, o.Quality, o.Owner, o.Z, o.Heading, o.FineX, o.FineY, o.IsQuantity, o.Link, i => s.Strings.Get(9, i));
                    if (!string.IsNullOrEmpty(d)) return d;
                }
                catch (Exception) { }
            }
            switch (o.Item)
            {
                case 0x180: return o.Owner != 0 ? "poisons (strength " + o.Quality + ")" : o.Quality + " damage";
                case 0x181: return string.Format("teleport to ({0}, {1}){2}", o.Quality, o.Owner, o.Z != 0 ? " on level " + o.Z : "");
                case 0x183: return s.IsUw2 ? "hack " + o.Quality : Uw1Traps.HackTrapName(o.Quality);
                case 0x185: return "change terrain";
                case 0x187: return "create its linked object";
                case 0x188: return o.Quality == 1 ? "open the door" : o.Quality == 2 ? "close the door" : o.Quality == 3 ? "toggle the door" : "swap the door's lock";
                case 0x18B: return string.Format("delete #{0} from ({1}, {2})", o.Link, o.Quality, o.Owner);
                case 0x18D: return "set variable " + VariableOf(s, o);
                case 0x18E: return "check variable " + VariableOf(s, o);
                case 0x190: { string t = s.Strings.Get(9, o.Quality << 5 | (s.IsUw2 ? o.Owner & 31 : o.Owner)); return t == null ? "text" : "\"" + Trim(t, 60) + "\""; }
            }
            if (IsTrigger(o.Item)) return string.Format("runs on ({0}, {1})", o.Quality, o.Owner);
            return "";
        }

        static string Trim(string t, int n) { t = t.Replace('\n', ' ').Trim(); return t.Length > n ? t.Substring(0, n) + "…" : t; }
    }
}
