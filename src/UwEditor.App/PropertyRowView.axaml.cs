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

        void OnFlagClick(object sender, RoutedEventArgs e)
        {
            var row = DataContext as PropertyRow;
            if (row == null || !row.IsFlag) return;
            Committed?.Invoke(row, Flag.IsChecked == true ? "1" : "0");
        }

        void Step(int by)
        {
            var row = DataContext as PropertyRow;
            if (row == null || !row.IsNumber) return;
            int v;
            if (!int.TryParse((Box.Text ?? "").Trim(), out v) && !int.TryParse(row.Value, out v)) return;
            int max = row.Maximum, min = row.Minimum;
            int n = v + by;
            if (row.IsHeading) n = ((n % 8) + 8) % 8;     // a heading turns round
            else n = Math.Max(min, Math.Min(max, n));
            if (n == v) return;
            Box.Text = n.ToString();
            Committed?.Invoke(row, n.ToString());
        }

        void OnUp(object sender, RoutedEventArgs e) { Step(1); }
        void OnDown(object sender, RoutedEventArgs e) { Step(-1); }

        void OnLostFocus(object sender, RoutedEventArgs e) { Commit(); }
    }
}
