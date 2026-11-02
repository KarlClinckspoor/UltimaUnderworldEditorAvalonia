using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>Level ▸ Play-test: the game run in DOSBox from a copy holding the unsaved levels, with the player on the chosen square.</summary>
    public partial class MainWindow
    {
        /// <summary>Where the play test's copy of the game goes (the settings folder; tests may set another).</summary>
        public string PlayTestFolder;
        /// <summary>Tests set this to false: the copy is made, DOSBox is not started.</summary>
        public bool PlayTestLaunch = true;
        /// <summary>The last play test made (for the tests).</summary>
        public PlayTestPlan LastPlayTest { get; private set; }

        void OnPlayTest(object sender, RoutedEventArgs e)
        {
            int x = vm.SelectedTileX, y = vm.SelectedTileY;
            if (x < 0 && vm.Session?.Start != null && vm.Level != null && vm.Session.Start.Level == vm.Level.Level) { x = (int)vm.Session.Start.X; y = (int)vm.Session.Start.Y; }
            if (x < 0) { vm.EditStatus = "Select the tile to start on first."; return; }
            _ = PlayTest(x, y);
        }

        void OnDosBoxPath(object sender, RoutedEventArgs e) { ShowRunPaths(); }

        async Task<string> ChooseDosBox()
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose DOSBox (DOSBox.exe, dosbox, DOSBox-X or DOSBox Staging)", AllowMultiple = false });
                string p = files.Count > 0 ? files[0].TryGetLocalPath() : null;
                if (p == null) return null;
                settings.DosBoxPath = p;
                settings.Save();
                vm.EditStatus = "Play tests run " + p + ".";
                return p;
            }
            catch (Exception ex) { vm.EditStatus = ex.Message; return null; }
        }

        /// <summary>Makes the copy and starts DOSBox in it. Returns the plan, or null when it could not.</summary>
        public async Task<PlayTestPlan> PlayTest(int x, int y)
        {
            var s = vm.Session;
            var level = vm.Level;
            if (s == null || level == null) return null;
            PlayTestPlan plan;
            try
            {
                string folder = PlayTestFolder ?? Path.Combine(Settings.Folder, "Play test", (s.IsUw2 ? "uw2-" : "uw1-") + new string(Path.GetFileName(s.GameFolder.TrimEnd('/', '\\')).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()));
                byte[] player = playerSheet != null && playerSheet.Dirty ? playerSheet.ToBytes() : null;
                plan = Core.PlayTest.Prepare(s, vm.Edits, player, level.Level, x, y, folder);
                LastPlayTest = plan;
            }
            catch (Exception ex) { await MessageWindow.Show(this, "Play test", ex.Message); return null; }
            if (!PlayTestLaunch) { vm.EditStatus = "Play test ready in " + plan.Folder + ". " + plan.Note; return plan; }
            string dosbox = !string.IsNullOrEmpty(settings.DosBoxPath) && File.Exists(settings.DosBoxPath) ? settings.DosBoxPath : Core.PlayTest.FindDosBox(s.GameFolder);
            if (dosbox == null)
            {
                string a = await MessageWindow.Ask(this, "Play test", "DOSBox was not found next to the game or where it is usually installed. Choose the DOSBox program (DOSBox, DOSBox-X or DOSBox Staging)?", "Choose…", "Cancel");
                if (a != "Choose…" || (dosbox = await ChooseDosBox()) == null) return plan;
            }
            try
            {
                // GOG's configuration (or the defaults) with C: on the copy and the game started (DosBoxConfig)
                string conf = WriteDosBoxConf(plan.Folder, plan.Exe, s.IsUw2 ? "playtest-uw2" : "playtest-uw1");
                var psi = new ProcessStartInfo(dosbox) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(dosbox) ?? plan.Folder };
                foreach (var arg in DosBoxConfig.Arguments(conf, settings.DosBoxArgs)) psi.ArgumentList.Add(arg);
                Process.Start(psi);
                vm.EditStatus = "Play test started. " + plan.Note + " (A copy in " + plan.Folder + "; the game folder is untouched.)";
                if (plan.Slot != null) _ = MessageWindow.Show(this, "Play test", plan.Note + "\n\nThe test runs in a copy of the game with the unsaved levels; your game folder is not changed.");
            }
            catch (Exception ex) { await MessageWindow.Show(this, "Play test", "DOSBox did not start (" + dosbox + "): " + ex.Message); }
            return plan;
        }
    }
}
