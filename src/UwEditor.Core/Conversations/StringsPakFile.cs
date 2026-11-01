using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// STRINGS.PAK for editing: the Huffman tree, the block table and each block's bytes.
    /// A block that was not changed is written back byte for byte; a changed one is encoded
    /// again with the file's own tree (STRINGS.PAK, GAMESTRN.C: a word string count, word
    /// offsets, then the strings, each ending in '|').
    /// </summary>
    public sealed class StringsPakFile
    {
        public string Path { get; private set; }
        byte[] file;
        HuffmanNode[] nodes;
        int tableStart;
        readonly List<int> order = new List<int>();
        readonly Dictionary<int, byte[]> raw = new Dictionary<int, byte[]>();
        readonly Dictionary<int, List<string>> edited = new Dictionary<int, List<string>>();
        readonly Dictionary<int, List<string>> cache = new Dictionary<int, List<string>>();

        public static StringsPakFile Load(string path)
        {
            var f = FromBytes(File.ReadAllBytes(path));
            f.Path = path;
            return f;
        }

        public static StringsPakFile FromBytes(byte[] data)
        {
            var f = new StringsPakFile { file = data };
            using (var ms = new MemoryStream(data))
            {
                var n = StringsPak.ReadHuffmanNodes(ms);
                f.nodes = n.Item1;
                f.tableStart = (int)n.Item2;
                var blocks = StringsPak.ReadStringBlocks(ms, n.Item2).Item1;
                var offsets = blocks.Select(b => (int)b.Offset).Distinct().OrderBy(x => x).ToList();
                foreach (var b in blocks)
                {
                    f.order.Add(b.BlockNumber);
                    int start = (int)b.Offset;
                    int next = offsets.FirstOrDefault(o => o > start);
                    int end = next > start ? next : data.Length;
                    var bytes = new byte[end - start];
                    Array.Copy(data, start, bytes, 0, bytes.Length);
                    f.raw[b.BlockNumber] = bytes;
                }
            }
            return f;
        }

        public IEnumerable<int> Blocks { get { return order; } }
        public bool Has(int block) { return raw.ContainsKey(block) || edited.ContainsKey(block); }
        public bool HasChanges { get { return edited.Count > 0; } }
        public bool IsChanged(int block) { return edited.ContainsKey(block); }

        /// <summary>A copy of a block's strings (an empty list for a block that is not there).</summary>
        public List<string> Get(int block)
        {
            List<string> e;
            if (edited.TryGetValue(block, out e)) return new List<string>(e);
            return Original(block);
        }

        /// <summary>A block's strings as the file has them, without edits.</summary>
        public List<string> Original(int block)
        {
            List<string> e;
            if (cache.TryGetValue(block, out e)) return new List<string>(e);
            byte[] b;
            if (!raw.TryGetValue(block, out b)) return new List<string>();
            using (var ms = new MemoryStream(b))
            {
                int count = b[0] | b[1] << 8;
                var sb = new StringBlock { BlockNumber = block, Offset = 0, StringCount = count, Offsets = new List<int>() };
                for (int i = 0; i < count; i++) sb.Offsets.Add(b[2 + 2 * i] | b[3 + 2 * i] << 8);
                e = StringsPak.ReadStrings(ms, sb, nodes);
            }
            cache[block] = e;
            return new List<string>(e);
        }

        /// <summary>The characters of s the file's tree cannot write.</summary>
        public string Unencodable(string s)
        {
            var bad = new List<char>();
            foreach (char ch in s)
                if (!StringsPak.CanEncode(ch.ToString(), nodes) && !bad.Contains(ch)) bad.Add(ch);
            return new string(bad.ToArray());
        }

        /// <summary>Replaces a block's strings (adds the block when it is new). Throws when a string cannot be written.</summary>
        public void Set(int block, IList<string> strings)
        {
            var list = strings.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                string bad = Unencodable(list[i] ?? "");
                if (bad.Length > 0) throw new InvalidDataException(string.Format("String {0} of block 0x{1:X4} has characters STRINGS.PAK's tree has no code for: {2}", i, block, string.Join(" ", bad.Select(ch => "'" + ch + "'"))));
                if ((list[i] ?? "").Contains("|")) throw new InvalidDataException(string.Format("String {0} of block 0x{1:X4} contains '|', which ends a string.", i, block));
            }
            var old = Original(block);
            if (raw.ContainsKey(block) && old.SequenceEqual(list)) { edited.Remove(block); return; }
            Encode(block, list);   // checks that it fits
            edited[block] = list;
            if (!order.Contains(block))
            {
                int at = order.FindIndex(b => b > block);
                if (at < 0) order.Add(block); else order.Insert(at, block);
            }
        }

        public void Revert(int block) { edited.Remove(block); if (!raw.ContainsKey(block)) order.Remove(block); }

        byte[] Encode(int block, List<string> strings)
        {
            var o = new MemoryStream();
            int count = strings.Count;
            o.WriteByte((byte)count); o.WriteByte((byte)(count >> 8));
            var bodies = new List<byte[]>();
            int at = 0;
            foreach (var s in strings)
            {
                var bits = StringsPak.ConvertString(s ?? "", nodes);
                if (bits == null) throw new InvalidDataException("A string of block 0x" + block.ToString("X4") + " cannot be encoded.");
                var b = bits.Select(x => (byte)x).ToArray();
                if (at > 0xFFFF) throw new InvalidDataException("Block 0x" + block.ToString("X4") + " is over 64 KB, more than its word offsets reach.");
                o.WriteByte((byte)at); o.WriteByte((byte)(at >> 8));
                bodies.Add(b);
                at += b.Length;
            }
            foreach (var b in bodies) o.Write(b, 0, b.Length);
            return o.ToArray();
        }

        public byte[] ToBytes()
        {
            if (edited.Count == 0) return (byte[])file.Clone();
            var o = new MemoryStream();
            o.Write(file, 0, tableStart);
            o.WriteByte((byte)order.Count); o.WriteByte((byte)(order.Count >> 8));
            int pos = tableStart + 2 + 6 * order.Count;
            var data = new List<byte[]>();
            foreach (int b in order)
            {
                List<string> e;
                byte[] bytes = edited.TryGetValue(b, out e) ? Encode(b, e) : raw[b];
                o.WriteByte((byte)b); o.WriteByte((byte)(b >> 8));
                o.WriteByte((byte)pos); o.WriteByte((byte)(pos >> 8)); o.WriteByte((byte)(pos >> 16)); o.WriteByte((byte)(pos >> 24));
                data.Add(bytes);
                pos += bytes.Length;
            }
            foreach (var d in data) o.Write(d, 0, d.Length);
            return o.ToArray();
        }

        /// <summary>Writes the file (the old one kept as STRINGS.PAK.BKP) and makes the saved state the base.</summary>
        public void Save(string path = null)
        {
            path = path ?? Path;
            var bytes = ToBytes();
            SafeFile.Write(path, bytes);
            var again = FromBytes(bytes);
            file = again.file; nodes = again.nodes; tableStart = again.tableStart;
            order.Clear(); order.AddRange(again.order);
            raw.Clear(); foreach (var kv in again.raw) raw[kv.Key] = kv.Value;
            edited.Clear(); cache.Clear();
            Path = path;
        }
    }
}
