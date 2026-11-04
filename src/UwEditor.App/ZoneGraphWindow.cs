using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>The lines between the zone labels: one arrowhead for a one-way link, two for a two-way one, dashed when unsure.</summary>
    public sealed class LinkLayer : Control
    {
        public ZoneGraphWindow Owner;

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // so empty space takes clicks
            Owner?.DrawLinks(dc);
        }
    }

    /// <summary>
    /// Tools ▸ Randomizer ▸ Zone graph (work in progress): a label for each zone, moved by dragging;
    /// links drawn between them, made by clicking two labels in a connect mode (one-way or two-way),
    /// and each link's list of requirements, filled from the doors between the zones when there are
    /// any (a locked door that cannot be picked needs its key).
    /// </summary>
    public sealed class ZoneGraphWindow : Window
    {
        const double CanvasW = 4800, CanvasH = 3600;
        readonly MainWindow main;
        readonly ZoningState z;
        readonly Canvas canvas = new Canvas { Width = CanvasW, Height = CanvasH };
        readonly LinkLayer layer = new LinkLayer { Width = CanvasW, Height = CanvasH };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly Dictionary<int, Border> labels = new Dictionary<int, Border>();
        readonly ToggleButton moveMode = new ToggleButton { Content = "Select and move", IsChecked = true };
        readonly ToggleButton oneWayMode = new ToggleButton { Content = "Connect one-way" };
        readonly ToggleButton twoWayMode = new ToggleButton { Content = "Connect two-way" };
        readonly CheckBox allText = new CheckBox { Content = "Requirements on every line" };
        readonly Slider zoom = new Slider { Minimum = 0.3, Maximum = 2, Value = 1, Width = 140 };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly StackPanel side = new StackPanel { Spacing = 6 };
        int selectedZone = -1, connectFrom = -1;
        ZoneLink selectedLink;
        int dragZone = -1;
        Point dragOffset;
        // panning with the middle button
        ScrollViewer scroll;
        bool panning;
        Point panStart;
        Vector panOrigin;
        // the force-directed layout
        readonly ToggleButton forceButton = new ToggleButton { Content = "Force layout" };
        readonly Slider attraction = new Slider { Minimum = 0.05, Maximum = 4, Value = 1, Width = 130 };
        readonly Slider repulsion = new Slider { Minimum = 0.05, Maximum = 4, Value = 1, Width = 130 };
        readonly TextBlock attractionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 34, FontSize = 12 };
        readonly TextBlock repulsionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 34, FontSize = 12 };
        readonly WrapPanel legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        readonly Avalonia.Threading.DispatcherTimer forceTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        readonly Dictionary<int, int> levelOf = new Dictionary<int, int>();
        int[] forceIds;
        double[] fx, fy;
        List<(int A, int B)> forceLinks;
        bool forceMoved;
        readonly ForceState forceState = new ForceState();

        public ZoningState Zoning { get { return z; } }
        public ZoneLink SelectedLink { get { return selectedLink; } }
        public int SelectedZone { get { return selectedZone; } }

        public ZoneGraphWindow(MainWindow main, ZoningState zoning)
        {
            this.main = main;
            z = zoning;
            Title = "Zone graph (randomizer, work in progress) — " + zoning.Session.Title;
            Width = 1280; Height = 820; MinWidth = 800; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            layer.Owner = this;
            canvas.Children.Add(layer);
            layer.PointerPressed += OnLayerPressed;
            canvas.PointerMoved += OnCanvasMoved;
            canvas.PointerReleased += (s, e) => { if (dragZone >= 0) { dragZone = -1; z.LinkChanged(); } };
            var zoomed = new LayoutTransformControl { LayoutTransform = scale, Child = canvas };
            var scroller = scroll = new ScrollViewer { Content = zoomed, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroller.Bind(ScrollViewer.BackgroundProperty, scroller.GetResourceObservable("Panel"));
            // the middle button pans the view, over the labels and the lines too (tunnelling, ahead of them)
            scroller.AddHandler(PointerPressedEvent, OnPanPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            scroller.AddHandler(PointerMovedEvent, OnPanMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            scroller.AddHandler(PointerReleasedEvent, OnPanReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            scroller.PointerCaptureLost += (s, e) => EndPan();
            // the wheel zooms about the mouse instead of scrolling
            scroller.AddHandler(PointerWheelChangedEvent, (s, e) =>
            {
                if (e.Delta.Y == 0) return;
                ZoomAt(zoom.Value * Math.Pow(1.15, e.Delta.Y), e.GetPosition(scroller));
                e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            zoom.ValueChanged += (s, e) => { scale.ScaleX = scale.ScaleY = zoom.Value; };
            foreach (var t in new[] { moveMode, oneWayMode, twoWayMode })
            {
                var me = t;
                me.Click += (s, e) => { foreach (var o in new[] { moveMode, oneWayMode, twoWayMode }) o.IsChecked = o == me; connectFrom = -1; Status(me == moveMode ? "Drag the labels; click a label or a line to edit it." : "Click the zone the path starts from, then the zone it leads to."); layer.InvalidateVisual(); };
            }
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            allText.IsCheckedChanged += (s, e) => layer.InvalidateVisual();
            foreach (var c in new Control[] { moveMode, oneWayMode, twoWayMode, Btn("Arrange by level", AutoLayout), allText, new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }, zoom,
                Btn("Check the logic…", Solve), Btn("Save the zoning", () => { try { z.Save(); Status("Saved " + z.Path + "."); } catch (Exception ex) { Status(ex.Message); } }) })
            {
                c.Margin = new Thickness(0, 0, 6, 0);
                bar.Children.Add(c);
            }
            attraction.ValueChanged += (s, e) => { attractionText.Text = attraction.Value.ToString("0.0#"); forceState.Reset(); };
            repulsion.ValueChanged += (s, e) => { repulsionText.Text = repulsion.Value.ToString("0.0#"); forceState.Reset(); };
            attractionText.Text = "1.0"; repulsionText.Text = "1.0";
            ToolTip.SetTip(forceButton, "Lays the labels out by forces, live: links pull the zones they join together, every two zones push apart, and a weak pull keeps everything near the middle. Drag a label meanwhile to hold it. Press again to stop; the places are kept in the zoning.");
            ToolTip.SetTip(attraction, "How hard a link pulls the two zones it joins together.");
            ToolTip.SetTip(repulsion, "How hard every two zones push each other apart.");
            forceButton.Click += (s, e) => { if (forceButton.IsChecked == true) StartForces(); else StopForces(); };
            forceTimer.Tick += (s, e) => ForceTick();
            var forceBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var c in new Control[] { forceButton, new TextBlock { Text = "Attraction", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }, attraction, attractionText,
                new TextBlock { Text = "Repulsion", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) }, repulsion, repulsionText })
            {
                c.Margin = new Thickness(c.Margin.Left, 0, 6, 0);
                forceBar.Children.Add(c);
            }
            var sideScroll = new ScrollViewer { Content = side, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Width = 360, Margin = new Thickness(12, 0, 0, 0) };
            var dock = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "Zone graph", FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            head.Children.Add(bar);
            head.Children.Add(forceBar);
            head.Children.Add(legend);
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(status, Dock.Bottom);
            status.Margin = new Thickness(0, 8, 0, 0);
            DockPanel.SetDock(sideScroll, Dock.Right);
            dock.Children.Add(head); dock.Children.Add(status); dock.Children.Add(sideScroll); dock.Children.Add(scroller);
            Content = dock;
            z.Changed += OnChanged;
            Closed += (s, e) => { z.Changed -= OnChanged; forceTimer.Stop(); };
            ManualWindow.Attach(this, "Randomizer");
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if (e.Key == Key.Delete && selectedLink != null && !(FocusManager?.GetFocusedElement() is TextBox)) { DeleteLink(); e.Handled = true; }
                else if (e.Key == Key.Escape) { connectFrom = -1; SelectZone(-1); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            BuildLabels();
            Status("Drag the labels; click a label or a line to edit it. Connect modes: click the start zone, then the end zone. The wheel zooms; drag with the middle button to pan.");
            ShowSide();
        }

        static Button Btn(string t, Action a) { var b = new Button { Content = t }; b.Click += (s, e) => a(); return b; }

        void Status(string s) { status.Text = s; }

        void OnChanged(string what)
        {
            if (what == "zones" || what == "file" || what == "tiles") BuildLabels();
            else if (what == "links") layer.InvalidateVisual();
            if (what == "file") { selectedLink = null; selectedZone = -1; ShowSide(); }
        }

        // ---- labels

        static IBrush TextOn(uint c)
        {
            double l = 0.299 * (c >> 16 & 0xFF) + 0.587 * (c >> 8 & 0xFF) + 0.114 * (c & 0xFF);
            return l > 140 ? Brushes.Black : Brushes.White;
        }

        /// <summary>Places zones without a position: a column per level they first appear on, in id order.</summary>
        public void AutoLayout() { Layout(true); }

        void Layout(bool all)
        {
            var byLevel = new SortedDictionary<int, List<Zone>>();
            foreach (var zone in z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                if (!all && zone.GraphX.HasValue) continue;
                var lv = z.Set.LevelsOf(zone.Id);
                int first = lv.Count > 0 ? lv[0] : 0;
                List<Zone> l;
                if (!byLevel.TryGetValue(first, out l)) byLevel[first] = l = new List<Zone>();
                l.Add(zone);
            }
            int col = 0;
            double maxY = 40;
            foreach (var kv in byLevel)
            {
                double y = 40;
                foreach (var zone in kv.Value)
                {
                    if (y > CanvasH - 80) { col++; y = 40; }
                    zone.GraphX = 30 + col * 260; zone.GraphY = y;
                    y += 44;
                    maxY = Math.Max(maxY, y);
                }
                col++;
            }
            if (byLevel.Count > 0) z.LinkChanged();
            BuildLabels();
        }

        void BuildLabels()
        {
            if (z.Set.Zones.Any(x => x.Id > ZoneSet.Unassigned && !x.GraphX.HasValue)) { Layout(false); return; }
            foreach (var b in labels.Values) canvas.Children.Remove(b);
            labels.Clear();
            levelOf.Clear();
            foreach (var zone in z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                uint c = z.Set.ColorOf(zone.Id);
                var lv = z.Set.LevelsOf(zone.Id);
                levelOf[zone.Id] = lv.Count > 0 ? lv[0] : 0;
                string levels = lv.Count == 0 ? "" : lv.Count == 1 ? "  L" + lv[0] : string.Format("  L{0}-L{1}", lv.First(), lv.Last());
                var tb = new TextBlock { Text = zone.Id + "  " + zone.Name + levels, Foreground = TextOn(c), FontSize = 12, FontWeight = zone.Id == z.Set.StartZone || zone.Id == z.Set.GoalZone ? FontWeight.Bold : FontWeight.Normal, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
                var b = new Border
                {
                    Child = tb, Background = new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c)),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3), BorderThickness = new Thickness(3),
                    BorderBrush = LevelBrush(levelOf[zone.Id]), Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = zone.Id,
                };
                ToolTip.SetTip(b, (zone.Id == z.Set.StartZone ? "Start. " : zone.Id == z.Set.GoalZone ? "Goal. " : "") + (string.IsNullOrEmpty(zone.Notes) ? zone.Name : zone.Notes));
                Canvas.SetLeft(b, zone.GraphX ?? 0);
                Canvas.SetTop(b, zone.GraphY ?? 0);
                b.PointerPressed += OnLabelPressed;
                canvas.Children.Add(b);
                labels[zone.Id] = b;
            }
            BuildLegend();
            layer.InvalidateVisual();
        }

        Rect LabelRect(int id)
        {
            Border b;
            if (!labels.TryGetValue(id, out b)) return new Rect();
            var size = b.Bounds.Width > 0 ? b.Bounds.Size : b.DesiredSize.Width > 0 ? b.DesiredSize : new Size(120, 24);
            return new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), size.Width, size.Height);
        }

        void OnLabelPressed(object sender, PointerPressedEventArgs e)
        {
            int id = (int)((Border)sender).Tag;
            e.Handled = true;
            if (oneWayMode.IsChecked == true || twoWayMode.IsChecked == true)
            {
                if (connectFrom < 0) { connectFrom = id; Status("From " + z.Label(id) + ": now click the zone it leads to."); layer.InvalidateVisual(); return; }
                try
                {
                    var k = z.Connect(connectFrom, id, twoWayMode.IsChecked == true);
                    selectedLink = k; selectedZone = -1;
                    Status(string.Format("Linked {0} {1} {2}: requires {3}.", z.Label(k.From), k.TwoWay ? "<->" : "->", z.Label(k.To), k.Requires));
                }
                catch (Exception ex) { Status(ex.Message); }
                connectFrom = -1;
                ShowSide();
                layer.InvalidateVisual();
                return;
            }
            SelectZone(id);
            dragZone = id;
            var p = e.GetPosition(canvas);
            dragOffset = new Point(p.X - Canvas.GetLeft((Border)sender), p.Y - Canvas.GetTop((Border)sender));
            e.Pointer.Capture(canvas);
        }

        void OnCanvasMoved(object sender, PointerEventArgs e)
        {
            if (dragZone < 0 || !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(canvas);
            double x = Math.Max(0, Math.Min(CanvasW - 60, p.X - dragOffset.X)), y = Math.Max(0, Math.Min(CanvasH - 24, p.Y - dragOffset.Y));
            z.MoveLabel(dragZone, x, y);
            if (forceIds != null)
            {
                int i = Array.IndexOf(forceIds, dragZone);
                if (i >= 0) { var r = LabelRect(dragZone); fx[i] = x + r.Width / 2; fy[i] = y + r.Height / 2; forceState.Reset(); }
            }
            Canvas.SetLeft(labels[dragZone], x);
            Canvas.SetTop(labels[dragZone], y);
            layer.InvalidateVisual();
        }

        // ---- panning with the middle button

        /// <summary>How far the view is scrolled (pixels of the zoomed canvas).</summary>
        public Vector ScrollOffset { get { return scroll.Offset; } set { scroll.Offset = ClampOffset(value); } }
        public bool Panning { get { return panning; } }

        Vector ClampOffset(Vector v)
        {
            double mx = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), my = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            return new Vector(Math.Max(0, Math.Min(mx, v.X)), Math.Max(0, Math.Min(my, v.Y)));
        }

        /// <summary>
        /// Zooms to a scale (kept within the Zoom slider's range) so that the point of the canvas
        /// under at (a point of the view) stays under it.
        /// </summary>
        public void ZoomAt(double value, Point at)
        {
            double old = zoom.Value, now = Math.Max(zoom.Minimum, Math.Min(zoom.Maximum, value));
            if (Math.Abs(now - old) < 1e-9) return;
            var c = new Point((scroll.Offset.X + at.X) / old, (scroll.Offset.Y + at.Y) / old);
            zoom.Value = now;                 // sets the scale
            scroll.UpdateLayout();            // the new extent, before scrolling to keep the point in place
            scroll.Offset = ClampOffset(new Vector(c.X * now - at.X, c.Y * now - at.Y));
        }

        void OnPanPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(scroll).Properties.IsMiddleButtonPressed) return;
            panning = true;
            panStart = e.GetPosition(scroll);
            panOrigin = scroll.Offset;
            scroll.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(scroll);
            e.Handled = true;
        }

        void OnPanMoved(object sender, PointerEventArgs e)
        {
            if (!panning) return;
            var p = e.GetPosition(scroll);
            scroll.Offset = ClampOffset(new Vector(panOrigin.X - (p.X - panStart.X), panOrigin.Y - (p.Y - panStart.Y)));
            e.Handled = true;
        }

        void OnPanReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!panning || e.InitialPressMouseButton != MouseButton.Middle) return;
            e.Handled = true;
            e.Pointer.Capture(null);
            EndPan();
        }

        void EndPan()
        {
            if (!panning) return;
            panning = false;
            scroll.Cursor = null;
        }

        /// <summary>Moves a label (for tests and scripts).</summary>
        public void MoveLabel(int id, double x, double y)
        {
            z.MoveLabel(id, x, y);
            if (labels.ContainsKey(id)) { Canvas.SetLeft(labels[id], x); Canvas.SetTop(labels[id], y); }
            layer.InvalidateVisual();
        }

        public void SelectZone(int id)
        {
            selectedZone = id;
            if (id >= 0) { selectedLink = null; z.CurrentZone = id; }
            ShowSide();
            layer.InvalidateVisual();
        }

        // ---- the level outlines and the force-directed layout

        static IBrush LevelBrush(int level)
        {
            uint c = ForceLayout.LevelColor(level);
            return new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
        }

        /// <summary>The level each zone's outline shows (its first level; 0 for none).</summary>
        public int LevelOf(int zone) { int l; return levelOf.TryGetValue(zone, out l) ? l : 0; }

        void BuildLegend()
        {
            legend.Children.Clear();
            legend.Children.Add(new TextBlock { Text = "Outline: the zone's (first) level", FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (int l in levelOf.Values.Where(v => v > 0).Distinct().OrderBy(v => v))
            {
                var chip = new Border { BorderBrush = LevelBrush(l), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 6, 2), Child = new TextBlock { Text = "L" + l, FontSize = 11 } };
                legend.Children.Add(chip);
            }
            legend.Children.Add(new TextBlock { Text = "Inside: the zone's colour in the zoning", FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        }

        /// <summary>The force layout's settings from the sliders.</summary>
        public ForceOptions Forces { get { return new ForceOptions { Attraction = attraction.Value, Repulsion = repulsion.Value }; } }
        public double Attraction { get { return attraction.Value; } set { attraction.Value = value; } }
        public double Repulsion { get { return repulsion.Value; } set { repulsion.Value = value; } }
        public bool ForcesRunning { get { return forceTimer.IsEnabled; } }
        public double Zoom { get { return zoom.Value; } set { zoom.Value = value; } }

        public void StartForces()
        {
            forceIds = labels.Keys.OrderBy(k => k).ToArray();
            var index = new Dictionary<int, int>();
            for (int i = 0; i < forceIds.Length; i++) index[forceIds[i]] = i;
            fx = new double[forceIds.Length]; fy = new double[forceIds.Length];
            for (int i = 0; i < forceIds.Length; i++) { var r = LabelRect(forceIds[i]); fx[i] = r.Center.X; fy[i] = r.Center.Y; }
            var seen = new HashSet<(int, int)>();
            forceLinks = new List<(int A, int B)>();
            foreach (var k in z.Set.Links)
            {
                int a, b;
                if (!index.TryGetValue(k.From, out a) || !index.TryGetValue(k.To, out b) || a == b) continue;
                if (seen.Add((Math.Min(a, b), Math.Max(a, b)))) forceLinks.Add((a, b));
            }
            forceMoved = false;
            forceState.Reset();
            forceButton.IsChecked = true;
            forceTimer.Start();
            Status("Force layout running: links pull, zones push apart. Drag a label to hold it; press Force layout again to stop.");
        }

        public void StopForces()
        {
            forceTimer.Stop();
            forceButton.IsChecked = false;
            forceIds = null;
            if (forceMoved) z.LinkChanged();
            Status("Force layout stopped; the places are kept in the zoning (save it to keep them).");
        }

        /// <summary>Steps of the layout (two a tick), the labels moved to match. Returns the largest move.</summary>
        public double ForceTick(int steps = 2)
        {
            if (forceIds == null) return 0;
            var pinned = new bool[forceIds.Length];
            if (dragZone >= 0) { int i = Array.IndexOf(forceIds, dragZone); if (i >= 0) pinned[i] = true; }
            var o = Forces;
            double most = 0;
            for (int s = 0; s < steps; s++) most = Math.Max(most, ForceLayout.Step(fx, fy, forceLinks, pinned, o, CanvasW * 4, CanvasH * 4, forceState));
            // keep the drawing in the top left of the canvas, where it shows without scrolling (unless a label is held)
            if (!pinned.Any(p => p))
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                for (int i = 0; i < forceIds.Length; i++)
                {
                    var r = LabelRect(forceIds[i]);
                    minX = Math.Min(minX, fx[i] - r.Width / 2); minY = Math.Min(minY, fy[i] - r.Height / 2);
                }
                double sx = 30 - minX, sy = 30 - minY;
                for (int i = 0; i < forceIds.Length; i++) { fx[i] += sx; fy[i] += sy; }
            }
            for (int i = 0; i < forceIds.Length; i++)
            {
                if (pinned[i] || !labels.ContainsKey(forceIds[i])) continue;
                var r = LabelRect(forceIds[i]);
                double x = Math.Max(0, Math.Min(CanvasW - r.Width, fx[i] - r.Width / 2)), y = Math.Max(0, Math.Min(CanvasH - r.Height, fy[i] - r.Height / 2));
                z.MoveLabel(forceIds[i], x, y);
                Canvas.SetLeft(labels[forceIds[i]], x); Canvas.SetTop(labels[forceIds[i]], y);
            }
            if (most > 0.01) forceMoved = true;
            layer.InvalidateVisual();
            return most;
        }

        // ---- links

        (Point a, Point b) Ends(ZoneLink k)
        {
            var ra = LabelRect(k.From); var rb = LabelRect(k.To);
            var ca = ra.Center; var cb = rb.Center;
            // links both ways between the same pair sit side by side
            bool twin = z.Set.Links.Any(o => o != k && ((o.From == k.To && o.To == k.From) || (o.From == k.From && o.To == k.To)));
            if (twin)
            {
                var d = cb - ca; double len = Math.Max(1, Math.Sqrt(d.X * d.X + d.Y * d.Y));
                int order = z.Set.Links.Where(o => (o.From == k.From && o.To == k.To) || (o.From == k.To && o.To == k.From)).ToList().IndexOf(k);
                double off = (order % 2 == 0 ? 1 : -1) * (5 + 6 * (order / 2));
                var n = new Vector(-d.Y / len * off, d.X / len * off);
                ca += n; cb += n;
            }
            return (Clip(ra, ca, cb), Clip(rb, cb, ca));
        }

        /// <summary>Where the line from inside a rectangle towards a point leaves it.</summary>
        static Point Clip(Rect r, Point from, Point to)
        {
            var d = to - from;
            double t = 1;
            if (Math.Abs(d.X) > 1e-6) t = Math.Min(t, Math.Abs((r.Width / 2 + 3) / d.X));
            if (Math.Abs(d.Y) > 1e-6) t = Math.Min(t, Math.Abs((r.Height / 2 + 3) / d.Y));
            return from + d * Math.Min(1, t);
        }

        static readonly Dictionary<string, Color> KindColors = new Dictionary<string, Color>
        {
            { "walk", Color.Parse("#9AA0A6") }, { "door", Color.Parse("#D9A441") }, { "portcullis", Color.Parse("#C77D3A") }, { "secret", Color.Parse("#8F7BD6") },
            { "doorman", Color.Parse("#4FB3A9") }, { "drop", Color.Parse("#5E9BD8") }, { "climb", Color.Parse("#7CC36A") }, { "teleport", Color.Parse("#D66BC7") },
            { "lever", Color.Parse("#E07060") }, { "stairs", Color.Parse("#B0B0B0") }, { "other", Color.Parse("#888888") },
        };

        public void DrawLinks(DrawingContext dc)
        {
            var font = new Typeface(FontFamily.Default);
            foreach (var k in z.Set.Links)
            {
                if (!labels.ContainsKey(k.From) || !labels.ContainsKey(k.To)) continue;
                var (a, b) = Ends(k);
                Color col;
                if (!KindColors.TryGetValue(k.Kind ?? "", out col)) col = Colors.Gray;
                bool sel = k == selectedLink;
                var pen = new Pen(new SolidColorBrush(sel ? Colors.White : col), sel ? 3 : 1.6, k.Confidence == "todo" ? new DashStyle(new double[] { 4, 3 }, 0) : null);
                dc.DrawLine(pen, a, b);
                Arrow(dc, a, b, pen.Brush);
                if (k.TwoWay) Arrow(dc, b, a, pen.Brush);
                bool related = sel || k.From == selectedZone || k.To == selectedZone;
                if (k.Requires != "free" && !string.IsNullOrEmpty(k.Requires) && (related || allText.IsChecked == true))
                {
                    string t = k.Requires.Length > 30 ? k.Requires.Substring(0, 29) + "…" : k.Requires;
                    var ft = new FormattedText(t, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10, Brushes.White);
                    var mid = new Point((a.X + b.X) / 2 - ft.Width / 2, (a.Y + b.Y) / 2 - ft.Height / 2);
                    dc.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 20, 20, 24)), new Rect(mid.X - 3, mid.Y - 1, ft.Width + 6, ft.Height + 2), 3);
                    dc.DrawText(ft, mid);
                }
            }
            if (selectedZone >= 0 && labels.ContainsKey(selectedZone))
                dc.DrawRectangle(null, new Pen(Brushes.White, 2), LabelRect(selectedZone).Inflate(4), 8, 8);
            if (connectFrom >= 0 && labels.ContainsKey(connectFrom))
                dc.DrawRectangle(null, new Pen(Brushes.White, 2, new DashStyle(new double[] { 3, 2 }, 0)), LabelRect(connectFrom).Inflate(4), 6, 6);
        }

        static void Arrow(DrawingContext dc, Point from, Point tip, IBrush brush)
        {
            var d = tip - from; double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len < 1) return;
            var u = d / len; var n = new Vector(-u.Y, u.X);
            var basePt = tip - u * 10;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(tip, true);
                g.LineTo(basePt + n * 5);
                g.LineTo(basePt - n * 5);
                g.EndFigure(true);
            }
            dc.DrawGeometry(brush, null, geo);
        }

        void OnLayerPressed(object sender, PointerPressedEventArgs e)
        {
            var p = e.GetPosition(canvas);
            ZoneLink best = null;
            double bestD = 7;
            foreach (var k in z.Set.Links)
            {
                if (!labels.ContainsKey(k.From) || !labels.ContainsKey(k.To)) continue;
                var (a, b) = Ends(k);
                double d = Dist(p, a, b);
                if (d < bestD) { bestD = d; best = k; }
            }
            SelectLink(best);
            if (best == null) { selectedZone = -1; connectFrom = -1; SelectZone(-1); }
        }

        static double Dist(Point p, Point a, Point b)
        {
            var ab = b - a; double l2 = ab.X * ab.X + ab.Y * ab.Y;
            double t = l2 < 1e-6 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / l2));
            var q = a + ab * t;
            return Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
        }

        public void SelectLink(ZoneLink k)
        {
            selectedLink = k;
            if (k != null) selectedZone = -1;
            ShowSide();
            layer.InvalidateVisual();
        }

        void DeleteLink()
        {
            if (selectedLink == null) return;
            var k = selectedLink;
            selectedLink = null;
            z.RemoveLink(k);
            Status("Link removed.");
            ShowSide();
        }

        // ---- the side panel

        void ShowSide()
        {
            side.Children.Clear();
            if (selectedLink != null) { LinkPanel(selectedLink); return; }
            if (selectedZone >= 0) { ZonePanel(selectedZone); return; }
            side.Children.Add(new TextBlock { Classes = { "h", "first" }, Text = "ZONE GRAPH" });
            side.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Text = string.Format("{0} zones, {1} links, {2} grants. Lines: one arrowhead one-way, two arrowheads two-way, dashed still to check; the colour is the kind (door, portcullis, secret, drop, lever…), and the text what the path needs.", z.Set.Zones.Count(x => x.Id > 1), z.Set.Links.Count, z.Set.Placements.Count),
            });
        }

        void ZonePanel(int id)
        {
            var zone = z.Set.GetZone(id);
            side.Children.Add(new TextBlock { Classes = { "h", "first" }, Text = "ZONE " + id });
            side.Children.Add(new TextBlock { Text = zone?.Name ?? "", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            var lv = z.Set.LevelsOf(id);
            int items = 0;
            try { items = z.Items.Count(i => i.Zone == id); } catch (Exception) { }
            side.Children.Add(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Text = string.Format("{0} tiles on level{1} {2}; {3} key items; {4} links.", z.Set.TileCount(id), lv.Count == 1 ? "" : "s", string.Join(", ", lv), items, z.Set.Links.Count(k => k.From == id || k.To == id)) });
            if (!string.IsNullOrEmpty(zone?.Notes)) side.Children.Add(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Text = zone.Notes });
            var row = new WrapPanel();
            row.Children.Add(Btn("Start here", () => { z.Set.StartZone = id; z.LinkChanged(); BuildLabels(); ShowSide(); }));
            row.Children.Add(Btn("Goal here", () => { z.Set.GoalZone = id; z.LinkChanged(); BuildLabels(); ShowSide(); }));
            row.Children.Add(Btn("Zones window…", () => main.ShowZones()));
            foreach (Control c in row.Children) c.Margin = new Thickness(0, 4, 6, 0);
            side.Children.Add(row);
            side.Children.Add(new TextBlock { Classes = { "h" }, Text = "LINKS" });
            foreach (var k in z.Set.Links.Where(k => k.From == id || k.To == id))
            {
                var b = new Button { Content = string.Format("{0} {1} {2}: {3}", z.Label(k.From), k.TwoWay ? "↔" : "→", z.Label(k.To), k.Requires), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 11 };
                var kk = k;
                b.Click += (s, e) => SelectLink(kk);
                side.Children.Add(b);
            }
        }

        void LinkPanel(ZoneLink k)
        {
            side.Children.Add(new TextBlock { Classes = { "h", "first" }, Text = "PATH" });
            var zones = z.Set.Zones.Where(x => x.Id > 1).Select(x => x.Id + "  " + x.Name).ToList();
            var ids = z.Set.Zones.Where(x => x.Id > 1).Select(x => x.Id).ToList();
            var from = new ComboBox { ItemsSource = zones, SelectedIndex = ids.IndexOf(k.From), HorizontalAlignment = HorizontalAlignment.Stretch };
            var to = new ComboBox { ItemsSource = zones, SelectedIndex = ids.IndexOf(k.To), HorizontalAlignment = HorizontalAlignment.Stretch };
            from.SelectionChanged += (s, e) => { if (from.SelectedIndex >= 0 && ids[from.SelectedIndex] != k.From) { k.From = ids[from.SelectedIndex]; z.LinkChanged(); } };
            to.SelectionChanged += (s, e) => { if (to.SelectedIndex >= 0 && ids[to.SelectedIndex] != k.To) { k.To = ids[to.SelectedIndex]; z.LinkChanged(); } };
            side.Children.Add(new TextBlock { Text = "From", FontSize = 12 });
            side.Children.Add(from);
            side.Children.Add(new TextBlock { Text = "To", FontSize = 12 });
            side.Children.Add(to);
            var two = new CheckBox { Content = "Two-way (also from the second zone back)", IsChecked = k.TwoWay };
            two.IsCheckedChanged += (s, e) => { k.TwoWay = two.IsChecked == true; z.LinkChanged(); };
            side.Children.Add(two);
            var swap = Btn("Swap the direction", () => { int t = k.From; k.From = k.To; k.To = t; z.LinkChanged(); ShowSide(); });
            side.Children.Add(swap);
            var kind = new ComboBox { ItemsSource = ZoneLink.Kinds, SelectedItem = ZoneLink.Kinds.Contains(k.Kind) ? k.Kind : "other", HorizontalAlignment = HorizontalAlignment.Stretch };
            kind.SelectionChanged += (s, e) => { k.Kind = kind.SelectedItem as string ?? "walk"; z.LinkChanged(); };
            side.Children.Add(new TextBlock { Text = "Kind", FontSize = 12 });
            side.Children.Add(kind);
            var conf = new ComboBox { ItemsSource = new[] { "certain", "todo" }, SelectedItem = k.Confidence == "todo" ? "todo" : "certain", HorizontalAlignment = HorizontalAlignment.Stretch };
            conf.SelectionChanged += (s, e) => { k.Confidence = conf.SelectedItem as string ?? "certain"; z.LinkChanged(); };
            side.Children.Add(new TextBlock { Text = "Sure?", FontSize = 12 });
            side.Children.Add(conf);

            side.Children.Add(new TextBlock { Classes = { "h" }, Text = "REQUIREMENTS (ALL OF THEM)" });
            var terms = new ObservableCollection<string>(k.RequirementList);
            var list = new ListBox { ItemsSource = terms, MinHeight = 60, MaxHeight = 180 };
            list.Bind(ListBox.BackgroundProperty, list.GetResourceObservable("PanelAlt"));
            var check = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
            Action apply = () =>
            {
                k.RequirementList = terms.ToList();
                string err;
                Requirement.TryParse(k.Requires, out err);
                check.Text = err == null ? (terms.Count == 0 ? "Free: nothing needed." : "As an expression: " + k.Requires) : "Not understood: " + err;
                check.Bind(TextBlock.ForegroundProperty, check.GetResourceObservable(err == null ? "Muted" : "Error"));
                z.LinkChanged();
            };
            var term = new AutoCompleteBox { Watermark = "key:5, pick:3, bash, item:orb, spell:levitate, zone:12, talismans:8 … (| for or)", ItemsSource = Suggestions(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 1 };
            var add = Btn("Add", () => { var t = (term.Text ?? "").Trim(); if (t.Length > 0) { terms.Add(t); term.Text = ""; apply(); } });
            term.KeyDown += (s, e) => { if (e.Key == Key.Enter) { var t = (term.Text ?? "").Trim(); if (t.Length > 0) { terms.Add(t); term.Text = ""; apply(); } e.Handled = true; } };
            var remove = Btn("Remove", () => { if (list.SelectedItem is string t) { terms.Remove(t); apply(); } });
            var suggest = Btn("From the doors", () =>
            {
                List<DoorPassage> used;
                string r = ZoneDoors.Suggest(z.Doors, k.From, k.To, out used);
                if (r == null) { Status("No door joins these two zones (walk, a drop, a lever or a conversation, maybe)."); return; }
                terms.Clear();
                foreach (var t in ZoneLink.SplitAnd(r)) terms.Add(t);
                k.Notes = string.Join("; ", used.Select(u => string.Format("L{0} ({1}, {2}) {3}: {4}", u.Level, u.X, u.Y, u.Kind, u.Notes)));
                apply();
                ShowSide();
                Status(used.Count + " door(s) between the zones.");
            });
            side.Children.Add(list);
            var addRow = new DockPanel();
            DockPanel.SetDock(add, Dock.Right);
            add.Margin = new Thickness(6, 0, 0, 0);
            addRow.Children.Add(add);
            addRow.Children.Add(term);
            side.Children.Add(addRow);
            var r2 = new WrapPanel();
            r2.Children.Add(remove); r2.Children.Add(suggest);
            foreach (Control c in r2.Children) c.Margin = new Thickness(0, 0, 6, 0);
            side.Children.Add(r2);
            side.Children.Add(check);
            side.Children.Add(new TextBlock
            {
                FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap,
                Text = "Each line must hold; a line with | holds when any of its parts does. key:N a key with owner N; pick:Z picking a lock of difficulty Z; bash breaking the door; item:, flag:, npc: something a zone grants; spell:NAME; zone:N reaching zone N (a lever there); talismans:N.",
            });
            side.Children.Add(new TextBlock { Classes = { "h" }, Text = "NOTES" });
            var notes = new TextBox { Text = k.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
            notes.LostFocus += (s, e) => { if (notes.Text != k.Notes) { k.Notes = notes.Text ?? ""; z.LinkChanged(); } };
            side.Children.Add(notes);
            if (k.Level > 0)
                side.Children.Add(Btn(string.Format("Go to level {0} ({1}, {2})", k.Level, k.X, k.Y), () => main.GoToTile(k.Level, k.X, k.Y)));
            side.Children.Add(Btn("Delete the link (Del)", DeleteLink));
            apply();
        }

        /// <summary>Terms for the requirement box: what the zones grant, the doors' keys, the spells.</summary>
        List<string> Suggestions()
        {
            var s = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "bash", "free", "spell:levitate", "spell:waterwalk", "spell:jump", "spell:reveal", "spell:gate_travel", "talismans:8" };
            foreach (var p in z.Set.Placements) if (!string.IsNullOrEmpty(p.Grants)) s.Add(Requirement.Normalize(p.Grants));
            foreach (var k in z.Set.Links) foreach (var t in ZoneLink.SplitAnd(k.Requires)) foreach (var part in t.Split('|')) s.Add(part.Trim());
            try { foreach (var i in z.Items) s.Add(i.Token); } catch (Exception) { }
            foreach (var zone in z.Set.Zones.Where(x => x.Id > 1)) s.Add("zone:" + zone.Id);
            return s.ToList();
        }

        /// <summary>The last logic check's solve over time, or null.</summary>
        public TimelineResult LastCheck { get; private set; }

        /// <summary>Whether the zoning shown was made by the automatic zones (rather than painted by hand).</summary>
        public bool IsAutomaticZoning { get { return (z.Set.Source ?? "").StartsWith("automatic zones", StringComparison.OrdinalIgnoreCase); } }

        /// <summary>
        /// The logic check: the same solve over time as Automatic zones ▸ Report (Phase C), on the
        /// levels with their unsaved edits, so that mechanisms set off, create traps and counts are
        /// followed. The options and the zoning compared with are the Automatic zones window's when
        /// it is open. A zoning painted by hand is that comparison, and its own drawn links are
        /// checked after it without world changes.
        /// </summary>
        public string CheckLogic()
        {
            var s = z.Session;
            var auto = main.OpenAutoZones;
            NavOptions nav;
            if (auto != null) nav = auto.MovementOptions.Clone();
            else { nav = new NavOptions { MaxClimb = main.ZoneClimb }; nav.JumpSpellClimb = 2 * nav.MaxClimb; }
            var abil = auto != null ? auto.LogicOptions.Clone() : new AbilityOptions();
            bool hand = !IsAutomaticZoning;
            var painted = hand ? z.Set : auto != null ? auto.ChosenComparison : AutoZonesWindow.DefaultPainted(s);
            var edits = main.ViewModel.Edits;
            Func<int, LevelModel> level = l => edits != null ? edits.Model(l) : s.LoadLevel(l);
            LastCheck = Timeline.Build(s, level, nav, abil, painted);
            var report = new System.Text.StringBuilder();
            report.AppendLine(hand
                ? "The solve over time, as Automatic zones ▸ Report: the zones are built from the levels again; the zoning drawn here gives the goal and the names."
                : "The solve over time, as Automatic zones ▸ Report: the zones are built from the levels again with " + (auto != null ? "that window's options" : "the default options") + ", so link edits made here are not part of it.");
            report.AppendLine();
            report.Append(Timeline.Report(LastCheck));
            if (hand)
            {
                var drawn = new ZoneSolver(z.Set, new SolverOptions()).Solve(z.Set.StartZone, z.Set.GoalZone);
                report.AppendLine();
                report.AppendLine();
                report.AppendLine("THE LINKS DRAWN HERE, WITHOUT WORLD CHANGES");
                report.AppendLine("This check follows only the links and placements of this zoning: it does not set off mechanisms, create traps or counts, so it may stop where the solve over time goes on.");
                report.AppendLine();
                report.Append(drawn.Report(z.Set));
            }
            return report.ToString();
        }

        async void Solve()
        {
            Status("Checking the logic (the solve over time)…");
            await System.Threading.Tasks.Task.Delay(30);   // let the status show before the solve holds the window
            string text;
            try { text = CheckLogic(); }
            catch (Exception ex) { Status("The solver stopped: " + ex.Message); return; }
            var sv = LastCheck?.Solve;
            Status(sv == null ? "The solve over time failed." : sv.GoalReached ? "Logic check: goal reached." : "Logic check: goal NOT reached.");
            await MessageWindow.ShowReport(this, "The logic from the start", text);
        }
    }
}
