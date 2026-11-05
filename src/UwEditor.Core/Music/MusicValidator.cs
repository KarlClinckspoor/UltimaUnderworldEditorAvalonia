using System;
using System.Collections.Generic;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;

namespace UwEditor.Core.Music
{
    public enum MusicSeverity { Info, Warning, Error }

    /// <summary>A finding about a theme: an error (the import is refused), a warning or a note.</summary>
    public sealed class MusicIssue
    {
        public MusicSeverity Severity { get; set; }
        public string Text { get; set; }

        public static MusicIssue Error(string t) { return new MusicIssue { Severity = MusicSeverity.Error, Text = t }; }
        public static MusicIssue Warn(string t) { return new MusicIssue { Severity = MusicSeverity.Warning, Text = t }; }
        public static MusicIssue Note(string t) { return new MusicIssue { Severity = MusicSeverity.Info, Text = t }; }

        public override string ToString()
        {
            return (Severity == MusicSeverity.Error ? "Error: " : Severity == MusicSeverity.Warning ? "Warning: " : "") + Text;
        }
    }

    /// <summary>What a theme file is checked against: the game, the version, the theme and the timbre libraries.</summary>
    public sealed class MusicTarget
    {
        public GameKind Kind { get; set; }
        public MusicVersion Version { get; set; }
        /// <summary>The theme (null: none in particular).</summary>
        public MusicTheme Theme { get; set; }
        /// <summary>UW.AD.</summary>
        public TimbreLibrary Fm { get; set; }
        /// <summary>UW2's UW.OPL (the Sound Blaster Pro 2 driver's library), or null.</summary>
        public TimbreLibrary Opl { get; set; }
        /// <summary>UW.MT.</summary>
        public TimbreLibrary Mt { get; set; }
    }

    /// <summary>
    /// Checks a theme against what the game and AIL's drivers can take (the requirements of the
    /// audio study, claude/audio-study.md section 2). Errors: the file is too big for the music
    /// buffer (it is not checked: a bigger file overwrites memory, uw1 SOUND.C read_file_to_mbuf),
    /// a TIMB timbre the library lacks (load_new_music fails and the music is off for the session),
    /// more than 32 notes sounding at once (the sequencer's note queue: the 33rd overwrites the
    /// first entry and that note never stops, xmidi.c XMIDI_note_on), a NEXT that closes a FOR
    /// the sequencer ignored (FOR loops nest 4 deep: xmidi.c XMIDI_control), an endless loop in a
    /// theme the game waits for, and no end-of-track event. Warnings and notes: channels the
    /// drivers do not play (they take 2–10: yamaha.c and mt32.c min_true_chan, max_rec_chan),
    /// programs and drum keys missing from TIMB, the channels the effects lock (fx_play takes
    /// channels with AIL_lock_channel: among 2–9, the one with the fewest notes sounding, the
    /// highest on a tie, xmidi.c xmidi_lock_channel; the music's notes on a locked channel are
    /// dropped), the FM voices, the loops.
    /// </summary>
    public static class MusicValidator
    {
        public const int MaxNotes = 32;
        public const int ForNest = 4;

        public static List<MusicIssue> Validate(byte[] bytes, MusicTarget t)
        {
            var l = new List<MusicIssue>();
            if (bytes == null) { l.Add(MusicIssue.Error("The file is missing: the game needs both versions of every theme.")); return l; }
            XmiFile x;
            List<XmiEvent> ev;
            try { x = XmiFile.Parse(bytes); ev = x.Events(); }
            catch (Exception ex) { l.Add(MusicIssue.Error("Not an XMI file the game can read: " + ex.Message)); return l; }
            int limit = MusicThemes.SizeLimit(t.Kind);
            if (bytes.Length > limit)
                l.Add(MusicIssue.Error(string.Format("The file is {0:N0} bytes, more than the {1:N0} the game reads a theme into. The game does not check the length, so a bigger file overwrites memory.", bytes.Length, limit)));
            else
                l.Add(MusicIssue.Note(string.Format("{0:N0} of {1:N0} bytes.", bytes.Length, limit)));
            if (x.Sequences.Count != 1 || x.InfoCount > 1)
                l.Add(MusicIssue.Warn(string.Format("The file holds {0} sequences; the game plays only the first.", Math.Max(x.Sequences.Count, x.InfoCount))));
            if (!ev.Any(e => e.IsEndOfTrack))
                l.Add(MusicIssue.Error("No end-of-track event: the sequencer would play on past the theme."));

            var timb = x.Timbres;
            CheckTimbres(l, t, timb);
            CheckPrograms(l, t, ev, timb);
            CheckNotes(l, t, ev);
            CheckChannels(l, t, ev);
            CheckLoops(l, t, ev);
            return l;
        }

