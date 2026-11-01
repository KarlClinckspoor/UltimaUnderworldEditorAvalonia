using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UwEditor.Core;
using UwEditor.Core.Editing;
using UwEditor.Core.Player;

namespace UwEditor.App
{
    /// <summary>The main window's editing: tools, painting, adding and moving objects, the start and the player, targets, saving.</summary>
    public partial class MainWindow
    {
        /// <summary>Ask about unsaved levels before closing or opening another game (tests turn it off).</summary>
        public bool AskBeforeDiscarding = true;
        /// <summary>Set while the main window closes tool windows itself, after asking about everything.</summary>
        bool quietClose;
        bool closeConfirmed;
        PlayerSheet playerSheet;

        /// <summary>A trap or trigger waiting for its target square to be clicked ("click and choose").</summary>
        sealed class TargetPick { public int Level, Index; public string Name; }
        TargetPick pick;

        /// <summary>"start" or "player" while the Move tool carries that arrow.</summary>
        string carrying;

        void InitEditing()
        {
            vm.LevelEdited += OnLevelEdited;
            vm.ToolChanged += () =>
            {
                View.Tool = vm.ToolIndex == 1 ? ViewTool.Move : vm.ToolIndex == 2 ? ViewTool.Paint : ViewTool.Select;
                carrying = null;
                if (vm.ToolIndex == 1) vm.EditStatus = "Move: select an object (or click an arrow), then click the square to put it on.";
                else if (vm.ToolIndex == 2) vm.EditStatus = "Paint: drag over tiles. Ctrl+wheel raises or lowers a floor.";
                else vm.EditStatus = "";
            };
            View.Painted += OnPainted;
            View.PaintEnded += EndPaintStroke;
            vm.OtherUnsaved = () => playerSheet != null && playerSheet.Dirty;
            View.HeightWheel += OnHeightWheel;
            View.DoublePicked += OnDoublePicked;
            View.FirstPersonChanged += () => { WalkButton.IsChecked = View.FirstPerson; UpdateViewText(); UpdateMarkers(); };
            View.CameraChanged += () => { if (View.FirstPerson) UpdateMarkers(); };
            View.GroundAt = GroundAt;
            PropertyRowView.Committed += OnRowCommitted;
            Closed += (s, e) => PropertyRowView.Committed -= OnRowCommitted;
            ObjectTree.DoubleTapped += (s, e) => Frame();
            Closing += OnClosingAskSave;
        }

        void OnRowCommitted(PropertyRow row, string text)
        {
            if (!vm.ObjectRows.Contains(row) && !vm.TileRows.Contains(row)) return;
            vm.EditField(row, text);
        }

        async void OnClosingAskSave(object sender, WindowClosingEventArgs e)
        {
            if (closeConfirmed || !AskBeforeDiscarding || !HasUnsaved()) { quietClose = true; return; }
            e.Cancel = true;
            if (await ConfirmDiscard("closing")) { closeConfirmed = true; Close(); }
        }

        /// <summary>Every kind of unsaved work the editor holds: levels and maps, the player, strings and conversations, and what the tool windows keep.</summary>
        public List<string> UnsavedParts()
        {
            var l = new List<string>();
            if (vm.HasLevelChanges) l.Add(vm.ChangedLevels);
            if (playerSheet != null && playerSheet.Dirty) l.Add("the player (PLAYER.DAT)");
            bool talk = (conversations != null && conversations.HasChanges) || (conversationWindow != null && conversationWindow.Model.ScriptChanged);
            if (talk) l.Add("conversations or strings");
            else if (strings != null && strings.HasChanges) l.Add("strings (STRINGS.PAK)");
            if (globalsWindow?.Globals != null && globalsWindow.Globals.Dirty) l.Add("global variables");
            if (graphicsWindow != null && graphicsWindow.Model.HasChanges) l.Add("pictures");
            if (zoning != null && zoning.Dirty) l.Add("zones");
            return l;
        }

        bool HasUnsaved() { return UnsavedParts().Count > 0; }

