using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>
    /// The randomizer's zoning for the open game, shared by the zones window, the zone graph and
    /// the 3D view: the zone file, the zone being painted, painting with undo, and the key items
    /// and doors found in the levels.
    /// </summary>
    public sealed class ZoningState
    {
        public GameSession Session { get; private set; }
        readonly Func<int, LevelModel> level;
        public ZoneSet Set { get; private set; }
        public string Path { get; private set; }
        public bool Dirty { get; private set; }
        /// <summary>Something changed: "tiles" (the overlay), "zones" (names, colours, the list), "links", "file".</summary>
        public event Action<string> Changed;

        int current = 2;
        public int CurrentZone
        {
            get { return current; }
            set { if (current != value) { current = value; Changed?.Invoke("current"); } }
        }

        /// <summary>The view paints zones instead of tiles.</summary>
        public bool Painting;
        /// <summary>A click floods the connected area instead of painting one tile.</summary>
        public bool FillMode;
        /// <summary>The fill's highest climb between tiles, in floor steps (going down is never limited).</summary>
        public int MaxClimb = ZoneFlood.DefaultMaxClimb;

        /// <summary>The view shows the zone colours on the floors.</summary>
        public bool ShowOverlay = true;

        readonly List<byte[][]> undo = new List<byte[][]>();
        List<KeyItem> items;
        List<DoorPassage> doors;

        public ZoningState(GameSession s, Func<int, LevelModel> level)
        {
            Session = s;
            this.level = level;
            Path = System.IO.Path.Combine(s.DataFolder, ZoneSet.DefaultFileName(s.IsUw2));
            if (File.Exists(Path))
            {
                try { Set = ZoneSet.Load(Path, s.Profile.LevelSlots); }
                catch (Exception) { Set = null; }
            }
            if (Set == null) Set = New();
            current = Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).Select(z => z.Id).DefaultIfEmpty(2).First();
        }

        ZoneSet New()
        {
            var zs = new ZoneSet(Session.Profile.LevelSlots) { Game = Session.IsUw2 ? "UW2" : "UW1", Source = "made in Underworld Editor" };
            zs.EnsureZone(2).Name = "Start";
            zs.StartZone = 2;
            return zs;
        }

        public bool FileExists { get { return File.Exists(Path); } }

        /// <summary>Whether any tile is in a zone (a zoning to use), rather than a new, unpainted one.</summary>
        public bool HasZones { get { return Set.Tiles.Any(t => t != null && t.Any(b => b > ZoneSet.Unassigned)); } }

        /// <summary>The zone ids with tiles.</summary>
        public int ZoneCount { get { var seen = new HashSet<int>(); foreach (var t in Set.Tiles) if (t != null) foreach (var b in t) if (b > ZoneSet.Unassigned) seen.Add(b); return seen.Count; } }

        /// <summary>A copy of the zoning as it is now (unsaved changes included).</summary>
        public ZoneSet CopyOfSet() { return ZoneSet.FromJson(Set.ToJson(), Session.Profile.LevelSlots); }

        bool splitByLevel;
        /// <summary>
        /// The solver gets the zoning with each zone that lies on several levels split into one zone a
        /// level, joined by free two-way links (ZoneSet.SplitByLevel); the zoning itself is not changed.
        /// </summary>
        public bool SplitByLevel
        {
            get { return splitByLevel; }
            set { if (splitByLevel == value) return; splitByLevel = value; Raise("split", false); }
        }

        /// <summary>The zoning as the solver gets it (Apply, the logic check): a copy, split by level when that option is on.</summary>
        public ZoneSet ForSolver(out string note)
        {
            note = "";
            return splitByLevel ? Set.SplitByLevel(out note) : CopyOfSet();
        }

        public ZoneSet ForSolver() { string note; return ForSolver(out note); }

        /// <summary>What an automatic zoning says once it is changed by hand (no longer AutoZones.IsAutomatic: the solver keeps its zones after a change, as for a painted one).</summary>
        public const string HandEditedSource = "hand-edited automatic zones (Underworld Editor)";

        void Raise(string what, bool dirty = true)
        {
            if (dirty) Dirty = true;
            // painting or a link drawn on an automatic zoning: from now on it is the user's
            if (dirty && (what == "tiles" || what == "links") && AutoZones.IsAutomatic(Set)) Set.Source = HandEditedSource;
            if (what == "tiles" || what == "file") items = null;
            if (what == "tiles" || what == "file") doors = null;
            Changed?.Invoke(what);
        }

        // ---- the file

        public void Save(string path = null)
        {
            path = path ?? Path;
            if (File.Exists(path) && !File.Exists(path + ".BKP")) File.Copy(path, path + ".BKP");
            Set.Save(path);
            Path = path;
            Dirty = false;
            AskForName = false;
            Changed?.Invoke("saved");
        }

        public void Open(string path)
        {
            Set = ZoneSet.Load(path, Session.Profile.LevelSlots);
            Path = path;
            Dirty = false;
            undo.Clear();
            current = Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).Select(z => z.Id).DefaultIfEmpty(2).First();
            Raise("file", false);
        }

        /// <summary>The UW1 zoning shipped with the editor (Karl's maps and notes), as a starting point.</summary>
        public void LoadExample()
        {
            using (var st = typeof(ZoningState).Assembly.GetManifestResourceStream("uw1-zones.example.json"))
            using (var r = new StreamReader(st))
                Set = ZoneSet.FromJson(r.ReadToEnd(), Session.Profile.LevelSlots);
            Dirty = true;
            undo.Clear();
            current = Set.StartZone;
            Raise("file");
        }

        /// <summary>Saving asks for a file name first (a set made elsewhere, such as the automatic zones).</summary>
        public bool AskForName { get; private set; }

        /// <summary>Takes a zoning made elsewhere (the automatic zones) to view and edit; saving asks for a file name, suggesting path.</summary>
        public void Adopt(ZoneSet set, string path)
        {
            Set = set;
            Path = path;
            AskForName = true;
            Dirty = true;
            undo.Clear();
            current = Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).Select(z => z.Id).DefaultIfEmpty(2).First();
            Raise("file");
        }

        public void NewFile()
        {
            Set = New();
            Dirty = true;
            undo.Clear();
            current = 2;
            Raise("file");
        }

        // ---- zones

        public Zone AddZone(string name = null)
        {
            int id = Set.NextZoneId();
            if (id < 0) throw new InvalidOperationException("All 254 zone ids (2-255) are in use.");
            var z = Set.EnsureZone(id);
            z.Name = string.IsNullOrWhiteSpace(name) ? "Zone " + id : name.Trim();
            z.Status = "unfinished";
            current = id;
            Raise("zones");
            return z;
        }

        public void Rename(int id, string name)
        {
            var z = Set.EnsureZone(id);
            z.Name = (name ?? "").Trim();
            Raise("zones");
        }

        public void SetColor(int id, string hex)
        {
            var z = Set.EnsureZone(id);
            uint c;
            z.Color = string.IsNullOrWhiteSpace(hex) ? "" : ZoneSet.TryParseColor(hex, out c) ? ZoneSet.ColorString(c) : throw new FormatException("A colour is #RRGGBB.");
            Raise("tiles");
        }

        public void SetNotes(int id, string notes)
        {
            var z = Set.GetZone(id);
            if (z == null || z.Notes == notes) return;
            z.Notes = notes ?? "";
            Raise("notes");
        }

        public void RemoveZone(int id)
        {
            Snapshot();
            Set.Remove(id);
            if (current == id) current = Set.Zones.Where(z => z.Id > ZoneSet.Unassigned).Select(z => z.Id).DefaultIfEmpty(2).First();
            Raise("tiles");
            Changed?.Invoke("zones");
        }

        public void MoveLabel(int id, double x, double y)
        {
            var z = Set.EnsureZone(id);
            z.GraphX = x; z.GraphY = y;
            Dirty = true;
        }

        // ---- painting

        public void Snapshot()
        {
            undo.Add(Set.Tiles.Select(t => t == null ? null : (byte[])t.Clone()).ToArray());
            if (undo.Count > 50) undo.RemoveAt(0);
        }

        public bool CanUndo { get { return undo.Count > 0; } }

        public void Undo()
        {
            if (undo.Count == 0) return;
            var t = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            for (int i = 0; i < t.Length && i < Set.Tiles.Length; i++) Set.Tiles[i] = t[i];
            Raise("tiles");
        }

        /// <summary>The level has no zone map yet: one from its tiles (rock 0, open 1).</summary>
        public void EnsureLevel(int level1)
        {
            if (Set.Tiles[level1 - 1] != null) return;
            var m = level(level1);
            Set.InitLevel(level1, (x, y) => !m.Tiles[x, y].IsSolid);
        }

        /// <summary>Paints one tile with the current zone (start: a new stroke for undo). Rock stays rock.</summary>
        public bool PaintTile(int level1, int x, int y, bool start)
        {
            EnsureLevel(level1);
            if (start) Snapshot();
            if (Set.ZoneAt(level1, x, y) == ZoneSet.Solid || Set.ZoneAt(level1, x, y) == current) return false;
            Set.EnsureZone(current);
            Set.SetZone(level1, x, y, (byte)current);
            Raise("tiles");
            return true;
        }

        /// <summary>
        /// Fills with zone n the tiles reachable from (x, y) that share its zone (ZoneFlood: four
        /// directions, no rock, no door tiles, no climb above MaxClimb; any drop is fine).
        /// </summary>
        public int FillArea(int level1, int x, int y, int zone)
        {
            EnsureLevel(level1);
            Snapshot();
            Set.EnsureZone(zone);
            int n = Set.Fill(level1, x, y, (byte)zone, new ZoneFlood(level(level1), MaxClimb));
            if (n == 0) undo.RemoveAt(undo.Count - 1);
            else Raise("tiles");
            return n;
        }

        public int Unassigned(int level1)
        {
            int n;
            return Set.CountsOnLevel(level1).TryGetValue(ZoneSet.Unassigned, out n) ? n : 0;
        }

        // ---- what the levels hold

        public List<KeyItem> Items { get { return items ?? (items = KeyItems.Find(Session, level, Set)); } }
        public List<DoorPassage> Doors { get { return doors ?? (doors = ZoneDoors.Find(Session, level, Set)); } }

        /// <summary>The level objects changed (edits): the key items and doors are read again when next asked for.</summary>
        /// <summary>Counts the level edits (LevelsChanged), so what is worked out from the levels can be kept until they change.</summary>
        public int LevelsVersion { get; private set; }

        public void LevelsChanged() { LevelsVersion++; items = null; doors = null; Changed?.Invoke("items"); }

        public void Mark(int level1, int index, bool? important)
        {
            string k = level1 + ":" + index;
            if (important == null) Set.Importance.Remove(k); else Set.Importance[k] = important.Value;
            items = null;
            Raise("items");
        }

        // ---- links

        public ZoneLink Connect(int from, int to, bool twoWay)
        {
            if (from == to) throw new InvalidOperationException("A zone does not need a link to itself.");
            List<DoorPassage> used;
            string req = ZoneDoors.Suggest(Doors, from, to, out used);
            var d = used.FirstOrDefault();
            var k = new ZoneLink
            {
                Id = Set.NextLinkId(), From = from, To = to, TwoWay = twoWay, Requires = req ?? "free",
                Kind = d != null ? d.Kind : "walk", Confidence = req == null ? "todo" : "certain",
                Notes = d != null ? string.Join("; ", used.Select(u => string.Format("L{0} ({1}, {2}) {3}: {4}", u.Level, u.X, u.Y, u.Kind, u.Notes))) : "",
            };
            if (d != null) { k.Level = d.Level; k.X = d.X; k.Y = d.Y; }
            Set.Links.Add(k);
            Raise("links");
            return k;
        }

        public void LinkChanged() { Raise("links"); }

        /// <summary>The zones' places in the zone graph changed (dirty: by the user, so they are worth saving; not dirty: placed for the view only).</summary>
        public void PositionsChanged(bool dirty) { Raise("layout", dirty); }

        public void RemoveLink(ZoneLink k)
        {
            Set.Links.Remove(k);
            Raise("links");
        }

        /// <summary>The zone's name with its id, for lists.</summary>
        public string Label(int id)
        {
            var z = Set.GetZone(id);
            return z == null ? "zone " + id : id + " " + z.Name;
        }
    }
}
