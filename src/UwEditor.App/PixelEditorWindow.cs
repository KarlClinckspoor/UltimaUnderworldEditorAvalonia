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
        /// <summary>The colours to draw with instead of the document's (the cycling colours at a moment), or null.</summary>
        public uint[] ShownColors;
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
                var px = new IndexedImage(Doc.Width, Doc.Height, Preview ?? Doc.Pixels, ShownColors ?? Doc.Colors).ToRgba();
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

    /// <summary>The mark on a swatch whose colour the game cycles: a corner of black and white stripes, seen on any colour.</summary>
    public sealed class CycleMark : Control
    {
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width;
            foreach (var (size, brush) in new[] { (w, (IBrush)Brushes.White), (w * 0.7, Brushes.Black), (w * 0.42, Brushes.White) })
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(0, 0), true);
                    c.LineTo(new Point(size, 0));
                    c.LineTo(new Point(0, size));
                    c.EndFigure(true);
                }
                ctx.DrawGeometry(brush, null, g);
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
        readonly TextBlock cycleNote = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        readonly CheckBox animate = new CheckBox { Content = "Animate the cycling colours" };
        IReadOnlyList<PaletteCycle> cycles;
        int[] paletteMap;
        uint[] fullPalette;
        Avalonia.Threading.DispatcherTimer cycleTimer;
        readonly System.Diagnostics.Stopwatch cycleClock = new System.Diagnostics.Stopwatch();
        /// <summary>The preview pictures under the drawing (tests look at where it is).</summary>
        public Control PreviewArea { get; private set; }
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
            toolPanel.Children.Add(new TextBlock { Text = "The left button draws with the first colour, the right button with the second; X swaps them. Alt+click picks a colour. Ctrl+Z and Ctrl+Y undo and redo; [ and ] change the brush width; + and − zoom.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });

            // the preview: under the drawing, at the picture's size and twice it (a wide picture only at a fitting size)
            RenderOptions.SetBitmapInterpolationMode(preview1, BitmapInterpolationMode.None);
            RenderOptions.SetBitmapInterpolationMode(preview2, BitmapInterpolationMode.None);
            var previewRow = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            previewRow.Children.Add(new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top });
            double previewHeight;
            if (Doc.Width <= 96 && Doc.Height <= 100)
            {
                preview1.Width = Doc.Width; preview1.Height = Doc.Height;
                preview2.Width = Doc.Width * 2; preview2.Height = Doc.Height * 2;
                preview1.VerticalAlignment = VerticalAlignment.Bottom; preview2.VerticalAlignment = VerticalAlignment.Bottom;
                previewRow.Children.Add(preview1); previewRow.Children.Add(preview2);
                previewHeight = Doc.Height * 2;
            }
            else
            {
                double s = Math.Min(1, Math.Min(240.0 / Doc.Width, 120.0 / Doc.Height));
                preview1.Width = Doc.Width * s; preview1.Height = Doc.Height * s;
                previewRow.Children.Add(preview1);
                previewHeight = preview1.Height;
            }
            PreviewArea = previewRow;
            // the zoom that fits the drawing area above the preview
            fit = (int)Math.Max(1, Math.Min(32, Math.Min(620.0 / Doc.Width, Math.Max(200, 560.0 - previewHeight - 20) / Doc.Height)));
            canvas.Zoom = fit; zoom.Value = fit;

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
            cycleNote.IsVisible = animate.IsVisible = false;
            colourPanel.Children.Add(cycleNote);
            colourPanel.Children.Add(animate);
            animate.IsCheckedChanged += (s, e) => Animate(animate.IsChecked == true);
            FillSwatches();

            var applyB = new Button { Content = "Apply" };
            applyB.Click += (s, e) => Apply(false);
            var applyClose = new Button { Content = "Apply and close" };
            applyClose.Click += (s, e) => Apply(true);
            var revert = new Button { Content = "Revert to the picture as opened" };
            revert.Click += (s, e) => { Doc.Revert(); Redraw(); };
            var undoB = new Button { Content = "Undo" }; undoB.Click += (s, e) => { Doc.Undo(); Redraw(); };
            var redoB = new Button { Content = "Redo" }; redoB.Click += (s, e) => { Doc.Redo(); Redraw(); };
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 10, 0, 0) };
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
            // the side columns scroll when the window is too low for them, so nothing runs over the buttons
            var toolScroll = new ScrollViewer { Content = toolPanel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            var colourScroll = new ScrollViewer { Content = colourPanel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            var middle = new DockPanel { ClipToBounds = true };
            DockPanel.SetDock(previewRow, Dock.Bottom);
            middle.Children.Add(previewRow); middle.Children.Add(scroll);
            var dock = new DockPanel { Margin = new Thickness(14) };
            DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(buttons, Dock.Bottom); DockPanel.SetDock(toolScroll, Dock.Left); DockPanel.SetDock(colourScroll, Dock.Right);
            dock.Children.Add(head); dock.Children.Add(buttons); dock.Children.Add(toolScroll); dock.Children.Add(colourScroll); dock.Children.Add(middle);
            Content = dock;
            Closed += (s, e) => Animate(false);
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
                var cyc = CycleOf(i);
                if (cyc != null) inner.Children.Add(new CycleMark { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
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
            if (i < 0 || i >= Doc.Colors.Length) return "no colour";
            uint c = Doc.Colors[i];
            string s = (c >> 24) == 0 ? string.Format("Colour {0}: transparent", i) : string.Format("Colour {0}: #{1:X2}{2:X2}{3:X2}", i, (c >> 16) & 255, (c >> 8) & 255, c & 255);
            var cyc = CycleOf(i);
            if (cyc != null)
            {
                int e = PaletteEntry(i);
                s += string.Format(". It cycles {0}: palette entries {1} turn round {2} times a second{3}", cyc.Where, cyc.Range, Math.Round(1 / cyc.Period, 1),
                    e != i ? " (this colour is entry " + e + ")" : "");
            }
            return s;
        }

        /// <summary>The palette entry a colour of the picture stands for (an auxiliary palette's colours point into the palette).</summary>
        int PaletteEntry(int i) { return paletteMap == null ? i : i < paletteMap.Length ? paletteMap[i] : -1; }

        /// <summary>The cycle a colour of the picture belongs to in the game, or null.</summary>
        public PaletteCycle CycleOf(int i)
        {
            if (cycles == null || i < 0 || i >= Doc.Colors.Length || (Doc.Colors[i] >> 24) == 0) return null;
            return PaletteCycles.Find(cycles, PaletteEntry(i));
        }

        /// <summary>
        /// Marks the colours the game cycles: cycles (PaletteCycles.InGame or MainMenu), map from the
        /// picture's colours to palette entries (null: the same numbers) and the whole palette, for
        /// the animation.
        /// </summary>
        public void ShowCycles(IReadOnlyList<PaletteCycle> cycles, int[] map, uint[] palette)
        {
            Animate(false);
            this.cycles = cycles; paletteMap = map; fullPalette = palette;
            int n = Enumerable.Range(0, Doc.Colors.Length).Count(i => CycleOf(i) != null);
            cycleNote.Text = n == 0 ? "" : string.Format("A striped corner marks the {0} colour{1} the game cycles (water, lava, fire): anything drawn with them changes colour in the game.", n, n == 1 ? "" : "s");
            cycleNote.IsVisible = animate.IsVisible = n > 0;
            animate.IsChecked = false;
            FillSwatches();
        }

        /// <summary>Marks the cycling colours of a picture of a picture file shown with a palette of pals: the main menu's for UW1's OPSCR.BYT, the game's otherwise.</summary>
        public void ShowCycles(PictureFile file, IndexedImage img, Palettes pals, int palette, bool uw2)
        {
            if (file == null || img == null || pals == null) return;
            // a 4-bit .GR picture's colours are its auxiliary palette's entries (gfx/GRFX.C); the others are the palette's own
            int[] map = file.Kind == PictureFileKind.Gr && img.Type != 4 ? pals.Aux[img.Aux & 31] : null;
            bool menu = file.Name.Equals("OPSCR.BYT", StringComparison.OrdinalIgnoreCase);
            ShowCycles(menu ? PaletteCycles.MainMenu(uw2) : PaletteCycles.InGame(uw2), map, pals.Get(palette));
        }

        /// <summary>Whether the drawing shows the cycling colours turning.</summary>
        public bool Animating { get { return cycleTimer != null; } }

        void Animate(bool on)
        {
            if (on == Animating) return;
            if (!on)
            {
                cycleTimer.Stop(); cycleTimer = null; cycleClock.Reset();
                canvas.ShownColors = null;
                Redraw();
                return;
            }
            if (cycles == null || fullPalette == null) return;
            double period = cycles.Min(c => c.Period);
            cycleClock.Restart();
            cycleTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(period) };
            cycleTimer.Tick += (s, e) => ShowCyclesAt(cycleClock.Elapsed.TotalSeconds);
            cycleTimer.Start();
            ShowCyclesAt(0);
        }

        /// <summary>Draws the picture as the cycling shows it some seconds in (the animation, and tests).</summary>
        public void ShowCyclesAt(double seconds)
        {
            if (cycles == null || fullPalette == null) return;
            canvas.ShownColors = PaletteCycles.At(Doc.Colors, paletteMap, fullPalette, cycles, seconds);
            Redraw();
        }

        /// <summary>The colours the drawing uses now (the cycled ones while animating).</summary>
        public uint[] ShownColors { get { return canvas.ShownColors ?? Doc.Colors; } }

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
            var px = new IndexedImage(Doc.Width, Doc.Height, Doc.Pixels, ShownColors).ToRgba();
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
            status.Text = err == null ? "Applied and saved in " + (saveWhat ?? "the file") + " (the old file is kept as .BKP)." : "Applied, but not saved: " + err;
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
            status.Text += " Applied to the picture, but its file is not saved yet.";
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
