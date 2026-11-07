using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core.Conversations;

namespace UwEditor.App
{
    /// <summary>
    /// Draws a ConversationGraph: nodes as boxes with their lines, edges with their conditions
    /// and answers. Drag to pan, the wheel to zoom; a click on a node selects it. Laid out in
    /// layers, or hanging from the start (HangingMode: the HangingLayout runs on a timer until it
    /// settles; a node dragged stays where it is put, held, until a right-click lets it go).
    /// </summary>
    public sealed class GraphCanvas : Control
    {
        sealed class NodeLook
        {
            public FormattedText Title;
            public readonly List<Tuple<FormattedText, double>> Lines = new List<Tuple<FormattedText, double>>();
        }

        public const double NodeWidth = 270, CodeWidth = 230;
        static readonly Typeface Sans = new Typeface(FontFamily.Default);
        static readonly Typeface MonoFace = new Typeface(new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"));
        readonly Dictionary<GraphNode, NodeLook> looks = new Dictionary<GraphNode, NodeLook>();
        readonly Dictionary<GraphEdge, FormattedText> labels = new Dictionary<GraphEdge, FormattedText>();
        double scale = 1;
        Vector offset = new Vector(20, 20);
        Point? dragFrom;
        Vector dragOffset;
        bool dragged, needHome = true;
        HangingLayout hanging;
        bool hangingMode;
        GraphNode dragNode;
        Vector grab;
        readonly Avalonia.Threading.DispatcherTimer timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        public ConversationGraph Graph { get; private set; }
        public GraphNode Selected { get; private set; }
        public double Scale { get { return scale; } }
        /// <summary>A node was clicked (selected).</summary>
        public event Action<GraphNode> NodeClicked;
        /// <summary>The text a node shows for a line (the window puts the current strings in).</summary>
        public Func<GraphLine, string> LineText = l => l.Text;
        public bool ShowStatements = true;
        /// <summary>The layered layout's choices (null: the best; tests draw the old layout with LayeredOptions.Before).</summary>
        public LayeredOptions Options;

        /// <summary>The hanging layout running, or null when laid out in layers.</summary>
        public HangingLayout Hanging { get { return hanging; } }
        /// <summary>The hanging layout is still moving the nodes.</summary>
        public bool Moving { get { return timer.IsEnabled; } }

        public GraphCanvas()
        {
            ClipToBounds = true;
            Focusable = true;
            timer.Tick += (o, e) => Tick();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            timer.Stop();
        }

        /// <summary>The graph hangs from its start (true) or stands in layers.</summary>
        public bool HangingMode
        {
            get { return hangingMode; }
            set
            {
                if (hangingMode == value) return;
                hangingMode = value;
                if (Graph == null) return;
                if (value) StartHanging(null, null);
                else { timer.Stop(); hanging = null; dragNode = null; Graph.Layout(36, 64, Options); }
                InvalidateVisual();
            }
        }

        /// <summary>Hangs the graph (laid out in layers just now) from its start; after an edit, the nodes start where they were in the graph before, held ones held again.</summary>
        void StartHanging(ConversationGraph before, HangingLayout was)
        {
            var g = Graph;
            if (before != null)
            {
                var old = new Dictionary<int, GraphNode>();
                foreach (var n in before.Visible) old[n.Address] = n;
                foreach (var n in g.Visible)
                {
                    GraphNode o;
                    if (old.TryGetValue(n.Address, out o)) { n.X = o.X; n.Y = o.Y; }
                }
                // the beads of long edges evenly between their ends
                foreach (var e in g.Edges)
                {
                    if (e.Bends.Count == 0) continue;
                    var upper = e.Back ? e.To : e.From;
                    var lower = e.Back ? e.From : e.To;
                    for (int k = 0; k < e.Bends.Count; k++)
                    {
                        double t = (k + 1.0) / (e.Bends.Count + 1);
                        var d = e.Bends[k];
                        d.Height = 0;
                        d.X = upper.CenterX + (lower.CenterX - upper.CenterX) * t - d.Width / 2;
                        d.Y = upper.Y + upper.Height + (lower.Y - upper.Y - upper.Height) * t;
                    }
                }
            }
            hanging = new HangingLayout(g, 36, 64);
            if (before != null && was != null)
                foreach (var p in was.Pinned.ToList())
                {
                    var n = g.Visible.FirstOrDefault(x => x.Address == p.Address);
                    if (n != null) hanging.Pin(n, true);
                }
            g.Route();
            timer.Start();
        }

        void Tick()
        {
            if (hanging == null || Graph == null) { timer.Stop(); return; }
            int n = Graph.Nodes.Count;
            hanging.Step(n < 150 ? 6 : n < 400 ? 3 : 2);
            InvalidateVisual();
            if (hanging.Settled && dragNode == null) timer.Stop();
        }

        /// <summary>Lets go of every held node but the start.</summary>
        public void LetGo()
        {
            if (hanging == null) return;
            hanging.Unpin();
            timer.Start();
            InvalidateVisual();
        }

        /// <summary>Moves a node of the hanging layout and holds it there (what dragging it does).</summary>
        public void MoveNode(GraphNode n, double x, double y)
        {
            if (hanging == null) return;
            hanging.MoveTo(n, x, y);
            timer.Start();
            InvalidateVisual();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            // a graph given before the control had a size is placed once it has one
            if (change.Property == BoundsProperty && needHome && Bounds.Width > 0) Home();
        }

        static IBrush B(string dark, string light) { return ColourScheme.Pick(dark, light); }

        /// <summary>Takes a graph: measures its nodes and lays it out. The view stays where it was when keepView.</summary>
        public void SetGraph(ConversationGraph g, bool keepView)
        {
            var before = Graph;
            var was = hanging;
            Graph = g;
            looks.Clear();
            labels.Clear();
            timer.Stop();
            hanging = null;
            dragNode = null;
            if (g == null) { InvalidateVisual(); return; }
            foreach (var n in g.Nodes.Where(x => !x.Dummy)) MeasureNode(n);
            g.Layout(36, 64, Options);
            if (hangingMode) StartHanging(keepView && was != null ? before : null, was);
            foreach (var e in g.Edges)
            {
                string t = e.Kind == GraphEdgeKind.Choice ? Short(e.Choice, 44) + "\n" + Short(e.Label, 44)
                    : e.Kind == GraphEdgeKind.Branch ? Short(e.Label, 48)
                    : e.Kind == GraphEdgeKind.Call ? "call" : null;
                if (t != null) labels[e] = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 10, EdgeBrush(e));
            }
            if (Selected != null) Selected = g.Visible.FirstOrDefault(n => n.Address == Selected.Address);
            if (!keepView) { needHome = true; Home(); }
            InvalidateVisual();
        }

