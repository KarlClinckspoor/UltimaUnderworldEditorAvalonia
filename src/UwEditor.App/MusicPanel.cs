using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using UltimateEditor.Games;
using UltimateEditor.Sound;
using UwEditor.Core;
using UwEditor.Core.Music;

namespace UwEditor.App
{
    /// <summary>
    /// The Music tab of Tools ▸ Sound and music: the themes of the open game with their two files
    /// (FM and MT-32), sizes against the game's buffer, the timbres they use and their validation;
    /// export as MIDI (one version, or every theme to a folder), import a MIDI file (FM, MT-32 or
    /// both, with the options and the validation report: an import with errors is refused), and
    /// revert to the original. No playback. Changes are held until SaveAll (the window's Save).
    /// </summary>
    public sealed class MusicPanel : UserControl
    {
        /// <summary>A row of the theme list.</summary>
        public sealed class ThemeRow
        {
            public MusicTheme Theme { get; set; }
            public string Number { get; set; }
            public string When { get; set; }
            public string Fm { get; set; }
            public string Mt32 { get; set; }
            public string Timbres { get; set; }
            public string Status { get; set; }
            public bool Bad { get; set; }
        }

        static readonly FontFamily Mono = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");
        static readonly double[] Widths = { 34, 0, 92, 92, 62, 124 };

        readonly MainWindow main;
        readonly GameSession session;
        readonly ListBox list = new ListBox();
        readonly TextBox report = ItemToolLayout.TextArea(true);
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly ComboBox make = new ComboBox { ItemsSource = new[] { "FM and MT-32", "FM only", "MT-32 only" }, SelectedIndex = 0, Width = 150 };
        readonly ComboBox mtFrom = new ComboBox { ItemsSource = new[] { "Map General MIDI to MT-32", "Keep the program numbers", "A second MIDI file for the MT-32" }, SelectedIndex = 0, Width = 250 };
        readonly CheckBox loopWhole = new CheckBox { Content = "Loop the whole theme" };
        readonly CheckBox moveChannels = new CheckBox { Content = "Move channels 1 and 11–16 to free ones", IsChecked = true };
        readonly CheckBox replaceDrums = new CheckBox { Content = "Replace drums UW.AD lacks", IsChecked = true };
        readonly CheckBox cc111 = new CheckBox { Content = "Controller 111 starts a loop", IsChecked = true };
        readonly List<Button> themeButtons = new List<Button>();

        /// <summary>The game's music (null without a game or a SOUND folder).</summary>
        public MusicLibrary Library { get; private set; }
        /// <summary>The rows shown, theme order.</summary>
        public List<ThemeRow> Rows { get; private set; } = new List<ThemeRow>();
        /// <summary>The report below the list: the selected theme's validation, or the last import's or export's result.</summary>
        public string ReportText { get { return report.Text; } }
        public string StatusText { get { return status.Text; } }
        public ListBox ThemeList { get { return list; } }

        public MusicPanel(MainWindow main)
        {
            this.main = main;
            session = main?.ViewModel?.Session;
            if (session != null)
            {
                Library = MusicLibrary.Open(session.GameFolder, session.Kind);
                if (Library.SoundFolder == null) Library = null;
            }
            Content = Build();
            Refresh();
            if (Rows.Count > 0) list.SelectedIndex = 0;
        }

        Control Build()
        {
            var root = new Grid { Margin = new Thickness(8), RowDefinitions = new RowDefinitions("Auto,Auto,3*,Auto,Auto,2*") };
            string limit = session == null ? "" : string.Format("{0:N0}", MusicThemes.SizeLimit(session.Kind));
            var head = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 6),
                Text = session == null ? "Open a game to see its music." :
                    Library == null ? "This game folder has no SOUND folder." :
                    string.Format("Each theme has two files: {0} for the FM cards (AdLib, Sound Blaster) and {1} for the Roland MT-32. A theme may take at most {2} bytes. " +
                                  "The editor does not play music: export a theme as MIDI to listen to it. Nothing is written until you save.",
                                  session.IsUw2 ? "UWAnn.XMI" : "AWnn.XMI", session.IsUw2 ? "UWRnn.XMI" : "UWnn.XMI", limit),
            };
            Grid.SetRow(head, 0); root.Children.Add(head);

