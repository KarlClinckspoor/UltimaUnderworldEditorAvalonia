using System;
using System.IO;
using UltimateEditor.Games;

namespace UwEditor.Core
{
    /// <summary>
    /// The game's light (map/LIGHTING.C, 3d/VIEW3D.C preset_grid, 3d/GAMESORT.C). The renderer
    /// shades through 16 colour maps (LIGHT.DAT; MONO.DAT's grey ones at light level 5), map 0
    /// the colours themselves and map 15 black. The player's light level, 0-7, picks a row of
    /// SHADES.DAT: smooth_div, smooth_base, smooth_lowpass and curvrad (the range in squares).
    /// A square at distance d (whole squares) gets map min(14, max(0, (sqrt(2 * (8d)²) *
    /// smooth_div &gt;&gt; 6) + smooth_lowpass) + smooth_base); beyond the range it is not drawn.
    /// The level comes from the brightest light carried (OBJECTS.DAT's light table: candle 1,
    /// torch 2, taper 3, lantern 4, UW2's light sphere 6), the light spells and Wizard Eye (6);
    /// UW2 also has a minimum light for some levels (DL.DAT).
    /// </summary>
    public sealed class GameLight
    {
        public struct Row { public int Div, Base, Lowpass, Range; }

        public readonly Row[] Rows = new Row[8];
        /// <summary>How bright each of the 16 maps leaves the palette, 1 for map 0 (LIGHT.DAT).</summary>
        public readonly float[] Factors = new float[16];
        /// <summary>The same for MONO.DAT's maps (level 5).</summary>
        public readonly float[] MonoFactors = new float[16];
        byte[] dl;

        public static readonly string[] LevelNames =
        {
            "0: no light", "1: candle", "2: torch", "3: taper", "4: lantern",
            "5: night vision (grey)", "6: light sphere, Wizard Eye", "7: brightest",
        };

        public static GameLight Load(string dataFolder, uint[] palette)
        {
            var g = new GameLight();
            string shades = GameProfile.Find(dataFolder, "SHADES.DAT");
            byte[] s = shades != null ? File.ReadAllBytes(shades) : null;
            // the shipped table, in case the file is missing
            int[,] shipped = { { 56, 5, -3, 3 }, { 46, 4, -3, 3 }, { 39, 2, -2, 4 }, { 30, 1, 0, 4 }, { 28, 0, 0, 5 }, { 21, 0, 0, 6 }, { 18, 0, 0, 7 }, { 18, 0, 0, 7 } };
            for (int i = 0; i < 8; i++)
            {
                Func<int, int> w = k => s != null && i * 12 + k * 2 + 1 < s.Length ? (short)(s[i * 12 + k * 2] | s[i * 12 + k * 2 + 1] << 8) : shipped[i, k];
                g.Rows[i] = new Row { Div = Math.Max(1, w(0)), Base = w(1), Lowpass = w(2), Range = w(3) };
            }
            Fill(g.Factors, GameProfile.Find(dataFolder, "LIGHT.DAT"), palette);
            Fill(g.MonoFactors, GameProfile.Find(dataFolder, "MONO.DAT"), palette);
            string dlp = GameProfile.Find(dataFolder, "DL.DAT");
            g.dl = dlp != null ? File.ReadAllBytes(dlp) : null;
            return g;
        }

        static double Lum(uint c) { return 0.299 * (c >> 16 & 0xFF) + 0.587 * (c >> 8 & 0xFF) + 0.114 * (c & 0xFF); }

        static void Fill(float[] f, string path, uint[] pal)
        {
            byte[] maps = path != null ? File.ReadAllBytes(path) : null;
            for (int k = 0; k < 16; k++)
            {
                if (maps == null || maps.Length < 4096 || pal == null) { f[k] = 1f - k / 15f; continue; }
                double a = 0, b = 0;
                for (int i = 1; i < 256; i++)
                {
                    a += Lum(pal[maps[k * 256 + i]]);
                    b += Lum(pal[i]);
                }
                f[k] = b > 0 ? (float)Math.Min(1.2, a / b) : 1f - k / 15f;
            }
        }

        /// <summary>The map (0-14, 15 not drawn) for a square d whole squares away at light level 0-7.</summary>
        public int ShadeAt(int level, int d)
        {
            var r = Rows[Math.Max(0, Math.Min(7, level))];
            if (d > r.Range) return 15;
            int u = (int)Math.Sqrt(2.0 * (8 * d) * (8 * d));
            int t = ((u * r.Div) >> 6) + r.Lowpass;
            if (t < 0) t = 0;
            return Math.Min(14, t + r.Base);
        }

        /// <summary>UW2's minimum light for a level (DL.DAT, value 10 and up: value mod 10), or -1.</summary>
        public int MinimumLight(int level)
        {
            if (dl == null || level < 1 || level > dl.Length) return -1;
            int v = dl[level - 1];
            return v >= 10 ? v % 10 : -1;
        }
    }
}
