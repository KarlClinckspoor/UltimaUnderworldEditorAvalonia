using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// Crash recovery: errors are caught and logged, the unsaved work is kept in a checkpoint at
    /// once, and a run that ended without a clean exit (a crash, a kill, the power) is found at the
    /// next start by the marker it left, which offers its checkpoint back.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The main window, for the error handlers set up before it exists.</summary>
        public static MainWindow Current { get; private set; }
        RunMarker marker;
        string recoveryCheckpoint;
        bool showingError;
        DateTime errorWindowStart;
        int errorsInWindow;
        /// <summary>Tests and scripted runs set this to false: no question about a run left behind.</summary>
        public bool OfferRecovery = true;
        /// <summary>The crash log of the last error caught (for the tests).</summary>
        public string LastCrashLog { get; private set; }

        void InitRecovery()
        {
            Current = this;
            DateTime started;
            try { using (var p = Process.GetCurrentProcess()) started = p.StartTime.ToUniversalTime(); }
            catch (Exception) { started = DateTime.UtcNow; }
            marker = new RunMarker { Pid = Environment.ProcessId, Started = started };
            UpdateRunMarker();
            Closed += (s, e) =>
            {
                Recovery.Remove(Settings.Folder, marker.Pid);
                if (Current == this) Current = null;
            };
            Opened += (s, e) =>
            {
                if (OfferRecovery && Program.ReadyFile == null && Program.StateFile == null) _ = OfferLeftBehind();
            };
        }

        /// <summary>Writes what this run holds to its marker: the game, the unsaved work, the checkpoint keeping it.</summary>
        public void UpdateRunMarker()
        {
            if (marker == null) return;
            try
            {
                var s = vm.Session;
                marker.Game = s == null ? null : (s.SaveFolder ?? s.GameFolder);
                marker.Unsaved = s == null ? new System.Collections.Generic.List<string>() : UnsavedParts();
                if (marker.Unsaved.Count == 0) recoveryCheckpoint = null;
                marker.Checkpoint = recoveryCheckpoint;
                Recovery.Write(Settings.Folder, marker);
            }
            catch (Exception) { }
        }

        /// <summary>A checkpoint was written: it is the one to offer if this run ends badly.</summary>
        void CheckpointKept(CheckpointInfo c)
        {
            if (c == null) return;
            recoveryCheckpoint = c.Folder;
            UpdateRunMarker();
        }

        /// <summary>An error no code caught. Logs it, keeps the unsaved work in a checkpoint and, unless the program is ending, says so.</summary>
        public void ReportError(Exception ex, bool fatal)
        {
            string context = "";
            try
            {
                var s = vm.Session;
                context = "Game: " + (s == null ? "(none)" : (s.SaveFolder ?? s.GameFolder) + " (" + s.Kind + ")") + "\nLevel shown: " + (vm.Level?.Level.ToString() ?? "-") + "\nUnsaved: " + (marker?.Unsaved.Count > 0 ? string.Join("; ", marker.Unsaved) : "nothing");
            }
            catch (Exception) { }
            string log = Recovery.WriteCrashLog(Settings.Folder, ex, context + (fatal ? "\nThe program ended." : "\nThe program went on."));
            LastCrashLog = log;
            CheckpointInfo c = null;
            try { c = EmergencyCheckpoint(); } catch (Exception) { }
            if (marker != null)
            {
                marker.CrashLog = log;
                if (c != null) recoveryCheckpoint = c.Folder;
                marker.Checkpoint = recoveryCheckpoint;
                try { Recovery.Write(Settings.Folder, marker); } catch (Exception) { }
            }
            if (fatal) return;
            // a burst of errors (one in a paint handler, say) gets one window, then the status line
            var now = DateTime.UtcNow;
            if ((now - errorWindowStart).TotalSeconds > 60) { errorWindowStart = now; errorsInWindow = 0; }
            errorsInWindow++;
            string kept = c != null ? "Your unsaved work was kept in a checkpoint (Level ▸ Checkpoints…, " + c.Time.ToLocalTime().ToString("HH:mm:ss") + ")." : "There was no unsaved work to keep.";
            try { vm.EditStatus = "Error: " + ex.Message + " " + kept; } catch (Exception) { }
            if (showingError || errorsInWindow > 3 || !AskBeforeDiscarding) return;
            showingError = true;
            _ = ShowError(ex, kept, log);
        }

        async Task ShowError(Exception ex, string kept, string log)
        {
            try
            {
                string a = await MessageWindow.Ask(this, "Something went wrong",
                    "The editor hit an error it did not expect:\n\n" + ex.GetType().Name + ": " + ex.Message + "\n\n" + kept +
                    "\n\nThe editor can usually go on. If anything looks wrong, restore the checkpoint, or save to a copy first." +
                    (log != null ? "\n\nThe details are in " + log + " (attach it to a bug report)." : ""),
                    "Keep working", "Open the log folder");
                if (a == "Open the log folder" && log != null) OpenFolder(Path.GetDirectoryName(log));
            }
            catch (Exception) { }
            finally { showingError = false; }
        }

        static void OpenFolder(string dir)
        {
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); } catch (Exception) { }
        }

        /// <summary>A checkpoint of the unsaved work that touches no control (an error may have come from any thread).</summary>
        CheckpointInfo EmergencyCheckpoint()
        {
            if (CheckpointStore == null) return null;
            var files = CheckpointFiles();
            return CheckpointStore.Write(vm.Edits, files, "after an error", CheckpointCopies);
        }

        /// <summary>At start: a run that did not exit cleanly and had unsaved work is offered back (open its game, restore its checkpoint).</summary>
        public async Task<string> OfferLeftBehind()
        {
            System.Collections.Generic.List<RunMarker> left;
            try { left = Recovery.LeftBehind(Settings.Folder).Where(m => m.Pid != marker?.Pid).ToList(); }
            catch (Exception) { return null; }
            string said = null;
            foreach (var m in left)
            {
                Recovery.Forget(m);
                if (m.Unsaved == null || m.Unsaved.Count == 0 || string.IsNullOrEmpty(m.Game)) continue;
                if (said != null) continue;    // one offer: the newest run
                string when = m.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                string how = m.CrashLog != null ? "ended with an error" : "did not close normally";
                bool hasCheckpoint = !string.IsNullOrEmpty(m.Checkpoint) && Directory.Exists(m.Checkpoint);
                if (!hasCheckpoint)
                {
                    await MessageWindow.Show(this, "The last run " + how, "The editor " + how + " (" + when + ") with unsaved work in " + m.Game + ": " + string.Join("; ", m.Unsaved) +
                        ". No checkpoint was written since it was last saved, so that work could not be kept." + (m.CrashLog != null ? "\n\nThe error is described in " + m.CrashLog + "." : ""));
                    said = "nothing to restore";
                    continue;
                }
                string a = await MessageWindow.Ask(this, "The last run " + how,
                    "The editor " + how + " (" + when + ") with unsaved work in " + m.Game + ": " + string.Join("; ", m.Unsaved) +
                    ".\n\nA checkpoint kept it. Open the game and restore the checkpoint as unsaved edits? (Nothing is written to the game until you save.)" +
                    (m.CrashLog != null ? "\n\nThe error is described in " + m.CrashLog + "." : ""),
                    "Restore", "Not now");
                if (a != "Restore") { said = "declined"; continue; }
                said = RestoreLeftBehind(m);
                await MessageWindow.Show(this, "Restore", said);
            }
            return said;
        }

        /// <summary>Opens the run's game and restores its checkpoint; returns what was done.</summary>
        public string RestoreLeftBehind(RunMarker m)
        {
            if (!Directory.Exists(m.Game)) return m.Game + " is not there any more. The checkpoint stays in " + m.Checkpoint + ".";
            if (vm.Session == null || !string.Equals(Path.GetFullPath(vm.Session.SaveFolder ?? vm.Session.GameFolder).TrimEnd('/', '\\'), Path.GetFullPath(m.Game).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                OpenPathNow(m.Game);
            if (CheckpointStore == null) return "The game could not be opened.";
            var c = CheckpointStore.List().FirstOrDefault(k => string.Equals(Path.GetFullPath(k.Folder), Path.GetFullPath(m.Checkpoint), StringComparison.OrdinalIgnoreCase));
            if (c == null) return "The checkpoint was not found among this game's (Level ▸ Checkpoints… lists them).";
            return RestoreCheckpoint(c);
        }
    }
}
