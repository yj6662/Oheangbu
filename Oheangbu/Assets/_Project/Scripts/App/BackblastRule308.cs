// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // The burst at the far end of a cone (SPEC-SPELL-120-308 WP-03, handler cone.backblast). Fixed at cast: where and when.
    // Decided at the burst: who (TwoStageRule308.Circle on the enemies as they are then).
    public readonly struct BackblastPlan308
    {
        public readonly Vector3 Centre;
        public readonly float Radius, At, Power;
        public readonly Element Element;

        public BackblastPlan308(Vector3 centre, float radius, float at, float power, Element element)
        { Centre = centre; Radius = radius; At = at; Power = power; Element = element; }
    }

    public static class BackblastRule308
    {
        // The far end of the cone's axis: `range` along the flat forward from the cone's origin.
        public static Vector3 EndPoint(Vector3 origin, Vector3 forward, float range)
        {
            Vector3 flat = AreaGeometry.Flat(forward);
            flat = flat.sqrMagnitude > .0001f ? flat.normalized : Vector3.forward;
            return origin + flat * Mathf.Max(0f, range);
        }

        // primaryAt = when the cone's own judgement lands. The burst follows `delay` later with castPower x powerScale.
        public static BackblastPlan308 Plan(Vector3 origin, Vector3 forward, float range, float primaryAt, float castPower,
            float radius, float delay, float powerScale, Element element)
            => new BackblastPlan308(EndPoint(origin, forward, range), radius, primaryAt + Mathf.Max(0f, delay), castPower * powerScale, element);

        // An enemy the cone also hit is hit again here when it still stands in the burst (two stages); one outside the cone
        // but inside the burst gets the burst alone.
        public static int Resolve(in BackblastPlan308 plan, SpellActorSnap[] atCast, SpellActorSnap[] now, List<SpellHitOrder> orders)
            => TwoStageRule308.Circle(atCast, now, plan.Centre, plan.Radius, -1, plan.At, plan.Power, plan.Element, orders);
    }
}