        static string Lib(MusicTarget t) { return t.Version == MusicVersion.Mt32 ? "UW.MT" : "UW.AD"; }

        static void CheckTimbres(List<MusicIssue> l, MusicTarget t, List<XmiTimbre> timb)
        {
            if (timb.Count == 0 && t.Version == MusicVersion.Fm)
                l.Add(MusicIssue.Warn("No TIMB timbres: the FM driver loads none for this theme, so its programs play only if an earlier theme loaded them."));
            var missing = new List<string>();
            var missingOpl = new List<string>();
            foreach (var tb in timb)
            {
                if (t.Version == MusicVersion.Mt32)
                {
                    // the MT-32 driver never asks for banks 0 (its built-in timbres) and 127 (its rhythm part): mt32.c request_filter
                    if (tb.Bank == 0 || tb.Bank == 127) continue;
                    if (t.Mt != null && !t.Mt.Contains(tb.Bank, tb.Patch)) missing.Add(Name(tb));
                }
                else
                {
                    if (t.Fm != null && !t.Fm.Contains(tb.Bank, tb.Patch)) missing.Add(Name(tb));
                    if (t.Opl != null && !t.Opl.Contains(tb.Bank, tb.Patch)) missingOpl.Add(Name(tb));
                }
            }
            if (missing.Count > 0)
                l.Add(MusicIssue.Error(string.Format("{0} lacks {1}: loading the theme fails and the game turns the music off for the rest of the session.", Lib(t), string.Join(", ", missing))));
            if (missingOpl.Count > 0)
                l.Add(MusicIssue.Error(string.Format("UW.OPL (the Sound Blaster Pro 2's library) lacks {0}: with that card the music goes off for the session.", string.Join(", ", missingOpl))));
            if (missing.Count == 0 && missingOpl.Count == 0 && timb.Count > 0)
            {
                int custom = timb.Count(b => b.Bank != 0 && b.Bank != 127);
                if (t.Version == MusicVersion.Mt32)
                    l.Add(MusicIssue.Note(string.Format("{0} timbres listed: {1}.", timb.Count, custom == 0 ? "all MT-32 built-in presets (bank 0) and rhythm keys" : custom + " from UW.MT, the rest built in")));
                else
                    l.Add(MusicIssue.Note(string.Format("{0} timbres listed, all in {1}.", timb.Count, t.Opl != null ? "UW.AD and UW.OPL" : "UW.AD")));
            }
        }

        static string Name(XmiTimbre tb)
        {
            if (tb.Bank == 127) return "drum key " + tb.Patch + " (" + MidiToXmi.DrumName(tb.Patch) + ")";
            return "timbre " + tb.Bank + ":" + tb.Patch;
        }

