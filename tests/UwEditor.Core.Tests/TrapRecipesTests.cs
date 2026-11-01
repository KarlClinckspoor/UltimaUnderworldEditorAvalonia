using System;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    /// <summary>Edit ▸ Trap recipes: each recipe on a shipped level of both games keeps the list check, makes the chain it says, and undoes exactly.</summary>
    public class TrapRecipesTests
    {
        readonly ITestOutputHelper output;
        public TrapRecipesTests(ITestOutputHelper output) { this.output = output; }

        static GameSession Open(string game) { string g = GameData.Game(game); return g == null ? null : GameSession.Open(g); }

        /// <summary>An empty open flat tile with rock on its north side and room around it, skipping some.</summary>
        static void FreeTile(LevelModel m, out int x, out int y, int skip = 0, bool wall = false)
        {
            for (y = 2; y < 62; y++)
                for (x = 2; x < 62; x++)
                {
                    var t = m.Tiles[x, y];
                    if (t.Type != TileType.Open || m.TileObjects(x, y).Count > 0) continue;
                    if (wall && !TrapRecipes.IsWall(m, x, y, WallSide.North)) continue;
                    if (skip-- > 0) continue;
                    return;
                }
            throw new Exception("no free tile");
        }

        static ObjectInfo Door(LevelModel m) { return m.Objects.First(o => !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x148 && !m.Tiles[o.TileX, o.TileY].IsSolid); }

        /// <summary>A single plain object lying on a tile (quantity 1).</summary>
        static ObjectInfo Single(LevelModel m)
        {
            return m.Objects.First(o => !o.Free && o.OnTile && o.Index >= 256 && o.Item < 0x100 && o.IsQuantity && o.Link == 1 && !o.Enchanted && (ItemCategories.Of(o.Item, m.Session.Kind) & ItemCategory.Container) == 0);
        }

        sealed class Before
        {
            public byte[] Data, Tex;
            public Before(LevelEditor e) { Data = (byte[])e.Data.Clone(); Tex = e.TextureBytes == null ? null : (byte[])e.TextureBytes.Clone(); }
            public void Same(LevelEditor e)
            {
                Assert.Equal(Data, e.Data);
                if (Tex != null) Assert.Equal(Tex, e.TextureBytes);
            }
        }

        void Verify(LevelEditor e, RecipeResult r, int tx, int ty)
        {
            output.WriteLine(r.Summary);
            bool ok;
            output.WriteLine(e.Check(out ok));
            Assert.True(ok);
            var m = e.Model;
            Assert.True(e.Model.CheckOk);
            var trig = m.Objects[r.Trigger];
            Assert.True(Triggers.IsTrigger(trig.Item));
            Assert.Contains(Triggers.TriggersOf(m), o => o.Index == r.Trigger);
            var chain = Triggers.Chain(m, trig);
            Assert.Equal(r.Trap, chain.Steps[0].Index);
            var target = Triggers.Targets(m, trig).Single();
            Assert.Equal(tx, target.X);
            Assert.Equal(ty, target.Y);
            Assert.Contains(Triggers.TriggersReaching(m, m.Objects[r.Trap]), o => o.Index == r.Trigger);
            // the trap counts its trigger (flags 9-12), lies on the target square, top level
            var trap = m.Objects[r.Trap];
            int w0 = trap.Record[0] | trap.Record[1] << 8;
            Assert.Equal(1, (w0 >> 9) & 0xF);
            Assert.True(trap.OnTile);
            Assert.Equal(tx, trap.TileX); Assert.Equal(ty, trap.TileY);
            Assert.True(trig.IsQuantity);
            Assert.Equal(4, ((trig.Record[0] | trig.Record[1] << 8) >> 9) & 4);   // flag 11: the player
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_new_lever_on_a_wall_toggles_a_door_and_undo_restores_the_level(string game)
        {
            var s = Open(game); if (s == null) return;
            var e = new LevelEditor(s, 1);
            var before = new Before(e);
            var door = Door(e.Model);
            int x, y; FreeTile(e.Model, out x, out y, 0, true);
            var r = TrapRecipes.LeverDoor(e, new LeverDoorOptions { Door = door.Index, WallX = x, WallY = y, Action = DoorAction.Toggle });
            Verify(e, r, door.TileX, door.TileY);
            var m = e.Model;
            var lever = m.Objects[r.Lever];
            Assert.Equal(0x175, lever.Item);
            Assert.False(lever.IsQuantity);
            Assert.Equal(7, lever.FineY);                      // on the north wall
            Assert.Equal(0, lever.Heading);
            Assert.Equal(r.Lever, m.Objects[r.Trigger].Parent);
            Assert.Equal(TrapRecipes.UseTrigger, m.Objects[r.Trigger].Item);
            Assert.Equal(TrapRecipes.DoorTrap, m.Objects[r.Trap].Item);
            Assert.Equal(3, m.Objects[r.Trap].Quality);
            Assert.True((((m.Objects[r.Trigger].Record[0] | m.Objects[r.Trigger].Record[1] << 8) >> 9) & 2) != 0);   // reusable
            if (s.IsUw2) Assert.Equal(0, m.Objects[r.Trap].Owner);   // the lock is left alone
            else
            {
                // UW1 swaps the lock: the trap carries a copy of the door's own, if it has one
                var lk = door.Children.Select(c => m.Objects[c]).FirstOrDefault(c => c.Item == 0x10F);
                if (lk != null) Assert.Equal(lk.Link, m.Objects[m.Objects[r.Trap].Children.Single()].Link);
            }
            Assert.Contains("one", e.UndoLabel.Replace("lever and door", "one"));
            e.Undo();
            before.Same(e);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void An_existing_switch_can_open_a_door_and_a_refused_recipe_changes_nothing(string game)
        {
            var s = Open(game); if (s == null) return;
            LevelEditor e = null;
            ObjectInfo sw = null;
            for (int l = 1; l <= (s.IsUw2 ? 80 : 9) && sw == null; l++)
            {
                LevelModel lm;
                try { lm = s.LoadLevel(l); } catch (Exception) { continue; }
                if (!lm.Present || !lm.Objects.Any(o => !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x148 && !lm.Tiles[o.TileX, o.TileY].IsSolid)) continue;
                sw = lm.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.Item >= 0x170 && o.Item < 0x180 && (o.IsQuantity ? o.Link <= 1 : o.Children.Count == 0));
                if (sw != null) e = new LevelEditor(s, l);
            }
            Assert.NotNull(sw);
            var before = new Before(e);
            var door = Door(e.Model);
            var r = TrapRecipes.LeverDoor(e, new LeverDoorOptions { Door = door.Index, Lever = sw.Index, Action = DoorAction.Open, Unlock = true });
            Verify(e, r, door.TileX, door.TileY);
            Assert.Equal(sw.Index, e.Model.Objects[r.Trigger].Parent);
            Assert.Equal(-1, r.Lever);
            if (s.IsUw2) Assert.Equal(1, e.Model.Objects[r.Trap].Owner);
            else Assert.Empty(e.Model.Objects[r.Trap].Children);
            // a second time: the switch holds a trigger now, refused before anything changes
            var after = new Before(e);
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.LeverDoor(e, new LeverDoorOptions { Door = door.Index, Lever = sw.Index }));
            after.Same(e);
            e.Undo();
            before.Same(e);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Teleports_there_and_back_on_one_level(string game)
        {
            var s = Open(game); if (s == null) return;
            var e = new LevelEditor(s, 1);
            var before = new Before(e);
            int ax, ay, bx, by, cx, cy, dx, dy;
            FreeTile(e.Model, out ax, out ay, 0); FreeTile(e.Model, out bx, out by, 5);
            FreeTile(e.Model, out cx, out cy, 9); FreeTile(e.Model, out dx, out dy, 13);
            var r = TrapRecipes.Teleport(e, null, new TeleportOptions { FromX = ax, FromY = ay, ToX = bx, ToY = by, Back = true, BackFromX = cx, BackFromY = cy, BackToX = dx, BackToY = dy });
            Verify(e, r, ax, ay);
            Verify(e, r.Back, cx, cy);
            var m = e.Model;
            var tp = m.Objects[r.Trap];
            Assert.Equal(TrapRecipes.TeleportTrap, tp.Item);
            Assert.Equal(0, tp.Z);
            var dest = Triggers.Targets(m, tp).First();
            Assert.Equal(1, dest.Level); Assert.Equal(bx, dest.X); Assert.Equal(by, dest.Y);
            Assert.Equal(TrapRecipes.MoveTrigger, m.Objects[r.Trigger].Item);
            Assert.Equal(m.Tiles[ax, ay].Height * 8, m.Objects[r.Trigger].Z);
            e.Undo();
            before.Same(e);
            // arriving on the square that sends the player back is refused
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.Teleport(e, null, new TeleportOptions { FromX = ax, FromY = ay, ToX = bx, ToY = by, Back = true, BackFromX = bx, BackFromY = by, BackToX = dx, BackToY = dy }));
            before.Same(e);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_teleport_to_another_level_and_back(string game)
        {
            var s = Open(game); if (s == null) return;
            var e1 = new LevelEditor(s, 1);
            var e2 = new LevelEditor(s, 2);
            var b1 = new Before(e1); var b2 = new Before(e2);
            int ax, ay, bx, by, cx, cy, dx, dy;
            FreeTile(e1.Model, out ax, out ay, 0); FreeTile(e2.Model, out bx, out by, 0);
            FreeTile(e2.Model, out cx, out cy, 4); FreeTile(e1.Model, out dx, out dy, 4);
            var r = TrapRecipes.Teleport(e1, e2, new TeleportOptions { FromX = ax, FromY = ay, ToX = bx, ToY = by, Back = true, BackFromX = cx, BackFromY = cy, BackToX = dx, BackToY = dy });
            Verify(e1, r, ax, ay);
            Verify(e2, r.Back, cx, cy);
            Assert.Equal(2, e1.Model.Objects[r.Trap].Z);
            Assert.Equal(1, e2.Model.Objects[r.Back.Trap].Z);
            var t = Triggers.Targets(e1.Model, e1.Model.Objects[r.Trap]).First();
            Assert.Equal(2, t.Level); Assert.Equal(bx, t.X);
            e1.Undo(); e2.Undo();
            b1.Same(e1); b2.Same(e2);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Plates_run_damage_message_variable_creature_door_and_delete_traps(string game)
        {
            var s = Open(game); if (s == null) return;
            var e = new LevelEditor(s, 1);
            var strings = StringsEditor.Open(s);
            int n9 = strings.Get(9).Count;
            var before = new Before(e);
            int x, y; FreeTile(e.Model, out x, out y, 2);
            int cx, cy; FreeTile(e.Model, out cx, out cy, 7);
            var door = Door(e.Model);
            var rock = Single(e.Model);
            var actions = new[]
            {
                new TrapAction { Kind = RecipeTrap.Damage, Damage = 7 },
                new TrapAction { Kind = RecipeTrap.Message, Text = "A cold wind blows." },
                new TrapAction { Kind = RecipeTrap.SetVariable, Variable = s.IsUw2 ? 0x105 : 12, Operation = 2, Value = s.IsUw2 ? 1 : 37 },
                new TrapAction { Kind = RecipeTrap.CreateObject, Template = ObjectSpec.Default(0x44, s.Kind), X = cx, Y = cy },
                new TrapAction { Kind = RecipeTrap.Door, Door = door.Index, DoorAction = DoorAction.Open },
                new TrapAction { Kind = RecipeTrap.DeleteObject, Delete = rock.Index },
            };
            foreach (var a in actions)
            {
                int active = new LevelBlock(e.Data, s.Kind).ActiveCount;
                var r = TrapRecipes.Plate(e, x, y, false, a, null, strings);
                var m = e.Model;
                int tx = a.Kind == RecipeTrap.CreateObject ? cx : a.Kind == RecipeTrap.Door ? door.TileX : x;
                int ty = a.Kind == RecipeTrap.CreateObject ? cy : a.Kind == RecipeTrap.Door ? door.TileY : y;
                Verify(e, r, tx, ty);
                Assert.Equal(x, m.Objects[r.Trigger].TileX);
                var trap = m.Objects[r.Trap];
                switch (a.Kind)
                {
                    case RecipeTrap.Damage: Assert.Equal(7, trap.Quality); Assert.Equal(0, trap.Owner); break;
                    case RecipeTrap.Message:
                        Assert.Equal(n9, r.StringIndex);
                        Assert.Equal(n9, trap.Quality << 5 | trap.Owner);
                        Assert.Equal("A cold wind blows.", strings.Get(9, n9));
                        break;
                    case RecipeTrap.SetVariable:
                        Assert.Equal(a.Variable, Triggers.VariableOf(s, trap));
                        if (s.IsUw2) Assert.Equal(1, (trap.Quality << 6) + trap.Owner);
                        else Assert.Equal(37, (trap.Quality << 5 | trap.Owner) << 3 | trap.FineY);
                        Assert.Equal(2, trap.Heading);
                        break;
                    case RecipeTrap.CreateObject:
                    {
                        var chain = Triggers.Chain(m, m.Objects[r.Trigger]);
                        var tmp = chain.Steps[1];
                        Assert.Equal(0x44, tmp.Item);
                        Assert.Equal(r.Trap, tmp.Parent);
                        Assert.False(trap.IsQuantity);
                        Assert.Equal(cx, (int)tmp.Record[0x16 + 1] >> 2 & 0x3F);   // home x
                        // the template stays off the active list
                        Assert.Equal(active, new LevelBlock(e.Data, s.Kind).ActiveCount);
                        Assert.DoesNotContain(tmp.Index, Enumerable.Range(0, active).Select(k => new LevelBlock(e.Data, s.Kind).Active(k)));
                        break;
                    }
                    case RecipeTrap.Door: Assert.Equal(1, trap.Quality); break;
                    case RecipeTrap.DeleteObject:
                        Assert.True(trap.IsQuantity);
                        Assert.Equal(rock.Index, trap.Link);
                        Assert.Equal(rock.TileX, trap.Quality); Assert.Equal(rock.TileY, trap.Owner);
                        Assert.Equal(0, ((m.Objects[r.Trigger].Record[0] | m.Objects[r.Trigger].Record[1] << 8) >> 9) & 2);   // used once
                        break;
                }
                output.WriteLine(Triggers.Short(s, trap));
            }
            for (int i = 0; i < actions.Length; i++) e.Undo();
            before.Same(e);
        }

        [Fact]
        public void An_underworld_ii_pressure_plate_starts_up_at_the_floor()
        {
            var s = Open("UW2"); if (s == null) return;
            var e = new LevelEditor(s, 1);
            var before = new Before(e);
            int x, y; FreeTile(e.Model, out x, out y, 3);
            var r = TrapRecipes.Plate(e, x, y, true, new TrapAction { Kind = RecipeTrap.Damage, Damage = 3 });
            Verify(e, r, x, y);
            var t = e.Model.Objects[r.Trigger];
            Assert.Equal(TrapRecipes.Uw2PressureTrigger, t.Item);
            Assert.Equal(0, t.FineY);
            Assert.Equal(e.Model.Tiles[x, y].Height * 8, t.Z);
            e.Undo();
            before.Same(e);
            var s1 = Open("UW");
            if (s1 != null) Assert.Throws<InvalidOperationException>(() => TrapRecipes.Plate(new LevelEditor(s1, 1), x, y, true, new TrapAction { Kind = RecipeTrap.Damage }));
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Picking_up_or_using_an_object_shows_a_message_or_sets_a_variable(string game)
        {
            var s = Open(game); if (s == null) return;
            var e = new LevelEditor(s, 1);
            var strings = StringsEditor.Open(s);
            var before = new Before(e);
            var obj = Single(e.Model);
            var r = TrapRecipes.ObjectTrigger(e, obj.Index, ObjectEvent.PickUp, new TrapAction { Kind = RecipeTrap.Message, Text = "It hums." }, null, strings);
            Verify(e, r, obj.TileX, obj.TileY);
            var m = e.Model;
            Assert.False(m.Objects[obj.Index].IsQuantity);
            Assert.Equal(obj.Index, m.Objects[r.Trigger].Parent);
            Assert.Equal(TrapRecipes.PickUpTrigger, m.Objects[r.Trigger].Item);
            Assert.Equal("It hums.", strings.Get(9, r.StringIndex));
            Assert.Contains("It hums", Triggers.Short(s, m.Objects[r.Trap]) + strings.Get(9, r.StringIndex));
            // a second trigger in the same object is refused; another object can have a use trigger
            Assert.Throws<InvalidOperationException>(() => TrapRecipes.ObjectTrigger(e, obj.Index, ObjectEvent.Use, new TrapAction { Kind = RecipeTrap.Damage }));
            var other = m.Objects.First(o => !o.Free && o.OnTile && o.Index >= 256 && o.Index != obj.Index && o.Item < 0x100 && o.IsQuantity && o.Link == 1 && !o.Enchanted && (ItemCategories.Of(o.Item, s.Kind) & ItemCategory.Container) == 0);
            var r2 = TrapRecipes.ObjectTrigger(e, other.Index, ObjectEvent.Use, new TrapAction { Kind = RecipeTrap.SetVariable, Variable = s.IsUw2 ? 9 : 5, QuestBit = false, Operation = 0, Value = 2 });
            Verify(e, r2, other.TileX, other.TileY);
            Assert.Equal(TrapRecipes.UseTrigger, e.Model.Objects[r2.Trigger].Item);
            // a stack is refused
            var stack = m.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.Index >= 256 && o.IsQuantity && o.Link > 1 && o.Link < 0x200 && !o.IsTrapOrTrigger);
            if (stack != null) Assert.Throws<InvalidOperationException>(() => TrapRecipes.ObjectTrigger(e, stack.Index, ObjectEvent.Use, new TrapAction { Kind = RecipeTrap.Damage }));
            e.Undo(); e.Undo();
            before.Same(e);
        }

        [Fact]
        public void A_batch_that_fails_half_way_leaves_the_level_and_its_history_as_they_were()
        {
            var s = Open("UW"); if (s == null) return;
            var e = new LevelEditor(s, 1);
            int x, y; FreeTile(e.Model, out x, out y);
            e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
            var before = new Before(e);
            string label = e.UndoLabel;
            Assert.Throws<InvalidOperationException>(() => e.Batch("two", () =>
            {
                e.AddObject(ObjectSpec.Default(0xA0, s.Kind), x, y);
                throw new InvalidOperationException("stop");
            }));
            before.Same(e);
            Assert.Equal(label, e.UndoLabel);
            Assert.False(e.CanRedo);
        }
    }
}
