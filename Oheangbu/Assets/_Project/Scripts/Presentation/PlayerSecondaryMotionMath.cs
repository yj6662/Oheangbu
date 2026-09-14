using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>Fixed-step, damped angular spring. Angles and velocities use radians.</summary>
    public static class PlayerSecondaryMotionMath
    {
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

        public static void Step(ref Vector3 angle, ref Vector3 velocity, Vector3 angularAcceleration,
            float frequencyHz, float dampingRatio, float maximumRadians, float seconds)
        {
            if (seconds <= 0f) return;
            float omega = Mathf.Max(.1f, frequencyHz) * (2f * Mathf.PI);
            // Implicit damping avoids injecting energy at the high-frequency end of the profile range.
            velocity = (velocity + (angularAcceleration - angle * (omega * omega)) * seconds)
                / (1f + 2f * Mathf.Max(0f, dampingRatio) * omega * seconds);
            angle += velocity * seconds;
            float limit = Mathf.Max(0f, maximumRadians);
            float squared = angle.sqrMagnitude;
            if (squared > limit * limit)
            {
                Vector3 normal = angle / Mathf.Sqrt(squared);
                angle = normal * limit;
                velocity -= normal * Mathf.Max(0f, Vector3.Dot(velocity, normal));
            }
            if (!Finite(angle) || !Finite(velocity)) { angle = Vector3.zero; velocity = Vector3.zero; }
        }

        public static Quaternion Rotation(Vector3 radians)
        {
            float magnitude = radians.magnitude;
            return magnitude < .000001f ? Quaternion.identity : Quaternion.AngleAxis(magnitude * Mathf.Rad2Deg, radians / magnitude);
        }

        public static Vector3 RotationDelta(Quaternion before, Quaternion after)
        {
            Quaternion delta = after * Quaternion.Inverse(before);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            return Finite(axis) && Finite(degrees) ? axis * (degrees * Mathf.Deg2Rad) : Vector3.zero;
        }
    }
}