        static string Short(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ");
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        void MeasureNode(GraphNode n)
        {
            var look = new NodeLook();
            double w = n.Kind == GraphNodeKind.Code ? CodeWidth : NodeWidth;
            string title = n.Title + (n.Kind == GraphNodeKind.Menu ? "  ·  menu" : "") + (n.Ends ? (n.Function == "main" ? "  ·  ends" : "  ·  returns") : "");
            look.Title = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 10, B("#8E8B85", "#6E6A62")) { MaxTextWidth = w - 12 };
            double h = 22;
            int statements = 0, hidden = 0;
            foreach (var l in n.Lines)
            {
                bool code = l.Kind == PseudoKind.Statement || (l.Kind == PseudoKind.Menu && l.Indent < 2);
                if (code)
                {
                    if (!ShowStatements) { hidden++; continue; }
                    if (++statements > 10) { hidden++; continue; }
                }
                string text = LineText(l);
                IBrush brush; Typeface face = Sans; double size = 12;
                switch (l.Kind)
                {
                    case PseudoKind.Say: brush = B("#E8E2D4", "#2B2A26"); text = Short(text, 320); break;
                    case PseudoKind.Respond: brush = B("#C9934C", "#8A5A1C"); text = "Player: " + Short(text, 200); break;
                    case PseudoKind.Menu:
                        if (l.Indent >= 2) { brush = B("#7FB6E0", "#2F6DA8"); text = Short(text, 140); }
                        else { brush = B("#A9B1A0", "#4A5544"); face = MonoFace; size = 10; text = Short(text, 70); }
                        break;
                    default: brush = B("#A9B1A0", "#4A5544"); face = MonoFace; size = 10; text = Short(text, 90); break;
                }
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush) { MaxTextWidth = w - 14 };
                look.Lines.Add(Tuple.Create(ft, h));
                h += ft.Height + 3;
            }
            if (hidden > 0)
            {
                var ft = new FormattedText("… " + hidden + " more statement" + (hidden == 1 ? "" : "s"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 10, B("#8E8B85", "#6E6A62"));
                look.Lines.Add(Tuple.Create(ft, h));
                h += ft.Height + 3;
            }
            n.Width = w;
            n.Height = Math.Max(30, h + 6);
            looks[n] = look;
        }

