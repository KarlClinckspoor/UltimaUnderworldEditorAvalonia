using CommunityToolkit.Mvvm.ComponentModel;

namespace UwModManager.App.ViewModels;

public abstract class ViewModelBase : ObservableObject { }

public sealed class WelcomeViewModel : ViewModelBase
{
    public string Text { get; } =
        "Add a game to begin. It can be a DOS game folder (GOG's, with UW.EXE or UW2.EXE and DATA), which the manager changes in place and can always put back, " +
        "or Underworld Exhumed's program (uw1port, uw2port), which gives each mod a home folder of its own.\n\n" +
        "Then import mods (.uwmod packages, made with the editor's Tools ▸ Mods) into the Library, and pick one for a game. A game uses one mod at a time.";
}
