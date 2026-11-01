using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using UwEditor.Core;
using UwEditor.Core.Editing;

namespace UwEditor.App
{
    /// <summary>
    /// The main window's selection of several objects and tiles (Ctrl+click, Ctrl+drag), copy,
    /// cut, paste and duplicate, dragging objects in the view, and the right-click menu.
    /// </summary>
    public partial class MainWindow
    {
        readonly HashSet<int> selObjects = new HashSet<int>();
        readonly HashSet<int> selTiles = new HashSet<int>();
        ObjectClip clip;
        /// <summary>The last tile the mouse was over in the view (paste goes there).</summary>
        int hoverTileX = -1, hoverTileY = -1;
        /// <summary>The tile and object a drag would drop on.</summary>
        PickResult dragOver = PickResult.None;
        Rectangle rectShape;

        /// <summary>The objects selected together (at least two), or empty.</summary>
        public IReadOnlyCollection<int> SelectedObjects { get { return selObjects; } }
        /// <summary>The tiles selected together (x + 64 y), or empty.</summary>
        public IReadOnlyCollection<int> SelectedTiles { get { return selTiles; } }
        public ObjectClip CopiedObjects { get { return clip; } }

        void InitSelection()
        {
            View.MenuRequested += OnContextRequested;
            View.CanDrag = CanDragPick;
            View.DragMoved += OnDragMoved;
            View.DragEnded += OnDragEnded;
            View.RectChanged += OnRectChanged;
            View.RectSelected += OnRectSelected;
            View.Hovered += h => { if (h.Hit && h.Tile >= 0) { hoverTileX = h.Tile % 64; hoverTileY = h.Tile / 64; } };
            vm.LevelChanged += l => { ClearMulti(false); };
        }

        /// <summary>The objects the commands act on: the ones selected together, else the selected one.</summary>
        List<int> ChosenObjects()
        {
            if (selObjects.Count > 0) return selObjects.ToList();
            return vm.SelectedObject >= 0 ? new List<int> { vm.SelectedObject } : new List<int>();
        }

        /// <summary>The tiles the commands act on: the ones selected together, else the selected one.</summary>
        public List<(int X, int Y)> ChosenTiles()
        {
            if (selTiles.Count > 0) return selTiles.OrderBy(t => t).Select(t => (t % 64, t / 64)).ToList();
            return vm.SelectedTileX >= 0 ? new List<(int, int)> { (vm.SelectedTileX, vm.SelectedTileY) } : new List<(int, int)>();
        }

        /// <summary>Adds objects and tiles (x + 64 y) to the selection, as Ctrl+click does.</summary>
        public void AddToSelection(IEnumerable<int> objects, IEnumerable<int> tiles)
        {
            foreach (int o in objects ?? Enumerable.Empty<int>()) selObjects.Add(o);
            foreach (int t in tiles ?? Enumerable.Empty<int>()) selTiles.Add(t);
            SayMulti();
            UpdateMarkers();
        }

        public void ClearSelectionAll() { ClearMulti(); vm.ClearSelection(); }

        void ClearMulti(bool redraw = true)
        {
            if (selObjects.Count == 0 && selTiles.Count == 0) return;
            selObjects.Clear(); selTiles.Clear();
            if (redraw) UpdateMarkers();
        }

        /// <summary>Ctrl+click: adds the object (or the tile) to the selection, or takes it out. True when the click was used.</summary>
        bool MultiClick(PickResult hit)
        {
            if ((View.LastModifiers & KeyModifiers.Control) == 0 || !hit.Hit || vm.Level == null) return false;
            if (selObjects.Count == 0 && selTiles.Count == 0)
            {
                if (vm.SelectedObject >= 0) selObjects.Add(vm.SelectedObject);
                else if (vm.SelectedTileX >= 0) selTiles.Add(vm.SelectedTileX + 64 * vm.SelectedTileY);
            }
            if (hit.Object >= 0) { if (!selObjects.Remove(hit.Object)) selObjects.Add(hit.Object); }
            else if (hit.Tile >= 0) { if (!selTiles.Remove(hit.Tile)) selTiles.Add(hit.Tile); }
            vm.Select(hit.Object, hit.Tile % 64, hit.Tile / 64, true);
            SayMulti();
            UpdateMarkers();
            return true;
        }

