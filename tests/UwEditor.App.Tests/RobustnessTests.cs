using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Crash recovery: an error is logged and the unsaved work kept; a run left behind is offered back.</summary>
    public class RecoveryTests
    {
        [AvaloniaFact]
        public void The_settings_folder_of_the_old_name_moves_to_the_new_one()
        {
            string root = Path.Combine(Path.GetTempPath(), "uwedit-rename-" + Guid.NewGuid().ToString("N"));
            string old = Path.Combine(root, Settings.OldName), dir = Path.Combine(root, Settings.Name);
            Directory.CreateDirectory(Path.Combine(old, "checkpoints"));
            File.WriteAllText(Path.Combine(old, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(old, "checkpoints", "a.json"), "x");
            Settings.MoveOldFolder(old, dir);
            Assert.False(Directory.Exists(old));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(dir, "settings.json")));
            Assert.Equal("x", File.ReadAllText(Path.Combine(dir, "checkpoints", "a.json")));
            // a folder of the new name is never overwritten
            Directory.CreateDirectory(old);
            File.WriteAllText(Path.Combine(old, "settings.json"), "old");
            Settings.MoveOldFolder(old, dir);
            Assert.Equal("{}", File.ReadAllText(Path.Combine(dir, "settings.json")));
            Directory.Delete(root, true);
        }

        static string Uw1 { get { return GameData.Game("UW"); } }

        static void RaiseFloor(MainWindow w)
        {
            var vm = w.ViewModel;
            var t = Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !vm.Level.Tiles[k.X, k.Y].IsSolid && vm.Level.Tiles[k.X, k.Y].Height < 10);
            vm.Edit(e => e.SetTileField(t.X, t.Y, "Floor height", vm.Level.Tiles[t.X, t.Y].Height + 2));
            Assert.True(vm.HasLevelChanges);
        }

        [AvaloniaFact]
        public void An_error_on_the_ui_thread_is_logged_the_work_kept_and_the_editor_goes_on()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            RaiseFloor(w);
            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("a test's error"));
            Ui.Pump();
            Assert.NotNull(w.LastCrashLog);
            string log = File.ReadAllText(w.LastCrashLog);
            Assert.Contains("a test's error", log);
            Assert.Contains("Unsaved:", log);
            var marker = Recovery.LeftBehind(Settings.Folder, pid => null).FirstOrDefault(m => m.Pid == Environment.ProcessId);
            Assert.NotNull(marker);
            Assert.NotNull(marker.Checkpoint);
            Assert.True(Directory.Exists(marker.Checkpoint));
            Assert.Equal(w.LastCrashLog, marker.CrashLog);
            Assert.Contains("Error:", w.ViewModel.EditStatus);
            w.Close();
            Assert.False(File.Exists(Recovery.MarkerPath(Settings.Folder, Environment.ProcessId)));
        }

        [AvaloniaFact]
        public void A_run_that_did_not_exit_is_found_and_its_checkpoint_restored()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            RaiseFloor(w);
            var level = w.ViewModel.Level.Level;
            var data = (byte[])w.ViewModel.Editor.Data.Clone();
            var c = w.WriteCheckpoint("test", true);
            Assert.NotNull(c);
            // the run "crashes": its marker stays, under a pid that is gone
            var m = Recovery.LeftBehind(Settings.Folder, pid => null).First(k => k.Pid == Environment.ProcessId);
            Assert.Equal(c.Folder, m.Checkpoint);
            Assert.NotEmpty(m.Unsaved);
            m.Pid = 999999; m.Started = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Recovery.Write(Settings.Folder, m);
            w.Close();
            Assert.Contains(Recovery.LeftBehind(Settings.Folder), k => k.Pid == 999999);

            var w2 = Ui.Open(null);
            w2.AskBeforeDiscarding = false;
            var left = Recovery.LeftBehind(Settings.Folder).First(k => k.Pid == 999999);
            string said = w2.RestoreLeftBehind(left);
            Assert.StartsWith("Restored level", said);
            Assert.True(w2.ViewModel.Edits.For(level).Data.SequenceEqual(data));
            Assert.True(w2.ViewModel.HasLevelChanges);
            Recovery.Forget(left);
            w2.Close();
        }

        [Fact]
        public void Markers_tell_a_live_run_from_one_left_behind()
        {
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-markers-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
                Recovery.Write(dir, new RunMarker { Pid = 10, Started = t0, Game = "/g", Unsaved = { "level 1" } });
                Recovery.Write(dir, new RunMarker { Pid = 11, Started = t0 });
                Recovery.Write(dir, new RunMarker { Pid = 12, Started = t0 });
                // 10 is running (same start), 11 is gone, 12's pid now belongs to a later process
                var left = Recovery.LeftBehind(dir, pid => pid == 10 ? t0 : pid == 12 ? t0.AddHours(1) : (DateTime?)null);
                Assert.Equal(new[] { 11, 12 }, left.Select(m => m.Pid).OrderBy(p => p));
                var live = Recovery.LeftBehind(dir, pid => null).First(m => m.Pid == 10);
                Assert.Equal("/g", live.Game);
                Assert.Equal(new[] { "level 1" }, live.Unsaved);
                Recovery.Forget(left[0]);
                Assert.Equal(2, Directory.GetFiles(dir, "running-*.json").Length);
                string log = Recovery.WriteCrashLog(dir, new AggregateException(new InvalidOperationException("inner one")), "Game: x");
                Assert.Contains("inner one", File.ReadAllText(log));
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }

    public class AppIconTests
    {
        [AvaloniaFact]
        public void Every_window_carries_the_black_sword_icon()
        {
            var w = Ui.Open(null);
            Assert.NotNull(w.Icon);
            var c = w.ShowCheckpoints();
            Assert.NotNull(c.Icon);
            c.Close();
            w.Close();
        }
    }
}

