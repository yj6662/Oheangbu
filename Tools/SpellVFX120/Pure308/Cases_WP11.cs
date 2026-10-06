// SPEC-SPELL-120-308 L2 cases of WP-11: the three single shots that are handed over to a handler (range, unguided fall,
// turn-limited guidance), the handover rule itself, and the table clauses of the five single shots without a final and of
// the 20 blank cells (the clause checklist gives each of them one L2 clause; no package owns those rows).
// The handover proof: with the WP-11 sheet in the table, (1) the dispatch rule without a registry is the rule of before for
// all 120 rows, (2) all 120 glyphs resolve to the same status and the same cast as with the sheet taken out, under the old
// flag gate and under the handler gate, (3) exactly the rows the sheet names are handed over when a registry holds their
// handler. The regression against the pre-#308 resolver over every book (A4, Cases_Table) runs on this same table.
// Not checked here (needs Unity objects, L3 / Play): the effect classes' Unity half, sight checks, the adapter's flight.
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using Oheangbu.EditorTools.WorldMacro;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        const string WP11Sheet = "Rules308_WP11";

        // One guided shot flown at a fixed frame rate against a target that moves as targetAt(time) says.
        static HomingShot308 WP11Fly(int rate, float speed, float turnRate, float turnLimit, float hitRadius, float maxAge,
            Func<float, Vector3> targetAt, out float landedAt)
        {
            var shot = HomingRule308.Launch(Vector3.zero, targetAt(0f), Vector3.forward);
            float dt = 1f / rate; landedAt = float.NaN;
            for (int k = 1; k <= rate * 60; k++)
            {
                float now = k * dt;
                if (HomingRule308.Step(ref shot, targetAt(now), dt, speed, turnRate, turnLimit, hitRadius, maxAge) != HomingStatus308.Flying)
                { landedAt = shot.Age; break; }
            }
            return shot;
        }

        static partial void RunWP11(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W11", false, "the table did not build"); return; }
            var rows = c.Build.Rows;
            // the five single shots without a final, by grid cell (no glyph is named): wood 1, fire 25, earth 49, metal 73, water 97
            int[] cells = { 1, 25, 49, 73, 97 };
            var singles = cells.Select(i => c.RowAt(i)).ToArray();
            SpellRow wood = singles[0], fire = singles[1], earth = singles[2], metal = singles[3], water = singles[4];
            var flags = new LegacySpellGate(true, true, true, true, true);
            SpellCast Cast(SpellRow row) { SpellResolveCore308.Resolve(row.Letter[0], 1f, 1f, row, flags, out SpellCast cast); return cast; }

            // ---- the handover rule ----
            var handed = rows.Where(SpellTakeover308.Declared).ToArray();
            r.Fact("handedOverRows", string.Join(" ", handed.Select(x => x.Index + ":" + x.Handler)));
            bool sheetThere = c.Sheets.Any(s => s.Name == WP11Sheet);
            r.Check("W11", sheetThere && handed.Length == 3 && handed.Select(x => x.Index).SequenceEqual(new[] { 1, 49, 97 }),
                "the WP-11 sheet hands over exactly the wood, earth and water single shots", string.Join(" ", handed.Select(x => x.Index)));
            r.Check("W11", wood.Handler == "single.ranged" && earth.Handler == "single.ballistic" && water.Handler == "single.homing",
                "each handed-over row names its handler", wood.Handler + " " + earth.Handler + " " + water.Handler);
            r.Check("W11", handed.All(x => x.Feature == SpellLegacyFeature.Book && x.Kind == SpellKind.AttackSingle && x.AreaShape == AreaShape.None && x.Final == SpellFinal.None),
                "a handed-over row keeps its legacy feature (book), kind and shape");
            r.Check("A4", rows.All(x => SpellTakeover308.UsesHandler(x, false) == (x.Feature == SpellLegacyFeature.None)),
                "without an effect registry the dispatch rule is the rule of before for all 120 rows (only a row without a legacy feature goes to a handler)");
            var withRegistry = rows.Where(x => SpellTakeover308.UsesHandler(x, true) != (x.Feature == SpellLegacyFeature.None)).Select(x => x.Index).ToArray();
            r.Check("W11", withRegistry.SequenceEqual(new[] { 1, 49, 97 }),
                "with a registry that holds the handlers, the dispatch rule differs from before on the three handed-over rows only", string.Join(" ", withRegistry));
            r.Check("W11", rows.Where(x => x.Feature != SpellLegacyFeature.None && !SpellTakeover308.Declared(x)).All(x => !SpellTakeover308.UsesHandler(x, true)) &&
                !SpellTakeover308.UsesHandler(null, true) && !SpellTakeover308.Declared(null),
                "the other 33 legacy rows never go to a handler, registry or not");

            // the same table with the WP-11 sheet taken out: every glyph resolves the same, under both gates
            var without = SpellTableBuilder308.Build(c.CanonicalCsv, c.Sheets.Where(s => s.Name != WP11Sheet).ToList(), c.UnlockCsv);
            r.Check("A4", without.Ok && without.Rows.Count(SpellTakeover308.Declared) == 0, "the table without the WP-11 sheet builds and hands nothing over");
            if (without.Ok)
            {
                var ids = new HashSet<string>(rows.Where(x => x.HasHandler && x.Feature == SpellLegacyFeature.None).Select(x => x.Handler));
                var gates = new List<KeyValuePair<string, ISpellGate>>();
                for (int set = 0; set < SpellEquivalence308.FlagSets; set++)
                {
                    SpellEquivalence308.Flags(set, out bool guk, out bool buffs, out bool wards, out bool giyeok, out bool mum);
                    gates.Add(new KeyValuePair<string, ISpellGate>("flags " + set, new LegacySpellGate(guk, buffs, wards, giyeok, mum)));
                }
                gates.Add(new KeyValuePair<string, ISpellGate>("handler gate, all finals", new ModelGate { Guk = true, Buffs = true, Wards = true, Giyeok = true, Mum = true, Registered = ids,
                    Unlocked = new HashSet<SpellFinal> { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Mieum, SpellFinal.Siot, SpellFinal.Ieung } }));
                gates.Add(new KeyValuePair<string, ISpellGate>("handler gate, no final", new ModelGate { Registered = ids }));
                int cases = 0; var differences = new List<string>();
                foreach (var gate in gates)
                    foreach (float brush in new[] { 1f, .37f })
                        for (int i = 1; i <= 120; i++)
                        {
                            char letter = SpellGrammar308.LetterAt(i);
                            var before = SpellResolveCore308.Resolve(letter, brush, brush, without.Rows[i - 1], gate.Value, out SpellCast a);
                            var after = SpellResolveCore308.Resolve(letter, brush, brush, rows[i - 1], gate.Value, out SpellCast b);
                            cases++;
                            if (before == after && (before != SpellResolveStatus.Ok || SpellEquivalence308.CastDifference(a, b) == null)) continue;
                            if (differences.Count < 8) differences.Add(gate.Key + " cell " + i);
                        }
                r.Fact("wp11ResolveCases", cases);
                r.Check("A4", differences.Count == 0, "with and without the WP-11 sheet all 120 glyphs resolve to the same status and the same cast (" + cases + " cases: 32 flag sets and the handler gate, two brush values)",
                    string.Join(" | ", differences));
                bool sameNumbers = true;
                for (int i = 0; i < 120; i++)
                {
                    var x = without.Rows[i]; var y = rows[i];
                    sameNumbers &= x.Feature == y.Feature && x.Kind == y.Kind && x.Gate == y.Gate && x.BasePower.Equals(y.BasePower) && x.ProjectileSpeedMul.Equals(y.ProjectileSpeedMul) &&
                        x.AreaShape == y.AreaShape && (SpellTakeover308.Declared(y) || x.Handler == y.Handler);
                }
                r.Check("A4", sameNumbers, "the WP-11 sheet changes no feature, kind, gate, base power, projectile speed or shape of any row; only the three handler names differ");
            }

            // ---- table clauses of the five single shots ----
            var casts = singles.Select(Cast).ToArray();
            r.Check("나-2", casts[1].SpeedMul == 1f, "the fire single shot flies at the standard projectile speed (multiplier 1)", casts[1].SpeedMul.ToString("R"));
            float[] powers = singles.Select(x => x.BasePower).OrderBy(x => x).ToArray();
            r.Check("나-3", fire.BasePower > 0f && fire.BasePower == powers[2] && metal.BasePower < fire.BasePower && fire.BasePower < earth.BasePower,
                "the fire single shot carries the baseline power: the median of the five, above the weakest and below the strongest", fire.BasePower.ToString("R"));
            r.Check("사-2", casts.Where((x, i) => i != 3).All(x => x.SpeedMul < casts[3].SpeedMul), "the metal single shot is the fastest of the five", casts[3].SpeedMul.ToString("R"));
            r.Check("사-3", singles.Where((x, i) => i != 3).All(x => x.BasePower > metal.BasePower) && metal.BasePower > 0f, "the metal single shot has the lowest base power of the five", metal.BasePower.ToString("R"));
            r.Check("마-3", singles.Where((x, i) => i != 2).All(x => x.BasePower < earth.BasePower), "the earth single shot has the highest base power of the five", earth.BasePower.ToString("R"));
            r.Check("아-2", casts[4].SpeedMul < 1f && casts[4].SpeedMul > 0f && casts.Where((x, i) => i != 4).All(x => x.SpeedMul > casts[4].SpeedMul),
                "the water single shot is slow: a projectile speed multiplier below 1, the lowest of the five", casts[4].SpeedMul.ToString("R"));

            // ---- the 20 blank cells, one clause each: a declared misfire whatever is registered or unlocked ----
            var everything = new ModelGate { Guk = true, Buffs = true, Wards = true, Giyeok = true, Mum = true,
                Registered = new HashSet<string>(rows.Where(x => x.HasHandler).Select(x => x.Handler)),
                Unlocked = new HashSet<SpellFinal> { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Mieum, SpellFinal.Siot, SpellFinal.Ieung } };
            foreach (var blank in rows.Where(x => x.Category == SpellCategory.Blank))
                r.Check(blank.Letter + "-1", !blank.HasHandler && string.IsNullOrEmpty(blank.Pending) &&
                    SpellResolveCore308.Resolve(blank.Letter[0], 1f, 1f, blank, everything, out _) == SpellResolveStatus.Blank &&
                    SpellResolveCore308.Resolve(blank.Letter[0], 1f, 1f, blank, flags, out _) == SpellResolveStatus.Blank,
                    "blank cell " + blank.Index + " resolves to Blank under every gate (the misfire: ink MisfireInkCost, the strokes evaporate)");

            // ---- wood single: range ----
            float range = wood.F("range", 0f);
            r.Check("가-3", range > 0f && RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, range * .5f), range) &&
                RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, range), range), "a target inside the row's range, or exactly on it, is reached", range.ToString("R"));
            r.Check("가-3", !RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, range + .01f), range) &&
                !RangedSingleRule308.Reaches(Vector3.zero, new Vector3(range, 0f, range), range) && !RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, 1f), 0f),
                "a target beyond the range is not reached (straight ahead or diagonally); a range of zero reaches nothing");
            Vector3 far = new Vector3(3f, 0f, 4f) * (range * .5f);            // 2.5 x range away
            r.Check("가-3", r.Near(Vector3.Distance(Vector3.zero, RangedSingleRule308.EndPoint(Vector3.zero, far, range)), range) &&
                RangedSingleRule308.EndPoint(Vector3.zero, new Vector3(0f, 0f, range * .5f), range) == new Vector3(0f, 0f, range * .5f),
                "a shot at a target out of range ends on the line to it, at the range; a shot within range ends at its target");

            // ---- earth single: unguided fall ----
            float fall = earth.F("fall.radius", 0f);
            Vector3 spot = new Vector3(2f, 0f, 9f);
            float speed = 18f, flight = BallisticRule308.FlightSeconds(Vector3.zero, spot, speed);
            r.Check("마-4", fall > 0f && r.Near(flight, Vector3.Distance(Vector3.zero, spot) / speed) && BallisticRule308.Lands(spot, spot, fall) &&
                BallisticRule308.Lands(spot, spot + new Vector3(fall * .9f, 0f, 0f), fall) && BallisticRule308.Lands(spot, spot + new Vector3(0f, 5f, 0f), fall),
                "the rock falls on the spot the target stood on at cast time, after distance / speed; a target still within fall.radius of that spot is hit (height does not matter)", fall.ToString("R"));
            bool walkedOut = true;
            foreach (float walk in new[] { 2f, 4f, 8f })
            {
                Vector3 now = spot + new Vector3(walk, 0f, 0f) * flight;       // the target walked sideways during the flight
                walkedOut &= BallisticRule308.Lands(spot, now, fall) == (walk * flight <= fall);
            }
            r.Check("마-4", walkedOut && !BallisticRule308.Lands(spot, spot + new Vector3(fall * 1.1f, 0f, 0f), fall) && !BallisticRule308.Lands(spot, spot, 0f),
                "the rock is not guided: a target that walked out of fall.radius during the flight is missed, whatever its speed says");

            // ---- water single: guidance within a turn limit ----
            float rate = water.F("turn.rate", 0f), limit = water.F("turn.limit", 0f), hit = water.F("hit.radius", 0f), maxAge = water.F("flight.max", 0f);
            float slow = 18f * water.ProjectileSpeedMul;
            r.Check("W11", rate > 0f && limit > 0f && hit > 0f && maxAge > 0f && slow > 0f, "the water single row carries its four guidance numbers");
            // a target that walks sideways: a straight shot passes behind it, the guided shot follows and hits
            Func<float, Vector3> strafe = t => new Vector3(3f * t, 0f, 12f);
            var landings = new List<float>(); bool followed = true, straightMisses = true;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var guided = WP11Fly(fps, slow, rate, limit, hit, maxAge, strafe, out float at);
                followed &= guided.Status == HomingStatus308.Hit && guided.Turned > 0f && guided.Turned <= limit + .01f && (strafe(at) - strafe(0f)).magnitude > hit;
                landings.Add(at);
                var straight = WP11Fly(fps, slow, 0f, limit, hit, maxAge, strafe, out _);
                straightMisses &= straight.Status == HomingStatus308.Missed && straight.Turned == 0f;
            }
            r.Check("아-4", followed, "a target that walks sideways during the flight is followed and hit, well away from where it stood at cast time (30, 60, 120 frames per second)",
                string.Join(" ", landings.Select(x => x.ToString("0.000"))));
            r.Check("아-4", straightMisses, "the same shot without steering (turn.rate 0) flies past that target: the hit comes from the tracking");
            r.Check("아-4", landings.Max() - landings.Min() < .1f, "the landing time does not depend on the frame rate (within a tenth of a second)", (landings.Max() - landings.Min()).ToString("0.000"));
            var still = WP11Fly(60, slow, rate, limit, hit, maxAge, t => new Vector3(0f, 0f, 9f), out float stillAt);
            r.Check("아-4", still.Status == HomingStatus308.Hit && still.Turned == 0f && r.Near(stillAt, (9f - hit) / slow, .05f),
                "a target that stands still is hit without any turning, after (distance - hit.radius) / speed", stillAt.ToString("0.000"));
            // a target that runs past the shot and stops behind it: the shot would have to turn far more than the limit
            Func<float, Vector3> pass = t => new Vector3(2f, 0f, 8f - 20f * Mathf.Min(t, 1f));
            bool limited = true; var turned = new List<float>();
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var shot = WP11Fly(fps, slow, rate, limit, hit, maxAge, pass, out _);
                limited &= shot.Status == HomingStatus308.Missed && shot.Turned <= limit + .01f && shot.Turned >= limit - .01f && shot.Age < maxAge;
                turned.Add(shot.Turned);
            }
            r.Check("아-3", limited, "a target that gets behind the shot is missed: the shot turns up to turn.limit degrees in total and no further, and is lost before flight.max",
                string.Join(" ", turned.Select(x => x.ToString("0.0"))));
            var free = WP11Fly(60, slow, rate, 720f, hit, 30f, pass, out _);
            r.Check("아-3", free.Status == HomingStatus308.Hit && free.Turned > limit, "the same flight with a turn limit far above the row's hits: the miss comes from the limit", free.Turned.ToString("0.0"));
            var quick = WP11Fly(60, slow, 1f, limit, hit, maxAge, strafe, out _);
            r.Check("아-3", quick.Status == HomingStatus308.Missed && quick.Turned < limit, "a shot that turns too slowly (1 degree per second) loses the walking target too: the rate limits it as well", quick.Turned.ToString("0.0"));
            var lost = WP11Fly(60, slow, rate, limit, hit, maxAge, t => new Vector3(0f, 0f, 9f + 2f * slow * t), out float lostAt);
            r.Check("아-3", lost.Status == HomingStatus308.Missed && lostAt >= maxAge - .05f && lostAt <= maxAge + .05f, "a target that outruns the shot is given up after flight.max seconds", lostAt.ToString("0.000"));

            // ---- 2026-10-04: a single shot behind a final flies its first stage as the glyph without a final flies ----
            // (Spellcraft Bible chapter 4: range, parabola and guidance belong to the initial consonant. PrimaryShotRule308 says
            // which handler flies the first stage of a row; the wiring asks it for every handler cast. The ten rows below went
            // through the lock-on single judgement until this fix: they could not miss.)
            SpellRow Ballistics(SpellRow row)
            {
                char letter = PrimaryShotRule308.BallisticsLetter(row);
                return letter != PrimaryShotRule308.None ? c.Row(letter) : null;
            }
            string Stage(SpellRow row, bool registry)
            {
                var ballistics = Ballistics(row);
                return PrimaryShotRule308.StageHandler(ballistics, registry && ballistics != null && ballistics.HasHandler);
            }
            var followers = rows.Where(PrimaryShotRule308.FollowsBase).ToArray();
            var staged = followers.Where(x => Stage(x, true).Length > 0).ToArray();
            r.Fact("primaryStageRows", string.Join(" ", staged.Select(x => x.Index + ":" + x.Handler + "<" + Stage(x, true))));
            // cells: 3, 5, 6 = the wood single + nieun / siot / ieung; 50, 51, 53, 54 = the earth single + giyeok / nieun / siot / ieung;
            // 99, 101, 102 = the water single + nieun / siot / ieung (the giyeok cells 2 and 98 are legacy traces, the mieum cells are installs)
            int[] woodBody = { 3, 5, 6 }, earthBody = { 50, 51, 53, 54 }, waterBody = { 99, 101, 102 };
            r.Check("W11", staged.Select(x => x.Index).SequenceEqual(woodBody.Concat(earthBody).Concat(waterBody)),
                "exactly ten single shots behind a final take their first stage from the handler of the glyph without a final", string.Join(" ", staged.Select(x => x.Index)));
            r.Check("W11", woodBody.All(i => Stage(c.RowAt(i), true) == "single.ranged") && earthBody.All(i => Stage(c.RowAt(i), true) == "single.ballistic") &&
                waterBody.All(i => Stage(c.RowAt(i), true) == "single.homing"),
                "the wood body flies with a range, the earth body falls unguided, the water body steers within its turn limit: none of the ten is on the lock-on plan");
            var lockOn = followers.Where(x => Stage(x, true).Length == 0).Select(x => x.Index).ToArray();
            // 27, 29, 30 = the fire single + nieun / siot / ieung; 75, 77, 78 = the metal single + nieun / siot / ieung
            r.Check("W11", lockOn.SequenceEqual(new[] { 27, 29, 30, 75, 77, 78 }) && new[] { 25, 73 }.All(i => PrimaryShotRule308.StageHandler(c.RowAt(i), true).Length == 0),
                "the fire and metal bodies have no flight rule of their own: their six single shots behind a final keep the lock-on single judgement, as their base glyphs do", string.Join(" ", lockOn));
            r.Check("W11", followers.All(x => Stage(x, false).Length == 0) && new[] { 1, 49, 97 }.All(i => PrimaryShotRule308.StageHandler(c.RowAt(i), false).Length == 0),
                "without an effect registry (old scenes, old checks) every first stage is the lock-on single judgement, as before");
            if (without.Ok)
            {
                bool plain = true;
                foreach (var row in without.Rows.Where(PrimaryShotRule308.FollowsBase))
                {
                    var ballistics = without.Rows[SpellGrammar308.IndexOf(PrimaryShotRule308.BallisticsLetter(row)) - 1];
                    plain &= PrimaryShotRule308.StageHandler(ballistics, true).Length == 0;
                }
                r.Check("W11", plain, "with the WP-11 sheet taken out no glyph has a flight rule: the ten keep the lock-on judgement together with their base glyphs (the handover stays one switch)");
            }
            // the stage is its base glyph's row itself: one set of numbers, read from the row without a final
            string[] flightKeys = { "range", "fall.radius", "turn.rate", "turn.limit", "hit.radius", "flight.max" };
            r.Check("W11", staged.All(x => ReferenceEquals(Ballistics(x), c.Row(SpellGrammar308.BaseOf(x.Letter[0]))) && flightKeys.All(k => !x.Has(k))) &&
                PrimaryShotRule308.BallisticsLetter(wood) == wood.Letter[0] && ReferenceEquals(Ballistics(earth), earth),
                "the ten rows carry no flight number of their own: the first stage reads the row of the glyph without a final (a glyph without a final reads its own row)");
            r.Check("W11", PrimaryShotRule308.BallisticsLetter(null) == PrimaryShotRule308.None && rows.Where(x => x.Kind != SpellKind.AttackSingle || x.AreaShape != AreaShape.None).All(x => PrimaryShotRule308.BallisticsLetter(x) == PrimaryShotRule308.None) &&
                rows.Where(x => x.Final == SpellFinal.None || x.Feature != SpellLegacyFeature.None || !x.HasHandler).All(x => !PrimaryShotRule308.FollowsBase(x)),
                "an area shape, an install, a buff, a legacy row and a glyph without a final are not followers: the rule answers for single shots behind a final only");

            // the earth body: every one of the four misses the walking enemy its base glyph misses, and hits the one it hits
            bool sameAsBase = true, someMiss = false, someHit = false; var walks = new List<string>();
            foreach (int cell in earthBody)
            {
                var ballistics = Ballistics(c.RowAt(cell));
                foreach (float walk in new[] { 0f, .5f, 1f, 2f, 4f, 8f })
                    foreach (float distance in new[] { 4f, 9f, 18f })
                    {
                        Vector3 stood = new Vector3(0f, 0f, distance);
                        float seconds = BallisticRule308.FlightSeconds(Vector3.zero, stood, speed);
                        Vector3 now = stood + new Vector3(walk * seconds, 0f, 0f);
                        bool follower = BallisticRule308.Lands(stood, now, ballistics.F("fall.radius", 0f));
                        bool baseGlyph = BallisticRule308.Lands(stood, now, earth.F("fall.radius", 0f));
                        sameAsBase &= follower == baseGlyph && follower == (walk * seconds <= fall);
                        someMiss |= !follower; someHit |= follower;
                        if (cell == earthBody[1] && distance == 18f) walks.Add(walk + "m/s:" + (follower ? "hit" : "miss"));
                    }
            }
            r.Check("W11", sameAsBase && someMiss && someHit,
                "the four earth single shots behind a final miss a walking enemy exactly as the base rock does (the enemy left fall.radius of the spot it stood on) and hit a standing one: the lock-on plan would have hit every time",
                string.Join(" ", walks));
            // the water body: the same flight as the base glyph, miss included
            bool sameFlight = true;
            foreach (int cell in waterBody)
            {
                var b = Ballistics(c.RowAt(cell));
                var passed = WP11Fly(60, slow, b.F("turn.rate", 0f), b.F("turn.limit", 0f), b.F("hit.radius", 0f), b.F("flight.max", 0f), pass, out float passedAt);
                var followedShot = WP11Fly(60, slow, b.F("turn.rate", 0f), b.F("turn.limit", 0f), b.F("hit.radius", 0f), b.F("flight.max", 0f), strafe, out float followedAt);
                var baseMiss = WP11Fly(60, slow, rate, limit, hit, maxAge, pass, out float baseMissAt);
                var baseHit = WP11Fly(60, slow, rate, limit, hit, maxAge, strafe, out float baseHitAt);
                sameFlight &= passed.Status == HomingStatus308.Missed && followedShot.Status == HomingStatus308.Hit && passed.Status == baseMiss.Status && followedShot.Status == baseHit.Status &&
                    passedAt == baseMissAt && followedAt == baseHitAt;
            }
            r.Check("W11", sameFlight,
                "the three water single shots behind a final fly the base glyph's guided flight: they hit the enemy that walks sideways and lose the one that gets behind them, at the same moments");
            // the wood body: the same reach as the base glyph
            bool sameReach = true;
            foreach (int cell in woodBody)
            {
                float reach = Ballistics(c.RowAt(cell)).F("range", 0f);
                sameReach &= reach == range && RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, reach), reach) && !RangedSingleRule308.Reaches(Vector3.zero, new Vector3(0f, 0f, reach + .01f), reach);
            }
            r.Check("W11", sameReach, "the three wood single shots behind a final reach as far as the base glyph's range and no further: a target beyond it is not hit", range.ToString("R"));
        }
    }
}
