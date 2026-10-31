using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Graphics;
using Xunit;
using Xunit.Abstractions;

namespace UwEditor.Core.Tests
{
    public class GraphicsTests
    {
        readonly ITestOutputHelper output;
        public GraphicsTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Png_round_trips_indices_and_reads_rgba()
        {
            var pal = Enumerable.Range(0, 16).Select(i => 0xFF000000u | (uint)(i * 16) << 16 | (uint)(255 - i * 16) << 8 | 7u).ToArray();
            pal[0] = 0;
            var px = Enumerable.Range(0, 7 * 5).Select(i => i % 16).ToArray();
            var png = Png.EncodeIndexed(7, 5, px, pal);
            var back = Png.Decode(png);
            Assert.Equal(px, back.Indices);
            Assert.Equal(0, back.Rgba[3]);          // index 0 is transparent
            Assert.Equal(255, back.Rgba[7]);
            var rgba = Png.Decode(Png.EncodeRgba(7, 5, back.Rgba));
            Assert.Null(rgba.Indices);
            Assert.Equal(back.Rgba, rgba.Rgba);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Every_picture_file_reads_and_its_exports_import_back_unchanged(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            string data = Path.Combine(g, "DATA");
            var kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            var pals = Palettes.Load(data);
            int files = 0, pictures = 0;
            foreach (var path in PictureFile.Find(data))
            {
                var f = PictureFile.Load(path, kind);
                files++;
                for (int i = 0; i < f.Count; i++)
                {
                    var img = f.Get(i, pals, f.DefaultPalette(i));
                    if (img == null) continue;
                    pictures++;
                    if (i % 7 != 0) continue;        // round-trip a sample of each file
                    var r = PictureImport.FromPng(img.ToPng(), img);
                    Assert.True(r.Ok, f.Name + " #" + i + ": " + r.Summary);
                    Assert.Equal(img.Pixels, r.Pixels);
                }
                Assert.Equal(File.ReadAllBytes(path), f.ToBytes());
            }
            output.WriteLine("{0}: {1} files, {2} pictures", game, files, pictures);
            Assert.True(pictures > 500);
        }

        [Fact]
        public void A_replaced_sprite_is_written_and_read_back()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            string data = Path.Combine(g, "DATA");
            var pals = Palettes.Load(data);
            var f = PictureFile.Load(Path.Combine(data, "OBJECTS.GR"), GameKind.UW1);
            var img = f.Get(1, pals, 0);
            var px = img.Pixels.Select(v => v == 0 ? 0 : (v % 15) + 1).ToArray();   // other colours of its 16
            f.Replace(1, img, px);
            var copy = Path.Combine(Path.GetTempPath(), "uwedit-obj-" + Guid.NewGuid().ToString("N") + ".GR");
            File.WriteAllBytes(copy, f.ToBytes());
            try
            {
                var back = PictureFile.Load(copy, GameKind.UW1);
                Assert.Equal(px, back.Get(1, pals, 0).Pixels);
                Assert.Equal(f.Get(0, pals, 0).Pixels, back.Get(0, pals, 0).Pixels);
                Assert.Equal(f.Count, back.Count);
            }
            finally { File.Delete(copy); }
        }

