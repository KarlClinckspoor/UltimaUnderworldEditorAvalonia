using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using UltimateEditor.Games;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>
    /// Run and Test: the game run in DOSBox or in Underworld Exhumed on the folder being edited
    /// (not a copy), the paths of both programs, and the save-everything and restore-everything
    /// commands of the File menu.
    /// </summary>
    public partial class MainWindow
    {
        RunPathsWindow runPathsWindow;
        /// <summary>Tests set this to false: the run is prepared (configuration, arguments), the program is not started.</summary>
        public bool RunLaunch = true;
        /// <summary>The last run prepared: the program, its arguments and working folder (for the tests).</summary>
        public (string Program, List<string> Arguments, string Folder, string Note) LastRun { get; private set; }

        /// <summary>Where the run files (the DOSBox configuration, a copy for a saved game kept elsewhere) go.</summary>
        public static string RunFolder { get { return Path.Combine(Settings.Folder, "Run"); } }

        void OnRunPaths(object sender, RoutedEventArgs e) { ShowRunPaths(); }

        public RunPathsWindow ShowRunPaths()
        {
            if (runPathsWindow != null) { runPathsWindow.Activate(); return runPathsWindow; }
            runPathsWindow = new RunPathsWindow(this);
            runPathsWindow.Closed += (s, e) => runPathsWindow = null;
            runPathsWindow.Show(this);
            return runPathsWindow;
        }

        // ---- the settings the paths window reads and writes

        public string DosBoxPath
        {
            get { return settings.DosBoxPath ?? ""; }
            set { settings.DosBoxPath = value ?? ""; settings.Save(); }
        }

        public string DosBoxArgs
        {
            get { return settings.DosBoxArgs ?? ""; }
            set { settings.DosBoxArgs = value ?? ""; settings.Save(); }
        }

        /// <summary>DOSBox as chosen, or found next to the game or where it is usually installed.</summary>
        public string DosBoxFound()
        {
            if (!string.IsNullOrEmpty(settings.DosBoxPath) && File.Exists(settings.DosBoxPath)) return settings.DosBoxPath;
            return Core.PlayTest.FindDosBox(vm.Session?.GameFolder);
        }

        /// <summary>The configuration a run starts from: GOG's next to the game, else the editor's defaults.</summary>
        public string DosBoxBaseConf(out string from)
        {
            string gog = DosBoxConfig.FindGogConf(vm.Session?.GameFolder);
            if (gog != null)
            {
                try { from = gog; return File.ReadAllText(gog); } catch (Exception) { }
            }
            from = null;
            return DosBoxConfig.Defaults;
        }

        /// <summary>Writes a DOSBox configuration that mounts folder and starts exe; returns its path.</summary>
        public string WriteDosBoxConf(string folder, string exe, string name)
        {
            string from;
            var text = DosBoxConfig.Build(DosBoxBaseConf(out from), folder, exe);
            Directory.CreateDirectory(RunFolder);
            string path = Path.Combine(RunFolder, name + ".conf");
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>Before running the game on the files: unsaved work is saved, left out, or the run cancelled. True to go on.</summary>
        async Task<bool> SaveBeforeRun(string where)
        {
            var parts = UnsavedParts();
            if (parts.Count == 0 || !AskBeforeDiscarding) return true;
            string a = await MessageWindow.Ask(this, "Run " + where, "The game reads the files, and these changes are not saved yet: " + string.Join("; ", parts) + ".", "Save all and run", "Run without them", "Cancel");
            if (a == "Cancel" || a == null) return false;
            if (a == "Save all and run") return SaveAll(true) && UnsavedParts().Count == 0;
            return true;
        }

        /// <summary>Whether the open saved game lies in the game folder as SAVE1-SAVE4 (the game finds it there).</summary>
        bool SaveInGameFolder(GameSession s)
        {
            if (!s.IsSave) return true;
            string parent = Path.GetDirectoryName(Path.GetFullPath(s.SaveFolder).TrimEnd('/', '\\'));
            return string.Equals(parent, Path.GetFullPath(s.GameFolder).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)
                && GameSession.IsSaveFolderName(Path.GetFileName(s.SaveFolder.TrimEnd('/', '\\')));
        }

        /// <summary>A saved game kept elsewhere: a copy of the game with it as SAVE1, in the run folder. Returns the copy's folder.</summary>
        string CopyForOutsideSave(GameSession s, string name)
        {
            string folder = Path.Combine(RunFolder, name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            Core.PlayTest.CopyGame(s.GameFolder, folder, folder);
            foreach (var d in Directory.GetDirectories(folder).Where(d => GameSession.IsSaveFolderName(Path.GetFileName(d)))) Directory.Delete(d, true);
            string slot = Path.Combine(folder, "SAVE1");
            Directory.CreateDirectory(slot);
            foreach (var f in Directory.GetFiles(s.SaveFolder)) File.Copy(f, Path.Combine(slot, Path.GetFileName(f)), true);
            return folder;
        }

        void OnRunDosBox(object sender, RoutedEventArgs e) { _ = RunDosBox(); }

        /// <summary>Runs the game in DOSBox on the game folder being edited, with GOG's configuration (or the defaults) and C: mounted on that folder.</summary>
        public async Task<bool> RunDosBox()
        {
            var s = vm.Session;
            if (s == null) return false;
            if (!await SaveBeforeRun("in DOSBox")) return false;
            string dosbox = DosBoxFound();
            if (dosbox == null && RunLaunch)
            {
                await MessageWindow.Show(this, "Run in DOSBox", "DOSBox was not found next to the game or where it is usually installed. Choose it in Run and Test ▸ Programs and paths.");
                ShowRunPaths();
                return false;
            }
            try
            {
                string folder = s.GameFolder, note;
                if (SaveInGameFolder(s)) note = s.IsSave ? "Restore saved game " + Path.GetFileName(s.SaveFolder.TrimEnd('/', '\\')).Substring(4) + " to play the save being edited." : "The game folder being edited runs as it is.";
                else
                {
                    folder = CopyForOutsideSave(s, "dosbox-" + (s.IsUw2 ? "uw2" : "uw1"));
                    note = "The saved game lies outside the game folder, so it runs from a copy as saved game 1; games saved there stay in " + folder + ".";
                }
                string exe = Path.GetFileName(s.ExePath ?? StartPosition.ExePath(s.DataFolder, s.Kind) ?? (s.IsUw2 ? "UW2.EXE" : "UW.EXE"));
                string conf = WriteDosBoxConf(folder, exe, s.IsUw2 ? "dosbox-uw2" : "dosbox-uw1");
                var args = DosBoxConfig.Arguments(conf, settings.DosBoxArgs);
                LastRun = (dosbox, args, folder, note);
                if (!RunLaunch) return true;
                var psi = new ProcessStartInfo(dosbox) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(dosbox) ?? folder };
                foreach (var a in args) psi.ArgumentList.Add(a);
                Process.Start(psi);
                vm.EditStatus = "DOSBox started on " + folder + ". " + note;
                return true;
            }
            catch (Exception ex) { await MessageWindow.Show(this, "Run in DOSBox", "DOSBox did not start: " + ex.Message); return false; }
        }

        void OnRunExhumed(object sender, RoutedEventArgs e) { _ = RunExhumed(); }

        /// <summary>
        /// Runs the game in Underworld Exhumed on the game folder being edited: --data the game
        /// folder and --home the same folder, so Journey Onward lists its SAVE1-SAVE4 (the save
        /// being edited among them) and games saved in the port land there. The port also keeps its
        /// settings file and recordings in its home.
        /// </summary>
        public async Task<bool> RunExhumed()
        {
            var s = vm.Session;
            if (s == null) return false;
            if (!await SaveBeforeRun("in Underworld Exhumed")) return false;
            string port = ExhumedPort(s.Kind);
            if (port == null && RunLaunch)
            {
                await MessageWindow.Show(this, "Run in Underworld Exhumed", "The port (" + ExhumedCheck.ProgramName(s.Kind) + ") was not found. Download a release of Underworld Exhumed (github.com/abedegno/underworld-exhumed) and choose it in Run and Test ▸ Programs and paths.");
                ShowRunPaths();
                return false;
            }
            try
            {
                string data = s.GameFolder, home = s.GameFolder, note;
                if (SaveInGameFolder(s)) note = s.IsSave ? "Journey Onward and slot " + Path.GetFileName(s.SaveFolder.TrimEnd('/', '\\')).Substring(4) + " is the save being edited." : "Journey Onward lists the game folder's saved games.";
                else
                {
                    home = Path.Combine(RunFolder, "exhumed-home-" + (s.IsUw2 ? "uw2" : "uw1"));
                    if (Directory.Exists(home)) Directory.Delete(home, true);
                    string slot = Path.Combine(home, "SAVE1");
                    Directory.CreateDirectory(slot);
                    foreach (var f in Directory.GetFiles(s.SaveFolder)) File.Copy(f, Path.Combine(slot, Path.GetFileName(f)), true);
                    note = "The saved game lies outside the game folder, so the port gets a copy as slot 1 (in " + home + ").";
                }
                var args = new List<string> { "--data", data, "--home", home };
                LastRun = (port, args, home, note);
                if (!RunLaunch) return true;
                var psi = new ProcessStartInfo(port) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(port) ?? home };
                foreach (var a in args) psi.ArgumentList.Add(a);
                Process.Start(psi);
                vm.EditStatus = "Underworld Exhumed started on " + data + ". " + note;
                return true;
            }
            catch (Exception ex) { await MessageWindow.Show(this, "Run in Underworld Exhumed", "The port did not start: " + ex.Message); return false; }
        }

        // ---- File ▸ Save all changes, Restore all

        void OnSaveAllChanges(object sender, RoutedEventArgs e) { SaveAllChanges(); }

        /// <summary>Saves every kind of unsaved work (levels, player, strings and conversations, globals, pictures, zones, the tool windows').</summary>
        public bool SaveAllChanges()
        {
            if (vm.Session == null) return false;
            if (UnsavedParts().Count == 0) { vm.EditStatus = "Nothing to save."; return true; }
            return SaveAll(true);
        }

        void OnRestoreAll(object sender, RoutedEventArgs e) { _ = RestoreAll(); }

        /// <summary>
        /// Drops every unsaved change and reads the game again from its files, after asking. A
        /// checkpoint of the dropped work is kept first, so Level ▸ Checkpoints can bring it back.
        /// The level and the camera stay where they were.
        /// </summary>
        public async Task<bool> RestoreAll()
        {
            var s = vm.Session;
            if (s == null) return false;
            var parts = UnsavedParts();
            if (parts.Count == 0) { vm.EditStatus = "Nothing unsaved: the files and the editor agree."; return true; }
            if (AskBeforeDiscarding)
            {
                string a = await MessageWindow.Ask(this, "Restore all", "Drop every unsaved change and read the game again from its files? Unsaved: " + string.Join("; ", parts) + ".\n\nA checkpoint of them is kept first (File ▸ Checkpoints… brings them back).", "Restore all", "Cancel");
                if (a != "Restore all") return false;
            }
            WriteCheckpoint("before Restore all", true);
            int level = vm.Level?.Level ?? 0;
            var cam = View.Camera;
            var target = cam.Target; float distance = cam.Distance, yaw = cam.Yaw, pitch = cam.Pitch;
            OpenPathNow(s.SaveFolder ?? s.GameFolder);
            if (vm.Session == null || vm.Session == s) return false;
            if (level > 0 && vm.Level?.Level != level) vm.LoadLevel(level);
            View.LookAt(target, distance, yaw, pitch);
            vm.EditStatus = "Restored: every unsaved change was dropped and the files read again (a checkpoint kept them).";
            return true;
        }
    }
}
