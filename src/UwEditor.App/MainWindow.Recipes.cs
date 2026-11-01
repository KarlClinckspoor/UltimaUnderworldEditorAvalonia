using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>A square (and maybe an object on it) chosen in the view or from the selection, for tool windows.</summary>
    public sealed class PickedPlace
    {
        public int Level, X, Y;
        /// <summary>The object clicked or selected, or -1 for the square alone.</summary>
        public int Object = -1;
        public override string ToString() { return string.Format("({0}, {1}), level {2}{3}", X, Y, Level, Object >= 0 ? ", #" + Object : ""); }
    }

    /// <summary>The main window's side of the trap recipes: the window, and a click in the view to choose a square or an object for it.</summary>
    public partial class MainWindow
    {
        TrapRecipesWindow trapRecipesWindow;
        Action<PickedPlace> placePick;
        string placePrompt;
        bool placePickHooked;

        void OnTrapRecipes(object sender, RoutedEventArgs e) { ShowTrapRecipes(); }

        public TrapRecipesWindow ShowTrapRecipes()
        {
            if (vm.Session == null || vm.Edits == null || vm.Level == null) return null;
            if (trapRecipesWindow != null) { trapRecipesWindow.Activate(); return trapRecipesWindow; }
            trapRecipesWindow = new TrapRecipesWindow(this);
            trapRecipesWindow.Closed += (s, e) => { trapRecipesWindow = null; CancelPlacePick(); };
            trapRecipesWindow.Show(this);
            return trapRecipesWindow;
        }

        /// <summary>
        /// Waits for the next click in the view (the Select tool is chosen for it): done gets the
        /// level shown, the square and the object clicked. Esc cancels (done is not called). A
        /// new request replaces one still waiting.
        /// </summary>
        public void RequestPlacePick(string prompt, Action<PickedPlace> done)
        {
            if (vm.Level == null) return;
            if (!placePickHooked)
            {
                placePickHooked = true;
                // after the main window's own click handling, which selects what was clicked
                View.Picked += OnPlacePicked;
                AddHandler(KeyDownEvent, (s, e) => { if (e.Key == Key.Escape && placePick != null) { CancelPlacePick(); vm.EditStatus = "Cancelled."; } }, RoutingStrategies.Tunnel);
            }
            vm.ToolIndex = 0;
            placePick = done;
            placePrompt = prompt;
            vm.EditStatus = prompt + " (Esc cancels)";
        }

        public bool IsPickingPlace { get { return placePick != null; } }

        public void CancelPlacePick() { placePick = null; placePrompt = null; }

        void OnPlacePicked(PickResult hit) { CompletePlacePick(hit.Object, hit.Tile); }

        /// <summary>Hands a click (object index or -1, tile x + 64 y or -1) to the waiting request; for the view and tests.</summary>
        public void CompletePlacePick(int obj, int tile)
        {
            var done = placePick;
            var level = vm.Level;
            if (done == null || level == null) return;
            int x = tile >= 0 ? tile % 64 : -1, y = tile >= 0 ? tile / 64 : -1;
            if (obj >= 0 && obj < level.Objects.Length && level.Objects[obj].TileX >= 0) { x = level.Objects[obj].TileX; y = level.Objects[obj].TileY; }
            if (x < 0) { vm.EditStatus = placePrompt + " (Esc cancels)"; return; }
            CancelPlacePick();
            done(new PickedPlace { Level = level.Level, X = x, Y = y, Object = obj });
        }

        /// <summary>The selected object or tile in the view, or null.</summary>
        public PickedPlace SelectedPlace()
        {
            var level = vm.Level;
            if (level == null) return null;
            int obj = vm.SelectedObject;
            if (obj >= 0 && level.Objects[obj].TileX >= 0) return new PickedPlace { Level = level.Level, X = level.Objects[obj].TileX, Y = level.Objects[obj].TileY, Object = obj };
            if (vm.SelectedTileX >= 0) return new PickedPlace { Level = level.Level, X = vm.SelectedTileX, Y = vm.SelectedTileY };
            return null;
        }

        /// <summary>Shows an object in the view and selects it (after a recipe made it).</summary>
        public void SelectMade(int level, int index)
        {
            if (vm.Level == null) return;
            if (vm.Level.Level != level) vm.LoadLevel(level);
            vm.FlushRefresh();
            var o = vm.Level.Objects[index];
            vm.Select(index, o.TileX, o.TileY, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
        }
    }
}
