using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UwModManager.App.ViewModels;
using UwModManager.App.Views;
using UwModManager.Core;
using UwEditor.Look;

namespace UwModManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var manager = new Manager();
            // the editor's colours and layout, as chosen in Settings (Light and Compact at first)
            ColourScheme.Set(manager.State.Settings.Theme);
            Density.Set(manager.State.Settings.Layout);
            var window = new MainWindow();
            var vm = new MainViewModel(manager, new Dialogs(window));
            window.DataContext = vm;
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
