using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace Underworld.Mods.Tests
{
    /// <summary>GOG's game.gog (an ISO 9660 image of both games): reading it and copying a game out.</summary>
    public class GogImageTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "uw-gog-tests", Guid.NewGuid().ToString("N").Substring(0, 8));
        public GogImageTests() { Directory.CreateDirectory(dir); }
        public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }

        static Dictionary<string, byte[]> SmallDisc() => new Dictionary<string, byte[]>
        {
            ["UW.BAT"] = Encoding.ASCII.GetBytes("@echo off"),
            ["UW/UW.EXE"] = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray(),
            ["UW/DATA/UW.CFG"] = Encoding.ASCII.GetBytes("0 -1 -1 -1 sound\r\n"),
            ["UW/DATA/LEV.ARK"] = new byte[3000],
            ["UW/SOUND/EMPTY.DAT"] = new byte[0],
            ["UW2/UW2.EXE"] = new byte[] { 1, 2, 3 },
            ["UW2/DATA/UW.CFG"] = new byte[] { 9 },
        };

        [Fact]
        public void An_iso_image_lists_both_games_and_copies_one_out_with_the_installs_sound_setup()
        {
            string image = Path.Combine(dir, "game.gog");
            FakeGogImage.Write(image, SmallDisc());
            Assert.Equal(new[] { GameKind.UW1, GameKind.UW2 }, GogImage.Games(image));

            // the install: UNDEROM1 holds the CD version's own sound setup, which the copy takes
            string install = Path.Combine(dir, "install");
            Directory.CreateDirectory(Path.Combine(install, "UNDEROM1", "DATA"));
            File.WriteAllText(Path.Combine(install, "UNDEROM1", "DATA", "UW.CFG"), "4 7 220 1 sound");
            File.WriteAllText(Path.Combine(install, "UNDEROM1", "MARKER.FIL"), "Do not delete me.");
            File.WriteAllText(Path.Combine(install, "dosboxULTIMA1.conf"), "");
            Assert.Equal(GameKind.UW1, GogImage.GameOfInstall(install));

            string dest = Path.Combine(dir, "copy", "UW");
            int n = GogImage.Extract(image, GameKind.UW1, dest, install);
            Assert.Equal(4, n);
            Assert.Equal(SmallDisc()["UW/UW.EXE"], File.ReadAllBytes(Path.Combine(dest, "UW.EXE")));
            Assert.Equal(3000, new FileInfo(Path.Combine(dest, "DATA", "LEV.ARK")).Length);
            Assert.Equal(0, new FileInfo(Path.Combine(dest, "SOUND", "EMPTY.DAT")).Length);
            Assert.Equal("4 7 220 1 sound", File.ReadAllText(Path.Combine(dest, "DATA", "UW.CFG")));
            Assert.False(File.Exists(Path.Combine(dest, "MARKER.FIL")));
            Assert.False(File.Exists(Path.Combine(dest, "UW2.EXE")));
            // nothing left beside it, and a second copy into the same folder is refused
            Assert.Single(Directory.GetFileSystemEntries(Path.Combine(dir, "copy")));
            Assert.Throws<IOException>(() => GogImage.Extract(image, GameKind.UW1, dest, install));
        }

        [Fact]
        public void The_other_game_takes_the_sound_setup_of_its_own_install_beside_this_one()
        {
            string image = Path.Combine(dir, "Ultima Underworld", "game.gog");
            Directory.CreateDirectory(Path.GetDirectoryName(image));
            FakeGogImage.Write(image, SmallDisc());
            string uw1 = Path.GetDirectoryName(image), uw2 = Path.Combine(dir, "Ultima Underworld 2");
            Directory.CreateDirectory(Path.Combine(uw1, "UNDEROM1", "DATA"));
            File.WriteAllText(Path.Combine(uw1, "UNDEROM1", "DATA", "UW.CFG"), "one");
            // without the second game's install, it has no setup of its own
            Assert.Null(GogImage.SetupInstall(uw1, GameKind.UW2));
            Directory.CreateDirectory(Path.Combine(uw2, "UNDEROM2", "DATA"));
            File.WriteAllText(Path.Combine(uw2, "UNDEROM2", "DATA", "UW.CFG"), "two");
            // a folder with a setup but no image of the same release is not the other install
            Assert.Null(GogImage.SetupInstall(uw1, GameKind.UW2));
            File.Copy(image, Path.Combine(uw2, "game.gog"));
            Assert.Equal(Path.GetFullPath(uw1), GogImage.SetupInstall(uw1, GameKind.UW1));
            Assert.Equal(Path.GetFullPath(uw2), GogImage.SetupInstall(uw1, GameKind.UW2));
            GogImage.Extract(image, GameKind.UW2, Path.Combine(dir, "copy", "UW2"), uw1);
            Assert.Equal("two", File.ReadAllText(Path.Combine(dir, "copy", "UW2", "DATA", "UW.CFG")));
            // the note names each game's install; a game folder reads its own line
            GogImage.WriteInstallNote(Path.Combine(dir, "copy"), uw1, new Dictionary<GameKind, string> { [GameKind.UW1] = uw1, [GameKind.UW2] = uw2 });
            Assert.Equal(Path.GetFullPath(uw2), GogImage.InstallOf(Path.Combine(dir, "copy", "UW2")));
            Directory.CreateDirectory(Path.Combine(dir, "copy", "UW"));
            Assert.Equal(Path.GetFullPath(uw1), GogImage.InstallOf(Path.Combine(dir, "copy", "UW")));
        }

        [Fact]
        public void A_zip_with_the_disc_in_a_folder_is_read_too()
        {
            string image = Path.Combine(dir, "game.gog");
            using (var z = ZipFile.Open(image, ZipArchiveMode.Create))
                foreach (var kv in SmallDisc())
                    using (var s = z.CreateEntry("UW12/" + kv.Key).Open()) s.Write(kv.Value, 0, kv.Value.Length);
            Assert.Equal(new[] { GameKind.UW1, GameKind.UW2 }, GogImage.Games(image));
            string dest = Path.Combine(dir, "UW2");
            GogImage.Extract(image, GameKind.UW2, dest);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dest, "UW2.EXE")));
            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(dest, "DATA", "UW.CFG")));
        }

        [Fact]
        public void A_file_that_is_neither_is_refused_and_leaves_nothing_behind()
        {
            string image = Path.Combine(dir, "game.gog");
            File.WriteAllBytes(image, new byte[40000]);
            Assert.Throws<InvalidDataException>(() => GogImage.Games(image));
            Assert.Throws<InvalidDataException>(() => GogImage.Extract(image, GameKind.UW1, Path.Combine(dir, "UW")));
            Assert.Single(Directory.GetFileSystemEntries(dir));
        }

        [Fact]
        public void The_known_release_is_recognised_by_its_hash()
        {
            Assert.Equal("GOG's Ultima Underworld 1+2 (2025)", GogImage.Release("846573E2E676647511206C419D23F584A2898BDDB30B69603CC30BB859779DED"));
            Assert.Null(GogImage.Release("00"));
            string image = Path.Combine(dir, "x.gog");
            File.WriteAllBytes(image, Encoding.ASCII.GetBytes("abc"));
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", GogImage.Sha256(image));
        }

        /// <summary>The test data's game folders, put on a disc and copied out again, come back byte for byte.</summary>
        [Fact]
        public void The_test_games_survive_the_round_trip()
        {
            string root = GameData.Root;
            var folders = new Dictionary<string, string>();
            foreach (var g in new[] { "UW", "UW2" })
                if (root != null && Directory.Exists(Path.Combine(root, g, "DATA"))) folders[g] = Path.Combine(root, g);
            if (folders.Count == 0) return;
            string image = Path.Combine(dir, "game.gog");
            FakeGogImage.FromFolders(image, folders);
            foreach (var kv in folders)
            {
                var game = kv.Key == "UW2" ? GameKind.UW2 : GameKind.UW1;
                string dest = Path.Combine(dir, "out", kv.Key);
                GogImage.Extract(image, game, dest);
                foreach (var f in Directory.EnumerateFiles(kv.Value, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(kv.Value, f);
                    if (rel.StartsWith("SAVE", StringComparison.OrdinalIgnoreCase)) continue;
                    Assert.Equal(File.ReadAllBytes(f), File.ReadAllBytes(Path.Combine(dest, rel)));
                }
            }
        }

        /// <summary>GOG's real image, when this PC has it installed where GOG puts it.</summary>
        [Fact]
        public void The_real_image_holds_the_same_games_as_the_test_data()
        {
            string image = @"C:\GOG Games\Ultima Underworld\game.gog";
            if (!File.Exists(image)) return;
            Assert.NotNull(GogImage.Release(GogImage.Sha256(image)));
            using (var r = GogImage.Open(image))
            {
                Assert.Contains(r.Entries, e => e.Path == "UW/DATA/LEV.ARK");
                Assert.Contains(r.Entries, e => e.Path == "UW2/DATA/LEV.ARK");
                string root = GameData.Root;
                foreach (var (disc, exe) in new[] { ("UW", "UW.EXE"), ("UW2", "UW2.EXE") })
                {
                    string mine = root == null ? null : Path.Combine(root, disc, exe);
                    if (mine == null || !File.Exists(mine)) continue;
                    Assert.Equal(File.ReadAllBytes(mine), r.Read(r.Entries.Single(e => e.Path == disc + "/" + exe)));
                }
            }
        }
    }
}
