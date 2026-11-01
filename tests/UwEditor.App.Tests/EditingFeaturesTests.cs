using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Copy and paste, several objects at once, the right-click menu, the brushes, the 2D map, undo history, the world check and the play test.</summary>
    public class EditingFeaturesTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        static (int X, int Y) OpenTile(LevelModel m, int skip = 0)
        {
            return Enumerable.Range(0, 64 * 64).Select(i => (i % 64, i / 64)).Where(t => !m.Tiles[t.Item1, t.Item2].IsSolid && t.Item1 > 1 && t.Item2 > 1).Skip(skip).First();
        }

        [AvaloniaFact]
        public void Copy_paste_duplicate_and_deleting_several_objects_are_single_undo_steps()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var bag = vm.Level.Objects.First(o => !o.Free && o.OnTile && !o.IsMobile && !o.IsTrapOrTrigger && o.Children.Count >= 2);
            vm.Select(bag.Index, -1, -1, true);
            Assert.True(w.CopySelection(false));
            var t = OpenTile(vm.Level, 30);
            var made = w.PasteClip(t.X, t.Y, -1);
            Assert.Single(made);
            Assert.Equal(bag.Children.Count, vm.Level.Objects[made[0]].Children.Count);
            Assert.Contains("Paste", vm.UndoText);
            var dup = w.Duplicate();
            Assert.Single(dup);
            Assert.Equal(t.X, vm.Level.Objects[dup[0]].TileX);
            // two at once, deleted as one step
            vm.Select(made[0], -1, -1, true);
            w.AddToSelection(new[] { made[0], dup[0] }, null);
            w.DeleteSelected();
            Assert.True(vm.Level.Objects[made[0]].Free && vm.Level.Objects[dup[0]].Free);
            w.Undo();
            Assert.False(vm.Level.Objects[made[0]].Free || vm.Level.Objects[dup[0]].Free);
            bool ok; vm.Editor.Check(out ok); Assert.True(ok);
            w.Close();
        }

        [AvaloniaFact]
        public void The_right_click_menu_offers_the_commands_for_objects_and_tiles()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var o = vm.Level.Objects.First(x => !x.Free && x.OnTile && !x.IsMobile);
            var items = w.ContextItems(new PickResult { Object = o.Index, Tile = o.TileX + 64 * o.TileY });
            var headers = items.OfType<MenuItem>().Select(m => (m.Header ?? "").ToString().Replace("_", "")).ToList();
            foreach (var h in new[] { "Copy", "Cut", "Duplicate", "Delete", "Add an object here…", "Use as the brush template", "Fill from here with the brush", "Play-test from here…" })
                Assert.Contains(h, headers);
            Assert.Equal(o.Index, vm.SelectedObject);
            w.Close();
        }

        [AvaloniaFact]
        public void The_template_brush_fill_and_paint_selection_paint_many_tiles_as_one_step()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var a = OpenTile(vm.Level);
            vm.Select(-1, a.X, a.Y, true);
            vm.PaintMode = 5; vm.PaintHeight = 9;
            var reach = w.WalkableFrom(a.X, a.Y);
            Assert.True(reach.Count > 5);
            Assert.Equal(reach.Count, w.FillFrom(a.X, a.Y));
            vm.FlushRefresh();
            Assert.All(reach, t => Assert.Equal(9, vm.Level.Tiles[t.X, t.Y].Height));
            w.Undo();
            Assert.NotEqual(9, vm.Level.Tiles[reach.Last().X, reach.Last().Y].Height);
            // the template from a tile, painted on two selected tiles
            var b = OpenTile(vm.Level, 50);
            Assert.True(vm.TakeTemplate(b.X, b.Y));
            vm.PaintMode = 6;
            var c = OpenTile(vm.Level, 200); var d = OpenTile(vm.Level, 300);
            w.AddToSelection(null, new[] { c.X + 64 * c.Y, d.X + 64 * d.Y });
            Assert.Equal(2, w.PaintTiles(w.ChosenTiles(), "Paint selection"));
            vm.FlushRefresh();
            Assert.Equal(vm.Level.Tiles[b.X, b.Y].Height, vm.Level.Tiles[d.X, d.Y].Height);
            Assert.Equal(vm.Level.Tiles[b.X, b.Y].FloorIndex, vm.Level.Tiles[c.X, c.Y].FloorIndex);
            w.Close();
        }

        [AvaloniaFact]
        public void The_2D_map_looks_straight_down_without_perspective_and_back()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var view = w.FindControl<LevelView>("View");
            view.LookAt(view.Camera.Target, 9f, 33f, 40f);
            bool ceiling = w.ViewModel.ShowCeiling;
            w.ToggleMap();
            Assert.True(view.TopDown);
            Assert.True(view.Camera.Orthographic);
            Assert.Equal(0f, view.Camera.Yaw);
            Assert.False(w.ViewModel.ShowCeiling);
            w.ToggleMap();
            Assert.False(view.Camera.Orthographic);
            Assert.Equal(33f, view.Camera.Yaw, 3);
            Assert.Equal(ceiling, w.ViewModel.ShowCeiling);
            var png = MapPicture.Png(w.ViewModel.Level, 8);
            Assert.True(png.Length > 1000);
            w.Close();
        }

        [AvaloniaFact]
        public void Undo_history_lists_the_steps_and_goes_back_to_one()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var t = OpenTile(vm.Level, 10);
            var before = (byte[])vm.Edits.For(vm.Level.Level).Data.Clone();
            for (int i = 0; i < 3; i++) vm.Edit(e => e.SetTileField(t.X + i, t.Y, "Floor height", 3 + i));
            vm.FlushRefresh();
            var h = w.ShowUndoHistory();
            Ui.Pump();
            var steps = vm.Edits.For(vm.Level.Level).UndoLabels.Count;
            Assert.True(steps >= 1);
            Assert.Contains(h.Rows, r => r.Back == 0);
            vm.Edits.For(vm.Level.Level).Step(steps);
            Assert.Equal(before, vm.Edits.For(vm.Level.Level).Data);
            h.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_world_check_window_lists_every_level_and_the_play_test_makes_its_copy()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var wc = w.ShowWorldCheck();
            Ui.Pump();
            Assert.True(wc.Findings.Count(f => f.Kind == "objects") >= 8);
            wc.Close();
            string to = Path.Combine(Path.GetTempPath(), "uwedit-playwin-" + Guid.NewGuid().ToString("N"));
            try
            {
                w.PlayTestFolder = to;
                w.PlayTestLaunch = false;
                var t = OpenTile(w.ViewModel.Level, 5);
                var plan = w.PlayTest(t.X, t.Y).Result;
                Assert.NotNull(plan);
                Assert.True(File.Exists(Path.Combine(to, PlayTest.Marker)));
            }
            finally { if (Directory.Exists(to)) Directory.Delete(to, true); }
            w.Close();
        }
    }
}
