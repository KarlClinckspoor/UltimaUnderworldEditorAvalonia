using System;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Saved games that crash or upset the game: found, repaired, and nothing else changed. Real saves from play come from GameData.Saves.</summary>
    public class SaveCheckTests
    {
        [Fact]
        public void An_empty_uw1_block_that_shares_the_next_ones_offset_is_unpointed_without_cutting_the_other()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var ark = (Uw1Archive)Archive.Load(Path.Combine(g, "DATA", "LEV.ARK"), GameKind.UW1);
            var map = Enumerable.Range(0, 4096).Select(i => (byte)(i % 7 + 1)).ToArray();
            ark.Write(Automap.MapBlock(GameKind.UW1, 2), map);
            // as the game leaves it: level 1's emptied notes at the offset where level 2's automap was then appended
            int nb = Automap.NotesBlock(GameKind.UW1, 1), mb = Automap.MapBlock(GameKind.UW1, 2);
            var bytes = ark.ToBytes();
            Array.Copy(bytes, 2 + 4 * mb, bytes, 2 + 4 * nb, 4);
            ark = (Uw1Archive)Archive.Open(bytes, GameKind.UW1);
            Assert.Equal(mb, ark.AliasOf(nb));
            Assert.Equal(map, ark.Read(nb));                  // the notes read as the automap: the crash
            var notes = new byte[Automap.NoteSize]; notes[0] = (byte)'A'; notes[0x32] = 10; notes[0x34] = 20;
            var w = (Uw1Archive)Archive.Open(bytes, GameKind.UW1);
            w.Write(nb, notes);                               // written: moved out, the automap kept
            Assert.Equal(notes, w.Read(nb));
            Assert.Equal(map, w.Read(mb));
            Assert.Equal(-1, w.AliasOf(nb));
            var d = (Uw1Archive)Archive.Open(bytes, GameKind.UW1);
            d.Delete(nb);                                     // deleted: only the pointer goes
            Assert.False(d.Has(nb));
            Assert.Equal(map, d.Read(mb));
            Assert.Equal(bytes.Length, d.ToBytes().Length);
            for (int b = 0; b < 9; b++) Assert.Equal(ark.Read(b), d.Read(b));
        }

        [Fact]
        public void Notes_with_no_end_or_off_the_screen_are_dropped_and_good_ones_kept()
        {
            var b = new byte[Automap.NoteSize * 3];
            System.Text.Encoding.ASCII.GetBytes("GOOD").CopyTo(b, 0); b[0x32] = 100; b[0x34] = 50;
            for (int i = 0; i < Automap.TextSize; i++) b[Automap.NoteSize + i] = (byte)'X'; b[Automap.NoteSize + 0x32] = 10; b[Automap.NoteSize + 0x34] = 10;
            System.Text.Encoding.ASCII.GetBytes("FAR").CopyTo(b, 2 * Automap.NoteSize); b[2 * Automap.NoteSize + 0x32] = 0x90; b[2 * Automap.NoteSize + 0x33] = 0x01; // x 400
            var keep = SaveCheck.CleanNotes(b);
            Assert.Single(keep);
            Assert.Equal("GOOD", keep[0].Text);
        }

        [Fact]
        public void Saves_kept_from_play_are_repaired_and_nothing_else_changes()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            foreach (var save in GameData.Saves("UW"))
            {
                var s = GameSession.Open(save, g);
                Assert.True(s.IsSave);
                var edits = new LevelEdits(s);
                var found = SaveCheck.Find(s, edits);
                string name = Path.GetFileName(save);
                if (name == "notes-alias-level5") Assert.Contains(found, f => f.Level == 5 && f.Kind == "notes" && f.Text.Contains("level 8's automap"));
                if (name == "lost-arrow-level2") Assert.Contains(found, f => f.Level == 2 && f.Kind == "objects" && f.Text.Contains("#496"));
                if (name == "lost-coin-level5") Assert.Contains(found, f => f.Level == 5 && f.Kind == "objects" && f.Text.Contains("#425"));
                SaveCheck.RepairAll(found, edits);
                var bytes = edits.ToBytes();
                string tmp = Path.Combine(Path.GetTempPath(), "uwedit-repaired-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tmp);
                try
                {
                    foreach (var f in Directory.GetFiles(save)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                    File.WriteAllBytes(Path.Combine(tmp, "LEV.ARK"), bytes);
                    var s2 = GameSession.Open(tmp, g);
                    var again = SaveCheck.Find(s2, new LevelEdits(s2));
                    Assert.True(again.All(f => !f.Problem), name + ": " + string.Join(" | ", again));
                    // only the blocks of the levels repaired changed
                    var touched = found.Where(f => f.CanFix).Select(f => f.Level).ToHashSet();
                    for (int b = 0; b < s.LevArk.Count; b++)
                    {
                        var a = s.LevArk.Read(b); var c = s2.LevArk.Read(b);
                        bool same = (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c));
                        if (same) continue;
                        int lv = b < 9 ? b + 1 : b >= 27 && b < 36 ? b - 26 : b >= 36 && b < 45 ? b - 35 : -1;
                        Assert.True(touched.Contains(lv), name + ": block " + b + " changed but level " + lv + " was not repaired");
                    }
                }
                finally { Directory.Delete(tmp, true); }
            }
        }

        [Fact]
        public void Shipped_saves_and_data_have_no_problems_and_the_check_is_quick()
        {
            foreach (var game in new[] { "UW", "UW2" })
            {
                string g = GameData.Game(game); if (g == null) continue;
                foreach (var path in new[] { g }.Concat(Enumerable.Range(1, 4).Select(i => Path.Combine(g, "SAVE" + i)).Where(Directory.Exists)))
                {
                    var s = GameSession.Open(path);
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    var found = SaveCheck.Find(s, new LevelEdits(s));
                    Assert.True(t.ElapsedMilliseconds < 8000, path + " took " + t.ElapsedMilliseconds + " ms");
                    Assert.True(found.All(f => !f.Problem), path + ": " + string.Join(" | ", found));
                }
            }
        }

        [Fact]
        public void A_save_with_any_name_or_outside_the_game_folder_opens_with_the_games_data()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var save = GameData.Saves("UW").FirstOrDefault(); if (save == null) return;
            Assert.Throws<SaveWithoutGameException>(() => GameSession.Open(save));
            var s = GameSession.Open(save, g);
            Assert.Equal(Path.GetFullPath(save), s.SaveFolder);
            Assert.NotNull(s.Player);
            Assert.Throws<FileNotFoundException>(() => GameSession.Open(Path.Combine(g, "DATA"), g));
        }
    }
}
