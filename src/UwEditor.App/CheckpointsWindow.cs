using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>File ▸ Checkpoints: how often and how many, the checkpoints of the open game, restoring one.</summary>
    public sealed class CheckpointsWindow : Window
    {
        readonly MainWindow main;
        readonly ListBox list = new ListBox();
        readonly NumericUpDown minutes = new NumericUpDown { Minimum = 0, Maximum = 240, Increment = 1, FormatString = "0", Width = 120 };
        readonly NumericUpDown copies = new NumericUpDown { Minimum = 1, Maximum = Checkpoints.MaxCopies, Increment = 1, FormatString = "0", Width = 120 };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
        public ObservableCollection<CheckpointInfo> Items { get; } = new ObservableCollection<CheckpointInfo>();

        public CheckpointsWindow(MainWindow main)
        {
            this.main = main;
            Title = "Checkpoints";
            Width = 760; Height = 520; MinWidth = 520; MinHeight = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            minutes.Value = main.CheckpointMinutes;
            copies.Value = main.CheckpointCopies;
            minutes.ValueChanged += (s, e) => main.CheckpointMinutes = (int)(minutes.Value ?? 5);
            copies.ValueChanged += (s, e) => { main.CheckpointCopies = (int)(copies.Value ?? 20); Refresh(); };
            list.ItemsSource = Items;
            list.Bind(ListBox.BackgroundProperty, list.GetResourceObservable("Panel"));

            var now = new Button { Content = "Checkpoint now" };
            now.Click += (s, e) => { var c = main.WriteCheckpoint("by hand", true); status.Text = c == null ? "Nothing unsaved to keep." : "Written: " + c.Summary; Refresh(); };
            var restore = new Button { Content = "Restore" };
            restore.Click += async (s, e) => { if (list.SelectedItem is CheckpointInfo c) { status.Text = await main.RestoreCheckpointAsk(c); } };
            var delete = new Button { Content = "Delete" };
            delete.Click += (s, e) => { if (list.SelectedItem is CheckpointInfo c) { main.CheckpointStore?.Delete(c); Refresh(); } };
            list.DoubleTapped += async (s, e) => { if (list.SelectedItem is CheckpointInfo c) status.Text = await main.RestoreCheckpointAsk(c); };

            var settingsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
            settingsRow.Children.Add(new TextBlock { Text = "Every", VerticalAlignment = VerticalAlignment.Center });
            settingsRow.Children.Add(minutes);
            settingsRow.Children.Add(new TextBlock { Text = "minutes (0: never), keep the newest", VerticalAlignment = VerticalAlignment.Center });
            settingsRow.Children.Add(copies);
            settingsRow.Children.Add(new TextBlock { Text = "(up to " + Checkpoints.MaxCopies + ")", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(now); buttons.Children.Add(restore); buttons.Children.Add(delete);
            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10),
                Text = "While there are unsaved changes, a copy of them is kept every few minutes: the changed levels and automaps, and STRINGS.PAK or PLAYER.DAT when they have unsaved edits. " +
                       "Restoring loads a checkpoint back as unsaved edits that can be undone; the game folder is not touched until you save. Checkpoints are kept in " + (main.CheckpointStore?.Root ?? "the settings folder") + ".",
            };
            var dock = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "Checkpoints", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            head.Children.Add(intro);
            head.Children.Add(settingsRow);
            DockPanel.SetDock(head, Dock.Top);
            var foot = new StackPanel();
            foot.Children.Add(buttons);
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);
            dock.Children.Add(head); dock.Children.Add(foot); dock.Children.Add(list);
            Content = dock;
            ManualWindow.Attach(this, "Checkpoints");
            Refresh();
        }

        public void Refresh()
        {
            Items.Clear();
            var store = main.CheckpointStore;
            if (store == null) { status.Text = "Open a game first."; return; }
            foreach (var c in store.List()) Items.Add(c);
            if (string.IsNullOrEmpty(status.Text)) status.Text = Items.Count + " checkpoint(s).";
        }
    }
}