        static IBrush EdgeBrush(GraphEdge e)
        {
            switch (e.Kind)
            {
                case GraphEdgeKind.Choice: return B("#7FB6E0", "#2F6DA8");
                case GraphEdgeKind.Branch: return B("#D9A441", "#9A6A10");
                case GraphEdgeKind.Call: return B("#8E8B85", "#8E8B85");
                default: return B("#9A968E", "#6E6A62");
            }
        }

        /// <summary>The start of the conversation at the top, at full size (or smaller, for a narrow window).</summary>
        public void Home()
        {
            if (Graph == null || Bounds.Width <= 0) { scale = 1; offset = new Vector(20, 20); return; }
            needHome = false;
            var first = Graph.Visible.FirstOrDefault();
            scale = Math.Max(0.5, Math.Min(1, Bounds.Width / Math.Max(1, Graph.Width - Graph.Left)));
            offset = first == null ? new Vector(20, 20) : new Vector(Bounds.Width / 2 - first.CenterX * scale, 20 - first.Y * scale + 10);
            InvalidateVisual();
        }

        /// <summary>The whole graph in the view.</summary>
        public void Fit()
        {
            if (Graph == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            double w = Graph.Width - Graph.Left, h = Graph.Height - Graph.Top;
            scale = Math.Max(0.05, Math.Min(1.5, Math.Min(Bounds.Width / Math.Max(1, w), Bounds.Height / Math.Max(1, h))));
            offset = new Vector((Bounds.Width - w * scale) / 2 - Graph.Left * scale, (Bounds.Height - h * scale) / 2 - Graph.Top * scale);
            InvalidateVisual();
        }

        public void ZoomTo(double s, Point? about = null)
        {
            var p = about ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
            double ns = Math.Max(0.05, Math.Min(3, s));
            var world = (p - offset) / scale;
            scale = ns;
            offset = p - world * scale;
            InvalidateVisual();
        }

        /// <summary>Selects a node and brings it into view.</summary>
        public void Select(GraphNode n, bool center)
        {
            Selected = n;
            if (n != null && center) needHome = false;
            if (n != null && center && Bounds.Width > 0)
            {
                var r = new Rect(n.X * scale + offset.X, n.Y * scale + offset.Y, n.Width * scale, n.Height * scale);
                if (!new Rect(Bounds.Size).Contains(r))
                    offset = new Vector(Bounds.Width / 2 - n.CenterX * scale, Bounds.Height / 3 - n.Y * scale);
            }
            InvalidateVisual();
        }

        /// <summary>The node at a point of the control, or null.</summary>
        public GraphNode NodeAt(Point p)
        {
            if (Graph == null) return null;
            var w = (p - offset) / scale;
            foreach (var n in Graph.Visible)
                if (w.X >= n.X && w.X <= n.X + n.Width && w.Y >= n.Y && w.Y <= n.Y + n.Height) return n;
            return null;
        }

        /// <summary>Where a node's middle is on the control (for tests and clicks).</summary>
        public Point PointOf(GraphNode n) { return new Point(n.CenterX * scale + offset.X, (n.Y + Math.Min(12, n.Height / 2)) * scale + offset.Y); }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var p = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;
            if (hanging != null && props.IsRightButtonPressed)
            {
                // a right-click lets a held node go
                var held = NodeAt(p);
                if (held != null && hanging.IsPinned(held)) { hanging.Pin(held, false); timer.Start(); InvalidateVisual(); }
                e.Handled = true;
                return;
            }
            dragFrom = p;
            needHome = false;
            dragOffset = offset;
            dragged = false;
            // in the hanging layout a node is dragged; elsewhere the view is
            dragNode = hanging != null ? NodeAt(p) : null;
            if (dragNode != null) { var w = (p - offset) / scale; grab = new Vector(w.X - dragNode.X, w.Y - dragNode.Y); }
            e.Pointer.Capture(this);
            Focus();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (dragFrom == null) return;
            var d = e.GetPosition(this) - dragFrom.Value;
            if (!dragged && Math.Abs(d.X) + Math.Abs(d.Y) < 4) return;
            dragged = true;
            if (dragNode != null && hanging != null)
            {
                var w = (e.GetPosition(this) - offset) / scale;
                MoveNode(dragNode, w.X - grab.X, w.Y - grab.Y);
                return;
            }
            offset = dragOffset + d;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var p = e.GetPosition(this);
            bool click = dragFrom != null && !dragged;
            dragFrom = null;
            dragNode = null;
            e.Pointer.Capture(null);
            if (!click) return;
            var n = NodeAt(p);
            if (n == null) return;
            Select(n, false);
            NodeClicked?.Invoke(n);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            ZoomTo(scale * Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
            e.Handled = true;
        }

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(B("#15161A", "#F4F1EA"), new Rect(Bounds.Size));
            if (Graph == null) return;
            bool detail = scale >= 0.35;
            var view = new Rect(-offset.X / scale, -offset.Y / scale, Bounds.Width / scale, Bounds.Height / scale);
            using (dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
            {
                foreach (var e in Graph.Edges) DrawEdge(dc, e, detail, view);
                foreach (var n in Graph.Visible)
                {
                    var r = new Rect(n.X, n.Y, n.Width, n.Height);
                    if (!r.Intersects(view)) continue;
                    DrawNode(dc, n, r, detail);
                }
            }
        }

        void DrawNode(DrawingContext dc, GraphNode n, Rect r, bool detail)
        {
            IBrush fill, border;
            switch (n.Kind)
            {
                case GraphNodeKind.Say: fill = B("#26241F", "#FFFDF8"); border = B("#C9934C", "#9A6420"); break;
                case GraphNodeKind.Menu: fill = B("#1E242C", "#F3F7FB"); border = B("#6FA6DC", "#2F6DA8"); break;
                default: fill = B("#1C1E23", "#ECE7DC"); border = B("#3A3E46", "#C9C2B4"); break;
            }
            bool sel = n == Selected;
            dc.DrawRectangle(fill, new Pen(sel ? B("#F1D9A8", "#B85A00") : border, sel ? 3 : n.Entry ? 2 : 1), r, 6, 6);
            // a held node of the hanging layout: a pin in its top right corner
            if (hanging != null && hanging.IsPinned(n)) dc.DrawEllipse(border, null, new Point(r.Right - 9, r.Y + 9), 3.5, 3.5);
            NodeLook look;
            if (!looks.TryGetValue(n, out look)) return;
            dc.DrawText(look.Title, new Point(r.X + 7, r.Y + 4));
            if (!detail) return;
            foreach (var l in look.Lines) dc.DrawText(l.Item1, new Point(r.X + 7, r.Y + l.Item2));
        }

        void DrawEdge(DrawingContext dc, GraphEdge e, bool detail, Rect view)
        {
            if (e.Points.Count < 2) return;
            // calls of a much-called function only for the selected node
            if (e.Shared && e.From != Selected && e.To != Selected) return;
            var brush = EdgeBrush(e);
            var pen = new Pen(brush, e.Kind == GraphEdgeKind.Choice ? 1.6 : 1.1, e.Kind == GraphEdgeKind.Call ? DashStyle.Dash : null);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                var p0 = new Point(e.Points[0].X, e.Points[0].Y);
                c.BeginFigure(p0, false);
                for (int i = 1; i < e.Points.Count; i++)
                {
                    var p1 = new Point(e.Points[i].X, e.Points[i].Y);
                    if (e.Straight) c.LineTo(p1);
                    else
                    {
                        double dy = (p1.Y - p0.Y) / 2;
                        c.CubicBezierTo(new Point(p0.X, p0.Y + dy), new Point(p1.X, p1.Y - dy), p1);
                    }
                    p0 = p1;
                }
                c.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, g);
            // the arrowhead
            var end = e.Points[e.Points.Count - 1];
            var before = e.Points[e.Points.Count - 2];
            Vector dir = e.Straight ? new Vector(end.X - before.X, end.Y - before.Y) : new Vector(0, end.Y >= before.Y ? 1 : -1);
            double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (len > 0.01)
            {
                dir = dir / len;
                var tip = new Point(end.X, end.Y);
                var side = new Vector(-dir.Y, dir.X);
                var a = tip - dir * 8 + side * 4;
                var b = tip - dir * 8 - side * 4;
                var head = new StreamGeometry();
                using (var c = head.Open()) { c.BeginFigure(tip, true); c.LineTo(a); c.LineTo(b); c.EndFigure(true); }
                dc.DrawGeometry(brush, null, head);
            }
            FormattedText label;
            if (!detail || !labels.TryGetValue(e, out label)) return;
            var at = e.LabelAt;
            var box = new Rect(at.X - label.Width / 2 - 3, at.Y - label.Height / 2 - 1, label.Width + 6, label.Height + 2);
            if (!box.Intersects(view)) return;
            dc.DrawRectangle(B("#15161AE0", "#F4F1EAE8"), null, box, 3, 3);
            dc.DrawText(label, new Point(box.X + 3, box.Y + 1));
        }
    }

