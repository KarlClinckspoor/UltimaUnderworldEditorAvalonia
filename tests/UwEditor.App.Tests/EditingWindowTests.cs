using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Level editing through the main window and the add, keys, strings, triggers and mods windows.</summary>
    public class EditingWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from, params string[] dirs)
            {
                foreach (var d in dirs)
                {
                    string src = Path.Combine(from, d);
                    if (!Directory.Exists(src)) continue;
                    Directory.CreateDirectory(Path.Combine(Folder, d));
                    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(Folder, d, Path.GetFileName(f)));
                }
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
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
        public void Typing_a_value_in_the_properties_panel_edits_the_object_and_undo_restores_it()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Select(942, -1, -1, true);
            Ui.Pump();
            var row = vm.ObjectRows.First(r => r.Name == "Quality" && r.Editable);
            vm.EditField(row, "0x15");
            vm.FlushRefresh();
            Assert.Equal(0x15, vm.Level.Objects[942].Quality);
            Assert.True(vm.HasLevelChanges);
            Assert.True(vm.CanUndo);
            Assert.Contains("Quality", vm.UndoText);
            w.Undo();
            Assert.Equal(40, vm.Level.Objects[942].Quality);
            // the object is still selected and the panel shows the old value
            Assert.Equal(942, vm.SelectedObject);
            Assert.Equal("40", vm.ObjectRows.First(r => r.Name == "Quality").Value);
            vm.EditField(vm.ObjectRows.First(r => r.Name == "Quality"), "99");   // out of range: refused
            Assert.Contains("63", vm.EditStatus);
            w.Close();
        }

        [AvaloniaFact]
        public void Painting_the_light_flag_is_one_undo_step_and_ctrl_wheel_raises_a_floor()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.ToolIndex = 2;
            bool lit = (vm.Level.Tiles[32, 3].Light & 1) != 0;
            vm.PaintMode = lit ? 1 : 0;   // the other way
            var ed = vm.Editor;
            ed.BeginBatch("paint");
            foreach (var x in new[] { 31, 32, 33 }) vm.Paint(x, 3);
            ed.EndBatch();
            vm.FlushRefresh();
            Assert.All(new[] { 31, 32, 33 }, x => Assert.Equal(lit ? 0 : 1, vm.Level.Tiles[x, 3].Light & 1));
            Assert.True(vm.CanUndo);
            w.Undo();
            Assert.False(vm.CanUndo);
            vm.PaintMode = 2;   // floor texture: the slot list fills with the level's floors
            Assert.Equal(vm.Level.FloorMap.Length, vm.Slots.Count);
            Assert.All(vm.Slots, s => Assert.NotNull(s.Picture));
            int h = vm.Level.Tiles[32, 3].Height;
            Assert.True(vm.Edit(e => e.RaiseFloor(32, 3, 1)));
            vm.FlushRefresh();
            Assert.Equal(Math.Min(15, h + 1), vm.Level.Tiles[32, 3].Height);
            w.Close();
        }

        [AvaloniaFact]
        public void The_add_window_changes_its_form_with_the_kind_of_item()
        {
            if (Uw1 == null) return;
            var s = GameSession.Open(Uw1);
            var w = new AddObjectWindow(s, null, false, "test");
            w.Show();
            Ui.Pump();
            var m = w.Model;
            m.SetItem(0x153);   // a boulder: the simple form
            Ui.Pump();
            Assert.True(m.IsBasic && m.HasQuantity);
            Assert.Equal(Races.CanBeOwned(s, 0x153), m.CanOwn);
            Assert.False(m.IsEnchantable);
            Assert.False(Ui.Find<ComboBox>(w, "EnchantmentBox").IsEffectivelyVisible);
            m.SetItem(0x005);   // a longsword: enchantments with what they do
            Ui.Pump();
            Assert.True(m.IsEnchantable);
            Assert.True(Ui.Find<ComboBox>(w, "EnchantmentBox").IsEffectivelyVisible);
            Assert.False(m.ShowUw2Warning);
            int acc = m.EnchantmentList.FindIndex(e => e.Name.Contains("Great Accuracy"));
            Assert.True(acc > 0);
            m.EnchantmentIndex = acc;
            var spec = m.Build();
            Assert.True(spec.Enchanted && spec.IsQuantity);
            Assert.Equal(0x200 | 0xC0 | (spec.Link & 0xF), spec.Link);
            Assert.Contains("class 12", m.EnchantmentEffect);
            m.SetItem(0x098);   // a wand: a spell object inside with charges
            m.SpellIndex = 3; m.Charges = 5;
            spec = m.Build();
            Assert.False(spec.IsQuantity);
            Assert.Single(spec.Contents);
            Assert.Equal(0x120, spec.Contents[0].Item);
            Assert.Equal(5, spec.Contents[0].Quality);
            m.SetItem(0x102);   // a key: its lock and its description
            m.LockNumber = 3;
            Assert.True(m.IsKey && !m.CanOwn);
            Assert.Equal(3, m.Build().Owner);
            string kt = s.Strings.Get(5, 0x67);
            Assert.Equal(string.IsNullOrWhiteSpace(kt) ? "(no description: block 5, 103)" : kt.Trim(), m.KeyText);
            m.SetItem(0x1A0);   // a trigger: its description
            Assert.True(m.IsTrap);
            Assert.Contains("Move trigger", m.TrapHelp);
            w.Close();

            if (Uw2 == null) return;
            var s2 = GameSession.Open(Uw2);
            var w2 = new AddObjectWindow(s2, null, true, "test");
            w2.Show();
            Ui.Pump();
            w2.Model.SetItem(0x005);
            Ui.Pump();
            Assert.True(w2.Model.ShowUw2Warning);
            Assert.True(Ui.Find<Border>(w2, "Uw2WarningBox").IsEffectivelyVisible);
            // the inventory list leaves out what cannot be carried
            Assert.DoesNotContain(w2.Model.Items, i => i.Item == 0x153 || i.Item == 0x1A0 || i.Item >= 0x40 && i.Item < 0x80);
            w2.Close();
        }

        [AvaloniaFact]
        public void An_object_added_in_the_level_is_selected_and_delete_takes_it_away()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var m = new AddObjectModel(vm.Session, null, false);
            m.SetItem(0x005);
            m.EnchantmentIndex = 1;
            int added = -1;
            Assert.True(vm.Edit(e => added = e.AddObject(m.Build(), 32, 3)));
            vm.FlushRefresh();
            vm.Select(added, 32, 3, true);
            Assert.Equal(0x005, vm.Level.Objects[added].Item);
            Assert.NotNull(vm.SelectedNode);
            Assert.Contains("·", vm.SelectedNode.Header);      // the enchantment's name in the list
            w.DeleteSelected();
            Assert.True(vm.Level.Objects[added].Free);
            Assert.True(vm.Level.CheckOk, vm.Level.CheckReport);
            w.Close();
        }

        [AvaloniaFact]
        public void An_owner_can_be_chosen_from_the_races_and_named_npcs_show_in_the_list()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Select(942, -1, -1, true);
            var owner = vm.ObjectRows.First(r => r.Name == "Owner");
            Assert.True(owner.HasChoices);
            Assert.Contains(owner.Choices, c => c.Label.Contains("goblin"));
            Assert.Equal("0: nobody", owner.Choices[0].Label);
            // Corby (level 2) talks under his name
            vm.LoadLevel(2);
            Assert.Contains(vm.Tree.SelectMany(Flatten), n => n.Header.Contains("fighter (Corby)"));
            Assert.Equal("Caliburn, the Sword of Justice", NamedItems.Of(UltimateEditor.Games.GameKind.UW1, 0x00A));
            w.Close();
        }

        static System.Collections.Generic.IEnumerable<TreeNode> Flatten(TreeNode n)
        {
            yield return n;
            foreach (var c in n.Children) foreach (var d in Flatten(c)) yield return d;
        }

        [AvaloniaFact]
        public void A_trigger_shows_its_help_and_target_and_its_target_can_be_chosen()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var trig = vm.Level.Objects.First(o => !o.Free && o.TileX >= 0 && Triggers.IsTrigger(o.Item));
            vm.Select(trig.Index, trig.TileX, trig.TileY, true);
            Assert.True(vm.IsTrapSelected && vm.HasTargetSquare);
            Assert.Contains("Target square", vm.ObjectHelp);
            Assert.Contains(vm.Targets, t => t.X == trig.Quality && t.Y == trig.Owner);
            Assert.True(Ui.Find<Button>(w, "PickTargetButton").IsEffectivelyVisible);
            Assert.True(vm.SetTarget(1, trig.Index, 1, 30, 4));
            vm.FlushRefresh();
            Assert.Equal(30, vm.Level.Objects[trig.Index].Quality);
            Assert.Equal(4, vm.Level.Objects[trig.Index].Owner);
            // the chains window lists it
            var tw = w.ShowTriggers();
            Ui.Pump();
            Assert.Contains(tw.Model.Nodes, n => n.Index == trig.Index && n.Header.Contains("runs on (30, 4)"));
            tw.Model.Selected = tw.Model.Nodes.First(n => n.Index == trig.Index);
            Assert.Contains("Acts on", tw.Model.Help);
            tw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void First_person_stands_on_the_selected_tile_and_walls_stop_the_walk()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Select(-1, 32, 3, true);
            w.ToggleFirstPerson();
            var v = Ui.Find<LevelView>(w, "View");
            Assert.True(v.FirstPerson);
            var start = v.Camera.Target;
            for (int i = 0; i < 400; i++) v.Walk(0.25f, 0);
            var t = v.Camera.Target;
            Assert.True(t.X >= 0 && t.X < 64 && t.Z >= 0 && t.Z < 64);
            Assert.False(vm.Level.Tiles[(int)t.X, (int)t.Z].IsSolid);   // never inside rock
            w.ToggleFirstPerson();
            Assert.False(v.FirstPerson);
            w.Close();
        }

        [AvaloniaFact]
        public void Keys_window_lists_lock_numbers_and_edits_a_key_description()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var kw = w.ShowKeys();
                kw.Loading.Wait(60000);
                Ui.Pump();
                var m = kw.Model;
                Assert.Equal(19, m.Groups.Count);
                var g = m.Groups.First(x => x.Keys.Count > 0);
                m.Selected = g;
                Assert.NotEmpty(m.Keys);
                m.EditText = "a key with a new description";
                Assert.True(m.Apply());
                Assert.True(m.HasChanges);
                kw.Model.Strings.Save();
                Assert.Equal("a key with a new description", w.ViewModel.Session.Strings.Get(5, 0x64 + g.Id));
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "STRINGS.PAK.ORIG")));
                kw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Strings_window_edits_an_item_name_and_the_list_shows_it_after_saving()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var sw = w.ShowStrings(4, 0x80);
                Ui.Pump();
                var m = sw.Model;
                Assert.Equal(4, m.Block.Number);
                Assert.Equal(0x80, m.Row.Index);
                m.EditText = "a_satchel&satchels";
                Assert.True(m.Apply());
                Assert.True(m.HasChanges);
                m.EditText = "ünïcode ✓";
                Assert.False(m.Apply());          // the file's tree cannot write these
                Assert.NotEmpty(m.Error);
                Assert.True(m.Save());
                Ui.Pump();
                Assert.Equal("satchel", w.ViewModel.Level.Objects[942].Name);
                Assert.Contains(w.ViewModel.Tree.SelectMany(Flatten), n => n.Header.Contains("satchel"));
                m.Search = "satchel";
                m.SearchAll();
                Assert.Contains(m.Rows, r => r.Block == 4 && r.Index == 0x80);
                sw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void The_start_arrow_moves_by_patching_the_executable_and_a_mod_carries_only_the_patch()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            using (var other = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var vm = w.ViewModel;
                Assert.True(w.MoveMarker("start", 1, 31, 3));
                Assert.Equal(31.5, vm.Session.NewGameStart.X);
                Assert.Equal(3.5, vm.Session.NewGameStart.Y);
                Assert.False(w.MoveMarker("start", 2, 31, 3));     // a new game starts on level 1
                // and a level edit, saved
                vm.Select(942, -1, -1, true);
                vm.EditField(vm.ObjectRows.First(r => r.Name == "Quality"), "12");
                Assert.True(w.SaveAll());
                var mw = w.ShowMods();
                var pkg = mw.Model.Collect();
                Assert.NotNull(pkg);
                Assert.Contains(pkg.Components, c => c.Kind == "bytes" && c.File == "UW.EXE" && c.Patches.Sum(p => p.New.Length) <= 3);
                Assert.Contains(pkg.Components, c => c.Kind == "ark-blocks" && c.File.EndsWith("LEV.ARK"));
                string mod = Path.Combine(copy.Folder, "test.uwmod");
                mw.Model.Write(mod);
                mw.Close();
                w.Close();

                // apply to an untouched copy
                var w2 = OpenQuiet(other.Folder);
                var mw2 = w2.ShowMods();
                mw2.Model.Load(mod);
                Assert.Contains("fits this game", mw2.Model.Status);
                Assert.True(mw2.Model.Apply());
                mw2.Model.RecheckLoaded();
                Assert.Contains("already applied", mw2.Model.Status);
                mw2.Close();
                w2.OpenPathNow(other.Folder);
                Assert.Equal(31.5, w2.ViewModel.Session.NewGameStart.X);
                Assert.Equal(12, w2.ViewModel.Level.Objects[942].Quality);
                w2.Close();
            }
        }

        [AvaloniaFact]
        public void The_player_arrow_moves_to_another_level_and_is_saved_in_player_dat()
        {
            if (Uw2 == null || !Directory.Exists(Path.Combine(Uw2, "SAVE1"))) return;
            using (var copy = new GameCopy(Uw2, "DATA", "SAVE1"))
            {
                var w = OpenQuiet(Path.Combine(copy.Folder, "SAVE1"));
                var vm = w.ViewModel;
                Assert.NotNull(vm.Session.PlayerPoint);
                Assert.NotNull(vm.Session.NewGameStart);
                int other = vm.Session.PlayerPoint.Level == 1 ? 2 : 1;
                vm.LoadLevel(other);
                int tx, ty;
                vm.Level.Centre(out tx, out ty);
                Assert.True(w.MoveMarker("player", other, tx, ty));
                Assert.Equal(other, vm.Session.PlayerPoint.Level);
                Assert.True(w.SaveAll());
                var again = GameSession.Open(Path.Combine(copy.Folder, "SAVE1"));
                Assert.Equal(other, again.Player.SavedLevel);
                Assert.Equal(tx, again.Player.TileX);
                Assert.Equal(ty, again.Player.TileY);
                w.Close();
            }
        }
    }
}
