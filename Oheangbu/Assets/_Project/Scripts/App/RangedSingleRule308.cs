// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    // The range of a straight single shot (SPEC-SPELL-120-308 WP-11, handler single.ranged). The shot flies straight at the
    // enemy it was aimed at and dies at the end of its range: a target beyond the range at cast time is not hit. The
    // distance is the one the wiring's single judgement uses for the flight time (caster to target, in space).
    public static class RangedSingleRule308
    {
        // A target standing exactly on the range is still reached. A range that is not positive reaches nothing.
        public static bool Reaches(Vector3 caster, Vector3 target, float range)
        {
            return range > 0f && Vector3.Distance(caster, target) <= range;
        }

        // Where the shot dies when its target is out of range: `range` along the line to the target.
        public static Vector3 EndPoint(Vector3 caster, Vector3 target, float range)
        {
            Vector3 to = target - caster;
            float distance = to.magnitude;
            if (!(distance > 0f) || !(range > 0f)) return caster;
            return caster + to * (Mathf.Min(distance, range) / distance);
        }
    }
}
