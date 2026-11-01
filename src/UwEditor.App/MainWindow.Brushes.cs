using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Randomizer;

namespace UwEditor.App
{
    /// <summary>The brush beyond one tile: the tile template, fill by the movement rules, painting the selection; the 2D map and its picture.</summary>
    public partial class MainWindow
    {
        void OnTakeTemplate(object sender, RoutedEventArgs e)
        {
            if (!vm.TakeTemplate(vm.SelectedTileX, vm.SelectedTileY)) vm.EditStatus = "Select a tile first: the template is taken from it.";
        }

        void OnPaintSelection(object sender, RoutedEventArgs e) { PaintTiles(ChosenTiles(), "Paint selection"); }

        /// <summary>Paints the brush on several tiles as one undo step. Returns how many were painted.</summary>
        public int PaintTiles(IList<(int X, int Y)> tiles, string label)
        {
            var e = vm.Editor;
            if (e == null || tiles.Count == 0) { vm.EditStatus = "Select tiles first (Ctrl+click or Ctrl+drag)."; return 0; }
            e.BeginBatch(label + ": " + MainViewModel.PaintModeNames[vm.PaintMode].ToLowerInvariant());
            try { foreach (var t in tiles) vm.Paint(t.X, t.Y); }
            finally { e.EndBatch(); }
            vm.EditStatus = string.Format("{0}: {1} on {2} tile{3} (one undo step).", label, MainViewModel.PaintModeNames[vm.PaintMode].ToLowerInvariant(), tiles.Count, tiles.Count == 1 ? "" : "s");
            return tiles.Count;
        }

        /// <summary>The tiles the player can walk to from (x, y) by the zones' movement rules (no rock, no doors, climbs of at most the zone climb).</summary>
        public List<(int X, int Y)> WalkableFrom(int x, int y)
        {
            var level = vm.Level;
            if (level == null) return new List<(int, int)>();
            var rules = new ZoneFlood(level, ZoneClimb);
            return rules.Reach(new byte[64 * 64], x, y).Select(k => (k % 64, k / 64)).ToList();
        }

        /// <summary>Fill: paints every tile reached from (x, y). Returns how many.</summary>
        public int FillFrom(int x, int y)
        {
            var tiles = WalkableFrom(x, y);
            if (tiles.Count == 0) { vm.EditStatus = string.Format("({0}, {1}) is rock or holds a door: nothing to fill.", x, y); return 0; }
            return PaintTiles(tiles, string.Format("Fill from ({0}, {1})", x, y));
        }

        /// <summary>The right-click menu's tile commands of the brush and the play test.</summary>
        IEnumerable<Control> ExtraTileItems(int tx, int ty)
        {
            bool editable = vm.Editor != null;
            var level = vm.Level;
            bool open = level != null && !level.Tiles[tx, ty].IsSolid;
            yield return Item("Use as the brush _template", () => { vm.TakeTemplate(tx, ty); vm.PaintMode = 6; });
            yield return Item("_Fill from here with the brush", () => FillFrom(tx, ty), editable && open);
            if (selTiles.Count > 0) yield return Item("Paint the _selected tiles with the brush", () => PaintTiles(ChosenTiles(), "Paint selection"), editable);
            yield return Item("Select the tiles _walkable from here", () =>
            {
                foreach (var t in WalkableFrom(tx, ty)) selTiles.Add(t.X + 64 * t.Y);
                SayMulti(); UpdateMarkers();
            }, open);
            yield return Item("Play-test _from here…", () => _ = PlayTest(tx, ty), open);
            yield return Item("Trap _recipes…", () => { vm.Select(-1, tx, ty, true); ShowTrapRecipes(); }, editable);
        }

        /// <summary>The right-click menu's extra object commands (trap recipes and the like hook in here).</summary>
        IEnumerable<Control> ExtraObjectItems(ObjectInfo o)
        {
            yield return Item("Trap _recipes…", () => ShowTrapRecipes(), vm.Editor != null);
        }

        // ---- the 2D map

        bool ceilingBefore2D;

        void OnToggleMap(object sender, RoutedEventArgs e) { ToggleMap(); }

        /// <summary>Switches between the 3D view and the 2D map (Tab in the view).</summary>
        public void ToggleMap()
        {
            if (vm.Level == null) return;
            bool on = !View.TopDown;
            if (on) { ceilingBefore2D = vm.ShowCeiling; vm.ShowCeiling = false; }
            View.SetTopDown(on);
            if (!on) vm.ShowCeiling = ceilingBefore2D;
            MapItem.IsChecked = on;
            vm.EditStatus = on ? "2D map: drag to move, wheel to zoom; clicks, Ctrl+drag selection and painting work as in 3D. Tab goes back." : "3D view.";
        }

        async void OnExportMap(object sender, RoutedEventArgs e)
        {
            if (vm.Level == null) return;
            try
            {
                var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = (vm.Session.IsUw2 ? "uw2" : "uw1") + "-level-" + vm.Level.Level + ".png", DefaultExtension = "png",
                    FileTypeChoices = new[] { new FilePickerFileType("PNG picture") { Patterns = new[] { "*.png" } } },
                });
                string path = f?.TryGetLocalPath();
                if (path == null) return;
                System.IO.File.WriteAllBytes(path, MapPicture.Png(vm.Level, 16));
                vm.EditStatus = "Map written to " + path + ".";
            }
            catch (Exception ex) { vm.EditStatus = "Could not write the map: " + ex.Message; }
        }
    }
}
