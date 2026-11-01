using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Level ▸ Item cleaner: removes objects by kind (debris, bones, junk, food, every loose item...)
    /// from one level or all, never what a quest or the game needs (Core's ItemCleaner). A preview
    /// lists what would go, with counts, and what is kept and why; Remove deletes them, one
    /// Edit ▸ Undo step a level.
    /// </summary>
    public sealed class ItemCleanerWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly CleanOptions opt = new CleanOptions();
        readonly ComboBox scope = new ComboBox { ItemsSource = new[] { "This level", "Every level" }, SelectedIndex = 0, Width = 160 };
        readonly ListBox list = new ListBox();
        readonly TextBox summary = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Height = 150 };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        readonly Button remove = new Button { Content = "Remove listed", IsEnabled = false };
        CleanPlan plan;

        public ItemCleanerWindow(MainWindow main)
        {
            this.main = main;
            session = main.ViewModel.Session;
            Title = "Item cleaner";
            Width = 1000; Height = 720; MinWidth = 720; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var kinds = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in ItemCleaner.Choices)
            {
                var k = c.Key;
                var box = new CheckBox { Content = c.Value, IsChecked = (opt.Kinds & k) != 0, Margin = new Thickness(0, 0, 14, 0) };
                box.IsCheckedChanged += (o, e) => { opt.Kinds = box.IsChecked == true ? opt.Kinds | k : opt.Kinds & ~k; Stale(); };
                kinds.Children.Add(box);
            }
            var inside = new CheckBox { Content = "Also inside containers", IsChecked = opt.InsideContainers };
            inside.IsCheckedChanged += (o, e) => { opt.InsideContainers = inside.IsChecked == true; Stale(); };
            var carried = new CheckBox { Content = "Also what creatures carry (never characters who talk)", IsChecked = opt.CreatureInventories };
            carried.IsCheckedChanged += (o, e) => { opt.CreatureInventories = carried.IsChecked == true; Stale(); };
            var name = new TextBox { Watermark = "Name contains", Width = 160 };
            name.TextChanged += (o, e) => { opt.Name = name.Text ?? ""; Stale(); };
            NumericUpDown Coord(int v, Action<int> set)
            {
                var n = new NumericUpDown { Minimum = 0, Maximum = 63, Increment = 1, FormatString = "0", Value = v, Width = 110 };
                n.ValueChanged += (o, e) => { set((int)(n.Value ?? 0)); Stale(); };
                return n;
            }
            scope.SelectionChanged += (o, e) => Stale();

            var preview = new Button { Content = "Preview", FontWeight = FontWeight.SemiBold };
            preview.Click += (o, e) => Preview();
            remove.Click += (o, e) => Remove();

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Item cleaner", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Removes objects of the kinds ticked. Never removed: keys, locks, quest items, talismans, runestones, books and scrolls with a text, maps, " +
                       "anything a trap or trigger names, the kinds an inventory trap asks for, objects that hold something (contents, a trigger, a lock), " +
                       "what characters who talk carry, creatures, doors, furniture, decals, switches, traps and triggers. Conversations can ask for ordinary items too: look before cleaning a quest area.",
            });
            top.Children.Add(kinds);
            top.Children.Add(Row(scope, inside, carried));
            top.Children.Add(Row(Label("Tiles from"), Coord(opt.X0, v => opt.X0 = v), Coord(opt.Y0, v => opt.Y0 = v), Label("to"), Coord(opt.X1, v => opt.X1 = v), Coord(opt.Y1, v => opt.Y1 = v), name));
            var buttons = Row(preview, remove, status);
            top.Children.Add(buttons);
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(summary, Dock.Bottom);
            summary.Margin = new Thickness(0, 8, 0, 0);

            list.DoubleTapped += (o, e) => GoToSelected();
            list.KeyDown += (o, e) => { if (e.Key == Key.Enter) { GoToSelected(); e.Handled = true; } };

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(summary); dock.Children.Add(list);
            Content = dock;
            ManualWindow.Attach(this, "Item cleaner");
            status.Text = "Pick the kinds, then Preview.";
        }

        /// <summary>The game the window works on.</summary>
        public GameSession Session { get { return session; } }

        static TextBlock Label(string t) { return new TextBlock { Text = t, VerticalAlignment = VerticalAlignment.Center }; }
        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foreach (var x in c) p.Children.Add(x); return p; }

        /// <summary>The settings, for tests and the window's controls.</summary>
        public CleanOptions Options { get { return opt; } }
        /// <summary>The last preview (null once a setting changed).</summary>
        public CleanPlan Plan { get { return plan; } }

        /// <summary>0 this level, 1 every level.</summary>
        public int Scope { get { return scope.SelectedIndex; } set { scope.SelectedIndex = value; } }

        void Stale()
        {
            plan = null;
            remove.IsEnabled = false;
            status.Text = "Settings changed: Preview again.";
        }

        IEnumerable<int> Levels
        {
            get
            {
                if (scope.SelectedIndex == 0) { var l = main.ViewModel.Level; return l != null && l.Present ? new[] { l.Level } : new int[0]; }
                return session.LevelSlots().Where(s => s.Present).Select(s => s.Level);
            }
        }

        /// <summary>The Preview button: lists what would go, with counts by name and what is kept.</summary>
        public CleanPlan Preview()
        {
            plan = ItemCleaner.Preview(session, main.ViewModel.Edits.Model, Levels.ToList(), opt);
            list.ItemsSource = plan.Remove;
            int levels = plan.Remove.Select(c => c.Level).Distinct().Count();
            var lines = new List<string>();
            lines.Add(string.Format("{0} object{1} would be removed from {2} level{3}.", plan.Remove.Count, plan.Remove.Count == 1 ? "" : "s", levels, levels == 1 ? "" : "s"));
            if (plan.ByName.Count > 0) lines.Add("By name: " + string.Join(", ", plan.ByName.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)) + ".");
            if (plan.Kept.Count > 0) lines.Add("Kept: " + string.Join(", ", plan.Kept.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)) + ".");
            summary.Text = string.Join("\n", lines);
            remove.IsEnabled = plan.Remove.Count > 0;
            status.Text = plan.Remove.Count == 0 ? "Nothing to remove." : "Double-click an object to go to it.";
            return plan;
        }

        /// <summary>The Remove button: deletes what the preview listed, one undoable step a level.</summary>
        public int Remove()
        {
            if (plan == null || plan.Remove.Count == 0) { status.Text = "Preview first."; return 0; }
            int n, levels = plan.Remove.Select(c => c.Level).Distinct().Count();
            try { n = ItemCleaner.Apply(main.ViewModel.Edits, plan); }
            catch (Exception ex) { status.Text = "Removing failed: " + ex.Message; return 0; }
            Preview();
            status.Text = string.Format("{0} object{1} removed from {2} level{3}; Edit ▸ Undo on a level puts them back.", n, n == 1 ? "" : "s", levels, levels == 1 ? "" : "s");
            return n;
        }

        void GoToSelected()
        {
            var c = list.SelectedItem as CleanCandidate;
            if (c != null) main.GoToObject(c.Level, c.Index);
        }
    }
}
