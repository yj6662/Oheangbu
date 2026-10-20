// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-17 (D308-121 결계): the arithmetic the domain handlers share, apart from Unity objects so the offline runner
    // checks it. Flat geometry (x, z); heights are kept.
    public static class DomainRule308
    {
        // The value for this enemy: bosses take their own (D308-124: a binding becomes a slow on a boss).
        public static float Pick(bool boss, float normal, float bossValue) => boss ? bossValue : normal;

        // An enemy crossed the edge INTO the domain this frame: it was seen outside before and is inside now. An enemy first
        // seen inside (it stood there when the domain came) never counts as crossing.
        public static bool Entered(bool seenBefore, bool wasInside, bool inside) => seenBefore && !wasInside && inside;

        // Where an enemy pushed out of the circle ends: on the edge plus margin, straight away from the centre. An enemy on
        // the centre itself goes along `fallback` (the caster's forward). Height kept.
        public static Vector3 PushOut(Vector3 centre, Vector3 position, float radius, float margin, Vector3 fallback)
        {
            float reach = Mathf.Max(0f, radius) + Mathf.Max(0f, margin);
            float dx = position.x - centre.x, dz = position.z - centre.z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length >= reach) return position;                          // already outside: not moved
            if (length < 1e-4f)
            {
                dx = fallback.x; dz = fallback.z;
                length = Mathf.Sqrt(dx * dx + dz * dz);
                if (length < 1e-4f) { dx = 0f; dz = 1f; length = 1f; }
            }
            return new Vector3(centre.x + dx / length * reach, position.y, centre.z + dz / length * reach);
        }

        // One frame of the slow pull toward the centre (뭉). speed = metres per second (at most), budget = metres this enemy
        // may still be moved by this cast (it never refills). The step stops `core` metres short of the centre. Returns the
        // flat step; used = its length.
        public static Vector3 PullStep(Vector3 centre, Vector3 position, float speed, float dt, float budget, float core, out float used)
        {
            used = 0f;
            if (!(speed > 0f) || !(dt > 0f) || !(budget > 0f)) return Vector3.zero;
            float dx = centre.x - position.x, dz = centre.z - position.z;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            float room = length - Mathf.Max(0f, core);
            if (!(room > 0f)) return Vector3.zero;
            used = Mathf.Min(Mathf.Min(speed * dt, budget), room);
            return new Vector3(dx / length * used, 0f, dz / length * used);
        }
    }
}
