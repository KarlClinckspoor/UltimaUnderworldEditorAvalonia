using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core;

namespace UwEditor.App
{
    /// <summary>A texture slot of the level, for the paint brush.</summary>
    public sealed class SlotItem
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public Bitmap Picture { get; set; }
    }

    /// <summary>The editing side of the main window: the level's edits, the tools and the brush.</summary>
    public sealed partial class MainViewModel
    {
        public LevelEdits Edits { get; private set; }
        /// <summary>The editor of the level shown (made on the first edit).</summary>
        public LevelEditor Editor { get { return level == null || !level.Present || Edits == null ? null : Edits.For(level.Level); } }   // an empty slot has no free lists to edit with
        /// <summary>The level was changed in place (not another level): redraw, keep the camera.</summary>
        public event Action LevelEdited;

        string editStatus = "";
        public string EditStatus { get { return editStatus; } set { Set(ref editStatus, value); } }

        public bool CanUndo { get { var e = level == null ? null : Edits?.Existing(level.Level); return e != null && e.CanUndo; } }
        public bool CanRedo { get { var e = level == null ? null : Edits?.Existing(level.Level); return e != null && e.CanRedo; } }
        public bool HasLevelChanges { get { return Edits != null && Edits.HasChanges; } }
        /// <summary>Unsaved work Save (Ctrl+S) writes besides the levels: the player (set by the main window).</summary>
        public Func<bool> OtherUnsaved;
        public bool CanSave { get { return HasLevelChanges || (OtherUnsaved != null && OtherUnsaved()); } }
        public string UndoText { get { var e = level == null ? null : Edits?.Existing(level.Level); return e != null && e.CanUndo ? "Undo " + e.UndoLabel : "Undo"; } }
        public string RedoText { get { var e = level == null ? null : Edits?.Existing(level.Level); return e != null && e.CanRedo ? "Redo " + e.RedoLabel : "Redo"; } }
        public string ChangedLevels
        {
            get
            {
                if (Edits == null || !Edits.HasChanges) return "No unsaved level changes";
                var l = Edits.DirtyLevels.ToList();
                return "Unsaved: level" + (l.Count == 1 ? " " : "s ") + string.Join(", ", l);
            }
        }

        /// <summary>After STRINGS.PAK was saved: names and texts again, the camera kept.</summary>
        public void RefreshNames()
        {
            icons.Clear();
            Edits?.Reread();
            if (level != null) { forceTree = true; RefreshLevel(); }
        }

        /// <summary>The next refresh rebuilds the object list even when no object changed (the names did).</summary>
        bool forceTree;

        /// <summary>The start or the player moved: the status bar's text.</summary>
        public void OnStartMoved()
        {
            var st = session?.Start;
            if (st == null) return;
            string start = session.NewGameStart == null ? "" : string.Format("new game ({0}, {1})", (int)session.NewGameStart.X, (int)session.NewGameStart.Y);
            StartText = session.PlayerPoint != null ? session.PlayerPoint.Description + " · " + start : session.NewGameStart.Description;
        }

        public void RaiseEditState()
        {
            foreach (var n in new[] { nameof(CanUndo), nameof(CanRedo), nameof(HasLevelChanges), nameof(CanSave), nameof(UndoText), nameof(RedoText), nameof(ChangedLevels) }) Raise(n);
        }

        bool refreshPending;

        /// <summary>Refreshes once the edits of this moment are done (a paint stroke changes many tiles).</summary>
        void ScheduleRefresh()
        {
            RaiseEditState();
            if (refreshPending) return;
            refreshPending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { refreshPending = false; RefreshLevel(); }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>Runs a pending refresh now (tests, and before reading the level after an edit).</summary>
        public void FlushRefresh()
        {
            if (!refreshPending) return;
            refreshPending = false;
            RefreshLevel();
        }

        // ---- traps and triggers

        string objectHelp = "";
        bool isTrap, hasTargetSquare;
        int trapVariable = -1;
        public string ObjectHelp { get { return objectHelp; } private set { Set(ref objectHelp, value); Raise(nameof(HasObjectHelp)); } }
        public bool HasObjectHelp { get { return !string.IsNullOrEmpty(objectHelp); } }
        public bool IsTrapSelected { get { return isTrap; } private set { Set(ref isTrap, value); } }
        public bool HasTargetSquare { get { return hasTargetSquare; } private set { Set(ref hasTargetSquare, value); } }
        bool isTeleport;
        /// <summary>A teleport trap is selected (Go to the teleport's target).</summary>
        public bool IsTeleportSelected { get { return isTeleport; } private set { Set(ref isTeleport, value); } }
        public int TrapVariable { get { return trapVariable; } private set { Set(ref trapVariable, value); Raise(nameof(HasTrapVariable)); } }
        public bool HasTrapVariable { get { return trapVariable >= 0; } }
        /// <summary>Where the selected trap or trigger acts, on any level.</summary>
        public List<TrapTarget> Targets { get; private set; } = new List<TrapTarget>();

        void UpdateObjectHelp(ObjectInfo o)
        {
            if (o == null || session == null || !o.IsTrapOrTrigger)
            {
                ObjectHelp = ""; IsTrapSelected = false; HasTargetSquare = false; IsTeleportSelected = false; TrapVariable = -1; Targets = new List<TrapTarget>();
                return;
            }
            string help = Triggers.Explain(session, o) ?? "";
            Targets = Triggers.Targets(level, o);
            if (Targets.Count > 0) help = "Acts on: " + string.Join("; ", Targets.Select(t => t.ToString())) + "\n\n" + help;
            ObjectHelp = help;
            IsTrapSelected = true;
            HasTargetSquare = Triggers.HasTargetSquare(o.Item);
            IsTeleportSelected = Triggers.IsTeleport(o.Item);
            TrapVariable = Triggers.VariableOf(session, o);
        }

        /// <summary>Sets a trigger's, teleport's or delete trap's target square to (x, y) on a level (the teleport's level from z).</summary>
        public bool SetTarget(int level, int index, int toLevel, int x, int y)
        {
            var e = Edits.For(level);
            var o = e.Model.Objects[index];
            var S = o.IsMobile ? UltimateEditor.Uw1.Uw1Schemas.MobileObject : UltimateEditor.Uw1.Uw1Schemas.StaticObject;
            try
            {
                if (toLevel != level && !Triggers.IsTeleport(o.Item)) throw new InvalidOperationException("Only a teleport trap reaches another level; " + o.Name + " acts on its own level.");
                e.BeginBatch("Target of #" + index);
                try
                {
                    e.SetObjectField(index, S["Quality"], x);
                    e.SetObjectField(index, S["Owner"], y);
                    if (Triggers.IsTeleport(o.Item)) e.SetObjectField(index, S["Z"], toLevel == level ? 0 : toLevel);
                }
                finally { e.EndBatch(); }
                EditStatus = string.Format("#{0} {1} now acts on ({2}, {3}){4}.", index, o.Name, x, y, toLevel != level ? " of level " + toLevel : "");
                return true;
            }
            catch (Exception ex) { EditStatus = ex.Message; return false; }
        }

        /// <summary>After an edit of the level shown: the model again, the selection kept where it still exists.</summary>
        public void RefreshLevel()
        {
            if (level == null) return;
            int obj = selectedObject, tx = selectedTileX, ty = selectedTileY;
            var before = level;
            level = Edits.Model(level.Level);
            Raise(nameof(Level));
            Raise(nameof(LevelSummary));
            // a tile edit leaves the objects as they were: the list (and what is open in it) stays;
            // a change of fields only updates the changed objects' lines (ViewModels.Looks.cs)
            if (forceTree || before == null || before.Level != level.Level || !LevelScene.SameObjects(before, level))
                if (forceTree || !UpdateTreeInPlace(before)) BuildTree();
            forceTree = false;
            if (obj >= 0 && (level.Objects[obj].Free || (level.Objects[obj].TileX < 0 && level.Objects[obj].Parent < 0))) obj = -1;
            if (obj >= 0 || tx >= 0) Select(obj, tx, ty, true); else ClearSelection();
            RaiseEditState();
            LevelEdited?.Invoke();
        }

        /// <summary>Runs an edit, reporting a refusal in the status line. Returns false when refused.</summary>
        public bool Edit(Action<LevelEditor> a, string done = null)
        {
            var e = Editor;
            if (e == null) return false;
            try
            {
                a(e);
                if (done != null) EditStatus = done;
                return true;
            }
            catch (Exception ex) { EditStatus = ex.Message; return false; }
        }

        public static bool TryParse(string text, out int v)
        {
            text = (text ?? "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>A value typed into the properties panel.</summary>
        public void EditField(PropertyRow row, string text)
        {
            int v;
            if (!TryParse(text, out v)) { EditStatus = "'" + text + "' is not a number."; ReselectFresh(); return; }
            bool ok = row.Target == "object"
                ? Edit(e => e.SetObjectField(row.TargetIndex, row.Field, v), string.Format("{0} of #{1} set to {2}.", row.Name, row.TargetIndex, v))
                : Edit(e => e.SetTileField(row.TargetIndex % 64, row.TargetIndex / 64, row.Field.Name, v), string.Format("{0} of tile ({1}, {2}) set to {3}.", row.Name, row.TargetIndex % 64, row.TargetIndex / 64, v));
            if (!ok) ReselectFresh();   // show the value as it is
        }

        // ---- tools and the brush

        int toolIndex, paintMode, paintSlot, paintType = 1;
        decimal? paintHeight = 4;
        public static readonly string[] ToolNames = { "Select", "Move", "Paint" };
        public string[] Tools { get { return ToolNames; } }
        public int ToolIndex { get { return toolIndex; } set { if (value >= 0 && Set(ref toolIndex, value)) { ToolChanged?.Invoke(); Raise(nameof(IsPainting)); } } }
        public event Action ToolChanged;
        public bool IsPainting { get { return toolIndex == 2; } }

        public static readonly string[] PaintModeNames = { "Light flag on", "Light flag off", "Floor texture", "Wall texture", "Tile type", "Floor height", "Tile template" };
        public string[] PaintModes { get { return PaintModeNames; } }
        public int PaintMode
        {
            get { return paintMode; }
            set
            {
                if (value < 0 || !Set(ref paintMode, value)) return;
                RefreshSlots();
                foreach (var n in new[] { nameof(PaintsTexture), nameof(PaintsType), nameof(PaintsHeight), nameof(PaintsTemplate) }) Raise(n);
            }
        }
        public bool PaintsTexture { get { return paintMode == 2 || paintMode == 3; } }
        public bool PaintsType { get { return paintMode == 4; } }
        public bool PaintsHeight { get { return paintMode == 5; } }
        public bool PaintsTemplate { get { return paintMode == 6; } }

        // ---- the tile template: a tile's type, height and textures, painted together
        int tplType = 1, tplHeight = 4, tplFloor, tplWall;
        bool useType = true, useHeight = true, useFloor = true, useWall = true, paintFill;
        public bool TemplateType { get { return useType; } set { Set(ref useType, value); } }
        public bool TemplateHeight { get { return useHeight; } set { Set(ref useHeight, value); } }
        public bool TemplateFloor { get { return useFloor; } set { Set(ref useFloor, value); } }
        public bool TemplateWall { get { return useWall; } set { Set(ref useWall, value); } }
        /// <summary>A click fills every tile the player can walk to from it (the zones' movement rules) instead of painting one.</summary>
        public bool PaintFill { get { return paintFill; } set { Set(ref paintFill, value); } }
        public string TemplateText { get { return string.Format("{0}, height {1}, floor {2}, wall {3}", TileInfo.TypeName((TileType)tplType), tplHeight, tplFloor, tplWall); } }

        /// <summary>Takes the template from a tile of the level shown.</summary>
        public bool TakeTemplate(int x, int y)
        {
            if (level == null || x < 0 || y < 0) return false;
            var t = level.Tiles[x, y];
            tplType = (int)t.Type; tplHeight = t.Height; tplFloor = t.FloorIndex; tplWall = t.WallIndex;
            Raise(nameof(TemplateText));
            EditStatus = string.Format("Template from ({0}, {1}): {2}.", x, y, TemplateText);
            return true;
        }
        public ObservableCollection<SlotItem> Slots { get; } = new ObservableCollection<SlotItem>();
        public int PaintSlot { get { return paintSlot; } set { if (value >= 0) Set(ref paintSlot, value); } }
        public List<string> TileTypes { get; } = Enumerable.Range(0, 10).Select(i => i + ": " + TileInfo.TypeName((TileType)i)).ToList();
        public int PaintType { get { return paintType; } set { if (value >= 0) Set(ref paintType, value); } }
        public decimal? PaintHeight { get { return paintHeight; } set { Set(ref paintHeight, value); } }

        /// <summary>The level's floor or wall slots, with their textures, for the brush.</summary>
        public void RefreshSlots()
        {
            int keep = paintSlot;
            Slots.Clear();
            if (level == null || session == null || !PaintsTexture) return;
            var g = session.Graphics;
            bool floor = paintMode == 2;
            int n = floor ? level.FloorMap.Length : level.WallMap.Length;
            for (int i = 0; i < n; i++)
            {
                int tex = floor ? level.FloorTextureNumber(i) : level.WallTextureNumber(i);
                var t = floor ? g.FloorTexture(tex) : g.WallTexture(tex);
                Slots.Add(new SlotItem { Index = i, Label = string.Format("{0}: texture {1}", i, tex), Picture = t == null ? null : ToBitmap(g.TextureRgba(t, floor ? g.FloorEdge : g.WallEdge, 32)) });
            }
            paintSlot = Math.Min(keep, Math.Max(0, Slots.Count - 1));
            Raise(nameof(PaintSlot));
        }

        /// <summary>Paints one tile with the brush (inside a stroke the window opened).</summary>
        public void Paint(int x, int y)
        {
            var e = Editor;
            if (e == null || x < 0 || y < 0 || x > 63 || y > 63) return;
            try
            {
                switch (paintMode)
                {
                    case 0: e.SetLightBit(x, y, true); break;
                    case 1: e.SetLightBit(x, y, false); break;
                    case 2: e.SetTileField(x, y, "Floor texture", paintSlot); break;
                    case 3: e.SetTileField(x, y, "Wall texture", paintSlot); break;
                    case 4: e.SetTileField(x, y, "Type", paintType); break;
                    case 5: e.SetTileField(x, y, "Floor height", Math.Clamp((int)(paintHeight ?? 0), 0, 15)); break;
                    case 6:
                        if (useType) e.SetTileField(x, y, "Type", tplType);
                        if (useHeight) e.SetTileField(x, y, "Floor height", tplHeight);
                        if (useFloor) e.SetTileField(x, y, "Floor texture", tplFloor);
                        if (useWall) e.SetTileField(x, y, "Wall texture", tplWall);
                        break;
                }
                EditStatus = string.Format("Painted {0} on ({1}, {2}).", PaintModeNames[paintMode].ToLowerInvariant(), x, y);
            }
            catch (Exception ex) { EditStatus = ex.Message; }
        }
    }
}
