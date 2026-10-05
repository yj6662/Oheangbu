// PURE308
using System.Collections.Generic;

namespace Oheangbu.App
{
    // "Sharper as it goes" (SPEC-SPELL-120-308 WP-02, handler volley.ramp): inside one volley every later shot carries a
    // larger share of the cast power. The rule gives each shot, in launch order, a weight; the host turns the weights into
    // powers (cast power x weight / sum of the weights), so the shots of one cast always add up to the cast power.
    // Nothing is remembered between casts: the weights depend on the shot count and the two end weights only.
    public static class VolleyRampRule308
    {
        // A ramp exists only with two or more shots and a strictly rising pair of positive, finite end weights.
        // (A NaN fails every comparison, so it is refused here too.)
        public static bool Valid(int shots, float first, float last)
        {
            return shots >= 2 && first > 0f && last > first && !float.IsInfinity(last);
        }

        // One weight per shot, linear from first (shot 0) to last (the final shot). false = no ramp, the list is left empty.
        // The result is strictly rising or it is refused: two end weights too close for the shot count give no ramp.
        public static bool Weights(int shots, float first, float last, List<float> weights)
        {
            weights.Clear();
            if (!Valid(shots, first, last)) return false;
            float step = (last - first) / (shots - 1);
            for (int i = 0; i < shots; i++) weights.Add(i == shots - 1 ? last : first + step * i);
            for (int i = 1; i < shots; i++)
                if (!(weights[i] > weights[i - 1])) { weights.Clear(); return false; }
            return true;
        }

        // The power shot number "shot" carries, exactly as the host computes it from the weights.
        public static float ShotPower(IReadOnlyList<float> weights, int shot, float castPower)
        {
            if (weights == null || shot < 0 || shot >= weights.Count) return 0f;
            float sum = 0f;
            for (int i = 0; i < weights.Count; i++) sum += weights[i];
            return sum > 0f ? castPower * weights[shot] / sum : 0f;
        }
    }
}
