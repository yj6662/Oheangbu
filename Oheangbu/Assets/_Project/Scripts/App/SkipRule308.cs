// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // The second landing of a skipping stone (SPEC-SPELL-120-308 WP-03, handler single.skip). After the primary lands on
    // its target the shot goes on in the direction it travelled and strikes a second point. Fixed at the impact: where and
    // when. Decided at the second landing: who (TwoStageRule308.Circle on the enemies as they are then).
    public readonly struct SkipPlan308
    {
        public readonly Vector3 Point;
        public readonly float Radius, At, Power;
        public readonly Element Element;

        public SkipPlan308(Vector3 point, float radius, float at, float power, Element element)
        { Point = point; Radius = radius; At = at; Power = power; Element = element; }
    }

    public static class SkipRule308
    {
        // The flat direction the shot travelled: from where the caster stood to the impact point. A shot that landed on the
        // caster's own spot keeps the caster's facing.
        public static Vector3 Direction(Vector3 castOrigin, Vector3 castForward, Vector3 impactPoint)
        {
            Vector3 travel = AreaGeometry.Flat(impactPoint - castOrigin);
            if (travel.sqrMagnitude > .0001f) return travel.normalized;
            Vector3 forward = AreaGeometry.Flat(castForward);
            return forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
        }

        // `distance` further along that direction, at the height of the impact point.
        public static Vector3 SecondPoint(Vector3 castOrigin, Vector3 castForward, Vector3 impactPoint, float distance)
            => impactPoint + Direction(castOrigin, castForward, impactPoint) * Mathf.Max(0f, distance);

        public static SkipPlan308 Plan(Vector3 castOrigin, Vector3 castForward, Vector3 impactPoint, float impactAt, float castPower,
            float distance, float delay, float radius, float powerScale, Element element)
            => new SkipPlan308(SecondPoint(castOrigin, castForward, impactPoint, distance), radius, impactAt + Mathf.Max(0f, delay),
                castPower * powerScale, element);

        // Everyone of the cast's life inside the radius of the second point, once. Nobody is exempt: the primary's target
        // is hit again only if it stands there.
        public static int Resolve(in SkipPlan308 plan, SpellActorSnap[] atCast, SpellActorSnap[] now, List<SpellHitOrder> orders)
            => TwoStageRule308.Circle(atCast, now, plan.Point, plan.Radius, -1, plan.At, plan.Power, plan.Element, orders);
    }
}
