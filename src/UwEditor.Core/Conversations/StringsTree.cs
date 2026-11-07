using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.Core.Conversations
{
    /// <summary>
    /// A new Huffman tree for STRINGS.PAK, for when a string needs a character the file's tree
    /// has no code for. The game reads whatever tree the file has (uw1 ui/GAMESTRN.C: the node
    /// count, farmalloc(nodes &lt;&lt; 2), the root the last node, a node whose left child is 0xFF a
    /// leaf). Children are bytes and 0xFF means none, so a tree has at most 255 nodes: 128
    /// characters, the terminator '|' among them. Laid out as the shipped trees are: the leaves
    /// first in character order, then the inner nodes, the root last.
    /// </summary>
    public static class StringsTree
    {
        public const int MaxCharacters = 128;

        /// <summary>The characters a string may not hold: '|' ends a string and 0 cuts it short
        /// (GAMESTRN.C puts a 0 where the terminator was); codes of 128 and up are past every
        /// font's 128 glyphs (gfx/GRLIBI.ASM's glyph table).</summary>
        public static bool Writable(char c) { return c != '|' && c != 0 && c < 128; }

        /// <summary>An optimal tree for these counts ('|' is added if missing). Throws when there are more than 128 characters.</summary>
        public static HuffmanNode[] Build(IDictionary<char, long> counts)
        {
            var chars = counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            if (!chars.Contains('|')) chars.Add('|');
            chars.Sort();
            if (chars.Count > MaxCharacters)
                throw new InvalidOperationException(string.Format("The strings use {0} different characters; STRINGS.PAK's tree holds at most {1} (its links are bytes).", chars.Count, MaxCharacters));
            // a tree needs two leaves under its root
            if (chars.Count == 1) chars.Add(chars[0] == ' ' ? 'a' : ' ');
            int leaves = chars.Count;
            var weight = new List<long>();
            var left = new List<int>();
            var right = new List<int>();
            var parent = new List<int>();
            foreach (var c in chars)
            {
                long w; counts.TryGetValue(c, out w);
                weight.Add(Math.Max(1, w)); left.Add(255); right.Add(255); parent.Add(255);
            }
            // (weight, node index): the lower index first on a tie, so the tree is the same each time
            var queue = new SortedSet<(long W, int I)>();
            for (int i = 0; i < leaves; i++) queue.Add((weight[i], i));
            while (queue.Count > 1)
            {
                var a = queue.Min; queue.Remove(a);
                var b = queue.Min; queue.Remove(b);
                int n = weight.Count;
                weight.Add(a.W + b.W); left.Add(a.I); right.Add(b.I); parent.Add(255);
                parent[a.I] = n; parent[b.I] = n;
                queue.Add((a.W + b.W, n));
            }
            var nodes = new HuffmanNode[weight.Count];
            for (int i = 0; i < nodes.Length; i++)
                nodes[i] = new HuffmanNode(i < leaves ? chars[i] : 0, i, parent[i], left[i], right[i]);
            return nodes;
        }

        /// <summary>Decodes one string from bits as the game does (GAMESTRN.C seg039_3495_7B5:
        /// from the last node, a 1 bit goes right, until a node whose left child is 0xFF), up to
        /// '|', 0xFF or 512 characters.</summary>
        public static string DecodeLikeTheGame(HuffmanNode[] nodes, byte[] data, int start)
        {
            var sb = new System.Text.StringBuilder();
            int pos = start, used = 8, bits = 0;
            while (true)
            {
                int v = nodes.Length - 1;
                while (nodes[v].Left != 0xFF)
                {
                    if (used == 8) { bits = pos < data.Length ? data[pos] : 0; pos++; used = 0; }
                    bool one = (bits & 0x80) != 0;
                    bits <<= 1; used++;
                    v = one ? nodes[v].Right : nodes[v].Left;
                }
                int c = nodes[v].Char;
                if (c == 0xFF || c == '|' || sb.Length >= 511) break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }
    }
}
