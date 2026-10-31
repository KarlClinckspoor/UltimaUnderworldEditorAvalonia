using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>One row of the properties panel. Record fields can be typed into (decimal or 0x hex); Enter or leaving the box commits.</summary>
    public partial class PropertyRowView : UserControl
    {
        /// <summary>A value was typed into a row: the row and the text.</summary>
        public static event Action<PropertyRow, string> Committed;

        public PropertyRowView() { InitializeComponent(); }

        void Commit()
        {
            var row = DataContext as PropertyRow;
            if (row == null || !row.Editable) return;
            string text = Box.Text ?? "";
            if (text.Trim() == (row.Value ?? "")) return;
            Committed?.Invoke(row, text);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Box.Text = (DataContext as PropertyRow)?.Value; e.Handled = true; }
        }

        void OnPick(object sender, RoutedEventArgs e)
        {
            var row = DataContext as PropertyRow;
            if (row?.Choices == null) return;
            var menu = new MenuFlyout();
            foreach (var c in row.Choices)
            {
                var mi = new MenuItem { Header = c.Label, FontWeight = c.Value.ToString() == row.Value ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal };
                int v = c.Value;
                mi.Click += (s, a) => { Box.Text = v.ToString(); Commit(); };
                menu.Items.Add(mi);
            }
            menu.ShowAt(PickButton);
        }

        void OnLostFocus(object sender, RoutedEventArgs e) { Commit(); }
    }
}
