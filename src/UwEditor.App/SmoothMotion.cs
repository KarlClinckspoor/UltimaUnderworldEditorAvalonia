using System;
using System.Numerics;

namespace UwEditor.App
{
    /// <summary>
    /// The camera's movement while keys are held: the velocity follows the wanted one with a short
    /// ease (a time constant of Ease seconds), so a held key glides from a standstill to full speed
    /// and stops the same way, and the distance moved depends on time, not on the keyboard's repeat.
    /// The four values are forward and right (tiles a second), turn and look (degrees a second).
    /// </summary>
    public sealed class SmoothMotion
    {
        /// <summary>How quickly the speed follows the keys: about two thirds of the change in this many seconds.</summary>
        public const double Ease = 0.08;
        /// <summary>Below this the movement has stopped.</summary>
        public const float Rest = 0.02f;

        public Vector4 Velocity { get; private set; }

        public bool Moving { get { return Math.Abs(Velocity.X) + Math.Abs(Velocity.Y) + Math.Abs(Velocity.Z) + Math.Abs(Velocity.W) > Rest; } }

        public void Stop() { Velocity = Vector4.Zero; }

        /// <summary>Moves the velocity towards wanted for dt seconds; returns how far it went (the integral of the velocity over dt, exactly for the ease).</summary>
        public Vector4 Step(Vector4 wanted, double dt)
        {
            if (dt <= 0) return Vector4.Zero;
            dt = Math.Min(dt, 0.25);                       // a stalled frame does not jump
            var v0 = Velocity;
            double k = Math.Exp(-dt / Ease);
            // v(t) = wanted + (v0 - wanted) e^(-t/Ease): its integral over dt
            var moved = wanted * (float)dt + (v0 - wanted) * (float)(Ease * (1 - k));
            Velocity = wanted + (v0 - wanted) * (float)k;
            if (wanted == Vector4.Zero && !Moving) Velocity = Vector4.Zero;
            return moved;
        }
    }
}
