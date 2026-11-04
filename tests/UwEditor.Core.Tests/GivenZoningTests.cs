using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// The solve over time (Phase C) on a zoning it is given (the Zones window's): each region takes
    /// the zone that holds most of its tiles, before and after the world changes; the zoning's own
    /// links are added to those the levels give; the key item shuffle uses it (UW1).
    /// </summary>
    public class GivenZoningTests
    {
        static readonly object gate = new object();
        static GameSession session;
        static TimelineResult auto;

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW");
                if (g == null) return null;
                return session = GameSession.Open(g);
            }
        }

        static TimelineResult Auto()
        {
            lock (gate)
            {
                if (auto != null) return auto;
                var s = Session();
                return auto = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false);
            }
        }

        static ZoneSet Painted(GameSession s)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimaUnderworldEditorAvalonia.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return ZoneSet.FromJson(File.ReadAllText(Path.Combine(dir.FullName, "docs", "examples", "uw1-zones.json")), s.Profile.LevelSlots);
        }

        [Fact]
        public void An_automatic_zoning_given_back_solves_exactly_as_the_automatic_zones()
        {
            var s = Session(); if (s == null) return;
            var a = Auto();
            var copy = ZoneSet.FromJson(a.Set.ToJson(), s.Profile.LevelSlots);    // as the Zones window holds it
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false, copy);
            Assert.Same(copy, t.Abilities.Auto.Given);
            Assert.True(t.Solve.GoalReached);
            Assert.Equal(a.Solve.ZoneSphere.Keys.OrderBy(x => x), t.Solve.ZoneSphere.Keys.OrderBy(x => x));
            Assert.Equal(a.Solve.Spheres.Count, t.Solve.Spheres.Count);
            Assert.Equal(a.Solve.Applied.OrderBy(x => x), t.Solve.Applied.OrderBy(x => x));
            Assert.Equal(0, t.Abilities.Auto.FreshZones);
            // its links are those the levels give, so none is added as drawn by hand, and no warnings about the zoning
            Assert.DoesNotContain(t.Set.Links, k => k.Hand);
            Assert.DoesNotContain(t.Abilities.Auto.Warnings, w => w.Contains("zoning"));
            // every zone keeps its id and name
            foreach (var z in a.Set.Zones.Where(z => z.Id > ZoneSet.Unassigned)) Assert.Equal(z.Name, t.Set.GetZone(z.Id)?.Name);
        }

        [Fact]
        public void A_painted_zoning_keeps_its_zones_names_and_extra_links_and_is_solved_over_time()
        {
            var s = Session(); if (s == null) return;
            var painted = Painted(s);
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false, painted);
            var zs = t.Set;
            // the start square lies in the painted start zone 53, which keeps its name
            Assert.Equal(53, zs.StartZone);
            Assert.Equal(painted.GetZone(53).Name, zs.GetZone(53).Name);
            Assert.Equal(painted.GetZone(53).GraphX, zs.GetZone(53).GraphX);
            // the tiles of a region painted all in one zone stay in it
            Assert.Equal(painted.ZoneAt(1, 32, 2), zs.ZoneAt(1, 32, 2));
            // links drawn by hand that the levels do not give hold in every state of the world
            Assert.Contains(zs.Links, k => k.Hand && k.Notes.StartsWith("drawn by hand"));
            // the zones whose parts have no free way between them are named (the hub, zone 2)
            Assert.Contains(t.Abilities.Auto.Warnings, w => w.Contains("no free way between them") && w.Contains("2 Main areas"));
            // open areas the painting leaves out get zones of their own, never a painted id
            Assert.True(t.Abilities.Auto.FreshZones > 0);
            var made = zs.Zones.Where(z => z.Id > ZoneSet.Unassigned && painted.GetZone(z.Id) == null).Select(z => z.Id).ToList();
            Assert.Equal(t.Abilities.Auto.FreshZones, made.Count);
            // the world changes over time, and the goal (level 9) is reached
            Assert.True(t.Solve.Applied.Count > 20, t.Solve.Applied.Count + " changes");
            Assert.True(t.Solve.GoalReached, Timeline.Report(t));
            // after a change a level keeps the painted zones: no part split off as a new zone (ids from 1000) where the painting covers it
            Assert.DoesNotContain(t.Solve.NewZones.Values, v => v.StartsWith("part of"));
        }

        [Fact]
        public void Splitting_by_level_makes_a_zone_a_level_joined_by_free_two_way_links()
        {
            var s = Session(); if (s == null) return;
            var painted = Painted(s);
            string note;
            var split = painted.SplitByLevel(out note);
            var multi = painted.Zones.Where(z => z.Id > ZoneSet.Unassigned && painted.LevelsOf(z.Id).Count > 1).ToList();
            int more = multi.Sum(z => painted.LevelsOf(z.Id).Count - 1);
            Assert.Contains(multi.Count + " zone(s) on several levels split into " + more + " more", note);
            // the hub, zone 2, lies on levels 1-7: its first level keeps the id, each other level gets a zone of its own
            Assert.Equal(7, painted.LevelsOf(2).Count);                  // the zoning given is not changed
            Assert.Equal(new[] { 1 }, split.LevelsOf(2));
            Assert.All(split.Zones.Where(z => z.Id > ZoneSet.Unassigned), z => Assert.True(split.LevelsOf(z.Id).Count <= 1, z.Id + " " + z.Name));
            Assert.EndsWith("(L1)", split.GetZone(2).Name);
            int l3 = split.ZoneAt(3, Enumerable.Range(0, 4096).First(t => painted.ZoneAt(3, t % 64, t / 64) == 2) % 64, Enumerable.Range(0, 4096).First(t => painted.ZoneAt(3, t % 64, t / 64) == 2) / 64);
            Assert.NotEqual(2, l3);
            Assert.Equal(painted.GetZone(2).Name + " (L3)", split.GetZone(l3).Name);
            Assert.Equal(split.ColorOf(2), split.ColorOf(l3));
            // the parts are joined in level order by free two-way links
            var added = split.Links.Where(k => k.Notes.Contains("split by level")).ToList();
            Assert.Equal(more, added.Count);
            Assert.All(added, k => Assert.True(k.TwoWay && k.Requires == "free" && k.Kind == "stairs"));
            Assert.Equal(painted.Links.Count + more, split.Links.Count);
            // solved over time: still beatable, and what the report flags now lies within a level
            var t = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), null, null, false, split);
            Assert.True(t.Solve.GoalReached);
            var parts = t.Abilities.Auto.Warnings.First(w => w.Contains("no free way between them"));
            Assert.DoesNotContain("2 Main areas (levels 1-6) (1", parts);   // no longer one zone of many parts across the levels
            Assert.Contains("(L1) (", parts);
        }

        [Fact]
        public void The_key_item_shuffle_uses_the_zoning_it_is_given()
        {
            var s = Session(); if (s == null) return;
            var painted = Painted(s);
            var r = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 5, Zoning = painted, ZoningFrom = "Karl's painted UW1 map", Names = painted, NamesFrom = "Karl's painted UW1 map" });
            Assert.True(r.Success, r.Refused + "\n" + string.Join("\n", r.Failures));
            string log = r.Log.ToString();
            Assert.Contains("Zones: Karl's painted UW1 map (the Zones window's)", log);
            Assert.DoesNotContain("is the automatic zone", log);
            // without a zoning, the automatic zones as before
            var plain = KeyShuffle.Run(s, null, new KeyShuffleOptions { Seed = 5 });
            Assert.Contains("Zones: automatic", plain.Log.ToString());
        }
    }
}
