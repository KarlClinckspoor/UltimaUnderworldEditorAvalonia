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

        [AvaloniaFact]
        public void Builds_reports_and_opens_in_the_zones_window()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            Ui.Pump();
            Assert.Same(aw, w.ShowAutoZones());
            aw.BuildNow();
            Ui.Pump();
            Assert.NotNull(aw.Result);
            Assert.NotNull(aw.Painted);          // Karl's shipped map is the default comparison
            var text = aw.GetLogicalDescendants().OfType<TextBox>().First(t => t.IsReadOnly).Text;
            // the report is the last phase's only: the solve over time
            Assert.StartsWith("AUTOMATIC ZONES, PHASE C", text);
            Assert.DoesNotContain("PHASE B", text);
            Assert.DoesNotContain("(Phase A)", text);
            Assert.DoesNotContain(aw.GetLogicalDescendants().OfType<Button>(), b => (b.Content as string ?? "").Contains("Static"));
            Shot(aw, "autozones");
            var open = aw.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "Open in the Zones window");
            Assert.True(open.IsEnabled);
            open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Ui.Pump();
            var z = w.Zoning();
            Assert.True(z.AskForName);
            Assert.Equal("uw1-zones-auto.json", Path.GetFileName(z.Path));
            Assert.Equal(aw.Result.Set.Zones.Count, z.Set.Zones.Count);
            Assert.NotSame(aw.Result.Set, z.Set);
            Shot(w, "autozones-main");
            aw.Close();
            w.Close();
        }

        [AvaloniaFact]
        public void Zone_graph_logic_check_solves_the_zoning_shown_over_time()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            aw.BuildNow();
            Ui.Pump();
            Assert.True(aw.Timeline.Solve.GoalReached);
            aw.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == "Open in the Zones window")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Ui.Pump();
            var g = w.ShowZoneGraph();
            Ui.Pump();
            // the automatic zones in the graph: solved as they are, so the same outcome as the window's report
            string text = g.CheckLogic();
            Assert.Contains("on this zoning", text);
            Assert.NotNull(g.LastCheck.Abilities.Auto.Given);
            Assert.True(g.LastCheck.Solve.GoalReached);
            Assert.Equal(aw.Timeline.Solve.ZoneSphere.Keys.OrderBy(x => x), g.LastCheck.Solve.ZoneSphere.Keys.OrderBy(x => x));
            Assert.Equal(aw.Timeline.Solve.Applied.Count, g.LastCheck.Solve.Applied.Count);
            aw.Close();
            Ui.Pump();
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
