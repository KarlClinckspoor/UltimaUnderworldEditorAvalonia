using UltimateEditor.Games;
using Underworld.Mods;
using UwEditor.TestSupport;
using UwModManager.Core;
using Xunit;

namespace UwModManager.Tests;

/// <summary>
/// GOG's 2025 release: the install plays from its CD image (game.gog), so the manager copies the
/// game out of it into its own folder, puts the mods there, and never writes to the install.
/// The image here is made from the test data, laid out as GOG's (UW and UW2 on one disc).
/// </summary>
public class GogTests
{
    /// <summary>A GOG install of <paramref name="game"/> as the 2025 release lays it out, or null without the test data.</summary>
    static string Install(string game)
    {
        var folders = new Dictionary<string, string>();
        foreach (var g in new[] { "UW", "UW2" })
            if (Directory.Exists(Path.Combine(Env.Root, g, "DATA"))) folders[g] = Path.Combine(Env.Root, g);
        if (!folders.ContainsKey(game)) { Env.Copy(game); return null; }     // Env.Copy fails when the data is required
        string dir = Env.Temp("gog-" + game);
        FakeGogImage.FromFolders(Path.Combine(dir, "game.gog"), folders);
        string n = game == "UW2" ? "2" : "1";
        // the hard-disk folder of the CD version: the sound setup the installer wrote
        Directory.CreateDirectory(Path.Combine(dir, "UNDEROM" + n, "DATA"));
        File.WriteAllText(Path.Combine(dir, "UNDEROM" + n, "DATA", "UW.CFG"), "4 7 220 1 sound\r\n2 7 220 1 speech\r\n");
        File.WriteAllText(Path.Combine(dir, "UNDEROM" + n, "MARKER.FIL"), "Do not delete me.  Thank you.");
        // GOG's settings, and the setup that mounts the image and starts the game from it
        File.WriteAllText(Path.Combine(dir, "dosboxULTIMA" + n + ".conf"), "[sdl]\nfullscreen=true\n[dosbox]\nmachine=svga_s3\n");
        File.WriteAllText(Path.Combine(dir, "dosboxULTIMA" + n + "_single.conf"), "[autoexec]\n@echo off\nmount C \"..\"\nimgmount d \"..\\game.gog\" -t iso -fs iso\nc:\nGOG.BAT\n");
        Directory.CreateDirectory(Path.Combine(dir, "DOSBOX"));
        File.WriteAllBytes(Path.Combine(dir, "DOSBOX", "dosbox.exe"), new byte[] { 0x4D, 0x5A });
        return dir;
    }

