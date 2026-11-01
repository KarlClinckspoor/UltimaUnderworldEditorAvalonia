using System.Linq;
using Avalonia.Headless.XUnit;
using UltimateEditor.Games;
using UwEditor.Core.Editing;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>Level ▸ Fixes and Level ▸ Item cleaner, through their windows.</summary>
    public class FixesWindowTests
    {
        static bool Ok(LevelEditor e) { bool ok; e.Check(out ok); return ok; }

        static MainWindow OpenQuiet(string folder)
        {
            var w = Ui.Open(folder);
            w.AskBeforeDiscarding = false;
            return w;
        }

        [AvaloniaFact]
        public void Fixes_window_finds_a_broken_list_goes_to_it_and_fix_all_mends_it()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = OpenQuiet(g);
            var vm = w.ViewModel;
            var e = vm.Edits.For(vm.Level.Level);
            var m = e.Model;
            // a tile's last object pointing back at its first: a loop
            var first = m.Objects.First(o => !o.Free && o.OnTile && o.Next > 0 && m.Objects[o.Next].Next > 0);
            int last = first.Next; while (m.Objects[last].Next > 0) last = m.Objects[last].Next;
            e.EditBytes("damage", (d, t) => { int at = LevelBlock.ObjectOffset(last) + 4; int v = d[at] | d[at + 1] << 8; v = (v & 0x3F) | first.Index << 6; d[at] = (byte)v; d[at + 1] = (byte)(v >> 8); });
            Ui.Pump();
            Assert.False(Ok(e));

            var fw = w.ShowFixes();
            Ui.Pump();
            var loop = fw.Findings.First(f => f.Check == LevelFixes.ReachedTwice);
            fw.GoTo(loop);
            Ui.Pump();
            Assert.Equal(first.Index, vm.SelectedObject);   // the object reached twice, selected
            Assert.True(fw.Fix(loop));
            Ui.Pump();
            Assert.True(Ok(e));
            Assert.DoesNotContain(fw.Findings, f => f.Severity == FixSeverity.ListError);
            Assert.Contains("Fix", vm.UndoText);
            w.Undo();
            Ui.Pump();
            Assert.False(Ok(e));
            Assert.Contains(fw.Findings, f => f.Check == LevelFixes.ReachedTwice);   // the window follows the undo
            Assert.True(fw.FixAll() > 0);
            Ui.Pump();
            Assert.True(Ok(e));
            fw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Item_cleaner_window_previews_and_removes_debris_from_every_level()
        {
            string g = GameData.Game("UW2"); if (g == null) return;
            var w = OpenQuiet(g);
            var cw = w.ShowItemCleaner();
            Ui.Pump();
            cw.Scope = 1;
            cw.Options.Kinds = CleanKind.Debris;
            var plan = cw.Preview();
            Assert.NotEmpty(plan.Remove);
            Assert.All(plan.Remove, c => Assert.Equal(CleanKind.Debris, ItemCleaner.KindOf(c.Item, GameKind.UW2)));
            int n = cw.Remove();
            Ui.Pump();
            Assert.Equal(plan.Remove.Count, n);
            Assert.Empty(cw.Plan.Remove);
            Assert.True(w.ViewModel.HasLevelChanges);
            foreach (var e in w.ViewModel.Edits.Editors) Assert.True(Ok(e), "level " + e.Level);
            cw.Close();
            w.Close();
        }
    }
}
