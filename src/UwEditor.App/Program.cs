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
        /// <summary>--select N: the object to select and frame at start.</summary>
        public static int SelectAtStart = -1;
        /// <summary>--ready-file PATH: written once the first level has been drawn (for scripted screenshots).</summary>
        public static string ReadyFile;
        /// <summary>--state-file PATH: the level, selection, camera and view rectangle as JSON after
        /// every change (for scripted tests).</summary>
        public static string StateFile;
        /// <summary>--size WxH: the window's size.</summary>
        public static int Width, Height;
        /// <summary>--conversations [N]: open the conversation window at start (on slot N).</summary>
        public static int ConversationAtStart = int.MinValue;
        /// <summary>--globals: open the global variables window at start.</summary>
        public static bool GlobalsAtStart;
        /// <summary>--window keys|strings|triggers|mods|add: a tool window to open (screenshots).</summary>
        public static string WindowAtStart;
        /// <summary>--light n: the game's light level n (0-7) in the view.</summary>
        public static int LightAtStart = -1;
        /// <summary>--walk: start in first person.</summary>
        public static bool WalkAtStart;
        /// <summary>--item hex: the item the add window starts on.</summary>
        public static int ItemAtStart = -1;
        /// <summary>--player / --graphics: open those windows at start (--tab picks the tab).</summary>
        public static bool PlayerAtStart, GraphicsAtStart;
        /// <summary>--tab N: the conversation window's tab at start (0 dialogue, 1 strings, 2 script, 3 who talks).</summary>
        public static int TabAtStart;

        [STAThread]
        public static void Main(string[] args)
        {
            // an error on another thread ends the program: log it and keep the unsaved work first
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject));
                var w = MainWindow.Current;
                if (w != null) w.ReportError(ex, e.IsTerminating);
                else UwEditor.Core.Editing.Recovery.WriteCrashLog(Settings.Folder, ex, "No window open.");
            };
            // a task nobody waited for: logged only
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                UwEditor.Core.Editing.Recovery.WriteCrashLog(Settings.Folder, e.Exception, "A background task failed and nothing waited for it; the program went on.");
                e.SetObserved();
            };
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--level" && i + 1 < args.Length) int.TryParse(args[++i], out LevelAtStart);
                else if (a == "--select" && i + 1 < args.Length) int.TryParse(args[++i], out SelectAtStart);
                else if (a == "--ready-file" && i + 1 < args.Length) ReadyFile = args[++i];
                else if (a == "--state-file" && i + 1 < args.Length) StateFile = args[++i];
                else if (a == "--size" && i + 1 < args.Length)
                {
                    var wh = args[++i].Split('x');
                    if (wh.Length == 2) { int.TryParse(wh[0], out Width); int.TryParse(wh[1], out Height); }
                }
                else if (a == "--conversations")
                {
                    ConversationAtStart = -1;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int slot)) { ConversationAtStart = slot; i++; }
                }
                else if (a == "--globals") GlobalsAtStart = true;
                else if (a == "--window" && i + 1 < args.Length) WindowAtStart = args[++i];
                else if (a == "--light" && i + 1 < args.Length) int.TryParse(args[++i], out LightAtStart);
                else if (a == "--walk") WalkAtStart = true;
                else if (a == "--item" && i + 1 < args.Length) int.TryParse(args[++i].Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out ItemAtStart);
                else if (a == "--player") PlayerAtStart = true;
                else if (a == "--graphics") GraphicsAtStart = true;
                else if (a == "--tab" && i + 1 < args.Length) int.TryParse(args[++i], out TabAtStart);
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
