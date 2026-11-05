using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using Underworld.Mods;
using UwEditor.Core.Mods;

namespace UwEditor.App
{
    public sealed class ModsViewModel : Observable
    {
        public GameSession Session { get; private set; }
        string name, id, version = "1.0.0", authors = "", description = "", referenceFolder = "", preview = "", status = "", checks = "", loadedPath = "";
        bool useOriginals = true, loadedFits;
        public ModPackage Package { get; private set; }
        public ModPackage Loaded { get; private set; }

        public ModsViewModel(GameSession s)
        {
            Session = s;
            name = "My " + (s.IsUw2 ? "UW2" : "UW1") + " mod";
            id = "my." + (s.IsUw2 ? "uw2" : "uw1") + ".mod";
        }

        public string Name { get { return name; } set { Set(ref name, value ?? ""); } }
        public string Id { get { return id; } set { Set(ref id, value ?? ""); } }
        public string Version { get { return version; } set { Set(ref version, value ?? ""); } }
        public string Authors { get { return authors; } set { Set(ref authors, value ?? ""); } }
        public string Description { get { return description; } set { Set(ref description, value ?? ""); } }
        public bool UseOriginals { get { return useOriginals; } set { if (Set(ref useOriginals, value)) Raise(nameof(UseFolder)); } }
        public bool UseFolder { get { return !useOriginals; } set { UseOriginals = !value; } }
        public string ReferenceFolder { get { return referenceFolder; } set { Set(ref referenceFolder, value ?? ""); } }
        public string Preview { get { return preview; } set { Set(ref preview, value); } }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public string Checks { get { return checks; } set { Set(ref checks, value); } }
        public string LoadedPath { get { return loadedPath; } set { Set(ref loadedPath, value); } }
        public bool HasPackage { get { return Package != null && Package.Files.Count > 0; } }
        public bool CanApply { get { return Loaded != null && loadedFits; } }

        /// <summary>Compares the saved game folder with its originals and makes the mod (checked: each delta rebuilds its file).</summary>
        public ModPackage Collect()
        {
            Package = null;
            if (UseFolder && !Directory.Exists(referenceFolder)) { Status = "Choose the folder of an untouched copy of the game."; Raise(nameof(HasPackage)); return null; }
            try
            {
                var changes = EditorMods.Collect(Session.GameFolder, Session.Kind, UseFolder ? referenceFolder : null);
                if (changes.Problems.Count > 0)
                {
                    Preview = "These keep the changes from being a mod:\n" + string.Join("\n", changes.Problems.Select(p => "  " + p)) + "\n";
                    Status = changes.Problems.Count + " problem(s): a mod changes the game's own files, and adds only sound files the game opens by number.";
                }
                else if (changes.Changed.Count == 0)
                {
                    Preview = "";
                    Status = "No changes found: save your edits first (only saved files are compared).";
                }
                else
                {
                    var auth = authors.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0);
                    var m = ModMaker.Make(changes, id.Trim(), name.Trim(), version.Trim(), description, auth, EditorMods.MadeWith);
                    Package = m;
                    var sb = new StringBuilder(m.Summary);
                    sb.Append("\nMade from (SHA-256 of each original):\n");
                    foreach (var f in m.Files) sb.AppendFormat("  {0}  {1}\n", f.Path, f.Added ? "(none: the mod adds it)" : f.BaseSha256);
                    sb.AppendFormat("\nThe package holds {0:N0} bytes of changes in all, and nothing of the original files.\n", m.Files.Sum(f => (long)f.Delta.Length));
                    Preview = sb.ToString();
                    Status = m.Files.Count + " file(s) changed.";
                }
            }
            catch (Exception ex) { Status = ex.Message; Package = null; }
            Raise(nameof(HasPackage));
            return Package;
        }

        public void Write(string path)
        {
            Package.Write(path);
            Status = "Written: " + path;
        }

        public void Load(string path)
        {
            try
            {
                Loaded = ModPackage.Load(path);
                LoadedPath = path;
                RecheckLoaded();
            }
            catch (Exception ex) { Loaded = null; loadedFits = false; Checks = ""; Status = "Could not read the mod: " + ex.Message; }
            Raise(nameof(CanApply));
        }

