using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UwEditor.App
{
    /// <summary>A small dialog with a text and an OK button.</summary>
    public sealed class MessageWindow : Window
    {
        public static Task Show(Window owner, string title, string text, bool mono = false)
        {
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true, IsCancel = true };
            var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 };
            if (mono) { body.FontFamily = (FontFamily)Application.Current.FindResource("Mono"); body.TextWrapping = TextWrapping.NoWrap; }
            var w = new MessageWindow
            {
                Title = title,
                Width = mono ? 560 : 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new DockPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10), [DockPanel.DockProperty] = Dock.Top },
                        new Border { Child = ok, Margin = new Thickness(0, 16, 0, 0), [DockPanel.DockProperty] = Dock.Bottom },
                        body,
                    },
                },
            };
            ok.Click += (s, e) => w.Close();
            return w.ShowDialog(owner);
        }

        /// <summary>A question with buttons; returns the button chosen (the last one if the window is closed).</summary>
        public static async Task<string> Ask(Window owner, string title, string text, params string[] buttons)
        {
            string chosen = buttons.Last();
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var w = new MessageWindow
            {
                Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                string b = buttons[i];
                var btn = new Button { Content = b, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = i == 0, IsCancel = i == buttons.Length - 1 };
                btn.Click += (s, e) => { chosen = b; w.Close(); };
                row.Children.Add(btn);
            }
            w.Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10), [DockPanel.DockProperty] = Dock.Top },
                    new Border { Child = row, Margin = new Thickness(0, 16, 0, 0), [DockPanel.DockProperty] = Dock.Bottom },
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 },
                },
            };
            if (owner == null) return chosen;
            await w.ShowDialog(owner);
            return chosen;
        }
    }
}
