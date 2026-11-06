using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UwEditor.TestSupport
{
    /// <summary>
    /// Writes a small ISO 9660 image, as GOG's game.gog is: the game folders of the test data
    /// under UW and UW2, so the tests need no GOG install. Only what the reader needs: the primary
    /// volume descriptor, the folders' records (both byte orders) and the files.
    /// </summary>
    public static class FakeGogImage
    {
        /// <summary>An image of the given game folders, each under its name on the disc (e.g. "UW" → tests/gamedata/UW).</summary>
        public static void FromFolders(string image, IDictionary<string, string> discFolderToSource)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var kv in discFolderToSource)
                foreach (var f in Directory.EnumerateFiles(kv.Value, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(kv.Value, f).Replace('\\', '/').ToUpperInvariant();
                    if (rel.StartsWith("SAVE")) continue;     // saved games are not on the CD
                    files[kv.Key + "/" + rel] = File.ReadAllBytes(f);
                }
            Write(image, files);
        }

        sealed class Dir
        {
            public string Name;
            public Dir Parent;
            public SortedDictionary<string, Dir> Dirs = new SortedDictionary<string, Dir>(StringComparer.Ordinal);
            public SortedDictionary<string, byte[]> Files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            public Dictionary<string, long> FileLba = new Dictionary<string, long>();
            public long Lba, Size;
        }

        const int Sector = 2048;

        static int RecLen(int nameLen) { return 33 + nameLen + (nameLen % 2 == 0 ? 1 : 0); }

        /// <summary>An image holding these files (paths with '/').</summary>
        public static void Write(string image, IDictionary<string, byte[]> files)
        {
            var root = new Dir { Name = "" };
            root.Parent = root;
            foreach (var kv in files)
            {
                var parts = kv.Key.Split('/');
                var d = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!d.Dirs.TryGetValue(parts[i], out var c)) d.Dirs[parts[i]] = c = new Dir { Name = parts[i], Parent = d };
                    d = c;
                }
                d.Files[parts[^1]] = kv.Value;
            }
            var all = new List<Dir>();
            var queue = new Queue<Dir>(); queue.Enqueue(root);
            while (queue.Count > 0) { var d = queue.Dequeue(); all.Add(d); foreach (var c in d.Dirs.Values) queue.Enqueue(c); }

            long next = 18;
            foreach (var d in all)
            {
                var lens = new List<int> { RecLen(1), RecLen(1) };
                lens.AddRange(d.Dirs.Keys.Select(n => RecLen(n.Length)));
                lens.AddRange(d.Files.Keys.Select(n => RecLen(n.Length + 2)));
                int sectors = 1, used = 0;
                foreach (var l in lens) { if (used + l > Sector) { sectors++; used = 0; } used += l; }
                d.Lba = next; d.Size = sectors * Sector; next += sectors;
            }
            foreach (var d in all)
                foreach (var f in d.Files) { d.FileLba[f.Key] = next; next += (f.Value.Length + Sector - 1) / Sector; }

            var img = new byte[next * Sector];
            // primary volume descriptor, then the terminator
            int p = 16 * Sector;
            img[p] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(img, p + 1); img[p + 6] = 1;
            Encoding.ASCII.GetBytes("UW12".PadRight(32)).CopyTo(img, p + 40);
            Both32(img, p + 80, next);
            Record(img, p + 156, root.Lba, root.Size, true, new byte[] { 0 });
            int t = 17 * Sector;
            img[t] = 255; Encoding.ASCII.GetBytes("CD001").CopyTo(img, t + 1); img[t + 6] = 1;

            foreach (var d in all)
            {
                int pos = (int)(d.Lba * Sector), end = pos + (int)d.Size, sectorEnd = pos + Sector;
                void Add(long lba, long size, bool dir, byte[] name)
                {
                    int len = RecLen(name.Length);
                    if (pos + len > sectorEnd) { pos = sectorEnd; sectorEnd += Sector; }
                    Record(img, pos, lba, size, dir, name);
                    pos += len;
                }
                Add(d.Lba, d.Size, true, new byte[] { 0 });
                Add(d.Parent.Lba, d.Parent.Size, true, new byte[] { 1 });
                foreach (var c in d.Dirs.Values) Add(c.Lba, c.Size, true, Encoding.ASCII.GetBytes(c.Name));
                foreach (var f in d.Files) Add(d.FileLba[f.Key], f.Value.Length, false, Encoding.ASCII.GetBytes(f.Key + ";1"));
                foreach (var f in d.Files) f.Value.CopyTo(img, d.FileLba[f.Key] * Sector);
            }
            File.WriteAllBytes(image, img);
        }

        static void Record(byte[] b, int at, long lba, long size, bool dir, byte[] name)
        {
            b[at] = (byte)RecLen(name.Length);
            Both32(b, at + 2, lba);
            Both32(b, at + 10, size);
            b[at + 25] = (byte)(dir ? 2 : 0);
            b[at + 28] = 1; b[at + 31] = 1;     // volume sequence number 1, both byte orders
            b[at + 32] = (byte)name.Length;
            name.CopyTo(b, at + 33);
        }

        static void Both32(byte[] b, int at, long v)
        {
            uint u = (uint)v;
            b[at] = (byte)u; b[at + 1] = (byte)(u >> 8); b[at + 2] = (byte)(u >> 16); b[at + 3] = (byte)(u >> 24);
            b[at + 4] = (byte)(u >> 24); b[at + 5] = (byte)(u >> 16); b[at + 6] = (byte)(u >> 8); b[at + 7] = (byte)u;
        }
    }
}
