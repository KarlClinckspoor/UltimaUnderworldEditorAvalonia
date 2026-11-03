using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The automatic zones (Phase A) on the shipped UW1 levels.</summary>
    public class AutoZonesTests
    {
        static string Example
        {
            get
            {
                string here = AppContext.BaseDirectory;
                for (int i = 0; i < 8 && here != null; i++, here = Path.GetDirectoryName(here))
                {
                    string p = Path.Combine(here, "docs", "examples", "uw1-zones.json");
                    if (File.Exists(p)) return p;
                }
                return null;
            }
        }

        static readonly object gate = new object();
        static AutoZoneResult cached;
        static GameSession session;
        static ZoneSet painted;

        static AutoZoneResult Uw1()
        {
            lock (gate)
            {
                if (cached != null) return cached;
                string g = GameData.Game("UW");
                if (g == null) return null;
                session = GameSession.Open(g);
                painted = Example == null ? null : ZoneSet.Load(Example, 9);
                cached = AutoZones.Build(session, l => session.LoadLevel(l), new NavOptions(), painted);
                return cached;
            }
        }

        [Fact]
        public void Regions_cover_every_open_tile_once_and_doors_are_nodes()
        {
            var res = Uw1(); if (res == null) return;
            var g = res.Graph;
            for (int l = 1; l <= 9; l++)
            {
                var m = session.LoadLevel(l);
                var count = new int[64 * 64];
                foreach (var r in g.Regions.Where(r => r.Level == l)) foreach (int t in r.Tiles) count[t]++;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        Assert.Equal(m.Tiles[x, y].IsSolid ? 0 : 1, count[y * 64 + x]);
                        if (!m.Tiles[x, y].IsSolid) Assert.Equal(g.RegionOf(l, x, y), g.Regions.First(r => r.Level == l && r.Tiles.Contains(y * 64 + x)).Id);
                    }
            }
            var doors = g.Regions.Where(r => r.IsDoor).ToList();
            Assert.True(doors.Count > 100);
            Assert.All(doors, d => Assert.Single(d.Tiles));
            Assert.Contains(g.Regions, r => r.Lava);
            Assert.Contains(g.Regions, r => r.WaterTiles > 0);
        }

        [Fact]
        public void Known_facts_of_level_1_hold()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            Assert.Contains(zs.Placements, p => p.Zone == zs.StartZone && p.Grants == "key:1");
            // the skeleton room / pillar room door: key 2, picked at 0, breakable
            Assert.Contains(zs.Links, k => k.Level == 1 && k.Kind == "door" && k.Requires == "key:2 | pick:0 | bash");
            // the stairs between levels 1 and 2 go both ways
            Assert.Contains(zs.Links, k => k.Kind == "stairs" && k.TwoWay && zs.LevelsOf(k.From).Contains(1) && zs.LevelsOf(k.To).Contains(2));
        }

        [Fact]
        public void Move_trigger_622_on_level_5_is_a_one_way_teleport()
        {
            var res = Uw1(); if (res == null) return;
            var g = res.Graph;
            var e = Assert.Single(g.Edges, x => x.Level == 5 && x.Trigger == 622);
            Assert.Equal("teleport", e.Kind);
            Assert.Equal(g.RegionOf(5, 11, 6), e.To);
            Assert.DoesNotContain(g.Edges, x => x.From == e.To && x.To == e.From && x.Requires == "free");
            var k = Assert.Single(res.Set.Links, x => x.Kind == "teleport" && x.Notes.Contains("#622"));
            Assert.False(k.TwoWay);
        }

        [Fact]
        public void Levers_and_mechanisms_are_found()
        {
            var res = Uw1(); if (res == null) return;
            var g = res.Graph;
            Assert.True(g.Levers.Count > 20);
            var lever = Assert.Single(g.Levers, l => l.Level == 3 && l.Index == 972);
            Assert.False(lever.OutOfReach);
            Assert.Contains("terrain", lever.What);
            // lever 972's chain drains the pool and opens (46, 7) into the sword room
            var m = Assert.Single(g.Mechanisms, x => x.Flag == "flag:terrain_L3_971");
            Assert.Contains("(46, 7) becomes open, height 4", m.What);
            Assert.NotEqual("", m.SetOff);
            // level 7: walking past (20, 42) with the crystal splinter opens the rock at (20, 41)
            var crystal = Assert.Single(g.Mechanisms, x => x.Level == 7 && x.Kind == "terrain" && x.Conditions.Contains("item:crystal"));
            Assert.Equal(20, crystal.X);
            Assert.Equal(41, crystal.Y);
            // the level 4 lever puzzle: levers above the hand's reach
            Assert.Contains(g.Levers, l => l.Level == 4 && l.Index == 526 && l.OutOfReach && l.PoleReach.Count > 0);
            Assert.DoesNotContain(g.Mechanisms, x => x.Kind == "create" && Editing.Triggers.IsSpawner(session.LoadLevel(x.Level), session.LoadLevel(x.Level).Objects[x.Trap]));
        }

        [Fact]
        public void The_set_saves_loads_compares_and_solves()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            Assert.InRange(zs.Zones.Count(z => z.Id > ZoneSet.Unassigned), 50, 254);
            Assert.All(zs.Links, k => Assert.NotNull(Requirement.TryParse(k.Requires, out _)));
            string dir = Path.Combine(Path.GetTempPath(), "uwauto-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string p = Path.Combine(dir, AutoZones.DefaultFileName(false));
                zs.Save(p);
                var back = ZoneSet.Load(p, 9);
                Assert.Equal(zs.ToJson(), back.ToJson());
                Assert.Equal("uw1-zones-auto.json", Path.GetFileName(p));
            }
            finally { Directory.Delete(dir, true); }
            Assert.NotNull(res.Solve);
            Assert.True(res.Solve.Spheres.Count > 3);
            Assert.True(res.SolveWithLava.ZoneSphere.Count > res.Solve.ZoneSphere.Count);
            if (painted != null)
            {
                if (painted.TileCount(painted.GoalZone) > 0) Assert.Equal(painted.GoalZone, AutoZones.Dominant(res, zs.GoalZone));
                else Assert.Equal(new[] { 9 }, zs.LevelsOf(zs.GoalZone).ToArray());     // zone 87 is not painted: the void's zone
                string report = AutoZones.Report(res, painted, session);
                Assert.Contains("COMPARISON WITH THE PAINTED MAP", report);
                Assert.Contains("LEVERS OUT OF REACH", report);
            }
        }

        [Fact]
        public void Options_change_the_graph()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var noJumps = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions { GapJumps = false }, new[] { 1 });
            Assert.Equal(0, noJumps.EdgeCount("jump"));
            var jumps = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), new[] { 1 });
            Assert.True(jumps.EdgeCount("jump") > 0);
            Assert.Equal(noJumps.Regions.Count, jumps.Regions.Count);
            var flat = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions { MaxClimb = 15 }, new[] { 1 });
            Assert.True(flat.Regions.Count < jumps.Regions.Count);
            Assert.Equal(0, flat.EdgeCount("climb"));
            // cut at level changes: off joins the main areas of levels 1 and 2 into one zone
            var cut = AutoZones.FromGraph(s, l => s.LoadLevel(l), NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions { CutAtLevelChanges = false }), null);
            Assert.Contains(cut.Set.Zones, z => cut.Set.LevelsOf(z.Id).Count > 1);
        }

        [Fact]
        public void Falls_never_kill_a_fresh_character()
        {
            Assert.Equal(0, NavGraph.FallDamage(2));
            Assert.Equal(0, NavGraph.FallDamage(3));
            Assert.InRange(NavGraph.FallDamage(15), 1, 29);
            for (int h = 1; h < 15; h++) Assert.True(NavGraph.FallDamage(h) <= NavGraph.FallDamage(h + 1));
        }
    }
}
