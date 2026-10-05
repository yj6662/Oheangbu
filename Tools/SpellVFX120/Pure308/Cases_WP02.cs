// SPEC-SPELL-120-308 L2 cases of WP-02 (numeric / conditional attacks): the largest single shot, the ramped volley, the heated
// volley, the execution. Rule math only (VolleyRampRule308, VolleyHeatRule308, ExecuteRule308) plus the rows of Rules308_WP02.csv.
// What the runner cannot see: the host's judgement (targets, clocks), the hit hook on a real EnemyVitals - those are L3.
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Spellcraft;

namespace Pure308
{
    public static partial class Program
    {
        static partial void RunWP02(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W02", false, "WP-02 cases need the table"); return; }

            // grid cells of the package, by index (no glyph is named in code):
            //   5 = wood a + siot (execution), 27 = fire a + nieun (largest single shot), 87 = metal o + nieun (heated volley),
            //   89 = metal o + siot (ramped volley), 85 = metal o without a final (the base volley the two inherit)
            SpellRow execute = c.RowAt(5), big = c.RowAt(27), heat = c.RowAt(87), ramp = c.RowAt(89), volley = c.RowAt(85), woodSingle = c.RowAt(1);
            float[] brushes = { 1f, .37f };

            // ---- the four rows: handler ids, gate, statuses ----
            r.Check("W02", execute.Handler == "single.execute" && big.Handler == "core.single" && heat.Handler == "volley.heat" && ramp.Handler == "volley.ramp",
                "the four rows carry the planned handler ids", execute.Handler + " " + big.Handler + " " + heat.Handler + " " + ramp.Handler);
            var rows = new[] { execute, big, heat, ramp };
            r.Check("W02", rows.All(x => x.Feature == SpellLegacyFeature.None && x.Gate == SpellGateMode.Final && string.IsNullOrEmpty(x.Pending)),
                "new rows: no legacy feature, the final-consonant gate, not reserved");
            r.Check("W02", execute.Final == SpellFinal.Siot && big.Final == SpellFinal.Nieun && heat.Final == SpellFinal.Nieun && ramp.Final == SpellFinal.Siot,
                "finals: siot / nieun / nieun / siot (none of them is granted in the main game today)");
            var ids = new HashSet<string>(rows.Select(x => x.Handler));
            var lockedGate = new ModelGate { Registered = ids };
            var openGate = new ModelGate { Registered = ids, Unlocked = new HashSet<SpellFinal> { SpellFinal.Nieun, SpellFinal.Siot } };
            var noHandlers = new ModelGate { Unlocked = new HashSet<SpellFinal> { SpellFinal.Nieun, SpellFinal.Siot } };
            r.Check("W02", rows.All(x => SpellResolveCore308.Resolve(x.Letter[0], 1f, 1f, x, lockedGate, out _) == SpellResolveStatus.Locked) &&
                rows.All(x => SpellResolveCore308.Resolve(x.Letter[0], 1f, 1f, x, openGate, out _) == SpellResolveStatus.Ok) &&
                rows.All(x => SpellResolveCore308.Resolve(x.Letter[0], 1f, 1f, x, noHandlers, out _) == SpellResolveStatus.Unavailable),
                "each row is Locked behind its final, Ok once the final is open, Unavailable without its registered handler");
            r.Check("W02", rows.All(x => new LegacySpellGate(true, true, true, true, true).Judge(x) == SpellResolveStatus.Unavailable),
                "none of the four opens through the old flag overloads (old scenes keep their 36 glyphs)");

            // ---- largest single shot (core.single with its own base power) ----
            r.Check("난-1", big.Handler == "core.single" && big.Params.All(p => p.Key == "cost"),
                "the row runs the plain single-target judgement and carries no effect parameter (nothing to control, spread or derive)",
                big.Handler + ", " + big.Params.Length + " parameters");
            r.Check("난-1", big.AreaShape == AreaShape.None && big.AreaAngle == 0f && big.AreaRadius == 0f && big.AreaLength == 0f && big.AreaSpeed == 0f &&
                big.AreaImpactDelay == 0f && big.VolleyShots == 0 && big.VolleyInterval == 0f && !big.ScatterVolley,
                "no area geometry of any kind: no zone, no volley, no second judgement shape");
            bool bigCasts = true;
            foreach (float brush in brushes)
            {
                var status = SpellResolveCore308.Resolve(big.Letter[0], brush, brush, big, openGate, out SpellCast cast);
                bigCasts &= status == SpellResolveStatus.Ok && cast.Kind == SpellKind.AttackSingle && cast.AreaShape == AreaShape.None &&
                    cast.Power.Equals(big.BasePower * brush) && cast.SpeedMul == 1f;
            }
            r.Check("난-2", big.Kind == SpellKind.AttackSingle && bigCasts,
                "resolves as a single-target cast of base power x brush at the standard projectile speed (one judgement, one impact)");
            var singles = c.Build.Rows.Where(x => x.HasHandler && x.Kind == SpellKind.AttackSingle && x.Index != big.Index).ToArray();
            float runnerUp = singles.Length > 0 ? singles.Max(x => x.BasePower) : 0f;
            r.Fact("wp02.bigBasePower", big.BasePower); r.Fact("wp02.nextSingleBasePower", runnerUp); r.Fact("wp02.singleRowsCompared", singles.Length);
            r.Check("난-2", singles.Length >= 5 && big.BasePower > runnerUp,
                "its base power is strictly the largest of the single-shot rows of the table", big.BasePower + " against " + runnerUp + " over " + singles.Length + " rows");
            r.Check("난-2", big.BasePower > c.RowAt(49).BasePower && c.RowAt(49).Kind == SpellKind.AttackSingle,
                "Spec section 15 default: it outranks the earth single shot", big.BasePower + " > " + c.RowAt(49).BasePower);

            // ---- ramped volley ----
            float first = ramp.F("ramp.first", 0f), last = ramp.F("ramp.last", 0f);
            int shots = ramp.VolleyShots;
            r.Check("W02", ramp.Has("ramp.first") && ramp.Has("ramp.last"), "the ramp row carries its two keys");
            r.Check("솟-1", ramp.Kind == volley.Kind && ramp.AreaShape == AreaShape.Volley && ramp.BasePower == volley.BasePower && ramp.VolleyShots == volley.VolleyShots &&
                ramp.VolleyInterval == volley.VolleyInterval && ramp.AreaImpactDelay == volley.AreaImpactDelay && ramp.AreaAngle == volley.AreaAngle &&
                ramp.AreaLength == volley.AreaLength && ramp.AreaSpeed == volley.AreaSpeed && ramp.ScatterVolley == volley.ScatterVolley,
                "the row is the base volley (same power, shot count, interval, delay, fan): one cast, one plan, the same shots",
                ramp.VolleyShots + " shots, interval " + ramp.VolleyInterval);
            var weights = new List<float>(); var again = new List<float>();
            bool built = VolleyRampRule308.Weights(shots, first, last, weights);
            VolleyRampRule308.Weights(shots, first, last, again);
            r.Check("솟-1", built && shots >= 2 && weights.Count == shots && ramp.VolleyInterval > 0f,
                "one weight per shot of the plan, in launch order (shot i leaves at delay + i x interval, interval > 0)", weights.Count + " weights for " + shots + " shots");
            r.Check("솟-1", weights.SequenceEqual(again), "nothing carries over between casts: the same row gives the same weights every time");
            bool linear = built;
            for (int i = 0; built && i < shots; i++)
                linear &= Math.Abs(weights[i] - ((double)first + ((double)last - first) * i / (shots - 1))) < 1e-5;
            r.Check("솟-2", linear && built && weights[0] == first && weights[shots - 1] == last, "the weights run in a straight line from ramp.first to ramp.last", first + " .. " + last);
            foreach (float brush in brushes)
            {
                SpellResolveCore308.Resolve(ramp.Letter[0], brush, brush, ramp, openGate, out SpellCast cast);
                float sum = 0f; bool rising = built; float previous = 0f;
                for (int i = 0; built && i < shots; i++)
                {
                    float power = VolleyRampRule308.ShotPower(weights, i, cast.Power);
                    rising &= power > previous; previous = power; sum += power;
                }
                float equal = cast.Power / shots;
                r.Check("솟-2", rising && cast.Power > 0f, "every later shot carries strictly more power than the one before (brush " + brush + ")");
                r.Check("솟-2", r.Near(sum, cast.Power, cast.Power * 1e-4f), "the shots of one cast add up to the cast power (brush " + brush + ")", sum + " / " + cast.Power);
                r.Check("솟-2", built && VolleyRampRule308.ShotPower(weights, 0, cast.Power) < equal && VolleyRampRule308.ShotPower(weights, shots - 1, cast.Power) > equal,
                    "the first shot is weaker and the last shot stronger than an equal split (brush " + brush + ")",
                    VolleyRampRule308.ShotPower(weights, 0, cast.Power) + " < " + equal + " < " + VolleyRampRule308.ShotPower(weights, shots - 1, cast.Power));
            }
            r.Fact("wp02.rampShots", shots); r.Fact("wp02.rampLastOverFirst", built ? weights[shots - 1] / weights[0] : 0f);
            var refused = new List<float> { 9f };
            bool refusals = !VolleyRampRule308.Weights(1, first, last, refused) && refused.Count == 0;                 // a single shot cannot ramp
            refusals &= !VolleyRampRule308.Weights(0, first, last, refused) && !VolleyRampRule308.Weights(shots, last, first, refused);   // no shots, falling pair
            refusals &= !VolleyRampRule308.Weights(shots, first, first, refused) && !VolleyRampRule308.Weights(shots, 0f, last, refused); // flat pair, zero start
            refusals &= !VolleyRampRule308.Weights(shots, float.NaN, last, refused) && !VolleyRampRule308.Weights(shots, first, float.NaN, refused);
            refusals &= !VolleyRampRule308.Weights(shots, first, float.PositiveInfinity, refused) && refused.Count == 0;
            refusals &= !VolleyRampRule308.Weights(24, 1f, 1.0000001f, refused) && refused.Count == 0;                 // too close to rise shot by shot
            r.Check("솟-2", refusals, "a row that cannot ramp (one shot, flat, falling, zero, non-finite or too-close end weights) builds no weights at all");
            r.Check("솟-2", VolleyRampRule308.ShotPower(weights, -1, 8f) == 0f && VolleyRampRule308.ShotPower(weights, shots, 8f) == 0f && VolleyRampRule308.ShotPower(null, 0, 8f) == 0f,
                "a shot outside the plan carries nothing");

            // ---- heated volley ----
            float step = heat.F("heat.step", 0f), window = heat.F("heat.window", 0f);
            int max = (int)Math.Round(heat.F("heat.max", 0f));
            r.Check("W02", heat.Has("heat.step") && heat.Has("heat.max") && heat.Has("heat.window") && step > 0f && max >= 1 && window > 0f,
                "the heat row carries its three keys with usable values", step + " / " + max + " / " + window);
            r.Check("W02", heat.Kind == volley.Kind && heat.AreaShape == AreaShape.Volley && heat.BasePower == volley.BasePower && heat.VolleyShots == volley.VolleyShots &&
                heat.VolleyInterval == volley.VolleyInterval && heat.AreaImpactDelay == volley.AreaImpactDelay, "the heat row is the base volley (same power, shots, interval, delay)");
            var state = default(VolleyHeat308);
            float clock = 100f, gap = window * .5f;
            var multipliers = new List<float>(); var stacks = new List<int>();
            for (int cast = 0; cast < max + 3; cast++)
            {
                multipliers.Add(VolleyHeatRule308.Multiplier(state, clock, window, step));
                state = VolleyHeatRule308.AfterCast(state, clock, window, max);
                stacks.Add(state.Stacks);
                clock += gap;
            }
            r.Fact("wp02.heatMultipliers", string.Join(" ", multipliers.Select(m => m.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            bool counted = true;
            for (int cast = 0; cast < stacks.Count; cast++) counted &= stacks[cast] == Math.Min(cast + 1, max);
            r.Check("손-1", counted, "every cast made inside the window of the one before adds one stack (up to the cap)", string.Join(" ", stacks));
            r.Check("손-1", multipliers[0] == 1f, "the first cast is cold: no bonus without a cast before it");
            var asked = new VolleyHeat308 { Stacks = 2, LastCastAt = 50f };
            float askedOnce = VolleyHeatRule308.Multiplier(asked, 51f, window, step);
            for (int i = 0; i < 5; i++) VolleyHeatRule308.Multiplier(asked, 51f, window, step);
            r.Check("손-1", asked.Stacks == 2 && asked.LastCastAt == 50f && VolleyHeatRule308.Multiplier(asked, 51f, window, step) == askedOnce,
                "only an accepted cast heats: reading the multiplier (a cast that then fails for ink) changes nothing");
            bool stepped = true;
            for (int cast = 0; cast < multipliers.Count; cast++)
                stepped &= r.Near(multipliers[cast], 1f + step * Math.Min(cast, max), 1e-5f) && (cast == 0 || cast > max || multipliers[cast] > multipliers[cast - 1]);
            r.Check("손-2", stepped, "the next volley's power rises by one step per stack: 1, 1+step, 1+2 step ...", string.Join(" ", multipliers));
            r.Check("손-2", r.Near(multipliers[multipliers.Count - 1], 1f + step * max, 1e-5f) && multipliers[multipliers.Count - 1] == multipliers[max],
                "the bonus stops at the cap (heat.max stacks)", "cap multiplier " + (1f + step * max));
            foreach (float brush in brushes)
            {
                SpellResolveCore308.Resolve(heat.Letter[0], brush, brush, heat, openGate, out SpellCast cast);
                var hot = new VolleyHeat308 { Stacks = 2, LastCastAt = 10f };
                float heated = cast.Power * VolleyHeatRule308.Multiplier(hot, 11f, window, step);
                r.Check("손-2", heated > cast.Power && r.Near(heated, heat.BasePower * brush * (1f + 2f * step), 1e-4f),
                    "the heated cast power = base power x brush x multiplier (brush " + brush + ")", heated + " from " + cast.Power);
            }
            var held = new VolleyHeat308 { Stacks = max, LastCastAt = 20f };
            r.Check("손-3", VolleyHeatRule308.Stacks(held, 20f + window, window) == max && VolleyHeatRule308.Multiplier(held, 20f + window, window, step) > 1f,
                "at the edge of the window the stack still counts");
            r.Check("손-3", VolleyHeatRule308.Stacks(held, 20f + window + .01f, window) == 0 && VolleyHeatRule308.Multiplier(held, 20f + window + .01f, window, step) == 1f,
                "past the window the stack is 0 and the next volley is cold again");
            var restarted = VolleyHeatRule308.AfterCast(held, 20f + window * 3f, window, max);
            r.Check("손-3", restarted.Stacks == 1 && restarted.LastCastAt == 20f + window * 3f, "a cast after the window starts over from one stack");
            var refreshed = VolleyHeatRule308.AfterCast(new VolleyHeat308 { Stacks = 1, LastCastAt = 20f }, 20f + window, window, max);
            r.Check("손-3", refreshed.Stacks == 2 && VolleyHeatRule308.Stacks(refreshed, 20f + window * 2f, window) == 2 &&
                VolleyHeatRule308.Stacks(new VolleyHeat308 { Stacks = 1, LastCastAt = 20f }, 20f + window * 2f, window) == 0,
                "each cast restarts the window from its own clock (the stack lives as long as the casts keep coming)");
            r.Check("손-3", VolleyHeatRule308.Stacks(default(VolleyHeat308), 5f, window) == 0 && VolleyHeatRule308.Multiplier(default(VolleyHeat308), 5f, window, step) == 1f,
                "a cleared state (death, rest, scene leave) is cold");
            r.Check("손-3", VolleyHeatRule308.Stacks(held, 21f, 0f) == 0 && VolleyHeatRule308.Stacks(held, float.NaN, window) == 0 &&
                VolleyHeatRule308.Multiplier(held, 21f, window, -1f) == 1f && VolleyHeatRule308.AfterCast(held, 21f, window, 0).Stacks == 0,
                "broken numbers never heat: no window, a non-finite clock, a negative step, a cap of 0");

            // ---- execution ----
            float threshold = execute.F("execute.hp01", 0f), bonus = execute.F("execute.bonus", 0f);
            r.Check("W02", execute.Has("execute.hp01") && execute.Has("execute.bonus") && threshold > 0f && threshold < 1f && bonus > 0f,
                "the execution row carries its two keys with usable values", threshold + " / " + bonus);
            r.Check("W02", execute.Kind == SpellKind.AttackSingle && execute.AreaShape == AreaShape.None && execute.BasePower == woodSingle.BasePower &&
                execute.ProjectileSpeedMul == woodSingle.ProjectileSpeedMul, "the execution row is the wood single shot (same base power and projectile speed)");
            const float MaxHp = 1000f;
            r.Check("갓-1", WP02Near(ExecuteRule308.Hp01Before(292f, 8f, MaxHp), .3f) && ExecuteRule308.Hp01Before(0f, 50f, MaxHp) == .05f && ExecuteRule308.Hp01Before(992f, 8f, MaxHp) == 1f,
                "the HP ratio just before the impact = (HP now + what the impact took) / max HP");
            r.Check("갓-1", ExecuteRule308.Executes(ExecuteRule308.Hp01Before(MaxHp * threshold - 8f, 8f, MaxHp), threshold),
                "a target exactly at the threshold before the impact executes (the edge counts)", ExecuteRule308.Hp01Before(MaxHp * threshold - 8f, 8f, MaxHp) + " <= " + threshold);
            r.Check("갓-1", !ExecuteRule308.Executes(ExecuteRule308.Hp01Before(MaxHp * threshold - 7f, 8f, MaxHp), threshold) && !ExecuteRule308.Executes(1f, threshold) &&
                !ExecuteRule308.Executes(.9f, threshold), "a target above the threshold does not execute (healthy, 90 %, a hair above the edge)");
            r.Check("갓-1", !ExecuteRule308.Executes(ExecuteRule308.Hp01Before(MaxHp * threshold - 10f, 20f, MaxHp), threshold),
                "the ratio is taken before the impact: a hit that itself pushes the target under the threshold does not execute");
            r.Check("갓-1", ExecuteRule308.Executes(.01f, threshold) && !ExecuteRule308.Executes(.01f, 0f) && !ExecuteRule308.Executes(.01f, -1f) &&
                !ExecuteRule308.Executes(float.NaN, threshold) && ExecuteRule308.Hp01Before(10f, 5f, 0f) == 0f,
                "low HP executes; a threshold of 0 or less, a non-finite ratio never does");
            foreach (float brush in brushes)
            {
                SpellResolveCore308.Resolve(execute.Letter[0], brush, brush, execute, openGate, out SpellCast cast);
                float extra = ExecuteRule308.BonusPower(.25f, threshold, cast.Power, bonus);
                r.Check("갓-2", cast.Power > 0f && extra.Equals(cast.Power * bonus) && ExecuteRule308.BonusPower(threshold, threshold, cast.Power, bonus).Equals(cast.Power * bonus),
                    "at or below the threshold the extra hit is cast power x execute.bonus (brush " + brush + ")", extra + " on top of " + cast.Power);
                r.Check("갓-2", ExecuteRule308.BonusPower(.9f, threshold, cast.Power, bonus) == 0f && ExecuteRule308.BonusPower(1f, threshold, cast.Power, bonus) == 0f,
                    "above the threshold there is no extra hit: the base damage only (brush " + brush + ")");
            }
            r.Check("갓-2", bonus >= 1f, "TEST value: the extra hit is at least as large as the hit itself (a big extra damage)", "execute.bonus " + bonus);
            r.Check("갓-2", ExecuteRule308.BonusPower(.1f, threshold, 0f, bonus) == 0f && ExecuteRule308.BonusPower(.1f, threshold, 10f, 0f) == 0f &&
                ExecuteRule308.BonusPower(.1f, threshold, float.NaN, bonus) == 0f, "no cast power or no bonus = no extra hit");
            var marks = new ExecuteMarks308();
            marks.Add(7, 5f, 12f, bonus, threshold, 'x'); marks.Add(0, 5f, 12f, bonus, threshold, 'x');
            bool once = marks.Count == 1 && !marks.TryTake(8, out _) && marks.Count == 1;                           // another attack's hit is not ours
            once &= marks.TryTake(7, out ExecuteMark308 taken) && taken.CastPower == 12f && taken.Bonus == bonus && taken.Threshold01 == threshold && marks.Count == 0;
            once &= !marks.TryTake(7, out _);                                                                         // the extra hit carries the same id: nothing left to take
            r.Check("갓-2", once, "one extra hit per attack identity: the mark is taken by the impact and the extra hit (same identity) finds none");
            // a whole impact on a model target: HP, the planned hit, the hook, the re-heard extra hit
            foreach (float startHp in new[] { 250f, 300f, 310f, 900f })
            {
                var model = new ExecuteMarks308(); float hp = startHp, power = 12f; int hits = 0;
                model.Add(41, 3f, power, bonus, threshold, 'x');
                void Land(float amount)                                   // what ApplyConfirmedEnemyHit + the hit hook do, without Unity objects
                {
                    float applied = Math.Min(amount, hp); hp -= applied; hits++;
                    if (!model.TryTake(41, out ExecuteMark308 mark) || hp <= 0f) return;
                    float extra = ExecuteRule308.BonusPower(ExecuteRule308.Hp01Before(hp, applied, MaxHp), mark.Threshold01, mark.CastPower, mark.Bonus);
                    if (extra > 0f) Land(extra);
                }
                Land(power);
                bool low = startHp / MaxHp <= threshold;
                r.Check("갓-2", hits == (low ? 2 : 1) && WP02Near(startHp - hp, low ? power * (1f + bonus) : power) && model.Count == 0,
                    "model target at " + startHp + " of " + MaxHp + ": " + (low ? "base hit + one extra hit" : "base hit only"), hits + " hits, " + (startHp - hp) + " damage");
            }
            var waiting = new ExecuteMarks308();
            waiting.Add(9, 10f, 12f, bonus, threshold, 'x');
            waiting.Expire(9.9f); waiting.Expire(9.95f);
            bool expiry = waiting.Count == 1;                                  // not due yet: any number of frames
            waiting.Expire(10f); expiry &= waiting.Count == 1 && waiting.TryTake(9, out _);   // the frame of the impact: the mark is still there for the hit
            waiting.Add(10, 10f, 12f, bonus, threshold, 'x');
            waiting.Expire(10f); waiting.Expire(10.02f); expiry &= waiting.Count == 0;        // the hit never landed: dropped on the next frame
            waiting.Add(11, 10f, 12f, bonus, threshold, 'x'); waiting.Clear(); expiry &= waiting.Count == 0;
            r.Check("갓-2", expiry, "a mark waits for its impact, survives the frame of the impact, and is dropped when the hit never landed (target died or hid first)");
        }

        static bool WP02Near(float a, float b) => Math.Abs(a - b) <= 1e-4f * Math.Max(1f, Math.Abs(b));
    }
}
