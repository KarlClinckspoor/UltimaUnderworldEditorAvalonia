using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using UltimateEditor.Uw1;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Object tables: OBJECTS.DAT's class tables (weapons, missiles, armour, creatures,
    /// containers, lights, food, trigger modes, animations) and COMOBJ.DAT's common properties,
    /// one tab and one grid each. A cell is edited in place (click, type, Enter); the values are
    /// range-checked, changed cells are marked, and Save writes the two files to DATA.
    /// </summary>
    public sealed class ObjectTablesWindow : Window, IShowsItemIcons
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly TabControl tabs = new TabControl();
        readonly TextBox filter = new TextBox { Watermark = "Filter by name or number (0x40)", Width = 260 };
        readonly CheckBox changedOnly = new CheckBox { Content = "Changed rows only" };
        readonly TextBlock explain = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, MinHeight = 34 };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly TextBlock changes = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.75 };
        readonly Button saveButton = new Button { Content = "Save", FontWeight = FontWeight.SemiBold };
        readonly List<TablePage> pages = new List<TablePage>();

        static readonly IBrush ChangedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xE0, 0x9A, 0x20));
        static readonly IBrush LineBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));

        /// <summary>The tables being edited (null when the files could not be read).</summary>
        public ObjectTables Tables { get; private set; }

        /// <summary>A picture file was saved: every table's icons are taken again.</summary>
        public void RefreshIcons() { foreach (var p in pages) p.RefreshIcons(); }

        /// <summary>The cell being edited, or null.</summary>
        CellEdit editing;
        /// <summary>The row last clicked (for Revert row).</summary>
        TablePage selPage; RowItem selRow;

        public ObjectTablesWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Title = "Object tables — " + s.Title;
            Width = 1180; Height = 780; MinWidth = 720; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Object tables", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = "The fixed properties of each kind of object, from DATA's OBJECTS.DAT (the class tables) and COMOBJ.DAT (the common properties). " +
                       "Click a cell to edit it; Enter or Tab keeps the value, Escape discards it. Numbers are decimal, or hexadecimal with 0x. " +
                       (s.IsSave ? "A saved game has no copy of its own: these files apply to every saved game and to a new game." : "The game reads them at start-up; saved games use them too."),
            });
            DockPanel.SetDock(top, Dock.Top);

            var revertRow = new Button { Content = "Revert row" };
            ToolTip.SetTip(revertRow, "Put the selected row back as it is in the file.");
            revertRow.Click += (o, e) => RevertRow();
            var revertAll = new Button { Content = "Revert all" };
            ToolTip.SetTip(revertAll, "Discard every unsaved change in both files.");
            revertAll.Click += (o, e) => { CommitEdit(); Tables?.RevertAll(); SetStatus("All changes discarded.", false); };
            saveButton.Click += (o, e) => SaveAll();
            filter.TextChanged += (o, e) => ApplyFilter();
            changedOnly.IsCheckedChanged += (o, e) => ApplyFilter();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            bar.Children.Add(filter); bar.Children.Add(changedOnly); bar.Children.Add(revertRow); bar.Children.Add(revertAll); bar.Children.Add(saveButton); bar.Children.Add(changes);
            DockPanel.SetDock(bar, Dock.Top);

            var foot = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            foot.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = LineBrush, Padding = new Thickness(0, 6, 0, 0), Child = explain });
            foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(bar); dock.Children.Add(foot);
            try
            {
                Tables = ObjectTables.Load(s);
                foreach (var t in ObjectTables.Layout) pages.Add(new TablePage(this, t));
                foreach (var p in pages) tabs.Items.Add(new TabItem { Header = p.Table.Name, Content = p.View });
                tabs.SelectionChanged += (o, e) => { CommitEdit(); var p = CurrentPage; if (p != null) explain.Text = p.Table.Summary + " (" + p.Table.Source + ")"; };
                tabs.SelectedIndex = 0;
                Tables.Changed += OnChanged;
                dock.Children.Add(tabs);
                SetStatus(string.Format("{0} and {1} from {2}.", ObjectTables.ObjectsFile, ObjectTables.ComObjFile, s.DataFolder), false);
            }
            catch (Exception ex)
            {
                Tables = null;
                dock.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
                saveButton.IsEnabled = false;
            }
            Content = dock;
            UpdateCounts();
            ManualWindow.Attach(this, "Object tables");
        }

        /// <summary>Unsaved edits in either file.</summary>
        public bool HasChanges { get { return Tables != null && (Tables.HasChanges || editing != null && editing.Box.Text != editing.Original); } }

        /// <summary>Writes the changed files (and the cell being typed in). False when that failed.</summary>
        public bool SaveAll()
        {
            if (Tables == null) return false;
            if (editing != null && !Commit()) return false;     // a refused value: reported, nothing written
            try
            {
                var written = Tables.Save();
                if (written.Count == 0) { SetStatus("Nothing to save.", false); return true; }
                // the open level is drawn again: COMOBJ.DAT says how each object is drawn
                if (main.ViewModel.Level != null && main.ViewModel.Edits != null) main.ViewModel.RefreshLevel();
                SetStatus("Saved " + string.Join(" and ", written) + " (the previous version is kept as .BKP, the first as .ORIG).", false);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Could not save: " + ex.Message, true);
                return false;
            }
        }

        /// <summary>The page of the tab shown.</summary>
        TablePage CurrentPage { get { int i = tabs.SelectedIndex; return i >= 0 && i < pages.Count ? pages[i] : null; } }

        /// <summary>The status line's text (errors and what was done).</summary>
        public string StatusText { get { return status.Text; } }
        /// <summary>The explanation shown for the selected field.</summary>
        public string ExplanationText { get { return explain.Text; } }

        /// <summary>Shows a table's tab.</summary>
        public void ShowTable(string name) { int i = pages.FindIndex(p => p.Table.Name == name); if (i >= 0) tabs.SelectedIndex = i; }

        /// <summary>The rows the filter lets through in a table.</summary>
        public int VisibleRows(string table) { var p = pages.First(x => x.Table.Name == table); return p.Shown.Count; }

        /// <summary>
        /// Types text into a cell, as a click, typing and Enter do: returns null when taken,
        /// else the error shown in the status line. For tests and scripting.
        /// </summary>
        public string EditCell(string table, int row, string field, string text)
        {
            ShowTable(table);
            var p = pages.First(x => x.Table.Name == table);
            int k = Array.FindIndex(p.Fields, f => f.Name == field);
            var r = p.Rows[row];
            p.List.ScrollIntoView(r);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            BeginEdit(p, r, k);
            if (editing == null) return "The cell is not shown.";
            editing.Box.Text = text;
            return Commit() ? null : status.Text;
        }

        /// <summary>Selects a row of a table (for Revert row).</summary>
        public void SelectRow(string table, int row)
        {
            ShowTable(table);
            var p = pages.First(x => x.Table.Name == table);
            selPage = p; selRow = p.Rows[row];
            p.List.SelectedItem = selRow;
        }

        void OnChanged()
        {
            foreach (var p in pages) p.UpdateRealized();
            if (changedOnly.IsChecked == true) ApplyFilter();
            UpdateCounts();
        }

        void UpdateCounts()
        {
            int n = Tables == null ? 0 : Tables.ChangedRows;
            changes.Text = n == 0 ? "No unsaved changes" : n + (n == 1 ? " row changed" : " rows changed");
            saveButton.IsEnabled = Tables != null && n > 0;
        }

        void ApplyFilter()
        {
            CommitEdit();
            foreach (var p in pages) p.Filter(filter.Text, changedOnly.IsChecked == true);
        }

        void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.Foreground = error ? Brush("Error", Brushes.IndianRed) : null;
            status.FontWeight = error ? FontWeight.SemiBold : FontWeight.Normal;
        }

        IBrush Brush(string key, IBrush fallback)
        {
            object o;
            return this.TryFindResource(key, ActualThemeVariant, out o) && o is IBrush b ? b : fallback;
        }

        void RevertRow()
        {
            CommitEdit();
            var p = CurrentPage;
            var r = p?.List.SelectedItem as RowItem ?? (selPage == p ? selRow : null);
            if (p == null || r == null) { SetStatus("Select a row first (click one of its cells).", true); return; }
            if (!Tables.IsRowChanged(p.Table, r.Row)) { SetStatus(r.Label + " has no changes.", false); return; }
            Tables.RevertRow(p.Table, r.Row);
            SetStatus(r.Label + " is back as it is in " + p.Table.FileName + ".", false);
        }

        // ---- the cell being edited

        sealed class CellEdit
        {
            public TablePage Page;
            public RowItem Row;
            public int Field;
            public TextBox Box;
            public string Original;
        }

        void Select(TablePage p, RowItem r, int k)
        {
            selPage = p; selRow = r;
            p.List.SelectedItem = r;
            ShowExplanation(p, r, k);
        }

        void ShowExplanation(TablePage p, RowItem r, int k)
        {
            if (k < 0) { explain.Text = p.Table.Summary; return; }
            var f = p.Fields[k];
            string text = ObjectTables.Explain(p.Table, f);
            if (r != null)
            {
                int v = Tables.Get(p.Table, r.Row, f);
                string m = Tables.Meaning(p.Table, f, v);
                text += "\n" + r.Label + ": " + v.ToString(CultureInfo.InvariantCulture) + (m != null ? " (" + m + ")" : "");
                if (Tables.IsChanged(p.Table, r.Row, f)) text += ", was " + Tables.SavedValue(p.Table, r.Row, f).ToString(CultureInfo.InvariantCulture) + " in the file";
                text += ".";
            }
            explain.Text = text;
        }

        void BeginEdit(TablePage p, RowItem r, int k)
        {
            if (editing != null && editing.Page == p && editing.Row == r && editing.Field == k) return;
            CommitEdit();
            Select(p, r, k);
            var cell = r.Cells != null && k < r.Cells.Length ? r.Cells[k] : null;
            if (cell == null) return;
            string value = Tables.Get(p.Table, r.Row, p.Fields[k]).ToString(CultureInfo.InvariantCulture);
            var box = new TextBox { Text = value, FontSize = 12, Padding = new Thickness(3, 1), MinHeight = 0, MinWidth = 0, BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
            editing = new CellEdit { Page = p, Row = r, Field = k, Box = box, Original = value };
            box.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Commit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; Cancel(); }
                else if (e.Key == Key.Tab)
                {
                    e.Handled = true;
                    var ed = editing;
                    if (ed == null || !Commit()) return;
                    int next = ed.Field + ((e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
                    if (next >= 0 && next < ed.Page.Fields.Length) BeginEdit(ed.Page, ed.Row, next);
                }
            };
            box.LostFocus += (o, e) => { if (editing != null && editing.Box == box) Commit(); };
            cell.Child = box;
            cell.BringIntoView();
            box.Focus();
            box.SelectAll();
        }

        /// <summary>Takes the typed value; false (the cell stays open) when it was refused.</summary>
        bool Commit()
        {
            var ed = editing;
            if (ed == null) return true;
            var f = ed.Page.Fields[ed.Field];
            string text = ed.Box.Text ?? "";
            if (text.Trim() == ed.Original) { Close(ed); return true; }
            string err = Tables.SetText(ed.Page.Table, ed.Row.Row, f, text);
            if (err != null)
            {
                SetStatus(ed.Row.Label + ": " + err, true);
                Close(ed);      // the cell shows the value kept
                return false;
            }
            Close(ed);
            int v = Tables.Get(ed.Page.Table, ed.Row.Row, f);
            string warn = Tables.Warning(ed.Page.Table, f, v);
            string m = Tables.Meaning(ed.Page.Table, f, v);
            if (warn != null) SetStatus(ed.Row.Label + ": " + f.Name + " = " + v + ". " + warn, true);
            else SetStatus(ed.Row.Label + ": " + f.Name + " = " + v + (m != null ? " (" + m + ")" : "") + ".", false);
            ShowExplanation(ed.Page, ed.Row, ed.Field);
            return true;
        }

        /// <summary>Commits the cell being edited; a refused value is dropped (and reported in the status line).</summary>
        void CommitEdit()
        {
            if (editing != null) Commit();
        }

        void Cancel()
        {
            var ed = editing;
            if (ed == null) return;
            Close(ed);
            SetStatus("Edit discarded.", false);
        }

        void Close(CellEdit ed)
        {
            if (editing == ed) editing = null;
            ed.Page.Paint(ed.Row, ed.Field);
        }

        // ---- one tab

        /// <summary>One row: a record of a table.</summary>
        public sealed class RowItem
        {
            public int Row;
            public int Item;
            public string Label;
            public string Search;
            public Bitmap Icon;
            /// <summary>The row's icon while it is on screen (null otherwise).</summary>
            public Image IconImage;
            /// <summary>The row's cells while it is on screen (null otherwise).</summary>
            public Border[] Cells;
            public Border Marker;
            public Control Head;
        }

        sealed class TablePage
        {
            readonly ObjectTablesWindow w;
            public readonly ObjectTable Table;
            public readonly FieldDef[] Fields;
            public readonly double[] Widths;
            public readonly List<RowItem> Rows = new List<RowItem>();
            public List<RowItem> Shown = new List<RowItem>();
            public readonly ListBox List = new ListBox();
            public readonly DockPanel View = new DockPanel();
            readonly ScrollViewer headScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Control headLabel;
            double hOffset;
            const double LabelWidth = 250, RowHeight = 24;

            public TablePage(ObjectTablesWindow w, ObjectTable t)
            {
                this.w = w;
                Table = t;
                Fields = t.Schema.Fields;
                Widths = Fields.Select(f => Math.Max(f.Bits == 1 ? 46.0 : 56.0, Math.Min(96.0, 7.0 * Longest(f.Name) + 12))).ToArray();
                var names = w.session.Names;
                for (int i = 0; i < t.Count; i++)
                {
                    int item = t.ItemOf(i);
                    string name = names.Get(item);
                    if (t.Name == "Triggers" && w.session.IsUw2) name += " / " + names.Get(item + 0x10);
                    Rows.Add(new RowItem { Row = i, Item = item, Label = string.Format("0x{0:X3} {1}", item, name), Search = (name + " " + item.ToString("X3") + " 0x" + item.ToString("X") + " " + item).ToLowerInvariant() });
                }
                foreach (var r in Rows) r.Icon = w.main.ViewModel.IconOf(r.Item);

                var head = new StackPanel { Orientation = Orientation.Horizontal, Height = 52 };
                headLabel = new Border { Width = LabelWidth + 22, Child = new TextBlock { Text = "Item", FontWeight = FontWeight.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30, 0, 0, 4) }, ZIndex = 1 };
                headLabel.Bind(Border.BackgroundProperty, w.GetResourceObservable("Panel"));
                head.Children.Add(headLabel);
                for (int k = 0; k < Fields.Length; k++)
                {
                    var f = Fields[k];
                    int kk = k;
                    var tb = new TextBlock { Text = f.Name, TextWrapping = TextWrapping.Wrap, FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, Opacity = f.Certainty == Certainty.Unknown ? 0.55 : 1 };
                    var b = new Border { Width = Widths[k], Padding = new Thickness(3, 0, 3, 4), Child = tb, Background = Brushes.Transparent, BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 1, 1) };
                    ToolTip.SetTip(b, f.Description);
                    b.PointerPressed += (o, e) => { w.CommitEdit(); w.ShowExplanation(this, null, kk); };
                    head.Children.Add(b);
                }
                headScroll.Content = head;
                DockPanel.SetDock(headScroll, Dock.Top);

                ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Auto);
                // compact rows that line up with the header
                List.Padding = new Thickness(0);
                List.BorderThickness = new Thickness(0);
                List.Bind(ListBox.BackgroundProperty, w.GetResourceObservable("Panel"));
                List.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
                {
                    Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, 0.0) },
                });
                headScroll.Bind(ScrollViewer.BackgroundProperty, w.GetResourceObservable("Panel"));
                List.ItemTemplate = new FuncDataTemplate<RowItem>((r, ns) => Build(r));
                List.AddHandler(ScrollViewer.ScrollChangedEvent, (o, e) =>
                {
                    // only the list's own scroll viewer: the edited cell's text box has one too,
                    // and its offset (0) put the header and the labels back to the left
                    if (!(e.Source is ScrollViewer sv) || sv.TemplatedParent != List) return;
                    hOffset = sv.Offset.X;
                    headScroll.Offset = new Vector(hOffset, 0);
                    headLabel.RenderTransform = new TranslateTransform(hOffset, 0);
                    foreach (var r in Shown) if (r.Head != null) r.Head.RenderTransform = new TranslateTransform(hOffset, 0);
                });
                View.Children.Add(headScroll);
                View.Children.Add(List);
                Filter(null, false);
            }

            static int Longest(string s) { return s.Split(' ', '/').Max(x => x.Length); }

            public void RefreshIcons()
            {
                foreach (var r in Rows)
                {
                    r.Icon = w.main.ViewModel.IconOf(r.Item);
                    if (r.IconImage != null) r.IconImage.Source = r.Icon;
                }
            }

            public void Filter(string text, bool changedOnly)
            {
                string q = (text ?? "").Trim().ToLowerInvariant();
                if (q.StartsWith("0x", StringComparison.Ordinal)) q = q.Substring(2).TrimStart('0');
                Shown = Rows.Where(r => (q.Length == 0 || r.Search.Contains(q)) && (!changedOnly || w.Tables.IsRowChanged(Table, r.Row))).ToList();
                List.ItemsSource = Shown;
            }

            Control Build(RowItem r)
            {
                if (r == null) return new TextBlock();
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Height = RowHeight };
                r.Marker = new Border { Width = 4, Margin = new Thickness(0, 2, 4, 2), CornerRadius = new CornerRadius(2) };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Width = LabelWidth + 22, ZIndex = 1 };
                label.Bind(StackPanel.BackgroundProperty, w.GetResourceObservable("Panel"));
                label.Children.Add(r.Marker);
                r.IconImage = new Image { Source = r.Icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(r.IconImage);
                label.Children.Add(new TextBlock { Text = r.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = LabelWidth - 30 });
                label.RenderTransform = new TranslateTransform(hOffset, 0);
                r.Head = label;
                sp.Children.Add(label);
                r.Cells = new Border[Fields.Length];
                for (int k = 0; k < Fields.Length; k++)
                {
                    int kk = k;
                    var b = new Border { Width = Widths[k], BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 1, 0), Background = Brushes.Transparent };
                    b.PointerPressed += (o, e) =>
                    {
                        if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                        e.Handled = true;
                        w.BeginEdit(this, r, kk);
                    };
                    r.Cells[k] = b;
                    sp.Children.Add(b);
                }
                Update(r);
                return sp;
            }

            /// <summary>Refreshes the rows on screen from the tables.</summary>
            public void UpdateRealized() { foreach (var r in Shown) if (r.Cells != null) Update(r); }

            public void Update(RowItem r)
            {
                if (r.Cells == null) return;
                for (int k = 0; k < Fields.Length; k++) Paint(r, k);
                r.Marker.Background = w.Tables.IsRowChanged(Table, r.Row) ? w.Brush("Accent", Brushes.Orange) : null;
            }

            /// <summary>Draws one cell: its value, marked when changed. Only the cell being edited
            /// has a frame (its text box's); a closed cell, changed or not, has none.</summary>
            public void Paint(RowItem r, int k)
            {
                if (r.Cells == null || k < 0 || k >= r.Cells.Length) return;
                var b = r.Cells[k];
                if (w.editing != null && w.editing.Row == r && w.editing.Field == k && w.editing.Page == this) return;
                var f = Fields[k];
                int v = w.Tables.Get(Table, r.Row, f);
                bool changed = w.Tables.IsChanged(Table, r.Row, f);
                b.Child = new TextBlock
                {
                    Text = v.ToString(CultureInfo.InvariantCulture), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
                    FontWeight = changed ? FontWeight.SemiBold : FontWeight.Normal,
                    Opacity = f.Certainty == Certainty.Unknown && !changed ? 0.6 : 1,
                };
                b.Background = changed ? ChangedBrush : Brushes.Transparent;
                b.BorderBrush = LineBrush;
                b.BorderThickness = new Thickness(0, 0, 1, 0);
            }
        }
    }
}
