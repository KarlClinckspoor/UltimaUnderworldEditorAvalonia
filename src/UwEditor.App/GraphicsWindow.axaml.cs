using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UwEditor.Core;
using UwEditor.Core.Graphics;

namespace UwEditor.App
{
    /// <summary>
    /// Tools ▸ Graphics: the pictures in DATA (.GR, .TR, .BYT, BYT.ARK), the critters'
    /// animation frames (CRIT) and the cutscenes (CUTS), each viewable, exportable as PNG
    /// and replaceable from a PNG of the same size in the picture's own colours.
    /// </summary>
    public partial class GraphicsWindow : Window
    {
        public GraphicsViewModel Model { get; private set; }
        readonly DispatcherTimer critterTimer = new DispatcherTimer(), cutTimer = new DispatcherTimer();

        public GraphicsWindow() { InitializeComponent(); }

        public GraphicsWindow(GameSession session)
        {
            InitializeComponent();
            ManualWindow.Attach(this, "Graphics");
            Model = new GraphicsViewModel(session);
            DataContext = Model;
            Title = "Graphics — " + session.Title;
            Model.Status = Model.Files.Count + " picture files in DATA.";
            Model.FileName = Model.Files.FirstOrDefault(f => f.ToUpperInvariant() == "OBJECTS.GR") ?? Model.Files.FirstOrDefault();
            Tabs.SelectionChanged += (s, e) => OnTab();
            critterTimer.Interval = TimeSpan.FromMilliseconds(160);
            critterTimer.Tick += (s, e) => Model.NextFrame();
            cutTimer.Tick += (s, e) => Model.NextCutFrame();
            Closed += (s, e) => { critterTimer.Stop(); cutTimer.Stop(); };
            // the palette box loses its choice when its list is refilled before it is shown: give it back
            Model.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(GraphicsViewModel.Critter))
                    Dispatcher.UIThread.Post(() => AuxBox.SelectedIndex = Model.AuxPalette, DispatcherPriority.Background);
            };
        }

        /// <summary>Shows a tab (0 pictures, 1 critters, 2 cutscenes), loading what it shows the first time.</summary>
        public void ShowTab(int i) { Tabs.SelectedIndex = i; OnTab(); }

        void OnTab()
        {
            if (Tabs.SelectedIndex == 1 && Model.Critters.Count == 0)
            {
                Model.LoadCritters();
                Model.Critter = Model.Critters.FirstOrDefault();
                Model.Status = Model.Critters.Count + " critter art files in CRIT.";
            }
            if (Tabs.SelectedIndex == 2 && Model.Cutscenes.Count == 0)
            {
                Model.LoadCutscenes();
                Model.CutsceneItem = Model.Cutscenes.FirstOrDefault(c => c.Animations.Count > 0);
                Model.Status = Model.Cutscenes.Count + " cutscenes in CUTS.";
            }
        }

        // ---- files

        async Task<string> SavePath(string suggested)
        {
            try
            {
                var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = suggested, DefaultExtension = "png",
                    FileTypeChoices = new[] { new FilePickerFileType("PNG picture") { Patterns = new[] { "*.png" } } },
                });
                return f?.TryGetLocalPath();
            }
            catch (Exception ex) { Model.Status = ex.Message; return null; }
        }

        async Task<byte[]> OpenPng()
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("PNG picture") { Patterns = new[] { "*.png" } } },
                });
                string p = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                return p == null ? null : File.ReadAllBytes(p);
            }
            catch (Exception ex) { Model.Status = ex.Message; return null; }
        }

        async Task Refused(ImportResult r)
        {
            if (r != null && !r.Ok) await MessageWindow.Show(this, "Not imported", r.Summary);
        }

        // ---- pictures

        /// <summary>The pixel editor on the picture shown (null when none is).</summary>
        public PixelEditorWindow EditPicture()
        {
            if (Model.Picture == null || Model.File == null) return null;
            var w = new PixelEditorWindow(Model.Picture, Model.File.Name + " picture " + (Model.PictureThumb?.Index ?? 0), Model.File.Describe(Model.PictureThumb?.Index ?? 0, Model.Picture), Model.ReplacePicturePixels,
                () => { try { Model.SavePictures(); return null; } catch (Exception ex) { return ex.Message; } }, Model.File.Name);
            w.Show(this);
            return w;
        }

        public PixelEditorWindow EditFrame()
        {
            if (Model.CritterImage == null) return null;
            var w = new PixelEditorWindow(Model.CritterImage, (Model.Critter?.ToString() ?? "critter") + ", frame " + Model.FrameIndex, "A critter frame: its auxiliary palette's colours. The page must stay under 0x7FFF bytes.", Model.ReplaceFramePixels,
                () => { try { Model.SaveCritter(); return null; } catch (Exception ex) { return ex.Message; } }, "the critter's pages");
            w.Show(this);
            return w;
        }

        public PixelEditorWindow EditCutFrame()
        {
            if (Model.CutImage == null || Model.Lpf == null) return null;
            var w = new PixelEditorWindow(Model.CutImage, Model.Lpf.Name + ", frame " + (Model.CutFrame + 1), "A cutscene frame: the animation's palette. The next frame's change is encoded again.", Model.ReplaceCutPixels,
                () => { try { Model.SaveCutscene(); return null; } catch (Exception ex) { return ex.Message; } }, Model.Lpf.Name);
            w.Show(this);
            return w;
        }

        void OnEditPicture(object sender, RoutedEventArgs e) { EditPicture(); }
        void OnEditFrame(object sender, RoutedEventArgs e) { EditFrame(); }
        void OnEditCutFrame(object sender, RoutedEventArgs e) { EditCutFrame(); }

        async void OnExportPicture(object sender, RoutedEventArgs e)
        {
            if (Model.Picture == null) return;
            string p = await SavePath(Path.GetFileNameWithoutExtension(Model.File.Name) + "_" + Model.PictureThumb.Index + ".png");
            if (p != null) ExportPicture(p);
        }

        public void ExportPicture(string path)
        {
            File.WriteAllBytes(path, Model.Picture.ToPng());
            Model.Status = "Exported to " + path + ".";
        }

        async void OnImportPicture(object sender, RoutedEventArgs e)
        {
            var b = await OpenPng();
            if (b != null) await Refused(Model.ImportPicture(b));
        }

        void OnRevertPicture(object sender, RoutedEventArgs e) { Model.RevertPicture(); }
        void OnSavePictures(object sender, RoutedEventArgs e) { Model.SavePictures(); }

        // ---- critters

        void OnPlayCritter(object sender, RoutedEventArgs e)
        {
            if (critterTimer.IsEnabled) { critterTimer.Stop(); PlayCritterButton.Content = "Play"; }
            else { critterTimer.Start(); PlayCritterButton.Content = "Stop"; }
        }

        async void OnExportFrame(object sender, RoutedEventArgs e)
        {
            if (Model.CritterImage == null) return;
            var f = Model.Animation.Frames[Model.FrameIndex];
            string p = await SavePath(f.Item1.Name.Replace('.', '_') + "_" + f.Item2 + ".png");
            if (p != null) { File.WriteAllBytes(p, Model.CritterImage.ToPng()); Model.Status = "Exported to " + p + "."; }
        }

        async void OnImportFrame(object sender, RoutedEventArgs e)
        {
            var b = await OpenPng();
            if (b != null) await Refused(Model.ImportFrame(b));
        }

        void OnSaveCritter(object sender, RoutedEventArgs e) { Model.SaveCritter(); }

        // ---- cutscenes

        void OnPlayCut(object sender, RoutedEventArgs e)
        {
            if (cutTimer.IsEnabled) { cutTimer.Stop(); PlayCutButton.Content = "Play"; }
            else { cutTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, Model.CutRate)); cutTimer.Start(); PlayCutButton.Content = "Stop"; }
        }

        async void OnExportCutFrame(object sender, RoutedEventArgs e)
        {
            if (Model.CutImage == null) return;
            string p = await SavePath(Model.Lpf.Name.Replace('.', '_') + "_" + (Model.CutFrame + 1).ToString("000") + ".png");
            if (p != null) { File.WriteAllBytes(p, Model.CutImage.ToPng()); Model.Status = "Exported to " + p + "."; }
        }

        async void OnExportAllCutFrames(object sender, RoutedEventArgs e)
        {
            if (Model.Lpf == null) return;
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
                string p = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
                if (p != null) Model.ExportAllCutFrames(p);
            }
            catch (Exception ex) { Model.Status = ex.Message; }
        }

        async void OnImportCutFrame(object sender, RoutedEventArgs e)
        {
            var b = await OpenPng();
            if (b != null) await Refused(Model.ImportCutFrame(b));
        }

        void OnSaveCutscene(object sender, RoutedEventArgs e) { Model.SaveCutscene(); }
    }
}
