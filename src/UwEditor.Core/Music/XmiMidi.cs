using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateEditor.Games;
using UltimateEditor.Sound;

namespace UwEditor.Core.Music
{
    /// <summary>
    /// The editor's own sequencer-specific meta events (type 0x7F, manufacturer 0x7D, which MIDI
    /// keeps for non-commercial use, then "XMI" and a kind letter) that carry what a standard MIDI
    /// file cannot say, so that a theme exported and imported again is the same file: T the TIMB
    /// list, t an XMIDI tempo event (in XMIDI it only counts beats; the time is fixed at 120 ticks a
    /// second), E where the XMIDI end of track was when notes run past it. Other programs ignore
    /// sequencer-specific events.
    /// </summary>
    public static class XmiTags
    {
        public const byte Manufacturer = 0x7D;

        public static byte[] Make(char kind, byte[] payload)
        {
            var d = new byte[5 + (payload == null ? 0 : payload.Length)];
            d[0] = Manufacturer; d[1] = (byte)'X'; d[2] = (byte)'M'; d[3] = (byte)'I'; d[4] = (byte)kind;
            if (payload != null) Array.Copy(payload, 0, d, 5, payload.Length);
            return d;
        }

        /// <summary>The kind letter of an editor tag, or '\0'.</summary>
        public static char KindOf(MidiEvent e)
        {
            if (!e.IsMeta || e.MetaType != 0x7F || e.Data == null || e.Data.Length < 5) return '\0';
            if (e.Data[0] != Manufacturer || e.Data[1] != 'X' || e.Data[2] != 'M' || e.Data[3] != 'I') return '\0';
            return (char)e.Data[4];
        }

        public static byte[] Payload(MidiEvent e)
        {
            var p = new byte[e.Data.Length - 5];
            Array.Copy(e.Data, 5, p, 0, p.Length);
            return p;
        }
    }

    /// <summary>
    /// XMI → standard MIDI file: format 0, one track, 60 ticks a quarter note at 500,000 µs a
    /// quarter note, so one MIDI tick is one XMIDI tick (1/120 s) and the timing is exact. Each
    /// note becomes a note-on and a note-off its duration later (the note-offs of a tick come
    /// before its other events, as the sequencer turns expired notes off before it plays the
    /// tick's events: xmidi.c xmidi_serve). Controllers, AIL's included (110–120), stay
    /// controllers; a FOR (116) also gets a "loopStart" marker and a NEXT (117, value 64 or
    /// more) a "loopEnd" marker, the convention other tools read. The TIMB list and the XMIDI
    /// tempo events go into the editor's tags (<see cref="XmiTags"/>).
    /// </summary>
    public static class XmiToMidi
    {
        public const int Ppqn = 60;
        public const int Tempo = 500000;

