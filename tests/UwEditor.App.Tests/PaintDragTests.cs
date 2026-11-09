using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>A paint stroke: press, drag over tiles, release paints every tile passed over (one undo step).</summary>
    public class PaintDragTests
    {
        readonly ITestOutputHelper output;
        public PaintDragTests(ITestOutputHelper output) { this.output = output; }

        static string Uw1 { get { return GameData.Game("UW"); } }

        [AvaloniaTheory]
        [InlineData(1, false, false)]   // light flag off, the press on a tile
        [InlineData(1, true, false)]    // the press where nothing is drawn: the stroke still opens
        [InlineData(1, false, true)]    // a click first, then a press and drag: not a double-click
        [InlineData(4, false, true)]    // tile type
        [InlineData(6, false, false)]   // tile template
        public void Dragging_paints_every_tile_passed_over(int mode, bool fromNothing, bool clickFirst)
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            vm.Select(-1, 32, 3, true);
            w.ToggleMap();                       // the 2D map: straight down, easy to aim
            Ui.Pump();
            vm.ToolIndex = 2;
            vm.PaintMode = mode;
            if (mode == 4) vm.PaintType = (int)TileType.OpenNE;
            Ui.Pump();
            // a row of screen points across the view, and the tiles under them
            var b = view.Bounds;
            var pts = Enumerable.Range(0, 41).Select(i => new Point(b.Width * (0.3 + 0.4 * i / 40.0), b.Height * 0.5)).ToList();
            var tiles = pts.Select(p => view.PickAt(p).Tile).ToList();
            // start on a tile, or on the first point that hits nothing
            int first = fromNothing ? tiles.FindIndex(x => x < 0) : tiles.FindIndex(x => x >= 0);
            Assert.True(first >= 0);
            pts = pts.Skip(first).ToList(); tiles = tiles.Skip(first).ToList();
            // light flag off: make the tiles under the row lit first, so turning it off shows
            if (mode == 1) { vm.Editor.BeginBatch("lit"); foreach (var x in tiles.Where(x => x >= 0).Distinct()) vm.Editor.SetLightBit(x % 64, x / 64, true); vm.Editor.EndBatch(); vm.FlushRefresh(); }
            output.WriteLine("view " + b + ", tiles under the row: " + string.Join(" ", tiles.Distinct().Select(t => t < 0 ? "-" : "(" + t % 64 + "," + t / 64 + ")")));
            var before = vm.Level.Tiles.Cast<TileInfo>().ToArray();
            Point Win(Point p) { return view.TranslatePoint(p, w).Value; }
            w.MouseMove(Win(pts[0]));
            if (clickFirst) { w.MouseDown(Win(pts[0]), MouseButton.Left); w.MouseUp(Win(pts[0]), MouseButton.Left); }
            w.MouseDown(Win(pts[0]), MouseButton.Left);
            // a quick move: only every fifth point arrives, the tiles between still get painted
            foreach (var p in pts.Where((p, i) => i % 5 == 0 || i == pts.Count - 1).Skip(1)) w.MouseMove(Win(p), RawInputModifiers.LeftMouseButton);
            w.MouseUp(Win(pts[pts.Count - 1]), MouseButton.Left);
            Ui.Pump();
            vm.FlushRefresh();
            var changed = new HashSet<int>();
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                var t0 = before[x * 64 + y]; var t1 = vm.Level.Tiles[x, y];
                if (t0.Type != t1.Type || t0.Light != t1.Light || t0.Height != t1.Height || t0.FloorIndex != t1.FloorIndex || t0.WallIndex != t1.WallIndex) changed.Add(x + 64 * y);
            }
            output.WriteLine("changed: " + string.Join(" ", changed.Select(t => "(" + t % 64 + "," + t / 64 + ")")) + " | " + vm.EditStatus);
            var passed = tiles.Where(t => t >= 0).Distinct().ToList();
            Assert.True(passed.Count >= 3, "the row crosses several tiles");
            Assert.True(changed.Count >= 2, "the drag painted more than the first tile: " + changed.Count);
            // every tile under the path that the brush changes was painted (a tile already as the brush leaves it is not "changed")
            Assert.All(passed.Where(x => !changed.Contains(x)), x => Assert.True(Same(before[(x % 64) * 64 + x / 64], vm.Level.Tiles[x % 64, x / 64]) && AlreadyBrush(vm, x % 64, x / 64), "(" + x % 64 + ", " + x / 64 + ") was skipped"));
            Assert.True(vm.CanUndo);
            w.Undo();                          // the whole stroke is one step
            if (clickFirst) w.Undo();          // and the click before it another
            vm.FlushRefresh();
            Assert.All(changed, x => Assert.True(Same(before[(x % 64) * 64 + x / 64], vm.Level.Tiles[x % 64, x / 64])));
            w.Close();
        }

        [AvaloniaTheory]
        [InlineData(0f)]
        [InlineData(45f)]
        [InlineData(135f)]
        [InlineData(250f)]
        public void Dragging_in_the_3D_view_paints_in_every_direction(float yaw)
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            var m = vm.Level;
            // the middle of a 3 x 3 block of open, flat tiles of one height
            int cx = -1, cy = -1;
            for (int y = 4; y < 60 && cx < 0; y++)
                for (int x = 4; x < 60 && cx < 0; x++)
                {
                    int h = m.Tiles[x, y].Height; bool ok = true;
                    for (int dy = -1; dy <= 1 && ok; dy++) for (int dx = -1; dx <= 1 && ok; dx++)
                        ok = m.Tiles[x + dx, y + dy].Type == TileType.Open && m.Tiles[x + dx, y + dy].Height == h;
                    if (ok) { cx = x; cy = y; }
                }
            Assert.True(cx > 0);
            vm.ToolIndex = 2;
            vm.PaintMode = 1;                                   // light flag off, on a lit block
            vm.Editor.BeginBatch("lit");
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) vm.Editor.SetLightBit(cx + dx, cy + dy, true);
            vm.Editor.EndBatch();
            vm.FlushRefresh();
            float floor = (float)m.FloorAt(cx, cy, 0.5, 0.5);
            view.LookAt(new System.Numerics.Vector3(cx + 0.5f, floor, cy + 0.5f), 7f, yaw, 50f);
            Ui.Pump();
            var b = view.Bounds;
            var mid = new Point(b.Width / 2, b.Height / 2);
            Point Win(Point p) { return view.TranslatePoint(p, w).Value; }
            var report = new List<string>();
            foreach (var dir in new[] { new Vector(1, 0), new Vector(-1, 0), new Vector(0, 1), new Vector(0, -1), new Vector(0.7, 0.7) })
            {
                var path = Enumerable.Range(0, 31).Select(k => mid + dir * (b.Height * 0.12 * k / 30)).ToList();
                var under = path.Select(p => view.PickAt(p).Tile).Where(x => x >= 0).Distinct().ToList();
                var lit = under.Where(x => (vm.Level.Tiles[x % 64, x / 64].Light & 1) != 0).ToList();
                w.MouseMove(Win(path[0]));
                w.MouseDown(Win(path[0]), MouseButton.Left);
                foreach (var p in path.Skip(1)) w.MouseMove(Win(p), RawInputModifiers.LeftMouseButton);
                w.MouseUp(Win(path[path.Count - 1]), MouseButton.Left);
                Ui.Pump();
                vm.FlushRefresh();
                var missed = lit.Where(x => (vm.Level.Tiles[x % 64, x / 64].Light & 1) != 0).ToList();
                report.Add(string.Format("yaw {0} dir {1}: {2} tiles under, {3} lit before, missed {4}", yaw, dir, under.Count, lit.Count, string.Join(" ", missed.Select(x => "(" + x % 64 + "," + x / 64 + ")"))));
                // light the block again for the next direction
                vm.Editor.BeginBatch("lit");
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) vm.Editor.SetLightBit(cx + dx, cy + dy, true);
                vm.Editor.EndBatch();
                vm.FlushRefresh();
            }
            output.WriteLine(string.Join(" || ", report));
            Assert.All(report, r => Assert.EndsWith("missed ", r));
            w.Close();
        }

        [AvaloniaTheory]
        [InlineData(0f, 0)]
        [InlineData(45f, 0)]
        [InlineData(135f, 0)]
        [InlineData(250f, 0)]
        [InlineData(0f, 5)]
        [InlineData(135f, 5)]
        public void Painting_tile_types_or_heights_by_dragging_in_3D_reaches_every_tile_in_every_direction(float yaw, int typeOrHeight)
        {
            if (Uw1 == null) return;
            var dirs = new[] { new Vector(1, 0), new Vector(-1, 0), new Vector(0, 1), new Vector(0, -1), new Vector(0.7, 0.7), new Vector(-0.7, 0.7) };
            var report = new List<string>();
            foreach (var dir in dirs)
            {
                var w = Ui.Open(Uw1);
                w.AskBeforeDiscarding = false;
                var vm = w.ViewModel;
                var view = w.FindControl<LevelView>("View");
                var m = vm.Level;
                int cx = -1, cy = -1;
                for (int y = 4; y < 60 && cx < 0; y++)
                    for (int x = 4; x < 60 && cx < 0; x++)
                    {
                        int h = m.Tiles[x, y].Height; bool ok = true;
                        for (int dy = -1; dy <= 1 && ok; dy++) for (int dx = -1; dx <= 1 && ok; dx++)
                            ok = m.Tiles[x + dx, y + dy].Type == TileType.Open && m.Tiles[x + dx, y + dy].Height == h && h <= 10;
                        if (ok) { cx = x; cy = y; }
                    }
                Assert.True(cx > 0);
                vm.ToolIndex = 2;
                if (typeOrHeight == 0) { vm.PaintMode = 4; vm.PaintType = (int)TileType.Solid; }
                else { vm.PaintMode = 5; vm.PaintHeight = m.Tiles[cx, cy].Height + 4; }
                float floor = (float)m.FloorAt(cx, cy, 0.5, 0.5);
                view.LookAt(new System.Numerics.Vector3(cx + 0.5f, floor, cy + 0.5f), 7f, yaw, 50f);
                Ui.Pump();
                var b = view.Bounds;
                var mid = new Point(b.Width / 2, b.Height / 2);
                Point Win(Point p) { return view.TranslatePoint(p, w).Value; }
                var path = Enumerable.Range(0, 31).Select(k => mid + dir * (b.Height * 0.12 * k / 30)).ToList();
                var under = path.Select(p => view.PickAt(p)).Where(h => h.Hit && h.Kind == 0).Select(h => h.Tile).Distinct().ToList();
                w.MouseMove(Win(path[0]));
                w.MouseDown(Win(path[0]), MouseButton.Left);
                Ui.Pump();
                foreach (var p in path.Skip(1)) { w.MouseMove(Win(p), RawInputModifiers.LeftMouseButton); Ui.Pump(); }   // the editor redraws between moves, as it does in use
                w.MouseUp(Win(path[path.Count - 1]), MouseButton.Left);
                Ui.Pump();
                vm.FlushRefresh();
                var missed = under.Where(x => typeOrHeight == 0 ? vm.Level.Tiles[x % 64, x / 64].Type != TileType.Solid : vm.Level.Tiles[x % 64, x / 64].Height != (int)vm.PaintHeight).ToList();
                report.Add(string.Format("dir {0}: floors under {1}, missed {2}", dir, string.Join(" ", under.Select(x => "(" + x % 64 + "," + x / 64 + ")")), string.Join(" ", missed.Select(x => "(" + x % 64 + "," + x / 64 + ")"))));
                w.Close();
            }
            output.WriteLine(string.Join(" || ", report));
            Assert.All(report, r => Assert.EndsWith("missed ", r));
        }

        [AvaloniaTheory]
        [InlineData(-1f, 4)]      // the 2D map, tile type
        [InlineData(-1f, 6)]      // the 2D map, tile template
        [InlineData(0f, 4)]
        [InlineData(90f, 6)]
        [InlineData(180f, 4)]
        [InlineData(270f, 6)]
        public void Dragging_over_solid_rock_opens_every_rock_tile_passed_over(float yaw, int mode)
        {
            if (Uw1 == null) return;
            var dirs = new[] { new Vector(1, 0), new Vector(-1, 0), new Vector(0, 1), new Vector(0, -1) };
            var report = new List<string>();
            foreach (var dir in dirs)
            {
                var w = Ui.Open(Uw1);
                w.AskBeforeDiscarding = false;
                var vm = w.ViewModel;
                var view = w.FindControl<LevelView>("View");
                var m = vm.Level;
                // a 5 x 5 block of solid rock
                int cx = -1, cy = -1;
                for (int y = 4; y < 60 && cx < 0; y++)
                    for (int x = 4; x < 60 && cx < 0; x++)
                    {
                        bool ok = true;
                        for (int dy = -2; dy <= 2 && ok; dy++) for (int dx = -2; dx <= 2 && ok; dx++) ok = m.Tiles[x + dx, y + dy].IsSolid;
                        if (ok) { cx = x; cy = y; }
                    }
                Assert.True(cx > 0);
                vm.ToolIndex = 2;
                if (mode == 4) { vm.PaintMode = 4; vm.PaintType = (int)TileType.Open; }
                else
                {
                    var open = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(p => m.Tiles[p.X, p.Y].Type == TileType.Open);
                    vm.PickBrush(open.X, open.Y);         // Ctrl+click: an open tile's template (works from any mode)
                    vm.PaintMode = 6;
                    vm.PickBrush(open.X, open.Y);
                }
                if (yaw < 0) { vm.Select(-1, cx, cy, true); w.ToggleMap(); }
                // rock is hit at its top (the ceiling's height): aim there
                view.LookAt(new System.Numerics.Vector3(cx + 0.5f, yaw < 0 ? 0 : LevelGeometry.Ceiling, cy + 0.5f), 7f, yaw < 0 ? (float?)null : yaw, 55f);
                Ui.Pump();
                var b = view.Bounds;
                var mid = new Point(b.Width / 2, b.Height / 2);
                Point Win(Point p) { return view.TranslatePoint(p, w).Value; }
                var path = Enumerable.Range(0, 31).Select(k => mid + dir * (b.Height * (yaw < 0 ? 0.1 : 0.22) * k / 30)).ToList();
                var before = m;
                var expected = path.Select(p => Picking.TileForBrush(view.PickAt(p), (x, y) => before.Tiles[x, y].IsSolid)).Where(x => x >= 0).Distinct().ToList();
                w.MouseMove(Win(path[0]));
                w.MouseDown(Win(path[0]), MouseButton.Left);
                Ui.Pump();
                foreach (var p in path.Skip(1)) { w.MouseMove(Win(p), RawInputModifiers.LeftMouseButton); Ui.Pump(); }
                w.MouseUp(Win(path[path.Count - 1]), MouseButton.Left);
                Ui.Pump();
                vm.FlushRefresh();
                var missed = expected.Where(x => vm.Level.Tiles[x % 64, x / 64].IsSolid).ToList();
                var stray = new List<int>();
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                    if (!expected.Contains(x + 64 * y) && !Same(before.Tiles[x, y], vm.Level.Tiles[x, y])) stray.Add(x + 64 * y);
                report.Add(string.Format("dir {0}: rock {1} | missed {2} | stray {3}", dir, string.Join(" ", expected.Select(x => "(" + x % 64 + "," + x / 64 + ")")),
                    string.Join(" ", missed.Select(x => "(" + x % 64 + "," + x / 64 + ")")), string.Join(" ", stray.Select(x => "(" + x % 64 + "," + x / 64 + ")"))));
                Assert.True(expected.Count >= 2, report.Last());
                w.Close();
            }
            output.WriteLine(string.Join(" || ", report));
            Assert.All(report, r => Assert.Contains("| missed  | stray ", r + " "));
            Assert.All(report, r => Assert.EndsWith("| stray ", r));
        }

        static bool Same(TileInfo a, TileInfo b)
        {
            return a.Type == b.Type && a.Light == b.Light && a.Height == b.Height && a.FloorIndex == b.FloorIndex && a.WallIndex == b.WallIndex;
        }

        /// <summary>The tile already is what the brush paints (so painting it changes nothing).</summary>
        static bool AlreadyBrush(MainViewModel vm, int x, int y)
        {
            var t = vm.Level.Tiles[x, y];
            switch (vm.PaintMode)
            {
                case 1: return (t.Light & 1) == 0;
                case 4: return (int)t.Type == vm.PaintType;
                default: return true;
            }
        }
    }
}
