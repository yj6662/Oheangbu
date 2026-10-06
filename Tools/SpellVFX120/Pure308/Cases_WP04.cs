// SPEC-SPELL-120-308 L2 cases of WP-04 (effects derived from a confirmed hit): crescents, droplets, ricochet, spreading ember,
// cycle shot. Rows are found by handler id; expected sets are computed here with independent geometry (bearing / distance),
// not with the rule's own helpers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        // A point at a bearing (degrees, 0 = +z, positive = to the right) and a flat distance from a centre.
        static Vector3 WP04Polar(Vector3 centre, float bearing, float distance)
        {
            float radians = bearing * Mathf.Deg2Rad;
            return centre + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        static float WP04Bearing(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        static float WP04Distance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Independent cone test: bearing of the point seen from the apex against the cone's axis bearing.
        static bool WP04InCone(Vector3 apex, float axis, Vector3 point, float half, float range, out bool edge)
        {
            float distance = WP04Distance(apex, point);
            float off = Mathf.Abs(Mathf.DeltaAngle(WP04Bearing(apex, point), axis));
            edge = Mathf.Abs(off - half) < .05f || Mathf.Abs(distance - range) < .005f || distance < .005f;
            return distance > 1e-4f && distance <= range && off <= half;
        }

        static SpellActorSnap WP04Actor(int id, Vector3 position, bool alive = true, uint life = 1) => new SpellActorSnap(id, position, alive, life, alive ? 1f : 0f, false);

        static string WP04Ids(IEnumerable<SpellHitOrder> orders) => string.Join(",", orders.Select(o => o.TargetId));

        // Smallest spell ink cost over every CombatConfigSO asset of the project (read only; the first bytes of an asset name its script).
        static float WP04MinSpellInkCost(Context c, out int configs)
        {
            configs = 0; float min = float.PositiveInfinity;
            string meta = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project", "Scripts", "Combat", "CombatConfigSO.cs.meta");
            string assets = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project");
            if (!File.Exists(meta) || !Directory.Exists(assets)) return min;
            string guid = "";
            foreach (string line in File.ReadAllLines(meta))
                if (line.StartsWith("guid:", StringComparison.Ordinal)) guid = line.Substring(5).Trim();
            if (guid.Length == 0) return min;
            var head = new byte[2048];
            var everywhere = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (string path in Directory.EnumerateFiles(assets, "*.asset", everywhere))
            {
                try
                {
                    int read;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) read = stream.Read(head, 0, head.Length);
                    if (System.Text.Encoding.UTF8.GetString(head, 0, read).IndexOf(guid, StringComparison.Ordinal) < 0) continue;
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string text = line.Trim();
                        if (!text.StartsWith("_spellInkCost:", StringComparison.Ordinal)) continue;
                        if (float.TryParse(text.Substring(14).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float cost)) { configs++; min = Mathf.Min(min, cost); }
                        break;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return min;
        }

        // The gate every WP-04 effect shares: nothing is derived unless the wiring reports the watched attack id as a confirmed hit.
        static void WP04ConfirmedHitOnly(Report r, string clause)
        {
            var watches = new DerivedHitWatchList308();
            watches.Add(new DerivedHitWatch308 { AttackId = 0, ImpactAt = 1f });                       // a shot into the air schedules nothing
            r.Check(clause, watches.Count == 0, "a shot into the air leaves nothing to wait for (no scheduled impact, no attack id)");
            watches.Add(new DerivedHitWatch308 { AttackId = 7, ImpactAt = 2f, Power = 12f });
            bool other = watches.TryTake(9, out _);
            bool first = watches.TryTake(7, out var taken);
            bool again = watches.TryTake(7, out _);
            r.Check(clause, !other && first && taken.Power == 12f && !again,
                "only the confirmed hit of the watched attack derives, and it derives once (another attack: no, the same one twice: no)");
            watches.Add(new DerivedHitWatch308 { AttackId = 5, ImpactAt = 2f });
            int early = watches.Sweep(1.9f); int atTime = watches.Sweep(2f); bool stillThere = watches.Count == 1;
            int late = watches.Sweep(2.01f);
            r.Check(clause, early == 0 && atTime == 0 && stillThere && late == 1 && watches.Count == 0 && !watches.TryTake(5, out _),
                "a scheduled impact that never became a hit (target fell first, wall) derives nothing: its watch is dropped one tick after its time");
            watches.Add(new DerivedHitWatch308 { AttackId = 6, ImpactAt = 2f });
            watches.Sweep(2f);
            r.Check(clause, watches.TryTake(6, out _), "the hit applied in the very tick its time passed is still recognised");
        }

        static partial void RunWP04(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W04", false, "WP-04 cases need the table"); return; }
            SpellRow Row(string handler) => c.Build.Rows.SingleOrDefault(x => x.Handler == handler && x.Feature == SpellLegacyFeature.None);
            var crescent = Row("hit.crescent"); var droplets = Row("hit.droplets"); var ricochet = Row("volley.ricochet");
            var spread = Row("hit.spread"); var cycle = Row("hit.cycle");
            if (!r.Check("W04", crescent != null && droplets != null && ricochet != null && spread != null && cycle != null, "the five WP-04 rows are in the table, one per handler")) return;

            // ---- rows: finals, elements, what they inherit from the row without a final ----
            SpellRow Base(SpellRow row) => c.RowAt(row.Index - (int)row.Final);
            bool Inherits(SpellRow row)
            {
                var b = Base(row);
                return row.BasePower == b.BasePower && row.BasePower > 0f && row.ProjectileSpeedMul == b.ProjectileSpeedMul && row.Kind == b.Kind && row.AreaShape == b.AreaShape &&
                    row.AreaAngle == b.AreaAngle && row.AreaLength == b.AreaLength && row.AreaSpeed == b.AreaSpeed && row.AreaImpactDelay == b.AreaImpactDelay &&
                    row.VolleyShots == b.VolleyShots && row.VolleyInterval == b.VolleyInterval && row.ScatterVolley == b.ScatterVolley;
            }
            r.Check("W04", Inherits(crescent) && Inherits(droplets) && Inherits(ricochet) && Inherits(spread) && Inherits(cycle),
                "each row carries the base power, projectile speed, kind and area geometry of its row without a final");
            r.Check("W04", crescent.Final == SpellFinal.Siot && droplets.Final == SpellFinal.Nieun && ricochet.Final == SpellFinal.Giyeok && spread.Final == SpellFinal.Nieun &&
                cycle.Final == SpellFinal.Ieung && new[] { crescent, droplets, ricochet, spread, cycle }.All(x => x.Gate == SpellGateMode.Final),
                "finals: siot, nieun, giyeok, nieun, ieung; every row follows the final-consonant unlock");
            r.Check("W04", crescent.Kind == SpellKind.AttackSingle && droplets.Kind == SpellKind.AttackSingle && spread.Kind == SpellKind.AttackSingle && cycle.Kind == SpellKind.AttackSingle &&
                ricochet.Kind == SpellKind.AttackArea && ricochet.AreaShape == AreaShape.Volley && ricochet.VolleyShots > 1,
                "four single shots and one volley");

            // ---- shared pieces: the timed queue ----
            var queue = new DerivedTimedQueue308<int>(); var taken = new List<int>();
            queue.Add(3f, 30); queue.Add(1f, 10); queue.Add(2f, 20); queue.Add(1f, 11); queue.Add(float.NaN, 99);
            int none = queue.TakeDue(.99f, taken); int two = queue.TakeDue(1f, taken); int rest = queue.TakeDue(10f, taken);
            r.Check("W04", none == 0 && two == 2 && rest == 2 && string.Join(",", taken) == "10,11,20,30" && queue.Count == 0,
                "timed queue: nothing before its time, earliest first, ties in the order added, each item once", string.Join(",", taken));
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var clock = new DerivedTimedQueue308<int>(); clock.Add(1.15f, 1); int landedAt = -1, total = 0; var due = new List<int>();
                for (int frame = 0; frame < fps * 3; frame++)
                {
                    due.Clear(); int n = clock.TakeDue(frame / (float)fps, due); total += n;
                    if (n > 0 && landedAt < 0) landedAt = frame;
                }
                r.Check("W04", total == 1 && landedAt >= 0 && landedAt / (float)fps >= 1.15f && (landedAt - 1) / (float)fps < 1.15f,
                    "a derived judgement lands once, on the first tick at or after its time (" + fps + " fps)", "frame " + landedAt);
            }

            // ---- shared pieces: nearest-first candidates ----
            {
                var centre = new Vector3(3000f, 3000f, 3003f); var ids = new List<int>();
                var ring = new[]
                {
                    WP04Actor(0, centre), WP04Actor(1, centre + new Vector3(-3f, 0f, -4f)), WP04Actor(2, centre + new Vector3(3f, 0f, -4f)),
                    WP04Actor(3, centre + new Vector3(0f, 9f, -5f)), WP04Actor(4, centre + new Vector3(0f, 0f, 2f)), WP04Actor(5, centre + new Vector3(0f, 0f, 1f), false),
                };
                DerivedHitRule308.Near(ring, centre, 5f, new[] { 0 }, ids);
                r.Check("W04", string.Join(",", ids) == "4,1,2,3", "candidates: living, inside the radius (edge included), nearest first, equal distances by id, height ignored, the skipped id left out",
                    string.Join(",", ids));
                DerivedHitRule308.Near(ring, centre, 0f, null, ids); int zero = ids.Count; DerivedHitRule308.Near(null, centre, 5f, null, ids);
                r.Check("W04", zero == 0 && ids.Count == 0, "no radius or no enemies: no candidates");
                var shuffled = new[] { WP04Actor(7, centre), WP04Actor(2, centre + Vector3.right) };
                r.Check("W04", DerivedHitRule308.TryFind(shuffled, 2, out var found) && found.Position == centre + Vector3.right && !DerivedHitRule308.TryFind(shuffled, 0, out _) &&
                    !DerivedHitRule308.TryFind(null, 0, out _), "a snapshot is found by its id, wherever it sits in the array");
                r.Check("W04", r.Near(DerivedHitRule308.Flight(centre, centre + new Vector3(3f, 7f, 4f), 2f), 2.5f) && DerivedHitRule308.Flight(centre, centre, 2f) == 0f &&
                    DerivedHitRule308.Flight(centre, centre + Vector3.forward, 0f) == 0f, "flight time = flat distance / speed (no speed: lands at once)");
            }

            // =====================================================================================================
            // crescents
            // =====================================================================================================
            WP04ConfirmedHitOnly(r, "낫-1");
            {
                float angle = crescent.F("split.angle", -1f), half = crescent.F("split.half", -1f), range = crescent.F("split.range", -1f);
                float share = crescent.F("split.power", -1f), delay = crescent.F("split.delay", -1f);
                r.Check("W04", angle > 0f && half > 0f && range > 0f && share > 0f && delay >= 0f && angle - half > 0f,
                    "the crescent row carries all five split keys; the two cones do not overlap (angle > half)", angle + " " + half + " " + range + " " + share + " " + delay);
                var apex = new Vector3(0f, 0f, 5f); var travel = Vector3.forward; float hitAt = 2.5f, shotPower = 12f;
                var actors = new[]
                {
                    WP04Actor(0, apex),                                               // the enemy that was hit
                    WP04Actor(1, WP04Polar(apex, angle, range * .6f)),                // on the right crescent's axis
                    WP04Actor(2, WP04Polar(apex, -angle, range * .6f)),               // on the left crescent's axis
                    WP04Actor(3, WP04Polar(apex, 0f, range * .5f)),                   // straight ahead: between the crescents
                    WP04Actor(4, WP04Polar(apex, angle, range + 1f)),                 // right axis, out of reach
                    WP04Actor(5, WP04Polar(apex, angle + half - 1f, range * .8f)),    // just inside the right cone's outer edge
                    WP04Actor(6, WP04Polar(apex, angle + half + 2f, range * .8f)),    // just outside it
                    WP04Actor(7, WP04Polar(apex, -angle, range * .4f), false),        // dead, inside the left cone
                    WP04Actor(8, WP04Polar(apex, 180f, 2f)),                          // behind the point of impact
                };
                var orders = new List<SpellHitOrder>();
                int left = CrescentRule308.Plan(actors, 0, apex, travel, angle, half, range, hitAt + delay, shotPower * share, crescent.Element, orders);
                var leftIds = orders.Take(left).Select(o => o.TargetId).ToArray(); var rightIds = orders.Skip(left).Select(o => o.TargetId).ToArray();
                r.Check("낫-1", orders.All(o => o.TargetId != 0) && orders.All(o => o.At == hitAt + delay && o.At >= hitAt),
                    "the crescents are judged after the hit (hit time + split.delay) and never hit the struck enemy again", WP04Ids(orders));
                // review B: above, the struck enemy stands on the apex and is left out by "no bearing" alone (a rule that forgot to
                // exempt it still passed). The rule exempts it by identity: a struck enemy that stands inside a cone is still not hit again.
                var offApex = new List<SpellHitOrder>();
                CrescentRule308.Plan(actors, 1, apex, travel, angle, half, range, hitAt + delay, shotPower * share, crescent.Element, offApex);
                r.Check("낫-1", !offApex.Any(o => o.TargetId == 1) && offApex.Any(o => o.TargetId == 5) && offApex.Any(o => o.TargetId == 2),
                    "the struck enemy is exempt by identity, wherever it stands (here inside the right cone); the other enemies of both cones are still hit", WP04Ids(offApex));
                r.Check("낫-2", left == 1 && leftIds.SequenceEqual(new[] { 2 }) && rightIds.SequenceEqual(new[] { 1, 5 }) && !leftIds.Intersect(rightIds).Any(),
                    "two separate secondary judgements: a left crescent and a right crescent, each with its own enemies", "left " + string.Join(",", leftIds) + " right " + string.Join(",", rightIds));
                Vector3 l = CrescentRule308.Turn(travel, -angle), rt = CrescentRule308.Turn(travel, angle);
                r.Check("낫-2", r.Near(Vector3.Angle(travel, l), angle, .01f) && r.Near(Vector3.Angle(travel, rt), angle, .01f) && r.Near(Vector3.Angle(l, rt), angle * 2f, .01f) &&
                    l.x < 0f && rt.x > 0f && r.Near(l.magnitude, 1f) && r.Near(rt.magnitude, 1f),
                    "the two crescents leave at -angle and +angle from the shot's direction (two different directions)");
                var quarter = CrescentRule308.Turn(Vector3.forward, 90f);
                r.Check("낫-3", r.Near(quarter.x, 1f) && r.Near(quarter.z, 0f) && quarter.y == 0f, "a positive turn is a turn to the right (the sense of AngleAxis about the up axis)");
                r.Check("낫-3", !orders.Any(o => o.TargetId == 3 || o.TargetId == 4 || o.TargetId == 6 || o.TargetId == 7 || o.TargetId == 8),
                    "outside both cones: straight ahead, out of reach, past the cone edge, behind the impact point; a dead enemy is not hit");
                // mirror the enemies about the shot's direction: the left and right crescents swap exactly
                var mirrored = actors.Select(a => WP04Actor(a.Id, new Vector3(-a.Position.x, a.Position.y, a.Position.z), a.Alive)).ToArray();
                var mirror = new List<SpellHitOrder>();
                int mirrorLeft = CrescentRule308.Plan(mirrored, 0, apex, travel, angle, half, range, hitAt + delay, shotPower * share, crescent.Element, mirror);
                r.Check("낫-3", mirror.Take(mirrorLeft).Select(o => o.TargetId).OrderBy(x => x).SequenceEqual(rightIds.OrderBy(x => x)) &&
                    mirror.Skip(mirrorLeft).Select(o => o.TargetId).OrderBy(x => x).SequenceEqual(leftIds.OrderBy(x => x)),
                    "left-right symmetry: mirroring the enemies about the shot's direction swaps the two crescents' hits");
                // the cones leave from the point of impact, not from the caster: move the apex and the same enemies fall out
                var moved = new List<SpellHitOrder>();
                CrescentRule308.Plan(actors, 0, apex + new Vector3(0f, 0f, 30f), travel, angle, half, range, hitAt + delay, shotPower * share, crescent.Element, moved);
                r.Check("낫-3", moved.Count == 0, "the cones leave from the point of impact: with the impact 30 m further on, none of these enemies is reached");
                // 300 random scenes against the independent bearing test, any shot direction
                var random = new System.Random(30804); int wrong = 0, compared = 0, hits = 0;
                for (int scene = 0; scene < 300; scene++)
                {
                    float shotBearing = (float)random.NextDouble() * 360f - 180f;
                    var point = new Vector3((float)random.NextDouble() * 20f - 10f, 0f, (float)random.NextDouble() * 20f - 10f);
                    var crowd = new SpellActorSnap[12];
                    crowd[0] = WP04Actor(0, point);
                    for (int i = 1; i < crowd.Length; i++)
                        crowd[i] = WP04Actor(i, WP04Polar(point, (float)random.NextDouble() * 360f, .2f + (float)random.NextDouble() * (range + 2f)), random.Next(8) != 0);
                    var got = new List<SpellHitOrder>();
                    int gotLeft = CrescentRule308.Plan(crowd, 0, point, WP04Polar(Vector3.zero, shotBearing, 1f), angle, half, range, 1f, 1f, crescent.Element, got);
                    for (int i = 1; i < crowd.Length; i++)
                    {
                        bool inLeft = WP04InCone(point, shotBearing - angle, crowd[i].Position, half, range, out bool edgeLeft) && crowd[i].Alive;
                        bool inRight = WP04InCone(point, shotBearing + angle, crowd[i].Position, half, range, out bool edgeRight) && crowd[i].Alive;
                        if (edgeLeft || edgeRight) continue;                                       // too close to a boundary to compare floats
                        compared++;
                        bool gotL = got.Take(gotLeft).Any(o => o.TargetId == i), gotR = got.Skip(gotLeft).Any(o => o.TargetId == i);
                        if (gotL != inLeft || gotR != inRight) wrong++;
                        if (gotL || gotR) hits++;
                    }
                }
                r.Check("낫-3", wrong == 0 && compared > 2500 && hits > 100, "300 random scenes, any shot direction: both cones agree with an independent bearing / distance test",
                    compared + " compared, " + hits + " hits, " + wrong + " wrong");
                r.Check("낫-4", crescent.Element == Element.Fire && orders.Count == 3 && orders.All(o => o.Element == Element.Fire),
                    "the secondary judgements carry the element of the cast, which for this row is fire");
                r.Check("W04", orders.All(o => r.Near(o.Power, shotPower * share)) && share < 1f, "a crescent carries the shot's scheduled power x split.power (less than the shot)");
                var nothing = new List<SpellHitOrder>();
                CrescentRule308.Plan(actors, 0, apex, Vector3.zero, angle, half, range, 1f, 1f, crescent.Element, nothing);
                CrescentRule308.Plan(actors, 0, apex, travel, angle, half, range, 1f, 0f, crescent.Element, nothing);
                CrescentRule308.Plan(null, 0, apex, travel, angle, half, range, 1f, 1f, crescent.Element, nothing);
                r.Check("W04", nothing.Count == 0, "no direction, no power or no enemies: no crescent judgement");
            }

            // =====================================================================================================
            // droplets
            // =====================================================================================================
            WP04ConfirmedHitOnly(r, "안-1");
            {
                int count = Mathf.RoundToInt(droplets.F("drop.count", -1f)); float radius = droplets.F("drop.radius", -1f);
                float share = droplets.F("drop.power", -1f), speed = droplets.F("drop.speed", -1f);
                r.Check("W04", count >= 2 && radius > 0f && share > 0f && share < 1f && speed > 0f, "the droplet row carries all four drop keys", count + " " + radius + " " + share + " " + speed);
                var point = new Vector3(1f, 0f, 5f); float now = 4f, shotPower = 12f;
                var actors = new[]
                {
                    WP04Actor(0, point),                                              // the enemy that was hit
                    WP04Actor(1, WP04Polar(point, 10f, radius * .30f)),
                    WP04Actor(2, WP04Polar(point, 200f, radius * .65f)),
                    WP04Actor(3, WP04Polar(point, 90f, radius * .90f)),
                    WP04Actor(4, WP04Polar(point, -60f, radius * .50f)),
                    WP04Actor(5, WP04Polar(point, 30f, radius + .5f)),                // out of reach
                    WP04Actor(6, WP04Polar(point, 0f, radius * .10f), false),         // dead, nearest of all
                };
                var scratch = new List<int>(); var orders = new List<SpellHitOrder>();
                DropletRule308.Plan(actors, 0, point, count, radius, speed, now, shotPower * share, droplets.Element, scratch, orders);
                // independent expectation: living, not the struck enemy, inside the radius, nearest first
                var expected = actors.Where(a => a.Alive && a.Id != 0 && WP04Distance(point, a.Position) <= radius).OrderBy(a => WP04Distance(point, a.Position)).Take(count).Select(a => a.Id).ToArray();
                r.Check("안-1", orders.All(o => o.At > now), "the droplets leave at the hit and land after it");
                r.Check("안-2", orders.Count == count && count == 3, "one hit breaks into drop.count droplets (" + count + ")", WP04Ids(orders));
                var fewer = new List<SpellHitOrder>(); DropletRule308.Plan(actors, 0, point, 2, radius, speed, now, 1f, droplets.Element, scratch, fewer);
                var crowdOfTwo = new[] { actors[0], actors[1], actors[4], actors[5], actors[6] }.Select((a, i) => WP04Actor(i, a.Position, a.Alive)).ToArray();
                var short1 = new List<SpellHitOrder>(); DropletRule308.Plan(crowdOfTwo, 0, point, count, radius, speed, now, 1f, droplets.Element, scratch, short1);
                var lonely = new List<SpellHitOrder>(); DropletRule308.Plan(new[] { actors[0] }, 0, point, count, radius, speed, now, 1f, droplets.Element, scratch, lonely);
                r.Check("안-2", fewer.Count == 2 && short1.Count == 2 && lonely.Count == 0,
                    "the number follows the data (count 2 = 2 droplets); fewer enemies than droplets = fewer droplets; nobody near = none");
                r.Check("안-3", orders.Select(o => o.TargetId).SequenceEqual(expected) && orders.Select(o => o.TargetId).Distinct().Count() == orders.Count &&
                    orders.All(o => o.TargetId != 0 && o.TargetId != 5 && o.TargetId != 6),
                    "each droplet chases a different living enemy near the impact, nearest first; never the struck enemy, never one out of reach", WP04Ids(orders) + " expected " + string.Join(",", expected));
                r.Check("안-3", orders.All(o => r.Near(o.At, now + WP04Distance(point, actors[o.TargetId].Position) / speed)) && orders.Select(o => o.At).Distinct().Count() == orders.Count,
                    "each droplet flies its own way: it lands after its own distance / drop.speed");
                r.Check("안-4", droplets.Element == Element.Water && orders.All(o => o.Element == Element.Water), "the droplets keep the element of the cast, which for this row is water");
                r.Check("W04", orders.All(o => r.Near(o.Power, shotPower * share)), "a droplet carries the shot's scheduled power x drop.power");
            }

            // =====================================================================================================
            // ricochet
            // =====================================================================================================
            {
                int bounces = Mathf.RoundToInt(ricochet.F("bounce.count", -1f)); float range = ricochet.F("bounce.range", -1f);
                float keep = ricochet.F("bounce.power", -1f), speed = ricochet.F("bounce.speed", -1f);
                r.Check("W04", bounces >= 1 && range > 0f && keep > 0f && keep < 1f && speed > 0f, "the ricochet row carries all four bounce keys", bounces + " " + range + " " + keep + " " + speed);
                // only shots that really hit bounce: three scheduled shots, the wiring confirms two
                var watches = new DerivedHitWatchList308();
                for (int shot = 0; shot < 3; shot++) watches.Add(new DerivedHitWatch308 { AttackId = 11 + shot, ImpactAt = 1f + shot * .1f, Shot = shot, Power = 2f });
                var bounced = new List<int>();
                foreach (long confirmed in new long[] { 11, 13, 99 })
                    if (watches.TryTake(confirmed, out var watch)) bounced.Add(watch.Shot);
                watches.Sweep(5f); int dropped = watches.Sweep(5f);
                r.Check("속-1", bounced.SequenceEqual(new[] { 0, 2 }) && dropped == 1 && watches.Count == 0,
                    "three scheduled shots, two confirmed hits: exactly those two shots bounce; the shot that never hit is forgotten", string.Join(",", bounced));
                WP04ConfirmedHitOnly(r, "속-1");

                var hitPoint = new Vector3(0f, 0f, 8f); float now = 6f, shotPower = 2f;
                var actors = new[]
                {
                    WP04Actor(0, hitPoint),                                           // the enemy the shot hit
                    WP04Actor(1, WP04Polar(hitPoint, 40f, range * .40f)),
                    WP04Actor(2, WP04Polar(hitPoint, -100f, range * .70f)),
                    WP04Actor(3, WP04Polar(hitPoint, 170f, range * .98f)),
                    WP04Actor(4, WP04Polar(hitPoint, 0f, range + .5f)),               // out of reach
                    WP04Actor(5, WP04Polar(hitPoint, 90f, range * .10f), false),      // dead
                };
                var scratch = new List<int>(); var visited = new List<int> { 0 };
                var reach = new[] { 1, 2, 3 };                                        // living, in reach, nearest first
                bool never = true, roundRobin = true; var spreadOver = new HashSet<int>();
                for (int shot = 0; shot < 24; shot++)
                {
                    int next = RicochetRule308.Next(actors, hitPoint, visited, shot, 1, bounces, range, scratch);
                    never &= next != 0 && next != 4 && next != 5 && next >= 0;
                    roundRobin &= next == reach[shot % reach.Length];
                    spreadOver.Add(next);
                }
                r.Check("속-2", never, "a bounce goes to another enemy: never the one the shot hit, never a dead one, never one out of reach");
                r.Check("속-2", roundRobin && spreadOver.Count == 3, "the bounces of a volley are dealt over the enemies in reach (nearest first, round-robin by shot): the ricochet spreads");
                r.Check("속-2", RicochetRule308.Next(new[] { actors[0], WP04Actor(1, actors[4].Position) }, hitPoint, visited, 0, 1, bounces, range, scratch) == -1 &&
                    RicochetRule308.Next(new[] { actors[0] }, hitPoint, visited, 0, 1, bounces, range, scratch) == -1,
                    "no other enemy in reach: the shot does not bounce (it never comes back to the same enemy)");
                bool ok = RicochetRule308.Bounce(actors, hitPoint, visited, 0, 1, bounces, range, speed, now, shotPower, keep, ricochet.Element, scratch, out SpellHitOrder order);
                r.Check("속-3", ok && order.TargetId == 1 && r.Near(order.At, now + WP04Distance(hitPoint, actors[1].Position) / speed) && order.At > now &&
                    r.Near(order.Power, shotPower * keep) && order.Element == ricochet.Element,
                    "a confirmed hit schedules one more impact: on the next enemy, after the bounce's own flight, with the shot's power x bounce.power");
                r.Check("속-3", bounces == 1 && RicochetRule308.Next(actors, actors[1].Position, new List<int> { 0, 1 }, 0, 2, bounces, range, scratch) == -1 &&
                    !RicochetRule308.Bounce(actors, actors[1].Position, new List<int> { 0, 1 }, 0, 2, bounces, range, speed, now, shotPower, keep, ricochet.Element, scratch, out _),
                    "bounce cap: with bounce.count 1 the bounced shot does not bounce again");
                // a longer chain (count 3, as data could say): no enemy twice, power falls with every bounce, the cap holds
                var chain = new List<int> { 0 }; var powers = new List<float>(); Vector3 from = hitPoint; int depth = 1;
                while (RicochetRule308.Bounce(actors, from, chain, 0, depth, 3, range * 3f, speed, now, shotPower, keep, ricochet.Element, scratch, out SpellHitOrder link))
                { chain.Add(link.TargetId); powers.Add(link.Power); from = actors[link.TargetId].Position; depth++; }
                r.Check("속-3", chain.Count == 4 && chain.Distinct().Count() == 4 && !chain.Contains(5) && powers.Count == 3 &&
                    r.Near(powers[0], shotPower * keep) && r.Near(powers[1], shotPower * keep * keep) && r.Near(powers[2], shotPower * keep * keep * keep),
                    "a chain of three bounces touches four different enemies, loses power at every bounce and stops at its cap", string.Join(">", chain));
                r.Check("W04", RicochetRule308.Next(actors, hitPoint, visited, 0, 0, bounces, range, scratch) == -1 && RicochetRule308.Next(actors, hitPoint, visited, 0, 1, 0, range, scratch) == -1 &&
                    !RicochetRule308.Bounce(actors, hitPoint, visited, 0, 1, bounces, range, speed, now, 0f, keep, ricochet.Element, scratch, out _),
                    "bounce.count 0, a depth below 1, or a shot without power: no bounce");
            }

            // =====================================================================================================
            // spreading ember
            // =====================================================================================================
            WP04ConfirmedHitOnly(r, "간-1");
            {
                float timer = spread.F("spread.timer", -1f), radius = spread.F("spread.radius", -1f); int moves = Mathf.RoundToInt(spread.F("spread.max", -1f));
                r.Check("W04", timer > 0f && radius > 0f && moves == 2, "the ember row carries its three spread keys; the move limit is the Spec default of 2", timer + " " + radius + " " + moves);
                var home = new Vector3(2f, 0f, 6f); float hitAt = 10f, power = 13.5f; var scratch = new List<int>();
                SpellActorSnap[] Crowd(bool carrierAlive, uint carrierLife = 1) => new[]
                {
                    WP04Actor(0, home, carrierAlive, carrierLife),                    // the carrier
                    WP04Actor(1, WP04Polar(home, 20f, radius * .70f)),
                    WP04Actor(2, WP04Polar(home, -120f, radius * .40f)),              // the nearest living other enemy
                    WP04Actor(3, WP04Polar(home, 100f, radius * .98f)),
                    WP04Actor(4, WP04Polar(home, 0f, radius + .3f)),                  // out of reach
                    WP04Actor(5, WP04Polar(home, 60f, radius * .10f), false),         // dead, nearer than all
                };
                bool planted = EmberSpreadRule308.Plant(home, 1, hitAt, power, timer, moves, out var ember);
                r.Check("간-1", planted && ember.Power == power && ember.Left == moves && r.Near(ember.Until, hitAt + timer) && ember.At == home,
                    "a confirmed hit plants one ember in its target: it carries the hit's power, its timer and its moves");
                r.Check("간-1", !EmberSpreadRule308.Plant(home, 1, hitAt, power, timer, 0, out _) && !EmberSpreadRule308.Plant(home, 1, hitAt, 0f, timer, moves, out _),
                    "no moves or no power: no ember");
                // sits and does nothing while the carrier stands and the time is not up
                var sitting = ember;
                var stay = EmberSpreadRule308.Step(ref sitting, Crowd(true), 0, hitAt + timer * .5f, timer, radius, scratch, out int stayNext, out _, out _);
                r.Check("간-3", stay == EmberStep308.Stay && stayNext == -1 && sitting.Left == moves && sitting.Until == ember.Until, "before its time, in a standing carrier, the ember stays and deals nothing");
                // the carrier falls: the ember moves at once, long before its timer
                var fallen = ember; float fellAt = hitAt + .5f;
                var jump = EmberSpreadRule308.Step(ref fallen, Crowd(false), 0, fellAt, timer, radius, scratch, out int afterFall, out bool defeated, out Vector3 leftFrom);
                r.Check("간-2", jump == EmberStep308.Jump && defeated && afterFall == 2 && fellAt < ember.Until && leftFrom == home,
                    "the carrier falls: the ember moves on the same tick, without waiting for its timer", jump + " next " + afterFall);
                var reborn = ember;
                r.Check("간-2", EmberSpreadRule308.Step(ref reborn, Crowd(true, 2), 0, fellAt, timer, radius, scratch, out int afterRebirth, out bool replaced, out _) == EmberStep308.Jump && replaced && afterRebirth == 2,
                    "a carrier that came back as another life no longer holds the ember: it moves on");
                var felled = ember;
                r.Check("간-2", EmberSpreadRule308.Step(ref felled, Crowd(false), -1, hitAt, timer, radius, scratch, out int afterKill, out bool killed, out _) == EmberStep308.Jump && killed && afterKill == 2,
                    "the planting hit itself felled the target: the ember moves on at the very clock of the hit");
                // the time is up: the ember moves although the carrier stands
                var timed = ember;
                var early = EmberSpreadRule308.Step(ref timed, Crowd(true), 0, hitAt + timer - .01f, timer, radius, scratch, out _, out _, out _);
                var onTime = EmberSpreadRule308.Step(ref timed, Crowd(true), 0, hitAt + timer, timer, radius, scratch, out int afterTime, out bool byDeath, out _);
                r.Check("간-3", early == EmberStep308.Stay && onTime == EmberStep308.Jump && !byDeath && afterTime == 2 && r.Near(timed.Until, hitAt + timer * 2f) && timed.Left == moves - 1,
                    "when spread.timer has passed the ember moves on (standing carrier), then sits in the new carrier with a fresh timer and one move less");
                // who it moves to
                var crowd = Crowd(false);
                int expected = crowd.Where(a => a.Alive && a.Id != 0 && WP04Distance(home, a.Position) <= radius).OrderBy(a => WP04Distance(home, a.Position)).First().Id;
                r.Check("간-4", afterFall == expected && expected == 2 && fallen.At == crowd[2].Position && fallen.Life == crowd[2].Life,
                    "it moves to the nearest living enemy inside spread.radius: not a dead one, not one out of reach", "next " + afterFall + " expected " + expected);
                var alone = ember; var far = new[] { WP04Actor(0, home, false), WP04Actor(1, WP04Polar(home, 0f, radius + .3f)) };
                r.Check("간-4", EmberSpreadRule308.Step(ref alone, far, 0, fellAt, timer, radius, scratch, out int nobody, out _, out _) == EmberStep308.End && nobody == -1,
                    "nobody in reach: the ember goes out and deals nothing");
                var back = ember; var pair = new[] { WP04Actor(0, home), WP04Actor(1, WP04Polar(home, 0f, 2f)) };
                EmberSpreadRule308.Step(ref back, pair, 0, hitAt + timer, timer, radius, scratch, out int toOther, out _, out _);
                r.Check("간-4", toOther == 1, "it never chooses the carrier it is leaving, even when that carrier still stands");
                // a carrier that walked away: the ember leaves from where the carrier is now
                var walker = ember; var walked = Crowd(true); walked[0] = WP04Actor(0, home + new Vector3(40f, 0f, 0f)); walked[4] = WP04Actor(4, home + new Vector3(41f, 0f, 0f));
                EmberSpreadRule308.Step(ref walker, walked, 0, hitAt + timer, timer, radius, scratch, out int nearWalker, out _, out Vector3 walkerFrom);
                r.Check("간-4", nearWalker == 4 && walkerFrom == walked[0].Position, "the ember follows its carrier: nearest is measured from where the carrier stands now");
                // same damage, and the move limit
                r.Check("간-5", fallen.Power == power && timed.Power == power, "the move deals the damage of the hit that planted the ember (the same power, unchanged)");
                var second = fallen; var afterFirst = Crowd(false); afterFirst[2] = WP04Actor(2, afterFirst[2].Position, false);   // the new carrier falls too
                // (a wider reach here only so that the fallen second carrier has a neighbour in this small scene)
                var jump2 = EmberSpreadRule308.Step(ref second, afterFirst, 2, fellAt + 1f, timer, radius * 2f, scratch, out int afterSecond, out _, out _);
                r.Check("간-5", jump2 == EmberStep308.Jump && afterSecond >= 0 && afterSecond != 2 && afterSecond != 0 && second.Power == power && second.Left == 0,
                    "the second move deals the same power again", "next " + afterSecond + " left " + second.Left);
                var third = second;
                r.Check("W04", EmberSpreadRule308.Step(ref third, afterFirst, -1, fellAt + 2f, timer, radius, scratch, out int afterThird, out _, out _) == EmberStep308.End && afterThird == -1,
                    "after spread.max moves the ember is used up: no third move");
            }

            // =====================================================================================================
            // cycle shot
            // =====================================================================================================
            WP04ConfirmedHitOnly(r, "앙-1");
            {
                float back = cycle.F("return.time", -1f), refund = cycle.F("refund.ink", -1f), hp01 = cycle.F("heal.hp01", -1f);
                r.Check("W04", back >= 0f && refund > 0f && hp01 > 0f && hp01 < 1f, "the cycle row carries return.time, refund.ink and heal.hp01", back + " " + refund + " " + hp01);
                float hitAt = 7f; float arrives = CycleRule308.ReturnAt(hitAt, back);
                var returns = new DerivedTimedQueue308<CycleReturn308>(); var due = new List<CycleReturn308>();
                returns.Add(arrives, CycleRule308.Plan(refund, 1f, hp01, 3));
                int before = returns.TakeDue(arrives - .01f, due); int onArrival = returns.TakeDue(arrives, due); int after = returns.TakeDue(arrives + 5f, due);
                r.Check("앙-1", r.Near(arrives, hitAt + back) && arrives >= hitAt && before == 0 && onArrival == 1 && after == 0,
                    "the shot comes back return.time after the hit: nothing is returned before it arrives");
                r.Check("앙-1", CycleRule308.ReturnAt(hitAt, -3f) == hitAt && CycleRule308.ReturnAt(hitAt, float.NaN) == hitAt, "a bad return time never brings the shot back before the hit");

                float minCost = WP04MinSpellInkCost(c, out int configs); float costScale = cycle.F("cost", 1f); float castCost = minCost * costScale;
                r.Fact("wp04.combatConfigs", configs); r.Fact("wp04.spellInkCostMin", minCost); r.Fact("wp04.cycleRefund", refund);
                r.Check("앙-2", configs > 0 && refund > 0f && refund < castCost && CycleRule308.Refund(refund, castCost) == refund,
                    "the row's refund is below the cast cost of every combat config in the project (" + configs + " configs, smallest spell cost " + minCost.ToString(CultureInfo.InvariantCulture) + ")",
                    "refund " + refund + " cost " + castCost);
                bool below = true;
                for (int a = 0; a <= 40; a++)
                    for (int b = 0; b <= 40; b++)
                    {
                        float give = a * .01f, cost = b * .01f, got = CycleRule308.Refund(give, cost);
                        below &= got >= 0f && (got == 0f || got < cost) && (give >= cost ? got == 0f : got == give);
                    }
                r.Check("앙-2", below && CycleRule308.Refund(float.NaN, 1f) == 0f && CycleRule308.Refund(.1f, float.NaN) == 0f && CycleRule308.Refund(-1f, 1f) == 0f,
                    "whatever the data says, the ink returned is below the cast cost: a refund that would pay the cast back returns nothing");
                r.Check("앙-2", refund <= castCost * .5f, "a small refund: at most half of the cast cost with today's numbers", (refund / castCost).ToString("0.###", CultureInfo.InvariantCulture));
                // once
                var watches = new DerivedHitWatchList308(); var queue2 = new DerivedTimedQueue308<CycleReturn308>();
                watches.Add(new DerivedHitWatch308 { AttackId = 21, ImpactAt = hitAt, Cost = castCost });
                for (int report = 0; report < 3; report++)
                    if (watches.TryTake(21, out var watch)) queue2.Add(CycleRule308.ReturnAt(hitAt, back), CycleRule308.Plan(refund, watch.Cost, hp01, 0));
                var got2 = new List<CycleReturn308>(); queue2.TakeDue(arrives, got2); queue2.TakeDue(arrives + 1f, got2);
                r.Check("앙-3", got2.Count == 1 && queue2.Count == 0 && got2[0].Ink == refund && got2[0].Hp01 == hp01,
                    "one confirmed hit gives exactly one return, with the row's ink and health", got2.Count.ToString());
                r.Check("앙-3", r.Near(CycleRule308.Heal(100f, hp01), 100f * hp01) && CycleRule308.Heal(100f, hp01) > 0f && CycleRule308.Heal(100f, hp01) < 100f * .5f &&
                    CycleRule308.Heal(100f, 2f) == 100f && CycleRule308.Heal(100f, -1f) == 0f && CycleRule308.Heal(0f, hp01) == 0f && CycleRule308.Heal(float.NaN, hp01) == 0f,
                    "the health restored is maximum health x heal.hp01 (a small share), clamped to 0..maximum");
            }
        }
    }
}