        /// <summary>Asks to save, discard or cancel; true when it is all right to go on.</summary>
        async Task<bool> ConfirmDiscard(string doing)
        {
            if (!AskBeforeDiscarding) return true;
            var parts = UnsavedParts();
            if (parts.Count == 0) return true;
            string what = "Unsaved: " + string.Join("; ", parts);
            string a = await MessageWindow.Ask(this, "Unsaved changes", what + ". Save them before " + doing + "?", "Save", "Discard", "Cancel");
            if (a == "Cancel") return false;
            if (a == "Save") return SaveAll(true) && UnsavedParts().Count == 0;
            return true;
        }

        // ---- the level after an edit

        void OnLevelEdited()
        {
            if (vm.Level == null) return;
            scene = LevelScene.Build(vm.Level, atlas, vm.Options);
            View.UpdateScene(scene);
            UpdateMarkers();
            UpdateGrid();
        }

        // ---- first person

        /// <summary>The floor under a point (tiles), or null for rock and the closed half of a diagonal tile.</summary>
        float? GroundAt(float x, float z)
        {
            var level = vm.Level;
            if (level == null) return null;
            int tx = (int)Math.Floor(x), ty = (int)Math.Floor(z);
            if (tx < 0 || ty < 0 || tx > 63 || ty > 63) return null;
            var t = level.Tiles[tx, ty];
            double fx = x - tx, fy = z - ty;
            switch (t.Type)
            {
                case TileType.Solid: return null;
                case TileType.OpenSE: if (!(fx > fy)) return null; break;
                case TileType.OpenSW: if (!(fx + fy < 1)) return null; break;
                case TileType.OpenNE: if (!(fx + fy > 1)) return null; break;
                case TileType.OpenNW: if (!(fy > fx)) return null; break;
            }
            return (float)level.FloorAt(tx, ty, fx, fy);
        }

        void OnFirstPerson(object sender, RoutedEventArgs e) { ToggleFirstPerson(); }

        public void ToggleFirstPerson()
        {
            if (vm.Level == null) return;
            if (View.FirstPerson) { View.SetFirstPerson(false); return; }
            Vector3? at = null;
            if (vm.SelectedTileX >= 0 && GroundAt(vm.SelectedTileX + 0.5f, vm.SelectedTileY + 0.5f) != null) at = new Vector3(vm.SelectedTileX + 0.5f, 0, vm.SelectedTileY + 0.5f);
            View.SetFirstPerson(true, at);
            if (!View.FirstPerson) vm.EditStatus = "Nowhere to stand there: select an open tile first.";
            else { vm.EditStatus = "First person: W A S D walk, Q E or drag turn, R V look up and down, wheel steps, Esc leaves."; View.Focus(); }
        }

        // ---- painting and heights

        void OnPainted(PickResult hit, bool start)
        {
            if (hit.Tile >= 0 && vm.Level != null && ZonePaint(hit.Tile % 64, hit.Tile / 64, start)) return;
            if (!hit.Hit || hit.Tile < 0 || vm.Editor == null) return;
            if (start)
            {
                EndPaintStroke();
                strokeEditor = vm.Editor;
                strokeEditor.BeginBatch("Paint " + MainViewModel.PaintModeNames[vm.PaintMode].ToLowerInvariant());
            }
            if (strokeEditor != vm.Editor) return;               // the level changed during the stroke
            vm.Paint(hit.Tile % 64, hit.Tile / 64);
        }

        /// <summary>The level a paint stroke began on: its undo step closes there, whatever level is shown by then.</summary>
        LevelEditor strokeEditor;

        void EndPaintStroke()
        {
            var e = strokeEditor;
            strokeEditor = null;
            e?.EndBatch();
        }

        void OnHeightWheel(PickResult hit, int steps)
        {
            if (hit.Tile < 0) return;
            int x = hit.Tile % 64, y = hit.Tile / 64;
            if (vm.Edit(e => e.RaiseFloor(x, y, steps)))
                vm.EditStatus = string.Format("Floor of ({0}, {1}) {2} (Ctrl+wheel).", x, y, steps > 0 ? "raised" : "lowered");
        }

