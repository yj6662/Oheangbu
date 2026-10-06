// SPEC-SPELL-120-308 L2 cases of WP-03 (two-stage attack glyphs): cone + burst at its end, single shot + burst at the impact,
// single shot + skip to a second point, path + trailing second front, path + returning front.
// What is checked here is the pure half: where and when the second stage happens, who it hits, how often, and that it can
// never be judged before a primary hit of the same cast has been applied. Expected sets and times are computed on their own
// with AreaGeometry and plain arithmetic, never with the rule under test. Clock samples: 30, 60 and 120 frames per second,
// plus a stalled machine (one frame per second: a single frame is longer than every delay in the sheet).
// Not checked here (needs Unity objects, L3 / Play): the effect classes' Unity half, sight checks, presentation.
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
        const float WP03Eps = .002f;

        // Enemies of one simulated cast. Position(t) = Home + Velocity x max(0, t - MovesFrom).
        sealed class WP03World
        {
            public float CastAt;
            public Vector3[] Home, Velocity;
            public float[] MovesFrom, PrimaryAt, DiesAt, RestoredAt;
            public bool[] AliveAtCast, PrimaryKills;
            public int Count => Home.Length;

            public WP03World(float castAt, params Vector3[] home)
            {
                CastAt = castAt; Home = home; int n = home.Length;
                Velocity = new Vector3[n]; MovesFrom = new float[n]; PrimaryAt = new float[n]; DiesAt = new float[n]; RestoredAt = new float[n];
                AliveAtCast = new bool[n]; PrimaryKills = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    MovesFrom[i] = castAt; PrimaryAt[i] = float.PositiveInfinity; DiesAt[i] = float.PositiveInfinity; RestoredAt[i] = float.PositiveInfinity;
                    AliveAtCast[i] = true;
                }
            }

            public Vector3 PositionAt(int i, float time) => Home[i] + Velocity[i] * Mathf.Max(0f, time - MovesFrom[i]);
            public uint LifeAtCast(int i) => (uint)(7 + i);

            public SpellActorSnap[] AtCast()
            {
                var snaps = new SpellActorSnap[Count];
                for (int i = 0; i < Count; i++) snaps[i] = new SpellActorSnap(i, PositionAt(i, CastAt), AliveAtCast[i], LifeAtCast(i), 1f, false);
                return snaps;
            }
        }

        sealed class WP03Run
        {
            public int[] SecondHits, SecondFrame, PrimaryFrame;
            public float[] SecondJudged, SecondPower;
            public int Judgements;                          // frames in which the second stage produced at least one order
            public WP03Run(int n)
            {
                SecondHits = new int[n]; SecondFrame = new int[n]; PrimaryFrame = new int[n]; SecondJudged = new float[n]; SecondPower = new float[n];
                for (int i = 0; i < n; i++) { SecondFrame[i] = -1; PrimaryFrame[i] = -1; SecondJudged[i] = float.NaN; }
            }
        }

        // One wiring frame, in the wiring's order: the effects tick (second stages are judged with TwoStageClock308), then the
        // wiring applies the primary hits that are due (onPrimary = the hit hook), then the rest of the world moves on.
        static WP03Run WP03Frames(WP03World w, int rate, float until, Action<float, SpellActorSnap[], List<SpellHitOrder>> judge,
            Action<int, float> onPrimary = null)
        {
            int n = w.Count;
            var run = new WP03Run(n); var clock = new TwoStageClock308(); var orders = new List<SpellHitOrder>();
            var alive = (bool[])w.AliveAtCast.Clone(); var life = new uint[n]; var primaryDone = new bool[n]; var restored = new bool[n];
            for (int i = 0; i < n; i++) life[i] = w.LifeAtCast(i);
            for (int k = 0; ; k++)
            {
                float now = w.CastAt + k / (float)rate;
                float judged = clock.Advance(now);
                var snaps = new SpellActorSnap[n];
                for (int i = 0; i < n; i++) snaps[i] = new SpellActorSnap(i, w.PositionAt(i, now), alive[i], life[i], 1f, false);
                orders.Clear();
                judge(judged, snaps, orders);
                if (orders.Count > 0) run.Judgements++;
                foreach (var order in orders)
                {
                    run.SecondHits[order.TargetId]++;
                    if (run.SecondFrame[order.TargetId] < 0) { run.SecondFrame[order.TargetId] = k; run.SecondJudged[order.TargetId] = judged; run.SecondPower[order.TargetId] = order.Power; }
                }
                for (int i = 0; i < n; i++)
                {
                    if (primaryDone[i] || !(now >= w.PrimaryAt[i])) continue;
                    primaryDone[i] = true;
                    if (!alive[i] || life[i] != w.LifeAtCast(i)) continue;          // the wiring drops a hit aimed at another life
                    run.PrimaryFrame[i] = k;
                    onPrimary?.Invoke(i, now);
                    if (w.PrimaryKills[i]) { alive[i] = false; life[i]++; }
                }
                for (int i = 0; i < n; i++)
                {
                    if (alive[i] && now >= w.DiesAt[i]) { alive[i] = false; life[i]++; }
                    if (!restored[i] && now >= w.RestoredAt[i]) { restored[i] = true; alive[i] = true; life[i]++; }
                }
                if (now > until) break;
            }
            return run;
        }

        static bool WP03Near(Vector3 a, Vector3 b, float tolerance = WP03Eps) => (a - b).magnitude <= tolerance;
        static string WP03Ids(IEnumerable<int> ids) => string.Join(",", ids.OrderBy(x => x));
        static readonly int[] WP03Rates = { 30, 60, 120, 1 };

        static partial void RunWP03(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W03", false, "WP-03 cases need the table"); return; }
            SpellRow Row(string handler)
            {
                var rows = c.Build.Rows.Where(x => x.Feature == SpellLegacyFeature.None && x.Handler == handler).ToArray();
                return rows.Length == 1 ? rows[0] : null;
            }
            SpellRow cone = Row("cone.backblast"), blast = Row("single.blast"), skip = Row("single.skip"), spine = Row("path.stalagmite"), tide = Row("path.tide");
            if (!r.Check("W03", cone != null && blast != null && skip != null && spine != null && tide != null, "each of the five WP-03 handlers has exactly one row")) return;
            WP03Rows(r, c, cone, blast, skip, spine, tide);
            WP03Shared(r);
            WP03Backblast(r, cone);
            WP03ImpactBlast(r, blast);
            WP03Skip(r, skip);
            WP03Stalagmite(r, c, spine);
            WP03Tide(r, c, tide);
        }

        // ---- the rows: inherited numbers, parameters, gate ----
        static void WP03Rows(Report r, Context c, SpellRow cone, SpellRow blast, SpellRow skip, SpellRow spine, SpellRow tide)
        {
            var keys = new Dictionary<SpellRow, string[]>
            {
                { cone, new[] { "blast.radius", "blast.delay", "blast.power" } },
                { blast, new[] { "blast.radius", "blast.power" } },
                { skip, new[] { "skip.distance", "skip.delay", "skip.radius", "skip.power" } },
                { spine, new[] { "second.delay", "second.power" } },
                { tide, new[] { "return.pause", "return.speed", "return.power" } },
            };
            foreach (var pair in keys)
            {
                var row = pair.Key; var basis = c.Row(SpellGrammar308.BaseOf(row.Letter[0]));
                string name = row.Handler;
                r.Check("W03", row.Category == SpellCategory.Attack && row.Final != SpellFinal.None && row.Gate == SpellGateMode.Final && string.IsNullOrEmpty(row.Pending),
                    name + ": an attack row behind its final consonant, not reserved");
                r.Check("W03", basis != null && basis.Final == SpellFinal.None && basis.HasHandler && row.BasePower > 0f && row.BasePower == basis.BasePower &&
                    row.ProjectileSpeedMul == basis.ProjectileSpeedMul && row.Kind == basis.Kind && row.AreaShape == basis.AreaShape && row.AreaAngle == basis.AreaAngle &&
                    row.AreaRadius == basis.AreaRadius && row.AreaLength == basis.AreaLength && row.AreaSpeed == basis.AreaSpeed && row.AreaImpactDelay == basis.AreaImpactDelay,
                    name + ": base power, speed, kind and area geometry are the base glyph's (Inherit base)", row.BasePower + " " + row.AreaShape);
                r.Check("W03", pair.Value.All(row.Has) && row.Params.Length == pair.Value.Length, name + ": the row carries exactly the keys its handler reads",
                    string.Join(";", row.Params.Select(p => p.Key)));
                var open = new ModelGate(); open.Registered.Add(row.Handler); open.Unlocked.Add(row.Final);
                var locked = new ModelGate(); locked.Registered.Add(row.Handler);
                var status = SpellResolveCore308.Resolve(row.Letter[0], .37f, .2f, row, open, out SpellCast cast);
                r.Check("W03", status == SpellResolveStatus.Ok && r.Near(cast.Power, row.BasePower * .37f) && cast.Kind == row.Kind && cast.AreaShape == row.AreaShape && cast.Brush01 == .2f,
                    name + ": resolves with base power x brush when its handler is registered and its final is unlocked", status + " " + cast.Power);
                r.Check("W03", SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, locked, out _) == SpellResolveStatus.Locked &&
                    SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, new ModelGate(), out _) == SpellResolveStatus.Unavailable &&
                    SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, new LegacySpellGate(true, true, true, true, true), out _) == SpellResolveStatus.Unavailable,
                    name + ": Locked behind its final, Unavailable without its handler, never opened by the old flag overloads");
            }
            r.Check("W03", cone.AreaShape == AreaShape.Cone && blast.AreaShape == AreaShape.None && skip.AreaShape == AreaShape.None &&
                spine.AreaShape == AreaShape.Path && tide.AreaShape == AreaShape.Path, "primary shapes: cone, single, single, path, path");
            r.Check("W03", cone.Element == Element.Fire && blast.Element == Element.Earth && skip.Element == Element.Earth && spine.Element == Element.Earth && tide.Element == Element.Water,
                "elements follow the initial consonant");
            r.Check("W03", spine.Final == SpellFinal.Giyeok && tide.Final == SpellFinal.Giyeok && c.Build.Unlocks.Any(u => u.Final == SpellFinal.Giyeok && u.GrantedInMain) &&
                !c.Build.Unlocks.Any(u => (u.Final == cone.Final || u.Final == blast.Final || u.Final == skip.Final) && u.GrantedInMain),
                "the two giyeok-final rows open in the main game; the nieun and ieung finals are TEST unlock only");
        }

        // ---- shared pieces ----
        static void WP03Shared(Report r)
        {
            var clock = new TwoStageClock308();
            float first = clock.Advance(1f), second = clock.Advance(2f); clock.Reset(); float afterReset = clock.Advance(3f);
            r.Check("W03", float.IsNegativeInfinity(first) && second == 1f && float.IsNegativeInfinity(afterReset), "the judging clock is the previous frame's clock; nothing is due on the first frame or after a reset");
            r.Check("W03", !TwoStageRule308.Due(5f, float.NegativeInfinity) && !TwoStageRule308.Due(5f, 4.99f) && TwoStageRule308.Due(5f, 5f) && TwoStageRule308.Due(5f, 9f) &&
                !TwoStageRule308.Due(float.NaN, 9f) && !TwoStageRule308.Due(float.PositiveInfinity, 9f), "a stage is due once the judging clock reaches its time; a time that is not a number is never due");
            var alive3 = new SpellActorSnap(0, Vector3.zero, true, 3, 1f, false);
            r.Check("W03", TwoStageRule308.SameLife(alive3, alive3) && !TwoStageRule308.SameLife(alive3, new SpellActorSnap(0, Vector3.zero, true, 4, 1f, false)) &&
                !TwoStageRule308.SameLife(alive3, new SpellActorSnap(0, Vector3.zero, false, 3, 0f, false)) &&
                !TwoStageRule308.SameLife(new SpellActorSnap(0, Vector3.zero, false, 3, 0f, false), alive3), "same life = alive at cast, alive now, same life number");
            var one = new[] { alive3 }; var orders = new List<SpellHitOrder>();
            r.Check("W03", TwoStageRule308.Circle(one, one, Vector3.zero, 0f, -1, 1f, 5f, Element.Earth, orders) == 0 && TwoStageRule308.Circle(one, one, Vector3.zero, 2f, -1, 1f, 0f, Element.Earth, orders) == 0 &&
                TwoStageRule308.Circle(null, one, Vector3.zero, 2f, -1, 1f, 5f, Element.Earth, orders) == 0 && TwoStageRule308.Circle(one, one, Vector3.zero, float.NaN, -1, 1f, 5f, Element.Earth, orders) == 0 &&
                orders.Count == 0 && TwoStageRule308.Circle(one, one, Vector3.zero, 2f, -1, 1f, 5f, Element.Earth, orders) == 1,
                "a circle judgement without radius, power or snapshots orders nothing");
            var broken = new[]
            {
                new PathFront308(Vector3.zero, Vector3.forward, 1f, 10f, 0f, 1f, false), new PathFront308(Vector3.zero, Vector3.forward, 1f, 0f, 5f, 1f, false),
                new PathFront308(Vector3.zero, Vector3.forward, 0f, 10f, 5f, 1f, false), new PathFront308(Vector3.zero, Vector3.forward, 1f, 10f, 5f, float.PositiveInfinity, true),
            };
            orders.Clear(); var passed = new bool[1];
            r.Check("W03", broken.All(f => !f.Valid && PathFrontRule308.Sweep(f, one, one, passed, 99f, 5f, Element.Earth, orders) == 0) && !passed[0],
                "a front without width, length, speed or a finite start is not valid and judges nobody");
            var front = new PathFront308(new Vector3(1f, 0f, 1f), new Vector3(0f, 3f, 2f), 1f, 10f, 5f, 4f, false);
            var back = new PathFront308(new Vector3(1f, 0f, 1f), Vector3.forward, 1f, 10f, 5f, 4f, true);
            r.Check("W03", front.Valid && WP03Near(front.Direction, Vector3.forward) && front.Along(3f) == 0f && r.Near(front.Along(5f), 5f) && front.Along(6f) == 10f && front.Along(99f) == 10f &&
                r.Near(front.EndsAt, 6f) && !front.Started(3.9f) && front.Started(4f) && !front.Finished(5.9f) && front.Finished(6f) && r.Near(front.ArrivalAt(5f), 5f) &&
                WP03Near(front.Point(5f), new Vector3(1f, 0f, 6f)), "an outbound front runs 0 -> length on a flat unit direction and stays at the far end");
            r.Check("W03", back.Along(3f) == 10f && r.Near(back.Along(5f), 5f) && back.Along(6f) == 0f && r.Near(back.ArrivalAt(8f), 4.4f) && back.Reached(8f, 4.5f) && !back.Reached(8f, 4.3f) &&
                !back.Reached(8f, 3f) && front.Reached(2f, 4.5f) && !front.Reached(3f, 4.5f), "a returning front runs length -> 0; a place is reached once the front is at it or beyond");
            orders.Clear();
            r.Check("W03", PathFrontRule308.Sweep(front, one, one, passed, 3.9f, 5f, Element.Earth, orders) == 0 && !passed[0], "a front judges nobody before it starts");
        }

        // ---- cone + burst at the far end ----
        static void WP03Backblast(Report r, SpellRow row)
        {
            float radius = row.F("blast.radius", 0f), delay = row.F("blast.delay", 0f), scale = row.F("blast.power", 0f);
            // test geometry: the game's cone angle and range live in CombatConfigSO; any cone longer than the burst is wide will do
            Vector3 origin = new Vector3(3.5f, 0f, -2.25f), forward = new Vector3(.6f, 0f, .8f), right = new Vector3(.8f, 0f, -.6f);
            float range = radius * 2f + 4f; const float halfAngle = 30f, primaryDelay = .3f, castAt = 100f;
            float castPower = row.BasePower * .37f, primaryAt = castAt + primaryDelay;
            var plan = BackblastRule308.Plan(origin, forward, range, primaryAt, castPower, radius, delay, scale, row.Element);
            Vector3 end = origin + forward * range;

            bool onAxis = AreaGeometry.InCone(origin, forward, plan.Centre, .1f, range + WP03Eps, out _, out float reach);
            r.Check("논-1", WP03Near(plan.Centre, end) && onAxis && r.Near(reach, range, WP03Eps), "the burst centre is the far end of the cone axis: the cone's range along its flat forward",
                plan.Centre + " reach " + reach);
            r.Check("논-1", WP03Near(BackblastRule308.EndPoint(origin, forward * 3f + Vector3.up * 2f, range), end) && WP03Near(BackblastRule308.EndPoint(origin, Vector3.up, range), origin + Vector3.forward * range),
                "a pitched or unnormalised facing gives the same level end point");

            var world = new WP03World(castAt,
                end - forward * .2f,                          // 0 just inside the cone's far end
                end + right * (radius - .2f),                 // 1 beside the end point
                end + forward * (radius - .2f),               // 2 beyond the cone's range
                origin + forward * 1.5f,                      // 3 close to the caster
                end + forward * (radius + .2f),               // 4 just outside the burst
                origin - forward * 5f,                        // 5 behind the caster
                end - forward * (radius - .1f) + right * .1f, // 6 on the near edge of the burst
                end - forward * 1f - right * .5f,             // 7 killed by the cone
                end - forward * .5f + right * .5f,            // 8 hit by the cone, then restored into another life before the burst
                end + forward * .3f);                         // 9 dead at cast, alive again at the burst
            int n = world.Count;
            var inCone = new bool[n]; var inBurst = new bool[n];
            for (int i = 0; i < n; i++)
            {
                inCone[i] = AreaGeometry.InCone(origin, forward, world.Home[i], halfAngle, range, out _, out _);
                inBurst[i] = AreaGeometry.InCircle(end, world.Home[i], radius, out _);
                if (inCone[i]) world.PrimaryAt[i] = primaryAt;
            }
            world.PrimaryKills[7] = true; world.RestoredAt[8] = primaryAt + delay * .5f; world.AliveAtCast[9] = false; world.RestoredAt[9] = castAt + .1f; world.PrimaryAt[9] = float.PositiveInfinity;

            // standing, nobody changes life: the rule against the independent circle
            var atCast = world.AtCast(); var all = new WP03World(castAt, world.Home).AtCast(); var orders = new List<SpellHitOrder>();
            BackblastRule308.Resolve(plan, all, all, orders);
            var want = Enumerable.Range(0, n).Where(i => inBurst[i]).ToArray();
            r.Check("논-1", orders.Select(o => o.TargetId).OrderBy(x => x).SequenceEqual(want) && orders.Select(o => o.TargetId).Distinct().Count() == orders.Count,
                "the burst hits exactly the enemies inside its radius around the end point, each once", "got " + WP03Ids(orders.Select(o => o.TargetId)) + " want " + WP03Ids(want));
            r.Check("논-1", inBurst[2] && !inCone[2] && inCone[3] && !inBurst[3] && inCone[0] && inBurst[0] && !inCone[5] && !inBurst[5] && !inBurst[4],
                "the scenario holds a burst-only enemy beyond the cone, a cone-only enemy near the caster, one in both and two in neither");

            r.Check("논-2", scale > 1f && plan.Power > castPower && r.Near(plan.Power, castPower * scale) && orders.All(o => r.Near(o.Power, castPower * scale) && o.Power > castPower),
                "the burst is the bigger hit: cast power x blast.power, and the sheet keeps blast.power above 1", "scale " + scale);

            r.Check("논-3", delay > 0f && r.Near(plan.At, primaryAt + delay) && plan.At > primaryAt && orders.All(o => o.At == plan.At),
                "the burst comes blast.delay seconds after the cone lands", "delay " + delay);
            var expected = Enumerable.Range(0, n).Where(i => inBurst[i] && i != 7 && i != 8 && i != 9).ToArray();
            foreach (int rate in WP03Rates)
            {
                bool fired = false; float firedJudged = float.NaN; int firedFrame = -1, frame = -1;
                var run = WP03Frames(world, rate, plan.At + 3f, (judged, now, list) =>
                {
                    frame++;
                    if (fired || !TwoStageRule308.Due(plan.At, judged)) return;
                    fired = true; firedJudged = judged; firedFrame = frame;
                    BackblastRule308.Resolve(plan, atCast, now, list);
                });
                var got = Enumerable.Range(0, n).Where(i => run.SecondHits[i] > 0).ToArray();
                bool once = run.SecondHits.All(h => h <= 1) && run.Judgements == 1;
                bool timed = firedJudged >= plan.At && firedJudged - plan.At < 1f / rate + WP03Eps;
                bool ordered = Enumerable.Range(0, n).All(i => run.PrimaryFrame[i] < 0 || run.PrimaryFrame[i] < firedFrame);
                r.Check("논-3", fired && once && timed && ordered && got.SequenceEqual(expected),
                    rate + " fps: the burst is judged once, with the first frame clock that reached its time, after every cone hit was applied; the enemy the cone killed, the restored one and the one dead at cast get nothing",
                    "judged " + firedJudged + " at " + plan.At + " got " + WP03Ids(got) + " want " + WP03Ids(expected));
            }
        }

        // ---- single shot + burst at the impact point ----
        static void WP03ImpactBlast(Report r, SpellRow row)
        {
            float radius = row.F("blast.radius", 0f), scale = row.F("blast.power", 0f);
            const float castAt = 200f, impactAt = 200.42f;
            float castPower = row.BasePower * .8f;
            Vector3 origin = new Vector3(-4f, 0f, 2f), forward = new Vector3(.6f, 0f, .8f), right = new Vector3(.8f, 0f, -.6f);
            Vector3 targetAtCast = origin + forward * 6f, impact = targetAtCast + right * 1.5f;      // the target walked sideways while the shot flew
            var cast = new[]
            {
                new SpellActorSnap(0, targetAtCast, true, 10, 1f, false),                               // the primary's target
                new SpellActorSnap(1, impact + right * (radius + 3f), true, 11, 1f, false),             // walks into the burst
                new SpellActorSnap(2, targetAtCast - right * (radius - .2f), true, 12, 1f, false),      // near the target at cast, out of the burst at impact
                new SpellActorSnap(3, impact + forward * (radius - .1f), true, 13, 1f, false),          // inside all along
                new SpellActorSnap(4, impact + forward * (radius + .1f), true, 14, 1f, false),          // just outside
                new SpellActorSnap(5, impact - forward * .5f, true, 15, 1f, false),                     // dies before the impact
                new SpellActorSnap(6, impact + right * .5f, true, 16, 1f, false),                       // restored into another life
                new SpellActorSnap(7, impact - right * .5f, false, 17, 0f, false),                      // dead at cast, alive at impact
            };
            var now = (SpellActorSnap[])cast.Clone();
            now[0] = new SpellActorSnap(0, impact, true, 10, .5f, false);
            now[1] = new SpellActorSnap(1, impact + right * (radius - .3f), true, 11, 1f, false);
            now[2] = new SpellActorSnap(2, targetAtCast - right * (radius - .2f), true, 12, 1f, false);
            now[5] = new SpellActorSnap(5, impact - forward * .5f, false, 16, 0f, false);
            now[6] = new SpellActorSnap(6, impact + right * .5f, true, 18, 1f, false);
            now[7] = new SpellActorSnap(7, impact - right * .5f, true, 18, 1f, false);
            var orders = new List<SpellHitOrder>();
            int count = ImpactBlastRule308.Plan(cast, now, 0, impact, impactAt, castPower * scale, radius, row.Element, orders);
            var want = Enumerable.Range(1, now.Length - 1).Where(i => cast[i].Alive && now[i].Alive && cast[i].Life == now[i].Life &&
                AreaGeometry.InCircle(impact, now[i].Position, radius, out _)).ToArray();

            r.Check("만-1", count == orders.Count && orders.Count > 0 && orders.All(o => o.At == impactAt) && !row.Has("blast.delay"),
                "the burst is judged at the moment of the primary's impact: every order carries the impact time, the row has no delay", "orders " + orders.Count);
            r.Check("만-2", orders.Select(o => o.TargetId).OrderBy(x => x).SequenceEqual(want) && want.SequenceEqual(new[] { 1, 3 }) &&
                orders.Select(o => o.TargetId).Distinct().Count() == orders.Count,
                "the burst hits the enemies inside its radius around the impact point as they stand at the impact, each once", "got " + WP03Ids(orders.Select(o => o.TargetId)) + " want " + WP03Ids(want));
            r.Check("만-2", orders.All(o => o.TargetId != 0) && AreaGeometry.InCircle(impact, now[0].Position, radius, out _),
                "the primary's own target stands at the centre and is never hit by the burst");
            r.Check("만-2", radius > 0f && orders.All(o => r.Near(o.Power, castPower * scale) && o.Element == row.Element), "burst power = cast power x blast.power, in the cast's element",
                "radius " + radius + " scale " + scale);
            var none = new List<SpellHitOrder>();
            r.Check("만-2", ImpactBlastRule308.Plan(cast, now, -1, impact, impactAt, castPower * scale, radius, row.Element, none) == 0 &&
                ImpactBlastRule308.Plan(cast, now, cast.Length, impact, impactAt, castPower * scale, radius, row.Element, none) == 0 && none.Count == 0,
                "no impact on a target, no burst (a shot into the air bursts nothing)");
            var kept = new List<SpellHitOrder>();
            ImpactBlastRule308.Plan(cast, cast, 0, targetAtCast, impactAt, castPower * scale, radius, row.Element, kept);
            r.Check("만-2", kept.Any(o => o.TargetId == 2) && !orders.Any(o => o.TargetId == 2) && !kept.Any(o => o.TargetId == 1),
                "positions are the ones at the impact, not at the cast: who left is spared, who came in is hit");
        }

        // ---- single shot + skip to a second point ----
        static void WP03Skip(Report r, SpellRow row)
        {
            float distance = row.F("skip.distance", 0f), delay = row.F("skip.delay", 0f), radius = row.F("skip.radius", 0f), scale = row.F("skip.power", 0f);
            const float castAt = 300f, flight = .4f;
            float castPower = row.BasePower * .5f;
            Vector3 origin = new Vector3(2f, .2f, -3f), facing = new Vector3(-1f, 0f, 0f), forward = new Vector3(.6f, 0f, .8f), right = new Vector3(.8f, 0f, -.6f);
            Vector3 impact = origin + forward * 5f + Vector3.up * .3f;
            Vector3 second = SkipRule308.SecondPoint(origin, facing, impact, distance);
            Vector3 flatTravel = AreaGeometry.Flat(impact - origin), flatSkip = AreaGeometry.Flat(second - impact);

            r.Check("망-2", distance > 0f && r.Near(flatSkip.magnitude, distance, WP03Eps) && Vector3.Cross(flatTravel, flatSkip).magnitude < WP03Eps && Vector3.Dot(flatTravel, flatSkip) > 0f &&
                r.Near(AreaGeometry.Flat(second - origin).magnitude, flatTravel.magnitude + distance, WP03Eps) && r.Near(second.y, impact.y),
                "the second point lies skip.distance beyond the impact point, on the line the shot travelled, away from the caster, at the impact's height", second.ToString());
            r.Check("망-2", WP03Near(SkipRule308.Direction(origin, facing, impact), forward) && WP03Near(SkipRule308.Direction(origin, facing, origin + Vector3.up), facing) &&
                WP03Near(SkipRule308.SecondPoint(origin, facing, impact, -3f), impact), "the direction is the shot's flat travel (the caster's facing only when the shot landed on the caster's own spot)");

            var world = new WP03World(castAt,
                impact,                                   // 0 the primary's target
                second,                                   // 1 at the second point
                second + right * (radius - .1f),          // 2 inside the radius
                second + right * (radius + .1f),          // 3 just outside
                second - forward * (radius + .3f),        // 4 between the two points, outside
                second + forward * .4f,                   // 5 dies before the second landing
                second - right * .4f);                    // 6 restored into another life
            int n = world.Count;
            float impactAt = castAt + flight;
            world.PrimaryAt[0] = impactAt; world.DiesAt[5] = impactAt + delay * .5f; world.RestoredAt[6] = impactAt + delay * .5f;
            var standing = new WP03World(castAt, world.Home).AtCast();
            var plan = SkipRule308.Plan(origin, facing, impact, impactAt, castPower, distance, delay, radius, scale, row.Element);
            var orders = new List<SpellHitOrder>();
            SkipRule308.Resolve(plan, standing, standing, orders);
            var want = Enumerable.Range(0, n).Where(i => AreaGeometry.InCircle(second, world.Home[i], radius, out _)).ToArray();

            r.Check("망-1", delay > 0f && r.Near(plan.At, impactAt + delay) && plan.At > impactAt && WP03Near(plan.Point, second), "the second landing comes skip.delay seconds after the primary's impact",
                "delay " + delay);
            r.Check("망-3", orders.Select(o => o.TargetId).OrderBy(x => x).SequenceEqual(want) && want.Contains(1) && want.Contains(2) && !want.Contains(3) &&
                orders.Select(o => o.TargetId).Distinct().Count() == orders.Count && orders.All(o => r.Near(o.Power, castPower * scale) && o.At == plan.At),
                "the second landing hits exactly the enemies inside skip.radius of the second point, each once, with cast power x skip.power",
                "got " + WP03Ids(orders.Select(o => o.TargetId)) + " want " + WP03Ids(want));
            var expected = want.Where(i => i != 5 && i != 6).ToArray();
            var atCast = world.AtCast();
            foreach (int rate in WP03Rates)
            {
                bool landed = false, fired = false; SkipPlan308 live = default; float firedJudged = float.NaN; int firedFrame = -1, frame = -1;
                var run = WP03Frames(world, rate, impactAt + delay + 3f, (judged, now, list) =>
                {
                    frame++;
                    if (!landed || fired || !TwoStageRule308.Due(live.At, judged)) return;
                    fired = true; firedJudged = judged; firedFrame = frame;
                    SkipRule308.Resolve(live, atCast, now, list);
                }, (actor, now) =>
                {
                    // the hit hook: the plan is fixed when the primary really lands, with that frame's clock and the target's position then
                    landed = true; live = SkipRule308.Plan(origin, facing, world.PositionAt(actor, now), now, castPower, distance, delay, radius, scale, row.Element);
                });
                var got = Enumerable.Range(0, n).Where(i => run.SecondHits[i] > 0).ToArray();
                bool timed = firedJudged >= live.At && firedJudged - live.At < 1f / rate + WP03Eps && live.At >= impactAt + delay - WP03Eps;
                r.Check("망-1", landed && fired && timed && run.PrimaryFrame[0] >= 0 && run.PrimaryFrame[0] < firedFrame,
                    rate + " fps: the second landing is judged after the primary landed, with the first frame clock that reached impact + skip.delay",
                    "judged " + firedJudged + " at " + live.At);
                r.Check("망-3", run.SecondHits.All(h => h <= 1) && run.Judgements == 1 && got.SequenceEqual(expected),
                    rate + " fps: one judgement, each enemy at most once; the enemy that died and the restored one get nothing", "got " + WP03Ids(got) + " want " + WP03Ids(expected));
            }
            var missed = new WP03World(castAt, world.Home); missed.PrimaryAt[0] = impactAt; missed.DiesAt[0] = castAt + .1f;
            bool hooked = false;
            var missedRun = WP03Frames(missed, 60, impactAt + delay + 2f, (judged, now, list) => { }, (actor, now) => hooked = true);
            r.Check("망-1", !hooked && missedRun.PrimaryFrame[0] < 0, "a primary whose target is gone before it lands never lands: no second point is made");
        }

        // ---- path + a second front trailing the storm front ----
        static void WP03Stalagmite(Report r, Context c, SpellRow row)
        {
            float lag = row.F("second.delay", 0f), scale = row.F("second.power", 0f);
            float halfWidth = row.AreaRadius, length = row.AreaLength, speed = row.AreaSpeed, stormDelay = row.AreaImpactDelay;
            const float castAt = 50f;
            float castPower = row.BasePower * .9f, stormStartsAt = castAt + stormDelay;
            Vector3 start = new Vector3(3.5f, 0f, -2.25f), forward = new Vector3(.6f, 0f, .8f), right = new Vector3(.8f, 0f, -.6f);
            var front = StalagmiteRule308.Front(start, forward, halfWidth, length, speed, stormStartsAt, lag);
            var basis = c.Row(SpellGrammar308.BaseOf(row.Letter[0]));

            r.Check("목-1", basis != null && row.AreaShape == AreaShape.Path && halfWidth == basis.AreaRadius && length == basis.AreaLength && speed == basis.AreaSpeed &&
                stormDelay == basis.AreaImpactDelay && row.BasePower == basis.BasePower && halfWidth > 0f && length > 0f && speed > 0f,
                "the primary is the base glyph's corridor: same half width, length, front speed, start delay and base power", halfWidth + " " + length + " " + speed + " " + stormDelay);
            r.Check("목-1", front.Valid && !front.Returning && WP03Near(front.Start, start) && WP03Near(front.Direction, forward) && front.HalfWidth == halfWidth && front.Length == length && front.Speed == speed,
                "the second front runs the storm's own corridor at the storm's speed, in the storm's direction");

            var world = new WP03World(castAt,
                start + forward * 1f,                                            // 0 near the caster
                start + forward * (length * .45f) + right * (halfWidth - .5f),   // 1 near the corridor's edge
                start + forward * (length - .25f) - right * 1f,                  // 2 near the far end
                start + forward * (length * .5f) + right * (halfWidth + .5f),    // 3 outside the width
                start + forward * (length + 1f),                                 // 4 beyond the far end
                start - forward * 2f,                                            // 5 behind the caster
                start + forward * (length * .65f),                               // 6 on the axis
                start + forward * (length * .3f) - right * .5f,                  // 7 killed by the storm
                start + forward * (length * .8f) + right * .5f);                 // 8 restored into another life between the two fronts
            int n = world.Count;
            var inside = new bool[n]; var along = new float[n]; var primaryAt = new float[n]; var secondAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                inside[i] = AreaGeometry.InCorridor(start, forward, world.Home[i], halfWidth, length, out along[i], out _);
                primaryAt[i] = stormStartsAt + along[i] / speed; secondAt[i] = primaryAt[i] + lag;
                if (inside[i]) world.PrimaryAt[i] = primaryAt[i];
            }
            world.PrimaryKills[7] = true; world.RestoredAt[8] = primaryAt[8] + lag * .5f;
            r.Check("목-1", inside[0] && inside[1] && inside[2] && inside[6] && inside[7] && inside[8] && !inside[3] && !inside[4] && !inside[5],
                "the scenario holds six enemies inside the corridor and three outside (too wide, too far, behind)");

            r.Check("목-2", lag > 0f && r.Near(front.StartsAt, stormStartsAt + lag) && Enumerable.Range(0, n).Where(i => inside[i]).All(i =>
                    Mathf.Abs(front.ArrivalAt(along[i]) - secondAt[i]) < WP03Eps && secondAt[i] > primaryAt[i]),
                "the second front reaches every place second.delay seconds after the storm front did", "lag " + lag);

            var atCast = world.AtCast();
            var expected = Enumerable.Range(0, n).Where(i => inside[i] && i != 7 && i != 8).ToArray();
            foreach (int rate in WP03Rates)
            {
                var passed = new bool[n];
                var run = WP03Frames(world, rate, front.EndsAt + 3f, (judged, now, list) => PathFrontRule308.Sweep(front, atCast, now, passed, judged, castPower * scale, row.Element, list));
                var got = Enumerable.Range(0, n).Where(i => run.SecondHits[i] > 0).ToArray();
                bool timed = expected.All(i => run.SecondJudged[i] >= secondAt[i] - WP03Eps && run.SecondJudged[i] < secondAt[i] + 1f / rate + WP03Eps);
                bool ordered = expected.All(i => run.PrimaryFrame[i] >= 0 && run.PrimaryFrame[i] < run.SecondFrame[i]);
                r.Check("목-2", timed && ordered, rate + " fps: each enemy's second hit is judged with the first frame clock that reached its storm hit + second.delay, after its storm hit was applied",
                    string.Join(" ", expected.Select(i => i + ":" + (run.SecondJudged[i] - secondAt[i]).ToString("0.###"))));
                r.Check("목-3", got.SequenceEqual(expected) && run.SecondHits.All(h => h <= 1) && expected.All(i => r.Near(run.SecondPower[i], castPower * scale)),
                    rate + " fps: the second front hits the living enemies inside the corridor once each with cast power x second.power; the one the storm killed and the restored one get nothing",
                    "got " + WP03Ids(got) + " want " + WP03Ids(expected));
            }

            // enemies that move between the two fronts (positions are the ones at the moment the front reaches them)
            float edge = halfWidth + 3f;
            var moving = new WP03World(castAt,
                start + forward * (length * .75f),               // 0 walks against the front, towards the caster
                start + forward * (length * .5f),                // 1 leaves the corridor sideways before the front arrives
                start + forward * (length * .8f) + right * edge, // 2 steps into the corridor ahead of the front
                start + forward * 2f + right * edge,             // 3 steps into the corridor behind the front
                start + forward * (length * .6f) - right * 1f);  // 4 dies between the two fronts
            moving.Velocity[0] = -forward * 4f;
            moving.Velocity[1] = right * 20f;
            moving.Velocity[2] = -right * 3f;                                  // inside the width one second after the cast, still inside when the front arrives
            moving.Velocity[3] = -right * 20f; moving.MovesFrom[3] = front.ArrivalAt(2f) + .5f;
            moving.DiesAt[4] = front.ArrivalAt(length * .6f) - .2f;
            var movingAtCast = moving.AtCast();
            foreach (int rate in WP03Rates.Where(x => x >= 30))
            {
                var passed = new bool[moving.Count];
                var run = WP03Frames(moving, rate, front.EndsAt + 3f, (judged, now, list) => PathFrontRule308.Sweep(front, movingAtCast, now, passed, judged, castPower * scale, row.Element, list));
                r.Check("목-3", run.SecondHits.SequenceEqual(new[] { 1, 0, 1, 0, 0 }),
                    rate + " fps, moving enemies: walking against the front = hit once (cannot slip between two frames), left the corridor = spared, stepped in ahead = hit once, stepped in behind = spared, died first = nothing",
                    string.Join(",", run.SecondHits));
            }
        }

        // ---- path + returning front ----
        static void WP03Tide(Report r, Context c, SpellRow row)
        {
            float pause = row.F("return.pause", 0f), speedScale = row.F("return.speed", 0f), scale = row.F("return.power", 0f);
            float halfWidth = row.AreaRadius, length = row.AreaLength, speed = row.AreaSpeed, waveDelay = row.AreaImpactDelay;
            const float castAt = 70f;
            float castPower = row.BasePower * .6f, outStartsAt = castAt + waveDelay, turnAt = outStartsAt + length / speed, backSpeed = speed * speedScale;
            Vector3 start = new Vector3(-6f, 0f, 4f), forward = new Vector3(.8f, 0f, -.6f), right = new Vector3(-.6f, 0f, -.8f);
            var back = TideRule308.Return(start, forward, halfWidth, length, speed, outStartsAt, pause, speedScale);
            var basis = c.Row(SpellGrammar308.BaseOf(row.Letter[0]));

            var world = new WP03World(castAt,
                start + forward * 1.5f,                                          // 0 near the caster
                start + forward * (length * .5f) - right * (halfWidth - .5f),    // 1 near the corridor's edge
                start + forward * (length - .5f) + right * 1f,                   // 2 near the far end
                start + forward * (length * .5f) + right * (halfWidth + .5f),    // 3 outside the width
                start + forward * (length + 1.5f),                               // 4 beyond the far end
                start - forward * 2f,                                            // 5 behind the caster
                start + forward * (length * .7f),                                // 6 on the axis
                start + forward * (length * .35f) + right * .5f);                // 7 killed by the wave on the way out
            int n = world.Count;
            var inside = new bool[n]; var along = new float[n]; var outAt = new float[n]; var backAt = new float[n];
            for (int i = 0; i < n; i++)
            {
                inside[i] = AreaGeometry.InCorridor(start, forward, world.Home[i], halfWidth, length, out along[i], out _);
                outAt[i] = outStartsAt + along[i] / speed; backAt[i] = turnAt + pause + (length - along[i]) / backSpeed;
                if (inside[i]) world.PrimaryAt[i] = outAt[i];
            }
            world.PrimaryKills[7] = true;
            var standingInside = Enumerable.Range(0, n).Where(i => inside[i]).ToArray();

            r.Check("옥-1", basis != null && row.AreaShape == AreaShape.Path && halfWidth == basis.AreaRadius && length == basis.AreaLength && speed == basis.AreaSpeed &&
                waveDelay == basis.AreaImpactDelay && row.BasePower == basis.BasePower && halfWidth > 0f && length > 0f && speed > 0f,
                "the way out is the base glyph's corridor: same half width, length, front speed, start delay and base power", halfWidth + " " + length + " " + speed + " " + waveDelay);
            r.Check("옥-1", r.Near(TideRule308.TurnAt(outStartsAt, length, speed), turnAt) && standingInside.Length == 5 && standingInside.All(i => outAt[i] <= turnAt + WP03Eps) && !inside[3] && !inside[4] && !inside[5],
                "the wave runs to the far end of its corridor: every enemy in it is reached on the way out before the wave turns", "turn " + turnAt);

            r.Check("옥-2", back.Valid && back.Returning && r.Near(back.StartsAt, turnAt + pause) && back.StartsAt >= turnAt && WP03Near(back.Start, start) && WP03Near(back.Direction, forward) &&
                back.HalfWidth == halfWidth && back.Length == length && r.Near(back.Speed, backSpeed) && speedScale > 0f,
                "the returning front starts return.pause seconds after the wave reached the far end, on the same corridor, at the outbound speed x return.speed", "pause " + pause + " speed " + back.Speed);
            bool falls = true; float before = float.PositiveInfinity;
            for (int k = 0; k <= 20; k++)
            {
                float position = back.Along(back.StartsAt + (back.EndsAt - back.StartsAt) * k / 20f);
                falls &= position <= before + 1e-4f; before = position;
            }
            r.Check("옥-2", falls && back.Along(back.StartsAt) == length && back.Along(back.EndsAt) == 0f && WP03Near(back.Point(back.StartsAt), start + forward * length) && WP03Near(back.Point(back.EndsAt), start) &&
                r.Near(back.EndsAt, turnAt + pause + length / backSpeed, WP03Eps), "it runs from the far end back to the caster's spot and stops there");
            r.Check("옥-2", standingInside.All(i => Mathf.Abs(back.ArrivalAt(along[i]) - backAt[i]) < WP03Eps && backAt[i] > outAt[i]) &&
                standingInside.All(i => standingInside.All(j => along[i] <= along[j] || (backAt[i] < backAt[j] && outAt[i] > outAt[j]))),
                "on the way back the far enemies are reached first: the order of the way out, reversed");

            var atCast = world.AtCast();
            var expected = standingInside.Where(i => i != 7).ToArray();
            foreach (int rate in WP03Rates)
            {
                var passed = new bool[n];
                var run = WP03Frames(world, rate, back.EndsAt + 3f, (judged, now, list) => PathFrontRule308.Sweep(back, atCast, now, passed, judged, castPower * scale, row.Element, list));
                var got = Enumerable.Range(0, n).Where(i => run.SecondHits[i] > 0).ToArray();
                bool timed = expected.All(i => run.SecondJudged[i] >= backAt[i] - WP03Eps && run.SecondJudged[i] < backAt[i] + 1f / rate + WP03Eps);
                bool ordered = expected.All(i => run.PrimaryFrame[i] >= 0 && run.PrimaryFrame[i] < run.SecondFrame[i]);
                int[] total = Enumerable.Range(0, n).Select(i => (run.PrimaryFrame[i] >= 0 ? 1 : 0) + run.SecondHits[i]).ToArray();
                r.Check("옥-2", got.SequenceEqual(expected) && timed && ordered,
                    rate + " fps: the returning front hits each living enemy in the corridor with the first frame clock that reached its return time, after its way-out hit was applied",
                    "got " + WP03Ids(got) + " want " + WP03Ids(expected));
                r.Check("옥-3", total.All(t => t <= 2) && expected.All(i => total[i] == 2) && total[7] == 1 && total[3] == 0 && total[4] == 0 && total[5] == 0 &&
                    run.SecondHits.All(h => h <= 1) && expected.All(i => r.Near(run.SecondPower[i], castPower * scale)),
                    rate + " fps: two hits per enemy in the corridor (one out, one back), one for the enemy the way out killed, none outside; never more than two",
                    string.Join(",", total));
            }

            var moving = new WP03World(castAt,
                start + forward * (length - 2f),                 // 0 runs for the caster when the wave turns (the same way the returning front runs)
                start + forward * 3f,                            // 1 runs away from the caster, against the returning front
                start + forward * (length * .5f));               // 2 leaves the corridor sideways before the wave comes back
            moving.Velocity[0] = -forward * 4f; moving.MovesFrom[0] = turnAt;
            moving.Velocity[1] = forward * 4f; moving.MovesFrom[1] = back.StartsAt;
            moving.Velocity[2] = right * 20f; moving.MovesFrom[2] = turnAt;
            var movingAtCast = moving.AtCast();
            foreach (int rate in WP03Rates.Where(x => x >= 30))
            {
                var passed = new bool[moving.Count];
                var run = WP03Frames(moving, rate, back.EndsAt + 3f, (judged, now, list) => PathFrontRule308.Sweep(back, movingAtCast, now, passed, judged, castPower * scale, row.Element, list));
                r.Check("옥-3", run.SecondHits.SequenceEqual(new[] { 1, 1, 0 }),
                    rate + " fps, moving enemies: overtaken from behind = one return hit, running against the front = one return hit, left the corridor = spared",
                    string.Join(",", run.SecondHits));
            }
        }
    }
}
