using UltimateEditor.Games;
using Underworld.Mods;

namespace UwModManager.Core;

/// <summary>Game files by the paths a mod names them (upper case, '/'): a folder, the vault's originals, an Exhumed home over its game.</summary>
public interface IFileSource
{
    IEnumerable<string> List();
    byte[]? Read(string rel);
}

/// <summary>
/// A game folder: the files a mod may change (Underworld.Mods.GamePaths: the executable, DATA,
/// CRIT, CUTS, SOUND; not the player's UW.CFG, saved games or backups), found whatever the
/// case of their names on disk.
/// </summary>
public sealed class GameDir : IFileSource
{
    public string Root { get; }
    public GameKind Game { get; }

    public GameDir(string root, GameKind game) { Root = root; Game = game; }

    public IEnumerable<string> List() => GamePaths.List(Root, Game);

    public byte[]? Read(string rel) { string? p = Resolve(rel); return p == null ? null : File.ReadAllBytes(p); }

    /// <summary>The file on disk, or null.</summary>
    public string? Resolve(string rel) => GamePaths.Find(Root, rel);

    /// <summary>Where a file goes: its folders as the game has them (whatever their case), else as the mod names them.</summary>
    public string PathFor(string rel)
    {
        var parts = rel.Split('/');
        string cur = Root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string? next = Directory.Exists(cur) ? Directory.GetDirectories(cur).FirstOrDefault(d => string.Equals(Path.GetFileName(d), parts[i], StringComparison.OrdinalIgnoreCase)) : null;
            cur = next ?? Path.Combine(cur, parts[i]);
        }
        return Path.Combine(cur, parts[^1]);
    }

    /// <summary>A directory under root, whatever its case, or null.</summary>
    public static string? FindDir(string root, string name) =>
        Directory.Exists(root) ? Directory.GetDirectories(root).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) : null;

    /// <summary>The full path of <paramref name="path"/> when it is inside <paramref name="root"/>, else an exception: the last guard before writing.</summary>
    public static string Inside(string root, string path)
    {
        string r = Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        string p = Path.GetFullPath(path);
        if (!p.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new ModException("refused to write outside " + root + ": " + path);
        return p;
    }
}
