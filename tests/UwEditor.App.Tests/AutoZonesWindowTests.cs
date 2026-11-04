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
        public void Zone_graph_logic_check_is_the_solve_over_time_and_agrees_with_the_automatic_zones()
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
            Assert.True(g.IsAutomaticZoning);
            // the automatic zones in the graph: the same solve as the window's report, so the same outcome
            string text = g.CheckLogic();
            Assert.Contains("AUTOMATIC ZONES, PHASE C", text);
            Assert.DoesNotContain("WITHOUT WORLD CHANGES", text);
            Assert.True(g.LastCheck.Solve.GoalReached);
            Assert.Equal(aw.Timeline.Solve.ZoneSphere.Count, g.LastCheck.Solve.ZoneSphere.Count);
            Assert.Equal(aw.Timeline.Solve.Applied.Count, g.LastCheck.Solve.Applied.Count);
            aw.Close();
            Ui.Pump();
            // a painted zoning: the solve over time still decides, and the drawn links are checked after it
            w.Zoning().LoadExample();
            Ui.Pump();
            Assert.False(g.IsAutomaticZoning);
            text = g.CheckLogic();
            Assert.True(g.LastCheck.Solve.GoalReached);
            Assert.True(text.IndexOf("AUTOMATIC ZONES, PHASE C", StringComparison.Ordinal) < text.IndexOf("WITHOUT WORLD CHANGES", StringComparison.Ordinal));
            g.Close();
            w.Close();
        }
    }
}
