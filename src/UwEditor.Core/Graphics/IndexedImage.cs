using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// A picture as the game stores it: indices into a small colour table. For 8-bit pictures the
    /// table is the whole palette; for 4-bit and 5-bit ones it is the auxiliary palette's 16 or 32
    /// colours. Colours (ARGB) with alpha 0 are transparent (palette index 0 in a sprite).
    /// </summary>
    public sealed class IndexedImage
    {
        public int Width, Height;
        public int[] Pixels;
        public uint[] Colors;
        /// <summary>Extra values to keep with the picture (a .GR type and auxiliary palette, a critter frame's hot spot).</summary>
        public int Type, Aux, HotX, HotY;

        public IndexedImage(int w, int h, int[] pixels, uint[] colors)
        {
            Width = w; Height = h; Pixels = pixels; Colors = colors;
        }

        public IndexedImage Clone()
        {
            var c = (IndexedImage)MemberwiseClone();
            c.Pixels = (int[])Pixels.Clone();
            return c;
        }

        public byte[] ToRgba()
        {
            var o = new byte[Width * Height * 4];
            for (int k = 0; k < Width * Height; k++)
            {
                int i = Pixels[k];
                uint c = i >= 0 && i < Colors.Length ? Colors[i] : 0xFFFF00FFu;
                o[4 * k] = (byte)(c >> 16); o[4 * k + 1] = (byte)(c >> 8); o[4 * k + 2] = (byte)c; o[4 * k + 3] = (byte)(c >> 24);
            }
            return o;
        }

        /// <summary>An indexed PNG whose palette is the picture's colours, so importing it back keeps every index.</summary>
        public byte[] ToPng() { return Png.EncodeIndexed(Width, Height, Pixels, Colors); }
    }

    /// <summary>What importing a picture found.</summary>
    public sealed class ImportResult
    {
        public int[] Pixels;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Notes = new List<string>();
        /// <summary>Colours the picture uses that its colour table lacks, with pixel counts.</summary>
        public readonly Dictionary<uint, int> Missing = new Dictionary<uint, int>();
        public bool Ok { get { return Errors.Count == 0 && Pixels != null; } }
        public string Summary { get { return string.Join("\n", Errors.Concat(Notes)); } }
    }

    /// <summary>
    /// Turning a PNG back into a picture's indices, refusing what the game cannot show: a
    /// different size, or a colour outside the picture's colour table (unless asked to take the
    /// nearest one). An indexed PNG whose palette is the table (as the editor exports it) is
    /// read index for index, so colours the palette has twice stay as they were.
    /// </summary>
    public static class PictureImport
    {
        public static ImportResult FromPng(byte[] png, IndexedImage like, bool nearest = false)
        {
            var r = new ImportResult();
            PngImage img;
            try { img = Png.Decode(png); }
            catch (Exception ex) { r.Errors.Add(ex.Message); return r; }
            return FromImage(img, like, nearest, r);
        }

        public static ImportResult FromImage(PngImage img, IndexedImage like, bool nearest = false, ImportResult r = null)
        {
            r = r ?? new ImportResult();
            if (img.Width != like.Width || img.Height != like.Height)
            {
                r.Errors.Add(string.Format("The picture is {0} × {1}; this one must stay {2} × {3}, the size the game reads it at.", img.Width, img.Height, like.Width, like.Height));
                return r;
            }
            int n = img.Width * img.Height;
            var px = new int[n];
            // an indexed file with the same table: take the indices
            if (img.Indices != null && SameTable(img.Palette, like.Colors))
            {
                bool inRange = true;
                for (int k = 0; k < n; k++) { px[k] = img.Indices[k]; if (px[k] >= like.Colors.Length) inRange = false; }
                if (inRange) { r.Pixels = px; r.Notes.Add("The pixels were read index for index (the PNG's palette is the picture's own)."); return r; }
            }
            int transparent = Array.FindIndex(like.Colors, c => (c >> 24) == 0);
            var exact = new Dictionary<uint, int>();
            for (int i = 0; i < like.Colors.Length; i++)
            {
                uint c = like.Colors[i];
                if ((c >> 24) == 0) continue;
                uint rgb = c & 0xFFFFFF;
                if (!exact.ContainsKey(rgb)) exact[rgb] = i;
            }
            int transparentPixels = 0, mapped = 0;
            for (int k = 0; k < n; k++)
            {
                byte a = img.Rgba[4 * k + 3];
                uint rgb = (uint)(img.Rgba[4 * k] << 16 | img.Rgba[4 * k + 1] << 8 | img.Rgba[4 * k + 2]);
                if (a < 128)
                {
                    if (transparent < 0) { transparentPixels++; px[k] = Nearest(like.Colors, rgb); continue; }
                    px[k] = transparent;
                    continue;
                }
                int idx;
                if (exact.TryGetValue(rgb, out idx)) { px[k] = idx; continue; }
                r.Missing[rgb] = r.Missing.TryGetValue(rgb, out int m) ? m + 1 : 1;
                px[k] = Nearest(like.Colors, rgb);
                mapped++;
            }
            if (transparentPixels > 0)
                r.Errors.Add(string.Format("{0} pixel{1} transparent, but this picture has no transparent colour (it is drawn solid).", transparentPixels, transparentPixels == 1 ? " is" : "s are"));
            if (r.Missing.Count > 0)
            {
                var list = r.Missing.OrderByDescending(kv => kv.Value).Take(8)
                    .Select(kv => string.Format("#{0:X6} ({1} px, nearest #{2:X6})", kv.Key, kv.Value, like.Colors[Nearest(like.Colors, kv.Key)] & 0xFFFFFF));
                string msg = string.Format("{0} colour{1} not in this picture's {2}-colour table: {3}{4}",
                    r.Missing.Count, r.Missing.Count == 1 ? " is" : "s are", like.Colors.Length, string.Join(", ", list), r.Missing.Count > 8 ? ", …" : ".");
                if (nearest) r.Notes.Add(msg + " " + mapped + " pixels were given the nearest colour.");
                else r.Errors.Add(msg + " Use the nearest colours, or repaint with the table's colours (export the picture to get them).");
            }
            r.Pixels = px;
            return r;
        }

        static bool SameTable(uint[] png, uint[] table)
        {
            if (png == null || png.Length < table.Length) return false;
            for (int i = 0; i < table.Length; i++)
            {
                bool clearA = (png[i] >> 24) == 0, clearB = (table[i] >> 24) == 0;
                if (clearA != clearB) return false;
                if (!clearA && (png[i] & 0xFFFFFF) != (table[i] & 0xFFFFFF)) return false;
            }
            return true;
        }

        /// <summary>The nearest solid colour of the table (squared RGB distance).</summary>
        public static int Nearest(uint[] table, uint rgb)
        {
            int best = 0; long bd = long.MaxValue;
            int r = (int)(rgb >> 16) & 255, g = (int)(rgb >> 8) & 255, b = (int)rgb & 255;
            for (int i = 0; i < table.Length; i++)
            {
                if ((table[i] >> 24) == 0) continue;
                int dr = (int)(table[i] >> 16 & 255) - r, dg = (int)(table[i] >> 8 & 255) - g, db = (int)(table[i] & 255) - b;
                long d = 3L * dr * dr + 4L * dg * dg + 2L * db * db;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }
}