            var header = RowGrid(new[] { "No.", "Plays", "FM", "MT-32", "Timbres", "Check" }, true);
            header.Margin = new Thickness(12, 0, 12, 2);
            Grid.SetRow(header, 1); root.Children.Add(header);

            list.ItemTemplate = new FuncDataTemplate<ThemeRow>((r, ns) => r == null ? new TextBlock() : RowGrid(new[] { r.Number, r.When, r.Fm, r.Mt32, r.Timbres, r.Status }, false, r.Bad));
            list.SelectionChanged += (o, e) => { ShowSelected(); UpdateButtons(); };
            list.MinHeight = 120;
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 4)), new Setter(ListBoxItem.MinHeightProperty, 0.0) } });
            Grid.SetRow(list, 2); root.Children.Add(list);

            var exportFm = Btn("Export FM MIDI…", async () => await PickExport(MusicVersion.Fm));
            var exportMt = Btn("Export MT-32 MIDI…", async () => await PickExport(MusicVersion.Mt32));
            var exportAll = Btn("Export all…", async () => await PickExportAll());
            var import = Btn("Import MIDI…", async () => await PickImport());
            import.FontWeight = FontWeight.SemiBold;
            var revert = Btn("Revert to the original", () => { Revert(); return Task.CompletedTask; });
            ToolTip.SetTip(exportFm, "Export the selected theme's FM file as a standard MIDI file (format 0, 120 ticks a second, with loops as loopStart/loopEnd markers).");
            ToolTip.SetTip(exportMt, "Export the selected theme's MT-32 file as a standard MIDI file. Its programs are MT-32 presets, not General MIDI.");
            ToolTip.SetTip(exportAll, "Export both files of every theme into a folder (AW01.mid, UW01.mid...).");
            ToolTip.SetTip(revert, "Restore both files of the selected theme as the game shipped them, from the .ORIG copies the editor keeps.");
            themeButtons.AddRange(new[] { exportFm, exportMt, import, revert });
            var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            foreach (var b in new[] { exportFm, exportMt, exportAll, import, revert }) { b.Margin = new Thickness(0, 0, 8, 4); buttons.Children.Add(b); }
            exportAll.IsEnabled = Library != null;
            Grid.SetRow(buttons, 3); root.Children.Add(buttons);

            var opts = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            opts.Children.Add(Label("Import makes"));
            opts.Children.Add(make);
            opts.Children.Add(Label("MT-32 programs"));
            opts.Children.Add(mtFrom);
            foreach (var c in new[] { loopWhole, moveChannels, replaceDrums, cc111 }) { c.Margin = new Thickness(0, 0, 14, 0); c.FontSize = 12; opts.Children.Add(c); }
            ToolTip.SetTip(mtFrom, "The MT-32 is not General MIDI: its program numbers stand for other instruments. Map a MIDI file made for General MIDI, keep the numbers of one made for the MT-32, or give a second file for the MT-32 version.");
            ToolTip.SetTip(loopWhole, "Add an endless FOR/NEXT loop around the whole theme when it has no loop marks. The game restarts or replaces most themes when they end anyway; the victory theme must not loop.");
            ToolTip.SetTip(moveChannels, "The game plays only channels 2 to 10 (10 is the drums).");
            ToolTip.SetTip(replaceDrums, "FM: a drum key that bank 127 of UW.AD lacks plays the nearest drum it has. When this is unticked, such a theme is refused.");
            ToolTip.SetTip(cc111, "RPG Maker marks its loop start with controller 111. In the game's sequencer 111 means channel protection, so it is never kept.");
            make.SelectionChanged += (o, e) => mtFrom.IsEnabled = make.SelectedIndex != 1;
            Grid.SetRow(opts, 4); root.Children.Add(opts);

            var bottom = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            bottom.Children.Add(status);
            report.MinHeight = 100;
            report.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(report, 1); bottom.Children.Add(report);
            Grid.SetRow(bottom, 5); root.Children.Add(bottom);
            return root;
        }

        static TextBlock Label(string t) { return new TextBlock { Text = t, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; }

        static Button Btn(string text, Func<Task> act)
        {
            var b = new Button { Content = text };
            b.Click += async (o, e) => await act();
            return b;
        }

        static Grid RowGrid(string[] cells, bool header, bool bad = false)
        {
            var g = new Grid();
            for (int i = 0; i < Widths.Length; i++) g.ColumnDefinitions.Add(Widths[i] == 0 ? new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 160 } : new ColumnDefinition(Widths[i], GridUnitType.Pixel));
            for (int i = 0; i < cells.Length; i++)
            {
                var t = new TextBlock
                {
                    Text = cells[i], FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 6, 0),
                    FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                    FontFamily = !header && (i == 2 || i == 3 || i == 4) ? Mono : FontFamily.Default,
                    HorizontalAlignment = i == 0 || i == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                if (!header && i == 1) ToolTip.SetTip(t, cells[i]);
                if (!header && i == 5 && bad) t.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x50, 0x3C));
                Grid.SetColumn(t, i);
                g.Children.Add(t);
            }
            return g;
        }

        /// <summary>Rebuilds the rows from the library (sizes, timbres, the validation), keeping the selection.</summary>
        public void Refresh()
        {
            int sel = list.SelectedIndex;
            Rows = new List<ThemeRow>();
            if (Library != null)
            {
                int limit = MusicThemes.SizeLimit(Library.Kind);
                foreach (var t in Library.Themes)
                {
                    var fm = Library.Read(t, MusicVersion.Fm);
                    var mt = Library.Read(t, MusicVersion.Mt32);
                    var issues = Library.Validate(t, MusicVersion.Fm).Concat(Library.Validate(t, MusicVersion.Mt32)).ToList();
                    int errors = issues.Count(i => i.Severity == MusicSeverity.Error), warnings = issues.Count(i => i.Severity == MusicSeverity.Warning);
                    string st = errors > 0 ? errors + (errors == 1 ? " error" : " errors") : warnings > 0 ? warnings + (warnings == 1 ? " warning" : " warnings") : "OK";
                    if (Library.IsChanged(t, MusicVersion.Fm) || Library.IsChanged(t, MusicVersion.Mt32)) st += ", changed";
                    Rows.Add(new ThemeRow
                    {
                        Theme = t, Number = t.Number.ToString(), When = t.When,
                        Fm = Size(t.FmFile, fm), Mt32 = Size(t.Mt32File, mt),
                        Timbres = TimbreCount(fm) + "/" + TimbreCount(mt),
                        Status = st, Bad = errors > 0,
                    });
                }
            }
            list.ItemsSource = Rows;
            if (Rows.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(Rows.Count - 1, sel));
            UpdateButtons();
        }

        static string Size(string file, byte[] b)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            return b == null ? name + " missing" : string.Format("{0} {1,5:N0}", name, b.Length);
        }

        static string TimbreCount(byte[] b)
        {
            if (b == null) return "–";
            try { return XmiFile.Parse(b).Timbres.Count.ToString(); }
            catch (FormatException) { return "?"; }
        }

        void UpdateButtons()
        {
            bool any = SelectedTheme != null;
            foreach (var b in themeButtons) b.IsEnabled = any;
        }

        /// <summary>The theme selected in the list.</summary>
        public MusicTheme SelectedTheme
        {
            get { return (list.SelectedItem as ThemeRow)?.Theme; }
            set { int i = Rows.FindIndex(r => r.Theme == value || value != null && r.Theme.Number == value.Number); if (i >= 0) list.SelectedIndex = i; }
        }

        void ShowSelected()
        {
            var t = SelectedTheme;
            if (t == null || Library == null) { report.Text = ""; return; }
            report.Text = ThemeReport(t);
        }

        /// <summary>A theme's report: when it plays, both files' checks and the timbres they use.</summary>
        public string ThemeReport(MusicTheme t)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Theme {0}: {1}. When it ends, {2}.\n", t.Number, t.When, t.AtEnd);
            foreach (var v in new[] { MusicVersion.Fm, MusicVersion.Mt32 })
            {
                sb.AppendLine();
                sb.AppendFormat("{0} ({1}){2}\n", t.File(v), v == MusicVersion.Fm ? "FM" : "MT-32", Library.IsChanged(t, v) ? ", changed, not saved yet" : "");
                foreach (var i in Library.Validate(t, v)) sb.Append("  ").AppendLine(i.ToString());
                var b = Library.Read(t, v);
                if (b == null) continue;
                List<XmiTimbre> timb;
                try { timb = XmiFile.Parse(b).Timbres; }
                catch (FormatException) { continue; }
                sb.Append("  Timbres (bank:patch): ").AppendLine(string.Join(", ", timb.Select(x => TimbreName(x, v))));
            }
            return sb.ToString();
        }

        static string TimbreName(XmiTimbre x, MusicVersion v)
        {
            if (x.Bank == 127) return "drum " + x.Patch + " " + MidiToXmi.DrumName(x.Patch);
            if (x.Bank != 0) return x.Bank + ":" + x.Patch;
            return x.Patch + " " + (v == MusicVersion.Mt32 ? Mt32Programs.Mt32Name(x.Patch) : Mt32Programs.GmName(x.Patch));
        }

        // ---- export

        /// <summary>Writes the selected theme's file as a MIDI file; false (with the reason in the status line) when it cannot.</summary>
        public bool ExportMidi(MusicVersion v, string path)
        {
            var t = SelectedTheme;
            if (t == null || Library == null) return false;
            try
            {
                var m = Library.ExportMidi(t, v);
                if (m == null) { status.Text = t.File(v) + " is missing."; return false; }
                File.WriteAllBytes(path, m);
                status.Text = string.Format("{0} exported to {1}.", t.File(v), path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                status.Text = "Export failed: " + ex.Message;
                return false;
            }
        }

        /// <summary>Every theme, both files, as MIDI files in folder; returns how many.</summary>
        public int ExportAll(string folder)
        {
            if (Library == null) return 0;
            try
            {
                int n = Library.ExportAll(folder);
                status.Text = string.Format("{0} MIDI files written to {1}.", n, folder);
                return n;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                status.Text = "Export failed: " + ex.Message;
                return 0;
            }
        }

        async Task PickExport(MusicVersion v)
        {
            var t = SelectedTheme;
            var top = TopLevel.GetTopLevel(this);
            if (t == null || top == null) return;
            try
            {
                var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = MusicLibrary.MidiName(t, v), DefaultExtension = "mid",
                    FileTypeChoices = new[] { new FilePickerFileType("MIDI file") { Patterns = new[] { "*.mid", "*.midi" } } },
                });
                string p = f?.TryGetLocalPath();
                if (p != null) ExportMidi(v, p);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        async Task PickExportAll()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            try
            {
                var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder for the MIDI files", AllowMultiple = false });
                string p = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                if (p != null) ExportAll(p);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        // ---- import

        /// <summary>The import options as the panel's controls set them.</summary>
        public MusicImportOptions Options
        {
            get
            {
                return new MusicImportOptions
                {
                    LoopWholeTheme = loopWhole.IsChecked == true, MoveChannels = moveChannels.IsChecked == true,
                    ReplaceMissingDrums = replaceDrums.IsChecked == true, Cc111LoopStart = cc111.IsChecked == true,
                };
            }
        }

        /// <summary>0 both files, 1 FM only, 2 MT-32 only.</summary>
        public int ImportMakes { get { return make.SelectedIndex; } set { make.SelectedIndex = value; } }
        /// <summary>0 map GM to MT-32, 1 keep the numbers, 2 a second file.</summary>
        public int Mt32Source { get { return mtFrom.SelectedIndex; } set { mtFrom.SelectedIndex = value; } }
        public CheckBox LoopWholeBox { get { return loopWhole; } }
        public CheckBox ReplaceDrumsBox { get { return replaceDrums; } }

        /// <summary>
        /// Imports MIDI files into the selected theme: midi for the FM version (and the MT-32 one
        /// unless mt32Midi is given). The report shows what the conversion did and the validation;
        /// with an error nothing changes. True when the theme was changed (written on SaveAll).
        /// </summary>
        public bool ImportMidi(byte[] midi, byte[] mt32Midi = null, string name = null)
        {
            var t = SelectedTheme;
            if (t == null || Library == null || midi == null) return false;
            bool fm = ImportMakes != 2, mt = ImportMakes != 1;
            bool second = mt && Mt32Source == 2;
            if (second && mt32Midi == null)
            {
                if (fm) { status.Text = "Choose the MT-32 MIDI file too."; return false; }
                mt32Midi = midi;            // MT-32 only: the one file is the MT-32 one, its programs kept
            }
            var results = Library.Import(t, midi, second ? mt32Midi : null, fm, mt, Options, Mt32Source == 0);
            bool refused = results.Any(kv => kv.Value.HasErrors);
            var sb = new StringBuilder();
            sb.AppendFormat("Import of {0} into theme {1}: {2}\n", name ?? "the MIDI file", t.Number,
                refused ? "refused, nothing changed (fix the errors below)." : "done. Save to write the files.");
            foreach (var kv in results)
            {
                sb.AppendLine();
                sb.AppendFormat("{0} ({1}):\n", t.File(kv.Key), kv.Key == MusicVersion.Fm ? "FM" : "MT-32");
                foreach (var i in kv.Value.Notes.Concat(kv.Value.Validation)) sb.Append("  ").AppendLine(i.ToString());
            }
            int sel = list.SelectedIndex;
            Refresh();
            list.SelectedIndex = sel;
            report.Text = sb.ToString();
            status.Text = refused ? "Import refused: the theme has errors." : string.Format("Theme {0} imported (not saved).", t.Number);
            return !refused;
        }

        async Task<byte[]> PickMidi(string title)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return null;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title, AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("MIDI file") { Patterns = new[] { "*.mid", "*.midi", "*.rmi", "*.smf" } }, FilePickerFileTypes.All },
            });
            string p = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (p == null) return null;
            lastName = Path.GetFileName(p);
            return File.ReadAllBytes(p);
        }
        string lastName;

        async Task PickImport()
        {
            if (SelectedTheme == null) return;
            try
            {
                bool second = ImportMakes == 0 && Mt32Source == 2;
                var m = await PickMidi(ImportMakes == 2 ? "Choose the MIDI file for the MT-32 version" : second ? "Choose the MIDI file for the FM version" : "Choose the MIDI file");
                if (m == null) return;
                string name = lastName;
                byte[] mt = null;
                if (second)
                {
                    mt = await PickMidi("Choose the MIDI file for the MT-32 version (its programs are kept)");
                    if (mt == null) return;
                    name += " and " + lastName;
                }
                ImportMidi(m, mt, name);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        /// <summary>The selected theme's two files as the game shipped them (written on SaveAll). False when the editor never changed them.</summary>
        public bool Revert()
        {
            var t = SelectedTheme;
            if (t == null || Library == null) return false;
            bool a = Library.Revert(t, MusicVersion.Fm), b = Library.Revert(t, MusicVersion.Mt32);
            Refresh();
            ShowSelected();
            status.Text = a || b ? string.Format("Theme {0} goes back to the original when you save.", t.Number) : "Theme " + t.Number + " is already as the game shipped it.";
            return a || b;
        }

        // ---- unsaved work

        /// <summary>Imported or reverted themes not yet written.</summary>
        public bool HasChanges { get { return Library != null && Library.HasChanges; } }

        /// <summary>Writes the changed theme files (SafeFile: .ORIG kept once, .BKP each time). False when writing failed.</summary>
        public bool SaveAll()
        {
            if (Library == null) return true;
            try
            {
                var written = Library.Save();
                if (written.Count > 0) status.Text = "Saved " + string.Join(", ", written.Select(Path.GetFileName)) + ".";
                Refresh();
                ShowSelected();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                status.Text = "Saving the music failed: " + ex.Message;
                return false;
            }
        }
    }
}
