using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using UwEditor.Core;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The main window, driven as a user would, without a screen.</summary>
    public class MainWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        [AvaloniaFact]
        public void Without_a_game_the_window_offers_to_open_one()
        {
            var w = Ui.Open(null);
            Assert.True(w.ViewModel.NoGame);
            Assert.Contains("Open game folder", Ui.Find<TextBlock>(w, "ViewHint").Text);
            w.Close();
        }

        [AvaloniaFact]
        public void Opening_UW1_shows_level_1_over_the_start_square()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var vm = w.ViewModel;
            Assert.Equal(1, vm.Level.Level);
            Assert.Contains("Ultima Underworld", w.Title);
            Assert.Contains("start square (32, 2)", vm.StartText);
            Assert.NotEmpty(vm.Tree);
            Assert.Equal("534 objects", vm.ListSummary);
            Assert.All(vm.Tree, n => Assert.NotNull(n.Icon));
            w.Close();
        }

        [AvaloniaFact]
        public void Typing_in_the_search_box_narrows_the_list_to_matching_names_and_texts()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var box = Ui.Find<TextBox>(w, "FilterBox");
            Ui.Type(w, box, "goblin");
            Assert.NotEmpty(w.ViewModel.Tree);
            // a line matches by its name, its NPC name or its text (a scroll about goblins), or holds a match
            var vm = w.ViewModel;
            Assert.All(vm.Tree, n =>
            {
                var o = vm.Level.Objects[n.ObjectIndex];
                string all = (o.Name + " " + ObjectTexts.NpcName(vm.Session, o) + " " + ObjectTexts.TextOf(vm.Session, o)).ToLowerInvariant();
                Assert.True(all.Contains("goblin") || n.Children.Count > 0, n.Header);
            });
            Assert.Contains(vm.Tree, n => n.Header.Contains("goblin"));
            Assert.Contains("of 534 objects shown", w.ViewModel.ListSummary);
            w.Close();
        }

        [AvaloniaFact]
        public void A_quick_filter_chip_shows_only_that_kind_and_unclicking_restores_the_list()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var vm = w.ViewModel;
            var bar = Ui.Find<ItemsControl>(w, "QuickFilterBar");
            var food = bar.GetLogicalDescendants().OfType<ToggleButton>().First(b => (string)b.Content == "Food");
            Ui.Click(w, food);
            Assert.True(food.IsChecked);
            var s = vm.Session;
            // every top-level line is food or holds food
            Assert.All(vm.Tree, n =>
            {
                var o = vm.Level.Objects[n.ObjectIndex];
                bool isFood = (ItemCategories.Of(o.Item, s.Kind) & ItemCategory.Food) != 0;
                Assert.True(isFood || n.Children.Count > 0, n.Header);
            });
            Ui.Click(w, food);
            Assert.Equal("534 objects", vm.ListSummary);
            w.Close();
        }

        [AvaloniaFact]
        public void Selecting_an_object_in_the_list_fills_the_properties_and_the_view_highlight()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var vm = w.ViewModel;
            Ui.Type(w, Ui.Find<TextBox>(w, "FilterBox"), "#942");
            var sack = vm.Tree.Single();
            vm.SelectedNode = sack;
            Ui.Pump();
            Assert.Equal("sack", vm.ObjectTitle);
            Assert.Contains(vm.ObjectRows, r => r.Name == "Item id" && r.Value == "128");
            Assert.Contains("diagonal, open SW", vm.TileTitle);
            Assert.Equal(942, Ui.Find<LevelView>(w, "View").State.SelectedObject);
            w.Close();
        }

        [AvaloniaFact]
        public void Page_down_steps_to_the_next_level_and_H_switches_the_grid()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var vm = w.ViewModel;
            Ui.Find<LevelView>(w, "View").Focus();
            Ui.Key(w, Key.PageDown);
            Assert.Equal(2, vm.Level.Level);
            bool grid = vm.ShowGrid;
            Ui.Key(w, Key.H);
            Assert.NotEqual(grid, vm.ShowGrid);
            w.Close();
        }

        [AvaloniaFact]
        public void The_grid_puts_its_numbers_at_the_edges_of_the_view()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.ViewModel.ShowGrid = true;
            Ui.Pump();
            var labels = Ui.Find<Canvas>(w, "GridLabels").Children.OfType<Border>().Select(b => ((TextBlock)b.Child).Text).ToList();
            Assert.Contains("X: 30", labels);
            Assert.Contains("X: 35", labels);
            w.ViewModel.ShowGrid = false;
            Ui.Pump();
            Assert.Empty(Ui.Find<Canvas>(w, "GridLabels").Children);
            w.Close();
        }

        [AvaloniaFact]
        public async Task Find_in_all_levels_finds_texts_and_goes_to_the_object()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var find = w.ShowFind();
            Ui.Pump();
            find.Model.Text = "garamon";
            await find.SearchAsync();
            Ui.Pump();
            Assert.Equal(3, find.Model.Hits.Count);   // his ghost (NPC name), his gravestone, a scroll that names him
            var grave = find.Model.Hits.First(h => h.Hit.Name == "gravestone");
            Assert.Contains("empty grave of Garamon", grave.Detail);
            find.Model.Selected = grave;
            find.GoToSelected();
            Ui.Pump();
            Assert.Equal(5, w.ViewModel.Level.Level);
            Assert.Equal(grave.Hit.Index, w.ViewModel.SelectedObject);
            find.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void UW2_opens_with_worlds_and_its_start_square()
        {
            if (Uw2 == null) return;
            var w = Ui.Open(Uw2);
            var vm = w.ViewModel;
            Assert.True(vm.IsUw2);
            Assert.StartsWith("Britannia", vm.Worlds[vm.WorldIndex]);
            Assert.Contains("start square (19, 48)", vm.StartText);
            w.Close();
        }

        [AvaloniaFact]
        public void A_saved_game_opens_over_the_player()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE2"))) return;
            var w = Ui.Open(Path.Combine(Uw2, "SAVE2"));
            Assert.Contains("player in SAVE2", w.ViewModel.StartText);
            w.Close();
        }

        // ---- pictures of the interface

        [AvaloniaFact]
        public void Snapshot_of_the_panels_with_UW1_open()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            Ui.Type(w, Ui.Find<TextBox>(w, "FilterBox"), "#942");
            w.ViewModel.SelectedNode = w.ViewModel.Tree.Single();
            var frame = Ui.Capture(w, Ui.Find<LevelView>(w, "View"));
            Assert.Equal(Ui.Width, frame.Width);
            Assert.True(frame.DistinctColours() > 50, "the panels are drawn");
            Expect(Snapshots.Check(Ui.BaselineDir, "uw1-panels", frame, new Ui.Codec()));
            w.Close();
        }

        [AvaloniaFact]
        public async Task Snapshot_of_the_find_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var find = w.ShowFind();
            find.Model.Text = "garamon";
            await find.SearchAsync();
            var frame = Ui.Capture(find, Ui.Find<ListBox>(find, "Results"));
            Expect(Snapshots.Check(Ui.BaselineDir, "find-window", frame, new Ui.Codec()));
            find.Close();
            w.Close();
        }

        static void Expect(SnapshotResult r)
        {
            if (r.Outcome == SnapshotOutcome.Recorded) Assert.Skip(r.Message);
            Assert.True(r.Outcome == SnapshotOutcome.Matched, r.Message);
        }
    }
}
