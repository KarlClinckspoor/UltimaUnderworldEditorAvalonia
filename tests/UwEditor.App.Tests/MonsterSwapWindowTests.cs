using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core;
using UwEditor.Core.Randomizer;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The monster swap window: the formula box, the special-kinds toggle, several entries added at once, the sortable strength table and the layout.</summary>
    public class MonsterSwapWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }
        static string Uw2 { get { return GameData.Game("UW2"); } }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        /// <summary>Saves a capture when MONSTER_SHOTS names a folder (to look at the window).</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("MONSTER_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        [AvaloniaFact]
        public void The_formula_follows_the_settings()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            var slider = mw.GetLogicalDescendants().OfType<Slider>().First();                  // the target
            slider.Value = 0;
            mw.GetLogicalDescendants().OfType<Slider>().Skip(1).First().Value = 40;             // the spread
            Ui.Pump();
            Assert.Contains("σ = 0.5 × spread/100 = 0.2", mw.FormulaText);
            Assert.Contains("t = p₀ + N(0, σ)", mw.FormulaText);
            slider.Value = 30;
            Ui.Pump();
            Assert.Contains("0.7·p₀ + 0.3·d", mw.FormulaText);
            mw.GetLogicalDescendants().OfType<Slider>().Skip(1).First().Value = 100;
            Ui.Pump();
            Assert.Contains("U(0, 1)", mw.FormulaText);
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Several_entries_go_to_the_ignored_list_at_once_and_come_back()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            var panel = mw.IgnoredPanel;
            int before = mw.Options.Ignored.Count;
            var left = panel.CandidateList;
            Assert.Equal(SelectionMode.Multiple, left.SelectionMode & SelectionMode.Multiple);
            var pick = left.ItemsSource.Cast<IgnoredObject>().Take(4).ToList();
            foreach (var p in pick) left.SelectedItems.Add(p);
            panel.AddSelected();
            Ui.Pump();
            Assert.Equal(before + 4, mw.Options.Ignored.Count);
            foreach (var p in pick) Assert.Contains(mw.Options.Ignored, x => x.Level == p.Level && x.Index == p.Index);
            var right = panel.ChosenList;
            foreach (var p in right.ItemsSource.Cast<IgnoredObject>().Where(x => pick.Any(p => p.Level == x.Level && p.Index == x.Index)).ToList()) right.SelectedItems.Add(p);
            panel.RemoveSelected();
            Ui.Pump();
            Assert.Equal(before, mw.Options.Ignored.Count);
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_whitelist_takes_several_talkers_at_once()
        {
            if (Uw2 == null) return;
            var w = OpenQuiet(Uw2);
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            var panel = mw.WhitelistPanel;
            int before = mw.Options.Whitelist.Count;
            var pick = panel.CandidateList.ItemsSource.Cast<IgnoredObject>().Take(3).ToList();
            foreach (var p in pick) panel.CandidateList.SelectedItems.Add(p);
            panel.AddSelected();
            Ui.Pump();
            Assert.Equal(before + 3, mw.Options.Whitelist.Count);
            foreach (var p in panel.ChosenList.ItemsSource.Cast<IgnoredObject>().Where(x => pick.Any(p => p.Level == x.Level && p.Index == x.Index)).ToList()) panel.ChosenList.SelectedItems.Add(p);
            panel.RemoveSelected();
            Assert.Equal(before, mw.Options.Whitelist.Count);
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_strength_table_sorts_by_a_header_and_reverses_on_a_second_click()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var mw = w.ShowMonsterSwap();
            Ui.Pump();
            mw.Tabs.SelectedIndex = 3;
            Ui.Pump();
            var t = mw.StrengthTableView;
            Assert.Equal(2, t.SortColumn);                                              // experience, as the game rates them
            var heads = t.GetLogicalDescendants().OfType<Button>().ToList();
            var combined = heads.First(b => (b.Content as string ?? "").StartsWith("Combined"));
            Ui.Click(mw, combined);
            var v = t.Shown.Select(r => r.Combined).ToList();
            Assert.Equal(v.OrderBy(x => x), v);
            Assert.Contains("▲", combined.Content as string);
            Ui.Click(mw, combined);
            v = t.Shown.Select(r => r.Combined).ToList();
            Assert.Equal(v.OrderByDescending(x => x), v);
            Assert.Contains("▼", combined.Content as string);
            Assert.Contains(t.Shown, r => r.Name == "fire elemental" && r.Fireproof);
            Shot(mw, "monsters-strength-table");
            mw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void The_special_kinds_toggle_keeps_the_fire_elementals_in_place()
        {
            if (Uw1 == null) return;
            using (var copy = new EditingWindowTestsCopy(Uw1))
            {
                var w = OpenQuiet(copy.Folder);
                var mw = w.ShowMonsterSwap();
                Ui.Pump();
                Assert.True(mw.Options.LeaveSpecialKinds);                                   // on by default
                Assert.Contains("fire elementals", mw.SpecialKindsCheck.Content as string);
                var edits = w.ViewModel.Edits;
                Func<int[]> elementals = () => Enumerable.Range(1, 9).SelectMany(l => edits.Model(l).Objects.Where(o => !o.Free && o.IsCritter && o.Item == 0x78).Select(o => l * 1024 + o.Index)).OrderBy(i => i).ToArray();
                var at = elementals();
                Assert.NotEmpty(at);
                mw.Options.Seed = 3; mw.Options.Scope = "all";
                mw.Swap();
                Ui.Pump();
                Assert.Equal(at, elementals());
                Assert.Contains("fire elementals, left alone by the option", mw.ReportText);
                mw.UndoAll();
                mw.SpecialKindsCheck.IsChecked = false;
                Ui.Pump();
                Assert.False(mw.Options.LeaveSpecialKinds);
                mw.Swap();
                Ui.Pump();
                Assert.NotEqual(at, elementals());
                mw.UndoAll();
                mw.SpecialKindsCheck.IsChecked = true;                                      // the next test starts from the default
                mw.Close();
                w.Close();
            }
        }

        [AvaloniaFact]
        public void The_report_takes_the_extra_height_and_the_settings_scroll_when_short()
        {
            if (Uw1 == null) return;
            var w = OpenQuiet(Uw1);
            var mw = w.ShowMonsterSwap();
            mw.Width = 1300; mw.Height = 980;
            Ui.Pump(); Ui.Pump();
            double tabsLarge = mw.Tabs.Bounds.Height, reportLarge = mw.ReportBox.Bounds.Height;
            Shot(mw, "monsters-large");
            mw.Height = 1180;
            Ui.Pump(); Ui.Pump();
            Assert.Equal(tabsLarge, mw.Tabs.Bounds.Height, 0);                          // the settings keep their natural size
            Assert.True(mw.ReportBox.Bounds.Height >= reportLarge + 190, reportLarge + " → " + mw.ReportBox.Bounds.Height);
            mw.Width = 680; mw.Height = 520;
            Ui.Pump(); Ui.Pump();
            Shot(mw, "monsters-small");
            Assert.True(mw.Tabs.Bounds.Height < tabsLarge);                             // short: the settings scroll
            Assert.True(mw.ReportBox.Bounds.Height >= 119, "report " + mw.ReportBox.Bounds.Height);
            mw.Close();
            w.Close();
        }

        /// <summary>A temporary copy of a game's DATA folder.</summary>
        sealed class EditingWindowTestsCopy : IDisposable
        {
            public readonly string Folder = Path.Combine(Path.GetTempPath(), "uwedit-test-" + Guid.NewGuid().ToString("N"));
            public EditingWindowTestsCopy(string from)
            {
                Directory.CreateDirectory(Path.Combine(Folder, "DATA"));
                foreach (var f in Directory.GetFiles(Path.Combine(from, "DATA"))) File.Copy(f, Path.Combine(Folder, "DATA", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(from, "*.EXE")) File.Copy(f, Path.Combine(Folder, Path.GetFileName(f)));
            }
            public void Dispose() { try { Directory.Delete(Folder, true); } catch (Exception) { } }
        }
    }
}
