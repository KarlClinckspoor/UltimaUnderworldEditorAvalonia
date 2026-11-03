using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The properties panel's folding, a creature's lists and inventory button, objects on a tile, and facing arrows.</summary>
    public class PanelTweaksTests
    {
        [AvaloniaFact]
        public void Sections_fold_creatures_get_lists_and_an_add_button()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            Assert.True(vm.ObjectOpen);
            Assert.StartsWith("▾", vm.ObjectHeader);
            vm.ObjectOpen = false;
            Assert.StartsWith("▸", vm.ObjectHeader);
            vm.ObjectOpen = true;
            vm.TileOpen = false;
            Assert.StartsWith("▸", vm.TileHeader);
            vm.TileOpen = true;

            var bat = vm.Level.Objects.First(o => !o.Free && o.OnTile && o.IsCritter);
            vm.Select(bat.Index, bat.TileX, bat.TileY, true);
            Ui.Pump();
            var goal = vm.ObjectRows.First(r => r.Name == "Goal");
            Assert.True(goal.HasChoices);
            Assert.Contains(goal.Choices, c => c.Value == 5 && c.Label.Contains("attack"));
            Assert.False(string.IsNullOrEmpty(goal.ValueNote));
            var att = vm.ObjectRows.First(r => r.Name == "Attitude");
            Assert.Equal(4, att.Choices.Count);
            Assert.True(vm.ObjectRows.First(r => r.Name == "Link").CanAddInside);
            // a plain item with a quantity has no (+)
            var coin = vm.Level.Objects.FirstOrDefault(o => !o.Free && o.OnTile && o.IsQuantity && !o.IsTrapOrTrigger && !o.IsMobile);
            if (coin != null)
            {
                vm.Select(coin.Index, coin.TileX, coin.TileY, true);
                Ui.Pump();
                Assert.False(vm.ObjectRows.First(r => r.Name == "Link").CanAddInside);
            }
            w.Close();
        }

        [AvaloniaFact]
        public void Objects_on_a_tile_are_listed_and_the_hidden_one_can_be_dragged()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var m = vm.Level;
            int tx = -1, ty = -1;
            for (int y = 0; y < 64 && tx < 0; y++)
                for (int x = 0; x < 64; x++)
                    if (m.TileObjects(x, y).Count(o => !o.IsTrapOrTrigger) >= 2) { tx = x; ty = y; break; }
            Assert.True(tx >= 0);
            var objs = m.TileObjects(tx, ty);
            var front = objs[0];
            var hidden = objs[1];
            var items = w.ContextItems(new PickResult { Object = front.Index, Tile = tx + 64 * ty });
            var list = items.OfType<MenuItem>().First(i => (i.Header as string ?? "").StartsWith("Objects on this tile"));
            var entry = list.Items.OfType<MenuItem>().First(i => (i.Header as string).Contains("#" + hidden.Index + " "));
            entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Ui.Pump();
            Assert.Equal(hidden.Index, vm.SelectedObject);
            // a press on the front object of that tile now grabs the hidden one
            Assert.True(w.FindControl<LevelView>("View").CanDrag(new PickResult { Object = front.Index, Tile = tx + 64 * ty }));
            // but not on another tile
            Assert.False(w.FindControl<LevelView>("View").CanDrag(new PickResult { Object = -1, Tile = ((tx + 3) % 64) + 64 * ty }));
            // facing arrows switch on and off
            vm.ShowFacing = true;
            Ui.Pump();
            vm.ShowFacing = false;
            w.Close();
        }
    }
}
