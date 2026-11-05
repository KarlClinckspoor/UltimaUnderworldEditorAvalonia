using UwModManager.Core;
using Xunit;

namespace UwModManager.Tests;

/// <summary>How the games start: the manager's DOSBox setup, the player's settings file and arguments, and a port that cannot start.</summary>
public class StartOptionsTests
{
    static (Manager m, Target t, string dosbox) DosTarget()
    {
        string folder = Env.Temp("dos");
        Directory.CreateDirectory(Path.Combine(folder, "DATA"));
        File.WriteAllText(Path.Combine(folder, "UW.EXE"), "x");
        string dosbox = Path.Combine(Env.Temp("dosbox"), "dosbox");
        File.WriteAllText(dosbox, "x");
        var m = new Manager(Env.Temp("mgr"));
        m.State.Settings.DosBox = dosbox;
        return (m, new Target { Kind = TargetKind.Dos, Folder = folder, Game = "uw1", Name = "UW" }, dosbox);
    }

    [Fact]
    public void Without_gogs_setup_the_manager_writes_one_with_gogs_settings()
    {
        var (m, t, _) = DosTarget();
        var psi = Launcher.DosBox(m.Paths, m.State.Settings, t);
        string conf = File.ReadAllText(psi.ArgumentList[1]);
        // GOG's dosboxULTIMA1.conf / dosboxULTIMA2.conf: what the games' sound setup expects, and the machine
        foreach (var s in new[] { "machine=svga_s3", "memsize=16", "cycles=max", "sbtype=sbpro1", "sbbase=220", "irq=7", "dma=1", "mpu401=intelligent", "ems=true", "xms=true", "aspect=true" })
            Assert.Contains(s + "\n", conf);
        int auto = conf.IndexOf("[autoexec]");
        Assert.True(auto > conf.IndexOf("[dos]"));
        Assert.Contains("mount c \"" + t.Folder + "\"\nc:\ncls\nUW.EXE\nexit\n", conf[auto..]);
        Assert.DoesNotContain("\r", conf);
    }

    [Fact]
    public void The_players_settings_file_comes_last_then_the_arguments()
    {
        var (m, t, _) = DosTarget();
        string mine = Path.Combine(Env.Temp("mine"), "my settings.conf");
        File.WriteAllText(mine, "[sdl]\nfullscreen=false\n");
        t.ExtraConfig = mine;
        t.ExtraArguments = "-fullscreen  -c \"echo hello there\"";
        var psi = Launcher.DosBox(m.Paths, m.State.Settings, t);
        Assert.Equal(new[] { "-conf", Launcher.ManagerConf(m.Paths, t), "-conf", mine, "-fullscreen", "-c", "echo hello there" }, psi.ArgumentList);
        // with GOG's setups: theirs, then the player's, then the arguments, then GOG's own ending
        File.WriteAllText(Path.Combine(t.Folder, "dosboxUW.conf"), "[sdl]\n");
        File.WriteAllText(Path.Combine(t.Folder, "dosboxUW_single.conf"), "[autoexec]\n");
        t.ExtraArguments = "-fullscreen";
        psi = Launcher.DosBox(m.Paths, m.State.Settings, t);
        Assert.Equal(new[] { "-conf", Path.Combine(t.Folder, "dosboxUW.conf"), "-conf", Path.Combine(t.Folder, "dosboxUW_single.conf"), "-conf", mine, "-fullscreen", "-noconsole", "-c", "exit" }, psi.ArgumentList);
        // a settings file that is gone is said, not skipped
        t.ExtraConfig = mine + ".gone";
        Assert.Throws<FileNotFoundException>(() => Launcher.DosBox(m.Paths, m.State.Settings, t));
    }

    [Fact]
    public void Arguments_split_as_a_shell_would()
    {
        Assert.Empty(Launcher.SplitArguments("   "));
        Assert.Equal(new[] { "--scale", "4", "--mouse", "lock" }, Launcher.SplitArguments(" --scale 4\t--mouse  lock "));
        Assert.Equal(new[] { "-conf", @"C:\My Games\a.conf", "" }, Launcher.SplitArguments("-conf \"C:\\My Games\\a.conf\" \"\""));
        Assert.Equal(new[] { "say \"hi\"" }, Launcher.SplitArguments("\"say \\\"hi\\\"\""));
        Assert.Equal(new[] { @"C:\dos\x" }, Launcher.SplitArguments(@"C:\dos\x"));
    }

