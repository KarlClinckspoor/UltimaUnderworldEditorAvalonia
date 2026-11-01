using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace UwEditor.App
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // an error in an event handler, a timer or a drawing pass: logged, the unsaved work kept, the editor goes on
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                e.Handled = true;
                var w = MainWindow.Current;
                if (w != null) w.ReportError(e.Exception, false);
                else UwEditor.Core.Editing.Recovery.WriteCrashLog(Settings.Folder, e.Exception, "No window open.");
            };
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
