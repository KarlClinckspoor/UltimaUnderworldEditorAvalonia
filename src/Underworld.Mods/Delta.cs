using System;
using System.Collections.Generic;
using System.IO;
using UltimateEditor.Games;

namespace Underworld.Mods
{
    /// <summary>
    /// The difference between an original file and an edited one (docs/mod-format.md, "The
    /// delta"): COPY runs of the original, ADD the new bytes, and for UW2 archives BLOCK, a block
    /// of the original decompressed, changed by a nested delta and compressed again. A delta
    /// applies to one exact original; reading outside it, or giving a result of another length,
    /// is refused.
    /// </summary>
    public static class Delta
    {
        const byte OpEnd = 0x00, OpCopy = 0x01, OpAdd = 0x02, OpBlock = 0x03;
        const byte BlockPlain = 0, BlockLzss = 1;
        static readonly byte[] Magic = { (byte)'U', (byte)'W', (byte)'D', (byte)'2' };

        /// <summary>Hashed window: a COPY found through the index matches at least this many bytes.</summary>
        const int Window = 16;
        /// <summary>A COPY that continues the previous one's displacement (bytes changed in place) needs only this many.</summary>
        const int AlignedMin = 4;
        const int MaxCandidates = 64;

        // ---- making

        /// <summary>
        /// The delta from <paramref name="original"/> to <paramref name="edited"/>. With
        /// <paramref name="uw2Archive"/>, the blocks the game stores compressed are diffed
        /// decompressed (BLOCK) when compressing the result again gives exactly the edited bytes.
        /// The delta is checked: applying it gives <paramref name="edited"/>, or this throws.
        /// </summary>
        public static byte[] Make(byte[] original, byte[] edited, bool uw2Archive = false)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (edited == null) throw new ArgumentNullException(nameof(edited));
            var w = new Writer();
            w.Bytes(Magic);
            w.U(original.Length);
            w.U(edited.Length);
            var blocks = uw2Archive ? Uw2Blocks(original, edited) : new List<BlockRegion>();
            var index = new Index(original);
            long prevEnd = 0;
            int at = 0;
            foreach (var r in blocks)
            {
                Ops(index, edited, at, r.Start, w, ref prevEnd);
                w.Byte(OpBlock);
                w.U(r.Block);
                w.Byte(r.Mode);
                var inner = new Index(r.OriginalContent);
                long innerPrev = 0;
                Ops(inner, r.EditedContent, 0, r.EditedContent.Length, w, ref innerPrev);
                w.Byte(OpEnd);
                at = r.End;
            }
            Ops(index, edited, at, edited.Length, w, ref prevEnd);
            w.Byte(OpEnd);
            var delta = w.ToArray();
            var check = Apply(original, delta);
            if (!Same(check, edited)) throw new InvalidOperationException("The delta does not rebuild the edited file (a bug in Delta.Make).");
            return delta;
        }

        /// <summary>COPY and ADD for edited[start, end) against the index's file.</summary>
        static void Ops(Index index, byte[] edited, int start, int end, Writer w, ref long prevEnd)
        {
            byte[] src = index.Source;
            int i = start, addStart = start;
            // where the last COPY ended, in both files: the next bytes are tried there first
            long alignSrc = prevEnd;
            int alignDst = start;
            while (i < end)
            {
                int bestLen = 0, bestBack = 0;
                long bestSrc = -1;
                // the same displacement as the last COPY: bytes changed in place
                long a = alignSrc + (i - alignDst);
                if (a >= 0 && a < src.Length)
                {
                    int l = MatchLength(src, (int)a, edited, i, end);
                    if (l >= AlignedMin) { bestLen = l; bestSrc = a; }
                }
                if (i + Window <= end && bestLen < 4 * Window)
                {
                    foreach (int c in index.Candidates(edited, i))
                    {
                        int l = MatchLength(src, c, edited, i, end);
                        if (l < Window) continue;
                        int back = 0;
                        while (i - back > addStart && c - back > 0 && edited[i - back - 1] == src[c - back - 1]) back++;
                        if (l + back > bestLen + bestBack) { bestLen = l; bestBack = back; bestSrc = c; }
                    }
                }
                if (bestSrc < 0) { i++; continue; }
                int copyDst = i - bestBack;
                long copySrc = bestSrc - bestBack;
                int copyLen = bestLen + bestBack;
                if (copyDst > addStart)
                {
                    w.Byte(OpAdd);
                    w.U(copyDst - addStart);
                    w.Bytes(edited, addStart, copyDst - addStart);
                }
                w.Byte(OpCopy);
                w.Z(copySrc - prevEnd);
                w.U(copyLen);
                prevEnd = copySrc + copyLen;
                i = copyDst + copyLen;
                addStart = i;
                alignSrc = prevEnd;
                alignDst = i;
            }
            if (end > addStart)
            {
                w.Byte(OpAdd);
                w.U(end - addStart);
                w.Bytes(edited, addStart, end - addStart);
            }
        }

