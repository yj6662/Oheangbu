using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-02 fixtures (SPEC-SPELL-120-308 section 13): the largest single shot, the ramped volley, the heated volley and
    // the execution, on the real wiring. Every cast comes from the resolver (f.Cast); impacts land through the wiring's own
    // tick (f.Land). What stays for Play: real brush input, the flight of the shots and the adapter's presentation, and the
    // heat window running out (Edit Mode has a frozen clock; the window edge is an L2 case of VolleyHeatRule308).
    public static partial class Spell120Checks308
    {
        static partial void FixturesWP02(Report r)
        {
            // grid cells by index (no glyph is named): 5 = wood a + siot (execution), 27 = fire a + nieun (largest single shot),
            // 87 = metal o + nieun (heated volley), 89 = metal o + siot (ramped volley)
            char execute = SpellGrammar308.LetterAt(5), big = SpellGrammar308.LetterAt(27), heat = SpellGrammar308.LetterAt(87), ramp = SpellGrammar308.LetterAt(89);
            if (!r.Wants(new string(new[] { execute, big, heat, ramp }))) return;
            string[] sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP02" };
            using (var f = NewFixture(r, SpellEffectInstaller308.CreateAll(), sheets))
            {
                float cost = f.Config.SpellInkCost, brush = f.Brush();
                f.Resolver.TryRow(execute, out var executeRow); f.Resolver.TryRow(big, out var bigRow);
                f.Resolver.TryRow(heat, out var heatRow); f.Resolver.TryRow(ramp, out var rampRow);
                if (!r.Check("W02", executeRow != null && bigRow != null && heatRow != null && rampRow != null && executeRow.Handler == ExecuteEffect308.HandlerId &&
                    bigRow.Handler == CoreSingleEffect308.HandlerId && heatRow.Handler == VolleyHeatEffect308.HandlerId && rampRow.Handler == VolleyRampEffect308.HandlerId,
                    "the four rows of Rules308_WP02 are in the fixture book with their handlers")) return;
                r.Check("W02", f.Registry.Has(ExecuteEffect308.HandlerId) && f.Registry.Has(VolleyHeatEffect308.HandlerId) && f.Registry.Has(VolleyRampEffect308.HandlerId),
                    "the installer registers the three handlers of the package");
                EnemyVitals[] everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                float[] Hp() => everyone.Select(e => e.Hp).ToArray();

                // ---- behind their finals the four glyphs are the usual misfire ----
                foreach (char letter in new[] { execute, big, heat, ramp })
                {
                    f.RestoreAll(); f.Aim(f.Inside[0]); float inkBefore = f.Ink.Value; f.Cast(letter);
                    r.Check("W02", f.Misfires.Count == 1 && f.Misfires[0].Value == SpellResolveStatus.Locked && Near(inkBefore - f.Ink.Value, f.Config.MisfireInkCost) &&
                        f.Accepted.Count == 0 && f.PendingCount == 0, "cell " + SpellGrammar308.IndexOf(letter) + ": a locked final is the usual misfire (misfire ink, no effect)");
                }
                f.Finals.Open.Add(SpellFinal.Nieun); f.Finals.Open.Add(SpellFinal.Siot);

                // ---- largest single shot: one impact, nothing else ----
                f.RestoreAll(); f.Aim(f.Inside[0]);
                float ink = f.Ink.Value; float[] before = Hp(); float groggy = f.Inside[0].Groggy.Value01;
                f.Cast(big);
                bool onePlan = f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Area == null && f.Plans[0].Hits.Count == 1 &&
                    f.Plans[0].Hits[0].Target == f.Inside[0] && f.PendingCount == 1 && Near(ink - f.Ink.Value, cost);
                r.Check("난-2", onePlan && Near(f.Plans[0].Hits[0].Power, bigRow.BasePower * brush) && f.Accepted[0].Kind == SpellKind.AttackSingle,
                    "one spell cost, one single-target plan, one scheduled hit on the aimed target of base power x brush");
                f.Land();
                float[] after = Hp();
                r.Check("난-2", onePlan && f.Hits.Count == 1 && Near(before[0] - after[0], bigRow.BasePower * brush) && f.PendingCount == 0,
                    "the hit lands once for base power x brush (" + (bigRow.BasePower * brush).ToString("0.###") + ")");
                bool othersUntouched = true;
                for (int i = 1; i < everyone.Length; i++) othersUntouched &= before[i] == after[i];
                r.Check("난-1", othersUntouched && f.Hits.Count == 1 && !f.Inside[0].Control.BlocksActions(Time.time) && f.Inside[0].Control.MovementScale(Time.time) == 1f &&
                    f.Inside[0].Groggy.Value01 == groggy, "nothing but that hit: no other enemy is touched, no control on the target, no groggy, no second judgement");
                var plain = f.Registry.Find(bigRow.Handler);
                r.Check("난-1", plain != null && plain.Params.Count == 0 && !(plain is ISpellHitHook) && !(plain is ISpellCastHook) && !(plain is ISpellAcceptHook) &&
                    !(plain is ISpellCostHook) && !(plain is IPlayerHitHook), "the row's handler has no parameter and no hook (it cannot derive, spread or control)");
                float strongest = 0f;
                for (int index = 1; index <= SpellGrammar308.Count; index++)
                    if (index != bigRow.Index && f.Resolver.TryRow(SpellGrammar308.LetterAt(index), out var other) && other.HasHandler && other.Kind == SpellKind.AttackSingle)
                        strongest = Mathf.Max(strongest, other.BasePower);
                r.Check("난-2", bigRow.BasePower > strongest && strongest > 0f, "its base power is the largest of the single-shot rows in the book (" + bigRow.BasePower + " > " + strongest + ")");

                // ---- ramped volley: one cast, later shots stronger ----
                f.RestoreAll(); f.Aim(f.Inside[0]); ink = f.Ink.Value; before = Hp();
                f.Cast(ramp);
                var rampPlan = f.Plans.Count == 1 ? f.Plans[0] : null;
                bool oneVolley = f.Misfires.Count == 0 && f.Accepted.Count == 1 && rampPlan != null && rampPlan.Area != null && rampPlan.Area.Shape == AreaShape.Volley &&
                    rampPlan.Area.Shots.Count == rampRow.VolleyShots && Near(ink - f.Ink.Value, cost);
                r.Check("솟-1", oneVolley, "one cast = one spell cost and one volley plan with the row's shot count (" + rampRow.VolleyShots + ")");
                if (oneVolley)
                {
                    var weights = new List<float>();
                    VolleyRampRule308.Weights(rampRow.VolleyShots, rampRow.F("ramp.first", 0f), rampRow.F("ramp.last", 0f), weights);
                    float castPower = f.Accepted[0].Power, reserved = 0f, strongestSoFar = 0f, planned = 0f; int found = 0; bool inOrder = true, shares = weights.Count == rampRow.VolleyShots;
                    for (int i = 0; shares && i < rampPlan.Area.Shots.Count; i++)
                    {
                        var shot = rampPlan.Area.Shots[i];
                        inOrder &= i == 0 || shot.LaunchTime > rampPlan.Area.Shots[i - 1].LaunchTime;
                        float expected = VolleyRampRule308.ShotPower(weights, i, castPower);
                        planned += expected;
                        if (shot.Target == null) continue;       // a scattered ray that crosses no enemy reserves no damage
                        found++; reserved += shot.Power;
                        shares &= Near(shot.Power, expected, .0001f) && shot.Power > strongestSoFar;
                        strongestSoFar = shot.Power;
                    }
                    r.Check("솟-1", inOrder && Near(castPower, rampRow.BasePower * brush), "the shots of the plan leave in index order; the cast power is base power x brush");
                    r.Check("솟-2", shares && found >= 2, "every shot that found a target carries its ramp share, and each is stronger than every earlier one (" + found + " of " + rampRow.VolleyShots + " shots found a target)");
                    r.Check("솟-2", shares && Near(planned, castPower, .001f), "the shares of all shots add up to the cast power (" + planned.ToString("0.###") + " / " + castPower.ToString("0.###") + ")");
                    f.Land();
                    after = Hp(); float lost = 0f;
                    for (int i = 0; i < everyone.Length; i++) lost += before[i] - after[i];
                    r.Check("솟-2", Near(lost, reserved, .01f) && f.Hits.Count == found && f.PendingCount == 0, "the reserved shots land for exactly their ramped powers (" + lost.ToString("0.###") + ")");
                }

                // ---- heated volley: each cast inside the window raises the next one ----
                float step = heatRow.F("heat.step", 0f); int max = Mathf.RoundToInt(heatRow.F("heat.max", 0f));
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Wiring.ClearSpells308(SpellClearReason.Rest);
                var powers = new List<float>(); bool paid = true, evenShots = true;
                for (int cast = 0; cast < max + 2; cast++)
                {
                    f.Ink.Restore(); f.ClearLog(); ink = f.Ink.Value;
                    f.Cast(heat);
                    bool accepted = f.Accepted.Count == 1 && f.Plans.Count == 1;
                    powers.Add(accepted ? f.Accepted[0].Power : float.NaN);
                    paid &= accepted && Near(ink - f.Ink.Value, cost);
                    if (!accepted) continue;
                    foreach (var shot in f.Plans[0].Area.Shots)   // the heated power is split evenly, as the base volley splits it
                        if (shot.Target != null) evenShots &= Near(shot.Power, f.Accepted[0].Power / heatRow.VolleyShots, .0001f);
                }
                bool rising = paid;
                for (int cast = 0; cast < powers.Count; cast++)
                    rising &= Near(powers[cast], heatRow.BasePower * brush * (1f + step * Mathf.Min(cast, max)), .0005f);
                r.Check("손-1", paid && Near(powers[0], heatRow.BasePower * brush), "casts made one after another: the first is cold (base power x brush), every one costs one spell cost");
                r.Check("손-2", rising && evenShots, "each cast is one step stronger than the one before, up to the cap: x" + string.Join(" x", powers.Select(p => (p / (heatRow.BasePower * brush)).ToString("0.##"))));
                // another glyph in between is neither heated nor does it change the heat
                f.Ink.Restore(); f.ClearLog(); f.Cast(big);
                bool otherPlain = f.Accepted.Count == 1 && Near(f.Accepted[0].Power, bigRow.BasePower * brush);
                f.Ink.Restore(); f.ClearLog(); f.Cast(heat);
                r.Check("손-1", otherPlain && f.Accepted.Count == 1 && Near(f.Accepted[0].Power, heatRow.BasePower * brush * (1f + step * max), .0005f),
                    "only the heated volley's own casts count: another glyph is cast at its plain power and leaves the heat as it was");
                // a cast that fails for ink does not heat
                f.Wiring.ClearSpells308(SpellClearReason.Rest);
                f.Ink.Restore(); f.ClearLog(); f.Cast(heat);                 // stack 1
                f.Ink.Restore(cost * .5f); f.ClearLog(); f.Cast(heat);       // not enough ink: no cast
                bool refusedCast = f.Accepted.Count == 0 && f.Misfires.Count == 0;
                f.Ink.Restore(); f.ClearLog(); f.Cast(heat);                 // still one stack behind it
                r.Check("손-1", refusedCast && f.Accepted.Count == 1 && Near(f.Accepted[0].Power, heatRow.BasePower * brush * (1f + step), .0005f),
                    "a cast that fails for ink adds no heat: the next one carries one step, not two");
                f.Wiring.ClearSpells308(SpellClearReason.Rest);
                f.Ink.Restore(); f.ClearLog(); f.Cast(heat);
                r.Check("손-3", f.Accepted.Count == 1 && Near(f.Accepted[0].Power, heatRow.BasePower * brush),
                    "the stack is the caster's own state: cleared with the effects (death, rest, scene leave), the next volley is cold again");

                // ---- execution: one extra hit under the same attack identity, only at or below the threshold ----
                float threshold = executeRow.F("execute.hp01", 0f), bonus = executeRow.F("execute.bonus", 0f);
                var executeEffect = f.Registry.Find(ExecuteEffect308.HandlerId) as ExecuteEffect308;
                float hitPower = executeRow.BasePower * brush;
                void Execution(string name, float startHp, bool expectExtra)
                {
                    f.RestoreAll(); var target = f.Inside[0]; f.Aim(target);
                    target.TakeDamage(target.MaxHp - startHp);                       // bring it down outside the wiring
                    float hp = target.Hp, groggyBefore = target.Groggy.Value01; float[] hpBefore = Hp();
                    f.Cast(execute);
                    bool planned = f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Target == target &&
                        Near(f.Plans[0].Hits[0].Power, hitPower) && f.PendingCount == 1 && executeEffect != null && executeEffect.PendingMarks == 1;
                    f.Land();
                    float[] hpAfter = Hp(); bool alone = true;
                    for (int i = 1; i < everyone.Length; i++) alone &= hpBefore[i] == hpAfter[i];
                    long id = planned ? f.Plans[0].Hits[0].AttackId : 0;
                    if (expectExtra)
                        r.Check("갓-2", planned && f.Hits.Count == 2 && f.Hits.All(h => h.Attack.AttackId == id && h.Attack.Source == DamageSource.PlayerDirect && h.Target == target) &&
                            Near(f.Hits[0].AppliedDamage, hitPower) && Near(f.Hits[1].AppliedDamage, hitPower * bonus) && Near(hp - target.Hp, hitPower * (1f + bonus)) &&
                            alone && executeEffect.PendingMarks == 0 && target.Groggy.Value01 == groggyBefore,
                            name + ": the base hit, then exactly one extra hit of cast power x bonus under the same attack identity; no groggy, nobody else is touched");
                    else
                        r.Check("갓-2", planned && f.Hits.Count == 1 && f.Hits[0].Attack.AttackId == id && Near(hp - target.Hp, hitPower) && alone && executeEffect.PendingMarks == 0,
                            name + ": the base hit only");
                    r.Check("갓-1", planned && (f.Hits.Count == 2) == (hp / target.MaxHp <= threshold), name + ": executes exactly when the HP ratio before the impact is at or below the threshold (" + (hp / target.MaxHp).ToString("0.###") + " / " + threshold + ")");
                }
                float maxHp = f.Inside[0].MaxHp, edge = Mathf.Round(maxHp * threshold);
                Execution("low target", Mathf.Round(maxHp * threshold * .5f), true);
                Execution("target exactly at the threshold", edge, true);
                Execution("target just above the threshold", edge + 1f, false);
                Execution("healthy target", maxHp, false);
                // the hit that kills needs no execution, and leaves no mark behind
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Inside[0].TakeDamage(f.Inside[0].MaxHp - hitPower * .5f);
                f.Cast(execute); f.Land();
                r.Check("갓-2", f.Hits.Count == 1 && f.Hits[0].Killed && !f.Inside[0].IsAlive && executeEffect != null && executeEffect.PendingMarks == 0,
                    "a target the base hit kills takes no extra hit; the mark is gone");
                // another glyph's hit on a low target never executes
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Inside[0].TakeDamage(f.Inside[0].MaxHp - Mathf.Round(maxHp * threshold * .5f));
                float lowHp = f.Inside[0].Hp;
                f.Cast(big); f.Land();
                r.Check("갓-1", f.Hits.Count == 1 && Near(lowHp - f.Inside[0].Hp, bigRow.BasePower * brush), "only the execution glyph's own impact executes: another glyph's hit on a low target is its plain damage");
                // cleared marks never execute
                f.RestoreAll(); f.Aim(f.Inside[0]); f.Inside[0].TakeDamage(f.Inside[0].MaxHp - Mathf.Round(maxHp * threshold * .5f));
                lowHp = f.Inside[0].Hp;
                f.Cast(execute);
                int marksBefore = executeEffect != null ? executeEffect.PendingMarks : -1;
                executeEffect?.Clear(SpellClearReason.Rest);
                f.Land();
                r.Check("갓-2", marksBefore == 1 && executeEffect.PendingMarks == 0 && f.Hits.Count == 1 && Near(lowHp - f.Inside[0].Hp, hitPower),
                    "a cleared effect (death, rest, scene leave) holds no mark: a hit that still lands is the base hit only");
            }
        }
    }
}
