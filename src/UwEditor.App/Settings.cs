using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UwEditor.App
{
    /// <summary>What the editor remembers between runs, as JSON in the user's settings folder.</summary>
    public sealed class Settings
    {
        public List<string> Recent { get; set; } = new List<string>();
        /// <summary>Saved games kept outside a game folder, with the game folder each is read with.</summary>
        public Dictionary<string, string> GameForSave { get; set; } = new Dictionary<string, string>();
        public double LeftWidth { get; set; } = 290;
        public double RightWidth { get; set; } = 360;
        public bool Grid { get; set; } = true;
        public int GridStep { get; set; } = 5;
        public bool Models { get; set; } = true;
        /// <summary>The properties panel's sections, folded or not.</summary>
        public bool ObjectOpen { get; set; } = true;
        /// <summary>Show ▸ Facing arrows.</summary>
        public bool Facing { get; set; }
        public bool TileOpen { get; set; } = true;
        /// <summary>"Dark" or "Light" (Light by default).</summary>
        public string Theme { get; set; } = "Light";
        /// <summary>View ▸ Layout: Comfortable or Compact (Density; Compact by default).</summary>
        public string Layout { get; set; } = "Compact";
        /// <summary>Minutes between checkpoints of unsaved edits (0: off).</summary>
        public int CheckpointMinutes { get; set; } = 5;
        /// <summary>How many checkpoints to keep (1-1000).</summary>
        public int CheckpointCopies { get; set; } = 20;
        /// <summary>The zone fill's highest climb, in floor steps.</summary>
        public int ZoneClimb { get; set; } = UwEditor.Core.Randomizer.ZoneFlood.DefaultMaxClimb;
        /// <summary>The solver gets the zoning with its multi-level zones split by level (joined by free two-way links).</summary>
        public bool ZoneSplitByLevel { get; set; }
        /// <summary>The DOSBox program the play test runs (found or chosen once).</summary>
        public string DosBoxPath { get; set; } = "";
        /// <summary>More DOSBox arguments for the play test (a -conf file, say).</summary>
        public string DosBoxArgs { get; set; } = "";
        /// <summary>Underworld Exhumed's ports for the checks (uw1port, uw2port), chosen once.</summary>
        public string ExhumedUw1Path { get; set; } = "";
        public string ExhumedUw2Path { get; set; } = "";
        /// <summary>The check in Underworld Exhumed: also beside the edits, a sweep of each level, a look round at each stop.</summary>
        public bool ExhumedEdits { get; set; }
        public bool ExhumedSweep { get; set; }
        public bool ExhumedLook { get; set; }
        /// <summary>The check in Underworld Exhumed shows the game's window while it runs.</summary>
        public bool ExhumedShowWindow { get; set; }
        /// <summary>The port in use crashes on UW1's level 9 (Underworld Exhumed v1.0.2 and later builds so far).</summary>
        public bool ExhumedLevel9Crash { get; set; } = true;
        /// <summary>The conversation editor's Dialogue tab wraps lines longer than the editor is wide.</summary>
        public bool DialogueWrap { get; set; }

        /// <summary>The accent table of each game folder (Tools ▸ Graphics ▸ Fonts), by the folder's full path.</summary>
        public Dictionary<string, AccentSettings> Accents { get; set; } = new Dictionary<string, AccentSettings>();

        static string AccentKey(string folder) { return string.IsNullOrEmpty(folder) ? "" : Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant(); }

        /// <summary>A game folder's accent settings (empty ones when it has none).</summary>
        public AccentSettings AccentsFor(string folder)
        {
            AccentSettings a;
            if (Accents != null && Accents.TryGetValue(AccentKey(folder), out a) && a != null) return a;
            return new AccentSettings();
        }

        public void SetAccents(string folder, AccentSettings a)
        {
            Accents = Accents ?? new Dictionary<string, AccentSettings>();
            if (a == null || (a.Map.Count == 0 && !a.DebugSymbols && !a.UndrawnCodes)) Accents.Remove(AccentKey(folder));
            else Accents[AccentKey(folder)] = a;
        }

        /// <summary>The folder the settings (and the checkpoints) live in.</summary>
        public static string Folder { get { return Path.GetDirectoryName(PathOf()); } }

        static string PathOf()
        {
            // UWEDITOR_SETTINGS_DIR: tests keep their own settings away from the user's
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SETTINGS_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                string app = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                dir = Path.Combine(app, Name);
                if (!moved) { moved = true; MoveOldFolder(Path.Combine(app, OldName), dir); }
            }
            return Path.Combine(dir, "settings.json");
        }

        /// <summary>The settings folder's name, and the one versions before the rename used.</summary>
        public const string Name = "UwEditorAvalonia", OldName = "UwEditorFree";
        static bool moved;

        /// <summary>
        /// When the folder of the old name is there and the new one is not: the old one
        /// (settings, checkpoints, crash logs, run files) becomes the new one, or is copied when
        /// it cannot be moved.
        /// </summary>
        public static void MoveOldFolder(string old, string dir)
        {
            try
            {
                if (!Directory.Exists(old) || Directory.Exists(dir)) return;
                try { Directory.Move(old, dir); }
                catch (Exception) { CopyFolder(old, dir); }
            }
            catch (Exception) { }
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), false);
            foreach (var d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
        }

        public static Settings Load()
        {
            try
            {
                string p = PathOf();
                if (File.Exists(p)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(p)) ?? new Settings();
            }
            catch (Exception) { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                string p = PathOf();
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { }
        }

        public void AddRecent(string folder)
        {
            Recent.RemoveAll(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, folder);
            if (Recent.Count > 8) Recent.RemoveRange(8, Recent.Count - 8);
        }
    }

    /// <summary>A game folder's accent table and its choices of free codes.</summary>
    public sealed class AccentSettings
    {
        /// <summary>Code → letter.</summary>
        public Dictionary<int, string> Map { get; set; } = new Dictionary<int, string>();
        /// <summary>Symbols only the debug strings (block 24) use count as free.</summary>
        public bool DebugSymbols { get; set; }
        /// <summary>The codes the DOS game draws as '?' (1-31, 7Bh-7Fh) count as free.</summary>
        public bool UndrawnCodes { get; set; }

        public UwEditor.Core.AccentTable Table() { return UwEditor.Core.AccentTable.FromDictionary(Map); }
    }
}
