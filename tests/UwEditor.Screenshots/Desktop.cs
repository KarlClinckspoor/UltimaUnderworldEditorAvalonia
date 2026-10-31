using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using UwEditor.TestSupport;
using Xunit;

namespace UwEditor.Screenshots
{
    /// <summary>
    /// A private virtual screen for the tests: Xvfb with GLX on a free display, shared by the
    /// tests of one run. Tools needed: Xvfb, xdotool, ImageMagick (import, convert). The editor
    /// runs on Mesa's software OpenGL there, so pictures are the same from machine to machine
    /// up to small differences.
    /// </summary>
    public sealed class Desktop : IDisposable
    {
        public string Display { get; private set; }
        public string Missing { get; private set; }
        Process xvfb;
        public const int ScreenWidth = 1600, ScreenHeight = 1000;

        public Desktop()
        {
            if (!OperatingSystem.IsLinux()) { Missing = "screenshot tests run on Linux only"; return; }
            foreach (var tool in new[] { "Xvfb", "xdotool", "import", "convert" })
                if (Which(tool) == null) { Missing = tool + " is not installed (Xvfb, xdotool and ImageMagick are needed)"; return; }
            for (int n = 90; n < 110; n++)
            {
                if (File.Exists("/tmp/.X" + n + "-lock")) continue;
                Display = ":" + n;
                xvfb = Process.Start(new ProcessStartInfo("Xvfb", Display + " -screen 0 " + ScreenWidth + "x" + ScreenHeight + "x24 +extension GLX -nolisten tcp")
                { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                for (int i = 0; i < 50 && !File.Exists("/tmp/.X" + n + "-lock"); i++) Thread.Sleep(100);
                if (!xvfb.HasExited) return;
            }
            Missing = "could not start Xvfb";
        }

        public void Dispose()
        {
            try { if (xvfb != null && !xvfb.HasExited) xvfb.Kill(); } catch (Exception) { }
        }

        static string Which(string tool)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
                if (File.Exists(Path.Combine(dir, tool))) return Path.Combine(dir, tool);
            return null;
        }

        /// <summary>Runs a tool on the virtual screen and returns its output.</summary>
        public string Run(string tool, string args, byte[] stdoutBytes = null)
        {
            var psi = new ProcessStartInfo(tool, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment["DISPLAY"] = Display;
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                return o;
            }
        }

        public byte[] RunBytes(string tool, string args)
        {
            var psi = new ProcessStartInfo(tool, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment["DISPLAY"] = Display;
            using (var p = Process.Start(psi))
            using (var ms = new MemoryStream())
            {
                p.StandardOutput.BaseStream.CopyTo(ms);
                p.WaitForExit(30000);
                return ms.ToArray();
            }
        }
    }

    /// <summary>PNG files through ImageMagick.</summary>
    public sealed class MagickCodec : IPngCodec
    {
        readonly Desktop desktop;
        public MagickCodec(Desktop d) { desktop = d; }

        public RawImage Load(string path)
        {
            var wh = desktop.Run("identify", "-format \"%w %h\" \"" + path + "\"").Trim().Split(' ');
            int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
            var rgba = desktop.RunBytes("convert", "\"" + path + "\" -alpha on -depth 8 rgba:-");
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
            return new RawImage(w, h, rgba);
        }

        public void Save(RawImage img, string path)
        {
            string raw = Path.GetTempFileName();
            File.WriteAllBytes(raw, img.Rgba);
            desktop.Run("convert", "-size " + img.Width + "x" + img.Height + " -depth 8 rgba:\"" + raw + "\" \"" + path + "\"");
            File.Delete(raw);
        }
    }

    /// <summary>What the editor reports through --state-file.</summary>
    public sealed class EditorState
    {
        public int level { get; set; }
        public int selectedObject { get; set; }
        public int tileX { get; set; }
        public int tileY { get; set; }
        public float[] target { get; set; }
        public float yaw { get; set; }
        public float pitch { get; set; }
        public float distance { get; set; }
        public bool grid { get; set; }
        public bool ceiling { get; set; }
        public double[] view { get; set; }
        public string glError { get; set; }
    }

    /// <summary>
    /// One run of the editor on the virtual screen: started with a folder and options, waited
    /// for until it has drawn its first level, then clicked, keyed and captured.
    /// </summary>
    public sealed class Editor : IDisposable
    {
        public const int Width = 1280, Height = 800;
        readonly Desktop desktop;
        readonly Process process;
        readonly string work;
        public string WindowId { get; private set; }
        string StateFile { get { return Path.Combine(work, "state.json"); } }

        public Editor(Desktop d, string folder, params string[] options)
        {
            desktop = d;
            work = Path.Combine(Path.GetTempPath(), "uweditor-shot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string ready = Path.Combine(work, "ready");
            var args = new List<string> { "\"" + AppDll + "\"", "\"" + folder + "\"", "--size", Width + "x" + Height, "--ready-file", "\"" + ready + "\"", "--state-file", "\"" + StateFile + "\"" };
            args.AddRange(options);
            var psi = new ProcessStartInfo("dotnet", string.Join(" ", args)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment["DISPLAY"] = d.Display;
            psi.Environment["LIBGL_ALWAYS_SOFTWARE"] = "1";
            psi.Environment["UWEDITOR_SETTINGS_DIR"] = work;
            psi.Environment["AVALONIA_TELEMETRY_OPTOUT"] = "1";
            process = Process.Start(psi);
            process.OutputDataReceived += (s, e) => { };
            process.ErrorDataReceived += (s, e) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var sw = Stopwatch.StartNew();
            while (!File.Exists(ready))
            {
                if (process.HasExited) throw new InvalidOperationException("the editor exited with code " + process.ExitCode);
                if (sw.Elapsed.TotalSeconds > 60) throw new TimeoutException("the editor did not draw its level within 60 s");
                Thread.Sleep(100);
            }
            WindowId = d.Run("xdotool", "search --onlyvisible --name \"Underworld Editor\"").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
            if (WindowId == null) throw new InvalidOperationException("the editor's window was not found");
            Settle();
        }

        /// <summary>The editor's program: UwEditorFree.dll in the App project's output (Debug or Release, the newest).</summary>
        static string AppDll
        {
            get
            {
                string bin = Path.Combine(Snapshots.RepoRoot, "src", "UwEditor.App", "bin");
                var found = Directory.Exists(bin) ? Directory.GetFiles(bin, "UwEditorFree.dll", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "publish" + Path.DirectorySeparatorChar))
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                return found ?? throw new FileNotFoundException("Build src/UwEditor.App first (dotnet build).");
            }
        }

        public EditorState State
        {
            get
            {
                for (int i = 0; i < 20; i++)
                {
                    try { return JsonSerializer.Deserialize<EditorState>(File.ReadAllText(StateFile)); }
                    catch (IOException) { Thread.Sleep(50); }
                    catch (JsonException) { Thread.Sleep(50); }
                }
                return null;
            }
        }

        /// <summary>The window as drawn.</summary>
        public RawImage Capture()
        {
            string png = Path.Combine(work, "capture.png");
            desktop.Run("import", "-window " + WindowId + " \"" + png + "\"");
            return new MagickCodec(desktop).Load(png);
        }

        /// <summary>The 3D view's part of a capture.</summary>
        public RawImage ViewPart(RawImage window)
        {
            var v = State.view;
            return window.Crop((int)v[0], (int)v[1], (int)v[2], (int)v[3]);
        }

        /// <summary>Clicks at a point of the window (pixels from its top left).</summary>
        public void Click(int x, int y, int button = 1)
        {
            desktop.Run("xdotool", string.Format("mousemove --window {0} {1} {2} click {3}", WindowId, x, y, button));
            Settle();
        }

        /// <summary>Clicks at a point of the 3D view.</summary>
        public void ClickView(double x, double y)
        {
            var v = State.view;
            Click((int)(v[0] + x), (int)(v[1] + y));
        }

        /// <summary>Presses keys (xdotool names: "t", "Next", "ctrl+shift+f"). The 3D view has the focus after a click in it.</summary>
        public void Key(string keys)
        {
            desktop.Run("xdotool", "key " + keys);
            Settle();
        }

        /// <summary>Waits until two captures in a row are the same (the view has drawn what changed).</summary>
        public void Settle(int maxMs = 4000)
        {
            Thread.Sleep(150);
            byte[] last = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < maxMs)
            {
                var now = Capture().Rgba;
                if (last != null && now.AsSpan().SequenceEqual(last)) return;
                last = now;
                Thread.Sleep(120);
            }
        }

        public void Dispose()
        {
            try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch (Exception) { }
            try { Directory.Delete(work, true); } catch (Exception) { }
        }
    }

    /// <summary>The virtual screen, once for all the tests of the run.</summary>
    [CollectionDefinition("desktop")]
    public sealed class DesktopCollection : ICollectionFixture<Desktop> { }
}
