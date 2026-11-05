using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace UwEditor.App
{
    /// <summary>
    /// The layout of the item swap and the item shuffle: the header, the settings (their own
    /// height up to a cap, scrolling inside when the window is short), the buttons, and below
    /// them the formula and the report, which take every pixel the window grows by. The formula
    /// is refreshed a few times a second from the settings as they are.
    /// </summary>
    public static class ItemToolLayout
    {
        /// <summary>The smallest height the formula and the report keep together.</summary>
        public const double MinText = 200;

        public static TextBox TextArea(bool wrap)
        {
            return new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
        }

        /// <summary>Lays the parts out in the window. settings gets a fixed height (cap, or less when the window is short); formula() is read again every 300 ms while the window is open.</summary>
        public static void Build(Window w, Control header, Control settings, Control buttons, TextBox formula, TextBox report, double cap, Func<string> formulaText)
        {
            var grid = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,2*") };
            Grid.SetRow(header, 0); grid.Children.Add(header);
            Grid.SetRow(settings, 1); grid.Children.Add(settings);
            Grid.SetRow(buttons, 2); grid.Children.Add(buttons);
            var fh = new TextBlock { Text = "Formula (the maths behind these settings)", FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(fh, 3); grid.Children.Add(fh);
            Grid.SetRow(formula, 4); grid.Children.Add(formula);
            var split = new GridSplitter { Height = 6, ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(split, 5); grid.Children.Add(split);
            Grid.SetRow(report, 6); grid.Children.Add(report);
            formula.MinHeight = 60; report.MinHeight = 80;
            w.Content = grid;

            Action fit = () =>
            {
                double h = w.ClientSize.Height - 32 - header.Bounds.Height - buttons.Bounds.Height - fh.Bounds.Height - 6 - MinText;
                double target = Math.Max(140, Math.Min(cap, h));
                if (Math.Abs(settings.Height - target) > 0.5) settings.Height = target;
            };
            settings.Height = cap;
            w.SizeChanged += (o, e) => fit();
            header.SizeChanged += (o, e) => fit();
            buttons.SizeChanged += (o, e) => fit();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            Action refresh = () =>
            {
                string t;
                try { t = formulaText(); } catch (Exception ex) { t = "The formula could not be written: " + ex.Message; }
                if (formula.Text != t) formula.Text = t;
            };
            timer.Tick += (o, e) => refresh();
            w.Opened += (o, e) => { refresh(); fit(); timer.Start(); };
            w.Closed += (o, e) => timer.Stop();
            refresh();
        }
    }
}
