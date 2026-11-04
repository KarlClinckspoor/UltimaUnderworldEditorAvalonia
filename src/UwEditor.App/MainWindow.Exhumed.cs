using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UltimateEditor.Games;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>Checking edits in Underworld Exhumed, and exporting them as a saved game.</summary>
    public partial class MainWindow
    {
        ExhumedWindow exhumedWindow;
        /// <summary>Where checks go (the settings folder; tests may set another).</summary>
        public string ExhumedFolder;
        /// <summary>Tests set this to false: the check is set up, the port is not run.</summary>
        public bool ExhumedLaunch = true;

        void OnExhumedCheck(object sender, RoutedEventArgs e) { ShowExhumed(); }

        public ExhumedWindow ShowExhumed()
        {
            if (vm.Session == null) return null;
            if (exhumedWindow != null) { exhumedWindow.Activate(); return exhumedWindow; }
            exhumedWindow = new ExhumedWindow(this);
            exhumedWindow.Closed += (s, e) => exhumedWindow = null;
            exhumedWindow.Show(this);
            return exhumedWindow;
        }

        /// <summary>The path chosen for the game's port ("" when none).</summary>
        public string ExhumedChosen(GameKind kind) { return (kind == GameKind.UW2 ? settings.ExhumedUw2Path : settings.ExhumedUw1Path) ?? ""; }

        /// <summary>The port for a game: the one chosen, else one found.</summary>
        public string ExhumedPortFor(GameKind kind) { return ExhumedPort(kind); }

        /// <summary>The port for the game: the one chosen, else one found.</summary>
        public string ExhumedPort(GameKind kind)
        {
            string chosen = kind == GameKind.UW2 ? settings.ExhumedUw2Path : settings.ExhumedUw1Path;
            if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return chosen;
            return ExhumedCheck.FindPort(kind, new[] { vm.Session?.GameFolder, Path.GetDirectoryName(vm.Session?.GameFolder ?? "") });
        }

        /// <summary>The check's thoroughness, remembered between runs.</summary>
        public ExhumedOptions ExhumedThoroughness
        {
            get { return new ExhumedOptions { VisitEdits = settings.ExhumedEdits, Sweep = settings.ExhumedSweep, LookAround = settings.ExhumedLook, PortCrashesOnLevel9 = settings.ExhumedLevel9Crash }; }
            set { settings.ExhumedEdits = value.VisitEdits; settings.ExhumedSweep = value.Sweep; settings.ExhumedLook = value.LookAround; settings.ExhumedLevel9Crash = value.PortCrashesOnLevel9; settings.Save(); }
        }

        public void SetExhumedPort(GameKind kind, string path)
        {
            if (kind == GameKind.UW2) settings.ExhumedUw2Path = path; else settings.ExhumedUw1Path = path;
            settings.Save();
        }

        /// <summary>Sets a check up with the unsaved levels (and player) in the settings folder.</summary>
        public ExhumedPlan PrepareExhumed(IList<int> levels, bool saveAtEnd, bool keepAlive = true, ExhumedOptions options = null, bool showWindow = false)
        {
            var s = vm.Session;
            string folder = ExhumedFolder ?? Path.Combine(Settings.Folder, "Exhumed check", (s.IsUw2 ? "uw2-" : "uw1-") + new string(Path.GetFileName(s.GameFolder.TrimEnd('/', '\\')).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()));
            byte[] player = playerSheet != null && playerSheet.Dirty ? playerSheet.ToBytes() : null;
            return ExhumedCheck.Prepare(s, vm.Edits, player, levels, folder, saveAtEnd, keepAlive, options, showWindow);
        }

        /// <summary>The check shows the game's window while it runs, remembered.</summary>
        public bool ExhumedShowWindow
        {
            get { return settings.ExhumedShowWindow; }
            set { settings.ExhumedShowWindow = value; settings.Save(); }
        }

        SaveCheckWindow saveCheckWindow;

        void OnSaveCheck(object sender, RoutedEventArgs e) { ShowSaveCheck(); }

        public SaveCheckWindow ShowSaveCheck()
        {
            if (vm.Session == null) return null;
            if (saveCheckWindow != null) { saveCheckWindow.Activate(); saveCheckWindow.Run(); return saveCheckWindow; }
            saveCheckWindow = new SaveCheckWindow(this);
            saveCheckWindow.Closed += (s, e) => saveCheckWindow = null;
            saveCheckWindow.Show(this);
            return saveCheckWindow;
        }

        /// <summary>After opening a saved game: its problems, said in the status line (a quick look; the window has the details).</summary>
        public List<UwEditor.Core.Editing.SaveFinding> SaveProblemsAtOpen { get; private set; }

        void CheckSaveAtOpen(GameSession s)
        {
            SaveProblemsAtOpen = null;
            if (!s.IsSave) return;
            try
            {
                var found = UwEditor.Core.Editing.SaveCheck.Find(s, vm.Edits).Where(f => f.Problem).ToList();
                SaveProblemsAtOpen = found;
                if (found.Count > 0)
                    vm.EditStatus = "This saved game has " + found.Count + " problem(s) that can crash or upset the game (" + string.Join(", ", found.Select(f => "level " + f.Level + " " + f.Kind).Distinct().Take(4)) + "): File ▸ Check and repair the saved game…";
            }
            catch (Exception) { }
        }

        void OnExportSave(object sender, RoutedEventArgs e) { _ = ExportSave(); }

        /// <summary>File ▸ Export as a saved game: the edits written as a SAVEn folder, in Underworld Exhumed's saves or any folder.</summary>
        public async Task<string> ExportSave(string folder = null)
        {
            var s = vm.Session;
            if (s == null) return null;
            if (ExhumedCheck.SaveSource(s) == null)
            {
                await MessageWindow.Show(this, "Export as a saved game", "A saved game holds the player as well as the levels, and this game has none to start from. Save a game once in the game (any slot), or open a SAVEn folder.");
                return null;
            }
            if (folder == null)
            {
                string home = ExhumedCheck.PortHome(s.Kind);
                var choices = new List<string>();
                for (int i = 1; i <= 4; i++) choices.Add("Exhumed slot " + i);
                choices.Add("Another folder…"); choices.Add("Cancel");
                string a = await MessageWindow.Ask(this, "Export as a saved game",
                    "The unsaved levels" + (playerSheet != null && playerSheet.Dirty ? " and player" : "") + " are written as a saved game, from " + ExhumedCheck.SaveSource(s) + ". " +
                    "Put it among Underworld Exhumed's saved games (" + home + "), where Journey Onward or Ctrl+R finds it, or in a folder of your choice (a SAVEn folder of the original game, say). A slot already there is replaced.",
                    choices.ToArray());
                if (a == null || a == "Cancel") return null;
                if (a.StartsWith("Exhumed slot ", StringComparison.Ordinal)) folder = Path.Combine(home, "SAVE" + a.Substring(13));
                else
                {
                    var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the folder for the saved game (an empty one, or a SAVEn folder)", AllowMultiple = false });
                    folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
                    if (folder == null) return null;
                }
            }
            try
            {
                byte[] player = playerSheet != null && playerSheet.Dirty ? playerSheet.ToBytes() : null;
                string done = ExhumedCheck.WriteSave(s, vm.Edits, player, folder, "Editor " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                vm.EditStatus = "Saved game written to " + done + ".";
                return done;
            }
            catch (Exception ex)
            {
                await MessageWindow.Show(this, "Export as a saved game", "Could not write it: " + ex.Message);
                return null;
            }
        }
    }
}
