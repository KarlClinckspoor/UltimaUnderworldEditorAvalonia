using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using UwEditor.Core;
using UwEditor.Render;

namespace UwEditor.App
{
    /// <summary>
    /// The 3D view: hosts <see cref="GlRenderer"/> in Avalonia's OpenGL control and turns mouse
    /// and keys into camera moves and picks. It draws only when something changed.
    /// Mouse: left click selects, left or right drag orbits, middle or Shift+left drag pans,
    /// wheel zooms. Keys: W/A/S/D or arrows move, Q/E turn, R/F up and down... see Help.
    /// </summary>
    /// <summary>What a left click and drag do in the view.</summary>
    public enum ViewTool { Select, Paint, Move }

    public sealed class LevelView : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
    {
        /// <summary>Select: click picks, drag orbits. Paint: click and drag paint tiles (right drag orbits). Move: click a tile to move the selected object there.</summary>
        public ViewTool Tool = ViewTool.Select;
        /// <summary>A tile was painted: the pick, and whether it starts a stroke.</summary>
        public event Action<PickResult, bool> Painted;
        /// <summary>A paint stroke ended.</summary>
        public event Action PaintEnded;
        /// <summary>Ctrl + wheel over a tile: steps up (positive) or down.</summary>
        public event Action<PickResult, int> HeightWheel;
        /// <summary>A double click picked something.</summary>
        public event Action<PickResult> DoublePicked;

        // ---- first person
        /// <summary>Walking at eye height through the level instead of orbiting over it.</summary>
        public bool FirstPerson { get; private set; }
        /// <summary>The floor height (in tiles) at a point, or null where there is rock; set by the window.</summary>
        public Func<float, float, float?> GroundAt;
        /// <summary>The eye's height above the floor, in tiles (a floor step is 0.25).</summary>
        public const float EyeHeight = 0.75f;
        float savedDistance = 8, savedPitch = 45;
        public event Action FirstPersonChanged;
        /// <summary>Where the last click (or double click) was, in the view's coordinates.</summary>
        public Point LastClick { get; private set; }
        bool painting;
        readonly GlRenderer renderer = new GlRenderer();
        public readonly OrbitCamera Camera = new OrbitCamera();
        public readonly ViewState State = new ViewState();
        LevelScene scene;
        bool glReady;
        Point pressAt, lastAt;
        bool dragging, panning, leftDown;
        readonly Stopwatch frameClock = new Stopwatch();

        /// <summary>A click picked something (or nothing: Hit false).</summary>
        public event Action<PickResult> Picked;
        /// <summary>What lies under the mouse changed.</summary>
        public event Action<PickResult> Hovered;
        /// <summary>The camera moved.</summary>
        public event Action CameraChanged;

        public double LastFrameMs { get; private set; }
        public string GlInfo { get { return renderer.Info; } }
        public string GlError { get; private set; }

        public LevelView()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        /// <summary>The GL picture is not something Avalonia can hit-test, so the whole area takes the mouse.</summary>
        public bool HitTest(Point point) { return new Rect(Bounds.Size).Contains(point); }

        /// <summary>A frame was drawn (raised on the UI thread).</summary>
        public event Action FrameDrawn;

        protected override void OnOpenGlInit(GlInterface gl)
        {
            try
            {
                renderer.Init(gl.GetProcAddress, GlVersion.Type == GlProfileType.OpenGLES);
                glReady = true;
                if (scene != null) Upload(scene, true);
                // a new GL context (the view moved to another window, say) gets the overlays again too
                if (lastGrid != null) renderer.SetGrid(lastGrid);
                if (lastLines != null) renderer.SetLines(lastLines);
                if (lastMarks != null) renderer.SetMarkers(lastMarks);
            }
            catch (Exception e)
            {
                GlError = e.Message;
            }
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            renderer.Dispose();
            glReady = false;
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (!glReady) return;
            frameClock.Restart();
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int w = (int)Math.Max(1, Bounds.Width * scale), h = (int)Math.Max(1, Bounds.Height * scale);
            renderer.Render(fb, w, h, Camera, State);
            if (renderer.Error != null) GlError = renderer.Error;
            LastFrameMs = frameClock.Elapsed.TotalMilliseconds;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FrameDrawn?.Invoke());
        }

