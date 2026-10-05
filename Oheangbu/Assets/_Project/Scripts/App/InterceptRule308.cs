// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // One enemy projectile in the air as a rule sees it (SPEC-SPELL-120-308 WP-13). Velocity may be a direction only
    // (its length is not used): the rule asks where the projectile is and whether it comes towards the caster.
    public readonly struct SpellProjectileSnap
    {
        public readonly int Id;
        public readonly Vector3 Position, Velocity;
        public readonly bool Live;

        public SpellProjectileSnap(int id, Vector3 position, Vector3 velocity, bool live)
        { Id = id; Position = position; Velocity = velocity; Live = live; }
    }

    // The interception glyphs (handlers intercept.single and intercept.barrage): a needle, or a whole barrage, shoots enemy
    // projectiles out of the air. Only a projectile that is still in the air, comes towards the caster and is inside the
    // spell's fan (flat half angle and range from the caster, like every cone judgement) can be picked.
    public static class InterceptRule308
    {
        public const int None = -1;

        // In the air and flying towards the caster (a projectile that flies away, or stands still, is not "incoming").
        public static bool Incoming(in SpellProjectileSnap shot, Vector3 caster)
            => shot.Live && Vector3.Dot(AreaGeometry.Flat(caster - shot.Position), AreaGeometry.Flat(shot.Velocity)) > 0f;

        public static bool InFan(in SpellProjectileSnap shot, Vector3 caster, Vector3 forward, float halfAngle, float range, out float distance)
            => AreaGeometry.InCone(caster, forward, shot.Position, halfAngle, range, out _, out distance);

        // The needle: the incoming projectile inside the fan that is nearest to the caster (the most urgent one).
        // A tie goes to the lower id. None = nothing to intercept.
        public static int PickOne(IReadOnlyList<SpellProjectileSnap> shots, Vector3 caster, Vector3 forward, float halfAngle, float range)
        {
            int best = None; float bestDistance = float.PositiveInfinity;
            for (int i = 0; shots != null && i < shots.Count; i++)
            {
                if (!Incoming(shots[i], caster) || !InFan(shots[i], caster, forward, halfAngle, range, out float distance)) continue;
                if (best != None && distance >= bestDistance) continue;
                best = i; bestDistance = distance;
            }
            return best;
        }

        // The barrage: every incoming projectile inside the fan, nearest first, at most max of them. Returns how many.
        public static int PickMany(IReadOnlyList<SpellProjectileSnap> shots, Vector3 caster, Vector3 forward, float halfAngle, float range,
            int max, List<int> picked)
        {
            picked.Clear();
            if (shots == null || max <= 0) return 0;
            var distances = new List<float>();
            for (int i = 0; i < shots.Count; i++)
            {
                if (!Incoming(shots[i], caster) || !InFan(shots[i], caster, forward, halfAngle, range, out float distance)) continue;
                int at = 0;
                while (at < distances.Count && distances[at] <= distance) at++;      // stable: equal distances keep id order
                distances.Insert(at, distance); picked.Insert(at, i);
            }
            if (picked.Count > max) picked.RemoveRange(max, picked.Count - max);
            return picked.Count;
        }

        // Seconds the needle needs to reach a projectile where it is now (speed is already at least 1 in the wiring).
        public static float FlightSeconds(Vector3 caster, Vector3 point, float speed)
            => speed > 0f ? Vector3.Distance(caster, point) / speed : 0f;
    }
}
