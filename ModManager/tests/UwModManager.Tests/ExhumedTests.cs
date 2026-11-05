using System.Diagnostics;
using UltimateEditor.Games;
using Underworld.Mods;
using UwModManager.Core;
using Xunit;

namespace UwModManager.Tests;

/// <summary>Exhumed homes: a mod's files over the game folder, its settings, and the fork's features.</summary>
public class ExhumedTests
{
    static string FeaturesFor(string game) => game == "UW2"
        ? "features 1 uw2port\nstart-square\nexe-patch 0x39C50-0x6321C 0x63790-0x643D0 0x68A90-0x717A0\ndata-overlay\nenhance skip-intro,wrap-menu,subtitles\n"
        : "features 1 uw1port\nstart-square\nexe-patch 0x3C7B0-0x58FC4 0x59440-0x59EF0 0x5DCC0-0x65700\ndata-overlay\nenhance skip-intro,wrap-menu,tidy-objects\n";

    static Target Exhumed(Manager m, string folder, string game, PortFeatures f, string portExe = null)
    {
        if (portExe == null)
        {
            portExe = Path.Combine(Env.Temp("port"), game == "UW2" ? "uw2port" : "uw1port");
            File.WriteAllText(portExe, "not a program");
        }
        var t = new Target { Kind = TargetKind.Exhumed, Folder = folder, Game = game == "UW2" ? "uw2" : "uw1", PortExe = portExe, Name = "Exhumed", Features = f };
        m.State.Targets.Add(t);
        return t;
    }

