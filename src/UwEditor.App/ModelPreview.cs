using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using UltimateEditor.Uw1;

namespace UwEditor.App
{
    /// <summary>
    /// A small software view of a built-in model: flat-shaded faces in palette colours, drawn
    /// back to front; drag to turn it, wheel to zoom. Textured faces are drawn grey with an
    /// outline (their texture comes from the object using the model).
    /// </summary>
    public sealed class ModelPreview : Control
    {
        Uw1ExeModels.Model model;
        uint[] palette;
        float yaw = 35, pitch = 25, zoom = 1;
        Point last;
        bool dragging;
        public bool ShowEdges = true;

        public void SetModel(Uw1ExeModels.Model m, uint[] pal)
        {
            model = m; palette = pal;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); dragging = true; last = e.GetPosition(this); e.Pointer.Capture(this); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); dragging = false; e.Pointer.Capture(null); }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!dragging) return;
            var p = e.GetPosition(this);
            yaw += (float)(p.X - last.X) * 0.6f;
            pitch = Math.Clamp(pitch + (float)(p.Y - last.Y) * 0.5f, -89, 89);
            last = p;
            InvalidateVisual();
        }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            zoom = Math.Clamp(zoom * (float)Math.Pow(1.15, e.Delta.Y), 0.2f, 8f);
            InvalidateVisual();
            e.Handled = true;
        }

        public override void Render(DrawingContext dc)
        {
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x19)), new Rect(Bounds.Size));
            if (model == null) return;
            var verts = model.Vertices;
            var pts = verts.Where(v => v != null).ToList();
            if (pts.Count == 0) return;
            // centre and size
            float minx = pts.Min(v => v[0]), maxx = pts.Max(v => v[0]), miny = pts.Min(v => v[1]), maxy = pts.Max(v => v[1]), minz = pts.Min(v => v[2]), maxz = pts.Max(v => v[2]);
            var c = new Vector3((minx + maxx) / 2, (miny + maxy) / 2, (minz + maxz) / 2);
            float size = Math.Max(0.05f, Math.Max(maxx - minx, Math.Max(maxy - miny, maxz - minz)));
            double w = Bounds.Width, h = Bounds.Height;
            float scale = (float)(Math.Min(w, h) * 0.7 / size) * zoom;
            double ya = yaw * Math.PI / 180, pa = pitch * Math.PI / 180;
            float cy = (float)Math.Cos(ya), sy = (float)Math.Sin(ya), cp = (float)Math.Cos(pa), sp = (float)Math.Sin(pa);
            Func<float[], Vector3> view = v =>
            {
                // model: x east, y north, z up; turn round z, then tilt round x
                float x = v[0] - c.X, y = v[1] - c.Y, z = v[2] - c.Z;
                float x1 = x * cy - y * sy, y1 = x * sy + y * cy;
                float y2 = y1 * cp - z * sp, z2 = y1 * sp + z * cp;
                return new Vector3(x1, z2, y2);   // screen x, screen up, depth (away)
            };
            var light = Vector3.Normalize(new Vector3(-0.4f, 0.7f, -0.6f));
            var faces = new List<(float depth, StreamGeometry geo, Color col, bool tex)>();
            foreach (var f in model.Faces)
            {
                var tris = f.Triangles ?? new int[0][];
                if (f.Vertices == null || f.Vertices.Any(i => i < 0 || i >= verts.Count || verts[i] == null)) continue;
                var vs = f.Vertices.Select(i => view(verts[i])).ToArray();
                if (vs.Length < 3) continue;
                var n = Vector3.Cross(vs[1] - vs[0], vs[2] - vs[0]);
                for (int k = 3; n.LengthSquared() < 1e-10f && k < vs.Length; k++) n = Vector3.Cross(vs[1] - vs[0], vs[k] - vs[0]);
                float shade = n.LengthSquared() > 1e-10f ? 0.55f + 0.45f * Math.Abs(Vector3.Dot(Vector3.Normalize(n), light)) : 0.8f;
                uint pc = f.Textured || f.Color < 0 || palette == null ? 0xFF8A8F99u : palette[f.Color];
                var col = Color.FromRgb((byte)((pc >> 16 & 0xFF) * shade), (byte)((pc >> 8 & 0xFF) * shade), (byte)((pc & 0xFF) * shade));
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    Func<Vector3, Point> scr = v => new Point(w / 2 + v.X * scale, h / 2 - v.Y * scale);
                    g.BeginFigure(scr(vs[0]), true);
                    for (int k = 1; k < vs.Length; k++) g.LineTo(scr(vs[k]));
                    g.EndFigure(true);
                }
                faces.Add((vs.Average(v => v.Z), geo, col, f.Textured));
            }
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), 1);
            var texEdge = new Pen(new SolidColorBrush(Color.FromArgb(200, 0xC9, 0x93, 0x4C)), 1);
            foreach (var f in faces.OrderByDescending(x => x.depth))
                dc.DrawGeometry(new SolidColorBrush(f.col), ShowEdges ? (f.tex ? texEdge : edge) : null, f.geo);
        }
    }
}
