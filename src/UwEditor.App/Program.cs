using System;
using Avalonia;

namespace UwEditor.App
{
    static class Program
    {
        /// <summary>The folder given on the command line, opened at start (the game folder, DATA or a SAVEn folder).</summary>
        public static string OpenAtStart;
        /// <summary>--level N: the level to show first instead of the start level.</summary>
        public static int LevelAtStart;

        [STAThread]
        public static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--level" && i + 1 < args.Length) int.TryParse(args[++i], out LevelAtStart);
                else if (a == "--trace") System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
                else if (!a.StartsWith("-")) OpenAtStart = a;
            }
            // Avalonia refuses Mesa's software renderer (llvmpipe) for GLX; without it a Linux
            // machine with no GPU driver (a VM) would get no 3D view at all, so accept it.
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("AVALONIA_GLX_IGNORE_RENDERER_BLACKLIST") == null)
                Environment.SetEnvironmentVariable("AVALONIA_GLX_IGNORE_RENDERER_BLACKLIST", "1");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace(Avalonia.Logging.LogEventLevel.Warning);
        }
    }
}
