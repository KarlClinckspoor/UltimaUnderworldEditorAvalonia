using System;
using System.IO;
using System.Linq;
using UwEditor.Core.Randomizer;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class RandomizerTests
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

        [Fact]
        public void Glasbey_colours_are_distinct_and_never_near_black_white_or_magenta()
        {
            var c = Glasbey.Colors;
            Assert.Equal(254, c.Length);
            Assert.Equal(c.Length, c.Distinct().Count());
            foreach (var x in c)
            {
                double l, a, b;
                Glasbey.Lab((int)(x >> 16 & 255), (int)(x >> 8 & 255), (int)(x & 255), out l, out a, out b);
                Assert.True(l >= 22);
            }
            Assert.Equal(c[0], ZoneSet.DefaultColor(2));
            Assert.Equal(0xFFFF00FFu, ZoneSet.DefaultColor(ZoneSet.Unassigned));
        }

        [Fact]
        public void The_uw1_example_reads_round_trips_and_solves()
        {
            if (Example == null) return;
            var zs = ZoneSet.Load(Example, 9);
            Assert.Equal(53, zs.StartZone);
            Assert.Equal(53, zs.ZoneAt(1, 32, 2));       // the new-game start square is in the starting area
            Assert.True(zs.Zones.Count > 80);
            Assert.True(zs.Links.Count > 100);
            Assert.Equal(zs.ToJson(), ZoneSet.FromJson(zs.ToJson(), 9).ToJson());
            var res = new ZoneSolver(zs, new SolverOptions()).Solve(zs.StartZone, zs.GoalZone);
            Assert.True(res.GoalReached);
        }

        [Fact]
        public void Requirement_lists_and_expressions_convert_both_ways()
        {
            var k = new ZoneLink { Requires = "key:5 & (pick:3 | bash) & item:orb" };
            Assert.Equal(new[] { "key:5", "pick:3 | bash", "item:orb" }, k.RequirementList);
            k.RequirementList = new System.Collections.Generic.List<string> { "key:2 | bash", "spell:levitate" };
            Assert.Equal("(key:2 | bash) & spell:levitate", k.Requires);
            string err;
            Assert.NotNull(Requirement.TryParse(k.Requires, out err));
            Assert.Null(err);
            k.RequirementList = new System.Collections.Generic.List<string>();
            Assert.Equal("free", k.Requires);
        }

        [Fact]
        public void Pictures_export_and_import_back_with_north_up()
        {
            var zs = new ZoneSet(9);
            zs.SetZone(1, 3, 0, 7);        // the south edge
            zs.SetZone(1, 60, 63, 9);      // the north edge
            var png = zs.ExportPicture(1);
            var img = UwEditor.Core.Graphics.Png.Decode(png);
            Assert.Equal(9, img.Indices[0 * 64 + 60]);           // top row of the picture is y = 63
            Assert.Equal(7, img.Indices[63 * 64 + 3]);
            var back = new ZoneSet(9);
            Assert.Null(back.ImportPicture(1, png));
            Assert.Equal(7, back.ZoneAt(1, 3, 0));
            Assert.Equal(9, back.ZoneAt(1, 60, 63));
            Assert.NotNull(back.GetZone(7));
        }

        [Fact]
        public void Key_items_and_doors_of_uw1_level_1_in_the_example_zones()
        {
            string g = GameData.Game("UW");
            if (g == null || Example == null) return;
            var s = GameSession.Open(g);
            var zs = ZoneSet.Load(Example, 9);
            var items = KeyItems.Find(s, l => s.LoadLevel(l), zs, new[] { 1 });
            Assert.Contains(items, i => i.Token == "key:1" && i.Zone == 53);       // the key in the starting pack
            Assert.Contains(items, i => i.Kind == "NPC" && i.Token == "npc:bragit");
            Assert.DoesNotContain(items, i => i.Item >= 0x150 && i.Item < 0x170);   // no furniture or decals
            var doors = ZoneDoors.Find(s, l => s.LoadLevel(l), zs, new[] { 1 });
            // the skeleton room's doors (key 2, breakable) and the pillar room (key 2, pick 0)
            Assert.Contains(doors, d => d.ZoneA == 2 && d.ZoneB == 9 && d.Requires == "key:2 | pick:0 | bash");
            Assert.Contains(doors, d => d.ZoneA == 2 && d.ZoneB == 8 && d.Requires.StartsWith("key:2"));
            System.Collections.Generic.List<DoorPassage> used;
            Assert.Equal("key:2 | pick:2 | bash", ZoneDoors.Suggest(doors, 8, 2, out used));
            Assert.Equal(2, used.Count);
            zs.Importance["1:" + items.First(i => i.Token == "npc:bragit").Index] = false;
            Assert.DoesNotContain(KeyItems.Find(s, l => s.LoadLevel(l), zs, new[] { 1 }), i => i.Token == "npc:bragit");
        }

        [Fact]
        public void Zone_fill_stops_at_walls_doors_and_climbs_but_not_drops()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var m = s.LoadLevel(1);
            var f = new ZoneFlood(m);
            var zs = new ZoneSet(s.Profile.LevelSlots);
            zs.InitLevel(1, (x, y) => !m.Tiles[x, y].IsSolid);
            int n = zs.Fill(1, 32, 2, 5, f);
            Assert.True(n > 10);
            Assert.Equal(5, zs.ZoneAt(1, 32, 2));
            int doors = 0;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    if (m.Tiles[x, y].IsSolid) Assert.Equal(ZoneSet.Solid, zs.ZoneAt(1, x, y));
                    if (f.HasDoor(x, y)) { doors++; Assert.NotEqual(5, zs.ZoneAt(1, x, y)); }
                }
            Assert.True(doors > 0);
            Assert.Empty(f.Reach(zs.Tiles[0], 0, 0));          // rock
            // two flat neighbours more than the climb apart: down is fine, up is not
            bool found = false;
            for (int y = 0; y < 64 && !found; y++)
                for (int x = 0; x < 63 && !found; x++)
                {
                    var a = m.Tiles[x, y]; var b = m.Tiles[x + 1, y];
                    if (a.Type != TileType.Open || b.Type != TileType.Open || f.HasDoor(x, y) || f.HasDoor(x + 1, y)) continue;
                    if (b.Height - a.Height > ZoneFlood.DefaultMaxClimb) { found = true; Assert.False(f.CanStep(x, y, 1)); Assert.True(f.CanStep(x + 1, y, 3)); }
                    if (Math.Abs(b.Height - a.Height) <= ZoneFlood.DefaultMaxClimb) { Assert.True(f.CanStep(x, y, 1)); Assert.True(f.CanStep(x + 1, y, 3)); }
                }
            Assert.True(found);
        }
    }
}
