using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using UltimateEditor.Sound;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Sound;

namespace UwEditor.App
{
    /// <summary>A row of the Speech tab: a speech file and the cutscene line it goes with.</summary>
    public sealed class SpeechRow
    {
        public SpeechItem Item;
        public VocInfo Info;
        public string Line;
        public string State;
        public string Name { get { return Item.Name; } }
    }

    /// <summary>A row of the Effects tab: an effect of SOUNDS.DAT, or (UW2) a trap sound UWnn.VOC (effect 100 + n).</summary>
    public sealed class EffectRow
    {
        public int Number;
        /// <summary>A trap sound (UWnn.VOC), not a SOUNDS.DAT entry.</summary>
        public bool IsTrap;
        public SoundEffect Effect;
        /// <summary>The .VOC it plays (SPnn.VOC when digital, UWnn.VOC for a trap sound), or null.</summary>
        public string File;
        public VocInfo Info;
        public string What;
        public string State;
        public List<TrapSound> Traps = new List<TrapSound>();
    }

    /// <summary>
    /// Tools ▸ Sound and music: the game's digitised sound. The Speech tab lists the speech files
    /// with the cutscene line each goes with (UW1's introduction, UW2's cutscene speech); the
    /// Effects tab the effects of SOUNDS.DAT, editable, with what plays each, and in UW2 the
    /// digitised ones and the trap sounds. Clips play through OpenAL, export as WAV and are
    /// replaced from WAV files, converted to what the game plays and checked against its
    /// limits. Nothing is written until Save. More tabs (Music) go in Tabs, their unsaved work
    /// through AddPanel.
    /// </summary>
    public sealed class SoundWindow : Window
    {
        readonly MainWindow main;
        readonly GameSession session;
        readonly TextBlock status = new TextBlock { FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        readonly ListBox speechList = new ListBox();
        readonly ListBox effectList = new ListBox();
        readonly TextBlock speechDetail = Note("");
        readonly StackPanel effectDetail = new StackPanel { Spacing = 6 };
        readonly List<Tuple<Func<bool>, Func<bool>>> panels = new List<Tuple<Func<bool>, Func<bool>>>();
        readonly DispatcherTimer playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        ObjectTables tables;
        List<TrapSound> traps;
        bool filling;
        string playing;

        /// <summary>The tabs: Speech, Effects; the lead adds Music.</summary>
        public TabControl Tabs { get; } = new TabControl();
        /// <summary>The SOUND folder with the unsaved changes.</summary>
        public SoundLibrary Library { get; private set; }
        /// <summary>The import settings of the Speech tab.</summary>
        public SoundConvertOptions SpeechOptions { get; } = new SoundConvertOptions();
        /// <summary>The import settings of the Effects tab.</summary>
        public SoundConvertOptions EffectOptions { get; } = new SoundConvertOptions();
        public string Status { get { return status.Text; } }
        public ListBox SpeechList { get { return speechList; } }
        public ListBox EffectList { get { return effectList; } }

        public SoundWindow(MainWindow main, GameSession s)
        {
            this.main = main;
            session = s;
            Library = SoundLibrary.For(s);
            Title = "Sound and music — " + s.Title;
            Width = 1000; Height = 720; MinWidth = 760; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            try { tables = ObjectTables.Load(s); } catch (Exception) { tables = null; }

            Tabs.Items.Add(new TabItem { Header = "Speech", Content = SpeechTab() });
            Tabs.Items.Add(new TabItem { Header = "Effects", Content = EffectsTab() });

            var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new TextBlock { Text = "Sound and music", FontSize = 16, FontWeight = FontWeight.SemiBold });
            var where = Note("The game folder's SOUND files" + (s.IsSave ? " (saved games share them)" : "") + ". Nothing is written until Save; each file written keeps NAME.EXT.BKP, and the first version NAME.EXT.ORIG.");
            ToolTip.SetTip(where, Library.SoundFolder);
            head.Children.Add(where);
            var save = new Button { Content = "Save", FontWeight = FontWeight.SemiBold };
            ToolTip.SetTip(save, "Writes every changed file of this window");
            save.Click += (o, e) => SaveAll();
            var stop = new Button { Content = "Stop" };
            ToolTip.SetTip(stop, "Stops the sound playing");
            stop.Click += (o, e) => StopPlaying();
            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var buttons = Row(save, stop);
            buttons.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(buttons, Dock.Left);
            status.Margin = new Thickness(0);
            status.VerticalAlignment = VerticalAlignment.Center;
            bottom.Children.Add(buttons);
            bottom.Children.Add(status);
            DockPanel.SetDock(head, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            var dock = new DockPanel { Margin = new Thickness(16) };
            dock.Children.Add(head); dock.Children.Add(bottom); dock.Children.Add(Tabs);
            Content = dock;
            ManualWindow.Attach(this, "Sound and music");
            playTimer.Tick += (o, e) => { if (!AudioOutput.IsPlaying) { playTimer.Stop(); if (playing != null) { status.Text = playing + ": played."; playing = null; } } };
            Action<LevelModel> other = l => { if (main.ViewModel.Session != session) Close(); };
            main.ViewModel.LevelChanged += other;
            Closed += (o, e) => { main.ViewModel.LevelChanged -= other; StopPlaying(); };
            RefreshSpeech();
            RefreshEffects();
            status.Text = Library.EffectsProblem ?? "";
        }

        // ---- unsaved work

        /// <summary>Adds a tab whose unsaved work this window's HasChanges and SaveAll take in (the Music panel).</summary>
        public TabItem AddPanel(string header, Control content, Func<bool> hasChanges, Func<bool> saveAll)
        {
            var t = new TabItem { Header = header, Content = content };
            Tabs.Items.Add(t);
            if (hasChanges != null && saveAll != null) panels.Add(Tuple.Create(hasChanges, saveAll));
            return t;
        }

        /// <summary>Any unsaved file of the window (or of a panel added by AddPanel).</summary>
        public bool HasChanges { get { return Library.HasChanges || panels.Any(p => p.Item1()); } }

        /// <summary>Writes every changed file; false (with the reason in the status line) when one fails.</summary>
        public bool SaveAll()
        {
            bool ok = true;
            if (Library.HasChanges)
            {
                try
                {
                    var done = Library.SaveAll();
                    status.Text = "Saved: " + string.Join(", ", done) + ". The game reads them when it plays them.";
                }
                catch (Exception e) { status.Text = "Not saved: " + e.Message; ok = false; }
            }
            foreach (var p in panels)
                if (p.Item1()) ok &= p.Item2();
            RefreshSpeech();
            RefreshEffects();
            return ok;
        }

        // ---- playing

        /// <summary>Plays a file as it is now (unsaved changes included); returns the status line.</summary>
        public string Play(string name)
        {
            var b = Library.Bytes(name);
            if (b == null) return status.Text = name + " is not there.";
            VocFile v;
            try { v = VocFile.Parse(b); } catch (Exception e) { return status.Text = name + ": " + e.Message; }
            var samples = v.Samples8();
            string why = AudioOutput.Play(samples, v.SampleRate);
            if (why != null) return status.Text = why;
            playing = name;
            playTimer.Start();
            return status.Text = string.Format("Playing {0} ({1:0.0} s at {2} Hz).", name, (double)samples.Length / Math.Max(1, v.SampleRate), v.SampleRate);
        }

        public void StopPlaying() { AudioOutput.Stop(); playTimer.Stop(); playing = null; }

        // ---- export and import

        /// <summary>Writes a file's sound as an 8-bit mono WAV; returns null or the error.</summary>
        public string ExportWav(string name, string path)
        {
            var b = Library.Bytes(name);
            if (b == null) return status.Text = name + " is not there.";
            try { File.WriteAllBytes(path, SoundConversion.VocToWav(b)); }
            catch (Exception e) { return status.Text = "Not exported: " + e.Message; }
            status.Text = "Exported " + name + " to " + path + ".";
            return null;
        }

        /// <summary>Exports files as NAME.WAV into a folder; returns how many.</summary>
        public int ExportAll(IEnumerable<string> names, string folder)
        {
            int n = 0;
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var name in names)
                {
                    var b = Library.Bytes(name);
                    if (b == null) continue;
                    File.WriteAllBytes(Path.Combine(folder, Path.ChangeExtension(name, ".WAV")), SoundConversion.VocToWav(b));
                    n++;
                }
            }
            catch (Exception e) { status.Text = "Export stopped: " + e.Message; return n; }
            status.Text = string.Format("Exported {0} file{1} to {2}.", n, n == 1 ? "" : "s", folder);
            return n;
        }

