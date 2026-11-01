using System.Collections.Generic;
using System.Linq;
using Avalonia.Interactivity;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>The undo history and world check windows.</summary>
    public partial class MainWindow
    {
        UndoHistoryWindow historyWindow;
        WorldCheckWindow worldCheckWindow;

        void OnUndoHistory(object sender, RoutedEventArgs e) { ShowUndoHistory(); }
        void OnWorldCheck(object sender, RoutedEventArgs e) { ShowWorldCheck(); }

        public UndoHistoryWindow ShowUndoHistory()
        {
            if (vm.Session == null) return null;
            if (historyWindow != null) { historyWindow.Activate(); historyWindow.Fill(); return historyWindow; }
            historyWindow = new UndoHistoryWindow(this);
            historyWindow.Closed += (s, e) => historyWindow = null;
            historyWindow.Show(this);
            return historyWindow;
        }

        public WorldCheckWindow ShowWorldCheck()
        {
            if (vm.Session == null) return null;
            if (worldCheckWindow != null) { worldCheckWindow.Activate(); worldCheckWindow.Run(); return worldCheckWindow; }
            worldCheckWindow = new WorldCheckWindow(this);
            worldCheckWindow.Closed += (s, e) => worldCheckWindow = null;
            worldCheckWindow.Show(this);
            return worldCheckWindow;
        }

        /// <summary>The lock numbers of the keys the player carries in an open saved game (null for a new game).</summary>
        public List<int> CarriedKeys()
        {
            var sheet = PlayerSheetOf();
            if (sheet == null) return null;
            return sheet.Inventory.All().Where(i => KeyIndex.IsKey(i.ItemId, vm.Session.Kind)).Select(i => i.Owner).ToList();
        }
    }
}
