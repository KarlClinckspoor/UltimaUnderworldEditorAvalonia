using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>Edit ▸ Undo history: the current level's steps, newest at the top; clicking one goes back (or forward) to it.</summary>
    public sealed class UndoHistoryWindow : Window
    {
        readonly MainWindow main;
        readonly ListBox list = new ListBox();
        readonly TextBlock title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock others = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        bool filling;

        /// <summary>One row: a step, how far back it lies (0 the state now, n undone ones are negative).</summary>
        public sealed class Row
        {
            public string Text { get; set; }
            public int Back { get; set; }
            public bool Done { get; set; }
            public override string ToString() { return Text; }
        }

        public UndoHistoryWindow(MainWindow main)
        {
            this.main = main;
            Title = "Undo history";
            Width = 420; Height = 560; MinWidth = 300; MinHeight = 260;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            list.SelectionChanged += (s, e) =>
            {
                if (filling || !(list.SelectedItem is Row r) || r.Back == 0) return;
                var ed = Editor;
                if (ed == null) return;
                // steps made on several levels at once go back and forward together (LevelEdits.Step)
                string refused;
                int n = main.ViewModel.Edits.Step(ed.Level, r.Back, out refused);
                main.ViewModel.FlushRefresh();
                main.ViewModel.EditStatus = (r.Back > 0 ? "Went back " + n + " step(s)." : "Went forward " + n + " step(s).") + (refused != null ? " " + refused : "");
                Fill();
            };
            var dock = new DockPanel { Margin = new Thickness(14) };
            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(title);
            head.Children.Add(status);
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(others, Dock.Bottom);
            others.Margin = new Thickness(0, 8, 0, 0);
            dock.Children.Add(head); dock.Children.Add(others); dock.Children.Add(list);
            Content = dock;
            ManualWindow.Attach(this, "Undo history");
            var vm = main.ViewModel;
            Action<LevelModel> onLevel = l => Fill();
            Action onEdit = Fill;
            vm.LevelChanged += onLevel;
            vm.LevelEdited += onEdit;
            Closed += (s, e) => { vm.LevelChanged -= onLevel; vm.LevelEdited -= onEdit; };
            Fill();
        }

        LevelEditor Editor { get { var vm = main.ViewModel; return vm.Level == null ? null : vm.Edits?.Existing(vm.Level.Level); } }

        /// <summary>The rows shown (for the tests).</summary>
        public List<Row> Rows { get; private set; } = new List<Row>();

        public void Fill()
        {
            var vm = main.ViewModel;
            filling = true;
            Rows = new List<Row>();
            var ed = Editor;
            title.Text = vm.Level == null ? "No level" : "Level " + vm.Level.Level;
            if (ed != null)
            {
                var redo = ed.RedoLabels;
                var undo = ed.UndoLabels;
                // the redo steps on top (the next one nearest), then the steps done, the newest (the state now) marked
                for (int i = 0; i < redo.Count; i++) Rows.Add(new Row { Text = "↷ " + redo[i], Back = -(redo.Count - i), Done = false });
                for (int i = undo.Count - 1; i >= 0; i--) Rows.Add(new Row { Text = (i == undo.Count - 1 ? "● " : "   ") + undo[i], Back = undo.Count - 1 - i, Done = true });
                Rows.Add(new Row { Text = (undo.Count == 0 ? "● " : "   ") + "As opened (or the oldest step kept)", Back = undo.Count, Done = true });
                status.Text = string.Format("{0} step(s) to undo, {1} to redo. Click a step to put the level back as it was after that step; ↷ marks steps to redo. Up to {2} steps are kept.", undo.Count, redo.Count, LevelEditor.UndoDepth);
            }
            else status.Text = "Nothing edited on this level yet.";
            list.ItemsSource = Rows;
            list.SelectedItem = Rows.FirstOrDefault(r => r.Back == 0);
            var dirty = vm.Edits == null ? new List<int>() : vm.Edits.Editors.Where(e => e.CanUndo && (vm.Level == null || e.Level != vm.Level.Level)).Select(e => e.Level).OrderBy(l => l).ToList();
            others.Text = dirty.Count == 0 ? "" : "Other levels with history: " + string.Join(", ", dirty) + " (each level keeps its own).";
            filling = false;
        }
    }

    /// <summary>Level ▸ World check: locks without keys, keys without locks, levels near the object limits.</summary>
    public sealed class WorldCheckWindow : Window
    {
        readonly MainWindow main;
        readonly ListBox list = new ListBox();
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        public List<WorldFinding> Findings { get; private set; } = new List<WorldFinding>();

        public WorldCheckWindow(MainWindow main)
        {
            this.main = main;
            Title = "World check";
            Width = 860; Height = 600; MinWidth = 520; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var again = new Button { Content = "Check again" };
            again.Click += (s, e) => Run();
            var go = new Button { Content = "Go to" };
            go.Click += (s, e) => GoTo();
            list.DoubleTapped += (s, e) => GoTo();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(again); row.Children.Add(go);
            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "World check", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Checks every level, unsaved edits included, for locked locks that no key opens (keys count on the floor, in containers, carried by creatures, made by traps, and in a saved game's inventory) and keys whose lock is nowhere, and shows how full each level's object lists are. Double-click a line to go there.",
            });
            head.Children.Add(status);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(row, Dock.Bottom);
            dock.Children.Add(head); dock.Children.Add(row); dock.Children.Add(list);
            Content = dock;
            ManualWindow.Attach(this, "World check");
            Run();
        }

        public void Run()
        {
            var vm = main.ViewModel;
            if (vm.Session == null) return;
            var keys = main.CarriedKeys();
            Findings = WorldCheck.Run(vm.Session, l => vm.Edits != null ? vm.Edits.Model(l) : vm.Session.LoadLevel(l), keys);
            list.ItemsSource = Findings.Select(f => new TextBlock
            {
                Text = (f.Severity == 2 ? "✖ " : f.Severity == 1 ? "▲ " : "· ") + f.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = f.Severity == 2 ? new SolidColorBrush(Color.Parse("#FF6B5E")) : f.Severity == 1 ? new SolidColorBrush(Color.Parse("#FFC24A")) : null,
                Tag = f,
            }).ToList();
            int bad = Findings.Count(f => f.Severity == 2), warn = Findings.Count(f => f.Severity == 1);
            status.Text = bad == 0 && warn == 0 ? "Nothing wrong found." : string.Format("{0} problem(s), {1} warning(s).", bad, warn);
        }

        void GoTo()
        {
            if (!(list.SelectedItem is TextBlock tb) || !(tb.Tag is WorldFinding f)) return;
            if (f.X >= 0) main.GoToTile(f.Level, f.X, f.Y);
            else main.ViewModel.LoadLevel(f.Level);
            if (f.Index > 0)
            {
                main.ViewModel.FlushRefresh();
                main.ViewModel.Select(f.Index, -1, -1, true);
            }
        }
    }
}
