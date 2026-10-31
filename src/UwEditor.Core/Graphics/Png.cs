using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace UwEditor.Core.Graphics
{
    /// <summary>A decoded PNG: RGBA pixels, and the palette indices when the file was indexed.</summary>
    public sealed class PngImage
    {
        public int Width, Height;
        /// <summary>R, G, B, A per pixel, top row first.</summary>
        public byte[] Rgba;
        /// <summary>For an indexed file: the index of each pixel and the file's palette (ARGB); otherwise null.</summary>
        public int[] Indices;
        public uint[] Palette;
    }

    /// <summary>
    /// Just enough PNG for exporting and importing the games' pictures: writes 8-bit indexed
    /// files (the game's palette as the PNG palette, so indices survive a round trip) and reads
    /// non-interlaced files of every colour type at depths 1-16.
    /// </summary>
    public static class Png
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>An indexed PNG. palette: ARGB, up to 256; an entry with alpha 0 is transparent.</summary>
        public static byte[] EncodeIndexed(int width, int height, int[] indices, uint[] palette)
        {
            if (palette.Length < 1 || palette.Length > 256) throw new ArgumentException("1-256 colours");
            var ms = new MemoryStream();
            ms.Write(Signature, 0, 8);
            var ihdr = new byte[13];
            Be(ihdr, 0, width); Be(ihdr, 4, height);
            ihdr[8] = 8; ihdr[9] = 3;
            Chunk(ms, "IHDR", ihdr);
            var plte = new byte[palette.Length * 3];
            int lastAlpha = -1;
            for (int i = 0; i < palette.Length; i++)
            {
                plte[3 * i] = (byte)(palette[i] >> 16); plte[3 * i + 1] = (byte)(palette[i] >> 8); plte[3 * i + 2] = (byte)palette[i];
                if ((palette[i] >> 24) != 0xFF) lastAlpha = i;
            }
            Chunk(ms, "PLTE", plte);
            if (lastAlpha >= 0)
            {
                var trns = new byte[lastAlpha + 1];
                for (int i = 0; i <= lastAlpha; i++) trns[i] = (byte)(palette[i] >> 24);
                Chunk(ms, "tRNS", trns);
            }
            var raw = new byte[(width + 1) * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) raw[y * (width + 1) + 1 + x] = (byte)indices[y * width + x];
            Chunk(ms, "IDAT", Deflate(raw));
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        /// <summary>An RGBA PNG.</summary>
        public static byte[] EncodeRgba(int width, int height, byte[] rgba)
        {
            var ms = new MemoryStream();
            ms.Write(Signature, 0, 8);
            var ihdr = new byte[13];
            Be(ihdr, 0, width); Be(ihdr, 4, height);
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(ms, "IHDR", ihdr);
            var raw = new byte[(width * 4 + 1) * height];
            for (int y = 0; y < height; y++) Array.Copy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
            Chunk(ms, "IDAT", Deflate(raw));
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        public static PngImage Decode(byte[] file)
        {
            if (file == null || file.Length < 8) throw new InvalidDataException("Not a PNG file.");
            for (int i = 0; i < 8; i++) if (file[i] != Signature[i]) throw new InvalidDataException("Not a PNG file.");
            int p = 8, width = 0, height = 0, depth = 0, type = 0, interlace = 0;
            byte[] plte = null, trns = null;
            var idat = new MemoryStream();
            while (p + 8 <= file.Length)
            {
                int len = (file[p] << 24) | (file[p + 1] << 16) | (file[p + 2] << 8) | file[p + 3];
                string name = Encoding.ASCII.GetString(file, p + 4, 4);
                if (len < 0 || p + 12 + len > file.Length) throw new InvalidDataException("The PNG is cut short.");
                var data = new byte[len];
                Array.Copy(file, p + 8, data, 0, len);
                p += 12 + len;
                if (name == "IHDR")
                {
                    width = BeInt(data, 0); height = BeInt(data, 4); depth = data[8]; type = data[9]; interlace = data[12];
                }
                else if (name == "PLTE") plte = data;
                else if (name == "tRNS") trns = data;
                else if (name == "IDAT") idat.Write(data, 0, data.Length);
                else if (name == "IEND") break;
            }
            if (width <= 0 || height <= 0) throw new InvalidDataException("The PNG has no image header.");
            if (interlace != 0) throw new InvalidDataException("Interlaced PNG files are not read; save it without interlacing.");
            int channels = type == 0 ? 1 : type == 2 ? 3 : type == 3 ? 1 : type == 4 ? 2 : type == 6 ? 4 : 0;
            if (channels == 0) throw new InvalidDataException("Unknown PNG colour type " + type + ".");
            if (type == 3 && plte == null) throw new InvalidDataException("The indexed PNG has no palette.");
            int bpp = Math.Max(1, channels * depth / 8);
            int stride = (width * channels * depth + 7) / 8;
            byte[] raw;
            using (var z = new ZLibStream(new MemoryStream(idat.ToArray()), CompressionMode.Decompress))
            using (var o = new MemoryStream()) { z.CopyTo(o); raw = o.ToArray(); }
            if (raw.Length < (stride + 1) * height) throw new InvalidDataException("The PNG's pixel data is cut short.");
            var cur = new byte[stride];
            var prev = new byte[stride];
            var img = new PngImage { Width = width, Height = height, Rgba = new byte[width * height * 4] };
            if (type == 3)
            {
                img.Indices = new int[width * height];
                int n = plte.Length / 3;
                img.Palette = new uint[n];
                for (int i = 0; i < n; i++)
                    img.Palette[i] = (uint)((trns != null && i < trns.Length ? trns[i] : 255) << 24 | plte[3 * i] << 16 | plte[3 * i + 1] << 8 | plte[3 * i + 2]);
            }
            for (int y = 0; y < height; y++)
            {
                int f = raw[y * (stride + 1)];
                Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
                Unfilter(f, cur, prev, bpp);
                for (int x = 0; x < width; x++)
                {
                    int k = (y * width + x) * 4;
                    switch (type)
                    {
                        case 3:
                            {
                                int idx = Sample(cur, x, depth);
                                img.Indices[y * width + x] = idx;
                                uint c = idx < img.Palette.Length ? img.Palette[idx] : 0xFF000000u;
                                img.Rgba[k] = (byte)(c >> 16); img.Rgba[k + 1] = (byte)(c >> 8); img.Rgba[k + 2] = (byte)c; img.Rgba[k + 3] = (byte)(c >> 24);
                                break;
                            }
                        case 0:
                            {
                                int v = Scale(Sample(cur, x, depth), depth);
                                img.Rgba[k] = img.Rgba[k + 1] = img.Rgba[k + 2] = (byte)v;
                                img.Rgba[k + 3] = 255;
                                if (trns != null && trns.Length >= 2 && Sample(cur, x, depth) == (trns[0] << 8 | trns[1])) img.Rgba[k + 3] = 0;
                                break;
                            }
                        default:
                            {
                                int w = depth / 8;   // 1 or 2 bytes a channel
                                Func<int, byte> ch = c => cur[(x * channels + c) * w];
                                if (type == 2) { img.Rgba[k] = ch(0); img.Rgba[k + 1] = ch(1); img.Rgba[k + 2] = ch(2); img.Rgba[k + 3] = 255; }
                                else if (type == 4) { img.Rgba[k] = img.Rgba[k + 1] = img.Rgba[k + 2] = ch(0); img.Rgba[k + 3] = ch(1); }
                                else { img.Rgba[k] = ch(0); img.Rgba[k + 1] = ch(1); img.Rgba[k + 2] = ch(2); img.Rgba[k + 3] = ch(3); }
                                if (type == 2 && trns != null && trns.Length >= 6 && depth == 8 && ch(0) == trns[1] && ch(1) == trns[3] && ch(2) == trns[5]) img.Rgba[k + 3] = 0;
                                break;
                            }
                    }
                }
                var t = prev; prev = cur; cur = t;
            }
            return img;
        }

        static int Sample(byte[] row, int x, int depth)
        {
            if (depth == 8) return row[x];
            if (depth == 16) return row[2 * x] << 8 | row[2 * x + 1];
            int perByte = 8 / depth;
            int b = row[x / perByte];
            int shift = 8 - depth * (x % perByte + 1);
            return (b >> shift) & ((1 << depth) - 1);
        }

        static int Scale(int v, int depth)
        {
            if (depth == 16) return v >> 8;
            if (depth == 8) return v;
            return v * 255 / ((1 << depth) - 1);
        }

        static void Unfilter(int f, byte[] cur, byte[] prev, int bpp)
        {
            for (int i = 0; i < cur.Length; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                switch (f)
                {
                    case 0: break;
                    case 1: cur[i] = (byte)(cur[i] + a); break;
                    case 2: cur[i] = (byte)(cur[i] + b); break;
                    case 3: cur[i] = (byte)(cur[i] + (a + b) / 2); break;
                    case 4:
                        {
                            int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
                            cur[i] = (byte)(cur[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                            break;
                        }
                    default: throw new InvalidDataException("Unknown PNG filter " + f + ".");
                }
            }
        }

        static byte[] Deflate(byte[] raw)
        {
            var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        static void Chunk(Stream s, string name, byte[] data)
        {
            var b = new byte[4];
            Be(b, 0, data.Length); s.Write(b, 0, 4);
            var nb = Encoding.ASCII.GetBytes(name);
            s.Write(nb, 0, 4);
            s.Write(data, 0, data.Length);
            uint crc = Crc(nb, 0xFFFFFFFFu);
            crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            Be(b, 0, (int)crc); s.Write(b, 0, 4);
        }

        static uint[] crcTable;
        static uint Crc(byte[] d, uint c)
        {
            if (crcTable == null)
            {
                var t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint v = n;
                    for (int k = 0; k < 8; k++) v = (v & 1) != 0 ? 0xEDB88320u ^ (v >> 1) : v >> 1;
                    t[n] = v;
                }
                crcTable = t;
            }
            foreach (var x in d) c = crcTable[(c ^ x) & 0xFF] ^ (c >> 8);
            return c;
        }

        static void Be(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
        static int BeInt(byte[] b, int at) { return b[at] << 24 | b[at + 1] << 16 | b[at + 2] << 8 | b[at + 3]; }
    }
}
