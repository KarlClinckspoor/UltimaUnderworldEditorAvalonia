using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UwEditor.App
{
    /// <summary>A column of a SortableTable: its header, width, text and sort key.</summary>
    public sealed class TableColumn<T>
    {
        public string Header { get; set; }
        public double Width { get; set; } = 80;
        /// <summary>Numbers: right-aligned.</summary>
        public bool Right { get; set; }
        public Func<T, string> Text { get; set; }
        /// <summary>What the column sorts by (the text when null).</summary>
        public Func<T, IComparable> Key { get; set; }
        /// <summary>A tooltip for the header.</summary>
        public string Tip { get; set; }
        /// <summary>The column takes the width the others leave (at least Width).</summary>
        public bool Fill { get; set; }
    }

    /// <summary>
    /// A read-only table whose column headers sort it: a click sorts by that column, a second
    /// click reverses the order. Built from plain controls (no DataGrid package).
    /// </summary>
    public sealed class SortableTable<T> : ScrollViewer
    {
        readonly List<TableColumn<T>> columns;
        readonly List<T> rows;
        readonly Grid header = new Grid();
        readonly Grid body = new Grid();
        readonly List<Button> headerButtons = new List<Button>();
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");

        /// <summary>The column sorted by (-1: the rows' own order).</summary>
        public int SortColumn { get; private set; } = -1;
        public bool Descending { get; private set; }
        /// <summary>The rows in the order shown.</summary>
        public List<T> Shown { get; private set; }

        public SortableTable(IEnumerable<TableColumn<T>> columns, IEnumerable<T> rows)
        {
            this.columns = columns.ToList();
            this.rows = rows.ToList();
            Shown = this.rows.ToList();
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
            foreach (var g in new[] { header, body })
                foreach (var c in this.columns) g.ColumnDefinitions.Add(c.Fill ? new ColumnDefinition(1, GridUnitType.Star) { MinWidth = c.Width } : new ColumnDefinition(c.Width, GridUnitType.Pixel));
            for (int i = 0; i < this.columns.Count; i++)
            {
                int col = i;
                var b = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3), FontWeight = FontWeight.SemiBold, FontSize = 12,
                    HorizontalContentAlignment = this.columns[i].Right ? HorizontalAlignment.Right : HorizontalAlignment.Left, CornerRadius = new CornerRadius(0),
                };
                if (this.columns[i].Tip != null) ToolTip.SetTip(b, this.columns[i].Tip);
                b.Click += (o, e) => SortBy(col);
                Grid.SetColumn(b, i);
                header.Children.Add(b);
                headerButtons.Add(b);
            }
            var rowsView = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var dock = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };
            // as wide as the view (a filling column takes the rest), never narrower than the columns: then it scrolls
            double least = this.columns.Sum(c => c.Width);
            dock.Width = least;
            SizeChanged += (o, e) => dock.Width = Math.Max(least, e.NewSize.Width);
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);
            dock.Children.Add(rowsView);
            Content = dock;
            Build();
        }

        /// <summary>A header click: sort by the column, or reverse the order when it is the one sorted by.</summary>
        public void SortBy(int column)
        {
            if (column < 0 || column >= columns.Count) return;
            Descending = column == SortColumn ? !Descending : false;
            SortColumn = column;
            var c = columns[column];
            Func<T, IComparable> key = c.Key ?? (r => c.Text(r) ?? "");
            var indexed = rows.Select((r, i) => (r, i));
            var sorted = Descending ? indexed.OrderByDescending(x => key(x.r), Comparer<IComparable>.Create(Compare)) : indexed.OrderBy(x => key(x.r), Comparer<IComparable>.Create(Compare));
            Shown = sorted.ThenBy(x => x.i).Select(x => x.r).ToList();
            Build();
        }

        /// <summary>New rows, shown in the order sorted by now.</summary>
        public void SetRows(IEnumerable<T> newRows)
        {
            rows.Clear();
            rows.AddRange(newRows);
            if (SortColumn < 0) { Shown = rows.ToList(); Build(); return; }
            int col = SortColumn;
            bool desc = Descending;
            SortColumn = -1;
            SortBy(col);
            if (desc) SortBy(col);
        }

        static int Compare(IComparable a, IComparable b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            return a.CompareTo(b);
        }

        void Build()
        {
            for (int i = 0; i < columns.Count; i++)
                headerButtons[i].Content = columns[i].Header + (i == SortColumn ? (Descending ? " ▼" : " ▲") : "");
            body.Children.Clear();
            body.RowDefinitions.Clear();
            for (int r = 0; r < Shown.Count; r++)
            {
                body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (r % 2 == 1)
                {
                    var band = new Border { Opacity = 0.06, Background = Brushes.Gray };
                    Grid.SetRow(band, r); Grid.SetColumnSpan(band, columns.Count);
                    body.Children.Add(band);
                }
                for (int i = 0; i < columns.Count; i++)
                {
                    var t = new TextBlock
                    {
                        Text = columns[i].Text(Shown[r]) ?? "", FontFamily = Mono, FontSize = 12, Margin = new Thickness(6, 2),
                        TextAlignment = columns[i].Right ? TextAlignment.Right : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    if (columns[i].Fill && t.Text.Length > 0) ToolTip.SetTip(t, t.Text);
                    Grid.SetRow(t, r); Grid.SetColumn(t, i);
                    body.Children.Add(t);
                }
            }
        }
    }
}