        /// <summary>
        /// Converts a WAV file for a .VOC of the given use and holds it as the file's new bytes
        /// (written on Save). The result says how it went; the status line too.
        /// </summary>
        public SoundConvertResult Import(string name, SoundUse use, string wavPath, SoundConvertOptions opt)
        {
            SoundConvertResult r;
            try { r = SoundConversion.Convert(WavFile.Load(wavPath), use, opt); }
            catch (Exception e) { r = new SoundConvertResult { Problem = Path.GetFileName(wavPath) + ": " + e.Message }; }
            if (!r.Ok) { status.Text = name + " not imported: " + r.Problem; return r; }
            Library.Put(name, r.Voc);
            status.Text = name + ": " + r.Summary + " Save to write it.";
            RefreshSpeech();
            RefreshEffects();
            return r;
        }

        public SoundConvertResult ImportSpeech(int number, string wavPath) { return Import(Library.SpeechName(number), Library.SpeechUse, wavPath, SpeechOptions); }
        public SoundConvertResult ImportEffect(int effect, string wavPath) { return Import(SoundLibrary.EffectName(effect), SoundUse.Uw2Effect, wavPath, EffectOptions); }
        public SoundConvertResult ImportTrapSound(int owner, string wavPath) { return Import(SoundLibrary.TrapName(owner), SoundUse.Uw2Trap, wavPath, EffectOptions); }

