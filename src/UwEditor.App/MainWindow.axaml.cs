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

        static readonly Color Uw1Accent = Color.Parse("#C9934C"), Uw2Accent = Color.Parse("#6FA6DC");
        static readonly Vector4 StartColour = new Vector4(1f, 0.25f, 0.85f, 1f), SelColour = new Vector4(1f, 0.85f, 0.25f, 1f), HoverColour = new Vector4(1f, 1f, 1f, 0.45f);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = vm;
            vm.LevelChanged += OnLevelChanged;
            vm.SelectionChanged += UpdateMarkers;
            vm.OptionsChanged += OnOptionsChanged;
            View.Picked += OnPicked;
            View.Hovered += OnHovered;
            View.CameraChanged += UpdateViewText;
            View.FrameDrawn += UpdateViewText;
            AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            Columns.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(settings.LeftWidth, 200, 600));
            Columns.ColumnDefinitions[4].Width = new GridLength(Math.Clamp(settings.RightWidth, 240, 700));
            BuildRecentMenu();
            Opened += (s, e) =>
            {
                if (Program.OpenAtStart != null) OpenPath(Program.OpenAtStart);
                if (Program.LevelAtStart > 0 && vm.Session != null && Program.LevelAtStart <= vm.Session.Profile.LevelSlots) vm.LoadLevel(Program.LevelAtStart);
                View.Focus();
            };
            Closing += (s, e) =>
            {
                settings.LeftWidth = Columns.ColumnDefinitions[0].ActualWidth;
                settings.RightWidth = Columns.ColumnDefinitions[4].ActualWidth;
                settings.Save();
            };
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

        public void OpenPath(string path)
        {
            GameSession s;
            try { s = GameSession.Open(path); }
            catch (Exception ex)
            {
                _ = MessageWindow.Show(this, "Could not open the game", ex.Message);
                return;
            }
            atlas = SpriteAtlas.Build(s.Graphics.ObjectPictures);
            Resources["Accent"] = new SolidColorBrush(s.IsUw2 ? Uw2Accent : Uw1Accent);
            Title = "Underworld Editor — " + s.Title;
            settings.AddRecent(s.SaveFolder ?? s.GameFolder);
            settings.Save();
            BuildRecentMenu();
            vm.Open(s);
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
            scene = level == null ? null : LevelScene.Build(level, atlas, vm.Options);
            View.SetScene(scene);
            View.State.SelectedObject = View.State.SelectedTile = View.State.HoverObject = View.State.HoverTile = -1;
            vm.HoverText = "";
            PlaceCamera(level);
            UpdateMarkers();
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

        void OnOptionsChanged(bool rebuildMesh)
        {
            if (vm.Level == null) return;
            View.State.Darkness = vm.Darkness;
            if (rebuildMesh)
            {
                scene = LevelScene.Build(vm.Level, atlas, vm.Options);
                View.SetScene(scene);
            }
            else
            {
                scene.RebuildSprites(vm.Options);
                View.RefreshSprites();
            }
            View.Redraw();
        }

        // ---- selection and markers

        void OnPicked(PickResult hit)
        {
            if (!hit.Hit) { vm.ClearSelection(); return; }
            vm.Select(hit.Object, hit.Tile % 64, hit.Tile / 64, true);
            if (vm.SelectedNode != null) ObjectTree.ScrollIntoView(vm.SelectedNode);
        }

        void UpdateMarkers()
        {
            var lines = new List<float>();
            var level = vm.Level;
            if (level != null && scene != null)
            {
                if (level.Level == vm.Session.Start.Level)
                    LevelScene.StartMarker(lines, StartPoint(level), (float)vm.Session.Start.FacingDegrees, StartColour);
                if (vm.SelectedTileX >= 0) scene.TileOutline(lines, vm.SelectedTileX, vm.SelectedTileY, SelColour);
            }
            View.State.SelectedObject = vm.SelectedObject;
            View.State.SelectedTile = vm.SelectedObject >= 0 ? -1 : (vm.SelectedTileX >= 0 ? vm.SelectedTileX + 64 * vm.SelectedTileY : -1);
            View.SetLines(lines);
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
            Vector3 target;
            float distance;
            if (vm.SelectedObject >= 0 && level.Objects[vm.SelectedObject].TileX >= 0)
            {
                var o = level.Objects[vm.SelectedObject];
                target = new Vector3((float)o.WorldX, (float)o.WorldZ + 0.2f, (float)o.WorldY);
                distance = 2.5f;
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

        void OnCheckLevel(object sender, RoutedEventArgs e)
        {
            var l = vm.Level;
            if (l == null) return;
            _ = MessageWindow.Show(this, "Object lists of level " + l.Level,
                (l.CheckOk ? "The level passes the game's check (Obj_ListOkay, obj/OBJECTS.C).\n\n" : "The level fails the game's check (Obj_ListOkay).\n\n") + l.CheckReport);
        }

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
                "  R, V                    up, down a floor step\n\n" +
                "Anywhere\n" +
                "  Ctrl+O                  open a game\n" +
                "  PgUp, PgDn              previous, next level\n" +
                "  F                       frame the selection\n" +
                "  T                       look straight down\n" +
                "  G                       ceiling on or off\n" +
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
            if (e.Key == Key.O && (e.KeyModifiers & KeyModifiers.Control) != 0) { OnOpen(this, null); e.Handled = true; return; }
            if (typing || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0) return;
            switch (e.Key)
            {
                case Key.PageUp: vm.StepLevel(-1); break;
                case Key.PageDown: vm.StepLevel(1); break;
                case Key.Home: GoToStart(); break;
                case Key.F: Frame(); break;
                case Key.T: OnTopDown(this, null); break;
                case Key.G: vm.ShowCeiling = !vm.ShowCeiling; break;
                case Key.Escape: vm.ClearSelection(); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
