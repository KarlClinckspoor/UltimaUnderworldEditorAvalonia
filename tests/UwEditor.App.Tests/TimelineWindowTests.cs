using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>The automatic zones window's Phase C options and report: the timeline, the ring and the killing options.</summary>
    public class TimelineWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        [AvaloniaFact]
        public void The_timeline_options_change_the_build()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            Ui.Pump();
            Assert.DoesNotContain(aw.GetLogicalDescendants().OfType<CheckBox>(), c => (c.Content as string ?? "").StartsWith("Bashing"));
            Assert.True(Box(aw, "Ring of Levitation").IsChecked);
            Assert.False(Box(aw, "Killing NPCs").IsChecked);
            aw.BuildNow();
            Ui.Pump();
            Assert.NotNull(aw.Timeline);
            Assert.Same(aw.Timeline.Abilities, aw.Abilities);
            var text = aw.GetLogicalDescendants().OfType<TextBox>().First(t => t.IsReadOnly).Text;
            Assert.Contains("PHASE C", text);
            Assert.Contains("RETURN CHECK", text);
            Assert.Contains("CHANGES THAT CLOSE THINGS", text);
            Assert.DoesNotContain("PHASE B", text);
            string dir = Environment.GetEnvironmentVariable("AUTOZONES_SHOTS");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); new Ui.Codec().Save(Ui.Capture(aw), Path.Combine(dir, "autozones-c.png")); }
            int zones = aw.Timeline.Solve.ZoneSphere.Count;

            Box(aw, "Killing NPCs").IsChecked = true;
            Box(aw, "Ring of Levitation").IsChecked = false;
            Ui.Pump();
            Assert.True(aw.LogicOptions.KillNpcs);
            Assert.False(aw.LogicOptions.RingNeedsLava);
            aw.BuildNow();
            Ui.Pump();
            Assert.True(aw.Timeline.Options.KillNpcs);
            Assert.True(aw.Timeline.Solve.ZoneSphere.Count >= zones);
            aw.Close();
            w.Close();
        }
    }
}
