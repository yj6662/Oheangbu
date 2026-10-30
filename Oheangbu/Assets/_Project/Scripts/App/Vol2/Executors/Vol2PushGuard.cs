using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>D308-158 (SPEC-VOL2-EARTH-PUSH-308): shortens an earth push so a body is never shoved through a wall or off the walkable ground. Asked once, when the push is decided
    /// (Vol2CastCoordinator.BodyEffect), so the status sink only has to move the body. One instance per coordinator (its ray buffer is its own - nothing static).
    ///  - Wall: one ray at <see cref="Vol2ShapeTuning.PushWallHeight"/> above the feet along the push, as long as the push plus the body's radius; the first solid that is not an enemy and not
    ///    the caster stops the push in front of it (radius + 2 cm skin).
    ///  - Ground: from the feet outward every <see cref="Vol2ShapeTuning.PushProbeStep"/> metres a ray goes down; the push ends at the last sample that has ground within
    ///    +/- <see cref="Vol2ShapeTuning.PushStepHeight"/> of the sample before it and a surface not steeper than <see cref="Vol2ShapeTuning.PushSlopeNormalY"/> (the same idea as the
    ///    D308-129 shove of the player: the push never leaves ground a body could walk on). NavMesh is NOT the criterion (a plank way is off the enemy mesh, a cliff path is walkable).
    ///    A body with no ground at all under its feet (a fixture without a floor, a flying body) is not judged by the ground - only by the wall.</summary>
    public sealed class Vol2PushGuard
    {
        readonly RaycastHit[] _hits = new RaycastHit[16];
        Transform _ignore;

        /// <summary>The distance the push may really go: <paramref name="meters"/> or less. <paramref name="cut"/> says why it was cut ("wall" / "edge"), empty when it was not.
        /// <paramref name="ignoreRoot"/>: the caster's object (its colliders are no wall and no ground); may be null.</summary>
        public float Clamp(Vector3 feet, Vector3 flatDir, float meters, float radius, Vol2ShapeTuning t, Transform ignoreRoot, out string cut)
        {
            cut = ""; _ignore = ignoreRoot;
            if (!(meters > 0f) || flatDir.sqrMagnitude < 1e-8f) return Mathf.Max(0f, meters);
            flatDir.y = 0f; flatDir.Normalize();
            float allowed = meters;

            // wall
            float reach = meters + Mathf.Max(0f, radius);
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * t.PushWallHeight, flatDir, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            float wall = float.PositiveInfinity;
            for (int i = 0; i < n; i++) if (Solid(_hits[i].collider) && _hits[i].distance < wall) wall = _hits[i].distance;
            if (!float.IsPositiveInfinity(wall))
            {
                float room = Mathf.Max(0f, wall - Mathf.Max(0f, radius) - 0.02f);
                if (room < allowed) { allowed = room; cut = "wall"; }
            }
            if (allowed <= 0f) return 0f;

            // ground
            if (!Ground(feet, feet.y, t, out float groundY)) return allowed;
            float step = Mathf.Max(0.03f, t.PushProbeStep), last = 0f;
            for (float d = step; ; d += step)
            {
                float at = Mathf.Min(d, allowed);
                if (!Ground(feet + flatDir * at, groundY, t, out float y)) { cut = "edge"; return last; }
                groundY = y; last = at;
                if (at >= allowed) return allowed;
            }
        }

        /// <summary>Walkable ground under <paramref name="p"/> within one step height of <paramref name="aroundY"/>.</summary>
        bool Ground(Vector3 p, float aroundY, Vol2ShapeTuning t, out float y)
        {
            y = aroundY;
            float up = t.PushStepHeight + 0.05f;
            var from = new Vector3(p.x, aroundY + up, p.z);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, up + t.PushStepHeight, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; float normalY = 0f;
            for (int i = 0; i < n; i++)
                if (Solid(_hits[i].collider) && _hits[i].distance < best) { best = _hits[i].distance; normalY = _hits[i].normal.y; }
            if (float.IsPositiveInfinity(best) || normalY < t.PushSlopeNormalY) return false;
            y = from.y - best; return true;
        }

        /// <summary>A collider that can be a wall or a floor: not an enemy (a target, not a wall) and not the caster's.</summary>
        bool Solid(Collider c)
        {
            if (c == null) return false;
            if (c.GetComponentInParent<EnemyVitals>() != null) return false;
            return _ignore == null || !c.transform.IsChildOf(_ignore);
        }
    }
}
