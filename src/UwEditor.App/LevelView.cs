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
    public sealed class LevelView : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
    {
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

        public void RefreshSprites()
        {
            if (scene == null) return;
            renderer.SetSprites(scene.Sprites, scene.Atlas);
            Redraw();
        }

        public void SetLines(List<float> lines)
        {
            renderer.SetLines(lines == null ? new float[0] : lines.ToArray());
            Redraw();
        }

        void Upload(LevelScene s, bool atlas)
        {
            if (s == null) { renderer.SetLevel(null, null); renderer.SetSprites(null, null); return; }
            renderer.SetLevel(s.Mesh, s.Layers);
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
            return Picking.Pick(o, d, scene.Mesh, scene.Sprites, Camera.Right);
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
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pt = e.GetCurrentPoint(this);
            var p = pt.Position;
            var props = pt.Properties;
            bool any = props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed;
            if (any)
            {
                var d = p - lastAt;
                if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 4) dragging = true;
                if (dragging)
                {
                    if (panning) Camera.Pan((float)d.X, (float)d.Y, (float)Bounds.Height);
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

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            if (!dragging && leftDown && e.InitialPressMouseButton == MouseButton.Left && !panning)
                Picked?.Invoke(PickAt(e.GetPosition(this)));
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
            Camera.Zoom((float)Math.Pow(0.88, e.Delta.Y));
            CameraMoved();
            e.Handled = true;
        }

        // ---- keys

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
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