        // ---- what to draw

        public LevelScene Scene { get { return scene; } }

        public void SetScene(LevelScene s)
        {
            scene = s;
            Upload(s, true);
            Redraw();
        }

        /// <summary>The scene's objects (models and sprites) changed.</summary>
        public void RefreshObjects()
        {
            if (scene == null) return;
            renderer.SetModels(scene.Models);
            renderer.SetSprites(scene.Sprites, scene.Atlas);
            Redraw();
        }

        float[] lastGrid, lastLines, lastMarks;

        public void SetGrid(List<float> lines)
        {
            renderer.SetGrid(lastGrid = lines == null ? new float[0] : lines.ToArray());
            Redraw();
        }

        public void SetLines(List<float> lines)
        {
            renderer.SetLines(lastLines = lines == null ? new float[0] : lines.ToArray());
            Redraw();
        }

        /// <summary>Solid markers (Core.Markers): arrows and bullseyes.</summary>
        public void SetMarkers(List<float> tris)
        {
            renderer.SetMarkers(lastMarks = tris == null ? new float[0] : tris.ToArray());
            Redraw();
        }

        /// <summary>The same level after an edit: new geometry and objects, the atlas kept.</summary>
        public void UpdateScene(LevelScene s)
        {
            scene = s;
            Upload(s, false);
            Redraw();
        }

        void Upload(LevelScene s, bool atlas)
        {
            if (s == null) { renderer.SetLevel(null, null); renderer.SetSprites(null, null); renderer.SetModels(null); return; }
            renderer.SetLevel(s.Mesh, s.Layers);
            renderer.SetModels(s.Models);
            if (atlas && s.Atlas != null) renderer.SetAtlas(s.Atlas.Image);
            renderer.SetSprites(s.Sprites, s.Atlas);
        }

        public void Redraw()
        {
            RequestNextFrameRendering();
        }

        // ---- picking

        public PickResult PickAt(Point p)
        {
            if (scene == null) return PickResult.None;
            Vector3 o, d;
            Camera.Ray((float)p.X, (float)p.Y, (float)Bounds.Width, (float)Bounds.Height, out o, out d);
            return Picking.Pick(o, d, scene.Mesh, scene.Sprites, Camera.Right, Camera.Up, scene.Models);
        }