        void OnDoublePicked(PickResult hit)
        {
            if (!hit.Hit) return;
            vm.Select(hit.Object, hit.Tile % 64, hit.Tile / 64, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
            Frame();
        }

        /// <summary>For the view's clicks: handles picking a target, carrying an arrow, the Move tool and clicking an arrow. True when the click was used.</summary>
        bool EditClick(PickResult hit)
        {
            var level = vm.Level;
            if (level == null) return false;
            int tx = hit.Tile >= 0 ? hit.Tile % 64 : -1, ty = hit.Tile >= 0 ? hit.Tile / 64 : -1;
            if (pick != null)
            {
                if (tx < 0) { vm.EditStatus = "Click a square for the target of " + pick.Name + " (Esc cancels)."; return true; }
                var p = pick;
                pick = null;
                if (vm.SetTarget(p.Level, p.Index, level.Level, tx, ty))
                {
                    if (level.Level != p.Level) vm.LoadLevel(p.Level);
                    vm.FlushRefresh();
                    var o = vm.Level.Objects[p.Index];
                    vm.Select(o.Index, o.TileX, o.TileY, true);
                }
                return true;
            }
            string arrow = ArrowAt(View.LastClick);
            if (carrying != null)
            {
                if (tx < 0) return true;
                MoveMarker(carrying, level.Level, tx, ty, hit.Point);
                carrying = null;
                return true;
            }
            if (arrow != null)
            {
                if (View.Tool == ViewTool.Move)
                {
                    carrying = arrow;
                    vm.EditStatus = arrow == "player" ? "Click the square to put the player on (on any level: change level first)." : "Click the square on level 1 for the new-game start.";
                    return true;
                }
                if (arrow == "player") { ShowPlayer()?.ShowTab(0); return true; }
                var st = vm.Session.NewGameStart;
                vm.EditStatus = string.Format("New-game start: ({0}, {1}) on level 1, in {2}. Move tool: click the arrow, then a square.", (int)st.X, (int)st.Y,
                    vm.Session.ExePath == null ? "no executable (the shipped default)" : System.IO.Path.GetFileName(vm.Session.ExePath));
                return true;
            }
            if (View.Tool == ViewTool.Move && vm.SelectedObject >= 0 && tx >= 0 && hit.Object != vm.SelectedObject)
            {
                var o = level.Objects[vm.SelectedObject];
                int fx = Math.Clamp((int)((hit.Point.X - tx) * 8), 0, 7), fy = Math.Clamp((int)((hit.Point.Z - ty) * 8), 0, 7);
                int idx = o.Index;
                if (vm.Edit(e => e.MoveObject(idx, tx, ty, fx, fy), string.Format("Moved #{0} {1} to ({2}, {3}).", idx, o.Name, tx, ty)))
                {
                    vm.FlushRefresh();
                    vm.Select(idx, tx, ty, true);
                }
                return true;
            }
            return false;
        }

        // ---- the start and the player

        Vector3? MarkerPoint(StartPoint sp, LevelModel level)
        {
            if (sp == null || level == null || sp.Level != level.Level) return null;
            int tx = Math.Clamp((int)sp.X, 0, 63), ty = Math.Clamp((int)sp.Y, 0, 63);
            double z = double.IsNaN(sp.Z) ? level.FloorAt(tx, ty, sp.X - tx, sp.Y - ty) : sp.Z;
            return new Vector3((float)sp.X, (float)z, (float)sp.Y);
        }

        /// <summary>The solid markers: the red start arrow, the green player arrow, bullseyes on the selected trap's squares.</summary>
        List<float> BuildMarkers(List<float> lines)
        {
            var tris = new List<float>();
            var level = vm.Level;
            if (level == null || vm.Session == null) return tris;
            // walking, an arrow round the eye would fill the view
            Func<Vector3?, bool> near = p => View.FirstPerson && p != null && new Vector2(p.Value.X - View.Camera.Target.X, p.Value.Z - View.Camera.Target.Z).Length() < 0.7f;
            var start = MarkerPoint(vm.Session.NewGameStart, level);
            if (near(start)) start = null;
            if (start != null)
            {
                Markers.ArrowDown(tris, start.Value, Markers.Red);
                LevelScene.StartMarker(lines, start.Value, (float)vm.Session.NewGameStart.FacingDegrees, new Vector4(1f, 0.3f, 0.25f, 1f));
            }
            var player = MarkerPoint(vm.Session.PlayerPoint, level);
            if (near(player)) player = null;
            if (player != null)
            {
                Markers.ArrowDown(tris, player.Value, Markers.Green);
                LevelScene.StartMarker(lines, player.Value, (float)vm.Session.PlayerPoint.FacingDegrees, new Vector4(0.3f, 1f, 0.4f, 1f));
            }
            markerLabels.Clear();
            foreach (var t in vm.Targets)
            {
                if (t.X < 0 || t.X > 63 || t.Y < 0 || t.Y > 63) continue;
                var tile = level.Tiles[t.X, t.Y];
                float y = tile.IsSolid ? 0 : (float)level.FloorAt(t.X, t.Y, 0.5, 0.5);
                if (t.Level != level.Level)
                {
                    // a teleport to another level: faded, at the same square of this level, with where it leads
                    Markers.Bullseye(tris, new Vector3(t.X + 0.5f, y, t.Y + 0.5f), alpha: 0.35f);
                    LevelScene.Line(lines, new Vector3(t.X + 0.5f, y, t.Y + 0.5f), new Vector3(t.X + 0.5f, y + 1.2f, t.Y + 0.5f), new Vector4(1f, 0.2f, 0.15f, 0.4f));
                    markerLabels.Add((new Vector3(t.X + 0.5f, y + 1.3f, t.Y + 0.5f), "→ " + LevelName(t.Level) + string.Format(" ({0}, {1})", t.X, t.Y)));
                    continue;
                }
                Markers.Bullseye(tris, new Vector3(t.X + 0.5f, y, t.Y + 0.5f));
                // a post over it, drawn over everything, so it is found behind walls
                var c = new Vector4(1f, 0.2f, 0.15f, 1f);
                LevelScene.Line(lines, new Vector3(t.X + 0.5f, y, t.Y + 0.5f), new Vector3(t.X + 0.5f, y + 1.2f, t.Y + 0.5f), c);
            }
            AddZoneOverlay(tris);
            return tris;
        }

        /// <summary>Text pinned to points of the view (a teleport's other level), drawn on the label layer.</summary>
        readonly List<(Vector3 At, string Text)> markerLabels = new List<(Vector3, string)>();

        string LevelName(int level)
        {
            var slot = vm.Session?.LevelSlots().FirstOrDefault(sl => sl.Level == level);
            return slot != null && !string.IsNullOrWhiteSpace(slot.Name) ? slot.Name : "level " + level;
        }

        /// <summary>The selected teleport's destination: opens its level and looks at the square.</summary>
        public bool GoToTeleportTarget()
        {
            var t = vm.Targets.FirstOrDefault(x => x.What.StartsWith("teleports"));
            if (t == null) { vm.EditStatus = "The selected object is not a teleport trap."; return false; }
            GoToTile(t.Level, t.X, t.Y);
            vm.EditStatus = "The teleport's destination: " + LevelName(t.Level) + string.Format(", ({0}, {1}).", t.X, t.Y);
            return true;
        }

        void OnGoToTeleportTarget(object sender, RoutedEventArgs e) { GoToTeleportTarget(); }

        /// <summary>Places the marker labels over the view (called when the camera or the markers change).</summary>
        void UpdateMarkerLabels()
        {
            MarkerLabels.Children.Clear();
            foreach (var l in markerLabels)
            {
                var p = Project(l.At);
                if (p == null) continue;
                var tb = new TextBlock { Text = l.Text, Foreground = LabelText, FontSize = 12, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                var b = new Border { Child = tb, Background = LabelBack, CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2), Opacity = 0.85 };
                b.Measure(Size.Infinity);
                Canvas.SetLeft(b, p.Value.X - b.DesiredSize.Width / 2);
                Canvas.SetTop(b, p.Value.Y - b.DesiredSize.Height - 2);
                MarkerLabels.Children.Add(b);
            }
        }

        /// <summary>Screen point of a world point, or null behind the camera.</summary>
        Point? Project(Vector3 p)
        {
            double w = View.Bounds.Width, h = View.Bounds.Height;
            var vp = View.Camera.View * View.Camera.Projection((float)(w / Math.Max(1, h)));
            var c = Vector4.Transform(new Vector4(p, 1), vp);
            if (c.W <= 0.01f) return null;
            return new Point((c.X / c.W + 1) / 2 * w, (1 - c.Y / c.W) / 2 * h);
        }

        /// <summary>Which arrow ("start", "player") is under a point of the view, or null.</summary>
        string ArrowAt(Point at)
        {
            var level = vm.Level;
            if (level == null || vm.Session == null) return null;
            foreach (var kv in new[] { ("player", MarkerPoint(vm.Session.PlayerPoint, level)), ("start", MarkerPoint(vm.Session.NewGameStart, level)) })
            {
                if (kv.Item2 == null) continue;
                var a = Project(kv.Item2.Value);
                var b = Project(kv.Item2.Value + new Vector3(0, Markers.ArrowHeight, 0));
                if (a == null || b == null) continue;
                if (DistanceToSegment(at, a.Value, b.Value) < 16) return kv.Item1;
            }
            return null;
        }

        static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-6 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
            double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return Math.Sqrt(x * x + y * y);
        }

