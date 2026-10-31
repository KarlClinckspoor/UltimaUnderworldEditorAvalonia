using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A door between two zones and what it takes to get through it.</summary>
    public sealed class DoorPassage
    {
        public int Level, X, Y, Door;
        public int ZoneA, ZoneB;
        public string Kind;         // door, portcullis, secret
        public string Requires;     // the requirement language
        public string Notes;
        public override string ToString() { return string.Format("L{0} ({1}, {2}) #{3} {4}: {5} <-> {6}, {7}", Level, X, Y, Door, Kind, ZoneA, ZoneB, Requires); }
    }

    /// <summary>
    /// The doors that join zones and their locks, read as the game reads them (obj/USEITEMS.C
    /// checkLock, combat/DAMAGE.C damage_object): a door with no lock, or an unlocked one, is free;
    /// a locked one opens with its key (the lock's link &amp; 0x1FF; 0 = no key fits), by picking when
    /// its difficulty z (the lock's Z) is below 15 (3z skill; z 14 needs 31), or by bashing when the
    /// door can be broken (COMOBJ quality class below 3 and not tenacious). Open doors are free.
    /// </summary>
    public static class ZoneDoors
    {
        public static List<DoorPassage> Find(GameSession s, Func<int, LevelModel> level, ZoneSet zs, IEnumerable<int> levels = null)
        {
            var list = new List<DoorPassage>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || (levels != null && !levels.Contains(slot.Level))) continue;
                if (slot.Level > zs.Levels || zs.Tiles[slot.Level - 1] == null) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                foreach (var o in m.Objects)
                {
                    if (o.Free || !o.OnTile || o.Item < 0x140 || o.Item > 0x14F) continue;
                    int z0 = zs.ZoneAt(slot.Level, o.TileX, o.TileY);
                    var others = new List<int>();
                    foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int z = zs.ZoneAt(slot.Level, o.TileX + d.Item1, o.TileY + d.Item2);
                        if (z > ZoneSet.Unassigned && z != z0 && !others.Contains(z)) others.Add(z);
                    }
                    if (z0 <= ZoneSet.Unassigned)
                    {
                        // a door in a tile of its own: it joins the zones on its two sides
                        if (others.Count < 2) continue;
                        z0 = others[0];
                        others.RemoveAt(0);
                    }
                    if (others.Count == 0) continue;
                    string notes;
                    string req = Requirement(s, m, o, out notes);
                    string kind = (o.Item & 7) == 6 ? "portcullis" : (o.Item & 7) == 7 ? "secret" : "door";
                    foreach (int z in others)
                        list.Add(new DoorPassage { Level = slot.Level, X = o.TileX, Y = o.TileY, Door = o.Index, ZoneA = Math.Min(z0, z), ZoneB = Math.Max(z0, z), Kind = kind, Requires = req, Notes = notes });
                }
            }
            return list;
        }

        /// <summary>What it takes to open a door, in the requirement language, and why.</summary>
        public static string Requirement(GameSession s, LevelModel m, ObjectInfo door, out string notes)
        {
            var why = new List<string>();
            if (door.Item >= 0x148) { notes = "open door"; return "free"; }
            ObjectInfo lockObj = null;
            foreach (int c in door.Children) if (m.Objects[c].Item == 0x10F) lockObj = m.Objects[c];
            bool portcullis = (door.Item & 7) == 6;
            if (portcullis) why.Add("portcullis: a lever or button somewhere may open it too");
            if (lockObj == null) { why.Add("no lock"); notes = string.Join("; ", why); return "free"; }
            bool locked = ((lockObj.Record[1] >> 1) & 1) != 0;
            if (!locked) { why.Add("its lock is open"); notes = string.Join("; ", why); return "free"; }
            int key = lockObj.Link & 0x1FF;
            int z = lockObj.Z;
            int qclass = (Races.ComObjByte(s, door.Item, 6) >> 2) & 3;
            bool tenacious = (door.Record[1] & 0x20) != 0;
            bool breakable = qclass < 3 && !tenacious;
            var alts = new List<string>();
            if (key > 0) { alts.Add("key:" + key); why.Add("key " + key); } else why.Add("no key fits");
            if (z < 15) { alts.Add("pick:" + z); why.Add("pick difficulty " + z + (z == 14 ? " (skill 31)" : " (skill " + 3 * z + ")")); } else why.Add("cannot be picked");
            if (breakable) { alts.Add("bash"); why.Add("breakable (quality class " + qclass + ")"); } else why.Add(tenacious ? "indestructible (tenacious)" : "unbreakable (quality class 3)");
            if (alts.Count == 0)
            {
                why.Add("only a lever, a trap or a conversation opens it");
                notes = string.Join("; ", why);
                return string.Format("flag:door_l{0}_{1}_{2}", m.Level, door.TileX, door.TileY);
            }
            notes = string.Join("; ", why);
            return string.Join(" | ", alts);
        }

        /// <summary>The doors between two zones, and a requirement for going from one to the other through any of them.</summary>
        public static string Suggest(IEnumerable<DoorPassage> doors, int a, int b, out List<DoorPassage> used)
        {
            used = doors.Where(d => (d.ZoneA == Math.Min(a, b) && d.ZoneB == Math.Max(a, b))).ToList();
            if (used.Count == 0) return null;
            var reqs = used.Select(d => d.Requires).Distinct().ToList();
            if (reqs.Contains("free")) return "free";
            return reqs.Count == 1 ? reqs[0] : string.Join(" | ", reqs.Select(r => r.Contains("|") ? "(" + r + ")" : r));
        }
    }
}
