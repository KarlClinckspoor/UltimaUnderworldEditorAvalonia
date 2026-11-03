using System;
using System.Collections.Generic;
using System.Linq;
using UwEditor.Core.Editing;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The movement assumptions of the automatic zones (Karl's heuristics; ceilings are ignored).</summary>
    public sealed class NavOptions
    {
        /// <summary>The highest climb between touching tile edges, in floor steps, both ways inside a region (ZoneFlood's rule).</summary>
        public int MaxClimb = ZoneFlood.DefaultMaxClimb;
        /// <summary>The highest climb with the jump spell (Leap halves gravity, MB_LEAP in motion/PHYSICS.C do_player_input, so a jump goes about twice as high).</summary>
        public int JumpSpellClimb = 2 * ZoneFlood.DefaultMaxClimb;
        /// <summary>Gap jumps over lower ground are made at all (kind "jump").</summary>
        public bool GapJumps = true;
        /// <summary>The widest gap, in open tiles, a running jump crosses with nothing (Karl: 2).</summary>
        public int GapTiles = 2;
        /// <summary>The widest gap with the jump spell (Karl: 3).</summary>
        public int JumpSpellGapTiles = 3;
        /// <summary>Gap jumps along the diagonals too (a diagonal tile counts as 1.41).</summary>
        public bool DiagonalJumps = false;
        /// <summary>Teleports between levels never merge zones (each level's zones stay apart).</summary>
        public bool CutAtLevelChanges = true;
        /// <summary>Groups of regions with one neighbour and no objects (empty pits and ledges) go into that neighbour's zone.</summary>
        public bool FoldEmptyDeadEnds = true;
    }

    /// <summary>A set of tiles of one level where the player walks anywhere and back, or a door tile, or a lava patch.</summary>
    public sealed class NavRegion
    {
        public int Id;
        public int Level;
        /// <summary>The tiles, as y * 64 + x.</summary>
        public readonly List<int> Tiles = new List<int>();
        /// <summary>A door tile: its own node; its requirement is on every edge that touches it.</summary>
        public bool IsDoor;
        public int DoorIndex = -1;
        public string DoorKind;
        public string DoorRequires = "free";
        public string DoorNotes = "";
        public bool Lava;
        public int WaterTiles, LavaTiles, NoMagicTiles;
        public int MinHeight = 99, MaxHeight = -1;
        /// <summary>The automatic zone it ended in (0 until zones are made).</summary>
        public int Zone;
        public int X { get { return Tiles.Count == 0 ? -1 : Tiles[0] % 64; } }
        public int Y { get { return Tiles.Count == 0 ? -1 : Tiles[0] / 64; } }
        public override string ToString()
        {
            return string.Format("R{0} L{1} ({2}, {3}) {4} tiles{5}{6}{7}", Id, Level, X, Y, Tiles.Count, IsDoor ? " door #" + DoorIndex : "", Lava ? " lava" : WaterTiles > 0 ? " water " + WaterTiles : "", NoMagicTiles > 0 ? " no-magic " + NoMagicTiles : "");
        }
    }

    /// <summary>A one-way way from one region to another, with what it needs (the requirement language; region:N stands for a region that must be reached).</summary>
    public sealed class NavEdge
    {
        public int From, To;
        /// <summary>walk, drop, climb, jump, door, portcullis, secret, teleport, stairs (a teleport to another level).</summary>
        public string Kind = "walk";
        public string Requires = "free";
        /// <summary>Where it starts (level, tile).</summary>
        public int Level, X, Y;
        /// <summary>A drop's fall, in floor steps (the largest difference at the edge); the highest of the passages joined.</summary>
        public int Fall;
        /// <summary>How many tile passages this edge stands for.</summary>
        public int Count = 1;
        /// <summary>The trigger of a teleport, or -1.</summary>
        public int Trigger = -1;
        public string Notes = "";
        public override string ToString() { return string.Format("R{0} -> R{1} [{2}] {3}{4}", From, To, Kind, Requires, Fall > 0 ? " fall " + Fall : ""); }
    }

    /// <summary>A switch (lever, button, pull chain) whose use trigger runs a chain, and whether it can be reached from the floor.</summary>
    public sealed class NavLever
    {
        public int Level, Index, Item, X, Y, Z;
        public string Name;
        /// <summary>Regions from which the hand reaches it.</summary>
        public readonly List<int> Reach = new List<int>();
        /// <summary>Regions from which only the pole reaches it.</summary>
        public readonly List<int> PoleReach = new List<int>();
        /// <summary>The nearest region (for a lever nothing reaches).</summary>
        public int Nearest = -1;
        /// <summary>The floor height (steps) it is reached from, and how far above that floor it is (object z units, 8 a step).</summary>
        public int Floor, Above;
        public string What = "";
        public bool OutOfReach { get { return Reach.Count == 0; } }
        /// <summary>The requirement for using it: one of its regions reached, with the pole or levitation when the hand does not reach.</summary>
        public string Requires;
        public override string ToString()
        {
            return string.Format("L{0} #{1} {2} ({3}, {4}) z {5}: {6}", Level, Index, Name, X, Y, Z, OutOfReach ? (PoleReach.Count > 0 ? "out of reach, the pole reaches it" : "out of reach, even with the pole") : "in reach");
        }
    }

    /// <summary>A trigger chain that changes the world (not yet applied to the regions).</summary>
    public sealed class NavMechanism
    {
        public int Level;
        public int Trigger, TriggerItem;
        public int Trap, TrapItem;
        /// <summary>terrain, delete, create, hack, variable, door, quest.</summary>
        public string Kind;
        /// <summary>Where the player sets it off, as a requirement (region:N terms), or "" when the player cannot set it off.</summary>
        public string SetOff = "";
        /// <summary>Conditions met on the way along the chain (an item held, a variable's value).</summary>
        public string Conditions = "";
        public string What = "";
        /// <summary>The flag a later phase grants when it runs: flag:terrain_L5_622.</summary>
        public string Flag;
        /// <summary>The square the trap acts on, and for a terrain change the rectangle's size.</summary>
        public int X, Y, W = 1, H = 1;
        /// <summary>The regions its squares lie in now.</summary>
        public readonly List<int> Regions = new List<int>();
        public override string ToString() { return string.Format("{0}  L{1} trigger #{2} -> {3} #{4}: {5}{6}", Flag, Level, Trigger, Kind, Trap, What, Conditions.Length > 0 ? " (if " + Conditions + ")" : ""); }
    }

    /// <summary>
    /// The walkable regions of the levels and the ways between them, read from the geometry, the
    /// doors and the trigger chains (Phase A of the automatic zones). Movement is heuristic, after
    /// Karl's rules: inside a region every step climbs at most MaxClimb floor steps both ways
    /// (ZoneFlood's rule, compared at the touching edges); a drop is free; a climb above that needs
    /// the jump spell, and above JumpSpellClimb levitation; a straight gap of lower ground of up to
    /// GapTiles tiles is jumped, JumpSpellGapTiles with the jump spell. Ceilings are ignored (UW1
    /// has no area cut off by a low one). Water joins regions (UW1 swimming is normal movement,
    /// motion/PHYSICS.C parse_player_terr) but is counted for waterwalk; lava regions stand apart
    /// and entering one needs hazard:lava.
    /// </summary>
    public sealed class NavGraph
    {
        public NavOptions Options { get; private set; }
        public int Levels { get; private set; }
        public readonly List<NavRegion> Regions = new List<NavRegion>();
        public readonly List<NavEdge> Edges = new List<NavEdge>();
        public readonly List<NavLever> Levers = new List<NavLever>();
        public readonly List<NavMechanism> Mechanisms = new List<NavMechanism>();
        /// <summary>Per level (0-based), the region id of each tile (y * 64 + x), -1 for rock or none.</summary>
        public int[][] RegionAt;
        /// <summary>Things worth saying that are not edges (triggers the player cannot set off, conditions met).</summary>
        public readonly List<string> Notes = new List<string>();

        static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

        public int RegionOf(int level1, int x, int y)
        {
            if (level1 < 1 || level1 > Levels || RegionAt[level1 - 1] == null || x < 0 || y < 0 || x > 63 || y > 63) return -1;
            return RegionAt[level1 - 1][y * 64 + x];
        }

        /// <summary>The region at (x, y), or the nearest non-door region within radius (Chebyshev rings, as the game's teleport searches a 9 by 9 box: event/WORLDEV.C do_teleport).</summary>
        public int NearestRegion(int level1, int x, int y, int radius = 4, bool allowDoor = false)
        {
            for (int r = 0; r <= radius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int k = RegionOf(level1, x + dx, y + dy);
                        if (k >= 0 && (allowDoor || !Regions[k].IsDoor)) return k;
                    }
            return -1;
        }

        public int EdgeCount(string kind) { return Edges.Count(e => e.Kind == kind); }

        // ---------------------------------------------------------------- building

        /// <summary>Builds the regions, edges, levers and mechanisms of every level present.</summary>
        public static NavGraph Build(GameSession s, Func<int, LevelModel> level, NavOptions options = null, IEnumerable<int> only = null)
        {
            var g = new NavGraph { Options = options ?? new NavOptions(), Levels = s.Profile.LevelSlots };
            g.RegionAt = new int[g.Levels][];
            TerrainTable terrain = null;
            try { terrain = TerrainTable.Load(s); } catch (Exception) { }
            var models = new Dictionary<int, LevelModel>();
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present || (only != null && !only.Contains(slot.Level))) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null || !m.Present) continue;
                models[slot.Level] = m;
            }
            var lv = new Dictionary<int, LevelNav>();
            foreach (var kv in models) lv[kv.Key] = g.BuildRegions(s, kv.Value, terrain);
            foreach (var kv in models) g.BuildEdges(lv[kv.Key]);
            foreach (var kv in models) g.ReadTriggers(s, lv[kv.Key], lv);
            g.ApplyDoorRequirements(lv);
            g.Merge();
            return g;
        }

        /// <summary>What a level's tiles are, for the walk rules.</summary>
        sealed class LevelNav
        {
            public LevelModel M;
            public int Level;
            public bool[] Door = new bool[64 * 64];
            public ObjectInfo[] DoorObj = new ObjectInfo[64 * 64];
            public bool[] Water = new bool[64 * 64], Lava = new bool[64 * 64];
            public int[] Class = new int[64 * 64];      // floor terrain class: 0 floor, 1 water, 2 lava
            /// <summary>Terms that open each door tile besides its lock (levers, triggers), by tile.</summary>
            public Dictionary<int, List<string>> DoorOpeners = new Dictionary<int, List<string>>();
            public bool Open(int x, int y) { return x >= 0 && y >= 0 && x < 64 && y < 64 && !M.Tiles[x, y].IsSolid; }
        }

        LevelNav BuildRegions(GameSession s, LevelModel m, TerrainTable terrain)
        {
            var n = new LevelNav { M = m, Level = m.Level };
            foreach (var o in m.Objects)
                if (!o.Free && o.OnTile && o.Item >= 0x140 && o.Item <= 0x14F && n.Open(o.TileX, o.TileY))
                {
                    int k = o.TileY * 64 + o.TileX;
                    if (!n.Door[k]) { n.Door[k] = true; n.DoorObj[k] = o; }
                }
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = m.Tiles[x, y];
                    if (t.IsSolid || terrain == null || t.FloorIndex < 0 || t.FloorIndex >= m.FloorMap.Length) continue;
                    // UW1: a floor's class is its TERRAIN.DAT word >> 4, 1 water and 2 lava (map/TEXTMAPS.C, motion/COLLIDE.C); UW2: bits 6-7
                    int w = terrain.Get(terrain.EntryOf(m.FloorMap[t.FloorIndex], true));
                    int c = s.IsUw2 ? (w >> 6) & 3 : (w >> 4) & 3;
                    n.Class[y * 64 + x] = c;
                    n.Water[y * 64 + x] = c == 1;
                    n.Lava[y * 64 + x] = c == 2;
                }
            // union of the two-way steps
            var parent = new int[64 * 64];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            Func<int, int> find = null;
            find = i => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int a = y * 64 + x;
                    if (!n.Open(x, y) || n.Door[a]) continue;
                    for (int d = 0; d < 2; d++)     // north and east cover every pair once
                    {
                        int nx = x + Dx[d], ny = y + Dy[d];
                        if (!n.Open(nx, ny)) continue;
                        int b = ny * 64 + nx;
                        if (n.Door[b] || n.Lava[a] != n.Lava[b]) continue;
                        int up, down;
                        if (!Rise(m, x, y, d, out up) || !Rise(m, nx, ny, (d + 2) & 3, out down)) continue;
                        if (up <= Options.MaxClimb && down <= Options.MaxClimb) parent[find(a)] = find(b);
                    }
                }
            var at = new int[64 * 64];
            for (int i = 0; i < at.Length; i++) at[i] = -1;
            var byRoot = new Dictionary<int, NavRegion>();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int k = y * 64 + x;
                    if (!n.Open(x, y)) continue;
                    NavRegion r;
                    if (n.Door[k])
                    {
                        var d = n.DoorObj[k];
                        string notes;
                        r = new NavRegion { Id = Regions.Count, Level = m.Level, IsDoor = true, DoorIndex = d.Index, DoorKind = (d.Item & 7) == 6 ? "portcullis" : (d.Item & 7) == 7 ? "secret" : "door" };
                        r.DoorRequires = ZoneDoors.Requirement(s, m, d, out notes);
                        r.DoorNotes = notes;
                        Regions.Add(r);
                    }
                    else if (!byRoot.TryGetValue(find(k), out r))
                    {
                        r = new NavRegion { Id = Regions.Count, Level = m.Level, Lava = n.Lava[k] };
                        Regions.Add(r);
                        byRoot[find(k)] = r;
                    }
                    r.Tiles.Add(k);
                    at[k] = r.Id;
                    var t = m.Tiles[x, y];
                    if (n.Water[k]) r.WaterTiles++;
                    if (n.Lava[k]) r.LavaTiles++;
                    // no magic: the tile's bit (combat/SPELLS.C anti_magic_p), and all of UW1's level 9 (do_spell refuses there)
                    if (t.NoMagic || (!s.IsUw2 && m.Level == 9)) r.NoMagicTiles++;
                    r.MinHeight = Math.Min(r.MinHeight, t.Height);
                    r.MaxHeight = Math.Max(r.MaxHeight, t.Height + (t.Type >= TileType.SlopeUpN ? 1 : 0));
                }
            RegionAt[m.Level - 1] = at;
            return n;
        }

        /// <summary>Whether (x, y) is open towards dir, and the climb from its edge into the neighbour (negative going down).</summary>
        static bool Rise(LevelModel m, int x, int y, int dir, out int rise)
        {
            rise = 0;
            int nx = x + Dx[dir], ny = y + Dy[dir];
            if (nx < 0 || ny < 0 || nx > 63 || ny > 63) return false;
            var a = m.Tiles[x, y];
            var b = m.Tiles[nx, ny];
            if (a.IsSolid || b.IsSolid || !ZoneFlood.IsSideOpen(a.Type, dir) || !ZoneFlood.IsSideOpen(b.Type, (dir + 2) & 3)) return false;
            int alo, ahi, blo, bhi;
            ZoneFlood.EdgeHeights(a, dir, out alo, out ahi);
            ZoneFlood.EdgeHeights(b, (dir + 2) & 3, out blo, out bhi);
            rise = blo - ahi;
            return true;
        }

        /// <summary>The geometric part of a step: free, a drop, the jump spell or levitation.</summary>
        string ClimbRequirement(int rise, out string kind)
        {
            if (rise <= Options.MaxClimb) { kind = rise < -Options.MaxClimb ? "drop" : "walk"; return "free"; }
            kind = "climb";
            return rise <= Options.JumpSpellClimb ? "spell:jump" : "spell:levitate";
        }

        void AddEdge(NavEdge e) { Edges.Add(e); }

        void BuildEdges(LevelNav n)
        {
            var m = n.M;
            var at = RegionAt[m.Level - 1];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int a = y * 64 + x;
                    if (at[a] < 0) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + Dx[d], ny = y + Dy[d];
                        if (!n.Open(nx, ny)) continue;
                        int b = ny * 64 + nx;
                        if (at[b] == at[a]) continue;
                        int rise, back;
                        if (!Rise(m, x, y, d, out rise) || !Rise(m, nx, ny, (d + 2) & 3, out back)) continue;
                        string kind;
                        var req = new List<string>();
                        string geo = ClimbRequirement(rise, out kind);
                        req.Add(geo);
                        var ra = Regions[at[a]];
                        var rb = Regions[at[b]];
                        if (rb.IsDoor || ra.IsDoor)
                        {
                            var door = rb.IsDoor ? rb : ra;
                            if (kind == "walk") kind = door.DoorKind;
                            req.Add("door:" + door.Id);     // replaced by the door's requirement once the levers are known
                        }
                        if (n.Lava[b] && !n.Lava[a]) req.Add("hazard:lava");
                        AddEdge(new NavEdge { From = at[a], To = at[b], Kind = kind, Requires = ZoneLink.JoinAnd(req), Level = m.Level, X = x, Y = y, Fall = kind == "drop" ? -rise : 0 });
                    }
                }
            if (Options.GapJumps) GapJumps(n);
        }

        void GapJumps(LevelNav n)
        {
            var m = n.M;
            var at = RegionAt[m.Level - 1];
            var free = new HashSet<long>();
            foreach (var e in Edges) if (e.Level == m.Level && e.Requires == "free") free.Add((long)e.From << 32 | (uint)e.To);
            var dirs = new List<int[]> { new[] { 0, 1 }, new[] { 1, 0 }, new[] { 0, -1 }, new[] { -1, 0 } };
            if (Options.DiagonalJumps) { dirs.Add(new[] { 1, 1 }); dirs.Add(new[] { 1, -1 }); dirs.Add(new[] { -1, 1 }); dirs.Add(new[] { -1, -1 }); }
            var best = new Dictionary<long, NavEdge>();
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int a = y * 64 + x;
                    if (at[a] < 0 || Regions[at[a]].IsDoor) continue;
                    var ta = m.Tiles[x, y];
                    foreach (var d in dirs)
                    {
                        bool diag = d[0] != 0 && d[1] != 0;
                        int card = d[1] > 0 ? 0 : d[0] > 0 ? 1 : d[1] < 0 ? 2 : 3;
                        int nearLo, nearHi;
                        if (diag) { nearLo = LevelGeometry.CornerHeights(ta).Min(); nearHi = LevelGeometry.CornerHeights(ta).Max(); if (ta.Type != TileType.Open && ta.Type < TileType.SlopeUpN) continue; }
                        else { if (!ZoneFlood.IsSideOpen(ta.Type, card)) continue; ZoneFlood.EdgeHeights(ta, card, out nearLo, out nearHi); }
                        int maxGap = Options.JumpSpellGapTiles;
                        for (int k = 1; k <= maxGap + 1; k++)
                        {
                            int tx = x + d[0] * k, ty = y + d[1] * k;
                            if (!n.Open(tx, ty)) break;
                            int tk = ty * 64 + tx;
                            if (n.Door[tk]) break;
                            var tt = m.Tiles[tx, ty];
                            if (diag ? tt.Type != TileType.Open && tt.Type < TileType.SlopeUpN : !ZoneFlood.IsSideOpen(tt.Type, (card + 2) & 3)) break;
                            int hi = LevelGeometry.CornerHeights(tt).Max();
                            if (hi < nearLo)
                            {
                                // lower ground: part of the gap, if the jump goes on over it
                                if (!diag && !ZoneFlood.IsSideOpen(tt.Type, card)) break;
                                continue;
                            }
                            int gaps = k - 1;
                            if (gaps == 0) break;
                            int farLo, farHi;
                            if (diag) farLo = LevelGeometry.CornerHeights(tt).Min();
                            else ZoneFlood.EdgeHeights(tt, (card + 2) & 3, out farLo, out farHi);
                            if (farLo > nearHi + Options.MaxClimb) break;
                            int from = at[a], to = at[tk];
                            if (to < 0 || to == from || free.Contains((long)from << 32 | (uint)to)) break;
                            double len = diag ? gaps * 1.4142 : gaps;
                            string req = len <= Options.GapTiles + 0.01 ? "free" : len <= Options.JumpSpellGapTiles + 0.01 ? "spell:jump" : null;
                            if (req == null) break;
                            if (n.Lava[tk] && !n.Lava[a]) req = req == "free" ? "hazard:lava" : req + " & hazard:lava";
                            long key = (long)from << 32 | (uint)to;
                            NavEdge old;
                            bool better = !best.TryGetValue(key, out old) || (old.Requires != "free" && req == "free");
                            if (better)
                                best[key] = new NavEdge { From = from, To = to, Kind = "jump", Requires = req, Level = m.Level, X = x, Y = y, Notes = string.Format("gap of {0} tile{1} to ({2}, {3}){4}", gaps, gaps == 1 ? "" : "s", tx, ty, diag ? ", diagonal" : "") };
                            else old.Count++;
                            break;
                        }
                    }
                }
            Edges.AddRange(best.Values);
        }

        // ---------------------------------------------------------------- triggers

        /// <summary>A trap met along a chain, the square the chain runs at there, and the conditions passed on the way.</summary>
        sealed class ChainStep
        {
            public ObjectInfo Trap;
            public int RunX, RunY;
            public List<string> Conditions;
        }

        /// <summary>
        /// The traps a trigger's chain runs, in order (event/TRIGGER.C UseTrap, SetOffTrap): a trigger
        /// met along the way runs its own chain at its own target square; create object, delete
        /// object, ward, tell and UW1's door trap end the chain; an inventory trap ends it when the
        /// item is missing (so later traps need the item), a check-variable trap branches.
        /// </summary>
        static List<ChainStep> Walk(GameSession s, LevelModel m, ObjectInfo trigger)
        {
            var list = new List<ChainStep>();
            var conds = new List<string>();
            int rx = trigger.Quality, ry = trigger.Owner;
            var seen = new HashSet<int> { trigger.Index };
            var next = Triggers.LinksTo(m, trigger);
            while (next != null && seen.Add(next.Index) && list.Count < 256)
            {
                if (Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Triggers.LinksTo(m, next); continue; }
                if (!Triggers.IsTrap(next.Item)) break;
                list.Add(new ChainStep { Trap = next, RunX = rx, RunY = ry, Conditions = new List<string>(conds) });
                int it = next.Item;
                if (it == 0x18C) conds.Add(ItemToken(s, next.Quality << 5 | next.Owner));
                else if (it == 0x18E && !s.IsUw2) conds.Add(string.Format("flag:var{0}_is_{1}", next.Z, (next.Quality * 32 | next.Owner) * 8 + next.FineY));
                if (it == 0x187 || it == 0x18B || it == 0x189 || (!s.IsUw2 && (it == 0x18A || it == 0x188))) break;
                next = Triggers.LinksTo(m, next);
            }
            return list;
        }

        static string Slug(string t) { return new string((t ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); }

        /// <summary>The token an item grants when held (the key items' own token when it has one).</summary>
        public static string ItemToken(GameSession s, int item)
        {
            string tok;
            if (!s.IsUw2 && KeyItems.Uw1Notable.TryGetValue(item, out tok)) return tok;
            return "item:" + Slug(s.Names.Get(item));
        }

        static bool PlayerSetsOff(ObjectInfo t) { return ((t.Record[1] >> 3) & 1) != 0; }      // id bit 11 (event/TRIGGER.C UseTrigger)

        /// <summary>A trigger placed by the level, not one reached along another chain.</summary>
        static bool IsRoot(LevelModel m, ObjectInfo o)
        {
            if (o.Free || o.TileX < 0 || !Triggers.IsTrigger(o.Item)) return false;
            return o.Parent < 0 || !m.Objects[o.Parent].IsTrapOrTrigger;
        }

        static readonly string[] TriggerVerb = { "walk onto", "pick up", "use", "look at", "step on", "open", "unlock" };

        void ReadTriggers(GameSession s, LevelNav n, Dictionary<int, LevelNav> all)
        {
            var m = n.M;
            int L = m.Level;
            // the levers first: their reach
            var leverOf = new Dictionary<int, NavLever>();
            foreach (var o in m.Objects)
            {
                if (o.Free || !o.OnTile || o.Item < 0x170 || o.Item > 0x17F) continue;
                bool holds = false;
                foreach (int c in o.Children) if (Triggers.IsTrigger(m.Objects[c].Item)) holds = true;
                if (!holds) continue;
                var lv = Lever(n, o);
                Levers.Add(lv);
                leverOf[o.Index] = lv;
            }
            foreach (var t in m.Objects)
            {
                if (!IsRoot(m, t)) continue;
                int ti = t.Item - 0x1A0;
                if (!s.IsUw2 && ti == 4) continue;      // UW1's step-on trigger never goes off (no code reports mode 1)
                var holder = t.Parent >= 0 ? m.Objects[t.Parent] : null;
                NavLever lever = null;
                if (holder != null) leverOf.TryGetValue(holder.Index, out lever);
                // where the player sets it off
                string setOff;
                var fromRegions = new List<int>();
                string extra = "";
                if (lever != null)
                {
                    setOff = lever.Requires;
                    fromRegions.AddRange(lever.Reach.Count > 0 ? lever.Reach : lever.PoleReach.Count > 0 ? lever.PoleReach : new List<int> { lever.Nearest });
                    if (lever.Reach.Count == 0) extra = lever.PoleReach.Count > 0 ? "item:pole | spell:levitate" : "spell:levitate";
                }
                else
                {
                    int r = NearestRegion(L, t.TileX, t.TileY, 2, true);
                    if (r >= 0) fromRegions.Add(r);
                    setOff = r >= 0 ? "region:" + r : "";
                }
                bool player = s.IsUw2 || PlayerSetsOff(t);
                if (!player) setOff = "";
                string how = (s.IsUw2 ? "trigger" : TriggerVerb[Math.Min(ti, TriggerVerb.Length - 1)]) + (holder != null ? " " + holder.Name : "");
                foreach (var st in Walk(s, m, t))
                {
                    var trap = st.Trap;
                    string cond = ZoneLink.JoinAnd(st.Conditions);
                    if (Triggers.IsTeleport(trap.Item))
                    {
                        if (!player) { Notes.Add(string.Format("L{0} trigger #{1} at ({2}, {3}) teleports, but the player cannot set it off (no flag 11).", L, t.Index, t.TileX, t.TileY)); continue; }
                        int to = trap.Z == 0 ? L : trap.Z;
                        int dest = NearestRegion(to, trap.Quality, trap.Owner);
                        if (dest < 0) { Notes.Add(string.Format("L{0} teleport #{1} (trigger #{2}) lands on L{3} ({4}, {5}): no open square there.", L, trap.Index, t.Index, to, trap.Quality, trap.Owner)); continue; }
                        foreach (int fr in fromRegions)
                        {
                            if (fr < 0 || (fr == dest)) continue;
                            var req = new List<string> { extra, cond };
                            AddEdge(new NavEdge
                            {
                                From = fr, To = dest, Kind = to != L ? "stairs" : "teleport", Requires = ZoneLink.JoinAnd(req), Level = L, X = t.TileX, Y = t.TileY, Trigger = t.Index,
                                Notes = string.Format("{0} #{1} ({2}, {3}) -> teleport #{4} to L{5} ({6}, {7})", Triggers.IsTrigger(t.Item) ? (s.IsUw2 ? "trigger" : Uw1Name(t.Item)) : "", t.Index, t.TileX, t.TileY, trap.Index, to, trap.Quality, trap.Owner),
                            });
                        }
                        continue;
                    }
                    if (trap.Item == 0x188 && (trap.Quality == 1 || trap.Quality == 3) && player)
                    {
                        // a door trap opens the door on the square its chain runs at (event/TRIGGER.C, UW1 TRAP_DOOR)
                        int k = st.RunY * 64 + st.RunX;
                        if (st.RunX >= 0 && st.RunX < 64 && st.RunY >= 0 && st.RunY < 64 && n.Door[k])
                        {
                            List<string> ops;
                            if (!n.DoorOpeners.TryGetValue(k, out ops)) n.DoorOpeners[k] = ops = new List<string>();
                            ops.Add(ZoneLink.JoinAnd(new[] { setOff, cond }));
                            if (lever != null) lever.What += (lever.What.Length > 0 ? "; " : "") + string.Format("opens the {0} at ({1}, {2})", Regions[RegionOf(L, st.RunX, st.RunY)].DoorKind, st.RunX, st.RunY);
                        }
                    }
                    var mech = Mechanism(s, m, t, st, how);
                    if (mech == null) continue;
                    mech.SetOff = setOff;
                    mech.Conditions = cond == "free" ? "" : cond;
                    if (lever != null) lever.What += (lever.What.Length > 0 ? "; " : "") + mech.Kind + ": " + mech.What;
                    if (mech.Kind == "door" && lever != null) continue;     // a lever's door is on the door's edges
                    var same = Mechanisms.Find(x => x.Level == L && x.Trigger == t.Index && x.Kind == mech.Kind);
                    if (same != null)
                    {
                        // one mechanism per trigger and kind: the effects joined
                        int colon = mech.What.IndexOf(": ");
                        same.What += "; " + (colon >= 0 ? mech.What.Substring(colon + 2) : mech.What);
                        foreach (int r in mech.Regions) if (!same.Regions.Contains(r)) same.Regions.Add(r);
                        continue;
                    }
                    Mechanisms.Add(mech);
                }
            }
        }

        static string Uw1Name(int item) { return UltimateEditor.Uw1.Uw1Traps.IsTrigger(item) ? UltimateEditor.Uw1.Uw1Traps.TriggerNames[item - 0x1A0].ToLowerInvariant() : "trap"; }

        NavMechanism Mechanism(GameSession s, LevelModel m, ObjectInfo t, ChainStep st, string how)
        {
            var trap = st.Trap;
            int L = m.Level;
            var me = new NavMechanism { Level = L, Trigger = t.Index, TriggerItem = t.Item, Trap = trap.Index, TrapItem = trap.Item, X = st.RunX, Y = st.RunY };
            string flag;
            switch (trap.Item)
            {
                case 0x185:
                    {
                        // change terrain (event/TRIGGER.C TRAP_CHANGE_TERRAIN, event/WORLDEV.C change_terrain): the rectangle x..x+fine x, y..y+fine y
                        me.Kind = "terrain";
                        me.W = trap.FineX + 1; me.H = trap.FineY + 1;
                        var parts = new List<string>();
                        int type = trap.Heading * 2 + (trap.Quality & 1);
                        if (type == 15) type = 10;
                        int height = trap.Z / 8, floor = trap.Quality / 2;
                        bool uw2 = s.IsUw2;
                        if (type < 10) parts.Add(type == 0 ? "rock" : type == 1 ? "open" : type < 6 ? "diagonal" : "slope");
                        if (height < (uw2 ? 15 : 14)) parts.Add("height " + height);
                        if (floor < (uw2 ? 15 : 11)) parts.Add("floor texture " + floor);
                        if (trap.Owner < (uw2 ? 63 : 48)) parts.Add("wall texture " + trap.Owner);
                        me.What = string.Format("({0}, {1}){2} becomes {3}", me.X, me.Y, me.W * me.H > 1 ? string.Format(" to ({0}, {1})", me.X + me.W - 1, me.Y + me.H - 1) : "", parts.Count == 0 ? "the same" : string.Join(", ", parts));
                        break;
                    }
                case 0x18B:
                    {
                        me.Kind = "delete";
                        me.X = trap.Quality; me.Y = trap.Owner;
                        var o = trap.Link > 0 && trap.Link < m.Objects.Length ? m.Objects[trap.Link] : null;
                        me.What = string.Format("deletes #{0} {1} from ({2}, {3})", trap.Link, o == null || o.Free ? "?" : o.Name, me.X, me.Y);
                        if (o != null && !o.Free && o.Item >= 0x140 && o.Item <= 0x14F) me.What += " (a door)";
                        break;
                    }
                case 0x187:
                    {
                        if (Triggers.IsSpawner(m, trap)) return null;
                        me.Kind = "create";
                        var o = trap.Link > 0 && trap.Link < m.Objects.Length ? m.Objects[trap.Link] : null;
                        me.What = string.Format("creates a copy of #{0} {1} at ({2}, {3}){4}", trap.Link, o == null || o.Free ? "?" : o.Name, me.X, me.Y, trap.Quality > 0 ? string.Format(", {0:0} %", Triggers.SpawnChance(trap.Quality)) : "");
                        break;
                    }
                case 0x183:
                    me.Kind = "hack";
                    me.What = s.IsUw2 ? "hack " + trap.Quality : UltimateEditor.Uw1.Uw1Traps.HackTrapName(trap.Quality);
                    if (!s.IsUw2 && UltimateEditor.Uw1.Uw1Traps.HackTrapName(trap.Quality) == "No effect in UW1") return null;
                    break;
                case 0x18D:
                    me.Kind = "variable";
                    me.What = Triggers.Short(s, trap) + (s.IsUw2 ? "" : string.Format(" (operation {0}, value {1})", trap.Heading, (trap.Quality * 32 | trap.Owner) * 8 + trap.FineY));
                    if (!s.IsUw2 && trap.Z == 0) me.Kind = "quest";
                    break;
                case 0x188:
                    me.Kind = "door";
                    me.What = Triggers.Short(s, trap) + string.Format(" at ({0}, {1})", me.X, me.Y);
                    break;
                default:
                    return null;
            }
            flag = string.Format("flag:{0}_L{1}_{2}", me.Kind, L, t.Index);
            me.Flag = flag;
            me.What = Uw1HowText(how) + ": " + me.What;
            if (me.Kind == "terrain")
            {
                for (int y = me.Y; y < me.Y + me.H; y++)
                    for (int x = me.X; x < me.X + me.W; x++)
                    {
                        int r = RegionOf(L, x, y);
                        if (r >= 0 && !me.Regions.Contains(r)) me.Regions.Add(r);
                    }
            }
            return me;
        }

        static string Uw1HowText(string how) { return how; }

        // the player's reach (ui/INTERACT.C InPickRange, BlockingTerrain; game/PLAYDATA.C init_spells):
        // squared distance in 1/8 tiles at most 0x90 (0x190 with the pole), the object at most 24 z
        // units above the feet and 8 below (48 and 16 with the pole), and for the hand no tile on the
        // line lower than the feet less a step, nor of another terrain class.
        const int HandDist = 0x90, PoleDist = 0x190;

        NavLever Lever(LevelNav n, ObjectInfo o)
        {
            var m = n.M;
            int L = m.Level;
            var lv = new NavLever { Level = L, Index = o.Index, Item = o.Item, X = o.TileX, Y = o.TileY, Z = o.Z, Name = o.Name };
            int ox = o.TileX * 8 + o.FineX, oy = o.TileY * 8 + o.FineY, oz = o.Z;
            int bestGap = int.MaxValue;
            double nearest = double.MaxValue;
            for (int ty = o.TileY - 3; ty <= o.TileY + 3; ty++)
                for (int tx = o.TileX - 3; tx <= o.TileX + 3; tx++)
                {
                    int r = RegionOf(L, tx, ty);
                    if (r < 0 || Regions[r].IsDoor) continue;
                    // the nearest point of the tile to the lever
                    int px = Math.Max(tx * 8, Math.Min(tx * 8 + 7, ox)), py = Math.Max(ty * 8, Math.Min(ty * 8 + 7, oy));
                    int d2 = (px - ox) * (px - ox) + (py - oy) * (py - oy);
                    if (d2 < nearest) { nearest = d2; lv.Nearest = r; }
                    var h = LevelGeometry.CornerHeights(m.Tiles[tx, ty]);
                    int fmin = h.Min() * 8, fmax = h.Max() * 8;
                    // the best foot height in the tile's range: the lever from 8 below to 24 above
                    int pz = Math.Max(fmin, Math.Min(fmax, oz - 8));
                    int gap = oz - pz;
                    if (d2 <= HandDist && oz <= pz + 24 && oz >= pz - 8 && !HandBlocked(n, tx, ty, o.TileX, o.TileY, pz))
                    {
                        if (!lv.Reach.Contains(r)) lv.Reach.Add(r);
                    }
                    else if (d2 <= PoleDist && oz <= pz + 48 && oz >= pz - 16)
                    {
                        if (!lv.PoleReach.Contains(r)) lv.PoleReach.Add(r);
                    }
                    if (d2 <= PoleDist && Math.Abs(gap) < Math.Abs(bestGap)) { bestGap = gap; lv.Floor = pz / 8; lv.Above = gap; }
                }
            lv.PoleReach.RemoveAll(r => lv.Reach.Contains(r));
            if (lv.Reach.Count > 0) lv.Requires = Or(lv.Reach.Select(r => "region:" + r));
            else if (lv.PoleReach.Count > 0) lv.Requires = ZoneLink.JoinAnd(new[] { Or(lv.PoleReach.Select(r => "region:" + r)), "item:pole | spell:levitate" });
            else lv.Requires = lv.Nearest >= 0 ? "region:" + lv.Nearest + " & spell:levitate" : "flag:unreachable_lever_L" + L + "_" + o.Index;
            return lv;
        }

        static string Or(IEnumerable<string> terms)
        {
            var t = terms.Distinct().ToList();
            return t.Count == 0 ? "free" : string.Join(" | ", t);
        }

        bool HandBlocked(LevelNav n, int px, int py, int lx, int ly, int footZ)
        {
            if (px == lx && py == ly) return false;
            var m = n.M;
            int pz = Math.Max(0, footZ - 8);
            int terr = n.Class[py * 64 + px];
            int xdir = Math.Sign(lx - px), ydir = Math.Sign(ly - py);
            int x = px, y = py;
            for (int guard = 0; guard < 16; guard++)
            {
                x += xdir; if ((xdir < 0 && x < lx) || (xdir > 0 && x > lx)) x = lx;
                y += ydir; if ((ydir < 0 && y < ly) || (ydir > 0 && y > ly)) y = ly;
                var t = m.Tiles[x, y];
                if ((pz >> 3) > t.Height && !HasBridge(m, x, y)) return true;
                if (x == lx && y == ly) break;
                int c = n.Class[y * 64 + x];
                if (c != 0 && c != terr && !HasBridge(m, x, y)) return true;
            }
            return false;
        }

        static bool HasBridge(LevelModel m, int x, int y)
        {
            foreach (var o in m.TileObjects(x, y)) if (o.Item == 0x164) return true;
            return false;
        }

        // ---------------------------------------------------------------- doors and tidying

        void ApplyDoorRequirements(Dictionary<int, LevelNav> lv)
        {
            var reqOf = new Dictionary<int, string>();
            foreach (var r in Regions)
            {
                if (!r.IsDoor) continue;
                string req = r.DoorRequires;
                LevelNav n;
                List<string> ops;
                if (lv.TryGetValue(r.Level, out n) && n.DoorOpeners.TryGetValue(r.Tiles[0], out ops))
                {
                    var alts = ops.Distinct().Select(x => x.Contains("|") && ops.Count > 1 ? "(" + x + ")" : x).ToList();
                    string by = string.Join(" | ", alts);
                    if (req.StartsWith("flag:door_")) req = by;
                    else if (req != "free") req = req + " | " + (alts.Count == 1 && by.Contains("&") ? "(" + by + ")" : by);
                    r.DoorNotes += (r.DoorNotes.Length > 0 ? "; " : "") + "opened by a trap chain (" + by + ")";
                }
                r.DoorRequires = req;
                reqOf[r.Id] = req;
            }
            foreach (var e in Edges)
            {
                if (!e.Requires.Contains("door:")) continue;
                var terms = ZoneLink.SplitAnd(e.Requires).Select(t => t.StartsWith("door:") ? reqOf[int.Parse(t.Substring(5))] : t);
                e.Requires = ZoneLink.JoinAnd(terms);
            }
        }

        /// <summary>One edge per (from, to, kind, requirement): the passages counted, the highest fall kept.</summary>
        void Merge()
        {
            var by = new Dictionary<string, NavEdge>();
            var list = new List<NavEdge>();
            foreach (var e in Edges)
            {
                string k = e.From + ">" + e.To + "|" + e.Kind + "|" + e.Requires;
                NavEdge old;
                if (by.TryGetValue(k, out old)) { old.Count += e.Count; old.Fall = Math.Max(old.Fall, e.Fall); continue; }
                by[k] = e;
                list.Add(e);
            }
            Edges.Clear();
            Edges.AddRange(list);
        }

        /// <summary>The worst drops, in floor steps, with what they do to a fresh character (see <see cref="FallDamage"/>).</summary>
        public List<NavEdge> Drops(int minFall)
        {
            return Edges.Where(e => e.Kind == "drop" && e.Fall >= minFall).OrderByDescending(e => e.Fall).ToList();
        }

        /// <summary>
        /// The damage of landing after a fall of so many floor steps, before the Acrobat check
        /// (motion/PHYSICS.C phys_affect_player: impact &gt;&gt; 8, doubled when the landing
        /// bounces, dealt only above 3; motion/MOTION.C do_zbounce: impact = |v / 15| × (15 −
        /// bounce), bounce 5 for the player). The landing speed grows with the square root of the
        /// height, scaled so that a fall of jumpPeak steps lands at the jump's take-off speed 0x263
        /// (PIN_JUMP). Without a measured jump peak this is an estimate.
        /// </summary>
        public static int FallDamage(int steps, double jumpPeak = 2.0)
        {
            if (steps <= 0) return 0;
            double v = 0x263 * Math.Sqrt(steps / jumpPeak);
            int vz = (int)(v / 15);
            int impact = vz * (15 - 5);
            int dmg = impact >> 8;
            if (vz * 5 >= 0x8D) dmg <<= 1;      // it bounces up again: vel[2] stays non-zero, doubling the damage
            return dmg > 3 ? dmg : 0;
        }
    }
}
