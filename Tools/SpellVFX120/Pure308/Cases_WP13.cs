// SPEC-SPELL-120-308 L2 cases of WP-13 (opened by D308-13 Q5): the eight glyphs that answer to something an enemy does or
// leaves behind. What is checked here is the pure half: which projectile a needle or a barrage picks, what a guard stance
// does to a hit and what the unblockable hit does to the stance, where a haze stands and how many strikes it makes miss,
// which hazards a cleansing wave wipes and when, what a rock and a moving wall of cover cut off.
// Expected sets are computed with AreaGeometry and plain arithmetic, never with the rule under test.
// Not checked here (needs Unity objects, L3 / Play): the effect classes' Unity half, the physics queries that find TEST
// targets, the enemy -> player doorway itself, real enemies that shoot, guard or leave hazards.
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
        static int WP13Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        // How many of n strikes miss, and which ones, for an enemy whose veil is asked once per strike.
        static List<int> WP13Misses(EnemyStrikeVeil308 veil, int strikes, bool ranged, float now)
        {
            var missed = new List<int>();
            for (int i = 0; i < strikes; i++) if (veil.Misses(ranged, now)) missed.Add(i);
            return missed;
        }

        static partial void RunWP13(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W13", false, "the table did not build"); return; }
            // grid cells (no glyph is named): 18 wood o + ieung, 42 fire o + ieung, 50 earth a + giyeok, 66 earth o + ieung,
            // 77 metal a + siot, 78 metal a + ieung, 90 metal o + ieung, 111 water o + nieun
            SpellRow wave = c.RowAt(18), steam = c.RowAt(42), rock = c.RowAt(50), mud = c.RowAt(66), thrust = c.RowAt(77), needle = c.RowAt(78),
                barrage = c.RowAt(90), purge = c.RowAt(111);
            bool rows = wave.Handler == "path.cover" && steam.Handler == "zone.haze" && rock.Handler == "single.cover" && mud.Handler == "zone.haze" &&
                thrust.Handler == "single.unblockable" && needle.Handler == "intercept.single" && barrage.Handler == "intercept.barrage" && purge.Handler == "path.purge";
            if (!r.Check("W13", rows, "the eight WP-13 rows name their handlers (the WP-13 sheet is in the table)")) return;
            r.Check("W13", new[] { wave, steam, rock, mud, thrust, needle, barrage, purge }.All(x => x.Feature == SpellLegacyFeature.None && x.Gate != SpellGateMode.Open && x.Final != SpellFinal.None && string.IsNullOrEmpty(x.Pending)),
                "they are new rows behind their final consonant: no legacy feature, no open gate, no pending mark");
            // 2026-10-04 (D308-13 Q5: an enemy / world dependent glyph is the spell rule plus a TEST target): the rock-cover row is
            // the one WP-13 row behind the final the main game grants, so it is the one that must stay TEST-only (Cases_Core308 checks
            // what a normal save and the TEST unlock resolve).
            r.Check("W13", rock.Gate == SpellGateMode.Test && new[] { wave, steam, mud, thrust, needle, barrage, purge }.All(x => x.Gate == SpellGateMode.Final),
                "the rock-cover row is TEST-unlock only (Gate test in its rule sheet); the other seven follow their final's own unlock", rock.Gate.ToString());

            // ---- the freezing needle and the barrage ----
            Vector3 caster = new Vector3(5f, 0f, 5f), forward = Vector3.forward;
            float needleRange = needle.F("intercept.range", 0f), needleAngle = needle.F("intercept.angle", 0f);
            SpellProjectileSnap Shot(int id, float x, float z, float vx, float vz, bool live = true) => new SpellProjectileSnap(id, caster + new Vector3(x, 1.2f, z), new Vector3(vx, 0f, vz), live);
            var air = new List<SpellProjectileSnap>
            {
                Shot(0, 0f, 9f, 0f, -8f),                      // straight ahead, coming in
                Shot(1, .5f, 4f, 0f, -8f),                     // nearer, coming in: the urgent one
                Shot(2, 0f, 2f, 0f, 8f),                       // nearest of all, but flying away
                Shot(3, 6f, 3f, -8f, 0f),                      // coming in from the side, outside the needle's narrow fan
                Shot(4, 0f, needleRange + 3f, 0f, -8f),        // coming in, beyond the range
                Shot(5, 0f, 3f, 0f, -8f, false),               // already out of the air
                Shot(6, 0f, 6f, 0f, 0f),                       // hangs in the air without moving
            };
            // expected by hand: incoming = velocity points at the caster; in the fan = AreaGeometry.InCone
            bool Expected(SpellProjectileSnap s, float halfAngle, float range) => s.Live &&
                Vector3.Dot(AreaGeometry.Flat(caster - s.Position), AreaGeometry.Flat(s.Velocity)) > 0f && AreaGeometry.InCone(caster, forward, s.Position, halfAngle, range, out _, out _);
            var candidates = air.Where(s => Expected(s, needleAngle, needleRange)).Select(s => s.Id).ToArray();
            int one = InterceptRule308.PickOne(air, caster, forward, needleAngle, needleRange);
            r.Check("상-1", needleRange > 0f && needleAngle > 0f && candidates.SequenceEqual(new[] { 0, 1 }) && one == 1,
                "the needle picks an enemy projectile that is in the air, comes towards the caster and is inside its fan: the nearest of them", string.Join(" ", candidates) + " -> " + one);
            r.Check("상-1", !InterceptRule308.Incoming(air[2], caster) && !InterceptRule308.Incoming(air[6], caster) && !InterceptRule308.Incoming(air[5], caster) &&
                !InterceptRule308.InFan(air[3], caster, forward, needleAngle, needleRange, out _) && !InterceptRule308.InFan(air[4], caster, forward, needleAngle, needleRange, out _),
                "a projectile that flies away, hangs still, has already fallen, comes in outside the fan or is beyond the range is not picked");
            r.Check("상-1", InterceptRule308.PickOne(air.Where(s => s.Id >= 2).ToList(), caster, forward, needleAngle, needleRange) == InterceptRule308.None &&
                InterceptRule308.PickOne(new List<SpellProjectileSnap>(), caster, forward, needleAngle, needleRange) == InterceptRule308.None && InterceptRule308.PickOne(null, caster, forward, needleAngle, needleRange) == InterceptRule308.None,
                "with nothing to intercept the rule answers None (the cast is then the base glyph's single shot)");
            float needleSpeed = 18f * needle.ProjectileSpeedMul;
            r.Check("상-1", needle.ProjectileSpeedMul == c.RowAt(73).ProjectileSpeedMul && needle.BasePower == c.RowAt(73).BasePower &&
                r.Near(InterceptRule308.FlightSeconds(caster, air[1].Position, needleSpeed), Vector3.Distance(caster, air[1].Position) / needleSpeed),
                "the needle flies with the base glyph's projectile speed: it reaches the projectile after distance / speed");
            var afterwards = air.Select(s => s.Id == 1 ? new SpellProjectileSnap(s.Id, s.Position, s.Velocity, false) : s).ToList();
            r.Check("상-2", InterceptRule308.PickOne(afterwards, caster, forward, needleAngle, needleRange) == 0 && !InterceptRule308.Incoming(afterwards[1], caster),
                "a projectile that was frozen and dropped is out of the air: the next needle goes for the next one");

            int max = Mathf.RoundToInt(barrage.F("intercept.max", 0f));
            float fanAngle = barrage.AreaAngle, fanRange = barrage.AreaLength;
            var swarm = new List<SpellProjectileSnap>();
            for (int i = 0; i < 10; i++) swarm.Add(Shot(i, -4f + i, 3f + (i * 7 % 10), 0f, -8f));      // ten incoming, scattered over the fan
            swarm.Add(Shot(10, 0f, 4f, 0f, 8f));                                                        // flying away
            swarm.Add(Shot(11, 0f, fanRange + 2f, 0f, -8f));                                            // beyond the fan
            var inFan = swarm.Where(s => Expected(s, fanAngle, fanRange)).OrderBy(s => AreaGeometry.Flat(s.Position - caster).magnitude).ThenBy(s => s.Id).Select(s => s.Id).ToList();
            var picked = new List<int>();
            int count = InterceptRule308.PickMany(swarm, caster, forward, fanAngle, fanRange, max, picked);
            r.Check("송-1", max >= 2 && fanAngle > 0f && fanRange > 0f && inFan.Count > max && count == max && picked.SequenceEqual(inFan.Take(max)),
                "the barrage picks several enemy projectiles: every incoming one inside the volley's own fan, nearest first, at most intercept.max", string.Join(" ", picked));
            var all = new List<int>();
            InterceptRule308.PickMany(swarm, caster, forward, fanAngle, fanRange, 64, all);
            r.Check("송-1", all.SequenceEqual(inFan) && !all.Contains(10) && !all.Contains(11) && InterceptRule308.PickMany(swarm, caster, forward, fanAngle, fanRange, 0, all) == 0,
                "with room for all of them it picks exactly the incoming projectiles inside the fan; a barrage with no room picks none");
            r.Check("송-2", barrage.AreaShape == AreaShape.Volley && barrage.AreaShape == c.RowAt(85).AreaShape && barrage.AreaImpactDelay == c.RowAt(85).AreaImpactDelay &&
                barrage.AreaAngle == c.RowAt(85).AreaAngle && barrage.AreaLength == c.RowAt(85).AreaLength && barrage.BasePower == c.RowAt(85).BasePower,
                "the barrage is the base glyph's volley (same fan, same formation delay, same power): all its picks come from one judgement at the moment it forms");
            var again = new List<int>();
            InterceptRule308.PickMany(swarm.Select(s => picked.Contains(s.Id) ? new SpellProjectileSnap(s.Id, s.Position, s.Velocity, false) : s).ToList(), caster, forward, fanAngle, fanRange, max, again);
            r.Check("송-2", again.Count > 0 && !again.Intersect(picked).Any(), "projectiles a barrage shot down are out of the air together: a later barrage picks only the others", string.Join(" ", again));

            // ---- the unblockable thrust and the guard stance ----
            {
                var stance = new EnemyGuardStance308();
                float plain = stance.Intake(12f, 7);
                bool raised = stance.Raise(1f);
                float guarded = stance.Intake(12f, 7);
                bool told = stance.LetThrough(8);
                float other = stance.Intake(12f, 9), through = stance.Intake(12f, 8), reused = stance.Intake(12f, 8);
                r.Check("삿-1", WP13Bits(plain) == WP13Bits(12f) && raised && guarded == 0f && told && other == 0f && WP13Bits(through) == WP13Bits(12f) && reused == 0f,
                    "a full guard stops every hit; the hit registered as unblockable lands whole, once (another hit, and the same id again, are stopped)");
                var half = new EnemyGuardStance308(); half.Raise(.5f);
                half.LetThrough(3); half.Forget(3);
                r.Check("삿-1", r.Near(half.Intake(10f, 3), 5f) && !half.Passes(3) && half.Raised && !half.LetThrough(0) && !half.Raise(0f) && !half.Raise(float.NaN),
                    "a partial guard stops its share; an entry taken back (its hit never landed) lets nothing through; no attack id or no share: nothing is registered");
                half.Lower();
                r.Check("삿-1", WP13Bits(half.Intake(10f, 4)) == WP13Bits(10f) && half.IsEmpty, "a lowered guard stops nothing");
                // through the enemy's modifier state: the stance is ignored, the defence stat is not
                var enemy = new EnemyModifierState308(); enemy.Stance.Raise(1f); enemy.Stance.LetThrough(21);
                float armoured = enemy.Intake(10f, .4f, 21, 0f), blocked = enemy.Intake(10f, .4f, 22, 0f);
                r.Check("삿-1", r.Near(armoured, 6f) && blocked == 0f, "the unblockable hit ignores the stance only: the enemy's defence stat still applies to it", armoured.ToString("R"));
                var untouched = new EnemyModifierState308(); bool same = true;
                foreach (float damage in new[] { .001f, 1f, 7.3f, 12f, 250.5f }) same &= WP13Bits(untouched.Intake(damage, 0f, 5, 3f)) == WP13Bits(damage);
                r.Check("삿-1", same && untouched.IsEmpty, "an enemy that never guarded takes every hit back bit for bit (nothing changes for the enemies that exist)");
                enemy.Clear();
                r.Check("삿-1", enemy.IsEmpty && !enemy.Stance.Raised && WP13Bits(enemy.Intake(10f, 0f, 30, 0f)) == WP13Bits(10f), "a new life starts without the stance and without entries");
                r.Check("삿-1", thrust.BasePower == c.RowAt(73).BasePower && thrust.ProjectileSpeedMul == c.RowAt(73).ProjectileSpeedMul && thrust.Kind == SpellKind.AttackSingle,
                    "the thrust is the base glyph's single shot (same power, same speed)");
            }

            // ---- the haze ----
            // clause ids are built from the row's own glyph (no glyph is named in this file)
            foreach (var pair in new[] { new KeyValuePair<string, SpellRow>(steam.Letter, steam), new KeyValuePair<string, SpellRow>(mud.Letter, mud) })
            {
                SpellRow row = pair.Value; string glyph = pair.Key;
                float radius = row.F("haze.radius", 0f), life = row.F("haze.life", 0f), miss = row.F("haze.miss", 0f);
                Vector3 start = new Vector3(2f, 0f, -3f), direction = new Vector3(1f, .7f, 1f);               // a slope: the haze lies flat
                float length = row.AreaShape == AreaShape.Path ? row.AreaLength : 8f;                         // a cone's range is the combat config's: any length serves here
                Vector3 flat = new Vector3(1f, 0f, 1f).normalized;
                Vector3 centre = HazeRule308.Centre(start, direction, length);
                r.Check(glyph + "-1", radius > 0f && life > 0f && r.Near((centre - (start + flat * (length * .5f))).magnitude, 0f) && row.BasePower > 0f &&
                    row.AreaShape == (row.Index == steam.Index ? AreaShape.Cone : AreaShape.Path),
                    "the first hit is the base glyph's shape; the haze stands at the middle of that shape, on its flat axis", row.AreaShape + " " + radius.ToString("R"));
                var zone = new SpellZone308 { Owner = "zone.haze", Centre = centre, Radius = radius, From = 10f, Until = 10f + life, Life = life, Value = miss };
                Vector3 inside = centre + flat * (radius * .9f), outside = centre + flat * (radius * 1.1f);
                r.Check(glyph + "-1", SpellZoneRule308.Active(zone, 10f) && SpellZoneRule308.Active(zone, 10f + life - .01f) && !SpellZoneRule308.Active(zone, 9.99f) && !SpellZoneRule308.Active(zone, 10f + life),
                    "the haze stays for haze.life seconds after it forms, then it is gone", life.ToString("R"));
                r.Check(glyph + "-2", miss > 0f && miss <= 1f && HazeRule308.MissShare(zone, inside, 11f) == miss && HazeRule308.MissShare(zone, outside, 11f) == 0f &&
                    HazeRule308.MissShare(zone, inside, 9f) == 0f && HazeRule308.MissShare(zone, inside, 10f + life) == 0f && AreaGeometry.InCircle(centre, inside, radius, out _) && !AreaGeometry.InCircle(centre, outside, radius, out _),
                    "an enemy inside the standing haze misses haze.miss of its strikes; outside it, before it forms and after it is gone, none", miss.ToString("R"));
                var veil = new EnemyStrikeVeil308();
                bool hung = veil.Blur(41, 10f + life, miss);
                int strikes = 40;
                var melee = WP13Misses(veil, strikes, false, 11f); var ranged = WP13Misses(veil, strikes, true, 11f);
                int expected = Mathf.RoundToInt(strikes * miss);
                r.Check(glyph + "-2", hung && melee.Count == expected && ranged.Count == expected, "of " + strikes + " strikes exactly the stated share miss, melee and ranged alike (a fixed pattern, not a dice roll)",
                    melee.Count + " / " + ranged.Count + " of " + strikes);
                veil.Remove(41);
                r.Check(glyph + "-2", WP13Misses(veil, strikes, false, 11f).Count == 0 && veil.IsEmpty, "an enemy that walked out of the haze strikes true again at once");
                veil.Blur(41, 10f + life, miss);
                r.Check(glyph + "-2", WP13Misses(veil, strikes, false, 10f + life).Count == 0 && veil.IsEmpty, "the blur ends with the haze");
            }
            {
                var veil = new EnemyStrikeVeil308();
                veil.Blur(1, 100f, .25f); veil.Blur(2, 100f, .5f);
                int both = WP13Misses(veil, 40, false, 0f).Count;
                veil.Remove(2);
                int weaker = WP13Misses(veil, 40, false, 0f).Count;
                r.Check("농-2", both == 20 && weaker == 10, "two hazes do not add up: the stronger one counts; when it goes, the weaker one counts", both + " then " + weaker);
                var tenth = new EnemyStrikeVeil308(); tenth.Blur(1, 100f, .1f);
                var pattern = WP13Misses(tenth, 30, false, 0f);
                r.Check("몽-2", pattern.SequenceEqual(new[] { 9, 19, 29 }), "a share of one in ten misses every tenth strike, whatever the float sum says", string.Join(" ", pattern));
                r.Check("W13", !new EnemyStrikeVeil308().Misses(true, 0f) && !tenth.Blur(0, 100f, .5f) && !tenth.Blur(5, 100f, 0f) && !tenth.Blur(5, float.NaN, .5f) && !tenth.Screen(0, 100f),
                    "an enemy nothing was hung on never misses (every enemy that exists today); an entry without an owner, a share or an end is refused");
                var state = new EnemyModifierState308(); state.Veil.Blur(3, 50f, .5f); state.Veil.Screen(4, 50f);
                bool before = !state.IsEmpty; state.Clear();
                r.Check("W13", before && state.IsEmpty && !state.Veil.Misses(true, 0f), "the veil is cleared with the enemy's life, like every other modifier");
            }

            // ---- the cleansing wave ----
            {
                r.Check("온-1", purge.AreaShape == AreaShape.Path && purge.AreaShape == c.RowAt(109).AreaShape && purge.BasePower == c.RowAt(109).BasePower &&
                    purge.AreaLength == c.RowAt(109).AreaLength && purge.AreaSpeed == c.RowAt(109).AreaSpeed, "the first hit is the base glyph's wave (same corridor, speed and power)");
                Vector3 start = new Vector3(-1f, 0f, 2f), direction = new Vector3(0f, 0f, 1f);
                float half = purge.AreaRadius, length = purge.AreaLength, speed = purge.AreaSpeed, startsAt = 3f + purge.AreaImpactDelay;
                var front = new PathFront308(start, direction, half, length, speed, startsAt, false);
                SpellHazardSnap Hazard(int id, float lateral, float along, float radius, bool purged = false) => new SpellHazardSnap(id, start + new Vector3(lateral, 0f, along), radius, purged);
                var hazards = new[]
                {
                    Hazard(0, 0f, 4f, 1f),                     // on the axis
                    Hazard(1, half + .5f, 9f, 1f),             // its centre is outside the corridor, its circle reaches in
                    Hazard(2, half + 2f, 6f, 1f),              // beside the corridor
                    Hazard(3, 0f, -2f, 1f),                    // behind the caster
                    Hazard(4, 0f, length + 3f, 1f),            // beyond the far end
                    Hazard(5, 0f, 7f, 1f, true),               // already wiped
                    Hazard(6, -1f, length + .5f, 1f),          // its circle reaches back over the far end
                    Hazard(7, 0f, .5f, 1f),                    // under the caster's feet
                };
                bool Touches(SpellHazardSnap h)
                {
                    Vector3 rel = h.Position - start;
                    return !h.Purged && Mathf.Abs(rel.x) <= half + h.Radius && rel.z >= -h.Radius && rel.z <= length + h.Radius;
                }
                var expected = hazards.Where(Touches).Select(h => h.Id).ToArray();
                var onPath = hazards.Where(h => PurgeRule308.OnPath(front, h, out _)).Select(h => h.Id).ToArray();
                r.Check("온-1", half > 0f && length > 0f && speed > 0f && onPath.SequenceEqual(expected) && expected.SequenceEqual(new[] { 0, 1, 6, 7 }),
                    "the wave wipes the hazards whose circle touches its corridor: not the ones beside it, behind the caster, beyond its far end, or already wiped", string.Join(" ", onPath));
                bool times = true;
                foreach (var h in hazards.Where(Touches))
                {
                    PurgeRule308.OnPath(front, h, out float nearEdge);
                    float edge = Mathf.Clamp((h.Position - start).z - h.Radius, 0f, length), at = startsAt + edge / speed;
                    times &= r.Near(nearEdge, edge) && r.Near(PurgeRule308.WipedAt(front, nearEdge), at) && !PurgeRule308.Reached(front, h, at - .02f) && PurgeRule308.Reached(front, h, at + .02f);
                }
                r.Check("온-1", times, "a hazard is wiped the moment the advancing front reaches its near edge: nearer hazards first, none before the wave starts");
                bool frames = true;
                foreach (int rate in new[] { 30, 60, 120 })
                {
                    var wiped = new int[hazards.Length]; var order = new List<int>();
                    for (int k = 0; k <= rate * 12; k++)
                    {
                        float now = 3f + k / (float)rate;
                        for (int i = 0; i < hazards.Length; i++)
                            if (wiped[i] == 0 && PurgeRule308.Reached(front, hazards[i], now)) { wiped[i]++; order.Add(i); }
                    }
                    frames &= hazards.All(h => wiped[h.Id] == (Touches(h) ? 1 : 0)) && order.SequenceEqual(new[] { 7, 0, 1, 6 });
                }
                r.Check("온-1", frames, "frame by frame (30, 60, 120 per second) every hazard on the path is wiped exactly once, in the order the front meets them");
                r.Check("온-1", !PurgeRule308.OnPath(new PathFront308(start, direction, 0f, length, speed, startsAt, false), hazards[0], out _), "a wave without a corridor wipes nothing");
                // 2026-10-04 (review B, M3): the effect asks physics only for the strip the front crossed since the last look
                // (PurgeRule308.Strip, half a corridor width of room). Whatever the frame length, every hazard on the path lies in
                // the strip of the look in which the front reaches it, so no hazard is skipped by looking at less than the corridor.
                bool strips = true; int looks = 0; float widest = 0f;
                foreach (float step in new[] { 1f / 120f, 1f / 30f, .25f, 1f, 100f })
                {
                    var seen = new bool[hazards.Length]; float before = 0f; bool began = false;
                    for (float now = startsAt; ; now += step)
                    {
                        float travelled = front.Travelled(now);
                        PurgeRule308.Strip(front, began ? before : 0f, travelled, half, out float near, out float far);
                        began = true; before = travelled; looks++; widest = Mathf.Max(widest, far - near);
                        for (int i = 0; i < hazards.Length; i++)
                        {
                            if (seen[i] || !PurgeRule308.Reached(front, hazards[i], now)) continue;
                            seen[i] = true;
                            float along = hazards[i].Position.z - start.z, edge = hazards[i].Radius;
                            strips &= along + edge >= near && along - edge <= far;      // the hazard's circle touches the strip of this look
                        }
                        if (front.Finished(now)) break;
                    }
                    strips &= expected.All(i => seen[i]) && Enumerable.Range(0, hazards.Length).Where(i => !expected.Contains(i)).All(i => !seen[i]);
                }
                r.Check("온-1", strips, "looking only at the strip the front crossed since the last look (plus half a corridor width) finds every hazard on the path in the look that wipes it, at 120, 30, 4 and 1 looks per second and in one single look (" + looks + " looks)");
                PurgeRule308.Strip(front, front.Travelled(startsAt + 1f), front.Travelled(startsAt + 1f + 1f / 60f), half, out float oneNear, out float oneFar);
                r.Check("온-1", r.Near(oneFar - oneNear, speed / 60f + 2f * half, .001f) && oneFar - oneNear < length,
                    "one frame's strip is the distance the front ran plus a corridor width: a small part of the corridor, not all of it", (oneFar - oneNear).ToString("0.00") + " of " + length.ToString("0.0"));
            }

            // ---- the rock that takes root ----
            {
                float radius = rock.F("cover.radius", 0f), life = rock.F("cover.life", 0f), reach = rock.F("cover.range", 0f);
                int rocks = Mathf.RoundToInt(rock.F("cover.max", 0f));
                Vector3 player = new Vector3(0f, 0f, 0f), landing = new Vector3(0f, 0f, 5f);
                r.Check("막-1", rock.BasePower == c.RowAt(49).BasePower && rock.Kind == SpellKind.AttackSingle && radius > 0f && life > 0f && rocks >= 1 && reach > 0f,
                    "the cast is the base glyph's single rock (same power) and carries the numbers of the rock that stays");
                Vector3 airLanding = CoverRule308.AirLanding(new Vector3(1f, 2f, 1f), new Vector3(0f, -.6f, 2f), reach);
                r.Check("막-1", r.Near((airLanding - new Vector3(1f, 2f, 1f + reach)).magnitude, 0f) && CoverRule308.AirLanding(player, Vector3.zero, reach) == player + Vector3.forward * reach,
                    "a cast without a target drops its rock cover.range ahead of the caster, on the caster's flat facing", reach.ToString("R"));
                // review B: the landing point and the rooted piece come from the rule the effect calls (the check used to build its own piece
                // and compare it with itself). thrownAt = where the target stood at cast time, landing = where it stands when the rock lands.
                // 2026-10-04 (the first stage follows the base glyph): the rock of this body is not guided, so it takes root on the spot
                // it was thrown at, whether its target still stands there or walked away. Only a guided first stage (a table whose
                // base glyph has no fall rule) roots where its target stands at the landing.
                Vector3 thrownAt = new Vector3(0f, 0f, 4f);
                r.Check("막-1", CoverRule308.RockLanding(thrownAt, false, true, landing) == thrownAt && CoverRule308.RockLanding(thrownAt, false, false, landing) == thrownAt,
                    "an unguided rock takes root on the spot it was thrown at (where its target stood at cast time), whether the target is still there, walked away or is gone");
                r.Check("막-1", CoverRule308.RockLanding(thrownAt, true, true, landing) == landing && CoverRule308.RockLanding(thrownAt, true, false, landing) == thrownAt,
                    "a guided first stage roots where its target stands when it lands; a target that is gone by then leaves the spot the rock was thrown at");
                var piece = CoverRule308.RockOf(9, CoverRule308.RockLanding(landing, false, true, landing + Vector3.right * 3f), radius, 20f, life);
                r.Check("막-1", piece.Centre == landing && !piece.Wall && piece.Radius == radius && piece.Token == 9,
                    "the rooted rock is a circle of cover.radius around the landing spot");
                r.Check("막-2", piece.From == 20f && r.Near(piece.Until, 20f + life) && CoverRule308.RockOf(9, landing, radius, 20f, -1f).Until == 20f,
                    "the rooted rock stands from its landing for cover.life seconds (a broken life never makes it stand backwards in time)");
                r.Check("막-2", CoverRule308.Stands(piece, 20f) && CoverRule308.Stands(piece, 20f + life - .01f) && !CoverRule308.Stands(piece, 19.99f) && !CoverRule308.Stands(piece, 20f + life),
                    "the rock stays as cover for cover.life seconds, then it is gone", life.ToString("R"));
                Vector3 behindRock = new Vector3(0f, 0f, 12f), aside = new Vector3(8f, 0f, 5f), grazing = new Vector3(radius * 3f, 0f, 12f);
                r.Check("막-2", CoverRule308.Blocks(piece, behindRock, player, 21f) && CoverRule308.Blocks(piece, player, behindRock, 21f) &&
                    !CoverRule308.Blocks(piece, aside, player, 21f) && !CoverRule308.Blocks(piece, grazing, player, 21f) && !CoverRule308.Blocks(piece, behindRock, player, 20f + life),
                    "while it stands it cuts the straight line between an enemy and the player that passes through it; a line that passes beside it is not cut, and nothing is cut once it is gone");
                r.Check("막-2", !CoverRule308.Blocks(piece, landing + new Vector3(radius * .5f, 0f, 0f), player, 21f) && !CoverRule308.Blocks(piece, behindRock, landing, 21f) &&
                    !CoverRule308.Blocks(piece, new Vector3(0f, 0f, 3f), player, 21f),
                    "an enemy or a player standing inside the rock's circle has nothing in between; an enemy on the player's side of the rock is not cut off either");
                var veil = new EnemyStrikeVeil308(); veil.Screen(9, 20f + life);
                r.Check("막-2", WP13Misses(veil, 10, true, 21f).Count == 10 && WP13Misses(veil, 10, false, 21f).Count == 0 && WP13Misses(veil, 10, true, 20f + life).Count == 0,
                    "cover stops ranged strikes only, every one of them while it holds; melee strikes are not stopped; the screen ends with the rock");
            }

            // ---- the wave that covers who walks behind it ----
            {
                SpellRow circle = c.RowAt(13);                 // the wood glyph without a final
                r.Check("공-1", wave.AreaShape == AreaShape.Path && wave.Kind == SpellKind.AttackArea && wave.BasePower > 0f && wave.BasePower < circle.BasePower &&
                    wave.AreaRadius > 0f && wave.AreaLength > 0f && wave.AreaSpeed > 0f,
                    "the wave is a path with a small base power: above zero and below the wood glyph without a final", wave.BasePower.ToString("R") + " < " + circle.BasePower.ToString("R"));
                Vector3 start = Vector3.zero, direction = Vector3.forward;
                float half = wave.AreaRadius, length = wave.AreaLength, speed = wave.AreaSpeed, startsAt = 5f + wave.AreaImpactDelay;
                var front = new PathFront308(start, direction, half, length, speed, startsAt, false);
                bool advancing = true; float last = -1f;
                foreach (float along in new[] { 0f, length * .25f, length * .5f, length })
                {
                    float at = front.ArrivalAt(along);
                    advancing &= r.Near(at, startsAt + along / speed) && at > last && r.Near(front.Along(at), along, .001f);
                    last = at;
                }
                r.Check("공-1", advancing && r.Near(front.EndsAt, startsAt + length / speed), "the front advances along the corridor at the row's speed: an enemy further away is reached (and hit) later");
                float mid = startsAt + length * .5f / speed;
                var wall = CoverRule308.WallOf(front, 17, mid);
                Vector3 player = new Vector3(0f, 0f, 1f), ahead = new Vector3(.5f, 0f, length + 4f), behind = new Vector3(0f, 0f, -6f), flank = new Vector3(half * 6f, 0f, length * .5f + .5f);
                r.Check("공-2", wall.Wall && r.Near((wall.Centre - new Vector3(0f, 0f, length * .5f)).magnitude, 0f) && wall.Radius == half && wall.Until == front.EndsAt,
                    "the cover is a wall across the corridor at the wave front");
                r.Check("공-2", CoverRule308.Blocks(wall, ahead, player, mid) && !CoverRule308.Blocks(wall, behind, player, mid) && !CoverRule308.Blocks(wall, flank, player, mid),
                    "the player behind the wave is covered from an enemy ahead of it; not from one behind the player, and not from one whose line passes round the end of the wall");
                r.Check("공-2", !CoverRule308.Blocks(wall, ahead, new Vector3(0f, 0f, length * .5f + 2f), mid) && !CoverRule308.Blocks(wall, player, ahead, mid),
                    "a player who walked ahead of the wave has no cover; the wall covers the one behind it only");
                var early = CoverRule308.WallOf(front, 17, startsAt + .01f); var late = CoverRule308.WallOf(front, 17, front.EndsAt - .01f);
                Vector3 midEnemy = new Vector3(0f, 0f, length * .5f);
                r.Check("공-2", CoverRule308.Blocks(early, midEnemy, player, startsAt + .01f) == (early.Centre.z > player.z) && !CoverRule308.Blocks(late, midEnemy, player, front.EndsAt - .01f) &&
                    late.Centre.z > early.Centre.z,
                    "the cover moves with the wave: an enemy the front has already passed is no longer cut off");
                r.Check("공-2", !CoverRule308.Blocks(wall, ahead, player, startsAt - .01f) && !CoverRule308.Blocks(CoverRule308.WallOf(front, 17, front.EndsAt), ahead, player, front.EndsAt),
                    "there is no cover before the wave starts or after it reached the far end of its corridor");
            }
        }
    }
}
