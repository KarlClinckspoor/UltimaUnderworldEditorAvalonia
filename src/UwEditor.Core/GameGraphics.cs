using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>A picture as 32-bit RGBA, top row first.</summary>
    public sealed class RgbaImage
    {
        public readonly int Width, Height;
        public readonly byte[] Pixels;

        public RgbaImage(int w, int h)
        {
            Width = w; Height = h;
            Pixels = new byte[w * h * 4];
        }
    }

    /// <summary>
    /// The pictures the 3D view needs: palette 0 (PALS.DAT, 6-bit RGB), the wall and floor
    /// textures (UW1 W64.TR and F32.TR, UW2 T64.TR for both: type 2, edge, count, offsets,
    /// raw edge² bytes each) and the object pictures (OBJECTS.GR, 4-bit through ALLPALS.DAT).
    /// docs/FORMATS.md "Pictures" and "Palettes and light".
    /// </summary>
    public sealed class GameGraphics
    {
        /// <summary>Palette 0 as 0xAARRGGBB (alpha 255).</summary>
        public readonly uint[] Palette = new uint[256];
        public readonly List<byte[]> WallTextures = new List<byte[]>();
        public int WallEdge;
        public readonly List<byte[]> FloorTextures = new List<byte[]>();
        public int FloorEdge;
        /// <summary>OBJECTS.GR, by picture number (= item id); null for an empty picture.</summary>
        public readonly List<RgbaImage> ObjectPictures = new List<RgbaImage>();
        int[][] aux;

        public static GameGraphics Load(string data, GameKind kind)
        {
            var g = new GameGraphics();
            string pals = GameProfile.Find(data, "PALS.DAT");
            if (pals != null)
            {
                var b = File.ReadAllBytes(pals);
                for (int i = 0; i < 256 && i * 3 + 2 < b.Length; i++)
                {
                    uint r = (uint)Math.Min(255, b[i * 3] * 255 / 63), gg = (uint)Math.Min(255, b[i * 3 + 1] * 255 / 63), bl = (uint)Math.Min(255, b[i * 3 + 2] * 255 / 63);
                    g.Palette[i] = 0xFF000000u | r << 16 | gg << 8 | bl;
                }
            }
            var profile = GameProfile.For(kind);
            g.WallEdge = ReadTr(GameProfile.Find(data, profile.WallTextureFile), g.WallTextures);
            if (kind == GameKind.UW2)
            {
                // one list for walls and floors (TEXTMAPS.C)
                g.FloorTextures.AddRange(g.WallTextures);
                g.FloorEdge = g.WallEdge;
            }
            else
                g.FloorEdge = ReadTr(GameProfile.Find(data, profile.FloorTextureFile), g.FloorTextures);

            string allpals = GameProfile.Find(data, "ALLPALS.DAT");
            g.aux = new int[32][];
            byte[] ap = allpals != null ? File.ReadAllBytes(allpals) : new byte[0];
            for (int p = 0; p < 32; p++)
            {
                g.aux[p] = new int[16];
                for (int i = 0; i < 16; i++) g.aux[p][i] = p * 16 + i < ap.Length ? ap[p * 16 + i] : 0;
            }
            string objects = GameProfile.Find(data, "OBJECTS.GR");
            if (objects != null) g.ReadPictures(File.ReadAllBytes(objects), g.ObjectPictures);
            return g;
        }

        static int ReadTr(string path, List<byte[]> into)
        {
            if (path == null) return 0;
            var b = File.ReadAllBytes(path);
            if (b.Length < 4 || b[0] != 2) throw new InvalidDataException(Path.GetFileName(path) + " is not a texture file");
            int edge = b[1], n = b[2] | b[3] << 8;
            for (int i = 0; i < n; i++)
            {
                int o = BitConverter.ToInt32(b, 4 + 4 * i);
                var t = new byte[edge * edge];
                if (o >= 0 && o + t.Length <= b.Length) Array.Copy(b, o, t, 0, t.Length);
                into.Add(t);
            }
            return edge;
        }

        void ReadPictures(byte[] file, List<RgbaImage> into)
        {
            int[] o;
            try { o = GrFile.Offsets(file); }
            catch (FormatException) { return; }
            for (int i = 0; i + 1 < o.Length; i++)
            {
                GrFile.Picture p = null;
                try { p = GrFile.Decode(file, o, i); }
                catch (Exception) { p = null; }
                into.Add(p == null ? null : ToRgba(p));
            }
        }

        /// <summary>A .GR picture in colour; palette index 0 is transparent (the games' sprites).</summary>
        public RgbaImage ToRgba(GrFile.Picture p)
        {
            var img = new RgbaImage(p.Width, p.Height);
            int[] map = p.Type == 4 ? null : aux[p.Aux & 31];
            for (int k = 0; k < p.Width * p.Height && k < p.Pixels.Length; k++)
            {
                int idx = map == null ? p.Pixels[k] : map[p.Pixels[k] & 15];
                Put(img.Pixels, k, idx == 0 ? 0u : Palette[idx]);
            }
            return img;
        }

        /// <summary>A texture (palette indices, edge²) in colour, scaled to the given edge.</summary>
        public RgbaImage TextureRgba(byte[] tex, int edge, int outEdge)
        {
            var img = new RgbaImage(outEdge, outEdge);
            for (int y = 0; y < outEdge; y++)
                for (int x = 0; x < outEdge; x++)
                {
                    int sx = x * edge / outEdge, sy = y * edge / outEdge;
                    Put(img.Pixels, y * outEdge + x, Palette[tex[sy * edge + sx]]);
                }
            return img;
        }

        static void Put(byte[] px, int k, uint argb)
        {
            px[k * 4] = (byte)(argb >> 16);
            px[k * 4 + 1] = (byte)(argb >> 8);
            px[k * 4 + 2] = (byte)argb;
            px[k * 4 + 3] = (byte)(argb >> 24);
        }

        public byte[] WallTexture(int n) { return n >= 0 && n < WallTextures.Count ? WallTextures[n] : null; }
        public byte[] FloorTexture(int n) { return n >= 0 && n < FloorTextures.Count ? FloorTextures[n] : null; }
    }
}