        /// <summary>The open game's player sheet, shared with the player window.</summary>
        PlayerSheet PlayerSheetOf()
        {
            var s = vm.Session;
            if (s == null || !s.IsSave) return null;
            if (playerSheet != null) return playerSheet;
            string path = UltimateEditor.Games.GameProfile.Find(s.SaveFolder, "PLAYER.DAT");
            if (path == null) return null;
            try { playerSheet = PlayerSheet.Load(path, s.Kind, s.DataFolder); }
            catch (Exception ex) { _ = MessageWindow.Show(this, "Could not read PLAYER.DAT", ex.Message); return null; }
            return playerSheet;
        }

        /// <summary>Moves the start ("start", level 1 only, patches the executable) or the player ("player", any level, kept until saved).</summary>
        public bool MoveMarker(string which, int level, int tx, int ty, Vector3? point = null)
        {
            var s = vm.Session;
            if (s == null || vm.Level == null) return false;
            if (vm.Level.Tiles[tx, ty].IsSolid) { vm.EditStatus = string.Format("({0}, {1}) is rock: nowhere to stand.", tx, ty); return false; }
            try
            {
                if (which == "start")
                {
                    if (level != 1) throw new InvalidOperationException("A new game always starts on level 1 (player_setup in the main menu); only the square can move.");
                    s.SetNewGameStart(tx, ty);
                    vm.EditStatus = string.Format("New-game start moved to ({0}, {1}) and saved in {2} (the original is kept as .ORIG).", tx, ty, System.IO.Path.GetFileName(s.ExePath));
                }
                else
                {
                    var sheet = PlayerSheetOf();
                    if (sheet == null) throw new InvalidOperationException("The player's position is in a saved game's PLAYER.DAT: open a SAVE folder.");
                    double fx = point != null && (int)Math.Floor(point.Value.X) == tx ? point.Value.X - tx : 0.5;
                    double fy = point != null && (int)Math.Floor(point.Value.Z) == ty ? point.Value.Z - ty : 0.5;
                    double floor = vm.Level.FloorAt(tx, ty, fx, fy);
                    sheet.Dat.SetPosition(level, (int)((tx + fx) * 256), (int)((ty + fy) * 256), (int)Math.Round(floor * 256), sheet.Dat.SavedFacing);
                    sheet.Dirty = true;
                    NoteEdit();
                    s.PlayerMoved(sheet.Dat);
                    vm.RaiseEditState();
                    vm.EditStatus = string.Format("Player moved to ({0}, {1}) on level {2} (save with Ctrl+S or in the player window).", tx, ty, level);
                }
                UpdateMarkers();
                vm.OnStartMoved();
                return true;
            }
            catch (Exception ex) { vm.EditStatus = ex.Message; return false; }
        }

