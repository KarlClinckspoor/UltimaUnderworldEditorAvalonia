using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Tests;
using Xunit;

namespace Underworld.Mods.Tests
{
    public class PackageTests
    {
        static string Temp(string name)
        {
            string d = Path.Combine(Path.GetTempPath(), "uwmods-tests", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>A small game folder: the executable, LEV.ARK, STRINGS.PAK and one sound, copied from the real game.</summary>
        static string SmallGame(string game)
        {
            string g = GameData.Game(game);
            if (g == null) return null;
            var kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            string d = Temp(game);
            foreach (var rel in new[] { GamePaths.ExeName(kind), "DATA/LEV.ARK", "DATA/STRINGS.PAK", "SOUND/UW.AD" })
            {
                string src = GamePaths.Find(g, rel);
                string dst = Path.Combine(d, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst);
            }
            return d;
        }

        static string CopyDir(string src)
        {
            string d = Temp("copy");
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(d, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(f, to);
            }
            return d;
        }

        static void EditLevel(string folder, GameKind kind)
        {
            string p = Path.Combine(folder, "DATA", "LEV.ARK");
            var a = Archive.Open(File.ReadAllBytes(p), kind);
            var b = a.Read(0);
            b[b.Length / 3] ^= 0x21;
            a.Write(0, b);
            File.WriteAllBytes(p, a.ToBytes());
        }

        static void MoveStart(string folder, GameKind kind)
        {
            string p = Path.Combine(folder, GamePaths.ExeName(kind));
            var exe = File.ReadAllBytes(p);
            Assert.True(StartPosition.Patch(exe, kind, 30, 4));
            File.WriteAllBytes(p, exe);
        }

        static Func<string, byte[]> Reader(string folder) => rel => { string p = GamePaths.Find(folder, rel); return p == null ? null : File.ReadAllBytes(p); };

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void A_mod_rebuilds_the_edited_files_byte_for_byte(string game)
        {
            string orig = SmallGame(game);
            if (orig == null) return;
            var kind = game == "UW2" ? GameKind.UW2 : GameKind.UW1;
            string edited = CopyDir(orig);
            EditLevel(edited, kind);
            MoveStart(edited, kind);
            var changes = ModMaker.Compare(orig, edited, kind);
            Assert.Empty(changes.Problems);
            Assert.Equal(new[] { "DATA/LEV.ARK", GamePaths.ExeName(kind) }.OrderBy(x => x, StringComparer.Ordinal), changes.Changed.Select(c => c.Path));
            var mod = ModMaker.Make(changes, "test.mod", "Test", "1.0.0", "", new[] { "Tests" }, "tests");
            var bytes = mod.ToBytes();
            Assert.Equal(bytes, mod.ToBytes());
            var back = ModPackage.FromBytes(bytes);
            Assert.Equal(kind, back.Game);
            Assert.All(back.Check(Reader(orig)), c => Assert.Equal(FileState.Original, c.State));
            Assert.All(back.Check(Reader(edited)), c => Assert.Equal(FileState.Applied, c.State));
            var built = back.Build(Reader(orig));
            foreach (var kv in built) Assert.Equal(File.ReadAllBytes(GamePaths.Find(edited, kv.Key)), kv.Value);
            // the package is small: differences only
            Assert.True(bytes.Length < 2000, "package of " + bytes.Length + " bytes");
            // the executable change is the start square only
            var exeO = File.ReadAllBytes(GamePaths.Find(orig, GamePaths.ExeName(kind)));
            var exeM = built[GamePaths.ExeName(kind)];
            Assert.True(ExeChanges.OnlyStart(exeO, exeM, kind));
            Assert.True(ExeChanges.MovedStart(exeO, exeM, kind, out int x, out int y));
            Assert.Equal((30, 4), (x, y));
        }

        [Fact]
        public void A_mod_refuses_a_file_that_is_not_its_original()
        {
            string orig = SmallGame("UW");
            if (orig == null) return;
            string edited = CopyDir(orig);
            EditLevel(edited, GameKind.UW1);
            var mod = ModMaker.Make(ModMaker.Compare(orig, edited, GameKind.UW1), "t", "T", "1", "", null, "");
            string other = CopyDir(orig);
            var lev = File.ReadAllBytes(Path.Combine(other, "DATA", "LEV.ARK"));
            lev[lev.Length - 1] ^= 1;
            File.WriteAllBytes(Path.Combine(other, "DATA", "LEV.ARK"), lev);
            Assert.Equal(FileState.Different, mod.Check(Reader(other)).Single().State);
            Assert.Throws<ModException>(() => mod.Build(Reader(other)));
            File.Delete(Path.Combine(other, "DATA", "LEV.ARK"));
            Assert.Equal(FileState.Missing, mod.Check(Reader(other)).Single().State);
            Assert.Throws<ModException>(() => mod.Build(Reader(other)));
        }

        [Fact]
        public void Added_or_removed_files_cannot_go_into_a_mod()
        {
            string orig = SmallGame("UW");
            if (orig == null) return;
            string edited = CopyDir(orig);
            File.WriteAllBytes(Path.Combine(edited, "SOUND", "21.VOC"), new byte[] { 1, 2, 3 });
            File.Delete(Path.Combine(edited, "SOUND", "UW.AD"));
            // not game files: ignored
            File.WriteAllBytes(Path.Combine(edited, "README.TXT"), new byte[] { 1 });
            Directory.CreateDirectory(Path.Combine(edited, "SAVE1"));
            File.WriteAllBytes(Path.Combine(edited, "SAVE1", "LEV.ARK"), new byte[] { 1 });
            var c = ModMaker.Compare(orig, edited, GameKind.UW1);
            Assert.Equal(2, c.Problems.Count);
            Assert.Contains(c.Problems, p => p.StartsWith("SOUND/21.VOC"));
            Assert.Contains(c.Problems, p => p.StartsWith("SOUND/UW.AD"));
            Assert.Throws<ModException>(() => ModMaker.Make(c, "t", "T", "1", "", null, ""));
            Assert.Throws<ModException>(() => ModMaker.Make(ModMaker.Compare(orig, CopyDir(orig), GameKind.UW1), "t", "T", "1", "", null, ""));
        }

        [Fact]
        public void The_editors_first_versions_say_what_changed()
        {
            string orig = SmallGame("UW");
            if (orig == null) return;
            string folder = CopyDir(orig);
            string lev = Path.Combine(folder, "DATA", "LEV.ARK");
            File.Copy(lev, lev + ".ORIG");
            File.Copy(lev, lev + ".BKP");
            EditLevel(folder, GameKind.UW1);
            var c = ModMaker.FromOriginals(folder, GameKind.UW1);
            Assert.Empty(c.Problems);
            Assert.Equal("DATA/LEV.ARK", c.Changed.Single().Path);
            // an empty .ORIG: a file the editor added
            File.WriteAllBytes(Path.Combine(folder, "SOUND", "21.VOC"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(folder, "SOUND", "21.VOC.ORIG"), new byte[0]);
            Assert.Single(ModMaker.FromOriginals(folder, GameKind.UW1).Problems);
        }

        static byte[] Zip(string json, params (string Name, byte[] Data)[] entries)
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                using (var s = z.CreateEntry("mod.json").Open()) { var b = Encoding.UTF8.GetBytes(json); s.Write(b, 0, b.Length); }
                foreach (var e in entries) using (var s = z.CreateEntry(e.Name).Open()) s.Write(e.Data, 0, e.Data.Length);
            }
            return ms.ToArray();
        }

        static string Json(string path, string game = "uw1", int format = 2, string deltaSha = null, byte[] delta = null)
        {
            delta = delta ?? new byte[] { 1 };
            string h = new string('a', 64);
            return "{\"format\":" + format + ",\"game\":\"" + game + "\",\"id\":\"x\",\"name\":\"x\",\"version\":\"1\",\"files\":[{\"path\":\"" + path +
                "\",\"base\":{\"sha256\":\"" + h + "\",\"size\":1},\"target\":{\"sha256\":\"" + h + "\",\"size\":1},\"delta\":{\"path\":\"deltas/" + path +
                ".uwd\",\"sha256\":\"" + (deltaSha ?? ModPackage.Sha256(delta)) + "\"}}]}";
        }

        [Theory]
        [InlineData("../UW.EXE")]
        [InlineData("SAVE1/LEV.ARK")]
        [InlineData("UWSETUP.EXE")]
        [InlineData("UW2.EXE")]
        [InlineData("DATA/LEV.ARK.BKP")]
        [InlineData("data/LEV.ARK")]
        [InlineData("DATA//LEV.ARK")]
        [InlineData("C:/DATA/LEV.ARK")]
        public void A_package_naming_a_file_a_mod_may_not_change_is_refused(string path)
        {
            var d = new byte[] { 1 };
            Assert.Throws<ModException>(() => ModPackage.FromBytes(Zip(Json(path), ("deltas/" + path + ".uwd", d))));
        }

        [Fact]
        public void Damaged_or_old_packages_are_refused()
        {
            var d = new byte[] { 1 };
            // the right paths read
            Assert.Single(ModPackage.FromBytes(Zip(Json("DATA/LEV.ARK"), ("deltas/DATA/LEV.ARK.uwd", d))).Files);
            // a delta that is not the one named
            Assert.Throws<ModException>(() => ModPackage.FromBytes(Zip(Json("DATA/LEV.ARK", deltaSha: new string('b', 64)), ("deltas/DATA/LEV.ARK.uwd", d))));
            // no delta
            Assert.Throws<ModException>(() => ModPackage.FromBytes(Zip(Json("DATA/LEV.ARK"))));
            // format 1, and an unknown game
            Assert.Throws<ModException>(() => ModPackage.FromBytes(Zip(Json("DATA/LEV.ARK", format: 1), ("deltas/DATA/LEV.ARK.uwd", d))));
            Assert.Throws<ModException>(() => ModPackage.FromBytes(Zip(Json("DATA/LEV.ARK", game: "uw3"), ("deltas/DATA/LEV.ARK.uwd", d))));
            // not a zip
            Assert.Throws<ModException>(() => ModPackage.FromBytes(new byte[] { 1, 2, 3 }));
        }
    }
}
