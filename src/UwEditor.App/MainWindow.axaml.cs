using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using UwEditor.Core;

namespace UwEditor.App
{
    public partial class MainWindow : Window
    {
        readonly MainViewModel vm = new MainViewModel();
        readonly Settings settings = Settings.Load();
        SpriteAtlas atlas;
        LevelScene scene;

        static readonly IBrush LabelText = new SolidColorBrush(Color.Parse("#FFD72A")), LabelBack = new SolidColorBrush(Color.Parse("#D015161A"));
        static readonly Color Uw1Accent = Color.Parse("#C9934C"), Uw2Accent = Color.Parse("#6FA6DC");
        static readonly Vector4 StartColour = new Vector4(1f, 0.25f, 0.85f, 1f), SelColour = new Vector4(1f, 0.85f, 0.25f, 1f), HoverColour = new Vector4(1f, 1f, 1f, 0.45f);

        /// <summary>The window's state, for tests.</summary>
        public MainViewModel ViewModel { get { return vm; } }

        public MainWindow()
        {
            InitializeComponent();
            if (Program.Width > 0 && Program.Height > 0) { Width = Program.Width; Height = Program.Height; }
            DataContext = vm;
            vm.LevelChanged += OnLevelChanged;
            vm.SelectionChanged += UpdateMarkers;
            vm.OptionsChanged += OnOptionsChanged;
            View.Picked += OnPicked;
            View.Hovered += OnHovered;
            View.CameraChanged += UpdateViewText;
            View.FrameDrawn += UpdateViewText;
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            InitEditing();
            InitSelection();
            InitCheckpoints();
            Columns.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(settings.LeftWidth, 200, 600));
            Columns.ColumnDefinitions[4].Width = new GridLength(Math.Clamp(settings.RightWidth, 240, 700));
            vm.ShowGrid = settings.Grid;
            vm.GridStep = settings.GridStep;
            vm.ShowModels = settings.Models;
            ColourScheme.Set(settings.Theme);
            DarkThemeItem.IsChecked = settings.Theme != ColourScheme.Light;
            LightThemeItem.IsChecked = settings.Theme == ColourScheme.Light;
            View.CameraChanged += UpdateGridLabels;
            View.CameraChanged += UpdateMarkerLabels;
            if (Program.StateFile != null)
            {
                vm.SelectionChanged += WriteState;
                vm.LevelChanged += l => WriteState();
                vm.OptionsChanged += b => WriteState();
                View.CameraChanged += WriteState;
            }
            View.SizeChanged += (s, e) => UpdateGridLabels();
            BuildRecentMenu();
            Opened += (s, e) =>
            {
                if (Program.OpenAtStart != null) OpenPathNow(Program.OpenAtStart);
                if (Program.LevelAtStart > 0 && vm.Session != null && Program.LevelAtStart <= vm.Session.Profile.LevelSlots) vm.LoadLevel(Program.LevelAtStart);
                if (Program.SelectAtStart >= 0 && vm.Level != null && Program.SelectAtStart < vm.Level.Objects.Length)
                {
                    var o = vm.Level.Objects[Program.SelectAtStart];
                    vm.Select(o.Index, o.TileX, o.TileY, true);
                    Frame();
                }
                if (Program.ConversationAtStart != int.MinValue && vm.Session != null)
                {
                    var cw = ShowConversations(Program.ConversationAtStart);
                    if (cw != null) cw.Model.Tab = Program.TabAtStart;
                }
                if (Program.GlobalsAtStart && vm.Session != null) ShowGlobals();
                if (Program.PlayerAtStart && vm.Session != null) { var pw = ShowPlayer(); if (pw != null) pw.Tabs.SelectedIndex = Program.TabAtStart; }
                if (Program.GraphicsAtStart && vm.Session != null) ShowGraphics()?.ShowTab(Program.TabAtStart);
                if (Program.WindowAtStart != null && vm.Session != null) OpenWindowAtStart(Program.WindowAtStart);
                if (Program.LightAtStart >= 0 && vm.Session != null) vm.LightIndex = Math.Min(7, Program.LightAtStart) + 1;
                if (Program.WalkAtStart && vm.Session != null) { vm.Select(-1, (int)vm.Session.Start.X, (int)vm.Session.Start.Y, false); ToggleFirstPerson(); }
                if (Program.ReadyFile != null) WriteReadyFileAfterFrames(3);
                View.Focus();
            };
            Closing += (s, e) =>
            {
                settings.LeftWidth = Columns.ColumnDefinitions[0].ActualWidth;
                settings.RightWidth = Columns.ColumnDefinitions[4].ActualWidth;
                settings.Grid = vm.ShowGrid;
                settings.GridStep = (int)(vm.GridStep ?? 5);
                settings.Models = vm.ShowModels;
                settings.Save();
            };
        }

