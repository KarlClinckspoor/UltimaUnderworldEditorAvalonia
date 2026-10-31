using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Mods;

namespace UwEditor.App
{
    public sealed class ModsViewModel : Observable
    {
        public GameSession Session { get; private set; }
        string name, id, version = "1.0.0", authors = "", description = "", referenceFolder = "", preview = "", status = "", checks = "", loadedPath = "";
        bool useOriginals = true, force;
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
        public bool Force { get { return force; } set { Set(ref force, value); } }
        public bool HasPackage { get { return Package != null && Package.Components.Count > 0; } }
        public bool CanApply { get { return Loaded != null; } }

        /// <summary>Compares the game folder with its reference and lists what the mod would hold.</summary>
        public ModPackage Collect()
        {
            if (UseFolder && !Directory.Exists(referenceFolder)) { Status = "Choose the folder of an untouched copy of the game."; Package = null; Raise(nameof(HasPackage)); return null; }
            try
            {
                var m = ModPackage.FromChanges(Session.GameFolder, Session.Kind, UseFolder ? referenceFolder : null);
                m.Id = id.Trim(); m.Name = name.Trim(); m.Version = version.Trim(); m.Description = description;
                m.Authors.Clear();
                m.Authors.AddRange(authors.Split(',').Select(a => a.Trim()).Where(a => a.Length > 0));
                Package = m;
                var sb = new StringBuilder(m.Summary);
                sb.Append("\nMade from (SHA-256 of each file it changes):\n");
                foreach (var kv in m.BaseFiles.OrderBy(k => k.Key)) sb.AppendFormat("  {0}  {1}\n", kv.Key, kv.Value);
                Preview = sb.ToString();
                Status = m.Components.Count == 0 ? "No changes found: save your edits first (only saved files are compared)." : m.Components.Count + " change(s) found.";
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
                Loaded = ModPackage.Read(path);
                LoadedPath = path;
                RecheckLoaded();
            }
            catch (Exception ex) { Loaded = null; Checks = ""; Status = "Could not read the mod: " + ex.Message; }
            Raise(nameof(CanApply));
        }

        public void RecheckLoaded()
        {
            if (Loaded == null) return;
            var sb = new StringBuilder(Loaded.Summary);
            string game = Session.IsUw2 ? "uw2" : "uw1";
            if (!string.Equals(Loaded.Game, game, StringComparison.OrdinalIgnoreCase)) sb.Append("\nThis mod is for " + Loaded.Game.ToUpperInvariant() + "; the open game is " + game.ToUpperInvariant() + ".\n");
            sb.Append("\nAgainst ").Append(Session.GameFolder).Append(":\n");
            var list = Loaded.Check(Session.GameFolder, Session.Kind);
            foreach (var c in list) sb.Append("  ").Append(c).Append('\n');
            int bad = list.Count(c => c.State == ComponentState.Conflict || c.State == ComponentState.MissingFile);
            int done = list.Count(c => c.State == ComponentState.AlreadyApplied);
            Checks = sb.ToString();
            Status = bad > 0 ? bad + " part(s) do not fit this game." : done == list.Count ? "The mod is already applied." : "The mod fits this game.";
        }

        public bool Apply()
        {
            if (Loaded == null) return false;
            try
            {
                var written = Loaded.Apply(Session.GameFolder, Session.Kind, force);
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
