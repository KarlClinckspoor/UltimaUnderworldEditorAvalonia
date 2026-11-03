using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The automatic zones, Phase C (C1): Karl's corrections, the world changing over time, the return check, the new fact kinds and the options as tokens, on the shipped UW1 levels.</summary>
    public class TimelineTests
    {
        static string Docs(string rel)
        {
            string here = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && here != null; i++, here = Path.GetDirectoryName(here))
            {
                string p = Path.Combine(here, "docs", rel);
                if (File.Exists(p) || Directory.Exists(p)) return p;
            }
            return null;
        }

        static readonly object gate = new object();
        static TimelineResult cached;
        static GameSession session;

        static GameSession Session()
        {
            lock (gate)
            {
                if (session != null) return session;
                string g = GameData.Game("UW");
                if (g == null) return null;
                session = GameSession.Open(g);
                return session;
            }
        }

        /// <summary>The default build with the shipped facts (every docs/facts/uw1-*.json).</summary>
        static TimelineResult Uw1()
        {
            lock (gate)
            {
                if (cached != null) return cached;
                var s = Session();
                if (s == null) return null;
                string ex = Docs(Path.Combine("examples", "uw1-zones.json"));
                cached = Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), new AbilityOptions(), ex == null ? null : ZoneSet.Load(ex, 9));
                return cached;
            }
        }

        /// <summary>A build with only uw1-facts.json plus the facts given (so that the other files cannot change what is tested).</summary>
        static TimelineResult With(string extraJson, AbilityOptions opt = null)
        {
            var s = Session();
            var facts = new FactSet();
            var mine = FactSet.Shipped(false);
            var own = new FactSet();
            own.Facts.AddRange(mine.Facts.Where(f => f.File == "uw1-facts.json"));
            own.Files.Add("uw1-facts.json");
            facts.Add(own);
            if (extraJson != null)
            {
                string path = Path.Combine(Path.GetTempPath(), "uw1-test-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path, extraJson);
                try { facts.Add(FactSet.Load(path)); } finally { File.Delete(path); }
            }
            return Timeline.Build(s, l => s.LoadLevel(l), new NavOptions(), opt ?? new AbilityOptions(), null, facts, variants: false);
        }

        static int ZoneAt(TimelineResult r, int level, int x, int y) { return r.Set.ZoneAt(level, x, y); }

        // ---------------------------------------------------------------- the facts format

        [Fact]
        public void Every_facts_file_loads()
        {
            var shipped = FactSet.Shipped(false);
            Assert.Equal("uw1-facts.json", shipped.Files[0]);
            Assert.Empty(shipped.Check());
            string dir = Docs("facts");
            if (dir == null) return;
            var files = Directory.GetFiles(dir, "uw1-*.json").Select(Path.GetFileName).OrderBy(n => n).ToList();
            Assert.Equal(files, shipped.Files.OrderBy(n => n).ToList());
            Assert.Equal(files.Sum(f => FactSet.Load(Path.Combine(dir, f)).Facts.Count), shipped.Facts.Count);
            Assert.All(shipped.Facts, f => Assert.False(string.IsNullOrEmpty(f.Source), f.Id));
            // Phase C's facts of uw1-facts.json
            Assert.Contains(shipped.Facts, f => f.Id == "ring_of_levitation" && f.Kind == "link" && f.Option == "ring_needs_lava" && f.Otherwise == "free" && f.To.Object == 683);
            Assert.Contains(shipped.Facts, f => f.Id == "l4_lever_puzzle" && f.Kind == "free" && f.Objects.SequenceEqual(new[] { 526, 811, 853, 799 }));
            Assert.Contains(shipped.Facts, f => f.Id == "l5_levers" && f.Kind == "free" && f.Objects.Contains(667) && f.Objects.Contains(663));
            Assert.Contains(shipped.Facts, f => f.Id == "slasher_chamber_door" && f.Kind == "needs" && f.Level == 8 && f.Trigger == 539);
        }

        const string Kinds = @"{ ""format"": ""uw1-facts"", ""version"": 1, ""game"": ""UW1"", ""facts"": [
  { ""id"": ""a_link"", ""kind"": ""link"", ""from"": { ""level"": 1, ""x"": 10, ""y"": 20 }, ""to"": [2, 9, 35], ""requires"": ""key:1"", ""twoWay"": true, ""replace"": true, ""option"": ""option:kill_npcs"", ""otherwise"": ""free"", ""linkKind"": ""door"", ""source"": ""test"" },
  { ""id"": ""a_warp"", ""kind"": ""warp"", ""from"": ""any"", ""to"": { ""level"": 2, ""object"": 868 }, ""requires"": ""spell:gate_travel"", ""source"": ""test"" },
  { ""id"": ""a_level_warp"", ""kind"": ""warp"", ""fromLevel"": 7, ""to"": { ""level"": 1, ""x"": 1, ""y"": 2 }, ""requires"": ""free"", ""source"": ""test"" },
  { ""id"": ""sapling"", ""kind"": ""warp"", ""to"": ""planted"", ""requires"": ""item:silver_seed"", ""source"": ""test"" },
  { ""id"": ""a_goal"", ""kind"": ""goal"", ""level"": 9, ""x"": 52, ""y"": 51, ""trigger"": 1022, ""requires"": ""talismans:8"", ""source"": ""test"" },
  { ""id"": ""a_free"", ""kind"": ""free"", ""level"": 4, ""trigger"": 747, ""token"": ""flag:var27_is_38"", ""source"": ""test"" },
  { ""id"": ""a_free_flag"", ""kind"": ""free"", ""level"": 4, ""object"": 747, ""flags"": [""var27_is_38""], ""source"": ""test"" },
  { ""id"": ""a_needs"", ""kind"": ""needs"", ""token"": ""flag:terrain_L8_539"", ""requires"": ""item:key_of_infinity"", ""source"": ""test"" },
  { ""id"": ""bad_link"", ""kind"": ""link"", ""from"": { ""level"": 1 }, ""to"": { ""level"": 1, ""x"": 3, ""y"": 3 }, ""requires"": ""key:1 &"", ""option"": ""flying_pigs"" },
  { ""id"": ""bad_free"", ""kind"": ""free"", ""level"": 4 },
  { ""id"": ""bad_goal"", ""kind"": ""goal"", ""requires"": ""free"" }
] }";

        [Fact]
        public void The_new_fact_kinds_are_read()
        {
            var set = FactSet.FromJson(Kinds, "uw1-test.json");
            var link = set.Facts.Single(f => f.Id == "a_link");
            Assert.Equal(1, link.From.Level); Assert.Equal(10, link.From.X); Assert.Equal(20, link.From.Y);
            Assert.Equal(2, link.To.Level); Assert.Equal(9, link.To.X); Assert.Equal(35, link.To.Y);
            Assert.True(link.TwoWay); Assert.True(link.Replace);
            Assert.Equal("kill_npcs", link.Option);
            Assert.Equal("free", link.Otherwise);
            Assert.Equal("door", link.LinkKind);
            var warp = set.Facts.Single(f => f.Id == "a_warp");
            Assert.Equal(0, warp.FromLevel);
            Assert.True(warp.To.HasObject);
            Assert.Equal(7, set.Facts.Single(f => f.Id == "a_level_warp").FromLevel);
            Assert.True(set.Facts.Single(f => f.Id == "sapling").To.IsPlanted);
            var goal = set.Facts.Single(f => f.Id == "a_goal");
            Assert.True(goal.To.HasTile);
            Assert.Equal(52, goal.To.X);
            var free = set.Facts.Single(f => f.Id == "a_free");
            Assert.Equal(new[] { 747 }, free.Triggers);
            Assert.Empty(free.Flags);           // token beside a trigger only names the flag
            Assert.Equal(new[] { "flag:var27_is_38" }, set.Facts.Single(f => f.Id == "a_free_flag").Flags);
            Assert.Equal("item:key_of_infinity", set.MechanismNeeds(8, 539, "flag:terrain_L8_539"));
            Assert.Equal("free", set.MechanismNeeds(8, 540, "flag:terrain_L8_540"));
            var problems = set.Check();
            Assert.Contains(problems, p => p.StartsWith("bad_link:") && p.Contains("'from'"));
            Assert.Contains(problems, p => p.StartsWith("bad_link:") && p.Contains("flying_pigs"));
            Assert.Contains(problems, p => p.StartsWith("bad_link:") && !p.Contains("'from'") && !p.Contains("flying_pigs"));    // the requirement
            Assert.Contains(problems, p => p.StartsWith("bad_free:"));
            Assert.Contains(problems, p => p.StartsWith("bad_goal:"));
            Assert.DoesNotContain(problems, p => p.StartsWith("a_"));
            Assert.DoesNotContain(problems, p => p.StartsWith("sapling"));
        }

        [Fact]
        public void The_options_are_tokens()
        {
            var o = new AbilityOptions();
            var t = o.Tokens();
            Assert.Contains("magic", t);
            Assert.Contains("ring_needs_lava", t);
            Assert.DoesNotContain("kill_npcs", t);
            Assert.DoesNotContain("glitches", t);
            var so = o.ToSolver();
            Assert.True(Requirement.Parse("option:magic & option:ring_needs_lava").Eval(new Ctx(so)));
            Assert.False(Requirement.Parse("option:kill_npcs").Eval(new Ctx(so)));
            Assert.True(Requirement.Parse("option:kill_npcs | npc:x").Eval(new Ctx(so, "npc:x")));
            o.KillNpcs = true; o.RingNeedsLava = false; o.Magic = false;
            so = o.ToSolver();
            Assert.True(Requirement.Parse("option:kill_npcs").Eval(new Ctx(so)));
            Assert.False(Requirement.Parse("option:ring_needs_lava").Eval(new Ctx(so)));
            // Karl's point 5: bashing is always in the logic, whatever the other options
            Assert.True(so.AllowBash);
            Assert.True(Requirement.Parse("bash").Eval(new Ctx(new AbilityOptions { LockPicking = false }.ToSolver())));
            Assert.All(FactSet.KnownOptions, n => Assert.Matches("^[a-z_]+$", n));
            // a zone file solved without the option set reads option: as off, and now_zone: as zone:
            Assert.False(Requirement.Parse("option:magic").Eval(new Ctx(new SolverOptions())));
        }

        sealed class Ctx : IRequirementContext
        {
            readonly HashSet<string> tokens;
            public Ctx(SolverOptions o, params string[] t) { Options = o; tokens = new HashSet<string>(t); }
            public SolverOptions Options { get; private set; }
            public bool HasToken(string t) { return tokens.Contains(t); }
            public bool ZoneReachable(int z) { return false; }
            public int TalismanCount() { return 0; }
        }

        // ---------------------------------------------------------------- Karl's corrections

        [Fact]
        public void The_ring_of_levitation_needs_lava_protection()
        {
            var r = Uw1(); if (r == null) return;
            int from = ZoneAt(r, 7, 59, 43), ring = ZoneAt(r, 7, 44, 44);
            Assert.NotEqual(from, ring);
            var link = Assert.Single(r.FactLinks, k => k.Notes == "facts: ring_of_levitation");
            Assert.Equal(from, link.From);
            Assert.Equal(ring, link.To);
            Assert.Equal("spell:lava", link.Requires);
            Assert.True(link.TwoWay);
            var sv = r.Solve;
            Assert.True(sv.ZoneSphere.ContainsKey(ring));
            int lava = new[] { "worn:lava", "perm:lava", "cast:lava" }.Where(sv.TokenSphere.ContainsKey).Select(t => sv.TokenSphere[t]).DefaultIfEmpty(99).Min();
            Assert.True(sv.ZoneSphere[ring] > lava);
            Assert.Contains(sv.Spheres[sv.ZoneSphere[ring]].Zones, z => z == ring);
            // off: the two zones are one (a free link both ways)
            var o = r.Options.Clone();
            o.RingNeedsLava = false;
            var off = Timeline.Solve(r, o, "ring off");
            Assert.True(off.ZoneSphere[ring] <= off.ZoneSphere[from] + 1);
            Assert.True(off.ZoneSphere[ring] <= sv.ZoneSphere[ring]);
        }

        [Fact]
        public void The_level_4_lever_puzzle_and_the_level_5_levers_are_free()
        {
            var r = Uw1(); if (r == null) return;
            var g = r.Abilities.Auto.Graph;
            foreach (int i in new[] { 526, 811, 853, 799 })
            {
                var lv = Assert.Single(g.Levers, l => l.Level == 4 && l.Index == i);
                Assert.True(lv.Free);
                Assert.DoesNotContain("pole", lv.Requires);
                Assert.DoesNotContain("spell:", lv.Requires);
            }
            var opens = Assert.Single(g.Mechanisms, m => m.Flag == "flag:terrain_L4_550");
            Assert.DoesNotContain("spell:", opens.SetOff);
            // the timeline applies the four changes and reaches the alcove after the puzzle without the pole or a spell
            var o = r.Options.Clone();
            o.Magic = false; o.SingleUseItems = false;
            var sv = Timeline.Solve(r, o, "no magic");
            foreach (var f in new[] { "flag:terrain_l4_702", "flag:terrain_l4_789", "flag:terrain_l4_809", "flag:terrain_l4_550" }) Assert.Contains(f, sv.Applied);
            int alcove = ZoneAt(r, 4, 57, 27);
            Assert.True(alcove > ZoneSet.Unassigned);
            Assert.True(sv.ZoneSphere.ContainsKey(alcove));
            Assert.False(r.Abilities.Solve.ZoneSphere.ContainsKey(alcove) && r.Abilities.Solve.ZoneSphere[alcove] < sv.ZoneSphere[alcove] - 10);
            // level 5: the Kneenibble portcullis needs no variable code, and lever #663 creates the Ring of Humility
            Assert.DoesNotContain(r.Set.Links, k => k.Level == 5 && k.Requires.Contains("flag:var31"));
            Assert.Contains("flag:create_l5_635", sv.Fired);
            Assert.Contains(r.Set.Placements, p => p.Grants == "talisman:ring" && p.Requires.Contains("flag:create_L5_635"));
            Assert.True(sv.TokenSphere.ContainsKey("talisman:ring"));
            Assert.True(sv.TokenSphere["talisman:ring"] >= sv.TokenSphere["flag:create_l5_635"]);
        }

        [Fact]
        public void Telekinesis_has_the_poles_uses()
        {
            var s = Session(); if (s == null) return;
            Assert.Equal("telekinesis", Abilities.OfSpell(false, 35));      // Ort Por Ylem (combat/SPELLS.C xt_spells 8)
            Assert.Equal(new[] { 14, 15, 23 }, Abilities.Uw1Runes(35));
            Assert.Null(Abilities.OfSpell(false, 23));                       // class 11 minor 3 is an object spell, not the effect
            Assert.Equal("telekinesis", Abilities.OfEffect(11, 3));
            Assert.Contains("telekinesis", Abilities.Names);
            Assert.Contains("spell:telekinesis", NavGraph.PoleOrLevitate);
            // without the facts' free puzzle, the first lever asks for the pole or Telekinesis
            var g = NavGraph.Build(s, l => s.LoadLevel(l), new NavOptions(), new[] { 4 });
            var lv = Assert.Single(g.Levers, l => l.Index == 526);
            Assert.Contains("item:pole | spell:telekinesis", lv.Requires);
            var r = Uw1(); if (r == null) return;
            Assert.Contains(r.Set.Placements, p => p.Grants == "cast:telekinesis" && p.Requires.Contains("rune:ort") && p.Requires.Contains("rune:ylem"));
        }

        [Fact]
        public void Bashing_is_free_and_only_picking_doors_are_left_to_open()
        {
            var r = Uw1(); if (r == null) return;
            Assert.Null(typeof(AbilityOptions).GetField("Bashing"));
            var doors = AbilityZones.DoorsToOpen(r.Set, new AbilityOptions { LockPicking = false });
            Assert.All(doors, d => Assert.Matches(@"pick:\d+", d.Link.Requires));
            Assert.DoesNotContain(r.Solve.Blocked, b => b.Missing.Contains("bash"));
        }

        [Fact]
        public void Levers_are_used_from_their_own_side_and_not_from_rock()
        {
            var r = Uw1(); if (r == null) return;
            var g = r.Abilities.Auto.Graph;
            // #972 lies in a rock square until the vines' look trigger #586 opens it
            var sword = Assert.Single(g.Levers, l => l.Level == 3 && l.Index == 972);
            Assert.Equal("flag:terrain_L3_586", sword.InRock);
            Assert.Contains("flag:terrain_L3_586", Assert.Single(g.Mechanisms, m => m.Flag == "flag:terrain_L3_971").SetOff);
            // Eb's button #840 behind the portcullis at (47, 51) is not used from outside it
            var eb = Assert.Single(g.Levers, l => l.Level == 1 && l.Index == 840);
            Assert.All(eb.Reach, rr => Assert.DoesNotContain(g.Regions[rr].Tiles, t => t % 64 < 47 && t / 64 == 51));
        }

        // ---------------------------------------------------------------- the world over time

        [Fact]
        public void Lever_972_refloods_level_3_into_the_sword_area()
        {
            var r = Uw1(); if (r == null) return;
            var sv = r.Solve;
            int area = ZoneAt(r, 3, 58, 9);
            Assert.True(area > ZoneSet.Unassigned);
            var change = Assert.Single(r.Changes, c => c.Flag == "flag:terrain_l3_971");
            Assert.True(change.Applied);
            Assert.Contains("opens", change.Effect);
            Assert.Contains("flag:terrain_l3_971", sv.Applied);
            // the static solve (Phase B, no changes) never gets there; the timeline does, after the change
            Assert.False(r.Abilities.Solve.ZoneSphere.ContainsKey(area));
            Assert.True(sv.ZoneSphere.ContainsKey(area));
            Assert.True(sv.ZoneSphere[area] > sv.TokenSphere["flag:terrain_l3_971"]);
            Assert.True(sv.TokenSphere["flag:terrain_l3_971"] >= sv.TokenSphere["flag:terrain_l3_586"]);
            Assert.True(sv.TokenSphere.ContainsKey("item:broken_blade"));
        }

        [Fact]
        public void The_crystal_splinter_opens_the_tombs_on_level_7()
        {
            var r = Uw1(); if (r == null) return;
            var sv = Timeline.Solve(r, r.Options, "with the crystal", new[] { "item:crystal" });
            Assert.Contains("flag:terrain_l7_887", sv.Applied);
            int tombs = ZoneAt(r, 7, 20, 40);
            Assert.True(tombs > ZoneSet.Unassigned);
            Assert.True(sv.ZoneSphere.ContainsKey(tombs));
            Assert.True(sv.ZoneSphere[tombs] > sv.TokenSphere["flag:terrain_l7_887"]);
            // without it the walk past (20, 42) does nothing
            var none = With(null);
            Assert.DoesNotContain("flag:terrain_l7_887", none.Solve.Fired);
            Assert.DoesNotContain(tombs, none.Solve.ZoneSphere.Keys);
        }

        [Fact]
        public void A_door_closed_behind_the_player_is_one_way()
        {
            var r = Uw1(); if (r == null) return;
            var cl = Assert.Single(r.Closers, c => c.Level == 7 && c.Door == 735);
            Assert.StartsWith("one way", cl.Treatment);
            Assert.Contains("key:23", cl.After);
            Assert.Equal(1, cl.EdgesChanged);
            var g = r.Abilities.Auto.Graph;
            int away = cl.Sides.Single(p => !p.Value).Key, towards = cl.Sides.Single(p => p.Value).Key;
            Assert.All(g.Edges.Where(e => e.From == cl.DoorRegion && e.To == away), e => Assert.Contains("key:23", e.Requires));
            Assert.Contains(g.Edges, e => e.From == cl.DoorRegion && e.To == towards && !e.Requires.Contains("key:23"));
            var change = Assert.Single(r.Changes, c => c.Flag == "flag:door_l7_656");
            Assert.False(change.Applied);
            Assert.Equal("closes", change.Effect);
            Assert.Contains("one way", change.Treatment);
            // a closer without a locked lock does nothing; every closer has a treatment
            Assert.All(r.Closers, c => Assert.False(string.IsNullOrEmpty(c.Treatment)));
            Assert.Contains(r.Changes, c => c.Effect.Contains("closes") && c.Mechanism.Kind == "terrain" && c.Applied);
        }

        [Fact]
        public void The_return_check_finds_a_one_way_trap()
        {
            var s = Session(); if (s == null) return;
            // no Magic, no single-use items: the drop into L2's vault (behind the pick:10 door) cannot be climbed back
            var o = new AbilityOptions { Magic = false, SingleUseItems = false, LockPicking = true, PickSkill = 30, PickRetry = true };
            var r = With(null, o);
            int vault = ZoneAt(r, 2, 9, 18);
            var trap = Assert.Single(r.Solve.Traps, t => r.Set.LevelsOf(t.Zone).Contains(2) && t.Entry != null && t.Entry.Kind == "drop");
            Assert.Equal(vault, trap.Entry.From);
            Assert.True(r.Solve.SafeZones >= 0);
            Assert.Contains("RETURN CHECK", Timeline.Report(r));
            // with Magic (Levitate) the player gets back up
            var m = r.Options.Clone(); m.Magic = true;
            Assert.DoesNotContain(Timeline.Solve(r, m, "magic").Traps, t => t.Zone == trap.Zone);
            // a planted sapling (held from the start here) saves every zone
            var sap = With(@"{ ""format"": ""uw1-facts"", ""facts"": [ { ""id"": ""sapling"", ""kind"": ""warp"", ""to"": ""planted"", ""requires"": ""item:rune_bag"", ""source"": ""test"" } ] }", o);
            Assert.Empty(sap.Solve.Traps);
            Assert.True(sap.Solve.SavedBySapling > 0);
        }

        // ---------------------------------------------------------------- the new kinds at work

        [Fact]
        public void Links_needs_and_goals_from_a_facts_file()
        {
            var s = Session(); if (s == null) return;
            var st = s.NewGameStart ?? s.Start;
            string json = @"{ ""format"": ""uw1-facts"", ""facts"": [
  { ""id"": ""to_moonorb"", ""kind"": ""link"", ""from"": { ""level"": " + st.Level + @", ""x"": " + (int)st.X + @", ""y"": " + (int)st.Y + @" }, ""to"": { ""level"": 2, ""x"": 9, ""y"": 35 }, ""requires"": ""key:1"", ""source"": ""test"" },
  { ""id"": ""to_room"", ""kind"": ""link"", ""from"": { ""level"": " + st.Level + @", ""x"": " + (int)st.X + @", ""y"": " + (int)st.Y + @" }, ""to"": { ""level"": 2, ""x"": 30, ""y"": 30 }, ""requires"": ""free"", ""option"": ""kill_npcs"", ""source"": ""test"" },
  { ""id"": ""no_sword"", ""kind"": ""needs"", ""level"": 3, ""trigger"": 971, ""requires"": ""flag:never_given"", ""source"": ""test"" },
  { ""id"": ""goal_home"", ""kind"": ""goal"", ""level"": " + st.Level + @", ""x"": " + (int)st.X + @", ""y"": " + (int)st.Y + @", ""requires"": ""key:1 & option:magic"", ""source"": ""test"" }
] }";
            var r = With(json);
            Assert.Empty(r.Facts.Check());
            int moon = ZoneAt(r, 2, 9, 35), room = ZoneAt(r, 2, 30, 30), sword = ZoneAt(r, 3, 58, 9);
            var sv = r.Solve;
            Assert.True(sv.ZoneSphere.ContainsKey(moon));
            Assert.Equal("facts: to_moonorb", sv.FirstLink[moon].Notes);
            Assert.False(sv.ZoneSphere.ContainsKey(room));          // option:kill_npcs off: no link
            Assert.DoesNotContain("flag:terrain_l3_971", sv.Fired);  // the needs fact holds it back
            Assert.False(sv.ZoneSphere.ContainsKey(sword));
            Assert.Equal(r.Set.StartZone, r.GoalZone);
            Assert.True(sv.GoalReached);
            var o = r.Options.Clone(); o.KillNpcs = true; o.Magic = false;
            var kill = Timeline.Solve(r, o, "kill");
            Assert.True(kill.ZoneSphere.ContainsKey(room));
            Assert.False(kill.GoalReached);
            Assert.Contains("option:magic", kill.GoalMissing);
        }

        [Fact]
        public void Warps_and_free_flags_from_a_facts_file()
        {
            var s = Session(); if (s == null) return;
            var st = s.NewGameStart ?? s.Start;
            string json = @"{ ""format"": ""uw1-facts"", ""facts"": [
  { ""id"": ""to_vault"", ""kind"": ""warp"", ""from"": ""any"", ""to"": { ""level"": 2, ""x"": 9, ""y"": 18 }, ""requires"": ""item:pole"", ""source"": ""test"" },
  { ""id"": ""plate_code"", ""kind"": ""free"", ""level"": 4, ""object"": 747, ""flags"": [""flag:var27_is_38""], ""source"": ""test"" },
  { ""id"": ""goal_far"", ""kind"": ""goal"", ""at"": [" + st.Level + ", " + (int)st.X + ", " + (int)st.Y + @"], ""requires"": ""flag:never_given"", ""source"": ""test"" }
] }";
            var r = With(json);
            int ledge = ZoneAt(r, 2, 9, 18), plate = ZoneAt(r, 4, 13, 44);
            var sv = r.Solve;
            Assert.True(sv.ZoneSphere.ContainsKey(ledge));
            Assert.Equal("warp", sv.FirstLink[ledge].Kind);
            Assert.True(sv.ZoneSphere[ledge] > sv.TokenSphere["item:pole"]);
            Assert.True(sv.TokenSphere.ContainsKey("flag:var27_is_38"));
            Assert.True(sv.ZoneSphere.ContainsKey(plate));
            Assert.False(sv.GoalReached);
            Assert.Contains("flag:never_given", sv.GoalMissing);
            // without the facts neither is reached
            var none = With(null);
            Assert.False(none.Solve.ZoneSphere.ContainsKey(ledge));
            Assert.False(none.Solve.ZoneSphere.ContainsKey(plate));
        }

        [Fact]
        public void The_timeline_reports_and_runs_the_main_option_sets()
        {
            var r = Uw1(); if (r == null) return;
            Assert.Empty(r.Solve.Warnings);
            Assert.Equal(10, r.Variants.Count);
            foreach (var kv in r.Variants) Assert.True(kv.Value.ZoneSphere.Count > 150, kv.Key);
            Assert.True(r.Solve.ZoneSphere.Count >= r.Abilities.Solve.ZoneSphere.Count);
            Assert.True(r.Solve.Applied.Count > 20);
            string text = Timeline.Report(r);
            foreach (var h in new[] { "PHASE C", "TIMELINE", "CHANGES THAT CLOSE THINGS", "CHANGES THAT OPEN THINGS", "RETURN CHECK", "FACTS: LINKS, WARPS, GOAL" }) Assert.Contains(h, text);
            // with the quest facts (uw1-quests.json, C2) shipped too, the goal is reached with the defaults
            if (r.Facts.Files.Contains("uw1-quests.json")) { Assert.NotNull(r.GoalFact); Assert.True(r.Solve.GoalReached, string.Join(", ", r.Solve.GoalMissing)); }
            // the base zones still save and load
            var back = ZoneSet.FromJson(r.Set.ToJson(), 9);
            Assert.Equal(r.Set.Links.Count, back.Links.Count);
        }
    }
}