namespace UwEditor.App.Tests
{
    public class ExhumedWindowTests
    {
        [AvaloniaFact]
        public void The_check_window_runs_the_port_and_reports()
        {
            if (OperatingSystem.IsWindows()) return;
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            var t = System.Linq.Enumerable.Range(0, 64 * 64).Select(i => (X: i % 64, Y: i / 64)).First(k => !vm.Level.Tiles[k.X, k.Y].IsSolid && vm.Level.Tiles[k.X, k.Y].Height < 10);
            vm.Edit(e => e.SetTileField(t.X, t.Y, "Floor height", vm.Level.Tiles[t.X, t.Y].Height + 1));
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-exw-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string fake = dir + "-uw1port.sh";
            try
            {
                File.WriteAllText(fake, "#!/bin/sh\nwhile [ $# -gt 0 ]; do case \"$1\" in --home) H=\"$2\"; shift;; --data) D=\"$2\"; shift;; --input-script) S=\"$2\"; shift;; esac; shift; done\n" +
                    "grep -o 'cheat goto [0-9]* [0-9]* [0-9]*' \"$S\" | while read a b l x y; do echo \"uw1port: cheat: goto $l $x $y (not as DOS)\" >&2; done\n" +
                    "mkdir -p \"$H/SAVE1\"; sleep 1; cp \"$D/DATA/LEV.ARK\" \"$H/SAVE1/LEV.ARK\"; exit 0\n");
                System.Diagnostics.Process.Start("chmod", new[] { "+x", fake }).WaitForExit();
                w.ExhumedFolder = dir;
                w.SetExhumedPort(UltimateEditor.Games.GameKind.UW1, fake);
                var x = w.ShowExhumed();
                Ui.Pump();
                Assert.Equal(new[] { vm.Level.Level }, x.ChosenLevels);
                var task = x.RunCheck();
                var until = DateTime.UtcNow.AddSeconds(60);
                while (!task.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
                Assert.True(task.IsCompleted);
                var r = task.Result;
                Assert.NotNull(r);
                Assert.True(r.Passed, r.Report());
                Assert.True(r.Resaved);
                Assert.Contains("passed", vm.EditStatus);
                x.Close();
            }
            finally { try { Directory.Delete(dir, true); File.Delete(fake); } catch (Exception) { } w.Close(); }
        }
    }
}

namespace UwEditor.App.Tests
{
    public class SaveCheckWindowTests
    {
        [AvaloniaFact]
        public void A_save_with_problems_is_flagged_on_open_and_repaired_by_the_window()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            foreach (var save in GameData.Saves("UW"))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "uwedit-savewin-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tmp);
                foreach (var f in Directory.GetFiles(save)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));
                var w = Ui.Open(null);
                w.AskBeforeDiscarding = false;
                try
                {
                    w.OpenSession(UwEditor.Core.GameSession.Open(tmp, g));
                    Ui.Pump();
                    Assert.NotEmpty(w.SaveProblemsAtOpen);
                    Assert.Contains("Check and repair", w.ViewModel.EditStatus);
                    var sw = w.ShowSaveCheck();
                    Assert.NotEmpty(sw.Findings);
                    Assert.True(sw.Repair(sw.Findings.ToList()) > 0);
                    Assert.True(w.SaveAll(true));
                    var again = UwEditor.Core.GameSession.Open(tmp, g);
                    var left = UwEditor.Core.Editing.SaveCheck.Find(again, new UwEditor.Core.Editing.LevelEdits(again));
                    Assert.True(left.All(f => !f.Problem), Path.GetFileName(save) + ": " + string.Join(" | ", left));
                    sw.Close();
                }
                finally { w.Close(); Directory.Delete(tmp, true); }
            }
        }
    }
}

