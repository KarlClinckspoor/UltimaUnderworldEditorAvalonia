using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace UwEditor.Core.Tests
{
    public class ViewTests
    {
        [Fact]
        public void Camera_looking_north_has_east_on_the_right()
        {
            // the level's axes are left-handed (x east, z north); the view must not mirror them
            var cam = new OrbitCamera { Target = new Vector3(10, 0, 10), Yaw = 0, Pitch = 30, Distance = 5 };
            var vp = cam.View * cam.Projection(1.5f);
            Func<Vector3, Vector2> screen = p =>
            {
                var c = Vector4.Transform(new Vector4(p, 1), vp);
                return new Vector2(c.X / c.W, c.Y / c.W);
            };
            Assert.True(screen(new Vector3(11, 0, 10)).X > screen(new Vector3(10, 0, 10)).X, "east is right");
            Assert.True(screen(new Vector3(10, 0, 11)).Y > screen(new Vector3(10, 0, 10)).Y, "north is up the screen");
            Assert.True(cam.Eye.Z < cam.Target.Z, "the camera is south of what it looks at");
        }

        [Fact]
        public void The_ray_through_the_middle_of_the_view_hits_the_target()
        {
            var cam = new OrbitCamera { Target = new Vector3(20, 1, 30), Yaw = 135, Pitch = 50, Distance = 7 };
            Vector3 o, d;
            cam.Ray(400, 300, 800, 600, out o, out d);
            var toTarget = Vector3.Normalize(cam.Target - o);
            Assert.True(Vector3.Dot(d, toTarget) > 0.9999f);
        }

        [Fact]
        public void Picking_finds_the_floor_and_a_sprite_in_front_of_a_wall()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var level = s.LoadLevel(1);
            var mesh = LevelGeometry.Build(level, false);
            // straight down onto the start square
            var hit = Picking.Pick(new Vector3(32.5f, 10, 2.5f), new Vector3(0, -1, 0), mesh, null, Vector3.UnitX, Vector3.UnitY);
            Assert.Equal(32 + 64 * 2, hit.Tile);
            Assert.Equal(level.FloorAt(32, 2, 0.5, 0.5), hit.Point.Y, 3);
            // the sack on (33, 3) is picked before the floor under it
            var atlas = SpriteAtlas.Build(s.Graphics.ObjectPictures);
            var sprites = ObjectSprites.Build(level, atlas, new SpriteFilter());
            var sack = sprites.First(sp => level.Objects[sp.Object].Name == "sack" && level.Objects[sp.Object].TileX == 33 && level.Objects[sp.Object].TileY == 3);
            var above = sack.Base + new Vector3(0, 2, -0.6f);
            var dir = Vector3.Normalize(sack.Base + new Vector3(0, sack.Height / 2, 0) - above);
            var hit2 = Picking.Pick(above, dir, mesh, sprites, Vector3.UnitX, Vector3.UnitY);
            Assert.Equal(sack.Object, hit2.Object);
        }

        [Fact]
        public void Every_level_builds_a_mesh_with_valid_layers()
        {
            foreach (var name in new[] { "UW", "UW2" })
            {
                string g = GameData.Game(name); if (g == null) continue;
                var s = GameSession.Open(g);
                var atlas = SpriteAtlas.Build(s.Graphics.ObjectPictures);
                foreach (var slot in s.LevelSlots().Where(x => x.Present))
                {
                    var level = s.LoadLevel(slot.Level);
                    var scene = LevelScene.Build(level, atlas, new SceneOptions { Ceiling = true });
                    Assert.True(scene.Mesh.TriangleCount > 100, name + " level " + slot.Level);
                    Assert.Equal(scene.Mesh.MissingLayer + 1, scene.Layers.Count);
                    Assert.All(scene.Mesh.Vertices, v => Assert.InRange(v.Layer, 0, scene.Layers.Count - 1));
                    Assert.All(scene.Layers, l => Assert.Equal(LevelScene.LayerEdge * LevelScene.LayerEdge * 4, l.Pixels.Length));
                }
            }
        }

        [Fact]
        public void Uw2_void_levels_have_no_ceiling_and_the_ceiling_uses_floor_slot_15()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var s = GameSession.Open(g);
            Assert.False(s.LoadLevel(65).HasCeiling);
            Assert.True(s.LoadLevel(68).HasCeiling);
            Assert.True(s.LoadLevel(1).HasCeiling);
            Assert.Equal(15, s.LoadLevel(1).CeilingFloorIndex);
            Assert.DoesNotContain(LevelGeometry.Build(s.LoadLevel(65), true).TriangleKind, k => k == 2);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_walls_texture_starts_at_its_top_edge_as_the_games_does(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var s = GameSession.Open(g);
            var mesh = LevelGeometry.Build(s.LoadLevel(1), false);
            int faces = 0, offGrid = 0;
            // a wall is two triangles in a row: (bottom a, bottom b, top b) and (bottom a, top b, top a)
            for (int t = 0; t + 1 < mesh.TriangleCount; t++)
            {
                if (mesh.TriangleKind[t] != 1) continue;
                var a0 = mesh.Vertices[3 * t]; var b0 = mesh.Vertices[3 * t + 1]; var b1 = mesh.Vertices[3 * t + 2]; var a1 = mesh.Vertices[3 * t + 5];
                Assert.Equal(0, b1.V); Assert.Equal(0, a1.V);
                float h = ((a1.Y - a0.Y) + (b1.Y - b0.Y)) / 2;
                Assert.Equal(h, a0.V, 4); Assert.Equal(h, b0.V, 4);
                faces++;
                if (Math.Abs(a1.Y - Math.Round(a1.Y)) > 0.01) offGrid++;      // a step whose top is not a whole tile up
                t++;
            }
            Assert.True(faces > 100);
            Assert.True(offGrid > 0);
        }
    }
}
