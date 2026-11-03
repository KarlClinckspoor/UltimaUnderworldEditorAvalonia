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
    /// <summary>The automatic zones window's Phase B options: the logic checkboxes change the build and its report.</summary>
    public class AbilityZonesWindowTests
    {
        static string Uw1 { get { return GameData.Game("UW"); } }

        static void Shot(Window w, string name)
        {
            string dir = Environment.GetEnvironmentVariable("AUTOZONES_SHOTS");
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            new Ui.Codec().Save(Ui.Capture(w), Path.Combine(dir, name + ".png"));
        }

        static CheckBox Box(Window w, string start)
        {
            return w.GetLogicalDescendants().OfType<CheckBox>().First(c => (c.Content as string ?? "").StartsWith(start));
        }

        [AvaloniaFact]
        public void The_logic_options_change_the_solve()
        {
            if (Uw1 == null) return;
            var w = Ui.Open(Uw1);
            w.AskBeforeDiscarding = false;
            var aw = w.ShowAutoZones();
            Ui.Pump();
            aw.BuildNow();
            Ui.Pump();
            Assert.NotNull(aw.Abilities);
            Assert.Same(aw.Abilities.Auto, aw.Result);
            var text = aw.GetLogicalDescendants().OfType<TextBox>().First(t => t.IsReadOnly).Text;
            Assert.Contains("PHASE B", text);
            Assert.Contains("HIDDEN GATES", text);
            Assert.Contains("COMPARISON WITH THE PAINTED MAP", text);
            int withMagic = aw.Abilities.Solve.ZoneSphere.Count;
            Shot(aw, "autozones-b");

            // Magic off, lock picking off (bashing is always on since Phase C): fewer zones, and the doors to open are listed
            Box(aw, "Magic").IsChecked = false;
            Ui.Pump();
            Assert.False(aw.LogicOptions.Magic);
            Assert.False(aw.LogicOptions.LockPicking);
            aw.BuildNow();
            Ui.Pump();
            Assert.False(aw.Abilities.Options.Magic);
            Assert.True(aw.Abilities.Solve.ZoneSphere.Count < withMagic);
            Assert.NotEmpty(aw.Abilities.DoorsToOpen);
            text = aw.GetLogicalDescendants().OfType<TextBox>().First(t => t.IsReadOnly).Text;
            Assert.Contains("DOORS THE SHUFFLE MUST OPEN", text);
            aw.Close();
            w.Close();
        }
    }
}
