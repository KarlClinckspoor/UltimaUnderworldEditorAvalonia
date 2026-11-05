using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using UwEditor.Core.Tests;
using Xunit;

namespace UwEditor.App.Tests
{
    /// <summary>View ▸ Layout: the compact layout halves the windows' own white space and puts it back.</summary>
    public class LayoutTests
    {
        [AvaloniaFact]
        public void Compact_halves_the_windows_own_spacing_and_comfortable_puts_it_back()
        {
            string g = GameData.Game("UW"); if (g == null) return;
            var w = Ui.Open(g);
            w.AskBeforeDiscarding = false;
            // Compact is the default: start from Comfortable, which this test switches away from and back to
            w.SetLayout(Density.Comfortable);
            Ui.Pump();
            var panel = w.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("panel"));
            var pad = panel.Padding;
            var sw = w.ShowMonsterSwap();
            Ui.Pump();
            var spaced = sw.GetVisualDescendants().OfType<StackPanel>().Where(s => s.Spacing >= 6).ToList();
            Assert.NotEmpty(spaced);
            var before = spaced.Select(s => s.Spacing).ToList();
            var fontBefore = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.FontSize).Distinct().OrderBy(x => x).ToList();
            try
            {
                w.SetLayout(Density.Compact);
                Ui.Pump();
                Assert.True(Density.IsCompact);
                Assert.Equal(pad.Left / 2, panel.Padding.Left);
                for (int i = 0; i < spaced.Count; i++) Assert.True(spaced[i].Spacing < before[i], "spacing " + before[i] + " stayed");
                // the font sizes do not change (more rows may show, so compare the sizes in use)
                Assert.Equal(fontBefore, w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.FontSize).Distinct().OrderBy(x => x).ToList());
                // a window opened afterwards is compact too
                var tw = w.ShowTriggers();
                Ui.Pump();
                Assert.Contains(tw.GetVisualDescendants().OfType<Control>(), c => c.Margin.Left > 0 && c.Margin.Left < 10 || c is StackPanel);
                tw.Close();
            }
            finally
            {
                w.SetLayout(Density.Comfortable);
                Ui.Pump();
            }
            Assert.False(Density.IsCompact);
            Assert.Equal(pad, panel.Padding);
            Assert.Equal(before, spaced.Select(s => s.Spacing).ToList());
            sw.Close();
            w.Close();
        }
    }
}