    /// <summary>
    /// Conversations ▸ Graph: the shown conversation's flow as a node graph (ConversationGraph),
    /// laid out in layers, or hanging from its start (HangingLayout), where nodes can be dragged
    /// and stay where they are put. A click on a node selects its first instruction in the script and shows
    /// its strings beside the graph, where editing one edits the conversation's string (the
    /// conversation editor's Strings, marked changed; Revert conversation undoes it).
    /// </summary>
    public sealed class ConversationGraphWindow : Window
    {
        readonly ConversationWindow cw;
        readonly GraphCanvas canvas = new GraphCanvas();
        readonly StackPanel side = new StackPanel { Spacing = 6 };
        readonly TextBlock status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock zoomText = new TextBlock { Width = 50, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        readonly CheckBox statements = new CheckBox { Content = "Statements", IsChecked = true };
        readonly ComboBox layout = new ComboBox { Name = "GraphLayout", ItemsSource = new[] { "In layers", "Hanging" }, SelectedIndex = 0, Width = 110 };
        readonly Button letGo = new Button { Content = "Let go", IsEnabled = false };
        Conversation conv;
        List<string> strings = new List<string>();
        int sideFor = -1;
        bool rebuildQueued;

        public GraphCanvas Canvas { get { return canvas; } }
        public ConversationGraph Graph { get { return canvas.Graph; } }
        /// <summary>The layout: 0 in layers, 1 hanging from the start.</summary>
        public int LayoutChoice { get { return layout.SelectedIndex; } set { layout.SelectedIndex = value; } }
        /// <summary>The string boxes beside the graph for the selected node.</summary>
        public IEnumerable<TextBox> StringBoxes { get { return side.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBox>()); } }

