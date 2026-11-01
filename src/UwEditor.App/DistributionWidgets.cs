using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>Theme colours for the hand-drawn widgets.</summary>
    static class WidgetPaint
    {
        public static IBrush Brush(Control c, string key, IBrush fallback)
        {
            object v;
            var app = Application.Current;
            if (app != null && app.TryFindResource(key, app.ActualThemeVariant, out v) && v is IBrush ab) return ab;
            return c.TryFindResource(key, c.ActualThemeVariant, out v) && v is IBrush b ? b : fallback;
        }

        public static FormattedText Text(string s, IBrush brush, double size = 10.5)
        {
            return new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), size, brush);
        }
    }

    /// <summary>
    /// Bars to drag: one value per x (a weight or a chance), 0 to <see cref="Max"/>. Press on a
    /// bar and drag up or down; sweeping sideways sets every bar the pointer crosses.
    /// </summary>
    public sealed class BarsEditor : Control
    {
        double[] values;
        public double[] Values { get { return values; } set { values = value; InvalidateVisual(); } }
        public string[] Labels { get; set; }
        public double Max { get; set; } = 100;
        /// <summary>Weights: the hovered bar also shows its share of the total.</summary>
        public bool Relative { get; set; } = true;
        /// <summary>The y axis's name, drawn at the top left.</summary>
        public string Caption { get; set; } = "";
        public event Action Changed;

        int hover = -1, last = -1;
        bool dragging;
        const double Left = 34, Right = 8, Top = 18, Bottom = 20;

        public BarsEditor(double[] values, string[] labels)
        {
            this.values = values;
            Labels = labels;
            Height = 150;
            MaxWidth = 820;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Width = 820;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        Rect Plot { get { return new Rect(Left, Top, Math.Max(10, Bounds.Width - Left - Right), Math.Max(10, Bounds.Height - Top - Bottom)); } }

        int BarAt(Point p)
        {
            var r = Plot;
            if (values == null || values.Length == 0) return -1;
            int i = (int)Math.Floor((p.X - r.X) / (r.Width / values.Length));
            return Math.Max(0, Math.Min(values.Length - 1, i));
        }

        double ValueAt(Point p)
        {
            var r = Plot;
            double v = (r.Bottom - p.Y) / r.Height * Max;
            v = Math.Max(0, Math.Min(Max, v));
            return Math.Round(v);
        }

        public override void Render(DrawingContext dc)
        {
            var r = Plot;
            var bg = WidgetPaint.Brush(this, "PanelAlt", Brushes.DimGray);
            var line = WidgetPaint.Brush(this, "Line", Brushes.Gray);
            var text = WidgetPaint.Brush(this, "Muted", Brushes.Gray);
            var accent = WidgetPaint.Brush(this, "Accent", Brushes.Orange);
            dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            dc.FillRectangle(bg, r);
            var pen = new Pen(line, 1);
            for (int k = 0; k <= 4; k++)
            {
                double y = r.Bottom - r.Height * k / 4;
                dc.DrawLine(pen, new Point(r.X, y), new Point(r.Right, y));
                var t = WidgetPaint.Text((Max * k / 4).ToString("0"), text);
                dc.DrawText(t, new Point(r.X - t.Width - 4, y - t.Height / 2));
            }
            if (!string.IsNullOrEmpty(Caption)) dc.DrawText(WidgetPaint.Text(Caption, text), new Point(2, 0));
            if (values == null || values.Length == 0) return;
            double w = r.Width / values.Length, sum = values.Sum(v => Math.Max(0, v));
            int every = Math.Max(1, (int)Math.Ceiling(24 / w));
            for (int i = 0; i < values.Length; i++)
            {
                double h = Math.Max(0, Math.Min(Max, values[i])) / Max * r.Height;
                var bar = new Rect(r.X + i * w + Math.Min(2, w * 0.15), r.Bottom - h, Math.Max(1, w - 2 * Math.Min(2, w * 0.15)), h);
                dc.FillRectangle(accent, bar);
                if (i == hover) dc.DrawRectangle(null, new Pen(WidgetPaint.Brush(this, "Text", Brushes.White), 1), bar.Inflate(1));
                if (Labels != null && i < Labels.Length && (i % every == 0 || Labels[i].Length > 2))
                {
                    var t = WidgetPaint.Text(Labels[i], text);
                    dc.DrawText(t, new Point(r.X + i * w + w / 2 - t.Width / 2, r.Bottom + 3));
                }
            }
            if (hover >= 0 && hover < values.Length)
            {
                string label = Labels != null && hover < Labels.Length ? Labels[hover] : (hover + 1).ToString();
                string s = label + ": " + values[hover].ToString("0.#") + (Relative ? (sum > 0 ? string.Format(" ({0:0.#}% of all)", 100 * Math.Max(0, values[hover]) / sum) : "") : "%");
                var t = WidgetPaint.Text(s, WidgetPaint.Brush(this, "Text", Brushes.White), 11);
                double x = Math.Max(r.X, Math.Min(r.Right - t.Width, r.X + hover * w + w / 2 - t.Width / 2));
                dc.DrawText(t, new Point(x, 2));
            }
        }

        void Set(Point p)
        {
            int i = BarAt(p);
            if (i < 0) return;
            double v = ValueAt(p);
            if (last >= 0 && last != i)
            {
                // fill the bars between the last one and this one, so a quick sweep leaves no gaps
                double lv = values[last];
                int step = i > last ? 1 : -1;
                for (int k = last + step; k != i; k += step) values[k] = Math.Round(lv + (v - lv) * (k - last) / (double)(i - last));
            }
            values[i] = v;
            last = i;
            hover = i;
            InvalidateVisual();
            Changed?.Invoke();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            dragging = true; last = -1;
            e.Pointer.Capture(this);
            Set(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            if (dragging) Set(p);
            else { int h = BarAt(p); if (h != hover) { hover = h; InvalidateVisual(); } }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            dragging = false; last = -1;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (!dragging) { hover = -1; InvalidateVisual(); }
        }
    }

    /// <summary>
    /// A curve through points: click empty space to add a point, drag a point to move it,
    /// right-click a point to remove it. The two end points stay at the ends of the range (only
    /// their height moves). The curve is monotone cubic, so it never overshoots its points.
    /// </summary>
    public sealed class CurveEditor : Control
    {
        List<CurvePoint> points;
        public List<CurvePoint> Points { get { return points; } set { points = value; Normalise(); InvalidateVisual(); } }
        public double XMin { get; private set; }
        public double XMax { get; private set; }
        public double YMax { get; set; } = 100;
        /// <summary>Where the curve is read (levels, qualities): dots on the curve.</summary>
        public double[] Marks { get; set; }
        public string XCaption { get; set; } = "";
        public string YCaption { get; set; } = "";
        /// <summary>Labels for whole x values, or null for numbers.</summary>
        public Func<double, string> XLabel { get; set; }
        public double XTickStep { get; set; } = 1;
        public event Action Changed;

        CurvePoint drag;
        Point? hoverPos;
        const double Left = 34, Right = 10, Top = 18, Bottom = 22, Grab = 8;

        public CurveEditor(List<CurvePoint> points, double xmin, double xmax)
        {
            XMin = xmin; XMax = xmax;
            this.points = points;
            Normalise();
            Height = 180;
            MaxWidth = 820;
            ClipToBounds = true;
        }

        /// <summary>Sorted, inside the range, with points at both ends.</summary>
        void Normalise()
        {
            if (points == null) return;
            foreach (var p in points) { p.X = Math.Max(XMin, Math.Min(XMax, p.X)); p.Y = Math.Max(0, Math.Min(YMax, p.Y)); }
            points.Sort((a, b) => a.X.CompareTo(b.X));
            if (points.Count == 0 || points[0].X > XMin) points.Insert(0, new CurvePoint(XMin, points.Count > 0 ? points[0].Y : 50));
            if (points[points.Count - 1].X < XMax) points.Add(new CurvePoint(XMax, points[points.Count - 1].Y));
        }

        Rect Plot { get { return new Rect(Left, Top, Math.Max(10, Bounds.Width - Left - Right), Math.Max(10, Bounds.Height - Top - Bottom)); } }
        Point ToScreen(double x, double y) { var r = Plot; return new Point(r.X + (x - XMin) / (XMax - XMin) * r.Width, r.Bottom - y / YMax * r.Height); }
        void FromScreen(Point p, out double x, out double y)
        {
            var r = Plot;
            x = XMin + (p.X - r.X) / r.Width * (XMax - XMin);
            y = (r.Bottom - p.Y) / r.Height * YMax;
            x = Math.Max(XMin, Math.Min(XMax, x)); y = Math.Max(0, Math.Min(YMax, y));
        }

        CurvePoint Near(Point p)
        {
            CurvePoint best = null; double bd = Grab;
            foreach (var q in points)
            {
                var s = ToScreen(q.X, q.Y);
                double d = Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y));
                if (d <= bd) { bd = d; best = q; }
            }
            return best;
        }

        public override void Render(DrawingContext dc)
        {
            var r = Plot;
            var bg = WidgetPaint.Brush(this, "PanelAlt", Brushes.DimGray);
            var line = WidgetPaint.Brush(this, "Line", Brushes.Gray);
            var muted = WidgetPaint.Brush(this, "Muted", Brushes.Gray);
            var accent = WidgetPaint.Brush(this, "Accent", Brushes.Orange);
            var text = WidgetPaint.Brush(this, "Text", Brushes.White);
            dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            dc.FillRectangle(bg, r);
            var pen = new Pen(line, 1);
            for (int k = 0; k <= 4; k++)
            {
                double y = r.Bottom - r.Height * k / 4;
                dc.DrawLine(pen, new Point(r.X, y), new Point(r.Right, y));
                var t = WidgetPaint.Text((YMax * k / 4).ToString("0"), muted);
                dc.DrawText(t, new Point(r.X - t.Width - 4, y - t.Height / 2));
            }
            for (double x = XMin; x <= XMax + 1e-9; x += XTickStep)
            {
                var s = ToScreen(x, 0);
                dc.DrawLine(pen, new Point(s.X, r.Bottom), new Point(s.X, r.Bottom + 3));
                var t = WidgetPaint.Text(XLabel != null ? XLabel(x) : x.ToString("0"), muted);
                dc.DrawText(t, new Point(s.X - t.Width / 2, r.Bottom + 4));
            }
            if (!string.IsNullOrEmpty(YCaption)) dc.DrawText(WidgetPaint.Text(YCaption, muted), new Point(2, 0));
            if (points == null || points.Count == 0) return;
            // the curve
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                int steps = Math.Max(40, (int)r.Width / 2);
                for (int i = 0; i <= steps; i++)
                {
                    double x = XMin + (XMax - XMin) * i / steps;
                    var s = ToScreen(x, Math.Max(0, Math.Min(YMax, Distributions.Eval(points, x))));
                    if (i == 0) g.BeginFigure(s, false); else g.LineTo(s);
                }
                g.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(accent, 2), geo);
            if (Marks != null)
                foreach (var m in Marks)
                {
                    var s = ToScreen(m, Math.Max(0, Math.Min(YMax, Distributions.Eval(points, m))));
                    dc.DrawEllipse(muted, null, s, 2.5, 2.5);
                }
            foreach (var q in points)
            {
                var s = ToScreen(q.X, q.Y);
                dc.DrawEllipse(q == drag ? text : accent, new Pen(text, 1), s, 5, 5);
            }
            if (hoverPos.HasValue)
            {
                double x, y; FromScreen(hoverPos.Value, out x, out y);
                double v = Distributions.Eval(points, x);
                string sx = XLabel != null ? XLabel(Math.Round(x)) : x.ToString("0.#");
                var t = WidgetPaint.Text(string.Format("{0} {1}: {2:0.#}", XCaption, sx, Math.Max(0, v)), text, 11);
                dc.DrawText(t, new Point(Math.Max(r.X, r.Right - t.Width), 2));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var p = e.GetPosition(this);
            var props = e.GetCurrentPoint(this).Properties;
            var near = Near(p);
            if (props.IsRightButtonPressed)
            {
                if (near != null && near != points[0] && near != points[points.Count - 1] && points.Count > 2)
                {
                    points.Remove(near);
                    InvalidateVisual();
                    Changed?.Invoke();
                }
                e.Handled = true;
                return;
            }
            if (!props.IsLeftButtonPressed) return;
            if (near == null)
            {
                double x, y; FromScreen(p, out x, out y);
                if (x <= XMin || x >= XMax || points.Any(q => Math.Abs(q.X - x) < 1e-6)) return;
                near = new CurvePoint(x, y);
                points.Add(near);
                points.Sort((a, b) => a.X.CompareTo(b.X));
                Changed?.Invoke();
            }
            drag = near;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            hoverPos = p;
            if (drag != null)
            {
                double x, y; FromScreen(p, out x, out y);
                int i = points.IndexOf(drag);
                if (i == 0) x = XMin;
                else if (i == points.Count - 1) x = XMax;
                else
                {
                    double gap = (XMax - XMin) / 1000;
                    x = Math.Max(points[i - 1].X + gap, Math.Min(points[i + 1].X - gap, x));
                }
                drag.X = x; drag.Y = Math.Round(y, 1);
                Changed?.Invoke();
            }
            Cursor = new Cursor(drag != null || Near(p) != null ? StandardCursorType.Hand : StandardCursorType.Cross);
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            drag = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            hoverPos = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Bars lying down, one row per named value (effects, items): drag a row's bar left or
    /// right; sweeping up or down sets every row crossed. The share of the total shows at the end.
    /// </summary>
    public sealed class BarListEditor : Control
    {
        readonly string[] labels;
        readonly double[] values;
        public double Max { get; set; } = 100;
        public event Action Changed;
        const double Row = 20, LabelWidth = 250, Right = 110;
        int last = -1;
        bool dragging;

        public BarListEditor(string[] labels, double[] values)
        {
            this.labels = labels;
            this.values = values;
            Height = labels.Length * Row + 4;
            MaxWidth = 880;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Width = 880;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }

        public double[] Values { get { return values; } }

        Rect Track { get { return new Rect(LabelWidth, 2, Math.Max(20, Bounds.Width - LabelWidth - Right), labels.Length * Row); } }

        public override void Render(DrawingContext dc)
        {
            var r = Track;
            var bg = WidgetPaint.Brush(this, "PanelAlt", Brushes.DimGray);
            var line = WidgetPaint.Brush(this, "Line", Brushes.Gray);
            var text = WidgetPaint.Brush(this, "Text", Brushes.White);
            var muted = WidgetPaint.Brush(this, "Muted", Brushes.Gray);
            var accent = WidgetPaint.Brush(this, "Accent", Brushes.Orange);
            dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            double sum = values.Sum(v => Math.Max(0, v));
            for (int i = 0; i < labels.Length; i++)
            {
                double y = r.Y + i * Row;
                var t = WidgetPaint.Text(labels[i], text, 12);
                dc.DrawText(t, new Point(0, y + (Row - t.Height) / 2));
                var track = new Rect(r.X, y + 3, r.Width, Row - 6);
                dc.FillRectangle(bg, track);
                double w = Math.Max(0, Math.Min(Max, values[i])) / Max * r.Width;
                dc.FillRectangle(accent, new Rect(r.X, y + 3, w, Row - 6));
                var v = WidgetPaint.Text(values[i].ToString("0.#") + (sum > 0 ? string.Format("  ({0:0.#}%)", 100 * Math.Max(0, values[i]) / sum) : ""), muted, 11);
                dc.DrawText(v, new Point(r.Right + 8, y + (Row - v.Height) / 2));
            }
            dc.DrawLine(new Pen(line, 1), new Point(r.X, r.Y), new Point(r.X, r.Bottom));
        }

        void Set(Point p)
        {
            var r = Track;
            int i = (int)Math.Floor((p.Y - r.Y) / Row);
            if (i < 0 || i >= labels.Length) return;
            double v = Math.Round(Math.Max(0, Math.Min(Max, (p.X - r.X) / r.Width * Max)));
            if (last >= 0 && last != i)
            {
                double lv = values[last]; int step = i > last ? 1 : -1;
                for (int k = last + step; k != i; k += step) values[k] = Math.Round(lv + (v - lv) * (k - last) / (double)(i - last));
            }
            values[i] = v;
            last = i;
            InvalidateVisual();
            Changed?.Invoke();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            if (p.X < LabelWidth - 4) return;
            dragging = true; last = -1;
            e.Pointer.Capture(this);
            Set(p);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (dragging) Set(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            dragging = false; last = -1;
            e.Pointer.Capture(null);
        }
    }
}
