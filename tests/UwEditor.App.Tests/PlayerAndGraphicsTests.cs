using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Graphics;
using UwEditor.Core.Player;
using UwEditor.Core.Tests;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The player window and the graphics window, without a screen; saving on copies of the game.</summary>
    public class PlayerAndGraphicsTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from, params string[] dirs)
            {
                foreach (var d in dirs)
                {
                    string src = Path.Combine(from, d);
                    if (!Directory.Exists(src)) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        [AvaloniaFact]
        public void Player_window_warns_about_uw2_cheat_detection_and_saves_stats_and_inventory()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            using (var copy = new GameCopy(Uw2, "DATA", "SAVE1"))
            {
                var w = Ui.Open(Path.Combine(copy.Folder, "SAVE1"));
                var pw = w.ShowPlayer();
                Ui.Pump();
                var m = pw.Model;
                Assert.Equal("Karl", m.Name);
                Assert.NotNull(m.Portrait);
                Assert.Contains("Nothing the game would notice", m.WarningSummary);
                m.Strength = 30; m.Dexterity = 30;
                Ui.Pump();
                Assert.Contains(m.Warnings, x => x.Label == "GAME DETECTS" && x.Message.Contains("CHEATED"));
                Assert.Contains("cheating", m.WarningSummary);
                m.Strength = 25; m.Dexterity = 19;
                Assert.DoesNotContain(m.Warnings, x => x.Label == "GAME DETECTS");
                m.Skills[3].Value = 12;
                m.Recompute();
                // inventory: coins into the sack, a sword to the pack
                pw.ShowTab(1);
                Ui.Pump();
                m.Selected = m.Items.First(n => n.Item.ItemId == 0x80);
                m.NewItem = 0xA0;
                var coins = m.Add(true);
                Assert.NotNull(coins);
                m.Link = 99;
                m.NewItem = 0x04;
                var sword = m.Add(false);
                Assert.NotNull(sword);
                Assert.True(pw.Save(), m.Status);
                pw.Close();
                w.Close();
                var back = PlayerSheet.Load(Path.Combine(copy.Folder, "SAVE1", "PLAYER.DAT"), UltimateEditor.Games.GameKind.UW2, Path.Combine(copy.Folder, "DATA"));
                Assert.Equal(12, back.Skill(3));
                Assert.Equal(back.ExpectedMaxVitality, back.MaxVitality);
                Assert.Contains(back.Inventory.Top.First(t => t.ItemId == 0x80).Contents, c => c.ItemId == 0xA0 && c.Quantity == 99);
                Assert.Contains(back.Inventory.Top, t => t.ItemId == 0x04 && t.Slot >= PlayerInventory.PackFirst);
                Assert.True(File.Exists(Path.Combine(copy.Folder, "SAVE1", "PLAYER.DAT.BKP")));
            }
        }

        [AvaloniaFact]
        public void With_DATA_open_there_is_no_player_to_edit()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            Assert.Null(w.ShowPlayer());
            w.Close();
        }

        [AvaloniaFact]
        public void Graphics_window_exports_and_imports_a_sprite_and_refuses_a_wrong_size()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA", "CRIT", "CUTS"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                Ui.Pump();
                var m = gw.Model;
                Assert.Equal("OBJECTS.GR", m.FileName);
                Assert.Equal(464, m.PictureThumbs.Count);
                m.PictureThumb = m.PictureThumbs[1];
                string tmp = Path.Combine(copy.Folder, "pic.png");
                gw.ExportPicture(tmp);
                var r = m.ImportPicture(File.ReadAllBytes(tmp));
                Assert.True(r.Ok, r.Summary);
                Assert.Contains("index for index", r.Summary);
                var wrong = Png.EncodeRgba(3, 3, new byte[36]);
                r = m.ImportPicture(wrong);
                Assert.False(r.Ok);
                Assert.Contains("must stay", m.Status);
                m.SavePictures();
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "OBJECTS.GR.BKP")));
                // a texture file and palettes
                m.FileName = "W64.TR";
                Assert.Equal(m.Picture.Width, m.Picture.Height);
                m.PaletteIndex = 3;
                Assert.Equal(3, m.PaletteIndex);
                gw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Graphics_window_plays_critters_and_cutscenes_and_imports_their_frames()
        {
            if (Uw2 == null) return;
            using (var copy = new GameCopy(Uw2, "DATA", "CRIT", "CUTS"))
            {
                var w = Ui.Open(copy.Folder);
                var gw = w.ShowGraphics();
                gw.ShowTab(1);
                Ui.Pump();
                var m = gw.Model;
                Assert.Equal(32, m.Critters.Count);
                Assert.NotNull(m.CritterImage);
                Assert.NotEmpty(m.AuxNames);
                int before = m.FrameIndex;
                m.NextFrame();
                Assert.True(m.FrameIndex != before || m.Animation.Frames.Count == 1);
                var r = m.ImportFrame(m.CritterImage.ToPng());
                Assert.True(r.Ok, r.Summary);
                m.SaveCritter();
                Assert.Contains("Saved", m.Status);

                gw.ShowTab(2);
                Ui.Pump();
                Assert.NotNull(m.Lpf);
                Assert.NotNull(m.CutImage);
                m.CutFrame = 1;
                var frame = m.CutImage.Clone();
                for (int k = 0; k < 400; k++) frame.Pixels[k] = frame.Pixels[1000];
                r = m.ImportCutFrame(Png.EncodeIndexed(frame.Width, frame.Height, frame.Pixels, frame.Colors));
                Assert.True(r.Ok, r.Summary);
                Assert.True(m.Lpf.Changed);
                string path = m.Lpf.Path;
                m.SaveCutscene();
                var back = LpfAnimation.Load(path);
                Assert.Equal(frame.Pixels, back.Frames()[1]);
                int n = m.ExportAllCutFrames(Path.Combine(copy.Folder, "frames"));
                Assert.Equal(m.Lpf.FrameCount, Directory.GetFiles(Path.Combine(copy.Folder, "frames")).Length);
                gw.Close();
                w.Close();
            }
        }

        // ---- pictures

        [AvaloniaFact]
        public void Snapshot_of_the_player_window()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            var w = Ui.Open(Path.Combine(Uw2, "SAVE1"));
            var pw = w.ShowPlayer();
            pw.Width = 1180; pw.Height = 800;
            Ui.Pump();
            var frame = Ui.Capture(pw, Ui.Find<ListBox>(pw, "WarningList"));
            Expect(Snapshots.Check(Ui.BaselineDir, "player-window", frame, new Ui.Codec()));
            pw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Snapshot_of_the_graphics_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            var gw = w.ShowGraphics();
            gw.Width = 1280; gw.Height = 820;
            gw.ShowTab(1);
            Ui.Pump();
            var frame = Ui.Capture(gw, Ui.Find<ListBox>(gw, "CritterList"));
            Expect(Snapshots.Check(Ui.BaselineDir, "graphics-window", frame, new Ui.Codec()));
            gw.Close();
            w.Close();
        }

        static void Expect(SnapshotResult r)
        {
            if (r.Outcome == SnapshotOutcome.Recorded) Assert.Skip(r.Message);
            Assert.True(r.Outcome == SnapshotOutcome.Matched, r.Message);
        }
    }
}
