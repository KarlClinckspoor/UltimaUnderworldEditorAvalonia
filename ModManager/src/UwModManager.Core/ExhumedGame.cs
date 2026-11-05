using System.Diagnostics;
using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;

namespace UwModManager.Core;

/// <summary>
/// Underworld Exhumed (uw1port, uw2port). The port reads the GOG folder and lays its home folder
/// over it, and never writes the game folder, so a mod goes into a home folder of its own (with
/// its own saved games): its files at their DOS paths, and the start square and the executable's
/// other byte changes in the home's settings file (the fork's --start-square
/// and --exe-patch; docs/MODDING.md there). Each mod, and the original game, has its home under
/// the manager's folder; the player's own home is never written.
/// </summary>
public static class ExhumedGame
{
    public const string PatchFile = "MODS-EXE.PATCH";

    /// <summary>Asks the port what it supports (--features). Null for a build without it.</summary>
    public static PortFeatures? Probe(string portExe)
    {
        try
        {
            var psi = new ProcessStartInfo(portExe, "--features") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (Exception) { } return null; }
            return p.ExitCode == 0 ? ParseFeatures(output) : null;
        }
        catch (Exception) { return null; }
    }

    public static PortFeatures? ParseFeatures(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("features ")) return null;
        var f = new PortFeatures();
        foreach (var l in lines.Skip(1))
        {
            var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "start-square": f.StartSquare = true; break;
                case "data-overlay": f.DataOverlay = true; break;
                case "exe-patch":
                    foreach (var r in parts.Skip(1))
                    {
                        var ab = r.Split('-');
                        if (ab.Length == 2) f.ExePatchRanges.Add(new[] { Convert.ToInt64(ab[0], 16), Convert.ToInt64(ab[1], 16) });
                    }
                    break;
                case "enhance": if (parts.Length > 1) f.Enhancements = parts[1].Split(',').ToList(); break;
            }
        }
        return f;
    }

    /// <summary>The player's own home folder of the port (where it keeps its settings and saves).</summary>
    public static string NormalHome(string game)
    {
        string name = game == "uw2" ? "uw2port" : "uw1port";
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + name);
    }

    public static string ConfigName(string game) => game == "uw2" ? "uw2port.cfg" : "uw1port.cfg";

    public static string HomeFor(AppPaths paths, Target t, string? mod)
    {
        string name = mod == null ? "original" : mod;
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return Path.Combine(paths.ExhumedHomes, t.Id, name.Replace('@', '-'));
    }

    /// <summary>
    /// What a mod does to the executable, which the port does not run: the start square it moves
    /// (null: none), and the other runs of bytes it changes (file offset, original, new). Empty
    /// for a mod that leaves the executable alone. Throws ModException when the game's executable
    /// is not the one the mod was made from.
    /// </summary>
    public static ((int X, int Y)? Start, List<(int Offset, byte[] Old, byte[] New)> Patches) ExeChange(ModPackage mod, Func<string, byte[]?> original, GameKind game)
    {
        var patches = new List<(int, byte[], byte[])>();
        var f = mod.Files.FirstOrDefault(x => x.Path == ExeIdentity.ExeName(game));
        if (f == null) return (null, patches);
        byte[] orig = original(f.Path) ?? throw new ModException("the game has no " + f.Path);
        byte[] modded = mod.BuildOne(f, orig);
        (int, int)? start = ExeChanges.MovedStart(orig, modded, game, out int x, out int y) ? (x, y) : null;
        if (orig.Length != modded.Length) { patches.Add((0, orig, modded)); return (start, patches); }
        int at = StartPosition.Find(orig, game);
        for (int i = 0; i < orig.Length; i++)
        {
            if (orig[i] == modded[i] || (at >= 0 && (i == at + 3 || i == at + 5))) continue;
            int s = i;
            while (i < orig.Length && orig[i] != modded[i] && !(at >= 0 && (i == at + 3 || i == at + 5))) i++;
            patches.Add((s, orig[s..i], modded[s..i]));
            i--;
        }
        return (start, patches);
    }

    /// <summary>Why the mod cannot be played by this build, or empty.</summary>
    public static List<string> Unsupported(ModPackage m, PortFeatures? f, Func<string, byte[]?> original, GameKind game)
    {
        var r = new List<string>();
        ((int X, int Y)? Start, List<(int Offset, byte[] Old, byte[] New)> Patches) exe;
        try { exe = ExeChange(m, original, game); }
        catch (ModException e) { r.Add(e.Message); return r; }
        if (exe.Start != null && (f == null || !f.StartSquare))
            r.Add("a moved start square needs an Exhumed build that lists start-square in --features (a build from the fork's modding branch)");
        if (exe.Patches.Count > 0 && (f == null || !f.ExePatch))
            r.Add("changes to the executable's bytes need an Exhumed build that lists exe-patch in --features (a build from the fork's modding branch)");
        else
            foreach (var p in exe.Patches)
                if (!f!.ExePatchRanges.Any(rg => p.Offset >= rg[0] && p.Offset + p.New.Length <= rg[1]))
                    r.Add(string.Format("the change at 0x{0:X} of the executable is in its code, which Exhumed does not run (only its data can be patched)", p.Offset));
        return r;
    }

    /// <summary>
    /// Makes the home folder of <paramref name="mod"/> (null: the original game) ready: seeded once
    /// from the player's own home (settings, sound cards), the mod's files written (built from the
    /// originals, byte for byte the author's), files of an earlier preparation that are no longer
    /// wanted removed, and the settings file's mod keys set (the start square, the executable's
    /// byte changes). <paramref name="shield"/>: files of the game folder a DOS mod changed in
    /// place, which this home must hide with the originals (from <paramref name="original"/>).
    /// </summary>
    public static string Prepare(AppPaths paths, Target t, ModPackage? mod, Func<string, byte[]?> original, IEnumerable<string> shield)
    {
        string home = HomeFor(paths, t, mod == null ? null : Library.KeyOf(mod));
        bool fresh = !Directory.Exists(home);
        Directory.CreateDirectory(home);
        if (fresh) Seed(home, t.Game);
        var desired = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? startSquare = null;
        var patchLines = new List<string>();
        if (mod != null)
        {
            if (mod.Game != t.GameKind) throw new ModException("the mod is for " + mod.GameName);
            var why = Unsupported(mod, t.Features, original, t.GameKind);
            if (why.Count > 0) throw new ModException("this Exhumed build cannot play the mod: " + string.Join("; ", why));
            foreach (var f in mod.Files)
                if (f.Path != t.ExeName) desired[f.Path] = mod.BuildOne(f, original(f.Path));
            var exe = ExeChange(mod, original, t.GameKind);
            if (exe.Start != null) startSquare = exe.Start.Value.X + "," + exe.Start.Value.Y;
            foreach (var p in exe.Patches) patchLines.Add(string.Format("0x{0:X} {1} {2}", p.Offset, Convert.ToHexString(p.Old), Convert.ToHexString(p.New)));
        }
        foreach (var rel in shield)
            if (!desired.ContainsKey(rel) && original(rel) is byte[] o) desired[rel] = o;
        // files written for an earlier preparation of this home that are not wanted now
        if (t.WrittenHome == home)
            foreach (var kv in t.Written)
                if (!desired.ContainsKey(kv.Key))
                {
                    string p = HomePath(home, kv.Key, t.GameKind);
                    if (File.Exists(p) && Files.Sha(File.ReadAllBytes(p)) == kv.Value) File.Delete(p);
                }
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in desired)
        {
            Files.WriteAtomic(HomePath(home, kv.Key, t.GameKind), kv.Value);
            written[kv.Key] = Files.Sha(kv.Value);
        }
        if (patchLines.Count > 0)
            File.WriteAllText(Path.Combine(home, PatchFile), "# " + (mod == null ? "" : mod.Name + " " + mod.Version) + ": byte changes of " + t.ExeName + " (OFFSET ORIGINAL NEW)\n" + string.Join("\n", patchLines) + "\n");
        else if (File.Exists(Path.Combine(home, PatchFile))) File.Delete(Path.Combine(home, PatchFile));
        SetConfig(home, t.Game, "start-square", startSquare);
        SetConfig(home, t.Game, "exe-patch", patchLines.Count > 0 ? PatchFile : null);
        t.Written = written;
        t.WrittenHome = home;
        t.ActiveMod = mod == null ? null : Library.KeyOf(mod);
        return home;
    }

    static string HomePath(string home, string rel, GameKind game)
    {
        if (!GamePaths.IsGameFile(rel, game)) throw new ModException("refused to write " + rel + ", which is not a game file");
        return GameDir.Inside(home, Path.Combine(new[] { home }.Concat(rel.Split('/')).ToArray()));
    }

    /// <summary>A new home starts with the player's settings and sound cards (not their saves or recordings).</summary>
    static void Seed(string home, string game)
    {
        string normal = NormalHome(game);
        string cfg = Path.Combine(normal, ConfigName(game));
        if (File.Exists(cfg))
        {
            var keep = File.ReadAllLines(cfg).Where(l => !ModKeys.Any(k => l.StartsWith(k + "="))).ToList();
            File.WriteAllLines(Path.Combine(home, ConfigName(game)), keep);
        }
        var uwcfg = Underworld.Mods.GamePaths.Find(normal, "DATA/UW.CFG");
        if (uwcfg != null)
        {
            Directory.CreateDirectory(Path.Combine(home, "DATA"));
            File.Copy(uwcfg, Path.Combine(home, "DATA", "UW.CFG"), true);
        }
    }

    /// <summary>The settings-file keys a mod owns (set by <see cref="Prepare"/>), which a player's extra settings file does not change.</summary>
    public static readonly IReadOnlySet<string> ModKeys = new HashSet<string>(StringComparer.Ordinal) { "start-square", "exe-patch" };

    /// <summary>A settings file's key=value lines, in order (blank lines and # comments skipped).</summary>
    public static List<(string Key, string Value)> ReadConfig(string path)
    {
        var r = new List<(string, string)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            string l = raw.Trim();
            if (l.Length == 0 || l.StartsWith('#') || l.StartsWith(';')) continue;
            int eq = l.IndexOf('=');
            if (eq <= 0) continue;
            r.Add((l[..eq].Trim(), l[(eq + 1)..].Trim()));
        }
        return r;
    }

    /// <summary>Sets (or removes, for null) a key of the home's settings file, as the port writes it.</summary>
    public static void SetConfig(string home, string game, string key, string? value)
    {
        string path = Path.Combine(home, ConfigName(game));
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string> { "# " + (game == "uw2" ? "uw2port" : "uw1port") + " settings" };
        lines.RemoveAll(l => l.StartsWith(key + "="));
        if (value != null) lines.Add(key + "=" + value);
        File.WriteAllLines(path, lines);
    }

    /// <summary>The port's arguments for a home: the game folder, the home, and the enhancements.</summary>
    public static List<string> Arguments(Target t, string home, IEnumerable<string> enhancements)
    {
        var a = new List<string> { "--data", t.Folder, "--home", home };
        var e = enhancements.Distinct().ToList();
        if (t.Features != null) e = e.Where(x => t.Features.Enhancements.Count == 0 || t.Features.Enhancements.Contains(x)).ToList();
        if (e.Count > 0) { a.Add("--enhance"); a.Add(string.Join(",", e)); }
        return a;
    }
}
