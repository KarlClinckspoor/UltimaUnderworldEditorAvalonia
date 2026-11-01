using Avalonia.Interactivity;

namespace UwEditor.App
{
    /// <summary>The main window's side of the object tables window (OBJECTS.DAT and COMOBJ.DAT).</summary>
    public partial class MainWindow
    {
        ObjectTablesWindow objectTablesWindow;

        /// <summary>The object tables window while it is open (for the unsaved-work prompt and closing it with the game), or null.</summary>
        public ObjectTablesWindow ObjectTablesOpen { get { return objectTablesWindow; } }

        void OnObjectTables(object sender, RoutedEventArgs e) { ShowObjectTables(); }

        /// <summary>Tools ▸ Object tables: opens (or brings up) the window for the open game.</summary>
        public ObjectTablesWindow ShowObjectTables()
        {
            if (vm.Session == null) return null;
            if (objectTablesWindow != null) { objectTablesWindow.Activate(); return objectTablesWindow; }
            var w = new ObjectTablesWindow(this, vm.Session);
            objectTablesWindow = w;
            w.Closed += (s, e) => { if (objectTablesWindow == w) objectTablesWindow = null; };
            MessageWindow.GuardClose(w, "Object tables", () => w.HasChanges, w.SaveAll, () => AskBeforeDiscarding && !quietClose);
            w.Show(this);
            return w;
        }
    }
}
