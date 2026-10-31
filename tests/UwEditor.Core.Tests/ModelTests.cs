using System;
using System.Linq;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class ModelTests
    {
        readonly ITestOutputHelper output;
        public ModelTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Levers_doors_and_furniture_are_drawn_as_models()
        {
            foreach (var name in new[] { "UW", "UW2" })
            {
                string g = GameData.Game(name); if (g == null) continue;
                var s = GameSession.Open(g);
                Assert.NotNull(s.ExeModels);
                var level = s.LoadLevel(1);
                var mesh = ObjectModels.Build(level, false);
                output.WriteLine("{0}: {1} models, {2} objects as models, {3} triangles, {4} texture layers",
                    name, s.ExeModels.Models.Count, mesh.Objects.Count, mesh.TriangleCount, mesh.Layers.Count);
                foreach (var grp in mesh.Objects.Select(i => level.Objects[i].Name).GroupBy(n => n).OrderByDescending(x => x.Count()).Take(12))
                    output.WriteLine("  {0} × {1}", grp.Count(), grp.Key);
                Assert.True(mesh.Objects.Count > 10);
                Assert.All(mesh.Vertices, v => Assert.InRange(v.Layer, -1, mesh.Layers.Count - 1));
            }
        }

        [Fact]
        public void A_wall_lever_lies_on_its_wall()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var level = s.LoadLevel(1);
            var mesh = ObjectModels.Build(level, false);
            var lever = level.Objects.First(o => o.Index == 623);   // tile (59, 45), heading 4 (south), fine y 0
            Assert.Contains(623, mesh.Objects);
            var pts = mesh.Vertices.Where(v => (int)v.Object == 623).Select(v => v.Position).ToList();
            output.WriteLine("lever 623 at {0:0.000}, {1:0.000}, h {2:0.000}; model x {3:0.000}..{4:0.000}, z {5:0.000}..{6:0.000}, y {7:0.000}..{8:0.000}",
                lever.WorldX, lever.WorldY, lever.WorldZ, pts.Min(p => p.X), pts.Max(p => p.X), pts.Min(p => p.Z), pts.Max(p => p.Z), pts.Min(p => p.Y), pts.Max(p => p.Y));
            // its wall is the south edge of the tile, y = 45; the model stays within a tile's tenth of it
            Assert.InRange(pts.Min(p => p.Z), 44.98f, 45.2f);
            Assert.InRange(pts.Max(p => p.Z), 44.98f, 45.2f);
        }
    }
}

namespace UwEditor.Core.Tests
{
    public class GridAndDoorTests
    {
        readonly ITestOutputHelper output;
        public GridAndDoorTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Grid_lines_lie_on_multiples_of_the_step_and_labels_name_them()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var level = s.LoadLevel(1);
            var f = GridOverlay.Lines(level, 5);
            Assert.True(f.Count > 0);
            for (int i = 0; i < f.Count; i += 14)
            {
                float x0 = f[i], z0 = f[i + 2], x1 = f[i + 7], z1 = f[i + 9];
                bool onX = x0 == x1 && x0 % 5 == 0, onZ = z0 == z1 && z0 % 5 == 0;
                Assert.True(onX || onZ, string.Format("segment ({0}, {1})-({2}, {3})", x0, z0, x1, z1));
            }
            var cam = new OrbitCamera { Target = new Vector3(32.5f, 3, 2.5f), Yaw = 0, Pitch = 60, Distance = 10 };
            var labels = GridOverlay.Labels(cam, 800, 600, 5, 3f);
            output.WriteLine(string.Join(", ", labels.Select(l => string.Format("{0} at {1:0},{2:0}", l.Text, l.X, l.Y))));
            Assert.Contains(labels, l => l.Text == "X: 30");
            Assert.Contains(labels, l => l.Text == "X: 35");
            // labels sit on the view's border or inside it
            Assert.All(labels, l => { Assert.InRange(l.X, -0.5f, 800.5f); Assert.InRange(l.Y, -0.5f, 600.5f); });
            // looking north from (32.5, 2.5): x = 30 is left of x = 35
            Assert.True(labels.First(l => l.Text == "X: 30").X < labels.First(l => l.Text == "X: 35").X);
        }

        [Fact]
        public void Open_doors_stand_three_steps_above_their_floor()
        {
            // 3d/DRAWOBJ.C do_door draws a door with flags & 7 set 0xC0 lower, because opening
            // it raised the object by 24 (obj/USEITEMS.C OpenDoor): check the shipped levels agree
            foreach (var name in new[] { "UW", "UW2" })
            {
                string g = GameData.Game(name); if (g == null) continue;
                var s = GameSession.Open(g);
                int open = 0, raised = 0;
                foreach (var slot in s.LevelSlots().Where(x => x.Present))
                {
                    var level = s.LoadLevel(slot.Level);
                    foreach (var o in level.Objects.Where(o => o.OnTile && o.Item >= 0x140 && o.Item < 0x150 && (o.Item & 7) != 6))
                    {
                        int flags = (o.Record[0] | o.Record[1] << 8) >> 9 & 7;
                        if (flags == 0) continue;
                        open++;
                        var t = level.Tiles[o.TileX, o.TileY];
                        if (o.Z - t.Height * 8 == 24) raised++;
                    }
                }
                output.WriteLine("{0}: {1} doors drawn open, {2} of them 24 above the floor", name, open, raised);
                Assert.True(open == 0 || raised * 10 >= open * 9);
            }
        }
    }
}
