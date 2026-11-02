using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Run and Test (DOSBox and Underworld Exhumed on the folder being edited) and File ▸ Save all changes / Restore all.</summary>
    public class RunAndTestTests
    {
        static T Wait<T>(System.Threading.Tasks.Task<T> t)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            while (!t.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(10); }
            Assert.True(t.IsCompleted);
            return t.Result;
        }

        [AvaloniaFact]
        public void DOSBox_and_Exhumed_run_on_the_save_being_edited()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            string save = Path.Combine(g, "SAVE1"); if (!Directory.Exists(save)) return;
            var w = Ui.Open(save);
            w.AskBeforeDiscarding = false;
            w.RunLaunch = false;
            Assert.True(Wait(w.RunDosBox()));
            var run = w.LastRun;
            Assert.Equal("-conf", run.Arguments[0]);
            string conf = File.ReadAllText(run.Arguments[1]);
            Assert.Contains("mount c \"" + Path.GetFullPath(g).TrimEnd('/', '\\') + "\"", conf);
            Assert.Contains("\nUW2", conf.Replace("\r", ""));
            Assert.Contains("saved game 1", run.Note);
            Assert.True(Wait(w.RunExhumed()));
            run = w.LastRun;
            Assert.Equal(new[] { "--data", w.ViewModel.Session.GameFolder, "--home", w.ViewModel.Session.GameFolder }, run.Arguments);
            Assert.Contains("slot 1", run.Note);
            w.Close();
        }

        [AvaloniaFact]
        public void A_save_kept_elsewhere_runs_from_a_copy()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var save = GameData.Saves("UW").FirstOrDefault(); if (save == null) return;
            var w = Ui.Open(null);
            w.AskBeforeDiscarding = false;
            w.RunLaunch = false;
            w.OpenSession(UwEditor.Core.GameSession.Open(save, g));
            Ui.Pump();
            Assert.True(Wait(w.RunExhumed()));
            var home = w.LastRun.Folder;
            Assert.StartsWith(MainWindow.RunFolder, home);
            Assert.True(File.Exists(Path.Combine(home, "SAVE1", "LEV.ARK")));
            Assert.True(Wait(w.RunDosBox()));
            Assert.True(File.Exists(Path.Combine(w.LastRun.Folder, "SAVE1", "PLAYER.DAT")));
            Assert.True(File.Exists(Path.Combine(w.LastRun.Folder, "UW.EXE")));
            w.Close();
        }

        [AvaloniaFact]
        public void Restore_all_drops_every_change_and_keeps_a_checkpoint()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            vm.LoadLevel(2);
            Ui.Pump();
            var before = (byte[])vm.Session.LoadLevel(2).Block.Data.Clone();
            var t = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !vm.Level.Tiles[k.X, k.Y].IsSolid && vm.Level.Tiles[k.X, k.Y].Height < 10);
            vm.Edit(e => e.SetTileField(t.X, t.Y, "Floor height", vm.Level.Tiles[t.X, t.Y].Height + 1));
            Assert.NotEmpty(w.UnsavedParts());
            int checkpoints = w.CheckpointStore.List().Count;
            Assert.True(Wait(w.RestoreAll()));
            Ui.Pump();
            Assert.Empty(w.UnsavedParts());
            Assert.Equal(2, vm.Level.Level);
            Assert.Equal(before, vm.Level.Block.Data);
            Assert.Equal(checkpoints + 1, w.CheckpointStore.List().Count);
            Assert.True(w.SaveAllChanges());       // nothing to save
            Assert.Equal("Nothing to save.", vm.EditStatus);
            w.Close();
        }

        [AvaloniaFact]
        public void The_programs_window_opens()
        {
            var w = Ui.Open(GameData.Game("UW"));
            var p = w.ShowRunPaths();
            Ui.Pump();
            Assert.NotNull(p);
            p.Close();
            w.Close();
        }
    }
}
