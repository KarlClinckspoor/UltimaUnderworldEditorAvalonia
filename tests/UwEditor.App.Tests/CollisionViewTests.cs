using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>View ▸ Collision edges and faces, the editing messages over the view, painting solid rock and Objects to the floor.</summary>
    public class CollisionViewTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        [AvaloniaFact]
        public void Collision_toggles_add_lines_and_faces_to_the_view()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            Ui.Pump();
            int lines = view.LineFloats, marks = view.MarkerFloats;
            vm.ShowCollisionEdges = true;
            Ui.Pump();
            Assert.True(view.LineFloats > lines + 7 * 24 * 10, "edges drawn");
            Assert.Equal(marks, view.MarkerFloats);
            vm.ShowCollisionFaces = true;
            Ui.Pump();
            Assert.True(view.MarkerFloats > marks + 7 * 36 * 10, "faces drawn");
            vm.ShowCollisionEdges = false; vm.ShowCollisionFaces = false;
            Ui.Pump();
            Assert.Equal(lines, view.LineFloats);
            Assert.Equal(marks, view.MarkerFloats);
            w.Close();
        }

        [AvaloniaFact]
        public void A_long_message_does_not_change_the_views_size()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            vm.EditStatus = "";
            Ui.Pump();
            var size = view.Bounds.Size;
            vm.EditStatus = string.Concat(Enumerable.Repeat("A long message about what just happened in the level. ", 12));
            Ui.Pump();
            Assert.Equal(size, view.Bounds.Size);
            // the message is shown, over the view
            Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.EditStatus && t.IsEffectivelyVisible);
            w.Close();
        }

        [AvaloniaFact]
        public void Painting_reaches_a_solid_tile_and_can_put_objects_on_the_floor()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.ToolIndex = 2;
            vm.PaintMode = 4;
            Assert.True(vm.PaintsShape);
            // a solid tile next to an open one: paint it open through a wall hit on the open side
            var m = vm.Level;
            int sx = -1, sy = -1;
            for (int y = 1; y < 63 && sx < 0; y++)
                for (int x = 1; x < 62 && sx < 0; x++)
                    if (m.Tiles[x, y].Type == TileType.Open && m.Tiles[x + 1, y].IsSolid) { sx = x + 1; sy = y; }
            Assert.True(sx > 0);
            vm.PaintType = (int)TileType.Open;
            var hit = new PickResult { Object = -1, Tile = (sx - 1) + 64 * sy, Kind = 1, Point = new System.Numerics.Vector3(sx, 1f, sy + 0.5f), Direction = new System.Numerics.Vector3(1, 0, 0) };
            int tile = Picking.TileForBrush(hit, (x, y) => vm.Level.Tiles[x, y].IsSolid);
            Assert.Equal(sx + 64 * sy, tile);
            vm.Editor.BeginBatch("paint");
            vm.Paint(tile % 64, tile / 64);
            vm.Editor.EndBatch();
            vm.FlushRefresh();
            Assert.Equal(TileType.Open, vm.Level.Tiles[sx, sy].Type);
            // Objects to the floor: a raised floor brings the tile's items up with it
            var item = vm.Level.Objects.First(o => !o.Free && o.OnTile && o.Item < 0x40 && vm.Level.Tiles[o.TileX, o.TileY].Type == TileType.Open && vm.Level.Tiles[o.TileX, o.TileY].Height < 12);
            vm.PaintMode = 5;
            vm.PaintHeight = vm.Level.Tiles[item.TileX, item.TileY].Height + 2;
            vm.DropObjects = true;
            vm.Editor.BeginBatch("paint");
            vm.Paint(item.TileX, item.TileY);
            vm.Editor.EndBatch();
            vm.FlushRefresh();
            Assert.Contains("put on the floor", vm.EditStatus);
            var o2 = vm.Level.Objects[item.Index];
            Assert.Equal(vm.Editor.FloorZ(o2.TileX, o2.TileY, o2.FineX, o2.FineY), o2.Z);
            w.Close();
        }
    }
}
