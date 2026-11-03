using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core.Conversations;

namespace UwEditor.Core.Sound
{
    /// <summary>What the editor knows of one .VOC: its length, rate and size against the game's limit.</summary>
    public sealed class VocInfo
    {
        public string Name;
        public SoundUse Use;
        public bool Exists;
        public int FileBytes;
        public int Samples;
        public int Rate;
        public double Seconds { get { return Rate <= 0 ? 0 : (double)Samples / Rate; } }
        /// <summary>Why the game would not play it as it is (the shape or the size), or null.</summary>
        public string Problem;
        /// <summary>"64,402 of 98,304 bytes" and the like.</summary>
        public string SizeText
        {
            get
            {
                if (!Exists) return "none";
                return SoundLimits.HasLimit(Use) ? string.Format("{0:N0} / {1:N0}", FileBytes, SoundLimits.MaxFileBytes(Use) + (Use == SoundUse.Uw1Speech ? 0 : 1)) : string.Format("{0:N0}", FileBytes);
            }
        }
    }

    /// <summary>A speech file and the cutscene line it goes with.</summary>
    public sealed class SpeechItem
    {
        public int Number;
        public string Name;
        /// <summary>The say opcodes that play it (usually one).</summary>
        public readonly List<SpeechLine> Lines = new List<SpeechLine>();
        public string CutsceneText { get { return Lines.Count == 0 ? "—" : string.Join(", ", Lines.Select(l => l.Cutscene.ToString()).Distinct()); } }
    }

    /// <summary>A UW2 special-effects trap that plays a sound file.</summary>
    public sealed class TrapSound
    {
        public int Level;
        /// <summary>The level's name in the level picker ("Level 3  ·  slot 11").</summary>
        public string LevelName;
        public int Index;
        public int X, Y;
        /// <summary>The trap's owner: it plays effect owner + 100, SOUND\UW&lt;owner&gt;.VOC.</summary>
        public int Owner;
        public string Name { get { return SoundLibrary.TrapName(Owner); } }
        public override string ToString() { return string.Format("{0}, object {1}{2}", (LevelName ?? "Level " + Level).Replace("  ·  ", ", "), Index, X >= 0 ? string.Format(" at ({0}, {1})", X, Y) : ""); }
    }

    /// <summary>
    /// The game's SOUND folder (the game folder's, also for a saved game): the speech and
    /// digitised effect files and SOUNDS.DAT, with changes held in memory until SaveAll. Files
    /// are found whatever the case of their names. Writes go through SafeFile (NAME.EXT.BKP,
    /// and NAME.EXT.ORIG the first time). A file the editor creates gets an empty NAME.EXT.ORIG
    /// beside it: "there was no such file", which Make a mod reads as a new file.
    /// </summary>
    public sealed class SoundLibrary
    {
        public string GameFolder { get; private set; }
        public GameKind Kind { get; private set; }
        public bool IsUw2 { get { return Kind == GameKind.UW2; } }
        public string SoundFolder { get; private set; }
        /// <summary>SOUNDS.DAT as edited (null when the game has none or it cannot be read).</summary>
        public SoundsDat Effects { get; private set; }
        public string EffectsProblem { get; private set; }
        byte[] effectsSaved;
        readonly Dictionary<string, byte[]> pending = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> removing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<SpeechLine> lines;

        public const string EffectsFile = "SOUNDS.DAT";

        public SoundLibrary(string gameFolder, GameKind kind)
        {
            GameFolder = gameFolder;
            Kind = kind;
            SoundFolder = FindDir(gameFolder, "SOUND") ?? Path.Combine(gameFolder, "SOUND");
            ReloadEffects();
        }

        public static SoundLibrary For(GameSession s) { return new SoundLibrary(s.GameFolder, s.Kind); }

        void ReloadEffects()
        {
            Effects = null; effectsSaved = null; EffectsProblem = null;
            string p = DiskPath(EffectsFile);
            if (p == null) { EffectsProblem = "There is no SOUND\\SOUNDS.DAT."; return; }
            try
            {
                effectsSaved = File.ReadAllBytes(p);
                Effects = SoundsDat.Parse(effectsSaved, IsUw2);
            }
            catch (Exception e) { EffectsProblem = "SOUNDS.DAT cannot be read: " + e.Message; Effects = null; }
        }

        // ---- names

        /// <summary>UW1 nn.VOC, UW2 BSPnn.VOC.</summary>
        public string SpeechName(int n) { return (IsUw2 ? "BSP" : "") + n.ToString("00") + ".VOC"; }
        /// <summary>UW2 SPnn.VOC, a digital effect.</summary>
        public static string EffectName(int n) { return "SP" + n.ToString("00") + ".VOC"; }
        /// <summary>UW2 UWnn.VOC, a trap sound (effect nn + 100).</summary>
        public static string TrapName(int n) { return "UW" + n.ToString("00") + ".VOC"; }
        public SoundUse SpeechUse { get { return IsUw2 ? SoundUse.Uw2Speech : SoundUse.Uw1Speech; } }

