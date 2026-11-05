using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Schedules (UW2): the 16 schedules of SCD.ARK, block 0 by the time of day and
    /// block n by X clock n, each a list of rows in time order. A row's fields are named as
    /// event/SCDEVENT.C reads them, with their ranges; rows are added, copied and deleted, a
    /// new time moves a row to its place. Save writes the file (SCD.ARK.BKP keeps the old one).
    /// </summary>
    public sealed class ScheduleWindow : Window
    {
        readonly MainWindow main;
        readonly Schedules sc;
        readonly ComboBox blockBox = new ComboBox { Width = 330 };
        readonly ListBox rowList = new ListBox { FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"), FontSize = 12 };
        readonly StackPanel fieldPanel = new StackPanel { Spacing = 4 };
        readonly TextBlock rowText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        readonly Button saveButton = new Button { Content = "Save SCD.ARK", FontWeight = FontWeight.SemiBold };
        readonly Button revertButton = new Button { Content = "Revert" };
        int block, row = -1;
        bool filling;

        public GameSession Session { get { return sc.Session; } }
        public Schedules Schedules { get { return sc; } }
        public int Block { get { return block; } }
        public int Row { get { return row; } }
        public string StatusText { get { return status.Text; } }
        /// <summary>Set when the window is closed without asking (another game opened).</summary>
        public bool Quiet { get; private set; }

        public ScheduleWindow(MainWindow main, Schedules schedules)
        {
            this.main = main;
            sc = schedules;
            Title = "Schedules — " + sc.Where + "/SCD.ARK";
            Width = 1100; Height = 760; MinWidth = 760; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Schedules", FontSize = 16, FontWeight = FontWeight.SemiBold });
            top.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75,
                Text = (sc.Where == "DATA" ? "DATA/SCD.ARK: what a new game starts with (the game copies it into SAVE0). " : sc.Where + "/SCD.ARK: this saved game's own schedules. ") +
                       "Block 0 runs by the time of day (72 steps a day, 0-71); block n runs by X clock n (0-255). A row runs once its block's clock passes its time, on the level it names, " +
                       "while the player is there; a level the player is away from catches up when the player comes back. Rows stay in time order, so giving a row a new time moves it. " +
                       "A negative event is disabled: a test row (event 10) above it runs it when the test passes.",
            });
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            tools.Children.Add(new TextBlock { Text = "Block", VerticalAlignment = VerticalAlignment.Center });
            tools.Children.Add(blockBox);
            var add = new Button { Content = "Add row" };
            ToolTip.SetTip(add, "Add a new row after the selected one, with the same time (event 0, any level).");
            add.Click += (o, e) => AddRow(false);
            var copy = new Button { Content = "Copy row" };
            copy.Click += (o, e) => AddRow(true);
            var del = new Button { Content = "Delete row" };
            del.Click += (o, e) => DeleteRow();
            var up = new Button { Content = "▲" };
            ToolTip.SetTip(up, "Move the row up past the one above. Rows with the same time run in list order.");
            up.Click += (o, e) => MoveRow(-1);
            var down = new Button { Content = "▼" };
            down.Click += (o, e) => MoveRow(1);
            foreach (var b in new Control[] { add, copy, del, up, down }) tools.Children.Add(b);
            top.Children.Add(tools);
            DockPanel.SetDock(top, Dock.Top);

            saveButton.Click += (o, e) => SaveAll();
            revertButton.Click += (o, e) => Revert();
            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 12, 0) };
            buttons.Children.Add(saveButton); buttons.Children.Add(revertButton);
            DockPanel.SetDock(buttons, Dock.Left);
            foot.Children.Add(buttons); foot.Children.Add(status);
            DockPanel.SetDock(foot, Dock.Bottom);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,8,2*") };
            var right = new ScrollViewer { Content = new StackPanel { Children = { rowText, fieldPanel } }, Padding = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(rowList, 0);
            Grid.SetColumn(right, 2);
            grid.Children.Add(rowList); grid.Children.Add(right);

            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(grid);
            Content = dock;

            // compact rows: a long block shows more of itself
            rowList.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>()) { Setters = { new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Thickness(8, 3)), new Avalonia.Styling.Setter(ListBoxItem.MinHeightProperty, 0.0) } });
            blockBox.SelectionChanged += (o, e) => { if (!filling && blockBox.SelectedIndex >= 0) SelectBlock(blockBox.SelectedIndex); };
            rowList.SelectionChanged += (o, e) => { if (!filling) { row = rowList.SelectedIndex; FillFields(); } };
            FillBlocks();
            SelectBlock(0);
            ManualWindow.Attach(this, "Schedules");
        }

        /// <summary>Closes without the unsaved-changes question (the caller asked already).</summary>
        public void CloseQuietly() { Quiet = true; Close(); }

        public bool HasChanges { get { return sc.HasChanges; } }

        void FillBlocks()
        {
            filling = true;
            int keep = block;
            blockBox.ItemsSource = Enumerable.Range(0, sc.Blocks.Count).Select(b => string.Format("{0}: {1} ({2} rows)", b, ScdArk.ClockName(b), sc.Blocks[b].Rows.Count)).ToList();
            blockBox.SelectedIndex = keep;
            filling = false;
        }

        /// <summary>Shows a block's rows (the first selected).</summary>
        public void SelectBlock(int b)
        {
            block = Math.Max(0, Math.Min(sc.Blocks.Count - 1, b));
            if (blockBox.SelectedIndex != block) { filling = true; blockBox.SelectedIndex = block; filling = false; }
            FillRows(sc.Blocks[block].Rows.Count > 0 ? 0 : -1);
        }

        public void SelectRow(int i)
        {
            FillRows(i);
        }

        string RowLine(int i, ScdRow r)
        {
            return string.Format("{0,3}  t {1,3}  {2,-9} {3}{4}", i, r.Time, r.Level == 0xFF ? "any" : r.Level >= 0xF6 ? "world " + (r.Level - 0xF6) : "lvl " + r.Level, r.Once ? "once " : "", sc.Describe(r));
        }

        void FillRows(int select)
        {
            var rows = sc.Blocks[block].Rows;
            filling = true;
            rowList.ItemsSource = rows.Select((r, i) => RowLine(i, r)).ToList();
            row = rows.Count == 0 ? -1 : Math.Max(-1, Math.Min(rows.Count - 1, select));
            rowList.SelectedIndex = row;
            filling = false;
            if (row >= 0) rowList.ScrollIntoView(row);
            FillFields();
            UpdateState();
        }

        void FillFields()
        {
            fieldPanel.Children.Clear();
            var rows = sc.Blocks[block].Rows;
            if (row < 0 || row >= rows.Count)
            {
                rowText.Text = rows.Count == 0 ? "This schedule has no rows; click Add row to make one." : "Select a row.";
                return;
            }
            var r = rows[row];
            rowText.Text = "Row " + row + ": " + ScdRow.LevelText(r.Level) + ": " + sc.Describe(r);
            foreach (var f in sc.Fields(block, r)) fieldPanel.Children.Add(FieldRow(r, f));
        }

        Control FieldRow(ScdRow r, ScdField f)
        {
            int v = f.Get(r);
            var name = new TextBlock { Text = f.Name, Width = 120, VerticalAlignment = VerticalAlignment.Center };
            var box = new NumericUpDown
            {
                Minimum = Math.Min(f.Min, v), Maximum = Math.Max(f.Max, v), Value = v, Increment = 1, FormatString = "0", Width = 130,
                VerticalAlignment = VerticalAlignment.Center, Tag = f.Name,
            };
            var meaning = new TextBlock { Text = Range(f) + "  " + sc.Meaning(r, f), FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(name, f.Help);
            ToolTip.SetTip(box, f.Help);
            box.ValueChanged += (o, e) =>
            {
                if (filling || box.Value == null) return;
                int nv = (int)box.Value.Value;
                // the field list depends on the event and mode: rebuild after this control is done
                Dispatcher.UIThread.Post(() => SetField(f.Name, nv));
            };
            var p = new Grid { ColumnDefinitions = new ColumnDefinitions("120,130,*"), ColumnSpacing = 8 };
            Grid.SetColumn(box, 1);
            Grid.SetColumn(meaning, 2);
            p.Children.Add(name); p.Children.Add(box); p.Children.Add(meaning);
            return p;
        }

        static string Range(ScdField f) { return "(" + f.Min + "–" + f.Max + ")"; }

        /// <summary>Sets a field of the selected row by name. False (with the reason in the status line) when refused.</summary>
        public bool SetField(string name, int value)
        {
            var rows = sc.Blocks[block].Rows;
            if (row < 0 || row >= rows.Count) return false;
            var f = sc.Fields(block, rows[row]).FirstOrDefault(x => x.Name == name);
            if (f == null) { status.Text = "This row has no field " + name + "."; return false; }
            if (f.Get(rows[row]) == value) return true;
            try
            {
                int was = row;
                int at = sc.SetField(block, row, f, value);
                status.Text = f.Name + " of row " + was + " set to " + value + (at != was ? "; the row moved to " + at + " by its time." : ".");
                FillBlocks();
                FillRows(at);
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; FillFields(); return false; }
        }

        /// <summary>Adds a row after the selected one (a copy of it, or an empty one with its time).</summary>
        public void AddRow(bool copy)
        {
            int at = sc.AddRow(block, row, copy && row >= 0);
            status.Text = (copy && row >= 0 ? "Row " + row + " copied" : "Row added") + " as row " + at + ".";
            FillBlocks();
            FillRows(at);
        }

        public void DeleteRow()
        {
            if (row < 0) return;
            var r = sc.Blocks[block].Rows[row];
            sc.DeleteRow(block, row);
            status.Text = "Row " + row + " deleted" + (r.Event == 10 ? ": the disabled rows after it no longer belong to a test." : ".");
            FillBlocks();
            FillRows(row);
        }

        public void MoveRow(int d)
        {
            if (row < 0) return;
            if (!sc.MoveRow(block, row, d)) { status.Text = "Rows run in order of time: change the time to move a row past rows of another time."; return; }
            FillRows(row + d);
        }

        /// <summary>The file as it is on disk again.</summary>
        public void Revert()
        {
            sc.Revert();
            status.Text = "Back to " + sc.Where + "/SCD.ARK as it is on disk.";
            FillBlocks();
            SelectBlock(block);
        }

        /// <summary>Writes SCD.ARK; false when refused or failed (the reason in the status line).</summary>
        public bool SaveAll()
        {
            try
            {
                var written = sc.Save();
                status.Text = written.Count == 0 ? "Nothing changed." : "Saved block" + (written.Count == 1 ? " " : "s ") + string.Join(", ", written) + " to " + sc.Path + " (the old file is kept as SCD.ARK.BKP).";
                UpdateState();
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }

        void UpdateState()
        {
            bool dirty = sc.HasChanges;
            saveButton.IsEnabled = dirty;
            revertButton.IsEnabled = dirty;
            var problems = sc.Problems();
            if (problems.Count > 0) status.Text = string.Join(" ", problems);
            Title = "Schedules — " + sc.Where + "/SCD.ARK" + (dirty ? " *" : "");
        }
    }
}
