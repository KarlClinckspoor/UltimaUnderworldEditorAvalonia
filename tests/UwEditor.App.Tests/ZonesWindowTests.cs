using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The Zones window: the start (in UW.EXE) and the goal (a square that ends the game), the links, Check if solvable.</summary>
    public class ZonesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        /// <summary>DATA and the executable in a temporary folder (Mark as start writes UW.EXE).</summary>
        sealed class Copy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-zones-" + Guid.NewGuid().ToString("N"));
            public Copy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static Button Btn(Window w, string text) { return w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == text); }

        [AvaloniaFact]
        public void Start_goal_links_and_check_if_solvable()
        {
            if (Uw1 == null) return;
            using (var c = new Copy(Uw1))
            {
                var w = Ui.Open(c.Folder);
                w.AskBeforeDiscarding = false;
                var z = w.Zoning();
                z.LoadExample();
                var zw = w.ShowZones();
                Ui.Pump();
                var vm = w.ViewModel;
                var s = vm.Session;
                // the picture buttons are no longer offered
                Assert.Null(Btn(zw, "Import pictures…"));
                Assert.Null(Btn(zw, "Export pictures…"));
                Assert.NotNull(Btn(zw, "Check if solvable…"));
                // the start and the goal as the game has them: the start square in UW.EXE, level 9's end
                Assert.Contains(", start", zw.Rows.First(r => r.Id == 53).Label);
                Assert.Contains("Start: level 1 (32, 2), zone 53", zw.StartGoalText);
                Assert.Contains("L9 (52, 51)", zw.StartGoalText);

                // Mark as start: another painted zone's open square on level 1 (moves the start in UW.EXE)
                var m = vm.Edits.Model(1);
                int t = Enumerable.Range(0, 4096).First(i => { int x = i % 64, y = i / 64, v = z.Set.ZoneAt(1, x, y); return v > 1 && v != 53 && m.Tiles[x, y].Type == UwEditor.Core.TileType.Open; });
                int tx = t % 64, ty = t / 64, there = z.Set.ZoneAt(1, tx, ty);
                vm.LoadLevel(1);
                vm.Select(-1, tx, ty, false);
                Ui.Pump();
                Assert.True(zw.MarkStart());
                Assert.Equal(new[] { tx, ty }, new[] { (int)s.NewGameStart.X, (int)s.NewGameStart.Y });
                Assert.True(File.Exists(Path.Combine(c.Folder, "UW.EXE.ORIG")));
                Assert.Equal(there, z.Set.StartZone);
                Assert.Contains(", start", zw.Rows.First(r => r.Id == there).Label);
                Assert.DoesNotContain(", start", zw.Rows.First(r => r.Id == 53).Label);
                // Mark as goal on the same square: a second end of the game, level 9's stays
                Assert.True(zw.MarkGoal());
                Assert.Equal(2, EndGame.Find(s, vm.Edits.Model).Count);
                Assert.Contains(", ends the game", zw.Rows.First(r => r.Id == there).Label);
                Assert.Contains("L1 (" + tx + ", " + ty + ")", zw.StartGoalText);
                // Check if solvable: the solve over time on this zoning, won at once on that square
                string text = zw.CheckSolvable();
                Assert.StartsWith("Solvable with these options (the goal reached by ", text);
                Assert.Contains("main option sets", text);
                Assert.True(zw.LastCheck.Solve.GoalReached);
                Assert.Contains(zw.LastCheck.Ends, e => e.Key.Level == 1 && e.Key.X == tx && e.Key.Y == ty);
                vm.Edits.For(1).Undo();
                Ui.Pump();
                Assert.Single(EndGame.Find(s, vm.Edits.Model));

                // links: made here, from the current zone, with the requirement from the doors (2 -> 9: the pillar room's door)
                z.CurrentZone = 2;
                Ui.Pump();
                int before = z.Set.Links.Count;
                var k = zw.AddLink(9);
                Assert.NotNull(k);
                Assert.Equal(before + 1, z.Set.Links.Count);
                Assert.Equal("key:2 | pick:0 | bash", k.Requires);
                Assert.Same(k, zw.SelectedLink);
                var editor = zw.GetLogicalDescendants().OfType<LinkEditorPanel>().Single();
                Assert.Same(k, editor.Link);
                // editing it keeps the editor (only the list's lines change)
                k.RequirementList = new System.Collections.Generic.List<string> { "key:2" };
                z.LinkChanged();
                Ui.Pump();
                Assert.Same(editor, zw.GetLogicalDescendants().OfType<LinkEditorPanel>().Single());
                Assert.Contains(zw.GetLogicalDescendants().OfType<ListBox>().SelectMany(l => (l.ItemsSource as System.Collections.IEnumerable ?? new object[0]).Cast<object>()), o => (o as string ?? "").EndsWith(": key:2"));
                // showing a link does not count as a change to the zoning
                z.Save(Path.Combine(c.Folder, "DATA", "uw1-zones.json"));
                zw.SelectLink(z.Set.Links.First(l => l.From == 2 || l.To == 2));
                Ui.Pump();
                Assert.False(z.Dirty);
                zw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void The_zone_graph_is_a_view_that_follows_the_zones_window_and_shows_the_logic_check()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            z.LoadExample();
            var zw = w.ShowZones();
            var g = w.ShowZoneGraph();
            Ui.Pump();
            // no editing here: no connect modes, no link editor, no zoom bar, no save
            foreach (var gone in new[] { "Select and move", "Connect one-way", "Connect two-way", "Save the zoning", "Start here", "Goal here" })
                Assert.Null(Btn(g, gone));
            Assert.Empty(g.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Where(t => (t.Content as string ?? "").StartsWith("Connect")));
            Assert.Equal(2, g.GetLogicalDescendants().OfType<Slider>().Count());                   // attraction and repulsion only
            Assert.NotNull(Btn(g, "Fit"));
            Assert.NotNull(Btn(g, "Reset graph appearance"));
            // the start: the zone of UW.EXE's start square
            Assert.Equal(53, g.StartZone);
            // a link made in the Zones window shows here, and a link chosen here opens there
            z.CurrentZone = 2;
            Ui.Pump();
            var k = zw.AddLink(9);
            Ui.Pump();
            Assert.Contains(k, g.Zoning.Set.Links);
            g.SelectLink(k);
            Ui.Pump();
            Assert.Empty(g.GetLogicalDescendants().OfType<LinkEditorPanel>());
            Assert.NotNull(Btn(g, "Edit in the Zones window"));
            Assert.Same(zw, g.EditInZones(k));
            Assert.Same(k, zw.SelectedLink);
            // choosing a zone in the Zones window chooses it here too
            z.CurrentZone = 53;
            Ui.Pump();
            Assert.Equal(53, g.SelectedZone);
            // Check the logic: what is reached keeps its colour with its step, links from it to what is not reached are blocked
            Assert.Null(g.Reached);
            g.CheckLogic();
            Ui.Pump();
            Assert.NotNull(g.Reached);
            Assert.Equal(0, g.Reached[53]);
            Assert.True(g.Reached.Count > 40, g.Reached.Count + " zones reached");
            Assert.Contains(z.Set.Links, l => g.LinkState(l) == "used");
            var label = g.GetLogicalDescendants().OfType<Border>().First(b => b.Tag is int id && id == 53);
            Assert.StartsWith("[0] 53", ((TextBlock)label.Child).Text);
            var faded = g.GetLogicalDescendants().OfType<Border>().Where(b => b.Tag is int id && !g.Reached.ContainsKey(id)).ToList();
            Assert.All(faded, b => Assert.True(b.Opacity < 0.5));
            // split by level: a zone's level parts count as the zone
            zw.SplitByLevelBox.IsChecked = true;
            Ui.Pump();
            g.CheckLogic();
            Assert.True(g.Reached.ContainsKey(2));
            Assert.All(g.Reached.Keys, id => Assert.NotNull(z.Set.GetZone(id)));
            // Reset graph appearance
            g.ResetAppearance();
            Ui.Pump();
            Assert.Null(g.Reached);
            Assert.Null(g.LinkState(k));
            label = g.GetLogicalDescendants().OfType<Border>().First(b => b.Tag is int id && id == 53);   // the split option built the labels again
            Assert.Equal(1, label.Opacity);
            Assert.All(g.GetLogicalDescendants().OfType<Border>().Where(b => b.Tag is int), b => Assert.Equal(1, b.Opacity));
            Assert.StartsWith("53", ((TextBlock)label.Child).Text);
            // Fit shows every label
            g.Width = 1280; g.Height = 820;
            Ui.Pump();
            g.Fit();
            Assert.True(g.Zoom <= 1 && g.Zoom >= 0.3);
            zw.SplitByLevelBox.IsChecked = false;
            g.Close(); zw.Close();
            w.Close();
        }
    }
}
