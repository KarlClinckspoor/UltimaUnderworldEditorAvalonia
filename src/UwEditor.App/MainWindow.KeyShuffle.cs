using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of Randomizer ▸ Scatter ▸ Apply…</summary>
    public partial class MainWindow
    {
        KeyShuffleWindow keyShuffleWindow;

        void OnKeyShuffle(object sender, RoutedEventArgs e) { ShowKeyShuffle(); }

        /// <summary>Opens the key item shuffle window (one at a time).</summary>
        public KeyShuffleWindow ShowKeyShuffle()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (keyShuffleWindow != null) { keyShuffleWindow.Activate(); return keyShuffleWindow; }
            var w = new KeyShuffleWindow(this, vm.Session);
            keyShuffleWindow = w;
            w.Closed += (s, e) => { if (keyShuffleWindow == w) keyShuffleWindow = null; };
            w.Show(this);
            return w;
        }
    }
}
