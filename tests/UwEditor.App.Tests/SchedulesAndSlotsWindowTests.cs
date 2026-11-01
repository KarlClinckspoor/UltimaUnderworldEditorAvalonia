using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using UltimateEditor.Games;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Schedules, File ▸ Saved games, and the Level menu's new and cleared levels.</summary>
    public class SchedulesAndSlotsWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class Copy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-slots-" + Guid.NewGuid().ToString("N"));
            public Copy(string from, params string[] dirs)
            {
                foreach (var d in dirs)
                {
                    string src = Path.Combine(from, d);
                    if (!Directory.Exists(src)) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        [AvaloniaFact]
        public void The_schedules_window_edits_and_saves_the_saves_scd_ark()
        {
            if (Uw2 == null) return;
            using (var c = new Copy(Uw2, "DATA", "SAVE1"))
            {
                var w = OpenQuiet(Path.Combine(c.Folder, "SAVE1"));
                var sw = w.ShowSchedules();
                Ui.Pump();
                Assert.NotNull(sw);
                Assert.Same(sw, w.ShowSchedules());
                Assert.Equal("SAVE1", sw.Schedules.Where);
                Assert.False(sw.HasChanges);
                sw.SelectBlock(1);
                Assert.Equal(1, sw.Block);
                Assert.Equal(0, sw.Row);
                int n = sw.Schedules.Blocks[1].Rows.Count;
                sw.AddRow(false);
                Assert.Equal(n + 1, sw.Schedules.Blocks[1].Rows.Count);
                Assert.Equal(1, sw.Row);
                Assert.True(sw.SetField("Event", 4));
                Assert.True(sw.SetField("Quest flag", 12));
                Assert.Contains("quest flag 12", sw.Schedules.Describe(sw.Schedules.Blocks[1].Rows[1]));
                Assert.False(sw.SetField("Time", 300));               // X clocks are bytes
                Assert.Contains("0 to 255", sw.StatusText);
                Assert.True(sw.SetField("Time", 255));
                Assert.Equal(n, sw.Row);                               // moved to the end by its time
                Assert.True(sw.HasChanges);
                Assert.True(sw.SaveAll());
                Assert.False(sw.HasChanges);
                var again = Schedules.Load(GameSession.Open(Path.Combine(c.Folder, "SAVE1")));
                Assert.Equal(n + 1, again.Blocks[1].Rows.Count);
                Assert.Equal(255, again.Blocks[1].Rows[n].Time);
                // DATA's file was not touched
                Assert.Equal(File.ReadAllBytes(Path.Combine(Uw2, "DATA", "SCD.ARK")), File.ReadAllBytes(GameProfile.Find(Path.Combine(c.Folder, "DATA"), "SCD.ARK")));
                sw.DeleteRow();
                Assert.True(sw.HasChanges);
                sw.Revert();
                Assert.False(sw.HasChanges);
                sw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Schedules_are_not_offered_for_ultima_underworld()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            Assert.Null(w.ShowSchedules());
            Ui.Pump();
            w.Close();
        }

        [AvaloniaFact]
        public void The_saved_games_window_renames_copies_and_opens_slots()
        {
            if (Uw2 == null) return;
            using (var c = new Copy(Uw2, "DATA", "SAVE1", "SAVE2"))
            {
                var w = OpenQuiet(Path.Combine(c.Folder, "SAVE1"));
                var sg = w.ShowSavedGames();
                Ui.Pump();
                Assert.Equal(4, sg.Slots.Count);
                Assert.Equal("Karl", sg.Slots[0].Description);
                Assert.True(sg.Rename(2, "Second try"));
                Assert.Equal("Second try", sg.Slots[1].Description);
                Assert.False(sg.CopyNow(2, 1));                       // SAVE1 is open
                Assert.Contains("open", sg.StatusText);
                Assert.True(sg.CopyNow(2, 3));
                Assert.Equal("Second try", sg.Slots[2].Description);
                Assert.True(sg.CopyNow(1, 2));
                Assert.True(Directory.Exists(Path.Combine(c.Folder, "SAVE2.BKP")));
                Assert.Equal("Karl", sg.Slots[1].Description);
                sg.Open(3);
                Ui.Pump();
                Assert.EndsWith("SAVE3", w.ViewModel.Session.SaveFolder);
                sg.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void An_empty_slot_becomes_a_level_that_saves_and_takes_objects()
        {
            if (Uw2 == null) return;
            using (var c = new Copy(Uw2, "DATA"))
            {
                var w = OpenQuiet(c.Folder);
                var vm = w.ViewModel;
                int slot = vm.Session.LevelSlots().First(x => !x.Present).Level;
                vm.LoadLevel(slot);
                Assert.Null(vm.Editor);
                Assert.False(w.ClearLevelNow(false));
                Assert.True(w.MakeSlotALevelNow(true));
                Ui.Pump();
                Assert.True(vm.Level.Present);
                Assert.NotNull(vm.Editor);
                Assert.True(vm.Level.CheckOk, vm.Level.CheckReport);
                Assert.Equal(25, vm.Level.OpenTileCount);
                Assert.DoesNotContain("(empty)", vm.SelectedSlot.Name);
                Assert.False(w.MakeSlotALevelNow(true));              // it holds a level now
                int added = -1;
                Assert.True(vm.Edit(e => added = e.AddObject(new ObjectSpec { Item = 0x0A0 }, 32, 32)));
                vm.FlushRefresh();
                Assert.True(vm.Level.CheckOk, vm.Level.CheckReport);
                Assert.Equal(1, vm.Level.UsedObjectCount);
                vm.Edits.Save();
                var s2 = GameSession.Open(c.Folder);
                var m = s2.LoadLevel(slot);
                Assert.True(m.Present);
                Assert.True(m.CheckOk, m.CheckReport);
                Assert.Equal(0x0A0, m.Objects[added].Item);
                // undo twice: an empty slot again
                w.Undo(); w.Undo();
                Ui.Pump();
                vm.FlushRefresh();
                Assert.False(vm.Level.Present);
                Assert.Contains("(empty)", vm.LevelsInWorld.First(x => x.Level == slot).Name);
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Clearing_the_level_is_one_undo_step()
        {
            if (Uw1 == null) return;
            using (var c = new Copy(Uw1, "DATA"))
            {
                var w = OpenQuiet(c.Folder);
                var vm = w.ViewModel;
                int objects = vm.Level.UsedObjectCount, open = vm.Level.OpenTileCount;
                Assert.True(w.ClearLevelNow(false));
                vm.FlushRefresh();
                Assert.Equal(0, vm.Level.UsedObjectCount);
                Assert.Equal(open, vm.Level.OpenTileCount);
                Assert.True(vm.Level.CheckOk, vm.Level.CheckReport);
                Assert.True(w.ClearLevelNow(true));
                vm.FlushRefresh();
                Assert.Equal(0, vm.Level.OpenTileCount);
                w.Undo();
                vm.FlushRefresh();
                Assert.Equal(open, vm.Level.OpenTileCount);
                w.Undo();
                vm.FlushRefresh();
                Assert.Equal(objects, vm.Level.UsedObjectCount);
                Assert.True(vm.Level.CheckOk, vm.Level.CheckReport);
                w.Close();
            }
        }
    }
}
