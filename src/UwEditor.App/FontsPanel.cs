using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Conversations;
using UwEditor.Core.Graphics;

namespace UwEditor.App
{
    /// <summary>
    /// A glyph drawn big, one cell a pixel: click or drag to draw (the first cell decides
    /// whether the stroke draws or rubs out; the right button rubs out), and drag the red width
    /// marker (or click in the strip under the cells) to set how far the text moves on.
    /// Columns past the width are shaded: their pixels overlap the next character.
    /// </summary>
    public sealed class GlyphCanvas : Control
    {
        public UwFont Font;
        public int Code;
        public int Zoom = 22;
        public const int Strip = 14;
        /// <summary>Before a stroke or a width drag starts (for undo).</summary>
        public event Action Starting;
        /// <summary>After any pixel or the width changed.</summary>
        public event Action Edited;
        bool painting, draggingWidth, paintValue;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Font == null) return new Size(0, 0);
            return new Size(Font.Columns * Zoom + 1, Font.Height * Zoom + Strip + 2);
        }

        public void Fit(double width, double height)
        {
            if (Font == null) return;
            Zoom = (int)Math.Max(8, Math.Min(34, Math.Min((width - 2) / Font.Columns, (height - Strip - 2) / Font.Height)));
            InvalidateMeasure(); InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            if (Font == null) return;
            var g = Font.Glyphs[Code];
            int w = Font.Columns * Zoom, h = Font.Height * Zoom;
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, w, h));
            var past = new SolidColorBrush(Color.FromArgb(40, 230, 40, 40));
            if (g.Width < Font.Columns) ctx.FillRectangle(past, new Rect(g.Width * Zoom, 0, w - g.Width * Zoom, h));
            var ink = new SolidColorBrush(Color.Parse("#111111"));
            var inkPast = new SolidColorBrush(Color.Parse("#7A2A2A"));
            for (int y = 0; y < Font.Height; y++)
                for (int x = 0; x < Font.Columns; x++)
                    if (Font.Pixel(Code, x, y)) ctx.FillRectangle(x < g.Width ? ink : inkPast, new Rect(x * Zoom, y * Zoom, Zoom, Zoom));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1);
            for (int x = 0; x <= Font.Columns; x++) ctx.DrawLine(grid, new Point(x * Zoom + 0.5, 0), new Point(x * Zoom + 0.5, h));
            for (int y = 0; y <= Font.Height; y++) ctx.DrawLine(grid, new Point(0, y * Zoom + 0.5), new Point(w, y * Zoom + 0.5));
            // the baseline under 'x'
            int baseRow = Font.Height - FontBdf.Descent(Font);
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 40, 110, 220)), 1), new Point(0, baseRow * Zoom + 0.5), new Point(w, baseRow * Zoom + 0.5));
            // the width marker and its handle in the strip
            var red = new SolidColorBrush(Color.Parse("#E02020"));
            double mx = Math.Min(g.Width, Font.Columns) * Zoom + 0.5;
            ctx.DrawLine(new Pen(red, 2), new Point(mx, 0), new Point(mx, h + Strip));
            var tri = new StreamGeometry();
            using (var c = tri.Open()) { c.BeginFigure(new Point(mx, h + 2), true); c.LineTo(new Point(mx - 6, h + Strip)); c.LineTo(new Point(mx + 6, h + Strip)); c.EndFigure(true); }
            ctx.DrawGeometry(red, null, tri);
        }

        (int X, int Y) Cell(Point p) { return ((int)Math.Floor(p.X / Zoom), (int)Math.Floor(p.Y / Zoom)); }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (Font == null) return;
            var p = e.GetPosition(this);
            var pt = e.GetCurrentPoint(this);
            double mx = Math.Min(Font.Glyphs[Code].Width, Font.Columns) * Zoom;
            Starting?.Invoke();
            if (p.Y >= Font.Height * Zoom || Math.Abs(p.X - mx) <= 4)
            {
                draggingWidth = true;
                SetWidthAt(p.X);
            }
            else
            {
                var c = Cell(p);
                painting = true;
                paintValue = pt.Properties.IsRightButtonPressed ? false : !Font.Pixel(Code, c.X, c.Y);
                Paint(c.X, c.Y, paintValue);
            }
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (Font == null) return;
            var p = e.GetPosition(this);
            if (draggingWidth) SetWidthAt(p.X);
            else if (painting) { var c = Cell(p); Paint(c.X, c.Y, paintValue); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            painting = draggingWidth = false;
            e.Pointer.Capture(null);
        }

        void SetWidthAt(double x)
        {
            int w = (int)Math.Round(x / Zoom);
            w = Math.Max(0, Math.Min(Font.Columns, w));
            if (Font.Glyphs[Code].Width == w) return;
            Font.Glyphs[Code].Width = w;
            InvalidateVisual();
            Edited?.Invoke();
        }

        /// <summary>Sets a pixel as a stroke would (outside the cells nothing happens).</summary>
        public void Paint(int x, int y, bool on)
        {
            if (Font == null || x < 0 || y < 0 || x >= Font.Columns || y >= Font.Height) return;
            if (Font.Pixel(Code, x, y) == on) return;
            Font.SetPixel(Code, x, y, on);
            InvalidateVisual();
            Edited?.Invoke();
        }
    }

    /// <summary>
    /// The Fonts tab of Tools ▸ Graphics: the six DATA\FONT*.SYS, each code 0-127 with its
    /// glyph, its letter through the accent table and its width; a 1-bit glyph editor; "Make
    /// accented letter"; a preview line at game scale on the scroll's paper; export and import
    /// (PNG sheet, BDF, TrueType); the accent table with its presets; and the checks of what
    /// stops a mapped letter showing in the game. Saving writes every changed font with all 128
    /// glyphs (FONT*.SYS.ORIG and .BKP kept, as the other files).
    /// </summary>
    public sealed class FontsPanel : UserControl
    {
        sealed class FontRow
        {
            public string Name;
            public bool Changed;
            public override string ToString() { return Name + (Changed ? "  · changed" : ""); }
        }

        readonly GameSession session;
        readonly MainWindow main;
        readonly Dictionary<string, byte[]> onDisk = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<(string Font, int Code, FontGlyph Glyph, int Count)> undo = new List<(string, int, FontGlyph, int)>();
        AccentSettings accents;
        static (FontGlyph Glyph, int Columns)? clipboard;

        readonly ListBox fontList = new ListBox { FontSize = 12 };
        readonly TextBlock useText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 };
        readonly TextBlock shapeText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        readonly NumericUpDown spaceBox = new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 110 };
        readonly NumericUpDown maxBox = new NumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 110 };
        readonly WrapPanel grid = new WrapPanel { Orientation = Orientation.Horizontal };
        readonly List<Border> cells = new List<Border>();
        readonly GlyphCanvas canvas = new GlyphCanvas();
        readonly TextBlock codeText = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly TextBlock codeNote = new TextBlock { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        readonly NumericUpDown widthBox = new NumericUpDown { Minimum = 0, Maximum = 16, Increment = 1, FormatString = "0", Width = 100 };
        readonly TextBox letterBox = new TextBox { Width = 44, MaxLength = 1, HorizontalContentAlignment = HorizontalAlignment.Center };
        readonly TextBox baseBox = new TextBox { Width = 44, MaxLength = 1, HorizontalContentAlignment = HorizontalAlignment.Center, Text = "e" };
        readonly ComboBox markBox = new ComboBox { Width = 150, ItemsSource = Enum.GetValues(typeof(AccentMark)).Cast<AccentMark>().Select(AccentMaker.Label).ToList(), SelectedIndex = 1 };
        readonly TextBox previewBox = new TextBox { Text = "Avatar, thou art welcome. Café, ação, pingüino.", MinWidth = 300 };
        readonly Image previewImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
        readonly TextBlock previewInfo = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly NumericUpDown zoomBox = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 3, Increment = 1, FormatString = "0", Width = 90 };
        readonly ListBox tableList = new ListBox { FontSize = 12, Height = 150 };
        readonly ComboBox presetBox = new ComboBox { ItemsSource = AccentTable.Presets, SelectedIndex = 0, Width = 160 };
        readonly CheckBox debugBox = new CheckBox { Content = "Also symbols only the debug strings use", FontSize = 12 };
        readonly CheckBox undrawnBox = new CheckBox { Content = "Also codes the DOS game draws as '?'", FontSize = 12 };
        readonly TextBlock freeText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        readonly TextBox checksBox = ItemToolLayout.TextArea(true);
        readonly TextBlock status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        CodeUse codeUse;
        bool quiet;
        /// <summary>The letters the last preset fill could not give a code.</summary>
        string leftOver = "";
        readonly Border banner = Warning(true), tableWarning = Warning(false);

        /// <summary>What the red warning says when an executable change is needed. The change: in
        /// gfx/GRLIBI.ASM at 4B2C (both games), cmp al,20h becomes cmp al,01h and cmp al,7Ah
        /// becomes cmp al,7Fh, so the rasteriser draws codes 1-127 (bytes of 128 and up stay
        /// '?': the compare is signed). Not made by the editor yet.</summary>
        public const string ExeChangeDetails = "The game draws only codes 20h-7Ah: every other code comes out as '?'. For these letters to show, the game's executable must be changed: two bytes of the text drawer (gfx/GRLIBI.ASM at 4B2C), cmp al,20h to cmp al,01h and cmp al,7Ah to cmp al,7Fh, which lets it draw codes 1-127. The editor does not make this change yet. Without it the DOS game draws these letters as '?', and so do Underworld Exhumed and UltimaHacks, which do not run the DOS executable and would need the same change in their own code.";

        static Border Warning(bool big)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.Parse("#B00020")), CornerRadius = new CornerRadius(4), Padding = new Thickness(big ? 14 : 8, big ? 10 : 6),
                Margin = new Thickness(0, big ? 0 : 4, 0, big ? 6 : 4), IsVisible = false,
                Child = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = big ? 16 : 12, TextWrapping = TextWrapping.Wrap },
            };
        }

        /// <summary>The red warning's text, or null when the stock game can draw every mapped letter.</summary>
        public string WarningText
        {
            get
            {
                var undrawn = session.Accents.Map.Where(kv => !AccentTable.GameDraws(kv.Key)).Select(kv => kv.Value).ToList();
                if (undrawn.Count == 0 && leftOver.Length == 0) return null;
                var parts = new List<string>();
                if (undrawn.Count > 0) parts.Add("The accent table maps " + string.Join(" ", undrawn) + " to codes the stock game cannot draw.");
                if (leftOver.Length > 0) parts.Add("No free code was left for " + string.Join(" ", leftOver.ToCharArray()) + ": the stock game has only " + FreeCodes().Count(AccentTable.GameDraws) + " codes it draws that no string uses.");
                return "⚠ THE GAME'S EXECUTABLE WOULD HAVE TO BE CHANGED. " + string.Join(" ", parts) + " " + ExeChangeDetails;
            }
        }

        public bool WarningShown { get { return banner.IsVisible; } }

        void RefreshWarning()
        {
            string t = WarningText;
            foreach (var b in new[] { banner, tableWarning })
            {
                b.IsVisible = t != null;
                ((TextBlock)b.Child).Text = t ?? "";
            }
        }

        /// <summary>The six fonts as edited (unsaved changes too).</summary>
        public GameFonts Fonts { get; } = new GameFonts();
        /// <summary>The font shown.</summary>
        public UwFont Font { get; private set; }
        /// <summary>The code being edited.</summary>
        public int Code { get; private set; } = 'A';
        public GlyphCanvas Canvas { get { return canvas; } }
        public AccentTable Table { get { return session.Accents; } }
        public string Status { get { return status.Text; } private set { status.Text = value; } }
        public string PreviewInfo { get { return previewInfo.Text; } }
        public string ChecksText { get { return checksBox.Text; } }
        public string FreeCodesText { get { return freeText.Text; } }
        public IReadOnlyList<Border> Cells { get { return cells; } }

        public FontsPanel(GameSession session, MainWindow main)
        {
            this.session = session;
            this.main = main;
            accents = main?.AccentSettings() ?? new AccentSettings { Map = session.Accents.ToDictionary() };
            debugBox.IsChecked = accents.DebugSymbols;
            undrawnBox.IsChecked = accents.UndrawnCodes;
            foreach (var n in FontFiles.Names)
            {
                string p = GameProfile.Find(session.DataFolder, n);
                if (p == null) continue;
                try
                {
                    var b = File.ReadAllBytes(p);
                    var f = UwFont.Read(b);
                    f.Name = n;
                    Fonts.ByName[n] = f;
                    onDisk[n] = b;
                    paths[n] = p;
                }
                catch (Exception ex) { Status = n + " cannot be read: " + ex.Message; }
            }
            Content = Build();
            ScanStrings();
            fontList.ItemsSource = Fonts.ByName.Keys.Select(n => new FontRow { Name = n }).ToList();
            fontList.SelectionChanged += (s, e) => { var r = fontList.SelectedItem as FontRow; if (r != null) ShowFont(r.Name); };
            if (fontList.ItemCount > 0) fontList.SelectedIndex = 0;
            RefreshTable();
            RunChecks();
        }

        static Button Btn(string text, Action click, string tip = null)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 3) };
            b.Click += (s, e) => click();
            if (tip != null) ToolTip.SetTip(b, tip);
            return b;
        }

        static TextBlock Head(string t) { return new TextBlock { Text = t, FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 8, 0, 4) }; }

        Control Build()
        {
            // left: fonts, the shape, export and import, save
            var left = new StackPanel { Spacing = 4 };
            left.Children.Add(new Border { Height = 160, Child = fontList });
            left.Children.Add(useText);
            left.Children.Add(shapeText);
            var sp = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
            sp.Children.Add(new TextBlock { Text = "Space width", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            Grid.SetColumn(spaceBox, 1); sp.Children.Add(spaceBox);
            var ml = new TextBlock { Text = "Max width", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            Grid.SetRow(ml, 1); sp.Children.Add(ml);
            Grid.SetRow(maxBox, 1); Grid.SetColumn(maxBox, 1); sp.Children.Add(maxBox);
            left.Children.Add(sp);
            spaceBox.ValueChanged += (s, e) => { if (!quiet && Font != null && spaceBox.Value.HasValue) { Font.SpaceWidth = (int)spaceBox.Value.Value; Changed(); } };
            maxBox.ValueChanged += (s, e) => { if (!quiet && Font != null && maxBox.Value.HasValue) { Font.MaxWidth = (int)maxBox.Value.Value; Changed(); } };
            var lw = new WrapPanel();
            lw.Children.Add(Btn("Max width = widest", () => { if (Font == null) return; maxBox.Value = Font.WidestGlyph(); }, "Set the header's max width to the widest glyph's width."));
            left.Children.Add(lw);
            left.Children.Add(Head("Export and import"));
            var ex = new WrapPanel();
            ex.Children.Add(Btn("Export PNG…", async () => { var p = await SavePath(Base() + ".png", "PNG sheet", "png"); if (p != null) ExportPng(p); }, "16 × 8 cells, black on white; the red row under each glyph is its width."));
            ex.Children.Add(Btn("Import PNG…", async () => { var p = await OpenPath("PNG sheet", "*.png"); if (p != null) await Report(ImportPng(p)); }));
            ex.Children.Add(Btn("Export BDF…", async () => { var p = await SavePath(Base() + ".bdf", "BDF font", "bdf"); if (p != null) ExportBdf(p); }, "The X11 bitmap font format: each glyph at its letter from the accent table, with its width. Reads back exactly."));
            ex.Children.Add(Btn("Import BDF…", async () => { var p = await OpenPath("BDF font", "*.bdf"); if (p != null) await Report(ImportBdf(p)); }));
            ex.Children.Add(Btn("Export TrueType…", async () => { var p = await SavePath(Base() + ".ttf", "TrueType font", "ttf"); if (p != null) ExportTtf(p); }, "A font to install in Windows, macOS or Linux: each pixel a square, the letters at their accent table characters."));
            left.Children.Add(ex);
            left.Children.Add(Head("Save"));
            var sv = new WrapPanel();
            sv.Children.Add(Btn("Save fonts", () => { try { Save(); } catch (Exception e) { Status = e.Message; } }, "Write every changed font to DATA with all 128 glyphs. The first original is kept as .ORIG, the last as .BKP. Make a mod carries them."));
            sv.Children.Add(Btn("Revert font", RevertFont, "Back to the font as DATA has it."));
            left.Children.Add(sv);
            left.Children.Add(status);

            // middle: the codes
            var gridScroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            for (int c = 0; c < UwFont.Codes; c++)
            {
                int code = c;
                var cell = new Border { Width = 46, Height = 62, Margin = new Thickness(1), BorderThickness = new Thickness(1), Padding = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand) };
                cell.PointerPressed += (s, e) => { SelectCode(code); e.Handled = true; };
                cells.Add(cell);
                grid.Children.Add(cell);
            }

            // right: the glyph, the accent table, the preview, the checks
            var right = new StackPanel { Spacing = 4 };
            right.Children.Add(codeText);
            right.Children.Add(codeNote);
            canvas.Starting += PushUndo;
            canvas.Edited += () => { Changed(); quiet = true; widthBox.Value = Font.Glyphs[Code].Width; quiet = false; };
            right.Children.Add(new Border { Child = canvas, HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) });
            var wrow = new WrapPanel();
            wrow.Children.Add(new TextBlock { Text = "Width ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            wrow.Children.Add(widthBox);
            widthBox.ValueChanged += (s, e) => { if (!quiet && Font != null && widthBox.Value.HasValue) SetWidth((int)widthBox.Value.Value); };
            right.Children.Add(wrow);
            var tools = new WrapPanel();
            tools.Children.Add(Btn("Copy", CopyGlyph, "Copy this glyph (to paste into another code or another font)."));
            tools.Children.Add(Btn("Paste", PasteGlyph));
            tools.Children.Add(Btn("Clear", () => Apply(g => { Array.Clear(g.Rows, 0, g.Rows.Length); })));
            tools.Children.Add(Btn("←", () => Shift(-1, 0), "Move the pixels a column left."));
            tools.Children.Add(Btn("→", () => Shift(1, 0)));
            tools.Children.Add(Btn("↑", () => Shift(0, -1)));
            tools.Children.Add(Btn("↓", () => Shift(0, 1)));
            tools.Children.Add(Btn("Undo", Undo));
            tools.Children.Add(Btn("Revert glyph", RevertGlyph));
            right.Children.Add(tools);
            right.Children.Add(Head("Make accented letter"));
            var mk = new WrapPanel();
            mk.Children.Add(new TextBlock { Text = "Letter ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            mk.Children.Add(baseBox);
            mk.Children.Add(new TextBlock { Text = "  with ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            mk.Children.Add(markBox);
            mk.Children.Add(Btn("Make at this code", () => { var b = (baseBox.Text ?? "").FirstOrDefault(); if (b != 0) MakeAccented(b, (AccentMark)markBox.SelectedIndex); }, "Copy the letter's glyph to this code and add the mark, drawn for this font's size. Touch it up by hand."));
            right.Children.Add(mk);
            var lt = new WrapPanel();
            lt.Children.Add(new TextBlock { Text = "This code's letter ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            lt.Children.Add(letterBox);
            lt.Children.Add(Btn("Set", () => { var l = (letterBox.Text ?? "").FirstOrDefault(); if (l != 0) SetLetter(Code, l); }, "In the accent table: this code stands for the letter in the strings editors."));
            lt.Children.Add(Btn("Remove", () => SetLetter(Code, '\0')));
            right.Children.Add(lt);

            right.Children.Add(Head("Accent table (this game folder)"));
            right.Children.Add(tableWarning);
            right.Children.Add(tableList);
            var pr = new WrapPanel();
            pr.Children.Add(presetBox);
            pr.Children.Add(Btn("Fill free codes", () => FillPreset((presetBox.SelectedItem as AccentPreset)?.Name), "Give the preset's letters that have no code yet the free codes, in order, as far as they go."));
            pr.Children.Add(Btn("Clear table", () => { session.Accents.Clear(); leftOver = ""; SaveTable(); }));
            right.Children.Add(pr);
            debugBox.IsCheckedChanged += (s, e) => { if (!quiet) SaveTable(); };
            undrawnBox.IsCheckedChanged += (s, e) => { if (!quiet) SaveTable(); };
            right.Children.Add(debugBox);
            right.Children.Add(undrawnBox);
            right.Children.Add(freeText);

            var rightScroll = new ScrollViewer { Content = right };

            // bottom: preview and checks
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Margin = new Thickness(0, 8, 0, 0) };
            var pv = new StackPanel { Spacing = 4 };
            var pvTop = new DockPanel();
            var zl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            zl.Children.Add(new TextBlock { Text = "Scale", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            zl.Children.Add(zoomBox);
            DockPanel.SetDock(zl, Dock.Right);
            pvTop.Children.Add(zl);
            pvTop.Children.Add(previewBox);
            pv.Children.Add(new TextBlock { Text = "Preview (on the scroll's paper; type accented letters)", FontWeight = FontWeight.SemiBold, FontSize = 13 });
            pv.Children.Add(pvTop);
            pv.Children.Add(new ScrollViewer { Content = previewImage, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            pv.Children.Add(previewInfo);
            previewBox.TextChanged += (s, e) => RenderPreview();
            zoomBox.ValueChanged += (s, e) => RenderPreview();
            bottom.Children.Add(pv);
            var ck = new DockPanel();
            var ckHead = new DockPanel();
            var again = Btn("Check again", () => RunChecks());
            DockPanel.SetDock(again, Dock.Right);
            ckHead.Children.Add(again);
            ckHead.Children.Add(new TextBlock { Text = "Checks: what stops a mapped letter showing", FontWeight = FontWeight.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(ckHead, Dock.Top);
            ck.Children.Add(ckHead);
            checksBox.Height = 110;
            ck.Children.Add(checksBox);
            Grid.SetColumn(ck, 2);
            bottom.Children.Add(ck);

            var main = new Grid { ColumnDefinitions = new ColumnDefinitions("230,10,*,10,400"), RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetColumnSpan(banner, 5); main.Children.Add(banner);
            var leftScroll = new ScrollViewer { Content = left };
            Grid.SetRow(leftScroll, 1); main.Children.Add(leftScroll);
            Grid.SetRow(gridScroll, 1); Grid.SetColumn(gridScroll, 2); main.Children.Add(gridScroll);
            Grid.SetRow(rightScroll, 1); Grid.SetColumn(rightScroll, 4); main.Children.Add(rightScroll);
            Grid.SetRow(bottom, 2); Grid.SetColumnSpan(bottom, 5); main.Children.Add(bottom);
            return main;
        }

        string Base() { return Path.GetFileNameWithoutExtension(Font?.Name ?? "FONT"); }

        // ----- showing

        public void SelectFont(string name)
        {
            var row = (fontList.ItemsSource as IEnumerable<FontRow>)?.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row != null) fontList.SelectedItem = row;
            ShowFont(name);
        }

        void ShowFont(string name)
        {
            var f = Fonts[name];
            if (f == null || f == Font) return;
            Font = f;
            canvas.Font = f;
            quiet = true;
            spaceBox.Value = f.SpaceWidth; maxBox.Value = f.MaxWidth;
            widthBox.Maximum = Math.Max(f.Columns, f.WidestGlyph());
            quiet = false;
            useText.Text = FontFiles.Use(name);
            shapeText.Text = string.Format("{0} rows high, {1} byte{2} a row ({3} columns), {4} bytes a glyph (the game takes at most {5}). The file holds {6} glyphs; saving writes all 128 so no code reads another font's leftovers. Height and bytes a row are fixed: the game lays text out by them.",
                f.Height, f.RowBytes, f.RowBytes == 1 ? "" : "s", f.Columns, f.GlyphBytes, UwFont.MaxGlyphBytes, f.Count);
            canvas.Zoom = Math.Max(10, Math.Min(30, 360 / Math.Max(f.Columns, f.Height)));
            canvas.InvalidateMeasure();
            SelectCode(Code);
            RefreshCells();
            RenderPreview();
        }

        public void SelectCode(int code)
        {
            Code = Math.Max(0, Math.Min(127, code));
            canvas.Code = Code;
            canvas.InvalidateVisual();
            ShowCode();
            RefreshCellBorders();
        }

        void ShowCode()
        {
            if (Font == null) return;
            var g = Font.Glyphs[Code];
            var l = session.Accents.LetterOf(Code);
            string ch = Code >= 0x20 && Code < 0x7F ? "'" + (char)Code + "'" : "";
            codeText.Text = string.Format("Code {0} (0x{0:X2}) {1}{2}", Code, ch, l != null ? "  →  " + l : "");
            var notes = new List<string>();
            if (!UwFont.IsDrawn(Code)) notes.Add("The DOS game draws this code as '?' (it draws only 20h-7Ah).");
            if (!g.Present) notes.Add("The file has no glyph here: the game reads whatever an earlier font left. Saving writes a blank one.");
            string problem = AccentTable.CodeProblem(Code);
            if (problem != null) notes.Add("Cannot carry a letter: " + problem);
            else if (codeUse != null && codeUse.All[Code] > 0 && l == null) notes.Add("Used " + codeUse.All[Code] + " times in STRINGS.PAK" + (codeUse.OutsideDebug[Code] == 0 ? ", only in the debug strings" : "") + ".");
            codeNote.Text = string.Join(" ", notes);
            quiet = true;
            widthBox.Value = g.Width;
            letterBox.Text = l?.ToString() ?? "";
            char b; AccentMark m;
            if (l != null && AccentMaker.Decompose(l.Value, out b, out m)) { baseBox.Text = b.ToString(); markBox.SelectedIndex = (int)m; }
            quiet = false;
        }

        void RefreshCells()
        {
            if (Font == null) return;
            for (int c = 0; c < UwFont.Codes; c++) FillCell(c);
            RefreshCellBorders();
        }

        void FillCell(int c)
        {
            var g = Font.Glyphs[c];
            var l = session.Accents.LetterOf(c);
            string ch = l != null ? l.ToString() : c >= 0x21 && c < 0x7F ? ((char)c).ToString() : "";
            int z = Math.Max(1, Math.Min(3, 40 / Math.Max(Font.Columns, Font.Height)));
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = c.ToString(), FontSize = 9, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new Border
            {
                Height = 26, Background = UwFont.IsDrawn(c) ? Brushes.White : new SolidColorBrush(Color.Parse("#D8D8D8")),
                Child = new Image { Source = GlyphBitmap(Font, c, z), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            stack.Children.Add(new TextBlock { Text = (ch.Length > 0 ? ch + " · " : "") + (g.Present ? g.Width.ToString() : "–"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = l != null ? new SolidColorBrush(Color.Parse("#D06A10")) : null });
            cells[c].Child = stack;
            string tip = "Code " + c + (l != null ? ", the accent table's '" + l + "'" : "") + (g.Present ? ", width " + g.Width : ", no glyph in the file") + (UwFont.IsDrawn(c) ? "" : ". The DOS game draws it as '?'.");
            ToolTip.SetTip(cells[c], tip);
        }

        void RefreshCellBorders()
        {
            for (int c = 0; c < cells.Count; c++)
            {
                bool mapped = session.Accents.LetterOf(c) != null;
                cells[c].BorderBrush = c == Code ? new SolidColorBrush(Color.Parse("#2A7FFF")) : mapped ? new SolidColorBrush(Color.Parse("#D06A10")) : new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
                cells[c].BorderThickness = new Thickness(c == Code ? 2 : 1);
            }
        }

        static Bitmap Rgba(int w, int h, byte[] px)
        {
            var bmp = new WriteableBitmap(new PixelSize(Math.Max(1, w), Math.Max(1, h)), new Vector(96, 96), PixelFormats.Rgba8888, AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
            return bmp;
        }

        static Bitmap GlyphBitmap(UwFont f, int code, int zoom)
        {
            int w = f.Columns * zoom, h = f.Height * zoom;
            var px = new byte[w * h * 4];
            var g = f.Glyphs[code];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = 4 * (y * w + x);
                    bool on = f.Pixel(code, x / zoom, y / zoom);
                    bool past = x / zoom >= g.Width;
                    byte v = on ? (past ? (byte)140 : (byte)0) : (byte)255;
                    px[i] = v; px[i + 1] = on && past ? (byte)60 : v; px[i + 2] = on && past ? (byte)60 : v; px[i + 3] = (byte)(on ? 255 : 0);
                }
            return Rgba(w, h, px);
        }

        // ----- the preview

        /// <summary>The preview's text (shown letters; typed accented letters go through the accent table).</summary>
        public string PreviewText { get { return previewBox.Text; } set { previewBox.Text = value; RenderPreview(); } }

        void RenderPreview()
        {
            if (Font == null) return;
            string text = previewBox.Text ?? "";
            var codes = new List<int>();
            var missing = new List<char>();
            foreach (char ch in text)
            {
                int c = session.Accents.CodeOf(ch);
                if (c < 0) c = ch < 128 ? ch : -1;
                if (c < 0) { if (!missing.Contains(ch)) missing.Add(ch); c = '?'; }
                codes.Add(c);
            }
            int width;
            var px = Font.Render(codes, out width);
            int zoom = (int)(zoomBox.Value ?? 3);
            int pad = 3;
            int w = (width + Font.Columns + 2 * pad) * zoom, h = (Font.Height + 2 * pad) * zoom;
            uint paper = 0xFFC8A870, ink = 0xFF1A1008;
            try
            {
                var pal = Palettes.Load(session.DataFolder).Get(0);
                paper = pal[session.IsUw2 ? 0x71 : 0x2A];
            }
            catch (Exception) { }
            var b = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int gx = x / zoom - pad, gy = y / zoom - pad;
                    bool on = gx >= 0 && gy >= 0 && gx < px.GetLength(0) && gy < Font.Height && px[gx, gy];
                    uint c = on ? ink : paper;
                    int i = 4 * (y * w + x);
                    b[i] = (byte)(c >> 16); b[i + 1] = (byte)(c >> 8); b[i + 2] = (byte)c; b[i + 3] = 255;
                }
            previewImage.Source = Rgba(w, h, b);
            int measured = Font.StringWidth(codes);
            var info = new List<string> { string.Format("{0} pixels wide ({1} characters).", width, codes.Count) };
            if (codes.Count > 54) info.Add("The game's string_width counts only the first 54 characters (it measures " + measured + " pixels): a longer line is laid out wrong.");
            else if (measured != width) info.Add("The game measures " + measured + " pixels: codes it draws as '?' still count their own widths.");
            if (codes.Any(c => !UwFont.IsDrawn(c))) info.Add("Codes outside 20h-7Ah are drawn as '?'.");
            if (missing.Count > 0) info.Add("No code for: " + string.Join(" ", missing) + " (shown as '?'; give them codes in the accent table).");
            previewInfo.Text = string.Join(" ", info);
        }

        // ----- editing

        void PushUndo()
        {
            if (Font == null) return;
            undo.Add((Font.Name, Code, Font.Glyphs[Code].Clone(), Font.Count));
            if (undo.Count > 200) undo.RemoveAt(0);
        }

        public void Undo()
        {
            if (undo.Count == 0) { Status = "Nothing to undo."; return; }
            var u = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            var f = Fonts[u.Font];
            if (f == null) return;
            f.Glyphs[u.Code] = u.Glyph;
            f.Count = u.Count;
            if (f != Font) SelectFont(u.Font);
            SelectCode(u.Code);
            Changed();
        }

        void Apply(Action<FontGlyph> change)
        {
            if (Font == null) return;
            PushUndo();
            var g = Font.Glyphs[Code];
            if (!g.Present) Font.SetPixel(Code, 0, 0, false);   // makes the code present
            change(Font.Glyphs[Code]);
            Changed();
        }

        public void SetWidth(int w)
        {
            if (Font == null) return;
            w = Math.Max(0, Math.Min(255, w));
            if (Font.Glyphs[Code].Width == w) return;
            Apply(g => g.Width = w);
        }

        void Shift(int dx, int dy)
        {
            Apply(g =>
            {
                var rows = (uint[])g.Rows.Clone();
                uint mask = Font.Columns == 32 ? uint.MaxValue : (1u << Font.Columns) - 1;
                for (int y = 0; y < Font.Height; y++)
                {
                    int sy = y - dy;
                    uint r = sy >= 0 && sy < Font.Height ? rows[sy] : 0;
                    g.Rows[y] = (dx > 0 ? r >> dx : r << -dx) & mask;
                }
            });
        }

        public void CopyGlyph()
        {
            if (Font == null) return;
            clipboard = (Font.Glyphs[Code].Clone(), Font.Columns);
            Status = "Copied code " + Code + " of " + Font.Name + ".";
        }

        public void PasteGlyph()
        {
            if (Font == null || clipboard == null) { Status = "Nothing copied."; return; }
            PushUndo();
            Font.SetGlyph(Code, clipboard.Value.Glyph, clipboard.Value.Columns);
            Status = "Pasted into code " + Code + (clipboard.Value.Glyph.Rows.Length != Font.Height || clipboard.Value.Columns != Font.Columns ? " (cut or padded to this font's " + Font.Columns + " × " + Font.Height + ")" : "") + ".";
            Changed();
        }

        /// <summary>Puts the base letter with the mark at the code being edited.</summary>
        public void MakeAccented(char baseLetter, AccentMark mark)
        {
            if (Font == null) return;
            if (baseLetter >= 128) { Status = "The base letter must be a plain letter."; return; }
            if (!Font.Glyphs[baseLetter].Present || Font.Glyphs[baseLetter].IsBlank) { Status = "This font has no glyph for '" + baseLetter + "'."; return; }
            PushUndo();
            var g = AccentMaker.Make(Font, baseLetter, mark);
            Font.SetGlyph(Code, g, Font.Columns);
            Status = "Code " + Code + ": '" + baseLetter + "' with " + AccentMaker.Label(mark).ToLowerInvariant() + ". Touch it up by hand if it needs it.";
            Changed();
        }

        void RevertGlyph()
        {
            if (Font == null) return;
            var orig = UwFont.Read(onDisk[Font.Name]);
            PushUndo();
            Font.Glyphs[Code] = orig.Glyphs[Code].Clone();
            if (!orig.Glyphs[Code].Present && Code < Font.Count) Font.Glyphs[Code].Present = true;
            Changed();
        }

        void RevertFont()
        {
            if (Font == null) return;
            string n = Font.Name;
            var f = UwFont.Read(onDisk[n]);
            f.Name = n;
            Fonts.ByName[n] = f;
            undo.RemoveAll(u => u.Font == n);
            Font = null;
            ShowFont(n);
            Changed();
            Status = n + " reverted.";
        }

        void Changed()
        {
            canvas.InvalidateVisual();
            if (Font != null) FillCell(Code);
            ShowCode();
            RenderPreview();
            var rows = fontList.ItemsSource as IEnumerable<FontRow>;
            if (rows != null)
            {
                bool any = false;
                foreach (var r in rows) { bool c = IsChanged(r.Name); if (c != r.Changed) { r.Changed = c; any = true; } }
                if (any) { var sel = fontList.SelectedItem; fontList.ItemsSource = rows.ToList(); fontList.SelectedItem = (fontList.ItemsSource as IEnumerable<FontRow>).FirstOrDefault(r => r.Name == (sel as FontRow)?.Name); }
            }
        }

        bool IsChanged(string name)
        {
            var f = Fonts[name];
            if (f == null) return false;
            try { return !f.ToBytes().SequenceEqual(onDisk[name]); } catch (Exception) { return true; }
        }

        public bool HasChanges { get { return Fonts.ByName.Keys.Any(IsChanged); } }

        /// <summary>Writes every changed font with all 128 glyphs (the old file kept as .BKP, the first as .ORIG). Returns the files written.</summary>
        public List<string> Save()
        {
            var done = new List<string>();
            foreach (var n in Fonts.ByName.Keys.ToList())
            {
                if (!IsChanged(n)) continue;
                var f = Fonts[n];
                var problems = f.Problems();
                if (problems.Count > 0) throw new InvalidDataException(n + ": " + string.Join(" ", problems));
                f.FillTo128();
                var bytes = f.ToBytes();
                SafeFile.Write(paths[n], bytes);
                onDisk[n] = bytes;
                done.Add(n);
            }
            Changed();
            RunChecks();
            Status = done.Count == 0 ? "No font has changed." : "Saved " + string.Join(", ", done) + " (the old files kept as .BKP).";
            return done;
        }

        // ----- the accent table

        void ScanStrings()
        {
            try
            {
                var pak = SharedStrings();
                codeUse = pak == null ? null : CodeUse.Scan(pak.Blocks.SelectMany(b => pak.Get(b).Select(s => (b, s))));
            }
            catch (Exception) { codeUse = null; }
        }

        StringsPakFile SharedStrings()
        {
            try { var p = main?.Strings()?.Pak; if (p != null) return p; } catch (Exception) { }
            string path = GameProfile.Find(session.DataFolder, "STRINGS.PAK");
            return path == null ? null : StringsPakFile.Load(path);
        }

        /// <summary>The codes a letter can take now (the check boxes' choices).</summary>
        public List<int> FreeCodes()
        {
            if (codeUse == null) return new List<int>();
            return codeUse.FreeCodes(session.Accents, debugBox.IsChecked == true, undrawnBox.IsChecked == true);
        }

        void RefreshTable()
        {
            RefreshWarning();
            tableList.ItemsSource = session.Accents.Map.Select(kv => string.Format("{0,3}  {1}  →  {2}{3}", kv.Key, kv.Key >= 0x21 && kv.Key < 0x7F ? "'" + (char)kv.Key + "'" : "   ", kv.Value, AccentTable.GameDraws(kv.Key) ? "" : "   (drawn as '?' by the DOS game)")).ToList();
            var free = FreeCodes().Where(c => session.Accents.LetterOf(c) == null).ToList();
            freeText.Text = codeUse == null ? "STRINGS.PAK could not be read: no free codes." :
                free.Count == 0 ? "No free code is left." :
                free.Count + " free code" + (free.Count == 1 ? "" : "s") + ": " + string.Join(" ", free.Select(c => c >= 0x21 && c < 0x7F ? "'" + (char)c + "'" : c.ToString())) + ". The DOS game draws only 20h-7Ah, so the free codes are the printable symbols no string uses.";
        }

        void SaveTable()
        {
            accents = new AccentSettings { Map = session.Accents.ToDictionary(), DebugSymbols = debugBox.IsChecked == true, UndrawnCodes = undrawnBox.IsChecked == true };
            if (main != null) main.SaveAccents(accents);
            RefreshTable();
            RefreshCells();
            ShowCode();
            RenderPreview();
            RunChecks();
        }

        /// <summary>Gives a code a letter in the accent table ('\0' removes it). False, with the reason in Status, when it cannot.</summary>
        public bool SetLetter(int code, char letter)
        {
            try
            {
                if (letter == '\0') session.Accents.Remove(code);
                else
                {
                    ScanStrings();   // the strings may have changed since the panel was built
                    var free = FreeCodes();
                    if (!free.Contains(code) && session.Accents.LetterOf(code) == null)
                    {
                        string why = AccentTable.CodeProblem(code) ?? (codeUse != null && codeUse.All[code] > 0 ? "STRINGS.PAK uses '" + (char)code + "' " + codeUse.All[code] + " times: its strings would show the letter instead." : !AccentTable.GameDraws(code) ? "The DOS game draws code " + code + " as '?' (tick Also codes the DOS game draws as '?' to use it anyway)." : "It is not free.");
                        Status = "Code " + code + " cannot be '" + letter + "': " + why;
                        return false;
                    }
                    session.Accents.Set(code, letter);
                }
            }
            catch (ArgumentException ex) { Status = ex.Message; return false; }
            SaveTable();
            Status = letter == '\0' ? "Code " + code + " has no letter now." : "Code " + code + " is '" + letter + "' in the strings editors.";
            return true;
        }

        /// <summary>Fills the free codes with a preset's letters. Returns the letters left without a code.</summary>
        public string FillPreset(string name)
        {
            var p = AccentTable.Presets.FirstOrDefault(x => x.Name == name);
            if (p == null) return "";
            ScanStrings();   // a code typed in the strings since the panel was built is no longer free
            string left = session.Accents.Fill(p, FreeCodes());
            leftOver = left;
            SaveTable();
            Status = name + ": " + session.Accents.Count + " letters have codes" + (left.Length > 0 ? "; no free code for " + string.Join(" ", left.ToCharArray()) + "." : ".");
            return left;
        }

        /// <summary>Sets the free-code choices (as the check boxes do).</summary>
        public void SetFreeChoices(bool debugSymbols, bool undrawn)
        {
            quiet = true;
            debugBox.IsChecked = debugSymbols; undrawnBox.IsChecked = undrawn;
            quiet = false;
            SaveTable();
        }

        // ----- checks

        public List<string> RunChecks()
        {
            List<string> r;
            try
            {
                var pak = SharedStrings();
                var blocks = pak == null ? new List<(int, IList<string>)>() : pak.Blocks.Select(b => (b, (IList<string>)pak.Get(b))).ToList();
                r = FontChecks.Run(session.Accents, Fonts, blocks, FontChecks.Originals(session.DataFolder), FontChecks.MapNotes(session));
            }
            catch (Exception ex) { r = new List<string> { "The checks could not run: " + ex.Message }; }
            checksBox.Text = session.Accents.IsEmpty ? "The accent table is empty: nothing to check." : r.Count == 0 ? "No problems: every mapped letter used in the strings has a glyph of its own in the fonts that show it." : string.Join("\n", r);
            return r;
        }

        // ----- export and import

        public void ExportPng(string path) { File.WriteAllBytes(path, FontSheet.Export(Font)); Status = "Exported " + Font.Name + " to " + path + "."; }

        public void ExportBdf(string path) { File.WriteAllText(path, FontBdf.Export(Font, session.Accents)); Status = "Exported " + Font.Name + " to " + path + "."; }

        public void ExportTtf(string path)
        {
            File.WriteAllBytes(path, FontTtf.Export(Font, session.Accents, FontTtf.FamilyName(Font, session.IsUw2 ? "UW2" : "UW1")));
            Status = "Exported " + Font.Name + " to " + path + " (font name \"" + FontTtf.FamilyName(Font, session.IsUw2 ? "UW2" : "UW1") + "\").";
        }

        /// <summary>Replaces the font with a PNG sheet's glyphs. Returns the refusal or the warnings (empty when none).</summary>
        public string ImportPng(string path)
        {
            try
            {
                var warn = new List<string>();
                var f = FontSheet.Import(Font, File.ReadAllBytes(path), warn);
                Replace(f);
                Status = "Imported " + path + " into " + Font.Name + " (not saved).";
                return string.Join("\n", warn);
            }
            catch (Exception ex) { Status = "Not imported: " + ex.Message; return "Not imported: " + ex.Message; }
        }

        /// <summary>Replaces the font with a BDF's glyphs (fitted into the game font's height and columns). Returns the refusal or the warnings.</summary>
        public string ImportBdf(string path)
        {
            try
            {
                var warn = new List<string>();
                var f = FontBdf.Import(File.ReadAllText(path), session.Accents, Font, warn);
                if (f.Height != Font.Height || f.RowBytes != Font.RowBytes)
                {
                    // a BDF of another shape (one this editor wrote from another font): fit its glyphs
                    var fit = Font.Clone();
                    for (int c = 0; c < UwFont.Codes; c++) if (f.Glyphs[c].Present) fit.SetGlyph(c, f.Glyphs[c], f.Columns);
                    warn.Add(string.Format("The BDF is {0} × {1}; the glyphs were cut or padded to {2}'s {3} × {4}.", f.Columns, f.Height, Font.Name, Font.Columns, Font.Height));
                    f = fit;
                }
                Replace(f);
                Status = "Imported " + path + " into " + Font.Name + " (not saved).";
                return string.Join("\n", warn);
            }
            catch (Exception ex) { Status = "Not imported: " + ex.Message; return "Not imported: " + ex.Message; }
        }

        void Replace(UwFont f)
        {
            string n = Font.Name;
            f.Name = n;
            for (int c = 0; c < UwFont.Codes; c++) undo.Add((n, c, Font.Glyphs[c].Clone(), Font.Count));
            Fonts.ByName[n] = f;
            Font = null;
            ShowFont(n);
            Changed();
        }

        async Task Report(string text)
        {
            var w = TopLevel.GetTopLevel(this) as Window;
            if (!string.IsNullOrEmpty(text) && w != null) await MessageWindow.Show(w, "Font import", text);
        }

        async Task<string> SavePath(string suggested, string kind, string ext)
        {
            try
            {
                var f = await TopLevel.GetTopLevel(this).StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = suggested, DefaultExtension = ext,
                    FileTypeChoices = new[] { new FilePickerFileType(kind) { Patterns = new[] { "*." + ext } } },
                });
                return f?.TryGetLocalPath();
            }
            catch (Exception ex) { Status = ex.Message; return null; }
        }

        async Task<string> OpenPath(string kind, string pattern)
        {
            try
            {
                var files = await TopLevel.GetTopLevel(this).StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType(kind) { Patterns = new[] { pattern } } },
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            }
            catch (Exception ex) { Status = ex.Message; return null; }
        }
    }
}
