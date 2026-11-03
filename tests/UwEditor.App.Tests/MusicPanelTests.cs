using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UltimateEditor.Sound;
using UwEditor.Core.Music;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The music panel in a bare window: the themes, a MIDI imported into a game copy and saved, a refused import, export and revert.</summary>
    public class MusicPanelTests
    {
        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-music-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                foreach (var d in new[] { "DATA", "SOUND" })
                {
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(Path.Combine(from, d))) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("MUSIC_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        /// <summary>A small MIDI file: format 0, 96 ticks a beat, a program and two notes on channel 1, a drum on channel 10.</summary>
        static byte[] Midi(int drumKey)
        {
            var t = new List<byte>
            {
                0, 0xFF, 0x51, 3, 0x07, 0xA1, 0x20,
                0, 0xC0, 48,
                0, 0x90, 60, 100,
                0, 0x99, (byte)drumKey, 90,
                0x60, 0x80, 60, 0,
                0, 0x89, (byte)drumKey, 0,
                0, 0x90, 64, 100,
                0x60, 0x80, 64, 0,
                0, 0xFF, 0x2F, 0,
            };
            var o = new List<byte> { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0, 96, (byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, (byte)t.Count };
            o.AddRange(t);
            return o.ToArray();
        }

        [AvaloniaTheory]
        [InlineData("UW", 12, 3, "AW03.XMI", "UW03.XMI")]
        [InlineData("UW2", 17, 8, "UWA10.XMI", "UWR10.XMI")]
        public void The_panel_imports_a_midi_into_a_game_copy_and_saves(string game, int themes, int number, string fmFile, string mtFile)
        {
            string g = GameData.Game(game); if (g == null) return;
            using (var copy = new GameCopy(g))
            {
                var w = Ui.Open(copy.Folder);
                w.AskBeforeDiscarding = false;
                var panel = new MusicPanel(w);
                var host = new Window { Width = 900, Height = 600, Content = panel };
                host.Show();
                Ui.Pump();
                Assert.Equal(themes, panel.Rows.Count);
                Assert.All(panel.Rows, r => Assert.DoesNotContain("error", r.Status));
                Assert.StartsWith(Path.GetFileNameWithoutExtension(panel.Library.Themes[0].FmFile), panel.Rows[0].Fm);
                panel.SelectedTheme = panel.Library.Theme(number);
                Ui.Pump();
                Assert.Contains("Theme " + number + ":", panel.ReportText);
                Assert.Contains("Timbres (bank:patch)", panel.ReportText);
                Shot(host, "music-" + game);

                string sound = panel.Library.SoundFolder;
                var fmBefore = File.ReadAllBytes(Path.Combine(sound, fmFile));
                // a refused import: UW.AD has no cowbell (56) and replacing drums is off
                panel.ReplaceDrumsBox.IsChecked = false;
                Assert.False(panel.ImportMidi(Midi(56), null, "cowbell.mid"));
                Assert.False(panel.HasChanges);
                Assert.Contains("refused", panel.ReportText);
                Assert.Contains("drum key 56", panel.ReportText);
                // the same with the drum replaced, both versions
                panel.ReplaceDrumsBox.IsChecked = true;
                Assert.True(panel.ImportMidi(Midi(56), null, "song.mid"));
                Ui.Pump();
                Assert.True(panel.HasChanges);
                Assert.Contains("changed", panel.Rows.First(r => r.Theme.Number == number).Status);
                Assert.Contains("Drum key 56 (Cowbell)", panel.ReportText);
                Assert.Equal(fmBefore, File.ReadAllBytes(Path.Combine(sound, fmFile)));        // not written before saving
                Shot(host, "music-" + game + "-imported");

                // export the imported theme as MIDI: it is the song
                string mid = Path.Combine(copy.Folder, "out.mid");
                Assert.True(panel.ExportMidi(MusicVersion.Mt32, mid));
                var back = MidiFile.Parse(File.ReadAllBytes(mid));
                Assert.Contains(back.Tracks[0].Events, e => e.Kind == 0xC0 && e.Data1 == Mt32Programs.Map(48));

                Assert.True(panel.SaveAll());
                Assert.False(panel.HasChanges);
                var fmNow = XmiFile.Parse(File.ReadAllBytes(Path.Combine(sound, fmFile)));
                Assert.Equal(2, fmNow.Events().Count(e => e.IsNote && e.Channel == 1));
                Assert.Contains(fmNow.Events(), e => e.IsProgram && e.Data1 == 48);
                Assert.True(File.Exists(Path.Combine(sound, fmFile + ".ORIG")));
                Assert.True(File.Exists(Path.Combine(sound, mtFile + ".ORIG")));
                Assert.DoesNotContain("error", panel.Rows.First(r => r.Theme.Number == number).Status);

                // revert: back to the shipped files on the next save
                Assert.True(panel.Revert());
                Assert.True(panel.HasChanges);
                Assert.True(panel.SaveAll());
                Assert.Equal(fmBefore, File.ReadAllBytes(Path.Combine(sound, fmFile)));

                Assert.Equal(themes * 2, panel.ExportAll(Path.Combine(copy.Folder, "midi")));
                host.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Without_a_game_the_panel_is_empty()
        {
            var w = Ui.Open(null);
            var panel = new MusicPanel(w);
            var host = new Window { Width = 900, Height = 600, Content = panel };
            host.Show();
            Ui.Pump();
            Assert.Empty(panel.Rows);
            Assert.False(panel.HasChanges);
            Assert.True(panel.SaveAll());
            host.Close();
            w.Close();
        }
    }
}
