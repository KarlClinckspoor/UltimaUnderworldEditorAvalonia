using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Graphics
{
    /// <summary>A critter frame: its size, hot spot, format and pixels (auxiliary palette indexes).</summary>
    public sealed class CritterFrame
    {
        public int Number;
        public int Width, Height, HotX, HotY, Format;
        public int[] Pixels;
        /// <summary>The frame's bytes in the page, from its header on.</summary>
        public byte[] Bytes;
        public int Offset;
    }

    /// <summary>An animation: frames in order (page and frame number).</summary>
    public sealed class CritterAnimation
    {
        public string Name;
        public readonly List<Tuple<CritterPage, int>> Frames = new List<Tuple<CritterPage, int>>();
        public override string ToString() { return Name + " (" + Frames.Count + " frame" + (Frames.Count == 1 ? ")" : "s)"); }
    }

    /// <summary>
    /// One critter page file. UW1's CRIT\CRxxPAGE.Nyy (critter/CRPAGES.C, UW-Formats): slot
    /// base, slot count, a segment number per slot, the segments (8 frame numbers each, 0xFF
    /// ends), the auxiliary palettes (32 colour indexes each), the frame count, a format byte
    /// and a word offset per frame. UW2's CRIT\CRxx.yy (3d/PGCACHE.ASM parse_cpg): the 32-byte
    /// palettes from 0, a word offset per frame number at 0x80 (frame numbers run on across a
    /// critter's pages; CRIT\PG.MP gives each page's last). A frame is width, height, hot spot
    /// x and y, the format (6: 5-bit run-length, 8: 4-bit run-length), a word count of
    /// words and the packed words. The game reads at most 0x7FFF bytes of a page (load_cpg).
    /// </summary>
    public sealed class CritterPage
    {
        public const int MaxBytes = 0x7FFF;
        public string Path { get; private set; }
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        public GameKind Game { get; private set; }
        public int PageNumber { get; private set; }
        public byte[] Data { get; private set; }
        public readonly List<int[]> Palettes = new List<int[]>();
        public readonly SortedDictionary<int, CritterFrame> Frames = new SortedDictionary<int, CritterFrame>();
        /// <summary>UW1: the slot base, and each slot's segment (0xFF none); the segments' frame lists.</summary>
        public int SlotBase;
        public int[] SlotSegments = new int[0];
        public List<int[]> Segments = new List<int[]>();
        int tableAt, tableCount;
        readonly HashSet<int> changed = new HashSet<int>();

        public static CritterPage Load(string path, GameKind game, int page)
        {
            var p = new CritterPage { Path = path, Game = game, PageNumber = page, Data = File.ReadAllBytes(path) };
            p.Parse();
            return p;
        }

        void Parse()
        {
            var d = Data;
            if (Game == GameKind.UW1)
            {
                int q = 0;
                SlotBase = d[q++];
                int nslots = d[q++];
                SlotSegments = new int[nslots];
                for (int i = 0; i < nslots; i++) SlotSegments[i] = d[q++];
                int nseg = d[q++];
                for (int i = 0; i < nseg; i++) { Segments.Add(d.Skip(q).Take(8).Select(b => (int)b).ToArray()); q += 8; }
                int naux = d[q++];
                for (int i = 0; i < naux; i++) { Palettes.Add(d.Skip(q).Take(32).Select(b => (int)b).ToArray()); q += 32; }
                tableCount = d[q++];
                q++;                    // the format byte
                tableAt = q;
            }
            else
            {
                for (int i = 0; i < 4; i++) Palettes.Add(d.Skip(32 * i).Take(32).Select(b => (int)b).ToArray());
                tableAt = 0x80;
                tableCount = 256;
                int first = int.MaxValue;
                for (int i = 0; i < 256; i++) { int o = U16(d, 0x80 + 2 * i); if (o > 0) first = Math.Min(first, o); }
                if (first != int.MaxValue) tableCount = Math.Min(256, (first - 0x80) / 2);
            }
            var offs = new List<int>();
            for (int i = 0; i < tableCount; i++) offs.Add(U16(d, tableAt + 2 * i));
            var sorted = offs.Where(o => o > 0).Distinct().OrderBy(o => o).ToList();
            for (int i = 0; i < tableCount; i++)
            {
                int o = offs[i];
                if (o <= 0 || o + 7 > d.Length) continue;
                int next = sorted.FirstOrDefault(x => x > o);
                int end = next > o ? next : d.Length;
                var f = new CritterFrame { Number = i, Offset = o, Width = d[o], Height = d[o + 1], HotX = d[o + 2], HotY = d[o + 3], Format = d[o + 4] };
                f.Bytes = d.Skip(o).Take(end - o).ToArray();
                f.Pixels = DecodePixels(f, d, o);
                Frames[i] = f;
            }
        }

        static int[] DecodePixels(CritterFrame f, byte[] d, int o)
        {
            int n = f.Width * f.Height;
            var px = new int[n];
            int words = U16(d, o + 5);
            int bits = f.Format == 6 ? 5 : f.Format == 8 ? 4 : 0;
            if (bits == 0) return px;
            var w = new int[words];
            int bit = 0, start = o + 7;
            for (int i = 0; i < words; i++)
            {
                int v = 0;
                for (int k = 0; k < bits; k++, bit++)
                {
                    int at = start + bit / 8;
                    int b = at < d.Length ? d[at] : 0;
                    v = v << 1 | (b >> (7 - bit % 8)) & 1;
                }
                w[i] = v;
            }
            GrFile.DecodeRle(w, px);
            return px;
        }

        /// <summary>The colours of auxiliary palette n (16 for a 4-bit frame, 32 for a 5-bit one); its index-0 colour is transparent.</summary>
        public uint[] Colors(int palette, int format, Palettes pals, int mainPalette = 0)
        {
            var aux = palette >= 0 && palette < Palettes.Count ? Palettes[palette] : new int[32];
            int n = format == 8 ? 16 : 32;
            return pals.Through(aux.Take(n).ToList(), mainPalette, true);
        }

        public IndexedImage Image(int frame, int palette, Palettes pals, int mainPalette = 0)
        {
            CritterFrame f;
            if (!Frames.TryGetValue(frame, out f)) return null;
            return new IndexedImage(f.Width, f.Height, f.Pixels, Colors(palette, f.Format, pals, mainPalette)) { Type = f.Format, HotX = f.HotX, HotY = f.HotY };
        }

        public bool HasChanges { get { return changed.Count > 0; } }
        public bool IsChanged(int frame) { return changed.Contains(frame); }

        /// <summary>
        /// Replaces a frame's pixels (same size; indexes below 16 for a 4-bit frame, 32 for a
        /// 5-bit one) and lays the page out again. Throws when the page would pass the 0x7FFF
        /// bytes the game reads.
        /// </summary>
        public void Replace(int frame, int[] pixels)
        {
            CritterFrame f;
            if (!Frames.TryGetValue(frame, out f)) throw new ArgumentException("No frame " + frame + " in " + Name + ".");
            if (pixels.Length != f.Width * f.Height) throw new ArgumentException("The frame must keep its size, " + f.Width + " × " + f.Height + ".");
            int bits = f.Format == 6 ? 5 : 4, max = 1 << bits;
            foreach (var v in pixels) if (v < 0 || v >= max) throw new ArgumentException("A pixel is outside the frame's " + max + " colours.");
            var words = EncodeRle(pixels, max - 1);
            if (words.Count > 0xFFFF) throw new InvalidDataException("The frame does not fit its word count.");
            var b = new byte[7 + (words.Count * bits + 7) / 8];
            Array.Copy(f.Bytes, b, 5);
            b[5] = (byte)words.Count; b[6] = (byte)(words.Count >> 8);
            int bit = 0;
            foreach (var w in words)
                for (int k = bits - 1; k >= 0; k--, bit++)
                    if (((w >> k) & 1) != 0) b[7 + bit / 8] |= (byte)(0x80 >> (bit % 8));
            var old = f.Bytes;
            f.Bytes = b;
            var data = Layout();
            if (data.Length > MaxBytes)
            {
                f.Bytes = old;
                throw new InvalidDataException(string.Format("{0} would be {1} bytes; the game reads at most {2} (0x7FFF) of a critter page (3d/PGCACHE.ASM load_cpg). Use fewer colours or larger areas of one colour.", Name, data.Length, MaxBytes));
            }
            f.Pixels = (int[])pixels.Clone();
            Data = data;
            foreach (var x in Frames.Values) x.Offset = U16(Data, tableAt + 2 * x.Number);
            changed.Add(frame);
        }

        /// <summary>The page with its frames laid out again, in their order in the file, after the header.</summary>
        byte[] Layout()
        {
            var order = Frames.Values.OrderBy(f => f.Offset).ToList();
            int start = order.Count > 0 ? order[0].Offset : Data.Length;
            var o = new MemoryStream();
            o.Write(Data, 0, start);
            var at = new Dictionary<int, int>();
            foreach (var f in order) { at[f.Number] = (int)o.Length; o.Write(f.Bytes, 0, f.Bytes.Length); }
            var r = o.ToArray();
            foreach (var kv in at) { r[tableAt + 2 * kv.Key] = (byte)kv.Value; r[tableAt + 2 * kv.Key + 1] = (byte)(kv.Value >> 8); }
            return r;
        }

        public void Save()
        {
            Conversations.SafeFile.Write(Path, Data);
            changed.Clear();
        }

        /// <summary>The run-length code of .GR files, for words up to mask (15 or 31); counts stay below 16 a word.</summary>
        public static List<int> EncodeRle(int[] pixels, int mask)
        {
            var o = new List<int>();
            int n = pixels.Length, i = 0;
            while (i < n)
            {
                var reps = new List<int[]>();
                while (i < n)
                {
                    int len = Run(pixels, i);
                    if (len < 3) break;
                    reps.Add(new[] { len, pixels[i] });
                    i += len;
                }
                if (reps.Count == 0) Count(o, 1);
                else
                {
                    if (reps.Count > 1) { Count(o, 2); Count(o, reps.Count); }
                    foreach (var r in reps) { Count(o, r[0]); o.Add(r[1] & mask); }
                }
                if (i >= n) break;
                int start = i;
                while (i < n && i - start < 0xFFF && Run(pixels, i) < 3) i++;
                Count(o, i - start);
                for (int k = start; k < i; k++) o.Add(pixels[k] & mask);
            }
            return o;
        }

        static int Run(int[] p, int i)
        {
            int j = i + 1;
            while (j < p.Length && p[j] == p[i] && j - i < 0xFFF) j++;
            return j - i;
        }

        static void Count(List<int> o, int c)
        {
            if (c < 16) o.Add(c);
            else if (c < 256) { o.Add(0); o.Add(c >> 4); o.Add(c & 0xF); }
            else { o.Add(0); o.Add(0); o.Add(0); o.Add(c >> 8); o.Add((c >> 4) & 0xF); o.Add(c & 0xF); }
        }

        static int U16(byte[] d, int p) { return p + 1 < d.Length ? d[p] | d[p + 1] << 8 : 0; }
    }

    /// <summary>A critter art file (its pages) and its animations.</summary>
    public sealed class CritterFile
    {
        public int Number;
        public string Name;
        public readonly List<CritterPage> Pages = new List<CritterPage>();
        public readonly List<CritterAnimation> Animations = new List<CritterAnimation>();
        /// <summary>The critter types (item id − 0x40) drawn with it, and the palette each uses.</summary>
        public readonly List<Tuple<int, int>> Users = new List<Tuple<int, int>>();
        public override string ToString() { return string.Format("{0:00}  {1}", Number, Name); }
    }

    /// <summary>
    /// The critters' art: UW1's CRIT\ASSOC.ANM (32 names of 8 characters, then at 0x100 a
    /// critter file and palette per critter type) and its CRxxPAGE.Nyy pages; UW2's
    /// CRIT\AS.AN (the same pairs), CR.AN (the animations: 32 files × 8 actions × 8 views, 8
    /// bytes each, up to 7 frame numbers ended by 0xFF, then the count), PG.MP and CRxx.yy.
    /// File numbers are written in octal in the names.
    /// </summary>
    public static class CritterArt
    {
        public static List<CritterFile> Load(string gameFolder, GameKind game, ObjectNames names)
        {
            var list = new List<CritterFile>();
            string crit = Directory.GetDirectories(gameFolder).FirstOrDefault(d => string.Equals(System.IO.Path.GetFileName(d), "CRIT", StringComparison.OrdinalIgnoreCase));
            if (crit == null) return list;
            string assoc = GameProfile.Find(crit, game == GameKind.UW2 ? "AS.AN" : "ASSOC.ANM");
            byte[] a = assoc != null ? File.ReadAllBytes(assoc) : new byte[0];
            int pairsAt = game == GameKind.UW2 ? 0 : 0x100;
            byte[] seq = game == GameKind.UW2 && GameProfile.Find(crit, "CR.AN") != null ? File.ReadAllBytes(GameProfile.Find(crit, "CR.AN")) : null;
            for (int n = 0; n < 32; n++)
            {
                var f = new CritterFile { Number = n };
                for (int p = 0; p < 8; p++)
                {
                    string name = game == GameKind.UW2 ? string.Format("CR{0}{1}.{2:00}", n >> 3, n & 7, p) : string.Format("CR{0}{1}PAGE.N{2:00}", n >> 3, n & 7, p);
                    string path = GameProfile.Find(crit, name);
                    if (path == null) continue;
                    try { f.Pages.Add(CritterPage.Load(path, game, p)); } catch (Exception) { }
                }
                if (f.Pages.Count == 0) continue;
                for (int t = 0; t < 64 && pairsAt + 2 * t + 1 < a.Length; t++)
                    if (a[pairsAt + 2 * t] == n) f.Users.Add(Tuple.Create(t, (int)a[pairsAt + 2 * t + 1]));
                string label = game == GameKind.UW1 && 8 * n + 8 <= a.Length ? Ascii(a, 8 * n) : null;
                var users = f.Users.Select(u => names == null ? null : names.Get(0x40 + u.Item1)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
                f.Name = (string.IsNullOrEmpty(label) ? "" : label + (users.Count > 0 ? ": " : "")) + string.Join(", ", users);
                if (string.IsNullOrEmpty(f.Name)) f.Name = "(no critter uses it)";
                if (game == GameKind.UW1) Uw1Animations(f);
                else Uw2Animations(f, seq);
                list.Add(f);
            }
            return list;
        }

        static string Ascii(byte[] a, int at)
        {
            var c = new List<char>();
            for (int i = 0; i < 8 && a[at + i] != 0; i++) c.Add((char)a[at + i]);
            return new string(c.ToArray());
        }

        static void Uw1Animations(CritterFile f)
        {
            foreach (var p in f.Pages)
                for (int s = 0; s < p.SlotSegments.Length; s++)
                {
                    int seg = p.SlotSegments[s];
                    if (seg == 0xFF || seg >= p.Segments.Count) continue;
                    var anim = new CritterAnimation { Name = string.Format("animation {0} (page {1}, segment {2})", p.SlotBase + s, p.PageNumber, seg) };
                    foreach (int fr in p.Segments[seg]) { if (fr == 0xFF) break; if (p.Frames.ContainsKey(fr)) anim.Frames.Add(Tuple.Create(p, fr)); }
                    if (anim.Frames.Count > 0) f.Animations.Add(anim);
                }
            // frames no animation shows
            AddRest(f);
        }

        static void Uw2Animations(CritterFile f, byte[] seq)
        {
            if (seq != null)
                for (int action = 0; action < 8; action++)
                    for (int view = 0; view < 8; view++)
                    {
                        int at = ((f.Number * 8 + action) * 8 + view) * 8;
                        if (at + 8 > seq.Length) continue;
                        int count = seq[at + 7];
                        var anim = new CritterAnimation { Name = string.Format("action {0}, view {1}", action, view) };
                        for (int k = 0; k < 7 && k < count; k++)
                        {
                            int fr = seq[at + k];
                            if (fr == 0xFF) break;
                            var page = f.Pages.FirstOrDefault(p => p.Frames.ContainsKey(fr));
                            if (page != null) anim.Frames.Add(Tuple.Create(page, fr));
                        }
                        if (anim.Frames.Count > 0) f.Animations.Add(anim);
                    }
            AddRest(f);
        }

        static void AddRest(CritterFile f)
        {
            var all = new CritterAnimation { Name = "every frame" };
            foreach (var p in f.Pages) foreach (var fr in p.Frames.Keys) all.Frames.Add(Tuple.Create(p, fr));
            f.Animations.Insert(0, all);
        }
    }
}
