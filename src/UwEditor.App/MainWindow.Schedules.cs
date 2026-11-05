using System;
using Avalonia.Interactivity;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>The main window's side of Tools ▸ Schedules, File ▸ Saved games and the Level menu's new and cleared levels.</summary>
    public partial class MainWindow
    {
        void OnSchedules(object sender, RoutedEventArgs e) { ShowSchedules(); }
        void OnSavedGames(object sender, RoutedEventArgs e) { ShowSavedGames(); }
        void OnMakeSlotALevel(object sender, RoutedEventArgs e) { MakeSlotALevel(); }
        void OnClearLevel(object sender, RoutedEventArgs e) { ClearLevel(false); }
        void OnClearLevelAndTiles(object sender, RoutedEventArgs e) { ClearLevel(true); }

        ScheduleWindow scheduleWindow;

        /// <summary>Tools ▸ Schedules: UW2's SCD.ARK (the save's, or DATA's). Explains and returns null for UW1.</summary>
        public ScheduleWindow ShowSchedules()
        {
            if (vm.Session == null) return null;
            if (!vm.Session.IsUw2)
            {
                _ = MessageWindow.Show(this, "Schedules", "Schedules belong to Ultima Underworld II. Its SCD.ARK holds the events that move NPCs about by the time of day and carry the plot along as the X clocks advance. Ultima Underworld I has no schedules.");
                return null;
            }
            if (scheduleWindow != null)
            {
                // a window on another game's file stays while it has unsaved edits
                if (scheduleWindow.Session == vm.Session || scheduleWindow.HasChanges) { scheduleWindow.Activate(); return scheduleWindow; }
                scheduleWindow.CloseQuietly();
            }
            Schedules sc;
            try { sc = Schedules.Load(vm.Session); }
            catch (Exception ex) { _ = MessageWindow.Show(this, "Schedules", ex.Message); return null; }
            var w = new ScheduleWindow(this, sc);
            scheduleWindow = w;
            w.Closed += (s, e) => { if (scheduleWindow == w) scheduleWindow = null; };
            MessageWindow.GuardClose(w, "Schedules", () => w.HasChanges, w.SaveAll, () => AskBeforeDiscarding && !quietClose && !w.Quiet);
            w.Show(this);
            return w;
        }

        SaveSlotsWindow saveSlotsWindow;

        /// <summary>File ▸ Saved games: the game folder's SAVE1 to SAVE4.</summary>
        public SaveSlotsWindow ShowSavedGames()
        {
            if (vm.Session == null) return null;
            if (saveSlotsWindow != null) { saveSlotsWindow.Refresh(); saveSlotsWindow.Activate(); return saveSlotsWindow; }
            saveSlotsWindow = new SaveSlotsWindow(this);
            saveSlotsWindow.Closed += (s, e) => saveSlotsWindow = null;
            saveSlotsWindow.Show(this);
            return saveSlotsWindow;
        }

        // ---- new and cleared levels

        /// <summary>Level ▸ Make this slot a level: asks, then puts a new level into the empty slot shown.</summary>
        public async void MakeSlotALevel()
        {
            var level = vm.Level;
            if (vm.Session == null || level == null) return;
            if (level.Present)
            {
                await MessageWindow.Show(this, "Make this slot a level", "This slot already holds a level. Level ▸ Clear the level empties it instead.");
                return;
            }
            string a = await MessageWindow.Ask(this, "Make this slot a level",
                "Slot " + level.Level + " holds no level, and the game cannot enter it. Make it a new, empty level? " +
                "It gets no objects, a texture map" + (vm.Session.IsUw2 ? " copied from its world" : "") + " and either a small open room in the middle (5 by 5 tiles at (30, 30)-(34, 34)) or nothing but rock. " +
                "It is one Edit ▸ Undo step, written when you save the levels.",
                "With a room", "All rock", "Cancel");
            if (a == "Cancel") return;
            MakeSlotALevelNow(a == "With a room");
        }

        /// <summary>Puts a new level into the empty slot shown, without asking. False when the slot holds a level.</summary>
        public bool MakeSlotALevelNow(bool room)
        {
            var level = vm.Level;
            if (vm.Session == null || vm.Edits == null || level == null || level.Present) return false;
            var s = vm.Session;
            int l = level.Level;
            var tex = NewLevels.TextureMap(s, l, x => vm.Edits.Existing(x) != null ? vm.Edits.Existing(x).TextureBytes : (s.LevArk.Has(s.Profile.TextureBlock(x)) ? s.LevArk.Read(s.Profile.TextureBlock(x)) : null));
            vm.WatchLevelSlots();
            vm.Edits.CreateLevel(l, NewLevels.EmptyBlock(s.Kind, room), tex, "Make slot " + l + " a level");
            vm.FlushRefresh();
            vm.RefreshLevelSlots();
            vm.LoadLevel(l);                 // as if picked: the view, markers and camera start on the new level
            vm.EditStatus = "Slot " + l + " is a new level" + (room ? " with a room at (30, 30)-(34, 34)." : " of solid rock.") + " Save the levels to write it.";
            return true;
        }

        /// <summary>Level ▸ Clear the level: asks, then removes every object of the level shown (and with tilesToo makes every tile rock).</summary>
        public async void ClearLevel(bool tilesToo)
        {
            if (vm.Editor == null) return;
            string a = await MessageWindow.Ask(this, "Clear the level",
                "Remove every object of level " + vm.Level.Level + (tilesToo ? " and make every tile solid rock" : "") + "? " +
                "The free lists start again as in a new level; creatures, items, traps and triggers all go" + (vm.Session.IsUw2 ? ", and so do the animations and timers" : "") + ". " +
                "It is one Edit ▸ Undo step, written when you save the levels.",
                "Clear", "Cancel");
            if (a != "Clear") return;
            ClearLevelNow(tilesToo);
        }

        /// <summary>Clears the level shown without asking. False when there is no level to clear.</summary>
        public bool ClearLevelNow(bool tilesToo)
        {
            var e = vm.Editor;
            if (e == null) return false;
            var kind = vm.Session.Kind;
            e.EditBytes(tilesToo ? "Clear the level and its tiles" : "Clear the level", (d, t) =>
            {
                NewLevels.ResetObjects(d, kind);
                if (tilesToo) NewLevels.MakeAllSolid(d);
            });
            vm.FlushRefresh();
            bool ok;
            string report = e.Check(out ok);
            vm.EditStatus = "Level " + e.Level + " cleared" + (tilesToo ? " to solid rock" : "") + (ok ? "." : "; the list check fails: " + report);
            return ok;
        }
    }
}