        [Fact]
        public void Import_refuses_other_sizes_and_colours_unless_nearest_is_asked_for()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            string data = Path.Combine(g, "DATA");
            var pals = Palettes.Load(data);
            var f = PictureFile.Load(Path.Combine(data, "OBJECTS.GR"), GameKind.UW1);
            var img = f.Get(0, pals, 0);          // a 4-bit sprite: 15 colours and transparent
            // another size
            var big = new PngImage { Width = img.Width + 1, Height = img.Height, Rgba = new byte[(img.Width + 1) * img.Height * 4] };
            var r = PictureImport.FromImage(big, img);
            Assert.False(r.Ok);
            Assert.Contains("must stay", r.Errors[0]);
            // a colour not in its table
            var rgba = img.ToRgba();
            int k = Array.FindIndex(Enumerable.Range(0, img.Width * img.Height).ToArray(), i => rgba[4 * i + 3] == 255);
            rgba[4 * k] = 1; rgba[4 * k + 1] = 254; rgba[4 * k + 2] = 3;
            var png = Png.EncodeRgba(img.Width, img.Height, rgba);
            r = PictureImport.FromPng(png, img);
            Assert.False(r.Ok);
            Assert.Single(r.Missing);
            Assert.Contains("#01FE03", r.Summary);
            r = PictureImport.FromPng(png, img, nearest: true);
            Assert.True(r.Ok);
            Assert.InRange(r.Pixels[k], 1, 15);
            // a texture has no transparent colour
            var tr = PictureFile.Load(Path.Combine(data, "W64.TR"), GameKind.UW1);
            var tex = tr.Get(0, pals, 0);
            var clear = new PngImage { Width = tex.Width, Height = tex.Height, Rgba = new byte[tex.Width * tex.Height * 4] };
            r = PictureImport.FromImage(clear, tex);
            Assert.False(r.Ok);
            Assert.Contains("no transparent colour", r.Summary);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Critter_frames_decode_and_a_frame_replaced_with_its_own_pixels_reads_back(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            var s = GameSession.Open(g);
            var files = CritterArt.Load(g, kind, s.Names);
            Assert.True(files.Count > 20);
            int frames = 0;
            foreach (var f in files)
                foreach (var p in f.Pages)
                {
                    Assert.Equal(File.ReadAllBytes(p.Path), p.Data);
                    Assert.True(p.Data.Length <= CritterPage.MaxBytes);
                    foreach (var fr in p.Frames.Values) { frames++; Assert.Equal(fr.Width * fr.Height, fr.Pixels.Length); Assert.Contains(fr.Format, new[] { 6, 8 }); }
                }
            output.WriteLine("{0}: {1} critter files, {2} frames, {3} animations", game, files.Count, frames, files.Sum(f => f.Animations.Count));
            var page = files[1].Pages[0];
            int first = page.Frames.Keys.First();
            var px = page.Frames[first].Pixels.ToArray();
            page.Replace(first, px);
            var back = CritterPage.Load(WriteTemp(page.Data), kind, 0);
            Assert.Equal(px, back.Frames[first].Pixels);
            foreach (var k in page.Frames.Keys) Assert.Equal(page.Frames[k].Pixels, back.Frames[k].Pixels);
        }

        [Fact]
        public void A_critter_page_that_would_pass_0x7FFF_bytes_is_refused()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var files = CritterArt.Load(g, GameKind.UW2, null);
            var page = files.SelectMany(f => f.Pages).OrderByDescending(p => p.Data.Length).First();
            var fr = page.Frames.Values.OrderByDescending(x => x.Width * x.Height).First();
            var rnd = new Random(1);
            var noise = Enumerable.Range(0, fr.Width * fr.Height).Select(i => rnd.Next(1, 32)).ToArray();
            var before = page.Data;
            var e = Assert.Throws<InvalidDataException>(() => page.Replace(fr.Number, noise));
            Assert.Contains("0x7FFF", e.Message);
            Assert.Same(before, page.Data);
            Assert.False(page.HasChanges);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Cutscene_frames_decode_and_a_replaced_frame_is_re_encoded_within_the_pages(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var cuts = Cutscene.Find(g);
            Assert.True(cuts.Count > 5);
            var path = cuts[0].Animations[1];
            var a = LpfAnimation.Load(path);
            Assert.Equal(File.ReadAllBytes(path), a.ToBytes());
            var frames = a.Frames().Select(f => f.ToArray()).ToList();
            Assert.Equal(320, a.Width);
            int k = frames.Count / 2;
            var px = frames[k].ToArray();
            for (int y = 50; y < 150; y++) for (int x = 100; x < 220; x++) px[y * 320 + x] = (x + y) % 2 == 0 ? 15 : px[y * 320 + x];
            a.Replace(k, px);
            var back = LpfAnimation.Parse(a.ToBytes());
            var bf = back.Frames();
            Assert.Equal(frames.Count, bf.Count);
            for (int i = 0; i < frames.Count; i++) Assert.Equal(i == k ? px : frames[i], bf[i]);
            output.WriteLine("{0}: {1} cutscenes; {2}: {3} frames, {4} → {5} bytes", game, cuts.Count, a.Name, frames.Count, File.ReadAllBytes(path).Length, a.ToBytes().Length);
            // the first frame (and the loop back to it) re-encoded too
            px = frames[0].ToArray();
            px[0] = (px[0] + 1) & 255;
            a.Replace(0, px);
            back = LpfAnimation.Parse(a.ToBytes());
            Assert.Equal(px, back.Frames()[0]);
            Assert.Equal(frames[1], back.Frames()[1]);
        }

        static string WriteTemp(byte[] b)
        {
            string p = Path.Combine(Path.GetTempPath(), "uwedit-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(p, b);
            return p;
        }
    }
}
