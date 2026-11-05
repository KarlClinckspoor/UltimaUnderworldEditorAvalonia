using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UwModManager.Core;

namespace UwModManager.App.ViewModels;

/// <summary>A choice in a game's mod list: a library mod, or the original game.</summary>
public sealed class ModChoice
{
    public string? Key { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Problems { get; init; } = new();
    public List<string> Notes { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> Changes { get; init; } = new();
    public bool Usable => Problems.Count == 0;
    public bool InUse { get; init; }
    public string Badge => InUse ? "in use" : Usable ? "" : "not usable here";
}

public sealed partial class EnhancementChoice : ObservableObject
{
    public required string Name { get; init; }
    [ObservableProperty] private bool _on;
}

public sealed class SlotItem
{
    public required SaveSlot Slot { get; init; }
    public string Title => "SAVE" + Slot.Number;
    public string Text => Slot.Exists ? (Slot.Description.Length > 0 ? Slot.Description : "(no description)") + "   " + Slot.Written.ToString("yyyy-MM-dd HH:mm") : "empty";
    public bool Exists => Slot.Exists;
}

/// <summary>A game: its mod (one at a time), UltimaHacks or Exhumed's enhancements, its saves, and Play.</summary>
public sealed partial class TargetViewModel : ViewModelBase
{
    readonly MainViewModel main;
    public Target Target { get; }
    Manager M => main.Manager;

    public ObservableCollection<ModChoice> Choices { get; } = new();
    public ObservableCollection<EnhancementChoice> Enhancements { get; } = new();
    public ObservableCollection<SlotItem> Slots { get; } = new();
    public ObservableCollection<string> StatusLines { get; } = new();

    [ObservableProperty] private ModChoice? _selected;
    [ObservableProperty] private bool _ultimaHacks;
    [ObservableProperty] private string _localChanges = "";
    [ObservableProperty] private bool _busy;

    public bool IsDos => Target.Kind == TargetKind.Dos;
    public bool IsExhumed => Target.Kind == TargetKind.Exhumed;
    public string Header => Target.Name;
    public string Kind => (IsDos ? "DOS game, changed in place" : "Underworld Exhumed, a home folder per mod") + " · " + (Target.Game == "uw2" ? "Ultima Underworld II" : "Ultima Underworld");
    public string Folder => Target.Folder + (IsExhumed ? "\n" + Target.PortExe : "");
    public bool HasLocalChanges => LocalChanges.Length > 0;
    public string UltimaHacksCredit => Hacks.Credit;

    // ---- how the game is started: more arguments and a settings file of the player's

    [ObservableProperty] private string _extraArguments;
    [ObservableProperty] private string _extraConfig;
    public string ArgumentsHint => IsDos ? "More DOSBox options, e.g. -fullscreen" : "More options for the port, e.g. --scale 4 --mouse lock --no-aspect";
    public string ConfigHint => IsDos ? "A DOSBox .conf of your own, read last" : "A settings file of your own (key=value lines)";
    public string StartOptionsNote => IsDos
        ? "DOSBox gets the setups above, then your settings file (a .conf: DOSBox reads it last, so its settings win, e.g. [sdl] fullscreen=false), then these arguments."
        : "Your settings file's key=value lines (as in uw1port.cfg, e.g. scale=4) are copied into the home's settings file at each start; start-square and exe-patch stay the mod's. The arguments come after the manager's.";

    partial void OnExtraArgumentsChanged(string value) { Target.ExtraArguments = value.Trim(); M.Save(); }
    partial void OnExtraConfigChanged(string value) { Target.ExtraConfig = value.Trim().Trim('"'); M.Save(); }

    [RelayCommand]
    async Task BrowseExtraConfig()
    {
        string? p = IsDos ? await main.Dialogs.PickFile("A DOSBox settings file", "DOSBox settings", "*.conf")
                          : await main.Dialogs.PickFile("An Exhumed settings file", "Settings", "*.cfg", "*.txt");
        if (p != null) ExtraConfig = p;
    }

    public TargetViewModel(MainViewModel main, Target target)
    {
        this.main = main;
        Target = target;
        _ultimaHacks = target.UltimaHacks;
        _extraArguments = target.ExtraArguments ?? "";
        _extraConfig = target.ExtraConfig ?? "";
        Refresh();
    }

    public void Refresh()
    {
        Choices.Clear();
        Choices.Add(new ModChoice { Key = null, Title = "The original game", Detail = "no mod", InUse = Target.ActiveMod == null,
            Description = IsDos ? "Every file as it was when the manager took the folder over." : "The game as Exhumed plays it, in a home folder of its own." });
        foreach (var e in M.Library.List().Where(e => e.Game == Target.Game))
        {
            var mf = e.Package;
            Choices.Add(new ModChoice
            {
                Key = e.Key, Title = mf.Name + "  " + mf.Version,
                Detail = "Mod" + (mf.Authors.Count > 0 ? " · by " + string.Join(", ", mf.Authors) : ""),
                Description = mf.Description, InUse = Target.ActiveMod == e.Key,
                Problems = M.Problems(Target, mf), Notes = M.Notes(Target, mf),
                Warnings = new List<string>(), Changes = ModNotes.Changes(mf),
            });
        }
        Selected = Choices.FirstOrDefault(c => c.InUse) ?? Choices[0];

        // the home's own enhancements (its settings file, which the game's settings menu changes too)
        Enhancements.Clear();
        var on = IsExhumed ? M.Enhancements(Target) : new List<string>();
        if (IsExhumed && Target.Features != null)
            foreach (var n in Target.Features.Enhancements)
            {
                var ec = new EnhancementChoice { Name = n, On = on.Contains(n) };
                ec.PropertyChanged += (_, _) => SaveEnhancements();
                Enhancements.Add(ec);
            }

        StatusLines.Clear();
        if (IsDos)
        {
            StatusLines.Add(M.HasGogExe(Target) ? "Executable: the GOG release's." : "Executable: not the GOG release's (UltimaHacks needs it).");
            var local = DosGame.LocalChanges(Target);
            LocalChanges = local.Count == 0 ? "" : "Changed outside the manager: " + string.Join(", ", local.Take(8)) + (local.Count > 8 ? $" and {local.Count - 8} more" : "") +
                ". Capture them as a mod before switching, or the manager will ask about them when you switch.";
            var gog = Launcher.GogConfs(Target.Folder);
            StatusLines.Add(gog.Count > 0 ? "DOSBox setup: GOG's (" + string.Join(", ", gog.Select(Path.GetFileName)) + ")."
                : "DOSBox setup: the manager's, with GOG's settings for the game (written at each start to " + Launcher.ManagerConf(M.Paths, Target) + ").");
            foreach (var c in DosGame.CdImageConfs(Target.Folder)) StatusLines.Add($"{c} runs the game from GOG's CD image (game.gog), which never sees the folder's files.");
        }
        else
        {
            var missing = Target.PortExe == null ? new List<string>() : Launcher.MissingDlls(Target.PortExe);
            if (missing.Count > 0) StatusLines.Add("This program cannot start: it needs " + Launcher.WhichAre(missing) + " not beside it. Play offers to copy the DLLs from a release.");
            StatusLines.Add(Target.Features == null ? "This Exhumed build reports no mod features, so only mods that change data files play in it."
                : $"Start square: {(Target.Features.StartSquare ? "yes" : "no")} · executable data patches: {(Target.Features.ExePatch ? "yes" : "no")}");
            StatusLines.Add("Home: " + ExhumedGame.HomeFor(M.Paths, Target, Target.ActiveMod));
            if (Target.Features == null) StatusLines.Add("Enhancements (set in the game's settings): " + (on.Count > 0 ? string.Join(", ", on) : "none") + ".");
        }
        OnPropertyChanged(nameof(HasLocalChanges));

        Slots.Clear();
        foreach (var s in Saves.List(M.SavesFolder(Target))) Slots.Add(new SlotItem { Slot = s });
    }

    /// <summary>A box ticked: the home's settings file changes now (names the port knows but this build does not list are kept).</summary>
    void SaveEnhancements()
    {
        var listed = Enhancements.Select(e => e.Name).ToHashSet();
        var keep = M.Enhancements(Target).Where(n => !listed.Contains(n));
        M.SetEnhancements(Target, keep.Concat(Enhancements.Where(e => e.On).Select(e => e.Name)));
    }

    [RelayCommand]
    async Task Apply()
    {
        var choice = Selected;
        if (choice == null) return;
        if (!choice.Usable) { await main.Dialogs.Tell("This mod cannot be used here", string.Join("\n", choice.Problems)); return; }
        await main.Guard(async () =>
        {
            Busy = true;
            try
            {
                if (IsExhumed)
                {
                    string home = await Task.Run(() => M.UseExhumed(Target, choice.Key));
                    main.Status = $"{Target.Name}: {choice.Title} (home {home}).";
                }
                else
                {
                    var plan = await Task.Run(() => M.PlanDos(Target, choice.Key, UltimaHacks));
                    bool overwrite = false;
                    if (plan.OutsideChanges.Count > 0)
                    {
                        int a = await main.Dialogs.Ask("Files changed outside the manager",
                            "These files have changed since the manager last wrote them (perhaps the editor saved them):\n\n" + string.Join("\n", plan.OutsideChanges) +
                            "\n\nSwitching will overwrite them. Capture them as a mod first?", "Capture them first", "Overwrite them", "Cancel");
                        if (a == 0) { await Capture(); return; }
                        if (a != 1) return;
                        overwrite = true;
                    }
                    await Task.Run(() => M.UseDos(Target, choice.Key, UltimaHacks, overwrite));
                    main.Status = $"{Target.Name}: {choice.Title}{(UltimaHacks ? " with UltimaHacks" : "")}, {plan.Writes.Count} file(s) written.";
                    if (plan.Warnings.Count > 0) await main.Dialogs.Tell(choice.Title, string.Join("\n\n", plan.Warnings));
                }
            }
            finally { Busy = false; }
            main.UpdateSubtitles();
            Refresh();
        });
    }

    [RelayCommand]
    async Task Play()
    {
        await main.Guard(async () =>
        {
            if (IsDos && (Target.ActiveMod != (Selected?.Key) || Target.UltimaHacks != UltimaHacks))
            {
                int a = await main.Dialogs.Ask("Not applied yet", "Your choice is not applied yet: the folder still has " + (Target.ActiveMod ?? "the original game") + ". Apply it first?", "Apply, then play", "Play as it is", "Cancel");
                if (a == 2 || a < 0) return;
                if (a == 0) { await Apply(); }
            }
            if (IsExhumed && !await PortCanStart()) return;
            var psi = M.LaunchInfo(Target);
            var p = System.Diagnostics.Process.Start(psi);
            main.Status = "Started: " + Launcher.CommandLine(psi);
            Refresh();
            // a program that cannot start (a missing DLL, a bad setting) ends at once and without a word
            string? why = await Launcher.EarlyExit(p, TimeSpan.FromSeconds(4));
            if (why != null)
            {
                main.Status = "Stopped at once: " + Launcher.CommandLine(psi);
                await main.Dialogs.Tell("The game stopped", Path.GetFileName(psi.FileName) + " stopped as soon as it started: " + why + ".\n\nThe command was:\n" + Launcher.CommandLine(psi));
            }
            else if (IsExhumed && p != null) _ = RefreshWhenClosed(p);
        });
    }

    /// <summary>When the port closes, show what the game's settings menu changed (its enhancements) and the saves.</summary>
    async Task RefreshWhenClosed(System.Diagnostics.Process p)
    {
        try { await p.WaitForExitAsync(); }
        catch (InvalidOperationException) { return; }
        Refresh();
    }

    /// <summary>
    /// Whether the port's program has the DLLs it loads at start. A build made from the sources
    /// comes without them (SDL3.dll and the others are beside a release's program), and Windows
    /// then stops it before it runs, silently: offer to copy them from a release's folder.
    /// </summary>
    async Task<bool> PortCanStart()
    {
        if (Target.PortExe == null) return true;
        var missing = Launcher.MissingDlls(Target.PortExe);
        if (missing.Count == 0) return true;
        int a = await main.Dialogs.Ask("Exhumed cannot start",
            Path.GetFileName(Target.PortExe) + " needs " + Launcher.WhichAre(missing) + " not beside it (" + Path.GetDirectoryName(Target.PortExe) +
            "), in Windows' folders or on the PATH, so Windows would stop it at once. A build made from the sources comes without its DLLs; an Exhumed release has them beside its program.\n\n" +
            "Copy the DLLs from a release's folder?", "Copy them from a folder…", "Cancel");
        if (a != 0) return false;
        string? dir = await main.Dialogs.PickFolder("The folder of an Exhumed release (with SDL3.dll)");
        if (dir == null) return false;
        var copied = Launcher.CopyDlls(dir, Target.PortExe);
        missing = Launcher.MissingDlls(Target.PortExe);
        if (missing.Count > 0)
        {
            await main.Dialogs.Tell("Still missing", (copied.Count > 0 ? "Copied " + string.Join(", ", copied) + ", but " : "") + string.Join(", ", missing) + (missing.Count == 1 ? " is" : " are") + " still missing. Choose the folder of a release of the same game.");
            return false;
        }
        main.Status = "Copied " + string.Join(", ", copied) + " beside " + Target.PortExe + ".";
        if (Target.Features == null) { Target.Features = await Task.Run(() => ExhumedGame.Probe(Target.PortExe)); M.Save(); }
        Refresh();
        return true;
    }

    [RelayCommand]
    async Task Capture()
    {
        var d = await main.Dialogs.AskDetails("Keep this folder's changes as a mod", new ModDetails { Name = "My changes", Version = "1.0.0" });
        if (d == null) return;
        await main.Guard(async () =>
        {
            var (pkg, diff) = await Task.Run(() => M.Capture(Target, d));
            if (pkg == null)
            {
                await main.Dialogs.Tell("Nothing captured", diff.Changed.Count == 0 && diff.Problems.Count == 0 ? "Nothing has changed." : string.Join("\n", diff.Problems));
                return;
            }
            M.Library.Add(pkg);
            main.Status = $"Captured {pkg.Files.Count} changed file(s) as {pkg.Name} {pkg.Version}, now in the library.";
            Refresh();
        });
    }

    [RelayCommand]
    async Task RestoreSave(SlotItem? slot)
    {
        if (slot == null) return;
        var backups = Saves.Backups(M.Paths, M.SavesFolder(Target), slot.Slot.Number).Take(8).ToList();
        if (backups.Count == 0) { await main.Dialogs.Tell("No backups", "No backup of SAVE" + slot.Slot.Number + " yet: one is made whenever the manager replaces a slot."); return; }
        int which = await main.Dialogs.Ask("Restore SAVE" + slot.Slot.Number, "The game now in the slot is backed up first. Which backup do you want to restore?", backups.Select(Path.GetFileNameWithoutExtension).ToArray()!);
        if (which < 0) return;
        await main.Guard(async () =>
        {
            Saves.Restore(M.Paths, M.Vault, backups[which], slot.Slot);
            main.Status = "SAVE" + slot.Slot.Number + " restored.";
            Refresh();
            await Task.CompletedTask;
        });
    }

    [RelayCommand]
    async Task Remove()
    {
        bool modded = IsDos && Target.Written.Count > 0;
        int a = await main.Dialogs.Ask("Forget " + Target.Name, modded
            ? "The folder has a mod in place. The manager will put the original game back before it forgets the folder."
            : "The manager will forget this game. Its files stay as they are.",
            modded ? new[] { "Put it back, then forget", "Cancel" } : new[] { "Forget", "Cancel" });
        if (a != 0) return;
        bool restore = modded;
        await main.Guard(async () =>
        {
            if (restore) await Task.Run(() => M.UseDos(Target, null, false));
            M.Remove(Target);
            main.RefreshGames();
            main.Page = new WelcomeViewModel();
        });
    }

    [RelayCommand]
    void Reload() => Refresh();
}
