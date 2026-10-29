// PURE308
using UnityEngine;

namespace Oheangbu.App.World
{
    // D308-148 (앞잡 = 일필휘지 한 획, step 5): the clock rules of the one stroke, as pure functions of "seconds since the fire" and
    // small state classes, so the pure runner can replay them (Cases_RiposteStroke308) and the Unity half cannot drift from them.
    // Every number is TEST (the brief's table; look and drying times are judged in the mp4 pass).
    public struct RiposteStroke308Timing
    {
        public float PrepSeconds, RevealSeconds, RevealPower;
        public float LookRise, LookHold, LookReturn;
        public float DryStart, CrackEnd, FallEnd;

        public static RiposteStroke308Timing Default => new RiposteStroke308Timing
        {
            PrepSeconds = .05f, RevealSeconds = .15f, RevealPower = 1.5f,
            LookRise = .04f, LookHold = .08f, LookReturn = .45f,
            DryStart = .60f, CrackEnd = .85f, FallEnd = 1.20f,
        };
    }

    public static class RiposteStroke308Timeline
    {
        /// <summary>How far the brush has written (0..1 of the arc length) t seconds after the fire: nothing during the prep, then
        /// an accelerating sweep (RevealPower > 1 starts slow and ends fast, like a swing).</summary>
        public static float Reveal(in RiposteStroke308Timing k, float t)
        {
            float x = Mathf.Clamp01((t - k.PrepSeconds) / Mathf.Max(1e-4f, k.RevealSeconds));
            return Mathf.Pow(x, Mathf.Max(.2f, k.RevealPower));
        }

        /// <summary>The time at which Reveal reaches u (the inverse of Reveal; used to predict the pierce time before it happens).</summary>
        public static float TimeAtReveal(in RiposteStroke308Timing k, float u)
        {
            float x = Mathf.Pow(Mathf.Clamp01(u), 1f / Mathf.Max(.2f, k.RevealPower));
            return k.PrepSeconds + x * k.RevealSeconds;
        }

        /// <summary>The colour-drain weight of the riposte's own volume (0..1): rises over LookRise to the pierce, holds LookHold, returns
        /// to nothing LookReturn after the pierce. impactT = the predicted pierce time, then the real one.</summary>
        public static float LookWeight(in RiposteStroke308Timing k, float t, float impactT)
        {
            float rise = Mathf.Max(1e-3f, k.LookRise);
            if (t <= impactT - rise) return 0f;
            if (t < impactT) return Mathf.SmoothStep(0f, 1f, (t - (impactT - rise)) / rise);
            float hold = Mathf.Max(0f, k.LookHold), ret = Mathf.Max(hold + 1e-3f, k.LookReturn);
            if (t < impactT + hold) return 1f;
            if (t < impactT + ret) return 1f - Mathf.SmoothStep(0f, 1f, (t - impactT - hold) / (ret - hold));
            return 0f;
        }

        /// <summary>Drying 0..1 (the crack threshold of the shader): 0 until DryStart, the cracks open to .7 by CrackEnd, the flakes
        /// fall away to 1 at FallEnd. Never a fade: the shader cuts it out through the crack field.</summary>
        public static float Dry(in RiposteStroke308Timing k, float t)
        {
            if (t <= k.DryStart) return 0f;
            if (t < k.CrackEnd) return .7f * Mathf.SmoothStep(0f, 1f, (t - k.DryStart) / Mathf.Max(1e-3f, k.CrackEnd - k.DryStart));
            if (t < k.FallEnd) return .7f + .3f * Mathf.SmoothStep(0f, 1f, (t - k.CrackEnd) / Mathf.Max(1e-3f, k.FallEnd - k.CrackEnd));
            return 1f;
        }

