using System.Text;
using UltimateEditor.Games;
using Underworld.Mods;
using UwModManager.Core;

namespace UwModManager.Cli;

/// <summary>uwmod: the mod manager from the command line (the program's operations, for scripts and tests).</summary>
public static class Program
{
    const string Help = @"uwmod: Ultima Underworlds Mod Manager on the command line

  uwmod [--home DIR] COMMAND ...      DIR: the manager's folder (default: the app's, or UWMM_HOME)

Targets (games the manager installs mods into):
  targets                             list them
  add-dos FOLDER [--name N] [--as-is] take over a DOS game folder (its originals go into the
                                      vault); --as-is: take its current files as the originals,
                                      whatever a manager recorded in it before
  add-exhumed PORT [--data FOLDER] [--name N]
                                      add an Exhumed build (uw1port/uw2port) and the game
                                      folder it reads
  remove TARGET                       forget a target (a DOS folder must have the original
                                      game back first: uwmod use TARGET --original)
  status TARGET                       show the mod in use, files changed outside the manager,
                                      and whether the executable is GOG's

Mods:
  library                             list the imported mods
  import FILE.uwmod                   add a package to the library
  info FILE.uwmod|MOD                 show a package's details
  use TARGET MOD|--original [--ultimahacks] [--overwrite] [--dry-run]
                                      use a mod (one at a time) or the original game;
                                      --ultimahacks (DOS): use UltimaHacks' executable
  capture TARGET --id ID --name NAME [--version V] [--author A] [-o FILE]
                                      capture a DOS folder's changes as a mod (into the
                                      library, or into FILE)

Saved games (backed up before the manager changes them; not mods):
  saves TARGET                        list the saved-game slots

Other:
  play TARGET [--print]               start the game (--print: only show the command)
  hack uw1|uw2 IN.EXE OUT.EXE         apply UltimaHacks to the GOG executable
  recover                             undo a change that a crash interrupted