        static int MatchLength(byte[] src, int s, byte[] dst, int d, int end)
        {
            int n = 0;
            while (s + n < src.Length && d + n < end && src[s + n] == dst[d + n]) n++;
            return n;
        }

        static bool Same(byte[] a, byte[] b)
        {
            return a.AsSpan().SequenceEqual(b);
        }

        /// <summary>The original's 16-byte windows by hash, for finding where edited bytes came from.</summary>
        sealed class Index
        {
            public readonly byte[] Source;
            readonly int[] head, next;
            readonly int mask;

            public Index(byte[] src)
            {
                Source = src;
                int bits = 10;
                while (bits < 22 && (1 << bits) < src.Length) bits++;
                mask = (1 << bits) - 1;
                head = new int[1 << bits];
                for (int k = 0; k < head.Length; k++) head[k] = -1;
                int n = Math.Max(0, src.Length - Window + 1);
                next = new int[n];
                for (int p = 0; p < n; p++)
                {
                    int h = Hash(src, p);
                    next[p] = head[h];
                    head[h] = p;
                }
            }

            int Hash(byte[] b, int p)
            {
                ulong x = BitConverter.ToUInt64(b, p), y = BitConverter.ToUInt64(b, p + 8);
                ulong h = x * 0x9E3779B97F4A7C15UL ^ (y + 0x632BE59BD9B4E019UL) * 0xC2B2AE3D27D4EB4FUL;
                return (int)(h >> 40) & mask;
            }

            public IEnumerable<int> Candidates(byte[] dst, int p)
            {
                if (next.Length == 0) yield break;
                int c = head[Hash(dst, p)], k = 0;
                while (c >= 0 && k++ < MaxCandidates) { yield return c; c = next[c]; }
            }
        }

        // ---- UW2 archives

        sealed class BlockRegion
        {
            public int Block, Start, End;
            public byte Mode;
            public byte[] OriginalContent, EditedContent;
        }

        /// <summary>
        /// The edited archive's compressed blocks worth a BLOCK: the original has the block with
        /// other stored bytes, and compressing the edited content again gives exactly the stored
        /// bytes. In file order, not overlapping.
        /// </summary>
        static List<BlockRegion> Uw2Blocks(byte[] original, byte[] edited)
        {
            var list = new List<BlockRegion>();
            if (!Uw2Layout.IsValid(original) || !Uw2Layout.IsValid(edited)) return list;
            var a = new Uw2Archive(original);
            var b = new Uw2Archive(edited);
            for (int k = 0; k < b.Count; k++)
            {
                if (!b.Has(k) || !b.Compressed(k) || k >= a.Count || !a.Has(k)) continue;
                byte[] stored = b.ReadStored(k);
                byte[] old = a.ReadStored(k);
                if (old.AsSpan().SequenceEqual(stored)) continue;
                byte[] content, oldContent;
                try { content = b.Read(k); oldContent = a.Read(k); }
                catch (Exception) { continue; }
                byte[] again = Lzss.EncodeBlock(content);
                if (again == null || !again.AsSpan().SequenceEqual(stored)) continue;
                list.Add(new BlockRegion { Block = k, Start = (int)b.Offset(k), End = (int)b.Offset(k) + stored.Length, Mode = BlockLzss, OriginalContent = oldContent, EditedContent = content });
            }
            list.Sort((x, y) => x.Start.CompareTo(y.Start));
            var kept = new List<BlockRegion>();
            int last = 0;
            foreach (var r in list)
            {
                if (r.Start < last || r.Start < Uw2Layout.HeaderSize(edited)) continue;
                kept.Add(r);
                last = r.End;
            }
            return kept;
        }

        // ---- applying