        /// <summary>The brush arm's pose weight: up over raise seconds from the fire, down over lower seconds starting hold seconds after the
        /// pierce (impactT = the predicted pierce time until the real one).</summary>
        public static float Pose(float t, float impactT, float raise, float hold, float lower)
        {
            float up = Mathf.Clamp01(t / Mathf.Max(.01f, raise));
            float down = 1f - Mathf.Clamp01((t - impactT - hold) / Mathf.Max(.01f, lower));
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(up, down));
        }

        /// <summary>Deterministic 0..1 from a seed and a lane (flake / speck scatter without a System.Random allocation).</summary>
        public static float Hash01(uint seed, int lane)
        {
            unchecked
            {
                uint h = seed * 747796405u + (uint)lane * 2891336453u + 2891336453u;
                h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
                h = (h >> 22) ^ h;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }
    }

    /// <summary>The single hit of the stroke. Level-triggered, so a frame so long that the head skips over the torso (or a retarget that moves
    /// the torso mark behind the head) still hits - and a latch, so it hits exactly once. The damage amount is the stroke's total (45),
    /// handed out once; the weak-point window's 1.5 is EnemyVitals' alone and is never multiplied here.</summary>
    public sealed class RiposteStroke308Impact
    {
        public bool Fired { get; private set; }
        public float FiredAtU { get; private set; } = -1f;

        bool _taken;

        public void Reset() { Fired = false; FiredAtU = -1f; _taken = false; }

        /// <summary>true exactly once: the first time the revealed head has reached the torso mark.</summary>
        public bool Advance(float headU, float impactU)
        {
            if (Fired || headU < impactU) return false;
            Fired = true; FiredAtU = headU;
            return true;
        }

        /// <summary>The one hit's damage: total on the first call after the pierce, 0 on any other.</summary>
        public float TakeDamage(float total)
        {
            if (!Fired || _taken) return 0f;
            _taken = true;
            return Mathf.Max(0f, total);
        }
    }

    /// <summary>The short hit stop at the pierce, kept from the volley with its two faults mended: the restored value is the one that was
    /// saved (not Max(1, saved), which overwrote another system's slow-motion), and a pause by a menu (scale 0 while the stop's own scale
    /// is above 0) holds the countdown. If another system changed the scale meanwhile the stop lets go without touching it.</summary>
    public sealed class RiposteStroke308HitStop
    {
        public float Left { get; private set; }
        public float Saved { get; private set; } = 1f;
        public float Set { get; private set; }
        public bool Running => Left > 0f;

        /// <summary>Asks for a stop. true = the caller writes setScale to Time.timeScale. Not while one runs, not from a paused (0) scale.</summary>
        public bool Begin(float currentScale, float stopScale, float seconds, out float setScale)
        {
            setScale = currentScale;
            if (Left > 0f || currentScale <= 0f || seconds <= 0f) return false;
            Saved = currentScale; Set = Mathf.Min(Mathf.Max(0f, stopScale), currentScale); Left = seconds;
            setScale = Set;
            return true;
        }

        /// <summary>Counts the stop down in real seconds. true = the stop ended and restore is the scale to write back.</summary>
        public bool Tick(float realSeconds, float currentScale, out float restore)
        {
            restore = currentScale;
            if (Left <= 0f) return false;
            if (currentScale <= 0f && Set > 0f) return false;                          // a menu paused the game: wait
            if (Mathf.Abs(currentScale - Set) > 1e-4f) { Left = 0f; return false; }    // someone else owns the scale now: let go
            Left -= Mathf.Max(0f, realSeconds);
            if (Left > 0f) return false;
            Left = 0f; restore = Saved; return true;
        }

        /// <summary>Disable / cancel: true = the scale is still the stop's own and restore is the saved one.</summary>
        public bool Abort(float currentScale, out float restore)
        {
            restore = currentScale;
            bool mine = Left > 0f && Mathf.Abs(currentScale - Set) <= 1e-4f;
            Left = 0f;
            if (!mine) return false;
            restore = Saved; return true;
        }
    }
}
