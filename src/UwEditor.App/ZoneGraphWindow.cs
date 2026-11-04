using System;
using System.Collections.Generic;
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
    /// The Zones window's Zone graph… (UW1 only): a view of the zoning, kept up to date as
    /// it is edited in the Zones window. A label for each zone (dragged, or laid out by forces;
    /// the places are kept in the zoning), the links between them, the start (a thick outline)
    /// and the goal (thick dashed). Check the logic runs the solve over time on the zoning and
    /// shows what it reaches; Reset graph appearance takes that away. Links are made and edited
    /// in the Zones window.
    /// </summary>
    public sealed class ZoneGraphWindow : Window
    {
        const double CanvasW = 4800, CanvasH = 3600, MinZoom = 0.3, MaxZoom = 2;
        readonly MainWindow main;
        readonly ZoningState z;
        readonly Canvas canvas = new Canvas { Width = CanvasW, Height = CanvasH };
        readonly LinkLayer layer = new LinkLayer { Width = CanvasW, Height = CanvasH };
        readonly ScaleTransform scale = new ScaleTransform(1, 1);
        readonly Dictionary<int, Border> labels = new Dictionary<int, Border>();
        readonly Dictionary<int, string> labelText = new Dictionary<int, string>();
        readonly CheckBox allText = new CheckBox { Content = "Requirements on every line" };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly StackPanel side = new StackPanel { Spacing = 6 };
        double zoom = 1;
        int selectedZone = -1;
        ZoneLink selectedLink;
        int dragZone = -1;
        Point dragOffset;
        // the start and the goal, as the game has them
        int startZone = -1;
        HashSet<int> goalZones = new HashSet<int>();
        // what the last Check the logic reached: zone -> sphere (null: not shown)
        Dictionary<int, int> reached;
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
            Title = "Zone graph (randomizer, UW1 only) — " + zoning.Session.Title;
            Width = 1280; Height = 820; MinWidth = 800; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            layer.Owner = this;
            canvas.Children.Add(layer);
            layer.PointerPressed += OnLayerPressed;
            canvas.PointerMoved += OnCanvasMoved;
            canvas.PointerReleased += (s, e) => { if (dragZone >= 0) { dragZone = -1; z.PositionsChanged(true); } };
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
                ZoomAt(zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(scroller));
                e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            allText.IsCheckedChanged += (s, e) => layer.InvalidateVisual();
            var fit = Btn("Fit", Fit);
            ToolTip.SetTip(fit, "Zooms and scrolls so that every label shows. The wheel zooms too, about the mouse; the middle button pans.");
            var check = Btn("Check the logic…", Solve);
            ToolTip.SetTip(check, "The solve over time on this zoning, as Apply uses it. Zones it reaches keep their colour and show their step; the others fade, and links that stay closed turn red.");
            var reset = Btn("Reset graph appearance", ResetAppearance);
            ToolTip.SetTip(reset, "Takes away what Check the logic showed.");
            foreach (var c in new Control[] { Btn("Arrange by level", AutoLayout), allText, fit, check, reset, Btn("Zones window…", () => main.ShowZones()) })
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
            var sideScroll = new ScrollViewer { Content = side, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Width = 340, Margin = new Thickness(12, 0, 0, 0) };
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
                if (e.Key == Key.Escape) { SelectZone(-1); e.Handled = true; }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            BuildLabels();
            Status("A view of the Zones window's zoning: links are made and edited there. Drag the labels; click a label or a line to see it. The wheel zooms; drag with the middle button to pan.");
            ShowSide();
        }

        static Button Btn(string t, Action a) { var b = new Button { Content = t }; b.Click += (s, e) => a(); return b; }

        void Status(string s) { status.Text = s; }

        void OnChanged(string what)
        {
            if (what == "zones" || what == "file" || what == "tiles" || what == "split") BuildLabels();
            else
            {
                RefreshStartGoal();
                layer.InvalidateVisual();
            }
            if (what == "file") { selectedLink = null; selectedZone = -1; reached = null; }
            if (what == "current" && z.CurrentZone != selectedZone && labels.ContainsKey(z.CurrentZone)) { selectedZone = z.CurrentZone; selectedLink = null; }
            if (selectedLink != null && !z.Set.Links.Contains(selectedLink)) selectedLink = null;
            ShowSide();
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
            // new zones go in columns after the zones already placed, not on top of them
            int col = 0;
            if (!all)
            {
                var placed = z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned && x.GraphX.HasValue).ToList();
                if (placed.Count > 0) col = (int)Math.Ceiling((placed.Max(x => x.GraphX.Value) - 30) / 260.0) + 1;
            }
            foreach (var kv in byLevel)
            {
                double y = 40;
                foreach (var zone in kv.Value)
                {
                    if (y > CanvasH - 80) { col++; y = 40; }
                    zone.GraphX = 30 + col * 260; zone.GraphY = y;
                    y += 44;
                }
                col++;
            }
            // all: the user asked for a new layout (worth saving); otherwise the new zones are only placed for the view
            if (byLevel.Count > 0) z.PositionsChanged(all);
            BuildLabels();
        }

        /// <summary>The start zone and the goal zones as the game has them now (UW.EXE's start square; level 9's arrival and the squares that end the game).</summary>
        void RefreshStartGoal()
        {
            startZone = main.ZoningStartZone(z);
            goalZones = main.ZoningGoalZones(z);
        }

        public int StartZone { get { return startZone; } }
        public IReadOnlyCollection<int> GoalZones { get { return goalZones; } }

        void BuildLabels()
        {
            if (z.Set.Zones.Any(x => x.Id > ZoneSet.Unassigned && !x.GraphX.HasValue)) { Layout(false); return; }
            RefreshStartGoal();
            foreach (var b in labels.Values) canvas.Children.Remove(b);
            labels.Clear();
            labelText.Clear();
            levelOf.Clear();
            foreach (var zone in z.Set.Zones.Where(x => x.Id > ZoneSet.Unassigned))
            {
                uint c = z.Set.ColorOf(zone.Id);
                var lv = z.Set.LevelsOf(zone.Id);
                levelOf[zone.Id] = lv.Count > 0 ? lv[0] : 0;
                string levels = lv.Count == 0 ? "" : lv.Count == 1 ? "  L" + lv[0] : string.Format("  L{0}-L{1}", lv.First(), lv.Last());
                labelText[zone.Id] = zone.Id + "  " + zone.Name + levels;
                bool key = zone.Id == startZone || goalZones.Contains(zone.Id);
                var tb = new TextBlock { Text = labelText[zone.Id], Foreground = TextOn(c), FontSize = 12, FontWeight = key ? FontWeight.Bold : FontWeight.Normal, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
                var b = new Border
                {
                    Child = tb, Background = new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c)),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 3), BorderThickness = new Thickness(3),
                    BorderBrush = LevelBrush(levelOf[zone.Id]), Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = zone.Id,
                };
                ToolTip.SetTip(b, (zone.Id == startZone ? "Start. " : "") + (goalZones.Contains(zone.Id) ? "Goal. " : "") + (string.IsNullOrEmpty(zone.Notes) ? zone.Name : zone.Notes));
                Canvas.SetLeft(b, zone.GraphX ?? 0);
                Canvas.SetTop(b, zone.GraphY ?? 0);
                b.PointerPressed += OnLabelPressed;
                canvas.Children.Add(b);
                labels[zone.Id] = b;
            }
            ApplyCheckLook();
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
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
            int id = (int)((Border)sender).Tag;
            e.Handled = true;
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

        // ---- zoom and panning

        /// <summary>How far the view is scrolled (pixels of the zoomed canvas).</summary>
        public Vector ScrollOffset { get { return scroll.Offset; } set { scroll.Offset = ClampOffset(value); } }
        public bool Panning { get { return panning; } }
        /// <summary>The scale of the view (0.3-2): the wheel and Fit set it.</summary>
        public double Zoom { get { return zoom; } set { zoom = Math.Max(MinZoom, Math.Min(MaxZoom, value)); scale.ScaleX = scale.ScaleY = zoom; } }

        Vector ClampOffset(Vector v)
        {
            double mx = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), my = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
            return new Vector(Math.Max(0, Math.Min(mx, v.X)), Math.Max(0, Math.Min(my, v.Y)));
        }

        /// <summary>Zooms to a scale (0.3-2) so that the point of the canvas under at (a point of the view) stays under it.</summary>
        public void ZoomAt(double value, Point at)
        {
            double old = zoom, now = Math.Max(MinZoom, Math.Min(MaxZoom, value));
            if (Math.Abs(now - old) < 1e-9) return;
            var c = new Point((scroll.Offset.X + at.X) / old, (scroll.Offset.Y + at.Y) / old);
            Zoom = now;
            scroll.UpdateLayout();            // the new extent, before scrolling to keep the point in place
            scroll.Offset = ClampOffset(new Vector(c.X * now - at.X, c.Y * now - at.Y));
        }

        /// <summary>Fit: the zoom and scroll that show every label (as near 1 as they fit).</summary>
        public void Fit()
        {
            if (labels.Count == 0) return;
            var rects = labels.Keys.Select(LabelRect).ToList();
            double x0 = rects.Min(r => r.Left) - 20, y0 = rects.Min(r => r.Top) - 20, x1 = rects.Max(r => r.Right) + 20, y1 = rects.Max(r => r.Bottom) + 20;
            double vw = scroll.Viewport.Width > 0 ? scroll.Viewport.Width : Bounds.Width - 400, vh = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : Bounds.Height - 200;
            Zoom = Math.Min(1, Math.Min(vw / Math.Max(1, x1 - x0), vh / Math.Max(1, y1 - y0)));
            scroll.UpdateLayout();
            scroll.Offset = ClampOffset(new Vector(x0 * zoom, y0 * zoom));
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
            legend.Children.Add(new TextBlock { Text = "Inside: the zone's colour in the zoning. Thick outline: the start; thick dashed: the goal (level 9's arrival, a square that ends the game).", FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        }

        /// <summary>The force layout's settings from the sliders.</summary>
        public ForceOptions Forces { get { return new ForceOptions { Attraction = attraction.Value, Repulsion = repulsion.Value }; } }
        public double Attraction { get { return attraction.Value; } set { attraction.Value = value; } }
        public double Repulsion { get { return repulsion.Value; } set { repulsion.Value = value; } }
        public bool ForcesRunning { get { return forceTimer.IsEnabled; } }

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
            if (forceMoved) z.PositionsChanged(true);
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

        static readonly Color Blocked = Color.Parse("#E5484D");

        /// <summary>After Check the logic: "used" (both ends reached), "blocked" (from a reached zone to one not reached), "out" (neither end reached); null when not shown.</summary>
        public string LinkState(ZoneLink k)
        {
            if (reached == null) return null;
            bool a = reached.ContainsKey(k.From), b = reached.ContainsKey(k.To);
            if (a && b) return "used";
            if (a || (k.TwoWay && b)) return "blocked";
            return "out";
        }

        IBrush ThemeText { get { object o; return this.TryFindResource("Text", ActualThemeVariant, out o) && o is IBrush br ? br : Brushes.White; } }

        public void DrawLinks(DrawingContext dc)
        {
            var font = new Typeface(FontFamily.Default);
            foreach (var k in z.Set.Links)
            {
                if (!labels.ContainsKey(k.From) || !labels.ContainsKey(k.To)) continue;
                var (a, b) = Ends(k);
                Color col;
                if (!KindColors.TryGetValue(k.Kind ?? "", out col)) col = Colors.Gray;
                string state = LinkState(k);
                double width = 1.6;
                if (state == "blocked") { col = Blocked; width = 2.4; }
                else if (state == "out") col = Color.FromArgb(70, col.R, col.G, col.B);
                else if (state == "used") width = 2.2;
                bool sel = k == selectedLink;
                var pen = new Pen(sel ? ThemeText : new SolidColorBrush(col), sel ? 3 : width, k.Confidence == "todo" ? new DashStyle(new double[] { 4, 3 }, 0) : null);
                dc.DrawLine(pen, a, b);
                Arrow(dc, a, b, pen.Brush);
                if (k.TwoWay) Arrow(dc, b, a, pen.Brush);
                bool related = sel || k.From == selectedZone || k.To == selectedZone;
                if (k.Requires != "free" && !string.IsNullOrEmpty(k.Requires) && (related || allText.IsChecked == true || state == "blocked"))
                {
                    string t = k.Requires.Length > 30 ? k.Requires.Substring(0, 29) + "…" : k.Requires;
                    var ft = new FormattedText(t, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 10, Brushes.White);
                    var mid = new Point((a.X + b.X) / 2 - ft.Width / 2, (a.Y + b.Y) / 2 - ft.Height / 2);
                    dc.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 20, 20, 24)), new Rect(mid.X - 3, mid.Y - 1, ft.Width + 6, ft.Height + 2), 3);
                    dc.DrawText(ft, mid);
                }
            }
            // the start (thick) and the goal (thick, dashed), outside the level outline
            var fg = ThemeText;
            if (labels.ContainsKey(startZone)) dc.DrawRectangle(null, new Pen(fg, 4), LabelRect(startZone).Inflate(7), 10, 10);
            foreach (int g in goalZones.Where(labels.ContainsKey))
                dc.DrawRectangle(null, new Pen(fg, 4, new DashStyle(new double[] { 2, 1.5 }, 0)), LabelRect(g).Inflate(g == startZone ? 13 : 7), 10, 10);
            if (selectedZone >= 0 && labels.ContainsKey(selectedZone))
                dc.DrawRectangle(null, new Pen(fg, 1.5), LabelRect(selectedZone).Inflate(3), 8, 8);
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
            if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
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
            if (best != null) SelectLink(best);
            else SelectZone(-1);
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

        // ---- the side panel (to read; editing is in the Zones window)

        void ShowSide()
        {
            side.Children.Clear();
            if (selectedLink != null) { LinkPanel(selectedLink); return; }
            if (selectedZone >= 0 && z.Set.GetZone(selectedZone) != null) { ZonePanel(selectedZone); return; }
            side.Children.Add(new TextBlock { Classes = { "h", "first" }, Text = "ZONE GRAPH" });
            side.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Text = string.Format("{0} zones, {1} links. Lines: one arrowhead one-way, two arrowheads two-way, dashed still to check; the colour is the kind (door, portcullis, secret, drop, lever…), and the text what the path needs. Links are made and edited in the Zones window; this window shows them as they change.", z.Set.Zones.Count(x => x.Id > 1), z.Set.Links.Count),
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
            string role = (id == startZone ? " The start." : "") + (goalZones.Contains(id) ? " A goal." : "");
            int sphere;
            string check = reached == null ? "" : reached.TryGetValue(id, out sphere) ? " Reached in step " + sphere + "." : " Not reached.";
            side.Children.Add(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Text = string.Format("{0} tiles on level{1} {2}; {3} key items; {4} links.{5}{6}", z.Set.TileCount(id), lv.Count == 1 ? "" : "s", string.Join(", ", lv), items, z.Set.Links.Count(k => k.From == id || k.To == id), role, check) });
            if (!string.IsNullOrEmpty(zone?.Notes)) side.Children.Add(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Text = zone.Notes });
            side.Children.Add(Btn("Edit in the Zones window", () => { var zw = main.ShowZones(); z.CurrentZone = id; }));
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
            side.Children.Add(new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Text = string.Format("{0} {1} {2}", z.Label(k.From), k.TwoWay ? "↔" : "→", z.Label(k.To)) });
            string state = LinkState(k);
            side.Children.Add(new TextBlock
            {
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Text = string.Format("Kind: {0}{1}. Needs: {2}.{3}", k.Kind, k.Confidence == "todo" ? " (still to check)" : "", string.IsNullOrEmpty(k.Requires) ? "free" : k.Requires,
                    state == "blocked" ? " The last check reached its start but not its end." : state == "used" ? " The last check reached both ends." : state == "out" ? " The last check reached neither end." : ""),
            });
            if (!string.IsNullOrEmpty(k.Notes)) side.Children.Add(new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Text = k.Notes });
            var row = new WrapPanel();
            row.Children.Add(Btn("Edit in the Zones window", () => EditInZones(k)));
            if (k.Level > 0) row.Children.Add(Btn(string.Format("Go to level {0} ({1}, {2})", k.Level, k.X, k.Y), () => main.GoToTile(k.Level, k.X, k.Y)));
            foreach (Control c in row.Children) c.Margin = new Thickness(0, 4, 6, 0);
            side.Children.Add(row);
        }

        /// <summary>Opens the Zones window on a link (its first zone current, the link chosen).</summary>
        public ZonesWindow EditInZones(ZoneLink k)
        {
            var zw = main.ShowZones();
            if (zw == null) return null;
            z.CurrentZone = k.From;
            zw.SelectLink(k);
            return zw;
        }

        // ---- the logic check

        /// <summary>The last logic check's solve over time, or null.</summary>
        public TimelineResult LastCheck { get; private set; }

        /// <summary>After Check the logic: each zone of the zoning reached -> the step it was reached in; null when not shown.</summary>
        public IReadOnlyDictionary<int, int> Reached { get { return reached; } }

        /// <summary>
        /// The logic check: the solve over time on this zoning, as Apply uses it (see
        /// MainWindow.SolveZoning). The graph then shows what it reached: those zones keep their
        /// colour with their step, the others fade, and a link from a zone reached to one not
        /// reached turns red. Reset graph appearance takes that away.
        /// </summary>
        public string CheckLogic()
        {
            TimelineResult r;
            string text = main.SolveZoning(z, out r);
            ShowCheck(r);
            return text;
        }

        /// <summary>Shows on the graph what a logic check reached.</summary>
        void ShowCheck(TimelineResult r)
        {
            LastCheck = r;
            reached = new Dictionary<int, int>();
            var given = r.Abilities.Auto.Given;
            if (given != null && r.Solve != null)
            {
                Action<int, int> mark = (id, sphere) =>
                {
                    if (z.Set.GetZone(id) == null) return;
                    int was;
                    if (!reached.TryGetValue(id, out was) || sphere < was) reached[id] = sphere;
                };
                foreach (var kv in r.Solve.ZoneSphere) mark(given.Original(kv.Key), kv.Value);
                // a zone of the zoning merged into another (one walkable area): reached when the zone that took it is
                foreach (var kv in r.Abilities.Auto.GivenMap)
                {
                    int sphere;
                    if (kv.Value != kv.Key && r.Solve.ZoneSphere.TryGetValue(kv.Value, out sphere)) mark(given.Original(kv.Key), sphere);
                }
            }
            ApplyCheckLook();
            layer.InvalidateVisual();
            ShowSide();
        }

        /// <summary>Reset graph appearance: takes away what Check the logic showed.</summary>
        public void ResetAppearance()
        {
            reached = null;
            ApplyCheckLook();
            layer.InvalidateVisual();
            ShowSide();
            Status("The graph shows the zoning again, without the last check.");
        }

        void ApplyCheckLook()
        {
            foreach (var kv in labels)
            {
                var tb = (TextBlock)kv.Value.Child;
                int sphere;
                if (reached == null) { kv.Value.Opacity = 1; tb.Text = labelText[kv.Key]; }
                else if (reached.TryGetValue(kv.Key, out sphere)) { kv.Value.Opacity = 1; tb.Text = "[" + sphere + "] " + labelText[kv.Key]; }
                else { kv.Value.Opacity = 0.3; tb.Text = labelText[kv.Key]; }
            }
        }

        bool solving;

        async void Solve()
        {
            if (solving) return;
            solving = true;
            Status("Checking the logic (the solve over time)…");
            string text;
            try
            {
                // the solve on another thread, so the editor keeps answering meanwhile
                var done = await main.SolveZoningAsync(z);
                text = done.Item1;
                ShowCheck(done.Item2);
            }
            catch (Exception ex) { Status("The solver stopped: " + ex.Message); return; }
            finally { solving = false; }
            var sv = LastCheck?.Solve;
            Status((sv == null ? "The solve over time failed." : sv.GoalReached ? "Logic check: goal reached." : "Logic check: goal NOT reached.")
                + (LastCheck?.Abilities.Auto.Given == null ? " The zoning has no zones yet, so nothing is shown on the graph." : string.Format(" {0} of {1} zones reached (numbers in brackets: the step); the others fade, and red lines lead from a zone reached to one not reached. Reset graph appearance takes this away.", reached.Count, labels.Count)));
            await MessageWindow.ShowReport(this, "The logic from the start", text);
        }
    }
}
