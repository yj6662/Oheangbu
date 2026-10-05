// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    // The stone spine that rises behind a storm (SPEC-SPELL-120-308 WP-03, handler path.stalagmite). The primary is the
    // path judgement of the base glyph: a storm front that leaves the caster at stormStartsAt and runs the corridor. The
    // second stage is a second front on the same corridor at the same speed, `delay` seconds behind the storm front, so a
    // standing enemy takes its second hit `delay` seconds after its first. Who is hit is decided as the second front
    // reaches each enemy (PathFrontRule308.Sweep).
    public static class StalagmiteRule308
    {
        public static PathFront308 Front(Vector3 start, Vector3 direction, float halfWidth, float length, float speed,
            float stormStartsAt, float delay)
            => new PathFront308(start, direction, halfWidth, length, speed, stormStartsAt + Mathf.Max(0f, delay), false);
    }
}