TARGET is a target's id or name; MOD is a library key (id@version), or a .uwmod file.";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { return Run(args.ToList()); }
        catch (Exception e) when (e is ModException || e is InvalidOperationException || e is IOException || e is KeyNotFoundException || e is InvalidDataException || e is ArgumentException)
        {
            Console.Error.WriteLine("uwmod: " + e.Message);
            return 1;
        }
    }

    static int Run(List<string> a)
    {
        string? home = Take(a, "--home");
        if (a.Count == 0 || a[0] is "-h" or "--help" or "help") { Console.WriteLine(Help); return 0; }
        string cmd = a[0]; a.RemoveAt(0);
        if (cmd == "hack") return HackCmd(a);
        var m = new Manager(home);
        string? recovered = m.Recover();
        if (recovered != null) Console.WriteLine(recovered);
        switch (cmd)
        {
            case "targets":
                foreach (var t in m.State.Targets)
                    Console.WriteLine($"{t.Id}  {t.Name}  [{t.Kind}, {t.Game}]  {t.Folder}  mod: {t.ActiveMod ?? "(original)"}{(t.UltimaHacks ? " + UltimaHacks" : "")}");
                return 0;
            case "add-dos":
            {
                string? name = Take(a, "--name");
                bool asIs = Flag(a, "--as-is");
                var t = m.AddDos(Arg(a, 0, "FOLDER"), name, asIs);
                if (t.Written.Count > 0) Console.WriteLine($"The folder was taken over before and has {t.ActiveMod} in place; its originals are in the vault.");
                Console.WriteLine($"Added {t.Name} ({t.Id}): {t.Baseline.Count} files kept in the vault. " + (m.HasGogExe(t) ? "The executable is the GOG release's." : "The executable is not the GOG release's (UltimaHacks and Exhumed need it)."));
                foreach (var c in DosGame.CdImageConfs(t.Folder))
                    Console.WriteLine($"Note: {c} runs the game from GOG's CD image (game.gog), which never sees files changed in the folder. Extract the image into the folder and point that setup at it.");
                return 0;
            }
            case "add-exhumed":
            {
                string? data = Take(a, "--data"), name = Take(a, "--name");
                var t = m.AddExhumed(Arg(a, 0, "PORT"), data, name);
                Console.WriteLine($"Added {t.Name} ({t.Id}), game folder {t.Folder}.");
                Console.WriteLine(t.Features == null ? "This build reports no mod features (--features), so only mods that change data files play in it." :
                    $"Features: start square {(t.Features.StartSquare ? "yes" : "no")}, executable data patches {(t.Features.ExePatch ? "yes" : "no")}.");
                return 0;
            }
            case "remove": m.Remove(m.Get(Arg(a, 0, "TARGET"))); return 0;
            case "status":
            {
                var t = m.Get(Arg(a, 0, "TARGET"));
                Console.WriteLine($"{t.Name} [{t.Kind}, {t.Game}] {t.Folder}");
                Console.WriteLine("Mod: " + (t.ActiveMod ?? "(original game)") + (t.UltimaHacks ? ", with UltimaHacks" : ""));
                if (t.Kind == TargetKind.Dos)
                {
                    Console.WriteLine("Executable: " + (m.HasGogExe(t) ? "the GOG release's" : "not the GOG release's"));
                    var local = DosGame.LocalChanges(t);
                    Console.WriteLine(local.Count == 0 ? "No changes made outside the manager." : "Changed outside the manager: " + string.Join(", ", local));
                    foreach (var o in t.Overwritten)
                        Console.WriteLine($"Overwritten when told to, {o.When:yyyy-MM-dd HH:mm}: {o.File}, kept in the vault as {o.Sha}");
                }
                else Console.WriteLine("Home: " + ExhumedGame.HomeFor(m.Paths, t, t.ActiveMod));
                return 0;
            }
            case "library":
                foreach (var e in m.Library.List())
                    Console.WriteLine($"{e.Key,-36} {e.Game}  {e.Name}");
                return 0;
            case "import":
            {
                var e = m.Library.Import(Arg(a, 0, "FILE"));
                Console.WriteLine($"Imported {e.Name} {e.Version} as {e.Key}.");
                return 0;
            }
            case "info":
            {
                string s = Arg(a, 0, "FILE|MOD");
                var pkg = File.Exists(s) ? ModPackage.Load(s) : m.Library.Load(s);
                Console.WriteLine($"{pkg.Name} {pkg.Version} ({pkg.Id}) for {pkg.GameName}");
                if (pkg.Authors.Count > 0) Console.WriteLine("By " + string.Join(", ", pkg.Authors));
                if (pkg.Description.Length > 0) Console.WriteLine(pkg.Description);
                if (pkg.MadeWith.Length > 0) Console.WriteLine("Made with " + pkg.MadeWith);
                foreach (var c in ModNotes.Changes(pkg)) Console.WriteLine("  " + c);
                foreach (var w in ModNotes.Warnings(pkg)) Console.WriteLine("! " + w);
                return 0;
            }
            case "use":
            {
                bool uh = Flag(a, "--ultimahacks"), overwrite = Flag(a, "--overwrite"), dry = Flag(a, "--dry-run"), original = Flag(a, "--original");
                var t = m.Get(Arg(a, 0, "TARGET"));
                string? key = original ? null : ModKey(m, Arg(a, 1, "MOD"));
                if (key != null)
                {
                    var mf = m.Library.Find(key)!.Package;
                    var problems = m.Problems(t, mf);
                    if (problems.Count > 0) { Console.Error.WriteLine("uwmod: the mod cannot be used here: " + string.Join("; ", problems)); return 1; }
                    foreach (var n in m.Notes(t, mf)) Console.WriteLine("Note: " + n);
                }
                if (t.Kind == TargetKind.Exhumed)
                {
                    if (uh) Console.WriteLine("Note: UltimaHacks is for the DOS game; Exhumed has its own enhancements.");
                    if (dry) { Console.WriteLine("Would prepare the home " + ExhumedGame.HomeFor(m.Paths, t, key)); return 0; }
                    string h = m.UseExhumed(t, key);
                    Console.WriteLine($"{t.Name}: {key ?? "the original game"}, home {h}.");
                    return 0;
                }
                var plan = m.PlanDos(t, key, uh);
                foreach (var kv in plan.Writes) Console.WriteLine((kv.Value == null ? "  remove " : "  write  ") + kv.Key + (plan.OutsideChanges.Contains(kv.Key) ? "   (changed outside the manager)" : ""));
                foreach (var w in plan.Warnings) Console.WriteLine("! " + w);
                if (dry) return 0;
                m.UseDos(t, key, uh, overwrite);
                Console.WriteLine($"{t.Name}: {key ?? "the original game"}{(uh ? " with UltimaHacks" : "")} ({plan.Writes.Count} files written).");
                return 0;
            }
            case "capture":
            {
                var t = m.Get(Arg(a, 0, "TARGET"));
                string? output = Take(a, "-o");
                var d = Details(a);
                var (pkg, diff) = m.Capture(t, d);
                if (pkg == null) { Console.Error.WriteLine(diff.Changed.Count == 0 && diff.Problems.Count == 0 ? "uwmod: nothing has changed" : "uwmod: " + string.Join("; ", diff.Problems)); return 1; }
                if (output != null) pkg.Write(output); else m.Library.Add(pkg);
                Console.WriteLine($"Captured {pkg.Files.Count} changed file(s) as {Library.KeyOf(pkg)}" + (output != null ? " in " + output : " into the library") + ".");
                return 0;
            }
            case "saves":
            {
                var t = m.Get(Arg(a, 0, "TARGET"));
                foreach (var s in Saves.List(m.SavesFolder(t))) Console.WriteLine(s);
                return 0;
            }
            case "play":
            {
                var t = m.Get(Arg(a, 0, "TARGET"));
                var psi = m.LaunchInfo(t);
                Console.WriteLine(Launcher.CommandLine(psi));
                if (!Flag(a, "--print")) System.Diagnostics.Process.Start(psi);
                return 0;
            }
            case "recover":
                Console.WriteLine(recovered ?? "Nothing to recover.");
                return 0;
            default:
                Console.Error.WriteLine("uwmod: unknown command " + cmd + " (see uwmod help)");
                return 2;
        }
    }

    static int HackCmd(List<string> a)
    {
        var game = Arg(a, 0, "uw1|uw2") == "uw2" ? GameKind.UW2 : GameKind.UW1;
        byte[] exe = File.ReadAllBytes(Arg(a, 1, "IN.EXE"));
        File.WriteAllBytes(Arg(a, 2, "OUT.EXE"), Hacks.BuildExe(exe, game, null));
        Console.WriteLine(Hacks.Credit);
        return 0;
    }

    static ModDetails Details(List<string> a) => new()
    {
        Id = Take(a, "--id") ?? throw new ArgumentException("--id is required"),
        Name = Take(a, "--name") ?? throw new ArgumentException("--name is required"),
        Version = Take(a, "--version") ?? "1.0.0",
        Authors = (Take(a, "--author") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Description = Take(a, "--description") ?? "",
    };

    /// <summary>A library key, importing a .uwmod file first when a path is given.</summary>
    static string ModKey(Manager m, string s)
    {
        if (File.Exists(s)) return m.Library.Import(s).Key;
        if (m.Library.Find(s) != null) return s;
        var byId = m.Library.List().Where(e => e.Package.Id == s).ToList();
        if (byId.Count == 1) return byId[0].Key;
        throw new KeyNotFoundException("the library has no mod " + s + " (see uwmod library)");
    }

    static string? Take(List<string> a, string name)
    {
        int i = a.IndexOf(name);
        if (i < 0 || i + 1 >= a.Count) return null;
        string v = a[i + 1];
        a.RemoveRange(i, 2);
        return v;
    }

    static bool Flag(List<string> a, string name) => a.Remove(name);

    static string Arg(List<string> a, int i, string what) => i < a.Count ? a[i] : throw new ArgumentException(what + " is required (see uwmod help)");
}
