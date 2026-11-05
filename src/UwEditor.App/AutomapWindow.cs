using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// The player's map as the game's map screen lays it out (320 × 200, three pixels a tile,
    /// north up), with the tiles seen, the notes, and optionally the level under them. Paints
    /// tiles seen or unseen, or places, moves and picks notes.
    /// </summary>
    public sealed class AutomapView : Control
    {
        public GameSession Session;
        public LevelModel Level;
        public Automap Map;
        /// <summary>"seen", "unseen" or "notes".</summary>
        public string Mode = "seen";
        public int Brush = 0;
        public bool ShowLevel = true;
        public MapNote Selected;
        public event Action Changed;
        public event Action<MapNote> NoteSelected;
        public event Action<MapNote> NoteAdded;

        bool dragging;
        Point dragStart;
        int noteX0, noteY0;

        static readonly IBrush Parchment = new SolidColorBrush(Color.Parse("#E9DCB8"));
        static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#6B4A2B"));
        static readonly IBrush Water = new SolidColorBrush(Color.Parse("#3F6FA8"));
        static readonly IBrush Lava = new SolidColorBrush(Color.Parse("#B8452A"));
        static readonly IBrush Ice = new SolidColorBrush(Color.Parse("#8FB8C8"));
        static readonly IBrush Door = new SolidColorBrush(Color.Parse("#2E1E10"));
        static readonly IBrush Known = new SolidColorBrush(Color.Parse("#B9A27C"));
        static readonly IBrush Ghost = new SolidColorBrush(Color.FromArgb(70, 120, 120, 120));
        static readonly IBrush NoteInk = new SolidColorBrush(Color.Parse("#1F1408"));
        static readonly IBrush SelectedBack = new SolidColorBrush(Color.FromArgb(90, 255, 210, 60));

        public AutomapView() { ClipToBounds = true; Focusable = true; }

        double Scale { get { return Math.Max(0.5, Math.Min(Bounds.Width / 320.0, Bounds.Height / 200.0)); } }
        Point Origin { get { double s = Scale; return new Point((Bounds.Width - 320 * s) / 2, (Bounds.Height - 200 * s) / 2); } }
        Point ToScreen(double px, double py) { var o = Origin; double s = Scale; return new Point(o.X + px * s, o.Y + (200 - py) * s); }
        void ToPixel(Point p, out double px, out double py) { var o = Origin; double s = Scale; px = (p.X - o.X) / s; py = 200 - (p.Y - o.Y) / s; }

        /// <summary>The screen rectangle of tile (x, y): its map pixels run up from (px, py).</summary>
        Rect TileRect(int x, int y)
        {
            double px, py;
            Automap.TileToPixel(x, y, out px, out py);
            var a = ToScreen(px, py + 3);
            return new Rect(a.X, a.Y, 3 * Scale, 3 * Scale);
        }

        FormattedText NoteText(MapNote n)
        {
            return new FormattedText(n.Text.Length == 0 ? "·" : n.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("DejaVu Sans Mono,Consolas,monospace")), Math.Max(7, 5 * Scale), NoteInk);
        }

        Rect NoteRect(MapNote n)
        {
            var t = NoteText(n);
            var p = ToScreen(n.X, n.Y);
            return new Rect(p.X, p.Y - t.Height, t.Width, t.Height);
        }

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(WidgetPaint.Brush(this, "Panel", Brushes.Black), new Rect(Bounds.Size));
            var o = Origin; double s = Scale;
            dc.FillRectangle(Parchment, new Rect(o.X, o.Y, 320 * s, 200 * s));
            if (Map == null || Level == null) return;
            bool uw2 = Session != null && Session.IsUw2;
            for (int y = 1; y < 63; y++)
                for (int x = 1; x < 63; x++)
                {
                    var r = TileRect(x, y);
                    if (ShowLevel && !Level.Tiles[x, y].IsSolid) dc.FillRectangle(Ghost, r);
                    int b = Map.Map[y * 64 + x], t = b & 0xF;
                    if (t == 0) continue;
                    if (t >= 10) { dc.FillRectangle(Known, r); continue; }          // UW2: known, not seen
                    int terr = uw2 ? (b >> 6) & 3 : (b >> 4) & 3;
                    bool door = uw2 ? (b & 0x30) == 0x10 : (b >> 6) == 1;
                    IBrush fill = terr == 1 ? Water : terr == 2 ? Lava : terr == 3 ? Ice : Ink;
                    var shape = new StreamGeometry();
                    using (var g = shape.Open())
                    {
                        // corners: 0 SW, 1 SE, 2 NE, 3 NW (screen: bottom left, bottom right, top right, top left)
                        var pts = new[] { r.BottomLeft, r.BottomRight, r.TopRight, r.TopLeft };
                        int[] poly = t == 2 ? new[] { 0, 1, 2 } : t == 3 ? new[] { 0, 1, 3 } : t == 4 ? new[] { 1, 2, 3 } : t == 5 ? new[] { 0, 2, 3 } : new[] { 0, 1, 2, 3 };
                        g.BeginFigure(pts[poly[0]], true);
                        for (int i = 1; i < poly.Length; i++) g.LineTo(pts[poly[i]]);
                        g.EndFigure(true);
                    }
                    dc.DrawGeometry(fill, null, shape);
                    if (t >= 6) dc.DrawLine(new Pen(Parchment, Math.Max(1, s * 0.6)), r.Center, t == 6 ? new Point(r.Center.X, r.Top) : t == 7 ? new Point(r.Center.X, r.Bottom) : t == 8 ? new Point(r.Right, r.Center.Y) : new Point(r.Left, r.Center.Y));
                    if (door) dc.FillRectangle(Door, r.Deflate(r.Width * 0.3));
                }
            foreach (var n in Map.Notes)
            {
                var rr = NoteRect(n);
                if (n == Selected) dc.FillRectangle(SelectedBack, rr.Inflate(2));
                dc.DrawText(NoteText(n), rr.TopLeft);
            }
        }

        MapNote NoteAt(Point p)
        {
            if (Map == null) return null;
            for (int i = Map.Notes.Count - 1; i >= 0; i--) if (NoteRect(Map.Notes[i]).Inflate(3).Contains(p)) return Map.Notes[i];
            return null;
        }

        void PaintAt(Point p)
        {
            double px, py, tx, ty;
            ToPixel(p, out px, out py);
            Automap.PixelToTile(px, py, out tx, out ty);
            int x = (int)Math.Floor(tx), y = (int)Math.Floor(ty);
            if (x < 0 || y < 0 || x > 63 || y > 63) return;
            if (Map.Paint(Session, Level, x, y, Brush, Mode == "seen") > 0) { InvalidateVisual(); Changed?.Invoke(); }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (Map == null || Level == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            if (Mode == "notes")
            {
                var n = NoteAt(p);
                if (n == null)
                {
                    if (Map.Notes.Count >= Automap.MaxNotes) return;
                    double px, py;
                    ToPixel(p, out px, out py);
                    Map.Snapshot("Add a note");
                    n = new MapNote { Text = "", X = (int)Math.Round(Math.Clamp(px, 0, 319)), Y = (int)Math.Round(Math.Clamp(py, 0, 199)) };
                    Map.Notes.Add(n);
                    Selected = n;
                    InvalidateVisual();
                    Changed?.Invoke();
                    NoteAdded?.Invoke(n);
                    return;
                }
                Selected = n;
                NoteSelected?.Invoke(n);
                dragging = true; dragStart = p; noteX0 = n.X; noteY0 = n.Y;
                Map.Snapshot("Move a note");
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            Map.Snapshot(Mode == "seen" ? "Mark seen" : "Mark unseen");
            dragging = true;
            e.Pointer.Capture(this);
            PaintAt(p);
            Changed?.Invoke();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!dragging) return;
            var p = e.GetPosition(this);
            if (Mode == "notes" && Selected != null)
            {
                double s = Scale;
                Selected.X = (int)Math.Round(Math.Clamp(noteX0 + (p.X - dragStart.X) / s, 0, 319));
                Selected.Y = (int)Math.Round(Math.Clamp(noteY0 - (p.Y - dragStart.Y) / s, 0, 199));
                InvalidateVisual();
                Changed?.Invoke();
            }
            else if (Mode != "notes") PaintAt(p);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            dragging = false;
            e.Pointer.Capture(null);
        }
    }

    /// <summary>Level ▸ Automap and map notes: the player's map of the current level and the notes on it, saved with the levels.</summary>
    public sealed class AutomapWindow : Window
    {
        readonly MainWindow main;
        readonly AutomapView view = new AutomapView();
        readonly ListBox notes = new ListBox();
        readonly TextBox text = new TextBox { Watermark = "The note's text (49 characters)", MaxLength = 49 };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock title = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        bool filling;

        public AutomapWindow(MainWindow main)
        {
            this.main = main;
            Title = "Automap and map notes";
            Width = 1100; Height = 720; MinWidth = 760; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            RadioButton Mode(string label, string mode, bool on)
            {
                var r = new RadioButton { Content = label, GroupName = "automapmode", IsChecked = on };
                r.IsCheckedChanged += (o, e) => { if (r.IsChecked == true) { view.Mode = mode; view.Cursor = new Cursor(mode == "notes" ? StandardCursorType.Ibeam : StandardCursorType.Cross); } };
                return r;
            }
            var brush = new ComboBox { ItemsSource = new[] { "1 tile", "3 × 3", "5 × 5", "9 × 9" }, SelectedIndex = 0, Width = 100 };
            brush.SelectionChanged += (o, e) => view.Brush = new[] { 0, 1, 2, 4 }[Math.Max(0, brush.SelectedIndex)];
            var showLevel = new CheckBox { Content = "Show the level under the map", IsChecked = true };
            showLevel.IsCheckedChanged += (o, e) => { view.ShowLevel = showLevel.IsChecked == true; view.InvalidateVisual(); };
            var all = new Button { Content = "Mark everything seen" };
            all.Click += (o, e) => Edit("Mark everything seen", a => a.All(main.ViewModel.Session, view.Level, true));
            var none = new Button { Content = "Mark nothing seen" };
            none.Click += (o, e) => Edit("Mark nothing seen", a => a.All(main.ViewModel.Session, view.Level, false));
            var undo = new Button { Content = "Undo" };
            undo.Click += (o, e) => { if (view.Map != null && view.Map.CanUndo) { view.Map.Undo(); Changed(); Fill(); } };

            var tools = new StackPanel { Spacing = 8, Width = 230, Margin = new Thickness(0, 0, 12, 0) };
            tools.Children.Add(new TextBlock { Text = "Paint", FontWeight = FontWeight.SemiBold });
            tools.Children.Add(Mode("Mark seen (as the game would)", "seen", true));
            tools.Children.Add(Mode("Mark unseen", "unseen", false));
            tools.Children.Add(Mode("Notes: click to add, drag to move", "notes", false));
            tools.Children.Add(Row(new TextBlock { Text = "Brush", VerticalAlignment = VerticalAlignment.Center }, brush));
            tools.Children.Add(showLevel);
            tools.Children.Add(all); tools.Children.Add(none); tools.Children.Add(undo);
            tools.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7,
                Text = "A seen tile gets the byte the game writes when you see it: the tile's shape, its floor's water, lava or ice, and doors. Rock is never drawn. Changes are saved with the levels (Save levels). A new game starts from DATA's maps, so Underworld II's castle is already drawn.",
            });

            notes.Bind(ListBox.BackgroundProperty, notes.GetResourceObservable("Panel"));
            notes.SelectionChanged += (o, e) =>
            {
                if (filling) return;
                view.Selected = notes.SelectedItem as MapNote;
                filling = true; text.Text = view.Selected?.Text ?? ""; filling = false;
                view.InvalidateVisual();
            };
            text.TextChanged += (o, e) =>
            {
                if (filling || view.Selected == null) return;
                view.Selected.Text = Automap.Clean(text.Text);
                Changed();
                RefreshList();
            };
            text.GotFocus += (o, e) => { if (view.Selected != null && view.Map != null) view.Map.Snapshot("Edit a note"); };
            var del = new Button { Content = "Delete the note" };
            del.Click += (o, e) =>
            {
                if (view.Selected == null || view.Map == null) return;
                view.Map.Snapshot("Delete a note");
                view.Map.Notes.Remove(view.Selected);
                view.Selected = null;
                Changed(); Fill();
            };
            var right = new DockPanel { Width = 300, Margin = new Thickness(12, 0, 0, 0) };
            var rhead = new StackPanel { Spacing = 6 };
            rhead.Children.Add(new TextBlock { Text = "Notes", FontWeight = FontWeight.SemiBold });
            rhead.Children.Add(text);
            rhead.Children.Add(del);
            DockPanel.SetDock(rhead, Dock.Top);
            right.Children.Add(rhead);
            right.Children.Add(notes);

            view.Changed += () => Changed();
            view.NoteSelected += n => { filling = true; notes.SelectedItem = n; text.Text = n.Text; filling = false; };
            view.NoteAdded += n => { Fill(); filling = true; notes.SelectedItem = n; text.Text = ""; filling = false; text.Focus(); };

            var dock = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(title);
            head.Children.Add(status);
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(tools, Dock.Left);
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(head); dock.Children.Add(tools); dock.Children.Add(right); dock.Children.Add(view);
            Content = dock;
            ManualWindow.Attach(this, "Automap and map notes");
            main.ViewModel.LevelChanged += OnLevel;
            main.ViewModel.LevelEdited += OnLevelEdited;
            Closed += (o, e) => { main.ViewModel.LevelChanged -= OnLevel; main.ViewModel.LevelEdited -= OnLevelEdited; };
            OnLevel(main.ViewModel.Level);
        }

        static StackPanel Row(params Control[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) p.Children.Add(x); return p; }

        public AutomapView View { get { return view; } }

        void OnLevel(LevelModel level)
        {
            var vm = main.ViewModel;
            view.Session = vm.Session;
            view.Level = level;
            view.Map = level == null || vm.Edits == null ? null : vm.Edits.AutomapFor(level.Level);
            view.Selected = null;
            Fill();
        }

        /// <summary>The level was edited in the main window: paint by its new shapes (the model is rebuilt after an edit).</summary>
        void OnLevelEdited()
        {
            var level = main.ViewModel.Level;
            if (level == null || view.Level == null || level.Level != view.Level.Level) return;
            view.Level = level;
            view.InvalidateVisual();
        }

        void Edit(string label, Action<Automap> a)
        {
            if (view.Map == null) return;
            view.Map.Snapshot(label);
            a(view.Map);
            Changed();
        }

        void Changed()
        {
            view.InvalidateVisual();
            if (view.Map != null) main.ViewModel.Edits.AutomapEdited(view.Map);
            UpdateStatus();
        }

        void Fill()
        {
            RefreshList();
            filling = true; text.Text = view.Selected?.Text ?? ""; filling = false;
            view.InvalidateVisual();
            UpdateStatus();
        }

        void RefreshList()
        {
            filling = true;
            var sel = view.Selected;
            notes.ItemsSource = view.Map == null ? new List<MapNote>() : view.Map.Notes.ToList();
            notes.SelectedItem = sel;
            filling = false;
        }

        void UpdateStatus()
        {
            var l = view.Level;
            title.Text = l == null ? "No level" : "The player's map of level " + l.Level;
            status.Text = view.Map == null ? "" : string.Format("{0} tiles seen, {1} of {2} notes{3}.", view.Map.SeenCount, view.Map.Notes.Count, Automap.MaxNotes, view.Map.Dirty ? "; unsaved changes" : "");
        }
    }
}
