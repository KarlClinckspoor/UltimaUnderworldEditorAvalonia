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
        public double LeftWidth { get; set; } = 290;
        public double RightWidth { get; set; } = 360;
        public bool Grid { get; set; } = true;
        public int GridStep { get; set; } = 5;
        public bool Models { get; set; } = true;

        static string PathOf()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UwEditorFree");
            return Path.Combine(dir, "settings.json");
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
}
