using UnityEngine;

namespace Oheangbu.Presentation
{
    /// <summary>
    /// #308 player juice (SPEC-ANIM-JUICE-308 section 1): the closed forms. Every curve is a function of the time since its
    /// event - there is no integrated state, so a curve is exactly 0 (or exactly its rest value) once its length has passed
    /// and its size does not depend on the frame rate. Pure: Mathf only, no Time, no scene.
    /// Reference implementation: Tools/Unity/Stage308_juice/checks/juice_curves.py (Juice308Build check-curves compares).
    /// The .7 / .3 window of Ring / Settle and the polynomial eases are part of the forms, not tuning values.
    /// </summary>
    public static class PlayerJuice308Curves
    {
        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        public static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (6f * x - 15f) + 10f); }
        public static float EaseOutQuad(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
        public static float EaseOutCubic(float x) { x = Mathf.Clamp01(x); float k = 1f - x; return 1f - k * k * k; }

        /// <summary>0 -> 1 over attack (ease-out cubic) -> 0 over release (smootherstep). Exactly 0 outside (0, attack + release).</summary>
        public static float Pulse(float t, float attack, float release)
        {
            if (!(t > 0f) || !(attack > 0f) || !(release > 0f) || t >= attack + release) return 0f;
            if (t < attack) return EaseOutCubic(t / attack);
            return 1f - Smoother((t - attack) / release);
        }

        /// <summary>Impulse response of a damped spring, scaled so its first peak is 1; the last 30 % of the length closes a
        /// window, so the value is exactly 0 from `duration` on.</summary>
        public static float Ring(float t, float hz, float zeta, float duration)
        {
            if (!(t > 0f) || !(duration > 0f) || t >= duration || !(hz > 0f)) return 0f;
            zeta = Mathf.Clamp(zeta, 0f, .999f);
            float w = 2f * Mathf.PI * hz, wd = w * Mathf.Sqrt(Mathf.Max(1e-6f, 1f - zeta * zeta));
            float peakTime = Mathf.Atan2(wd, zeta * w) / wd;
            float peak = Mathf.Exp(-zeta * w * peakTime) * Mathf.Sin(wd * peakTime);
            if (!(peak > 1e-6f)) return 0f;
            float raw = Mathf.Exp(-zeta * w * t) * Mathf.Sin(wd * t);
            return raw / peak * (1f - Smooth((t - .7f * duration) / (.3f * duration)));
        }

        /// <summary>Seconds from the event to the first peak of Ring (the window does not move it while it lies before 70 %).</summary>
        public static float RingPeakTime(float hz, float zeta)
        {
            if (!(hz > 0f)) return 0f;
            zeta = Mathf.Clamp(zeta, 0f, .999f);
            float w = 2f * Mathf.PI * hz, wd = w * Mathf.Sqrt(Mathf.Max(1e-6f, 1f - zeta * zeta));
            return Mathf.Atan2(wd, zeta * w) / wd;
        }

        /// <summary>Step response 0 -> 1 with overshoot; exactly 1 from `duration` on.</summary>
        public static float Settle(float t, float hz, float zeta, float duration)
        {
            if (!(t > 0f)) return 0f;
            if (!(duration > 0f) || t >= duration || !(hz > 0f)) return 1f;
            zeta = Mathf.Clamp(zeta, 0f, .999f);
            float w = 2f * Mathf.PI * hz, wd = w * Mathf.Sqrt(Mathf.Max(1e-6f, 1f - zeta * zeta));
            float r = 1f - Mathf.Exp(-zeta * w * t) * (Mathf.Cos(wd * t) + zeta * w / wd * Mathf.Sin(wd * t));
            float k = Smooth((t - .7f * duration) / (.3f * duration));
            return r + (1f - r) * k;
        }

        /// <summary>Low frame rates: a ring is slowed to fps / 5 so one period keeps at least five frames (section 1).</summary>
        public static float RingHz(float hz, float frameSeconds, float minimumFrameRate)
        {
            if (!(frameSeconds > 0f)) return hz;
            float fps = 1f / frameSeconds;
            return fps < minimumFrameRate ? Mathf.Min(hz, fps / 5f) : hz;
        }

        // ---------------------------------------------------------------- B-2 throw

        /// <summary>B-2: where the tip is on the path last pointer (0) -> launch point (1) at `t` seconds after the commit:
        /// a short pull back, then the throw.</summary>
        public static float CastAlong(float t, float cock, float flick, float back)
        {
            if (!(t > 0f)) return 0f;
            if (t < cock) return -back * EaseOutQuad(t / Mathf.Max(1e-5f, cock));
            return -back + (1f + back) * EaseOutCubic((t - cock) / Mathf.Max(1e-5f, flick));
        }

        /// <summary>B-2: the throw past the launch point as a share of the tier's overshoot: 0 -> 1 with the throw, then it
        /// comes back by `rebound` while the hand holds (the hold plus half a throw).</summary>
        public static float CastOver(float t, float cock, float flick, float hold, float rebound)
        {
            if (!(t > cock)) return 0f;
            float s = EaseOutCubic((t - cock) / Mathf.Max(1e-5f, flick));
            float u = t - cock - flick;
            return u <= 0f ? s : 1f - rebound * Smooth(u / Mathf.Max(1e-5f, hold + .5f * flick));
        }

        /// <summary>B-2: 0 -> 1 lowering of arm and brush after cock + throw + hold.</summary>
        public static float CastDrop(float t, float cock, float flick, float hold, float drop)
            => Smooth((t - cock - flick - hold) / Mathf.Max(1e-5f, drop));

        // ---------------------------------------------------------------- C-1 call stroke

        /// <summary>C-1: overshoot past the stroke's end, as a share of the amplitude, `seconds` after the call moment:
        /// 0 -> 1 (rise) -> settles back to exactly 0 at rise + length.</summary>
        public static float AirOver(float seconds, float rise, float hz, float zeta, float length)
        {
            if (!(seconds > 0f)) return 0f;
            if (seconds < rise) return EaseOutCubic(seconds / Mathf.Max(1e-5f, rise));
            return 1f - Settle(seconds - rise, hz, zeta, length);
        }

        /// <summary>C-1: the arm's weight over the call progress: up with the raise, held extended until holdTo, then down
        /// (the end stays at progress 1).</summary>
        public static float AirWeight(float progress, float raiseEnd, float holdTo)
            => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / Mathf.Max(1e-5f, raiseEnd)))
               * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(holdTo, 1f, progress)));

        // ---------------------------------------------------------------- C-4 car

        /// <summary>C-4: vertical offset of the car's visual body (m): 0 -> -drop (fall) -> back to exactly 0 at fall + length.</summary>
        public static float CarSettle(float t, float drop, float fall, float hz, float zeta, float length)
        {
            if (!(t > 0f)) return 0f;
            if (t < fall) return -drop * EaseOutQuad(t / Mathf.Max(1e-5f, fall));
            return -drop * (1f - Settle(t - fall, hz, zeta, length));
        }

        /// <summary>C-4 recall: the visual body lifts and comes back; exactly 0 from `seconds` on.</summary>
        public static float CarLift(float t, float lift, float seconds) => lift * Pulse(t, .35f * seconds, .65f * seconds);

        // ---------------------------------------------------------------- D camera

        /// <summary>D-2b: distance falloff of the set-down thump (1 up to full, 0 from zero on).</summary>
        public static float DistanceScale(float distance, float full, float zero)
            => zero <= full ? (distance <= full ? 1f : 0f) : 1f - Smooth((distance - full) / (zero - full));

        /// <summary>D: the summed camera reaction is clamped after the sum (pitch and roll symmetric, the field of view too).</summary>
        public static Vector3 ClampCamera(Vector3 pitchRollFov, float maxPitch, float maxRoll, float maxFov)
            => new Vector3(Mathf.Clamp(pitchRollFov.x, -maxPitch, maxPitch), Mathf.Clamp(pitchRollFov.y, -maxRoll, maxRoll),
                Mathf.Clamp(pitchRollFov.z, -maxFov, maxFov));
    }
}
