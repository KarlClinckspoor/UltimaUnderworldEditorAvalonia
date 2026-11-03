using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UltimateEditor.Sound;
using UwEditor.Core.Sound;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Sound and music: the Speech and Effects tabs, importing a WAV file and saving it as the game's VOC.</summary>
    public class SoundWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from, params string[] dirs)
            {
                foreach (var d in new[] { "DATA", "SOUND", "CUTS" }.Concat(dirs))
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

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        /// <summary>Saves a capture when SOUND_SHOTS names a folder (to look at the window).</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("SOUND_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        /// <summary>A stereo 16-bit 44.1 kHz WAV file of a falling tone.</summary>
        static string Wav(string folder, double seconds)
        {
            int rate = 44100, n = (int)(seconds * rate);
            var l = new float[n]; var r = new float[n];
            for (int i = 0; i < n; i++) { double t = (double)i / rate; l[i] = (float)(0.4 * Math.Sin(2 * Math.PI * (800 - 200 * t) * t)); r[i] = l[i] * 0.5f; }
            string p = Path.Combine(folder, "tone-" + seconds + ".wav");
            File.WriteAllBytes(p, new WavFile(rate, new[] { l, r }).ToBytes(16));
            return p;
        }

        [AvaloniaFact]
        public void The_window_imports_a_wav_and_saves_the_games_voc_in_both_games()
        {
            foreach (var game in new[] { Uw1, Uw2 }.Where(g => g != null))
                using (var copy = new GameCopy(game))
                {
                    var w = OpenQuiet(copy.Folder);
                    bool uw2 = w.ViewModel.Session.IsUw2;
                    var sw = w.ShowSound();
                    sw.Width = 900; sw.Height = 600;
                    Ui.Pump();
                    Assert.Same(sw, w.ShowSound());
                    Assert.Equal(new[] { "Speech", "Effects", "Music" }, sw.Tabs.Items.Cast<TabItem>().Select(t => (string)t.Header));
                    Assert.False(sw.HasChanges);
                    Assert.Equal(uw2 ? 8 : 42, sw.SpeechList.ItemCount);
                    var rows = sw.EffectList.ItemsSource.Cast<EffectRow>().ToList();
                    Assert.Equal(uw2 ? 49 : 24, rows.Count(r => !r.IsTrap));
                    if (uw2) Assert.Contains(rows, r => r.IsTrap && r.Number == 100);
                    var first = sw.SpeechList.ItemsSource.Cast<SpeechRow>().First();
                    Assert.False(string.IsNullOrWhiteSpace(first.Line));
                    Shot(sw, (uw2 ? "uw2" : "uw1") + "-speech");
                    sw.Tabs.SelectedIndex = 1;
                    sw.EffectList.SelectedIndex = uw2 ? 1 : 3;
                    Ui.Pump();
                    Shot(sw, (uw2 ? "uw2" : "uw1") + "-effects");

                    // playing works or says why (no sound device here), and nothing breaks
                    string played = sw.Play(first.Name);
                    Assert.False(string.IsNullOrEmpty(played));
                    sw.StopPlaying();

                    // export, then import a generated WAV over a speech file
                    string wavOut = Path.Combine(copy.Folder, "out.wav");
                    Assert.Null(sw.ExportWav(first.Name, wavOut));
                    var exported = WavFile.Load(wavOut);
                    Assert.Equal(uw2 ? 11111 : 12048, exported.SampleRate);
                    var r1 = sw.ImportSpeech(first.Item.Number, Wav(copy.Folder, 2));
                    Assert.True(r1.Ok, r1.Problem);
                    Assert.True(sw.HasChanges);
                    Assert.Contains(sw.SpeechList.ItemsSource.Cast<SpeechRow>(), r => r.Name == first.Name && r.State == "unsaved");
                    // UW1: a line over 98,304 bytes is refused
                    if (!uw2)
                    {
                        var tooLong = sw.ImportSpeech(first.Item.Number, Wav(copy.Folder, 9));
                        Assert.False(tooLong.Ok);
                        Assert.True(tooLong.TooLong);
                        Assert.Contains("not imported", sw.Status);
                    }
                    if (uw2)
                    {
                        // a MIDI effect made digital, with its SPnn.VOC; a new trap sound
                        Assert.False(rows.First(r => r.Number == 13).Effect.Digital);
                        sw.SetDigital(13, true);
                        Assert.Contains("SP13.VOC", sw.Status);
                        Assert.True(sw.ImportEffect(13, Wav(copy.Folder, 0.5)).Ok);
                        Assert.True(sw.ImportTrapSound(7, Wav(copy.Folder, 1)).Ok);
                        Assert.Contains(sw.EffectList.ItemsSource.Cast<EffectRow>(), r => r.IsTrap && r.Number == 107);
                        sw.SetEffect(13, "Priority", 700);
                    }
                    else sw.SetEffect(3, "Note", 52);

                    Assert.True(sw.SaveAll(), sw.Status);
                    Assert.False(sw.HasChanges);
                    string snd = SoundLibrary.FindDir(copy.Folder, "SOUND");
                    var voc = VocFile.Load(Path.Combine(snd, first.Name));
                    Assert.Null(voc.ShapeProblem);
                    Assert.Equal(uw2 ? 0xA6 : 0xAD, voc.TimeConstant);
                    Assert.InRange(voc.Samples8().Length / (double)voc.SampleRate, 1.9, 2.05);
                    Assert.True(File.Exists(Path.Combine(snd, first.Name + ".ORIG")));
                    var dat = SoundsDat.Parse(File.ReadAllBytes(Path.Combine(snd, "SOUNDS.DAT")), uw2);
                    if (uw2)
                    {
                        Assert.True(dat.Effects[13].Digital);
                        Assert.Equal(700, dat.Effects[13].Priority);
                        var sp13 = File.ReadAllBytes(Path.Combine(snd, "SP13.VOC"));
                        Assert.True(sp13.Length < 65536);
                        Assert.Null(VocFile.Parse(sp13).ShapeProblem);
                        Assert.True(File.Exists(Path.Combine(snd, "UW07.VOC")));
                    }
                    else Assert.Equal(52, dat.Effects[3].Note);

                    // revert to the original, saved
                    sw.Revert(first.Name);
                    Assert.True(sw.SaveAll());
                    Assert.Equal(File.ReadAllBytes(Path.Combine(game, "SOUND", first.Name)), File.ReadAllBytes(Path.Combine(snd, first.Name)));
                    sw.Close();
                    Ui.Pump();
                    w.Close();
                }
        }

        [AvaloniaFact]
        public void A_saved_game_uses_the_game_folders_sound_files()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            using (var copy = new GameCopy(Uw2, "SAVE1"))
            {
                var w = OpenQuiet(Path.Combine(copy.Folder, "SAVE1"));
                Assert.True(w.ViewModel.Session.IsSave);
                var sw = w.ShowSound();
                Ui.Pump();
                Assert.Equal(Path.Combine(copy.Folder, "SOUND"), sw.Library.SoundFolder);
                Assert.Equal(49, sw.EffectList.ItemsSource.Cast<EffectRow>().Count(r => !r.IsTrap));
                Assert.Equal(8, sw.SpeechList.ItemCount);
                sw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void A_panel_added_to_the_window_takes_part_in_its_unsaved_work()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1))
            {
                var w = OpenQuiet(copy.Folder);
                var sw = w.ShowSound();
                bool dirty = true, saved = false;
                sw.AddPanel("Extra", new TextBlock { Text = "extra" }, () => dirty, () => { saved = true; dirty = false; return true; });
                Assert.Equal(4, sw.Tabs.ItemCount);
                Assert.True(sw.HasChanges);
                Assert.True(sw.SaveAll());
                Assert.True(saved);
                Assert.False(sw.HasChanges);
                sw.Close();
                w.Close();
            }
        }
    }
}
