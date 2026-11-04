using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of the automatic zones (Randomizer ▸ Scatter ▸ Automatic zones…).</summary>
    public partial class MainWindow
    {
        AutoZonesWindow autoZonesWindow;

        void OnAutoZones(object sender, RoutedEventArgs e) { ShowAutoZones(); }

        /// <summary>The automatic zones window if it is open (the zone graph's logic check takes its options), or null.</summary>
        public AutoZonesWindow OpenAutoZones { get { return autoZonesWindow; } }

        /// <summary>Opens the automatic zones window (one at a time).</summary>
        public AutoZonesWindow ShowAutoZones()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (autoZonesWindow != null) { autoZonesWindow.Activate(); return autoZonesWindow; }
            autoZonesWindow = new AutoZonesWindow(this, vm.Session);
            autoZonesWindow.Closed += (s, e) => autoZonesWindow = null;
            autoZonesWindow.Show(this);
            return autoZonesWindow;
        }
    }
}
