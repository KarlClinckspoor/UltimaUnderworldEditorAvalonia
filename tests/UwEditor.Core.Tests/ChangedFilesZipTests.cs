using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;
using UwEditor.Core.Mods;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Tools ▸ Mods ▸ Changed files as a .zip: whole files at their folders, with a note that it is not to share.</summary>
    public class ChangedFilesZipTests
    {
        [Fact]
        public void The_changed_files_go_whole_into_their_folders_with_the_note()
        {
            var changes = new FolderChanges { Game = GameKind.UW1 };
            byte[] lev = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
            changes.Changed.Add(("DATA/LEV.ARK", new byte[] { 1, 2, 3 }, lev));
            changes.Changed.Add(("SOUND/12.VOC", null, new byte[] { 9, 9 }));
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-zip-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                string zip = Path.Combine(dir, "uw1-changed-files.zip");
                File.WriteAllText(zip, "an older one");
                var written = EditorMods.WriteZip(changes, zip, "Ultima Underworld");
                Assert.Equal(new[] { "DATA/LEV.ARK", "SOUND/12.VOC" }, written);
                Assert.False(File.Exists(zip + ".tmp"));
                using (var z = ZipFile.OpenRead(zip))
                {
                    Assert.Equal(new[] { "DATA/LEV.ARK", "SOUND/12.VOC", EditorMods.ZipReadmeName }, z.Entries.Select(e => e.FullName));
                    using (var ms = new MemoryStream()) { z.GetEntry("DATA/LEV.ARK").Open().CopyTo(ms); Assert.Equal(lev, ms.ToArray()); }
                    string note;
                    using (var r = new StreamReader(z.GetEntry(EditorMods.ZipReadmeName).Open(), Encoding.UTF8)) note = r.ReadToEnd();
                    Assert.Contains("not for sharing in public", note);
                    Assert.Contains("not for the mod manager", note);
                    Assert.Contains(@"SOUND\12.VOC  (new)", note);
                }
                // unzipped over a folder, the files land at the game's paths
                string game = Path.Combine(dir, "game");
                ZipFile.ExtractToDirectory(zip, game);
                Assert.Equal(lev, File.ReadAllBytes(Path.Combine(game, "DATA", "LEV.ARK")));
                // nothing changed: no .zip
                Assert.Throws<InvalidOperationException>(() => EditorMods.WriteZip(new FolderChanges { Game = GameKind.UW1 }, Path.Combine(dir, "none.zip"), "Ultima Underworld"));
                Assert.False(File.Exists(Path.Combine(dir, "none.zip")));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void A_mod_names_the_editors_version()
        {
            // Directory.Build.props' <Version> (0.1.0 and up), not the SDK's 1.0.0 default
            Assert.Matches(@"^UwEditorAvalonia 0\.\d+\.\d+$", EditorMods.MadeWith);
        }
    }
}
