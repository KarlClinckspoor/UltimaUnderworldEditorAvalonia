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
        /// <summary>A right click without a drag: the context menu, for what is under the mouse.</summary>
        public event Action<PickResult> MenuRequested;
        /// <summary>Whether a left press on this pick may drag it (the selected objects, in the Select tool).</summary>
        public Func<PickResult, bool> CanDrag;
        /// <summary>An object drag: what is under the mouse now (the start is the press's pick).</summary>
        public event Action<PickResult> DragMoved;
        /// <summary>The drag ended over a pick (dropped), or was cancelled (Hit false, Esc or capture lost).</summary>
        public event Action<PickResult> DragEnded;
        /// <summary>Ctrl + left drag: the rectangle being drawn (null when it ends), in the view's coordinates.</summary>
        public event Action<Rect?> RectChanged;
        /// <summary>Ctrl + left drag ended: the rectangle to select in.</summary>
        public event Action<Rect> RectSelected;
        /// <summary>The keys held at the last click.</summary>
        public KeyModifiers LastModifiers { get; private set; }
        bool objectDrag, objectDragging, rectSelect;

        // ---- the 2D map
        /// <summary>The 2D map: looking straight down without perspective; left and right drags pan, the wheel zooms.</summary>
        public bool TopDown { get; private set; }
        float savedYaw, savedPitch2D = 45, savedDistance2D = 8;
        public event Action TopDownChanged;

        /// <summary>Switches the 2D map on or off (the 3D camera comes back as it was).</summary>
        public void SetTopDown(bool on)
        {
            if (on == TopDown) return;
            if (on && FirstPerson) SetFirstPerson(false);
            if (on)
            {
                savedYaw = Camera.Yaw; savedPitch2D = Camera.Pitch; savedDistance2D = Camera.Distance;
                Camera.Orthographic = true;
                Camera.Yaw = 0; Camera.Pitch = 89;
                Camera.Distance = Math.Max(Camera.Distance, 14);
            }
            else
            {
                Camera.Orthographic = false;
                Camera.Yaw = savedYaw; Camera.Pitch = savedPitch2D; Camera.Distance = savedDistance2D;
            }
            TopDown = on;
            TopDownChanged?.Invoke();
            CameraMoved();
        }

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

        /// <summary>
        /// Draws some objects' sprites moved by offsets (world units) without changing the scene, as a
        /// drag shows where they will go; null puts them back. Picking still uses the scene's places.
        /// </summary>
        public void PreviewSprites(IDictionary<int, Vector3> offsets)
        {
            if (scene == null) return;
            if (offsets == null || offsets.Count == 0)
            {
                if (!previewing) return;
                previewing = false;
                renderer.SetSprites(scene.Sprites, scene.Atlas);
                Redraw();
                return;
            }
            var list = new List<SpriteInstance>(scene.Sprites.Count);
            foreach (var s in scene.Sprites)
            {
                Vector3 d;
                if (offsets.TryGetValue(s.Object, out d)) { var m = s; m.Base += d; list.Add(m); }
                else list.Add(s);
            }
            previewing = true;
            renderer.SetSprites(list, scene.Atlas);
            Redraw();
        }
        bool previewing;

        float[] lastGrid, lastLines, lastMarks;
        /// <summary>Ctrl+click with the Paint tool: the brush is taken from the tile under the mouse.</summary>
        public event Action<PickResult> BrushPicked;
        /// <summary>How many floats the marker lines and the marker triangles hold now (for the tests).</summary>
        public int LineFloats { get { return lastLines == null ? 0 : lastLines.Length; } }
        public int MarkerFloats { get { return lastMarks == null ? 0 : lastMarks.Length; } }

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

        /// <summary>The same level after an edit: new geometry and objects, the atlas kept (the objects only when they changed).</summary>
        public void UpdateScene(LevelScene s)
        {
            scene = s;
            if (s != null && s.ObjectsReused && !previewing) renderer.SetLevel(s.Mesh, s.Layers);
            else Upload(s, false);
            Redraw();
        }

        void Upload(LevelScene s, bool atlas)
        {
            previewing = false;
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
            // during a paint stroke, the level as it was when the stroke began: the walls and steps the
            // stroke makes would otherwise catch the mouse, and the next tile under it would be missed
            var sc = painting && strokeScene != null ? strokeScene : scene;
            if (sc == null) return PickResult.None;
            Vector3 o, d;
            Camera.Ray((float)p.X, (float)p.Y, (float)Bounds.Width, (float)Bounds.Height, out o, out d);
            return Picking.Pick(o, d, sc.Mesh, sc.Sprites, Camera.Right, Camera.Up, sc.Models);
        }

        /// <summary>The scene when the paint stroke began (picking keeps to it until the stroke ends).</summary>
        LevelScene strokeScene;

        // ---- mouse

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var pt = e.GetCurrentPoint(this);
            pressAt = lastAt = pt.Position;
            dragging = false;
            leftDown = pt.Properties.IsLeftButtonPressed;
            LastModifiers = e.KeyModifiers;
            panning = pt.Properties.IsMiddleButtonPressed || (leftDown && (e.KeyModifiers & KeyModifiers.Shift) != 0)
                || (TopDown && (pt.Properties.IsRightButtonPressed || (leftDown && Tool == ViewTool.Select)));   // the 2D map pans; left drags pan only off the selection
            objectDrag = objectDragging = rectSelect = false;
            e.Pointer.Capture(this);
            e.Handled = true;
            // a second press soon after a click is a double-click, except with the brush: a click, then a drag, paints on
            if (leftDown && !panning && e.ClickCount == 2 && Tool != ViewTool.Paint)
            {
                LastClick = pt.Position;
                DoublePicked?.Invoke(PickAt(pt.Position));
                leftDown = false;
                return;
            }
            if (leftDown && Tool == ViewTool.Select && !FirstPerson && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                rectSelect = true; panning = false;
                return;
            }
            if (leftDown && Tool == ViewTool.Select && (e.KeyModifiers & KeyModifiers.Shift) == 0 && CanDrag != null && CanDrag(PickAt(pt.Position)))
            {
                objectDrag = true; panning = false;
                return;
            }
            if (leftDown && !panning && Tool == ViewTool.Paint && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                // Ctrl+click with the brush: pick it up from the tile instead of painting
                BrushPicked?.Invoke(PickAt(pt.Position));
                leftDown = false;
                return;
            }
            if (leftDown && !panning && Tool == ViewTool.Paint)
            {
                strokeScene = scene;
                painting = true;
                Painted?.Invoke(PickAt(pt.Position), true);
            }
        }

        Rect DragRect(Point p) { return new Rect(new Point(Math.Min(p.X, pressAt.X), Math.Min(p.Y, pressAt.Y)), new Point(Math.Max(p.X, pressAt.X), Math.Max(p.Y, pressAt.Y))); }

        /// <summary>Stops an object drag without dropping (Esc).</summary>
        public void CancelDrag()
        {
            if (!objectDrag) return;
            bool was = objectDragging;
            objectDrag = objectDragging = false;
            if (was) DragEnded?.Invoke(PickResult.None);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pt = e.GetCurrentPoint(this);
            var p = pt.Position;
            var props = pt.Properties;
            bool any = props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed;
            if ((rectSelect || objectDrag) && props.IsLeftButtonPressed)
            {
                if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 5) dragging = true;
                if (dragging && rectSelect) RectChanged?.Invoke(DragRect(p));
                if (dragging && objectDrag) { objectDragging = true; DragMoved?.Invoke(PickAt(p)); }
                lastAt = p;
                return;
            }
            if (painting && props.IsLeftButtonPressed)
            {
                // every few pixels along the way from the last point, so a quick move skips no tile
                var d = p - lastAt;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(d.X * d.X + d.Y * d.Y) / 4));
                for (int k = 1; k <= steps; k++)
                {
                    var ph = PickAt(lastAt + d * ((double)k / steps));
                    if (ph.Hit && ph.Tile != State.HoverTile)
                    {
                        State.HoverTile = ph.Tile; State.HoverObject = -1;
                        Hovered?.Invoke(ph);
                        Painted?.Invoke(ph, false);
                    }
                }
                Redraw();
                lastAt = p;
                return;
            }
            if (any)
            {
                var d = p - lastAt;
                if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 4) dragging = true;
                if (dragging)
                {
                    if ((panning || TopDown) && !FirstPerson) Camera.Pan((float)d.X, (float)d.Y, (float)Bounds.Height);
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
            if (painting) { painting = false; strokeScene = null; PaintEnded?.Invoke(); }
            if (rectSelect) { rectSelect = false; RectChanged?.Invoke(null); }
            if (objectDrag) CancelDrag();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            bool wasPainting = painting, wasRect = rectSelect, wasDrag = objectDrag, wasDragging = objectDragging;
            var at = e.GetPosition(this);
            rectSelect = objectDrag = objectDragging = false;      // before the capture goes (that would cancel them)
            e.Pointer.Capture(null);                                // may end the stroke (capture lost)
            if (wasPainting) { if (painting) { painting = false; strokeScene = null; PaintEnded?.Invoke(); } }
            else if (wasRect && dragging) { RectChanged?.Invoke(null); RectSelected?.Invoke(DragRect(at)); }
            else if (wasDrag && wasDragging) DragEnded?.Invoke(PickAt(at));
            else if (!dragging && e.InitialPressMouseButton == MouseButton.Right && !FirstPerson)
            {
                LastClick = at;
                LastModifiers = e.KeyModifiers;
                MenuRequested?.Invoke(PickAt(at));
            }
            else if (!dragging && (leftDown || wasRect || wasDrag) && e.InitialPressMouseButton == MouseButton.Left && (!panning || TopDown))
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

        /// <summary>
        /// The camera keys: W/A/S/D and the arrows move and Q/E turn for as long as they are held (and
        /// in first person R/V look up and down), smoothly (SmoothMotion); R/V step the point looked at
        /// a floor step up or down, + and − zoom. Marks the key handled.
        /// </summary>
        public void CameraKey(KeyEventArgs e)
        {
            if (FirstPerson && e.Key == Key.Escape) { SetFirstPerson(false); e.Handled = true; return; }
            if (IsMotionKey(e.Key))
            {
                held.Add(e.Key);
                shiftHeld = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                StartMotion();
                e.Handled = true;
                return;
            }
            if (FirstPerson) return;
            switch (e.Key)
            {
                case Key.R: Camera.Target += new Vector3(0, LevelGeometry.Step, 0); break;
                case Key.V: Camera.Target -= new Vector3(0, LevelGeometry.Step, 0); break;
                case Key.Add: case Key.OemPlus: Camera.Zoom(0.8f); break;
                case Key.Subtract: case Key.OemMinus: Camera.Zoom(1.25f); break;
                default: return;
            }
            CameraMoved();
            e.Handled = true;
        }

        // ---- smooth movement while keys are held

        readonly HashSet<Key> held = new HashSet<Key>();
        bool shiftHeld, frameAsked;
        TimeSpan lastFrame = TimeSpan.MinValue;
        readonly SmoothMotion motion = new SmoothMotion();
        TopLevel keysFrom;

        /// <summary>Orbiting: speed in tiles a second at the default distance (it grows with the distance); turning in degrees a second; Shift is three times faster.</summary>
        public const float OrbitSpeed = 8f, TurnSpeed = 110f, WalkSpeed = 2.5f, RunSpeed = 6f, LookSpeed = 80f;

        bool IsMotionKey(Key k)
        {
            switch (k)
            {
                case Key.W: case Key.A: case Key.S: case Key.D: case Key.Up: case Key.Down: case Key.Left: case Key.Right: case Key.Q: case Key.E: return true;
                case Key.R: case Key.V: return FirstPerson;
            }
            return false;
        }

        /// <summary>The keys held now (tests).</summary>
        public int HeldKeys { get { return held.Count; } }
        /// <summary>Whether the camera is still gliding (keys held, or slowing down).</summary>
        public bool IsMoving { get { return held.Count > 0 || motion.Moving; } }

        /// <summary>A key came up (anywhere in the window): it stops moving the camera.</summary>
        public void KeyReleased(Key k, KeyModifiers mods)
        {
            if (k == Key.LeftShift || k == Key.RightShift) shiftHeld = false;
            else shiftHeld = (mods & KeyModifiers.Shift) != 0;
            held.Remove(k);
        }

        /// <summary>Lets go of every key (the window lost the focus, the view changed mode).</summary>
        public void ReleaseKeys() { held.Clear(); }

        /// <summary>What the held keys ask for: forward, right (−1..1), turn and look (−1..1).</summary>
        Vector4 Wanted()
        {
            float f = 0, r = 0, t = 0, l = 0;
            if (held.Contains(Key.W) || held.Contains(Key.Up)) f += 1;
            if (held.Contains(Key.S) || held.Contains(Key.Down)) f -= 1;
            if (held.Contains(Key.D)) r += 1;
            if (held.Contains(Key.A)) r -= 1;
            if (held.Contains(Key.E)) t += 1;
            if (held.Contains(Key.Q)) t -= 1;
            // the arrows: sideways when orbiting, turning when walking (as before)
            if (FirstPerson) { if (held.Contains(Key.Right)) t += 1; if (held.Contains(Key.Left)) t -= 1; }
            else { if (held.Contains(Key.Right)) r += 1; if (held.Contains(Key.Left)) r -= 1; }
            if (FirstPerson) { if (held.Contains(Key.V)) l += 1; if (held.Contains(Key.R)) l -= 1; }
            if (TopDown) t = 0;
            var move = new Vector2(f, r);
            if (move.LengthSquared() > 1) move = Vector2.Normalize(move);   // diagonals no faster
            float speed = FirstPerson ? (shiftHeld ? RunSpeed : WalkSpeed) : OrbitSpeed * Math.Clamp(Camera.Distance / 8f, 0.4f, 6f) * (shiftHeld ? 3 : 1);
            float turn = TurnSpeed * (shiftHeld ? 2 : 1);
            return new Vector4(move.X * speed, move.Y * speed, t * turn, l * LookSpeed);
        }

        /// <summary>Moves the camera by dt seconds of the held keys (the frame loop; tests call it directly). True while it still moves.</summary>
        public bool StepMotion(double dt)
        {
            var d = motion.Step(Wanted(), dt);
            if (d == Vector4.Zero) return IsMoving;
            if (FirstPerson) Walk(d.X, d.Y);
            else Camera.Move(d.X, d.Y);
            if (d.Z != 0 || d.W != 0) Camera.Orbit(d.Z, d.W);
            CameraMoved();
            return IsMoving;
        }

        /// <summary>Whether held keys move the camera every frame by themselves (tests turn it off and call StepMotion).</summary>
        public bool KeysByFrame = true;

        void StartMotion()
        {
            if (frameAsked || !KeysByFrame) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            frameAsked = true;
            lastFrame = TimeSpan.MinValue;
            top.RequestAnimationFrame(OnMotionFrame);
        }

        void OnMotionFrame(TimeSpan now)
        {
            frameAsked = false;
            double dt = lastFrame == TimeSpan.MinValue ? 1 / 60.0 : (now - lastFrame).TotalSeconds;
            lastFrame = now;
            if (!StepMotion(dt)) { motion.Stop(); return; }
            var top = TopLevel.GetTopLevel(this);
            if (top == null) { held.Clear(); motion.Stop(); return; }
            frameAsked = true;
            top.RequestAnimationFrame(OnMotionFrame);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            keysFrom = TopLevel.GetTopLevel(this);
            if (keysFrom == null) return;
            // key ups anywhere in the window, even when another panel has the focus (the window passes the key downs on)
            keysFrom.AddHandler(KeyUpEvent, OnAnyKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            if (keysFrom is WindowBase w) w.Deactivated += OnWindowDeactivated;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (keysFrom != null)
            {
                keysFrom.RemoveHandler(KeyUpEvent, OnAnyKeyUp);
                if (keysFrom is WindowBase w) w.Deactivated -= OnWindowDeactivated;
            }
            keysFrom = null;
            held.Clear();
        }

        void OnAnyKeyUp(object sender, KeyEventArgs e) { KeyReleased(e.Key, e.KeyModifiers); }
        void OnWindowDeactivated(object sender, EventArgs e) { held.Clear(); }

        /// <summary>Starts or ends walking. Starting stands on the floor under the camera's target (or at a given point), looking the camera's way.</summary>
        public void SetFirstPerson(bool on, Vector3? at = null)
        {
            if (on == FirstPerson && at == null) return;
            if (on && TopDown) SetTopDown(false);
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
            held.Clear(); motion.Stop();
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
            if (distance.HasValue) Camera.Distance = TopDown ? Math.Max(distance.Value, 6) : distance.Value;
            if (yaw.HasValue && !TopDown) Camera.Yaw = yaw.Value;          // the 2D map stays north up, straight down
            if (pitch.HasValue && !TopDown) Camera.Pitch = pitch.Value;
            CameraMoved();
        }
    }
}