        /// <summary>The file back to the original (written on Save).</summary>
        public void Revert(string name)
        {
            Library.Revert(name);
            status.Text = name + ": " + (Library.Exists(name) ? "back to the original" : "removed (the editor added it)") + ". Save to write it.";
            RefreshSpeech();
            RefreshEffects();
        }

        async Task ImportAsk(string name, SoundUse use, SoundConvertOptions opt)
        {
            string path = await OpenWav();
            if (path == null) return;
            var r = Import(name, use, path, opt);
            if (r.Ok || !r.TooLong || opt.OverLimit != OverLimit.Refuse) { if (!r.Ok) await MessageWindow.Show(this, "Not imported", status.Text); return; }
            string a = await MessageWindow.Ask(this, "Too long", r.Problem, "Shorten it", "Lower the rate", "Cancel");
            if (a == "Cancel") return;
            var o2 = new SoundConvertOptions { TimeConstant = opt.TimeConstant, Normalise = opt.Normalise, TrimSilence = opt.TrimSilence, Dither = opt.Dither, Seed = opt.Seed, OverLimit = a == "Shorten it" ? OverLimit.Shorten : OverLimit.LowerRate };
            r = Import(name, use, path, o2);
            if (!r.Ok) await MessageWindow.Show(this, "Not imported", status.Text);
        }

