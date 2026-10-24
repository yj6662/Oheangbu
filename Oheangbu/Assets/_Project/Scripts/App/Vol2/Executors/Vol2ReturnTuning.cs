using System;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>TEMPORARY EXCEPTIONS of slice 7 (Exit Blocking): numbers and readings the 돌아온다 executor needs that NumbersV3 does not hold. They sit
    /// apart from <see cref="Vol2ShapeTuning"/> (TE-S3-n) and <see cref="Vol2VerbTuning"/> (TE-S4-n) so no slice edits another slice's class. The executor
    /// holds its own instance (<c>new Vol2ReturnExecutor(tuning)</c>); a fixture builds one with the values it wants. None is spell balance beyond the
    /// stated reading: each is a stand-in to be replaced by a data key (or confirmed and moved into the data) before the vol.2 runtime leaves the lab.
    ///  TE-S7-1  LegUs: a fan or a ring takes 0.5 s to go out and 0.5 s to come back (the data only caps the whole trip: timing.return.roundTrip 1.2 s,
    ///           which is also the cast's budget life, so the trip ends TripMarginUs before it and no return contact is refused as "Expired").
    ///           A path travels at the shape tuning's PathSpeed (TE-S3-1): 12 m out and 12 m back is 0.67 s.
    ///  TE-S7-2  FanTravel: how far the fan's apex travels (the data says "the fan itself goes out and comes back", not how far): 0 = the fan's own depth (space.fan.depth).
    ///  TE-S7-3  Orbit (겅 넝 멍 성 엉): radius 1.6 m, half width 0.3 m, one revolution per second, starting forward and turning clockwise from above. The data gives
    ///           the life (6 s) and "no second pass" but not the circle. One SwingId per revolution is what makes a later touch a "separate swing".
    ///  TE-S7-4  The orbit's own pass AND any real brush swing reported to the attachment (Vol2SwingSignal) are both contacts of the same budget (first, then one second
    ///           contact); the rules bound the damage either way. Nothing in the game raises a swing yet (slice 2/3 note), so today only the orbit touches.
    ///  TE-S7-5  ProjectileRadius 0.25 m: how big an enemy shot is for the touch test. ReflectSpeed 20 m/s: a reflected shot reaches its shooter after distance / speed
    ///           (at most ReflectMaxFlightUs); the ledger is booked at the moment of the reflection, the damage is delivered when the shot arrives.
    ///  TE-S7-6  HpPerUnit: the unit conversion of the borrowed power. The shot's power is its shooter's AttackDamage in HP; the rules' unit is "damage units, 무받침 ㅏ 화 = 1"
    ///           (Vol2Units). units = HP / HpPerUnit, then the rules take x combat.borrowed.multiplier (0.5), at most combat.borrowed.reflectTotalCap (1.0) in all, three
    ///           reflections. The default is the same TEST placeholder as the defence kit's (Vol2Units.DefaultHpPerUnit, 10): if the kit's value changes, change this one
    ///           too. It is written into the reflect record's DamageNote.
    ///           COUPLING: the borrowed damage reaches the shooter through the coordinator's damage port as a LASTING-damage request (Lasting = true: no C, no K, no equipment
    ///           and no ink power; the defence sink resolves it as Units x B - the weak-point window multiplier B still applies if the shooter stands in a window). There is no
    ///           "borrowed" request kind yet; the coordinator / kit should own one, and HpPerUnit should come from the kit rather than from a copy here.
    ///  TE-S7-7  옹's drag: bodies are pulled toward DragStopDistance (1.5 m) from the caster, over min(combat.bind.maxDuration, what is left of the trip); a boss is
    ///           exempt (combat.bind.bossExempt = 0 s). A body is airborne when it hangs more than AirborneGap (0.5 m) above the ground under it (a raycast down;
    ///           GroundHeight overrides it for a fixture); an airborne body is pulled down to the ground as well. The pull is a Push request whose Direction points at the
    ///           caster (not flat when the body is airborne): a status sink must take Direction as given.
    ///  TE-S7-8  웅's cleanse takes ENEMY-faction materials (patches and products alike) the band overlaps, nearest the ring's centre first (ties by id), at most
    ///           peak.웅.cleanseCount (4). They end with Vol2MaterialEnd.Cleansed (no child, no product). "My residue gets wet" has no flag on a material yet: it is
    ///           recorded in the report (WetRequests) and the cast log only.
    ///  TE-S7-9  앙 carries the first of MY materials the returning head meets (the farthest along the path; ties by id), peak.앙.carryCount (1) of them, to the point
    ///           peak.앙.destinationDistance (2 m) ahead of the caster at the caster's height. The material is moved (same id, amount, expiry), never remade; a fan or a ring is rebuilt
    ///           at the new place with its own shape.
    ///  TE-S7-10 What a body gets that the shape touches only on the way BACK: a normal first touch (the instant term), and no return (the return is the SECOND pass
    ///           over a body already hit). Everything that touches a body goes through the cast's budget.</summary>
    public sealed class Vol2ReturnTuning
    {
        public long LegUs = 500000;
        public long TripMarginUs = 100000;
        public float FanTravel = 0f;
        public float OrbitRadius = 1.6f;
        public float OrbitHalfWidth = 0.3f;
        public long OrbitRevolutionUs = 1000000;
        public float ProjectileRadius = 0.25f;
        public float ReflectSpeed = 20f;
        public long ReflectMaxFlightUs = 1000000;
        public float HpPerUnit = Vol2Units.DefaultHpPerUnit;
        public float DragStopDistance = 1.5f;
        public float AirborneGap = 0.5f;
        /// <summary>The height of the ground under a body; NaN = unknown. Null = a raycast down (falls back to the caster's feet).</summary>
        public Func<IVol2Target, float> GroundHeight;
        /// <summary>The longest step of a sweep (microseconds) and the most steps one tick takes (a long frame or a clock jump is cut into this many).</summary>
        public long SubstepUs = 20000;
        public int MaxSubsteps = 64;
        /// <summary>How far from the caster an enemy shot is looked for (metres).</summary>
        public float ProjectileReach = 24f;
    }
}
