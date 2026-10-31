using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UwEditor.Core.Graphics
{
    /// <summary>
    /// A Deluxe Paint LPF animation, as the cutscene player reads it (gfx/CUTS.C): a 0xB00-byte
    /// header ("LPF ", large page count at 6, record count at 8, size at 0x14, last-delta flag at
    /// 0x1A, frame rate at 0x44, the palette at 0x100 as blue, green, red, 0, and 256 large page
    /// descriptors at 0x500: first record, records, bytes), then large pages of 64 KB from 0xB00:
    /// the descriptor again, a word, a size word per record, the records. A record is a byte, a
    /// flag byte (with extra data when set: a word length and that many bytes, even), then a
    /// word: 0 a whole picture, 1 a run/skip/dump delta on the last one. With the last-delta flag
    /// the last record turns the last frame back into the first, for looping.
    ///
    /// The player's limits, which writing keeps to: a page fits in its 64 KB window (bytes +
    /// 2 × records + 8), at most 256 records a page (the size table's room before the script
    /// in the work area), at most 256 pages.
    /// </summary>
    public sealed class LpfAnimation
    {
        public string Path { get; private set; }
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        byte[] header, original;
        public readonly List<byte[]> Records = new List<byte[]>();
        public int Width { get { return U16(header, 0x14); } }
        public int Height { get { return U16(header, 0x16); } }
        public bool LastDelta { get { return header[0x1A] != 0; } }
        public int Rate { get { return Math.Max(1, (int)U16(header, 0x44)); } }
        /// <summary>Frames: the records less the looping one.</summary>
        public int FrameCount { get { return Records.Count - (LastDelta && Records.Count > 0 ? 1 : 0); } }
        public uint[] Palette { get; private set; }
        public bool Changed { get; private set; }
        List<int[]> frames;

        public const int PageBytes = 0x10000, MaxRecordsPerPage = 256, MaxPages = 256;

        public static LpfAnimation Load(string path)
        {
            var a = Parse(File.ReadAllBytes(path));
            a.Path = path;
            return a;
        }

        public static LpfAnimation Parse(byte[] d)
        {
            if (d.Length < 0xB00 || d[0] != 'L' || d[1] != 'P' || d[2] != 'F') throw new InvalidDataException("Not an LPF animation.");
            var a = new LpfAnimation { header = d.Take(0xB00).ToArray(), original = (byte[])d.Clone() };
            int nlps = U16(d, 6);
            var pages = new List<Tuple<int, int, int, int>>();     // page, base, records, bytes
            for (int p = 0; p < nlps && p < 256; p++)
                pages.Add(Tuple.Create(p, U16(d, 0x500 + 6 * p), U16(d, 0x502 + 6 * p), U16(d, 0x504 + 6 * p)));
            foreach (var pg in pages.OrderBy(x => x.Item2))
            {
                int at = 0xB00 + pg.Item1 * PageBytes;
                int n = pg.Item3;
                int data = at + 8 + 2 * n;
                for (int r = 0; r < n; r++)
                {
                    int size = U16(d, at + 8 + 2 * r);
                    var rec = new byte[size];
                    if (data + size <= d.Length) Array.Copy(d, data, rec, 0, size);
                    a.Records.Add(rec);
                    data += size;
                }
            }
            a.Palette = new uint[256];
            for (int i = 0; i < 256; i++)
                a.Palette[i] = 0xFF000000u | (uint)d[0x100 + 4 * i + 2] << 16 | (uint)d[0x100 + 4 * i + 1] << 8 | d[0x100 + 4 * i];
            return a;
        }

        /// <summary>Every frame as palette indexes, decoded in order from a cleared screen.</summary>
        public List<int[]> Frames()
        {
            if (frames != null) return frames;
            var list = new List<int[]>();
            var buf = new byte[Width * Height];
            for (int r = 0; r < FrameCount; r++)
            {
                Apply(Records[r], buf);
                list.Add(buf.Select(b => (int)b).ToArray());
            }
            frames = list;
            return list;
        }

        public IndexedImage Frame(int i)
        {
            var f = Frames()[i];
            return new IndexedImage(Width, Height, (int[])f.Clone(), (uint[])Palette.Clone()) { Type = 4 };
        }

        /// <summary>Where a record's picture data starts: after the byte, the flag and its extra data, then the type word.</summary>
        static int DataStart(byte[] rec)
        {
            if (rec.Length < 2) return rec.Length;
            return rec[1] != 0 ? ((U16(rec, 2) + 1) & ~1) + 4 : 2;
        }

        public static void Apply(byte[] rec, byte[] buf)
        {
            int off = DataStart(rec);
            if (rec.Length == 0 || rec.Length - off <= 0) return;
            int type = rec[off];
            if (type == 0) Array.Copy(rec, off + 2, buf, 0, Math.Min(buf.Length, rec.Length - off - 2));
            else if (type == 1) RunSkipDump(rec, off + 2, buf);
        }

        /// <summary>The delta decoder (UW1 seg002_A, UW2's LPFDELTA.ASM; the classic DPaint "RunSkipDump").</summary>
        static void RunSkipDump(byte[] s, int p, byte[] dst)
        {
            int d = 0;
            while (p < s.Length)
            {
                int cnt = (sbyte)s[p++];
                if (cnt > 0) { for (int k = 0; k < cnt && p < s.Length; k++) Put(dst, d++, s[p++]); continue; }
                if (cnt == 0)
                {
                    int n = s[p++], v = s[p++];
                    for (int k = 0; k < n; k++) Put(dst, d++, (byte)v);
                    continue;
                }
                cnt -= unchecked((sbyte)0x80);   // cnt + 128: the short skip length
                if (cnt != 0) { d += cnt; continue; }
                int w = U16(s, p); p += 2;
                if ((short)w > 0) { d += w; continue; }
                if (w == 0) return;
                w -= 0x8000;
                if (w >= 0x4000)
                {
                    w -= 0x4000;
                    byte v = s[p++];
                    for (int k = 0; k < w; k++) Put(dst, d++, v);
                }
                else for (int k = 0; k < w && p < s.Length; k++) Put(dst, d++, s[p++]);
            }
        }

        static void Put(byte[] dst, int d, byte v) { if (d >= 0 && d < dst.Length) dst[d] = v; }

        /// <summary>A delta record from one frame to the next: runs where 4 or more pixels repeat, dumps elsewhere, skips over what stays.</summary>
        public static byte[] EncodeDelta(int[] from, int[] to)
        {
            var o = new List<byte> { 0x42, 0x00, 0x01, 0x00 };
            int n = to.Length, i = 0;
            while (i < n)
            {
                int skip = 0;
                while (i + skip < n && from[i + skip] == to[i + skip]) skip++;
                if (i + skip >= n) break;
                for (int left = skip; left > 0;)
                {
                    if (left < 0x80) { o.Add((byte)(0x80 | left)); left = 0; }
                    else { int sk = Math.Min(left, 0x7FFF); o.Add(0x80); o.Add((byte)sk); o.Add((byte)(sk >> 8)); left -= sk; }
                }
                i += skip;
                // the changed span: until 4 unchanged pixels in a row (a skip is then cheaper)
                int end = i;
                while (end < n && !(Same(from, to, end, 4))) end++;
                int j = i;
                while (j < end)
                {
                    int run = 1;
                    while (j + run < end && to[j + run] == to[j] && run < 0x3FFF) run++;
                    if (run >= 4)
                    {
                        if (run <= 255) { o.Add(0); o.Add((byte)run); o.Add((byte)to[j]); }
                        else { int w = 0xC000 | run; o.Add(0x80); o.Add((byte)w); o.Add((byte)(w >> 8)); o.Add((byte)to[j]); }
                        j += run;
                        continue;
                    }
                    int k = j;
                    while (k < end && k - j < 0x3FFF)
                    {
                        int r = 1;
                        while (k + r < end && to[k + r] == to[k] && r < 4) r++;
                        if (r >= 4) break;
                        k++;
                    }
                    int len = k - j;
                    if (len < 0x80) o.Add((byte)len);
                    else { int w = 0x8000 | len; o.Add(0x80); o.Add((byte)w); o.Add((byte)(w >> 8)); }
                    for (int q = j; q < k; q++) o.Add((byte)to[q]);
                    j = k;
                }
                i = end;
            }
            o.Add(0x80); o.Add(0); o.Add(0);        // a long op of 0: the end
            return o.ToArray();
        }

        static bool Same(int[] a, int[] b, int i, int n)
        {
            if (i + n > a.Length) n = a.Length - i;
            for (int k = 0; k < n; k++) if (a[i + k] != b[i + k]) return false;
            return true;
        }

        /// <summary>
        /// Replaces frame i. The records that change are this frame's (a delta from the one
        /// before, or a whole picture if it was one), the next one's delta and, for a looping
        /// animation, the delta back to the first frame. Throws when the pages would break the
        /// player's limits; then nothing changes.
        /// </summary>
        public void Replace(int i, int[] pixels)
        {
            var fr = Frames();
            if (i < 0 || i >= fr.Count) throw new ArgumentOutOfRangeException("i");
            if (pixels.Length != Width * Height) throw new ArgumentException("The frame must stay " + Width + " × " + Height + ".");
            var oldRecords = Records.ToList();
            var oldFrame = fr[i];
            fr[i] = (int[])pixels.Clone();
            var blank = new int[Width * Height];
            Func<int, byte[]> encode = r =>
            {
                int[] prev = r == 0 ? blank : fr[r - 1];
                if (r == 0 && Type(Records[0]) == 0)
                {
                    var rec = new byte[4 + Width * Height];
                    rec[0] = 0x42; rec[2] = 0;
                    for (int k = 0; k < Width * Height; k++) rec[4 + k] = (byte)fr[0][k];
                    return rec;
                }
                return EncodeDelta(prev, fr[r]);
            };
            Records[i] = encode(i);
            if (i + 1 < fr.Count) Records[i + 1] = encode(i + 1);
            if (LastDelta && (i == 0 || i == fr.Count - 1)) Records[fr.Count] = EncodeDelta(fr[fr.Count - 1], fr[0]);
            try { Pack(); }
            catch (Exception)
            {
                Records.Clear(); Records.AddRange(oldRecords);
                fr[i] = oldFrame;
                throw;
            }
            Changed = true;
        }

        static int Type(byte[] rec)
        {
            int off = DataStart(rec);
            return off < rec.Length ? rec[off] : -1;
        }

        /// <summary>The records laid out in pages, greedily, within the player's limits.</summary>
        List<List<int>> Pack()
        {
            var pages = new List<List<int>>();
            var cur = new List<int>();
            int bytes = 0;
            for (int r = 0; r < Records.Count; r++)
            {
                int size = Records[r].Length;
                if (size + 8 + 2 > PageBytes) throw new InvalidDataException(string.Format("Frame {0} alone needs {1} bytes, more than a 64 KB page of the cutscene player.", r, size));
                if (cur.Count > 0 && (bytes + size + 8 + 2 * (cur.Count + 1) > PageBytes || cur.Count >= MaxRecordsPerPage))
                {
                    pages.Add(cur); cur = new List<int>(); bytes = 0;
                }
                cur.Add(r); bytes += size;
            }
            if (cur.Count > 0) pages.Add(cur);
            if (pages.Count > MaxPages) throw new InvalidDataException("The animation needs more than 256 large pages.");
            return pages;
        }

        /// <summary>The file: as it was until a frame is replaced, then laid out again.</summary>
        public byte[] ToBytes()
        {
            if (!Changed) return (byte[])original.Clone();
            var pages = Pack();
            var h = (byte[])header.Clone();
            W16(h, 6, pages.Count);
            W16(h, 4, Math.Max(U16(header, 4), pages.Count));
            W16(h, 0xC, Math.Max(U16(header, 0xC), pages.Max(p => p.Count)));
            for (int p = 0; p < 256; p++) { W16(h, 0x500 + 6 * p, 0); W16(h, 0x502 + 6 * p, 0); W16(h, 0x504 + 6 * p, 0); }
            var o = new MemoryStream();
            o.Write(h, 0, h.Length);
            for (int p = 0; p < pages.Count; p++)
            {
                var recs = pages[p];
                int nbytes = recs.Sum(r => Records[r].Length);
                W16(h, 0x500 + 6 * p, recs[0]); W16(h, 0x502 + 6 * p, recs.Count); W16(h, 0x504 + 6 * p, nbytes);
                var page = new MemoryStream();
                var desc = new byte[8];
                W16(desc, 0, recs[0]); W16(desc, 2, recs.Count); W16(desc, 4, nbytes);
                page.Write(desc, 0, 8);
                foreach (int r in recs) { page.WriteByte((byte)Records[r].Length); page.WriteByte((byte)(Records[r].Length >> 8)); }
                foreach (int r in recs) page.Write(Records[r], 0, Records[r].Length);
                var pb = page.ToArray();
                o.Write(pb, 0, pb.Length);
                // every page but the last fills its 64 KB
                if (p + 1 < pages.Count) o.Write(new byte[PageBytes - pb.Length], 0, PageBytes - pb.Length);
            }
            var r2 = o.ToArray();
            Array.Copy(h, r2, h.Length);
            return r2;
        }

        public void Save(string path = null)
        {
            path = path ?? Path;
            var b = ToBytes();
            Conversations.SafeFile.Write(path, b);
            original = b;
            header = b.Take(0xB00).ToArray();
            Changed = false;
        }

        static int U16(byte[] d, int p) { return p + 1 < d.Length ? d[p] | d[p + 1] << 8 : 0; }
        static void W16(byte[] d, int p, int v) { d[p] = (byte)v; d[p + 1] = (byte)(v >> 8); }
    }

    /// <summary>A cutscene: CUTS\csNNN.n00 (the script) and csNNN.n01 on (the animations), NNN and the extension's digits in octal (gfx/CUTS.C).</summary>
    public sealed class Cutscene
    {
        public int Number;
        public string Script;
        public readonly List<string> Animations = new List<string>();
        public string Label { get { return string.Format("Cutscene {0} (cs{1}), {2} animation{3}", Number, Convert.ToString(Number, 8).PadLeft(3, '0'), Animations.Count, Animations.Count == 1 ? "" : "s"); } }
        public override string ToString() { return Label; }

        public static List<Cutscene> Find(string gameFolder)
        {
            var list = new List<Cutscene>();
            string cuts = Directory.GetDirectories(gameFolder).FirstOrDefault(d => string.Equals(System.IO.Path.GetFileName(d), "CUTS", StringComparison.OrdinalIgnoreCase));
            if (cuts == null) return list;
            var files = Directory.GetFiles(cuts).Select(System.IO.Path.GetFileName)
                .Where(f => f.Length == 9 && f.StartsWith("CS", StringComparison.OrdinalIgnoreCase) && f[5] == '.' && char.ToUpperInvariant(f[6]) == 'N').ToList();
            foreach (var g in files.GroupBy(f => f.Substring(2, 3)).OrderBy(g => g.Key))
            {
                int n;
                try { n = Convert.ToInt32(g.Key, 8); } catch (Exception) { continue; }
                var c = new Cutscene { Number = n };
                foreach (var f in g.OrderBy(x => Convert.ToInt32(x.Substring(7, 2), 8)))
                {
                    string full = System.IO.Path.Combine(cuts, f);
                    if (f.Substring(7, 2) == "00") c.Script = full; else c.Animations.Add(full);
                }
                list.Add(c);
            }
            return list;
        }
    }
}