        void OnMoveStart(object sender, RoutedEventArgs e)
        {
            if (vm.Level != null && vm.SelectedTileX >= 0) MoveMarker("start", vm.Level.Level, vm.SelectedTileX, vm.SelectedTileY);
        }

        void OnMovePlayer(object sender, RoutedEventArgs e)
        {
            if (vm.Level != null && vm.SelectedTileX >= 0) MoveMarker("player", vm.Level.Level, vm.SelectedTileX, vm.SelectedTileY);
        }

        // ---- targets

        void OnPickTarget(object sender, RoutedEventArgs e) { StartPickTarget(); }

        public void StartPickTarget()
        {
            if (vm.Level == null || vm.SelectedObject < 0 || !vm.HasTargetSquare) return;
            var o = vm.Level.Objects[vm.SelectedObject];
            pick = new TargetPick { Level = vm.Level.Level, Index = o.Index, Name = o.ToString() };
            vm.EditStatus = "Click the square " + o.Name + " should act on" + (Triggers.IsTeleport(o.Item) ? " (change level first to teleport to another level)" : "") + ". Esc cancels.";
        }

        void OnTrapVariable(object sender, RoutedEventArgs e)
        {
            if (vm.Level == null || vm.SelectedObject < 0) return;
            var o = vm.Level.Objects[vm.SelectedObject];
            int n = Triggers.VariableOf(vm.Session, o);
            if (n < 0) return;
            var g = ShowGlobals();
            if (g != null) _ = SelectGlobal(g, n, o.Quality << 5 | o.Owner);
        }

