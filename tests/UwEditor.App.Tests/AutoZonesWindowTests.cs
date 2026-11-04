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
    /// <summary>The automatic zones window: building, the report, and handing the set to the Zones window without touching the painted file.</summary>
    public class AutoZonesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        /// <summary>Saves a capture when AUTOZONES_SHOTS names a folder (to look at the window).</summary>
        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("AUTOZONES_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        static Button Btn(Window w, string text) { return w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == text); }

        static void Wait(System.Threading.Tasks.Task task)
        {
            var until = DateTime.UtcNow.AddMinutes(2);
            while (!task.IsCompleted && DateTime.UtcNow < until) { Ui.Pump(); System.Threading.Thread.Sleep(20); }
            Assert.True(task.IsCompleted);
        }

        [AvaloniaFact]
        public void Build_and_report_then_build_and_use_puts_the_zones_in_the_zones_window_and_closes()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            // the Zones window's Autozone button opens this window
            var zw = w.ShowZones();
            Ui.Pump();
            Ui.Click(zw, Btn(zw, "Autozone…"));
            var aw = w.OpenAutoZones;
            Assert.NotNull(aw);
            Assert.Same(aw, w.ShowAutoZones());
            // two buttons only; no comparison with a painted zoning; the options in two named groups
            Assert.Equal(new[] { "Build and Report", "Build and Use" }, aw.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string).Where(t => t != null && t.StartsWith("Build")).ToArray());
            Assert.Null(Btn(aw, "Report"));
            Assert.Null(Btn(aw, "Export JSON…"));
            Assert.Null(Btn(aw, "Open in the Zones window"));
            Assert.Empty(aw.GetLogicalDescendants().OfType<ComboBox>());
            var texts = aw.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("AUTOZONE MOVEMENT", texts);
            Assert.Contains("PLAYER LOGIC (THE SOLVER)", texts);
            Assert.DoesNotContain("Logic", texts);
            Assert.DoesNotContain(aw.GetLogicalDescendants().OfType<CheckBox>(), c => (c.Content as string ?? "").StartsWith("Damage-trap"));
            // the start square: the editor's, normally
            var start = aw.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith("Consider the current starting square"));
            Assert.True(start.IsChecked);
            Assert.False(aw.MovementOptions.ShippedStart);

            Ui.Click(aw, Btn(aw, "Build and Report"));
            Assert.NotNull(aw.Result);
            var text = aw.ReportText;
            // the report is the last phase's only: the solve over time
            Assert.StartsWith("AUTOMATIC ZONES, PHASE C", text);
            Assert.Contains("Start: new game start square (32, 2)", text);
            Assert.DoesNotContain("PHASE B", text);
            Assert.DoesNotContain("(Phase A)", text);
            Shot(aw, "autozones");
            start.IsChecked = false;
            Ui.Pump();
            Assert.True(aw.MovementOptions.ShippedStart);
            start.IsChecked = true;
            Ui.Pump();

            var task = aw.BuildAndUse();
            Wait(task);
            Assert.True(task.Result);
            Assert.Null(w.OpenAutoZones);                     // closed
            var z = w.Zoning();
            Assert.True(z.AskForName);
            Assert.Equal("uw1-zones-auto.json", Path.GetFileName(z.Path));
            Assert.Equal(aw.Result.Set.Zones.Count, z.Set.Zones.Count);
            Assert.NotSame(aw.Result.Set, z.Set);
            Assert.True(AutoZones.IsAutomatic(z.Set));
            Shot(w, "autozones-main");
            zw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Zone_graph_logic_check_solves_the_zoning_shown_over_time()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            Wait(aw.BuildAndUse());                              // the default options, as the logic check uses with no window open
            Assert.True(aw.Timeline.Solve.GoalReached);
            var g = w.ShowZoneGraph();
            Ui.Pump();
            // the automatic zones in the graph: solved as they are, so the same outcome as the window's report
            string text = g.CheckLogic();
            Assert.Contains("on this zoning", text);
            Assert.NotNull(g.LastCheck.Abilities.Auto.Given);
            Assert.True(g.LastCheck.Solve.GoalReached);
            Assert.Equal(aw.Timeline.Solve.ZoneSphere.Keys.OrderBy(x => x), g.LastCheck.Solve.ZoneSphere.Keys.OrderBy(x => x));
            Assert.Equal(aw.Timeline.Solve.Applied.Count, g.LastCheck.Solve.Applied.Count);
            // a painted zoning: its own zones, solved over time
            w.Zoning().LoadExample();
            Ui.Pump();
            text = g.CheckLogic();
            Assert.Contains("Zones: the zoning given", text);
            Assert.Contains("no free way between them", text);
            Assert.True(g.LastCheck.Solve.GoalReached);
            Assert.Equal(w.Zoning().Set.GetZone(53).Name, g.LastCheck.Set.GetZone(g.LastCheck.Set.StartZone).Name);
            // an empty zoning: automatic zones
            w.Zoning().NewFile();
            Ui.Pump();
            text = g.CheckLogic();
            Assert.Contains("no zones yet", text);
            Assert.Null(g.LastCheck.Abilities.Auto.Given);
            g.Close();
            w.Close();
        }
    }
}
