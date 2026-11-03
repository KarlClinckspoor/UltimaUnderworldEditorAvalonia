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
            Assert.Contains("COMPARISON WITH THE PAINTED MAP", text);
            Assert.Contains("MECHANISMS", text);
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
    }
}
