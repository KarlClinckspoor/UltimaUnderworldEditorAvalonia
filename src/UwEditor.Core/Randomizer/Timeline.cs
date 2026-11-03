using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UwEditor.Core.Randomizer
{
    /// <summary>A world change (a mechanism of the levels), what it does to the ways, and how the timeline treats it.</summary>
    public sealed class WorldChange
    {
        public NavMechanism Mechanism;
        public string Flag { get { return Requirement.Normalize(Mechanism.Flag); } }
        public int Level { get { return Mechanism.Level; } }
        /// <summary>What the change does to the ways of its level, found by applying it alone: joins, new ways, ways gone, zones split, squares turned to rock.</summary>
        public readonly List<string> Opens = new List<string>(), Closes = new List<string>();
        /// <summary>opens, closes, opens and closes, no way changed, not applied (variables, hack traps, creatures).</summary>
        public string Effect = "";
        /// <summary>How the timeline uses it.</summary>
        public string Treatment = "";
        /// <summary>Applied to its level when it goes off (else only its flag is granted).</summary>
        public bool Applied;
        public override string ToString() { return Mechanism.Flag + ": " + Effect; }
    }

    /// <summary>One step of the timeline: the zones reached, the tokens gained, the changes set off and applied.</summary>
    public sealed class TimelineSphere
    {
        public int Index;
        public readonly List<int> Zones = new List<int>();
        public readonly List<string> Tokens = new List<string>();
        public readonly List<string> TokenSources = new List<string>();
        /// <summary>Mechanism flags gained (set off), and those applied to their level.</summary>
        public readonly List<string> SetOff = new List<string>(), Applied = new List<string>();
        /// <summary>The state of each changed level after this sphere (level -> state key).</summary>
        public Dictionary<int, string> State = new Dictionary<int, string>();
    }

    /// <summary>A zone the player can enter but not leave (back to the start), with what it holds.</summary>
    public sealed class OneWayTrap
    {
        public int Zone, Sphere;
        /// <summary>The zones reachable from it without getting back (the trap's inside).</summary>
        public readonly List<int> Inside = new List<int>();
        /// <summary>Tokens gained only inside it.</summary>
        public readonly List<string> Holds = new List<string>();
        /// <summary>Tokens gained only inside it that a later step uses: they strand the player.</summary>
        public readonly List<string> Strands = new List<string>();
        /// <summary>The link that led in (one way).</summary>
        public ZoneLink Entry;
        public override string ToString() { return "zone " + Zone + " (sphere " + Sphere + ")"; }
    }

    /// <summary>A timeline solve: spheres, when each zone and token came, the changes applied, the goal, the return check.</summary>
    public sealed class TimelineSolve
    {
        public string Label = "";
        public readonly List<TimelineSphere> Spheres = new List<TimelineSphere>();
        public readonly Dictionary<int, int> ZoneSphere = new Dictionary<int, int>();
        public readonly Dictionary<string, int> TokenSphere = new Dictionary<string, int>();
        public readonly Dictionary<int, ZoneLink> FirstLink = new Dictionary<int, ZoneLink>();
        public readonly List<string> Fired = new List<string>(), Applied = new List<string>();
        public bool GoalZoneReached, GoalReached;
        public readonly List<string> GoalMissing = new List<string>();
        public readonly List<BlockedLink> Blocked = new List<BlockedLink>();
        public readonly List<OneWayTrap> Traps = new List<OneWayTrap>();
        /// <summary>Zones the return check found safe thanks to the planted silver sapling.</summary>
        public int SavedBySapling;
        /// <summary>The solve again with nothing taken from inside a trap (or -1 when not run).</summary>
        public int SafeZones = -1;
        public bool SafeGoal;
        public readonly List<string> Warnings = new List<string>();
        /// <summary>Names of the zones made by changes (parts split off), by id.</summary>
        public readonly Dictionary<int, string> NewZones = new Dictionary<int, string>();
    }

    /// <summary>Phase C of the automatic zones: the zones of Phase B, the world changes and the facts' links, warps and goal, and the timeline solves.</summary>
    public sealed class TimelineResult
    {
        public AbilityZoneResult Abilities;
        public ZoneSet Set { get { return Abilities.Set; } }
        public FactSet Facts { get { return Abilities.Facts; } }
        public AbilityOptions Options { get { return Abilities.Options; } }
        public readonly List<WorldChange> Changes = new List<WorldChange>();
        public List<NavCloser> Closers { get { return Abilities.Auto.Graph.Closers; } }
        /// <summary>Links made by the facts (kind link), as they stand with the options.</summary>
        public readonly List<ZoneLink> FactLinks = new List<ZoneLink>();
        public readonly List<Fact> Warps = new List<Fact>();
        public int GoalZone;
        public string GoalRequires = "free";
        public Fact GoalFact;
        /// <summary>Goal facts with an option (Phase D: goal_orb, option:require_orb): their requirement joins the goal's when the option is on.</summary>
        public readonly List<Fact> GoalExtras = new List<Fact>();

        /// <summary>What the goal needs with these solver options: the goal fact's requirement and those of the goal facts whose option is on.</summary>
        public string GoalFor(SolverOptions so)
        {
            var terms = new List<string> { GoalRequires };
            foreach (var f in GoalExtras) if (so.OptionsOn.Contains(f.Option)) terms.Add(f.Requires);
            return ZoneLink.JoinAnd(terms);
        }
        public TimelineSolve Solve;
        public readonly List<KeyValuePair<string, TimelineSolve>> Variants = new List<KeyValuePair<string, TimelineSolve>>();
        public readonly List<string> Warnings = new List<string>();
        internal Timeline.Engine Engine;
    }

    /// <summary>
    /// Phase C (C1) of the automatic zones: a solver that follows the world as it changes.
    /// When the player can set a mechanism off (its trigger's zone reached, its conditions met:
    /// an item held, a variable's value, a Search check), its flag is granted and, unless it only
    /// takes ways away, its change is applied to that level (terrain rewritten, objects deleted,
    /// doors opened); that level alone is then rebuilt and the search goes on. Doors that a trigger
    /// the player cannot avoid closes behind him are one way (NavGraph.Closers). At the end a
    /// return check looks for zones the player can enter but not leave.
    /// </summary>
    public static class Timeline
    {
        /// <summary>Builds Phase B's zones with the closers made one way, adds the facts' links, warps and goal, classifies the changes and solves.</summary>
        public static TimelineResult Build(GameSession s, Func<int, LevelModel> level, NavOptions nav = null, AbilityOptions opt = null, ZoneSet painted = null, FactSet facts = null, bool variants = true)
        {
            nav = (nav ?? new NavOptions()).Clone();
            nav.ClosersOneWay = true;
            opt = opt ?? new AbilityOptions();
            var b = AbilityZones.Build(s, level, nav, opt, painted, facts);
            b.Set.Source = "automatic zones (Underworld Editor), Phase C";
            var res = new TimelineResult { Abilities = b };
            res.Engine = new Engine(s, level, b, res);
            res.Engine.Prepare();
            Classify(res);
            res.Solve = Solve(res, opt, "with these options");
            if (variants) RunVariants(res);
            return res;
        }

        /// <summary>The option tokens of the movement options (option:cut_levels ...).</summary>
        static void NavTokens(NavOptions n, HashSet<string> into)
        {
            if (n.CutAtLevelChanges) into.Add("cut_levels");
            if (n.GapJumps) into.Add("gap_jumps");
            if (n.DiagonalJumps) into.Add("diagonal_jumps");
        }

        /// <summary>The solver's options for these logic options and these zones (the movement options as tokens too), with tokens held from the start.</summary>
        public static SolverOptions SolverFor(TimelineResult res, AbilityOptions opt, IEnumerable<string> startTokens)
        {
            var so = opt.ToSolver();
            if (startTokens != null) so.StartTokens.AddRange(startTokens);
            NavTokens(res.Abilities.Nav, so.OptionsOn);
            return so;
        }

        /// <summary>One timeline solve with the given logic options (the zones and the facts as built).</summary>
        public static TimelineSolve Solve(TimelineResult res, AbilityOptions opt, string label, IEnumerable<string> startTokens = null)
        {
            var so = SolverFor(res, opt, startTokens);
            var sv = res.Engine.Run(so, opt, label, null);
            res.Engine.ReturnCheck(sv, so);
            if (sv.Traps.Count > 0)
            {
                // again, taking nothing from inside a trap (unless the goal is there)
                var banned = new HashSet<int>(sv.Traps.SelectMany(t => t.Inside.Concat(new[] { t.Zone })));
                banned.Remove(res.GoalZone);
                var safe = res.Engine.Run(so, opt, label + " (safe)", banned);
                sv.SafeZones = safe.ZoneSphere.Count(kv => kv.Key < 1000);
                sv.SafeGoal = safe.GoalReached;
            }
            return sv;
        }

        static void RunVariants(TimelineResult res)
        {
            res.Variants.Clear();
            foreach (bool magic in new[] { true, false })
                foreach (bool once in new[] { false, true })
                    foreach (bool pick in new[] { false, true })
                    {
                        var o = res.Options.Clone();
                        o.Magic = magic; o.SingleUseItems = once; o.LockPicking = pick;
                        if (pick) { o.PickSkill = 30; o.PickRetry = true; }
                        string label = string.Format("Magic {0}, single-use {1}, picking {2}", magic ? "on" : "off", once ? "on" : "off", pick ? "on" : "off");
                        res.Variants.Add(new KeyValuePair<string, TimelineSolve>(label, Solve(res, o, label)));
                    }
            var k = res.Options.Clone();
            k.KillNpcs = true;
            res.Variants.Add(new KeyValuePair<string, TimelineSolve>("these options, killing NPCs on", Solve(res, k, "killing NPCs on")));
            var r = res.Options.Clone();
            r.RingNeedsLava = !r.RingNeedsLava;
            res.Variants.Add(new KeyValuePair<string, TimelineSolve>("these options, Ring of Levitation " + (r.RingNeedsLava ? "needs lava protection" : "free"), Solve(res, r, "ring")));
        }

        // ---------------------------------------------------------------- classifying the changes

        static readonly HashSet<string> Appliable = new HashSet<string> { "terrain", "delete", "door" };

        /// <summary>Applies each mechanism alone to its level and compares the rebuilt level with the level rebuilt unchanged.</summary>
        static void Classify(TimelineResult res)
        {
            var e = res.Engine;
            foreach (var m in res.Abilities.Auto.Graph.Mechanisms)
            {
                var c = new WorldChange { Mechanism = m };
                res.Changes.Add(c);
                if (!Appliable.Contains(m.Kind))
                {
                    c.Effect = "not applied";
                    c.Treatment = m.Kind == "create" ? "its flag is granted when set off (what it creates needs the flag)" :
                        m.Kind == "hack" ? "its flag is granted when set off; the hack itself is not modelled" : "its flag is granted when set off (facts may test it)";
                    continue;
                }
                if (m.Kind == "door" && (m.DoorAction == 2 || (m.DoorAction == 3 && m.Unavoidable)))
                {
                    var cl = res.Closers.FirstOrDefault(x => x.Mechanism == m);
                    c.Effect = "closes";
                    c.Closes.Add(string.Format("closes the door at ({0}, {1})", m.X, m.Y));
                    c.Treatment = cl == null ? "not applied (set off by a lever: the player leaves it)" : "never applied: the door is one way instead (" + cl.Treatment + ")";
                    continue;
                }
                // the mechanisms of one trigger go off together: try them together
                var together = res.Abilities.Auto.Graph.Mechanisms.Where(x => x.Level == m.Level && x.Trigger == m.Trigger && Appliable.Contains(x.Kind) && !(x.Kind == "door" && (x.DoorAction == 2 || (x.DoorAction == 3 && x.Unavoidable))))
                    .Select(x => Requirement.Normalize(x.Flag)).ToList();
                var basis = e.Rebuild(m.Level, new List<string>());
                var trial = e.Rebuild(m.Level, together);
                Compare(res, basis, trial, c);
                bool opens = c.Opens.Count > 0, closes = c.Closes.Count > 0;
                c.Effect = opens && closes ? "opens and closes" : opens ? "opens" : closes ? "closes" : "no way changed";
                if (closes && !opens && !m.Unavoidable)
                {
                    c.Treatment = "not applied: it only takes ways away and the player chooses to set it off";
                    continue;
                }
                c.Applied = true;
                c.Treatment = closes ? "applied when set off; the ways it takes away are gone from then on (the zones reached before stay reached)" : "applied when set off";
            }
            e.Applicable = new HashSet<string>(res.Changes.Where(c => c.Applied).Select(c => c.Flag));
        }

        static void Compare(TimelineResult res, LevelState a, LevelState b, WorldChange c)
        {
            var zs = res.Set;
            Func<ZoneLink, string> pair = k => k.From + ">" + k.To;
            var pa = new Dictionary<string, List<ZoneLink>>();
            foreach (var k in a.Links) { List<ZoneLink> l; if (!pa.TryGetValue(pair(k), out l)) pa[pair(k)] = l = new List<ZoneLink>(); l.Add(k); }
            var pb = new Dictionary<string, List<ZoneLink>>();
            foreach (var k in b.Links) { List<ZoneLink> l; if (!pb.TryGetValue(pair(k), out l)) pb[pair(k)] = l = new List<ZoneLink>(); l.Add(k); }
            Func<int, string> zn = z => b.NewNames.ContainsKey(z) ? z + " " + b.NewNames[z] : SolveResult.ZoneName(zs, z);
            foreach (var kv in pb)
            {
                var k = kv.Value[0];
                List<ZoneLink> old;
                bool freeNow = kv.Value.Any(x => x.Requires == "free");
                if (!pa.TryGetValue(kv.Key, out old)) c.Opens.Add(string.Format("{0} -> {1} ({2}, {3})", zn(k.From), zn(k.To), k.Kind, k.Requires));
                else if (freeNow && !old.Any(x => x.Requires == "free")) c.Opens.Add(string.Format("{0} -> {1} now free", zn(k.From), zn(k.To)));
            }
            // a way that went still counts as there when another route of the changed level does the same, needing nothing more
            Func<string, HashSet<string>> toks = r => { var l = new List<string>(); Requirement.TryParse(r, out _)?.Tokens(l); return new HashSet<string>(l.Where(t => t != "free")); };
            Func<int, int, string, bool> still = (from, to, req) =>
            {
                var allowed = toks(req);
                var seen = new HashSet<int> { b.ZoneOfBase(from) };
                var q = new Queue<int>(seen);
                int goal = b.ZoneOfBase(to);
                while (q.Count > 0)
                {
                    int z = q.Dequeue();
                    if (z == goal) return true;
                    foreach (var k in b.Links)
                    {
                        if (k.Requires != "free" && !toks(k.Requires).IsSubsetOf(allowed)) continue;
                        int nx = k.From == z ? k.To : k.TwoWay && k.To == z ? k.From : -1;
                        if (nx >= 0 && seen.Add(nx)) q.Enqueue(nx);
                    }
                }
                return false;
            };
            foreach (var kv in pa)
            {
                var k = kv.Value[0];
                List<ZoneLink> now;
                if (!pb.TryGetValue(kv.Key, out now))
                {
                    // a way gone; unless both ends are one zone now, or another way does as much
                    if (b.ZoneOfBase(k.From) == b.ZoneOfBase(k.To)) continue;
                    if (kv.Value.Any(x => still(k.From, k.To, x.Requires))) continue;
                    c.Closes.Add(string.Format("{0} -> {1} ({2}) gone", zn(k.From), zn(k.To), k.Kind));
                }
                else if (kv.Value.Any(x => x.Requires == "free") && !now.Any(x => x.Requires == "free") && !still(k.From, k.To, "free")) c.Closes.Add(string.Format("{0} -> {1} no longer free", zn(k.From), zn(k.To)));
            }
            foreach (var z in b.NewNames.Keys.Where(z => !a.NewNames.ContainsKey(z) || a.NewNames[z] != b.NewNames[z]))
                if (!a.NewNames.Values.Contains(b.NewNames[z])) c.Closes.Add("splits off " + z + " " + b.NewNames[z]);
            int rock = 0;
            for (int i = 0; i < 64 * 64; i++) if (!a.Model.Tiles[i % 64, i / 64].IsSolid && b.Model.Tiles[i % 64, i / 64].IsSolid) rock++;
            if (rock > 0) c.Closes.Add(rock + " square" + (rock == 1 ? "" : "s") + " turned to rock");
            int opened = 0;
            for (int i = 0; i < 64 * 64; i++) if (a.Model.Tiles[i % 64, i / 64].IsSolid && !b.Model.Tiles[i % 64, i / 64].IsSolid) opened++;
            if (opened > 0 && c.Opens.Count == 0) c.Opens.Add(opened + " square" + (opened == 1 ? "" : "s") + " opened (no new way between zones)");
            foreach (var kv in b.Absorbed) c.Opens.Add(string.Format("joins {0} with {1}", zn(kv.Key), zn(kv.Value)));
        }

        // ---------------------------------------------------------------- the level states

        /// <summary>A level as the timeline sees it after some changes: its rebuilt regions grouped into zones, the links, the mechanisms.</summary>
        internal sealed class LevelState
        {
            public int Level;
            public string Key;
            public LevelModel Model;
            public NavGraph Graph;
            /// <summary>Region id -> zone id.</summary>
            public int[] Zone;
            public readonly List<ZoneLink> Links = new List<ZoneLink>();
            public readonly List<KeyValuePair<NavMechanism, Requirement>> Mechs = new List<KeyValuePair<NavMechanism, Requirement>>();
            /// <summary>Zones split off by the changes (fresh ids) and their names.</summary>
            public readonly Dictionary<int, string> NewNames = new Dictionary<int, string>();
            /// <summary>Base zones with no group of their own any more -> the zone that took their tiles.</summary>
            public readonly Dictionary<int, int> Absorbed = new Dictionary<int, int>();
            /// <summary>Placements on this level that lie in another zone now.</summary>
            public readonly Dictionary<Placement, int> PlacementZone = new Dictionary<Placement, int>();
            public int ZoneOfBase(int z) { int t; return Absorbed.TryGetValue(z, out t) ? t : z; }
            /// <summary>Tile (y * 64 + x) -> zone, for placements found later (Phase D).</summary>
            public readonly Dictionary<int, int> TileZone = new Dictionary<int, int>();
        }

        /// <summary>The changing world: the base zones, the level states (cached by the changes applied) and the solver.</summary>
        internal sealed class Engine
        {
            readonly GameSession s;
            readonly Func<int, LevelModel> orig;
            readonly AbilityZoneResult b;
            readonly TimelineResult res;
            readonly ZoneSet zs;
            readonly Dictionary<string, LevelState> cache = new Dictionary<string, LevelState>();
            readonly Dictionary<string, NavMechanism> byFlag = new Dictionary<string, NavMechanism>();
            readonly Dictionary<string, Requirement> parsed = new Dictionary<string, Requirement>();
            /// <summary>Where each placement lies (level, x, y), when known.</summary>
            readonly Dictionary<Placement, int[]> spot = new Dictionary<Placement, int[]>();
            /// <summary>The base mechanisms per level, with their set-off requirement in zone terms.</summary>
            readonly Dictionary<int, List<KeyValuePair<NavMechanism, Requirement>>> baseMechs = new Dictionary<int, List<KeyValuePair<NavMechanism, Requirement>>>();
            readonly Dictionary<int, List<ZoneLink>> baseLinks = new Dictionary<int, List<ZoneLink>>();
            readonly List<ZoneLink> globalLinks = new List<ZoneLink>();
            readonly HashSet<string> cuts = new HashSet<string>();
            /// <summary>Zones that are only a door kept apart (a closer's doorway): the player passes them, he never stays in them.</summary>
            readonly HashSet<int> doorways = new HashSet<int>();
            int nextFresh = 1000;
            /// <summary>Phase D (the key item shuffle): the placements a run uses instead of the zone set's, and where the new ones lie (level, x, y).</summary>
            List<Placement> placements;
            readonly Dictionary<Placement, int[]> extraSpot = new Dictionary<Placement, int[]>();
            /// <summary>The count facts (kind "count"), by id.</summary>
            Dictionary<string, Fact> countFacts = new Dictionary<string, Fact>();
            readonly Dictionary<string, int> freshIds = new Dictionary<string, int>();
            /// <summary>The flags of the changes applied when set off (Classify).</summary>
            public HashSet<string> Applicable = new HashSet<string>();
            string plantedRequires;

            public Engine(GameSession s, Func<int, LevelModel> level, AbilityZoneResult b, TimelineResult res)
            {
                this.s = s; orig = level; this.b = b; this.res = res; zs = b.Set;
            }

            Requirement Req(string text)
            {
                text = string.IsNullOrWhiteSpace(text) ? "free" : text;
                Requirement r;
                if (parsed.TryGetValue(text, out r)) return r;
                string err;
                r = Requirement.TryParse(text, out err) ?? Requirement.Parse("flag:__unparsable__");
                if (err != null) res.Warnings.Add("Cannot read '" + text + "': " + err);
                parsed[text] = r;
                return r;
            }

            static readonly Regex RegionTerm = new Regex(@"region:(\d+)");

            /// <summary>The base world and the facts' links, warps and goal.</summary>
            public void Prepare()
            {
                var g = b.Auto.Graph;
                foreach (var m in g.Mechanisms) { string f = Requirement.Normalize(m.Flag); if (!byFlag.ContainsKey(f)) byFlag[f] = m; }
                foreach (var grp in g.Regions.Where(r => r.Zone > 0).GroupBy(r => r.Zone)) if (grp.All(r => r.KeepApart)) doorways.Add(grp.Key);
                foreach (var grp in g.Mechanisms.GroupBy(m => m.Level))
                    baseMechs[grp.Key] = grp.Select(m => new KeyValuePair<NavMechanism, Requirement>(m, Req(When(m, r => g.Regions[r].Zone)))).ToList();
                foreach (var k in zs.Links)
                {
                    if (k.Kind == "stairs") { globalLinks.Add(k); continue; }
                    List<ZoneLink> l;
                    if (!baseLinks.TryGetValue(k.Level, out l)) baseLinks[k.Level] = l = new List<ZoneLink>();
                    l.Add(k);
                }
                // where the placements lie
                var objs = new Dictionary<int, LevelModel>();
                Func<int, LevelModel> lm = l => { LevelModel m; if (!objs.TryGetValue(l, out m)) { try { m = b.WalledLevel(l); } catch (Exception) { m = null; } objs[l] = m; } return m; };
                var place = new Regex(@"^#(\d+) .*?level (\d+) \((\d+), (\d+)\)");
                foreach (var p in zs.Placements)
                {
                    var mm = place.Match(p.Notes ?? "");
                    if (mm.Success) { spot[p] = new[] { int.Parse(mm.Groups[2].Value), int.Parse(mm.Groups[3].Value), int.Parse(mm.Groups[4].Value) }; continue; }
                    var src = p.Source ?? "";
                    if (src.StartsWith("facts: "))
                    {
                        var f = res.Facts.Facts.FirstOrDefault(x => x.Id == src.Substring(7));
                        var at = f?.TopSpot;
                        if (at == null) continue;
                        int[] t = Tile(at, lm);
                        if (t != null) spot[p] = t;
                    }
                }
                Facts(lm);
                countFacts = res.Facts.OfKind("count").Where(f => f.Grants.Length > 0 && f.AtLeast > 0).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
            }

            /// <summary>The placements the runs use (null: the zone set's again), with the tiles of those the zone set does not hold.</summary>
            public void UsePlacements(List<Placement> list, Dictionary<Placement, int[]> spots)
            {
                placements = list;
                extraSpot.Clear();
                if (spots != null) foreach (var kv in spots) extraSpot[kv.Key] = kv.Value;
            }

            List<Placement> Placements { get { return placements ?? zs.Placements; } }

            /// <summary>The zone of a placement's tile in a changed level (placements the zone set does not hold).</summary>
            int ZoneOfTile(LevelState st, int[] t, int dflt)
            {
                int key = t[2] * 64 + t[1], z;
                if (st.TileZone.TryGetValue(key, out z)) return z > 0 ? z : dflt;
                int r = st.Graph.NearestRegion(st.Level, t[1], t[2], 2, true);
                z = r >= 0 && r < st.Zone.Length ? st.Zone[r] : 0;
                st.TileZone[key] = z;
                return z > 0 ? z : dflt;
            }

            int[] Tile(FactSpot at, Func<int, LevelModel> lm)
            {
                if (at == null || at.Level <= 0 || at.Level > zs.Levels) return null;
                if (at.HasTile) return new[] { at.Level, at.X, at.Y };
                if (!at.HasObject) return null;
                var m = lm(at.Level);
                if (m == null || at.Object >= m.Objects.Length || m.Objects[at.Object].Free || m.Objects[at.Object].TileX < 0) return null;
                return new[] { at.Level, m.Objects[at.Object].TileX, m.Objects[at.Object].TileY };
            }

            int ZoneAt(int[] t) { return t == null ? 0 : zs.NearestZone(t[0], t[1], t[2], 3); }

            void Facts(Func<int, LevelModel> lm)
            {
                foreach (var f in res.Facts.OfKind("link"))
                {
                    int a = ZoneAt(Tile(f.From, lm)), z = ZoneAt(Tile(f.To, lm));
                    if (a <= ZoneSet.Unassigned || z <= ZoneSet.Unassigned) { res.Warnings.Add("Facts: " + f.Id + ": no zone at " + f.From + " or " + f.To + "."); continue; }
                    if (a == z) { res.Warnings.Add("Facts: " + f.Id + ": " + f.From + " and " + f.To + " lie in the same zone (" + SolveResult.ZoneName(zs, a) + "); the link changes nothing."); continue; }
                    res.FactLinks.Add(new ZoneLink { Id = 9000 + res.FactLinks.Count, From = a, To = z, Kind = f.LinkKind.Length > 0 ? f.LinkKind : "other", Requires = f.Requires, TwoWay = f.TwoWay, Notes = "facts: " + f.Id, Level = f.From.Level, X = f.From.X, Y = f.From.Y, Confidence = f.Confidence == "todo" ? "todo" : "certain" });
                    if (f.Replace) { cuts.Add(a + ">" + z); cuts.Add(z + ">" + a); }
                }
                foreach (var f in res.Facts.OfKind("warp"))
                {
                    res.Warps.Add(f);
                    if (f.To != null && f.To.IsPlanted) plantedRequires = plantedRequires == null ? f.Requires : plantedRequires + " | " + f.Requires;
                }
                var goals = res.Facts.OfKind("goal").Where(f => f.Option.Length == 0).ToList();
                res.GoalExtras.AddRange(res.Facts.OfKind("goal").Where(f => f.Option.Length > 0));
                res.GoalZone = zs.GoalZone;
                if (goals.Count > 0)
                {
                    var f = goals[0];
                    int z = ZoneAt(Tile(f.To, lm));
                    if (z > ZoneSet.Unassigned) { res.GoalZone = z; res.GoalRequires = string.IsNullOrWhiteSpace(f.Requires) ? "free" : f.Requires; res.GoalFact = f; }
                    else res.Warnings.Add("Facts: " + f.Id + ": no zone at " + f.To + ".");
                    if (goals.Count > 1) res.Warnings.Add("Facts: " + goals.Count + " goal facts; the first (" + f.Id + ") is used.");
                }
            }

            /// <summary>What sets a mechanism off, in zone terms: where (its trigger's regions), its conditions, a look's Search check.</summary>
            string When(NavMechanism m, Func<int, int> zoneOf)
            {
                if (m.SetOff.Length == 0) return "flag:__never__";
                string needs = res.Facts.MechanismNeeds(m.Level, m.Trigger, m.Flag);
                string where = RegionTerm.Replace(m.SetOff, mm => { int r = int.Parse(mm.Groups[1].Value); int z = zoneOf(r); return z > 0 ? "zone:" + z : "flag:__no_zone__"; });
                var terms = new List<string> { where, m.Conditions, needs };
                // a look trigger of height z > 0 passes skill_check(Search, z); Reveal sets it off with Search 45 (event/TRIGGER.C UseTrigger, combat/SPELLS.C sp_true_sight)
                if (m.Look > 0) terms.Add(m.Look < 30 ? "spell:reveal | search:" + m.Look : "search:" + m.Look);
                return ZoneLink.JoinAnd(terms);
            }

            // ---- applying changes to a level

            /// <summary>The level with the changes applied, in the given order (event/TRIGGER.C, event/WORLDEV.C change_terrain).</summary>
            LevelModel Changed(int level, List<string> flags)
            {
                var o = orig(level);
                var m = LevelModel.FromBytes(s, level, o.Block.Data, o.TextureBytes);
                foreach (var f in flags)
                {
                    NavMechanism me;
                    if (!byFlag.TryGetValue(f, out me) || me.Level != level) continue;
                    Apply(m, me);
                }
                HashSet<int> walls;
                if (b.WallTiles.TryGetValue(level, out walls)) foreach (int t in walls) m.Tiles[t % 64, t / 64].Type = TileType.Solid;
                return m;
            }

            void Apply(LevelModel m, NavMechanism me)
            {
                var trap = m.Objects[me.Trap];
                switch (me.Kind)
                {
                    case "terrain":
                        {
                            // change_terrain: type (10 or more unchanged), height (UW1 at most 13), floor (11 or more unchanged); the wall texture does not matter here
                            // the chain may hold several change-terrain traps: apply each one met (NavMechanism joins them)
                            foreach (var t in TerrainTraps(m, me))
                            {
                                int type = t.Item2.Heading * 2 + (t.Item2.Quality & 1);
                                if (type == 15) type = 10;
                                int height = t.Item2.Z >> 3, floor = t.Item2.Quality >> 1;
                                for (int y = t.Item1[1]; y <= t.Item1[1] + t.Item2.FineY; y++)
                                    for (int x = t.Item1[0]; x <= t.Item1[0] + t.Item2.FineX; x++)
                                    {
                                        if (x < 0 || y < 0 || x > 63 || y > 63) continue;
                                        var tile = m.Tiles[x, y];
                                        if (height <= (s.IsUw2 ? 15 : 13)) tile.Height = height;
                                        if (floor < (s.IsUw2 ? 15 : 11)) tile.FloorIndex = floor;
                                        if (type < 10) tile.Type = (TileType)type;
                                    }
                            }
                            break;
                        }
                    case "delete":
                        {
                            // TRAP_DELETE_OBJECT: the object the trap links to, from the list of tile (quality, owner)
                            int x = trap.Quality, y = trap.Owner, i = trap.Link;
                            if (i <= 0 || i >= m.Objects.Length || x < 0 || y < 0 || x > 63 || y > 63) break;
                            var t = m.Tiles[x, y];
                            if (t.FirstObject == i) t.FirstObject = m.Objects[i].Next;
                            else
                            {
                                int j = t.FirstObject, guard = 0;
                                while (j > 0 && j < m.Objects.Length && guard++ < 1100) { if (m.Objects[j].Next == i) { m.Objects[j].Next = m.Objects[i].Next; break; } j = m.Objects[j].Next; }
                            }
                            var stack = new Stack<int>();
                            stack.Push(i);
                            var seen = new HashSet<int>();
                            while (stack.Count > 0) { int k = stack.Pop(); if (k <= 0 || !seen.Add(k)) continue; m.Objects[k].Free = true; foreach (int c in m.Objects[k].Children) stack.Push(c); }
                            break;
                        }
                    case "door":
                        {
                            // TRAP_DOOR (UW1): the door's lock goes, a copy of the trap's lock comes, then open, close or toggle
                            ObjectInfo door = null;
                            foreach (var o in m.TileObjects(me.X, me.Y)) if (o.Item >= 0x140 && o.Item <= 0x14F) { door = o; break; }
                            if (door == null) break;
                            door.Children.RemoveAll(c => m.Objects[c].Item == 0x10F);
                            if (!trap.IsQuantity && trap.Link > 0 && trap.Link < m.Objects.Length && m.Objects[trap.Link].Item == 0x10F) door.Children.Add(trap.Link);
                            bool open = door.Item >= 0x148;
                            bool want = trap.Quality == 1 ? true : trap.Quality == 2 ? false : !open;
                            if (want && !open) door.Item += 8;
                            if (!want && open) door.Item -= 8;
                            break;
                        }
                }
            }

            /// <summary>The change-terrain traps a mechanism's chain runs, with the square each runs at.</summary>
            static List<Tuple<int[], ObjectInfo>> TerrainTraps(LevelModel m, NavMechanism me)
            {
                var list = new List<Tuple<int[], ObjectInfo>>();
                var trig = m.Objects[me.Trigger];
                int rx = trig.Quality, ry = trig.Owner;
                var seen = new HashSet<int> { trig.Index };
                var next = Editing.Triggers.LinksTo(m, trig);
                while (next != null && seen.Add(next.Index) && seen.Count < 256)
                {
                    if (Editing.Triggers.IsTrigger(next.Item)) { rx = next.Quality; ry = next.Owner; next = Editing.Triggers.LinksTo(m, next); continue; }
                    if (!Editing.Triggers.IsTrap(next.Item)) break;
                    if (next.Item == 0x185) list.Add(Tuple.Create(new[] { rx, ry }, next));
                    int it = next.Item;
                    // an inventory or variable check: the mechanism goes off only when it passes (its conditions), so go on
                    if (it == 0x187 || it == 0x18B || it == 0x189 || it == 0x18A || it == 0x188) break;
                    next = Editing.Triggers.LinksTo(m, next);
                }
                return list;
            }

            /// <summary>The level rebuilt with the changes (flags, in order), its regions grouped and mapped onto the base zones (cached).</summary>
            public LevelState Rebuild(int level, List<string> flags)
            {
                var applied = flags.Where(f => byFlag.ContainsKey(f) && byFlag[f].Level == level).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
                string key = level + "|" + string.Join(",", applied);
                LevelState st;
                if (cache.TryGetValue(key, out st)) return st;
                var model = Changed(level, applied);
                var scratch = new AbilityZoneResult { Facts = b.Facts, Options = b.Options };
                Func<int, LevelModel> lv = l => l == level ? model : b.WalledLevel(l);
                var g = AbilityZones.Graph(s, lv, b.Nav, b.Facts, scratch, null, new[] { level });
                st = new LevelState { Level = level, Key = key, Model = model, Graph = g };
                Group(st, lv);
                cache[key] = st;
                return st;
            }

            /// <summary>
            /// Groups the rebuilt regions as the automatic zones do (the strongly connected parts
            /// of the free edges, door tiles with a neighbour, empty dead ends folded) and gives each
            /// group a zone: the base zone with the most tiles in it, each base zone to one group
            /// only (the one holding most of its tiles). A group left without one gets a new zone
            /// (a part split off by the change). A base zone whose tiles all went into groups that
            /// took other zones is joined to the zone that took most of them by a free link both
            /// ways (merged by the change).
            /// </summary>
            void Group(LevelState st, Func<int, LevelModel> lv)
            {
                var g = st.Graph;
                int n = g.Regions.Count;
                var adj = new List<int>[n];
                for (int i = 0; i < n; i++) adj[i] = new List<int>();
                foreach (var e in g.Edges) if (e.Requires == "free" && e.From != e.To && e.Kind != "stairs") adj[e.From].Add(e.To);
                var comp = AutoZones.Scc(adj);
                var size = new Dictionary<int, int>();
                foreach (int c in comp) { int k; size.TryGetValue(c, out k); size[c] = k + 1; }
                var nb = new Dictionary<int, SortedSet<int>>();
                foreach (var e in g.Edges)
                {
                    if (e.Kind == "stairs" || e.Kind == "teleport") continue;
                    foreach (var p in new[] { (e.From, e.To), (e.To, e.From) })
                    {
                        if (!g.Regions[p.Item1].IsDoor || g.Regions[p.Item2].IsDoor) continue;
                        SortedSet<int> set;
                        if (!nb.TryGetValue(p.Item1, out set)) nb[p.Item1] = set = new SortedSet<int>();
                        set.Add(p.Item2);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    if (!g.Regions[i].IsDoor || g.Regions[i].KeepApart || size[comp[i]] > 1) continue;
                    SortedSet<int> set;
                    if (nb.TryGetValue(i, out set) && set.Count > 0) comp[i] = comp[set.Min];
                }
                if (g.Options.FoldEmptyDeadEnds)
                {
                    // the stairs of this level (not in a one-level graph) keep their regions from folding, as in the full graph
                    var busy = new HashSet<int>();
                    var g0 = b.Auto.Graph;
                    foreach (var e in g0.Edges)
                    {
                        if (e.Kind != "stairs") continue;
                        foreach (int r0 in new[] { e.From, e.To })
                        {
                            var reg = g0.Regions[r0];
                            if (reg.Level != st.Level) continue;
                            foreach (int t in reg.Tiles) { int r = g.RegionOf(st.Level, t % 64, t / 64); if (r >= 0) busy.Add(r); }
                        }
                    }
                    AutoZones.FoldDeadEnds(s, lv, g, comp, busy);
                }
                // the base zones in each group
                var counts = new Dictionary<int, Dictionary<int, int>>();
                var tiles = new Dictionary<int, int>();
                for (int i = 0; i < n; i++)
                {
                    Dictionary<int, int> c;
                    if (!counts.TryGetValue(comp[i], out c)) counts[comp[i]] = c = new Dictionary<int, int>();
                    foreach (int t in g.Regions[i].Tiles)
                    {
                        int z = zs.ZoneAt(st.Level, t % 64, t / 64);
                        int k; tiles.TryGetValue(comp[i], out k); tiles[comp[i]] = k + 1;
                        if (z <= ZoneSet.Unassigned) continue;
                        int v; c.TryGetValue(z, out v); c[z] = v + 1;
                    }
                }
                var claims = counts.SelectMany(kv => kv.Value.Select(z => new { Group = kv.Key, Zone = z.Key, Count = z.Value })).OrderByDescending(x => x.Count).ThenBy(x => x.Zone).ThenBy(x => x.Group).ToList();
                var idOf = new Dictionary<int, int>();
                var taken = new HashSet<int>();
                foreach (var c in claims)
                {
                    if (idOf.ContainsKey(c.Group) || taken.Contains(c.Zone)) continue;
                    idOf[c.Group] = c.Zone;
                    taken.Add(c.Zone);
                }
                foreach (var grp in counts.Keys.OrderBy(x => x))
                {
                    if (idOf.ContainsKey(grp)) continue;
                    var top = counts[grp].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).FirstOrDefault();
                    var mine = Enumerable.Range(0, n).Where(i => comp[i] == grp).ToList();
                    // the same part gets the same id in every state: keyed by the level, its main base zone and its first tile of that zone
                    int first = mine.SelectMany(i => g.Regions[i].Tiles).Where(t => top <= ZoneSet.Unassigned || zs.ZoneAt(st.Level, t % 64, t / 64) == top).DefaultIfEmpty(-1).Min();
                    string fk = st.Level + "|" + top + "|" + first;
                    int id;
                    if (!freshIds.TryGetValue(fk, out id)) freshIds[fk] = id = nextFresh++;
                    idOf[grp] = id;
                    st.NewNames[id] = top > ZoneSet.Unassigned ? string.Format("part of {0} at ({1}, {2})", ZoneName(top), first % 64, first / 64) : string.Format("L{0} new area at ({1}, {2})", st.Level, first % 64, first / 64);
                }
                st.Zone = new int[n];
                for (int i = 0; i < n; i++) st.Zone[i] = idOf[comp[i]];
                // base zones of this level with no group: merged into the group holding most of their tiles
                var levelZones = zs.CountsOnLevel(st.Level).Keys.Where(z => z > ZoneSet.Unassigned).ToList();
                foreach (int z in levelZones)
                {
                    if (taken.Contains(z)) continue;
                    var best = counts.Where(kv => kv.Value.ContainsKey(z)).OrderByDescending(kv => kv.Value[z]).Select(kv => (int?)kv.Key).FirstOrDefault();
                    if (best == null) continue;
                    st.Absorbed[z] = idOf[best.Value];
                    st.Links.Add(new ZoneLink { From = z, To = idOf[best.Value], Kind = "walk", Requires = "free", TwoWay = true, Level = st.Level, Notes = "merged by the change" });
                }
                // the links
                var seenLinks = new HashSet<string>();
                Func<int, int> zoneOf = r => r >= 0 && r < n ? st.Zone[r] : 0;
                foreach (var e in g.Edges)
                {
                    if (e.Kind == "stairs") continue;
                    int a = zoneOf(e.From), z = zoneOf(e.To);
                    if (a == 0 || z == 0 || a == z) continue;
                    string req = AutoZones.Normalize(AutoZones.Simplify(RegionTerm.Replace(e.Requires, mm => "zone:" + zoneOf(int.Parse(mm.Groups[1].Value))), a));
                    string k = a + ">" + z + "|" + e.Kind + "|" + req;
                    if (!seenLinks.Add(k)) continue;
                    st.Links.Add(new ZoneLink { From = a, To = z, Kind = e.Kind, Requires = req, Level = st.Level, X = e.X, Y = e.Y, Notes = e.Notes, Confidence = e.Kind == "jump" || req.Contains("flag:var") ? "todo" : "certain" });
                }
                foreach (var m in g.Mechanisms) st.Mechs.Add(new KeyValuePair<NavMechanism, Requirement>(m, Req(When(m, zoneOf))));
                foreach (var grp in Enumerable.Range(0, n).GroupBy(i => st.Zone[i])) if (grp.All(i => g.Regions[i].KeepApart)) doorways.Add(grp.Key);
                foreach (var kv in spot)
                {
                    if (kv.Value[0] != st.Level) continue;
                    int r = g.NearestRegion(st.Level, kv.Value[1], kv.Value[2], 2, true);
                    if (r < 0) continue;
                    if (st.Zone[r] != kv.Key.Zone) st.PlacementZone[kv.Key] = st.Zone[r];
                }
            }

            string ZoneName(int z) { var zz = zs.GetZone(z); return zz == null ? "zone " + z : zz.Name; }

            // ---- the solve

            sealed class Ctx : IRequirementContext, INowContext
            {
                public SolverOptions Options { get; set; }
                public HashSet<string> Tokens = new HashSet<string>();
                public HashSet<int> Reach = new HashSet<int>();
                /// <summary>Zones reached before (the return check): their levers were pulled and stay so.</summary>
                public HashSet<int> Before;
                public bool HasToken(string t) { return Tokens.Contains(Requirement.Normalize(t)); }
                public bool ZoneReachable(int z) { return Reach.Contains(z) || (Before != null && Before.Contains(z)); }
                public bool ZoneNow(int z) { return Reach.Contains(z); }
                public int TalismanCount() { return Tokens.Count(t => t.StartsWith("talisman:")); }
            }

            /// <summary>The links of the world in the given level states.</summary>
            List<ZoneLink> LinksOf(Dictionary<int, LevelState> cur, SolverOptions so)
            {
                var list = new List<ZoneLink>(globalLinks);
                for (int l = 1; l <= zs.Levels; l++)
                {
                    LevelState st;
                    if (cur.TryGetValue(l, out st)) list.AddRange(st.Links);
                    else { List<ZoneLink> bl; if (baseLinks.TryGetValue(l, out bl)) list.AddRange(bl); }
                }
                if (cuts.Count > 0) list.RemoveAll(k => cuts.Contains(k.From + ">" + k.To));
                foreach (var k in res.FactLinks)
                {
                    var f = res.Facts.Facts.FirstOrDefault(x => "facts: " + x.Id == k.Notes);
                    string req = k.Requires;
                    if (f != null && f.Option.Length > 0 && !so.OptionsOn.Contains(f.Option))
                    {
                        if (string.IsNullOrWhiteSpace(f.Otherwise)) continue;
                        req = f.Otherwise;
                    }
                    list.Add(new ZoneLink { Id = k.Id, From = k.From, To = k.To, Kind = k.Kind, Requires = req, TwoWay = k.TwoWay, Notes = k.Notes, Level = k.Level, X = k.X, Y = k.Y, Confidence = k.Confidence });
                }
                return list;
            }

            int PlacementZone(Placement p, Dictionary<int, LevelState> cur)
            {
                int[] t;
                LevelState st;
                if (spot.TryGetValue(p, out t) && cur.TryGetValue(t[0], out st))
                {
                    int z;
                    if (st.PlacementZone.TryGetValue(p, out z)) return z;
                }
                else if (extraSpot.TryGetValue(p, out t) && cur.TryGetValue(t[0], out st)) return ZoneOfTile(st, t, p.Zone);
                return p.Zone;
            }

            bool WarpHolds(Fact f, Ctx ctx, Dictionary<int, LevelState> cur)
            {
                if (f.Option.Length > 0 && !ctx.Options.OptionsOn.Contains(f.Option)) return false;
                if (f.FromLevel > 0 && !ctx.Reach.Any(z => zs.LevelsOf(z).Contains(f.FromLevel))) return false;
                return Req(f.Requires).Eval(ctx);
            }

            int WarpTarget(Fact f)
            {
                if (f.To == null || f.To.IsPlanted) return 0;
                LevelModel m = null;
                var t = Tile(f.To, l => { if (m == null || m.Level != l) { try { m = b.WalledLevel(l); } catch (Exception) { m = null; } } return m; });
                return ZoneAt(t);
            }

            /// <summary>The forward search with the world changing; banned zones grant nothing (the safe solve).</summary>
            public TimelineSolve Run(SolverOptions so, AbilityOptions opt, string label, HashSet<int> banned)
            {
                var sv = new TimelineSolve { Label = label };
                var ctx = new Ctx { Options = so };
                foreach (var t in so.StartTokens) ctx.Tokens.Add(Requirement.Normalize(t));
                var cur = new Dictionary<int, LevelState>();
                var applied = new Dictionary<int, List<string>>();
                var fired = new HashSet<string>();
                int start = zs.StartZone;
                ctx.Reach.Add(start);
                sv.ZoneSphere[start] = 0;
                var warpTo = res.Warps.ToDictionary(f => f, f => WarpTarget(f));
                var counted = new HashSet<Placement>();
                var tally = new Dictionary<string, int>();
                var sphere = new TimelineSphere { Index = 0 };
                sphere.Zones.Add(start);
                List<ZoneLink> links = LinksOf(cur, so);
                for (int round = 0; round < 400; round++)
                {
                    bool any = false;
                    if (round > 0)
                    {
                        sphere = new TimelineSphere { Index = sv.Spheres.Count };
                        var newly = new List<int>();
                        foreach (var k in links)
                        {
                            if (k.Confidence == "todo" && !so.TrustTodoLinks) continue;
                            bool fwd = ctx.Reach.Contains(k.From) && !ctx.Reach.Contains(k.To) && !newly.Contains(k.To);
                            bool back = k.TwoWay && ctx.Reach.Contains(k.To) && !ctx.Reach.Contains(k.From) && !newly.Contains(k.From);
                            if (!fwd && !back) continue;
                            if (!Req(k.Requires).Eval(ctx)) continue;
                            int z = fwd ? k.To : k.From;
                            newly.Add(z);
                            sv.FirstLink[z] = k;
                        }
                        foreach (var f in res.Warps)
                        {
                            int z = warpTo[f];
                            if (z <= ZoneSet.Unassigned || ctx.Reach.Contains(z) || newly.Contains(z) || !WarpHolds(f, ctx, cur)) continue;
                            newly.Add(z);
                            sv.FirstLink[z] = new ZoneLink { From = 0, To = z, Kind = "warp", Requires = f.Requires, Notes = "facts: " + f.Id };
                        }
                        foreach (int z in newly) { ctx.Reach.Add(z); sv.ZoneSphere[z] = sphere.Index; sphere.Zones.Add(z); }
                        any = newly.Count > 0;
                    }
                    // grant what the reached zones hold, then set off what can go off
                    bool gained = true;
                    var toApply = new Dictionary<int, List<string>>();
                    while (gained)
                    {
                        gained = false;
                        foreach (var p in Placements)
                        {
                            string t = Requirement.Normalize(p.Grants);
                            bool counts = t.Length == 0 && !string.IsNullOrEmpty(p.Counts);
                            if (counts ? counted.Contains(p) : (t.Length == 0 || ctx.Tokens.Contains(t))) continue;
                            int z = PlacementZone(p, cur);
                            if (!ctx.Reach.Contains(z) || (banned != null && banned.Contains(z))) continue;
                            if (!string.IsNullOrEmpty(p.Requires) && !Req(p.Requires).Eval(ctx)) continue;
                            if (counts)
                            {
                                // a count fact (Shak's 20 gold): the quantities add up; the token comes once the total is reached
                                counted.Add(p);
                                int n; tally.TryGetValue(p.Counts, out n); tally[p.Counts] = n += p.Quantity;
                                Fact cf;
                                if (!countFacts.TryGetValue(p.Counts, out cf) || n < cf.AtLeast) continue;
                                t = Requirement.Normalize(cf.Grants);
                                if (ctx.Tokens.Contains(t)) continue;
                            }
                            ctx.Tokens.Add(t);
                            sphere.Tokens.Add(t);
                            sphere.TokenSources.Add(t + " (zone " + z + (string.IsNullOrEmpty(p.Requires) ? "" : ", with " + p.Requires) + ")");
                            sv.TokenSphere[t] = sphere.Index;
                            gained = true;
                        }
                        for (int l = 1; l <= zs.Levels; l++)
                        {
                            LevelState st;
                            List<KeyValuePair<NavMechanism, Requirement>> ms;
                            if (cur.TryGetValue(l, out st)) ms = st.Mechs;
                            else if (!baseMechs.TryGetValue(l, out ms)) continue;
                            foreach (var kv in ms)
                            {
                                string f = Requirement.Normalize(kv.Key.Flag);
                                if (fired.Contains(f) || !kv.Value.Eval(ctx)) continue;
                                fired.Add(f);
                                sv.Fired.Add(f);
                                sphere.SetOff.Add(f);
                                if (!ctx.Tokens.Contains(f)) { ctx.Tokens.Add(f); sv.TokenSphere[f] = sphere.Index; }
                                gained = true;
                                if (Applicable.Contains(f))
                                {
                                    List<string> a;
                                    if (!toApply.TryGetValue(l, out a)) toApply[l] = a = new List<string>();
                                    a.Add(f);
                                }
                            }
                        }
                    }
                    foreach (var kv in toApply)
                    {
                        List<string> a;
                        if (!applied.TryGetValue(kv.Key, out a)) applied[kv.Key] = a = new List<string>();
                        a.AddRange(kv.Value);
                        var st = Rebuild(kv.Key, a);
                        cur[kv.Key] = st;
                        foreach (var z in st.NewNames) sv.NewZones[z.Key] = z.Value;
                        sphere.Applied.AddRange(kv.Value);
                        sv.Applied.AddRange(kv.Value);
                    }
                    foreach (var kv in cur) sphere.State[kv.Key] = kv.Value.Key;
                    if (toApply.Count > 0) links = LinksOf(cur, so);
                    if (round > 0 && !any && sphere.Tokens.Count == 0 && sphere.SetOff.Count == 0) break;
                    sv.Spheres.Add(sphere);
                }
                // the goal
                sv.GoalZoneReached = ctx.Reach.Contains(res.GoalZone);
                var greq = Req(res.GoalFor(so));
                sv.GoalReached = sv.GoalZoneReached && greq.Eval(ctx);
                if (!sv.GoalZoneReached) sv.GoalMissing.Add("the goal zone " + ZoneName(res.GoalZone));
                greq.Missing(ctx, sv.GoalMissing);
                // blocked links out of the reached part
                foreach (var k in links)
                {
                    foreach (bool rev in new[] { false, true })
                    {
                        if (rev && !k.TwoWay) continue;
                        int a = rev ? k.To : k.From, z = rev ? k.From : k.To;
                        if (!ctx.Reach.Contains(a) || ctx.Reach.Contains(z)) continue;
                        var bl = new BlockedLink { Link = k, Reverse = rev, FromZone = a, ToZone = z };
                        Req(k.Requires).Missing(ctx, bl.Missing);
                        if (bl.Missing.Count == 0) bl.Missing.Add(k.Confidence == "todo" ? "(todo link, not trusted)" : "(?)");
                        sv.Blocked.Add(bl);
                    }
                }
                lastWorld = cur;
                lastLinks = links;
                return sv;
            }

            Dictionary<int, LevelState> lastWorld;
            List<ZoneLink> lastLinks;

            /// <summary>
            /// The return check: from each zone reached, with what the player held when he got there
            /// (and what he finds on the way), in the world as it stood at the end of that sphere,
            /// can he get back to the start? If not, the zone is a one-way trap. Once the silver
            /// sapling's requirement holds (a warp fact to "planted"), the player plants it before
            /// going one way, so every zone reached later can return (death takes him back).
            /// </summary>
            public void ReturnCheck(TimelineSolve sv, SolverOptions so)
            {
                int start = zs.StartZone;
                var planted = plantedRequires == null ? null : Req(plantedRequires);
                var states = new Dictionary<string, List<ZoneLink>>();
                var warpTo = res.Warps.Where(f => f.To != null && !f.To.IsPlanted).ToDictionary(f => f, f => WarpTarget(f));
                var trapZones = new Dictionary<int, OneWayTrap>();
                foreach (var kv in sv.ZoneSphere.OrderBy(x => x.Value).ThenBy(x => x.Key))
                {
                    int z = kv.Key, sp = kv.Value;
                    if (z == start || doorways.Contains(z)) continue;
                    var ctx = new Ctx { Options = so, Before = new HashSet<int>(sv.ZoneSphere.Where(x => x.Value < sp).Select(x => x.Key)) };
                    foreach (var t in sv.TokenSphere) if (t.Value < sp) ctx.Tokens.Add(t.Key);
                    if (planted != null && planted.Eval(ctx)) { sv.SavedBySapling++; continue; }
                    // the world at the end of sphere sp
                    var st = sp < sv.Spheres.Count ? sv.Spheres[sp].State : new Dictionary<int, string>();
                    string key = string.Join(";", st.OrderBy(x => x.Key).Select(x => x.Value));
                    var cur = st.ToDictionary(x => x.Key, x => cache[x.Value]);
                    List<ZoneLink> links;
                    if (!states.TryGetValue(key, out links)) states[key] = links = LinksOf(cur, so);
                    ctx.Reach.Add(z);
                    bool back = false;
                    for (int round = 0; round < 300 && !back; round++)
                    {
                        bool any = false;
                        foreach (var p in Placements)
                        {
                            string t = Requirement.Normalize(p.Grants);
                            if (t.Length == 0 || ctx.Tokens.Contains(t) || !ctx.Reach.Contains(PlacementZone(p, cur))) continue;
                            if (!string.IsNullOrEmpty(p.Requires) && !Req(p.Requires).Eval(ctx)) continue;
                            ctx.Tokens.Add(t); any = true;
                        }
                        foreach (var k in links)
                        {
                            bool fwd = ctx.Reach.Contains(k.From) && !ctx.Reach.Contains(k.To);
                            bool rev = k.TwoWay && ctx.Reach.Contains(k.To) && !ctx.Reach.Contains(k.From);
                            if ((fwd || rev) && Req(k.Requires).Eval(ctx)) { ctx.Reach.Add(fwd ? k.To : k.From); any = true; }
                        }
                        foreach (var w in warpTo)
                            if (w.Value > ZoneSet.Unassigned && !ctx.Reach.Contains(w.Value) && WarpHolds(w.Key, ctx, null)) { ctx.Reach.Add(w.Value); any = true; }
                        if (ctx.Reach.Contains(start)) back = true;
                        if (!any) break;
                    }
                    if (back) continue;
                    var trap = new OneWayTrap { Zone = z, Sphere = sp };
                    ZoneLink entry;
                    if (sv.FirstLink.TryGetValue(z, out entry)) trap.Entry = entry;
                    trap.Inside.AddRange(ctx.Reach.Where(x => x != z).OrderBy(x => x));
                    trapZones[z] = trap;
                    sv.Traps.Add(trap);
                }
                // what the traps hold that nothing outside a trap gives, and whether a later step needs it
                var used = new HashSet<string>();
                var needed = new List<string>();
                foreach (var k in lastLinks ?? new List<ZoneLink>()) Req(k.Requires).Tokens(needed);
                foreach (var p in Placements) if (!string.IsNullOrEmpty(p.Requires)) Req(p.Requires).Tokens(needed);
                Req(res.GoalFor(so)).Tokens(needed);
                foreach (var t in needed) used.Add(Requirement.Normalize(t));
                var inTrap = new HashSet<int>(trapZones.Keys);
                foreach (var trap in sv.Traps)
                {
                    var here = new HashSet<int>(trap.Inside) { trap.Zone };
                    foreach (var p in Placements)
                    {
                        string t = Requirement.Normalize(p.Grants);
                        if (t.Length == 0 || !here.Contains(p.Zone) || !sv.TokenSphere.ContainsKey(t)) continue;
                        bool elsewhere = Placements.Any(q => Requirement.Normalize(q.Grants) == t && sv.ZoneSphere.ContainsKey(q.Zone) && !inTrap.Contains(q.Zone) && !here.Contains(q.Zone));
                        if (elsewhere || trap.Holds.Contains(t)) continue;
                        trap.Holds.Add(t);
                        if (used.Contains(t) || (t.StartsWith("talisman:") && used.Any(u => u.StartsWith("talismans:")))) trap.Strands.Add(t);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- the report

        static string Z(TimelineResult res, TimelineSolve sv, int id)
        {
            string n;
            if (sv != null && sv.NewZones.TryGetValue(id, out n)) return id + " " + n;
            return SolveResult.ZoneName(res.Set, id);
        }

        public static string Report(TimelineResult res)
        {
            var zs = res.Set;
            var sv = res.Solve;
            var sb = new StringBuilder();
            sb.AppendLine("AUTOMATIC ZONES, PHASE C: THE WORLD OVER TIME");
            sb.AppendLine();
            foreach (var w in res.Warnings.Distinct()) sb.AppendLine("Warning: " + w);
            sb.AppendLine("Options: " + res.Options + ".");
            sb.AppendLine("Facts read: " + string.Join(", ", res.Facts.Files) + " (" + res.Facts.Facts.Count + " facts; " + string.Join(", ", res.Facts.Facts.GroupBy(f => f.Kind).Select(g => g.Key + " " + g.Count())) + ").");
            sb.AppendLine("Rules: glitchless. Bashing is always allowed (fists). item:pole means the pole or Telekinesis.");
            sb.AppendLine("Zones are those of the base state. After a change, its level alone is rebuilt; each group of regions takes the base zone that has the most tiles in it; a part split off gets a new zone (ids from 1000); a base zone left without a group is joined by a free link to the one that took its tiles.");
            sb.AppendLine();
            if (sv != null)
            {
                sb.AppendLine(string.Format("SOLVE ({0}): {1}; {2} spheres, {3} of {4} zones reached, {5} changes set off, {6} applied.", sv.Label, sv.GoalReached ? "goal reached" : "goal NOT reached",
                    sv.Spheres.Count, sv.ZoneSphere.Count(kv => kv.Key < 1000), zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), sv.Fired.Count, sv.Applied.Count));
                sb.AppendLine("  Goal: " + Z(res, sv, res.GoalZone) + (res.GoalFact != null ? " (facts: " + res.GoalFact.Id + "), needs " + res.GoalRequires : " (no goal fact: the largest zone of the last level)") + (sv.GoalMissing.Count > 0 ? "; missing: " + string.Join(", ", sv.GoalMissing.Distinct().Take(20)) : ""));
                var missing = sv.Blocked.SelectMany(x => x.Missing).GroupBy(t => t).OrderByDescending(x => x.Count()).Take(15).ToList();
                if (missing.Count > 0) sb.AppendLine("  What blocks the links out of the reached part (token: links): " + string.Join(", ", missing.Select(x => x.Key + " " + x.Count())));
                if (sv.SafeZones >= 0) sb.AppendLine(string.Format("  Taking nothing from inside the one-way traps: {0} zones{1}.", sv.SafeZones, sv.SafeGoal ? ", goal reached" : ""));
                sb.AppendLine();
                sb.AppendLine("  The main option sets (zones reached, spheres, changes applied, traps, goal):");
                foreach (var kv in res.Variants)
                    sb.AppendLine(string.Format("    {0}: {1} zones, {2} spheres, {3} applied, {4} one-way traps{5}{6}", kv.Key, kv.Value.ZoneSphere.Count(x => x.Key < 1000), kv.Value.Spheres.Count, kv.Value.Applied.Count, kv.Value.Traps.Count,
                        kv.Value.SafeZones >= 0 ? " (" + kv.Value.SafeZones + " safe)" : "", kv.Value.GoalReached ? ", goal reached" : ""));
                sb.AppendLine();
                sb.AppendLine("TIMELINE (each sphere: zones reached, tokens gained, changes set off and applied)");
                foreach (var sp in sv.Spheres)
                {
                    sb.AppendLine(string.Format("  {0}: {1} zones: {2}", sp.Index, sp.Zones.Count, string.Join(", ", sp.Zones.Take(14).Select(z => Z(res, sv, z))) + (sp.Zones.Count > 14 ? ", …" : "")));
                    if (sp.Tokens.Count > 0) sb.AppendLine("     gains " + string.Join(", ", sp.Tokens.Where(t => !sp.SetOff.Contains(t)).Take(30)) + (sp.Tokens.Count > 30 ? " …" : ""));
                    if (sp.SetOff.Count > 0) sb.AppendLine("     sets off " + string.Join(", ", sp.SetOff.Select(f => sp.Applied.Contains(f) ? f + " (applied)" : f)));
                }
                sb.AppendLine();
            }
            sb.AppendLine("CHANGES THAT CLOSE THINGS (and how each is treated)");
            foreach (var c in res.Changes.Where(c => c.Closes.Count > 0))
                sb.AppendLine(string.Format("  {0} [{1}{2}]: {3}. {4}.{5}", c.Mechanism.Flag, c.Effect, c.Mechanism.Unavoidable ? ", unavoidable" : ", chosen", string.Join("; ", c.Closes.Take(6)) + (c.Closes.Count > 6 ? " …" : ""), Capital(c.Treatment),
                    c.Opens.Count > 0 ? " Opens: " + string.Join("; ", c.Opens.Take(4)) + (c.Opens.Count > 4 ? " …" : "") + "." : ""));
            foreach (var cl in res.Closers)
                sb.AppendLine(string.Format("  door closer {0}: trigger #{1}; sides {2}; {3} edges changed", cl, cl.Mechanism.Trigger, string.Join(", ", cl.Sides.Select(p => "region " + p.Key + (p.Value ? " (trigger side)" : ""))), cl.EdgesChanged));
            sb.AppendLine();
            sb.AppendLine("CHANGES THAT OPEN THINGS");
            foreach (var c in res.Changes.Where(c => c.Opens.Count > 0 && c.Closes.Count == 0))
                sb.AppendLine(string.Format("  {0}{1}: {2}{3}", c.Mechanism.Flag, c.Mechanism.Free ? " (free by the facts)" : "", string.Join("; ", c.Opens.Take(5)) + (c.Opens.Count > 5 ? " …" : ""), sv != null && sv.Applied.Contains(c.Flag) ? "  [applied]" : sv != null && sv.Fired.Contains(c.Flag) ? "" : "  [not set off]"));
            sb.AppendLine(string.Format("  Changes with no effect on the ways: {0}; not applied (variables, creatures, hacks): {1}.", res.Changes.Count(c => c.Effect == "no way changed"), res.Changes.Count(c => c.Effect == "not applied")));
            sb.AppendLine();
            if (sv != null)
            {
                sb.AppendLine(string.Format("RETURN CHECK (from each zone reached, back to the start with what was held then; {0} one-way traps{1})", sv.Traps.Count, sv.SavedBySapling > 0 ? ", " + sv.SavedBySapling + " zones saved by the planted sapling" : ""));
                foreach (var t in sv.Traps)
                    sb.AppendLine(string.Format("  {0} (sphere {1}){2}{3}{4}{5}", Z(res, sv, t.Zone), t.Sphere, t.Entry != null ? " entered by [" + t.Entry.Kind + "] " + t.Entry.Requires + (t.Entry.Level > 0 ? " at L" + t.Entry.Level + " (" + t.Entry.X + ", " + t.Entry.Y + ")" : "") : "",
                        t.Inside.Count > 0 ? "; leads on to " + string.Join(", ", t.Inside.Take(5).Select(z => Z(res, sv, z))) + (t.Inside.Count > 5 ? " …" : "") : "",
                        t.Holds.Count > 0 ? "; holds " + string.Join(", ", t.Holds.Take(8)) : "; nothing needed inside",
                        t.Strands.Count > 0 ? "; STRANDS: " + string.Join(", ", t.Strands) : ""));
                sb.AppendLine();
            }
            sb.AppendLine("FACTS: LINKS, WARPS, GOAL");
            foreach (var k in res.FactLinks)
            {
                var f = res.Facts.Facts.FirstOrDefault(x => "facts: " + x.Id == k.Notes);
                sb.AppendLine(string.Format("  {0}: {1} {2} {3} needs {4}{5}", k.Notes, Z(res, sv, k.From), k.TwoWay ? "<->" : "->", Z(res, sv, k.To), k.Requires,
                    f != null && f.Option.Length > 0 ? " with option:" + f.Option + (f.Otherwise.Length > 0 ? ", else " + f.Otherwise : ", else no link") : ""));
            }
            foreach (var f in res.Warps) sb.AppendLine(string.Format("  warp {0}: from {1} to {2} when {3}", f.Id, f.FromLevel > 0 ? "level " + f.FromLevel : "every zone", f.To, f.Requires));
            if (res.GoalFact != null) sb.AppendLine(string.Format("  goal {0}: {1} at {2}, needs {3}", res.GoalFact.Id, Z(res, sv, res.GoalZone), res.GoalFact.To, res.GoalRequires));
            foreach (var f in res.Facts.OfKind("free")) sb.AppendLine(string.Format("  free {0}: L{1} {2}", f.Id, f.Level, string.Join(", ", f.Objects.Select(o => "#" + o).Concat(f.Triggers.Select(t => "trigger #" + t)).Concat(f.Flags))));
            var npc = res.Abilities.NpcHeld;
            if (npc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("ITEMS NPCs CARRY ({0}; option:kill_npcs {1})", npc.Count, res.Options.KillNpcs ? "on" : "off"));
                foreach (var p in npc.Where(p => !p.Grants.StartsWith("item:food")).Take(40)) sb.AppendLine(string.Format("  {0} in {1}: needs {2} ({3})", p.Grants, Z(res, sv, p.Zone), p.Requires, p.Notes));
            }
            if (res.Abilities.Created.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ITEMS A CREATE TRAP MAKES");
                foreach (var p in res.Abilities.Created) sb.AppendLine(string.Format("  {0} in {1}: needs {2}{3}", p.Grants, Z(res, sv, p.Zone), p.Requires, sv != null && sv.TokenSphere.ContainsKey(Requirement.Normalize(p.Grants)) ? "  [obtained]" : ""));
            }
            return sb.ToString();
        }

        static string Capital(string t) { return string.IsNullOrEmpty(t) ? t : char.ToUpperInvariant(t[0]) + t.Substring(1); }
    }
}