        void SayMulti()
        {
            var parts = new List<string>();
            if (selObjects.Count > 0) parts.Add(selObjects.Count + " object" + (selObjects.Count == 1 ? "" : "s"));
            if (selTiles.Count > 0) parts.Add(selTiles.Count + " tile" + (selTiles.Count == 1 ? "" : "s"));
            vm.EditStatus = parts.Count == 0 ? "" : "Selected: " + string.Join(" and ", parts) + ". Ctrl+click adds or removes; Esc clears; the Paint tool's Paint selection paints the tiles.";
        }

        /// <summary>Selects tiles and their objects: every open tile whose middle lies in the rectangle (view coordinates).</summary>
        public int SelectInRect(Rect r)
        {
            var level = vm.Level;
            if (level == null) return 0;
            int n = 0;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var t = level.Tiles[x, y];
                    if (t.IsSolid) continue;
                    var p = Project(new Vector3(x + 0.5f, (float)level.FloorAt(x, y, 0.5, 0.5), y + 0.5f));
                    if (p == null || !r.Contains(p.Value)) continue;
                    selTiles.Add(x + 64 * y);
                    n++;
                    foreach (var o in level.TileObjects(x, y)) if (!o.Free && o.Index > 0) selObjects.Add(o.Index);
                }
            SayMulti();
            UpdateMarkers();
            return n;
        }