    /// <summary>Every file of a folder, by its hash: to show the install was not touched.</summary>
    static Dictionary<string, string> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(dir, f), f => Files.Sha(File.ReadAllBytes(f)));

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void The_game_is_copied_out_of_the_image_and_the_install_is_left_alone(string game)
    {
        string install = Install(game);
        if (install == null) return;
        var before = Snapshot(install);
        var m = new Manager(Env.Temp("mgr"));
        Assert.True(Manager.IsGogImageInstall(install));

        // an image of a hash the manager does not know is refused unless the player says so
        var u = Assert.Throws<UnknownImageException>(() => m.AddGog(install));
        Assert.Equal(GogImage.Sha256(Path.Combine(install, "game.gog")), u.Sha256);
        Assert.Empty(m.State.Targets);
        Assert.Empty(Directory.GetFileSystemEntries(m.Paths.Games));

        var t = m.AddGog(install, unknownImage: true);
        Assert.True(t.IsGogCopy);
        Assert.Equal(game == "UW2" ? "uw2" : "uw1", t.Game);
        Assert.StartsWith(m.Paths.Games, t.Folder);
        Assert.Equal(Env.Read(Path.Combine(Env.Root, game), t.ExeName), Env.Read(t.Folder, t.ExeName));
        Assert.True(m.HasGogExe(t));
        Assert.Empty(DosGame.LocalChanges(t));
        // the install's sound setup is the copy's (the player's setup, not a game file mods change)
        Assert.StartsWith("4 7 220 1 sound", File.ReadAllText(Path.Combine(t.Folder, "DATA", "UW.CFG")));
        Assert.False(File.Exists(Path.Combine(t.Folder, "MARKER.FIL")));

        // a mod goes into the copy; the install stays byte for byte as it was
        var kind = Env.Kind(game);
        var mod = Env.MakeMod(game, f => Env.EditLevel(f, kind, 1, 200, 4), "gog.test");
        m.Library.Add(mod);
        m.UseDos(t, Library.KeyOf(mod), false);
        Assert.NotEqual(DosGame.Original(t, m.Vault, "DATA/LEV.ARK"), Env.Read(t.Folder, "DATA/LEV.ARK"));
        Assert.Equal(before, Snapshot(install));

        // and the same game of the same install is not added twice
        Assert.Throws<InvalidOperationException>(() => m.AddGog(install, unknownImage: true));
        Assert.True(Manager.ImageUnchanged(t));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void It_plays_with_gogs_dosbox_and_settings_on_the_copy(string game)
    {
        string install = Install(game);
        if (install == null) return;
        var m = new Manager(Env.Temp("mgr"));
        var t = m.AddGog(install, unknownImage: true);
        var psi = Launcher.DosBox(m.Paths, m.State.Settings, t);
        string n = game == "UW2" ? "2" : "1";
        Assert.Equal(Path.GetFullPath(Path.Combine(install, "DOSBOX", "dosbox.exe")), Path.GetFullPath(psi.FileName), ignoreCase: true);
        Assert.Equal(Path.Combine(install, "DOSBOX"), psi.WorkingDirectory);
        // GOG's settings, not the setup that mounts game.gog; then the manager's start
        Assert.Equal(new[] { "-conf", Path.Combine(install, "dosboxULTIMA" + n + ".conf"), "-conf", Launcher.ManagerConf(m.Paths, t), "-noconsole", "-c", "exit" }, psi.ArgumentList);
        string conf = File.ReadAllText(Launcher.ManagerConf(m.Paths, t));
        Assert.Contains("mount c \"" + t.Folder + "\"", conf);
        Assert.Contains("\n" + t.ExeName + "\n", conf);
        Assert.DoesNotContain("game.gog", conf.Split('\n').Where(l => !l.StartsWith('#')).Aggregate("", (a, b) => a + b + "\n"));
        Assert.DoesNotContain("[sdl]", conf);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void Forgetting_it_deletes_the_copy_unless_it_holds_saved_games(string game)
    {
        string install = Install(game);
        if (install == null) return;
        var m = new Manager(Env.Temp("mgr"));
        var t = m.AddGog(install, unknownImage: true);
        Assert.Null(m.Remove(t));
        Assert.False(Directory.Exists(t.Folder));

        var t2 = m.AddGog(install, unknownImage: true);
        Directory.CreateDirectory(Path.Combine(t2.Folder, "SAVE1"));
        File.WriteAllBytes(Path.Combine(t2.Folder, "SAVE1", "PLAYER.DAT"), new byte[300]);
        File.WriteAllBytes(Path.Combine(t2.Folder, "SAVE1", "LEV.ARK"), new byte[300]);
        File.WriteAllText(Path.Combine(t2.Folder, "SAVE1", "DESC"), "mine");
        string note = m.Remove(t2);
        Assert.NotNull(note);
        Assert.True(File.Exists(Path.Combine(t2.Folder, "SAVE1", "LEV.ARK")));
        Assert.Empty(m.State.Targets);
    }

    [Fact]
    public void An_install_that_does_not_say_its_game_needs_one_and_exhumed_reads_the_copy()
    {
        string install = Install("UW");
        if (install == null) return;
        foreach (var f in Directory.GetFiles(install, "dosbox*.conf")) File.Delete(f);
        Directory.Delete(Path.Combine(install, "UNDEROM1"), true);
        var m = new Manager(Env.Temp("mgr"));
        Assert.Throws<InvalidOperationException>(() => m.AddGog(install, unknownImage: true));
        // before the copy, Exhumed has nothing to read there
        Assert.Throws<InvalidOperationException>(() => m.AddExhumed(Path.Combine(install, "uw1port.exe"), install));
        var t = m.AddGog(install, GameKind.UW1, unknownImage: true);
        Assert.Equal(GameKind.UW1, t.GameKind);
        var x = m.AddExhumed(Path.Combine(install, "uw1port.exe"), install);
        Assert.Equal(t.Folder, x.Folder);
    }
}
