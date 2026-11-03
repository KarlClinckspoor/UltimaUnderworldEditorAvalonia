using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core.Music;
using Xunit;

namespace UwEditor.Core.Tests
{
    /// <summary>Music: the XMI and timbre library formats, XMI ↔ MIDI, the import and the validation against the game.</summary>
    public class MusicTests
    {
        static IEnumerable<string> XmiFiles(string game)
        {
            string g = GameData.Game(game);
            if (g == null) return new string[0];
            var lib = MusicLibrary.Open(g, game == "UW2" ? GameKind.UW2 : GameKind.UW1);
            return Directory.GetFiles(lib.SoundFolder, "*.XMI").OrderBy(f => f);
        }

        [Theory]
        [InlineData("UW", 24)]
        [InlineData("UW2", 34)]
        public void Every_shipped_theme_round_trips_byte_for_byte(string game, int count)
        {
            if (GameData.Game(game) == null) return;
            var files = XmiFiles(game).ToList();
            Assert.Equal(count, files.Count);
            foreach (var f in files)
            {
                var b = File.ReadAllBytes(f);
                var x = XmiFile.Parse(b);
                Assert.Equal(b, x.ToBytes());
                Assert.Single(x.Sequences);
                Assert.Equal(1, x.InfoCount);
                var ev = x.Events();
                Assert.True(ev.Last().IsEndOfTrack, f);
                // rebuilt from its timbres and events alone, the file is the same (EVNT, RBRN and the chunks)
                Assert.True(b.SequenceEqual(XmiFile.Build(x.Timbres, ev).ToBytes()), Path.GetFileName(f) + " rebuilt differs");
            }
        }

        [Fact]
        public void An_edited_theme_reads_back_as_edited()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var lib = MusicLibrary.Open(g, GameKind.UW1);
            var x = XmiFile.Parse(lib.Read(lib.Theme(9), MusicVersion.Fm));
            var ev = x.Events();
            var first = ev.First(e => e.IsNote);
            first.Data1 += 12;
            first.Duration = 300;
            var timb = x.Timbres.ToList();
            timb.Add(new XmiTimbre(5, 0));
            var y = XmiFile.Parse(XmiFile.Build(timb, ev).ToBytes());
            var back = y.Events().First(e => e.IsNote);
            Assert.Equal(first.Data1, back.Data1);
            Assert.Equal(300, back.Duration);
            Assert.Contains(new XmiTimbre(5, 0), y.Timbres);
            Assert.Equal(ev.Count, y.Events().Count);
        }

