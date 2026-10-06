// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    // The tide that comes back (SPEC-SPELL-120-308 WP-03, handler path.tide). The primary is the path judgement of the
    // base glyph: a wave front that leaves the caster at outboundStartsAt and runs to the far end of the corridor. The
    // second stage is the returning front: it waits `pause` seconds at the far end and runs back to the start. One enemy is
    // hit at most twice by one cast: once on the way out (the wiring's scheduled hit), once on the way back
    // (PathFrontRule308.Sweep, at most one hit per enemy).
    public static class TideRule308
    {
        // When the outbound front reaches the far end.
        public static float TurnAt(float outboundStartsAt, float length, float speed) => outboundStartsAt + length / speed;

        // speedScale = the returning speed as a multiple of the outbound speed.
        public static PathFront308 Return(Vector3 start, Vector3 direction, float halfWidth, float length, float speed,
            float outboundStartsAt, float pause, float speedScale)
            => new PathFront308(start, direction, halfWidth, length, speed * speedScale,
                TurnAt(outboundStartsAt, length, speed) + Mathf.Max(0f, pause), true);
    }
}
