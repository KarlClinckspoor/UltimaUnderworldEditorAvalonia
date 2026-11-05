using System.IO;
using UltimateEditor.Games;

namespace UwModManager.Core;

/// <summary>
/// The executables UltimaHacks and Underworld Exhumed are made for: the GOG releases (Exhumed's
/// uwN/src/port/mem/fardata.c checks the size and CRC-32; UltimaHacks' hacks are for vF1.94S and
/// vF1.99S).
/// </summary>
public static class ExeIdentity
{
    public const int Uw1Size = 547248, Uw2Size = 675184;
    public const uint Uw1Crc = 0xF2BF2527u, Uw2Crc = 0xC824FB97u;

    public static bool IsGogOriginal(byte[]? exe, GameKind game)
    {
        int size = game == GameKind.UW2 ? Uw2Size : Uw1Size;
        uint crc = game == GameKind.UW2 ? Uw2Crc : Uw1Crc;
        return exe != null && exe.Length == size && Crc32(exe) == crc;
    }

    public static string ExeName(GameKind game) => game == GameKind.UW2 ? "UW2.EXE" : "UW.EXE";

    static readonly uint[] Table = MakeTable();

    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>CRC-32 (the zip/PNG one).</summary>
    public static uint Crc32(byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// An UltimaHacks hack file (compiled-hacks/uw1.hack, uw2.hack; MIT licence, John Glassmyer):
/// a protobuf Hack message (patcher-site/hack.proto) holding a list of edits to the original
/// GOG executable and the length that executable must have. Applied the way the Hack Applier
/// site does it: in order, an insert opens zero bytes, a copy copies within the file, an
/// overwrite writes bytes. (From the Unity editor's Modding/Exe/UltimaHack.cs.)
/// </summary>
public sealed class UltimaHack
{
    public enum EditKind { Insert, Copy, Overwrite }

    public sealed class Edit
    {
        public EditKind Kind;
        public int Start, Length, Source, Destination;
        public byte[] Data = Array.Empty<byte>();
    }

    public readonly List<Edit> Edits = new();
    /// <summary>The length the executable must have before the hack (0: not given).</summary>
    public int TargetLength;
    public string? Comment;

    public static UltimaHack Parse(byte[] proto)
    {
        var h = new UltimaHack();
        var r = new Proto(proto, 0, proto.Length);
        while (r.More)
        {
            r.Key(out int field, out int wire);
            if (field == 1 && wire == 2) h.Edits.Add(ReadEdit(r.Sub()));
            else if (field == 2 && wire == 2) { var w = r.Sub(); while (w.More) { w.Key(out int f, out int t); if (f == 1 && t == 0) h.TargetLength = (int)w.Varint(); else w.Skip(t); } }
            else if (field == 3 && wire == 2) { var w = r.Sub(); while (w.More) { w.Key(out int f, out int t); if (f == 1 && t == 2) h.Comment = System.Text.Encoding.UTF8.GetString(w.Bytes()); else w.Skip(t); } }
            else r.Skip(wire);
        }
        return h;
    }

    static Edit ReadEdit(Proto r)
    {
        Edit? e = null;
        while (r.More)
        {
            r.Key(out int field, out int wire);
            if (wire != 2) { r.Skip(wire); continue; }
            var m = r.Sub();
            if (field < 1 || field > 3) throw new InvalidDataException("unknown edit type " + field + " in the hack");
            e = new Edit { Kind = field == 1 ? EditKind.Insert : field == 2 ? EditKind.Copy : EditKind.Overwrite };
            while (m.More)
            {
                m.Key(out int f, out int t);
                if (e.Kind == EditKind.Overwrite && f == 2 && t == 2) { e.Data = m.Bytes(); continue; }
                if (t != 0) { m.Skip(t); continue; }
                int v = (int)m.Varint();
                if (e.Kind == EditKind.Insert) { if (f == 1) e.Start = v; else if (f == 2) e.Length = v; }
                else if (e.Kind == EditKind.Copy) { if (f == 1) e.Source = v; else if (f == 2) e.Destination = v; else if (f == 3) e.Length = v; }
                else if (f == 1) e.Start = v;
            }
        }
        return e ?? throw new InvalidDataException("an empty edit in the hack");
    }

    /// <summary>The hacked executable. Refuses an executable of another length than the hack's target.</summary>
    public byte[] Apply(byte[] exe)
    {
        if (TargetLength != 0 && exe.Length != TargetLength)
            throw new InvalidDataException(string.Format("the executable is {0} bytes; this hack is for one of {1} bytes (the GOG release)", exe.Length, TargetLength));
        byte[] cur = (byte[])exe.Clone();
        foreach (var e in Edits)
        {
            switch (e.Kind)
            {
                case EditKind.Insert:
                {
                    var n = new byte[cur.Length + e.Length];
                    Buffer.BlockCopy(cur, 0, n, 0, e.Start);
                    Buffer.BlockCopy(cur, e.Start, n, e.Start + e.Length, cur.Length - e.Start);
                    cur = n;
                    break;
                }
                case EditKind.Copy:
                    Buffer.BlockCopy(cur, e.Source, cur, e.Destination, e.Length);   // BlockCopy handles overlap like copyWithin
                    break;
                default:
                    Buffer.BlockCopy(e.Data, 0, cur, e.Start, e.Data.Length);
                    break;
            }
        }
        return cur;
    }

    /// <summary>True when <paramref name="exe"/> already has the hack (it was hacked before the
    /// manager saw it): the hacked length, and the bytes of every overwrite that no later edit moves.</summary>
    public bool IsAppliedTo(byte[]? exe)
    {
        if (exe == null || TargetLength <= 0) return false;
        int len = TargetLength;
        foreach (var e in Edits) if (e.Kind == EditKind.Insert) len += e.Length;
        if (exe.Length != len) return false;
        int checkedAny = 0;
        for (int i = 0; i < Edits.Count; i++)
        {
            var e = Edits[i];
            if (e.Kind != EditKind.Overwrite) continue;
            bool later = false;
            for (int j = i + 1; j < Edits.Count && !later; j++)
            {
                var f = Edits[j];
                if (f.Kind == EditKind.Insert) later = f.Start <= e.Start + e.Data.Length;
                else if (f.Kind == EditKind.Copy) later = f.Destination < e.Start + e.Data.Length && f.Destination + f.Length > e.Start;
                else later = f.Start < e.Start + e.Data.Length && f.Start + f.Data.Length > e.Start;
            }
            if (later) continue;
            if (e.Start + e.Data.Length > exe.Length) return false;
            for (int k = 0; k < e.Data.Length; k++) if (exe[e.Start + k] != e.Data[k]) return false;
            checkedAny++;
        }
        return checkedAny > 0;
    }

    sealed class Proto
    {
        readonly byte[] b; int p; readonly int end;
        public Proto(byte[] b, int p, int end) { this.b = b; this.p = p; this.end = end; }
        public bool More => p < end;
        public ulong Varint()
        {
            ulong v = 0; int s = 0;
            while (true)
            {
                if (p >= end) throw new InvalidDataException("truncated hack file");
                byte c = b[p++]; v |= (ulong)(c & 0x7F) << s; s += 7;
                if (c < 0x80) return v;
            }
        }
        public void Key(out int field, out int wire) { ulong k = Varint(); field = (int)(k >> 3); wire = (int)(k & 7); }
        public byte[] Bytes() { int n = (int)Varint(); if (p + n > end) throw new InvalidDataException("truncated hack file"); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); p += n; return r; }
        public Proto Sub() { int n = (int)Varint(); if (p + n > end) throw new InvalidDataException("truncated hack file"); var r = new Proto(b, p, p + n); p += n; return r; }
        public void Skip(int wire)
        {
            if (wire == 0) Varint();
            else if (wire == 1) p += 8;
            else if (wire == 2) { int n = (int)Varint(); p += n; }
            else if (wire == 5) p += 4;
            else throw new InvalidDataException("unsupported protobuf wire type " + wire);
        }
    }
}