        void OnRectChanged(Rect? r)
        {
            if (rectShape == null)
            {
                rectShape = new Rectangle { Stroke = new SolidColorBrush(Color.Parse("#FFD72A")), StrokeThickness = 1, Fill = new SolidColorBrush(Color.Parse("#30FFD72A")), StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 } };
            }
            MarkerLabels.Children.Remove(rectShape);
            if (r == null) return;
            rectShape.Width = r.Value.Width; rectShape.Height = r.Value.Height;
            Canvas.SetLeft(rectShape, r.Value.X); Canvas.SetTop(rectShape, r.Value.Y);
            MarkerLabels.Children.Add(rectShape);
        }

        void OnRectSelected(Rect r) { SelectInRect(r); }

        /// <summary>The selection's outlines: tiles in pale blue, the objects' tiles in yellow.</summary>
        void AddSelectionOutlines(List<float> lines)
        {
            if (scene == null || vm.Level == null) return;
            var tileColour = new Vector4(0.45f, 0.8f, 1f, 1f);
            foreach (int t in selTiles) scene.TileOutline(lines, t % 64, t / 64, tileColour);
            foreach (int i in selObjects)
            {
                var o = vm.Level.Objects[i];
                if (o.TileX >= 0 && !selTiles.Contains(o.TileX + 64 * o.TileY)) scene.TileOutline(lines, o.TileX, o.TileY, SelColour);
            }
            if (dragOver.Hit && dragOver.Tile >= 0)
            {
                var c = dragOver.Object >= 0 ? new Vector4(0.4f, 1f, 0.5f, 1f) : new Vector4(1f, 1f, 1f, 1f);
                scene.TileOutline(lines, dragOver.Tile % 64, dragOver.Tile / 64, c);
            }
        }

        // ---- copy and paste

        void OnCopy(object sender, RoutedEventArgs e) { CopySelection(false); }
        void OnCut(object sender, RoutedEventArgs e) { CopySelection(true); }
        void OnPaste(object sender, RoutedEventArgs e) { PasteClip(-1, -1, -1); }
        void OnDuplicate(object sender, RoutedEventArgs e) { Duplicate(); }

        /// <summary>Copies the chosen objects (with what they hold); cut also deletes them. True when something was copied.</summary>
        public bool CopySelection(bool cut)
        {
            var e = vm.Editor;
            var objs = ChosenObjects();
            if (e == null || objs.Count == 0) { vm.EditStatus = "Select objects to copy first."; return false; }
            clip = e.Copy(objs);
            if (clip.Roots.Count == 0) { vm.EditStatus = "Nothing to copy there."; return false; }
            string what = clip.Describe(vm.Session.Names);
            if (cut) { DeleteObjects(clip.Roots.Count == objs.Count ? objs : objs, "Cut " + what); vm.EditStatus = "Cut " + what + ": paste it with Ctrl+V."; }
            else vm.EditStatus = "Copied " + what + " (" + clip.Total + " object" + (clip.Total == 1 ? "" : "s") + "). Ctrl+V pastes on the tile under the mouse, or the selected one.";
            return true;
        }

        /// <summary>Pastes the clip on a tile (−1: under the mouse, else the selected tile) or into a container (≥ 0). Returns the new objects.</summary>
        public List<int> PasteClip(int x, int y, int container)
        {
            var e = vm.Editor;
            if (e == null) return new List<int>();
            if (clip == null) { vm.EditStatus = "Nothing to paste: copy objects first (Ctrl+C)."; return new List<int>(); }
            if (x < 0)
            {
                if (hoverTileX >= 0 && View.IsPointerOver) { x = hoverTileX; y = hoverTileY; }
                else if (vm.SelectedTileX >= 0) { x = vm.SelectedTileX; y = vm.SelectedTileY; }
                else { x = Math.Clamp((int)View.Camera.Target.X, 0, 63); y = Math.Clamp((int)View.Camera.Target.Z, 0, 63); }
            }
            List<int> made = null;
            string what = clip.Describe(vm.Session.Names);
            if (!vm.Edit(ed => made = ed.Paste(clip, x, y, container), "Pasted " + what + (container >= 0 ? " inside #" + container : string.Format(" on ({0}, {1})", x, y)) + "."))
                return new List<int>();
            if (clip.HasTalker) vm.EditStatus += " A creature with a conversation was copied: the game knows talkers by their conversation number, so there are two now.";
            vm.FlushRefresh();
            selObjects.Clear(); selTiles.Clear();
            if (made.Count > 1) foreach (int i in made) selObjects.Add(i);
            vm.Select(made[0], x, y, true);
            UpdateMarkers();
            return made;
        }

        /// <summary>Copies the chosen objects and pastes the copies on the same tiles.</summary>
        public List<int> Duplicate()
        {
            var e = vm.Editor;
            var objs = ChosenObjects();
            if (e == null || objs.Count == 0) { vm.EditStatus = "Select objects to duplicate first."; return new List<int>(); }
            var o = vm.Level.Objects[objs[0]];
            var c = e.Copy(objs);
            var keep = clip;
            clip = c;
            try
            {
                if (o.OnTile) return PasteClip(o.TileX, o.TileY, -1);
                return o.Parent >= 0 ? PasteClip(0, 0, o.Parent) : new List<int>();
            }
            finally { clip = keep; }
        }

        /// <summary>Deletes several objects as one undo step.</summary>
        void DeleteObjects(List<int> objs, string label = null)
        {
            var e = vm.Editor;
            if (e == null || objs.Count == 0) return;
            var level = vm.Level;
            // the outermost first; an object inside another one deleted goes with it
            var set = new HashSet<int>(objs);
            var tops = objs.Where(i => { for (int up = level.Objects[i].Parent, g = 0; up >= 0 && g < 1024; up = level.Objects[up].Parent, g++) if (set.Contains(up)) return false; return true; }).ToList();
            e.BeginBatch(label ?? "Delete " + tops.Count + " objects");
            int done = 0;
            try
            {
                foreach (int i in tops)
                {
                    var o = e.Model.Objects[i];
                    if (o.Free) continue;
                    try { e.DeleteObject(i); done++; } catch (Exception ex) { vm.EditStatus = ex.Message; }
                }
            }
            finally { e.EndBatch(); }
            vm.FlushRefresh();
            selObjects.Clear();
            vm.Select(-1, vm.SelectedTileX, vm.SelectedTileY, true);
            vm.EditStatus = "Deleted " + done + " object" + (done == 1 ? "" : "s") + " (one undo step).";
            UpdateMarkers();
        }

        // ---- dragging objects in the view

        bool CanDragPick(PickResult hit)
        {
            if (!hit.Hit || hit.Object < 0 || vm.Editor == null || pick != null || carrying != null) return false;
            return hit.Object == vm.SelectedObject || selObjects.Contains(hit.Object);
        }

        void OnDragMoved(PickResult hit)
        {
            dragOver = hit;
            var level = vm.Level;
            if (level != null && hit.Hit && hit.Tile >= 0)
            {
                int n = Math.Max(1, selObjects.Count);
                if (hit.Object >= 0 && !ChosenObjects().Contains(hit.Object) && CanTakeAll(hit.Object))
                    vm.EditStatus = "Drop to put " + (n == 1 ? level.Objects[vm.SelectedObject].Name : n + " objects") + " inside " + level.Objects[hit.Object].Name + " (Esc cancels).";
                else vm.EditStatus = string.Format("Drop to move {0} to ({1}, {2}) (Esc cancels).", n == 1 && vm.SelectedObject >= 0 ? level.Objects[vm.SelectedObject].Name : n + " objects", hit.Tile % 64, hit.Tile / 64);
            }
            UpdateMarkers();
        }

        bool CanTakeAll(int container)
        {
            var e = vm.Editor;
            return e != null && ChosenObjects().All(i => e.CanPutInside(i, container));
        }

        void OnDragEnded(PickResult hit)
        {
            dragOver = PickResult.None;
            var e = vm.Editor;
            var level = vm.Level;
            if (!hit.Hit || hit.Tile < 0 || e == null || level == null) { vm.EditStatus = "Not moved."; UpdateMarkers(); return; }
            var objs = ChosenObjects();
            int grabbed = vm.SelectedObject >= 0 ? vm.SelectedObject : objs[0];
            int tx = hit.Tile % 64, ty = hit.Tile / 64;
            if (hit.Object >= 0 && !objs.Contains(hit.Object) && CanTakeAll(hit.Object))
            {
                int into = hit.Object;
                e.BeginBatch("Put " + objs.Count + " object(s) in #" + into);
                try { foreach (int i in objs) e.PutInside(i, into); }
                catch (Exception ex) { vm.EditStatus = ex.Message; }
                finally { e.EndBatch(); }
                vm.FlushRefresh();
                vm.Select(grabbed, -1, -1, true);
                vm.EditStatus = "Put " + objs.Count + " object(s) inside #" + into + " " + level.Objects[into].Name + ".";
                UpdateMarkers();
                return;
            }
            var g = level.Objects[grabbed];
            int dx = g.OnTile ? tx - g.TileX : 0, dy = g.OnTile ? ty - g.TileY : 0;
            int fx = Math.Clamp((int)((hit.Point.X - tx) * 8), 0, 7), fy = Math.Clamp((int)((hit.Point.Z - ty) * 8), 0, 7);
            int moved = 0;
            e.BeginBatch(objs.Count == 1 ? "Move #" + grabbed : "Move " + objs.Count + " objects");
            try
            {
                foreach (int i in objs)
                {
                    var o = e.Model.Objects[i];
                    if (!o.OnTile && i != grabbed) continue;
                    int nx = o.OnTile ? o.TileX + dx : tx, ny = o.OnTile ? o.TileY + dy : ty;
                    if (nx < 0 || ny < 0 || nx > 63 || ny > 63 || e.Model.Tiles[nx, ny].IsSolid) continue;
                    if (i == grabbed) e.MoveObjectAnywhere(i, nx, ny, fx, fy); else e.MoveObjectAnywhere(i, nx, ny);
                    moved++;
                }
            }
            catch (Exception ex) { vm.EditStatus = ex.Message; }
            finally { e.EndBatch(); }
            vm.FlushRefresh();
            vm.Select(grabbed, -1, -1, true);
            if (moved > 0) vm.EditStatus = string.Format("Moved {0} object{1} (one undo step).", moved, moved == 1 ? "" : "s");
            UpdateMarkers();
        }

        // ---- the right-click menu

        void OnContextRequested(PickResult hit)
        {
            var level = vm.Level;
            if (level == null) return;
            var items = ContextItems(hit);
            if (items.Count == 0) return;
            var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
            menu.Open(View);
        }

        static MenuItem Item(string header, Action a, bool enabled = true, string gesture = null)
        {
            var m = new MenuItem { Header = header, IsEnabled = enabled };
            if (gesture != null) m.InputGesture = KeyGesture.Parse(gesture);
            m.Click += (s, e) => a();
            return m;
        }

        /// <summary>The right-click menu's commands for an object or a tile (public for the tests).</summary>
        public List<Control> ContextItems(PickResult hit)
        {
            var level = vm.Level;
            var items = new List<Control>();
            if (level == null || !hit.Hit) return items;
            int tx = hit.Tile >= 0 ? hit.Tile % 64 : -1, ty = hit.Tile >= 0 ? hit.Tile / 64 : -1;
            bool editable = vm.Editor != null;
            if (hit.Object >= 0)
            {
                var o = level.Objects[hit.Object];
                if (!ChosenObjects().Contains(o.Index)) { ClearMulti(); vm.Select(o.Index, tx, ty, true); }
                int n = ChosenObjects().Count;
                items.Add(new MenuItem { Header = n > 1 ? n + " objects" : "#" + o.Index + " " + o.Name, IsEnabled = false });
                items.Add(Item("_Copy", () => CopySelection(false), editable, "Ctrl+C"));
                items.Add(Item("Cu_t", () => CopySelection(true), editable, "Ctrl+X"));
                items.Add(Item("_Duplicate", () => Duplicate(), editable, "Ctrl+D"));
                bool container = !o.IsQuantity;
                if (clip != null) items.Add(Item("_Paste inside", () => PasteClip(0, 0, o.Index), editable && container));
                items.Add(Item("_Add an object inside…", () => { vm.Select(o.Index, tx, ty, true); _ = AddObject(); }, editable && container));
                items.Add(Item("De_lete", DeleteSelected, editable, "Delete"));
                items.Add(new Separator());
                if (o.IsTrapOrTrigger)
                {
                    if (vm.HasTargetSquare) items.Add(Item("Choose the _target in the view", StartPickTarget, editable));
                    if (vm.IsTeleportSelected) items.Add(Item("_Go to the teleport's target", () => GoToTeleportTarget()));
                    items.Add(Item("Make it _run a trap: click the trap…", () => StartLinkPick(o.Index), editable));
                }
                else items.Add(Item("Put _inside: click the container…", () => StartInsidePick(), editable));
                items.Add(Item("_Frame it", Frame, true, "F"));
                if (o.OnTile && tx >= 0) items.Add(Item("Select its _tile", () => { ClearMulti(); vm.Select(-1, tx, ty, true); UpdateMarkers(); }));
                foreach (var extra in ExtraObjectItems(o)) items.Add(extra);
            }
            if (tx >= 0)
            {
                if (hit.Object >= 0) items.Add(new Separator());
                var t = level.Tiles[tx, ty];
                items.Add(new MenuItem { Header = string.Format("Tile ({0}, {1})", tx, ty), IsEnabled = false });
                items.Add(Item("Add an _object here…", () => { ClearMulti(); vm.Select(-1, tx, ty, true); _ = AddObject(tx, ty, true); }, editable && !t.IsSolid, "Insert"));
                if (clip != null) items.Add(Item("_Paste here", () => PasteClip(tx, ty, -1), editable && !t.IsSolid, "Ctrl+V"));
                foreach (var extra in ExtraTileItems(tx, ty)) items.Add(extra);
                items.Add(Item("Move the new-game _start here", () => { vm.Select(-1, tx, ty, true); MoveMarker("start", level.Level, tx, ty); }, !t.IsSolid && level.Level == 1 && vm.Session.ExePath != null));
                if (vm.Session.IsSave) items.Add(Item("Move the _player here", () => { vm.Select(-1, tx, ty, true); MoveMarker("player", level.Level, tx, ty); }, !t.IsSolid));
            }
            return items;
        }

        // ---- picks started from the menu: a trap for a trigger, a container for the selection

        /// <summary>What the next click in the view chooses: "link" (a trap for trigger Index) or "inside" (a container for the chosen objects).</summary>
        sealed class ObjectPick { public string Kind; public int Index; }
        ObjectPick objectPick;

        void StartLinkPick(int trigger)
        {
            objectPick = new ObjectPick { Kind = "link", Index = trigger };
            vm.EditStatus = "Click the trap #" + trigger + " should run (Esc cancels). The trap goes into its chain.";
        }

        void StartInsidePick()
        {
            objectPick = new ObjectPick { Kind = "inside" };
            vm.EditStatus = "Click the container, creature or trigger to put " + ChosenObjects().Count + " object(s) in (Esc cancels).";
        }

        /// <summary>For the view's clicks while a pick from the menu waits. True when the click was used.</summary>
        bool ObjectPickClick(PickResult hit)
        {
            if (objectPick == null) return false;
            var p = objectPick;
            objectPick = null;
            var e = vm.Editor;
            if (!hit.Hit || hit.Object < 0 || e == null) { vm.EditStatus = "Cancelled: no object there."; return true; }
            if (p.Kind == "link")
            {
                var trap = vm.Level.Objects[hit.Object];
                if (!trap.IsTrapOrTrigger) { vm.EditStatus = trap.Name + " is not a trap."; return true; }
                if (vm.Edit(ed => ed.PutInside(hit.Object, p.Index), "#" + p.Index + " now runs #" + hit.Object + " " + trap.Name + "."))
                { vm.FlushRefresh(); vm.Select(p.Index, -1, -1, true); }
                return true;
            }
            var objs = ChosenObjects();
            if (!CanTakeAll(hit.Object)) { vm.EditStatus = vm.Level.Objects[hit.Object].Name + " cannot hold them."; return true; }
            e.BeginBatch("Put " + objs.Count + " object(s) in #" + hit.Object);
            try { foreach (int i in objs) e.PutInside(i, hit.Object); }
            catch (Exception ex) { vm.EditStatus = ex.Message; }
            finally { e.EndBatch(); }
            vm.FlushRefresh();
            ClearMulti();
            vm.Select(hit.Object, -1, -1, true);
            return true;
        }
    }
}
