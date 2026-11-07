using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UwEditor.TestSupport;

namespace UwEditor.App.Tests
{
    /// <summary>Helpers for driving the headless window: open, click a control, type, press keys, capture.</summary>
    public static class Ui
    {
        public const int Width = 1440, Height = 900;

        /// <summary>A shown main window with the given game folder open (null: none).</summary>
        public static MainWindow Open(string folder)
        {
            // every window starts from the default settings (a test's toggles are saved on close)
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SETTINGS_DIR");
            if (!string.IsNullOrEmpty(dir)) File.Delete(Path.Combine(dir, "settings.json"));
            var w = new MainWindow { Width = Width, Height = Height, OfferRecovery = false };
            w.Show();
            Pump();
            if (folder != null) w.OpenPath(folder);
            Pump();
            return w;
        }

        /// <summary>Runs queued layout, bindings and posted work.</summary>
        public static void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public static T Find<T>(Window w, string name) where T : Control
        {
            var c = w.FindControl<T>(name) ?? w.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name);
            if (c == null) throw new InvalidOperationException("No control named " + name);
            return c;
        }

        /// <summary>A left click in the middle of a control, as the mouse would.</summary>
        public static void Click(Window w, Control c, MouseButton button = MouseButton.Left)
        {
            Pump();
            var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) ?? throw new InvalidOperationException("control not in the window");
            w.MouseMove(p);
            w.MouseDown(p, button);
            w.MouseUp(p, button);
            Pump();
        }

        /// <summary>Clicks at a point of a control (pixels from its top left).</summary>
        public static void ClickAt(Window w, Control c, double x, double y)
        {
            Pump();
            var p = c.TranslatePoint(new Point(x, y), w) ?? throw new InvalidOperationException("control not in the window");
            w.MouseMove(p);
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Pump();
        }

        public static void Type(Window w, Control target, string text)
        {
            target.Focus();
            Pump();
            w.KeyTextInput(text);
            Pump();
        }

        public static void Key(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None)
        {
            w.KeyPressQwerty(ToPhysical(key), mods);
            w.KeyReleaseQwerty(ToPhysical(key), mods);
            Pump();
        }

        static PhysicalKey ToPhysical(Key k)
        {
            switch (k)
            {
                case Avalonia.Input.Key.PageDown: return PhysicalKey.PageDown;
                case Avalonia.Input.Key.PageUp: return PhysicalKey.PageUp;
                case Avalonia.Input.Key.Escape: return PhysicalKey.Escape;
                case Avalonia.Input.Key.Home: return PhysicalKey.Home;
                case Avalonia.Input.Key.Enter: return PhysicalKey.Enter;
                case Avalonia.Input.Key.F2: return PhysicalKey.F2;
                case Avalonia.Input.Key.F12: return PhysicalKey.F12;
                case Avalonia.Input.Key.Tab: return PhysicalKey.Tab;
                default:
                    if (k >= Avalonia.Input.Key.A && k <= Avalonia.Input.Key.Z)
                        return (PhysicalKey)Enum.Parse(typeof(PhysicalKey), k.ToString());
                    throw new ArgumentException("no physical key for " + k);
            }
        }

        /// <summary>The window as drawn, in RGBA.</summary>
        public static RawImage Capture(Window w, Control focus = null)
        {
            // no text box keeps the focus, so no blinking caret ends up in the picture
            (focus ?? (Control)w).Focus();
            Pump();
            var frame = w.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
            return ToRaw(frame);
        }

        public static RawImage ToRaw(Bitmap bmp)
        {
            int width = bmp.PixelSize.Width, height = bmp.PixelSize.Height;
            // CopyPixels gives the bitmap's own format: BGRA or RGBA, premultiplied or not
            int stride = width * 4;
            var data = new byte[stride * height];
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { bmp.CopyPixels(new PixelRect(0, 0, width, height), h.AddrOfPinnedObject(), data.Length, stride); }
            finally { h.Free(); }
            bool bgra = bmp.Format.HasValue && bmp.Format.Value == PixelFormats.Bgra8888;
            for (int i = 0; i < data.Length; i += 4)
            {
                if (bgra) { byte t = data[i]; data[i] = data[i + 2]; data[i + 2] = t; }
                data[i + 3] = 255;   // compare colours only
            }
            return new RawImage(width, height, data);
        }

        /// <summary>PNG files through Avalonia's bitmaps.</summary>
        public sealed class Codec : IPngCodec
        {
            public RawImage Load(string path) { using (var b = new Bitmap(path)) return ToRaw(b); }

            public void Save(RawImage img, string path)
            {
                var wb = new WriteableBitmap(new PixelSize(img.Width, img.Height), new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
                using (var fb = wb.Lock())
                    for (int y = 0; y < img.Height; y++) Marshal.Copy(img.Rgba, y * img.Width * 4, fb.Address + y * fb.RowBytes, img.Width * 4);
                wb.Save(path);
            }
        }

        public static string BaselineDir { get { return Path.Combine(Snapshots.RepoRoot, "tests", "UwEditor.App.Tests", "baselines"); } }
    }
}
