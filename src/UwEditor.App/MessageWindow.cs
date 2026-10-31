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
    }
}
