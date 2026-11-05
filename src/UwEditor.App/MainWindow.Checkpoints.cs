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
        int editStamp;
        string checkpointPrint = "";
        CheckpointsWindow checkpointsWindow;
        public Checkpoints CheckpointStore { get; private set; }

        /// <summary>The zone fill's highest climb in floor steps (0-15), remembered.</summary>
        public int ZoneClimb
        {
            get { return settings.ZoneClimb; }
            set { settings.ZoneClimb = Math.Max(0, Math.Min(15, value)); settings.Save(); if (zoning != null) zoning.MaxClimb = settings.ZoneClimb; }
        }

        /// <summary>The solver splits the zoning's multi-level zones by level (the Zones window's option), remembered.</summary>
        public bool ZoneSplitByLevel
        {
            get { return settings.ZoneSplitByLevel; }
            set { settings.ZoneSplitByLevel = value; settings.Save(); if (zoning != null) zoning.SplitByLevel = value; }
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
            editStamp = 0; checkpointPrint = "";
            lastCheckpoint = DateTime.UtcNow;
            if (vm.Edits != null) vm.Edits.Changed += e => editStamp++;
            checkpointsWindow?.Refresh();
            recoveryCheckpoint = null;
            UpdateRunMarker();
        }

        /// <summary>Something not in the levels changed (strings, the player).</summary>
        void NoteEdit() { editStamp++; }

        void CheckpointTick()
        {
            UpdateRunMarker();
            if (CheckpointMinutes <= 0 || CheckpointStore == null) return;
            if ((DateTime.UtcNow - lastCheckpoint).TotalMinutes < CheckpointMinutes) return;
            WriteCheckpoint("every " + CheckpointMinutes + " min", false);
        }

        /// <summary>The unsaved files a checkpoint keeps besides the levels: STRINGS.PAK, PLAYER.DAT and the edited automaps and notes.</summary>
        Dictionary<string, byte[]> CheckpointFiles()
        {
            var files = new Dictionary<string, byte[]>();
            var pak = conversations != null ? conversations.Strings : strings?.Pak;
            if (pak != null && pak.HasChanges) files["STRINGS.PAK"] = pak.ToBytes();
            if (playerSheet != null && playerSheet.Dirty) files["PLAYER.DAT"] = playerSheet.ToBytes();
            // conversations: the compiled edits (CNV.ARK), and a script typed but not applied yet, as text
            if (conversations != null && conversations.Archive.HasChanges) files["CNV.ARK"] = conversations.Archive.ToBytes();
            if (conversationWindow != null && conversationWindow.Model.ScriptChanged && conversationWindow.Current != null)
                files[string.Format("conversation-{0:D3}.txt", conversationWindow.Current.Slot)] = System.Text.Encoding.UTF8.GetBytes(conversationWindow.Model.Script ?? "");
            if (vm.Edits != null)
                foreach (var a in vm.Edits.Automaps.Where(a => a.Dirty))
                {
                    files[string.Format("automap-{0:D2}.bin", a.Level)] = (byte[])a.Map.Clone();
                    files[string.Format("notes-{0:D2}.bin", a.Level)] = Automap.WriteNotes(a.Notes);
                }
            return files;
        }

        static string Fingerprint(int stamp, Dictionary<string, byte[]> files)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (var kv in files.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var name = System.Text.Encoding.UTF8.GetBytes(kv.Key);
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    sha.TransformBlock(kv.Value, 0, kv.Value.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return stamp + ":" + Convert.ToHexString(sha.Hash);
            }
        }

        /// <summary>Writes a checkpoint of the unsaved work (force: even when nothing changed since the last one). Null when there is nothing unsaved.</summary>
        public CheckpointInfo WriteCheckpoint(string label, bool force)
        {
            lastCheckpoint = DateTime.UtcNow;
            if (CheckpointStore == null) return null;
            try
            {
                var files = CheckpointFiles();
                string print = Fingerprint(editStamp, files);
                if (!force && print == checkpointPrint) return null;
                var c = CheckpointStore.Write(vm.Edits, files, label, CheckpointCopies);
                checkpointPrint = print;
                if (c != null) CheckpointKept(c);
                if (c != null) { vm.EditStatus = "Checkpoint kept (" + c.Summary.Substring(11) + ")."; checkpointsWindow?.Refresh(); }
                return c;
            }
            catch (Exception ex) { vm.EditStatus = "Could not write a checkpoint: " + ex.Message; return null; }
        }

        /// <summary>Asks first when restoring would replace unsaved player or string edits (they have no undo), then restores.</summary>
        public async System.Threading.Tasks.Task<string> RestoreCheckpointAsk(CheckpointInfo c)
        {
            var lost = new List<string>();
            var pak = conversations != null ? conversations.Strings : strings?.Pak;
            if (c.Files.Contains("STRINGS.PAK") && pak != null && pak.HasChanges) lost.Add("strings");
            if (c.Files.Contains("PLAYER.DAT") && playerSheet != null && playerSheet.Dirty) lost.Add("the player");
            if (lost.Count > 0 && AskBeforeDiscarding)
            {
                string a = await MessageWindow.Ask(checkpointsWindow ?? (Avalonia.Controls.Window)this, "Restore the checkpoint", "The unsaved edits to " + string.Join(" and ", lost) + " will be replaced by the checkpoint's, and this cannot be undone. Restore?", "Restore", "Cancel");
                if (a != "Restore") return "Not restored.";
            }
            return RestoreCheckpoint(c);
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
                    conversationWindow?.Close();
                }
                var pd = CheckpointStore.ReadFile(c, "PLAYER.DAT");
                if (pd != null && vm.Session.IsSave)
                {
                    string path = UltimateEditor.Games.GameProfile.Find(vm.Session.SaveFolder, "PLAYER.DAT");
                    playerWindow?.Close();
                    globalsWindow?.Close();             // it shares the player sheet being replaced
                    playerSheet = PlayerSheet.FromBytes(pd, vm.Session.Kind, vm.Session.DataFolder, path);
                    vm.Session.PlayerMoved(playerSheet.Dat);
                    said.Add("the player");
                }
                int maps = 0;
                foreach (var f in c.Files.Where(f => f.StartsWith("automap-", StringComparison.Ordinal)))
                {
                    int lv;
                    if (!int.TryParse(f.Substring(8, 2), out lv)) continue;
                    var map = CheckpointStore.ReadFile(c, f);
                    var notes = CheckpointStore.ReadFile(c, string.Format("notes-{0:D2}.bin", lv));
                    var a = vm.Edits.AutomapFor(lv);
                    if (map == null || (map.SequenceEqual(a.Map) && (notes ?? new byte[0]).SequenceEqual(Automap.WriteNotes(a.Notes)))) continue;
                    a.Replace(map, notes == null ? a.Notes : Automap.ReadNotes(notes), "Checkpoint");
                    vm.Edits.AutomapEdited(a);
                    maps++;
                }
                if (maps > 0) said.Add(maps + " automap(s)");
                var cnv = CheckpointStore.ReadFile(c, "CNV.ARK");
                var lib = cnv != null ? Conversations() : null;
                if (lib != null)
                {
                    var from = UwEditor.Core.Conversations.ConversationArchive.FromBytes(cnv, vm.Session.Kind);
                    int n = 0;
                    foreach (int slot in from.Slots())
                    {
                        var a = from.RawBlock(slot); var b = lib.Archive.RawBlock(slot);
                        if (b != null && a.SequenceEqual(b)) continue;
                        lib.Archive.Write(slot, from.Read(slot));
                        n++;
                    }
                    if (n > 0) { said.Add(n + " conversation(s)"); conversationWindow?.Close(); }
                }
                foreach (var f in c.Files.Where(f => f.StartsWith("conversation-", StringComparison.Ordinal) && f.EndsWith(".txt", StringComparison.Ordinal)))
                {
                    int slot;
                    if (!int.TryParse(f.Substring(13, 3), out slot)) continue;
                    string text = System.Text.Encoding.UTF8.GetString(CheckpointStore.ReadFile(c, f));
                    var w = ShowConversations(slot);
                    if (w != null) { w.Model.Script = text; said.Add("the unapplied script of conversation " + slot + " (in the conversation window: apply or save it there)"); }
                }
                vm.FlushRefresh();
                UpdateMarkers();
                vm.RaiseEditState();
            }
            catch (Exception ex) { return "Could not restore: " + ex.Message; }
            string msg = said.Count == 0 ? "The checkpoint matches what is already here." : "Restored " + string.Join(", ", said) + " from " + c.Time.ToLocalTime().ToString("HH:mm:ss") + " as unsaved edits (Ctrl+Z undoes a level).";
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
