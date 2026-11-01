using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of Level ▸ Fixes and Level ▸ Item cleaner.</summary>
    public partial class MainWindow
    {
        FixesWindow fixesWindow;
        ItemCleanerWindow itemCleanerWindow;

        void OnFixes(object sender, RoutedEventArgs e) { ShowFixes(); }
        void OnItemCleaner(object sender, RoutedEventArgs e) { ShowItemCleaner(); }

        /// <summary>The Fixes window (single), checked on opening.</summary>
        public FixesWindow ShowFixes()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (fixesWindow != null && fixesWindow.Session != vm.Session) fixesWindow.Close();
            if (fixesWindow != null) { fixesWindow.Activate(); fixesWindow.Run(); return fixesWindow; }
            fixesWindow = new FixesWindow(this);
            fixesWindow.Closed += (s, e) => fixesWindow = null;
            fixesWindow.Show(this);
            return fixesWindow;
        }

        /// <summary>Closes both windows (another game opened: they hold the old session). For the open-game code to call with the other tool windows.</summary>
        public void CloseFixesWindows()
        {
            fixesWindow?.Close();
            itemCleanerWindow?.Close();
        }

        /// <summary>The item cleaner window (single).</summary>
        public ItemCleanerWindow ShowItemCleaner()
        {
            if (vm.Session == null || vm.Edits == null) return null;
            if (itemCleanerWindow != null && itemCleanerWindow.Session != vm.Session) itemCleanerWindow.Close();
            if (itemCleanerWindow != null) { itemCleanerWindow.Activate(); return itemCleanerWindow; }
            itemCleanerWindow = new ItemCleanerWindow(this);
            itemCleanerWindow.Closed += (s, e) => itemCleanerWindow = null;
            itemCleanerWindow.Show(this);
            return itemCleanerWindow;
        }
    }
}