        public ConversationGraphWindow(ConversationWindow owner)
        {
            cw = owner;
            Title = "Conversation graph";
            Width = 1300; Height = 860; MinWidth = 700; MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            canvas.LineText = Text;
            canvas.NodeClicked += n => { ShowNode(n); cw.ShowAddress(n.Address, false); };
            canvas.PropertyChanged += (o, e) => { if (e.Property == BoundsProperty) Zoomed(); };

            var fit = new Button { Content = "Fit" };
            ToolTip.SetTip(fit, "Fit the whole conversation in the window.");
            fit.Click += (o, e) => { canvas.Fit(); Zoomed(); };
            var home = new Button { Content = "Start" };
            ToolTip.SetTip(home, "Go back to the start of the conversation at full size.");
            home.Click += (o, e) => { canvas.Home(); Zoomed(); };
            var zin = new Button { Content = "+" };
            zin.Click += (o, e) => { canvas.ZoomTo(canvas.Scale * 1.25); Zoomed(); };
            var zout = new Button { Content = "−" };
            zout.Click += (o, e) => { canvas.ZoomTo(canvas.Scale / 1.25); Zoomed(); };
            statements.IsCheckedChanged += (o, e) => { canvas.ShowStatements = statements.IsChecked == true; Rebuild(true); };
            ToolTip.SetTip(statements, "Show the code's other statements, such as assignments and calls, in the nodes.");
            canvas.PointerWheelChanged += (o, e) => Zoomed();
            ToolTip.SetTip(layout, "In layers: tidy rows with the fewest crossings. Hanging: every node hangs from the start on springs and can be dragged; a dragged node stays where you put it until you right-click it.");
            layout.SelectionChanged += (o, e) =>
            {
                canvas.HangingMode = layout.SelectedIndex == 1;
                letGo.IsEnabled = canvas.HangingMode;
                Hint2();
            };
            ToolTip.SetTip(letGo, "Let go of every node you dragged in the hanging layout, so that they all hang freely again.");
            letGo.Click += (o, e) => canvas.LetGo();

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var c in new Control[] { home, fit, zout, zin, zoomText, statements, layout, letGo }) bar.Children.Add(c);
            bar.Children.Add(Legend("#C9934C", "the NPC speaks"));
            bar.Children.Add(Legend("#6FA6DC", "a menu; blue lines are its answers"));
            bar.Children.Add(Legend("#D9A441", "a condition"));
            bar.Children.Add(Legend("#8E8B85", "a call (dashed)"));