namespace UwEditor.App.Tests
{
    public class SpotTestWindowTests
    {
        static void Wait(Func<bool> done, int seconds = 30)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!done() && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
        }

        [AvaloniaFact]
        public void The_spot_test_runs_dosbox_a_round_at_a_time_and_counts_the_spots()
        {
            if (OperatingSystem.IsWindows()) return;
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            var vm = w.ViewModel;
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-spotw-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string fake = dir + "-dosbox.sh", calls = dir + "-calls.txt";
            string oldDosBox = w.DosBoxPath;
            try
            {
                // a DOSBox that notes its configuration and the saved games it was given, then closes
                File.WriteAllText(fake, "#!/bin/sh\nC=\"$2\"\nM=$(grep '^mount c' \"$C\" | sed 's/mount c \"\\(.*\\)\"/\\1/')\n" +
                    "echo \"run $(cat \"$M/SAVE1/DESC\")\" >> '" + calls + "'\nsleep 1\nexit 0\n");
                System.Diagnostics.Process.Start("chmod", new[] { "+x", fake }).WaitForExit();
                w.DosBoxPath = fake;
                w.SpotTestFolder = dir;
                var x = w.ShowSpotTest();
                Ui.Pump();
                x.QuickExit = 0;
                x.Choose(new[] { 2 });
                x.Sweep = true;
                int spots = x.Estimate();
                Assert.True(spots > 4);
                var task = x.StartTest();
                Wait(() => task.IsCompleted);
                Assert.True(task.Result);
                Assert.False(x.Waiting);
                Assert.Contains("DOSBox is closed", x.Status);
                Assert.Equal(fake, x.LastLaunch.Program);
                Assert.Equal(0, x.Run.Tested);
                Assert.Equal(4, x.Run.RoundCount);
                Assert.True(File.Exists(Path.Combine(dir, "SAVE4", "PLAYER.DAT")));
                // the same four again, then the next four
                var t2 = x.Again(); Wait(() => t2.IsCompleted);
                Assert.Equal(0, x.Run.Tested);
                var t3 = x.NextRound(); Wait(() => t3.IsCompleted);
                Assert.Equal(4, x.Run.Tested);
                Assert.Equal(4, x.Run.RoundStart);
                var lines = File.ReadAllLines(calls);
                Assert.Equal(3, lines.Length);
                Assert.StartsWith("run 1/" + spots + " L2", lines[0]);
                Assert.StartsWith("run 1/" + spots + " L2", lines[1]);
                Assert.StartsWith("run 5/" + spots + " L2", lines[2]);
                // stopping keeps the log
                x.StopTest();
                Assert.Null(x.Run);
                Assert.Contains("Stopped with 4 of " + spots + " spots tested", File.ReadAllText(Path.Combine(dir, UwEditor.Core.SpotTest.LogName)));
                Assert.False(Directory.Exists(Path.Combine(g, "SAVE1")));
                x.Close();
            }
            finally { w.DosBoxPath = oldDosBox; try { Directory.Delete(dir, true); File.Delete(fake); File.Delete(calls); } catch (Exception) { } w.Close(); }
        }
    }
}
