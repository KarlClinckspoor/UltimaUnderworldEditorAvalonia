using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    /// <summary>The main window's checkpoints: a copy of the unsaved work every few minutes.</summary>
    public partial class MainWindow
    {
        DispatcherTimer checkpointTimer;
        DateTime lastCheckpoint = DateTime.UtcNow;
        int editStamp, checkpointStamp;
        CheckpointsWindow checkpointsWindow;
        public Checkpoints CheckpointStore { get; private set; }

        /// <summary>The zone fill's highest climb in floor steps (0-15), remembered.</summary>
        public int ZoneClimb
        {
            get { return settings.ZoneClimb; }
            set { settings.ZoneClimb = Math.Max(0, Math.Min(15, value)); settings.Save(); if (zoning != null) zoning.MaxClimb = settings.ZoneClimb; }
        }

        public int CheckpointMinutes
        {
            get { return settings.CheckpointMinutes; }
            set { settings.CheckpointMinutes = Math.Max(0, Math.Min(240, value)); settings.Save(); }
        }

        public int CheckpointCopies
        {
            get { return Math.Max(1, Math.Min(Checkpoints.MaxCopies, settings.CheckpointCopies)); }
            set { settings.CheckpointCopies = Math.Max(1, Math.Min(Checkpoints.MaxCopies, value)); settings.Save(); CheckpointStore?.Prune(settings.CheckpointCopies); }
        }

        void InitCheckpoints()
        {
            checkpointTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            checkpointTimer.Tick += (s, e) => CheckpointTick();
            checkpointTimer.Start();
            Closed += (s, e) => checkpointTimer.Stop();
        }

        /// <summary>A new game: its checkpoint folder, and a count of edits to know when something changed.</summary>
        void CheckpointsFor(UwEditor.Core.GameSession s)
        {
            CheckpointStore = s == null ? null : new Checkpoints(Path.Combine(Settings.Folder, "checkpoints"), s);
            editStamp = checkpointStamp = 0;
            lastCheckpoint = DateTime.UtcNow;
            if (vm.Edits != null) vm.Edits.Changed += e => editStamp++;
            checkpointsWindow?.Refresh();
        }

        /// <summary>Something not in the levels changed (strings, the player).</summary>
        void NoteEdit() { editStamp++; }

        void CheckpointTick()
        {
            if (CheckpointMinutes <= 0 || CheckpointStore == null) return;
            if ((DateTime.UtcNow - lastCheckpoint).TotalMinutes < CheckpointMinutes) return;
            WriteCheckpoint("every " + CheckpointMinutes + " min", false);
        }

        /// <summary>Writes a checkpoint of the unsaved work (force: even when nothing changed since the last one). Null when there is nothing unsaved.</summary>
        public CheckpointInfo WriteCheckpoint(string label, bool force)
        {
            lastCheckpoint = DateTime.UtcNow;
            if (CheckpointStore == null || (!force && editStamp == checkpointStamp)) return null;
            var files = new Dictionary<string, byte[]>();
            try
            {
                if (strings != null && strings.HasChanges) files["STRINGS.PAK"] = strings.Pak.ToBytes();
                if (playerSheet != null && playerSheet.Dirty) files["PLAYER.DAT"] = playerSheet.ToBytes();
                var c = CheckpointStore.Write(vm.Edits, files, label, CheckpointCopies);
                checkpointStamp = editStamp;
                if (c != null) { vm.EditStatus = "Checkpoint kept (" + c.Summary.Substring(11) + ")."; checkpointsWindow?.Refresh(); }
                return c;
            }
            catch (Exception ex) { vm.EditStatus = "Could not write a checkpoint: " + ex.Message; return null; }
        }

        /// <summary>Loads a checkpoint back as unsaved edits; returns what was done.</summary>
        public string RestoreCheckpoint(CheckpointInfo c)
        {
            if (CheckpointStore == null || vm.Edits == null) return "No game open.";
            var said = new List<string>();
            try
            {
                var levels = CheckpointStore.RestoreLevels(c, vm.Edits);
                if (levels.Count > 0) said.Add("level" + (levels.Count == 1 ? " " : "s ") + string.Join(", ", levels));
                var pak = CheckpointStore.ReadFile(c, "STRINGS.PAK");
                var st = pak != null ? Strings() : null;
                if (st != null)
                {
                    var from = UwEditor.Core.Conversations.StringsPakFile.FromBytes(pak);
                    int n = 0;
                    foreach (int b in from.Blocks)
                        if (!from.Get(b).SequenceEqual(st.Get(b))) { st.Pak.Set(b, from.Get(b)); n++; }
                    if (n > 0) said.Add(n + " string block(s)");
                    stringsWindow?.Close();
                    keysWindow?.Close();
                }
                var pd = CheckpointStore.ReadFile(c, "PLAYER.DAT");
                if (pd != null && vm.Session.IsSave)
                {
                    string path = UltimateEditor.Games.GameProfile.Find(vm.Session.SaveFolder, "PLAYER.DAT");
                    playerWindow?.Close();
                    playerSheet = PlayerSheet.FromBytes(pd, vm.Session.Kind, vm.Session.DataFolder, path);
                    vm.Session.PlayerMoved(playerSheet.Dat);
                    said.Add("the player");
                }
                vm.FlushRefresh();
                UpdateMarkers();
                vm.RaiseEditState();
            }
            catch (Exception ex) { return "Could not restore: " + ex.Message; }
            string msg = said.Count == 0 ? "The checkpoint holds what is already here." : "Restored " + string.Join(", ", said) + " from " + c.Time.ToLocalTime().ToString("HH:mm:ss") + " as unsaved edits (Ctrl+Z undoes a level).";
            vm.EditStatus = msg;
            return msg;
        }

        void OnCheckpoints(object sender, RoutedEventArgs e) { ShowCheckpoints(); }

        public CheckpointsWindow ShowCheckpoints()
        {
            if (checkpointsWindow != null) { checkpointsWindow.Activate(); checkpointsWindow.Refresh(); return checkpointsWindow; }
            checkpointsWindow = new CheckpointsWindow(this);
            checkpointsWindow.Closed += (s, e) => checkpointsWindow = null;
            checkpointsWindow.Show(this);
            return checkpointsWindow;
        }
    }
}
