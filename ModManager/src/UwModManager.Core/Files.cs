using Underworld.Mods;

namespace UwModManager.Core;

public static class Files
{
    /// <summary>The hash of no file at all (a file a mod adds was not there before).</summary>
    public const string Absent = "absent";

    /// <summary>Writes through a temporary file and a rename, so a crash leaves the old file or the new one, never half of one.</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".uwmm-tmp";
        using (var f = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(data, 0, data.Length);
            f.Flush(true);
        }
        File.Move(tmp, path, true);
    }

    /// <summary>"sha256:" and the hex, or <see cref="Absent"/> for no file.</summary>
    public static string Sha(byte[]? data) => data == null ? Absent : "sha256:" + ModPackage.Sha256(data);

    public static bool Same(byte[]? a, byte[]? b) => a == null || b == null ? a == b : a.AsSpan().SequenceEqual(b);
}

/// <summary>What a mod is called, for making one (capture) or showing one.</summary>
public sealed class ModDetails
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public List<string> Authors { get; set; } = new();
}

/// <summary>Things to know about a mod that do not stop it.</summary>
public static class ModNotes
{
    /// <summary>A mod that changes the levels, the conversations' variables or the executable's start square needs a new game: saved games keep their own copies.</summary>
    public static List<string> Warnings(ModPackage m)
    {
        var r = new List<string>();
        bool levels = m.Files.Any(f => f.Path is "DATA/LEV.ARK" or "DATA/SCD.ARK" or "DATA/BABGLOBS.DAT");
        bool exe = m.Files.Any(f => f.Path == ExeIdentity.ExeName(m.Game));
        if (levels) r.Add("It changes the levels: start a new game to see the changes (saved games keep their own levels).");
        else if (exe) r.Add("It changes the executable (such as where a new game starts): start a new game to see it.");
        return r;
    }

    /// <summary>One line per file, for lists.</summary>
    public static List<string> Changes(ModPackage m) =>
        m.Files.Select(f => f.Path + (f.Added ? ": added (" + f.TargetSize.ToString("N0") + " bytes)" : ": " + f.Delta.Length.ToString("N0") + " bytes of changes")).ToList();
}
