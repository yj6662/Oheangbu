// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // A volley shot that hit bounces to another enemy (SPEC-SPELL-120-308 WP-04, handler volley.ricochet).
    // Only a confirmed hit bounces; the effect class asks this rule once per confirmed hit of a shot or of one of its bounces.
    public static class RicochetRule308
    {
        // depth = which bounce of the shot this would be (1 = the first). visited = every enemy the shot has touched so far
        // (the one it hit first and the ones its bounces hit): a shot never returns to any of them. The candidates are the
        // living enemies inside range of the point the shot bounces from, nearest first; they are dealt round-robin by the
        // shot's ordinal inside its volley, so a volley that lands on one enemy spreads over its neighbours instead of
        // sending every bounce to the same one. -1 = no bounce (the shot's bounces are used up, or nobody is in reach).
        public static int Next(SpellActorSnap[] actors, Vector3 from, IReadOnlyList<int> visited, int shot, int depth, int maxBounces,
            float range, List<int> scratch)
        {
            if (depth < 1 || depth > maxBounces) return -1;
            DerivedHitRule308.Near(actors, from, range, visited, scratch);
            if (scratch.Count == 0) return -1;
            int turn = shot % scratch.Count;
            return scratch[turn < 0 ? turn + scratch.Count : turn];
        }

        // The order of that bounce: it lands after its own flight and carries the shot's power scaled once per bounce.
        public static bool Bounce(SpellActorSnap[] actors, Vector3 from, IReadOnlyList<int> visited, int shot, int depth, int maxBounces,
            float range, float speed, float now, float shotPower, float bouncePower, Element element, List<int> scratch, out SpellHitOrder order)
        {
            order = default;
            float power = Power(shotPower, bouncePower, depth);
            if (float.IsNaN(power) || power <= 0f) return false;
            int next = Next(actors, from, visited, shot, depth, maxBounces, range, scratch);
            if (next < 0 || !DerivedHitRule308.TryFind(actors, next, out var actor)) return false;
            order = new SpellHitOrder(next, now + DerivedHitRule308.Flight(from, actor.Position, speed), power, element);
            return true;
        }

        // Power a shot carries on its depth-th bounce: the scale applies once per bounce.
        public static float Power(float shotPower, float bouncePower, int depth)
        {
            float power = shotPower;
            for (int i = 0; i < depth; i++) power *= bouncePower;
            return power;
        }
    }
}