        async Task SelectGlobal(GlobalsWindow g, int n, int value)
        {
            await g.Loading;
            var kind = UwEditor.Core.Conversations.GlobalKind.GameVariable;
            int number = n;
            if (vm.Session.IsUw2)
            {
                if (n >= 0x100 && n < 0x180) { kind = UwEditor.Core.Conversations.GlobalKind.Quest; number = n - 0x100; }
                else if (n >= 0x180 && n < 0x190) { kind = UwEditor.Core.Conversations.GlobalKind.Quest; number = 128 + n - 0x180; }
                else if (n >= 0x190 && n < 0x1A0) { kind = UwEditor.Core.Conversations.GlobalKind.XClock; number = n - 0x190; }
            }
            else if (n == 0) { kind = UwEditor.Core.Conversations.GlobalKind.Quest; number = (value << 3) & 31; }
            g.SelectVariable(kind, number);
        }

        // ---- objects

        async void OnAddObject(object sender, RoutedEventArgs e) { await AddObject(); }

        /// <summary>Opens the add window for the selected tile (or the one looked at), or for the selected container.</summary>
        public async Task<int> AddObject()
        {
            var level = vm.Level;
            if (level == null) return -1;
            int tx = vm.SelectedTileX, ty = vm.SelectedTileY;
            if (tx < 0)
            {
                tx = Math.Clamp((int)View.Camera.Target.X, 0, 63); ty = Math.Clamp((int)View.Camera.Target.Z, 0, 63);
            }
            if (level.Tiles[tx, ty].IsSolid && vm.SelectedObject < 0) { vm.EditStatus = string.Format("({0}, {1}) is rock: select an open tile first.", tx, ty); return -1; }
            string inside = null;
            int container = -1;
            if (vm.SelectedObject >= 0)
            {
                var o = level.Objects[vm.SelectedObject];
                if (!o.IsQuantity || o.Item == 0x10F || (o.Item >= 0x140 && o.Item < 0x150)) { container = o.Index; inside = o.ToString(); }
                else if (o.Parent >= 0) { container = o.Parent; inside = level.Objects[o.Parent].ToString(); }
            }
            bool byDefault = container >= 0 && (ItemCategories.Of(level.Objects[container].Item, vm.Session.Kind) & (ItemCategory.Container | ItemCategory.Door)) != 0;
            var w = await AddObjectWindow.Ask(this, vm.Session, vm.IconOf, false, string.Format("On tile ({0}, {1}) of level {2}.", tx, ty, level.Level), inside, byDefault);
            if (w.Result == null) return -1;
            var spec = w.Result;
            if (Triggers.IsTrigger(spec.Item)) { spec.Quality = tx; spec.Owner = ty; }
            int added = -1;
            int into = w.Inside ? container : -1;
            if (into >= 0 && spec.IsMobile) { vm.EditStatus = "A creature cannot go inside an object."; return -1; }
            vm.Edit(ed => added = ed.AddObject(spec, tx, ty, into), "Added " + spec.Describe(vm.Session.Names) + (into >= 0 ? " inside #" + into : string.Format(" on ({0}, {1})", tx, ty)) + ".");
            if (added >= 0)
            {
                vm.FlushRefresh();
                vm.Select(added, tx, ty, true);
                if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
            }
            return added;
        }