    [Fact]
    public void Exhumed_takes_the_players_settings_and_arguments()
    {
        var m = new Manager(Env.Temp("mgr"));
        string port = Path.Combine(Env.Temp("port"), "uw1port");
        File.WriteAllText(port, "not a program");
        string home = Env.Temp("home");
        string mine = Path.Combine(Env.Temp("mine"), "mine.cfg");
        File.WriteAllText(mine, "# mine\nscale=4\n\nstart-square=1,1\nmouse = lock\nnot a setting\n");
        var t = new Target { Kind = TargetKind.Exhumed, Folder = Env.Temp("game"), Game = "uw1", PortExe = port, Name = "Exhumed", ExtraConfig = mine, ExtraArguments = "--no-aspect" };
        ExhumedGame.SetConfig(home, "uw1", "start-square", "30,4");
        ExhumedGame.SetConfig(home, "uw1", "scale", "2");
        var psi = Launcher.Exhumed(t, home, new[] { "skip-intro" });
        Assert.Equal(new[] { "--data", t.Folder, "--home", home, "--no-aspect" }, psi.ArgumentList);
        var cfg = ExhumedGame.ReadConfig(Path.Combine(home, "uw1port.cfg"));
        Assert.Contains(("scale", "4"), cfg);
        Assert.DoesNotContain(("scale", "2"), cfg);
        Assert.Contains(("mouse", "lock"), cfg);
        Assert.Contains(("enhance", "skip-intro"), cfg);
        // the start square is the mod's
        Assert.Contains(("start-square", "30,4"), cfg);
        Assert.DoesNotContain(("start-square", "1,1"), cfg);
    }

    /// <summary>A 64-bit Windows program of one section whose import table names <paramref name="dll"/>.</summary>
    static byte[] TinyPe(string dll)
    {
        var b = new byte[0x400];
        void U16(int at, int v) => BitConverter.GetBytes((ushort)v).CopyTo(b, at);
        void U32(int at, uint v) => BitConverter.GetBytes(v).CopyTo(b, at);
        b[0] = (byte)'M'; b[1] = (byte)'Z';
        U32(0x3C, 0x80);
        b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
        U16(0x84, 0x8664);
        U16(0x86, 1);                   // one section
        U16(0x94, 0xF0);                // the optional header's size
        U16(0x98, 0x20B);               // PE32+
        U32(0x98 + 112 + 8, 0x1000);    // the import table's RVA
        U32(0x98 + 112 + 12, 40);
        int sec = 0x98 + 0xF0;
        U32(sec + 8, 0x200); U32(sec + 12, 0x1000); U32(sec + 16, 0x200); U32(sec + 20, 0x200);
        U32(0x200 + 12, 0x1040);        // the first descriptor's name
        System.Text.Encoding.ASCII.GetBytes(dll).CopyTo(b, 0x240);
        return b;
    }

    [Fact]
    public void A_dll_the_program_needs_and_lacks_is_named()
    {
        Assert.Equal(new[] { "SDL3.dll" }, Launcher.Imports(TinyPe("SDL3.dll")));
        Assert.Empty(Launcher.Imports(new byte[] { (byte)'M', (byte)'Z' }));
        Assert.Empty(Launcher.Imports(System.Text.Encoding.ASCII.GetBytes("not a program at all, but long enough to have a header of sixty-four bytes or more")));
        if (!OperatingSystem.IsWindows()) return;
        string dir = Env.Temp("port");
        string exe = Path.Combine(dir, "uw1port.exe");
        string dllName = "uwmm-test-" + Guid.NewGuid().ToString("N")[..8] + ".dll";
        File.WriteAllBytes(exe, TinyPe(dllName));
        Assert.Equal(new[] { dllName }, Launcher.MissingDlls(exe));
        // copied from a release's folder, it is found beside the program
        string release = Env.Temp("release");
        File.WriteAllText(Path.Combine(release, dllName), "x");
        Assert.Equal(new[] { dllName }, Launcher.CopyDlls(release, exe));
        Assert.Empty(Launcher.MissingDlls(exe));
        Assert.Empty(Launcher.CopyDlls(release, exe));
        // the system's own DLLs are found where Windows keeps them
        File.WriteAllBytes(exe, TinyPe("KERNEL32.dll"));
        Assert.Empty(Launcher.MissingDlls(exe));
        Assert.Contains("DLL", Launcher.WhyItStopped(unchecked((int)0xC0000135)));
        Assert.Contains("code 3", Launcher.WhyItStopped(3));
    }
}
