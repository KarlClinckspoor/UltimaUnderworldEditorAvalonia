using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UwModManager.Core;

namespace UwModManager.App.ViewModels;

public sealed partial class LibraryViewModel : ViewModelBase
{
    readonly MainViewModel main;
    public ObservableCollection<LibraryEntry> Entries { get; } = new();
    [ObservableProperty] private LibraryEntry? _selected;

    public LibraryViewModel(MainViewModel main) { this.main = main; Refresh(); }

    public void Refresh()
    {
        var key = Selected?.Key;
        Entries.Clear();
        foreach (var e in main.Manager.Library.List()) Entries.Add(e);
        Selected = Entries.FirstOrDefault(e => e.Key == key) ?? Entries.FirstOrDefault();
    }

    public bool HasSelection => Selected != null;
    public string Title => Selected == null ? "" : Selected.Name + "  " + Selected.Version;
    public string Line => Selected == null ? "" : "For " + Selected.GameName + (Selected.Package.Authors.Count > 0 ? " · by " + string.Join(", ", Selected.Package.Authors) : "");
    public string Description => Selected?.Package.Description ?? "";
    public string Origin => Selected == null ? "" : string.Join(" · ", new[] {
        Selected.Key,
        Selected.Package.MadeWith,
        $"{Selected.Size / 1024.0:0.#} KB" }.Where(p => !string.IsNullOrEmpty(p)));
    public IEnumerable<string> Changes => Selected == null ? Enumerable.Empty<string>() : ModNotes.Changes(Selected.Package);
    public IEnumerable<string> Warnings => Selected == null ? Enumerable.Empty<string>() : ModNotes.Warnings(Selected.Package);

    partial void OnSelectedChanged(LibraryEntry? value)
    {
        foreach (var p in new[] { nameof(HasSelection), nameof(Title), nameof(Line), nameof(Description), nameof(Origin), nameof(Changes), nameof(Warnings) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    async Task Import()
    {
        string? path = await main.Dialogs.PickFile("A mod package", "Mod packages", "*.uwmod");
        if (path == null) return;
        await main.Guard(async () =>
        {
            var e = main.Manager.Library.Import(path);
            Refresh();
            Selected = Entries.FirstOrDefault(x => x.Key == e.Key);
            main.Status = "Imported " + e.Name + " " + e.Version + ".";
            await Task.CompletedTask;
        });
    }

    [RelayCommand]
    async Task Remove()
    {
        if (Selected == null) return;
        var users = main.Manager.State.Targets.Where(t => t.ActiveMod == Selected.Key).ToList();
        if (users.Count > 0) { await main.Dialogs.Tell("In use", "Switch " + string.Join(", ", users.Select(u => u.Name)) + " to another mod or the original game first."); return; }
        if (await main.Dialogs.Ask("Remove " + Selected.Name, "Remove the package from the library?", "Remove", "Cancel") != 0) return;
        main.Manager.Library.Remove(Selected.Key);
        Refresh();
    }
}
