using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The automatic zones, Phase B (abilities, their sources, the logic options) on the shipped UW1 levels.</summary>
    public class AbilityZonesTests
    {
        static string Docs(string rel)
        {
            string here = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && here != null; i++, here = Path.GetDirectoryName(here))
            {
                string p = Path.Combine(here, "docs", rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static readonly object gate = new object();
        static AbilityZoneResult cached;
        static GameSession session;

        static AbilityZoneResult Uw1()
        {
            lock (gate)
            {
                if (cached != null) return cached;
                string g = GameData.Game("UW");
                if (g == null) return null;
                session = GameSession.Open(g);
                string ex = Docs(Path.Combine("examples", "uw1-zones.json"));
                var painted = ex == null ? null : ZoneSet.Load(ex, 9);
                cached = AbilityZones.Build(session, l => session.LoadLevel(l), new NavOptions(), new AbilityOptions(), painted);
                return cached;
            }
        }

        static SolveResult Solve(AbilityZoneResult r, Action<AbilityOptions> set)
        {
            var o = r.Options.Clone();
            set(o);
            return new ZoneSolver(r.Set, o.ToSolver()).Solve(r.Set.StartZone, r.Set.GoalZone);
        }

        [Fact]
        public void The_facts_file_loads_and_is_well_formed()
        {
            var shipped = FactSet.Shipped(false);
            Assert.Empty(shipped.Check());
            Assert.Contains(shipped.Facts, f => f.Id == "dragonskin_boots" && f.Kind == "craft" && f.Grants == "worn:lava");
            Assert.Contains(shipped.Facts, f => f.Kind == "flag" && f.Grants == "flag:orb_destroyed" && f.Level == 7 && f.Object == 820);
            Assert.Contains(shipped.Facts, f => f.Kind == "walls" && f.Level == 7 && f.Rect.Length == 4);
            Assert.All(shipped.Facts, f => Assert.False(string.IsNullOrEmpty(f.Source)));
            string path = Docs(Path.Combine("facts", "uw1-facts.json"));
            if (path != null) Assert.Equal(shipped.Facts.Count, FactSet.Load(path).Facts.Count);
            Assert.Empty(FactSet.Shipped(true).Facts);
            // a hand edit with a mistake is reported, not thrown
            var bad = FactSet.FromJson("{\"format\":\"uw1-facts\",\"facts\":[{\"id\":\"x\",\"kind\":\"craft\",\"grants\":\"worn:lava\",\"requires\":\"item:a &\"},{\"id\":\"y\",\"kind\":\"what\"}]}");
            Assert.Equal(2, bad.Check().Count);
        }

        [Fact]
        public void The_search_and_pick_rules_follow_skill_check()
        {
            // skill_check(v, t) > 0 when v - t + rand() % 31 > 15 (game/SKILLCHK.C)
            Assert.Equal(5, Requirement.PassChanceIn31(0, 10));
            Assert.Equal(15, Requirement.PassChanceIn31(0, 0));
            Assert.Equal(0, Requirement.PassChanceIn31(0, 15));
            Assert.Equal(31, Requirement.PassChanceIn31(26, 10));
            var o = new SolverOptions { SearchSkill = 0, SearchRetry = true };
            Assert.True(Requirement.SearchPasses(o, 0));
            Assert.True(Requirement.SearchPasses(o, 14));
            Assert.False(Requirement.SearchPasses(o, 15));
            o.SearchRetry = false;
            Assert.True(Requirement.SearchPasses(o, 0));        // height 0: no check at all
            Assert.False(Requirement.SearchPasses(o, 3));
            o.SearchSkill = 10;
            Assert.True(Requirement.SearchPasses(o, 10));
        }

        [Fact]
        public void Ability_sources_are_found()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            // the ring of levitation on L7, worn; the Fly potions on L2, single-use
            Assert.Contains(res.Sources, x => x.Ability == "levitate" && x.Worn && x.Level == 7 && x.Index == 683);
            Assert.Contains(res.Sources, x => x.Ability == "levitate" && !x.Permanent && x.Kind == "potion");
            Assert.Contains(res.Sources, x => x.Ability == "jump" && x.Worn);
            Assert.Contains(res.Sources, x => x.Ability == "reveal" && x.Kind == "scroll");
            Assert.Contains(res.Sources, x => x.Ability == "lava" && x.Kind == "potion");
            // water walk: no item carries it, but its runes do (Ylem Por)
            Assert.Contains(zs.Placements, p => p.Grants == "cast:waterwalk" && p.Requires.Contains("rune:ylem") && p.Requires.Contains("rune:por") && p.Requires.Contains("item:rune_bag"));
            Assert.Contains(zs.Placements, p => p.Grants == "cast:levitate" && p.Requires.Contains("rune:hur"));
            Assert.Contains(zs.Placements, p => p.Grants == "item:rune_bag" && p.Zone == zs.StartZone);
            Assert.Contains(zs.Placements, p => p.Grants == "worn:levitate");
            var sv = res.Solve;
            Assert.True(sv.TokenSphere.ContainsKey("cast:waterwalk"));
            Assert.True(sv.TokenSphere.ContainsKey("cast:levitate"));
            // hazard:lava is gone, lava is an ability now
            Assert.DoesNotContain(zs.Links, k => k.Requires.Contains("hazard:"));
            Assert.Contains(zs.Links, k => k.Requires.Contains("spell:lava"));
        }

        [Fact]
        public void The_dragon_skin_boots_chain_is_located()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            foreach (var t in new[] { "item:strong_thread", "item:bowl", "item:dead_rotworm", "item:mushroom", "item:flask_of_port", "item:stew_recipe", "item:food", "npc:lanugo", "npc:sethar_strongarm", "npc:marrowsuck", "item:dragon_scales" })
                Assert.Contains(zs.Placements, p => p.Grants == t && p.Zone > ZoneSet.Unassigned);
            // the recipe is Lanugo's (he hands it over), the scales Sethar's for the stew
            int lanugo = zs.Placements.First(p => p.Grants == "npc:lanugo").Zone;
            Assert.Contains(zs.Placements, p => p.Grants == "item:stew_recipe" && p.Zone == lanugo);
            Assert.All(zs.Placements.Where(p => p.Grants == "item:dragon_scales"), p => Assert.Contains("item:rotworm_stew", p.Requires));
            // rotworms count as a source of their corpse
            Assert.Contains(zs.Placements, p => p.Grants == "item:dead_rotworm" && p.Notes.Contains("rotworm") && p.Notes.Contains("killed"));
            Assert.Contains(zs.Placements, p => p.Grants == "worn:lava" && p.Requires.Contains("npc:marrowsuck") && p.Requires.Contains("item:dragon_scales"));
            Assert.True(res.Solve.TokenSphere.ContainsKey("worn:lava"));
            Assert.True(res.Solve.TokenSphere["worn:lava"] > res.Solve.TokenSphere["item:dragon_scales"] - 1);
            Assert.DoesNotContain(res.Warnings, w => w.Contains("grants") || w.Contains("talks as"));
        }

        [Fact]
        public void The_orb_and_level_7_casting()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            var m = session.LoadLevel(7);
            var orb = m.Objects[820];
            Assert.Equal(0x117, orb.Item);
            int zone = zs.NearestZone(7, orb.TileX, orb.TileY);
            Assert.Contains(zs.Placements, p => p.Grants == "flag:orb_destroyed" && p.Requires == "item:orb_rock" && p.Zone == zone);
            // on L7, casting needs the orb gone; items still work
            Assert.Contains(zs.Links, k => k.Level == 7 && k.Requires.Contains("cast:lava & flag:orb_destroyed") && k.Requires.Contains("use:lava"));
            Assert.DoesNotContain(zs.Links, k => k.Level == 7 && k.Requires.Contains("spell:"));
        }

        [Fact]
        public void The_maze_tiles_with_damage_triggers_are_walls()
        {
            var res = Uw1(); if (res == null) return;
            var walls = res.Walls["damaging_maze"];
            Assert.True(walls.Count >= 10);
            var fact = res.Facts.Facts.First(f => f.Id == "damaging_maze");
            var m = session.LoadLevel(7);
            var dmg = AbilityZones.DamageTiles(session, m);
            foreach (int t in dmg.Where(t => fact.InRect(t % 64, t / 64)))
            {
                Assert.False(m.Tiles[t % 64, t / 64].IsSolid);       // open in the level
                Assert.Equal(0, res.Set.ZoneAt(7, t % 64, t / 64));   // a wall for the zones
            }
            Assert.Equal(dmg.Count(t => fact.InRect(t % 64, t / 64)), walls.Count);
            // the global option walls the rest too
            Assert.True(res.DamageTilesElsewhere.Values.Sum() > 0);
            Assert.False(res.Walls.ContainsKey("global"));
        }

        [Fact]
        public void Hidden_gates_need_reveal_or_search()
        {
            var res = Uw1(); if (res == null) return;
            var zs = res.Set;
            var back = res.Hidden.Single(h => h.Level == 7 && h.Trigger == 351);
            Assert.Equal(0, back.Z);
            Assert.Equal(42, back.X); Assert.Equal(10, back.Y);
            Assert.Equal(349, back.Door);
            Assert.Contains(90, back.PaintedLinks);
            var bandit = res.Hidden.Single(h => h.Level == 3 && h.Trigger == 1007);
            Assert.Equal(7, bandit.Z);
            Assert.NotEmpty(bandit.ToZones);
            Assert.Contains(zs.Links, k => k.Kind == "secret" && k.From == bandit.FromZone && bandit.ToZones.Contains(k.To) && k.Requires == "spell:reveal | search:7");
            // Search 0 with retries passes it; one look at Search 0 without Reveal does not
            int room = bandit.ToZones[0];
            Assert.True(res.Solve.ZoneSphere.ContainsKey(room));
            var strict = Solve(res, o => { o.SearchRetry = false; o.Magic = false; o.SingleUseItems = false; });
            Assert.False(strict.ZoneSphere.ContainsKey(room));
            var skilled = Solve(res, o => { o.SearchRetry = false; o.Magic = false; o.SearchSkill = 7; });
            Assert.True(skilled.ZoneSphere.ContainsKey(room));
        }

        [Fact]
        public void Doors_to_open_without_picking_and_bashing()
        {
            var res = Uw1(); if (res == null) return;
            var off = res.Options.Clone();
            off.LockPicking = false; off.Bashing = false;
            var doors = AbilityZones.DoorsToOpen(res.Set, off);
            Assert.NotEmpty(doors);
            Assert.All(doors, d => Assert.True(d.Link.Requires.Contains("pick:") || d.Link.Requires.Contains("bash")));
            Assert.Contains(doors, d => d.Matters);
            var on = res.Options.Clone();
            on.LockPicking = true; on.Bashing = true;
            Assert.Empty(AbilityZones.DoorsToOpen(res.Set, on));
            string report = AbilityZones.Report(res);
            Assert.Contains("DOORS THE SHUFFLE MUST OPEN", report);
            Assert.Contains("HIDDEN GATES", report);
            Assert.Contains("THE DRAGON SKIN BOOTS", report);
        }

        [Fact]
        public void The_solver_follows_the_options()
        {
            var res = Uw1(); if (res == null) return;
            foreach (var kv in res.Variants) Assert.Empty(kv.Value.Warnings);
            Assert.Equal(8, res.Variants.Count);
            foreach (bool once in new[] { false, true })
                foreach (bool doors in new[] { false, true })
                {
                    var on = Solve(res, o => { o.Magic = true; o.SingleUseItems = once; o.LockPicking = doors; o.Bashing = doors; });
                    var off = Solve(res, o => { o.Magic = false; o.SingleUseItems = once; o.LockPicking = doors; o.Bashing = doors; });
                    Assert.True(off.ZoneSphere.Count <= on.ZoneSphere.Count);
                    Assert.True(on.ZoneSphere.Keys.Count(z => !off.ZoneSphere.ContainsKey(z)) >= 0);
                    Assert.All(off.ZoneSphere.Keys, z => Assert.True(on.ZoneSphere.ContainsKey(z)));
                }
            var strictest = Solve(res, o => { o.Magic = false; o.SingleUseItems = false; o.LockPicking = false; o.Bashing = false; });
            var loosest = Solve(res, o => { o.Magic = true; o.SingleUseItems = true; o.LockPicking = true; o.PickSkill = 30; o.Bashing = true; });
            Assert.True(strictest.ZoneSphere.Count < loosest.ZoneSphere.Count);
            Assert.True(res.Solve.ZoneSphere.Count > 150);
            // the old meaning stays for zonings without ability sources
            Assert.True(Requirement.Parse("spell:levitate").Eval(new Ctx(new SolverOptions())));
            Assert.False(Requirement.Parse("spell:levitate").Eval(new Ctx(new SolverOptions { AbilitySources = true })));
        }

        [Fact]
        public void The_set_saves_and_loads()
        {
            var res = Uw1(); if (res == null) return;
            var back = ZoneSet.FromJson(res.Set.ToJson(), 9);
            Assert.Equal(res.Set.Placements.Count, back.Placements.Count);
            Assert.Equal(res.Set.Links.Count, back.Links.Count);
            var a = new ZoneSolver(back, res.Options.ToSolver()).Solve(back.StartZone, back.GoalZone);
            Assert.Equal(res.Solve.ZoneSphere.Count, a.ZoneSphere.Count);
        }

        sealed class Ctx : IRequirementContext
        {
            public Ctx(SolverOptions o) { Options = o; }
            public SolverOptions Options { get; private set; }
            public bool HasToken(string t) { return false; }
            public bool ZoneReachable(int z) { return false; }
            public int TalismanCount() { return 0; }
        }
    }
}
