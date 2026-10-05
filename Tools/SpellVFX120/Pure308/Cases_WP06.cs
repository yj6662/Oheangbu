// SPEC-SPELL-120-308 L2 cases of WP-06 (lingering zones): the shared zone list, zone.slow, zone.spikes, cone.healzone, zone.tree.
// Rule arithmetic only. The Unity half (which enemies the first hit schedules, EnemyControlState, PlayerVitals.Heal, the hit
// hook, the damage source of a step) is covered by the edit-mode fixtures (Spell120Checks308.WP06.cs).
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        static SpellZone308 WP06Zone(string owner, int cast, Vector3 centre, float radius, float from, float life, float value)
            => new SpellZone308 { Owner = owner, Cast = cast, Centre = centre, Radius = radius, From = from, Until = from + life, Life = life, Value = value };

        // A frame clock: start + k / fps for k = 1.. until end is reached (the last frame lands on or just after end).
        static IEnumerable<float[]> WP06Frames(float start, float end, int fps)
        {
            float previous = start;
            for (int k = 1; ; k++)
            {
                float now = start + k / (float)fps;
                yield return new[] { previous, now };
                previous = now;
                if (now >= end) yield break;
            }
        }

        static partial void RunWP06(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W06", false, "WP-06 cases need the table"); return; }
            var slowRow = c.Build.Rows.SingleOrDefault(x => x.Handler == "zone.slow");
            var spikeRow = c.Build.Rows.SingleOrDefault(x => x.Handler == "zone.spikes");
            var healRow = c.Build.Rows.SingleOrDefault(x => x.Handler == "cone.healzone");
            var treeRow = c.Build.Rows.SingleOrDefault(x => x.Handler == "zone.tree");
            if (!r.Check("W06", slowRow != null && spikeRow != null && healRow != null && treeRow != null, "the four WP-06 rows are in the table (Rules308_WP06)")) return;
            // the rows these glyphs inherit from: same initial and medial without a final
            var circleRow = c.Row(SpellGrammar308.BaseOf(slowRow.Letter[0])); var coneRow = c.Row(SpellGrammar308.BaseOf(healRow.Letter[0]));
            r.Check("W06", slowRow.Index == 14 && spikeRow.Index == 17 && healRow.Index == 38 && treeRow.Index == 10 &&
                slowRow.Final == SpellFinal.Giyeok && spikeRow.Final == SpellFinal.Siot && healRow.Final == SpellFinal.Giyeok && treeRow.Final == SpellFinal.Mieum &&
                new[] { slowRow, spikeRow, healRow, treeRow }.All(x => x.Feature == SpellLegacyFeature.None && x.Gate == SpellGateMode.Final),
                "grid cells 14 / 17 / 38 / 10, handler route, each behind its own final consonant");
            r.Check("W06", treeRow.Kind == SpellKind.Buff && treeRow.BasePower == 0f && slowRow.Kind == SpellKind.AttackArea && spikeRow.Kind == SpellKind.AttackArea && healRow.Kind == SpellKind.AttackArea,
                "kinds: the tree is a buff without power, the other three are area attacks");

            const string A = "zone.slow", B = "zone.spikes", H = "cone.healzone", T = "zone.tree";
            var removed = new List<SpellZone308>();
            Vector3 centre = new Vector3(4f, 0f, 9f);

            // ================= the shared list =================
            {
                var zones = new SpellZoneRuntime308();
                int first = zones.Add(WP06Zone(A, zones.NextCast(), centre, 2f, 0f, 10f, .5f), 1, 0f, removed);
                int other = zones.Add(WP06Zone(B, zones.NextCast(), centre, 2f, 0f, 3f, 1f), 1, 0f, removed);
                r.Check("W06", first == 1 && other == 2 && zones.Count == 2 && zones.CountOf(A) == 1 && zones.CountOf(B) == 1 && removed.Count == 0,
                    "one list holds the zones of several owners; ids count up");
                zones.Sweep(A, 5f, removed);
                r.Check("W06", zones.Count == 2 && removed.Count == 0, "an owner's sweep leaves another owner's zone alone, even one that has run out");
                zones.Sweep(B, 5f, removed);
                r.Check("W06", zones.Count == 1 && removed.Count == 1 && removed[0].Owner == B && removed[0].End == SpellZoneEnd.Expired, "the owner's own sweep removes it as Expired");
                r.Check("W06", zones.Covered(centre, 5f) && zones.Covered(centre, 5f, A) && !zones.Covered(centre, 5f, B) && !zones.Covered(centre + Vector3.right * 2.5f, 5f) && !zones.Covered(centre, 10f),
                    "Covered reads every owner's active zones (what a later package asks)");
                removed.Clear(); zones.Drop(A, removed);
                r.Check("W06", zones.Count == 0 && removed.Count == 1 && removed[0].End == SpellZoneEnd.Dropped, "Drop removes the owner's zones (death, rest, scene leave)");
                var separate = new SpellZoneRuntime308();
                r.Check("W06", separate.Count == 0 && separate.NextCast() == 1, "a second runtime starts empty: the list is instance state, nothing is shared through a static");
                removed.Clear();
            }

            // ================= zone.slow =================
            {
                float radius = slowRow.F("zone.radius", 0f), life = slowRow.F("zone.life", 0f), speed = slowRow.F("slow.speed", 1f);
                int max = Mathf.RoundToInt(slowRow.F("zone.max", 0f)); float delay = slowRow.AreaImpactDelay;
                r.Check("곡-1", slowRow.AreaShape == AreaShape.Circle && slowRow.AreaShape == circleRow.AreaShape && slowRow.AreaLength == circleRow.AreaLength &&
                    slowRow.AreaImpactDelay == circleRow.AreaImpactDelay && radius > 0f && max == 1,
                    "the row is a circle with the wood circle's aim range and formation delay, its own radius, one zone at a time",
                    slowRow.AreaShape + " length " + slowRow.AreaLength + " delay " + delay + " radius " + radius + " max " + max);
                r.Check("곡-1", slowRow.BasePower == 0f && SpellResolveCore308.Resolve(slowRow.Letter[0], 1.4f, 1f, slowRow, new ModelGate { Registered = { A }, Unlocked = { SpellFinal.Giyeok } }, out SpellCast slowCast) == SpellResolveStatus.Ok && slowCast.Power == 0f,
                    "no hit of its own (section 15 default): base power 0, the resolved cast carries power 0");
                var zones = new SpellZoneRuntime308(); float t0 = 100f;
                int id = zones.Add(WP06Zone(A, zones.NextCast(), centre, radius, t0 + delay, life, speed), max, t0, removed);
                r.Check("곡-1", id != 0 && zones.CountOf(A) == 1 && zones.Zones[0].Centre == centre && zones.Zones[0].Radius == radius, "one circular zone at the aimed spot");
                int second = zones.Add(WP06Zone(A, zones.NextCast(), centre + Vector3.forward * 20f, radius, t0 + 1f + delay, life, speed), max, t0 + 1f, removed);
                r.Check("곡-1", second != 0 && zones.CountOf(A) == 1 && zones.Zones[0].Id == second && removed.Count == 1 && removed[0].Id == id && removed[0].End == SpellZoneEnd.Replaced,
                    "a second cast replaces the first zone (zone.max 1): still one zone");
                removed.Clear();

                var zone = WP06Zone(A, 1, centre, radius, t0 + delay, life, speed);
                r.Check("곡-2", !SpellZoneRule308.Active(zone, t0) && SpellZoneRule308.Active(zone, t0 + delay) && SpellZoneRule308.Active(zone, t0 + delay + life - .01f) &&
                    !SpellZoneRule308.Active(zone, t0 + delay + life), "the zone forms after the delay, stays for its life, and is over at the end of it",
                    "from " + zone.From + " until " + zone.Until);
                var lone = new SpellZoneRuntime308(); lone.Add(zone, max, t0, removed);
                lone.Sweep(A, t0 + delay + life - .01f, removed); bool kept = lone.CountOf(A) == 1;
                lone.Sweep(A, t0 + delay + life, removed);
                r.Check("곡-2", kept && lone.CountOf(A) == 0 && removed.Count == 1 && removed[0].End == SpellZoneEnd.Expired, "the sweep keeps it during its life and removes it afterwards (Expired)");
                removed.Clear();

                Vector3 inside = centre + new Vector3(radius * .6f, 0f, radius * .6f), edge = centre + Vector3.right * radius, outside = centre + Vector3.right * (radius + .05f);
                Vector3 above = centre + new Vector3(0f, 7f, 0f);
                float mid = t0 + delay + life * .5f;
                r.Check("곡-3", speed < 1f && SpellZoneRule308.SlowScale(zone, inside, mid) == speed && SpellZoneRule308.SlowScale(zone, edge, mid) == speed &&
                    SpellZoneRule308.SlowScale(zone, above, mid) == speed, "an enemy inside moves at slow.speed (< 1); the judgement is flat like every area");
                r.Check("곡-3", SpellZoneRule308.SlowScale(zone, outside, mid) == 1f, "an enemy outside moves at full speed");
                r.Check("곡-3", SpellZoneRule308.SlowScale(zone, inside, t0) == 1f && SpellZoneRule308.SlowScale(zone, inside, t0 + delay + life) == 1f,
                    "before the vines have formed and after they are gone nobody is slowed");
                // an enemy walking through: slowed exactly while inside
                var walker = new List<float>();
                for (int step = 0; step <= 20; step++)
                    walker.Add(SpellZoneRule308.SlowScale(zone, centre + Vector3.right * (-radius - 1f + step * (2f * radius + 2f) / 20f), mid));
                r.Check("곡-3", walker.First() == 1f && walker.Last() == 1f && walker.Any(x => x == speed) && walker.All(x => x == 1f || x == speed),
                    "walking through: full speed, slowed inside, full speed again once out");
                var absurd = zone; absurd.Value = 3f; var negative = zone; negative.Value = -1f;
                r.Check("곡-3", SpellZoneRule308.SlowScale(absurd, inside, mid) == 1f && SpellZoneRule308.SlowScale(negative, inside, mid) == 0f, "the scale handed to the control state always stays inside 0..1");
            }

            // ================= zone.spikes =================
            {
                float life = spikeRow.F("zone.life", 0f), dps = spikeRow.F("zone.dps", 0f), tick = spikeRow.F("zone.tick", 0f);
                int max = Mathf.RoundToInt(spikeRow.F("zone.max", 0f));
                r.Check("곳-1", spikeRow.AreaShape == circleRow.AreaShape && spikeRow.AreaRadius == circleRow.AreaRadius && spikeRow.AreaLength == circleRow.AreaLength &&
                    spikeRow.AreaImpactDelay == circleRow.AreaImpactDelay && spikeRow.BasePower == circleRow.BasePower && spikeRow.Kind == circleRow.Kind && spikeRow.Element == circleRow.Element,
                    "the first hit reads the wood circle's own numbers: shape, radius, aim range, delay, base power",
                    spikeRow.AreaShape + " r" + spikeRow.AreaRadius + " l" + spikeRow.AreaLength + " d" + spikeRow.AreaImpactDelay + " p" + spikeRow.BasePower);
                // the staggered rise (area.spikes) is the wood circle's own trait: this glyph's first hit lands at one moment
                r.Check("곳-1", spikeRow.F(SpellGrammar308.SpikesKey, 0f) == 0f && !spikeRow.Has("spike.count") && circleRow.F(SpellGrammar308.SpikesKey, 0f) > 0f,
                    "the circle and its numbers are shared, the staggered rise is not: every enemy in the circle is hit at the formation delay");
                // membership of the first hit is the circle test the wiring uses (AreaGeometry.InCircle)
                Vector3[] spots = { centre, centre + Vector3.right * (spikeRow.AreaRadius - .1f), centre + Vector3.right * (spikeRow.AreaRadius + .1f), centre + Vector3.forward * 30f };
                var area = WP06Zone(B, 1, centre, spikeRow.AreaRadius, 0f, life, 1f);
                r.Check("곳-1", spots.Select(p => SpellZoneRule308.Inside(area, p)).SequenceEqual(spots.Select(p => AreaGeometry.InCircle(centre, p, spikeRow.AreaRadius, out _))) &&
                    SpellZoneRule308.Inside(area, spots[1]) && !SpellZoneRule308.Inside(area, spots[2]), "the lingering zone is that same circle: same centre, same radius, same test");

                float t0 = 50f, delay = spikeRow.AreaImpactDelay;
                var zone = WP06Zone(B, 1, centre, spikeRow.AreaRadius, t0 + delay, life, 0f); zone.TickEvery = tick;
                r.Check("곳-2", life > 0f && max == 1 && SpellZoneRule308.Active(zone, t0 + delay) && SpellZoneRule308.Active(zone, t0 + delay + life * .99f) && SpellZoneRule308.Ended(zone, t0 + delay + life) &&
                    !SpellZoneRule308.Ended(zone, t0 + delay + life * .5f), "the spikes stay for zone.life after they have risen, then they are gone", "life " + life);

                float castPower = spikeRow.BasePower * 1.25f;          // base power x a brush multiplier
                float step = SpellZoneRule308.StepPower(castPower, dps, tick);
                int expectedSteps = Mathf.RoundToInt(life / tick);
                r.Check("곳-3", r.Near(step, castPower * dps * tick) && step > 0f && SpellZoneRule308.StepPower(0f, dps, tick) == 0f && SpellZoneRule308.StepPower(castPower, -1f, tick) == 0f,
                    "one step = cast power x zone.dps x zone.tick (0 when there is nothing to deal)", step.ToString());
                var totals = new List<string>(); bool sameAtEveryRate = true;
                foreach (int fps in new[] { 30, 60, 120, 7 })
                {
                    var zones = new SpellZoneRuntime308(); int id = zones.Add(zone, max, t0, removed); int steps = 0, early = 0;
                    foreach (float[] frame in WP06Frames(t0, t0 + delay + life + 1f, fps))
                    {
                        int due = zones.Ticks(id, frame[1]);
                        if (frame[1] < t0 + delay + tick - 1e-3f) early += due;
                        steps += due;
                    }
                    totals.Add(fps + "fps:" + steps);
                    sameAtEveryRate &= steps == expectedSteps && early == 0;
                }
                r.Check("곳-3", sameAtEveryRate && expectedSteps > 0, "an enemy that stays inside takes life / tick steps whatever the frame rate, none before the first interval has passed",
                    string.Join(" ", totals) + " expected " + expectedSteps);
                r.Check("곳-3", r.Near(step * expectedSteps, castPower * dps * life, 1e-3f), "total over the life = cast power x zone.dps x zone.life", (step * expectedSteps).ToString());
                // who takes a step: the enemies standing in the circle when it falls due
                var actors = new[]
                {
                    new SpellActorSnap(0, centre, true, 1, 1f, false), new SpellActorSnap(1, centre + Vector3.right * (spikeRow.AreaRadius * .9f), true, 1, 1f, true),
                    new SpellActorSnap(2, centre + Vector3.right * (spikeRow.AreaRadius + 1f), true, 1, 1f, false), new SpellActorSnap(3, centre, false, 1, 0f, false),
                };
                var hit = actors.Where(a => a.Alive && SpellZoneRule308.Inside(zone, a.Position)).Select(a => a.Id).ToArray();
                r.Check("곳-3", hit.SequenceEqual(new[] { 0, 1 }), "a step lands on the living enemies inside (a boss too), not on those outside or dead", string.Join(",", hit));
                // a long session: the clock is a coarse float (steps of several milliseconds), the step count must not change
                var coarse = WP06Zone(B, 1, centre, spikeRow.AreaRadius, 70000f + delay, life, 0f); coarse.TickEvery = tick;
                var noLife = coarse; noLife.Life = 0f;
                int coarseSteps = 0, noLifeSteps = 0;
                foreach (float[] frame in WP06Frames(70000f, 70000f + delay + life + 1f, 60))
                { coarseSteps += SpellZoneRule308.DueTicks(ref coarse, frame[1]); noLifeSteps += SpellZoneRule308.DueTicks(ref noLife, frame[1]); }
                r.Check("곳-3", coarseSteps == expectedSteps && (noLifeSteps == expectedSteps || noLifeSteps == expectedSteps - 1),
                    "nineteen hours into a session (a clock that moves in 8 ms steps) the zone still hands out exactly life / tick steps, because it carries its life as its own number",
                    "with life " + coarseSteps + ", from the clock difference alone " + noLifeSteps);
                var late = new SpellZoneRuntime308(); int lateId = late.Add(zone, max, t0, removed);
                r.Check("곳-3", late.Ticks(lateId, t0 + delay + life + 30f) == expectedSteps && late.Ticks(lateId, t0 + delay + life + 60f) == 0 && late.Ticks(999, t0) == 0,
                    "a long stall hands out the remaining steps once and never more than the life holds");
                removed.Clear();
            }

            // ================= cone.healzone =================
            {
                float radius = healRow.F("heal.radius", 0f), life = healRow.F("heal.life", 0f), rate = healRow.F("heal.hp01ps", 0f);
                int max = Mathf.RoundToInt(healRow.F("heal.max", 0f));
                r.Check("녹-1", healRow.AreaShape == AreaShape.Cone && healRow.AreaShape == coneRow.AreaShape && healRow.BasePower == coneRow.BasePower && healRow.BasePower > 0f &&
                    healRow.AreaAngle == coneRow.AreaAngle && healRow.AreaRadius == coneRow.AreaRadius && healRow.AreaLength == coneRow.AreaLength &&
                    healRow.AreaImpactDelay == coneRow.AreaImpactDelay && healRow.Kind == coneRow.Kind && healRow.Element == Element.Fire,
                    "the first hit reads the fire cone's own numbers: shape, power, geometry, delay", healRow.AreaShape + " p" + healRow.BasePower);

                // five enemies are hit by one cast at the same moment: zones open only where a hit landed, at most heal.max
                var zones = new SpellZoneRuntime308(); int cast = zones.NextCast(); float t0 = 20f;
                Vector3[] hits = { new Vector3(0, 0, 3), new Vector3(1, 0, 5), new Vector3(-1, 0, 6), new Vector3(2, 0, 7), new Vector3(-2, 0, 8) };
                Vector3 missed = new Vector3(30, 0, 30);
                var ids = hits.Select(p => zones.Add(WP06Zone(H, cast, p, radius, t0, life, rate), max, t0, removed)).ToArray();
                r.Check("녹-2", max == 3 && ids.Take(max).All(x => x != 0) && ids.Skip(max).All(x => x == 0) && zones.CountOf(H) == max && removed.Count == 0,
                    "one cast opens at most heal.max zones: the first hits to land get them, later hits of the same cast are refused (no churn)", string.Join(",", ids));
                r.Check("녹-2", hits.Take(max).All(p => zones.Covered(p, t0, H)) && !zones.Covered(missed, t0, H) && zones.Zones.All(z => hits.Contains(z.Centre)),
                    "every zone sits on the spot of an enemy that was hit; an enemy that was not hit has none");
                int laterCast = zones.NextCast(); int newer = zones.Add(WP06Zone(H, laterCast, missed, radius, t0 + 1f, life, rate), max, t0 + 1f, removed);
                r.Check("녹-2", newer != 0 && zones.CountOf(H) == max && removed.Count == 1 && removed[0].Id == ids[0] && removed[0].End == SpellZoneEnd.Replaced,
                    "a later cast replaces the oldest zone");
                removed.Clear();
                r.Check("녹-2", SpellZoneRule308.Admit(zones.Zones, H, laterCast, 0) == SpellZoneRule308.Refused && SpellZoneRule308.Admit(new List<SpellZone308>(), H, 1, max) == SpellZoneRule308.Free,
                    "admission: room = Free, a cap of 0 = Refused");

                var one = new SpellZoneRuntime308(); Vector3 spot = hits[0];
                one.Add(WP06Zone(H, one.NextCast(), spot, radius, t0, life, rate), max, t0, removed);
                Vector3 inside = spot + Vector3.right * (radius * .5f), outside = spot + Vector3.right * (radius + .1f);
                float second = SpellZoneRule308.HealShare(one.Zones, H, inside, t0 + 1f, t0 + 2f);
                r.Check("녹-3", rate > 0f && r.Near(second, rate, 1e-6f) && SpellZoneRule308.HealShare(one.Zones, H, outside, t0 + 1f, t0 + 2f) == 0f,
                    "one second inside heals heal.hp01ps of max HP; outside heals nothing", second.ToString());
                var sums = new List<string>(); bool rates = true;
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    float sum = 0f;
                    foreach (float[] frame in WP06Frames(t0 - 1f, t0 + life + 1f, fps)) sum += SpellZoneRule308.HealShare(one.Zones, H, inside, frame[0], frame[1]);
                    sums.Add(fps + "fps:" + sum.ToString("0.#####")); rates &= r.Near(sum, rate * life, 1e-3f);
                }
                r.Check("녹-3", rates, "standing inside for the whole life heals heal.hp01ps x heal.life at 30, 60 and 120 fps (frames that straddle the start or the end count in part)",
                    string.Join(" ", sums) + " expected " + rate * life);
                var two = new SpellZoneRuntime308(); int twoCast = two.NextCast();
                two.Add(WP06Zone(H, twoCast, spot, radius, t0, life, rate), max, t0, removed); two.Add(WP06Zone(H, twoCast, spot + Vector3.right * .5f, radius, t0, life, rate), max, t0, removed);
                r.Check("녹-3", r.Near(SpellZoneRule308.HealShare(two.Zones, H, spot, t0 + 1f, t0 + 2f), rate, 1e-6f), "two overlapping zones of this glyph heal like one (no stacking)");
                r.Check("녹-3", SpellZoneRule308.HealShare(two.Zones, T, spot, t0 + 1f, t0 + 2f) == 0f, "a zone heals only through its own owner");
                r.Check("녹-3", SpellZoneRule308.HealShare(one.Zones, H, inside, t0 + life + 1f, t0 + life + 2f) == 0f, "after its life a zone heals nothing");

                var empty = new SpellZoneRuntime308();                 // a cast that hit nobody adds nothing
                r.Check("녹-4", empty.CountOf(H) == 0 && SpellZoneRule308.HealShare(empty.Zones, H, Vector3.zero, 0f, 10f) == 0f && !empty.Covered(Vector3.zero, 1f),
                    "no hit = no zone = no healing anywhere");
            }

            // ================= zone.tree =================
            {
                float radius = treeRow.F("tree.radius", 0f), life = treeRow.F("tree.life", 0f), rate = treeRow.F("heal.hp01ps", 0f);
                float body = treeRow.F("tree.body", 0f), reach = treeRow.F("tree.reach", 0f);
                int max = Mathf.RoundToInt(treeRow.F("tree.max", 0f)), hitsNeeded = Mathf.RoundToInt(treeRow.F("tree.hits", 0f));
                float t0 = 300f; Vector3 feet = new Vector3(-3f, 1.5f, 12f);
                SpellZone308 Tree(SpellZoneRuntime308 zones, Vector3 at, float now)
                {
                    var tree = WP06Zone(T, zones.NextCast(), at, radius, now, life, rate);
                    tree.Body = body; tree.Reach = reach; tree.HitsLeft = hitsNeeded;
                    return tree;
                }
                var zones1 = new SpellZoneRuntime308(); int id = zones1.Add(Tree(zones1, feet, t0), max, t0, removed);
                r.Check("검-1", max == 1 && id != 0 && zones1.CountOf(T) == 1 && zones1.Zones[0].Centre == feet && zones1.Zones[0].From == t0,
                    "one zone at the spot where the caster stood, active at once");
                Vector3 walkedAway = feet + Vector3.forward * 15f;
                r.Check("검-1", !zones1.Covered(walkedAway, t0 + 1f, T) && zones1.Covered(feet, t0 + 1f, T) && zones1.Zones[0].Centre == feet, "the tree stays where it was planted; it does not follow the caster");
                int replanted = zones1.Add(Tree(zones1, walkedAway, t0 + 2f), max, t0 + 2f, removed);
                r.Check("검-1", replanted != 0 && zones1.CountOf(T) == 1 && zones1.Zones[0].Centre == walkedAway && removed.Count == 1 && removed[0].End == SpellZoneEnd.Replaced,
                    "planting again replaces the old tree (tree.max 1)");
                removed.Clear();

                var zones2 = new SpellZoneRuntime308(); zones2.Add(Tree(zones2, feet, t0), max, t0, removed);
                float inShare = SpellZoneRule308.HealShare(zones2.Zones, T, feet + Vector3.right * (radius - .1f), t0 + 3f, t0 + 4f);
                float outShare = SpellZoneRule308.HealShare(zones2.Zones, T, feet + Vector3.right * (radius + .1f), t0 + 3f, t0 + 4f);
                r.Check("검-2", rate > 0f && r.Near(inShare, rate, 1e-6f) && outShare == 0f, "inside the circle: heal.hp01ps of max HP per second; outside: nothing", inShare + " / " + outShare);
                float whole = 0f;
                foreach (float[] frame in WP06Frames(t0, t0 + life + 2f, 60)) whole += SpellZoneRule308.HealShare(zones2.Zones, T, feet, frame[0], frame[1]);
                r.Check("검-2", r.Near(whole, rate * life, 1e-3f), "the whole life inside heals heal.hp01ps x tree.life in total", whole + " expected " + rate * life);

                r.Check("검-3", SpellZoneRule308.Active(zones2.Zones[0], t0 + life - .01f) && SpellZoneRule308.Ended(zones2.Zones[0], t0 + life), "the tree lives for tree.life");
                zones2.Sweep(T, t0 + life, removed);
                r.Check("검-3", zones2.CountOf(T) == 0 && removed.Count == 1 && removed[0].End == SpellZoneEnd.Expired && SpellZoneRule308.HealShare(zones2.Zones, T, feet, t0 + life, t0 + life + 1f) == 0f,
                    "when its life runs out it is removed (Expired) and heals no more");
                removed.Clear();

                // ---- struck and gone ----
                var zones3 = new SpellZoneRuntime308(); zones3.Add(Tree(zones3, feet, t0), max, t0, removed);
                var other = WP06Zone(A, zones3.NextCast(), feet, 3f, t0, 50f, .5f); zones3.Add(other, 1, t0, removed);   // a zone without a body, same spot
                r.Check("검-4", body > 0f && hitsNeeded >= 1 && zones3.Strike(T, feet + Vector3.right * (body + 1f + .2f), 1f) == 0 && SpellZoneRule308.Active(zones3.Zones[0], t0 + 1f),
                    "a strike that does not reach the trunk leaves the tree standing");
                int broke = 0; for (int i = 0; i < hitsNeeded; i++) broke += zones3.Strike(null, feet + Vector3.right * (body + .5f), 1f);
                r.Check("검-4", broke == 1 && zones3.Zones[0].Broken && !SpellZoneRule308.Active(zones3.Zones[0], t0 + 1f) &&
                    SpellZoneRule308.HealShare(zones3.Zones, T, feet, t0 + 1f, t0 + 2f) == 0f, "tree.hits strikes that reach the trunk break it; a broken tree heals nothing from that moment");
                r.Check("검-4", !zones3.Zones[1].Broken && SpellZoneRule308.Active(zones3.Zones[1], t0 + 1f), "a zone without a body (vines, spikes, a healing zone) cannot be struck");
                zones3.Sweep(T, t0 + 1f, removed);
                r.Check("검-4", zones3.CountOf(T) == 0 && removed.Count == 1 && removed[0].End == SpellZoneEnd.Broken, "its owner removes it as Broken (the presenter hears a break, not an expiry)");
                removed.Clear();

                var zones4 = new SpellZoneRuntime308(); zones4.Add(Tree(zones4, feet, t0), max, t0, removed);
                r.Check("검-4", reach > 0f && zones4.StrikeFrom(T, feet + Vector3.right * (body + reach + .3f)) == 0 && !zones4.Zones[0].Broken,
                    "a melee strike on the player from beyond tree.reach of the trunk does not touch the tree");
                int byMelee = 0; for (int i = 0; i < hitsNeeded; i++) byMelee += zones4.StrikeFrom(T, feet + Vector3.right * (body + reach - .1f));
                r.Check("검-4", byMelee == 1 && zones4.Zones[0].Broken, "a melee strike on the player whose attacker stands within tree.reach of the trunk strikes the tree too");
                var zones5 = new SpellZoneRuntime308(); var passive = Tree(zones5, feet, t0); passive.Reach = 0f; zones5.Add(passive, max, t0, removed);
                r.Check("검-4", zones5.StrikeFrom(T, feet) == 0 && !zones5.Zones[0].Broken && zones5.Strike(T, feet, 0f) == (hitsNeeded == 1 ? 1 : 0),
                    "tree.reach 0 switches that reading off; a direct strike on the trunk still counts");
                var zones6 = new SpellZoneRuntime308(); var sturdy = Tree(zones6, feet, t0); sturdy.HitsLeft = 2; zones6.Add(sturdy, max, t0, removed);
                r.Check("검-4", zones6.Strike(T, feet, 0f) == 0 && !zones6.Zones[0].Broken && zones6.Zones[0].HitsLeft == 1 && zones6.Strike(T, feet, 0f) == 1 && zones6.Zones[0].Broken &&
                    zones6.Strike(T, feet, 0f) == 0, "the number of strikes it takes is data (tree.hits); a broken tree cannot be struck again");
                r.Check("검-4", !SpellZoneRule308.Struck(zones1.Zones[0], zones1.Zones[0].Centre, float.NaN) && !SpellZoneRule308.Struck(zones1.Zones[0], zones1.Zones[0].Centre, -1f),
                    "a strike without a real reach is ignored");
                removed.Clear();
            }
        }
    }
}