        [Fact]
        public void Timbre_libraries_round_trip_and_list_their_banks()
        {
            string g1 = GameData.Game("UW"), g2 = GameData.Game("UW2");
            if (g1 != null)
            {
                var lib = MusicLibrary.Open(g1, GameKind.UW1);
                foreach (var n in new[] { "UW.AD", "UW.MT" })
                {
                    var b = File.ReadAllBytes(lib.PathOf(n));
                    var t = TimbreLibrary.Parse(b);
                    Assert.True(t.Packed);
                    Assert.Equal(b, t.ToBytes());
                }
                Assert.Equal(128, lib.Fm.Patches(0).Count);
                Assert.Equal(24, lib.Fm.DrumKeys.Count);
                Assert.Equal(23, lib.Fm.Patches(1).Count);
                Assert.Equal(new List<int> { 1 }, lib.Mt.Banks);
                Assert.Equal(64, lib.Mt.Patches(1).Count);
                Assert.True(lib.Mt.IsMt32);
                Assert.False(lib.Fm.IsMt32);
                Assert.True(lib.Fm.Timbres.Where(t => t.Bank == 0).All(t => t.Size == 14));
                // an edit: a timbre's bytes change and the library rebuilds
                var e = TimbreLibrary.Parse(File.ReadAllBytes(lib.PathOf("UW.AD")));
                e.Find(0, 5).Data[5] ^= 0x3F;
                e.Changed();
                var back = TimbreLibrary.Parse(e.ToBytes());
                Assert.Equal(e.Find(0, 5).Data, back.Find(0, 5).Data);
                Assert.Equal(e.Timbres.Count, back.Timbres.Count);
            }
            if (g2 != null)
            {
                var lib = MusicLibrary.Open(g2, GameKind.UW2);
                foreach (var n in new[] { "UW.AD", "UW.OPL", "UW.MT", "SAMPLE.OPL" })
                {
                    var b = File.ReadAllBytes(lib.PathOf(n));
                    Assert.Equal(b, TimbreLibrary.Parse(b).ToBytes());
                }
                Assert.Equal(26, lib.Fm.DrumKeys.Count);
                Assert.Equal(32, lib.Fm.Patches(1).Count);
                Assert.NotNull(lib.Opl);
            }
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void Xmi_to_midi_to_xmi_keeps_every_event_of_every_theme(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var lib = MusicLibrary.Open(g, game == "UW2" ? GameKind.UW2 : GameKind.UW1);
            int n = 0;
            foreach (var t in lib.Themes)
                foreach (var v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
                {
                    var b = lib.Read(t, v);
                    var x = XmiFile.Parse(b);
                    var midiBytes = XmiToMidi.Convert(x).ToBytes();
                    var midi = MidiFile.Parse(midiBytes);
                    Assert.Equal(60, midi.Division);
                    Assert.Equal(0, midi.Format);
                    var r = MidiToXmi.Convert(midi, new MusicImportOptions { Version = v, MapGmToMt32 = false, FmTimbres = lib.Fm });
                    var want = x.Events().Select(e => e.ToString()).ToList();
                    var got = r.Xmi.Events().Select(e => e.ToString()).ToList();
                    Assert.Equal(want.Count, got.Count);
                    for (int i = 0; i < want.Count; i++) Assert.True(want[i] == got[i], t.File(v) + " event " + i + ": " + want[i] + " / " + got[i]);
                    Assert.Equal(x.Timbres, r.Xmi.Timbres);
                    Assert.True(b.SequenceEqual(r.Bytes), t.File(v) + ": not the same bytes");
                    // the notes in the MIDI file are where the XMI has them, in 120 Hz ticks
                    var ons = midi.Tracks[0].Events.Where(e => e.IsNoteOn).Select(e => e.Tick).ToList();
                    Assert.Equal(x.Events().Where(e => e.IsNote).Select(e => (long)e.Tick).ToList(), ons);
                    n++;
                }
            Assert.Equal(game == "UW2" ? 34 : 24, n);
        }

        [Theory]
        [InlineData("UW")]
        [InlineData("UW2")]
        public void The_shipped_themes_pass_the_validation(string game)
        {
            string g = GameData.Game(game); if (g == null) return;
            var lib = MusicLibrary.Open(g, game == "UW2" ? GameKind.UW2 : GameKind.UW1);
            foreach (var t in lib.Themes)
                foreach (var v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
                {
                    var issues = lib.Validate(t, v);
                    Assert.True(!issues.Any(i => i.Severity == MusicSeverity.Error), t.File(v) + ": " + string.Join(" | ", issues));
                }
            // UW1 theme 1 opens nine FORs at once (only four are kept, harmlessly); UW2 theme 25's FM file uses channel 1, which no driver plays
            if (game == "UW") Assert.Contains(lib.Validate(lib.Theme(1), MusicVersion.Fm), i => i.Text.Contains("ignores the rest"));
            else Assert.Contains(lib.Validate(lib.Theme(25), MusicVersion.Fm), i => i.Severity == MusicSeverity.Warning && i.Text.Contains("channel 1"));
        }

        // ---- a MIDI file built here

        /// <summary>A format 1 file at 96 ticks a beat: track 0 the tempo map and the loop markers, track 1 the music in running status.</summary>
        static byte[] TestMidi(Action<List<byte>> music, Action<List<byte>> conductor = null, int ppqn = 96)
        {
            var o = new List<byte>();
            o.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 1, 0, 2, (byte)(ppqn >> 8), (byte)ppqn });
            var t0 = new List<byte>();
            if (conductor != null) conductor(t0);
            else
            {
                Tempo(t0, 0, 500000);
                Marker(t0, 96, "loopStart");          // beat 1
                Tempo(t0, 288, 250000);               // beat 4: twice as fast
                Marker(t0, 384, "loopEnd");           // beat 8
            }
            t0.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
            Track(o, t0);
            var t1 = new List<byte>();
            music(t1);
            t1.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
            Track(o, t1);
            return o.ToArray();
        }

        static void Track(List<byte> o, List<byte> t)
        {
            o.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k', (byte)(t.Count >> 24), (byte)(t.Count >> 16), (byte)(t.Count >> 8), (byte)t.Count });
            o.AddRange(t);
        }

        static void Vlq(List<byte> o, long v)
        {
            var s = new Stack<byte>();
            s.Push((byte)(v & 0x7F));
            while ((v >>= 7) > 0) s.Push((byte)(0x80 | (v & 0x7F)));
            o.AddRange(s);
        }

        static void Tempo(List<byte> o, int delta, int us) { Vlq(o, delta); o.AddRange(new byte[] { 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us }); }
        static void Marker(List<byte> o, int delta, string text) { Vlq(o, delta); o.AddRange(new byte[] { 0xFF, 0x06, (byte)text.Length }); o.AddRange(System.Text.Encoding.ASCII.GetBytes(text)); }
        static void Ev(List<byte> o, int delta, params int[] bytes) { Vlq(o, delta); foreach (int b in bytes) o.Add((byte)b); }

        /// <summary>
        /// The music: channel 1 (which the game does not play: it moves) a program change, a bank
        /// select and notes in running status ended by velocity-0 note-ons; drums on channel 10.
        /// Beats 0–3 at 500,000 µs (60 ticks), from beat 4 at 250,000 µs (30 ticks).
        /// </summary>
        static void Song(List<byte> o, int drumKey = 42)
        {
            Ev(o, 0, 0xB0, 0, 0);                         // a GM bank select of 0: dropped
            Ev(o, 0, 0xC0, 40);                           // violin
            Ev(o, 0, 0x90, 60, 100);                      // note on, beat 0 to beat 1
            Ev(o, 96, 60, 0);                             // running status, velocity 0: off
            Ev(o, 0, 64, 90);                             // beat 1 to beat 5 (crosses the tempo change)
            Ev(o, 0, 0x99, 36, 110);                      // drums: bass drum, beat 1
            Ev(o, 48, 0x89, 36, 0);
            Ev(o, 0, 0x99, drumKey, 80);                  // a hi-hat (or the key asked for) from beat 1.5 to beat 2
            Ev(o, 48, drumKey, 0);
            Ev(o, 288, 0x80, 64, 0);                      // the channel 1 note off at beat 5
            Ev(o, 0, 0x90, 67, 100);                      // beat 5 to beat 8
            Ev(o, 288, 0x80, 67, 64);
        }

        static MusicLibrary Uw1() { string g = GameData.Game("UW"); return g == null ? null : MusicLibrary.Open(g, GameKind.UW1); }

        [Fact]
        public void A_midi_file_imports_with_its_timing_loops_programs_and_drums()
        {
            var lib = Uw1(); if (lib == null) return;
            var midi = TestMidi(o => Song(o));
            var theme = lib.Theme(2);
            var r = lib.Convert(theme, midi, new MusicImportOptions { Version = MusicVersion.Fm });
            Assert.False(r.HasErrors, string.Join(" | ", r.Notes.Concat(r.Validation)));
            var ev = r.Xmi.Events();
            var notes = ev.Where(e => e.IsNote).ToList();
            // channel 1 moved to channel 2; beats are 60 ticks, then 30 after the tempo change at beat 4 (tick 240)
            var mel = notes.Where(e => e.Channel != 9).ToList();
            Assert.All(mel, e => Assert.Equal(1, e.Channel));
            Assert.Equal(new[] { 0, 60, 270 }, mel.Select(e => e.Tick));
            Assert.Equal(new[] { 60, 210, 90 }, mel.Select(e => e.Duration));
            var drums = notes.Where(e => e.Channel == 9).ToList();
            Assert.Equal(new[] { 60, 90 }, drums.Select(e => e.Tick));
            Assert.Equal(new[] { 30, 30 }, drums.Select(e => e.Duration));
            Assert.Contains(ev, e => e.IsProgram && e.Data1 == 40 && e.Channel == 1);
            Assert.DoesNotContain(ev, e => e.IsController && e.Data1 == 0);
            // the markers became FOR at beat 1 and NEXT at beat 8 (60 and 240 + 4 × 30)
            var forE = ev.Single(e => e.IsController && e.Data1 == 116);
            var nextE = ev.Single(e => e.IsController && e.Data1 == 117);
            Assert.Equal(60, forE.Tick); Assert.Equal(0, forE.Data2);
            Assert.Equal(360, nextE.Tick); Assert.True(nextE.Data2 >= 64);
            Assert.True(ev.IndexOf(forE) < ev.IndexOf(notes.First(e => e.Tick == 60)), "the FOR comes before the notes of its tick");
            Assert.Equal(new[] { new XmiTimbre(40, 0), new XmiTimbre(36, 127), new XmiTimbre(42, 127) }, r.Xmi.Timbres);
            Assert.Contains(r.Notes, n => n.Text.Contains("Channel 1 moved to channel 2"));
            Assert.True(ev.Last().IsEndOfTrack);
            Assert.Equal(360, ev.Last().Tick);

            // the MT-32 version: the program goes through the GM → MT-32 map (violin 40 → Violin 1, 52)
            var m = lib.Convert(theme, midi, new MusicImportOptions { Version = MusicVersion.Mt32 });
            Assert.False(m.HasErrors, string.Join(" | ", m.Notes.Concat(m.Validation)));
            Assert.Contains(m.Xmi.Events(), e => e.IsProgram && e.Data1 == 52);
            Assert.Equal("Violin 1", Mt32Programs.Mt32Name(Mt32Programs.Map(40)));

            // UW2 takes it too
            string g2 = GameData.Game("UW2");
            if (g2 != null)
            {
                var lib2 = MusicLibrary.Open(g2, GameKind.UW2);
                var r2 = lib2.Convert(lib2.Theme(8), midi, new MusicImportOptions { Version = MusicVersion.Fm });
                Assert.False(r2.HasErrors, string.Join(" | ", r2.Notes.Concat(r2.Validation)));
            }
        }

        [Fact]
        public void Midi_details_running_status_overlaps_smpte_and_loop_options()
        {
            // same key twice at once: the first note-off ends the first note
            var midi = TestMidi(o =>
            {
                Ev(o, 0, 0x91, 60, 100);
                Ev(o, 30, 60, 100);
                Ev(o, 30, 0x81, 60, 0);
                Ev(o, 60, 60, 0);
            }, o => Tempo(o, 0, 500000), 60);
            var r = MidiToXmi.Convert(MidiFile.Parse(midi), new MusicImportOptions());
            var notes = r.Xmi.Events().Where(e => e.IsNote).ToList();
            Assert.Equal(new[] { 0, 30 }, notes.Select(e => e.Tick));
            Assert.Equal(new[] { 60, 90 }, notes.Select(e => e.Duration));
            Assert.DoesNotContain(r.Xmi.Events(), e => e.IsController && e.Data1 == 116);

            // loop the whole theme; a controller 111 (RPG Maker) instead starts a loop where it is
            var whole = MidiToXmi.Convert(MidiFile.Parse(midi), new MusicImportOptions { LoopWholeTheme = true }).Xmi.Events();
            Assert.Equal(0, whole.Single(e => e.IsController && e.Data1 == 116).Tick);
            Assert.Equal(120, whole.Single(e => e.IsController && e.Data1 == 117).Tick);
            var rpg = TestMidi(o => { Ev(o, 0, 0x91, 60, 100); Ev(o, 30, 0xB1, 111, 0); Ev(o, 30, 0x81, 60, 0); }, o => Tempo(o, 0, 500000), 60);
            var re = MidiToXmi.Convert(MidiFile.Parse(rpg), new MusicImportOptions()).Xmi.Events();
            Assert.Equal(30, re.Single(e => e.IsController && e.Data1 == 116).Tick);
            Assert.DoesNotContain(re, e => e.IsController && e.Data1 == 111);

            // SMPTE: 25 frames of 40 ticks = 1000 ticks a second
            var smpte = new List<byte>();
            smpte.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0xE7, 40 });
            var t = new List<byte>();
            Ev(t, 0, 0x92, 50, 90); Ev(t, 500, 0x82, 50, 0); t.AddRange(new byte[] { 0, 0xFF, 0x2F, 0 });
            Track(smpte, t);
            var s = MidiToXmi.Convert(MidiFile.Parse(smpte.ToArray()), new MusicImportOptions()).Xmi.Events().Single(e => e.IsNote);
            Assert.Equal(60, s.Duration);
        }

        static bool Refused(List<KeyValuePair<MusicVersion, MusicImportResult>> l) { return l.Any(kv => kv.Value.HasErrors); }

        [Fact]
        public void Imports_that_break_the_games_limits_are_refused_or_warned()
        {
            var lib = Uw1(); if (lib == null) return;
            var theme = lib.Theme(2);
            var opt = new MusicImportOptions();

            // too big: 2,000 notes take more than 6,300 bytes
            var big = TestMidi(o => { for (int i = 0; i < 2000; i++) { Ev(o, 0, 0x91, 40 + i % 40, 100); Ev(o, 10, 0x81, 40 + i % 40, 0); } }, o => Tempo(o, 0, 500000));
            var r = lib.Import(theme, big, null, true, true, opt);
            Assert.True(Refused(r));
            Assert.Contains(r[0].Value.Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("6,300"));
            Assert.False(lib.HasChanges);

            // a missing timbre: bank 5 (controller 114) has no program 3 in UW.AD
            var bank = TestMidi(o => { Ev(o, 0, 0xB1, 114, 5); Ev(o, 0, 0xC1, 3); Ev(o, 0, 0x91, 60, 100); Ev(o, 60, 0x81, 60, 0); }, o => Tempo(o, 0, 500000));
            var rb = lib.Convert(theme, bank, opt);
            Assert.Contains(new XmiTimbre(3, 5), rb.Xmi.Timbres);
            Assert.Contains(rb.Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("timbre 5:3"));
            var rbm = lib.Convert(theme, bank, new MusicImportOptions { Version = MusicVersion.Mt32, MapGmToMt32 = false });
            Assert.Contains(rbm.Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("UW.MT lacks"));

            // a missing drum note: UW1's bank 127 has no cowbell (56): refused as it is, replaced when asked
            var cow = TestMidi(o => Song(o, 56));
            var rc = lib.Convert(theme, cow, new MusicImportOptions { ReplaceMissingDrums = false });
            Assert.Contains(rc.Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("drum key 56"));
            var rr = lib.Convert(theme, cow, new MusicImportOptions());
            Assert.False(rr.HasErrors, string.Join(" | ", rr.Validation));
            Assert.Contains(rr.Notes, i => i.Severity == MusicSeverity.Warning && i.Text.Contains("Drum key 56 (Cowbell)"));

            // too many notes: a 40-note chord over four channels
            var chord = TestMidi(o =>
            {
                for (int i = 0; i < 40; i++) Ev(o, 0, 0x91 + i % 4, 30 + i, 90);
                for (int i = 0; i < 40; i++) Ev(o, i == 0 ? 120 : 0, 0x81 + i % 4, 30 + i, 0);
            }, o => Tempo(o, 0, 500000));
            var rn = lib.Convert(theme, chord, opt);
            Assert.Contains(rn.Validation, i => i.Severity == MusicSeverity.Error && i.Text.StartsWith("40 notes sound at once"));

            // loops nested five deep: the fifth FOR is ignored, so its NEXT goes back to the fourth one's place
            var deep = TestMidi(o => { Ev(o, 0, 0x91, 60, 100); Ev(o, 600, 0x81, 60, 0); }, o =>
            {
                Tempo(o, 0, 500000);
                for (int i = 0; i < 5; i++) Marker(o, 10, "loopStart 2");
                for (int i = 0; i < 5; i++) Marker(o, 10, "loopEnd");
            }, 60);
            var rd = lib.Convert(theme, deep, opt);
            Assert.Equal(5, rd.Xmi.Events().Count(e => e.IsController && e.Data1 == 116 && e.Data2 == 2));
            Assert.Contains(rd.Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("nest at most 4 deep"));
            var four = TestMidi(o => { Ev(o, 0, 0x91, 60, 100); Ev(o, 600, 0x81, 60, 0); }, o =>
            {
                Tempo(o, 0, 500000);
                for (int i = 0; i < 4; i++) Marker(o, 10, "loopStart 2");
                for (int i = 0; i < 4; i++) Marker(o, 10, "loopEnd");
            }, 60);
            Assert.DoesNotContain(lib.Convert(theme, four, opt).Validation, i => i.Severity == MusicSeverity.Error);

            // an endless loop in the victory theme, which the game waits for: refused; in a walking theme: fine
            var loopOpt = new MusicImportOptions { LoopWholeTheme = true };
            var simple = TestMidi(o => { Ev(o, 0, 0x91, 60, 100); Ev(o, 96, 0x81, 60, 0); }, o => Tempo(o, 0, 500000));
            Assert.Contains(lib.Convert(lib.Theme(9), simple, loopOpt).Validation, i => i.Severity == MusicSeverity.Error && i.Text.Contains("waits for theme 9"));
            Assert.DoesNotContain(lib.Convert(theme, simple, loopOpt).Validation, i => i.Severity == MusicSeverity.Error);

            // channel 1 kept (not moved) is warned about
            var one = TestMidi(o => { Ev(o, 0, 0x90, 60, 100); Ev(o, 96, 0x80, 60, 0); }, o => Tempo(o, 0, 500000));
            var stay = lib.Convert(theme, one, new MusicImportOptions { MoveChannels = false });
            Assert.Contains(stay.Validation, i => i.Severity == MusicSeverity.Warning && i.Text.Contains("channel 1"));
        }

        [Fact]
        public void Import_into_a_game_copy_saves_keeps_the_original_and_reverts()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-music-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "DATA"));
                Directory.CreateDirectory(Path.Combine(dir, "sound"));          // lower case: found anyway
                foreach (var f in Directory.GetFiles(Path.Combine(g, "SOUND"))) File.Copy(f, Path.Combine(dir, "sound", Path.GetFileName(f)));
                var lib = MusicLibrary.Open(dir, GameKind.UW1);
                var t = lib.Theme(3);
                var before = lib.OnDisk(t, MusicVersion.Fm);
                var r = lib.Import(t, TestMidi(o => Song(o)), null, true, true, new MusicImportOptions());
                Assert.False(Refused(r));
                Assert.True(lib.HasChanges);
                Assert.Equal(before, lib.OnDisk(t, MusicVersion.Fm));                     // nothing written yet
                var written = lib.Save();
                Assert.Equal(2, written.Count);
                Assert.False(lib.HasChanges);
                Assert.Equal(r[0].Value.Bytes, File.ReadAllBytes(lib.PathOf("AW03.XMI")));
                Assert.Equal(r[1].Value.Bytes, File.ReadAllBytes(lib.PathOf("UW03.XMI")));
                Assert.Equal(before, File.ReadAllBytes(lib.PathOf("AW03.XMI") + ".ORIG"));
                Assert.True(lib.Revert(t, MusicVersion.Fm));
                lib.Save();
                Assert.Equal(before, File.ReadAllBytes(lib.PathOf("AW03.XMI")));
                // export all
                Assert.Equal(24, lib.ExportAll(Path.Combine(dir, "midi")));
                Assert.True(MidiFile.LooksLikeMidi(File.ReadAllBytes(Path.Combine(dir, "midi", "AW03.mid"))));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
