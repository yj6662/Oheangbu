// PURE308
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // One enemy hazard or remnant on the ground as a rule sees it (SPEC-SPELL-120-308 WP-13): a flat circle.
    public readonly struct SpellHazardSnap
    {
        public readonly int Id;
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly bool Purged;

        public SpellHazardSnap(int id, Vector3 position, float radius, bool purged)
        { Id = id; Position = position; Radius = radius; Purged = purged; }
    }

    // The cleansing wave (handler path.purge): the base glyph's wave runs its corridor and wipes every enemy hazard it
    // washes over. A hazard is on the path when its circle touches the corridor; it is wiped the moment the front reaches
    // its near edge. Hazards behind the caster, beside the corridor or beyond its far end stay.
    public static class PurgeRule308
    {
        // Does the hazard's circle touch the corridor? nearEdge = where on the corridor axis the front first meets it.
        public static bool OnPath(in PathFront308 front, in SpellHazardSnap hazard, out float nearEdge)
        {
            nearEdge = 0f;
            if (!front.Valid || hazard.Purged) return false;
            float radius = Mathf.Max(0f, hazard.Radius);
            AreaGeometry.InCorridor(front.Start, front.Direction, hazard.Position, front.HalfWidth, front.Length, out float along, out float lateral);
            if (lateral > front.HalfWidth + radius || along < -radius || along > front.Length + radius) return false;
            nearEdge = Mathf.Clamp(along - radius, 0f, front.Length);
            return true;
        }

        // When the front wipes it (meaningful only when OnPath).
        public static float WipedAt(in PathFront308 front, float nearEdge) => front.ArrivalAt(nearEdge);

        // Has the front reached the hazard by this time?
        public static bool Reached(in PathFront308 front, in SpellHazardSnap hazard, float time)
            => OnPath(front, hazard, out float nearEdge) && front.Reached(nearEdge, time);

        // The strip of the corridor one look has to cover, as an interval on the corridor axis (0 = the corridor's start):
        // every place the front crossed between the last look and now, with `room` on either side of it.
        // travelledBefore / travelledNow = PathFront308.Travelled at the last look and now. A place the front crossed
        // between two looks always lies inside the strip of the later look, whatever the frame length.
        public static void Strip(in PathFront308 front, float travelledBefore, float travelledNow, float room, out float near, out float far)
        {
            float a = Mathf.Clamp(travelledBefore, 0f, front.Length), b = Mathf.Clamp(travelledNow, 0f, front.Length);
            if (front.Returning) { a = front.Length - a; b = front.Length - b; }
            room = Mathf.Max(0f, room);
            near = Mathf.Min(a, b) - room; far = Mathf.Max(a, b) + room;
        }
    }
}