            var top = new StackPanel { Spacing = 4 };
            top.Children.Add(help);
            top.Children.Add(bar);
            DockPanel.SetDock(top, Dock.Top);
            var foot = new Border { Child = status, Padding = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(foot, Dock.Bottom);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,340") };
            var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
            var sideView = new ScrollViewer { Content = side, Padding = new Thickness(10, 0, 12, 0) };
            Grid.SetColumn(split, 1); Grid.SetColumn(sideView, 2);
            grid.Children.Add(new Border { Child = canvas, BorderThickness = new Thickness(1), BorderBrush = ColourScheme.Pick("#2C3038", "#D5CFC2") });
            grid.Children.Add(split); grid.Children.Add(sideView);

            var dock = new DockPanel { Margin = new Thickness(14) };
            dock.Children.Add(top); dock.Children.Add(foot); dock.Children.Add(grid);
            Content = dock;
            Hint2();
            ManualWindow.Attach(this, "Conversation graph");
            cw.CurrentChanged += OnCurrentChanged;
            cw.StringsChanged += OnStringsChanged;
            cw.NamesChanged += OnNamesChanged;
            Closed += (o, e) => { cw.CurrentChanged -= OnCurrentChanged; cw.StringsChanged -= OnStringsChanged; cw.NamesChanged -= OnNamesChanged; };
            Rebuild(false);
        }

