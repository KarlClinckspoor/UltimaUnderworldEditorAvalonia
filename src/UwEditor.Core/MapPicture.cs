using System;
using System.Collections.Generic;
using UwEditor.Core.Graphics;

namespace UwEditor.Core
{
    /// <summary>
    /// A level seen from above as a picture: each open tile its floor texture, lighter the higher
    /// it lies, the closed half of a diagonal and rock dark, edges where the floor steps, and
    /// marks for objects (creatures red, traps and triggers purple, doors brown, the rest yellow).
    /// North is up.
    /// </summary>
    public static class MapPicture
    {
        public static RgbaImage Render(LevelModel level, int cell = 16)
        {
            cell = Math.Max(4, Math.Min(64, cell));
            int size = 64 * cell;
            var img = new RgbaImage(size, size);
            var g = level.Session.Graphics;
            var floors = new Dictionary<int, RgbaImage>();
            Func<int, RgbaImage> floor = i =>
            {
                RgbaImage r;
                if (floors.TryGetValue(i, out r)) return r;
                int n = level.FloorTextureNumber(i);
                var t = n < 0 ? null : g.FloorTexture(n);
                r = t == null ? null : g.TextureRgba(t, g.FloorEdge, cell);
                floors[i] = r;
                return r;
            };
            for (int ty = 0; ty < 64; ty++)
                for (int tx = 0; tx < 64; tx++)
                {
                    var t = level.Tiles[tx, ty];
                    var tex = t.IsSolid ? null : floor(t.FloorIndex);
                    double shade = 0.55 + 0.45 * t.Height / 15.0;
                    for (int py = 0; py < cell; py++)
                        for (int px = 0; px < cell; px++)
                        {
                            // picture rows go down; the level's y goes north (up)
                            int X = tx * cell + px, Y = (63 - ty) * cell + (cell - 1 - py);
                            int k = (Y * size + X) * 4;
                            double fx = (px + 0.5) / cell, fy = (py + 0.5) / cell;
                            if (t.IsSolid || !OpenAt(t.Type, fx, fy)) { img.Pixels[k] = 22; img.Pixels[k + 1] = 22; img.Pixels[k + 2] = 26; img.Pixels[k + 3] = 255; continue; }
                            byte r = 120, gg = 110, b = 100;
                            if (tex != null) { int s = ((cell - 1 - py) * cell + px) * 4; r = tex.Pixels[s]; gg = tex.Pixels[s + 1]; b = tex.Pixels[s + 2]; }
                            img.Pixels[k] = (byte)(r * shade); img.Pixels[k + 1] = (byte)(gg * shade); img.Pixels[k + 2] = (byte)(b * shade); img.Pixels[k + 3] = 255;
                        }
                }
            // edges between open tiles of different heights, and round rock
            for (int ty = 0; ty < 64; ty++)
                for (int tx = 0; tx < 64; tx++)
                {
                    var t = level.Tiles[tx, ty];
                    if (t.IsSolid) continue;
                    if (tx < 63 && (level.Tiles[tx + 1, ty].IsSolid || level.Tiles[tx + 1, ty].Height != t.Height))
                        for (int py = 0; py < cell; py++) Put(img, size, (tx + 1) * cell - 1, (63 - ty) * cell + py, 0, 0, 0);
                    if (ty < 63 && (level.Tiles[tx, ty + 1].IsSolid || level.Tiles[tx, ty + 1].Height != t.Height))
                        for (int px = 0; px < cell; px++) Put(img, size, tx * cell + px, (63 - ty) * cell, 0, 0, 0);
                }
            foreach (var o in level.Objects)
            {
                if (o.Free || !o.OnTile || o.Index == 0) continue;
                byte r, gg, b;
                if (o.IsCritter) { r = 230; gg = 50; b = 40; }
                else if (o.IsTrapOrTrigger) { r = 190; gg = 80; b = 230; }
                else if (o.Item >= 0x140 && o.Item < 0x150) { r = 150; gg = 95; b = 40; }
                else { r = 250; gg = 215; b = 60; }
                int cx = o.TileX * cell + (int)((o.FineX + 0.5) / 8 * cell), cy = (63 - o.TileY) * cell + (cell - 1 - (int)((o.FineY + 0.5) / 8 * cell));
                int rad = Math.Max(1, cell / 8);
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                        if (dx * dx + dy * dy <= rad * rad) Put(img, size, cx + dx, cy + dy, r, gg, b);
            }
            return img;
        }

        /// <summary>Whether a point of a tile (fractions east and north) is on its open part.</summary>
        static bool OpenAt(TileType t, double fx, double fy)
        {
            switch (t)
            {
                case TileType.OpenSE: return fx > fy;
                case TileType.OpenSW: return fx + fy < 1;
                case TileType.OpenNE: return fx + fy > 1;
                case TileType.OpenNW: return fy > fx;
                default: return true;
            }
        }

        static void Put(RgbaImage img, int size, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= size || y >= size) return;
            int k = (y * size + x) * 4;
            img.Pixels[k] = r; img.Pixels[k + 1] = g; img.Pixels[k + 2] = b; img.Pixels[k + 3] = 255;
        }

        public static byte[] Png(LevelModel level, int cell = 16)
        {
            var img = Render(level, cell);
            return Graphics.Png.EncodeRgba(img.Width, img.Height, img.Pixels);
        }
    }
}
