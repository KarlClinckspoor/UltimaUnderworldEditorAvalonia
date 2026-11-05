using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia;
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
                Assert.Equal(new[] { "DATA/LEV.ARK", "UW.EXE" }, pkg.Files.Select(f => f.Path));
                // the executable's change is the start square and nothing else
                var exe = pkg.Files.Single(f => f.Path == "UW.EXE");
                var exeOrig = File.ReadAllBytes(Path.Combine(copy.Folder, "UW.EXE.ORIG"));
                Assert.True(Underworld.Mods.ExeChanges.OnlyStart(exeOrig, pkg.BuildOne(exe, exeOrig), UltimateEditor.Games.GameKind.UW1));
                Assert.True(exe.Delta.Length < 32);
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
    
        [AvaloniaFact]
        public void Models_window_shows_the_built_in_models_and_exports_obj()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var mw = w.ShowModels();
            Ui.Pump();
            var m = mw.Model;
            Assert.Equal(32, m.Models.Count);
            var frame = m.Models.First(x => x.Index == 1);
            Assert.Equal(528, frame.Budget);
            Assert.True(frame.FaceCount > 0);
            mw.Select(0x0B);
            Assert.Contains("Shrine", m.Info);
            Assert.Contains("0x157", m.Info);
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-obj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                int n = m.ExportAll(dir);
                Assert.True(n >= 20);
                string obj = File.ReadAllText(Path.Combine(dir, ModelsViewModel.FileName(m.Models.First(x => x.Index == 0x0B))));
                Assert.Contains("mtllib", obj);
                Assert.True(obj.Split('\n').Count(l => l.StartsWith("v ")) == m.Models.First(x => x.Index == 0x0B).VertexCount);
                Assert.Contains("\nf ", obj);
                Assert.True(Directory.GetFiles(dir, "*.mtl").Length == n);
            }
            finally { Directory.Delete(dir, true); }
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_manual_opens_with_its_sections_and_finds_text()
        {
            var w = Ui.Open(null);
            var mw = ManualWindow.ShowAt(w, "3D models");
            Ui.Pump();
            Assert.Contains("Editing levels", mw.Sections);
            Assert.Contains("Mods", mw.Sections);
            Assert.True(mw.GoTo("triggers"));
            Assert.True(mw.Find("Ctrl+wheel", true) >= 2);
            Assert.Equal(0, mw.Find("no such words anywhere", true));
            mw.Close();
            w.Close();
        }
    
        [AvaloniaFact]
        public void The_add_window_groups_items_by_kind_in_a_tree()
        {
            if (Uw1 == null) return;
            var s = GameSession.Open(Uw1);
            var m = new AddObjectModel(s, null, false);
            Assert.Contains(m.Groups, g => g.Label.StartsWith("Melee weapons"));
            Assert.Contains(m.Groups, g => g.Label.StartsWith("Traps"));
            Assert.All(m.Groups, g => Assert.False(g.IsExpanded));
            Assert.Equal(m.Items.Count, m.Groups.Sum(g => g.Children.Count));
            m.Filter = "boulder";
            Assert.All(m.Groups, g => Assert.True(g.IsExpanded));
            Assert.All(m.Groups.SelectMany(g => g.Children), c => Assert.Contains("boulder", c.Label));
            var group = m.Groups[0];
            m.Selected = group;                     // a group opens or closes, nothing is chosen
            Assert.Equal(-1, m.Item);
            m.Filter = "";
            var sword = m.Items.First(c => c.Item == 0x005);
            m.Selected = sword;
            Assert.True(m.Groups.First(g => g.Children.Contains(sword)).IsExpanded);
            // a spell on a weapon: the note says a swing ignores it
            m.EnchantmentIndex = m.EnchantmentList.FindIndex(e => e.Flag11);
            Assert.Contains("swing ignores it", m.EnchantmentEffect);
        }

        [AvaloniaFact]
        public void Flags_are_checkboxes_small_fields_have_steppers_and_the_heading_an_arrow()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            vm.Select(942, -1, -1, true);
            Ui.Pump();
            var inv = vm.ObjectRows.First(r => r.Name == "Invisible");
            Assert.True(inv.IsFlag && !inv.IsNumber);
            var fx = vm.ObjectRows.First(r => r.Name == "Fine X");
            Assert.True(fx.HasSteppers);
            Assert.Equal(7, fx.Maximum);
            var head = vm.ObjectRows.First(r => r.Name == "Heading");
            Assert.True(head.IsHeading);
            Assert.False(vm.ObjectRows.First(r => r.Name == "Link").HasSteppers);
            // the + button steps the field and the edit lands in the level
            var view = w.GetLogicalDescendants().OfType<PropertyRowView>().First(v => v.DataContext == fx);
            int before = vm.Level.Objects[942].FineX;
            var up = view.GetLogicalDescendants().OfType<RepeatButton>().First(b => b.Name == "Up");
            Ui.Click(w, up);
            vm.FlushRefresh();
            Assert.Equal(Math.Min(7, before + 1), vm.Level.Objects[942].FineX);
            // ticking the invisible box sets the bit
            var invView = w.GetLogicalDescendants().OfType<PropertyRowView>().First(v => (v.DataContext as PropertyRow)?.Name == "Invisible");
            var box = invView.GetLogicalDescendants().OfType<CheckBox>().First();
            Ui.Click(w, box);
            vm.FlushRefresh();
            Assert.True(vm.Level.Objects[942].Invisible);
            w.Close();
        }

        [AvaloniaFact]
        public void The_light_levels_follow_the_game_and_the_colour_scheme_switches()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            Assert.Equal(9, vm.LightChoices.Count);
            Assert.NotNull(vm.Light);
            Assert.Equal(15, vm.Light.ShadeAt(0, 4));      // with no light nothing past 3 squares shows
            Assert.Equal(5, vm.Light.ShadeAt(0, 0));
            Assert.Equal(0, vm.Light.ShadeAt(4, 0));       // a lantern lights the player's square fully
            Assert.True(vm.Light.ShadeAt(7, 6) < 15);
            vm.LightIndex = 3;   // torch
            Assert.True(vm.Darkness);
            Assert.Equal(2, vm.EffectiveLight);
            Assert.Contains("4 squares", vm.LightNote);
            vm.Darkness = false;
            Assert.Equal(-1, vm.EffectiveLight);
            w.SetTheme(ColourScheme.Light);
            Ui.Pump();
            Assert.True(ColourScheme.IsLight);
            w.SetTheme(ColourScheme.Dark);
            Assert.False(ColourScheme.IsLight);
            w.Close();
        }

        [AvaloniaFact]
        public void Checkpoints_keep_unsaved_levels_and_restore_them_as_edits()
        {
            if (Uw1 == null) return;
            string dir = Path.Combine(Path.GetTempPath(), "uwedit-cp-" + Guid.NewGuid().ToString("N"));
            try
            {
                var w = OpenQuiet(Uw1);
                var vm = w.ViewModel;
                Assert.NotNull(w.CheckpointStore);
                Assert.Null(w.WriteCheckpoint("test", true));          // nothing unsaved yet
                vm.Select(942, -1, -1, true);
                vm.EditField(vm.ObjectRows.First(r => r.Name == "Quality"), "7");
                vm.FlushRefresh();
                var c = w.WriteCheckpoint("test", true);
                Assert.NotNull(c);
                Assert.Equal(new[] { 1 }, c.Levels);
                w.Undo();
                Assert.Equal(40, vm.Level.Objects[942].Quality);
                string said = w.RestoreCheckpoint(c);
                Assert.Contains("level 1", said);
                Assert.Equal(7, vm.Level.Objects[942].Quality);
                Assert.True(vm.CanUndo);
                // only the newest are kept
                var store = new Checkpoints(dir, vm.Session);
                for (int k = 0; k < 4; k++) { store.Write(vm.Edits, null, "k" + k, 2); System.Threading.Thread.Sleep(5); }
                Assert.Equal(2, store.List().Count);
                Assert.Equal("k3", store.List()[0].Label);
                foreach (var x in w.CheckpointStore.List()) w.CheckpointStore.Delete(x);
                w.Close();
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
    
        [AvaloniaFact]
        public void Item_shuffle_window_shuffles_and_undoes_every_change()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var sw = w.ShowItemShuffle();
                Ui.Pump();
                var edits = w.ViewModel.Edits;
                var before = Enumerable.Range(1, 9).ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
                sw.Options.Seed = 11;
                sw.Shuffle();
                Ui.Pump();
                Assert.True(sw.CanUndoShuffle);
                Assert.Contains("Shuffled", sw.ReportText);
                Assert.True(edits.HasChanges);
                Assert.Contains(before, kv => !kv.Value.SequenceEqual(edits.For(kv.Key).Data));
                sw.UndoAll();
                Ui.Pump();
                Assert.False(sw.CanUndoShuffle);
                foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
                sw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Item_swap_window_swaps_only_the_ticked_kinds_and_undoes()
        {
            if (Uw1 == null) return;
            // the swap's saved settings may hold an emptied ignore list from another test (ItemRandomizerWindowTests)
            System.IO.File.Delete(System.IO.Path.Combine(Settings.Folder, "item-swap-uw1.json"));
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var sw = w.ShowItemSwap();
                Ui.Pump();
                var edits = w.ViewModel.Edits;
                var before = Enumerable.Range(1, 9).ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
                var set = sw.Settings_;
                set.Seed = 5; set.AnyKind = false;
                set.Weapons = true; set.Armour = set.Food = set.Lights = set.Tools = set.Runes = set.Wands = set.Scrolls = set.Potions = set.Rings = false;
                var o = sw.Options();
                Assert.Equal("swap-kind", o.Mode);
                Assert.Contains(o.Ignored, x => x.Label.Contains("bullfrog"));
                sw.Swap();
                Ui.Pump();
                Assert.True(sw.CanUndoSwap);
                // only weapons moved: every changed object is a weapon, before and after
                for (int l = 1; l <= 9; l++)
                {
                    var a = w.ViewModel.Session.LoadLevel(l); var b = edits.Model(l);
                    for (int k = 0; k < a.Objects.Length; k++)
                        if (a.Objects[k].Item != b.Objects[k].Item) { Assert.True(a.Objects[k].Item < 0x20); Assert.True(b.Objects[k].Item < 0x20); }
                }
                sw.UndoAll();
                Ui.Pump();
                foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
                sw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Monster_swap_window_swaps_and_undoes()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var mw = w.ShowMonsterSwap();
                Ui.Pump();
                var edits = w.ViewModel.Edits;
                var before = Enumerable.Range(1, 9).ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
                Assert.Contains(mw.Options.Whitelist, x => x.Level == 2);           // the gazer
                mw.Options.Seed = 8; mw.Options.Scope = "level";
                mw.Swap();
                Ui.Pump();
                Assert.True(mw.CanUndoSwap);
                Assert.Contains("took another's place", mw.ReportText);
                Assert.Contains(before, kv => !kv.Value.SequenceEqual(edits.For(kv.Key).Data));
                mw.UndoAll();
                Ui.Pump();
                foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
                mw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Changing_level_keeps_the_place_and_a_teleport_leads_to_its_target()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var vm = w.ViewModel;
            var view = w.FindControl<LevelView>("View");
            view.LookAt(new System.Numerics.Vector3(20.5f, 1f, 30.5f), 9f, 33f, 48f);
            vm.LoadLevel(2);
            Ui.Pump();
            Assert.Equal(2, vm.Level.Level);
            Assert.Equal(20.5f, view.Camera.Target.X, 3);
            Assert.Equal(30.5f, view.Camera.Target.Z, 3);
            Assert.Equal(9f, view.Camera.Distance, 3);
            Assert.Equal(33f, view.Camera.Yaw, 3);
            // level 1's teleport #985 leads to (35, 20) on level 2
            vm.LoadLevel(1);
            vm.Select(985, -1, -1, false);
            Ui.Pump();
            Assert.True(vm.IsTeleportSelected);
            Assert.Contains(vm.Targets, t => t.Level == 2 && t.X == 35 && t.Y == 20);
            Assert.True(w.GoToTeleportTarget());
            Ui.Pump();
            Assert.Equal(2, vm.Level.Level);
            Assert.Equal(35, vm.SelectedTileX);
            Assert.Equal(20, vm.SelectedTileY);
            w.Close();
        }

        [AvaloniaFact]
        public void Unsaved_work_counts_strings_and_the_inventory_and_another_game_closes_the_tools_of_the_old()
        {
            if (Uw1 == null || Uw2 == null) return;
            using (var copy = new GameCopy(Uw2, "DATA", "SAVE1"))
            {
                var w = OpenQuiet(System.IO.Path.Combine(copy.Folder, "SAVE1"));
                Assert.Empty(w.UnsavedParts());
                var st = w.Strings();
                st.Set(3, 1, st.Get(3)[1] + " (edited)");
                Assert.Contains(w.UnsavedParts(), p => p.Contains("strings"));
                var pw = w.ShowPlayer();
                Ui.Pump();
                Assert.NotNull(pw);
                pw.Model.Sheet.Inventory.Changed = true;            // what an inventory edit marks
                Assert.Contains(w.UnsavedParts(), p => p.Contains("player"));
                Assert.True(w.ViewModel.CanSave);
                var swap = w.ShowMonsterSwap();
                Ui.Pump();
                w.OpenPathNow(Uw1);
                Ui.Pump();
                Assert.False(swap.IsVisible);
                Assert.Empty(w.UnsavedParts());
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Automap_window_marks_tiles_seen_and_the_change_is_unsaved_work()
        {
            if (Uw2 == null) return;
            using (var copy = new GameCopy(Uw2, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var aw = w.ShowAutomap();
                Ui.Pump();
                var map = aw.View.Map;
                Assert.NotNull(map);
                Assert.Equal(14, map.Notes.Count);                 // the castle's notes
                Assert.False(w.ViewModel.HasLevelChanges);
                map.Snapshot("all");
                map.All(w.ViewModel.Session, aw.View.Level, true);
                w.ViewModel.Edits.AutomapEdited(map);
                Ui.Pump();
                Assert.True(w.ViewModel.HasLevelChanges);
                aw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Zones_window_adds_renames_paints_fills_and_lists_key_items()
        {
            if (Uw1 == null) return;
            using (var copy = new GameCopy(Uw1, "DATA"))
            {
                var w = OpenQuiet(copy.Folder);
                var zw = w.ShowZones();
                Ui.Pump();
                var z = zw.Zoning;
                Assert.False(z.FileExists);
                z.LoadExample();
                Ui.Pump();
                Assert.Equal(53, z.CurrentZone);
                Assert.Contains(z.Items, i => i.Zone == 53 && i.Token == "key:1");
                var zone = z.AddZone("Test room");
                Assert.Equal("Test room", z.Set.GetZone(zone.Id).Name);
                z.Rename(zone.Id, "Renamed room");
                Assert.Equal("Renamed room", z.Set.GetZone(zone.Id).Name);
                // paint the starting square, then fill the rest of the starting area
                z.CurrentZone = zone.Id;
                Assert.True(z.PaintTile(1, 32, 2, true));
                Assert.Equal(zone.Id, z.Set.ZoneAt(1, 32, 2));
                int before53 = z.Set.TileCount(53);
                int n = z.FillArea(1, 31, 2, zone.Id);
                Assert.True(n > 10);
                Assert.Equal(before53 - n, z.Set.TileCount(53));
                // door tiles stay out of a fill
                var flood = new UwEditor.Core.Randomizer.ZoneFlood(w.ViewModel.Edits.Model(1));
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) if (flood.HasDoor(x, y)) Assert.NotEqual(zone.Id, z.Set.ZoneAt(1, x, y));
                z.Undo(); z.Undo();
                Assert.Equal(53, z.Set.ZoneAt(1, 32, 2));
                Assert.False(z.PaintTile(1, 0, 0, true));            // rock stays rock
                z.RemoveZone(zone.Id);
                Assert.Null(z.Set.GetZone(zone.Id));
                // painting through the view's paint tool
                z.CurrentZone = 53;
                z.Painting = true;
                w.ZonePaintingChanged();
                Assert.Equal(2, w.ViewModel.ToolIndex);
                z.Save();
                Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "uw1-zones.json")));
                var back = UwEditor.Core.Randomizer.ZoneSet.Load(Path.Combine(copy.Folder, "DATA", "uw1-zones.json"), 9);
                Assert.Equal(z.Set.Zones.Count, back.Zones.Count);
                zw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void Zone_graph_connects_zones_with_requirements_from_the_doors()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var z = w.Zoning();
            z.LoadExample();
            var g = w.ShowZoneGraph();
            Ui.Pump();
            // every zone has a label position
            Assert.All(z.Set.Zones.Where(x => x.Id > 1), x => Assert.True(x.GraphX.HasValue && x.GraphY.HasValue));
            g.MoveLabel(9, 1234, 567);
            Assert.Contains("\"graph\"", z.Set.ToJson());
            Assert.Equal(1234, z.Set.GetZone(9).GraphX);
            int before = z.Set.Links.Count;
            var k = z.Connect(2, 9, false);
            Assert.Equal(before + 1, z.Set.Links.Count);
            Assert.False(k.TwoWay);
            Assert.Equal("key:2 | pick:0 | bash", k.Requires);    // the pillar room's door
            Assert.Equal("door", k.Kind);
            g.SelectLink(k);
            Ui.Pump();
            Assert.Same(k, g.SelectedLink);
            k.RequirementList = new System.Collections.Generic.List<string> { "key:2" };
            Assert.Equal("key:2", k.Requires);
            var k2 = z.Connect(53, 72, true);                     // no door between them
            Assert.True(k2.TwoWay);
            Assert.Equal("todo", k2.Confidence);
            z.RemoveLink(k2);
            Assert.DoesNotContain(k2, z.Set.Links);
                        Assert.Throws<InvalidOperationException>(() => z.Connect(5, 5, true));
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Zone_graph_pans_with_the_middle_button_without_moving_a_label()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var z = w.Zoning();
            z.LoadExample();
            var g = w.ShowZoneGraph();
            g.Width = 1280; g.Height = 820;
            Ui.Pump();
            g.ScrollOffset = new Avalonia.Vector(300, 200);
            Ui.Pump();
            Assert.Equal(new Avalonia.Vector(300, 200), g.ScrollOffset);
            // press over a label, so the pan must win over selecting and dragging it
            var zone = z.Set.Zones.First(x => x.Id > 1);
            g.MoveLabel(zone.Id, 400, 300);
            Ui.Pump();
            var label = g.GetLogicalDescendants().OfType<Border>().First(b => b.Tag is int id && id == zone.Id);
            var start = label.TranslatePoint(new Avalonia.Point(10, 8), g).Value;
            g.MouseMove(start);
            g.MouseDown(start, MouseButton.Middle);
            Assert.True(g.Panning);
            g.MouseMove(start + new Avalonia.Point(-120, -90), RawInputModifiers.MiddleMouseButton);
            g.MouseUp(start + new Avalonia.Point(-120, -90), MouseButton.Middle);
            Ui.Pump();
            Assert.False(g.Panning);
            Assert.Equal(420, g.ScrollOffset.X, 1);
            Assert.Equal(290, g.ScrollOffset.Y, 1);
            Assert.Equal(400, z.Set.GetZone(zone.Id).GraphX);
            Assert.Equal(-1, g.SelectedZone);
            // it stops at the canvas's edge
            g.MouseMove(start);
            g.MouseDown(start, MouseButton.Middle);
            g.MouseMove(start + new Avalonia.Point(5000, 5000), RawInputModifiers.MiddleMouseButton);
            g.MouseUp(start + new Avalonia.Point(5000, 5000), MouseButton.Middle);
            Ui.Pump();
            Assert.Equal(new Avalonia.Vector(0, 0), g.ScrollOffset);
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Zone_graph_wheel_zooms_about_the_mouse()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            w.Zoning().LoadExample();
            var g = w.ShowZoneGraph();
            g.Width = 1280; g.Height = 820;
            Ui.Pump();
            var scroll = g.GetLogicalDescendants().OfType<ScrollViewer>().First(s => s.Content is LayoutTransformControl);
            g.ScrollOffset = new Vector(300, 200);
            Ui.Pump();
            var at = new Point(250, 180);                           // a point of the view
            var p = scroll.TranslatePoint(at, g).Value;
            Func<Point> under = () => new Point((g.ScrollOffset.X + at.X) / g.Zoom, (g.ScrollOffset.Y + at.Y) / g.Zoom);
            var before = under();
            g.MouseMove(p);
            g.MouseWheel(p, new Vector(0, 1));
            Ui.Pump();
            Assert.Equal(1.15, g.Zoom, 3);
            Assert.Equal(before.X, under().X, 0);                     // the canvas point under the mouse stays there
            Assert.Equal(before.Y, under().Y, 0);
            for (int i = 0; i < 6; i++) g.MouseWheel(p, new Vector(0, -1));
            Ui.Pump();
            Assert.True(g.Zoom < 1 && g.Zoom >= 0.3, "zoomed out to " + g.Zoom);
            for (int i = 0; i < 40; i++) g.MouseWheel(p, new Vector(0, 1));
            Ui.Pump();
            Assert.Equal(2, g.Zoom, 3);                               // the slider's top
            g.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Zone_graph_force_layout_pulls_linked_zones_together_and_outlines_by_level()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var z = w.Zoning();
            z.LoadExample();
            var g = w.ShowZoneGraph();
            Ui.Pump();
            // the outline is the zone's first level
            Assert.Equal(z.Set.LevelsOf(2)[0], g.LevelOf(2));
            Func<double> meanLink = () => z.Set.Links.Where(k => k.From != k.To && z.Set.GetZone(k.From) != null && z.Set.GetZone(k.To) != null).Average(k =>
            {
                var a = z.Set.GetZone(k.From); var b = z.Set.GetZone(k.To);
                return Math.Sqrt(Math.Pow(a.GraphX.Value - b.GraphX.Value, 2) + Math.Pow(a.GraphY.Value - b.GraphY.Value, 2));
            });
            g.AutoLayout();
            Ui.Pump();
            double columns = meanLink();
            g.StartForces();
            Assert.True(g.ForcesRunning);
            for (int i = 0; i < 250; i++) g.ForceTick();
            g.StopForces();
            Assert.False(g.ForcesRunning);
            double forced = meanLink();
            Assert.True(forced < columns, forced + " vs " + columns);
            Assert.True(z.Dirty);
            Assert.All(z.Set.Zones.Where(x => x.Id > 1), x => Assert.True(x.GraphX >= 0 && x.GraphY >= 0));
            // more repulsion spreads the zones
            g.Repulsion = 3;
            g.StartForces();
            for (int i = 0; i < 250; i++) g.ForceTick();
            g.StopForces();
            Assert.True(meanLink() > forced);
            g.Close();
            w.Close();
        }
    }
}
