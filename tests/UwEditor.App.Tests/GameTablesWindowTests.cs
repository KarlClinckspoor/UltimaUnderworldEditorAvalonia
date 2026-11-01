using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Tools ▸ Game tables and Level ▸ Level textures.</summary>
    public class GameTablesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        sealed class GameCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public GameCopy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
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
        public void Game_tables_window_edits_and_saves_each_table_in_both_games()
        {
            foreach (var game in new[] { Uw1, Uw2 }.Where(g => g != null))
                using (var copy = new GameCopy(game))
                {
                    var w = OpenQuiet(copy.Folder);
                    bool uw2 = w.ViewModel.Session.IsUw2;
                    var gt = w.ShowGameTables();
                    Ui.Pump();
                    Assert.Same(gt, w.ShowGameTables());
                    Assert.False(gt.HasChanges);

                    // terrain: the list has a row for each texture with a word
                    Assert.Equal(uw2 ? 256 : 210, gt.TerrainList.ItemCount);
                    if (!uw2)
                    {
                        gt.WhichTerrain.SelectedIndex = 1;
                        Ui.Pump();
                        Assert.True(gt.TerrainFloors);
                        Assert.Equal(52, gt.TerrainList.ItemCount);
                    }
                    gt.TerrainList.SelectedIndex = 5;
                    Ui.Pump();
                    int entry = gt.Terrain.EntryOf(5, gt.TerrainFloors);
                    gt.TerrainWord.Value = uw2 ? 0x80 : 0x20;              // lava
                    Ui.Pump();
                    Assert.True(gt.Terrain.IsChanged(entry));
                    Assert.Equal(uw2 ? 0x80 : 0x20, gt.Terrain.Get(entry));
                    Assert.Contains("lava", ((TableRow)gt.TerrainList.SelectedItem).Label);
                    Assert.True(((TableRow)gt.TerrainList.SelectedItem).Changed);

                    // class skills: the Mage's intelligence
                    gt.Tab = 1;
                    gt.SkillClass.SelectedIndex = 1;
                    Ui.Pump();
                    Assert.True(gt.Skills.Exists);
                    gt.Skills.SetAttribute(1, 2, 24);
                    // combinations: the first rule's output
                    gt.Tab = 2;
                    Ui.Pump();
                    ((ComboBox)gt.CombinationRows[0][5]).SelectedIndex = 0xA0;
                    Ui.Pump();
                    Assert.Equal(0xA0, gt.Combinations.Item(0, 2));
                    Assert.True(gt.Combinations.Changed(0, 2));
                    // light: the taper's range
                    gt.Tab = 3;
                    Ui.Pump();
                    gt.ShadeBoxes[3, 3].Value = 6;
                    Ui.Pump();
                    Assert.True(gt.Shades.IsChanged(3, 3));
                    if (uw2)
                    {
                        Assert.Equal(80, gt.MinimumLightList.ItemCount);
                        Assert.Equal(0, gt.MinimumLightList.SelectedIndex);   // level 1, the level shown
                        gt.MinimumLight.Set(2, 5, true);
                    }
                    else Assert.Null(gt.MinimumLightList);

                    Assert.True(gt.HasChanges);
                    Assert.True(gt.SaveAll());
                    Assert.False(gt.HasChanges);
                    var s = GameSession.Open(copy.Folder);
                    Assert.Equal(uw2 ? 0x80 : 0x20, TerrainTable.Load(s).Get(entry));
                    Assert.Equal(24, SkillsTable.Load(s).Attribute(1, 2));
                    Assert.Equal(0xA0, CombinationTable.Load(s).Item(0, 2));
                    Assert.Equal(6, ShadesTable.Load(s).Get(3, 3));
                    Assert.Equal(6, w.ViewModel.Light.Rows[3].Range);         // the view's light follows
                    if (uw2) { Assert.Equal(15, MinimumLightTable.Load(s).Raw(2)); Assert.Equal(5, w.ViewModel.Light.MinimumLight(2)); }
                    Assert.True(File.Exists(Path.Combine(copy.Folder, "DATA", "SHADES.DAT.ORIG")));

                    // revert puts back the file's values
                    gt.Shades.Set(0, 0, 40);
                    Assert.True(gt.HasChanges);
                    gt.Shades.Revert();
                    Assert.False(gt.HasChanges);
                    gt.Close();
                    Ui.Pump();
                    w.Close();
                }
        }

        [AvaloniaFact]
        public void Level_textures_window_changes_a_slot_undoably_and_follows_the_level()
        {
            foreach (var game in new[] { Uw1, Uw2 }.Where(g => g != null))
                using (var copy = new GameCopy(game))
                {
                    var w = OpenQuiet(copy.Folder);
                    var vm = w.ViewModel;
                    bool uw2 = vm.Session.IsUw2;
                    var lt = w.ShowLevelTextures();
                    Ui.Pump();
                    Assert.Same(lt, w.ShowLevelTextures());
                    Assert.Equal(vm.Level.Level, lt.Level.Level);
                    Assert.Equal(uw2 ? 64 : 48, lt.Slots.ItemCount);
                    lt.Slots.SelectedIndex = 3;
                    Ui.Pump();
                    Assert.Equal(TextureMap.Choices(vm.Session, TextureMap.Kind.Wall), lt.PickButtons.Count);
                    int old = vm.Level.WallMap[3], to = old == 7 ? 8 : 7;
                    Assert.True(lt.PickButtons[old].IsChecked == true);
                    Assert.True(lt.Choose(to));
                    Ui.Pump();
                    Assert.Equal(to, vm.Level.WallMap[3]);
                    Assert.Equal(to, lt.Level.WallMap[3]);
                    Assert.True(lt.PickButtons[to].IsChecked == true);
                    Assert.Contains("texture " + to, ((TableRow)lt.Slots.SelectedItem).Label);
                    Assert.True(vm.HasLevelChanges);
                    w.Undo();
                    Ui.Pump();
                    vm.FlushRefresh();
                    Ui.Pump();
                    Assert.Equal(old, vm.Level.WallMap[3]);
                    Assert.Equal(old, lt.Level.WallMap[3]);
                    w.Redo();
                    Ui.Pump();
                    vm.FlushRefresh();

                    // the doors
                    lt.KindBox.SelectedIndex = uw2 ? 1 : 2;
                    Ui.Pump();
                    Assert.Equal(6, lt.Slots.ItemCount);
                    lt.Slots.SelectedIndex = 0;
                    Ui.Pump();
                    int d = vm.Level.DoorMap[0] == 1 ? 2 : 1;
                    Assert.True(lt.Choose(d));
                    Ui.Pump();
                    Assert.Equal(d, vm.Level.DoorMap[0]);

                    // saved with the levels
                    w.SaveAll(false);
                    var back = GameSession.Open(copy.Folder).LoadLevel(vm.Level.Level);
                    Assert.Equal(to, back.WallMap[3]);
                    Assert.Equal(d, back.DoorMap[0]);

                    // another level: the window follows
                    vm.LoadLevel(2);
                    Ui.Pump();
                    Assert.Equal(2, lt.Level.Level);
                    lt.Close();
                    Ui.Pump();
                    w.Close();
                }
        }
    }
}