    [Fact]
    public void Features_parse()
    {
        var f = ExhumedGame.ParseFeatures(FeaturesFor("UW"));
        Assert.True(f.StartSquare && f.DataOverlay && f.ExePatch);
        Assert.Equal(3, f.ExePatchRanges.Count);
        Assert.Equal(0x5DCC0, f.ExePatchRanges[2][0]);
        Assert.Contains("tidy-objects", f.Enhancements);
        Assert.Null(ExhumedGame.ParseFeatures("usage: uw1port [options]"));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_mod_goes_into_a_home_of_its_own(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return;
        var kind = Env.Kind(game);
        var m = new Manager(Env.Temp("mgr"));
        var t = Exhumed(m, folder, game, ExhumedGame.ParseFeatures(FeaturesFor(game)));
        string modded = null;
        var mod = Env.MakeMod(game, f =>
        {
            Env.EditLevel(f, kind, 1, 100, 1);
            Env.SetStart(f, kind, 30, 4);
            Env.AddSound(f, game, new byte[] { 7, 7, 7 });
            modded = f;
        }, "test.ex");
        m.Library.Add(mod);
        string key = Library.KeyOf(mod);
        Assert.Empty(m.Problems(t, mod));
        string home = m.UseExhumed(t, key);
        Assert.Equal(Env.Read(modded, "DATA/LEV.ARK"), Env.Read(home, "DATA/LEV.ARK"));
        Assert.Equal(new byte[] { 7, 7, 7 }, Env.Read(home, Env.NewSound(game)));
        // the executable stays the port's business: the start square goes into the settings
        Assert.Null(Env.Read(home, t.ExeName));
        string cfg = File.ReadAllText(Path.Combine(home, ExhumedGame.ConfigName(t.Game)));
        Assert.Contains("start-square=30,4", cfg);
        Assert.DoesNotContain("exe-patch=", cfg);
        // the game folder is untouched
        string fresh = Env.Copy(game);
        foreach (var rel in new GameDir(fresh, kind).List()) Assert.Equal(Env.Read(fresh, rel), Env.Read(folder, rel));
        Assert.Null(Env.Read(folder, Env.NewSound(game)));
        // the original game: another home, without the mod's files or settings
        string orig = m.UseExhumed(t, null);
        Assert.NotEqual(home, orig);
        Assert.False(File.Exists(Path.Combine(orig, "DATA", "LEV.ARK")));
        Assert.DoesNotContain("start-square=", File.ReadAllText(Path.Combine(orig, ExhumedGame.ConfigName(t.Game))));
        // the launch: the game folder, the home, the enhancements in the home's settings
        var psi = Launcher.Exhumed(t, home, new[] { "skip-intro", "tidy-objects" });
        Assert.Equal(new[] { "--data", folder, "--home", home }, psi.ArgumentList);
        Assert.Contains("enhance=skip-intro,tidy-objects", File.ReadAllText(Path.Combine(home, ExhumedGame.ConfigName(t.Game))));
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void Data_patches_become_the_ports_patch_file_and_code_patches_are_refused(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return;
        var kind = Env.Kind(game);
        var m = new Manager(Env.Temp("mgr"));
        var f = ExhumedGame.ParseFeatures(FeaturesFor(game));
        var t = Exhumed(m, folder, game, f);
        byte[] exe = Env.Read(folder, t.ExeName);
        int data = (int)f.ExePatchRanges[2][0] + 0x40;        // DGROUP, which the port reads from the executable
        int code = BitConverter.ToUInt16(exe, 8) * 16 + 0x200;  // seg000's code, past the MZ header
        var good = Env.MakeMod(game, g => Poke(g, t.ExeName, data, 0x33), "test.data");
        var bad = Env.MakeMod(game, g => Poke(g, t.ExeName, code, 0x01), "test.code");
        m.Library.Add(good); m.Library.Add(bad);
        Assert.Empty(m.Problems(t, good));
        Assert.Contains(m.Problems(t, bad), p => p.Contains("code"));
        Assert.Throws<ModException>(() => m.UseExhumed(t, Library.KeyOf(bad)));
        string home = m.UseExhumed(t, Library.KeyOf(good));
        Assert.Contains("exe-patch=" + ExhumedGame.PatchFile, File.ReadAllText(Path.Combine(home, ExhumedGame.ConfigName(t.Game))));
        string line = File.ReadAllLines(Path.Combine(home, ExhumedGame.PatchFile)).Single(l => !l.StartsWith("#"));
        Assert.Equal(string.Format("0x{0:X} {1:X2} {2:X2}", data, exe[data], exe[data] ^ 0x33), line);
        // and without the features (an upstream build), neither is playable
        t.Features = null;
        Assert.NotEmpty(m.Problems(t, good));
    }

    static void Poke(string folder, string exeName, int at, byte xor)
    {
        string p = GamePaths.Find(folder, exeName);
        byte[] e = File.ReadAllBytes(p); e[at] ^= xor; File.WriteAllBytes(p, e);
    }

    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void A_dos_mod_in_the_same_folder_is_hidden_from_exhumed(string game)
    {
        string folder = Env.Copy(game);
        if (folder == null) return;
        var kind = Env.Kind(game);
        var m = new Manager(Env.Temp("mgr"));
        var dos = m.AddDos(folder);
        var mod = Env.MakeMod(game, g => { Env.EditLevel(g, kind, 1, 64, 3); Env.SetStart(g, kind, 10, 10); }, "test.dos");
        m.Library.Add(mod);
        m.UseDos(dos, Library.KeyOf(mod), true);   // with UltimaHacks: an executable Exhumed refuses
        var t = Exhumed(m, folder, game, ExhumedGame.ParseFeatures(FeaturesFor(game)));
        string home = m.UseExhumed(t, null);
        Assert.Equal(DosGame.Original(dos, m.Vault, "DATA/LEV.ARK"), File.ReadAllBytes(Path.Combine(home, "DATA", "LEV.ARK")));
        byte[] exe = File.ReadAllBytes(Path.Combine(home, t.ExeName));
        Assert.True(ExeIdentity.IsGogOriginal(exe, kind));
        // the DOS game back to the original: the shields go
        m.UseDos(dos, null, false);
        home = m.UseExhumed(t, null);
        Assert.False(File.Exists(Path.Combine(home, "DATA", "LEV.ARK")));
        Assert.False(File.Exists(Path.Combine(home, t.ExeName)));
    }

    /// <summary>The real port, when the tests are given one (UWMM_TEST_UW1PORT / UWMM_TEST_UW2PORT,
    /// built from the fork's modding branch): a new game in a mod's home starts on its square.</summary>
    [Theory, MemberData(nameof(Env.Games), MemberType = typeof(Env))]
    public void The_real_port_starts_a_new_game_on_the_mods_square(string game)
    {
        string port = Environment.GetEnvironmentVariable(game == "UW2" ? "UWMM_TEST_UW2PORT" : "UWMM_TEST_UW1PORT");
        string folder = Env.Copy(game);
        if (folder == null || string.IsNullOrEmpty(port) || !File.Exists(port)) return;
        var kind = Env.Kind(game);
        var m = new Manager(Env.Temp("mgr"));
        var t = m.AddExhumed(port, folder);
        Assert.NotNull(t.Features);
        Assert.True(t.Features.StartSquare);
        var mod = Env.MakeMod(game, g => Env.SetStart(g, kind, 33, 5), "test.square");
        m.Library.Add(mod);
        t.ActiveMod = Library.KeyOf(mod);
        var psi = m.LaunchInfo(t);
        string script = Path.Combine(AppContext.BaseDirectory, "TestData", game == "UW2" ? "newgame-uw2.script" : "newgame-uw1.script");
        foreach (var a in new[] { "--hidden", "--no-audio", "--no-recording", "--input-script", script, "--exit-after", game == "UW2" ? "62000" : "46000" }) psi.ArgumentList.Add(a);
        psi.Environment[game == "UW2" ? "UW2PORT_POS_LOG" : "UW1PORT_POS_LOG"] = "1";
        psi.RedirectStandardError = true;
        using var p = Process.Start(psi);
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        Assert.Contains("mods: start square (33, 5)", err);
        var motion = err.Split('\n').Single(l => l.Contains("motion:"));
        var parts = motion.Split(' ');
        int x = int.Parse(parts[Array.IndexOf(parts, "x") + 1]), y = int.Parse(parts[Array.IndexOf(parts, "y") + 1]);
        Assert.Equal((33, 5), (x >> 8, y >> 8));
    }
}
