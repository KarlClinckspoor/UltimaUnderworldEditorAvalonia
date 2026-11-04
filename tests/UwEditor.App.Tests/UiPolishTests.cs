using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using UwEditor.Core;
using UwEditor.Core.Graphics;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>
    /// The interface round of 2026-11-02: the pixel editor's preview and cycling colours, question
    /// buttons that wrap, the controls in two columns, short recent paths, coloured object names,
    /// lists updated in place, fine drag positions and smooth camera keys.
    /// </summary>
    public class UiPolishTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        static Rect BoundsIn(Control c, Visual root)
        {
            var p = c.TranslatePoint(new Point(0, 0), root) ?? throw new InvalidOperationException("not shown");
            return new Rect(p, c.Bounds.Size);
        }

        static void SaveFrame(Window w, string name)
        {
            string dir = Path.Combine(Snapshots.RepoRoot, "TestResults", "ui-polish");
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        // ---- the pixel editor

        [AvaloniaFact]
        public void The_pixel_editors_preview_sits_under_the_drawing_and_overlaps_nothing_at_any_size()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var g = w.ShowGraphics();
            g.Model.FileName = "BODIES.GR";
            Ui.Pump();
            var pe = g.EditPicture();
            Assert.NotNull(pe);
            pe.AskOnClose = false;
            foreach (var (width, height) in new[] { (1100.0, 790.0), (760.0, 520.0), (700.0, 480.0), (1500.0, 950.0) })
            {
                pe.Width = width; pe.Height = height;
                Ui.Pump();
                var preview = BoundsIn(pe.PreviewArea, pe);
                var undo = pe.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Undo");
                var buttons = BoundsIn((Control)undo.Parent, pe);
                Assert.True(preview.Bottom <= buttons.Top + 0.5, string.Format("{0}×{1}: the preview ({2}) runs into the buttons ({3})", width, height, preview, buttons));
                // the left column (tools) and the right one (colours) stay above the buttons too
                foreach (var sv in pe.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Content is StackPanel))
                    Assert.True(BoundsIn(sv, pe).Bottom <= buttons.Top + 0.5, width + "×" + height + ": a side column runs into the buttons");
                // and nothing runs sideways into the colours or off the window
                var columns = pe.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Content is StackPanel).Select(x => BoundsIn(x, pe)).OrderBy(r => r.Left).ToList();
                Assert.Equal(2, columns.Count);
                foreach (var img in pe.PreviewArea.GetVisualDescendants().OfType<Image>().Where(i => i.IsVisible))
                {
                    var r = BoundsIn(img, pe);
                    Assert.True(r.Left >= columns[0].Right - 0.5 && r.Right <= columns[1].Left + 0.5, width + "×" + height + ": a preview runs into a side column");
                }
                foreach (var b in ((Panel)undo.Parent).Children.OfType<Button>())
                    Assert.True(BoundsIn(b, pe).Right <= pe.Bounds.Width, width + "×" + height + ": " + b.Content + " runs off the window");
                if (width == 1100) SaveFrame(pe, "pixel-editor-1100x790");
                if (width == 700) SaveFrame(pe, "pixel-editor-700x480");
            }
            pe.Close();
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_colours_the_game_cycles_are_marked_and_say_so_and_can_be_animated()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var g = w.ShowGraphics();
            g.Model.FileName = "BODIES.GR";
            Ui.Pump();
            var pe = g.EditPicture();
            pe.AskOnClose = false;
            Ui.Pump();
            // the blue Karl painted with: 231 (#14307D) turns round E4h..E7h four times a second
            var c = pe.CycleOf(231);
            Assert.NotNull(c);
            Assert.Equal(0xE4, c.First);
            Assert.Null(pe.CycleOf(200));
            var swatches = pe.GetVisualDescendants().OfType<WrapPanel>().First(p => p.Children.Count == 256);
            var b231 = (Border)swatches.Children[231];
            Assert.Contains("cycles in the game", ToolTip.GetTip(b231) as string);
            Assert.Contains("0xE4–0xE7", ToolTip.GetTip(b231) as string);
            Assert.Single(b231.GetVisualDescendants().OfType<CycleMark>());
            Assert.Empty(((Border)swatches.Children[200]).GetVisualDescendants().OfType<CycleMark>());
            Assert.Equal(16 + 8, Enumerable.Range(0, 256).Count(i => pe.CycleOf(i) != null));
            // a moment later the entry shows the next one's colour (the bank turns down: E7h takes E4h's)
            var before = pe.ShownColors[231];
            pe.ShowCyclesAt(0.3);
            Assert.Equal(pe.Doc.Colors[0xE4], pe.ShownColors[231]);
            Assert.NotEqual(before, pe.ShownColors[231]);
            Assert.Equal(pe.Doc.Colors[200], pe.ShownColors[200]);
            var animate = pe.GetVisualDescendants().OfType<CheckBox>().First(x => (x.Content as string) == "Animate the cycling colours");
            Assert.True(animate.IsVisible);
            animate.IsChecked = true;
            Ui.Pump();
            Assert.True(pe.Animating);
            animate.IsChecked = false;
            Ui.Pump();
            Assert.False(pe.Animating);
            Assert.Equal(pe.Doc.Colors[231], pe.ShownColors[231]);
            pe.Close();
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void A_4_bit_item_picture_marks_the_colours_whose_auxiliary_entries_cycle()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var s = w.ViewModel.Session;
            var f = s.Pictures.Get("OBJECTS.GR");
            var pals = s.Pictures.Palettes;
            var cycles = PaletteCycles.InGame(false);
            // an item whose auxiliary palette points into a cycling run
            int item = Enumerable.Range(0, f.Count).FirstOrDefault(i =>
            {
                var img = f.Get(i, pals, 0);
                return img != null && img.Type != 4 && pals.Aux[img.Aux & 31].Skip(1).Any(e => PaletteCycles.Find(cycles, e) != null);
            });
            if (item == 0) { w.Close(); return; }
            var pe = w.EditItemPicture(item);
            pe.AskOnClose = false;
            var pic = f.Get(item, pals, 0);
            int k = Enumerable.Range(1, 15).First(i => PaletteCycles.Find(cycles, pals.Aux[pic.Aux & 31][i]) != null);
            Assert.NotNull(pe.CycleOf(k));
            pe.Close();
            w.Close();
        }

        // ---- the message window

        static MessageWindow AskAndLook(Window owner, string[] buttons, Action<MessageWindow> look)
        {
            MessageWindow shown = null;
            Action<MessageWindow> on = m => shown = m;
            MessageWindow.Opening += on;
            try
            {
                var task = MessageWindow.Ask(owner, "Export as a saved game", "Where should the saved game go? The Exhumed port keeps four slots.", buttons);
                Ui.Pump();
                Assert.NotNull(shown);
                look(shown);
                shown.Close();
                Ui.Pump();
                Assert.True(task.IsCompleted);
                Assert.Equal(buttons.Last(), task.Result);   // closing the window chooses the last (cancel)
            }
            finally { MessageWindow.Opening -= on; }
            return shown;
        }

        [AvaloniaFact]
        public void Six_question_buttons_wrap_inside_the_window_and_two_stay_on_one_line_at_the_right()
        {
            var w = Ui.Open(null);
            var six = new[] { "Exhumed slot 1", "Exhumed slot 2", "Exhumed slot 3", "Exhumed slot 4", "Another folder…", "Cancel" };
            AskAndLook(w, six, m =>
            {
                var bs = m.Buttons.Children.OfType<Button>().ToList();
                Assert.Equal(6, bs.Count);
                foreach (var b in bs)
                {
                    var r = BoundsIn(b, m);
                    Assert.True(r.Right <= m.Bounds.Width - 19, (b.Content as string) + " runs off the window: " + r + " in " + m.Bounds.Width);
                    Assert.True(r.Left >= 19);
                }
                Assert.True(bs.Select(b => BoundsIn(b, m).Top).Distinct().Count() >= 2, "the buttons should take two lines");
                Assert.True(bs[0].IsDefault);
                Assert.True(bs[5].IsCancel);
                SaveFrame(m, "ask-six-buttons");
            });
            AskAndLook(w, new[] { "Save", "Cancel" }, m =>
            {
                var bs = m.Buttons.Children.OfType<Button>().ToList();
                Assert.Single(bs.Select(b => BoundsIn(b, m).Top).Distinct());
                Assert.True(Math.Abs(BoundsIn(bs[1], m).Right - (m.Bounds.Width - 20)) < 2, "the buttons sit at the right");
            });
            w.Close();
        }

        [AvaloniaFact]
        public void A_long_report_shows_in_a_resizable_text_field_that_scrolls_both_ways()
        {
            var w = Ui.Open(null);
            MessageWindow shown = null;
            Action<MessageWindow> on = m => shown = m;
            MessageWindow.Opening += on;
            try
            {
                string text = string.Join("\n", Enumerable.Range(0, 300).Select(i => i + ": " + new string('x', 400)));
                var task = MessageWindow.ShowReport(w, "The logic from the start", text);
                Ui.Pump();
                Assert.NotNull(shown);
                Assert.True(shown.CanResize);
                Assert.True(shown.Bounds.Width <= 1100 && shown.Bounds.Height <= 760, "the report window is " + shown.Bounds.Size);
                var box = shown.Report;
                Assert.Equal(text, box.Text);
                Assert.True(box.IsReadOnly);
                Assert.Equal(TextWrapping.NoWrap, box.TextWrapping);
                var sv = box.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(sv.Extent.Height > sv.Viewport.Height + 100, "it scrolls down: " + sv.Extent + " in " + sv.Viewport);
                Assert.True(sv.Extent.Width > sv.Viewport.Width + 100, "it scrolls across: " + sv.Extent + " in " + sv.Viewport);
                // the OK button stays in the window
                var ok = shown.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "OK");
                Assert.True(BoundsIn(ok, shown).Bottom <= shown.Bounds.Height);
                SaveFrame(shown, "report");
                shown.Close();
                Ui.Pump();
                Assert.True(task.IsCompleted);
            }
            finally { MessageWindow.Opening -= on; }
            w.Close();
        }

        [AvaloniaFact]
        public void The_controls_come_in_two_columns_that_fit_a_1080p_screen()
        {
            var w = Ui.Open(null);
            MessageWindow shown = null;
            Action<MessageWindow> on = m => shown = m;
            MessageWindow.Opening += on;
            try
            {
                typeof(MainWindow).GetMethod("OnControls", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { null, null });
                Ui.Pump();
                Assert.NotNull(shown);
                var columns = shown.GetVisualDescendants().OfType<TextBlock>().Where(t => (t.Text ?? "").Contains("  ")).ToList();
                Assert.Equal(2, columns.Count);
                Assert.True(columns[1].Text.Split('\n').Length <= columns[0].Text.Split('\n').Length + 12, "about the same height");
                Assert.True(shown.Bounds.Width <= 1900 && shown.Bounds.Height <= 1000, "the controls box is " + shown.Bounds.Size);
                Assert.True(shown.Bounds.Width > 900);
                SaveFrame(shown, "controls");
                shown.Close();
                Ui.Pump();
            }
            finally { MessageWindow.Opening -= on; }
            w.Close();
        }

        [Fact]
        public void Columns_split_at_blank_lines_in_order()
        {
            var parts = MessageWindow.SplitColumns("a\nb\nc\n\nd\ne\n\nf\ng\nh\ni", 2);
            Assert.Equal(2, parts.Count);
            Assert.Equal("a\nb\nc\n\nd\ne\n\nf\ng\nh\ni", string.Join("\n\n", parts));
            Assert.Single(MessageWindow.SplitColumns("one paragraph\nonly", 2));
        }

        // ---- recent paths

        [Fact]
        public void Long_paths_keep_their_root_first_folder_and_last_folders_with_the_middle_left_out()
        {
            Assert.Equal("/home/karl/UW", PathText.MiddleEllipsis("/home/karl/UW", 40));
            string p = "/home/karl/games/dos/ultima underworld/backups/2026-11-01/UW2 saves/SAVE3";
            string s = PathText.MiddleEllipsis(p, 48);
            Assert.True(s.Length <= 48, s);
            Assert.StartsWith("/home/", s);
            Assert.EndsWith("/UW2 saves/SAVE3", s);
            Assert.Contains("…", s);
            string w = PathText.MiddleEllipsis(@"C:\Users\Karl\Documents\Games\GOG Galaxy\Ultima Underworld 2\SAVE1", 40);
            Assert.True(w.Length <= 40, w);
            Assert.StartsWith(@"C:\Users\…\", w);
            Assert.EndsWith(@"\SAVE1", w);
            // a last folder too long by itself loses its own middle
            string l = PathText.MiddleEllipsis("/a/" + new string('x', 100), 30);
            Assert.True(l.Length <= 30, l);
            Assert.StartsWith("/", l);
            Assert.Equal("ab…yz", PathText.Cut("abcdefwxyz", 5));
        }

        [AvaloniaFact]
        public void The_recent_menu_shows_short_paths_with_the_whole_path_as_the_tip()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var menu = Ui.Find<MenuItem>(w, "RecentMenu");
            var first = menu.ItemsSource.OfType<MenuItem>().First();
            Assert.Equal(Uw1, ToolTip.GetTip(first) as string);
            Assert.Equal(PathText.MiddleEllipsis(Uw1, 64).Replace("_", "__"), first.Header as string);
            w.Close();
        }

        // ---- coloured object names

        [AvaloniaFact]
        public void Creatures_are_coloured_by_attitude_enchanted_objects_purple_and_traps_underlined()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var level = vm.Level;
            int critters = 0, traps = 0, enchanted = 0;
            foreach (var o in level.Objects.Where(x => !x.Free && (x.OnTile || x.Parent >= 0)))
            {
                var look = ObjectLook.Of(o);
                int att = ObjectLook.AttitudeOf(o);
                if (o.IsCritter && o.IsMobile)
                {
                    critters++;
                    Assert.Equal(new[] { ObjectTint.Hostile, ObjectTint.Upset, ObjectTint.Mellow, ObjectTint.Friendly }[att], look.Tint);
                    Assert.Equal(att, (o.Record[0x0D + 1] >> 6) & 3);                   // OBJ_ATTITUDE: bits 14-15 of the word at 0Dh
                }
                else if (o.Enchanted) { enchanted++; Assert.Equal(ObjectTint.Enchanted, look.Tint); }
                else Assert.Equal(ObjectTint.None, look.Tint);
                Assert.Equal(o.IsTrapOrTrigger, look.Underlined);
                if (o.IsTrapOrTrigger) traps++;
                var node = vm.NodeOf(o.Index);
                if (node != null) Assert.Same(look, node.Look);
            }
            Assert.True(critters > 0 && traps > 0, "level 1 has creatures and traps");
            // the selected creature's name in the properties panel takes its colour too
            var critter = level.Objects.First(o => !o.Free && o.OnTile && o.IsCritter && o.IsMobile);
            vm.Select(critter.Index, -1, -1, true);
            Ui.Pump();
            Assert.Same(ObjectLook.Of(critter), vm.ObjectLook);
            var title = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("title") && t.Text == critter.Name);
            string cls = critter.Name == null ? null : ObjectLook.Of(critter).Tint.ToString().ToLowerInvariant();
            Assert.Contains(cls, title.Classes);
            vm.ClearSelection();
            Assert.Same(ObjectLook.Plain, vm.ObjectLook);
            w.Close();
        }

        [AvaloniaFact]
        public void The_name_colours_are_readable_on_the_panels_of_both_colour_schemes()
        {
            var app = Application.Current;
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                object panel;
                Assert.True(app.TryGetResource("Panel", theme, out panel));
                var bg = ((ISolidColorBrush)panel).Color;
                var seen = new List<Color>();
                foreach (var key in new[] { "ObjHostile", "ObjUpset", "ObjMellow", "ObjFriendly", "ObjEnchanted" })
                {
                    object v;
                    Assert.True(app.TryGetResource(key, theme, out v), key);
                    var c = ((ISolidColorBrush)v).Color;
                    Assert.True(Contrast(c, bg) >= 4.5, string.Format("{0} on the {1} panel: contrast {2:0.0}", key, theme, Contrast(c, bg)));
                    seen.Add(c);
                }
                Assert.Equal(5, seen.Distinct().Count());
            }
        }

        static double Luminance(Color c)
        {
            Func<byte, double> ch = v => { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * ch(c.R) + 0.7152 * ch(c.G) + 0.0722 * ch(c.B);
        }

        static double Contrast(Color a, Color b)
        {
            double x = Luminance(a), y = Luminance(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }

        [AvaloniaFact]
        public void Snapshot_of_the_coloured_object_list_in_both_schemes()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Filter = "";
            var critter = vm.Level.Objects.First(o => !o.Free && o.OnTile && o.IsCritter && o.IsMobile);
            vm.Select(critter.Index, -1, -1, true);
            Ui.Pump();
            SaveFrame(w, "object-list-dark");
            ColourScheme.Set(ColourScheme.Light);
            Ui.Pump();
            SaveFrame(w, "object-list-light");
            ColourScheme.Set(ColourScheme.Dark);
            Ui.Pump();
            w.Close();
        }

        // ---- the list after an edit

        [AvaloniaFact]
        public void Editing_a_field_updates_the_objects_line_in_place_and_keeps_what_is_open()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            // a creature carrying something: open its line, then edit what it carries
            var holder = vm.Level.Objects.First(o => !o.Free && o.OnTile && o.Children.Count > 0 && vm.NodeOf(o.Index) != null);
            int child = holder.Children[0];
            var node = vm.NodeOf(holder.Index);
            node.IsExpanded = true;
            var childNode = vm.NodeOf(child);
            vm.Select(child, -1, -1, true);
            Ui.Pump();
            int builds = vm.TreeBuilds;
            var rowsBefore = vm.ObjectRows.ToList();
            var q = vm.ObjectRows.First(r => r.Name == "Quality" && r.Editable);
            int quality = vm.Level.Objects[child].Quality;
            vm.EditField(q, ((quality + 1) % 64).ToString());
            vm.FlushRefresh();
            Ui.Pump();
            Assert.Equal((quality + 1) % 64, vm.Level.Objects[child].Quality);
            Assert.Equal(builds, vm.TreeBuilds);                    // not built again
            Assert.Same(node, vm.NodeOf(holder.Index));
            Assert.Same(childNode, vm.NodeOf(child));
            Assert.True(node.IsExpanded);
            Assert.Same(childNode, vm.SelectedNode);
            // the panel kept the rows that did not change and has the new value
            var rowsAfter = vm.ObjectRows.ToList();
            Assert.Equal(rowsBefore.Count, rowsAfter.Count);
            Assert.True(rowsAfter.Zip(rowsBefore, (a, b) => ReferenceEquals(a, b)).Count(x => x) > rowsAfter.Count / 2);
            Assert.Equal(((quality + 1) % 64).ToString(), vm.ObjectRows.First(r => r.Name == "Quality").Value);
            // a refused value shows the old one again
            vm.EditField(vm.ObjectRows.First(r => r.Name == "Quality"), "999");
            Assert.Equal(((quality + 1) % 64).ToString(), vm.ObjectRows.First(r => r.Name == "Quality").Value);
            // undo is a change of fields again; deleting an object builds the list again
            w.Undo();
            vm.FlushRefresh();
            Assert.Equal(quality, vm.Level.Objects[child].Quality);
            Assert.Equal(builds, vm.TreeBuilds);
            vm.Select(child, -1, -1, true);
            Assert.True(vm.Edit(e => e.DeleteObject(child)));
            vm.FlushRefresh();
            Assert.Equal(builds + 1, vm.TreeBuilds);
            w.Close();
        }

        [AvaloniaFact]
        public void A_filtered_list_is_built_again_when_an_edit_changes_what_matches()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var o = vm.Level.Objects.First(x => !x.Free && x.OnTile && !x.IsCritter && !x.IsTrapOrTrigger && !x.Enchanted && x.Index >= 256);
            vm.Filter = "#" + o.Index;
            Assert.NotNull(vm.NodeOf(o.Index));
            int builds = vm.TreeBuilds;
            vm.Select(o.Index, -1, -1, true);
            var S = UltimateEditor.Uw1.Uw1Schemas.StaticObject;
            Assert.True(vm.Edit(e => e.SetObjectField(o.Index, S["Quality"], (o.Quality + 1) % 64)));
            vm.FlushRefresh();
            Assert.Equal(builds, vm.TreeBuilds);                    // still matches "#index"
            w.Close();
        }

        // ---- dragging

        [Fact]
        public void The_drop_place_reads_as_tile_dot_fine()
        {
            Assert.Equal("(10.2, 20.5)", MainWindow.FinePlace(10, 20, 2, 5));
            int tx, ty, fx, fy;
            MainWindow.DropPoint(new PickResult { Object = -1, Tile = 10 + 64 * 20, Point = new Vector3(10.30f, 0, 20.70f) }, out tx, out ty, out fx, out fy);
            Assert.Equal((10, 20, 2, 5), (tx, ty, fx, fy));
            MainWindow.DropPoint(new PickResult { Object = -1, Tile = 10 + 64 * 20, Point = new Vector3(10.999f, 0, 20.0f) }, out tx, out ty, out fx, out fy);
            Assert.Equal((7, 0), (fx, fy));
        }

        [AvaloniaFact]
        public void Dragging_within_a_tile_follows_the_mouse_by_eighths_and_keeps_the_objects_place_in_the_list()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var level = vm.Level;
            // an object sharing its tile with others, so its place in the tile's list shows
            var o = level.Objects.First(x => !x.Free && x.OnTile && x.Index > 0 && level.TileObjects(x.TileX, x.TileY).Count() >= 2 && !level.Tiles[x.TileX, x.TileY].IsSolid);
            var orderBefore = level.TileObjects(o.TileX, o.TileY).Select(x => x.Index).ToList();
            vm.Select(o.Index, -1, -1, true);
            Ui.Pump();
            int nfx = (o.FineX + 3) % 8, nfy = (o.FineY + 5) % 8;
            var hit = new PickResult { Object = -1, Tile = o.TileX + 64 * o.TileY, Point = new Vector3(o.TileX + (nfx + 0.5f) / 8, 0, o.TileY + (nfy + 0.5f) / 8) };
            var t = typeof(MainWindow);
            t.GetMethod("OnDragMoved", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { hit });
            Assert.Contains(MainWindow.FinePlace(o.TileX, o.TileY, nfx, nfy), vm.EditStatus);
            int builds = vm.TreeBuilds;
            t.GetMethod("OnDragEnded", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(w, new object[] { hit });
            Ui.Pump();
            var moved = vm.Level.Objects[o.Index];
            Assert.Equal((o.TileX, o.TileY, nfx, nfy), (moved.TileX, moved.TileY, moved.FineX, moved.FineY));
            Assert.Equal(orderBefore, vm.Level.TileObjects(o.TileX, o.TileY).Select(x => x.Index).ToList());
            Assert.Equal(builds, vm.TreeBuilds);
            Assert.Contains("Move", vm.UndoText);
            w.Undo();
            Assert.Equal((o.FineX, o.FineY), (vm.Level.Objects[o.Index].FineX, vm.Level.Objects[o.Index].FineY));
            w.Close();
        }

        // ---- smooth camera keys

        [Fact]
        public void Smooth_motion_eases_in_and_out_and_does_not_depend_on_the_frame_rate()
        {
            var wanted = new Vector4(8, 0, 0, 0);
            var a = new SmoothMotion();
            float quarter = a.Step(wanted, 0.25).X;
            // the exact integral of the eased speed: 8 (0.25 − Ease (1 − e^(−0.25/Ease)))
            Assert.Equal(8 * (0.25 - SmoothMotion.Ease * (1 - Math.Exp(-0.25 / SmoothMotion.Ease))), quarter, 3);
            var b = new SmoothMotion();
            float sum = 0;
            for (int i = 0; i < 15; i++) sum += b.Step(wanted, 1 / 60.0).X;
            Assert.Equal(quarter, sum, 3);                          // 15 frames of 1/60 s go as far as one of 1/4 s
            for (int i = 0; i < 45; i++) b.Step(wanted, 1 / 60.0);
            Assert.Equal(8, b.Velocity.X, 2);
            // let go: it glides about speed × Ease and stops
            float glide = 0;
            for (int i = 0; i < 120 && b.Moving; i++) glide += b.Step(Vector4.Zero, 1 / 60.0).X;
            Assert.InRange(glide, 0.55f, 0.66f);
            Assert.False(b.Moving);
            Assert.Equal(Vector4.Zero, b.Velocity);
            // a stalled frame does not jump
            Assert.True(new SmoothMotion().Step(wanted, 5).X < 8 * 0.25f + 0.01f);
        }

        [AvaloniaFact]
        public void Holding_W_moves_the_view_steadily_and_letting_go_stops_it()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var v = Ui.Find<LevelView>(w, "View");
            v.KeysByFrame = false;                                  // the test steps the frames itself
            v.Focus();
            Ui.Pump();
            var start = v.Camera.Target;
            w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Assert.Equal(1, v.HeldKeys);
            var afterPress = v.Camera.Target;
            Assert.True(Vector3.Distance(start, afterPress) < 0.3f, "no jump on the key press: " + start + " to " + afterPress);
            // key repeats do not move it by themselves
            w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Assert.True(Vector3.Distance(afterPress, v.Camera.Target) < 0.3f);
            var p0 = v.Camera.Target;
            for (int i = 0; i < 30; i++) v.StepMotion(1 / 60.0);
            float half = Vector3.Distance(p0, v.Camera.Target);
            var p1 = v.Camera.Target;
            for (int i = 0; i < 30; i++) v.StepMotion(1 / 60.0);
            float second = Vector3.Distance(p1, v.Camera.Target);
            // 8 tiles a second at the default distance (more when farther out), reached after the ease
            float speed = LevelView.OrbitSpeed * Math.Clamp(v.Camera.Distance / 8f, 0.4f, 6f);
            Assert.InRange(second, speed * 0.5f - 0.2f, speed * 0.5f + 0.2f);
            Assert.InRange(half, speed * 0.5f - 0.2f - speed * (float)SmoothMotion.Ease, second - 0.1f);
            w.KeyReleaseQwerty(PhysicalKey.W, RawInputModifiers.None);
            Assert.Equal(0, v.HeldKeys);
            for (int i = 0; i < 60 && v.IsMoving; i++) v.StepMotion(1 / 60.0);
            Assert.False(v.IsMoving);
            var rest = v.Camera.Target;
            v.StepMotion(1 / 60.0);
            Assert.Equal(rest, v.Camera.Target);
            // Q turns while held
            float yaw = v.Camera.Yaw;
            w.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.None);
            for (int i = 0; i < 30; i++) v.StepMotion(1 / 60.0);
            w.KeyReleaseQwerty(PhysicalKey.Q, RawInputModifiers.None);
            Assert.True(Math.Abs(((v.Camera.Yaw - yaw) + 540) % 360 - 180) > 20);
            w.Close();
        }

        [AvaloniaFact]
        public void Walking_in_first_person_glides_too_and_walls_still_stop_it()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Select(-1, 32, 3, true);
            w.ToggleFirstPerson();
            var v = Ui.Find<LevelView>(w, "View");
            Assert.True(v.FirstPerson);
            v.KeysByFrame = false;
            v.Focus();
            w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            for (int i = 0; i < 600; i++) v.StepMotion(1 / 60.0);
            w.KeyReleaseQwerty(PhysicalKey.W, RawInputModifiers.None);
            var t = v.Camera.Target;
            Assert.False(vm.Level.Tiles[(int)t.X, (int)t.Z].IsSolid);
            // Escape still leaves, and lets go of the keys
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
            Ui.Key(w, Key.Escape);
            Assert.False(v.FirstPerson);
            Assert.Equal(0, v.HeldKeys);
            w.Close();
        }
    }
}
