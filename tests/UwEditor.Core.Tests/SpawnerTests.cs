using System;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>
    /// Wandering-monster spawners (event/TRIGGER.C DoWanderingMonsters): create-object traps with
    /// flags 0 holding a creature, which the game runs without a trigger. The shipped ones are
    /// found and described, and the Wandering monsters recipe makes one the game will run.
    /// </summary>
    public class SpawnerTests
    {
        static GameSession Open(string game) { string g = GameData.Game(game); return g == null ? null : GameSession.Open(g); }

        [Theory]
        [InlineData("UW", 34)]
        [InlineData("UW2", 49)]
        public void The_shipped_spawners_are_found_and_none_is_reached_by_a_trigger(string game, int expected)
        {
            var s = Open(game); if (s == null) return;
            int n = 0, hidden = 0;
            foreach (var slot in s.LevelSlots().Where(x => x.Present))
            {
                var m = s.LoadLevel(slot.Level);
                var sp = Triggers.Spawners(m);
                n += sp.Count;
                foreach (var o in sp)
                {
                    Assert.True(m.Objects[o.Link].IsCritter);
                    Assert.Contains("wandering-monster spawner", Triggers.SpawnerText(s, m, o));
                    // no trigger counts itself on a spawner (a trigger's own trap has flags 1 or more)
                    Assert.DoesNotContain(m.Objects, x => !x.Free && Triggers.IsTrigger(x.Item) && x.IsQuantity && x.Link == o.Index);
                    if (Triggers.SpawnerHidden(m, o)) hidden++;
                }
            }
            Assert.Equal(expected, n);
            Assert.Equal(0, hidden);
        }

        [Fact]
        public void Britannias_sewer_spawners_make_random_monsters()
        {
            var s = Open("UW2"); if (s == null) return;
            var m = s.LoadLevel(4);
            foreach (int i in new[] { 705, 706, 707, 708, 917, 918 })
            {
                Assert.True(Triggers.IsSpawner(m, m.Objects[i]));
                Assert.Equal(Triggers.Adventurer, m.Objects[m.Objects[i].Link].Item);
                Assert.Equal(45, m.Objects[i].Quality);
                Assert.Contains("random monster", Triggers.SpawnerText(s, m, m.Objects[i]));
            }
            Assert.Equal(18.0 / 63 * 100, Triggers.SpawnChance(45), 6);
            int rows, cols;
            Triggers.AdventurerRange(4, 0, 1, out rows, out cols);
            Assert.Equal(1, rows); Assert.Equal(2, cols);
            Triggers.AdventurerRange(4, 15, 10, out rows, out cols);
            Assert.Equal(3, rows); Assert.Equal(16, cols);
        }

        static void FreeTile(LevelModel m, out int x, out int y, int skip = 0)
        {
            for (y = 2; y < 62; y++)
                for (x = 2; x < 62; x++)
                {
                    if (m.Tiles[x, y].Type != TileType.Open || m.TileObjects(x, y).Count > 0) continue;
                    if (skip-- > 0) continue;
                    return;
                }
            throw new Exception("no free tile");
        }

        [Theory]
        [InlineData("UW", 0x44)]
        [InlineData("UW2", 0x7F)]
        public void The_recipe_makes_a_spawner_the_game_runs_and_undoes(string game, int creature)
        {
            var s = Open(game); if (s == null) return;
            var e = new LevelEditor(s, 2);
            var before = (byte[])e.Data.Clone();
            int active = new LevelBlock((byte[])e.Data.Clone(), s.Kind).ActiveCount;
            int x, y;
            FreeTile(e.Model, out x, out y);
            var r = TrapRecipes.Spawner(e, new SpawnerOptions { X = x, Y = y, Item = creature, Chance = 25 });
            var m = e.Model;
            Assert.True(m.CheckOk);
            var trap = m.Objects[r.Trap];
            Assert.Equal(TrapRecipes.CreateTrap, trap.Item);
            Assert.Equal(x, trap.TileX); Assert.Equal(y, trap.TileY);
            Assert.True(Triggers.IsSpawner(m, trap));
            Assert.Equal(Triggers.SpawnQuality(25), trap.Quality);
            Assert.Equal(47, trap.Quality);                      // 63 - 25 % of 63 = 47.25
            var t = m.Objects[trap.Link];
            Assert.Equal(creature, t.Item);
            Assert.Equal(trap.Index, t.Parent);
            Assert.Equal(x, t.Quality); Assert.Equal(y, t.Owner);   // home
            Assert.Equal(0, UltimateEditor.Uw1.Uw1Schemas.MobileObject["Attitude"].Get(t.Record));   // hostile
            // a template is not on the active list
            Assert.Equal(active, new LevelBlock((byte[])e.Data.Clone(), s.Kind).ActiveCount);
            Assert.Contains(trap.Index, Triggers.Spawners(m).Select(o => o.Index));
            Assert.Contains("Spawner #" + r.Trap, r.Summary);
            e.Undo();
            Assert.Equal(before, e.Data);
        }

        [Fact]
        public void The_recipe_refuses_what_the_game_could_not_run()
        {
            var s = Open("UW"); if (s == null) return;
            var e = new LevelEditor(s, 1);
            int x, y;
            FreeTile(e.Model, out x, out y);
            var before = (byte[])e.Data.Clone();
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Spawner(e, new SpawnerOptions { X = x, Y = y, Item = 0x7F }));      // no random monster in UW1
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Spawner(e, new SpawnerOptions { X = x, Y = y, Item = 0x95 }));      // not a creature
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Spawner(e, new SpawnerOptions { X = x, Y = y, Item = 0x44, Chance = 0.5 }));
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Spawner(e, new SpawnerOptions { X = 0, Y = 0, Item = 0x44 }));       // rock
            var sp = Triggers.Spawners(e.Model).First();
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Spawner(e, new SpawnerOptions { X = sp.TileX, Y = sp.TileY, Item = 0x44 }));   // a create-object trap is there
            Assert.Equal(before, e.Data);
        }
    }
}
