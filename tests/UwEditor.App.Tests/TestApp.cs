using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using UwEditor.App;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(UwEditor.App.Tests.TestApp))]

namespace UwEditor.App.Tests
{
    /// <summary>The editor's application on Avalonia's headless platform, drawn with Skia so
    /// frames of the interface can be captured. Settings go to a temporary folder.</summary>
    public static class TestApp
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            string settings = Path.Combine(Path.GetTempPath(), "uweditor-tests-" + Environment.ProcessId);
            Directory.CreateDirectory(settings);
            Environment.SetEnvironmentVariable("UWEDITOR_SETTINGS_DIR", settings);
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseHarfBuzz()
                .WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        }
    }
}
