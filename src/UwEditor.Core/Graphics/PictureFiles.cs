using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// The palettes: PALS.DAT (palettes of 256 6-bit RGB colours, 8 in UW1, 11 in UW2) and
    /// ALLPALS.DAT (32 auxiliary palettes of 16 colour indexes) (docs/FORMATS.md "Palettes").
    /// </summary>
    public sealed class Palettes
    {
        public readonly List<uint[]> Main = new List<uint[]>();
        public readonly int[][] Aux = new int[32][];

        public static Palettes Load(string data)
        {
            var p = new Palettes();
            string pals = GameProfile.Find(data, "PALS.DAT");
            byte[] b = pals != null ? File.ReadAllBytes(pals) : new byte[0x300];
            for (int n = 0; n + 0x300 <= b.Length; n += 0x300)
            {
                var pal = new uint[256];
                for (int i = 0; i < 256; i++)
                    pal[i] = 0xFF000000u | Six(b[n + 3 * i]) << 16 | Six(b[n + 3 * i + 1]) << 8 | Six(b[n + 3 * i + 2]);
                p.Main.Add(pal);
            }
            string all = GameProfile.Find(data, "ALLPALS.DAT");
            byte[] a = all != null ? File.ReadAllBytes(all) : new byte[0];
            for (int i = 0; i < 32; i++)
            {
                p.Aux[i] = new int[16];
                for (int k = 0; k < 16; k++) p.Aux[i][k] = i * 16 + k < a.Length ? a[i * 16 + k] : 0;
            }
            return p;
        }

        static uint Six(byte v) { return (uint)Math.Min(255, (v & 63) * 255 / 63); }

        public uint[] Get(int n) { return Main[Math.Max(0, Math.Min(Main.Count - 1, n))]; }

        /// <summary>A colour table through palette indexes; index 0 transparent when asked (the sprites').</summary>
        public uint[] Through(IList<int> indexes, int palette, bool zeroClear)
        {
            var pal = Get(palette);
            var t = new uint[indexes.Count];
            for (int i = 0; i < t.Length; i++) t[i] = zeroClear && indexes[i] == 0 ? 0u : pal[indexes[i] & 255];
            return t;
        }

        public uint[] Solid(int palette) { return (uint[])Get(palette).Clone(); }

        public uint[] Sprite(int palette)
        {
            var t = Solid(palette);
            t[0] = 0;
            return t;
        }
    }

    public enum PictureFileKind { Gr, Tr, Byt, BytArk }

    /// <summary>
    /// A file of pictures: a .GR (sprites, panels, portraits), a .TR (wall and floor textures),
    /// a UW1 .BYT screen or UW2's BYT.ARK of screens (docs/FORMATS.md "Pictures"). Pictures
    /// are read as indices; a replacement must keep the picture's size and colours, and the
    /// file is written back with the others unchanged.
    /// </summary>
    public sealed class PictureFile
    {
        public string Path { get; private set; }
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        public PictureFileKind Kind { get; private set; }
        public GameKind Game { get; private set; }
        byte[] file;
        int[] offsets;
        Archive ark;
        readonly Dictionary<int, IndexedImage> replaced = new Dictionary<int, IndexedImage>();
        public const int ScreenBytes = 320 * 200;

        public static PictureFile Load(string path, GameKind game)
        {
            var f = new PictureFile { Path = path, Game = game, file = File.ReadAllBytes(path) };
            string ext = System.IO.Path.GetExtension(path).ToUpperInvariant();
            if (ext == ".GR") { f.Kind = PictureFileKind.Gr; f.offsets = GrFile.Offsets(f.file); }
            else if (ext == ".TR") { f.Kind = PictureFileKind.Tr; if (f.file.Length < 4 || f.file[0] != 2) throw new InvalidDataException(f.Name + " is not a texture file."); }
            else if (ext == ".BYT") f.Kind = PictureFileKind.Byt;
            else if (f.Name.ToUpperInvariant() == "BYT.ARK") { f.Kind = PictureFileKind.BytArk; f.ark = Archive.Open(f.file, game); }
            else throw new InvalidDataException(f.Name + " is not a picture file the editor reads.");
            return f;
        }

        /// <summary>Every picture file in DATA.</summary>
        public static List<string> Find(string data)
        {
            var names = Directory.GetFiles(data).Where(p =>
            {
                string e = System.IO.Path.GetExtension(p).ToUpperInvariant();
                return e == ".GR" || e == ".TR" || e == ".BYT" || System.IO.Path.GetFileName(p).ToUpperInvariant() == "BYT.ARK";
            }).OrderBy(p => System.IO.Path.GetFileName(p).ToUpperInvariant()).ToList();
            return names;
        }

        public int Count
        {
            get
            {
                switch (Kind)
                {
                    case PictureFileKind.Gr: return offsets.Length - 1;
                    case PictureFileKind.Tr: return file[2] | file[3] << 8;
                    case PictureFileKind.Byt: return 1;
                    default: return ark.Count;
                }
            }
        }

        public int TextureEdge { get { return Kind == PictureFileKind.Tr ? file[1] : 0; } }

        /// <summary>The palette the game shows a picture with, where the code says (UW2's BYT.ARK screens); 0 otherwise.</summary>
        public int DefaultPalette(int i)
        {
            if (Kind == PictureFileKind.BytArk && Game == GameKind.UW2)
            {
                // gfx/SHOWPIC.C callers: 0 automap 1, 1 chargen 3, 5 main menu 2, 6 Origin 5, 7 Looking Glass 6, 8 end 7
                switch (i) { case 0: return 1; case 1: return 3; case 5: return 2; case 6: return 5; case 7: return 6; case 8: return 7; }
            }
            return 0;
        }

        public bool IsChanged(int i) { return replaced.ContainsKey(i); }
        public bool HasChanges { get { return replaced.Count > 0; } }

        /// <summary>Picture i with the colours of the given palette, or null when empty.</summary>
        public IndexedImage Get(int i, Palettes pals, int palette)
        {
            IndexedImage r;
            if (replaced.TryGetValue(i, out r)) return Recolour(r.Clone(), pals, palette);
            switch (Kind)
            {
                case PictureFileKind.Gr:
                    {
                        if (GrFile.IsEmpty(offsets, i)) return null;
                        int rw, rh;
                        if (RawPanel(i, out rw, out rh))
                        {
                            var raw = new int[rw * rh];
                            for (int k = 0; k < raw.Length; k++) raw[k] = file[offsets[i] + k];
                            return Recolour(new IndexedImage(rw, rh, raw, null) { Type = 4 }, pals, palette);
                        }
                        GrFile.Picture p;
                        try { p = GrFile.Decode(file, offsets, i); }
                        catch (Exception) { return null; }   // a type the game's decoders lack (0), or a picture cut short
                        if (p == null) return null;
                        var img = new IndexedImage(p.Width, p.Height, p.Pixels, null) { Type = p.Type, Aux = p.Aux };
                        return Recolour(img, pals, palette);
                    }
                case PictureFileKind.Tr:
                    {
                        int edge = file[1], o = BitConverter.ToInt32(file, 4 + 4 * i);
                        // UW2's T64.TR counts 264 textures, but 256-263 point at the end of the file: nothing there to show or replace
                        if (o < 0 || o + edge * edge > file.Length) return null;
                        var px = new int[edge * edge];
                        for (int k = 0; k < px.Length && o + k < file.Length; k++) px[k] = file[o + k];
                        return new IndexedImage(edge, edge, px, pals.Solid(palette)) { Type = 4 };
                    }
                case PictureFileKind.Byt:
                    return Screen(file, pals, palette);
                default:
                    {
                        var b = ark.Has(i) ? ark.Read(i) : null;
                        return b == null || b.Length < ScreenBytes ? null : Screen(b, pals, palette);
                    }
            }
        }

        /// <summary>PANELS.GR holds the side panels as bare 8-bit pictures without headers: 83 × 114 in UW1, 79 × 112 in UW2 (UW-Formats; measured).</summary>
        bool RawPanel(int i, out int w, out int h)
        {
            w = Game == GameKind.UW2 ? 79 : 83; h = Game == GameKind.UW2 ? 112 : 114;
            return Kind == PictureFileKind.Gr && Name.ToUpperInvariant() == "PANELS.GR" && offsets[i + 1] - offsets[i] == w * h;
        }

        static IndexedImage Screen(byte[] b, Palettes pals, int palette)
        {
            var px = new int[ScreenBytes];
            for (int k = 0; k < px.Length && k < b.Length; k++) px[k] = b[k];
            return new IndexedImage(320, 200, px, pals.Solid(palette)) { Type = 4 };
        }

        IndexedImage Recolour(IndexedImage img, Palettes pals, int palette)
        {
            if (Kind != PictureFileKind.Gr) { img.Colors = pals.Solid(palette); return img; }
            img.Colors = img.Type == 4 ? pals.Sprite(palette) : pals.Through(pals.Aux[img.Aux & 31], palette, true);
            return img;
        }

        public string Describe(int i, IndexedImage img)
        {
            if (img == null) return "empty";
            string kind = Kind == PictureFileKind.Gr ? (img.Type == 4 ? "8-bit" : img.Type == 8 ? "4-bit run-length, auxiliary palette " + img.Aux : "4-bit, auxiliary palette " + img.Aux)
                : Kind == PictureFileKind.Tr ? "texture" : "screen";
            return string.Format("{0} × {1}, {2}", img.Width, img.Height, kind);
        }

        /// <summary>Replaces picture i (until Save). The pixels must fit its size and colour table.</summary>
        public void Replace(int i, IndexedImage like, int[] pixels)
        {
            if (pixels == null || pixels.Length != like.Width * like.Height) throw new ArgumentException("The picture must keep its size.");
            int max = like.Colors.Length;
            foreach (var v in pixels) if (v < 0 || v >= max) throw new ArgumentException("A pixel is outside the picture's " + max + " colours.");
            var r = like.Clone();
            r.Pixels = (int[])pixels.Clone();
            replaced[i] = r;
        }

        public void Revert(int i) { replaced.Remove(i); }

        /// <summary>The file with the replacements; notes says when it grew.</summary>
        public byte[] ToBytes(List<string> notes = null)
        {
            if (replaced.Count == 0) return (byte[])file.Clone();
            byte[] o;
            switch (Kind)
            {
                case PictureFileKind.Gr:
                    {
                        int pw, ph;
                        if (replaced.Keys.Any(k => RawPanel(k, out pw, out ph)))
                        {
                            o = (byte[])file.Clone();
                            foreach (var kv in replaced)
                            {
                                if (!RawPanel(kv.Key, out pw, out ph)) throw new InvalidDataException("PANELS.GR's other pictures cannot be written.");
                                for (int k = 0; k < pw * ph; k++) o[offsets[kv.Key] + k] = (byte)kv.Value.Pixels[k];
                            }
                            break;
                        }
                        var map = replaced.ToDictionary(kv => kv.Key, kv => new GrFile.Picture { Type = kv.Value.Type, Width = kv.Value.Width, Height = kv.Value.Height, Aux = kv.Value.Aux, Pixels = kv.Value.Pixels });
                        o = GrFile.Rebuild(file, map);
                        if (notes != null && o.Length > file.Length)
                            notes.Add(string.Format("{0} grows from {1} to {2} bytes (the run-length code packs the new pictures less tightly).", Name, file.Length, o.Length));
                        break;
                    }
                case PictureFileKind.Tr:
                    {
                        o = (byte[])file.Clone();
                        int edge = file[1];
                        foreach (var kv in replaced)
                        {
                            int at = BitConverter.ToInt32(file, 4 + 4 * kv.Key);
                            if (at + edge * edge > o.Length) throw new InvalidDataException("Texture " + kv.Key + " runs past the end of " + Name + ".");
                            for (int k = 0; k < edge * edge; k++) o[at + k] = (byte)kv.Value.Pixels[k];
                        }
                        break;
                    }
                case PictureFileKind.Byt:
                    o = (byte[])file.Clone();
                    for (int k = 0; k < ScreenBytes; k++) o[k] = (byte)replaced[0].Pixels[k];
                    break;
                default:
                    {
                        var copy = Archive.Open(file, Game);
                        foreach (var kv in replaced)
                        {
                            var old = copy.Read(kv.Key);
                            var b = (byte[])old.Clone();
                            for (int k = 0; k < ScreenBytes; k++) b[k] = (byte)kv.Value.Pixels[k];
                            copy.Write(kv.Key, b);
                        }
                        o = copy.ToBytes();
                        break;
                    }
            }
            return o;
        }

        /// <summary>Writes the file (the old one kept as .BKP).</summary>
        public List<string> Save()
        {
            var notes = new List<string>();
            var bytes = ToBytes(notes);
            Conversations.SafeFile.Write(Path, bytes);
            file = bytes;
            if (Kind == PictureFileKind.Gr) offsets = GrFile.Offsets(file);
            if (Kind == PictureFileKind.BytArk) ark = Archive.Open(file, Game);
            replaced.Clear();
            return notes;
        }
    }
}
