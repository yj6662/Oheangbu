// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Why a zone left the list (the owner turns it into a presentation cue; no rule hangs on it).
    public enum SpellZoneEnd { None, Expired, Broken, Replaced, Dropped }

    // One world-fixed lingering zone (SPEC-SPELL-120-308 WP-06): a flat circle that lives from From to Until. What happens
    // inside is the owner's business: Value is the one number the owner applies there (a movement scale, a power per step,
    // a share of max HP per second). Plain data, no Unity object: the same struct runs in the offline runner.
    public struct SpellZone308
    {
        public int Id;              // given by the runtime, 1.. (0 = not in a runtime)
        public string Owner;        // handler id of the effect that placed it
        public int Cast;            // serial of the cast it came from (zones of one cast never replace each other)
        public char Letter;
        public Element Element;
        public Vector3 Centre;
        public float Radius;
        public float From, Until;   // active while From <= now < Until
        public float Life;          // Until - From as the owner's exact number (0 = not given: the difference is used)
        public float Value;
        public float TickEvery;     // seconds between steps for an owner that acts in steps (0 = it acts every frame)
        public int Ticks;           // steps already handed out
        public float Body;          // radius of the body an enemy strike can touch (0 = nothing to strike)
        public float Reach;         // how far a melee attacker reaches when its strike on the player is judged against the body (0 = never)
        public int HitsLeft;        // strikes it still takes before it breaks
        public bool Broken;
        public long Token;          // the owner's identity for this zone (control owner id, attack id of its steps)
        public int Fx;              // presenter handle id (0 = nothing shown); zones of one cast may share it
        public SpellZoneEnd End;    // set by the runtime when the zone leaves the list
    }

    // Zone arithmetic (SPEC-SPELL-120-308 WP-06). Flat geometry through AreaGeometry, like every area judgement.
    public static class SpellZoneRule308
    {
        public const int Free = -1, Refused = -2;
        // Fraction of one step forgiven when counting steps: the wiring's clock is a float that loses precision over a long session.
        const float StepSlack = 1e-3f;

        public static bool Active(in SpellZone308 zone, float now) => !zone.Broken && now >= zone.From && now < zone.Until;

        public static bool Ended(in SpellZone308 zone, float now) => zone.Broken || !(now < zone.Until);

        public static bool Inside(in SpellZone308 zone, Vector3 position) => AreaGeometry.InCircle(zone.Centre, position, zone.Radius, out _);

        public static bool Covers(in SpellZone308 zone, Vector3 position, float now) => Active(zone, now) && Inside(zone, position);

        // Movement scale of an enemy at this position: the zone's value inside an active zone, 1 anywhere else.
        public static float SlowScale(in SpellZone308 zone, Vector3 position, float now)
            => Covers(zone, position, now) ? Mathf.Clamp01(zone.Value) : 1f;

        // Seconds of the interval from..to during which the zone was active (a frame that straddles its start or end counts in part).
        public static float ActiveSeconds(in SpellZone308 zone, float from, float to)
        {
            if (zone.Broken) return 0f;
            float a = Mathf.Max(from, zone.From), b = Mathf.Min(to, zone.Until);
            return b > a ? b - a : 0f;
        }

        // Steps of a stepping zone that fall due up to now. Step k happens at From + k x TickEvery, the last one at Until:
        // life 5 with a step of 0.5 gives exactly ten steps whatever the frame rate. Advances the zone's own counter.
        public static int DueTicks(ref SpellZone308 zone, float now)
        {
            if (zone.Broken || zone.TickEvery <= 0f || now < zone.From) return 0;
            // past its end the zone has lived its whole life exactly: the last step is never lost to clock rounding
            float span = zone.Life > 0f ? zone.Life : zone.Until - zone.From;
            float lived = now < zone.Until ? Mathf.Min(now - zone.From, span) : span;
            int total = Mathf.FloorToInt(lived / zone.TickEvery + StepSlack);
            int due = total - zone.Ticks;
            if (due <= 0) return 0;
            zone.Ticks = total;
            return due;
        }

        // Power of one step of a damage zone: cast power x share per second x seconds per step (never negative).
        public static float StepPower(float castPower, float perSecond, float step)
            => castPower > 0f && perSecond > 0f && step > 0f ? castPower * perSecond * step : 0f;

        // Share of max HP a player standing at this position regains over from..to from the zones of one owner.
        // Overlapping zones of the same owner do not add up: the best one counts.
        public static float HealShare(IReadOnlyList<SpellZone308> zones, string owner, Vector3 position, float from, float to)
        {
            float best = 0f;
            if (zones == null) return best;
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (zone.Owner != owner || zone.Value <= 0f || !Inside(zone, position)) continue;
                best = Mathf.Max(best, zone.Value * ActiveSeconds(zone, from, to));
            }
            return best;
        }

        // An enemy strike that lands at point and reaches reach touches the zone's body (flat distance).
        public static bool Struck(in SpellZone308 zone, Vector3 point, float reach)
        {
            if (zone.Broken || zone.Body <= 0f || float.IsNaN(reach) || reach < 0f) return false;
            return AreaGeometry.InCircle(zone.Centre, point, zone.Body + reach, out _);
        }

        // Where a new zone of this owner goes when the owner may hold max zones: Free = there is room, an index = the zone it
        // replaces (the oldest of the owner), Refused = the owner is full of zones of this same cast (or max is not positive).
        public static int Admit(IReadOnlyList<SpellZone308> zones, string owner, int cast, int max)
        {
            if (max <= 0) return Refused;
            int count = 0, oldest = -1;
            for (int i = 0; zones != null && i < zones.Count; i++)
            {
                if (zones[i].Owner != owner) continue;
                count++;
                if (oldest < 0 || zones[i].Id < zones[oldest].Id) oldest = i;
            }
            if (count < max) return Free;
            return zones[oldest].Cast == cast ? Refused : oldest;
        }
    }
}