        static void CheckPrograms(List<MusicIssue> l, MusicTarget t, List<XmiEvent> ev, List<XmiTimbre> timb)
        {
            var bank = new int[16];
            var notListed = new SortedSet<string>();
            var drumsUnlisted = new SortedSet<int>();
            var drumsMissing = new SortedSet<int>();
            var mtRange = new SortedSet<int>();
            foreach (var e in ev)
            {
                if (e.IsController && e.Data1 == 114) bank[e.Channel] = e.Data2;
                if (e.IsProgram)
                {
                    var tb = new XmiTimbre(e.Data1, bank[e.Channel]);
                    bool builtIn = t.Version == MusicVersion.Mt32 && (tb.Bank == 0 || tb.Bank == 127);
                    if (!builtIn && !timb.Contains(tb)) notListed.Add(tb.Bank + ":" + tb.Patch);
                }
                if (e.IsNote && e.Channel == 9)
                {
                    if (t.Version == MusicVersion.Fm)
                    {
                        bool inLib = t.Fm == null || t.Fm.Contains(127, e.Data1);
                        bool listed = timb.Contains(new XmiTimbre(e.Data1, 127));
                        if (!inLib && !listed) drumsMissing.Add(e.Data1);
                        else if (inLib && !listed) drumsUnlisted.Add(e.Data1);
                    }
                    else if (e.Data1 < 35 || e.Data1 > 76) mtRange.Add(e.Data1);
                }
            }
            if (notListed.Count > 0)
                l.Add(MusicIssue.Warn("Programs not in TIMB: " + string.Join(", ", notListed) + ". The driver does not load them; they sound only if an earlier theme or effect loaded them."));
            if (drumsMissing.Count > 0)
                l.Add(MusicIssue.Error("UW.AD's drum bank (bank 127) lacks these drum keys, so they are silent: " + string.Join(", ", drumsMissing.Select(k => k + " (" + MidiToXmi.DrumName(k) + ")")) + "."));
            if (drumsUnlisted.Count > 0)
                l.Add(MusicIssue.Warn("Drum keys not in TIMB: " + string.Join(", ", drumsUnlisted) + ". They sound only if an earlier theme loaded them."));
            if (mtRange.Count > 0)
                l.Add(MusicIssue.Warn("Drum keys outside the MT-32's rhythm keys (35 to 76): " + string.Join(", ", mtRange) + ". An MT-32 plays nothing there."));
        }

        static void CheckNotes(List<MusicIssue> l, MusicTarget t, List<XmiEvent> ev)
        {
            // the note queue: a note takes an entry from its tick for max(duration, 1) ticks (xmidi.c: note_time = duration - 1, counted down before each tick's events)
            var marks = new List<KeyValuePair<int, int>>();
            var fmMarks = new List<KeyValuePair<int, int>>();
            foreach (var e in ev)
            {
                if (!e.IsNote) continue;
                int end = e.Tick + Math.Max(1, e.Duration);
                marks.Add(new KeyValuePair<int, int>(e.Tick, 1)); marks.Add(new KeyValuePair<int, int>(end, -1));
                if (e.Channel >= 1 && e.Channel <= 9) { fmMarks.Add(new KeyValuePair<int, int>(e.Tick, 1)); fmMarks.Add(new KeyValuePair<int, int>(end, -1)); }
            }
            int at;
            int most = Peak(marks, out at);
            if (most > MaxNotes)
                l.Add(MusicIssue.Error(string.Format("{0} notes sound at once at {1}: the sequencer holds {2}. The next one takes the first entry's place and that note never stops.", most, Time(at), MaxNotes)));
            else
                l.Add(MusicIssue.Note(string.Format("At most {0} notes at once (the sequencer holds {1}).", most, MaxNotes)));
            int fm = Peak(fmMarks, out at);
            if (t.Version == MusicVersion.Fm && fm > 9)
                l.Add(MusicIssue.Note(string.Format("Up to {0} notes at once: an OPL2 card has 9 voices (OPL3: 18), shared with the effects; the rest are not heard.", fm)));
            if (ev.Count > 0)
            {
                int end = ev.Max(e => e.Tick + (e.IsNote ? e.Duration : 0));
                l.Add(MusicIssue.Note(string.Format("{0} notes, {1} long.", ev.Count(e => e.IsNote), Time(end))));
            }
        }

