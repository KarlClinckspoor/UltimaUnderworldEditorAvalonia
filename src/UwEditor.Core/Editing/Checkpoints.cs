using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UwEditor.Core.Editing
{
    /// <summary>A checkpoint on disk: when it was made and what it holds.</summary>
    public sealed class CheckpointInfo
    {
        public string Folder;
        public DateTime Time;
        public string Label;
        public List<int> Levels = new List<int>();
        public List<string> Files = new List<string>();
        public long Bytes;
        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Levels.Count > 0) parts.Add("level" + (Levels.Count == 1 ? " " : "s ") + string.Join(", ", Levels));
                parts.AddRange(Files);
                return string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1}{2}  ({3:0.#} KB)", Time.ToLocalTime(), string.Join(", ", parts), string.IsNullOrEmpty(Label) ? "" : " · " + Label, Bytes / 1024.0);
            }
        }
        public override string ToString() { return Summary; }
    }

    /// <summary>
    /// Copies of the unsaved work, taken every few minutes (as the Unity editor's checkpoints):
    /// each changed level's block and texture block, and STRINGS.PAK or PLAYER.DAT when they
    /// have unsaved changes. They live in the editor's settings folder, one folder per opened
    /// game, and only the newest so many are kept. Restoring loads them back as unsaved,
    /// undoable edits; nothing in the game folder is touched.
    /// </summary>
    public sealed class Checkpoints
    {
        public const int DefaultMinutes = 5, DefaultCopies = 20, MaxCopies = 1000;
        public string Root { get; private set; }
        public GameSession Session { get; private set; }

        public Checkpoints(string baseFolder, GameSession s)
        {
            Session = s;
            string key = s.SaveFolder ?? s.GameFolder;
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(key).ToUpperInvariant()))).Substring(0, 10).ToLowerInvariant();
            string name = new string(Path.GetFileName(key.TrimEnd('/', '\\')).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            Root = Path.Combine(baseFolder, (s.IsUw2 ? "uw2-" : "uw1-") + name + "-" + hash);
        }

        /// <summary>Writes a checkpoint of the dirty levels and the extra files given (name → bytes); null when there is nothing to keep.</summary>
        public CheckpointInfo Write(LevelEdits edits, IDictionary<string, byte[]> files, string label, int keep)
        {
            var dirty = edits == null ? new List<LevelEditor>() : edits.Editors.Where(e => e.Dirty).OrderBy(e => e.Level).ToList();
            files = files ?? new Dictionary<string, byte[]>();
            if (dirty.Count == 0 && files.Count == 0) return null;
            var now = DateTime.UtcNow;
            string folder = Path.Combine(Root, now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var info = new CheckpointInfo { Folder = folder, Time = now, Label = label };
            foreach (var e in dirty)
            {
                File.WriteAllBytes(Path.Combine(folder, string.Format("level-{0:D2}.bin", e.Level)), e.Data);
                if (e.TextureBytes != null) File.WriteAllBytes(Path.Combine(folder, string.Format("textures-{0:D2}.bin", e.Level)), e.TextureBytes);
                info.Levels.Add(e.Level);
            }
            foreach (var kv in files)
            {
                File.WriteAllBytes(Path.Combine(folder, kv.Key), kv.Value);
                info.Files.Add(kv.Key);
            }
            var j = new JsonObject
            {
                ["game"] = Session.SaveFolder ?? Session.GameFolder, ["time"] = now.ToString("o"), ["label"] = label ?? "",
                ["levels"] = new JsonArray(info.Levels.Select(l => (JsonNode)l).ToArray()),
                ["files"] = new JsonArray(info.Files.Select(f => (JsonNode)f).ToArray()),
            };
            File.WriteAllText(Path.Combine(folder, "checkpoint.json"), j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            info.Bytes = Directory.GetFiles(folder).Sum(f => new FileInfo(f).Length);
            Prune(keep);
            return info;
        }

        /// <summary>The checkpoints of this game, newest first.</summary>
        public List<CheckpointInfo> List()
        {
            var list = new List<CheckpointInfo>();
            if (!Directory.Exists(Root)) return list;
            foreach (var d in Directory.GetDirectories(Root))
            {
                string meta = Path.Combine(d, "checkpoint.json");
                if (!File.Exists(meta)) continue;
                try
                {
                    var j = JsonNode.Parse(File.ReadAllText(meta));
                    var c = new CheckpointInfo
                    {
                        Folder = d,
                        Time = DateTime.Parse((string)j["time"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        Label = (string)j["label"],
                    };
                    foreach (var l in (j["levels"] as JsonArray) ?? new JsonArray()) c.Levels.Add((int)l);
                    foreach (var f in (j["files"] as JsonArray) ?? new JsonArray()) c.Files.Add((string)f);
                    c.Bytes = Directory.GetFiles(d).Sum(f => new FileInfo(f).Length);
                    list.Add(c);
                }
                catch (Exception) { }
            }
            return list.OrderByDescending(c => c.Time).ToList();
        }

        /// <summary>Deletes all but the newest keep checkpoints.</summary>
        public int Prune(int keep)
        {
            keep = Math.Max(1, Math.Min(MaxCopies, keep));
            int n = 0;
            foreach (var c in List().Skip(keep))
            {
                try { Directory.Delete(c.Folder, true); n++; } catch (Exception) { }
            }
            return n;
        }

        public void Delete(CheckpointInfo c) { if (Directory.Exists(c.Folder)) Directory.Delete(c.Folder, true); }

        public byte[] ReadFile(CheckpointInfo c, string name)
        {
            string p = Path.Combine(c.Folder, name);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        /// <summary>Loads the checkpoint's levels into the editors (each an undoable change); returns the levels restored.</summary>
        public List<int> RestoreLevels(CheckpointInfo c, LevelEdits edits)
        {
            var done = new List<int>();
            foreach (int l in c.Levels)
            {
                var data = ReadFile(c, string.Format("level-{0:D2}.bin", l));
                if (data == null) continue;
                var tex = ReadFile(c, string.Format("textures-{0:D2}.bin", l));
                var e = edits.For(l);
                if (data.SequenceEqual(e.Data) && (tex == null || e.TextureBytes == null || tex.SequenceEqual(e.TextureBytes))) continue;
                e.Replace(data, tex, "Restore checkpoint " + c.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                done.Add(l);
            }
            return done;
        }
    }
}