        readonly TextBlock help = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

        /// <summary>What dragging does, for the layout shown.</summary>
        void Hint2()
        {
            help.Text = canvas.HangingMode
                ? "The nodes hang from the start. A node you drag stays where you put it (a dot marks it) until you right-click it or press Let go. Drag the background to move the view; click a node to see it in the script."
                : "The conversation's flow, read from its code. Drag to move the view and use the wheel to zoom. Click a node to see it in the script and to edit its strings on the right. A loop runs up into the bottom of the node it goes back to.";
        }

        static Control Legend(string colour, string text)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            p.Children.Add(new Border { Width = 14, Height = 3, Background = new SolidColorBrush(Color.Parse(colour)), VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = text, FontSize = 11, Opacity = 0.8 });
            return p;
        }

        void Zoomed() { zoomText.Text = Math.Round(canvas.Scale * 100) + "%"; }

        void OnCurrentChanged() { sideFor = -1; Rebuild(false); }

        void OnNamesChanged() { sideFor = -1; Rebuild(true); }

        void OnStringsChanged()
        {
            // relaid out once the edit is done (a key press edits the string at once)
            if (rebuildQueued) return;
            rebuildQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { rebuildQueued = false; Rebuild(true); }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>The line's text with the conversation's current strings.</summary>
        string Text(GraphLine l)
        {
            if (l.StringId >= 0 && l.StringId < strings.Count)
            {
                string s = strings[l.StringId];
                if (l.Kind == PseudoKind.Menu && l.Indent >= 2)
                {
                    int colon = l.Text.IndexOf(':');
                    return (colon > 0 ? l.Text.Substring(0, colon) : "?") + ". " + s;
                }
                return s;
            }
            return l.Text;
        }

        /// <summary>Builds the graph of the shown conversation again (keepView: the same view and selection, after an edit).</summary>
        public void Rebuild(bool keepView)
        {
            conv = cw.Current;
            if (conv == null) { canvas.SetGraph(null, false); status.Text = "Choose a conversation in the conversation editor."; side.Children.Clear(); return; }
            string name = cw.Library.Name(conv.Slot);
            Title = "Conversation graph — " + (name ?? "conversation " + conv.Slot);
            strings = cw.CurrentStrings;
            ConversationGraph g;
            try
            {
                var d = Decompiler.Decompile(conv, strings.ToArray(), cw.Library.Kind, false, cw.Library.Names.Resolve(conv));
                g = ConversationGraph.Build(conv, d);
            }
            catch (Exception ex) { status.Text = "No graph: " + ex.Message; canvas.SetGraph(null, false); return; }
            int keep = canvas.Selected != null ? canvas.Selected.Address : -1;
            canvas.SetGraph(g, keepView);
            var sel = keep >= 0 ? g.Visible.FirstOrDefault(n => n.Address == keep) : null;
            canvas.Select(sel, false);
            if (sel == null) { side.Children.Clear(); sideFor = -1; Hint(); }
            else if (sel.Address != sideFor) ShowNode(sel);
            var v = g.Visible.ToList();
            status.Text = string.Format("{0}  ·  conversation {1}: {2} nodes ({3} with menus, {4} where the NPC speaks), {5} edges, {6} answers{7}.",
                name ?? "No name", conv.Slot, v.Count, v.Count(n => n.Kind == GraphNodeKind.Menu), v.Count(n => n.Kind == GraphNodeKind.Say), g.Edges.Count,
                g.Edges.Count(e => e.Kind == GraphEdgeKind.Choice), cw.ScriptPending ? "  ·  the script has unapplied edits, so this shows the applied code" : "");
            Zoomed();
        }

        void Hint()
        {
            side.Children.Add(new TextBlock { Text = "Click a node.", Opacity = 0.7, FontSize = 12 });
        }

        /// <summary>The selected node's details and its strings, editable, beside the graph.</summary>
        public void ShowNode(GraphNode n)
        {
            side.Children.Clear();
            sideFor = n == null ? -1 : n.Address;
            if (n == null) { Hint(); return; }
            canvas.Select(n, true);
            side.Children.Add(new TextBlock { Text = n.Title, FontSize = 15, FontWeight = FontWeight.SemiBold });
            side.Children.Add(new TextBlock
            {
                FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
                Text = string.Format("In {0}(), code {1}{2}.", n.Function, string.Join(", ", n.Ranges.OrderBy(r => r[0]).Select(r => r[0].ToString("X4") + "-" + (r[1] - 1).ToString("X4"))),
                    n.Ends ? (n.Function == "main" ? "; the conversation ends here" : "; the function returns here") : ""),
            });
            var go = new Button { Content = "Show in the script" };
            go.Click += (o, e) => cw.ShowAddress(n.Address, true);
            side.Children.Add(go);

            var ids = new List<int>();
            foreach (var l in n.Lines) if (l.StringId >= 0 && !ids.Contains(l.StringId)) ids.Add(l.StringId);
            foreach (int id in n.Strings) if (!ids.Contains(id)) ids.Add(id);
            if (ids.Count > 0) side.Children.Add(new TextBlock { Text = "Strings", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (int id in ids)
            {
                var row = cw.StringRowOf(id);
                if (row == null) continue;
                var line = n.Lines.FirstOrDefault(l => l.StringId == id);
                string what = line == null ? "used by the code" : line.Kind == PseudoKind.Say ? "the NPC says" : line.Kind == PseudoKind.Respond ? "the player says"
                    : line.Kind == PseudoKind.Menu ? "answer " + line.Text.Substring(0, Math.Max(0, line.Text.IndexOf(':'))) : "used by the code";
                var p = new StackPanel { Spacing = 2 };
                p.Children.Add(new TextBlock { Text = "String " + id + " · " + what, FontSize = 11, Opacity = 0.75 });
                var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, DataContext = row, Tag = id };
                box.Bind(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay });
                var err = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = ColourScheme.Pick("#E07060", "#B03A2A"), DataContext = row };
                err.Bind(TextBlock.TextProperty, new Binding("Error"));
                err.Bind(IsVisibleProperty, new Binding("HasError"));
                var mark = new TextBlock { Text = "Changed but not saved (Revert conversation undoes this).", FontSize = 10, Opacity = 0.7, DataContext = row };
                mark.Bind(IsVisibleProperty, new Binding("Changed"));
                p.Children.Add(box); p.Children.Add(err); p.Children.Add(mark);
                side.Children.Add(p);
            }
            var code = n.Lines.Where(l => l.Kind == PseudoKind.Statement || l.Kind == PseudoKind.Menu && l.Indent < 2).ToList();
            if (code.Count > 0)
            {
                side.Children.Add(new TextBlock { Text = "Code", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
                side.Children.Add(new SelectableTextBlock
                {
                    Text = string.Join("\n", code.Select(l => l.Address.ToString("X4") + "  " + l.Text)), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace"),
                });
            }
            var outs = n.Out.Where(e => !e.To.Dummy).ToList();
            if (outs.Count > 0) side.Children.Add(new TextBlock { Text = "Leads to", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var e in outs)
            {
                string t = (e.Kind == GraphEdgeKind.Choice ? e.Choice + "  [" + e.Label + "]" : e.Kind == GraphEdgeKind.Call ? "calls" : e.Label ?? "then") + "  →  " + e.To.Title;
                var b = new Button { Content = new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 11 }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                var to = e.To;
                b.Click += (o, ev) => { ShowNode(to); cw.ShowAddress(to.Address, false); };
                side.Children.Add(b);
            }
        }
    }
}