        // ---- mouse

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var pt = e.GetCurrentPoint(this);
            pressAt = lastAt = pt.Position;
            dragging = false;
            leftDown = pt.Properties.IsLeftButtonPressed;
            panning = pt.Properties.IsMiddleButtonPressed || (leftDown && (e.KeyModifiers & KeyModifiers.Shift) != 0);
            e.Pointer.Capture(this);
            e.Handled = true;
            if (leftDown && !panning && e.ClickCount == 2)
            {
                LastClick = pt.Position;
                DoublePicked?.Invoke(PickAt(pt.Position));
                leftDown = false;
                return;
            }
            if (leftDown && !panning && Tool == ViewTool.Paint)
            {
                painting = true;
                Painted?.Invoke(PickAt(pt.Position), true);
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pt = e.GetCurrentPoint(this);
            var p = pt.Position;
            var props = pt.Properties;
            bool any = props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed;
            if (painting && props.IsLeftButtonPressed)
            {
                var ph = PickAt(p);
                if (ph.Hit && (ph.Tile != State.HoverTile))
                {
                    State.HoverTile = ph.Tile; State.HoverObject = -1;
                    Hovered?.Invoke(ph);
                    Painted?.Invoke(ph, false);
                    Redraw();
                }
                lastAt = p;
                return;
            }
            if (any)
            {
                var d = p - lastAt;
                if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 4) dragging = true;
                if (dragging)
                {
                    if (panning && !FirstPerson) Camera.Pan((float)d.X, (float)d.Y, (float)Bounds.Height);
                    else if (FirstPerson) Camera.Orbit((float)d.X * 0.3f, (float)d.Y * 0.25f);
                    else Camera.Orbit((float)d.X * 0.35f, (float)d.Y * 0.3f);
                    CameraMoved();
                }
                lastAt = p;
            }
            else
            {
                var hit = PickAt(p);
                if (hit.Tile != State.HoverTile || hit.Object != State.HoverObject)
                {
                    State.HoverTile = hit.Object >= 0 ? -1 : hit.Tile;
                    State.HoverObject = hit.Object;
                    Hovered?.Invoke(hit);
                    Redraw();
                }
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            if (painting) { painting = false; PaintEnded?.Invoke(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            bool wasPainting = painting;
            e.Pointer.Capture(null);                                // may end the stroke (capture lost)
            if (wasPainting) { if (painting) { painting = false; PaintEnded?.Invoke(); } }
            else if (!dragging && leftDown && e.InitialPressMouseButton == MouseButton.Left && !panning)
            {
                LastClick = e.GetPosition(this);
                Picked?.Invoke(PickAt(LastClick));
            }
            dragging = panning = leftDown = false;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (State.HoverTile >= 0 || State.HoverObject >= 0)
            {
                State.HoverTile = State.HoverObject = -1;
                Hovered?.Invoke(PickResult.None);
                Redraw();
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            e.Handled = true;
            if ((e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                var hit = PickAt(e.GetPosition(this));
                int steps = e.Delta.Y > 0 ? 1 : e.Delta.Y < 0 ? -1 : 0;
                if (hit.Hit && steps != 0) HeightWheel?.Invoke(hit, steps);
                return;
            }
            if (FirstPerson) { Walk(e.Delta.Y > 0 ? 0.5f : -0.5f, 0); return; }
            Camera.Zoom((float)Math.Pow(0.88, e.Delta.Y));
            CameraMoved();
        }

        // ---- keys

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            CameraKey(e);
        }

        /// <summary>Moves the camera for W/A/S/D, the arrows, Q/E, R/V, + and −; marks the key handled.</summary>
        public void CameraKey(KeyEventArgs e)
        {
            if (FirstPerson && WalkKey(e)) { e.Handled = true; return; }
            float step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 4f : 1f;
            switch (e.Key)
            {
                case Key.W: case Key.Up: Camera.Move(step, 0); break;
                case Key.S: case Key.Down: Camera.Move(-step, 0); break;
                case Key.A: case Key.Left: Camera.Move(0, -step); break;
                case Key.D: case Key.Right: Camera.Move(0, step); break;
                case Key.Q: Camera.Orbit(-15, 0); break;
                case Key.E: Camera.Orbit(15, 0); break;
                case Key.R: Camera.Target += new Vector3(0, LevelGeometry.Step, 0); break;
                case Key.V: Camera.Target -= new Vector3(0, LevelGeometry.Step, 0); break;
                case Key.Add: case Key.OemPlus: Camera.Zoom(0.8f); break;
                case Key.Subtract: case Key.OemMinus: Camera.Zoom(1.25f); break;
                default: return;
            }
            CameraMoved();
            e.Handled = true;
        }

        bool WalkKey(KeyEventArgs e)
        {
            float step = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? 0.6f : 0.25f;
            switch (e.Key)
            {
                case Key.W: case Key.Up: Walk(step, 0); return true;
                case Key.S: case Key.Down: Walk(-step, 0); return true;
                case Key.A: Walk(0, -step); return true;
                case Key.D: Walk(0, step); return true;
                case Key.Left: case Key.Q: Camera.Orbit(-10, 0); CameraMoved(); return true;
                case Key.Right: case Key.E: Camera.Orbit(10, 0); CameraMoved(); return true;
                case Key.R: Camera.Orbit(0, -8); CameraMoved(); return true;
                case Key.V: Camera.Orbit(0, 8); CameraMoved(); return true;
                case Key.Escape: SetFirstPerson(false); return true;
            }
            return false;
        }

        /// <summary>Starts or ends walking. Starting stands on the floor under the camera's target (or at a given point), looking the camera's way.</summary>
        public void SetFirstPerson(bool on, Vector3? at = null)
        {
            if (on == FirstPerson && at == null) return;
            if (on)
            {
                if (!FirstPerson) { savedDistance = Camera.Distance; savedPitch = Camera.Pitch; }
                var p = at ?? Camera.Target;
                float? g = GroundAt?.Invoke(p.X, p.Z);
                if (g == null) return;   // rock: nowhere to stand
                FirstPerson = true;
                Camera.MinPitch = -70;
                Camera.Target = new Vector3(p.X, g.Value + EyeHeight, p.Z);
                Camera.Distance = 0.02f;
                Camera.Pitch = 5;
            }
            else
            {
                FirstPerson = false;
                Camera.MinPitch = -10;
                var t = Camera.Target;
                float? g = GroundAt?.Invoke(t.X, t.Z);
                Camera.Target = new Vector3(t.X, g ?? t.Y - EyeHeight, t.Z);
                Camera.Distance = Math.Max(3, savedDistance);
                Camera.Pitch = Math.Max(20, savedPitch);
            }
            FirstPersonChanged?.Invoke();
            CameraMoved();
        }

        /// <summary>Walks forward and to the right (tiles), stopping at rock and at floors more than two steps up; the eye follows the floor.</summary>
        public void Walk(float forward, float right)
        {
            if (!FirstPerson || GroundAt == null) return;
            double y = Camera.Yaw * Math.PI / 180;
            var f = new Vector3((float)Math.Sin(y), 0, (float)Math.Cos(y));
            var r = new Vector3((float)Math.Cos(y), 0, (float)-Math.Sin(y));
            var move = f * forward + r * right;
            var pos = Camera.Target;
            float ground = pos.Y - EyeHeight;
            // try the whole move, then each axis alone (sliding along walls)
            foreach (var m in new[] { move, new Vector3(move.X, 0, 0), new Vector3(0, 0, move.Z) })
            {
                if (m.LengthSquared() < 1e-8f) continue;
                var np = pos + m;
                float? g = Clear(np.X, np.Z, ground);
                if (g == null) continue;
                Camera.Target = new Vector3(np.X, g.Value + EyeHeight, np.Z);
                CameraMoved();
                return;
            }
        }

        /// <summary>The floor at a point if the body (0.2 tiles round) fits there: no rock, no step up of more than two floor steps.</summary>
        float? Clear(float x, float z, float from)
        {
            const float radius = 0.2f;
            float? centre = GroundAt(x, z);
            if (centre == null || centre.Value - from > 0.5f) return null;
            foreach (var d in new[] { new Vector2(radius, 0), new Vector2(-radius, 0), new Vector2(0, radius), new Vector2(0, -radius) })
            {
                float? g = GroundAt(x + d.X, z + d.Y);
                if (g == null || g.Value - from > 0.5f) return null;
            }
            return centre;
        }

        public void CameraMoved()
        {
            CameraChanged?.Invoke();
            Redraw();
        }

        /// <summary>Looks at a point from the current direction, at a given distance.</summary>
        public void LookAt(Vector3 target, float? distance = null, float? yaw = null, float? pitch = null)
        {
            Camera.Target = target;
            if (distance.HasValue) Camera.Distance = distance.Value;
            if (yaw.HasValue) Camera.Yaw = yaw.Value;
            if (pitch.HasValue) Camera.Pitch = pitch.Value;
            CameraMoved();
        }
    }
}