        /// <summary>For scripted tests: what is shown, as JSON (--state-file).</summary>
        void WriteState()
        {
            try
            {
                var c = View.Camera;
                var p = View.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
                var state = new
                {
                    level = vm.Level?.Level ?? 0,
                    selectedObject = vm.SelectedObject,
                    tileX = vm.SelectedTileX, tileY = vm.SelectedTileY,
                    target = new[] { c.Target.X, c.Target.Y, c.Target.Z },
                    yaw = c.Yaw, pitch = c.Pitch, distance = c.Distance,
                    grid = vm.ShowGrid, ceiling = vm.ShowCeiling,
                    view = new[] { p.X, p.Y, View.Bounds.Width, View.Bounds.Height },
                    glError = View.GlError,
                };
                System.IO.File.WriteAllText(Program.StateFile, System.Text.Json.JsonSerializer.Serialize(state));
            }
            catch (Exception) { }
        }

        /// <summary>For scripted screenshots: writes the ready file once a few frames have been drawn.</summary>
        void WriteReadyFileAfterFrames(int frames)
        {
            int left = frames;
            Action onFrame = null;
            onFrame = () =>
            {
                if (--left > 0) { View.Redraw(); return; }
                View.FrameDrawn -= onFrame;
                if (Program.StateFile != null) WriteState();
                try { System.IO.File.WriteAllText(Program.ReadyFile, "ready"); } catch (Exception) { }
            };
            View.FrameDrawn += onFrame;
            View.Redraw();
        }

        // ---- opening a game