        public void RecheckLoaded()
        {
            if (Loaded == null) return;
            var sb = new StringBuilder(Loaded.Summary);
            if (Loaded.Game != Session.Kind)
            {
                sb.Append("\nThis mod is for " + Loaded.GameName + "; the open game is the other one.\n");
                Checks = sb.ToString();
                Status = "This mod is for " + Loaded.GameName + ".";
                loadedFits = false;
                Raise(nameof(CanApply));
                return;
            }
            sb.Append("\nAgainst ").Append(Session.GameFolder).Append(":\n");
            var list = EditorMods.Check(Loaded, Session.GameFolder);
            foreach (var c in list) sb.Append("  ").Append(c.File.Path).Append(": ").Append(StateText(c)).Append('\n');
            int bad = list.Count(c => c.State == FileState.Different || c.State == FileState.Missing);
            int done = list.Count(c => c.State == FileState.Applied);
            Checks = sb.ToString();
            loadedFits = bad == 0 && done < list.Count;
            Status = bad > 0 ? bad + " file(s) are not the ones the mod was made from: it does not fit this game." : done == list.Count ? "The mod is already applied." : "The mod fits this game.";
            Raise(nameof(CanApply));
        }

        static string StateText(FileCheck c)
        {
            switch (c.State)
            {
                case FileState.Original: return c.File.Added ? "will be added" : "the original the mod was made from";
                case FileState.Applied: return "already as the mod makes it";
                case FileState.Missing: return "missing";
                default: return c.File.Added ? "a file of that name is already there" : "not the file the mod was made from";
            }
        }

        public bool Apply()
        {
            if (Loaded == null) return false;
            try
            {
                var written = EditorMods.Apply(Loaded, Session.GameFolder, Session.Kind);
                Status = written.Count == 0 ? "Nothing to write: already applied." : "Applied: " + string.Join(", ", written) + " (each original kept as .ORIG). The game was opened again.";
                return true;
            }
            catch (Exception ex) { Status = ex.Message; return false; }
        }
    }

    /// <summary>Tools ▸ Mods: pack the saved changes into a .uwmod, and apply one to the open game.</summary>
    public partial class ModsWindow : Window
    {
        public ModsViewModel Model { get; private set; }
        readonly Func<bool> hasUnsaved;
        readonly Action<string> reopen;

        public ModsWindow() { InitializeComponent(); }

        public ModsWindow(GameSession s, Func<bool> hasUnsaved, Action<string> reopen)
        {
            InitializeComponent();
            this.hasUnsaved = hasUnsaved; this.reopen = reopen;
            Model = new ModsViewModel(s);
            DataContext = Model;
            ManualWindow.Attach(this, "Mods");
            Title = "Mods — " + s.Title;
        }

        void OnCollect(object sender, RoutedEventArgs e)
        {
            if (hasUnsaved != null && hasUnsaved()) Model.Status = "There are unsaved edits; only what is saved goes into the mod. ";
            string pre = Model.Status;
            Model.Collect();
            if (hasUnsaved != null && hasUnsaved()) Model.Status = pre + Model.Status;
        }

        async void OnBrowseReference(object sender, RoutedEventArgs e)
        {
            var f = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "An untouched copy of the game folder" });
            var p = f.FirstOrDefault()?.TryGetLocalPath();
            if (p != null) Model.ReferenceFolder = p;
        }

        async void OnWrite(object sender, RoutedEventArgs e)
        {
            if (!Model.HasPackage) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Write the mod", SuggestedFileName = (Model.Id.Length > 0 ? Model.Id : "mod") + ".uwmod", DefaultExtension = "uwmod",
                FileTypeChoices = new[] { new FilePickerFileType("Underworld mod") { Patterns = new[] { "*.uwmod" } } },
            });
            var p = f?.TryGetLocalPath();
            if (p == null) return;
            try { Model.Write(p); }
            catch (Exception ex) { Model.Status = "Could not write the mod: " + ex.Message; }
        }

        async void OnOpenMod(object sender, RoutedEventArgs e)
        {
            var f = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a mod", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Underworld mod") { Patterns = new[] { "*.uwmod" } } },
            });
            var p = f.FirstOrDefault()?.TryGetLocalPath();
            if (p != null) Model.Load(p);
        }

        async void OnApply(object sender, RoutedEventArgs e)
        {
            if (hasUnsaved != null && hasUnsaved())
            {
                string a = await MessageWindow.Ask(this, "Unsaved edits", "Applying the mod opens the game again, and the unsaved edits would be lost. Apply anyway?", "Apply", "Cancel");
                if (a != "Apply") return;
            }
            if (!Model.Apply()) return;
            string folder = Model.Session.SaveFolder ?? Model.Session.GameFolder;
            string status = Model.Status;
            Close();
            reopen?.Invoke(folder);
        }
    }
}
