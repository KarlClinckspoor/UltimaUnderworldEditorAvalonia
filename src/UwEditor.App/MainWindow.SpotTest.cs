using System;
using System.IO;
using System.Linq;
using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>Run and Test ▸ DOSBox spot test: the Exhumed check's stops, four saved games at a time, played by hand in DOSBox.</summary>
    public partial class MainWindow
    {
        SpotTestWindow spotTestWindow;
        /// <summary>Where the spot test's copy of the game goes (the settings folder; tests may set another).</summary>
        public string SpotTestFolder;
        /// <summary>Tests set this to false: the saved games are written, DOSBox is not started (it counts as closed at once).</summary>
        public bool SpotTestLaunch = true;

        void OnSpotTest(object sender, RoutedEventArgs e) { ShowSpotTest(); }

        public SpotTestWindow ShowSpotTest()
        {
            if (vm.Session == null) return null;
            if (spotTestWindow != null) { spotTestWindow.Activate(); return spotTestWindow; }
            spotTestWindow = new SpotTestWindow(this);
            spotTestWindow.Closed += (s, e) => spotTestWindow = null;
            spotTestWindow.Show(this);
            return spotTestWindow;
        }

        /// <summary>The folder a spot test of the open game goes in.</summary>
        public string SpotTestFolderFor(Core.GameSession s)
        {
            return SpotTestFolder ?? Path.Combine(Settings.Folder, "Spot test", (s.IsUw2 ? "uw2-" : "uw1-") + new string(Path.GetFileName(s.GameFolder.TrimEnd('/', '\\')).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()));
        }
    }
}
