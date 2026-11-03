using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Music
{
    /// <summary>
    /// A game's music in its SOUND folder: the themes' two files each, the timbre libraries,
    /// and the changed files waiting to be saved. Files are looked up whatever their case.
    /// Nothing is written until Save, which goes through SafeFile (NAME.XMI.ORIG kept once,
    /// NAME.XMI.BKP each time).
    /// </summary>
    public sealed class MusicLibrary
    {
        public GameKind Kind { get; private set; }
        /// <summary>The game's SOUND folder (null when there is none).</summary>
        public string SoundFolder { get; private set; }
        public List<MusicTheme> Themes { get; private set; }
        /// <summary>UW.AD (FM), or null when missing or unreadable.</summary>
        public TimbreLibrary Fm { get; private set; }
        /// <summary>UW2's UW.OPL, or null.</summary>
        public TimbreLibrary Opl { get; private set; }
        /// <summary>UW.MT, or null.</summary>
        public TimbreLibrary Mt { get; private set; }
        readonly Dictionary<string, byte[]> pending = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The music of the game in gameFolder (the folder holding DATA and SOUND).</summary>
        public static MusicLibrary Open(string gameFolder, GameKind kind)
        {
            var m = new MusicLibrary { Kind = kind, Themes = MusicThemes.For(kind) };
            m.SoundFolder = FindDir(gameFolder, "SOUND");
            m.Fm = ReadLib(m, "UW.AD");
            m.Mt = ReadLib(m, "UW.MT");
            if (kind == GameKind.UW2) m.Opl = ReadLib(m, "UW.OPL");
            return m;
        }

        static string FindDir(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            foreach (var d in Directory.GetDirectories(folder))
                if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        static TimbreLibrary ReadLib(MusicLibrary m, string name)
        {
            string p = m.PathOf(name);
            if (p == null || !File.Exists(p)) return null;
            try { return TimbreLibrary.Parse(File.ReadAllBytes(p)); }
            catch (FormatException) { return null; }
        }

        /// <summary>The file's path in SOUND (as it is spelt there, or upper case for a new one); null without a SOUND folder.</summary>
        public string PathOf(string file)
        {
            if (SoundFolder == null) return null;
            return GameProfile.Find(SoundFolder, file) ?? Path.Combine(SoundFolder, file);
        }

        public MusicTheme Theme(int number) { return Themes.FirstOrDefault(t => t.Number == number); }

        /// <summary>The file as it will be saved: the pending change, or the file on disk, or null.</summary>
        public byte[] Read(MusicTheme t, MusicVersion v)
        {
            byte[] b;
            if (pending.TryGetValue(t.File(v), out b)) return b;
            return OnDisk(t, v);
        }

        /// <summary>The file on disk, or null.</summary>
        public byte[] OnDisk(MusicTheme t, MusicVersion v)
        {
            string p = PathOf(t.File(v));
            return p != null && File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        public bool IsChanged(MusicTheme t, MusicVersion v) { return pending.ContainsKey(t.File(v)); }
        public bool HasChanges { get { return pending.Count > 0; } }

        /// <summary>Replaces a theme's file (written on Save).</summary>
        public void Set(MusicTheme t, MusicVersion v, byte[] bytes)
        {
            var disk = OnDisk(t, v);
            if (disk != null && disk.SequenceEqual(bytes)) pending.Remove(t.File(v));
            else pending[t.File(v)] = bytes;
        }

        /// <summary>The untouched file the editor kept (NAME.XMI.ORIG) when it first wrote one, or null.</summary>
        public byte[] Original(MusicTheme t, MusicVersion v)
        {
            string p = PathOf(t.File(v));
            string o = p == null ? null : SafeFile.OriginalOf(p);
            return o == null ? null : File.ReadAllBytes(o);
        }

        /// <summary>Back to the original: the .ORIG file (written on Save), or the file on disk when the editor never wrote it. False when there is nothing to go back to.</summary>
        public bool Revert(MusicTheme t, MusicVersion v)
        {
            var o = Original(t, v);
            if (o != null) { Set(t, v, o); return true; }
            return pending.Remove(t.File(v));
        }

        /// <summary>Drops every change not yet saved.</summary>
        public void Discard() { pending.Clear(); }

        /// <summary>Writes every changed file; returns the paths written.</summary>
        public List<string> Save()
        {
            var written = new List<string>();
            if (SoundFolder == null) { if (pending.Count > 0) throw new IOException("The game has no SOUND folder."); return written; }
            foreach (var kv in pending.ToList())
            {
                string p = PathOf(kv.Key);
                SafeFile.Write(p, kv.Value);
                written.Add(p);
                pending.Remove(kv.Key);
            }
            return written;
        }

        public MusicTarget Target(MusicTheme t, MusicVersion v)
        {
            return new MusicTarget { Kind = Kind, Version = v, Theme = t, Fm = Fm, Opl = Opl, Mt = Mt };
        }

        /// <summary>The theme's file checked against the game.</summary>
        public List<MusicIssue> Validate(MusicTheme t, MusicVersion v) { return MusicValidator.Validate(Read(t, v), Target(t, v)); }

        /// <summary>The theme's file as a standard MIDI file, or null when it is missing or unreadable.</summary>
        public byte[] ExportMidi(MusicTheme t, MusicVersion v)
        {
            var b = Read(t, v);
            if (b == null) return null;
            return XmiToMidi.Convert(XmiFile.Parse(b)).ToBytes();
        }

        /// <summary>The file name an exported MIDI file gets: the theme file's name with .MID.</summary>
        public static string MidiName(MusicTheme t, MusicVersion v) { return Path.GetFileNameWithoutExtension(t.File(v)) + ".mid"; }

        /// <summary>Every theme, both versions, as MIDI files in folder; returns how many were written.</summary>
        public int ExportAll(string folder)
        {
            Directory.CreateDirectory(folder);
            int n = 0;
            foreach (var t in Themes)
                foreach (MusicVersion v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
                {
                    byte[] m;
                    try { m = ExportMidi(t, v); }
                    catch (FormatException) { continue; }
                    if (m == null) continue;
                    File.WriteAllBytes(Path.Combine(folder, MidiName(t, v)), m);
                    n++;
                }
            return n;
        }

        /// <summary>
        /// Makes a theme's file from a MIDI file and validates it against the game. Nothing changes
        /// here: the caller sets the result when it has no errors.
        /// </summary>
        public MusicImportResult Convert(MusicTheme t, byte[] midi, MusicImportOptions opt)
        {
            var r = new MusicImportResult();
            MidiFile m;
            try { m = MidiFile.Parse(midi); }
            catch (FormatException ex) { r.Notes.Add(MusicIssue.Error("Not a MIDI file the editor can read: " + ex.Message)); return r; }
            var o = opt.Clone();
            if (o.FmTimbres == null) o.FmTimbres = Fm;
            r = MidiToXmi.Convert(m, o);
            r.Validation = MusicValidator.Validate(r.Bytes, Target(t, opt.Version));
            return r;
        }

        /// <summary>
        /// Imports a theme: the FM version from midi, the MT-32 version from mt32Midi when given
        /// (its programs kept) or else from midi through the GM → MT-32 map when mapGm (kept as
        /// they are otherwise). versions says which to make. Both results are returned; the files
        /// change only when none has an error.
        /// </summary>
        public List<KeyValuePair<MusicVersion, MusicImportResult>> Import(MusicTheme t, byte[] midi, byte[] mt32Midi, bool fm, bool mt32, MusicImportOptions opt, bool mapGm = true)
        {
            var l = new List<KeyValuePair<MusicVersion, MusicImportResult>>();
            if (fm)
            {
                var o = opt.Clone(); o.Version = MusicVersion.Fm;
                l.Add(new KeyValuePair<MusicVersion, MusicImportResult>(MusicVersion.Fm, Convert(t, midi, o)));
            }
            if (mt32)
            {
                var o = opt.Clone(); o.Version = MusicVersion.Mt32; o.MapGmToMt32 = mt32Midi == null && mapGm;
                l.Add(new KeyValuePair<MusicVersion, MusicImportResult>(MusicVersion.Mt32, Convert(t, mt32Midi ?? midi, o)));
            }
            if (l.Count > 0 && l.All(kv => !kv.Value.HasErrors && kv.Value.Bytes != null))
                foreach (var kv in l) Set(t, kv.Key, kv.Value.Bytes);
            return l;
        }
    }
}
