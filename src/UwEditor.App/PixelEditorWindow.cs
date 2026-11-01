using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.App
{
    /// <summary>The drawing area of the pixel editor: the picture enlarged, a checkerboard under transparent pixels, a grid when large enough.</summary>
    public sealed class PixelCanvas : Control
    {
        public PixelDocument Doc;
        public int Zoom = 8;
        public bool Grid = true;
        /// <summary>The pixel under the mouse, or (−1, −1).</summary>
        public int HoverX = -1, HoverY = -1, BrushWidth = 1;
        /// <summary>A shape being dragged (line or rectangle): its preview is drawn by painting a copy.</summary>
        public int[] Preview;
        Bitmap bitmap;

        public void Changed() { bitmap = null; InvalidateVisual(); InvalidateMeasure(); }

        protected override Size MeasureOverride(Size available)
        {
            return Doc == null ? new Size(0, 0) : new Size(Doc.Width * Zoom, Doc.Height * Zoom);
        }

        public bool PixelAt(Point p, out int x, out int y)
        {
            x = (int)Math.Floor(p.X / Zoom); y = (int)Math.Floor(p.Y / Zoom);
            return Doc != null && Doc.Inside(x, y);
        }

        public override void Render(DrawingContext ctx)
        {
            if (Doc == null) return;
            double w = Doc.Width * Zoom, h = Doc.Height * Zoom;
            // the checkerboard shows through transparent pixels
            var light = new SolidColorBrush(Color.Parse("#5A5A60")); var dark = new SolidColorBrush(Color.Parse("#3C3C42"));
            ctx.FillRectangle(dark, new Rect(0, 0, w, h));
            int cell = Math.Max(4, Zoom / 2);
            for (int y = 0; y * cell < h; y++)
                for (int x = (y & 1); x * cell < w; x += 2)
                    ctx.FillRectangle(light, new Rect(x * cell, y * cell, Math.Min(cell, w - x * cell), Math.Min(cell, h - y * cell)));
            if (bitmap == null)
            {
                var px = Preview != null ? new IndexedImage(Doc.Width, Doc.Height, Preview, Doc.Colors).ToRgba() : Doc.ToRgba();
                var img = new RgbaImage(Doc.Width, Doc.Height);
                Array.Copy(px, img.Pixels, px.Length);
                bitmap = MainViewModel.ToBitmap(img);
            }
            using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                ctx.DrawImage(bitmap, new Rect(0, 0, Doc.Width, Doc.Height), new Rect(0, 0, w, h));
            if (Grid && Zoom >= 6)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), 1);
                for (int x = 1; x < Doc.Width; x++) ctx.DrawLine(pen, new Point(x * Zoom + 0.5, 0), new Point(x * Zoom + 0.5, h));
                for (int y = 1; y < Doc.Height; y++) ctx.DrawLine(pen, new Point(0, y * Zoom + 0.5), new Point(w, y * Zoom + 0.5));
            }
            if (HoverX >= 0)
            {
                int b = Math.Max(1, BrushWidth), x0 = HoverX - (b - 1) / 2, y0 = HoverY - (b - 1) / 2;
                ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#FFD72A")), 1), new Rect(x0 * Zoom + 0.5, y0 * Zoom + 0.5, b * Zoom - 1, b * Zoom - 1));
            }
        }
    }

    /// <summary>
    /// A pixel editor for one picture of the game: draw with the colours the picture can use (its
    /// palette, or for 4-bit pictures the 16 of its auxiliary palette), with a pencil of any width,
    /// an eraser (the transparent colour), a colour picker, fill, lines and rectangles, and undo.
    /// Apply puts the pixels back into the picture through its own Replace (saved with its file).
    /// </summary>
    public sealed class PixelEditorWindow : Window
    {
        public PixelDocument Doc { get; private set; }
        readonly Func<int[], string> apply;
        readonly PixelCanvas canvas = new PixelCanvas();
        readonly WrapPanel swatches = new WrapPanel { ItemWidth = 20, ItemHeight = 20 };
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock colourText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly Border primaryBox = new Border { Width = 34, Height = 34, BorderThickness = new Thickness(2), BorderBrush = Brushes.White };
        readonly Border secondaryBox = new Border { Width = 24, Height = 24, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        readonly Image preview1 = new Image(), preview2 = new Image();
        readonly NumericUpDown brush = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 1, Increment = 1, FormatString = "0", Width = 110 };
        readonly Slider zoom = new Slider { Minimum = 1, Maximum = 32, Width = 160 };
        readonly Dictionary<string, RadioButton> tools = new Dictionary<string, RadioButton>();
        int[] applied;
        public int Primary { get; private set; }
        public int Secondary { get; private set; }
        public string Tool { get; private set; } = "pencil";
        int startX, startY, button;
        bool drawing;

        readonly Func<string> save;
        readonly string saveWhat;

        /// <summary>A pixel editor on a picture; apply puts pixels back (null, or an error); save (optional) writes its file (null, or an error), named saveWhat.</summary>
        public PixelEditorWindow(IndexedImage img, string what, string note, Func<int[], string> apply, Func<string> save = null, string saveWhat = null)
        {
            this.apply = apply;
            this.save = save;
            this.saveWhat = saveWhat;
            Doc = new PixelDocument(img);
            applied = (int[])Doc.Pixels.Clone();
            canvas.Doc = Doc;
            Title = "Pixel editor — " + what;
            Width = 1100; Height = 760; MinWidth = 700; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // the colours: the most used one first, the transparent one second
            var used = Doc.Used();
            Primary = used.FirstOrDefault(c => c != Doc.Transparent);
            Secondary = Doc.Transparent >= 0 ? Doc.Transparent : used.Skip(1).FirstOrDefault();
            int fit = (int)Math.Max(1, Math.Min(32, Math.Min(620.0 / Doc.Width, 560.0 / Doc.Height)));
            canvas.Zoom = fit; zoom.Value = fit;
            zoom.PropertyChanged += (s, e) => { if (e.Property == Slider.ValueProperty) { canvas.Zoom = (int)Math.Round(zoom.Value); canvas.Changed(); } };
            brush.ValueChanged += (s, e) => { canvas.BrushWidth = (int)(brush.Value ?? 1); canvas.InvalidateVisual(); };

            var toolPanel = new StackPanel { Spacing = 4, Width = 190, Margin = new Thickness(0, 0, 12, 0) };
            toolPanel.Children.Add(new TextBlock { Text = "Tool", FontWeight = FontWeight.SemiBold });
            foreach (var t in new[] { ("pencil", "Pencil (B)"), ("eraser", "Eraser (E)"), ("picker", "Colour picker (I)"), ("fill", "Fill (G)"), ("line", "Line (L)"), ("rect", "Rectangle (R)"), ("filled", "Filled rectangle (Shift+R)") })
            {
                var r = new RadioButton { Content = t.Item2, GroupName = "pixeltool", IsChecked = t.Item1 == "pencil" };
                string id = t.Item1;
                r.IsCheckedChanged += (s, e) => { if (r.IsChecked == true) Tool = id; };
                if (id == "eraser" && Doc.Transparent < 0) { r.IsEnabled = false; ToolTip.SetTip(r, "This picture has no transparent colour."); }
                tools[id] = r;
                toolPanel.Children.Add(r);
            }
            toolPanel.Children.Add(new TextBlock { Text = "Brush width", Margin = new Thickness(0, 8, 0, 0) });
            toolPanel.Children.Add(brush);
            toolPanel.Children.Add(new TextBlock { Text = "Zoom", Margin = new Thickness(0, 8, 0, 0) });
            toolPanel.Children.Add(zoom);
            var grid = new CheckBox { Content = "Grid", IsChecked = true };
            grid.IsCheckedChanged += (s, e) => { canvas.Grid = grid.IsChecked == true; canvas.InvalidateVisual(); };
            toolPanel.Children.Add(grid);
            toolPanel.Children.Add(new TextBlock { Text = "Left button: the first colour; right button: the second. Alt+click picks a colour. Ctrl+Z, Ctrl+Y undo and redo; [ and ] change the width; + and − zoom.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
            toolPanel.Children.Add(new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            RenderOptions.SetBitmapInterpolationMode(preview1, BitmapInterpolationMode.None);
            RenderOptions.SetBitmapInterpolationMode(preview2, BitmapInterpolationMode.None);
            preview1.Width = Doc.Width; preview1.Height = Doc.Height; preview1.HorizontalAlignment = HorizontalAlignment.Left;
            preview2.Width = Doc.Width * 2; preview2.Height = Doc.Height * 2; preview2.HorizontalAlignment = HorizontalAlignment.Left;
            if (Doc.Width <= 96) { toolPanel.Children.Add(preview1); toolPanel.Children.Add(preview2); }
            else { preview1.Width = Math.Min(180, Doc.Width); preview1.Height = preview1.Width * Doc.Height / Doc.Width; toolPanel.Children.Add(preview1); }

            var colourPanel = new StackPanel { Spacing = 6, Width = 16 * 20 + 8, Margin = new Thickness(12, 0, 0, 0) };
            colourPanel.Children.Add(new TextBlock { Text = "Colours", FontWeight = FontWeight.SemiBold });
            var pair = new Panel { Width = 52, Height = 46, HorizontalAlignment = HorizontalAlignment.Left };
            secondaryBox.Margin = new Thickness(22, 18, 0, 0);
            pair.Children.Add(secondaryBox); pair.Children.Add(primaryBox);
            colourPanel.Children.Add(pair);
            colourPanel.Children.Add(colourText);
            colourPanel.Children.Add(new TextBlock
            {
                Text = Doc.Colors.Length <= 32 ? "The " + Doc.Colors.Length + " colours this picture can use (its auxiliary palette). A dot marks the ones it uses now." : "The palette's 256 colours. A dot marks the ones the picture uses now.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7,
            });
            colourPanel.Children.Add(swatches);
            FillSwatches();

            var applyB = new Button { Content = "Apply" };
            applyB.Click += (s, e) => Apply(false);
            var applyClose = new Button { Content = "Apply and close" };
            applyClose.Click += (s, e) => Apply(true);
            var revert = new Button { Content = "Revert to the picture as opened" };
            revert.Click += (s, e) => { Doc.Revert(); Redraw(); };
            var undoB = new Button { Content = "Undo" }; undoB.Click += (s, e) => { Doc.Undo(); Redraw(); };
            var redoB = new Button { Content = "Redo" }; redoB.Click += (s, e) => { Doc.Redo(); Redraw(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            buttons.Children.Add(undoB); buttons.Children.Add(redoB); buttons.Children.Add(revert); buttons.Children.Add(applyB); buttons.Children.Add(applyClose);
            if (save != null)
            {
                var saveB = new Button { Content = "Apply and save " + (saveWhat ?? "the file") };
                saveB.Click += (s, e) => ApplyAndSave();
                buttons.Children.Add(saveB);
            }

            var scroll = new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.Parse("#1E1E22")) };
            canvas.HorizontalAlignment = HorizontalAlignment.Left; canvas.VerticalAlignment = VerticalAlignment.Top;
            canvas.Cursor = new Cursor(StandardCursorType.Cross);
            canvas.PointerPressed += OnPressed;
            canvas.PointerMoved += OnMoved;
            canvas.PointerReleased += OnReleased;
            canvas.PointerExited += (s, e) => { canvas.HoverX = canvas.HoverY = -1; canvas.InvalidateVisual(); };

            var head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = what, FontSize = 16, FontWeight = FontWeight.SemiBold });
            if (!string.IsNullOrEmpty(note)) head.Children.Add(new TextBlock { Text = note, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            head.Children.Add(status);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom); DockPanel.SetDock(toolPanel, Dock.Left); DockPanel.SetDock(colourPanel, Dock.Right);
            dock.Children.Add(head); dock.Children.Add(buttons); dock.Children.Add(toolPanel); dock.Children.Add(colourPanel); dock.Children.Add(scroll);
            Content = dock;
            ManualWindow.Attach(this, "Pixel editor");
            AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            MessageWindow.GuardClose(this, "The picture", () => HasChanges, () => Apply(false), () => AskOnClose);
            Redraw();
        }

        /// <summary>Whether closing with unapplied drawing asks first (tests turn it off).</summary>
        public bool AskOnClose = true;
        /// <summary>Drawing not applied to the picture yet.</summary>
        public bool HasChanges { get { return !Doc.Pixels.SequenceEqual(applied); } }

        void FillSwatches()
        {
            swatches.Children.Clear();
            var used = new HashSet<int>(Doc.Used());
            for (int i = 0; i < Doc.Colors.Length; i++)
            {
                uint c = Doc.Colors[i];
                int idx = i;
                var b = new Border { Width = 18, Height = 18, Margin = new Thickness(1), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)) };
                b.Background = (c >> 24) == 0 ? (IBrush)new SolidColorBrush(Color.Parse("#4A4A50")) : new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
                var inner = new Panel();
                if ((c >> 24) == 0) inner.Children.Add(new TextBlock { Text = "∅", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
                if (used.Contains(i)) inner.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = Brightness(c) > 140 ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 1, 1) });
                b.Child = inner;
                ToolTip.SetTip(b, Describe(i));
                b.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(b).Properties.IsRightButtonPressed) Secondary = idx; else Primary = idx;
                    ShowColours();
                    e.Handled = true;
                };
                swatches.Children.Add(b);
            }
        }

        static int Brightness(uint c) { return (int)(((c >> 16) & 255) * 0.3 + ((c >> 8) & 255) * 0.59 + (c & 255) * 0.11); }

        string Describe(int i)
        {
            uint c = Doc.Colors[i];
            return (c >> 24) == 0 ? string.Format("Colour {0}: transparent", i) : string.Format("Colour {0}: #{1:X2}{2:X2}{3:X2}", i, (c >> 16) & 255, (c >> 8) & 255, c & 255);
        }

        IBrush Swatch(int i)
        {
            uint c = Doc.Colors[Math.Max(0, Math.Min(Doc.Colors.Length - 1, i))];
            return (c >> 24) == 0 ? (IBrush)new SolidColorBrush(Color.Parse("#4A4A50")) : new SolidColorBrush(Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c));
        }

        void ShowColours()
        {
            primaryBox.Background = Swatch(Primary);
            secondaryBox.Background = Swatch(Secondary);
            colourText.Text = "First: " + Describe(Primary) + "\nSecond: " + Describe(Secondary);
        }

        void Redraw()
        {
            canvas.Changed();
            var img = new RgbaImage(Doc.Width, Doc.Height);
            var px = Doc.ToRgba();
            Array.Copy(px, img.Pixels, px.Length);
            var bmp = MainViewModel.ToBitmap(img);
            preview1.Source = bmp; preview2.Source = bmp;
            ShowColours();
            status.Text = string.Format("{0} × {1} pixels, {2} colours{3}.{4}", Doc.Width, Doc.Height, Doc.Colors.Length,
                Doc.Transparent >= 0 ? " (colour " + Doc.Transparent + " transparent)" : "", HasChanges ? " Not applied yet." : "");
        }

        /// <summary>Applies the pixels and writes the picture's file. True when both worked.</summary>
        public bool ApplyAndSave()
        {
            if (!Apply(false)) return false;
            string err = save == null ? "nothing to save to" : save();
            status.Text = err == null ? "Applied and saved in " + (saveWhat ?? "the file") + " (the old file kept as .BKP)." : "Applied, but not saved: " + err;
            return err == null;
        }

        /// <summary>Puts the pixels into the picture. True when it took them.</summary>
        public bool Apply(bool close)
        {
            string err = apply(Doc.Pixels);
            if (err != null) { status.Text = "Not applied: " + err; return false; }
            applied = (int[])Doc.Pixels.Clone();
            if (close) { AskOnClose = false; Close(); return true; }
            Redraw();
            status.Text += " Applied to the picture (not saved yet: save its file).";
            return true;
        }

        // ---- drawing

        int ColourFor(int b) { return Tool == "eraser" && Doc.Transparent >= 0 ? Doc.Transparent : b == 2 ? Secondary : Primary; }

        void OnPressed(object sender, PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(canvas);
            int x, y;
            if (!canvas.PixelAt(pt.Position, out x, out y)) return;
            button = pt.Properties.IsRightButtonPressed ? 2 : 1;
            e.Pointer.Capture(canvas);
            e.Handled = true;
            if (Tool == "picker" || (e.KeyModifiers & KeyModifiers.Alt) != 0) { PickAt(x, y, button); return; }
            int w = (int)(brush.Value ?? 1);
            Doc.Begin();
            drawing = true; startX = x; startY = y; lastX = x; lastY = y;
            switch (Tool)
            {
                case "pencil": case "eraser": Doc.Dab(x, y, w, ColourFor(button)); break;
                case "fill": Doc.Fill(x, y, ColourFor(button)); Doc.End(); drawing = false; break;
                default: canvas.Preview = (int[])Doc.Pixels.Clone(); break;
            }
            Redraw();
        }

        void PickAt(int x, int y, int b)
        {
            int c = Doc.Get(x, y);
            if (c < 0) return;
            if (b == 2) Secondary = c; else Primary = c;
            ShowColours();
        }

        int lastX, lastY;

        void OnMoved(object sender, PointerEventArgs e)
        {
            var pt = e.GetCurrentPoint(canvas);
            int x, y;
            bool inside = canvas.PixelAt(pt.Position, out x, out y);
            canvas.HoverX = inside ? x : -1; canvas.HoverY = inside ? y : -1;
            if (inside) status.Text = string.Format("({0}, {1}): {2}", x, y, Describe(Doc.Get(x, y)));
            if (!drawing) { canvas.InvalidateVisual(); lastX = x; lastY = y; return; }
            x = Math.Max(0, Math.Min(Doc.Width - 1, x)); y = Math.Max(0, Math.Min(Doc.Height - 1, y));
            int w = (int)(brush.Value ?? 1);
            if (Tool == "pencil" || Tool == "eraser")
            {
                // a line from the last pixel, so a quick stroke leaves no gaps
                Doc.Line(lastX < 0 ? x : Math.Max(0, Math.Min(Doc.Width - 1, lastX)), lastY < 0 ? y : Math.Max(0, Math.Min(Doc.Height - 1, lastY)), x, y, w, ColourFor(button));
                canvas.Changed();
            }
            else if (canvas.Preview != null)
            {
                // shapes: drawn on a copy until the button comes up
                var saved = (int[])Doc.Pixels.Clone();
                Shape(startX, startY, x, y, w);
                canvas.Preview = (int[])Doc.Pixels.Clone();
                Array.Copy(saved, Doc.Pixels, saved.Length);
                canvas.Changed();
            }
            lastX = x; lastY = y;
        }

        void Shape(int x0, int y0, int x1, int y1, int w)
        {
            int c = ColourFor(button);
            if (Tool == "line") Doc.Line(x0, y0, x1, y1, w, c);
            else Doc.Rectangle(x0, y0, x1, y1, w, c, Tool == "filled");
        }

        void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            if (!drawing) return;
            drawing = false;
            if (canvas.Preview != null)
            {
                int x, y;
                canvas.PixelAt(e.GetPosition(canvas), out x, out y);
                x = Math.Max(0, Math.Min(Doc.Width - 1, x)); y = Math.Max(0, Math.Min(Doc.Height - 1, y));
                canvas.Preview = null;
                Shape(startX, startY, x, y, (int)(brush.Value ?? 1));
            }
            Doc.End();
            lastX = lastY = -1;
            Redraw();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            if (ctrl && e.Key == Key.Z) { Doc.Undo(); Redraw(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.Y) { Doc.Redo(); Redraw(); e.Handled = true; return; }
            if (ctrl || FocusManager?.GetFocusedElement() is TextBox) return;
            string t = null;
            switch (e.Key)
            {
                case Key.B: t = "pencil"; break;
                case Key.E: t = "eraser"; break;
                case Key.I: t = "picker"; break;
                case Key.G: t = "fill"; break;
                case Key.L: t = "line"; break;
                case Key.R: t = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? "filled" : "rect"; break;
                case Key.OemOpenBrackets: brush.Value = Math.Max(1, (brush.Value ?? 1) - 1); break;
                case Key.OemCloseBrackets: brush.Value = Math.Min(16, (brush.Value ?? 1) + 1); break;
                case Key.Add: case Key.OemPlus: zoom.Value = Math.Min(32, zoom.Value + 1); break;
                case Key.Subtract: case Key.OemMinus: zoom.Value = Math.Max(1, zoom.Value - 1); break;
                case Key.X: { int k = Primary; Primary = Secondary; Secondary = k; ShowColours(); break; }
                default: return;
            }
            if (t != null && tools[t].IsEnabled) tools[t].IsChecked = true;
            e.Handled = true;
        }

        /// <summary>Chooses a colour as the first (tests and scripts).</summary>
        public void SetPrimary(int index) { Primary = index; ShowColours(); }
        public void SetTool(string id) { if (tools.ContainsKey(id)) tools[id].IsChecked = true; Tool = id; }
    }
}
