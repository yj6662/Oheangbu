// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // The small burst at the impact point of a single shot (SPEC-SPELL-120-308 WP-03, handler single.blast).
    // It happens at the moment the primary lands on its target and only then: a shot that lands on nothing bursts nothing
    // (Spec section 15 default). The primary's own target is never hit by the burst.
    public static class ImpactBlastRule308
    {
        // primaryId = the actor the primary landed on (below 0 or out of range = no impact = no burst).
        // impactPoint = where that actor stands at the impact, impactAt = the moment of the impact.
        public static int Plan(SpellActorSnap[] atCast, SpellActorSnap[] now, int primaryId, Vector3 impactPoint, float impactAt,
            float power, float radius, Element element, List<SpellHitOrder> orders)
        {
            if (atCast == null || primaryId < 0 || primaryId >= atCast.Length) return 0;
            return TwoStageRule308.Circle(atCast, now, impactPoint, radius, primaryId, impactAt, power, element, orders);
        }
    }
}
