// PURE308
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // The unguided rock (SPEC-SPELL-120-308 WP-11, handler single.ballistic). The rock is thrown at the spot its target
    // stands on at cast time and falls there after the flight time the wiring's single judgement would use. It is not
    // guided: the target is hit only if it still stands within the fall radius of that spot when the rock lands
    // ("hard to aim": a target that keeps moving walks out from under it).
    public static class BallisticRule308
    {
        // Seconds from cast to landing: distance / speed, the wiring's own expression (speed is already at least 1).
        public static float FlightSeconds(Vector3 caster, Vector3 fallPoint, float speed)
        {
            return speed > 0f ? Vector3.Distance(caster, fallPoint) / speed : 0f;
        }

        // Does the rock that falls on fallPoint hit an enemy standing at targetNow? Flat distance, like every area judgement.
        public static bool Lands(Vector3 fallPoint, Vector3 targetNow, float fallRadius)
        {
            return fallRadius > 0f && AreaGeometry.InCircle(fallPoint, targetNow, fallRadius, out _);
        }
    }
}
