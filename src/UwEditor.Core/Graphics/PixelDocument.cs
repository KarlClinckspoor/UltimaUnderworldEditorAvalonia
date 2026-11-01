using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// A picture being drawn in the pixel editor: palette indexes of a fixed size, the colours
    /// the picture may use (its colour table: 16 for a 4-bit picture's auxiliary palette, 256 for
    /// an 8-bit one), and the drawing operations with undo. Nothing here knows the file: the
    /// pixels go back through the picture's own Replace.
    /// </summary>
    public sealed class PixelDocument
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int[] Pixels { get; private set; }
        /// <summary>The colour table (ARGB; alpha 0 transparent).</summary>
        public uint[] Colors { get; private set; }
        readonly int[] original;
        readonly List<int[]> undo = new List<int[]>(), redo = new List<int[]>();
        int[] stepStart;
        public const int UndoDepth = 200;

        public PixelDocument(IndexedImage img)
        {
            Width = img.Width; Height = img.Height;
            Pixels = (int[])img.Pixels.Clone();
            Colors = img.Colors ?? new uint[256];
            original = (int[])img.Pixels.Clone();
        }

        /// <summary>The colour index drawn as transparent (alpha 0), or −1 when the picture has none (textures, screens).</summary>
        public int Transparent { get { for (int i = 0; i < Colors.Length; i++) if ((Colors[i] >> 24) == 0) return i; return -1; } }

        public bool Changed { get { return !Pixels.SequenceEqual(original); } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        public bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }
        public int Get(int x, int y) { return Inside(x, y) ? Pixels[y * Width + x] : -1; }

        /// <summary>Starts one undo step (a stroke, a fill, a line).</summary>
        public void Begin() { if (stepStart == null) stepStart = (int[])Pixels.Clone(); }

        /// <summary>Ends the step; a step that changed nothing leaves no undo.</summary>
        public void End()
        {
            if (stepStart == null) return;
            if (!stepStart.SequenceEqual(Pixels))
            {
                undo.Add(stepStart);
                if (undo.Count > UndoDepth) undo.RemoveAt(0);
                redo.Clear();
            }
            stepStart = null;
        }

        public void Undo() { End(); if (undo.Count == 0) return; redo.Add(Pixels); Pixels = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); }
        public void Redo() { End(); if (redo.Count == 0) return; undo.Add(Pixels); Pixels = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); }

        /// <summary>Back to the picture as it was opened (one undo step).</summary>
        public void Revert() { Begin(); Array.Copy(original, Pixels, Pixels.Length); End(); }

        void Check(int c) { if (c < 0 || c >= Colors.Length) throw new ArgumentOutOfRangeException(nameof(c), "The picture has " + Colors.Length + " colours."); }

        /// <summary>A square brush of width w (1 = one pixel) centred on (x, y).</summary>
        public void Dab(int x, int y, int w, int c)
        {
            Check(c);
            w = Math.Max(1, w);
            int x0 = x - (w - 1) / 2, y0 = y - (w - 1) / 2;
            for (int yy = y0; yy < y0 + w; yy++)
                for (int xx = x0; xx < x0 + w; xx++)
                    if (Inside(xx, yy)) Pixels[yy * Width + xx] = c;
        }

        /// <summary>A line of brush dabs from one pixel to another (Bresenham), both ends included.</summary>
        public void Line(int x0, int y0, int x1, int y1, int w, int c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 100000; guard++)
            {
                Dab(x0, y0, w, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>A rectangle's outline (or the whole of it) between two corners.</summary>
        public void Rectangle(int x0, int y0, int x1, int y1, int w, int c, bool filled)
        {
            int l = Math.Min(x0, x1), r = Math.Max(x0, x1), t = Math.Min(y0, y1), b = Math.Max(y0, y1);
            if (filled) { for (int y = t; y <= b; y++) for (int x = l; x <= r; x++) if (Inside(x, y)) Pixels[y * Width + x] = c; return; }
            Line(l, t, r, t, w, c); Line(r, t, r, b, w, c); Line(r, b, l, b, w, c); Line(l, b, l, t, w, c);
        }

        /// <summary>Fills the area of one colour round (x, y), four neighbours a step. Returns how many pixels changed.</summary>
        public int Fill(int x, int y, int c)
        {
            Check(c);
            if (!Inside(x, y)) return 0;
            int from = Get(x, y);
            if (from == c) return 0;
            int n = 0;
            var stack = new Stack<int>();
            stack.Push(y * Width + x);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                if (Pixels[k] != from) continue;
                Pixels[k] = c; n++;
                int kx = k % Width, ky = k / Width;
                if (kx > 0) stack.Push(k - 1);
                if (kx < Width - 1) stack.Push(k + 1);
                if (ky > 0) stack.Push(k - Width);
                if (ky < Height - 1) stack.Push(k + Width);
            }
            return n;
        }

        /// <summary>The colours the picture uses now, most used first.</summary>
        public List<int> Used() { return Pixels.GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList(); }

        /// <summary>The picture as ARGB-ordered RGBA bytes (transparent pixels with alpha 0).</summary>
        public byte[] ToRgba() { return new IndexedImage(Width, Height, Pixels, Colors).ToRgba(); }
    }
}
