// PURE308
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // The haze that stays where a spray or a storm went (SPEC-SPELL-120-308 WP-13, handler zone.haze). The first hit is the
    // base glyph's own shape (a cone or a path). The haze is one circle on the axis of that shape, at its middle: the
    // place the spray covered. An enemy standing in it sees badly: a share of its strikes miss (EnemyStrikeVeil308).
    // The zone itself is a SpellZone308 of the shared zone list; its Value is the miss share.
    public static class HazeRule308
    {
        // Middle of the sprayed shape: half its length along its flat direction from where it started.
        public static Vector3 Centre(Vector3 start, Vector3 direction, float length)
        {
            Vector3 flat = AreaGeometry.Flat(direction);
            flat = flat.sqrMagnitude > .0001f ? flat.normalized : Vector3.forward;
            return start + flat * (Mathf.Max(0f, length) * .5f);
        }

        // Share of its strikes an enemy at this position misses now: the zone's value inside the active haze, 0 anywhere else.
        public static float MissShare(in SpellZone308 zone, Vector3 position, float now)
            => SpellZoneRule308.Covers(zone, position, now) ? Mathf.Clamp01(zone.Value) : 0f;
    }
}
