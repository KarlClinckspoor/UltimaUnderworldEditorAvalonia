using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Randomizer ▸ Key item shuffle: the options shared with the Automatic zones window, preview, shuffle, undo and the spoiler log.</summary>
    public class KeyShuffleWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        [AvaloniaFact]
        public void Preview_shuffle_and_undo()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.Same(kw, w.ShowKeyShuffle());
            // the logic options are the Automatic zones window's, with the orb option
            Assert.True(Box(kw, "Ring of Levitation").IsChecked);
            Assert.False(Box(kw, "Require breaking the orb").IsChecked);
            Box(kw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(kw.Options.Logic.RequireOrb);
            foreach (var c in KeyShuffle.Categories) Assert.True(Box(kw, KeyShuffle.CategoryName(c)).IsChecked);
            Assert.False(Box(kw, "Creature inventories").IsChecked);
            Assert.False(Box(kw, "Everything else as filler").IsChecked);
            kw.GetLogicalDescendants().OfType<NumericUpDown>().First().Value = 11;
            Ui.Pump();
            Assert.Equal(11, kw.Options.Seed);

            var edits = w.ViewModel.Edits;
            var r = kw.PreviewNow();
            Ui.Pump();
            Assert.NotNull(r);
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Contains("WHERE EACH KEY ITEM WENT", kw.Spoiler);
            Assert.Contains("the goal needs the orb broken", kw.Spoiler);
            Assert.Empty(edits.DirtyLevels);
            string dir = Environment.GetEnvironmentVariable("KEYSHUFFLE_SHOTS");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); new Ui.Codec().Save(Ui.Capture(kw), Path.Combine(dir, "keyshuffle.png")); }

            // Shuffle writes the preview on show (the same seed and levels)
            Assert.True(kw.ShuffleNow());
            Ui.Pump();
            Assert.Same(r, kw.Result);
            Assert.Equal(r.After.Keys.OrderBy(x => x), edits.DirtyLevels.OrderBy(x => x));
            foreach (var kv in r.After) Assert.True(edits.For(kv.Key).Data.AsSpan().SequenceEqual(kv.Value));
            string log = Path.Combine(Path.GetTempPath(), "uwedit-keyshuffle-" + Guid.NewGuid().ToString("N") + ".txt");
            try { kw.ExportTo(log); Assert.Contains("KEY ITEM SHUFFLE", File.ReadAllText(log)); }
            finally { File.Delete(log); }

            // Undo shuffle puts every byte back
            Assert.True(kw.UndoNow());
            Ui.Pump();
            foreach (var kv in r.Before) Assert.True(edits.Model(kv.Key).Block.Data.AsSpan().SequenceEqual(kv.Value), "level " + kv.Key);
            kw.Close();

            // the Automatic zones window shows the same options, the orb one included
            var aw = w.ShowAutoZones();
            Ui.Pump();
            Assert.False(Box(aw, "Require breaking the orb").IsChecked);
            Box(aw, "Require breaking the orb").IsChecked = true;
            Ui.Pump();
            Assert.True(aw.LogicOptions.RequireOrb);
            aw.Close();
            w.Close();
        }

        static void Wait(System.Threading.Tasks.Task task)
        {
            var until = DateTime.UtcNow.AddMinutes(2);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
            Assert.True(task.IsCompleted);
        }

        [AvaloniaFact]
        public void Apply_uses_the_zones_windows_zoning_and_quick_autozone_makes_one_when_there_is_none()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            Assert.False(z.HasZones);                       // a new zoning: nothing painted
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            Assert.False(kw.UsesZoning);
            Assert.True(kw.QuickAutozoneButton.IsEnabled);
            // Quick autozone: automatic zones in the Zones window, and the button goes faint
            var task = kw.QuickAutozoneAsync();
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            Wait(task);
            Assert.True(task.Result);
            Assert.True(z.HasZones);
            Assert.True(AutoZones.IsAutomatic(z.Set));
            Assert.Equal("uw1-zones-auto.json", Path.GetFileName(z.Path));
            Assert.True(kw.UsesZoning);
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            var r = kw.PreviewNow();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.NotNull(r.Options.Zoning);
            Assert.Contains("Zones: uw1-zones-auto.json with its unsaved changes (the Zones window's)", kw.Spoiler);
            // a painted zoning in the Zones window: Apply follows it, and names the places by it
            z.LoadExample();
            Ui.Pump();
            Assert.True(kw.UsesZoning);
            Assert.False(kw.QuickAutozoneButton.IsEnabled);
            var p = kw.PreviewNow();
            Assert.True(p.Success, string.Join("; ", p.Failures));
            Assert.Contains("Places are named by the zoning", kw.Spoiler);
            Assert.False(AutoZones.IsAutomatic(p.Options.Zoning));
            // a new, empty zoning: automatic zones made by Apply again, and Quick autozone comes back
            z.NewFile();
            Ui.Pump();
            Assert.False(kw.UsesZoning);
            Assert.True(kw.QuickAutozoneButton.IsEnabled);
            kw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_zones_windows_split_by_level_option_reaches_apply_and_the_logic_check()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var z = w.Zoning();
            z.LoadExample();
            var zw = w.ShowZones();
            Ui.Pump();
            Assert.False(zw.SplitByLevelBox.IsChecked == true);
            zw.SplitByLevelBox.IsChecked = true;
            Ui.Pump();
            Assert.True(z.SplitByLevel);
            Assert.True(w.ZoneSplitByLevel);                // remembered in the settings
            Assert.False(z.Dirty && z.Set.Links.Any(k => k.Notes.Contains("split by level")));   // the zoning itself is not changed
            Assert.Equal(7, z.Set.LevelsOf(2).Count);
            // Apply gets the split copy
            var kw = w.ShowKeyShuffle();
            Ui.Pump();
            var r = kw.PreviewNow();
            Assert.True(r.Success, string.Join("; ", r.Failures));
            Assert.Single(r.Options.Zoning.LevelsOf(2));
            Assert.Contains("multi-level zones split by level", kw.Spoiler);
            // so does the zone graph's logic check
            var g = w.ShowZoneGraph();
            Ui.Pump();
            string text = g.CheckLogic();
            Assert.Contains("Split by level: 5 zone(s) on several levels split into", text);
            Assert.True(g.LastCheck.Solve.GoalReached);
            zw.SplitByLevelBox.IsChecked = false;
            Ui.Pump();
            Assert.False(z.SplitByLevel);
            g.Close(); kw.Close(); zw.Close();
            w.Close();
        }
    }
}
