using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using Underworld.Mods;
using UwEditor.Core.Mods;
using UwEditor.Core.Sound;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>The sound formats (VOC, WAV, SOUNDS.DAT), the WAV → VOC conversion and its limits, and the sound library's saving.</summary>
    public class SoundTests
    {
        static IEnumerable<string> Games() { return new[] { "UW", "UW2" }.Select(GameData.Game).Where(g => g != null); }

        static string SoundDir(string game) { return SoundLibrary.FindDir(game, "SOUND"); }

        [Fact]
        public void Every_shipped_voc_reads_and_writes_back_byte_for_byte_and_the_one_block_ones_rebuild_from_their_samples()
        {
            int files = 0, rebuilt = 0;
            foreach (var g in Games())
                foreach (var f in Directory.GetFiles(SoundDir(g), "*.VOC"))
                {
                    var b = File.ReadAllBytes(f);
                    var v = VocFile.Parse(b);
                    Assert.Equal(b, v.ToBytes());
                    Assert.True(v.SampleRate > 4000, Path.GetFileName(f));
                    files++;
                    if (v.ShapeProblem != null) continue;
                    var again = VocFile.FromSamples(v.Samples8(), v.TimeConstant).ToBytes();
                    Assert.Equal(b, again);
                    rebuilt++;
                }
            if (files == 0) return;
            // SP18.VOC starts with an extended block (8): read, kept, but not of the game's plain shape
            Assert.True(rebuilt >= files - 1, rebuilt + " of " + files);
        }

        [Fact]
        public void Shipped_rates_are_the_games()
        {
            var uw1 = GameData.Game("UW");
            if (uw1 != null) Assert.Equal(12048, VocFile.Load(Path.Combine(SoundDir(uw1), "00.VOC")).SampleRate);
            var uw2 = GameData.Game("UW2");
            if (uw2 == null) return;
            Assert.Equal(11111, VocFile.Load(Path.Combine(SoundDir(uw2), "SP01.VOC")).SampleRate);
            var sp18 = VocFile.Load(Path.Combine(SoundDir(uw2), "SP18.VOC"));
            Assert.Equal(8, sp18.Blocks[0].Type);
            Assert.NotNull(sp18.ShapeProblem);
            Assert.Equal(25601, sp18.Samples8().Length);
        }

        [Fact]
        public void Sounds_dat_round_trips_and_edits_with_the_length_high_byte_first()
        {
            foreach (var g in Games())
            {
                bool uw2 = g.EndsWith("UW2");
                var b = File.ReadAllBytes(Path.Combine(SoundDir(g), "SOUNDS.DAT"));
                var d = SoundsDat.Parse(b, uw2);
                Assert.Equal(b, d.ToBytes());
                Assert.Equal(uw2 ? 49 : 24, d.Effects.Count);
                Assert.Equal(384, d.Effects[11].Length);              // stored 01 80
                Assert.Equal(1, d.Effects[11].Bytes[3]);
                Assert.Equal(SoundEffectNames.Table(uw2).Count, d.Effects.Count);
                if (uw2)
                {
                    Assert.True(d.Effects[1].Digital);
                    Assert.Equal(1005, d.Effects[3].Priority);         // 5 + 5 × 200
                }
                var e = d.Clone();
                e.Effects[5].Patch = 7; e.Effects[5].Note = 61; e.Effects[5].Volume = 99; e.Effects[5].Length = 0x1234;
                if (uw2) { e.Effects[13].Digital = true; e.Effects[13].Priority = 1234; }
                var back = SoundsDat.Parse(e.ToBytes(), uw2);
                Assert.Equal(7, back.Effects[5].Patch);
                Assert.Equal(61, back.Effects[5].Note);
                Assert.Equal(99, back.Effects[5].Volume);
                Assert.Equal(0x1234, back.Effects[5].Length);
                Assert.Equal(0x12, back.Effects[5].Bytes[3]);
                if (uw2) { Assert.True(back.Effects[13].Digital); Assert.Equal(1234, back.Effects[13].Priority); Assert.Equal(34, back.Effects[13].Bytes[6]); Assert.Equal(6, back.Effects[13].Bytes[7]); }
                Assert.Equal(b.Length, back.ToBytes().Length);
            }
        }

        static float[] Sine(int n, double freq, double rate, double amp = 0.5)
        {
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / rate));
            return s;
        }

        [Fact]
        public void Wav_files_write_and_read_back_unchanged_in_8_and_16_bits()
        {
            var w = new WavFile(22050, new[] { Sine(1001, 440, 22050), Sine(1001, 660, 22050, 0.3) });
            foreach (int bits in new[] { 8, 16 })
            {
                var b = w.ToBytes(bits);
                var r = WavFile.Parse(b);
                Assert.Equal(2, r.ChannelCount);
                Assert.Equal(22050, r.SampleRate);
                Assert.Equal(1001, r.Length);
                Assert.Equal(bits, r.Bits);
                Assert.Equal(b, r.ToBytes(bits));
                Assert.True(Math.Abs(r.Channels[0][100] - w.Channels[0][100]) < (bits == 8 ? 0.01 : 0.0001));
            }
            // a VOC's 8-bit samples come back exactly
            var raw = Enumerable.Range(0, 300).Select(i => (byte)(i * 7)).ToArray();
            var back = WavFile.Parse(WavFile.FromUnsigned8(raw, 12048).ToBytes(8));
            Assert.Equal(raw, back.Channels[0].Select(v => (byte)Math.Round(v * 128 + 128)).ToArray());
        }

        static byte[] HandWav(int format, int bits, int channels, int rate, Func<int, int, byte[]> sample, int n, bool extensible = false)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int bytes = bits / 8;
            w.Write("RIFF".ToCharArray()); w.Write(0); w.Write("WAVE".ToCharArray());
            w.Write("LIST".ToCharArray()); w.Write(3); w.Write(new byte[] { 1, 2, 3, 0 });   // an odd chunk with its pad byte
            w.Write("fmt ".ToCharArray()); w.Write(extensible ? 40 : 16);
            w.Write((short)(extensible ? 0xFFFE : format)); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * bytes); w.Write((short)(channels * bytes)); w.Write((short)bits);
            if (extensible) { w.Write((short)22); w.Write((short)bits); w.Write(0); w.Write((short)format); w.Write(new byte[14]); }
            w.Write("data".ToCharArray()); w.Write(n * channels * bytes);
            for (int i = 0; i < n; i++) for (int c = 0; c < channels; c++) w.Write(sample(i, c));
            w.Flush();
            return ms.ToArray();
        }

        [Fact]
        public void Wav_reading_takes_24_and_32_bit_pcm_float_and_extensible_files()
        {
            var w24 = WavFile.Parse(HandWav(1, 24, 1, 48000, (i, c) => { int v = i == 1 ? -4194304 : 4194304; return new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16) }; }, 4));
            Assert.Equal(0.5f, w24.Channels[0][0], 4);
            Assert.Equal(-0.5f, w24.Channels[0][1], 4);
            var w32 = WavFile.Parse(HandWav(1, 32, 2, 8000, (i, c) => BitConverter.GetBytes(c == 0 ? int.MinValue / 4 : int.MaxValue / 2), 3));
            Assert.Equal(-0.25f, w32.Channels[0][2], 4);
            Assert.Equal(0.5f, w32.Channels[1][2], 4);
            Assert.Equal(0.125f, w32.Mono()[0], 4);
            var wf = WavFile.Parse(HandWav(3, 32, 1, 44100, (i, c) => BitConverter.GetBytes(0.75f), 2, true));
            Assert.True(wf.IsFloat);
            Assert.Equal(0.75f, wf.Channels[0][1], 5);
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(HandWav(2, 16, 1, 8000, (i, c) => new byte[2], 2)));   // ADPCM
            Assert.Throws<InvalidDataException>(() => WavFile.Parse(new byte[] { 1, 2, 3 }));
        }

        static WavFile Clip(double seconds, int rate = 44100, int channels = 2, double lead = 0)
        {
            int n = (int)(seconds * rate), l = (int)(lead * rate);
            var chs = new float[channels][];
            for (int c = 0; c < channels; c++)
            {
                chs[c] = new float[l + n + l];
                var s = Sine(n, 1000, rate, 0.3);
                Array.Copy(s, 0, chs[c], l, n);
            }
            return new WavFile(rate, chs);
        }

        [Fact]
        public void A_wav_becomes_a_game_voc_at_the_games_rate()
        {
            var r = SoundConversion.Convert(Clip(3), SoundUse.Uw1Speech, new SoundConvertOptions());
            Assert.True(r.Ok, r.Problem);
            Assert.Equal(0xAD, r.TimeConstant);
            Assert.InRange(r.Seconds, 2.95, 3.05);
            var v = VocFile.Parse(r.Voc);
            Assert.Null(v.ShapeProblem);
            Assert.Equal(0x1A, v.DataOffset);
            Assert.Equal(0x10A, v.Version);
            Assert.Equal(12048, v.SampleRate);
            Assert.Equal(r.Samples.Length + VocFile.Overhead, r.Voc.Length);
            // the 1 kHz tone keeps its pitch: about 2 rising zero crossings a millisecond... 1000 a second
            int cross = 0;
            for (int i = 1; i < r.Samples.Length; i++) if (r.Samples[i - 1] < 128 && r.Samples[i] >= 128) cross++;
            Assert.InRange(cross, 2900, 3100);
            // normalised near full scale
            Assert.InRange(r.Samples.Max(), 245, 255);
            var r2 = SoundConversion.Convert(Clip(1, 22050, 1), SoundUse.Uw2Effect, new SoundConvertOptions());
            Assert.Equal(0xA6, r2.TimeConstant);
            Assert.Equal(11111, VocFile.Parse(r2.Voc).SampleRate);
            // the same input gives the same file (the dither's seed is fixed)
            Assert.Equal(r2.Voc, SoundConversion.Convert(Clip(1, 22050, 1), SoundUse.Uw2Effect, new SoundConvertOptions()).Voc);
        }

        [Fact]
        public void Silence_is_trimmed_and_a_chosen_rate_is_kept_under_22050()
        {
            var r = SoundConversion.Convert(Clip(1, 44100, 1, lead: 1.5), SoundUse.Uw2Effect, new SoundConvertOptions { TimeConstant = VocFile.TimeConstantFor(22050) });
            Assert.True(r.Ok, r.Problem);
            Assert.InRange(r.Seconds, 1.0, 1.1);
            Assert.InRange(r.Rate, 21000, 22050);
            Assert.Contains(r.Notes, n => n.StartsWith("trimmed"));
            var off = SoundConversion.Convert(Clip(1, 44100, 1, lead: 1.5), SoundUse.Uw2Effect, new SoundConvertOptions { TrimSilence = false });
            Assert.InRange(off.Seconds, 3.9, 4.1);
        }

        [Fact]
        public void Clips_over_the_limit_are_refused_shortened_or_lowered_in_rate()
        {
            // UW1 speech: at most 98,304 bytes read
            var longClip = Clip(10, 22050, 1);
            var refused = SoundConversion.Convert(longClip, SoundUse.Uw1Speech, new SoundConvertOptions());
            Assert.False(refused.Ok);
            Assert.True(refused.TooLong);
            Assert.Contains("10.00 s", refused.Problem);
            var cut = SoundConversion.Convert(longClip, SoundUse.Uw1Speech, new SoundConvertOptions { OverLimit = OverLimit.Shorten });
            Assert.True(cut.Ok);
            Assert.Equal(SoundLimits.Uw1SpeechFile, cut.Voc.Length);
            Assert.Null(SoundLimits.Check(SoundUse.Uw1Speech, cut.Voc.Length));
            Assert.NotNull(SoundLimits.Check(SoundUse.Uw1Speech, cut.Voc.Length + 1));
            var low = SoundConversion.Convert(longClip, SoundUse.Uw1Speech, new SoundConvertOptions { OverLimit = OverLimit.LowerRate });
            Assert.True(low.Ok, low.Problem);
            Assert.True(low.Rate < 12048 && low.Rate > 9000, low.Rate.ToString());
            Assert.True(low.Voc.Length <= SoundLimits.Uw1SpeechFile);
            Assert.InRange(low.Seconds, 9.9, 10.05);
            // UW2 effects and trap sounds: under 65,536 bytes
            var fx = SoundConversion.Convert(Clip(7, 22050, 1), SoundUse.Uw2Trap, new SoundConvertOptions());
            Assert.True(fx.TooLong);
            var fxCut = SoundConversion.Convert(Clip(7, 22050, 1), SoundUse.Uw2Trap, new SoundConvertOptions { OverLimit = OverLimit.Shorten });
            Assert.Equal(65535, fxCut.Voc.Length);
            Assert.NotNull(SoundLimits.Check(SoundUse.Uw2Effect, 65536));
            // far too long even at the lowest rate
            var tooLow = SoundConversion.Convert(Clip(30, 8000, 1), SoundUse.Uw2Effect, new SoundConvertOptions { OverLimit = OverLimit.LowerRate });
            Assert.False(tooLow.Ok);
            // UW2 cutscene speech is streamed: no limit
            var big = SoundConversion.Convert(Clip(20, 22050, 1), SoundUse.Uw2Speech, new SoundConvertOptions());
            Assert.True(big.Ok);
            Assert.True(big.Voc.Length > 200000);
        }

        [Fact]
        public void Every_speech_file_goes_with_a_cutscene_line()
        {
            var uw1 = GameData.Game("UW");
            if (uw1 != null)
            {
                var lib = new SoundLibrary(uw1, GameKind.UW1);
                var speech = lib.Speech();
                Assert.Equal(42, lib.Lines.Count);
                Assert.All(lib.Lines, l => Assert.Equal(0, l.Cutscene));
                Assert.All(speech, s => Assert.NotEmpty(s.Lines));      // every shipped nn.VOC is said
                Assert.All(speech, s => Assert.True(lib.Exists(s.Name), s.Name));
                Assert.Equal(26, lib.Lines.First(l => l.String == 0).Voc);
                Assert.Equal(98304 - 33, SoundLimits.MaxSamples(SoundUse.Uw1Speech));
                Assert.All(speech, s => Assert.Null(lib.Info(s.Name, SoundUse.Uw1Speech).Problem));
                var strings = GameStrings.Load(Path.Combine(uw1, "DATA"));
                Assert.False(string.IsNullOrWhiteSpace(strings.Get(0xC00, 0)));
            }
            var uw2 = GameData.Game("UW2");
            if (uw2 != null)
            {
                var lib = new SoundLibrary(uw2, GameKind.UW2);
                var map = lib.Lines.ToDictionary(l => l.Voc, l => l.Cutscene);
                Assert.Equal(new Dictionary<int, int> { { 0, 24 }, { 2, 25 }, { 3, 26 }, { 4, 27 }, { 5, 4 }, { 6, 5 }, { 7, 6 }, { 12, 7 } }, map);
                Assert.Equal(3, lib.Lines.First(l => l.Voc == 0).SilentFollowers.Count);
                Assert.Equal(8, lib.Speech().Count);
                var strings = GameStrings.Load(Path.Combine(uw2, "DATA"));
                Assert.False(string.IsNullOrWhiteSpace(strings.Get(0xC00 + 4, 0)));
            }
        }

        [Fact]
        public void New_and_replaced_sound_files_are_saved_carried_by_mods_and_reverted()
        {
            var g = GameData.Game("UW2");
            if (g == null) return;
            using (var c = new EditingTests.Copy(g, "SOUND", "CUTS"))
            {
                var lib = new SoundLibrary(c.Folder, GameKind.UW2);
                var tone = SoundConversion.Convert(Clip(0.5, 22050, 1), SoundUse.Uw2Trap, new SoundConvertOptions());
                lib.Put("UW05.VOC", tone.Voc);
                lib.Put("SP01.VOC", tone.Voc);
                lib.Effects.Effects[13].Digital = true;
                Assert.True(lib.HasChanges);
                Assert.Contains(5, lib.TrapFiles());
                var done = lib.SaveAll();
                Assert.Equal(3, done.Count);
                Assert.False(lib.HasChanges);
                string snd = SoundLibrary.FindDir(c.Folder, "SOUND");
                Assert.Equal(tone.Voc, File.ReadAllBytes(Path.Combine(snd, "UW05.VOC")));
                Assert.Empty(File.ReadAllBytes(Path.Combine(snd, "UW05.VOC.ORIG")));          // the mark of a new file
                Assert.Equal(File.ReadAllBytes(Path.Combine(g, "SOUND", "SP01.VOC")), File.ReadAllBytes(Path.Combine(snd, "SP01.VOC.ORIG")));
                Assert.True(new SoundLibrary(c.Folder, GameKind.UW2).Effects.Effects[13].Digital);
                // Make a mod carries the new trap sound (UW2 opens UWnn.VOC by number), the replaced one and SOUNDS.DAT
                var changes = EditorMods.Collect(c.Folder, GameKind.UW2, null);
                Assert.Empty(changes.Problems);
                var mod = ModMaker.Make(changes, "t", "T", "1", "", null, "");
                Assert.Contains(mod.Files, x => x.Path == "SOUND/UW05.VOC" && x.Added);
                Assert.Contains(mod.Files, x => x.Path == "SOUND/SP01.VOC" && !x.Added);
                Assert.Contains(mod.Files, x => x.Path == "SOUND/SOUNDS.DAT" && x.Delta.Length < 64);
                using (var clean = new EditingTests.Copy(g, "SOUND"))
                {
                    Assert.All(EditorMods.Check(mod, clean.Folder), x => Assert.Equal(FileState.Original, x.State));
                    EditorMods.Apply(mod, clean.Folder, GameKind.UW2);
                    Assert.Equal(tone.Voc, File.ReadAllBytes(Path.Combine(clean.Folder, "SOUND", "UW05.VOC")));
                    foreach (var f in mod.Files)
                        Assert.Equal(File.ReadAllBytes(GamePaths.Find(c.Folder, f.Path)), File.ReadAllBytes(GamePaths.Find(clean.Folder, f.Path)));
                    // the added file keeps the editor's mark of a new file, so a mod made from this copy carries it too
                    Assert.Contains(EditorMods.Collect(clean.Folder, GameKind.UW2, null).Changed, x => x.Path == "SOUND/UW05.VOC" && x.Original == null);
                }
                // revert: the new file goes, the replaced one comes back
                lib.Revert("UW05.VOC");
                lib.Revert("SP01.VOC");
                Assert.False(lib.Exists("UW05.VOC"));
                lib.SaveAll();
                Assert.False(File.Exists(Path.Combine(snd, "UW05.VOC")));
                Assert.False(File.Exists(Path.Combine(snd, "UW05.VOC.ORIG")));
                Assert.Equal(File.ReadAllBytes(Path.Combine(g, "SOUND", "SP01.VOC")), File.ReadAllBytes(Path.Combine(snd, "SP01.VOC")));
            }
        }

        [Fact]
        public void Uw2_special_effect_traps_name_their_sound_files()
        {
            var g = GameData.Game("UW2");
            if (g == null) return;
            var s = GameSession.Open(g);
            var traps = SoundLibrary.FindTraps(s, s.LoadLevel);
            var lib = SoundLibrary.For(s);
            Assert.All(traps, t => Assert.InRange(t.Owner, 0, 63));
            Assert.Equal(new[] { 0, 1 }, lib.TrapFiles());
            // every trap sound the levels play is shipped
            Assert.All(traps, t => Assert.True(lib.Exists(t.Name), t.ToString() + " plays " + t.Name));
            Assert.NotEmpty(SoundEffectNames.ForCreatures(s, Editing.ObjectTables.Load(s), 1));
        }
    }
}