        public static MidiFile Convert(XmiFile xmi)
        {
            var events = xmi.Events();
            var timb = xmi.Timbres;
            var keyed = new List<KeyValuePair<long, MidiEvent>>();       // sort key, event
            // key: tick << 24 | phase << 23 | sequence; phase 0: note-offs of earlier notes
            long seq = 0;
            Func<long, int, long> key = (tick, phase) => tick << 24 | (long)phase << 23 | (seq++ & 0x7FFFFF);
            int eot = -1;
            foreach (var e in events)
            {
                if (e.IsEndOfTrack) { eot = e.Tick; break; }
                if (e.IsNote)
                {
                    keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.Channel3(e.Tick, e.Status, e.Data1, e.Data2)));
                    int end = e.Tick + e.Duration;
                    keyed.Add(new KeyValuePair<long, MidiEvent>(key(end, e.Duration == 0 ? 1 : 0), MidiEvent.Channel3(end, 0x80 | e.Channel, e.Data1, 0)));
                    continue;
                }
                if (e.IsMeta)
                {
                    if (e.MetaType == 0x51)
                        keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.Meta(e.Tick, 0x7F, XmiTags.Make('t', e.Data))));
                    else
                        keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.Meta(e.Tick, e.MetaType, e.Data)));
                    continue;
                }
                if (e.IsSysEx)
                {
                    keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), new MidiEvent { Tick = e.Tick, Status = e.Status, Data = e.Data }));
                    continue;
                }
                keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.Channel3(e.Tick, e.Status, e.Data1, e.Data2)));
                if (e.IsController && e.Data1 == 116)
                    keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.TextMeta(e.Tick, 0x06, e.Data2 == 0 ? "loopStart" : "loopStart " + e.Data2)));
                if (e.IsController && e.Data1 == 117 && e.Data2 >= 64)
                    keyed.Add(new KeyValuePair<long, MidiEvent>(key(e.Tick, 1), MidiEvent.TextMeta(e.Tick, 0x06, "loopEnd")));
            }
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            var t = new MidiTrack();
            t.Events.Add(MidiEvent.Meta(0, 0x51, new byte[] { 0x07, 0xA1, 0x20 }));     // 500,000 µs
            var tp = new byte[timb.Count * 2];
            for (int i = 0; i < timb.Count; i++) { tp[2 * i] = timb[i].Patch; tp[2 * i + 1] = timb[i].Bank; }
            t.Events.Add(MidiEvent.Meta(0, 0x7F, XmiTags.Make('T', tp)));
            long last = 0;
            foreach (var k in keyed) { t.Events.Add(k.Value); last = Math.Max(last, k.Value.Tick); }
            if (eot < 0) eot = (int)last;
            if (eot < last)
            {
                // notes run past the XMIDI end: the MIDI file ends later, the tag says where the theme did
                int at = t.Events.FindIndex(1, e => e.Tick > eot);
                t.Events.Insert(at < 0 ? t.Events.Count : at, MidiEvent.Meta(eot, 0x7F, XmiTags.Make('E', null)));
            }
            t.Events.Add(MidiEvent.Meta(Math.Max(eot, last), 0x2F, null));
            var f = new MidiFile { Format = 0, Division = Ppqn };
            f.Tracks.Add(t);
            return f;
        }
    }

    /// <summary>How a MIDI file becomes a theme.</summary>
    public sealed class MusicImportOptions
    {
        /// <summary>The file being made: the FM one or the MT-32 one.</summary>
        public MusicVersion Version { get; set; }
        /// <summary>MT-32: map the programs from General MIDI to MT-32 presets (<see cref="Mt32Programs"/>); false keeps them (a MIDI made for the MT-32).</summary>
        public bool MapGmToMt32 { get; set; } = true;
        /// <summary>Channels the game does not play (1 and 11–16) move to unused channels 2–9 (not done for a file the editor exported).</summary>
        public bool MoveChannels { get; set; } = true;
        /// <summary>FM: drum keys bank 127 lacks become the nearest drum it has.</summary>
        public bool ReplaceMissingDrums { get; set; } = true;
        /// <summary>Wrap the whole theme in an endless FOR/NEXT loop (when it has no loop of its own).</summary>
        public bool LoopWholeTheme { get; set; }
        /// <summary>A controller 111 (RPG Maker's loop start) starts an endless loop to the end, when there are no other loop marks. In AIL, 111 is channel protection, so it is never kept.</summary>
        public bool Cc111LoopStart { get; set; } = true;
        /// <summary>The FM timbre library (UW.AD), for the drum keys.</summary>
        public TimbreLibrary FmTimbres { get; set; }

        public MusicImportOptions Clone() { return (MusicImportOptions)MemberwiseClone(); }
    }

    /// <summary>What an import made: the theme, its bytes and what the conversion did.</summary>
    public sealed class MusicImportResult
    {
        public XmiFile Xmi { get; set; }
        public byte[] Bytes { get; set; }
        /// <summary>What the conversion changed or dropped (warnings and notes).</summary>
        public List<MusicIssue> Notes { get; } = new List<MusicIssue>();
        /// <summary>The validation against the game (filled by the caller that knows the game).</summary>
        public List<MusicIssue> Validation { get; set; } = new List<MusicIssue>();
        public bool HasErrors { get { return Notes.Any(n => n.Severity == MusicSeverity.Error) || Validation.Any(n => n.Severity == MusicSeverity.Error); } }
    }

    /// <summary>
    /// A standard MIDI file → an XMI theme. Every track is merged by time; times go through the
    /// tempo map (or the SMPTE rate) to 120 Hz ticks; each note-on is paired with the first open
    /// note-off of its channel and key (a note-on of velocity 0 is a note-off; a note left open
    /// ends with the file) and becomes a note with its duration; MIDI tempo events set the
    /// timing (and stay as XMIDI tempo events, which count beats). Programs and controller 114
    /// (AIL's patch bank) make the TIMB list, with bank 127 for every drum key of channel 10;
    /// General MIDI bank selects (controllers 0 and 32) are dropped (the game has no such banks:
    /// bank 0 is used). Loops: controllers 116/117 stay; else "loopStart"/"loopEnd" markers become
    /// FOR/NEXT; else a controller 111 (RPG Maker) starts an endless loop to the end.
    /// </summary>
    public static class MidiToXmi
    {
        static readonly Regex LoopStart = new Regex(@"^\s*loop[\s_-]*start\b\D*(\d+)?", RegexOptions.IgnoreCase);
        static readonly Regex LoopEnd = new Regex(@"^\s*loop[\s_-]*end\b", RegexOptions.IgnoreCase);

        /// <summary>Meta events kept from other programs' files: text, channel and port prefix, SMPTE offset, time and key signatures (what the shipped themes have).</summary>
        static readonly int[] KeptMetas = { 0x01, 0x20, 0x21, 0x54, 0x58, 0x59 };

        sealed class Timed
        {
            public long Tick;
            public int Track, Index;
            public MidiEvent E;
            public int Xmi;
        }

        public static MusicImportResult Convert(MidiFile midi, MusicImportOptions opt)
        {
            var r = new MusicImportResult();
            if (midi.Format == 2) r.Notes.Add(MusicIssue.Warn("A format 2 MIDI file (independent patterns): its tracks are played together."));
            // merge the tracks by time
            var all = new List<Timed>();
            for (int t = 0; t < midi.Tracks.Count; t++)
                for (int i = 0; i < midi.Tracks[t].Events.Count; i++)
                    all.Add(new Timed { Tick = midi.Tracks[t].Events[i].Tick, Track = t, Index = i, E = midi.Tracks[t].Events[i] });
            all.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Track != b.Track ? a.Track.CompareTo(b.Track) : a.Index.CompareTo(b.Index));
            bool tagged = all.Any(x => XmiTags.KindOf(x.E) != '\0');
            bool hasForNext = all.Any(x => x.E.Kind == 0xB0 && (x.E.Data1 == 116 || x.E.Data1 == 117));
            bool hasMarkers = all.Any(x => IsLoopText(x.E));

            // times: 120 Hz ticks through the tempo map
            long tempo = 500000, lastTick = 0, acc = 0;    // acc: Σ ticks × µs a beat
            foreach (var x in all)
            {
                acc += (x.Tick - lastTick) * tempo;
                lastTick = x.Tick;
                if (midi.IsSmpte) x.Xmi = (int)((x.Tick * 120 * 2 + midi.SmpteTicksPerSecond) / (2L * midi.SmpteTicksPerSecond));
                else x.Xmi = (int)((acc * 120 * 2 + (long)midi.Division * 1000000) / (2L * midi.Division * 1000000));
                if (x.E.IsMeta && x.E.MetaType == 0x51 && x.E.Data != null && x.E.Data.Length >= 3)
                    tempo = Math.Max(1, x.E.Data[0] << 16 | x.E.Data[1] << 8 | x.E.Data[2]);
            }

            var outEv = new List<XmiEvent>();
            var open = new Dictionary<int, Queue<XmiEvent>>();
            List<XmiTimbre> stored = null;
            int endTick = 0, eotTag = -1, strayOffs = 0, gmBanks = 0, droppedMetas = 0, droppedSysex = 0, rpgLoop = -1;
            var loopMarks = new List<KeyValuePair<int, int>>();     // tick, repeat count (-1: loop end)
            foreach (var x in all)
            {
                var e = x.E;
                int t = x.Xmi;
                endTick = Math.Max(endTick, t);
                if (e.IsMeta)
                {
                    char tag = XmiTags.KindOf(e);
                    if (tag == 'T')
                    {
                        var p = XmiTags.Payload(e);
                        stored = new List<XmiTimbre>();
                        for (int i = 0; i + 1 < p.Length; i += 2) stored.Add(new XmiTimbre(p[i], p[i + 1]));
                        continue;
                    }
                    if (tag == 't') { outEv.Add(new XmiEvent { Tick = t, Status = 0xFF, MetaType = 0x51, Data = XmiTags.Payload(e) }); continue; }
                    if (tag == 'E') { eotTag = t; continue; }
                    if (tag != '\0') continue;
                    if (e.MetaType == 0x2F) continue;
                    if (e.MetaType == 0x51)
                    {
                        if (!tagged) outEv.Add(new XmiEvent { Tick = t, Status = 0xFF, MetaType = 0x51, Data = e.Data });
                        continue;
                    }
                    if (IsLoopText(e))
                    {
                        if (!hasForNext)
                        {
                            var m = LoopStart.Match(e.Text);
                            if (m.Success) loopMarks.Add(new KeyValuePair<int, int>(t, m.Groups[1].Success ? Math.Min(127, int.Parse(m.Groups[1].Value)) : 0));
                            else loopMarks.Add(new KeyValuePair<int, int>(t, -1));
                        }
                        continue;
                    }
                    if (tagged || Array.IndexOf(KeptMetas, (int)e.MetaType) >= 0) outEv.Add(new XmiEvent { Tick = t, Status = 0xFF, MetaType = e.MetaType, Data = e.Data });
                    else droppedMetas++;
                    continue;
                }
                if (e.IsSysEx)
                {
                    if (tagged || opt.Version == MusicVersion.Mt32) outEv.Add(new XmiEvent { Tick = t, Status = e.Status, Data = e.Data });
                    else droppedSysex++;
                    continue;
                }
                int ch = e.Channel;
                if (e.IsNoteOn)
                {
                    var n = new XmiEvent { Tick = t, Status = (byte)(0x90 | ch), Data1 = e.Data1, Data2 = e.Data2 };
                    int k = ch << 8 | e.Data1;
                    Queue<XmiEvent> q;
                    if (!open.TryGetValue(k, out q)) open[k] = q = new Queue<XmiEvent>();
                    q.Enqueue(n);
                    outEv.Add(n);
                    continue;
                }
                if (e.IsNoteOff)
                {
                    Queue<XmiEvent> q;
                    if (open.TryGetValue(ch << 8 | e.Data1, out q) && q.Count > 0) { var n = q.Dequeue(); n.Duration = t - n.Tick; }
                    else strayOffs++;
                    continue;
                }
                if (e.Kind == 0xB0 && (e.Data1 == 0 || e.Data1 == 32))
                {
                    if (e.Data2 != 0) gmBanks++;
                    continue;
                }
                if (e.Kind == 0xB0 && e.Data1 == 111 && opt.Cc111LoopStart && !hasForNext && !hasMarkers)
                {
                    if (rpgLoop < 0) rpgLoop = t;
                    continue;
                }
                outEv.Add(new XmiEvent { Tick = t, Status = e.Status, Data1 = e.Data1, Data2 = e.Data2 });
            }
            int eot = eotTag >= 0 ? eotTag : endTick;
            int unclosed = 0;
            foreach (var q in open.Values)
                while (q.Count > 0) { var n = q.Dequeue(); n.Duration = Math.Max(0, eot - n.Tick); unclosed++; }
            if (strayOffs > 0) r.Notes.Add(MusicIssue.Note(strayOffs + " note-off(s) without a note sounding were ignored."));
            if (unclosed > 0) r.Notes.Add(MusicIssue.Warn(unclosed + " note(s) never ended: they last to the end of the theme."));
            if (gmBanks > 0) r.Notes.Add(MusicIssue.Warn(gmBanks + " General MIDI bank select(s) (controllers 0 and 32) were dropped: the game has no such banks, bank 0's program plays."));
            if (droppedMetas > 0) r.Notes.Add(MusicIssue.Note(droppedMetas + " name, lyric or other text event(s) were left out (they only take room)."));
            if (droppedSysex > 0) r.Notes.Add(MusicIssue.Note(droppedSysex + " system exclusive message(s) were left out: the FM drivers ignore them."));

            if (opt.MoveChannels && !tagged) MoveChannels(outEv, r);
            if (opt.Version == MusicVersion.Mt32 && opt.MapGmToMt32)
            {
                int mapped = 0;
                foreach (var e in outEv)
                    if (e.IsProgram && e.Channel != 9) { e.Data1 = (byte)Mt32Programs.Map(e.Data1); mapped++; }
                if (mapped > 0) r.Notes.Add(MusicIssue.Note(mapped + " program change(s) mapped from General MIDI to the MT-32's presets."));
            }
            if (opt.Version == MusicVersion.Fm && opt.ReplaceMissingDrums && opt.FmTimbres != null) ReplaceDrums(outEv, opt.FmTimbres, r);

            // loops
            int loopChannel = outEv.Where(e => e.IsNote && e.Channel != 9).Select(e => e.Channel).DefaultIfEmpty(1).Min();
            var loops = new List<XmiEvent>();
            bool hasLoop = hasForNext;
            if (loopMarks.Count > 0)
            {
                foreach (var m in loopMarks)
                    loops.Add(m.Value < 0 ? Next(m.Key, loopChannel) : For(m.Key, loopChannel, m.Value));
                r.Notes.Add(MusicIssue.Note("Loop markers became AIL's FOR/NEXT controllers (116/117)."));
                hasLoop = true;
            }
            else if (rpgLoop >= 0)
            {
                loops.Add(For(rpgLoop, loopChannel, 0));
                loops.Add(Next(eot, loopChannel));
                r.Notes.Add(MusicIssue.Note("Controller 111 (RPG Maker's loop start) became an endless FOR/NEXT loop from there to the end."));
                hasLoop = true;
            }
            if (opt.LoopWholeTheme)
            {
                if (hasLoop) r.Notes.Add(MusicIssue.Note("The theme has loop marks of its own: they are kept and the whole theme is not looped."));
                else
                {
                    loops.Add(For(0, loopChannel, 0));
                    loops.Add(Next(eot, loopChannel));
                    r.Notes.Add(MusicIssue.Note("The whole theme loops endlessly (FOR at the start, NEXT at the end)."));
                }
            }
            // order: by tick; at a tick a NEXT first, then a FOR, then the rest in file order
            var keyed = new List<KeyValuePair<long, XmiEvent>>();
            long s = 0;
            foreach (var e in loops) keyed.Add(new KeyValuePair<long, XmiEvent>((long)e.Tick << 24 | (e.Data1 == 117 ? 0L : 1L) << 22 | s++, e));
            foreach (var e in outEv) keyed.Add(new KeyValuePair<long, XmiEvent>((long)e.Tick << 24 | 2L << 22 | s++, e));
            keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
            var events = keyed.Select(k => k.Value).ToList();
            events.Add(new XmiEvent { Tick = Math.Max(eot, events.Count > 0 ? events[events.Count - 1].Tick : 0), Status = 0xFF, MetaType = 0x2F, Data = new byte[0] });

            // TIMB: the stored list (an exported theme), then every program and drum key in order
            var timb = stored != null && !(opt.Version == MusicVersion.Mt32 && opt.MapGmToMt32) ? new List<XmiTimbre>(stored) : new List<XmiTimbre>();
            var bank = new int[16];
            foreach (var e in events)
            {
                if (e.IsController && e.Data1 == 114) bank[e.Channel] = e.Data2;
                XmiTimbre? need = null;
                if (e.IsProgram) need = new XmiTimbre(e.Data1, bank[e.Channel]);
                else if (e.IsNote && e.Channel == 9) need = new XmiTimbre(e.Data1, 127);
                if (need.HasValue && !timb.Contains(need.Value)) timb.Add(need.Value);
            }
            r.Xmi = XmiFile.Build(timb, events);
            r.Bytes = r.Xmi.ToBytes();
            return r;
        }

        static bool IsLoopText(MidiEvent e)
        {
            if (!e.IsMeta || (e.MetaType != 0x01 && e.MetaType != 0x06 && e.MetaType != 0x07)) return false;
            string t = e.Text;
            return LoopStart.IsMatch(t) || LoopEnd.IsMatch(t);
        }

        static XmiEvent For(int tick, int ch, int count) { return new XmiEvent { Tick = tick, Status = (byte)(0xB0 | ch), Data1 = 116, Data2 = (byte)count }; }
        static XmiEvent Next(int tick, int ch) { return new XmiEvent { Tick = tick, Status = (byte)(0xB0 | ch), Data1 = 117, Data2 = 127 }; }

        /// <summary>Channels 1 and 11–16 (which the game's drivers do not play: they take channels 2–10) go to unused channels 2–9.</summary>
        static void MoveChannels(List<XmiEvent> ev, MusicImportResult r)
        {
            var used = new SortedSet<int>(ev.Where(e => !e.IsMeta && !e.IsSysEx).Select(e => e.Channel));
            var free = new Queue<int>(Enumerable.Range(1, 8).Where(c => !used.Contains(c)));
            var map = new Dictionary<int, int>();
            foreach (int c in used)
            {
                if (c >= 1 && c <= 9) continue;
                if (free.Count > 0)
                {
                    int to = free.Dequeue();
                    map[c] = to;
                    r.Notes.Add(MusicIssue.Note(string.Format("Channel {0} moved to channel {1} (the game plays channels 2 to 10 only).", c + 1, to + 1)));
                }
                else r.Notes.Add(MusicIssue.Warn(string.Format("Channel {0} stays: no channel from 2 to 9 is free, and the game does not play it.", c + 1)));
            }
            if (map.Count == 0) return;
            foreach (var e in ev)
                if (!e.IsMeta && !e.IsSysEx && map.TryGetValue(e.Channel, out int to)) e.Status = (byte)(e.Status & 0xF0 | to);
        }

        /// <summary>General MIDI drum keys and the keys to try instead, best first.</summary>
        static readonly Dictionary<int, int[]> DrumFallback = new Dictionary<int, int[]>
        {
            { 35, new[] { 36 } }, { 36, new[] { 35 } }, { 37, new[] { 38, 40 } }, { 38, new[] { 40, 37 } }, { 39, new[] { 38, 40 } }, { 40, new[] { 38, 37 } },
            { 41, new[] { 43, 45, 47 } }, { 43, new[] { 41, 45, 47 } }, { 45, new[] { 47, 43, 48 } }, { 47, new[] { 45, 48, 43 } }, { 48, new[] { 50, 47, 45 } }, { 50, new[] { 48, 47 } },
            { 42, new[] { 44, 46 } }, { 44, new[] { 42, 46 } }, { 46, new[] { 42, 44 } },
            { 49, new[] { 57, 55, 52, 51 } }, { 52, new[] { 49, 57, 55 } }, { 55, new[] { 49, 57, 52 } }, { 57, new[] { 49, 55, 52 } },
            { 51, new[] { 59, 53, 49 } }, { 53, new[] { 51, 59 } }, { 59, new[] { 51, 53 } },
            { 54, new[] { 69, 70 } }, { 56, new[] { 67, 54, 75 } }, { 58, new[] { 69, 54 } },
            { 60, new[] { 61, 62 } }, { 61, new[] { 60, 64 } }, { 62, new[] { 63, 60 } }, { 63, new[] { 62, 64, 60 } }, { 64, new[] { 63, 61 } },
            { 65, new[] { 60, 62 } }, { 66, new[] { 61, 64 } }, { 67, new[] { 68, 56, 75 } }, { 68, new[] { 67, 56, 75 } },
            { 69, new[] { 70, 54 } }, { 70, new[] { 69, 54 } }, { 71, new[] { 72 } }, { 72, new[] { 71 } }, { 73, new[] { 74, 69 } }, { 74, new[] { 73, 69 } },
            { 75, new[] { 76, 77 } }, { 76, new[] { 77, 75 } }, { 77, new[] { 76, 75 } }, { 78, new[] { 79, 62 } }, { 79, new[] { 78, 64 } }, { 80, new[] { 81, 54 } }, { 81, new[] { 80, 54 } },
        };

        static void ReplaceDrums(List<XmiEvent> ev, TimbreLibrary lib, MusicImportResult r)
        {
            var have = lib.DrumKeys;
            if (have.Count == 0) return;
            var done = new SortedDictionary<int, int>();
            foreach (var e in ev)
            {
                if (!e.IsNote || e.Channel != 9 || have.Contains(e.Data1)) continue;
                int key = e.Data1, to;
                if (!done.TryGetValue(key, out to))
                {
                    to = -1;
                    if (DrumFallback.TryGetValue(key, out int[] tries))
                        foreach (int k in tries) if (have.Contains(k)) { to = k; break; }
                    if (to < 0) to = have.OrderBy(k => Math.Abs(k - key)).ThenBy(k => k).First();
                    done[key] = to;
                }
                e.Data1 = (byte)to;
            }
            foreach (var kv in done)
                r.Notes.Add(MusicIssue.Warn(string.Format("Drum key {0} ({1}) is not in UW.AD's drum bank: it plays key {2} ({3}).", kv.Key, DrumName(kv.Key), kv.Value, DrumName(kv.Value))));
        }

        static readonly string[] GmDrums =
        {
            "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare", "Hand Clap", "Electric Snare", "Low Floor Tom", "Closed Hi-Hat", "High Floor Tom", "Pedal Hi-Hat",
            "Low Tom", "Open Hi-Hat", "Low-Mid Tom", "Hi-Mid Tom", "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine",
            "Splash Cymbal", "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2", "Hi Bongo", "Low Bongo", "Mute Hi Conga", "Open Hi Conga", "Low Conga",
            "High Timbale", "Low Timbale", "High Agogo", "Low Agogo", "Cabasa", "Maracas", "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro",
            "Claves", "Hi Wood Block", "Low Wood Block", "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle",
        };

        /// <summary>The General MIDI name of a drum key (35–81), or "key n".</summary>
        public static string DrumName(int key) { return key >= 35 && key <= 81 ? GmDrums[key - 35] : "key " + key; }
    }
}
