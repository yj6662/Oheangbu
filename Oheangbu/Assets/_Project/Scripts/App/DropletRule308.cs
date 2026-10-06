// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // A confirmed hit breaks into droplets, each chasing a different enemy near the point of impact
    // (SPEC-SPELL-120-308 WP-04, handler hit.droplets).
    public static class DropletRule308
    {
        // Up to count droplets. Each goes to a different living enemy inside radius of the impact point, nearest first; the
        // struck enemy is never a droplet's target. Fewer enemies than droplets = fewer droplets (a droplet with nobody to
        // chase is lost). A droplet lands after its own flight: distance / speed.
        public static void Plan(SpellActorSnap[] actors, int primaryId, Vector3 point, int count, float radius, float speed,
            float now, float power, Element element, List<int> scratch, List<SpellHitOrder> orders)
        {
            if (actors == null || count <= 0 || float.IsNaN(power) || power <= 0f) return;
            DerivedHitRule308.Near(actors, point, radius, new[] { primaryId }, scratch);
            for (int i = 0; i < scratch.Count && i < count; i++)
            {
                if (!DerivedHitRule308.TryFind(actors, scratch[i], out var actor)) continue;
                orders.Add(new SpellHitOrder(actor.Id, now + DerivedHitRule308.Flight(point, actor.Position, speed), power, element));
            }
        }
    }
}