        /// <summary>The edited file: <paramref name="delta"/> applied to <paramref name="original"/>. Throws InvalidDataException when the delta is damaged or made from another file.</summary>
        public static byte[] Apply(byte[] original, byte[] delta)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (delta == null) throw new ArgumentNullException(nameof(delta));
            var r = new Reader(delta);
            for (int k = 0; k < Magic.Length; k++)
                if (r.Byte() != Magic[k]) throw new InvalidDataException("Not a delta (UWD2).");
            long baseLen = r.U(), targetLen = r.U();
            if (baseLen != original.Length) throw new InvalidDataException("The delta was made from a file of " + baseLen + " bytes, not " + original.Length + ".");
            if (targetLen > int.MaxValue) throw new InvalidDataException("The delta's result is too large.");
            var output = new byte[targetLen];
            int pos = 0;
            Run(r, original, output, ref pos, true);
            if (!r.AtEnd) throw new InvalidDataException("The delta has bytes after its end.");
            if (pos != output.Length) throw new InvalidDataException("The delta gives " + pos + " bytes, not the " + targetLen + " it promises.");
            return output;
        }

        /// <summary>The ops up to their end, writing into output. BLOCK only at the top level.</summary>
        static void Run(Reader r, byte[] src, byte[] output, ref int pos, bool top)
        {
            long prevEnd = 0;
            while (true)
            {
                byte op = r.Byte();
                if (op == OpEnd) return;
                if (op == OpCopy)
                {
                    long off = prevEnd + r.Z(), len = r.U();
                    if (off < 0 || len < 0 || off + len > src.Length) throw new InvalidDataException("The delta copies from outside the original.");
                    if (pos + len > output.Length) throw new InvalidDataException("The delta writes past its promised length.");
                    Array.Copy(src, off, output, pos, len);
                    pos += (int)len;
                    prevEnd = off + len;
                }
                else if (op == OpAdd)
                {
                    long len = r.U();
                    if (pos + len > output.Length) throw new InvalidDataException("The delta writes past its promised length.");
                    r.Bytes(output, pos, (int)len);
                    pos += (int)len;
                }
                else if (op == OpBlock && top)
                {
                    long block = r.U();
                    byte mode = r.Byte();
                    if (!Uw2Layout.IsValid(src)) throw new InvalidDataException("The delta reads a block, but the original is not a UW2 archive.");
                    var a = new Uw2Archive(src);
                    if (block >= a.Count || !a.Has((int)block)) throw new InvalidDataException("The delta reads block " + block + ", which the original lacks.");
                    byte[] content;
                    try { content = a.Read((int)block); }
                    catch (Exception e) { throw new InvalidDataException("Block " + block + " of the original cannot be read: " + e.Message); }
                    // the nested delta's length is unknown until it ends: run it into a growing buffer, capped by what is left
                    var inner = new GrowingOutput(output.Length - pos);
                    RunInto(r, content, inner);
                    byte[] edited = inner.ToArray();
                    byte[] stored;
                    if (mode == BlockLzss) stored = Lzss.EncodeBlock(edited) ?? throw new InvalidDataException("Block " + block + " no longer compresses.");
                    else if (mode == BlockPlain) stored = edited;
                    else throw new InvalidDataException("Unknown block mode " + mode + ".");
                    if (pos + stored.Length > output.Length) throw new InvalidDataException("The delta writes past its promised length.");
                    Array.Copy(stored, 0, output, pos, stored.Length);
                    pos += stored.Length;
                }
                else throw new InvalidDataException("Unknown delta op 0x" + op.ToString("X2") + ".");
            }
        }

        /// <summary>
        /// The bytes a delta adds (its ADD ops), with the block they go into for those inside a
        /// BLOCK (-1 outside): what a mod carries of its own. For checking that a package holds
        /// nothing of the original files.
        /// </summary>
        public static List<(int Block, byte[] Bytes)> Added(byte[] delta)
        {
            var r = new Reader(delta);
            for (int k = 0; k < Magic.Length; k++)
                if (r.Byte() != Magic[k]) throw new InvalidDataException("Not a delta (UWD2).");
            r.U(); r.U();
            var list = new List<(int, byte[])>();
            void Ops(int block)
            {
                while (true)
                {
                    byte op = r.Byte();
                    if (op == OpEnd) return;
                    if (op == OpCopy) { r.Z(); r.U(); }
                    else if (op == OpAdd) list.Add((block, r.Take((int)r.U())));
                    else if (op == OpBlock && block < 0) { int b = (int)r.U(); r.Byte(); Ops(b); }
                    else throw new InvalidDataException("Unknown delta op 0x" + op.ToString("X2") + ".");
                }
            }
            Ops(-1);
            return list;
        }

        /// <summary>A nested delta (COPY and ADD only) into a buffer of unknown length.</summary>
        static void RunInto(Reader r, byte[] src, GrowingOutput o)
        {
            long prevEnd = 0;
            while (true)
            {
                byte op = r.Byte();
                if (op == OpEnd) return;
                if (op == OpCopy)
                {
                    long off = prevEnd + r.Z(), len = r.U();
                    if (off < 0 || len < 0 || off + len > src.Length) throw new InvalidDataException("The delta copies from outside the original block.");
                    o.Write(src, (int)off, (int)len);
                    prevEnd = off + len;
                }
                else if (op == OpAdd)
                {
                    long len = r.U();
                    if (len > int.MaxValue) throw new InvalidDataException("The delta's block is too large.");
                    o.Write(r.Take((int)len), 0, (int)len);
                }
                else throw new InvalidDataException("Op 0x" + op.ToString("X2") + " is not allowed inside a block.");
            }
        }

        sealed class GrowingOutput
        {
            readonly MemoryStream ms = new MemoryStream();
            readonly long cap;
            // a compressed block is shorter than its content, so the content's cap is generous
            public GrowingOutput(long left) { cap = Math.Max(1L << 20, left * 16); }
            public void Write(byte[] b, int off, int len)
            {
                if (ms.Length + len > cap) throw new InvalidDataException("The delta's block grows past any sensible size.");
                ms.Write(b, off, len);
            }
            public byte[] ToArray() { return ms.ToArray(); }
        }

        // ---- numbers

        sealed class Writer
        {
            readonly MemoryStream ms = new MemoryStream();
            public void Byte(byte b) { ms.WriteByte(b); }
            public void Bytes(byte[] b) { ms.Write(b, 0, b.Length); }
            public void Bytes(byte[] b, int off, int len) { ms.Write(b, off, len); }
            public void U(long v)
            {
                if (v < 0) throw new ArgumentOutOfRangeException(nameof(v));
                UL((ulong)v);
            }
            /// <summary>Signed, zigzag-coded: 0, -1, 1, -2… as 0, 1, 2, 3…</summary>
            public void Z(long v) { UL(((ulong)v << 1) ^ (ulong)(v >> 63)); }
            void UL(ulong u)
            {
                while (u >= 0x80) { ms.WriteByte((byte)(u | 0x80)); u >>= 7; }
                ms.WriteByte((byte)u);
            }
            public byte[] ToArray() { return ms.ToArray(); }
        }

        sealed class Reader
        {
            readonly byte[] b;
            int p;
            public Reader(byte[] data) { b = data; }
            public bool AtEnd { get { return p == b.Length; } }
            public byte Byte()
            {
                if (p >= b.Length) throw new InvalidDataException("The delta ends early.");
                return b[p++];
            }
            public long U()
            {
                ulong v = UL();
                if (v > int.MaxValue) throw new InvalidDataException("A number in the delta is too large.");
                return (long)v;
            }
            public long Z()
            {
                ulong u = UL();
                long v = (long)(u >> 1) ^ -(long)(u & 1);
                if (v > int.MaxValue || v < -(long)int.MaxValue) throw new InvalidDataException("A number in the delta is too large.");
                return v;
            }
            ulong UL()
            {
                ulong v = 0;
                for (int shift = 0; ; shift += 7)
                {
                    if (shift > 63) throw new InvalidDataException("A number in the delta is too long.");
                    byte x = Byte();
                    v |= (ulong)(x & 0x7F) << shift;
                    if ((x & 0x80) == 0) return v;
                }
            }
            public void Bytes(byte[] dst, int off, int len)
            {
                if (len < 0 || p + len > b.Length) throw new InvalidDataException("The delta ends early.");
                Array.Copy(b, p, dst, off, len);
                p += len;
            }
            public byte[] Take(int len)
            {
                var d = new byte[len];
                Bytes(d, 0, len);
                return d;
            }
        }
    }

    /// <summary>Checks that bytes are a UW2 archive whose tables and blocks lie inside the file.</summary>
    internal static class Uw2Layout
    {
        public static int HeaderSize(byte[] f) { return 6 + 16 * (f[0] | f[1] << 8); }

        public static bool IsValid(byte[] f)
        {
            if (f == null || f.Length < 6) return false;
            int n = f[0] | f[1] << 8;
            if (n == 0 || HeaderSize(f) > f.Length) return false;
            for (int k = 0; k < n; k++)
            {
                uint off = U32(f, 6 + 4 * k), len = U32(f, 6 + 8 * n + 4 * k);
                if (off == 0) continue;
                if (off < HeaderSize(f) || (ulong)off + len > (ulong)f.Length) return false;
            }
            return true;
        }

        static uint U32(byte[] f, int p) { return (uint)(f[p] | f[p + 1] << 8 | f[p + 2] << 16 | f[p + 3] << 24); }
    }
}
