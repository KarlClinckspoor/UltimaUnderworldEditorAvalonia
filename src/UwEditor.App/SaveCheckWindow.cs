using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// File ▸ Check and repair the saved game: what makes the game crash or complain on a saved
    /// game (map notes read from another block, notes with no end, bad automap blocks, lost
    /// objects), each with its repair, made as unsaved edits.
    /// </summary>
    public sealed class SaveCheckWindow : Window
    {
        readonly MainWindow main;
        readonly ListBox list = new ListBox { SelectionMode = SelectionMode.Multiple };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
        public List<SaveFinding> Findings { get; private set; } = new List<SaveFinding>();

        public SaveCheckWindow(MainWindow main)
        {
            this.main = main;
            Title = "Check and repair the saved game";
            Width = 900; Height = 560; MinWidth = 520; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var again = new Button { Content = "Check again" };
            again.Click += (s, e) => Run();
            var fixSel = new Button { Content = "Repair the selected" };
            fixSel.Click += (s, e) => Repair(list.SelectedItems.OfType<TextBlock>().Select(t => t.Tag as SaveFinding).Where(f => f != null).ToList());
            var fixAll = new Button { Content = "Repair all", IsDefault = true };
            fixAll.Click += (s, e) => Repair(Findings.ToList());
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(fixAll); row.Children.Add(fixSel); row.Children.Add(again);
            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Check and repair the saved game", FontSize = 16, FontWeight = FontWeight.SemiBold });
            head.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "Finds what makes the original game crash or complain with this saved game: map notes read from another level's automap (the game's own bug when all of a level's notes are erased; opening the map then crashes), notes with no end, automap blocks of the wrong size, and objects lost from the object lists (\"Problems in object list\"). " +
                       "Repairs are unsaved edits: look them over, then Save. Nothing else in the save changes.",
            });
            head.Children.Add(status);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(row, Dock.Bottom);
            dock.Children.Add(head); dock.Children.Add(row); dock.Children.Add(list);
            Content = dock;
            ManualWindow.Attach(this, "Check and repair a saved game");
            Run();
        }

        public void Run()
        {
            var vm = main.ViewModel;
            if (vm.Session == null) return;
            Findings = SaveCheck.Find(vm.Session, vm.Edits);
            list.ItemsSource = Findings.Select(f => new TextBlock
            {
                Text = f + (f.CanFix ? "\n      Repair: " + f.FixText : "\n      (no automatic repair)"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = f.Problem ? new SolidColorBrush(Color.Parse("#FF6B5E")) : new SolidColorBrush(Color.Parse("#FFC24A")),
                Tag = f,
            }).ToList();
            status.Text = Findings.Count == 0 ? "Nothing wrong found." : string.Format("{0} problem(s), {1} warning(s).", Findings.Count(f => f.Problem), Findings.Count(f => !f.Problem));
        }

        public int Repair(List<SaveFinding> which)
        {
            var vm = main.ViewModel;
            if (vm.Edits == null || which.Count == 0) return 0;
            int n = 0;
            foreach (var f in which) if (SaveCheck.Repair(f, vm.Edits)) n++;
            vm.FlushRefresh();
            vm.RaiseEditState();
            main.UpdateRunMarker();
            Run();
            string msg = n == 0 ? "Nothing repaired." : "Repaired " + n + " as unsaved edits: Save (Ctrl+S) writes them into the saved game.";
            status.Text = msg + " " + status.Text;
            vm.EditStatus = msg;
            return n;
        }
    }
}