        static int Peak(List<KeyValuePair<int, int>> marks, out int at)
        {
            marks.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));   // ends before starts at the same tick
            int cur = 0, most = 0;
            at = 0;
            foreach (var m in marks) { cur += m.Value; if (cur > most) { most = cur; at = m.Key; } }
            return most;
        }

        /// <summary>A tick count as minutes:seconds.</summary>
        public static string Time(int ticks)
        {
            double s = ticks / 120.0;
            return string.Format("{0}:{1:00.0}", (int)(s / 60), s % 60);
        }

        static void CheckChannels(List<MusicIssue> l, MusicTarget t, List<XmiEvent> ev)
        {
            var notes = ev.Where(e => e.IsNote).ToList();
            var silent = new SortedSet<int>(notes.Where(e => e.Channel == 0 || e.Channel >= 10).Select(e => e.Channel + 1));
            if (silent.Count > 0)
                l.Add(MusicIssue.Warn("Notes on channel " + string.Join(", ", silent) + ": the game's drivers play channels 2 to 10 only, so these are silent."));
            var melodic = new SortedSet<int>(notes.Where(e => e.Channel >= 1 && e.Channel <= 8).Select(e => e.Channel + 1));
            int fx = MusicThemes.EffectChannels(t.Kind);
            bool protects = ev.Any(e => e.IsController && e.Data1 == 111 && e.Data2 >= 64);
            if (melodic.Count > 8 - fx)
                l.Add(MusicIssue.Note(string.Format("Uses {0} of channels 2–9. The effects take up to {1} of them while they play (each time the one with the fewest notes sounding, the highest on a tie), and the music's notes there are dropped meanwhile{2}.",
                    melodic.Count, fx, protects ? "; controller 111 protects a channel from that" : "")));
            else
                l.Add(MusicIssue.Note(string.Format("Uses {0} of channels 2–9: the {1} channels the effects take stay free.", melodic.Count, fx)));
            if (ev.Any(e => e.IsController && e.Data1 == 110))
                l.Add(MusicIssue.Note("The theme locks channels itself (controller 110), so fewer are left for the effects."));
        }

        static void CheckLoops(List<MusicIssue> l, MusicTarget t, List<XmiEvent> ev)
        {
            // the FOR stack as the sequencer keeps it: four slots; a FOR with no free slot is ignored
            var stack = new List<KeyValuePair<XmiEvent, bool>>();   // FOR, ignored
            int active = 0, ignoredHarmless = 0, deepest = 0;
            bool endless = false, stray = false, broken = false;
            foreach (var e in ev)
            {
                if (!e.IsController) continue;
                if (e.Data1 == 116)
                {
                    bool ignored = active >= ForNest;
                    if (!ignored) active++;
                    stack.Add(new KeyValuePair<XmiEvent, bool>(e, ignored));
                    deepest = Math.Max(deepest, stack.Count);
                }
                else if (e.Data1 == 117 && e.Data2 >= 64)
                {
                    if (stack.Count == 0) { stray = true; continue; }
                    var top = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (top.Value)
                    {
                        if (!broken) l.Add(MusicIssue.Error(string.Format("The NEXT at {0} closes a FOR the sequencer ignored: FOR loops nest at most {1} deep, so it loops back to the wrong place.", Time(e.Tick), ForNest)));
                        broken = true;
                        continue;
                    }
                    active--;
                    if (top.Key.Data2 == 0) endless = true;
                }
            }
            ignoredHarmless = stack.Count(s => s.Value);
            if (stray) l.Add(MusicIssue.Warn("A NEXT without a FOR: the sequencer ignores it."));
            if (ignoredHarmless > 0 && !broken)
                l.Add(MusicIssue.Note(string.Format("{0} FOR controllers open at once; the sequencer keeps {1} and ignores the rest (no NEXT goes back to them, so it does not matter).", deepest, ForNest)));
            if (endless)
            {
                if (t.Theme != null && t.Theme.MustEnd)
                    l.Add(MusicIssue.Error(string.Format("The theme loops forever, but the game waits for theme {0} to end before it changes the music, so the music would stay on it.", t.Theme.Number)));
                else
                    l.Add(MusicIssue.Note("The theme loops forever (an endless FOR/NEXT) and never ends by itself; the game still changes it when another theme is asked for."));
            }
            else if (t.Theme != null)
                l.Add(MusicIssue.Note("The theme has no endless loop: when it ends, " + t.Theme.AtEnd + " (the game restarts or replaces themes itself)."));
        }
    }
}
