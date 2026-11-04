using System;
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
            if (mono) { body.FontFamily = Application.Current.FindResource("Mono") as FontFamily ?? FontFamily.Default; body.TextWrapping = TextWrapping.NoWrap; }
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

        /// <summary>
        /// A long monospaced report: a read-only text field that scrolls both ways (lines are not
        /// wrapped) in a window that can be resized, sized to most of the owner's screen. The text
        /// can be selected and copied (Ctrl+A, Ctrl+C).
        /// </summary>
        public static Task ShowReport(Window owner, string title, string text)
        {
            double maxW = 1100, maxH = 760;
            var screen = owner?.Screens?.ScreenFromWindow(owner);
            if (screen != null)
            {
                double sc = screen.Scaling > 0 ? screen.Scaling : 1;
                maxW = Math.Min(maxW, screen.WorkingArea.Width / sc * 0.9);
                maxH = Math.Min(maxH, screen.WorkingArea.Height / sc * 0.85);
            }
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true, IsCancel = true };
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                FontFamily = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default, FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            var w = new MessageWindow
            {
                Title = title, Width = maxW, Height = maxH, MinWidth = 420, MinHeight = 260, CanResize = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new DockPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10), [DockPanel.DockProperty] = Dock.Top },
                        new Border { Child = ok, Margin = new Thickness(0, 12, 0, 0), [DockPanel.DockProperty] = Dock.Bottom },
                        box,
                    },
                },
            };
            w.Report = box;
            ok.Click += (s, e) => w.Close();
            Opening?.Invoke(w);
            return owner == null ? Task.CompletedTask : w.ShowDialog(owner);
        }

        /// <summary>The text field of a report window (ShowReport), or null.</summary>
        public TextBox Report { get; private set; }

        /// <summary>
        /// Makes a tool window that keeps its own unsaved work ask before closing: Save, Discard or
        /// Cancel. ask says whether to ask at all (the main window turns it off when it has already
        /// asked about everything, or in tests); save returns false when saving failed.
        /// </summary>
        public static void GuardClose(Window w, string what, System.Func<bool> dirty, System.Func<bool> save, System.Func<bool> ask)
        {
            bool closing = false;
            w.Closing += async (s, e) =>
            {
                if (closing || !dirty() || (ask != null && !ask())) return;
                e.Cancel = true;
                string a = await Ask(w, "Unsaved changes", what + " has unsaved changes. Save them before closing?", "Save", "Discard", "Cancel");
                if (a == "Cancel") return;
                if (a == "Save" && !save()) return;
                closing = true;
                w.Close();
            };
        }

        /// <summary>A question with buttons; returns the button chosen (the last one if the window is closed).</summary>
        public static async Task<string> Ask(Window owner, string title, string text, params string[] buttons)
        {
            string chosen = buttons.Last();
            // the buttons wrap onto more lines when they do not fit (six choices for a saved game's slot), kept to the right
            var row = new WrapPanel { Orientation = Orientation.Horizontal, ItemsAlignment = WrapPanelItemsAlignment.End, ItemSpacing = 8, LineSpacing = 8 };
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
            w.Buttons = row;
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
            Opening?.Invoke(w);
            await w.ShowDialog(owner);
            return chosen;
        }

        /// <summary>The row of buttons of a question (Ask).</summary>
        public Panel Buttons { get; private set; }

        /// <summary>A question window is about to be shown (tests look at it, and close it).</summary>
        public static event System.Action<MessageWindow> Opening;

        /// <summary>
        /// A long monospaced text in columns side by side (the controls): the text's paragraphs (split
        /// at blank lines) go in order into columns of about the same height, so a long list fits on the screen.
        /// </summary>
        public static Task ShowColumns(Window owner, string title, string text, int columns = 2)
        {
            var w = new MessageWindow
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true, IsCancel = true };
            ok.Click += (s, e) => w.Close();
            var grid = new Grid();
            var parts = SplitColumns(text, columns);
            var mono = Application.Current?.FindResource("Mono") as FontFamily ?? FontFamily.Default;
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28)));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var tb = new TextBlock { Text = parts[i], FontFamily = mono, FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.NoWrap };
                Grid.SetColumn(tb, 2 * i);
                grid.Children.Add(tb);
            }
            w.Content = new DockPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10), [DockPanel.DockProperty] = Dock.Top },
                    new Border { Child = ok, Margin = new Thickness(0, 16, 0, 0), [DockPanel.DockProperty] = Dock.Bottom },
                    new ScrollViewer { Content = grid, MaxHeight = 820, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto },
                },
            };
            Opening?.Invoke(w);
            return owner == null ? Task.CompletedTask : w.ShowDialog(owner);
        }

        /// <summary>The paragraphs of a text (split at blank lines) shared in order among columns of about the same number of lines.</summary>
        public static System.Collections.Generic.List<string> SplitColumns(string text, int columns)
        {
            var paras = (text ?? "").Replace("\r\n", "\n").Split(new[] { "\n\n" }, System.StringSplitOptions.None).ToList();
            int total = paras.Sum(p => p.Split('\n').Length + 1);
            var o = new System.Collections.Generic.List<string>();
            var cur = new System.Collections.Generic.List<string>();
            int lines = 0, done = 0;
            for (int i = 0; i < paras.Count; i++)
            {
                int n = paras[i].Split('\n').Length + 1;
                // a new column when this one has its share and the paragraph would take it past it
                int share = (total - done) / System.Math.Max(1, columns - o.Count);
                if (cur.Count > 0 && o.Count < columns - 1 && lines + n / 2 > share)
                {
                    o.Add(string.Join("\n\n", cur));
                    done += lines; cur.Clear(); lines = 0;
                }
                cur.Add(paras[i]);
                lines += n;
            }
            if (cur.Count > 0) o.Add(string.Join("\n\n", cur));
            return o;
        }
    }
}
