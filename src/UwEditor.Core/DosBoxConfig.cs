using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UwEditor.Core
{
    /// <summary>
    /// A DOSBox configuration for running the game: GOG's own configuration when the game came
    /// from GOG (its settings for the CPU, sound and the like, kept as they are), with an
    /// [autoexec] of our own that mounts the folder given as C: and starts the game. Without a
    /// GOG configuration, defaults that suit the Underworld games.
    /// </summary>
    public static class DosBoxConfig
    {
        /// <summary>The defaults used when no GOG configuration is found: an SVGA machine with EMS, a Sound Blaster 16 at 220/7/1 (the games' setup default) and an MPU-401 for the MT-32.</summary>
        public const string Defaults =
@"[sdl]
fullscreen=false
output=opengl

[dosbox]
machine=svga_s3
memsize=16

[render]
aspect=true

[cpu]
core=auto
cputype=auto
cycles=max

[mixer]
rate=44100

[midi]
mpu401=intelligent
mididevice=default

[sblaster]
sbtype=sb16
sbbase=220
irq=7
dma=1
hdma=5
oplmode=auto

[speaker]
pcspeaker=false

[dos]
xms=true
ems=true
umb=true
";

        /// <summary>
        /// GOG's configuration for the game, or null: a .conf file in the game folder or the folder
        /// above it with DOSBox sections, the main one before the "_single"/"_client"/"_server" ones
        /// GOG also ships (dosboxUW.conf, dosboxUW_single.conf and the like).
        /// </summary>
        public static string FindGogConf(string gameFolder)
        {
            if (string.IsNullOrEmpty(gameFolder)) return null;
            var dirs = new List<string> { gameFolder };
            string up = Path.GetDirectoryName(Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (up != null) dirs.Add(up);
            // a copy taken out of GOG's game.gog: the install it came from has GOG's settings
            string gog = Underworld.Mods.GogImage.InstallOf(gameFolder);
            if (gog != null) dirs.Add(gog);
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                var confs = Directory.GetFiles(d, "*.conf").Where(f =>
                {
                    try { var t = File.ReadAllText(f); return t.IndexOf("[cpu]", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("[sblaster]", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("[dosbox]", StringComparison.OrdinalIgnoreCase) >= 0; }
                    catch (Exception) { return false; }
                }).ToList();
                var main = confs.OrderBy(f => Rank(Path.GetFileNameWithoutExtension(f))).ThenBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (main != null) return main;
            }
            return null;
        }

        static int Rank(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("_single") || n.Contains("_client") || n.Contains("_server") || n.Contains("_settings")) return 2;
            return n.StartsWith("dosbox") ? 0 : 1;
        }

        /// <summary>The configuration's sections, in order (name in lower case → its lines, comments kept).</summary>
        public static List<KeyValuePair<string, List<string>>> Sections(string text)
        {
            var list = new List<KeyValuePair<string, List<string>>>();
            List<string> cur = null;
            foreach (var raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    cur = new List<string>();
                    list.Add(new KeyValuePair<string, List<string>>(t.Substring(1, t.Length - 2).Trim().ToLowerInvariant(), cur));
                    continue;
                }
                if (cur == null) continue;      // text before the first section
                cur.Add(line);
            }
            return list;
        }

        /// <summary>
        /// The configuration: every section of the base (GOG's, or the defaults) but [autoexec],
        /// then an [autoexec] that mounts mountFolder as C:, runs exe and leaves DOSBox when the
        /// game ends. The folder is quoted; a quote in it cannot be passed to DOSBox's mount and is refused.
        /// </summary>
        public static string Build(string baseConf, string mountFolder, string exe)
        {
            if (mountFolder.Contains('"')) throw new ArgumentException("DOSBox cannot mount a folder whose name holds a quote: " + mountFolder);
            var sb = new StringBuilder();
            sb.AppendLine("# Made by the Underworld Editor for one run; the editor writes it afresh each time.");
            foreach (var s in Sections(baseConf ?? Defaults))
            {
                if (s.Key == "autoexec") continue;
                sb.AppendLine("[" + s.Key + "]");
                foreach (var l in s.Value) sb.AppendLine(l);
            }
            sb.AppendLine("[autoexec]");
            sb.AppendLine("@echo off");
            string m = mountFolder.Length > 3 ? mountFolder.TrimEnd('\\', '/') : mountFolder;     // a drive's root keeps its separator
            sb.AppendLine("mount c \"" + m + "\"");
            sb.AppendLine("c:");
            sb.AppendLine("cls");
            sb.AppendLine(Path.GetFileNameWithoutExtension(exe));
            sb.AppendLine("exit");
            return sb.ToString();
        }

        /// <summary>Writes the configuration for a run to file and returns DOSBox's arguments: -conf file, then the extra arguments set.</summary>
        public static List<string> Arguments(string confPath, string extra)
        {
            var a = new List<string> { "-conf", confPath };
            if (!string.IsNullOrWhiteSpace(extra)) a.AddRange(PlayTest.SplitArgs(extra));
            return a;
        }
    }
}