        // ---- files

        /// <summary>A sub folder of root, whatever its case, or null.</summary>
        public static string FindDir(string root, string name)
        {
            if (root == null || !Directory.Exists(root)) return null;
            return Directory.GetDirectories(root).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The file on disk (any case), or null.</summary>
        public string DiskPath(string name)
        {
            if (!Directory.Exists(SoundFolder)) return null;
            return Directory.GetFiles(SoundFolder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The file's bytes as the game would read them after Save: the unsaved change, else the disk's; null when there is none.</summary>
        public byte[] Bytes(string name)
        {
            byte[] b;
            if (pending.TryGetValue(name, out b)) return b;
            if (removing.Contains(name)) return null;
            string p = DiskPath(name);
            return p == null ? null : File.ReadAllBytes(p);
        }

        public bool Exists(string name) { return pending.ContainsKey(name) || (!removing.Contains(name) && DiskPath(name) != null); }

        /// <summary>The file has an unsaved change (new bytes or a removal).</summary>
        public bool IsChanged(string name) { return pending.ContainsKey(name) || removing.Contains(name); }

        /// <summary>The editor keeps an original of it (NAME.EXT.ORIG): Revert can go back to it.</summary>
        public bool HasOriginal(string name) { string p = DiskPath(name); return p != null && SafeFile.OriginalOf(p) != null; }

        /// <summary>The file as shipped: its .ORIG (null when the editor created it), or the file itself when the editor never wrote it.</summary>
        public byte[] Original(string name)
        {
            string p = DiskPath(name);
            if (p == null) return null;
            string o = SafeFile.OriginalOf(p);
            if (o == null) return File.ReadAllBytes(p);
            var b = File.ReadAllBytes(o);
            return b.Length == 0 ? null : b;
        }

        /// <summary>Holds new bytes for a file (written by SaveAll).</summary>
        public void Put(string name, byte[] bytes)
        {
            removing.Remove(name);
            var disk = DiskPath(name);
            if (disk != null && File.ReadAllBytes(disk).SequenceEqual(bytes)) { pending.Remove(name); return; }
            pending[name] = bytes;
        }

        /// <summary>
        /// Back to the original: the .ORIG's bytes, or no file at all for one the editor added;
        /// an unsaved change of a file never written is dropped. Saved by SaveAll.
        /// </summary>
        public void Revert(string name)
        {
            string p = DiskPath(name);
            string o = p == null ? null : SafeFile.OriginalOf(p);
            if (o == null) { pending.Remove(name); return; }
            var b = File.ReadAllBytes(o);
            if (b.Length == 0) { pending.Remove(name); removing.Add(name); return; }
            Put(name, b);
        }

        /// <summary>Drops the unsaved change of a file.</summary>
        public void Discard(string name) { pending.Remove(name); removing.Remove(name); }

        public bool EffectsChanged { get { return Effects != null && effectsSaved != null && !Effects.ToBytes().SequenceEqual(effectsSaved); } }

        /// <summary>SOUNDS.DAT back to the file as saved.</summary>
        public void RevertEffects() { if (effectsSaved != null) Effects = SoundsDat.Parse(effectsSaved, IsUw2); }

        public bool HasChanges { get { return pending.Count > 0 || removing.Count > 0 || EffectsChanged; } }

        /// <summary>The names with unsaved changes, SOUNDS.DAT included.</summary>
        public IEnumerable<string> ChangedNames { get { return pending.Keys.Concat(removing).Concat(EffectsChanged ? new[] { EffectsFile } : new string[0]).OrderBy(n => n, StringComparer.OrdinalIgnoreCase); } }

        /// <summary>Writes every change; returns the files written or removed. Throws on a failure (what was written stays written).</summary>
        public List<string> SaveAll()
        {
            var done = new List<string>();
            if (!Directory.Exists(SoundFolder) && (pending.Count > 0)) Directory.CreateDirectory(SoundFolder);
            foreach (var kv in pending.ToList())
            {
                string p = DiskPath(kv.Key);
                if (p == null)
                {
                    p = Path.Combine(SoundFolder, kv.Key.ToUpperInvariant());
                    // the mark of a file the editor added: Make a mod carries it as a new file
                    if (!File.Exists(p + SafeFile.OriginalSuffix)) File.WriteAllBytes(p + SafeFile.OriginalSuffix, new byte[0]);
                }
                SafeFile.Write(p, kv.Value);
                pending.Remove(kv.Key);
                done.Add(Path.GetFileName(p));
            }
            foreach (var name in removing.ToList())
            {
                string p = DiskPath(name);
                if (p != null)
                {
                    File.Copy(p, SafeFile.BackupOf(p), true);
                    File.Delete(p);
                    string o = p + SafeFile.OriginalSuffix;
                    if (File.Exists(o) && new FileInfo(o).Length == 0) File.Delete(o);
                    done.Add(Path.GetFileName(p) + " (removed)");
                }
                removing.Remove(name);
            }
            if (EffectsChanged)
            {
                string p = DiskPath(EffectsFile) ?? Path.Combine(SoundFolder, EffectsFile);
                var b = Effects.ToBytes();
                SafeFile.Write(p, b);
                effectsSaved = b;
                done.Add(EffectsFile);
            }
            return done;
        }

        // ---- what is there

        /// <summary>Length, rate and size of a file as it is now (unsaved changes included).</summary>
        public VocInfo Info(string name, SoundUse use)
        {
            var i = new VocInfo { Name = name, Use = use };
            var b = Bytes(name);
            if (b == null) return i;
            i.Exists = true;
            i.FileBytes = b.Length;
            try
            {
                var v = VocFile.Parse(b);
                i.Rate = v.SampleRate;
                i.Samples = v.Samples8().Length;
                i.Problem = SoundLimits.Check(use, b.Length) ?? (v.ShapeProblem == null ? null : "the game plays it with clicks: " + v.ShapeProblem);
            }
            catch (Exception e) { i.Problem = e.Message; }
            return i;
        }

        /// <summary>The cutscene says, read once from CUTS.</summary>
        public List<SpeechLine> Lines { get { return lines ?? (lines = SpeechLines.Find(GameFolder, IsUw2)); } }

        /// <summary>
        /// The speech files: every one a cutscene's say opcode names (UW1 nn.VOC, UW2
        /// BSPnn.VOC), and any other of that pattern in SOUND.
        /// </summary>
        public List<SpeechItem> Speech()
        {
            var map = new SortedDictionary<int, SpeechItem>();
            foreach (var l in Lines)
            {
                SpeechItem it;
                if (!map.TryGetValue(l.Voc, out it)) map[l.Voc] = it = new SpeechItem { Number = l.Voc, Name = SpeechName(l.Voc) };
                it.Lines.Add(l);
            }
            foreach (var n in NumberedFiles(IsUw2 ? "BSP" : ""))
                if (!map.ContainsKey(n)) map[n] = new SpeechItem { Number = n, Name = SpeechName(n) };
            return map.Values.ToList();
        }

        /// <summary>UW2: the trap sound files there are (UW00–UW63, saved and unsaved).</summary>
        public List<int> TrapFiles() { return NumberedFiles("UW").Where(n => n < 64).ToList(); }

        /// <summary>The numbers nn of the files prefix + digits + .VOC in SOUND, with unsaved ones.</summary>
        public List<int> NumberedFiles(string prefix)
        {
            var names = new List<string>();
            if (Directory.Exists(SoundFolder)) names.AddRange(Directory.GetFiles(SoundFolder).Select(Path.GetFileName));
            names.AddRange(pending.Keys);
            var r = new SortedSet<int>();
            foreach (var n in names)
            {
                if (!n.EndsWith(".VOC", StringComparison.OrdinalIgnoreCase) || !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string digits = n.Substring(prefix.Length, n.Length - prefix.Length - 4);
                int v;
                if (digits.Length == 0 || !digits.All(char.IsDigit) || !int.TryParse(digits, out v)) continue;
                if (Exists(n)) r.Add(v);
            }
            return r.ToList();
        }

        /// <summary>
        /// UW2's special-effects traps (0x184) of quality 2 in the levels: each plays
        /// SOUND\UW&lt;owner&gt;.VOC when set off (event/TRIGGER.C, event/WORLDEV.C do_sfx).
        /// level: the level as edited (or as saved).
        /// </summary>
        public static List<TrapSound> FindTraps(GameSession s, Func<int, LevelModel> level)
        {
            var r = new List<TrapSound>();
            if (!s.IsUw2) return r;
            foreach (var slot in s.LevelSlots())
            {
                if (!slot.Present) continue;
                LevelModel m;
                try { m = level(slot.Level); } catch (Exception) { continue; }
                if (m == null) continue;
                foreach (var o in m.Objects)
                    if (o != null && !o.Free && o.Item == 0x184 && o.Quality == 2)
                        r.Add(new TrapSound { Level = slot.Level, LevelName = slot.Name, Index = o.Index, X = o.TileX, Y = o.TileY, Owner = o.Owner });
            }
            return r;
        }
    }
}
