using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>The main window's side of the randomizer's zoning: the shared state, its windows, painting and the overlay.</summary>
    public partial class MainWindow
    {
        /// <summary>
        /// The movement and logic options the zoning tools use: the Apply window's when it is open,
        /// else the Automatic zones window's, else the defaults (copies). from says which.
        /// </summary>
        public void ZoningOptions(out NavOptions nav, out AbilityOptions abil, out string from)
        {
            var apply = OpenKeyShuffle;
            var auto = OpenAutoZones;
            if (apply != null) { nav = apply.Options.Nav.Clone(); abil = apply.Options.Logic.Clone(); from = "the Apply window's options"; }
            else if (auto != null) { nav = auto.MovementOptions.Clone(); abil = auto.LogicOptions.Clone(); from = "the Automatic zones window's options"; }
            else { nav = new NavOptions { MaxClimb = ZoneClimb }; nav.JumpSpellClimb = 2 * nav.MaxClimb; abil = new AbilityOptions(); from = "the default options"; }
        }

        /// <summary>A solve over time set up on the UI thread (options, the zoning's copy, the levels as they are now), to run on any thread.</summary>
        sealed class ZoningSolve
        {
            public GameSession Session;
            public Func<int, LevelModel> Level;
            public NavOptions Nav;
            public AbilityOptions Abil;
            public string From, Split = "";
            public ZoneSet Zoning;
            public bool SplitByLevel;
        }

        ZoningSolve PrepareSolve(ZoningState z)
        {
            var p = new ZoningSolve { Session = z.Session, SplitByLevel = z.SplitByLevel };
            ZoningOptions(out p.Nav, out p.Abil, out p.From);
            // a copy of the levels with their unsaved edits, so that editing on while the solve runs changes nothing under it
            var edits = vm.Edits;
            if (edits != null) { var snap = KeyShuffle.Snapshot(z.Session, edits); p.Level = snap.Level; }
            else { var s = z.Session; p.Level = l => s.LoadLevel(l); }
            p.Zoning = z.HasZones ? z.ForSolver(out p.Split) : null;
            return p;
        }

        static string RunSolve(ZoningSolve p, out TimelineResult result)
        {
            result = Timeline.Build(p.Session, p.Level, p.Nav, p.Abil, null, null, true, p.Zoning);
            var report = new System.Text.StringBuilder();
            report.AppendLine(p.Zoning != null
                ? "The solve over time on this zoning (unsaved changes included), as Apply uses it, with " + p.From + "." + (p.SplitByLevel ? " Split by level: " + p.Split + "." : "")
                : "This zoning has no zones yet: the solve over time on automatic zones, with " + p.From + ".");
            report.AppendLine();
            report.Append(Timeline.Report(result));
            return report.ToString();
        }

        /// <summary>
        /// The solve over time on a zoning (Check the logic, Check if solvable), as Apply uses it:
        /// its copy for the solver (split by level when that option is on), on the levels with
        /// their unsaved edits, with the main option sets too, and the options of ZoningOptions. A
        /// zoning with nothing painted is solved on automatic zones. Returns the report.
        /// </summary>
        public string SolveZoning(ZoningState z, out TimelineResult result) { return RunSolve(PrepareSolve(z), out result); }

        /// <summary>SolveZoning with the solve on another thread, so the editor keeps drawing and answering meanwhile.</summary>
        public async System.Threading.Tasks.Task<Tuple<string, TimelineResult>> SolveZoningAsync(ZoningState z)
        {
            var p = PrepareSolve(z);
            return await System.Threading.Tasks.Task.Run(() => { TimelineResult r; string text = RunSolve(p, out r); return Tuple.Create(text, r); });
        }

        /// <summary>The start square the zoning tools use: as the solver takes it with the options in use (NavOptions.StartOf: UW.EXE's new-game square, or the game's own with ShippedStart).</summary>
        public StartPoint ZoningStart(ZoningState z)
        {
            NavOptions nav; AbilityOptions abil; string from;
            ZoningOptions(out nav, out abil, out from);
            return nav.StartOf(z.Session);
        }

        /// <summary>The zone of the start square (ZoningStart), else the zoning's start zone.</summary>
        public int ZoningStartZone(ZoningState z)
        {
            var st = ZoningStart(z);
            if (st != null) { int v = z.Set.ZoneAt(st.Level, (int)st.X, (int)st.Y); if (v > ZoneSet.Unassigned) return v; }
            return z.Set.StartZone;
        }

        // the squares that end the game, found again only when a level changed (ZoningState.LevelsVersion)
        ZoningState endsOf;
        int endsVersion = -1;
        List<EndTrigger> ends = new List<EndTrigger>();

        /// <summary>The squares that end the game in the levels as they are now (UW1), or none. Kept until a level changes.</summary>
        public List<EndTrigger> ZoningEnds(ZoningState z)
        {
            if (ReferenceEquals(endsOf, z) && endsVersion == z.LevelsVersion) return ends;
            var edits = vm.Edits;
            try { ends = EndGame.Find(z.Session, l => edits != null ? edits.Model(l) : z.Session.LoadLevel(l)); }
            catch (Exception) { ends = new List<EndTrigger>(); }
            endsOf = z; endsVersion = z.LevelsVersion;
            return ends;
        }

        // the shipped goal fact (level 9's arrival), read once
        static Fact shippedGoal;
        static bool shippedGoalRead;

        /// <summary>The zones of the goal: the zone the goal fact puts the player in (level 9's arrival), and those of the squares that end the game.</summary>
        public HashSet<int> ZoningGoalZones(ZoningState z)
        {
            var set = new HashSet<int>();
            if (z.Session.IsUw2) return set;
            try
            {
                if (!shippedGoalRead) { shippedGoalRead = true; shippedGoal = FactSet.Shipped(false).OfKind("goal").FirstOrDefault(x => x.Option.Length == 0); }
                var f = shippedGoal;
                if (f != null && f.To != null && f.To.HasTile) set.Add(z.Set.ZoneAt(f.To.Level, f.To.X, f.To.Y));
            }
            catch (Exception) { }
            foreach (var e in ZoningEnds(z)) set.Add(z.Set.ZoneAt(e.Level, e.X, e.Y));
            set.RemoveWhere(v => v <= ZoneSet.Unassigned);
            return set;
        }

        /// <summary>One line on a solve: solvable or not with its options, by what, and how many of the main option sets reach the goal.</summary>
        public static string Verdict(TimelineResult r)
        {
            var sv = r.Solve;
            string main = sv == null ? "The solve failed." : sv.GoalReached ? "Solvable with these options (the goal reached by " + sv.GoalBy + ")." : "NOT solvable with these options: missing " + string.Join(", ", sv.GoalMissing.Distinct().Take(6)) + ".";
            if (r.Variants.Count == 0) return main;
            var fail = r.Variants.Where(v => v.Value == null || !v.Value.GoalReached).Select(v => v.Key).ToList();
            return main + string.Format(" Of the {0} main option sets, {1} reach the goal{2}.", r.Variants.Count, r.Variants.Count - fail.Count, fail.Count > 0 ? " (not: " + string.Join("; ", fail) + ")" : "");
        }

        ZoningState zoning;
        ZonesWindow zonesWindow;
        ZoneGraphWindow zoneGraphWindow;
        List<float> zoneOverlay;
        int zoneOverlayLevel = -1;

        /// <summary>The zoning of the open game (read from DATA's zone file, or a new one).</summary>
        public ZoningState Zoning()
        {
            if (vm.Session == null) return null;
            if (zoning == null || zoning.Session != vm.Session)
            {
                zoning = new ZoningState(vm.Session, l => vm.Edits != null ? vm.Edits.Model(l) : vm.Session.LoadLevel(l));
                zoning.MaxClimb = settings.ZoneClimb;
                zoning.SplitByLevel = settings.ZoneSplitByLevel;
                zoning.Changed += ZonesChanged;
                if (vm.Edits != null) vm.Edits.Changed += e => zoning?.LevelsChanged();
            }
            return zoning;
        }

        void OnZones(object sender, RoutedEventArgs e) { ShowZones(); }
        void OnZoneGraph(object sender, RoutedEventArgs e) { ShowZoneGraph(); }
        void OnItemShuffle(object sender, RoutedEventArgs e) { ShowItemShuffle(); }
        void OnItemSwap(object sender, RoutedEventArgs e) { ShowItemSwap(); }
        void OnMonsterSwap(object sender, RoutedEventArgs e) { ShowMonsterSwap(); }
        void OnAutomap(object sender, RoutedEventArgs e) { ShowAutomap(); }

        AutomapWindow automapWindow;

        public AutomapWindow ShowAutomap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (automapWindow != null) { automapWindow.Activate(); return automapWindow; }
            automapWindow = new AutomapWindow(this);
            automapWindow.Closed += (s, e) => automapWindow = null;
            automapWindow.Show(this);
            return automapWindow;
        }

        MonsterSwapWindow monsterSwapWindow;

        public MonsterSwapWindow ShowMonsterSwap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (monsterSwapWindow != null) { monsterSwapWindow.Activate(); return monsterSwapWindow; }
            monsterSwapWindow = new MonsterSwapWindow(this, vm.Session);
            monsterSwapWindow.Closed += (s, e) => monsterSwapWindow = null;
            monsterSwapWindow.Show(this);
            return monsterSwapWindow;
        }

        ItemSwapWindow itemSwapWindow;

        public ItemSwapWindow ShowItemSwap()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (itemSwapWindow != null) { itemSwapWindow.Activate(); return itemSwapWindow; }
            itemSwapWindow = new ItemSwapWindow(this, vm.Session);
            itemSwapWindow.Closed += (s, e) => itemSwapWindow = null;
            itemSwapWindow.Show(this);
            return itemSwapWindow;
        }

        ItemShuffleWindow itemShuffleWindow;

        public ItemShuffleWindow ShowItemShuffle()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (itemShuffleWindow != null) { itemShuffleWindow.Activate(); return itemShuffleWindow; }
            itemShuffleWindow = new ItemShuffleWindow(this, vm.Session);
            itemShuffleWindow.Closed += (s, e) => itemShuffleWindow = null;
            itemShuffleWindow.Show(this);
            return itemShuffleWindow;
        }

        /// <summary>The item shuffle changed objects on many levels: the object list and the edit state are read again.</summary>
        public void ShuffleApplied() { RandomizerApplied("Item properties shuffled; Edit ▸ Undo (per level) or the shuffle window's Undo shuffle puts them back."); }

        /// <summary>A randomizer tool changed or restored levels: the zones follow, and the status line says so.</summary>
        public void RandomizerApplied(string status)
        {
            zoning?.LevelsChanged();
            vm.EditStatus = status;
        }

        public ZonesWindow ShowZones()
        {
            var z = Zoning();
            if (z == null) return null;
            if (zonesWindow != null) { zonesWindow.Activate(); return zonesWindow; }
            zonesWindow = new ZonesWindow(this, z);
            zonesWindow.Closed += (s, e) => { zonesWindow = null; ZonesChanged("tiles"); };
            zonesWindow.Show(this);
            ZonesChanged("tiles");
            return zonesWindow;
        }

        public ZoneGraphWindow ShowZoneGraph()
        {
            var z = Zoning();
            if (z == null) return null;
            if (zoneGraphWindow != null) { zoneGraphWindow.Activate(); return zoneGraphWindow; }
            zoneGraphWindow = new ZoneGraphWindow(this, z);
            zoneGraphWindow.Closed += (s, e) => zoneGraphWindow = null;
            zoneGraphWindow.Show(this);
            return zoneGraphWindow;
        }

        /// <summary>The zones window turned zone painting on or off.</summary>
        public void ZonePaintingChanged()
        {
            if (zoning != null && zoning.Painting)
            {
                vm.ToolIndex = 2;
                vm.EditStatus = "Painting zone " + zoning.CurrentZone + ": drag over tiles" + (zoning.FillMode ? " (a click fills the area)" : "") + ". Rock is never painted.";
            }
            else if (vm.ToolIndex == 2) vm.EditStatus = "";
        }

        /// <summary>Paints or fills a tile with the current zone when zone painting is on; false otherwise.</summary>
        bool ZonePaint(int x, int y, bool start)
        {
            if (zoning == null || !zoning.Painting || zonesWindow == null) return false;
            int level = vm.Level.Level;
            if (zoning.FillMode)
            {
                if (!start) return true;
                int n = zoning.FillArea(level, x, y, zoning.CurrentZone);
                vm.EditStatus = n == 0 ? "Nothing to fill there." : string.Format("{0} tiles filled as zone {1}.", n, zoning.CurrentZone);
                return true;
            }
            zoning.PaintTile(level, x, y, start);
            return true;
        }

        /// <summary>The zoning changed: the overlay is drawn again.</summary>
        public void ZonesChanged(string what)
        {
            if (what == "tiles" || what == "file" || what == "current" || what == "zones") zoneOverlay = null;
            UpdateMarkers();
        }

        /// <summary>Each tile's zone colour as a sheet over its floor (unassigned tiles magenta), while the zones window is open.</summary>
        void AddZoneOverlay(List<float> tris)
        {
            var level = vm.Level;
            if (zoning == null || zonesWindow == null || !zoning.ShowOverlay || level == null || level.Level > zoning.Set.Levels) return;
            if (zoneOverlay == null || zoneOverlayLevel != level.Level)
            {
                zoneOverlay = new List<float>();
                zoneOverlayLevel = level.Level;
                var t = zoning.Set.Tiles[level.Level - 1];
                if (t != null)
                    for (int y = 0; y < 64; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            int id = t[y * 64 + x];
                            if (id == ZoneSet.Solid || level.Tiles[x, y].IsSolid) continue;
                            uint c = zoning.Set.ColorOf(id);
                            float alpha = id == ZoneSet.Unassigned ? 0.6f : id == zoning.CurrentZone ? 0.55f : 0.38f;
                            var col = new Vector4((c >> 16 & 0xFF) / 255f, (c >> 8 & 0xFF) / 255f, (c & 0xFF) / 255f, alpha);
                            const float up = 0.025f;
                            Markers.TileSheet(zoneOverlay, x, y,
                                (float)level.FloorAt(x, y, 0, 0) + up, (float)level.FloorAt(x, y, 1, 0) + up,
                                (float)level.FloorAt(x, y, 0, 1) + up, (float)level.FloorAt(x, y, 1, 1) + up, col);
                        }
            }
            tris.AddRange(zoneOverlay);
        }

        /// <summary>Opens a level and selects a tile, looking at it.</summary>
        public void GoToTile(int level, int x, int y)
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != level) vm.LoadLevel(level);
            if (vm.Level == null) return;
            vm.Select(-1, x, y, true);
            Frame();
            Activate();
        }
    }
}
