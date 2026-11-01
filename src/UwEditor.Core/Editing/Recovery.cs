using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace UwEditor.Core.Editing
{
    /// <summary>
    /// What a running editor leaves in its settings folder (running-PID.json): the game it has
    /// open, whether work is unsaved and which checkpoint holds it. A clean exit removes it; one
    /// left by a process that is gone means the editor crashed or was killed, and the next start
    /// offers the checkpoint back.
    /// </summary>
    public sealed class RunMarker
    {
        public int Pid { get; set; }
        /// <summary>When the process started (UTC): with the pid, tells a reused pid from this run.</summary>
        public DateTime Started { get; set; }
        public DateTime Updated { get; set; }
        /// <summary>The folder opened (the game folder or a SAVEn folder), or null.</summary>
        public string Game { get; set; }
        /// <summary>The kinds of unsaved work at the last update (empty: nothing unsaved).</summary>
        public List<string> Unsaved { get; set; } = new List<string>();
        /// <summary>The newest checkpoint written since the work was last all saved, or null.</summary>
        public string Checkpoint { get; set; }
        /// <summary>The crash log written by the error that ended the run, if one did.</summary>
        public string CrashLog { get; set; }
        /// <summary>Where the marker was read from.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public string Path { get; set; }
    }

    /// <summary>Run markers and crash logs (see <see cref="RunMarker"/>).</summary>
    public static class Recovery
    {
        public const int KeepLogs = 20;

        public static string MarkerPath(string folder, int pid) { return Path.Combine(folder, "running-" + pid.ToString(CultureInfo.InvariantCulture) + ".json"); }

        /// <summary>Writes the marker (through a temporary file, so a crash mid-write leaves the old one).</summary>
        public static void Write(string folder, RunMarker m)
        {
            Directory.CreateDirectory(folder);
            m.Updated = DateTime.UtcNow;
            string path = MarkerPath(folder, m.Pid), tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, true);
        }

        public static void Remove(string folder, int pid)
        {
            try { File.Delete(MarkerPath(folder, pid)); } catch (Exception) { }
        }

        /// <summary>When a process started (UTC), or null when there is no such process.</summary>
        public static DateTime? ProcessStart(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) return null;
                    return p.StartTime.ToUniversalTime();
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>The markers of runs that ended without a clean exit: their process is gone, or the pid now belongs to another process.</summary>
        public static List<RunMarker> LeftBehind(string folder, Func<int, DateTime?> startOf = null)
        {
            startOf = startOf ?? ProcessStart;
            var list = new List<RunMarker>();
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder, "running-*.json"))
            {
                RunMarker m;
                try { m = JsonSerializer.Deserialize<RunMarker>(File.ReadAllText(f)); }
                catch (Exception) { continue; }
                if (m == null) continue;
                m.Path = f;
                var started = startOf(m.Pid);
                bool alive = started != null && Math.Abs((started.Value - m.Started).TotalSeconds) < 5;
                if (!alive) list.Add(m);
            }
            return list.OrderByDescending(m => m.Updated).ToList();
        }

        public static void Forget(RunMarker m)
        {
            try { if (m?.Path != null) File.Delete(m.Path); } catch (Exception) { }
        }

        /// <summary>Writes a crash log (the error, where it came from, the program, the system and what was open) to folder/crashes; returns its path, or null when it could not be written.</summary>
        public static string WriteCrashLog(string folder, Exception ex, string context)
        {
            try
            {
                string dir = Path.Combine(folder, "crashes");
                Directory.CreateDirectory(dir);
                var now = DateTime.Now;
                string path = Path.Combine(dir, "crash-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".txt");
                var sb = new StringBuilder();
                sb.AppendLine("Underworld Editor crash log, " + now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                var asm = typeof(Recovery).Assembly;
                string version = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? asm.GetName().Version?.ToString();
                sb.AppendLine("Version: " + version);
                sb.AppendLine("System: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + "), .NET " + Environment.Version);
                if (!string.IsNullOrEmpty(context)) sb.AppendLine(context.TrimEnd());
                sb.AppendLine();
                sb.AppendLine(Describe(ex));
                File.WriteAllText(path, sb.ToString());
                foreach (var old in Directory.GetFiles(dir, "crash-*.txt").OrderByDescending(f => f, StringComparer.Ordinal).Skip(KeepLogs))
                    try { File.Delete(old); } catch (Exception) { }
                return path;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The error with its inner errors and stack traces.</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null) return "(no error object)";
            var sb = new StringBuilder();
            int depth = 0;
            for (var e = ex; e != null && depth < 8; e = e.InnerException, depth++)
            {
                if (depth > 0) sb.AppendLine().AppendLine("Caused by:");
                sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                if (e.StackTrace != null) sb.AppendLine(e.StackTrace);
                if (e is AggregateException ag && ag.InnerExceptions.Count > 1)
                    foreach (var inner in ag.InnerExceptions.Skip(1)) sb.AppendLine("Also: " + inner.GetType().FullName + ": " + inner.Message);
            }
            return sb.ToString();
        }
    }
}
