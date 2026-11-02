using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The item swap and item property shuffle windows: their own settings, the formula box, the layout.</summary>
    public class ItemRandomizerWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("UWEDITOR_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        static void ClearSaved(bool uw2)
        {
            string g = uw2 ? "uw2" : "uw1";
            foreach (var f in new[] { "item-swap-", "item-shuffle-", "item-ignore-" }) File.Delete(Path.Combine(Settings.Folder, f + g + ".json"));
        }

        [AvaloniaFact]
        public void Swap_settings_are_its_own_and_saved_apart_from_the_shuffles()
        {
            if (Uw1 == null) return;
            ClearSaved(false);
            var w = OpenQuiet(Uw1);
            var sw = w.ShowItemSwap();
            Ui.Pump();
            var set = sw.Settings_;
            Assert.True(set.SpotOwnership);
            Assert.True(set.KeepStart);
            Assert.False(set.Bones);
            Assert.Equal(new[] { 934, 935, 936, 940, 959 }, set.Start.Select(x => x.Index).OrderBy(x => x));
            Assert.Contains(set.Ignored, x => x.Label.Contains("bullfrog"));
            set.Weapons = false; set.Scope = "level"; set.Bones = true; set.SwapEnchantments = true;
            set.Ignored.Clear();
            var o = sw.Options();
            Assert.False(o.ShuffleWeapons); Assert.Equal("level", o.SwapScope); Assert.True(o.SwapBones); Assert.True(o.SwapEnchantments);
            Assert.Same(set.Start, o.Kept);
            sw.Close();
            Ui.Pump();
            Assert.True(File.Exists(Path.Combine(Settings.Folder, "item-swap-uw1.json")));

            // the shuffle neither sees nor changes them, and its ignore list is still the game's
            var sh = w.ShowItemShuffle();
            Ui.Pump();
            Assert.True(sh.Options.ShuffleWeapons);
            Assert.Contains(sh.Options.Ignored, x => x.Label.Contains("bullfrog"));
            sh.Options.ShuffleArmour = false;
            sh.Close();
            Ui.Pump();

            var sw2 = w.ShowItemSwap();
            Ui.Pump();
            Assert.False(sw2.Settings_.Weapons);
            Assert.True(sw2.Settings_.Armour);
            Assert.Equal("level", sw2.Settings_.Scope);
            Assert.Empty(sw2.Settings_.Ignored);
            Assert.DoesNotContain("Shuffle", sw2.FormulaText);
            sw2.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Swap_window_keeps_the_start_gear_gives_spot_owners_and_undoes()
        {
            if (Uw1 == null) return;
            ClearSaved(false);
            var w = OpenQuiet(Uw1);
            var sw = w.ShowItemSwap();
            Ui.Pump();
            var edits = w.ViewModel.Edits;
            var before = Enumerable.Range(1, 9).ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            var l1 = w.ViewModel.Session.LoadLevel(1);
            var set = sw.Settings_;
            set.Seed = 8; set.AnyKind = true; set.Scope = "level"; set.SwapEnchantments = true; set.Bones = true;
            sw.Swap();
            Ui.Pump();
            Assert.True(sw.CanUndoSwap);
            Assert.Contains("starting equipment kept", sw.ReportText);
            Assert.Contains("took the owner of the spot", sw.ReportText);
            foreach (var x in set.Start) Assert.Equal(l1.Objects[x.Index].Record, edits.Model(1).Objects[x.Index].Record);
            foreach (int l in Enumerable.Range(1, 9)) { bool ok; edits.For(l).Check(out ok); Assert.True(ok); }
            sw.UndoAll();
            Ui.Pump();
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
            sw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_formula_boxes_follow_the_settings()
        {
            if (Uw2 == null) return;
            ClearSaved(true);
            var w = OpenQuiet(Uw2);
            var sw = w.ShowItemSwap();
            Ui.Pump();
            Assert.Contains("uniform random permutation", sw.FormulaText);
            sw.Settings_.Scope = "near"; sw.Settings_.Range = 3;
            sw.RefreshFormula();
            Assert.Contains("L(a) − 3 .. L(a) + 3", sw.FormulaText);
            Assert.Contains("Within each world", sw.FormulaText);
            sw.Close();

            var sh = w.ShowItemShuffle();
            Ui.Pump();
            sh.Options.WeaponQualitySpread = 7;
            sh.RefreshFormula();
            Assert.Contains("clamp(q + U{−7..7}, 1, 63)", sh.FormulaText);
            Assert.Contains("d = (level within its world − 1) / 7", sh.FormulaText);
            sh.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_text_boxes_take_the_room_a_window_grows_by()
        {
            if (Uw1 == null) return;
            ClearSaved(false);
            var w = OpenQuiet(Uw1);
            var sw = w.ShowItemSwap();
            var sh = w.ShowItemShuffle();
            Func<Window, (double, double)> sizes = win => win is ItemSwapWindow a ? (a.SettingsHeight, a.ReportHeight) : (((ItemShuffleWindow)win).SettingsHeight, ((ItemShuffleWindow)win).ReportHeight);
            foreach (Window win in new Window[] { sw, sh })
            {
                win.Width = 980; win.Height = 600; Ui.Pump(); Ui.Pump();
                var small = sizes(win);
                Shot(win, win.GetType().Name + "-small");
                win.Height = 1000; Ui.Pump(); Ui.Pump();
                var mid = sizes(win);
                win.Height = 1300; Ui.Pump(); Ui.Pump();
                var large = sizes(win);
                Shot(win, win.GetType().Name + "-large");
                Assert.True(small.Item1 >= 140 && small.Item2 >= 60, win.Title + " small " + small);
                Assert.True(large.Item2 > mid.Item2 + 150, win.Title + " report " + mid + " → " + large);
                Assert.Equal(mid.Item1, large.Item1, 0.5);      // the settings keep their height
                Assert.True(small.Item1 < large.Item1, win.Title + " the settings shrink in a short window");
            }
            sw.ShowTab(2); Ui.Pump();
            Shot(sw, "ItemSwapWindow-start");
            sw.Close(); sh.Close();
            w.Close();
        }
    }
}
