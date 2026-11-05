using System.IO.Compression;
using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;
using UwModManager.Core;
using Xunit;

namespace UwModManager.Tests;

/// <summary>Packages are untrusted: what they name stays inside the game; and the smaller guards
/// (save backups, UltimaHacks on an executable that has it, library names).</summary>
public class SafetyTests
{
    /// <summary>A package naming <paramref name="file"/>, written by hand as an attacker would.</summary>
    static byte[] Evil(string file, bool added = false)
    {
        byte[] delta = Encoding.ASCII.GetBytes("pwned");
        string h = new string('a', 64);
        string json = "{\"format\":2,\"game\":\"uw1\",\"id\":\"evil.mod\",\"name\":\"Evil\",\"version\":\"1\",\"files\":[{\"path\":\"" + file.Replace("\\", "\\\\") +
            "\",\"base\":" + (added ? "null" : "{\"sha256\":\"" + h + "\",\"size\":1}") + ",\"target\":{\"sha256\":\"" + h + "\",\"size\":5},\"delta\":{\"path\":\"deltas/" +
            file.Replace("\\", "\\\\") + ".uwd\",\"sha256\":\"" + ModPackage.Sha256(delta) + "\"}}]}";
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var s = z.CreateEntry("mod.json").Open()) { var b = Encoding.UTF8.GetBytes(json); s.Write(b, 0, b.Length); }
            using (var s = z.CreateEntry("deltas/" + file + ".uwd").Open()) s.Write(delta, 0, delta.Length);
        }
        return ms.ToArray();
    }

    [Theory]
    [InlineData("DATA/../UW.EXE")]
    [InlineData("DATA/../../victim.txt")]
    [InlineData("/DATA/LEV.ARK")]
    [InlineData("DATA//LEV.ARK")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("DATA/./LEV.ARK")]
    [InlineData("SAVE1/PLAYER.DAT")]
    [InlineData("UW.CFG")]
    [InlineData("DATA/UW.CFG")]
    [InlineData("DOSBOX.CONF")]
    public void A_package_naming_a_path_outside_the_game_files_is_refused(string file)
    {
        Assert.False(GamePaths.IsGameFile(file, GameKind.UW1));
        byte[] pkg = Evil(file);
        Assert.Throws<ModException>(() => ModPackage.FromBytes(pkg));
        string lib = Env.Temp("lib"), f = Path.Combine(Env.Temp("pkg"), "evil.uwmod");
        File.WriteAllBytes(f, pkg);
        Assert.Throws<ModException>(() => new Library(lib).Import(f));
        Assert.Empty(new Library(lib).List());
    }

    [Theory]
    [InlineData("DATA/NEW.DAT")]
    [InlineData("SOUND/UW99.XMI")]
    [InlineData("CUTS/CS999.N00")]
    public void A_package_adding_a_file_the_game_never_opens_is_refused(string file)
    {
        Assert.Throws<ModException>(() => ModPackage.FromBytes(Evil(file, added: true)));
    }

    [Theory]
    [InlineData("DATA/LEV.ARK")]
    [InlineData("data/lev.ark")]
    [InlineData("UW.EXE")]
    [InlineData("SOUND/UW01.XMI")]
    [InlineData("CRIT/CR00PAGE.N00")]
    public void Game_files_are_allowed(string file)
    {
        Assert.True(GamePaths.IsGameFile(file, GameKind.UW1));
    }

    [Fact]
    public void Leftover_temporary_files_are_not_game_files()
    {
        Assert.False(GamePaths.IsGameFile("DATA/LEV.ARK.uwmm-tmp", GameKind.UW1));
        Assert.False(GamePaths.IsGameFile("DATA/LEV.ARK.UWMOD-TMP", GameKind.UW1));
        Assert.False(GamePaths.IsGameFile("DATA/LEV.ARK.BKP", GameKind.UW1));
        Assert.False(GamePaths.IsGameFile("DATA/LEV.ARK.ORIG", GameKind.UW1));
    }

    [Fact]
    public void Save_backups_never_replace_each_other_and_stay_with_their_folder()
    {
        var m = new Manager(Env.Temp("mgr"));
        string a = Env.Temp("gameA"), b = Env.Temp("gameB");
        foreach (var g in new[] { a, b })
        {
            Directory.CreateDirectory(Path.Combine(g, "SAVE1"));
            File.WriteAllBytes(Path.Combine(g, "SAVE1", "LEV.ARK"), Encoding.ASCII.GetBytes(g));
        }
        var slotA = Saves.List(a)[0];
        string first = Saves.Backup(m.Paths, m.Vault, slotA, "before-restore")!;
        string second = Saves.Backup(m.Paths, m.Vault, slotA, "before-restore")!;
        Assert.NotEqual(first, second);
        Assert.Equal(2, Saves.Backups(m.Paths, a, 1).Count);
        Assert.Empty(Saves.Backups(m.Paths, b));
        Assert.Empty(Saves.Backups(m.Paths, a, 2));
    }

    [Fact]
    public void A_restore_whose_files_are_missing_leaves_the_slot_alone()
    {
        var m = new Manager(Env.Temp("mgr"));
        string g = Env.Temp("game");
        Directory.CreateDirectory(Path.Combine(g, "SAVE1"));
        File.WriteAllText(Path.Combine(g, "SAVE1", "LEV.ARK"), "current");
        string backup = Path.Combine(Env.Temp("b"), "x.json");
        File.WriteAllText(backup, "{\"files\":{\"LEV.ARK\":\"sha256:" + new string('0', 64) + "\"}}");
        Assert.Throws<FileNotFoundException>(() => Saves.Restore(m.Paths, m.Vault, backup, Saves.List(g)[0]));
        Assert.Equal("current", File.ReadAllText(Path.Combine(g, "SAVE1", "LEV.ARK")));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void An_executable_that_already_has_UltimaHacks_keeps_it_and_takes_a_start_square(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return;
        var kind = Env.Kind(game);
        byte[] gog = Env.Read(folder, ExeIdentity.ExeName(kind));
        Assert.True(ExeIdentity.IsGogOriginal(gog, kind));
        var hack = Hacks.For(kind);
        Assert.False(hack.IsAppliedTo(gog));
        byte[] hacked = hack.Apply(gog);
        Assert.True(hack.IsAppliedTo(hacked));
        // taken as the original, it is used as it is
        Assert.Equal(hacked, Hacks.BuildExe(hacked, kind, null));
        // the start square is found in the hacked executable too
        byte[] moved = Hacks.BuildExe(gog, kind, (30, 4));
        Assert.True(StartPosition.Read(moved, kind, out int x, out int y));
        Assert.Equal((30, 4), (x, y));
        Assert.Equal(hacked.Length, moved.Length);
        // another executable is not hacked
        byte[] other = (byte[])gog.Clone(); other[100] ^= 1;
        Assert.Throws<ModException>(() => Hacks.BuildExe(other, kind, null));
    }

    [Fact]
    public void Library_keys_that_make_the_same_file_name_are_both_kept()
    {
        var lib = new Library(Env.Temp("lib"));
        foreach (var id in new[] { "x-y", "x/y" })
        {
            var pkg = new ModPackage { Id = id, Name = id, Version = "1", Game = GameKind.UW1 };
            pkg.Add("DATA/LEV.ARK", new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 4 });
            lib.Add(pkg);
        }
        Assert.Equal(new[] { "x-y@1", "x/y@1" }, lib.List().Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));
    }
}
