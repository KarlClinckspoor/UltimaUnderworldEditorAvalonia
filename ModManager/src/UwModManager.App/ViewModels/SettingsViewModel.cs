using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UwEditor.Look;
using UwModManager.Core;

namespace UwModManager.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    readonly MainViewModel main;
    [ObservableProperty] private string _dosBox;

    public SettingsViewModel(MainViewModel main)
    {
        this.main = main;
        _dosBox = main.Manager.State.Settings.DosBox;
    }

    // ---- appearance: the editor's colours and layout, remembered

    public bool LightTheme { get => main.Manager.State.Settings.Theme != ColourScheme.Dark; set { if (value) SetTheme(ColourScheme.Light); } }
    public bool DarkTheme { get => main.Manager.State.Settings.Theme == ColourScheme.Dark; set { if (value) SetTheme(ColourScheme.Dark); } }
    public bool CompactLayout { get => main.Manager.State.Settings.Layout != Density.Comfortable; set { if (value) SetLayout(Density.Compact); } }
    public bool ComfortableLayout { get => main.Manager.State.Settings.Layout == Density.Comfortable; set { if (value) SetLayout(Density.Comfortable); } }

    void SetTheme(string name)
    {
        if (main.Manager.State.Settings.Theme == name) return;
        main.Manager.State.Settings.Theme = name;
        main.Manager.Save();
        ColourScheme.Set(name);
        OnPropertyChanged(nameof(LightTheme)); OnPropertyChanged(nameof(DarkTheme));
    }

    void SetLayout(string name)
    {
        if (main.Manager.State.Settings.Layout == name) return;
        main.Manager.State.Settings.Layout = name;
        main.Manager.Save();
        Density.Set(name);
        OnPropertyChanged(nameof(CompactLayout)); OnPropertyChanged(nameof(ComfortableLayout));
    }

    public string Folder => main.Manager.Paths.Root;
    public string Version => "Ultima Underworlds Mod Manager " + AppVersion.Text(typeof(SettingsViewModel).Assembly);
    public string Credits => Hacks.Credit + "\n\nThe file formats and mod packages (format 2) come from the Ultima Underworld Editor (KarlClinckspoor/UltimaUnderworldEditorAvalonia). " +
        "The ports are Underworld Exhumed (abedegno/underworld-exhumed); a moved start square and executable patches need the fork's modding branch.";
    public string HackLicense => Hacks.License();

    partial void OnDosBoxChanged(string value)
    {
        main.Manager.State.Settings.DosBox = value.Trim();
        main.Manager.Save();
    }

    [RelayCommand]
    async Task Browse()
    {
        string? p = await main.Dialogs.PickFile("The DOSBox program", "Programs", OperatingSystem.IsWindows() ? "*.exe" : "*");
        if (p != null) DosBox = p;
    }
}
