using System.Diagnostics;
using System.Text;

namespace UwModManager.Core;

/// <summary>Starts the games: DOSBox on a DOS folder, or the Exhumed port on a home.</summary>
public static class Launcher
{
    /// <summary>DOSBox's program: the setting, else GOG's in the game folder, else dosbox on the PATH.</summary>
    public static string? FindDosBox(Settings s, string folder)
    {
        if (!string.IsNullOrEmpty(s.DosBox) && File.Exists(s.DosBox)) return s.DosBox;
        foreach (var rel in new[] { "DOSBOX/DOSBox.exe", "DOSBOX/dosbox.exe", "dosbox/DOSBox.exe" })
        {
            string p = Path.Combine(folder, rel);
            if (File.Exists(p)) return p;
        }
        foreach (var name in new[] { "dosbox-staging", "dosbox", "dosbox-x" })
        {
            string? p = OnPath(name);
            if (p != null) return p;
        }
        return null;
    }

    static string? OnPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            foreach (var ext in OperatingSystem.IsWindows() ? new[] { ".exe", "" } : new[] { "" })
            {
                string p = Path.Combine(dir, name + ext);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    /// <summary>
    /// The settings of the setup the manager writes for a folder without GOG's: GOG's own for the
    /// two games (dosboxULTIMA1.conf, dosboxULTIMA2.conf), so a copy of the game plays as the GOG
    /// release does: an S3 SVGA machine with 16 MB, EMS and XMS, as many cycles as the PC gives, a
    /// Sound Blaster Pro at 220, IRQ 7, DMA 1 (what the games' sound setup expects) and an MPU-401
    /// for the MT-32 or General MIDI, full screen at the desktop's size with the aspect corrected.
    /// Only GOG's output=overlay, which newer DOSBoxes lack, is opengl here.
    /// </summary>
    public const string DosBoxDefaults =
@"[sdl]
fullscreen=true
fulldouble=false
fullresolution=desktop
windowresolution=1024x768
output=opengl
autolock=true
sensitivity=200
waitonerror=true
priority=higher,normal

[dosbox]
machine=svga_s3
memsize=16

[render]
frameskip=0
aspect=true
scaler=normal2x

[cpu]
core=auto
cputype=auto
cycles=max
cycleup=1000
cycledown=1000

[mixer]
nosound=false
rate=44100
blocksize=1024
prebuffer=25

[midi]
mpu401=intelligent
mididevice=default

[sblaster]
sbtype=sbpro1
sbbase=220
irq=7
dma=1
hdma=5
sbmixer=true
oplmode=auto
oplemu=default
oplrate=44100

[gus]
gus=false

[speaker]
pcspeaker=true
tandy=auto
disney=true

[joystick]
joysticktype=none

[serial]
serial1=dummy
serial2=dummy
serial3=disabled
serial4=disabled

[dos]
xms=true
ems=true
umb=true
keyboardlayout=auto
";

    /// <summary>GOG's setups in a game folder (dosbox*.conf), the base one first, then the game's, as GOG's shortcuts pass them.</summary>
    public static List<string> GogConfs(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "dosbox*.conf").OrderBy(c => Path.GetFileName(c).Contains('_') ? 1 : 0).ThenBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

    /// <summary>Where the manager writes its setup for a folder without GOG's.</summary>
    public static string ManagerConf(AppPaths paths, Target t) => Path.Combine(paths.DosBox, t.Id + ".conf");

    /// <summary>The setup the manager writes for a folder without GOG's: <see cref="DosBoxDefaults"/>, then an [autoexec] that mounts the folder and starts the game.</summary>
    public static string ManagerConfText(Target t)
    {
        if (t.Folder.Contains('"')) throw new ArgumentException("DOSBox cannot mount a folder whose name holds a quote: " + t.Folder);
        string folder = t.Folder.Length > 3 ? t.Folder.TrimEnd('\\', '/') : t.Folder;     // a drive's root keeps its separator
        return "# DOSBox setup for " + t.Name + ", written by Ultima Underworlds Mod Manager at each start (GOG's settings for the game).\n" +
            "# To change it, put your settings in a file of your own and choose it as the game's extra settings file: it is read after this one.\n\n" +
            DosBoxDefaults.Replace("\r\n", "\n") +
            "\n[autoexec]\n@echo off\nmount c \"" + folder + "\"\nc:\ncls\n" + t.ExeName + "\nexit\n";
    }

    /// <summary>
    /// The DOSBox command for a game folder: GOG's own setups when the folder has them (the base
    /// one first, then the game's, as GOG's shortcuts do), else a setup the manager writes with
    /// GOG's settings that mounts the folder and starts the game. The game's extra settings file
    /// comes last (DOSBox lets a later -conf override an earlier one), then its extra arguments.
    /// </summary>
    public static ProcessStartInfo DosBox(AppPaths paths, Settings s, Target t)
    {
        string exe = FindDosBox(s, t.Folder) ?? throw new FileNotFoundException("DOSBox was not found: set its program in Settings");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        var confs = GogConfs(t.Folder);
        bool gog = confs.Count > 0;
        if (gog)
            psi.WorkingDirectory = Directory.Exists(Path.Combine(t.Folder, "DOSBOX")) ? Path.Combine(t.Folder, "DOSBOX") : t.Folder;
        else
        {
            string conf = ManagerConf(paths, t);
            Directory.CreateDirectory(Path.GetDirectoryName(conf)!);
            File.WriteAllText(conf, ManagerConfText(t));
            confs.Add(conf);
            psi.WorkingDirectory = t.Folder;
        }
        foreach (var c in confs) { psi.ArgumentList.Add("-conf"); psi.ArgumentList.Add(c); }
        if (!string.IsNullOrWhiteSpace(t.ExtraConfig))
        {
            if (!File.Exists(t.ExtraConfig)) throw new FileNotFoundException("the game's extra DOSBox settings file was not found: " + t.ExtraConfig, t.ExtraConfig);
            psi.ArgumentList.Add("-conf");
            psi.ArgumentList.Add(t.ExtraConfig);
        }
        foreach (var a in SplitArguments(t.ExtraArguments)) psi.ArgumentList.Add(a);
        if (gog)
        {
            psi.ArgumentList.Add("-noconsole");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("exit");
        }
        return psi;
    }

    /// <summary>
    /// The port's command for a home. The game's extra settings file, if set, is copied into the
    /// home's settings file first (key=value lines, as the port writes them; the mod's own keys,
    /// start-square and exe-patch, are left to the mod), then the extra arguments follow the manager's.
    /// </summary>
    public static ProcessStartInfo Exhumed(Target t, string home, IEnumerable<string> enhancements)
    {
        if (string.IsNullOrEmpty(t.PortExe) || !File.Exists(t.PortExe)) throw new FileNotFoundException("the Exhumed program was not found", t.PortExe);
        var psi = new ProcessStartInfo(t.PortExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(t.PortExe)! };
        ExhumedGame.SetConfig(home, t.Game, "enhance", enhancements.Any() ? string.Join(",", enhancements.Distinct()) : null);
        if (!string.IsNullOrWhiteSpace(t.ExtraConfig))
        {
            if (!File.Exists(t.ExtraConfig)) throw new FileNotFoundException("the game's extra Exhumed settings file was not found: " + t.ExtraConfig, t.ExtraConfig);
            foreach (var (key, value) in ExhumedGame.ReadConfig(t.ExtraConfig))
                if (!ExhumedGame.ModKeys.Contains(key)) ExhumedGame.SetConfig(home, t.Game, key, value);
        }
        foreach (var a in ExhumedGame.Arguments(t, home, Array.Empty<string>())) psi.ArgumentList.Add(a);
        foreach (var a in SplitArguments(t.ExtraArguments)) psi.ArgumentList.Add(a);
        return psi;
    }

    public static string CommandLine(ProcessStartInfo psi) =>
        "\"" + psi.FileName + "\" " + string.Join(" ", psi.ArgumentList.Select(a => a.Length == 0 || a.Contains(' ') ? "\"" + a + "\"" : a));

    /// <summary>
    /// Arguments typed on one line, split as a shell would: spaces separate them, double quotes
    /// keep spaces inside one ("C:\My Games\x.conf"), and \" is a quote. A backslash before
    /// anything else stays, so Windows paths need no doubling.
    /// </summary>
    public static List<string> SplitArguments(string? line)
    {
        var r = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return r;
        var cur = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; any = true; }
            else if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { r.Add(cur.ToString()); cur.Clear(); any = false; }
            }
            else { cur.Append(c); any = true; }
        }
        if (any) r.Add(cur.ToString());
        return r;
    }

    // ---- why a program does not start

    /// <summary>
    /// The DLLs a Windows program loads at start that are nowhere Windows looks (beside it, the
    /// system folders, the PATH): a program missing one is stopped before it runs, silently when
    /// started from another program. Empty off Windows, for a file that is not a program, and for
    /// the system's own API sets. Only the program's own imports are read, not its DLLs' imports.
    /// </summary>
    public static List<string> MissingDlls(string exe)
    {
        var missing = new List<string>();
        if (!OperatingSystem.IsWindows() || !File.Exists(exe)) return missing;
        List<string> imports;
        try { imports = Imports(File.ReadAllBytes(exe)); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return missing; }
        var dirs = new List<string> { Path.GetDirectoryName(Path.GetFullPath(exe))!, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(d => d.Length > 0));
        foreach (var dll in imports)
        {
            string lower = dll.ToLowerInvariant();
            if (lower.StartsWith("api-ms-") || lower.StartsWith("ext-ms-")) continue;
            bool found = false;
            foreach (var d in dirs)
            {
                try { if (File.Exists(Path.Combine(d, dll))) { found = true; break; } }
                catch (ArgumentException) { }       // a PATH entry with characters a path cannot hold
            }
            if (!found) missing.Add(dll);
        }
        return missing;
    }

    /// <summary>"SDL3.dll, which is" / "SDL3.dll and libmt32emu-2.dll, which are": the missing DLLs in a sentence.</summary>
    public static string WhichAre(IReadOnlyList<string> dlls) =>
        (dlls.Count == 1 ? dlls[0] : string.Join(", ", dlls.Take(dlls.Count - 1)) + " and " + dlls[^1]) + (dlls.Count == 1 ? ", which is" : ", which are");

    /// <summary>The DLL names a PE program imports (its import table; delay-loaded ones are not needed to start). Empty for anything else.</summary>
    public static List<string> Imports(byte[] b)
    {
        var r = new List<string>();
        if (b.Length < 0x40 || b[0] != 'M' || b[1] != 'Z') return r;
        int pe = BitConverter.ToInt32(b, 0x3C);
        if (pe < 0 || pe + 24 > b.Length || b[pe] != 'P' || b[pe + 1] != 'E' || b[pe + 2] != 0 || b[pe + 3] != 0) return r;
        int sections = BitConverter.ToUInt16(b, pe + 6);
        int optSize = BitConverter.ToUInt16(b, pe + 20);
        int opt = pe + 24;
        if (opt + optSize > b.Length || optSize < 2) return r;
        ushort magic = BitConverter.ToUInt16(b, opt);
        int dirs = magic == 0x20B ? opt + 112 : magic == 0x10B ? opt + 96 : -1;
        if (dirs < 0 || dirs + 16 > opt + optSize) return r;
        uint importRva = BitConverter.ToUInt32(b, dirs + 8);
        if (importRva == 0) return r;
        int secTable = opt + optSize;
        int Offset(uint rva)
        {
            for (int i = 0; i < sections; i++)
            {
                int s = secTable + i * 40;
                if (s + 40 > b.Length) return -1;
                uint vsize = BitConverter.ToUInt32(b, s + 8), va = BitConverter.ToUInt32(b, s + 12), rawSize = BitConverter.ToUInt32(b, s + 16), raw = BitConverter.ToUInt32(b, s + 20);
                uint size = Math.Max(vsize, rawSize);
                if (rva >= va && rva < va + size) { long o = raw + (rva - va); return o < b.Length ? (int)o : -1; }
            }
            return -1;
        }
        int at = Offset(importRva);
        if (at < 0) return r;
        for (int n = 0; n < 4096 && at + 20 <= b.Length; n++, at += 20)
        {
            uint nameRva = BitConverter.ToUInt32(b, at + 12);
            if (nameRva == 0) break;
            int name = Offset(nameRva);
            if (name < 0) continue;
            int end = name;
            while (end < b.Length && b[end] != 0 && end - name < 260) end++;
            string dll = Encoding.ASCII.GetString(b, name, end - name);
            if (dll.Length > 0 && !r.Contains(dll, StringComparer.OrdinalIgnoreCase)) r.Add(dll);
        }
        return r;
    }

    /// <summary>Copies the DLLs of <paramref name="fromFolder"/> that are not beside <paramref name="exe"/> next to it; returns their names.</summary>
    public static List<string> CopyDlls(string fromFolder, string exe)
    {
        var copied = new List<string>();
        string to = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        if (string.Equals(Path.GetFullPath(fromFolder).TrimEnd('\\', '/'), to.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return copied;
        foreach (var dll in Directory.GetFiles(fromFolder, "*.dll"))
        {
            string dest = Path.Combine(to, Path.GetFileName(dll));
            if (File.Exists(dest)) continue;
            File.Copy(dll, dest);
            copied.Add(Path.GetFileName(dll));
        }
        return copied;
    }

    /// <summary>What a program's exit code says, when it stopped at once: Windows' own codes for a program that could not start, else the code.</summary>
    public static string WhyItStopped(int exitCode) => unchecked((uint)exitCode) switch
    {
        0xC0000135 => "Windows could not find a DLL it needs (STATUS_DLL_NOT_FOUND)",
        0xC0000139 => "a DLL beside it is not the version it was built with (STATUS_ENTRYPOINT_NOT_FOUND)",
        0xC000007B => "a DLL beside it is for another kind of Windows (32-bit and 64-bit mixed: STATUS_INVALID_IMAGE_FORMAT)",
        0xC0000142 => "a DLL it needs failed to start (STATUS_DLL_INIT_FAILED)",
        0xC0000005 => "it crashed (an access violation)",
        _ => "it ended with code " + exitCode + (exitCode < 0 || exitCode > 0xFFFF ? string.Format(" (0x{0:X8})", unchecked((uint)exitCode)) : ""),
    };

    /// <summary>
    /// Watches a program just started: why it stopped, when it ended with an error within
    /// <paramref name="within"/>; null when it is still running or ended cleanly.
    /// </summary>
    public static async Task<string?> EarlyExit(Process? p, TimeSpan within)
    {
        if (p == null) return "it did not start";
        try
        {
            using var cts = new CancellationTokenSource(within);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
            return p.ExitCode == 0 ? null : WhyItStopped(p.ExitCode);
        }
        catch (InvalidOperationException) { return null; }
    }
}
