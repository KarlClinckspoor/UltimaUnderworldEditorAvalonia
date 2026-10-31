using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace UwEditor.App
{
    /// <summary>
    /// Help ▸ Manual (F1): docs/MANUAL.md, built into the program, shown with its sections on the
    /// left and a search box. The renderer knows the Markdown the manual uses: headings,
    /// paragraphs, nested lists, tables, `code` and **bold**.
    /// </summary>
    public sealed class ManualWindow : Window
    {
        public const string OnlineUrl = "https://github.com/KarlClinckspoor/UltimaUnderworldEditorAvalonia/blob/main/docs/MANUAL.md";
        static ManualWindow open;

        readonly StackPanel page = new StackPanel { Spacing = 8, Margin = new Thickness(4, 0, 18, 24) };
        readonly ScrollViewer scroller;
        readonly ListBox toc = new ListBox();
        readonly TextBox search = new TextBox { PlaceholderText = "Search the manual (Enter: next)" };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
        readonly List<(string Title, Control Target, int Level)> headings = new List<(string, Control, int)>();
        readonly List<(Control Block, string Text)> searchable = new List<(Control, string)>();
        int lastHit = -1;

        public static string Text()
        {
            using (var s = typeof(ManualWindow).Assembly.GetManifestResourceStream("MANUAL.md"))
            {
                if (s == null) return "# Manual\n\nThe manual was not built into this program. It is at " + OnlineUrl + ".";
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        /// <summary>Shows the manual (one window), at a section whose title contains the text.</summary>
        public static ManualWindow ShowAt(Window owner, string section = null)
        {
            if (open == null)
            {
                open = new ManualWindow();
                open.Closed += (s, e) => open = null;
                if (owner != null) open.Show(owner); else open.Show();
            }
            else open.Activate();
            if (section != null) open.GoTo(section);
            return open;
        }

        /// <summary>F1 in a window opens the manual at its section.</summary>
        public static void Attach(Window w, string section)
        {
            w.AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.F1) { ShowAt(w, section); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public IReadOnlyList<string> Sections { get { return headings.Select(h => h.Title).ToList(); } }

        public ManualWindow()
        {
            Title = "Underworld Editor manual";
            Width = 1000; Height = 760; MinWidth = 640; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Build(Text());
            scroller = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            toc.ItemsSource = headings.Select(h => (h.Level > 2 ? "    " : "") + h.Title).ToList();
            toc.SelectionChanged += (s, e) => { if (toc.SelectedIndex >= 0) BringIntoView(headings[toc.SelectedIndex].Target); };
            toc.Background = Brushes.Transparent;
            search.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Find(search.Text, true); e.Handled = true; } };
            search.TextChanged += (s, e) => { lastHit = -1; Find(search.Text, false); };
            var online = new Button { Content = "Open online", Margin = new Thickness(8, 0, 0, 0) };
            online.Click += (s, e) => { try { Launcher.LaunchUriAsync(new Uri(OnlineUrl)); } catch (Exception) { } };
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            top.Children.Add(search);
            Grid.SetColumn(status, 1); status.Margin = new Thickness(10, 0, 0, 0); top.Children.Add(status);
            Grid.SetColumn(online, 2); top.Children.Add(online);
            var body = new Grid { ColumnDefinitions = new ColumnDefinitions("230,16,*") };
            body.Children.Add(toc);
            Grid.SetColumn(scroller, 2); body.Children.Add(scroller);
            var dock = new DockPanel { Margin = new Thickness(16) };
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(body);
            Content = dock;
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.F && (e.KeyModifiers & KeyModifiers.Control) != 0) { search.Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public bool GoTo(string section)
        {
            int i = headings.FindIndex(h => h.Title.IndexOf(section, StringComparison.OrdinalIgnoreCase) >= 0);
            if (i < 0) return false;
            toc.SelectedIndex = i;
            BringIntoView(headings[i].Target);
            return true;
        }

        void BringIntoView(Control c)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var p = c.TranslatePoint(new Point(0, 0), page);
                if (p != null && scroller != null) scroller.Offset = new Vector(0, Math.Max(0, p.Value.Y - 4));
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>Finds the next block containing the text; returns how many blocks contain it.</summary>
        public int Find(string text, bool next)
        {
            text = (text ?? "").Trim();
            if (text.Length < 2) { status.Text = ""; return 0; }
            var hits = searchable.Select((b, i) => (b, i)).Where(x => x.b.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).Select(x => x.i).ToList();
            status.Text = hits.Count == 0 ? "not found" : hits.Count + " places";
            if (hits.Count == 0) return 0;
            int at = hits.FirstOrDefault(i => i > lastHit, hits[0]);
            if (!next && lastHit >= 0) return hits.Count;
            lastHit = at;
            BringIntoView(searchable[at].Block);
            return hits.Count;
        }

        // ---- the Markdown the manual uses

        void Build(string md)
        {
            var lines = md.Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (line.Trim().Length == 0) { i++; continue; }
                var h = Regex.Match(line, @"^(#{1,4})\s+(.*)$");
                if (h.Success)
                {
                    int level = h.Groups[1].Length;
                    var tb = new TextBlock { Text = h.Groups[2].Value, FontWeight = FontWeight.SemiBold, FontSize = level == 1 ? 22 : level == 2 ? 17 : 14, Margin = new Thickness(0, level == 1 ? 0 : 12, 0, 2), TextWrapping = TextWrapping.Wrap };
                    if (level == 2) tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable("Accent"));
                    page.Children.Add(tb);
                    if (level >= 2) headings.Add((h.Groups[2].Value, tb, level));
                    searchable.Add((tb, tb.Text));
                    i++;
                    continue;
                }
                if (line.TrimStart().StartsWith("|"))
                {
                    var rows = new List<string[]>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                    {
                        var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
                        if (!cells.All(c => Regex.IsMatch(c, @"^:?-+:?$"))) rows.Add(cells);
                        i++;
                    }
                    AddTable(rows);
                    continue;
                }
                if (Regex.IsMatch(line, @"^\s*[-*]\s"))
                {
                    var items = new List<(int indent, string text)>();
                    while (i < lines.Length && lines[i].Trim().Length > 0)
                    {
                        var m = Regex.Match(lines[i], @"^(\s*)[-*]\s+(.*)$");
                        if (m.Success) items.Add((m.Groups[1].Length, m.Groups[2].Value));
                        else if (items.Count > 0) items[items.Count - 1] = (items[^1].indent, items[^1].text + " " + lines[i].Trim());
                        i++;
                    }
                    foreach (var it in items)
                    {
                        var row = new DockPanel { Margin = new Thickness(8 + it.indent * 9, 0, 0, 0) };
                        var dot = new TextBlock { Text = it.indent > 0 ? "◦" : "•", Width = 14, FontSize = 13 };
                        DockPanel.SetDock(dot, Dock.Left);
                        row.Children.Add(dot);
                        var tb = Para(it.text);
                        row.Children.Add(tb);
                        page.Children.Add(row);
                        searchable.Add((row, it.text));
                    }
                    continue;
                }
                // a paragraph: lines up to a blank line or another block
                var para = new List<string>();
                while (i < lines.Length && lines[i].Trim().Length > 0 && !lines[i].StartsWith("#") && !lines[i].TrimStart().StartsWith("|") && !Regex.IsMatch(lines[i], @"^\s*[-*]\s"))
                {
                    para.Add(lines[i].Trim());
                    i++;
                }
                var p = Para(string.Join(" ", para));
                page.Children.Add(p);
                searchable.Add((p, string.Join(" ", para)));
            }
        }

        void AddTable(List<string[]> rows)
        {
            if (rows.Count == 0) return;
            int cols = rows.Max(r => r.Length);
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            for (int c = 0; c < cols; c++) g.ColumnDefinitions.Add(new ColumnDefinition(c == cols - 1 ? GridLength.Star : GridLength.Auto));
            for (int r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var tb = Para(rows[r][c]);
                    tb.Margin = new Thickness(0, 2, 18, 2);
                    if (r == 0) tb.FontWeight = FontWeight.SemiBold;
                    Grid.SetRow(tb, r); Grid.SetColumn(tb, c);
                    g.Children.Add(tb);
                }
                searchable.Add((g, string.Join(" ", rows[r])));
            }
            page.Children.Add(g);
        }

        static TextBlock Para(string text)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20 };
            var mono = Application.Current?.FindResource("Mono") as FontFamily;
            foreach (Match m in Regex.Matches(text, @"(\*\*[^*]+\*\*|`[^`]+`|[^*`]+|[*`])"))
            {
                string t = m.Value;
                if (t.StartsWith("**") && t.EndsWith("**") && t.Length > 4) tb.Inlines.Add(new Run(t.Substring(2, t.Length - 4)) { FontWeight = FontWeight.SemiBold });
                else if (t.StartsWith("`") && t.EndsWith("`") && t.Length > 2)
                {
                    var run = new Run(t.Substring(1, t.Length - 2)) { FontFamily = mono ?? FontFamily.Default };
                    run.Bind(TextElement.ForegroundProperty, tb.GetResourceObservable("Code"));
                    tb.Inlines.Add(run);
                }
                else tb.Inlines.Add(new Run(t));
            }
            return tb;
        }
    }
}
