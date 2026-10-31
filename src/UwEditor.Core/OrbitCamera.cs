using System;
using System.Numerics;

namespace UwEditor.Core
{
    /// <summary>
    /// A camera turning round a target point. World coordinates are the level's: x east,
    /// y up, z north, in tiles. That is a left-handed set, so the view matrix mirrors z
    /// before the usual right-handed look-at; nothing else needs to know.
    /// </summary>
    public sealed class OrbitCamera
    {
        public Vector3 Target = new Vector3(32, 0, 32);
        /// <summary>Which way the camera looks, clockwise from north, in degrees.</summary>
        public float Yaw;
        /// <summary>How far it looks down, in degrees (0 level, 89 straight down).</summary>
        public float Pitch = 45;
        public float Distance = 8;
        public float FieldOfView = 60;
        public const float MinDistance = 0.5f, MaxDistance = 120f;
        /// <summary>How far up (negative) the camera may look; first person looks further up.</summary>
        public float MinPitch = -10;

        public Vector3 Forward
        {
            get
            {
                double y = Yaw * Math.PI / 180, p = Pitch * Math.PI / 180;
                return new Vector3((float)(Math.Sin(y) * Math.Cos(p)), (float)-Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p)));
            }
        }

        /// <summary>The horizontal direction to the camera's right.</summary>
        public Vector3 Right
        {
            get
            {
                double y = Yaw * Math.PI / 180;
                return new Vector3((float)Math.Cos(y), 0, (float)-Math.Sin(y));
            }
        }

        /// <summary>The screen's up direction in the world (perpendicular to the view).</summary>
        public Vector3 Up { get { return Vector3.Normalize(Vector3.Cross(Forward, Right)); } }

        public Vector3 Eye { get { return Target - Forward * Distance; } }

        static Vector3 Mirror(Vector3 v) { return new Vector3(v.X, v.Y, -v.Z); }

        public Matrix4x4 View
        {
            get
            {
                var look = Matrix4x4.CreateLookAt(Mirror(Eye), Mirror(Target), Vector3.UnitY);
                return Matrix4x4.CreateScale(1, 1, -1) * look;
            }
        }

        /// <summary>An OpenGL projection (depth -1..1).</summary>
        public Matrix4x4 Projection(float aspect)
        {
            float near = 0.05f, far = 400f;
            float f = 1f / (float)Math.Tan(FieldOfView * Math.PI / 360);
            var m = new Matrix4x4();
            m.M11 = f / Math.Max(0.01f, aspect);
            m.M22 = f;
            m.M33 = (far + near) / (near - far);
            m.M34 = -1;
            m.M43 = 2 * far * near / (near - far);
            return m;
        }

        public void Orbit(float dYaw, float dPitch)
        {
            Yaw = (Yaw + dYaw) % 360;
            if (Yaw < 0) Yaw += 360;
            Pitch = Math.Clamp(Pitch + dPitch, MinPitch, 89);
        }

        public void Zoom(float factor)
        {
            Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);
        }

        /// <summary>Moves the target in the view's plane by pixels (the view is height pixels high).</summary>
        public void Pan(float dxPixels, float dyPixels, float viewHeight)
        {
            float unitsPerPixel = 2f * Distance * (float)Math.Tan(FieldOfView * Math.PI / 360) / Math.Max(1, viewHeight);
            var up = Vector3.Cross(Forward, Right);   // the world is left-handed
            Target += (-Right * dxPixels + up * dyPixels) * unitsPerPixel;
        }

        /// <summary>Moves the target along the ground (forward / right in tiles).</summary>
        public void Move(float forward, float right)
        {
            var f = new Vector3(Forward.X, 0, Forward.Z);
            if (f.LengthSquared() < 1e-6f) f = new Vector3((float)Math.Sin(Yaw * Math.PI / 180), 0, (float)Math.Cos(Yaw * Math.PI / 180));
            f = Vector3.Normalize(f);
            Target += f * forward + Right * right;
        }

        /// <summary>The ray under a pixel (x right, y down, in a view of width × height).</summary>
        public void Ray(float px, float py, float width, float height, out Vector3 origin, out Vector3 dir)
        {
            float nx = 2 * px / Math.Max(1, width) - 1, ny = 1 - 2 * py / Math.Max(1, height);
            var vp = View * Projection(width / Math.Max(1, height));
            Matrix4x4 inv;
            Matrix4x4.Invert(vp, out inv);
            var a = Vector4.Transform(new Vector4(nx, ny, -1, 1), inv);
            var b = Vector4.Transform(new Vector4(nx, ny, 1, 1), inv);
            var pa = new Vector3(a.X, a.Y, a.Z) / a.W;
            var pb = new Vector3(b.X, b.Y, b.Z) / b.W;
            origin = pa;
            dir = Vector3.Normalize(pb - pa);
        }
    }
}
