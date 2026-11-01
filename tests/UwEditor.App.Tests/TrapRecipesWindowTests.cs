using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Edit ▸ Trap recipes through its window: picking in the view, Make, selection, undo and saving.</summary>
    public class TrapRecipesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-recipes-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                string src = Path.Combine(from, "DATA");
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static void FreeTile(LevelModel m, out int x, out int y)
        {
            for (y = 2; y < 62; y++)
                for (x = 2; x < 62; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && m.TileObjects(x, y).Count == 0) return;
            throw new Exception("no free tile");
        }

        static void Press(Control c) { c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Ui.Pump(); }

        [AvaloniaTheory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_plate_with_a_message_is_picked_in_the_view_made_selected_and_saved_with_its_string(string game)
        {
            string g = game == "UW" ? Uw1 : Uw2;
            if (g == null) return;
            using (var copy = new GameCopy(g))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var vm = w.ViewModel;
                var rw = w.ShowTrapRecipes();
                Assert.NotNull(rw);
                Assert.Same(rw, w.ShowTrapRecipes());          // one window
                rw.ShowRecipe(2);
                Ui.Pump();
                Assert.Equal(2, rw.Form.Recipe);
                int x, y;
                FreeTile(vm.Level, out x, out y);
                // "Pick in view", then a click on the square
                var pick = rw.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "Pick in view");
                Press(pick);
                Assert.True(w.IsPickingPlace);
                w.CompletePlacePick(-1, x + 64 * y);
                Ui.Pump();
                Assert.False(w.IsPickingPlace);
                var p = rw.Form.Place("plate");
                Assert.NotNull(p);
                Assert.Equal(x, p.X); Assert.Equal(y, p.Y); Assert.Equal(1, p.Level);
                rw.Form.Kind = RecipeTrap.Message;
                rw.Form.Text = "The floor creaks.";
                var strings = w.Strings();
                int n9 = strings.Get(9).Count;
                var r = rw.Make();
                Assert.True(r != null, rw.Status);
                Ui.Pump();
                Assert.Contains("Stepping on", rw.Status);
                Assert.Equal(r.Trigger, vm.SelectedObject);
                Assert.True(vm.HasLevelChanges);
                var m = vm.Level;
                Assert.True(m.CheckOk);
                var chain = Triggers.Chain(m, m.Objects[r.Trigger]);
                Assert.Equal(TrapRecipes.TextTrap, chain.Steps[0].Item);
                Assert.Equal(n9, chain.Steps[0].Quality << 5 | chain.Steps[0].Owner);
                Assert.Equal("The floor creaks.", strings.Get(9, n9));
                Assert.Contains("Recipe", vm.UndoText);
                // a refusal is told, not thrown: no second trigger in an object, no plate without a square
                rw.Form.Places.Clear();
                Assert.Null(rw.Make());
                Assert.Contains("Choose", rw.Status);
                // saved: the level and the string
                Assert.True(w.SaveAll(true));
                var s2 = GameSession.Open(copy.Folder);
                var m2 = s2.LoadLevel(1);
                Assert.True(m2.CheckOk);
                Assert.Equal(TrapRecipes.TextTrap, Triggers.Chain(m2, m2.Objects[r.Trigger]).Steps[0].Item);
                Assert.Equal("The floor creaks.", s2.Strings.Get(9, n9));
                rw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void A_lever_for_a_door_from_the_selection_undoes_in_one_step()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var before = (byte[])vm.Editor.Data.Clone();
            var rw = w.ShowTrapRecipes();
            rw.ShowRecipe(0);
            Ui.Pump();
            var m = vm.Level;
            var door = m.Objects.First(o => !o.Free && o.OnTile && o.Item >= 0x140 && o.Item < 0x148 && !m.Tiles[o.TileX, o.TileY].IsSolid);
            vm.Select(door.Index, door.TileX, door.TileY, true);
            var useSel = rw.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "Use selection");
            Press(useSel);
            Assert.Equal(door.Index, rw.Form.Place("door").Object);
            int wx = -1, wy = -1;
            for (int y = 2; y < 62 && wx < 0; y++)
                for (int x = 2; x < 62; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && TrapRecipes.IsWall(m, x, y, WallSide.East)) { wx = x; wy = y; break; }
            rw.Form.Places["wall"] = new PickedPlace { Level = 1, X = wx, Y = wy };
            rw.Form.Side = WallSide.East;
            var r = rw.Make();
            Assert.True(r != null, rw.Status);
            Assert.Equal(r.Trigger, vm.SelectedObject);
            Assert.Equal(r.Lever, vm.Level.Objects[r.Trigger].Parent);
            Assert.Equal(7, vm.Level.Objects[r.Lever].FineX);
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            rw.Close();
            w.Close();
        }
    }
}