        async void OnOpen(object sender, RoutedEventArgs e)
        {
            string path = null;
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Choose the game folder, its DATA folder or a SAVE folder",
                    AllowMultiple = false,
                });
                path = folders.FirstOrDefault()?.TryGetLocalPath();
            }
            catch (Exception ex)
            {
                // the system's picker can hand back a folder that does not exist
                _ = MessageWindow.Show(this, "Could not open the folder", ex.Message);
                return;
            }
            if (path != null) OpenPath(path);
        }

        public async void OpenPath(string path)
        {
            if (!await ConfirmDiscard("opening another game")) return;
            OpenPathNow(path);
        }

        /// <summary>Opens a game without asking about unsaved changes.</summary>
        public void OpenPathNow(string path)
        {
            GameSession s;
            try { s = GameSession.Open(path); }
            catch (Exception ex)
            {
                _ = MessageWindow.Show(this, "Could not open the game", ex.Message);
                return;
            }
            atlas = SpriteAtlas.Build(s.Graphics.ObjectPictures);
            ApplyAccent(s.IsUw2);
            Title = "Underworld Editor — " + s.Title;
            // the windows of the old game go: they hold its session, levels and results (asked about above)
            quietClose = true;
            try
            {
                conversationWindow?.Close();
                globalsWindow?.Close();
                playerWindow?.Close();
                graphicsWindow?.Close();
                keysWindow?.Close(); stringsWindow?.Close(); triggersWindow?.Close(); modsWindow?.Close(); modelsWindow?.Close();
                zonesWindow?.Close(); zoneGraphWindow?.Close();
                itemShuffleWindow?.Close(); itemSwapWindow?.Close(); monsterSwapWindow?.Close();
                findWindow?.Close(); automapWindow?.Close(); checkpointsWindow?.Close();
                historyWindow?.Close(); worldCheckWindow?.Close();
            }
            finally { quietClose = false; }
            zoning = null; zoneOverlay = null;
            conversations = null;
            strings = null;
            playerSheet = null;
            pick = null; carrying = null;
            if (View.FirstPerson) View.SetFirstPerson(false);
            settings.AddRecent(s.SaveFolder ?? s.GameFolder);
            settings.Save();
            BuildRecentMenu();
            vm.Open(s);
            CheckpointsFor(s);
        }

        void BuildRecentMenu()
        {
            var items = new List<object>();
            foreach (var r in settings.Recent)
            {
                var mi = new MenuItem { Header = r };
                string path = r;
                mi.Click += (s, e) => OpenPath(path);
                items.Add(mi);
            }
            if (items.Count == 0) items.Add(new MenuItem { Header = "(none yet)", IsEnabled = false });
            RecentMenu.ItemsSource = items;
        }

        void OnExit(object sender, RoutedEventArgs e) { Close(); }

        // ---- the level

        void OnLevelChanged(LevelModel level)
        {
            EndPaintStroke();
            scene = level == null ? null : LevelScene.Build(level, atlas, vm.Options);
            if (View.FirstPerson) View.SetFirstPerson(false);
            View.SetScene(scene);
            View.State.SelectedObject = View.State.SelectedTile = View.State.HoverObject = View.State.HoverTile = -1;
            vm.HoverText = "";
            // going up or down keeps the place and the angle; a new game, or the first level shown, starts at the start square
            if (level != null && cameraSession == vm.Session && cameraSet) KeepCamera(level);
            else PlaceCamera(level);
            cameraSession = vm.Session; cameraSet = level != null;
            ApplyLight();
            UpdateMarkers();
            UpdateGrid();
        }

        object cameraSession;
        bool cameraSet;

        /// <summary>Another level of the same game: the same x and y, distance and angles; the height of the new level's floor there.</summary>
        void KeepCamera(LevelModel level)
        {
            var t = View.Camera.Target;
            int tx = Math.Clamp((int)Math.Floor(t.X), 0, 63), ty = Math.Clamp((int)Math.Floor(t.Z), 0, 63);
            float y = level.Tiles[tx, ty].IsSolid ? t.Y : (float)level.FloorAt(tx, ty, t.X - tx, t.Z - ty);
            View.LookAt(new Vector3(t.X, y, t.Z), View.Camera.Distance, View.Camera.Yaw, View.Camera.Pitch);
        }

        /// <summary>Over the start square (a new game) or the player (a saved game) on that level,
        /// else over the middle of the level's open tiles.</summary>
        void PlaceCamera(LevelModel level)
        {
            if (level == null) return;
            var st = vm.Session.Start;
            Vector3 target;
            float yaw = 0;
            if (level.Level == st.Level)
            {
                target = StartPoint(level);
                yaw = (float)st.FacingDegrees;
            }
            else
            {
                int tx, ty;
                level.Centre(out tx, out ty);
                target = new Vector3(tx + 0.5f, (float)level.FloorAt(tx, ty, 0.5, 0.5), ty + 0.5f);
                View.LookAt(target, 12f, 0f, 60f);
                return;
            }
            View.LookAt(target + new Vector3(0, 0.3f, 0), 7f, yaw, 52f);
        }

        Vector3 StartPoint(LevelModel level)
        {
            var st = vm.Session.Start;
            int tx = Math.Clamp((int)st.X, 0, 63), ty = Math.Clamp((int)st.Y, 0, 63);
            double z = double.IsNaN(st.Z) ? level.FloorAt(tx, ty, st.X - tx, st.Y - ty) : st.Z;
            return new Vector3((float)st.X, (float)z, (float)st.Y);
        }

        void ApplyLight()
        {
            View.State.Light = vm.Light;
            View.State.LightLevel = vm.Light == null ? -1 : vm.EffectiveLight;
            View.Redraw();
        }

        void OnOptionsChanged(bool rebuildMesh)
        {
            ApplyLight();
            if (vm.Level == null || scene == null) return;
            if (rebuildMesh)
            {
                scene = LevelScene.Build(vm.Level, atlas, vm.Options);
                View.SetScene(scene);
            }
            else
            {
                scene.RebuildObjects(vm.Options);
                View.RefreshObjects();
            }
            UpdateGrid();
            View.Redraw();
        }

        // ---- the grid

        void UpdateGrid()
        {
            View.SetGrid(vm.Level != null && vm.Options.Grid ? GridOverlay.Lines(vm.Level, vm.Options.GridStep) : null);
            UpdateGridLabels();
        }

        /// <summary>The numbers at the view's edges, kept clear of the corners and of each other's place.</summary>
        void UpdateGridLabels()
        {
            GridLabels.Children.Clear();
            if (vm.Level == null || !vm.Options.Grid) return;
            double w = View.Bounds.Width, h = View.Bounds.Height;
            // the floor under the point looked at, so the numbers sit on the lines drawn there
            var t = View.Camera.Target;
            int tx = Math.Clamp((int)Math.Floor(t.X), 0, 63), ty = Math.Clamp((int)Math.Floor(t.Z), 0, 63);
            float planeY = vm.Level.Tiles[tx, ty].IsSolid ? t.Y : (float)vm.Level.FloorAt(tx, ty, t.X - tx, t.Z - ty);
            var placed = new List<Rect>();
            foreach (var l in GridOverlay.Labels(View.Camera, (float)w, (float)h, vm.Options.GridStep, planeY))
            {
                var tb = new TextBlock { Text = l.Text, Foreground = LabelText, FontSize = 11, FontWeight = FontWeight.SemiBold };
                var b = new Border { Child = tb, Background = LabelBack, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1) };
                b.Measure(Size.Infinity);
                var sz = b.DesiredSize;
                double x = l.X - sz.Width / 2, y = l.Y - sz.Height / 2;
                // labels sit just inside the edge the line leaves through
                x = Math.Clamp(x, 2, Math.Max(2, w - sz.Width - 2));
                y = Math.Clamp(y, 2, Math.Max(2, h - sz.Height - 2));
                // nearer lines were placed first; a label that would cover one is left out
                var r = new Rect(x - 3, y - 2, sz.Width + 6, sz.Height + 4);
                if (placed.Any(o => o.Intersects(r))) continue;
                placed.Add(r);
                Canvas.SetLeft(b, x);
                Canvas.SetTop(b, y);
                GridLabels.Children.Add(b);
            }
        }

        // ---- selection and markers

        void OnPicked(PickResult hit)
        {
            if (ObjectPickClick(hit)) return;
            if (EditClick(hit)) return;
            if (MultiClick(hit)) return;
            ClearMulti();
            if (!hit.Hit) { vm.ClearSelection(); return; }
            vm.Select(hit.Object, hit.Tile % 64, hit.Tile / 64, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
        }

        void UpdateMarkers()
        {
            var lines = new List<float>();
            var level = vm.Level;
            List<float> tris = null;
            if (level != null && scene != null)
            {
                tris = BuildMarkers(lines);
                if (vm.SelectedTileX >= 0) scene.TileOutline(lines, vm.SelectedTileX, vm.SelectedTileY, SelColour);
                AddSelectionOutlines(lines);
            }
            View.SetMarkers(tris);
            View.State.SelectedObject = vm.SelectedObject;
            View.State.SelectedTile = vm.SelectedObject >= 0 ? -1 : (vm.SelectedTileX >= 0 ? vm.SelectedTileX + 64 * vm.SelectedTileY : -1);
            View.SetLines(lines);
            UpdateMarkerLabels();
            UpdateViewText();
        }

        void OnHovered(PickResult hit)
        {
            var level = vm.Level;
            if (level == null || !hit.Hit) { vm.HoverText = ""; return; }
            int tx = hit.Tile % 64, ty = hit.Tile / 64;
            string s = "";
            if (hit.Tile >= 0)
            {
                var t = level.Tiles[tx, ty];
                s = string.Format("Tile ({0}, {1}) · {2} · height {3} · wall {4} · floor {5}", tx, ty, TileInfo.TypeName(t.Type), t.Height,
                    level.WallTextureNumber(t.WallIndex), level.FloorTextureNumber(t.FloorIndex));
            }
            if (hit.Object >= 0) s = level.Objects[hit.Object] + "  —  " + s;
            vm.HoverText = s;
        }

        void UpdateViewText()
        {
            var c = View.Camera;
            string gl = View.GlError != null ? "GL error: " + View.GlError : "";
            vm.ViewText = string.Format("{0}looking at ({1:0.0}, {2:0.0}) · {3:0.0} ms", gl.Length > 0 ? gl + " · " : "", c.Target.X, c.Target.Z, View.LastFrameMs);
        }

        // ---- commands

        void OnPreviousLevel(object sender, RoutedEventArgs e) { vm.StepLevel(-1); }
        void OnNextLevel(object sender, RoutedEventArgs e) { vm.StepLevel(1); }
        void OnFrame(object sender, RoutedEventArgs e) { Frame(); }
        void OnTopDown(object sender, RoutedEventArgs e) { View.Camera.Pitch = 89; View.CameraMoved(); }
        void OnGoToStart(object sender, RoutedEventArgs e) { GoToStart(); }

        void Frame()
        {
            var level = vm.Level;
            if (level == null) return;
            if (View.FirstPerson) View.SetFirstPerson(false);
            Vector3 target;
            float distance;
            if (vm.SelectedObject >= 0 && level.Objects[vm.SelectedObject].TileX >= 0)
            {
                var o = level.Objects[vm.SelectedObject];
                target = new Vector3((float)o.WorldX, (float)o.WorldZ + 0.2f, (float)o.WorldY);
                distance = 2.5f;
                // a trap or trigger: the object and the square it acts on, both in view
                var aim = vm.Targets.FirstOrDefault(t => t.Level == level.Level && (t.X != o.TileX || t.Y != o.TileY));
                if (aim != null)
                {
                    var far = new Vector3(aim.X + 0.5f, (float)level.FloorAt(Math.Clamp(aim.X, 0, 63), Math.Clamp(aim.Y, 0, 63), 0.5, 0.5), aim.Y + 0.5f);
                    float d = Vector3.Distance(target, far);
                    if (d < 24)
                    {
                        target = (target + far) / 2;
                        distance = Math.Max(4f, d * 1.1f + 2f);
                        View.LookAt(target, distance, null, Math.Max(View.Camera.Pitch, 50f));
                        return;
                    }
                }
                // wall objects and doors are seen head-on: looking the way they face (towards the wall)
                int model;
                var kind = ObjectModels.Classify(o.Item, vm.Session.RenderTypes, vm.Session.IsUw2, out model);
                if (kind == ObjectModels.Kind.Door || kind == ObjectModels.Kind.TmapLever || kind == ObjectModels.Kind.TmapWall
                    || (kind == ObjectModels.Kind.Model && (model == 0x0D || model == 0x10 || model == 0x11 || model == 0x12)))
                {
                    View.Camera.Yaw = o.Heading * 45f;
                    View.Camera.Pitch = Math.Min(View.Camera.Pitch, 30f);
                }
            }
            else if (vm.SelectedTileX >= 0)
            {
                target = new Vector3(vm.SelectedTileX + 0.5f, (float)level.FloorAt(vm.SelectedTileX, vm.SelectedTileY, 0.5, 0.5) + 0.2f, vm.SelectedTileY + 0.5f);
                distance = 3.5f;
            }
            else return;
            View.LookAt(target, distance, ClearYaw(target, distance));
        }

        /// <summary>The camera's direction, or the nearest eighth of a turn from it, from which no
        /// wall stands between the camera and the target (a wall lever is seen from its room).</summary>
        float ClearYaw(Vector3 target, float distance)
        {
            var cam = View.Camera;
            float start = cam.Yaw;
            for (int k = 0; k < 8; k++)
            {
                float yaw = start + 45f * ((k + 1) / 2) * (k % 2 == 0 ? 1 : -1);
                var probe = new OrbitCamera { Target = target, Yaw = yaw, Pitch = cam.Pitch, Distance = distance };
                if (!Picking.Blocked(scene?.Mesh, target, probe.Eye)) return yaw;
            }
            return start;
        }

        void GoToStart()
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != vm.Session.Start.Level) vm.LoadLevel(vm.Session.Start.Level);
            else PlaceCamera(vm.Level);
        }

        FindWindow findWindow;
        ConversationWindow conversationWindow;
        GlobalsWindow globalsWindow;
        UwEditor.Core.Conversations.ConversationLibrary conversations;

        /// <summary>The open game's conversations, read once and kept with their unsaved edits.</summary>
        public UwEditor.Core.Conversations.ConversationLibrary Conversations()
        {
            if (vm.Session == null) return null;
            if (conversations == null || conversations.Session != vm.Session)
            {
                try { conversations = UwEditor.Core.Conversations.ConversationLibrary.Open(vm.Session); }
                catch (Exception ex) { _ = MessageWindow.Show(this, "No conversations", ex.Message); return null; }
            }
            return conversations;
        }

        PlayerWindow playerWindow;
        GraphicsWindow graphicsWindow;

        void OnPlayer(object sender, RoutedEventArgs e) { ShowPlayer(); }
        void OnGraphics(object sender, RoutedEventArgs e) { ShowGraphics(); }

        /// <summary>The player window for the open saved game; with DATA open, says why there is none.</summary>
        public PlayerWindow ShowPlayer()
        {
            var s = vm.Session;
            if (s == null) return null;
            if (playerWindow != null) { playerWindow.Activate(); return playerWindow; }
            string path = s.IsSave ? UltimateEditor.Games.GameProfile.Find(s.SaveFolder, "PLAYER.DAT") : null;
            if (path == null)
            {
                _ = MessageWindow.Show(this, "No saved player", s.IsSave ? "This saved game has no PLAYER.DAT."
                    : "The player lives in a saved game's PLAYER.DAT. Open a SAVE folder (File ▸ Open game folder…); DATA's PLAYER.DAT is a 301-byte template the game never reads.");
                return null;
            }
            var sheet = PlayerSheetOf();
            if (sheet == null) return null;
            playerWindow = new PlayerWindow(sheet, s, vm.IconOf);
            playerWindow.Model.Edited += () => { NoteEdit(); vm.RaiseEditState(); };
            playerWindow.Closed += (o, e) => playerWindow = null;
            playerWindow.Show(this);
            return playerWindow;
        }

        /// <summary>The graphics window, one at a time.</summary>
        public GraphicsWindow ShowGraphics()
        {
            if (vm.Session == null) return null;
            if (graphicsWindow != null) { graphicsWindow.Activate(); return graphicsWindow; }
            graphicsWindow = new GraphicsWindow(vm.Session);
            var gw = graphicsWindow;
            MessageWindow.GuardClose(gw, "Graphics", () => gw.Model.HasChanges, () => { gw.Model.SaveAll(); return true; }, () => AskBeforeDiscarding && !quietClose);
            graphicsWindow.Closed += (o, e) => graphicsWindow = null;
            graphicsWindow.Show(this);
            return graphicsWindow;
        }

        void OnConversations(object sender, RoutedEventArgs e) { ShowConversations(); }
        void OnGlobals(object sender, RoutedEventArgs e) { ShowGlobals(); }

        /// <summary>The conversation window, one at a time; on a slot and a code address if given.</summary>
        public ConversationWindow ShowConversations(int slot = -1, int address = -1)
        {
            var lib = Conversations();
            if (lib == null) return null;
            if (conversationWindow == null)
            {
                conversationWindow = new ConversationWindow(lib, GoToUser, s => { var g = ShowGlobals(); if (g != null && s >= 0) _ = ShowConversationGlobals(g, s); });
                conversationWindow.Closed += (s, e) => conversationWindow = null;
                conversationWindow.Saved += () => { vm.Session.ReloadStrings(); vm.RefreshNames(); };
                conversationWindow.Show(this);
            }
            else conversationWindow.Activate();
            if (slot >= 0) conversationWindow.SelectSlot(slot);
            else if (conversationWindow.Model.Selected == null && conversationWindow.Model.Rows.Count > 0) conversationWindow.Model.Selected = conversationWindow.Model.Rows[0];
            if (address >= 0) conversationWindow.ShowAddress(address);
            return conversationWindow;
        }

        static async Task ShowConversationGlobals(GlobalsWindow g, int slot)
        {
            await g.Loading;
            g.ShowConversation(slot);
        }

        /// <summary>The global variables window, one at a time.</summary>
        public GlobalsWindow ShowGlobals()
        {
            var lib = Conversations();
            if (lib == null) return null;
            if (globalsWindow == null)
            {
                globalsWindow = new GlobalsWindow(lib, (slot, address) => ShowConversations(slot, address), PlayerSheetOf());
                var gl = globalsWindow;
                MessageWindow.GuardClose(gl, "Global variables", () => gl.Globals != null && gl.Globals.Dirty, () => gl.Save() != null, () => AskBeforeDiscarding && !quietClose);
                globalsWindow.Closed += (s, e) => globalsWindow = null;
                globalsWindow.Show(this);
            }
            else globalsWindow.Activate();
            return globalsWindow;
        }

        /// <summary>Goes to a creature that talks through a conversation.</summary>
        void GoToUser(UwEditor.Core.Conversations.ConversationUser u)
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != u.Level) vm.LoadLevel(u.Level);
            vm.Select(u.Index, u.TileX, u.TileY, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
            Frame();
            Activate();
        }

        void OnFindAll(object sender, RoutedEventArgs e) { ShowFind(); }

        /// <summary>The find window, one at a time, seeded with the list's search text.</summary>
        public FindWindow ShowFind()
        {
            if (vm.Session == null) return null;
            if (findWindow == null)
            {
                findWindow = new FindWindow(vm, GoTo);
                findWindow.Model.Text = vm.Filter;
                findWindow.Closed += (s, e) => findWindow = null;
                findWindow.Show(this);
            }
            else findWindow.Activate();
            return findWindow;
        }

        /// <summary>Opens a result's level, selects the object and frames it.</summary>
        public void GoTo(SearchHit h)
        {
            if (vm.Session == null) return;
            if (vm.Level == null || vm.Level.Level != h.Level) vm.LoadLevel(h.Level);
            vm.Select(h.Index, h.TileX, h.TileY, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
            Frame();
        }

        void OnCheckLevel(object sender, RoutedEventArgs e)
        {
            var l = vm.Level;
            if (l == null) return;
            _ = MessageWindow.Show(this, "Object lists of level " + l.Level,
                (l.CheckOk ? "The level passes the game's check (Obj_ListOkay, obj/OBJECTS.C).\n\n" : "The level fails the game's check (Obj_ListOkay).\n\n") + l.CheckReport);
        }

        void ApplyAccent(bool uw2)
        {
            var b = new SolidColorBrush(ColourScheme.Accent(uw2));
            Resources["Accent"] = b;
            // the tool windows take the game's accent too
            if (Application.Current != null) Application.Current.Resources["Accent"] = b;
        }

        void OnDarkTheme(object sender, RoutedEventArgs e) { SetTheme(ColourScheme.Dark); }
        void OnLightTheme(object sender, RoutedEventArgs e) { SetTheme(ColourScheme.Light); }

        /// <summary>Switches the colour scheme of every window, and remembers it.</summary>
        public void SetTheme(string name)
        {
            ColourScheme.Set(name);
            settings.Theme = name;
            settings.Save();
            DarkThemeItem.IsChecked = name != ColourScheme.Light;
            LightThemeItem.IsChecked = name == ColourScheme.Light;
            ApplyAccent(vm.Session != null && vm.Session.IsUw2);
            vm.EditStatus = (name == ColourScheme.Light ? "Light" : "Dark") + " colours.";
        }

        void OnManual(object sender, RoutedEventArgs e) { ManualWindow.ShowAt(this); }

        void OnControls(object sender, RoutedEventArgs e)
        {
            _ = MessageWindow.Show(this, "Controls",
                "In the 3D view\n" +
                "  Left click              select an object or tile\n" +
                "  Left or right drag      turn round the point looked at\n" +
                "  Middle or Shift+drag    move the point\n" +
                "  Wheel, + and −          closer, farther\n" +
                "  W A S D or arrows       move (Shift: faster)\n" +
                "  Q, E                    turn\n" +
                "  R, V                    up, down a floor step\n" +
                "  Double click            select and go to it\n" +
                "  Ctrl+wheel              raise or lower the floor under the mouse\n\n" +
                "Editing\n" +
                "  Tool: Select            click picks\n" +
                "  Tool: Move              click a square: the selected object (or an arrow) goes there\n" +
                "  Tool: Paint             drag over tiles: light flag, floor or wall texture, type, height\n" +
                "  Insert, Del             add an object, delete the selected one\n" +
                "  Ctrl+Z, Ctrl+Y          undo, redo (each level keeps its own)\n" +
                "  Ctrl+H                  undo history\n" +
                "  Ctrl+C, X, V, D         copy, cut, paste, duplicate the selected objects\n" +
                "  Ctrl+click, Ctrl+drag   add to the selection, select tiles in a rectangle\n" +
                "  Drag a selected object  move it; drop it on a container to put it inside\n" +
                "  Right click             the right-click menu\n" +
                "  Tab                     the 2D map, and back\n" +
                "  F5                      play-test from the selected tile\n" +
                "  Ctrl+S                  save the levels (and a moved player)\n" +
                "  Properties panel        type a value and press Enter\n\n" +
                "First person (P)\n" +
                "  W A S D                 walk;  Q E, drag  turn;  R V  look up, down\n" +
                "  Wheel                   step;  Esc  leave\n\n" +
                "Markers\n" +
                "  Red arrow               the new-game start (level 1)\n" +
                "  Green arrow             the saved player: click it for the player window\n" +
                "  Bullseye                where the selected trap or trigger acts\n\n" +
                "Anywhere\n" +
                "  Ctrl+O                  open a game\n" +
                "  Ctrl+Shift+F            find in all levels\n" +
                "  Ctrl+Shift+C            conversations\n" +
                "  Ctrl+Shift+G            global variables\n" +
                "  Ctrl+Shift+P            player and inventory (a saved game)\n" +
                "  Ctrl+Shift+R            graphics\n" +
                "  Ctrl+Shift+K            keys and locks\n" +
                "  Ctrl+Shift+T            triggers and traps\n" +
                "  Ctrl+Shift+S            strings\n" +
                "  Ctrl+Shift+A            automap and map notes\n" +
                "  Ctrl+Shift+M            mods\n" +
                "  Ctrl+Shift+O            3D models\n" +
                "  PgUp, PgDn              previous, next level\n" +
                "  F                       frame the selection\n" +
                "  T                       look straight down\n" +
                "  G                       ceiling on or off\n" +
                "  H                       grid on or off\n" +
                "  M                       3D models or pictures\n" +
                "  Home                    back to the start or the player\n" +
                "  Esc                     clear the selection", true);
        }

        void OnAbout(object sender, RoutedEventArgs e)
        {
            _ = MessageWindow.Show(this, "About",
                "Underworld Editor (free of Unity), a level editor for Ultima Underworld I and II.\n\n" +
                "The file formats come from the Unity editor's Formats library; their meaning from underworld-exhumed, " +
                "the reconstructed source of both games.\n\n" +
                "OpenGL: " + (View.GlInfo ?? "not started"));
        }

        void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            bool typing = FocusManager?.GetFocusedElement() is TextBox;
            if (e.Key == Key.O && (e.KeyModifiers & KeyModifiers.Control) != 0 && (e.KeyModifiers & KeyModifiers.Shift) == 0) { OnOpen(this, null); e.Handled = true; return; }
            bool ctrlShift = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == (KeyModifiers.Control | KeyModifiers.Shift);
            if (e.Key == Key.F && ctrlShift) { ShowFind(); e.Handled = true; return; }
            if (e.Key == Key.C && ctrlShift) { ShowConversations(); e.Handled = true; return; }
            if (e.Key == Key.G && ctrlShift) { ShowGlobals(); e.Handled = true; return; }
            if (e.Key == Key.P && ctrlShift) { ShowPlayer(); e.Handled = true; return; }
            if (e.Key == Key.R && ctrlShift) { ShowGraphics(); e.Handled = true; return; }
            if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None) { OnPlayTest(this, null); e.Handled = true; return; }
            if (e.Key == Key.F1) { ManualWindow.ShowAt(this, vm.ToolIndex == 0 ? null : "Editing levels"); e.Handled = true; return; }
            if (e.Key == Key.K && ctrlShift) { ShowKeys(); e.Handled = true; return; }
            if (e.Key == Key.T && ctrlShift) { ShowTriggers(); e.Handled = true; return; }
            if (e.Key == Key.S && ctrlShift) { ShowStrings(); e.Handled = true; return; }
            if (e.Key == Key.M && ctrlShift) { ShowMods(); e.Handled = true; return; }
            if (e.Key == Key.O && ctrlShift) { ShowModels(); e.Handled = true; return; }
            if (e.Key == Key.A && ctrlShift) { ShowAutomap(); e.Handled = true; return; }
            bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0 && (e.KeyModifiers & KeyModifiers.Shift) == 0;
            if (ctrl && e.Key == Key.S) { SaveAll(); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.C) { CopySelection(false); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.X) { CopySelection(true); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.V) { PasteClip(-1, -1, -1); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.D) { Duplicate(); e.Handled = true; return; }
            if (ctrl && !typing && e.Key == Key.H) { ShowUndoHistory(); e.Handled = true; return; }
            if (e.Key == Key.Tab && !typing && e.KeyModifiers == KeyModifiers.None && (View.IsFocused || View.IsPointerOver)) { ToggleMap(); e.Handled = true; return; }
            if (typing || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) return;
            if (View.FirstPerson && e.Key == Key.Escape) { View.SetFirstPerson(false); e.Handled = true; return; }
            // the camera keys work whichever panel has the focus (the arrows stay with lists)
            if (!View.IsFocused && (e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D || e.Key == Key.Q || e.Key == Key.E || e.Key == Key.R || e.Key == Key.V))
            {
                View.CameraKey(e);
                return;
            }
            switch (e.Key)
            {
                case Key.PageUp: vm.StepLevel(-1); break;
                case Key.PageDown: vm.StepLevel(1); break;
                case Key.Home: GoToStart(); break;
                case Key.F: Frame(); break;
                case Key.T: OnTopDown(this, null); break;
                case Key.G: vm.ShowCeiling = !vm.ShowCeiling; break;
                case Key.H: vm.ShowGrid = !vm.ShowGrid; break;
                case Key.M: vm.ShowModels = !vm.ShowModels; break;
                case Key.Escape:
                    View.CancelDrag();
                    if (pick != null || carrying != null || objectPick != null) { pick = null; carrying = null; objectPick = null; vm.EditStatus = "Cancelled."; }
                    else { ClearMulti(); vm.ClearSelection(); }
                    break;
                case Key.Delete: DeleteSelected(); break;
                case Key.Insert: _ = AddObject(); break;
                case Key.P: ToggleFirstPerson(); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
