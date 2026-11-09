using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Collision boxes (Hitboxes): the game's sizes and overlap rule, the shipped levels, drawing, the Fixes check, new overlaps; the brush's tile in solid rock; objects put on the floor.</summary>
    public class HitboxesTests
    {
        readonly ITestOutputHelper output;
        public HitboxesTests(ITestOutputHelper output) { this.output = output; }

        static Hitbox Box(int fx, int fy, int r, int bottom, int height, bool creature, int index = 2)
        {
            return new Hitbox { Index = index, FineX = fx, FineY = fy, TileX = fx / 8, TileY = fy / 8, Radius = r, Bottom = bottom, Top = bottom + Math.Max(1, height), Creature = creature };
        }

        [Fact]
        public void The_overlap_rule_is_the_games()
        {
            // two creatures of radius 2: each counts 1 when a creature moves, inclusive bounds -> 2 fine units apart touch, 3 do not
            Assert.True(Hitboxes.Overlap(Box(100, 100, 2, 0, 20, true), Box(102, 100, 2, 0, 20, true)));
            Assert.False(Hitboxes.Overlap(Box(100, 100, 2, 0, 20, true), Box(103, 100, 2, 0, 20, true)));
            // a creature against a static: the static's full radius
            Assert.True(Hitboxes.Overlap(Box(100, 100, 2, 0, 20, true), Box(104, 100, 3, 0, 20, false)));
            // one above the other: no overlap
            Assert.False(Hitboxes.Overlap(Box(100, 100, 2, 0, 20, true), Box(100, 100, 2, 20, 20, true)));
            // two statics: only shared space counts, touching edges do not
            Assert.False(Hitboxes.Overlap(Box(100, 100, 2, 0, 9, false), Box(104, 100, 2, 0, 9, false)));
            Assert.True(Hitboxes.Overlap(Box(100, 100, 2, 0, 9, false), Box(103, 100, 2, 0, 9, false)));
            // radius 4 is the whole tile
            Assert.True(Hitboxes.Overlap(Box(8 * 12 + 1, 8 * 12 + 1, 4, 0, 2, false), Box(8 * 12 + 7, 8 * 12 + 7, 1, 0, 9, false)));
        }

        [Fact]
        public void Boxes_come_from_COMOBJ_and_draw_as_edges_and_faces()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var com = Hitboxes.ReadComObj(s);
            var m = s.LoadLevel(2);
            var boxes = Hitboxes.Of(m, com);
            Assert.NotEmpty(boxes);
            Assert.All(boxes.Where(b => b.Creature), b => Assert.Equal(2, b.Radius));             // every UW1 creature: radius 2
            var door = boxes.First(b => b.Item >= 0x140 && b.Item <= 0x145);
            Assert.Equal(128, door.Top - door.Bottom);
            Assert.DoesNotContain(boxes, b => b.Item >= 0x180);                                 // traps and triggers have no body
            Assert.DoesNotContain(boxes, b => !b.Creature && m.Objects[b.Index].Item == 0xA0);  // coins: no height, no touch

            var ov = Hitboxes.Overlaps(boxes);
            // the shipped level 2 has a mountainman standing in a door
            Assert.Contains(ov, o => (o.A.Creature || o.B.Creature) && (o.A.Item >= 0x140 && o.A.Item < 0x148 || o.B.Item >= 0x140 && o.B.Item < 0x148));
            Assert.All(boxes.Where(b => ov.Any(o => o.A == b || o.B == b)), b => Assert.True(b.Overlapping));

            var lines = new List<float>(); var tris = new List<float>();
            Hitboxes.AddEdges(lines, boxes.Take(3));
            Hitboxes.AddFaces(tris, boxes.Take(3));
            Assert.Equal(3 * 24 * 7, lines.Count);
            Assert.Equal(3 * 36 * 7, tris.Count);
        }

        [Fact]
        public void Fixes_lists_creature_overlaps_only()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var f2 = LevelFixes.Find(s, 2, s.LoadLevel(2).Block.Data).Where(f => f.Check == LevelFixes.Hitbox).ToList();
            Assert.Single(f2);
            Assert.Contains("mountainman", f2[0].Text);
            Assert.False(f2[0].CanFix);
            // level 8's overlaps are between statics only (boulders, texture-mapped objects)
            Assert.DoesNotContain(LevelFixes.Find(s, 8, s.LoadLevel(8).Block.Data), f => f.Check == LevelFixes.Hitbox);
        }

        [Fact]
        public void Saving_warns_only_about_new_overlaps()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var com = Hitboxes.ReadComObj(s);
            var edits = new LevelEdits(s);
            var before = s.LoadLevel(1);
            Assert.Empty(Hitboxes.NewCreatureOverlaps(before, edits.Model(1), com));
            // put one creature right on another
            var m = edits.Model(1);
            var a = m.Objects.First(o => !o.Free && o.OnTile && o.IsCritter);
            var b = m.Objects.First(o => !o.Free && o.OnTile && o.IsCritter && o.Index != a.Index && (o.TileX != a.TileX || o.TileY != a.TileY));
            edits.For(1).MoveObject(b.Index, a.TileX, a.TileY, a.FineX, a.FineY, true);
            var added = Hitboxes.NewCreatureOverlaps(before, edits.Model(1), com);
            Assert.Contains(added, o => new[] { o.A.Index, o.B.Index }.OrderBy(i => i).SequenceEqual(new[] { a.Index, b.Index }.OrderBy(i => i)));
            output.WriteLine(string.Join("\n", added.Select(o => Hitboxes.Describe(s, o))));
        }

        [Fact]
        public void The_brush_reaches_solid_rock_behind_a_wall_and_from_above()
        {
            Func<int, int, bool> solid = (x, y) => x == 11;
            // a wall hit at x = 11 looking east (the wall belongs to the open tile 10): the rock behind it
            var wall = new PickResult { Object = -1, Tile = 10 + 64 * 5, Kind = 1, Point = new Vector3(11f, 1f, 5.5f), Direction = new Vector3(1, 0, 0), Origin = new Vector3(9, 1, 5.5f), Distance = 2f };
            Assert.Equal(11 + 64 * 5, Picking.TileForBrush(wall, solid));
            // a floor hit nearer than any rock is the tile hit
            var floor = new PickResult { Object = -1, Tile = 10 + 64 * 5, Kind = 0, Point = new Vector3(10.5f, 0.5f, 5.5f), Origin = new Vector3(10.5f, 3.5f, 5.5f), Direction = new Vector3(0, -1, 0), Distance = 3f };
            Assert.Equal(10 + 64 * 5, Picking.TileForBrush(floor, solid));
            // looking over a floor, the rock in front of it wins (it has no top face, but it stands up to the ceiling)
            var dir = Vector3.Normalize(new Vector3(1, -0.2f, 0));
            var far = new PickResult { Object = -1, Tile = 14 + 64 * 5, Kind = 0, Origin = new Vector3(9.5f, 3.9f, 5.5f), Direction = dir, Distance = 15f };
            Assert.Equal(11 + 64 * 5, Picking.TileForBrush(far, solid));
            // the camera inside rock (behind a wall) skips that rock
            var inside = new PickResult { Object = -1, Tile = -1, Kind = -1, Origin = new Vector3(11.5f, 1f, 5.5f), Direction = new Vector3(-1, 0, 0), Distance = float.MaxValue };
            Assert.Equal(0 + 64 * 5, Picking.TileForBrush(inside, (x, y) => x == 11 || x == 0));
            // a wall hit with open ground behind it stays the tile hit
            var step = wall; step.Point = new Vector3(12f, 0.5f, 5.5f); step.Tile = 11 + 64 * 5;
            Assert.Equal(11 + 64 * 5, Picking.TileForBrush(step, (x, y) => false));
            // nothing hit, looking down from above: the rock at the top of the column
            var none = PickResult.None; none.Origin = new Vector3(11.5f, 10f, 20.5f); none.Direction = new Vector3(0, -1, 0);
            Assert.Equal(11 + 64 * 20, Picking.TileForBrush(none, solid));
            // over open ground: the tile at floor level
            none.Origin = new Vector3(3.5f, 10f, 7.5f);
            Assert.Equal(3 + 64 * 7, Picking.TileForBrush(none, solid));
        }

        [Fact]
        public void Objects_go_on_the_new_floor_following_slopes_and_wall_things_stay()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var s = GameSession.Open(g);
            var edits = new LevelEdits(s);
            var e = edits.For(1);
            var m = e.Model;
            // an open flat tile with an item on it
            var item = m.Objects.First(o => !o.Free && o.OnTile && o.Item < 0x40 && m.Tiles[o.TileX, o.TileY].Type == TileType.Open);
            int x = item.TileX, y = item.TileY;
            e.SetTileField(x, y, "Floor height", Math.Min(15, m.Tiles[x, y].Height + 3));
            Assert.NotEqual(e.FloorZ(x, y, item.FineX, item.FineY), e.Model.Objects[item.Index].Z);   // under the new floor
            Assert.True(e.DropToFloor(x, y) >= 1);
            Assert.Equal(e.FloorZ(x, y, item.FineX, item.FineY), e.Model.Objects[item.Index].Z);
            Assert.Equal(0, e.DropToFloor(x, y));                                                      // already there
            // a slope: the floor rises across the tile, so two fine positions get different heights
            e.SetTileField(x, y, "Type", (int)TileType.SlopeUpN);
            e.DropToFloor(x, y);
            var o2 = e.Model.Objects[item.Index];
            Assert.Equal(e.FloorZ(x, y, o2.FineX, o2.FineY), o2.Z);
            Assert.NotEqual(e.FloorZ(x, y, 3, 0), e.FloorZ(x, y, 3, 7));
            // switches, writing and the like (0x160 on) keep their height
            var wallThing = m.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.Item >= 0x160 && o.Item < 0x180 && !m.Tiles[o.TileX, o.TileY].IsSolid && o.Z != e.FloorZ(o.TileX, o.TileY, o.FineX, o.FineY));
            if (wallThing != null)
            {
                int z = wallThing.Z;
                e.DropToFloor(wallThing.TileX, wallThing.TileY);
                Assert.Equal(z, e.Model.Objects[wallThing.Index].Z);
            }
            e.Undo(); e.Undo(); e.Undo(); e.Undo();
        }
    }
}