        void OnDeleteObject(object sender, RoutedEventArgs e) { DeleteSelected(); }

        public void DeleteSelected()
        {
            if (vm.Level == null || vm.SelectedObject < 0) return;
            var o = vm.Level.Objects[vm.SelectedObject];
            int idx = o.Index, tx = o.TileX, ty = o.TileY;
            if (vm.Edit(e => e.DeleteObject(idx), "Deleted #" + idx + " " + o.Name + (o.Children.Count > 0 ? " and what it held" : "") + "."))
            {
                vm.FlushRefresh();
                vm.Select(-1, tx, ty, true);
            }
        }

        void OnUndo(object sender, RoutedEventArgs e) { Undo(); }
        void OnRedo(object sender, RoutedEventArgs e) { Redo(); }

        public void Undo()
        {
            var ed = vm.Level == null ? null : vm.Edits?.Existing(vm.Level.Level);
            if (ed == null || !ed.CanUndo) return;
            string l = ed.UndoLabel;
            ed.Undo();
            vm.FlushRefresh();
            vm.EditStatus = "Undone: " + l + ".";
        }

        public void Redo()
        {
            var ed = vm.Level == null ? null : vm.Edits?.Existing(vm.Level.Level);
            if (ed == null || !ed.CanRedo) return;
            string l = ed.RedoLabel;
            ed.Redo();
            vm.FlushRefresh();
            vm.EditStatus = "Redone: " + l + ".";
        }

        void OnSaveLevels(object sender, RoutedEventArgs e) { SaveAll(); }

        /// <summary>Writes the changed levels (and their maps) into LEV.ARK and the player into PLAYER.DAT; with everything, also the strings, conversations, global variables, pictures and zones.</summary>
        public bool SaveAll() { return SaveAll(false); }

        public bool SaveAll(bool everything)
        {
            if (vm.Edits == null) return false;
            var said = new List<string>();
            try
            {
                if (vm.Edits.HasChanges)
                {
                    var levels = vm.Edits.Save();
                    said.Add("levels " + string.Join(", ", levels) + " written to " + System.IO.Path.GetFileName(vm.Session.LevArkPath));
                }
                if (playerSheet != null && playerSheet.Dirty)
                {
                    playerSheet.Save();
                    said.Add("the player saved in PLAYER.DAT");
                }
                if (everything)
                {
                    if (conversationWindow != null && (conversationWindow.Model.ScriptChanged || (conversations != null && conversations.HasChanges)))
                    {
                        if (conversationWindow.Save() == null) throw new InvalidOperationException("the conversation could not be saved: " + conversationWindow.Model.Status);
                        said.Add("conversations and strings");
                    }
                    else if (conversations != null && conversations.HasChanges) { conversations.Save(); said.Add("conversations and strings"); }
                    else if (strings != null && strings.HasChanges) { strings.Save(); said.Add("STRINGS.PAK"); }
                    if (said.Contains("conversations and strings")) { vm.Session.ReloadStrings(); vm.RefreshNames(); }
                    if (globalsWindow?.Globals != null && globalsWindow.Globals.Dirty)
                    {
                        if (globalsWindow.Save() == null) throw new InvalidOperationException("the global variables could not be saved: " + globalsWindow.Model.Status);
                        said.Add("global variables");
                    }
                    if (graphicsWindow != null && graphicsWindow.Model.HasChanges) { graphicsWindow.Model.SaveAll(); said.Add("pictures"); }
                    if (zoning != null && zoning.Dirty) { zoning.Save(); said.Add("zones"); }
                }
            }
            catch (Exception ex)
            {
                _ = MessageWindow.Show(this, "Could not save", ex.Message);
                vm.RaiseEditState();
                return false;
            }
            vm.RaiseEditState();
            vm.EditStatus = said.Count == 0 ? "Nothing to save." : "Saved: " + string.Join("; ", said) + " (the first original is kept as .ORIG).";
            return true;
        }
    }
}
