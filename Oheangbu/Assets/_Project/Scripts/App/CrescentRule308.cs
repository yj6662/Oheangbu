// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Two crescents that leave the point of a confirmed hit, mirrored about the shot's travel direction
    // (SPEC-SPELL-120-308 WP-04, handler hit.crescent). Each crescent is one cone judgement of its own.
    public static class CrescentRule308
    {
        // The flat direction turned about the vertical axis; positive degrees turn to the right (the sense of
        // Quaternion.AngleAxis(degrees, up), which the offline runner cannot call).
        public static Vector3 Turn(Vector3 direction, float degrees)
        {
            Vector3 flat = AreaGeometry.Flat(direction);
            if (flat.sqrMagnitude < 0.0001f) return Vector3.zero;
            flat.Normalize();
            float radians = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            return new Vector3(flat.x * cos + flat.z * sin, 0f, flat.z * cos - flat.x * sin);
        }

        // apex = where the first shot landed, travel = the direction it flew in. The left crescent leaves at -angle, the right
        // one at +angle; each hits every living enemy inside its own cone (half angle, range) once, at the same time.
        // The struck enemy is not hit again, and an enemy standing exactly on the apex has no bearing and is in neither cone.
        // Appends the left crescent's orders, then the right one's; returns how many belong to the left crescent.
        public static int Plan(SpellActorSnap[] actors, int primaryId, Vector3 apex, Vector3 travel, float angle, float half, float range,
            float at, float power, Element element, List<SpellHitOrder> orders)
        {
            if (actors == null || float.IsNaN(power) || power <= 0f || float.IsNaN(range) || range <= 0f || float.IsNaN(half) || half < 0f) return 0;
            int left = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 direction = Turn(travel, side * angle);
                if (direction.sqrMagnitude < 0.0001f) return 0;
                foreach (var actor in actors)
                {
                    if (!actor.Alive || actor.Id == primaryId) continue;
                    if (!AreaGeometry.InCone(apex, direction, actor.Position, half, range, out _, out float distance) || distance <= 0.0001f) continue;
                    orders.Add(new SpellHitOrder(actor.Id, at, power, element));
                    if (side < 0) left++;
                }
            }
            return left;
        }
    }
}
