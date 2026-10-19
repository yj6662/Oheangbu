using System;
using Oheangbu.App;
using UnityEngine;

namespace Pure308
{
    // SPEC-SPELL-120-308 WP-16 (D308-114): zone tests count the enemy's body radius (its body edge inside = inside).
    // The body radius itself (EnemyVitals.BodyRadius, measured from the collider) and the melee pass of the guard are
    // engine-side; here only the rules.
    public static partial class Program
    {
        static partial void RunWP16(Report r, Context c)
        {
            var zone = new SpellZone308 { Id = 1, Owner = "zone.blind", Centre = new Vector3(2f, 0f, -3f), Radius = 3f, From = 10f, Until = 16f, Value = .5f };
            Vector3 edge = zone.Centre + new Vector3(3.3f, 0f, 0f);       // .3 m beyond the drawn circle
            Vector3 far = zone.Centre + new Vector3(0f, 0f, 3.5f);        // .5 m beyond
            Vector3 high = zone.Centre + new Vector3(3.3f, 7f, 0f);       // above the edge: the test is flat

            r.Check("구역-1", !SpellZoneRule308.Inside(zone, edge) && SpellZoneRule308.Inside(zone, edge, .4f) && !SpellZoneRule308.Inside(zone, far, .4f),
                "an enemy whose body edge reaches the circle is inside; one whose body stays outside is not");
            r.Check("구역-1", SpellZoneRule308.Inside(zone, high, .4f), "the body test is flat like the point test (height does not matter)");
            r.Check("구역-1", SpellZoneRule308.Inside(zone, zone.Centre + new Vector3(3.4f, 0f, 0f), .4f),
                "exactly touching counts (centre distance = circle radius + body radius)");
            bool same = true;
            foreach (var p in new[] { zone.Centre, edge, far, zone.Centre + new Vector3(2.99f, 0f, 0f), zone.Centre + new Vector3(0f, 0f, -3.01f) })
                same &= SpellZoneRule308.Inside(zone, p, 0f) == SpellZoneRule308.Inside(zone, p);
            r.Check("구역-2", same, "a body radius of zero is the old point test");
            r.Check("구역-2", !SpellZoneRule308.Inside(zone, edge, -1f) && !SpellZoneRule308.Inside(zone, edge, float.NaN) && SpellZoneRule308.Inside(zone, zone.Centre, float.NaN),
                "a negative or not-a-number body radius counts as zero (the point test), never as a shrunken circle");

            r.Check("구역-3", SpellZoneRule308.Covers(zone, edge, 12f, .4f) && !SpellZoneRule308.Covers(zone, edge, 9.99f, .4f) && !SpellZoneRule308.Covers(zone, edge, 16f, .4f),
                "the body test keeps the zone's time window (From <= now < Until)");
            var broken = zone; broken.Broken = true;
            r.Check("구역-3", !SpellZoneRule308.Covers(broken, zone.Centre, 12f, .4f), "a broken zone covers no body");

            r.Check("몽-4", r.Near(HazeRule308.MissShare(zone, edge, 12f, .4f), .5f) && HazeRule308.MissShare(zone, edge, 12f) == 0f && HazeRule308.MissShare(zone, far, 12f, .4f) == 0f,
                "the haze blur share reaches an enemy whose body edge is in the cloud");
            var strong = zone; strong.Value = 1.7f;
            r.Check("몽-4", r.Near(HazeRule308.MissShare(strong, edge, 12f, .4f), 1f) && HazeRule308.MissShare(zone, edge, 20f, .4f) == 0f,
                "the body overload keeps the share's clamp and the cloud's life");
        }
    }
}