        async Task<string> OpenWav()
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a WAV file", AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("WAV sound") { Patterns = new[] { "*.wav", "*.WAV" } } },
                });
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            }
            catch (Exception e) { status.Text = e.Message; return null; }
        }

        async Task ExportAsk(string name)
        {
            if (!Library.Exists(name)) { status.Text = name + " is not there."; return; }
            try
            {
                var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export " + name, SuggestedFileName = Path.ChangeExtension(name, ".wav"), DefaultExtension = "wav",
                    FileTypeChoices = new[] { new FilePickerFileType("WAV sound") { Patterns = new[] { "*.wav" } } },
                });
                string p = f?.TryGetLocalPath();
                if (p != null) ExportWav(name, p);
            }
            catch (Exception e) { status.Text = e.Message; }
        }

        async Task ExportAllAsk(IEnumerable<string> names)
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Export into the folder", AllowMultiple = false });
                string p = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                if (p != null) ExportAll(names, p);
            }
            catch (Exception e) { status.Text = e.Message; }
        }

        // ---- the Speech tab

        Control SpeechTab()
        {
            var use = Library.SpeechUse;
            string intro = session.IsUw2
                ? "Speech of the cutscenes (BSPnn.VOC): the cutscene's say opcode starts the file and shows its subtitle (STRINGS block 0xC00 + cutscene) when speech is off. Streamed, so any length; the next say cuts it. The introduction has no speech."
                : "The introduction's speech (cutscene 0), one nn.VOC a subtitle line (STRINGS block 0xC00). The game reads " + SoundLimits.Describe(use) + " of each file; the next line's say opcode cuts it. Conversations, the dreams and the ending have no speech.";
            var cols = new List<Col<SpeechRow>>
            {
                new Col<SpeechRow> { Header = "File", Width = 90, Text = r => r.Name },
                new Col<SpeechRow> { Header = "Cutscene", Width = 70, Right = true, Text = r => r.Item.CutsceneText },
                new Col<SpeechRow> { Header = "Line", Width = 200, Fill = true, Text = r => r.Line },
                new Col<SpeechRow> { Header = "Length", Width = 62, Right = true, Text = r => r.Info.Exists ? r.Info.Seconds.ToString("0.0") + " s" : "" },
                new Col<SpeechRow> { Header = "Rate", Width = 70, Right = true, Text = r => r.Info.Exists ? r.Info.Rate + " Hz" : "" },
                new Col<SpeechRow> { Header = "Bytes", Width = 140, Right = true, Text = r => r.Info.SizeText },
                new Col<SpeechRow> { Header = "", Width = 90, Text = r => r.State },
            };
            var table = Table(speechList, cols);
            speechList.SelectionChanged += (o, e) => SpeechSelected();
            var play = B("Play", () => { if (speechList.SelectedItem is SpeechRow r) Play(r.Name); });
            var export = B("Export WAV…", async () => { if (speechList.SelectedItem is SpeechRow r) await ExportAsk(r.Name); });
            var all = B("Export all…", async () => await ExportAllAsk(Rows<SpeechRow>(speechList).Where(r => r.Info.Exists).Select(r => r.Name).ToList()));
            var import = B("Import WAV…", async () => { if (speechList.SelectedItem is SpeechRow r) await ImportAsk(r.Name, use, SpeechOptions); });
            var revert = B("Revert to the original", () => { if (speechList.SelectedItem is SpeechRow r) Revert(r.Name); });
            ToolTip.SetTip(revert, "Back to the file as shipped (its .ORIG copy), or drop the unsaved import");
            var buttons = Row(play, export, all, import, revert);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto"), Margin = new Thickness(4, 8) };
            var introText = Note(intro);
            introText.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetRow(table, 1); Grid.SetRow(speechDetail, 2); Grid.SetRow(buttons, 3);
            var settings = ImportSettings(use, SpeechOptions);
            Grid.SetRow(settings, 4);
            speechDetail.Margin = new Thickness(0, 6, 0, 0);
            buttons.Margin = new Thickness(0, 6, 0, 0);
            grid.Children.Add(introText); grid.Children.Add(table); grid.Children.Add(speechDetail); grid.Children.Add(buttons); grid.Children.Add(settings);
            return grid;
        }

        void RefreshSpeech()
        {
            int sel = speechList.SelectedIndex;
            var rows = new List<SpeechRow>();
            foreach (var it in Library.Speech())
            {
                var r = new SpeechRow { Item = it, Info = Library.Info(it.Name, Library.SpeechUse) };
                r.Line = it.Lines.Count == 0 ? "(no cutscene line plays it)" : string.Join(" / ", it.Lines.Select(LineText));
                r.State = StateOf(it.Name, r.Info);
                rows.Add(r);
            }
            speechList.ItemsSource = rows;
            if (rows.Count > 0) speechList.SelectedIndex = Math.Max(0, Math.Min(sel, rows.Count - 1));
            SpeechSelected();
        }

        string LineText(SpeechLine l)
        {
            string t = Clean(session.Strings.Get(l.StringBlock, l.String));
            foreach (var f in l.SilentFollowers) t += " " + Clean(session.Strings.Get(l.StringBlock, f));
            return t;
        }

        static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

        void SpeechSelected()
        {
            if (!(speechList.SelectedItem is SpeechRow r)) { speechDetail.Text = ""; return; }
            var parts = new List<string>();
            foreach (var l in r.Item.Lines)
                parts.Add(string.Format("Cutscene {0} ({1}), string {2} of block 0x{3:X}{4}: \"{5}\"", l.Cutscene, l.Script, l.String, l.StringBlock,
                    l.SilentFollowers.Count > 0 ? " (and " + string.Join(", ", l.SilentFollowers) + ", shown only without speech)" : "", LineText(l)));
            if (r.Item.Lines.Count == 0) parts.Add("No cutscene's say opcode plays this file.");
            if (!r.Info.Exists) parts.Add(r.Name + " is not there: the game shows the subtitle instead.");
            else parts.Add(string.Format("{0}: {1:0.00} s at {2} Hz, {3:N0} bytes; the game takes {4}.", r.Name, r.Info.Seconds, r.Info.Rate, r.Info.FileBytes, SoundLimits.Describe(Library.SpeechUse)));
            if (r.Info.Problem != null) parts.Add("Problem: " + r.Info.Problem + ".");
            speechDetail.Text = string.Join("\n", parts);
        }

        string StateOf(string name, VocInfo info)
        {
            string s = Library.IsChanged(name) ? (Library.Exists(name) ? "unsaved" : "removed") : Library.HasOriginal(name) ? (Library.Original(name) == null ? "added" : "replaced") : "";
            if (info != null && info.Problem != null) s = (s.Length > 0 ? s + ", " : "") + "problem";
            return s;
        }

        // ---- the Effects tab

        Control EffectsTab()
        {
            string intro = session.IsUw2
                ? "SOUNDS.DAT's " + SoundsDat.Uw2Max + " effects (the game's table holds no more). A digital effect plays SOUND\\SPnn.VOC (nn its number) on the Sound Blaster, else a note on the music card. Digitised files must be " + SoundLimits.Describe(SoundUse.Uw2Effect) + ": they share a cache of four 16 KB pages. Effects 100–163 are the trap sounds UW00–UW63.VOC, played by special-effects traps (0x184) of quality 2 and owner nn."
                : "SOUNDS.DAT's " + SoundsDat.Uw1Max + " effects (the game's table holds no more). Every effect is a note played on the music card (FM or MT-32) with a timbre of bank 1: Ultima Underworld has no digitised effects.";
            bool uw2 = session.IsUw2;
            var cols = new List<Col<EffectRow>>
            {
                new Col<EffectRow> { Header = "#", Width = 44, Right = true, Text = r => r.Number.ToString() },
                new Col<EffectRow> { Header = "What plays it", Width = 200, Fill = true, Text = r => r.What },
                new Col<EffectRow> { Header = "Patch", Width = 52, Right = true, Text = r => r.Effect == null ? "" : r.Effect.Patch.ToString() },
                new Col<EffectRow> { Header = "Note", Width = 48, Right = true, Text = r => r.Effect == null ? "" : r.Effect.Note.ToString() },
                new Col<EffectRow> { Header = "Vol", Width = 44, Right = true, Text = r => r.Effect == null ? "" : r.Effect.Volume.ToString() },
                new Col<EffectRow> { Header = "Length", Width = 60, Right = true, Text = r => r.Effect == null ? "" : r.Effect.Length.ToString() },
            };
            if (uw2)
            {
                cols.Add(new Col<EffectRow> { Header = "Prio", Width = 50, Right = true, Text = r => r.Effect == null ? "" : r.Effect.Priority.ToString() });
                cols.Add(new Col<EffectRow> { Header = "Plays", Width = 84, Text = r => r.File ?? "MIDI" });
                cols.Add(new Col<EffectRow> { Header = "Bytes", Width = 116, Right = true, Text = r => r.File == null ? "" : r.Info.SizeText });
            }
            cols.Add(new Col<EffectRow> { Header = "", Width = 76, Text = r => r.State });
            var table = Table(effectList, cols);
            effectList.SelectionChanged += (o, e) => EffectSelected();
            var detailView = new ScrollViewer { Content = effectDetail, Margin = new Thickness(0, 2, 0, 0), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, MinHeight = 90 };
            var revertDat = B("Revert SOUNDS.DAT", () => { Library.RevertEffects(); status.Text = "SOUNDS.DAT: back to the file as saved."; RefreshEffects(); });
            var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
            var rowButtons = Row(revertDat);
            if (uw2)
            {
                var number = new NumericUpDown { Minimum = 0, Maximum = 63, Increment = 1, FormatString = "00", Value = FirstFreeTrap(), Width = 110 };
                var add = B("Add a trap sound from WAV…", async () =>
                {
                    int n = (int)(number.Value ?? 0);
                    await ImportAsk(SoundLibrary.TrapName(n), SoundUse.Uw2Trap, EffectOptions);
                });
                ToolTip.SetTip(add, "A new SOUND\\UWnn.VOC: a special-effects trap (0x184) of quality 2 and owner nn plays it");
                rowButtons.Children.Add(new TextBlock { Text = "UW", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
                rowButtons.Children.Add(number);
                rowButtons.Children.Add(new TextBlock { Text = ".VOC", VerticalAlignment = VerticalAlignment.Center });
                rowButtons.Children.Add(add);
                rowButtons.Children.Add(B("Export all…", async () => await ExportAllAsk(Rows<EffectRow>(effectList).Where(r => r.File != null && r.Info.Exists).Select(r => r.File).ToList())));
            }
            bottom.Children.Add(rowButtons);
            if (uw2) bottom.Children.Add(ImportSettings(SoundUse.Uw2Effect, EffectOptions));
            // the list and the selected effect share the height; the splitter moves the line between them
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,5*,Auto,4*,Auto"), Margin = new Thickness(4, 8) };
            var introText = Note(intro);
            introText.Margin = new Thickness(0, 0, 0, 6);
            var split = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
            table.MinHeight = 90;
            Grid.SetRow(table, 1); Grid.SetRow(split, 2); Grid.SetRow(detailView, 3); Grid.SetRow(bottom, 4);
            grid.Children.Add(introText); grid.Children.Add(table); grid.Children.Add(split); grid.Children.Add(detailView); grid.Children.Add(bottom);
            return grid;
        }

        decimal FirstFreeTrap()
        {
            var have = new HashSet<int>(Library.TrapFiles());
            for (int i = 0; i < 64; i++) if (!have.Contains(i)) return i;
            return 63;
        }

        /// <summary>The special-effects traps of the levels as edited.</summary>
        public List<TrapSound> Traps
        {
            get
            {
                if (traps == null)
                {
                    var edits = main.ViewModel.Edits;
                    traps = SoundLibrary.FindTraps(session, l => edits != null && main.ViewModel.Session == session ? edits.Model(l) : session.LoadLevel(l));
                }
                return traps;
            }
        }

        void RefreshEffects()
        {
            int sel = effectList.SelectedIndex;
            var rows = new List<EffectRow>();
            var fx = Library.Effects;
            savedEffects = Library.EffectsChanged ? SavedEffects() : null;
            if (fx != null)
                for (int i = 0; i < fx.Effects.Count; i++)
                {
                    var e = fx.Effects[i];
                    var r = new EffectRow { Number = i, Effect = e, What = SoundEffectNames.Get(session.IsUw2, i).What };
                    if (e.Digital) { r.File = SoundLibrary.EffectName(i); r.Info = Library.Info(r.File, SoundUse.Uw2Effect); }
                    r.State = EffectState(r);
                    rows.Add(r);
                }
            if (session.IsUw2)
            {
                var owners = new SortedSet<int>(Library.TrapFiles());
                foreach (var t in Traps) owners.Add(t.Owner);
                foreach (int n in owners)
                {
                    var r = new EffectRow { Number = 100 + n, IsTrap = true, File = SoundLibrary.TrapName(n), Traps = Traps.Where(t => t.Owner == n).ToList() };
                    r.Info = Library.Info(r.File, SoundUse.Uw2Trap);
                    r.What = r.Traps.Count == 0 ? "Trap sound: no special-effects trap plays it" : string.Format("Trap sound: {0} trap{1} play it", r.Traps.Count, r.Traps.Count == 1 ? "" : "s");
                    r.State = StateOf(r.File, r.Info);
                    if (!r.Info.Exists) r.State = "missing";
                    rows.Add(r);
                }
            }
            effectList.ItemsSource = rows;
            if (rows.Count > 0) effectList.SelectedIndex = Math.Max(0, Math.Min(sel, rows.Count - 1));
            EffectSelected();
        }

        string EffectState(EffectRow r)
        {
            var parts = new List<string>();
            if (r.Effect != null && savedEffects != null)
            {
                var saved = r.Number < savedEffects.Effects.Count ? savedEffects.Effects[r.Number].Bytes : null;
                if (saved == null || !saved.SequenceEqual(r.Effect.Bytes)) parts.Add("unsaved");
            }
            if (r.File != null)
            {
                string s = StateOf(r.File, r.Info);
                if (!r.Info.Exists) s = "no file";
                if (s.Length > 0 && !parts.Contains(s)) parts.Add(s);
            }
            return string.Join(", ", parts);
        }

        SoundsDat savedEffects;

        /// <summary>SOUNDS.DAT as on disk (to mark the unsaved rows), or null.</summary>
        SoundsDat SavedEffects()
        {
            try
            {
                string p = Library.DiskPath(SoundLibrary.EffectsFile);
                return p == null ? null : SoundsDat.Parse(File.ReadAllBytes(p), session.IsUw2);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Changes a field of an effect (patch, note, volume, length, priority) in SOUNDS.DAT.</summary>
        public void SetEffect(int n, string field, int value)
        {
            var e = Library.Effects.Effects[n];
            switch (field)
            {
                case "Patch": e.Patch = value; break;
                case "Note": e.Note = value; break;
                case "Volume": e.Volume = value; break;
                case "Length": e.Length = value; break;
                case "Priority": e.Priority = value; break;
                default: throw new ArgumentException("No field " + field);
            }
            status.Text = string.Format("Effect {0}: {1} {2}. Save to write SOUNDS.DAT.", n, field.ToLowerInvariant(), value);
            UpdateEffectRow(n);
        }

        /// <summary>UW2: makes effect n digital (it plays SPnn.VOC) or a MIDI note again.</summary>
        public void SetDigital(int n, bool on)
        {
            var e = Library.Effects.Effects[n];
            e.Digital = on;
            string file = SoundLibrary.EffectName(n);
            status.Text = on
                ? "Effect " + n + " now plays " + file + (Library.Exists(file) ? "." : ", which is not there: import one (until then the game plays the MIDI note).") + " Save to write SOUNDS.DAT."
                : "Effect " + n + " plays its MIDI note again. Save to write SOUNDS.DAT.";
            RefreshEffects();
        }

        void UpdateEffectRow(int n)
        {
            // the row's texts change: rebuild the list, keeping the selection
            filling = true;
            try { RefreshEffects(); } finally { filling = false; }
        }

        void EffectSelected()
        {
            if (filling) return;
            effectDetail.Children.Clear();
            if (!(effectList.SelectedItem is EffectRow r)) return;
            var use = SoundEffectNames.Get(session.IsUw2, r.Number);
            var what = new TextBlock { Text = string.Format("Effect {0}: {1}.", r.Number, use.What), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
            effectDetail.Children.Add(what);
            if (r.Effect != null)
            {
                int n = r.Number;
                var e = r.Effect;
                var fields = Row(Num("Patch", e.Patch, 0, 255, v => SetEffect(n, "Patch", v)), Num("Note", e.Note, 0, 127, v => SetEffect(n, "Note", v)),
                    Num("Volume", e.Volume, 0, 127, v => SetEffect(n, "Volume", v)), Num("Length", e.Length, 0, 0xFFFF, v => SetEffect(n, "Length", v)));
                if (session.IsUw2)
                {
                    fields.Children.Add(Num("Priority", e.Priority, 0, 255 + 255 * 200, v => SetEffect(n, "Priority", v)));
                    var dig = new CheckBox { Content = "Digital (plays " + SoundLibrary.EffectName(n) + ")", IsChecked = e.Digital };
                    dig.IsCheckedChanged += (o, ev) => { if (dig.IsChecked == true != e.Digital) SetDigital(n, dig.IsChecked == true); };
                    fields.Children.Add(dig);
                }
                effectDetail.Children.Add(fields);
                if (!e.Digital)
                    effectDetail.Children.Add(Note("Plays on the music card (FM or MT-32): the editor can't play these. The patch is a timbre of bank 1 of UW.AD (FM) and UW.MT (MT-32); the length is in 1/256 seconds (" + e.Length + ": " + (e.Length / 256.0).ToString("0.00") + " s; sound/SOUND.C fx_play)." +
                        (session.IsUw2 ? " Tick Digital to play a .VOC instead." : "")));
                if (!e.Digital && session.IsUw2 && Library.Exists(SoundLibrary.EffectName(n)))
                {
                    string spare = SoundLibrary.EffectName(n);
                    effectDetail.Children.Add(Note(spare + " is there, but the game plays it only when Digital is ticked."));
                    effectDetail.Children.Add(Row(B("Play " + spare, () => Play(spare)), B("Export WAV…", async () => await ExportAsk(spare))));
                }
                else if (e.Digital && e.Length >= 5000)
                    effectDetail.Children.Add(Note("A digital effect of length 5000 or more loops (length / 64 − 1) times (uw2/src/sound/SOUND.C digi_fx_play)."));
            }
            if (r.File != null)
            {
                string f = r.File;
                var su = r.IsTrap ? SoundUse.Uw2Trap : SoundUse.Uw2Effect;
                effectDetail.Children.Add(Note(r.Info.Exists
                    ? string.Format("{0}: {1:0.00} s at {2} Hz, {3:N0} bytes ({4}).{5}", f, r.Info.Seconds, r.Info.Rate, r.Info.FileBytes, SoundLimits.Describe(su), r.Info.Problem != null ? " Problem: " + r.Info.Problem + "." : "")
                    : f + " is not there" + (r.IsTrap ? ": the trap plays nothing." : ": the game plays the MIDI note instead.") + " Import a WAV file to add it."));
                effectDetail.Children.Add(Row(B("Play", () => Play(f)), B("Export WAV…", async () => await ExportAsk(f)),
                    B("Import WAV…", async () => await ImportAsk(f, su, EffectOptions)), B("Revert to the original", () => Revert(f))));
            }
            var notes = new List<string>();
            if (!string.IsNullOrEmpty(use.Source)) notes.Add("From the calls in " + (session.IsUw2 ? "uw2" : "uw1") + "/src: " + use.Source + ".");
            var critters = SoundEffectNames.ForCreatures(session, tables, r.Number);
            if (critters.Count > 0) notes.Add("Creatures: " + string.Join(", ", critters) + ".");
            if (r.IsTrap)
            {
                if (r.Traps.Count == 0) notes.Add("No special-effects trap of quality 2 and owner " + (r.Number - 100) + " is in the levels: add one, set off by a trigger, to play it.");
                else notes.Add("Played by: " + string.Join("; ", r.Traps.Select(t => t.ToString())) + ".");
            }
            if (notes.Count > 0) effectDetail.Children.Add(Note(string.Join("\n", notes)));
        }

        // ---- import settings

        Control ImportSettings(SoundUse use, SoundConvertOptions opt)
        {
            int game = SoundLimits.GameTimeConstant(use);
            var rates = new List<Tuple<string, int>>
            {
                Tuple.Create("The game's rate (" + VocFile.RateOf(game) + " Hz)", -1),
                Tuple.Create("8000 Hz", VocFile.TimeConstantFor(8000)),
                Tuple.Create("11025 Hz", VocFile.TimeConstantFor(11025)),
                Tuple.Create("16000 Hz", VocFile.TimeConstantFor(16000)),
                Tuple.Create("22050 Hz", VocFile.TimeConstantFor(22050)),
            };
            var rate = new ComboBox { ItemsSource = rates.Select(r => r.Item1 == rates[0].Item1 ? r.Item1 : r.Item1 + " (" + VocFile.RateOf(r.Item2) + ")").ToList(), SelectedIndex = 0, Width = 230 };
            rate.SelectionChanged += (o, e) => opt.TimeConstant = rates[Math.Max(0, rate.SelectedIndex)].Item2;
            ToolTip.SetTip(rate, "The rate the file is written at. Lower rates fit longer clips; the Sound Blaster plays up to about 22 kHz in the games' mode.");
            var over = new ComboBox { ItemsSource = new[] { "Ask", "Shorten it", "Lower the rate" }, SelectedIndex = (int)opt.OverLimit, Width = 140 };
            over.SelectionChanged += (o, e) => opt.OverLimit = (OverLimit)Math.Max(0, over.SelectedIndex);
            var norm = new CheckBox { Content = "Normalise", IsChecked = opt.Normalise };
            norm.IsCheckedChanged += (o, e) => opt.Normalise = norm.IsChecked == true;
            var trim = new CheckBox { Content = "Trim silence", IsChecked = opt.TrimSilence };
            trim.IsCheckedChanged += (o, e) => opt.TrimSilence = trim.IsChecked == true;
            var p = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(new TextBlock { Text = "Import at", VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(rate);
            p.Children.Add(norm);
            p.Children.Add(trim);
            p.Children.Add(new TextBlock { Text = SoundLimits.HasLimit(use) ? "Over the limit:" : "", VerticalAlignment = VerticalAlignment.Center });
            if (SoundLimits.HasLimit(use)) p.Children.Add(over);
            ToolTip.SetTip(p, "WAV files of any rate, 8 to 32 bits, mono or stereo, are mixed to mono, resampled (windowed sinc), dithered to 8 bits and written as one plain block, the shape the game reads.");
            return p;
        }

        // ---- small builders

        sealed class Col<T>
        {
            public string Header;
            public double Width;
            public bool Fill;
            public bool Right;
            public Func<T, string> Text;
        }

        static IEnumerable<T> Rows<T>(ListBox l) { return (l.ItemsSource as IEnumerable<T>) ?? Enumerable.Empty<T>(); }

        Control Table<T>(ListBox list, List<Col<T>> cols)
        {
            Func<Grid> grid = () =>
            {
                var g = new Grid();
                foreach (var c in cols) g.ColumnDefinitions.Add(c.Fill ? new ColumnDefinition(1, GridUnitType.Star) { MinWidth = c.Width } : new ColumnDefinition(c.Width, GridUnitType.Pixel));
                return g;
            };
            var header = grid();
            header.Margin = new Thickness(8, 0, 8, 2);
            for (int i = 0; i < cols.Count; i++)
            {
                var t = new TextBlock { Text = cols[i].Header, FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(4, 0), TextAlignment = cols[i].Right ? TextAlignment.Right : TextAlignment.Left };
                Grid.SetColumn(t, i);
                header.Children.Add(t);
            }
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
            {
                Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(8, 2)), new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch), new Setter(ListBoxItem.MinHeightProperty, 0.0) },
            });
            list.Bind(ListBox.BackgroundProperty, list.GetResourceObservable("Panel"));
            list.ItemTemplate = new FuncDataTemplate<T>((r, ns) =>
            {
                var g = grid();
                if (r == null) return g;
                for (int i = 0; i < cols.Count; i++)
                {
                    string text = cols[i].Text(r) ?? "";
                    var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(4, 1), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = cols[i].Right ? TextAlignment.Right : TextAlignment.Left };
                    if (cols[i].Fill && text.Length > 0) ToolTip.SetTip(t, text);
                    Grid.SetColumn(t, i);
                    g.Children.Add(t);
                }
                return g;
            });
            var d = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            d.Children.Add(header);
            d.Children.Add(list);
            return d;
        }

        Control Num(string label, int value, int min, int max, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Value = value, Width = 76, ShowButtonSpinner = false };
            n.ValueChanged += (o, e) =>
            {
                if (n.Value == null) return;
                int v = (int)n.Value.Value;
                try { set(v); } catch (Exception ex) { status.Text = ex.Message; }
            };
            var p = new StackPanel { Spacing = 2 };
            p.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.8 });
            p.Children.Add(n);
            return p;
        }

        static Button B(string text, Action a) { var b = new Button { Content = text }; b.Click += (o, e) => a(); return b; }
        static Button B(string text, Func<Task> a) { var b = new Button { Content = text }; b.Click += async (o, e) => await a(); return b; }
        static TextBlock Note(string t) { return new TextBlock { Text = t, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }; }
        static WrapPanel Row(params Control[] c) { var p = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 }; foreach (var x in c) p.Children.Add(x); return p; }
    }
}
