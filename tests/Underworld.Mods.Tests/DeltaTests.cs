using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Underworld.Mods.Tests
{
    public class DeltaTests
    {
        readonly ITestOutputHelper output;
        public DeltaTests(ITestOutputHelper output) { this.output = output; }

        static byte[] RandomBytes(Random r, int n, bool structured)
        {
            var b = new byte[n];
            r.NextBytes(b);
            // stretches of zeros and repeats, as game files have
            if (structured)
                for (int i = 0; i + 64 < n; i += r.Next(64, 600))
                {
                    int len = r.Next(8, 64);
                    if (r.Next(2) == 0) Array.Clear(b, i, len);
                    else if (i >= 200) Array.Copy(b, i - 200, b, i, len);
                }
            return b;
        }

        static byte[] Mutate(Random r, byte[] src, int edits)
        {
            var list = src.ToList();
            for (int k = 0; k < edits; k++)
            {
                int at = list.Count == 0 ? 0 : r.Next(list.Count);
                switch (r.Next(3))
                {
                    case 0: // replace
                        for (int j = 0; j < r.Next(1, 20) && at + j < list.Count; j++) list[at + j] = (byte)r.Next(256);
                        break;
                    case 1: // insert
                        list.InsertRange(at, RandomBytes(r, r.Next(1, 100), false));
                        break;
                    default: // delete
                        list.RemoveRange(at, Math.Min(r.Next(1, 100), list.Count - at));
                        break;
                }
            }
            return list.ToArray();
        }

        [Fact]
        public void Random_edits_round_trip()
        {
            var r = new Random(1234);
            for (int t = 0; t < 200; t++)
            {
                var src = RandomBytes(r, r.Next(0, 20000), t % 2 == 0);
                var dst = Mutate(r, src, r.Next(0, 12));
                var d = Delta.Make(src, dst);
                Assert.Equal(dst, Delta.Apply(src, d));
            }
        }

        [Fact]
        public void Empty_files_round_trip()
        {
            var some = new byte[] { 1, 2, 3 };
            Assert.Equal(some, Delta.Apply(new byte[0], Delta.Make(new byte[0], some)));
            Assert.Equal(new byte[0], Delta.Apply(some, Delta.Make(some, new byte[0])));
            Assert.Equal(new byte[0], Delta.Apply(new byte[0], Delta.Make(new byte[0], new byte[0])));
        }

        [Fact]
        public void A_few_bytes_changed_in_place_make_a_small_delta()
        {
            var r = new Random(7);
            var src = RandomBytes(r, 100000, true);
            var dst = (byte[])src.Clone();
            dst[100] ^= 1; dst[5000] ^= 0x55; dst[5001] ^= 0x55; dst[99999] = 0;
            var d = Delta.Make(src, dst);
            Assert.True(d.Length < 40, "delta of " + d.Length + " bytes");
        }

        [Fact]
        public void Inserted_bytes_are_all_the_delta_carries()
        {
            var r = new Random(8);
            var src = RandomBytes(r, 100000, false);
            var added = RandomBytes(r, 50, false);
            var dst = src.Take(40000).Concat(added).Concat(src.Skip(40000)).ToArray();
            var d = Delta.Make(src, dst);
            Assert.True(d.Length < 50 + 30, "delta of " + d.Length + " bytes");
        }

        [Fact]
        public void A_damaged_or_foreign_delta_is_refused()
        {
            var r = new Random(9);
            var src = RandomBytes(r, 5000, true);
            var dst = Mutate(r, src, 5);
            var d = Delta.Make(src, dst);
            // made from another file
            Assert.Throws<InvalidDataException>(() => Delta.Apply(src.Take(4999).ToArray(), d));
            // cut short
            Assert.Throws<InvalidDataException>(() => Delta.Apply(src, d.Take(d.Length - 1).ToArray()));
            // trailing bytes
            Assert.Throws<InvalidDataException>(() => Delta.Apply(src, d.Concat(new byte[] { 0 }).ToArray()));
            // not a delta
            Assert.Throws<InvalidDataException>(() => Delta.Apply(src, new byte[] { 1, 2, 3, 4, 5 }));
            // a COPY from past the end of a 3-byte original: "UWD2", base 3, target 4, COPY +0 len 4, end
            var bad = new byte[] { (byte)'U', (byte)'W', (byte)'D', (byte)'2', 3, 4, 0x01, 0x00, 0x04, 0x00 };
            Assert.Throws<InvalidDataException>(() => Delta.Apply(new byte[] { 1, 2, 3 }, bad));
            // a BLOCK in a file that is no archive
            var block = new byte[] { (byte)'U', (byte)'W', (byte)'D', (byte)'2', 3, 0, 0x03, 0x00, 0x01, 0x00, 0x00 };
            Assert.Throws<InvalidDataException>(() => Delta.Apply(new byte[] { 1, 2, 3 }, block));
        }

        static byte[] GameFile(string game, string rel)
        {
            string g = GameData.Game(game);
            if (g == null) return null;
            string p = GamePaths.Find(g, rel);
            return p == null ? null : File.ReadAllBytes(p);
        }

        [Fact]
        public void Uw1_level_with_a_few_bytes_changed_makes_a_small_exact_delta()
        {
            var lev = GameFile("UW", "DATA/LEV.ARK");
            if (lev == null) return;
            var a = Archive.Open(lev, GameKind.UW1);
            var blk = a.Read(0);
            blk[0x4000] ^= 0x10; blk[0x4001] ^= 0x22; blk[0x6000] ^= 1;
            a.Write(0, blk);
            var edited = a.ToBytes();
            var d = Delta.Make(lev, edited);
            Assert.Equal(edited, Delta.Apply(lev, d));
            output.WriteLine("UW1 LEV.ARK, 3 bytes of level 1: delta " + d.Length + " bytes");
            Assert.True(d.Length < 64, "delta of " + d.Length + " bytes");
        }

        [Fact]
        public void Uw2_compressed_block_is_diffed_decompressed()
        {
            var lev = GameFile("UW2", "DATA/LEV.ARK");
            if (lev == null) return;
            var a = new Uw2Archive(lev);
            int k = Enumerable.Range(0, a.Count).First(b => a.Has(b) && a.Compressed(b) && a.StoredLength(b) > 2000);
            var blk = a.Read(k);
            blk[blk.Length / 2] ^= 0x10;
            blk[blk.Length / 2 + 300] ^= 0x01;
            a.Write(k, blk);
            var edited = a.ToBytes();
            Assert.True(new Uw2Archive(edited).Compressed(k));
            var plain = Delta.Make(lev, edited, uw2Archive: false);
            var withBlocks = Delta.Make(lev, edited, uw2Archive: true);
            Assert.Equal(edited, Delta.Apply(lev, withBlocks));
            Assert.Equal(edited, Delta.Apply(lev, plain));
            output.WriteLine("UW2 LEV.ARK block " + k + " (" + a.StoredLength(k) + " bytes stored), 2 bytes changed: BLOCK delta " + withBlocks.Length + " bytes, plain " + plain.Length);
            // the plain delta carries the recompressed rest of the block, BLOCK only the change
            Assert.True(withBlocks.Length < 100, "BLOCK delta of " + withBlocks.Length + " bytes (plain " + plain.Length + ")");
            Assert.True(withBlocks.Length * 4 < plain.Length, "BLOCK " + withBlocks.Length + " vs plain " + plain.Length);
        }

        [Fact]
        public void Unrelated_files_still_round_trip()
        {
            var a = GameFile("UW", "DATA/STRINGS.PAK");
            var b = GameFile("UW", "UW.EXE");
            if (a == null || b == null) return;
            Assert.Equal(b, Delta.Apply(a, Delta.Make(a, b)));
        }
    }
}
