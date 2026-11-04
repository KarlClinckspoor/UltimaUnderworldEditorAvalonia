using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UwEditor.Core.Randomizer
{
    /// <summary>The automatic zoning of a game: the region graph, the zones made from it, and how it compares with a painted zoning.</summary>
    public sealed class AutoZoneResult
    {
        public NavGraph Graph;
        public ZoneSet Set;
        /// <summary>Painted zones each auto zone overlaps, with tile counts.</summary>
        public Dictionary<int, Dictionary<int, int>> Overlap = new Dictionary<int, Dictionary<int, int>>();
        public SolveResult Solve;
        public List<string> Warnings = new List<string>();
        /// <summary>Region graph nodes left without a zone (only when the zone ids ran out).</summary>
        public int Unzoned;
        /// <summary>Empty dead ends folded into their only neighbour.</summary>
        public int Folded;
        /// <summary>A second solve that assumes hazard:lava (Phase B's lava boots), to show what blocks next.</summary>
        public SolveResult SolveWithLava;
        /// <summary>The zoning the zones were taken from (a painted one, or one made earlier), or null: the zones were made from the regions.</summary>
        public ZoneSet Given;
        /// <summary>With a given zoning: each of its zones (with tiles) -> the zone that took it (itself, or the one it was merged into).</summary>
        public Dictionary<int, int> GivenMap = new Dictionary<int, int>();
        /// <summary>With a given zoning: zones made for open areas it leaves out, and for doorways kept apart.</summary>
        public int FreshZones;
        public int GivenInto(int id) { int z; return GivenMap.TryGetValue(id, out z) ? z : 0; }
        /// <summary>The square the player starts on (NavOptions.StartOf), whose zone is the start zone.</summary>
        public StartPoint Start;
    }

    /// <summary>
    /// Phase A of the automatic randomizer zones: regions merged into zones where they reach one
    /// another freely (the strongly connected parts of the free edges, teleports between levels
    /// left out when cutting at level changes), door tiles given to a neighbouring zone, links
    /// from the edges between zones, placements from the key items, the start from the new-game
    /// square and the goal from a painted map's goal zone.
    /// </summary>
    public static class AutoZones
    {
        /// <summary>The default file name of an automatic zoning (never the painted uw1-zones.json).</summary>
        public static string DefaultFileName(bool uw2) { return uw2 ? "uw2-zones-auto.json" : "uw1-zones-auto.json"; }

        public static AutoZoneResult Build(GameSession s, Func<int, LevelModel> level, NavOptions options = null, ZoneSet painted = null)
        {
            var g = NavGraph.Build(s, level, options);
            return FromGraph(s, level, g, painted);
        }

        /// <param name="given">A zoning to use instead of making zones (see <see cref="AssignGiven"/>); painted is then this zoning too.</param>
        public static AutoZoneResult FromGraph(GameSession s, Func<int, LevelModel> level, NavGraph g, ZoneSet painted, ZoneSet given = null)
        {
            var res = new AutoZoneResult { Graph = g, Given = given };
            if (given != null) painted = given;
            var o = g.Options;
            int n = g.Regions.Count;
            // the free edges that merge
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            foreach (var e in g.Edges)
            {
                if (e.Requires != "free" || e.From == e.To) continue;
                if (e.Kind == "stairs" && o.CutAtLevelChanges) continue;
                adj[e.From].Add(e.To);
            }
            var comp = Scc(adj);
            // door nodes that stand alone go with a neighbour (the first by region id)
            var compSize = new Dictionary<int, int>();
            foreach (int c in comp) { int k; compSize.TryGetValue(c, out k); compSize[c] = k + 1; }
            var neighbours = new Dictionary<int, SortedSet<int>>();
            foreach (var e in g.Edges)
            {
                if (e.Kind == "stairs" || e.Kind == "teleport") continue;
                foreach (var pair in new[] { (e.From, e.To), (e.To, e.From) })
                {
                    if (!g.Regions[pair.Item1].IsDoor || g.Regions[pair.Item2].IsDoor) continue;
                    SortedSet<int> set;
                    if (!neighbours.TryGetValue(pair.Item1, out set)) neighbours[pair.Item1] = set = new SortedSet<int>();
                    set.Add(pair.Item2);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var r = g.Regions[i];
                if (!r.IsDoor || r.KeepApart || compSize[comp[i]] > 1) continue;
                SortedSet<int> set;
                if (neighbours.TryGetValue(i, out set) && set.Count > 0) comp[i] = comp[set.Min];
            }
            var zs = new ZoneSet(s.Profile.LevelSlots) { Game = s.IsUw2 ? "UW2" : "UW1", Source = given != null ? "the zoning given (Underworld Editor)" : "automatic zones (Underworld Editor), Phase A" };
            for (int l = 1; l <= zs.Levels; l++)
                if (g.RegionAt[l - 1] != null) { var m = level(l); zs.InitLevel(l, (x, y) => !m.Tiles[x, y].IsSolid); }
            if (given != null)
            {
                AssignGiven(g, comp, given, zs, res);
                // the objects marked as key items (or not) in the zoning count here too
                foreach (var kv in given.Importance) zs.Importance[kv.Key] = kv.Value;
            }
            else
            {
                if (o.FoldEmptyDeadEnds) res.Folded = FoldDeadEnds(s, level, g, comp);
                // zone ids: the biggest groups first, so that running out of ids only loses small pockets
                var groups = new Dictionary<int, List<int>>();
                for (int i = 0; i < n; i++) { List<int> l; if (!groups.TryGetValue(comp[i], out l)) groups[comp[i]] = l = new List<int>(); l.Add(i); }
                var ordered = groups.Values.OrderBy(l => g.Regions[l[0]].Level).ThenByDescending(l => l.Sum(r => g.Regions[r].Tiles.Count)).ThenBy(l => l.Min()).ToList();
                // keep each level's order, but hand out ids by size when there are too many
                var bySize = groups.Values.OrderByDescending(l => l.Sum(r => g.Regions[r].Tiles.Count)).ThenBy(l => l.Min()).ToList();
                var keep = new HashSet<List<int>>(bySize.Take(ZoneSet.MaxZone - 1));
                int next = 2;
                foreach (var grp in ordered)
                {
                    if (!keep.Contains(grp)) { res.Unzoned += grp.Count; continue; }
                    int id = next++;
                    foreach (int r in grp)
                    {
                        g.Regions[r].Zone = id;
                        foreach (int t in g.Regions[r].Tiles) zs.SetZone(g.Regions[r].Level, t % 64, t / 64, (byte)id);
                    }
                    zs.EnsureZone(id);
                }
            }
            if (res.Unzoned > 0) res.Warnings.Add(string.Format("More zones than ids (254): {0} small regions are left unassigned.", res.Unzoned));
            // links between zones
            var linkBy = new Dictionary<string, ZoneLink>();
            var linkCount = new Dictionary<ZoneLink, int>();
            Func<string, string> zoneTerms = req => Regex.Replace(req, @"region:(\d+)", mm => "zone:" + g.Regions[int.Parse(mm.Groups[1].Value)].Zone);
            foreach (var e in g.Edges)
            {
                int a = g.Regions[e.From].Zone, b = g.Regions[e.To].Zone;
                if (a == 0 || b == 0 || a == b) continue;
                string req = Normalize(Simplify(zoneTerms(e.Requires), a));
                string key = a + ">" + b + "|" + e.Kind;
                ZoneLink k;
                if (!linkBy.TryGetValue(key, out k))
                {
                    k = new ZoneLink { From = a, To = b, Kind = e.Kind, Requires = req, Level = e.Level, X = e.X, Y = e.Y, Notes = e.Notes, Confidence = e.Kind == "jump" || req.Contains("flag:var") ? "todo" : "certain" };
                    linkBy[key] = k;
                    linkCount[k] = 0;
                }
                else if (k.Requires != "free")
                {
                    if (req == "free") { k.Requires = "free"; k.Level = e.Level; k.X = e.X; k.Y = e.Y; k.Notes = e.Notes; }
                    else
                    {
                        // add the new requirement's alternatives that are not there yet (at most 6 in all)
                        var have = Alternatives(k.Requires);
                        var add = Alternatives(req).Where(a => !have.Contains(a)).ToList();
                        if (add.Count > 0 && have.Count + add.Count <= 6) k.Requires = Normalize(JoinOr(have.Concat(add)));
                    }
                }
                linkCount[k] += e.Count;
                if (e.Kind == "drop" && e.Fall > 0) k.Notes = MaxFallNote(k.Notes, e.Fall);
            }
            // a link both ways with the same kind and requirement is one two-way link
            var links = new List<ZoneLink>();
            var done = new HashSet<ZoneLink>();
            foreach (var k in linkBy.Values.OrderBy(x => x.From).ThenBy(x => x.To))
            {
                if (done.Contains(k)) continue;
                ZoneLink back;
                if (linkBy.TryGetValue(k.To + ">" + k.From + "|" + k.Kind, out back) && back.Requires == k.Requires && !done.Contains(back))
                {
                    k.TwoWay = true;
                    done.Add(back);
                    linkCount[k] += linkCount[back];
                }
                done.Add(k);
                if (linkCount[k] > 1 && k.Kind != "stairs" && k.Kind != "teleport") k.Notes = (k.Notes.Length > 0 ? k.Notes + "; " : "") + linkCount[k] + " passages";
                links.Add(k);
            }
            int lid = 1;
            foreach (var k in links) { k.Id = lid++; zs.Links.Add(k); }
            // a given zoning's own links (drawn by hand): on top of those the levels give, between the zones that took their ends
            if (given != null)
            {
                int hand = 0;
                Func<int, int, string, string, bool> levelsGive = (a, b, kind, req) => zs.Links.Any(l => !l.Hand && l.Kind == kind && l.Requires == req && ((l.From == a && l.To == b) || (l.TwoWay && l.From == b && l.To == a)));
                foreach (var k in given.Links)
                {
                    int a = res.GivenInto(k.From), b = res.GivenInto(k.To);
                    if (a <= ZoneSet.Unassigned || b <= ZoneSet.Unassigned || a == b) continue;
                    // one the levels give already (an automatic zoning's own links, or a hand link that matches a passage): not again
                    string req0 = string.IsNullOrWhiteSpace(k.Requires) ? "free" : Normalize(k.Requires);
                    if (levelsGive(a, b, k.Kind, req0) && (!k.TwoWay || levelsGive(b, a, k.Kind, req0))) continue;
                    zs.Links.Add(new ZoneLink { Id = lid++, From = a, To = b, Kind = k.Kind, Requires = string.IsNullOrWhiteSpace(k.Requires) ? "free" : k.Requires, TwoWay = k.TwoWay, Notes = "drawn by hand" + (string.IsNullOrEmpty(k.Notes) ? "" : ": " + k.Notes), Confidence = k.Confidence, Level = k.Level, X = k.X, Y = k.Y, Hand = true });
                    hand++;
                }
                if (hand > 0) res.Warnings.Add(string.Format("{0} link(s) drawn by hand added to those the levels give.", hand));
            }
            // placements
            var items = KeyItems.Find(s, level, zs);
            zs.Placements.AddRange(KeyItems.SuggestPlacements(zs, items));
            // start and goal
            var st = g.Options.StartOf(s);
            res.Start = st;
            if (st != null) zs.StartZone = Math.Max(2, (int)zs.NearestZone(st.Level, (int)st.X, (int)st.Y));
            zs.GoalZone = GoalFrom(zs, painted);
            if (painted != null && painted.GoalZone > ZoneSet.Unassigned && painted.TileCount(painted.GoalZone) == 0)
                res.Warnings.Add(string.Format("The painted goal zone {0} has no tiles, so the goal is the largest zone of the last level.", painted.GoalZone));
            if (painted != null) foreach (var kv in painted.OpenSources) zs.OpenSources[kv.Key] = kv.Value;
            zs.OpenSources["hazard:lava"] = "Phase A placeholder: walking into lava (later item:lava_boots | a fire resistance spell).";
            res.Set = zs;
            if (painted != null) res.Overlap = Overlaps(zs, painted);
            Name(res, s, painted, items);
            if (given != null)
            {
                // the given zones keep what they were called, their colours, notes and places in the graph
                foreach (var z in zs.Zones)
                {
                    var gz = given.GetZone(z.Id);
                    if (gz == null || z.Id <= ZoneSet.Unassigned) continue;
                    z.Name = gz.Name; z.Color = gz.Color; z.Notes = gz.Notes; z.GraphX = gz.GraphX; z.GraphY = gz.GraphY;
                    if (!string.IsNullOrEmpty(gz.Status)) z.Status = gz.Status;
                }
            }
            try { res.Solve = new ZoneSolver(zs, new SolverOptions()).Solve(zs.StartZone, zs.GoalZone); }
            catch (Exception ex) { res.Warnings.Add("The solver failed: " + ex.Message); }
            try
            {
                var lavaOpts = new SolverOptions();
                lavaOpts.StartTokens.Add("hazard:lava");
                res.SolveWithLava = new ZoneSolver(zs, lavaOpts).Solve(zs.StartZone, zs.GoalZone);
            }
            catch (Exception) { }
            return res;
        }

        /// <summary>Whether a zoning was made by the automatic zones (its zones are the regions' own groups), rather than painted.</summary>
        public static bool IsAutomatic(ZoneSet zs) { return zs != null && (zs.Source ?? "").StartsWith("automatic zones", StringComparison.OrdinalIgnoreCase); }

        /// <summary>The value most of the items have (values 0 and 1, rock and unassigned, left out; ties to the lower), or 0.</summary>
        internal static int Majority(IEnumerable<int> values)
        {
            var c = new Dictionary<int, int>();
            foreach (int v in values) { if (v <= ZoneSet.Unassigned) continue; int k; c.TryGetValue(v, out k); c[v] = k + 1; }
            return c.Count == 0 ? 0 : c.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        }

        /// <summary>
        /// The zones of a given zoning (painted, or made earlier) on the regions: each region takes
        /// the zone that holds most of its tiles. A region with none (a door square, which a fill
        /// leaves out) takes the zone most of its free-walking group has; an open area outside every
        /// zone gets a zone of its own. A doorway kept apart (a door closed behind the player) keeps
        /// a zone of its own, as the solver needs it apart. A region is the finest part there is
        /// (the player walks all of it and back), so two zones painted on one region become one.
        /// Warns about those merges and about zones whose parts have no free way between them,
        /// where the solver, taking a zone as reached all at once, is too kind.
        /// </summary>
        internal static void AssignGiven(NavGraph g, int[] comp, ZoneSet given, ZoneSet zs, AutoZoneResult res)
        {
            int n = g.Regions.Count;
            Func<NavRegion, IEnumerable<int>> givenOf = r => r.Tiles.Select(t => (int)given.ZoneAt(r.Level, t % 64, t / 64));
            var zone = new int[n];
            for (int i = 0; i < n; i++) if (!g.Regions[i].KeepApart) zone[i] = Majority(givenOf(g.Regions[i]));
            var members = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++) { List<int> l; if (!members.TryGetValue(comp[i], out l)) members[comp[i]] = l = new List<int>(); l.Add(i); }
            var groupZone = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (zone[i] > ZoneSet.Unassigned || g.Regions[i].KeepApart) continue;
                int z;
                if (!groupZone.TryGetValue(comp[i], out z)) groupZone[comp[i]] = z = Majority(members[comp[i]].SelectMany(j => givenOf(g.Regions[j])));
                zone[i] = z;
            }
            // ids for the zones made here: never one the zoning has
            var taken = new HashSet<int>(given.Zones.Select(z => z.Id));
            foreach (var t in given.Tiles) if (t != null) foreach (byte b in t) taken.Add(b);
            int nextId = 2;
            Func<int> fresh = () =>
            {
                while (nextId <= ZoneSet.MaxZone && taken.Contains(nextId)) nextId++;
                if (nextId > ZoneSet.MaxZone) return 0;
                taken.Add(nextId);
                res.FreshZones++;
                return nextId;
            };
            var normal = new HashSet<int>(zone.Where(z => z > ZoneSet.Unassigned));
            var doorway = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                if (!g.Regions[i].KeepApart) continue;
                int m = Majority(givenOf(g.Regions[i]));
                if (m > ZoneSet.Unassigned && !normal.Contains(m) && doorway.Add(m)) zone[i] = m;
                else zone[i] = fresh();
            }
            var freshOfGroup = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (zone[i] > ZoneSet.Unassigned) continue;
                int z;
                if (!freshOfGroup.TryGetValue(comp[i], out z)) freshOfGroup[comp[i]] = z = fresh();
                zone[i] = z;
            }
            var made = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                var r = g.Regions[i];
                r.Zone = zone[i];
                if (zone[i] <= ZoneSet.Unassigned) { res.Unzoned++; continue; }
                foreach (int t in r.Tiles) zs.SetZone(r.Level, t % 64, t / 64, (byte)zone[i]);
                zs.EnsureZone(zone[i]);
                if (!given.Zones.Any(z => z.Id == zone[i])) made.Add(zone[i]);
            }
            // each zone of the zoning -> the zone that took it
            var into = new Dictionary<int, Dictionary<int, int>>();
            for (int i = 0; i < n; i++)
                foreach (int v in givenOf(g.Regions[i]))
                {
                    if (v <= ZoneSet.Unassigned || zone[i] <= ZoneSet.Unassigned) continue;
                    Dictionary<int, int> c;
                    if (!into.TryGetValue(v, out c)) into[v] = c = new Dictionary<int, int>();
                    int k; c.TryGetValue(zone[i], out k); c[zone[i]] = k + 1;
                }
            var assigned = new HashSet<int>(zone.Where(z => z > ZoneSet.Unassigned));
            var merged = new List<string>();
            foreach (var kv in into.OrderBy(k => k.Key))
            {
                int to = assigned.Contains(kv.Key) ? kv.Key : kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First().Key;
                res.GivenMap[kv.Key] = to;
                if (to != kv.Key) merged.Add(string.Format("{0} into {1}", Label(given, kv.Key), Label(given, to)));
            }
            if (merged.Count > 0)
                res.Warnings.Add(string.Format("{0} zone(s) of the zoning share their walkable area with another (no wall, door or step between them), so they are one: {1}.", merged.Count, Cap(merged)));
            if (made.Count > 0)
                res.Warnings.Add(string.Format("{0} zone(s) made for open areas outside the zoning's zones and for doorways closed behind the player: {1}.", made.Count, Cap(made.OrderBy(x => x).Select(x => x.ToString()).ToList())));
            // zones whose parts have no free way between them, either way (door squares left out; an automatic zoning made them by its own rules)
            if (IsAutomatic(given)) return;
            var parent = Enumerable.Range(0, n).ToArray();
            Func<int, int> find = null;
            find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
            foreach (var e in g.Edges)
                if (e.Requires == "free" && e.From != e.To && zone[e.From] == zone[e.To] && zone[e.From] > ZoneSet.Unassigned) parent[find(e.From)] = find(e.To);
            var split = new List<string>();
            foreach (var grp in Enumerable.Range(0, n).Where(i => zone[i] > ZoneSet.Unassigned && !g.Regions[i].IsDoor && given.GetZone(zone[i]) != null).GroupBy(i => zone[i]).OrderBy(x => x.Key))
            {
                int parts = grp.Select(find).Distinct().Count();
                if (parts > 1) split.Add(string.Format("{0} ({1} parts)", Label(given, grp.Key), parts));
            }
            if (split.Count > 0)
                res.Warnings.Add(string.Format("{0} zone(s) of the zoning hold parts with no free way between them (a locked door, a climb or a jump inside the zone); the solver takes such a zone as reached all at once, so it may be too kind: {1}.", split.Count, Cap(split)));
        }

        static string Label(ZoneSet zs, int id) { var z = zs.GetZone(id); return z == null || string.IsNullOrEmpty(z.Name) ? id.ToString() : id + " " + z.Name; }

        static string Cap(List<string> items, int most = 15) { return string.Join("; ", items.Take(most)) + (items.Count > most ? string.Format("; and {0} more", items.Count - most) : ""); }

        /// <summary>
        /// Folds into its only neighbour every group of regions that touches just one other group
        /// and holds no objects (no items, creatures or levers): pits that are free to enter, and
        /// ledges that only a jump or levitation reaches, matter to nothing a later phase moves.
        /// Returns how many groups were folded.
        /// </summary>
        internal static int FoldDeadEnds(GameSession s, Func<int, LevelModel> level, NavGraph g, int[] comp, HashSet<int> alsoBusy = null)
        {
            int n = g.Regions.Count;
            var busy = new bool[n];
            if (alsoBusy != null) foreach (int r in alsoBusy) if (r >= 0 && r < n) busy[r] = true;
            var models = new Dictionary<int, LevelModel>();
            foreach (var r in g.Regions)
            {
                LevelModel m;
                if (!models.TryGetValue(r.Level, out m)) models[r.Level] = m = level(r.Level);
                if (r.IsDoor) { busy[r.Id] = true; continue; }
                foreach (int t in r.Tiles)
                    foreach (var o in m.TileObjects(t % 64, t / 64))
                        if (o.Item < 0x140 || (o.Item >= 0x170 && o.Item < 0x180)) { busy[r.Id] = true; break; }
            }
            foreach (var lv in g.Levers) { foreach (int r in lv.Reach.Concat(lv.PoleReach)) busy[r] = true; if (lv.Nearest >= 0) busy[lv.Nearest] = true; }
            var st = g.Options.StartOf(s);
            if (st != null) { int r0 = g.NearestRegion(st.Level, (int)st.X, (int)st.Y); if (r0 >= 0) busy[r0] = true; }
            int folded = 0;
            for (int pass = 0; pass < 4; pass++)
            {
                var nb = new Dictionary<int, HashSet<int>>();
                var groupBusy = new Dictionary<int, bool>();
                var size = new Dictionary<int, int>();
                for (int i = 0; i < n; i++)
                {
                    bool b; groupBusy.TryGetValue(comp[i], out b); groupBusy[comp[i]] = b || busy[i];
                    int c; size.TryGetValue(comp[i], out c); size[comp[i]] = c + g.Regions[i].Tiles.Count;
                }
                foreach (var e in g.Edges)
                {
                    int a = comp[e.From], b = comp[e.To];
                    if (a == b) continue;
                    HashSet<int> h;
                    if (!nb.TryGetValue(a, out h)) nb[a] = h = new HashSet<int>(); h.Add(b);
                    if (!nb.TryGetValue(b, out h)) nb[b] = h = new HashSet<int>(); h.Add(a);
                }
                var into = new Dictionary<int, int>();
                foreach (var kv in nb.OrderBy(k => size[k.Key]))
                {
                    if (kv.Value.Count != 1 || groupBusy[kv.Key]) continue;
                    int other = kv.Value.First();
                    if (into.ContainsKey(other)) continue;      // never fold two dead ends into each other in one pass
                    into[kv.Key] = other;
                }
                if (into.Count == 0) break;
                for (int i = 0; i < n; i++) { int t; if (into.TryGetValue(comp[i], out t)) comp[i] = t; }
                folded += into.Count;
            }
            return folded;
        }

        static string MaxFallNote(string notes, int fall)
        {
            var m = Regex.Match(notes ?? "", @"fall (\d+) steps");
            if (m.Success) return int.Parse(m.Groups[1].Value) >= fall ? notes : notes.Replace(m.Value, "fall " + fall + " steps");
            return (string.IsNullOrEmpty(notes) ? "" : notes + "; ") + "fall " + fall + " steps";
        }

        internal static List<string> Alternatives(string req)
        {
            var list = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i <= req.Length; i++)
            {
                if (i == req.Length || (req[i] == '|' && depth == 0)) { list.Add(req.Substring(start, i - start).Trim()); start = i + 1; }
                else if (req[i] == '(') depth++;
                else if (req[i] == ')') depth--;
            }
            return list.Select(x => x.StartsWith("(") && x.EndsWith(")") ? x.Substring(1, x.Length - 2).Trim() : x).ToList();
        }

        internal static string JoinOr(IEnumerable<string> alts)
        {
            var a = alts.Distinct().ToList();
            return string.Join(" | ", a.Select(x => x.Contains("&") ? "(" + x + ")" : x));
        }

        /// <summary>Drops the terms a link from zone "from" always meets (zone:from), and parses the result once to catch mistakes.</summary>
        internal static string Simplify(string req, int from)
        {
            var alts = Alternatives(req).Select(alt =>
            {
                var terms = ZoneLink.SplitAnd(alt).Where(t => t != "zone:" + from).ToList();
                return ZoneLink.JoinAnd(terms);
            }).ToList();
            if (alts.Contains("free")) return "free";
            return JoinOr(alts);
        }

        internal static string Normalize(string req)
        {
            string err;
            var r = Requirement.TryParse(req, out err);
            return r == null ? req : r.ToString();
        }

        static int GoalFrom(ZoneSet zs, ZoneSet painted)
        {
            if (painted != null && painted.GoalZone > ZoneSet.Unassigned)
            {
                var count = new Dictionary<int, int>();
                for (int l = 1; l <= Math.Min(zs.Levels, painted.Levels); l++)
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            if (painted.ZoneAt(l, x, y) != painted.GoalZone) continue;
                            int z = zs.ZoneAt(l, x, y);
                            if (z <= ZoneSet.Unassigned) continue;
                            int c; count.TryGetValue(z, out c); count[z] = c + 1;
                        }
                if (count.Count > 0) return count.OrderByDescending(kv => kv.Value).First().Key;
            }
            // no painted goal: the biggest zone of the last level present
            for (int l = zs.Levels; l >= 1; l--)
            {
                var c = zs.CountsOnLevel(l).Where(kv => kv.Key > ZoneSet.Unassigned).OrderByDescending(kv => kv.Value).ToList();
                if (c.Count > 0) return c[0].Key;
            }
            return 0;
        }

        /// <summary>For each auto zone, the painted zones it overlaps and on how many tiles.</summary>
        public static Dictionary<int, Dictionary<int, int>> Overlaps(ZoneSet auto, ZoneSet painted)
        {
            var d = new Dictionary<int, Dictionary<int, int>>();
            for (int l = 1; l <= Math.Min(auto.Levels, painted.Levels); l++)
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        int a = auto.ZoneAt(l, x, y), p = painted.ZoneAt(l, x, y);
                        if (a <= ZoneSet.Unassigned) continue;
                        Dictionary<int, int> m;
                        if (!d.TryGetValue(a, out m)) d[a] = m = new Dictionary<int, int>();
                        int c; m.TryGetValue(p, out c); m[p] = c + 1;
                    }
            return d;
        }

        static string LevelSpan(List<int> levels)
        {
            if (levels.Count == 0) return "L?";
            if (levels.Count == 1) return "L" + levels[0];
            bool run = levels.Last() - levels.First() == levels.Count - 1;
            return run ? "L" + levels.First() + "-L" + levels.Last() : "L" + string.Join(",", levels);
        }

        /// <summary>Names "L3 Sword blade area" from the painted zone most of it lies in, else from what it is.</summary>
        static void Name(AutoZoneResult res, GameSession s, ZoneSet painted, List<KeyItem> items)
        {
            var zs = res.Set;
            var g = res.Graph;
            var regionsOf = new Dictionary<int, List<NavRegion>>();
            foreach (var r in g.Regions) { if (r.Zone == 0) continue; List<NavRegion> l; if (!regionsOf.TryGetValue(r.Zone, out l)) regionsOf[r.Zone] = l = new List<NavRegion>(); l.Add(r); }
            var used = new Dictionary<string, int>();
            foreach (var z in zs.Zones)
            {
                if (z.Id <= ZoneSet.Unassigned) continue;
                List<NavRegion> rs;
                if (!regionsOf.TryGetValue(z.Id, out rs)) continue;
                var levels = rs.Select(r => r.Level).Distinct().OrderBy(x => x).ToList();
                int tiles = rs.Sum(r => r.Tiles.Count);
                int water = rs.Sum(r => r.WaterTiles), lava = rs.Sum(r => r.LavaTiles), nomagic = rs.Sum(r => r.NoMagicTiles);
                string desc = null;
                Dictionary<int, int> ov;
                if (painted != null && res.Overlap.TryGetValue(z.Id, out ov))
                {
                    var top = ov.Where(kv => kv.Key > ZoneSet.Unassigned).OrderByDescending(kv => kv.Value).FirstOrDefault();
                    if (top.Value * 2 >= tiles && painted.GetZone(top.Key) != null) desc = Strip(painted.GetZone(top.Key).Name);
                }
                if (z.Id == zs.StartZone) desc = desc == null ? "start" : "start, " + desc;
                if (desc == null)
                {
                    var r0 = rs.OrderByDescending(r => r.Tiles.Count).First();
                    string what = lava * 2 > tiles ? "lava" : water * 2 > tiles ? "water" : tiles <= 4 ? "ledge" : tiles <= 30 ? "room" : "area";
                    desc = string.Format("{0} at ({1}, {2})", what, r0.X, r0.Y);
                }
                string name = LevelSpan(levels) + " " + desc;
                int seen;
                if (used.TryGetValue(name, out seen)) { used[name] = seen + 1; name += " (" + (seen + 1) + ")"; }
                else used[name] = 1;
                z.Name = name;
                z.Levels = LevelSpan(levels);
                z.Status = "automatic";
                var notes = new List<string> { tiles + " tiles", rs.Count + " region" + (rs.Count == 1 ? "" : "s") };
                if (water > 0) notes.Add(water + " water");
                if (lava > 0) notes.Add(lava + " lava");
                if (nomagic > 0) notes.Add(nomagic + " no-magic");
                var held = items.Where(i => i.Zone == z.Id).Select(i => i.Token).Distinct().Take(8).ToList();
                if (held.Count > 0) notes.Add("holds " + string.Join(", ", held));
                z.Notes = string.Join("; ", notes) + ".";
            }
        }

        static string Strip(string painted)
        {
            // painted names that say the level already ("Spider maze (L7) and its L8 pockets") keep their words
            return painted.Trim();
        }

        /// <summary>Strongly connected parts (Tarjan, iterative): the component id of each node.</summary>
        internal static int[] Scc(List<int>[] adj)
        {
            int n = adj.Length;
            var index = new int[n];
            var low = new int[n];
            var comp = new int[n];
            var onStack = new bool[n];
            for (int i = 0; i < n; i++) { index[i] = -1; comp[i] = -1; }
            var stack = new Stack<int>();
            int counter = 0, ncomp = 0;
            var work = new Stack<(int node, int edge)>();
            for (int root = 0; root < n; root++)
            {
                if (index[root] >= 0) continue;
                work.Push((root, 0));
                while (work.Count > 0)
                {
                    var (v, ei) = work.Pop();
                    if (ei == 0)
                    {
                        index[v] = low[v] = counter++;
                        stack.Push(v); onStack[v] = true;
                    }
                    bool recursed = false;
                    for (int i = ei; i < adj[v].Count; i++)
                    {
                        int w = adj[v][i];
                        if (index[w] < 0)
                        {
                            work.Push((v, i + 1));
                            work.Push((w, 0));
                            recursed = true;
                            break;
                        }
                        if (onStack[w]) low[v] = Math.Min(low[v], index[w]);
                    }
                    if (recursed) continue;
                    if (low[v] == index[v])
                    {
                        while (true)
                        {
                            int w = stack.Pop(); onStack[w] = false; comp[w] = ncomp;
                            if (w == v) break;
                        }
                        ncomp++;
                    }
                    if (work.Count > 0)
                    {
                        var (u, _) = work.Peek();
                        low[u] = Math.Min(low[u], low[v]);
                    }
                }
            }
            return comp;
        }

        // ---------------------------------------------------------------- the report

        /// <summary>Counts, the comparison with the painted map, the solve, levers out of reach, drops and mechanisms, as text.</summary>
        public static string Report(AutoZoneResult res, ZoneSet painted, GameSession s = null)
        {
            var g = res.Graph;
            var zs = res.Set;
            var sb = new StringBuilder();
            sb.AppendLine("AUTOMATIC ZONES (Phase A)");
            sb.AppendLine();
            foreach (var w in res.Warnings) sb.AppendLine("Warning: " + w);
            var o = g.Options;
            sb.AppendLine(string.Format("Options: highest climb {0}, jump spell climb {1}, gap jumps {2} ({3} tiles, {4} with the jump spell{5}), cut at level changes {6}.",
                o.MaxClimb, o.JumpSpellClimb, o.GapJumps ? "on" : "off", o.GapTiles, o.JumpSpellGapTiles, o.DiagonalJumps ? ", diagonals too" : "", o.CutAtLevelChanges ? "on" : "off"));
            sb.AppendLine();
            sb.AppendLine("COUNTS");
            sb.AppendLine(string.Format("  Regions: {0} ({1} door tiles, {2} lava, {3} with water, {4} with no-magic tiles).", g.Regions.Count, g.Regions.Count(r => r.IsDoor), g.Regions.Count(r => r.Lava), g.Regions.Count(r => r.WaterTiles > 0), g.Regions.Count(r => r.NoMagicTiles > 0)));
            sb.AppendLine("  Region edges: " + g.Edges.Count + " (" + string.Join(", ", g.Edges.GroupBy(e => e.Kind).OrderByDescending(x => x.Count()).Select(x => x.Key + " " + x.Count())) + ").");
            for (int l = 1; l <= g.Levels; l++)
            {
                if (g.RegionAt[l - 1] == null) continue;
                sb.AppendLine(string.Format("    L{0}: {1} regions, {2} zones", l, g.Regions.Count(r => r.Level == l), zs.CountsOnLevel(l).Keys.Count(k => k > ZoneSet.Unassigned)));
            }
            sb.AppendLine(string.Format("  Zones: {0}. Links: {1} ({2}). Placements: {3}.", zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), zs.Links.Count,
                string.Join(", ", zs.Links.GroupBy(k => k.Kind).OrderByDescending(x => x.Count()).Select(x => x.Key + " " + x.Count())), zs.Placements.Count));
            sb.AppendLine(string.Format("  Mechanisms: {0} ({1}).", g.Mechanisms.Count, string.Join(", ", g.Mechanisms.GroupBy(m => m.Kind).OrderByDescending(x => x.Count()).Select(x => x.Key + " " + x.Count()))));
            sb.AppendLine(string.Format("  Levers (switches holding a trigger): {0}, out of reach {1}.", g.Levers.Count, g.Levers.Count(l => l.OutOfReach)));
            if (res.Folded > 0) sb.AppendLine(string.Format("  Empty dead ends folded into their only neighbour: {0} groups of regions.", res.Folded));
            sb.AppendLine(string.Format("  Start: {0}. Goal: {1}.", SolveResult.ZoneName(zs, zs.StartZone), SolveResult.ZoneName(zs, zs.GoalZone)));
            sb.AppendLine();

            if (res.Solve != null)
            {
                var sv = res.Solve;
                sb.AppendLine("SOLVE (default options: bash allowed, no lock picking, spells allowed)");
                sb.AppendLine(string.Format("  {0} {1} spheres, {2} of {3} zones reached.", sv.GoalReached ? "Goal reached." : "Goal NOT reached.", sv.Spheres.Count, sv.ZoneSphere.Count, zs.Zones.Count(z => z.Id > ZoneSet.Unassigned)));
                var missing = sv.Blocked.SelectMany(b => b.Missing).GroupBy(t => t).OrderByDescending(x => x.Count()).Take(15).ToList();
                if (missing.Count > 0) sb.AppendLine("  What blocks the links out of the reached part (token: links): " + string.Join(", ", missing.Select(x => x.Key + " " + x.Count())));
                if (sv.NoSource.Count > 0) sb.AppendLine("  Needed but granted nowhere: " + string.Join(", ", sv.NoSource.Take(30)) + (sv.NoSource.Count > 30 ? " …" : ""));
                sb.AppendLine("  Levels of the reached zones: " + string.Join(", ", sv.ZoneSphere.Keys.SelectMany(z => zs.LevelsOf(z)).Distinct().OrderBy(x => x).Select(x => "L" + x)));
                var lv = res.SolveWithLava;
                if (lv != null)
                {
                    sb.AppendLine(string.Format("  With hazard:lava assumed (Phase B's lava boots): {0} {1} spheres, {2} zones reached.", lv.GoalReached ? "goal reached," : "goal NOT reached,", lv.Spheres.Count, lv.ZoneSphere.Count));
                    var m2 = lv.Blocked.SelectMany(b => b.Missing).GroupBy(t => t).OrderByDescending(x => x.Count()).Take(15).ToList();
                    if (m2.Count > 0) sb.AppendLine("    then blocked by: " + string.Join(", ", m2.Select(x => x.Key + " " + x.Count())));
                    var never = lv.Unreached.Where(z => !zs.Links.Any(k => k.To == z || (k.TwoWay && k.From == z))).ToList();
                    if (never.Count > 0) sb.AppendLine("    zones no link leads into (only a mechanism, a conversation or a spell gets there): " + string.Join(", ", never.Select(z => SolveResult.ZoneName(zs, z))));
                }
                sb.AppendLine();
            }

            if (painted != null)
            {
                sb.AppendLine("COMPARISON WITH THE PAINTED MAP");
                Compare(res, painted, sb);
                sb.AppendLine();
            }

            sb.AppendLine("LEVERS OUT OF REACH (from the floor in front: the hand reaches 24 z units above the feet, 8 below; the pole 48 and 16, ui/INTERACT.C)");
            foreach (var lv in g.Levers.Where(l => l.OutOfReach))
                sb.AppendLine(string.Format("  L{0} #{1} {2} at ({3}, {4}), z {5}: {6} (nearest floor height {7}, {8} z above it). {9}", lv.Level, lv.Index, lv.Name, lv.X, lv.Y, lv.Z,
                    lv.PoleReach.Count > 0 ? "the pole reaches it" : "not even the pole", lv.Floor, lv.Above, lv.What));
            sb.AppendLine();

            sb.AppendLine("DROPS (fall in floor steps; damage estimated before the Acrobat check, see the findings)");
            var drops = g.Drops(6);
            foreach (var e in drops.Take(40))
                sb.AppendLine(string.Format("  L{0} ({1}, {2}): fall {3} steps, about {4} damage; {5} -> {6}", e.Level, e.X, e.Y, e.Fall, NavGraph.FallDamage(e.Fall), ZoneOf(res, e.From), ZoneOf(res, e.To)));
            if (drops.Count > 40) sb.AppendLine("  … " + (drops.Count - 40) + " more.");
            sb.AppendLine("  No drop kills a fresh character (30 + Strength / 5 hit points, game/SKILLS.C): the worst fall, 15 steps, does about " + NavGraph.FallDamage(15) + ".");
            sb.AppendLine();

            sb.AppendLine("MECHANISMS (world changes, listed but not applied yet)");
            foreach (var m in g.Mechanisms)
                sb.AppendLine(string.Format("  {0}: {1}{2}; set off from {3}", m.Flag, m.What, m.Conditions.Length > 0 ? " — only with " + m.Conditions : "", m.SetOff.Length == 0 ? "nowhere by the player" : Regex.Replace(m.SetOff, @"region:(\d+)", mm => ZoneOf(res, int.Parse(mm.Groups[1].Value)))));
            sb.AppendLine();
            if (g.Notes.Count > 0)
            {
                sb.AppendLine("NOTES");
                foreach (var t in g.Notes) sb.AppendLine("  " + t);
            }
            return sb.ToString();
        }

        static string ZoneOf(AutoZoneResult res, int region)
        {
            int z = region >= 0 && region < res.Graph.Regions.Count ? res.Graph.Regions[region].Zone : 0;
            return z == 0 ? "region " + region : SolveResult.ZoneName(res.Set, z);
        }

        /// <summary>The painted zone most of an auto zone lies in (0 none).</summary>
        public static int Dominant(AutoZoneResult res, int auto)
        {
            Dictionary<int, int> ov;
            if (!res.Overlap.TryGetValue(auto, out ov)) return 0;
            var top = ov.Where(kv => kv.Key > ZoneSet.Unassigned).OrderByDescending(kv => kv.Value).FirstOrDefault();
            return top.Key;
        }

        static void Compare(AutoZoneResult res, ZoneSet painted, StringBuilder sb)
        {
            var zs = res.Set;
            // painted zones split over several auto zones
            var parts = new Dictionary<int, List<KeyValuePair<int, int>>>();
            foreach (var kv in res.Overlap)
                foreach (var p in kv.Value)
                {
                    if (p.Key <= ZoneSet.Unassigned) continue;
                    List<KeyValuePair<int, int>> l;
                    if (!parts.TryGetValue(p.Key, out l)) parts[p.Key] = l = new List<KeyValuePair<int, int>>();
                    l.Add(new KeyValuePair<int, int>(kv.Key, p.Value));
                }
            int unpaintedTiles = res.Overlap.Values.Sum(m => m.Where(kv => kv.Key <= ZoneSet.Unassigned).Sum(kv => kv.Value));
            sb.AppendLine(string.Format("  Painted zones: {0}; auto zones: {1}. Auto-zoned tiles the painted map leaves unpainted or rock: {2}.", painted.Zones.Count(z => z.Id > ZoneSet.Unassigned), zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), unpaintedTiles));
            sb.AppendLine("  Painted zones split over several auto zones (parts of 3 tiles or more):");
            foreach (var kv in parts.OrderBy(k => k.Key))
            {
                var big = kv.Value.Where(p => p.Value >= 3).OrderByDescending(p => p.Value).ToList();
                if (big.Count < 2) continue;
                sb.AppendLine(string.Format("    {0}: {1}", SolveResult.ZoneName(painted, kv.Key), string.Join(", ", big.Take(8).Select(p => p.Value + " in " + SolveResult.ZoneName(zs, p.Key))) + (big.Count > 8 ? ", … " + (big.Count - 8) + " more" : "")));
            }
            sb.AppendLine("  Auto zones spanning several painted zones (3 tiles or more each):");
            foreach (var kv in res.Overlap.OrderBy(k => k.Key))
            {
                var big = kv.Value.Where(p => p.Key > ZoneSet.Unassigned && p.Value >= 3).OrderByDescending(p => p.Value).ToList();
                if (big.Count < 2) continue;
                sb.AppendLine(string.Format("    {0}: {1}", SolveResult.ZoneName(zs, kv.Key), string.Join(", ", big.Take(10).Select(p => p.Value + " of " + SolveResult.ZoneName(painted, p.Key))) + (big.Count > 10 ? ", … " + (big.Count - 10) + " more" : "")));
            }
            // links: by the painted zones at either end
            var paintedPairs = new HashSet<string>();
            foreach (var k in painted.Links) { paintedPairs.Add(k.From + ">" + k.To); if (k.TwoWay) paintedPairs.Add(k.To + ">" + k.From); }
            var autoPairs = new HashSet<string>();
            var autoOnly = new List<string>();
            foreach (var k in zs.Links)
            {
                int a = Dominant(res, k.From), b = Dominant(res, k.To);
                if (a == 0 || b == 0 || a == b) continue;
                autoPairs.Add(a + ">" + b);
                if (k.TwoWay) autoPairs.Add(b + ">" + a);
                if (!paintedPairs.Contains(a + ">" + b) && !(k.TwoWay && paintedPairs.Contains(b + ">" + a)))
                    autoOnly.Add(string.Format("{0} {1} {2} [{3}] {4}", SolveResult.ZoneName(painted, a), k.TwoWay ? "<->" : "->", SolveResult.ZoneName(painted, b), k.Kind, k.Requires));
            }
            // every auto zone touching a painted zone counts for the painted side
            var touching = new Dictionary<int, HashSet<int>>();
            foreach (var kv in res.Overlap) foreach (var p in kv.Value) { if (p.Value < 1) continue; HashSet<int> h; if (!touching.TryGetValue(p.Key, out h)) touching[p.Key] = h = new HashSet<int>(); h.Add(kv.Key); }
            var autoZonePairs = new HashSet<string>();
            foreach (var k in zs.Links) { autoZonePairs.Add(k.From + ">" + k.To); if (k.TwoWay) autoZonePairs.Add(k.To + ">" + k.From); }
            var paintedOnly = new List<string>();
            foreach (var k in painted.Links)
            {
                HashSet<int> fa, tb;
                bool found = false;
                if (touching.TryGetValue(k.From, out fa) && touching.TryGetValue(k.To, out tb))
                {
                    if (fa.Overlaps(tb)) found = true;      // the same auto zone holds both ends
                    else foreach (int x in fa) { foreach (int y in tb) if (autoZonePairs.Contains(x + ">" + y) || (k.TwoWay && autoZonePairs.Contains(y + ">" + x))) { found = true; break; } if (found) break; }
                }
                if (!found) paintedOnly.Add(string.Format("[{0}] {1} {2} {3} ({4}) {5}", k.Id, SolveResult.ZoneName(painted, k.From), k.TwoWay ? "<->" : "->", SolveResult.ZoneName(painted, k.To), k.Kind, k.Requires));
            }
            sb.AppendLine(string.Format("  Painted links with no auto link between the auto zones they touch ({0}):", paintedOnly.Count));
            foreach (var t in paintedOnly) sb.AppendLine("    " + t);
            sb.AppendLine(string.Format("  Auto links between painted zones the painted map does not link ({0}, by the painted zone most of each end lies in):", autoOnly.Count));
            foreach (var t in autoOnly.Distinct().Take(60)) sb.AppendLine("    " + t);
            if (autoOnly.Distinct().Count() > 60) sb.AppendLine("    …");
        }
    }
}
