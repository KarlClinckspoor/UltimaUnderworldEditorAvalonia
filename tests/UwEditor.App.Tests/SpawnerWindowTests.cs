using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Wandering-monster spawners in the Triggers window and the Wandering monsters recipe.</summary>
    public class SpawnerWindowTests
    {
        [AvaloniaFact]
        public void Spawners_are_labelled_and_the_recipe_makes_one()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(4);
            Ui.Pump();
            var tw = w.ShowTriggers();
            tw.Refresh();
            Ui.Pump();
            var group = tw.Model.Nodes.First(n => n.Header.Contains("wandering-monster spawners"));
            Assert.StartsWith("9 ", group.Header);   // six random monsters, a bloodworm and two giant tan rats
            Assert.Contains(group.Children, c => c.Index == 706 && c.Header.Contains("random monster") && c.Header.Contains("29 %"));
            Assert.DoesNotContain(tw.Model.Nodes.Where(n => n.Header.Contains("no trigger reaches")).SelectMany(n => n.Children), c => c.Index == 706);
            tw.Close();
            // the object panel says what it is
            var t = vm.Level.Objects[706];
            vm.Select(706, t.TileX, t.TileY, true);
            Ui.Pump();
            Assert.Contains("wandering-monster spawner", vm.ObjectHelp);

            var rw = w.ShowTrapRecipes();
            rw.ShowRecipe(4);
            Ui.Pump();
            Assert.Equal("Wandering monsters", TrapRecipes.Recipes[4].Name);
            var m = vm.Level;
            int x = -1, y = -1;
            for (int yy = 2; yy < 62 && x < 0; yy++)
                for (int xx = 2; xx < 62; xx++)
                    if (m.Tiles[xx, yy].Type == TileType.Open && m.TileObjects(xx, yy).Count == 0) { x = xx; y = yy; break; }
            rw.Form.Places["spawn"] = new PickedPlace { Level = 4, X = x, Y = y };
            rw.Form.SpawnChance = 40;
            var before = (byte[])vm.Editor.Data.Clone();
            var r = rw.Make();
            Assert.True(r != null, rw.Status);
            Assert.Equal(r.Trap, vm.SelectedObject);
            Assert.True(Triggers.IsSpawner(vm.Level, vm.Level.Objects[r.Trap]));
            Assert.Equal(Triggers.Adventurer, vm.Level.Objects[vm.Level.Objects[r.Trap].Link].Item);
            Assert.Contains("random monster", rw.Status);
            w.Undo();
            Assert.Equal(before, vm.Editor.Data);
            rw.Close();
            w.Close();
        }
    }
}
