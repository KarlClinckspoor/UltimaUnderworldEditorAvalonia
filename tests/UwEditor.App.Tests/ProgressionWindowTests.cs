using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using UwEditor.Core;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The Uniform progression choice in the monster swap and item swap windows.</summary>
    public class ProgressionWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        /// <summary>Saves a capture when PROGRESSION_SHOTS names a folder (to look at the window).</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("PROGRESSION_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        [AvaloniaFact]
        public void The_monster_swap_offers_uniform_progression_and_undoes_it()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            var edits = w.ViewModel.Edits;
            var levels = w.ViewModel.Session.LevelSlots().Where(x => x.Present).Select(x => x.Level).ToList();
            var before = levels.ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            Assert.True(mw.ScopeRow.IsEnabled);
            mw.RuleBox.SelectedIndex = 1;
            Ui.Pump();
            Assert.Equal("progression", mw.Options.Rule);
            Assert.False(mw.ScopeRow.IsEnabled);
            Assert.Contains("Uniform progression", mw.FormulaText);
            Assert.Contains("Pits of Carnage → Tomb of Praecor Loth", mw.FormulaText);
            Shot(mw, "MonsterSwap-progression");
            mw.Swap();
            Ui.Pump();
            Assert.True(mw.CanUndoSwap);
            Assert.Contains("Nearest fit:", mw.ReportText);
            Assert.Contains("walkers, ", mw.ReportText);
            Shot(mw, "MonsterSwap-progression-report");
            mw.UndoAll();
            Ui.Pump();
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
            mw.RuleBox.SelectedIndex = 0;            // the saved settings go back to rank matching
            Ui.Pump();
            Assert.True(mw.ScopeRow.IsEnabled);
            Assert.DoesNotContain("Uniform progression", mw.FormulaText);
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_item_swap_offers_uniform_progression_which_fixes_the_scope()
        {
            if (Uw1 == null) return;
            File.Delete(Path.Combine(Settings.Folder, "item-swap-uw1.json"));
            var w = OpenQuiet(Uw1);
            var sw = w.ShowItemSwap();
            Ui.Pump();
            var edits = w.ViewModel.Edits;
            var before = Enumerable.Range(1, 9).ToDictionary(l => l, l => (byte[])edits.For(l).Data.Clone());
            Assert.True(sw.ScopeChoices.IsEnabled);
            sw.ProgressionChoice.IsChecked = true;
            Ui.Pump();
            Assert.Equal("progression", sw.Settings_.Rule);
            Assert.False(sw.ScopeChoices.IsEnabled);
            sw.RefreshFormula();
            Assert.Contains("Uniform progression fixes both", sw.FormulaText);
            Assert.Contains("levels 1 → 2 → … → 9", sw.FormulaText);
            Shot(sw, "ItemSwap-progression");
            sw.Swap();
            Ui.Pump();
            Assert.True(sw.CanUndoSwap);
            Assert.Contains("weapons by level", sw.ReportText);
            Assert.Contains("armour by level", sw.ReportText);
            foreach (int l in Enumerable.Range(1, 9)) { bool ok; edits.For(l).Check(out ok); Assert.True(ok); }
            sw.ShowTab(0);
            Shot(sw, "ItemSwap-progression-report");
            sw.UndoAll();
            Ui.Pump();
            foreach (var kv in before) Assert.Equal(kv.Value, edits.For(kv.Key).Data);
            sw.Close();
            w.Close();
            File.Delete(Path.Combine(Settings.Folder, "item-swap-uw1.json"));
        }
    }
}
