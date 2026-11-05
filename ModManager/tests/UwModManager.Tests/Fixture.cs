using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;
using UwModManager.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UwModManager.Tests;

/// <summary>
/// The game files: UW_GAMEDATA (a folder with UW and/or UW2), else the editor's git-ignored
/// tests/gamedata (found beside UltimaUnderworldEditorAvalonia.sln). A game that is not there is
/// skipped, unless UW_REQUIRE_GAMEDATA=1. Each test gets fresh copies and a manager folder of its own.
/// </summary>
public static class Env
{
    public static string Root
    {
        get
        {
            string e = Environment.GetEnvironmentVariable("UW_GAMEDATA");
            if (!string.IsNullOrEmpty(e)) return e;
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "UltimaUnderworldEditorAvalonia.sln"))) d = d.Parent;
            return d == null ? "" : Path.Combine(d.FullName, "tests", "gamedata");
        }
    }

    static bool Required => Environment.GetEnvironmentVariable("UW_REQUIRE_GAMEDATA") == "1";

    public static IEnumerable<object[]> Games()
    {
        bool any = false;
        foreach (var g in new[] { "UW", "UW2" })
        {
            if (Directory.Exists(Path.Combine(Root, g, "DATA"))) { any = true; yield return new object[] { g }; }
            else if (Required) throw new InvalidOperationException("Game files for " + g + " not found under '" + Root + "' (UW_REQUIRE_GAMEDATA=1).");
        }
        if (!any) yield return new object[] { "none" };
    }

    public static string Copy(string game)
    {
        string src = Path.Combine(Root, game);
        if (!Directory.Exists(Path.Combine(src, "DATA")))
        {
            if (Required && game != "none") throw new InvalidOperationException("Game files for " + game + " not found under '" + Root + "' (UW_REQUIRE_GAMEDATA=1).");
            return null;
        }
        string dst = Temp(game);
        CopyDir(src, dst);
        return dst;
    }

    public static string Temp(string name)
    {
        string d = Path.Combine(Path.GetTempPath(), "uwmm-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public static GameKind Kind(string game) => game == "UW2" ? GameKind.UW2 : GameKind.UW1;

    /// <summary>A mod made the editor's way: change a copy, compare it with the original, package it (and read the package back).</summary>
    public static ModPackage MakeMod(string game, Action<string> change, string id = "test.mod", string version = "1.0.0", string name = null, string description = "")
    {
        string orig = Copy(game), mod = Copy(game);
        change(mod);
        return Package(game, orig, mod, id, version, name, description);
    }

    /// <summary>The mod that turns one folder into another.</summary>
    public static ModPackage Package(string game, string orig, string mod, string id, string version = "1.0.0", string name = null, string description = "")
    {
        var changes = ModMaker.Compare(orig, mod, Kind(game));
        Assert.Empty(changes.Problems);
        var pkg = ModMaker.Make(changes, id, name ?? "Test " + id, version, description, new[] { "Tests" }, "tests");
        return ModPackage.FromBytes(pkg.ToBytes());
    }

    /// <summary>Changes a byte of a level's map block (level 1 is block 0 in both games).</summary>
    public static void EditLevel(string folder, GameKind kind, int level, int at, byte xor)
    {
        string lev = GamePaths.Find(folder, "DATA/LEV.ARK");
        var ar = Archive.Load(lev, kind);
        byte[] blk = ar.Read(level - 1);
        blk[at] ^= xor;
        ar.Write(level - 1, blk);
        ar.Save(lev);
    }

    /// <summary>Changes a string, with the editor's own STRINGS.PAK writer.</summary>
    public static void EditString(string folder, int block, int index, string text)
    {
        string p = GamePaths.Find(folder, "DATA/STRINGS.PAK");
        var s = UwEditor.Core.Conversations.StringsPakFile.FromBytes(File.ReadAllBytes(p));
        var list = s.Get(block);
        list[index] = text;
        s.Set(block, list);
        File.WriteAllBytes(p, s.ToBytes());
    }

    public static string String(string folder, int block, int index) =>
        UwEditor.Core.Conversations.StringsPakFile.FromBytes(Read(folder, "DATA/STRINGS.PAK")).Get(block)[index];

    public static void SetStart(string folder, GameKind kind, int x, int y)
    {
        string p = GamePaths.Find(folder, kind == GameKind.UW2 ? "UW2.EXE" : "UW.EXE");
        byte[] exe = File.ReadAllBytes(p);
        Assert.True(StartPosition.Patch(exe, kind, x, y));
        File.WriteAllBytes(p, exe);
    }

    /// <summary>A sound file the game opens by number but does not ship, which a mod may add.</summary>
    public static string NewSound(string game) => game == "UW2" ? "SOUND/UW05.VOC" : "SOUND/21.VOC";

    public static void AddSound(string folder, string game, byte[] data) => File.WriteAllBytes(new GameDir(folder, Kind(game)).PathFor(NewSound(game)), data);

    public static byte[] Read(string folder, string rel) { string p = GamePaths.Find(folder, rel); return p == null ? null : File.ReadAllBytes(p); }
}
