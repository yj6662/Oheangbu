// SPEC-SPELL-120-308 L2 cases of WP-08 (the nine self buffs): the pure rules of App/BuffRule308.cs and the shared timer
// App/SpellBuffState308.cs, with the numbers of Rules308_WP08.csv. Rows are found by handler id (no glyph is named here).
// What this cannot see: the effect classes' Unity half (PlayerVitals.Heal, InkPool.SetTemporaryCapacity, EnemyControlState,
// ApplyDirectHit, the presenter) - that is Spell120Checks308.WP08.cs (edit mode) and Play.
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
        static partial void RunWP08(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W08", false, "WP-08 cases need the table"); return; }
            SpellRow Row(string handler) => c.Build.Rows.SingleOrDefault(x => x.Handler == handler && x.Feature == SpellLegacyFeature.None);
            SpellRow heal = Row("buff.burstheal"), empower = Row("buff.empower"), steadfast = Row("buff.steadfast"), swift = Row("buff.swift"),
                chain = Row("buff.chain"), reserve = Row("buff.reserve"), reflect = Row("buff.reflect"), slowback = Row("buff.slowback"), insight = Row("buff.insight");
            var rows = new[] { heal, empower, steadfast, swift, chain, reserve, reflect, slowback, insight };
            if (!r.Check("W08", rows.All(x => x != null), "the nine WP-08 rows are in the table (Rules308_WP08.csv), one handler each")) return;
            float[] frameRates = { 30f, 60f, 120f };

            // ---- the rows: buffs behind the nieun / mieum finals, no base power, a duration each ----
            r.Check("W08", rows.All(x => x.Category == SpellCategory.Buff && x.Kind == SpellKind.Buff && x.BasePower == 0f && x.Gate == SpellGateMode.Final &&
                (x.Final == SpellFinal.Nieun || x.Final == SpellFinal.Mieum) && x.F("duration", 0f) > 0f),
                "every row: category and kind Buff, base power 0, nieun or mieum final behind the unlock gate, a positive duration");
            var ids = new HashSet<string>(rows.Select(x => x.Handler));
            var shut = new ModelGate { Registered = ids };
            var open = new ModelGate { Registered = ids, Unlocked = new HashSet<SpellFinal> { SpellFinal.Nieun, SpellFinal.Mieum } };
            bool locked = true, casts = true;
            foreach (var row in rows)
            {
                locked &= NewResolve(row.Letter[0], shut, row, .8f).Key == SpellResolveStatus.Locked;
                var opened = NewResolve(row.Letter[0], open, row, .8f);
                casts &= opened.Key == SpellResolveStatus.Ok && opened.Value.Kind == SpellKind.Buff && opened.Value.Power == 0f && opened.Value.Element == row.Element;
            }
            r.Check("W08", locked, "without the final unlocked every one of the nine is Locked (the ordinary misfire)");
            r.Check("W08", casts, "with the final unlocked each resolves to a Buff cast of power 0 and the glyph's own element");

            // ---- the shared timer (Q8 default: the EA buff rule for every buff) ----
            {
                var buffs = new SpellBuffState308();
                char a = heal.Letter[0], b = empower.Letter[0];
                buffs.Activate(a, 0f, 30f); buffs.Activate(b, 5f, 30f);
                r.Check("W08", buffs.Active(a, 20f) && buffs.Active(b, 20f) && !buffs.Active(a, 30f) && buffs.Active(b, 30f), "different buffs run side by side, each on its own time");
                buffs.Activate(a, 10f, 30f);
                r.Check("W08", r.Near(buffs.Remaining(a, 10f), 30f), "recasting a buff replaces its remaining time with the new duration (never additive)", buffs.Remaining(a, 10f).ToString(CultureInfo.InvariantCulture));
                buffs.Clear();
                r.Check("W08", !buffs.Active(a, 11f) && !buffs.Active(b, 11f), "a clear (death, rest, scene leave) ends every buff: nothing is kept");
            }

            // ---- burst heal: short, and large ----
            {
                float duration = heal.F("duration", 0f), total = heal.F("heal.hp01", 0f);
                // the reference: the existing wood regeneration buff, read from the profile asset the main scene uses
                float regenDuration = float.NaN, regenRate = float.NaN; string source = "";
                string folder = Path.Combine(c.Root, "Oheangbu", "Assets", "_Project", "Art", "World", "Architecture296", "Data");
                if (Directory.Exists(folder))
                    foreach (string path in Directory.GetFiles(folder, "*EABuffs_TEST.asset"))
                    {
                        source = Path.GetFileName(path);
                        foreach (string line in File.ReadAllLines(path))
                        {
                            string text = line.Trim();
                            if (text.StartsWith("Duration:", StringComparison.Ordinal)) regenDuration = float.Parse(text.Substring(9).Trim(), CultureInfo.InvariantCulture);
                            else if (text.StartsWith("RegenMaxHpPerSecond:", StringComparison.Ordinal)) regenRate = float.Parse(text.Substring(20).Trim(), CultureInfo.InvariantCulture);
                        }
                    }
                r.Fact("wp08.regenReference", source + " duration " + regenDuration.ToString(CultureInfo.InvariantCulture) + " rate " + regenRate.ToString(CultureInfo.InvariantCulture));
                r.Fact("wp08.burstHeal", "duration " + duration.ToString(CultureInfo.InvariantCulture) + " total " + total.ToString(CultureInfo.InvariantCulture));
                bool reference = r.Check("W08", !float.IsNaN(regenDuration) && !float.IsNaN(regenRate) && regenDuration > 0f,
                    "the reference numbers of the existing regeneration buff were read from the main scene's EA buff profile", source);
                r.Check("건-1", reference && duration > 0f && duration < regenDuration, "the burst heal runs shorter than the regeneration buff",
                    duration.ToString(CultureInfo.InvariantCulture) + " s < " + regenDuration.ToString(CultureInfo.InvariantCulture) + " s");
                r.Check("건-2", reference && total > regenRate * duration, "over its own window it heals more than the regeneration buff heals in the same time",
                    total.ToString(CultureInfo.InvariantCulture) + " > " + (regenRate * duration).ToString(CultureInfo.InvariantCulture));
                foreach (float fps in frameRates)
                {
                    var state = new BurstHealState308();
                    float start = 10f, sum = state.Begin(start, duration, total, true), late = 0f; bool ended = false;
                    int frames = Mathf.CeilToInt((duration + 1f) * fps);
                    for (int i = 1; i <= frames; i++)
                    {
                        float now = start + i / fps, share = state.Advance(now, true);
                        if (ended) late += share; else sum += share;
                        if (now >= start + duration) ended = true;                        // the frame that reaches the end pays the last piece
                    }
                    r.Check("건-2", r.Near(sum, total, 1e-3f) && ended && late == 0f && !state.Running, "the whole share is paid inside the window, nothing after it (" + fps + " fps)",
                        "sum " + sum.ToString(CultureInfo.InvariantCulture) + " late " + late.ToString(CultureInfo.InvariantCulture));
                }
                var dead = new BurstHealState308();
                dead.Begin(0f, duration, total, true);
                float first = dead.Advance(duration * .25f, true), atDeath = dead.Advance(duration * .5f, false), later = dead.Advance(duration * .75f, true);
                r.Check("건-2", first > 0f && atDeath == 0f && later == 0f && !dead.Running, "a dead player is not healed, and the buff does not come back after the death (no revival)");
                var refused = new BurstHealState308();
                r.Check("건-2", refused.Begin(0f, duration, total, false) == 0f && !refused.Running, "a cast on a dead player starts nothing");
                var recast = new BurstHealState308();
                recast.Begin(0f, duration, total, true);
                float owed = recast.Begin(duration * .5f, duration, total, true), rest = recast.Advance(duration * 2f, true);
                r.Check("건-2", r.Near(owed, total * .5f) && r.Near(rest, total), "a recast pays what the old window owed up to then and replaces the rest (not additive)",
                    "owed " + owed.ToString(CultureInfo.InvariantCulture) + " then " + rest.ToString(CultureInfo.InvariantCulture));
            }

            // ---- empower: the next attack, once ----
            {
                float duration = empower.F("duration", 0f), mul = empower.F("empower.mul", 0f);
                char buff = empower.Letter[0], attack = SpellGrammar308.LetterAt(1), other = SpellGrammar308.LetterAt(25);
                var buffs = new SpellBuffState308(); var state = new EmpowerState308();
                float power = 12f, speed = 1.3f;
                state.Modify(buffs, buff, attack, SpellKind.AttackSingle, mul, 0f, ref power, ref speed);
                r.Check("넌-1", power == 12f && !state.Accept(buffs, buff, attack, SpellKind.AttackSingle, 0f), "without the buff an attack is not changed");
                buffs.Activate(buff, 0f, duration, 1);
                buffs.Activate(buff, 0f, duration, 1);
                r.Check("넌-1", buffs.Charges(buff, 0f) == 1, "the buff holds one charge; a recast does not stack a second one");
                bool untouched = true;
                foreach (SpellKind kind in new[] { SpellKind.Parry, SpellKind.Summon, SpellKind.Field, SpellKind.Buff, SpellKind.Ward, SpellKind.Install })
                {
                    float p = 5f, s = 1f;
                    state.Modify(buffs, buff, other, kind, mul, 1f, ref p, ref s);
                    untouched &= p == 5f && s == 1f && !state.Accept(buffs, buff, other, kind, 1f) && buffs.Charges(buff, 1f) == 1;
                }
                r.Check("넌-1", untouched, "a cast that is not an attack (parry, summon, field, buff, ward, install) neither takes nor uses the charge");
                power = 12f; speed = 1.3f;
                state.Modify(buffs, buff, attack, SpellKind.AttackSingle, mul, 2f, ref power, ref speed);
                r.Check("넌-2", r.Near(power, 12f * mul) && mul > 1f && speed == 1.3f, "the attack's power is multiplied by the row's multiplier; the projectile speed is not touched",
                    "x" + mul.ToString(CultureInfo.InvariantCulture) + " -> " + power.ToString(CultureInfo.InvariantCulture));
                r.Check("넌-1", buffs.Charges(buff, 2f) == 1, "applying the multiplier does not use the charge yet: a cast refused for ink keeps the buff");
                float areaPower = 9f, areaSpeed = 1f;
                state.Modify(buffs, buff, other, SpellKind.AttackArea, mul, 3f, ref areaPower, ref areaSpeed);
                r.Check("넌-2", r.Near(areaPower, 9f * mul), "an area attack is amplified the same way (once, not on top of the refused cast)", areaPower.ToString(CultureInfo.InvariantCulture));
                r.Check("넌-1", !state.Accept(buffs, buff, attack, SpellKind.AttackSingle, 3f) && !state.Accept(buffs, buff, other, SpellKind.AttackArea, 4f) && buffs.Charges(buff, 4f) == 1,
                    "an accepted cast that is not the one that was amplified (another glyph, another moment) does not use the charge");
                r.Check("넌-1", state.Accept(buffs, buff, other, SpellKind.AttackArea, 3f) && !buffs.Active(buff, 3f), "accepting the amplified cast uses the charge and ends the buff");
                power = 12f; speed = 1.3f;
                state.Modify(buffs, buff, attack, SpellKind.AttackSingle, mul, 5f, ref power, ref speed);
                r.Check("넌-1", power == 12f && !state.Accept(buffs, buff, attack, SpellKind.AttackSingle, 5f), "the attack after that is back to its own power: one attack only");
                buffs.Activate(buff, 10f, duration, 1);
                power = 12f;
                state.Modify(buffs, buff, attack, SpellKind.AttackSingle, mul, 10f + duration, ref power, ref speed);
                r.Check("넌-1", power == 12f && !buffs.Active(buff, 10f + duration), "an unused buff runs out after its duration");
            }

            // ---- steadfast: the letter survives a hit, the damage does not change ----
            {
                float duration = steadfast.F("duration", 0f); char buff = steadfast.Letter[0];
                var buffs = new SpellBuffState308();
                bool interrupts = true;
                BuffRule308.Steadfast(buffs.Active(buff, 1f), 7f, ref interrupts);
                bool without = interrupts;
                buffs.Activate(buff, 2f, duration);
                interrupts = true; BuffRule308.Steadfast(buffs.Active(buff, 2f + duration * .5f), 7f, ref interrupts);
                bool during = interrupts;
                interrupts = true; BuffRule308.Steadfast(buffs.Active(buff, 2f + duration), 7f, ref interrupts);
                bool afterEnd = interrupts;
                r.Check("넘-1", without && !during && afterEnd, "a hit asks for the letter to be dropped before and after the buff, and not while it runs");
                bool kept = false; BuffRule308.Steadfast(false, 7f, ref kept);
                r.Check("넘-1", !kept, "the rule never turns an interrupt on by itself (it only withholds one)");
                bool same = true;
                foreach (float damage in new[] { .5f, 7f, 33.25f, 400f })
                {
                    bool flag = true;
                    same &= BuffRule308.Steadfast(true, damage, ref flag).Equals(damage) && BuffRule308.Steadfast(false, damage, ref flag).Equals(damage);
                }
                r.Check("넘-2", same, "the damage of the hit passes through unchanged, with or without the buff (the real HP loss is the edit-mode fixture)");
            }

            // ---- swift: projectile speed up, power unchanged ----
            {
                float mul = swift.F("speed.mul", 0f);
                float power = 12f, speed = 1.3f;
                BuffRule308.Swift(true, SpellKind.AttackSingle, mul, ref power, ref speed);
                float distance = 9f, baseSpeed = 18f;
                r.Check("선-1", mul > 1f && r.Near(speed, 1.3f * mul) && power.Equals(12f), "an attack cast: projectile speed multiplier x the row's value, power bit-identical",
                    "x" + mul.ToString(CultureInfo.InvariantCulture) + " -> " + speed.ToString(CultureInfo.InvariantCulture));
                r.Check("선-1", distance / (baseSpeed * speed) < distance / (baseSpeed * 1.3f) && r.Near(distance / (baseSpeed * speed) * mul, distance / (baseSpeed * 1.3f)),
                    "the flight time over the same distance shrinks by exactly that factor");
                float areaPower = 9f, areaSpeed = 1f;
                BuffRule308.Swift(true, SpellKind.AttackArea, mul, ref areaPower, ref areaSpeed);
                r.Check("선-1", r.Near(areaSpeed, mul) && areaPower.Equals(9f), "an area attack cast gets the same multiplier on its SpellCast.SpeedMul, power unchanged");
                // review B: what that multiplier reaches. The wiring's volley and path judgements fly at the row's own AreaSpeed and never
                // read SpeedMul, so the buff speeds up the single projectile (and the giyeok trace) only. Stated here so the clause does not
                // read as "every projectile": the volley glyphs and the waves keep their speed (open question for the user).
                r.Check("선-1", c.RowAt(85).AreaShape == AreaShape.Volley && c.RowAt(85).AreaSpeed > 0f && c.RowAt(61).AreaShape == AreaShape.Path && c.RowAt(61).AreaSpeed > 0f &&
                    c.RowAt(109).AreaShape == AreaShape.Path && c.RowAt(109).AreaSpeed > 0f && c.RowAt(73).AreaShape == AreaShape.None && c.RowAt(73).AreaSpeed == 0f,
                    "declared limit: the volley and the two path glyphs carry a flight speed of their own in the area data (the cast hook cannot reach it); only a single shot flies by SpeedMul");
                bool untouched = true;
                foreach (SpellKind kind in new[] { SpellKind.Parry, SpellKind.Summon, SpellKind.Field, SpellKind.Buff, SpellKind.Ward, SpellKind.Install })
                {
                    float p = 5f, s = 1f;
                    BuffRule308.Swift(true, kind, mul, ref p, ref s);
                    untouched &= p == 5f && s == 1f;
                }
                power = 12f; speed = 1.3f;
                BuffRule308.Swift(false, SpellKind.AttackSingle, mul, ref power, ref speed);
                r.Check("선-1", untouched && power == 12f && speed == 1.3f, "casts that are not attacks, and attacks without the buff, are not changed");
            }

            // ---- chain discount: only when the element differs from the cast accepted just before ----
            {
                float discount = chain.F("chain.discount", 0f);
                var state = new ChainState308();
                r.Check("언-1", state.CostScale(true, Element.Fire, discount) == 1f, "no cast was accepted before: no discount");
                state.Record(chain.Element);                                              // the buff's own cast is the previous cast of the next one
                r.Check("언-1", state.CostScale(true, chain.Element, discount) == 1f && state.CostScale(true, Element.Fire, discount) < 1f,
                    "right after the buff's own cast: the same element pays in full, another element is discounted");
                r.Check("언-1", state.CostScale(false, Element.Fire, discount) == 1f, "without the buff a different element pays in full");
                bool pairs = true;
                foreach (Element previous in new[] { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water })
                    foreach (Element current in new[] { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water })
                    {
                        var s = new ChainState308(); s.Record(previous);
                        pairs &= (s.CostScale(true, current, discount) < 1f) == (previous != current);
                    }
                r.Check("언-1", pairs, "all 25 element pairs: discounted exactly when the two elements differ");
                var sequence = new ChainState308(); sequence.Record(Element.Water);
                float a = sequence.CostScale(true, Element.Fire, discount); sequence.Record(Element.Fire);
                float b = sequence.CostScale(true, Element.Fire, discount); sequence.Record(Element.Fire);
                float d = sequence.CostScale(true, Element.Wood, discount); sequence.Record(Element.Wood);
                r.Check("언-1", a < 1f && b == 1f && d < 1f, "a chain water, fire, fire, wood: discount, full, discount (the previous cast moves with every accepted cast)");
                sequence.Clear();
                r.Check("언-1", sequence.CostScale(true, Element.Earth, discount) == 1f, "after a clear there is no previous cast again");
                float cost = .15f;
                r.Check("언-2", discount > 0f && r.Near(state.CostScale(true, Element.Fire, discount), 1f - discount) && r.Near(cost * state.CostScale(true, Element.Fire, discount), cost * (1f - discount)),
                    "the discounted cast spends cost x (1 - discount)", "scale " + state.CostScale(true, Element.Fire, discount).ToString(CultureInfo.InvariantCulture));
                r.Check("언-2", state.CostScale(true, Element.Fire, 5f) == 0f && state.CostScale(true, Element.Fire, -1f) == 1f, "a discount outside 0..1 can make a cast free at most, never negative and never dearer");
            }

            // ---- ink reserve: wider vessel, excess into a dwindling reserve ----
            {
                float capacity = reserve.F("reserve.capacity", 0f), decay = reserve.F("reserve.decay", 0f);
                var state = new InkReserveState308();
                r.Check("엄-1", state.Idle && state.Capacity == 1f, "before the buff the pool carries no temporary coefficient");
                state.Begin(capacity);
                r.Check("엄-1", capacity > 1f && state.Expanded && state.Capacity == capacity && state.Reserve == 0f, "while the buff runs the capacity coefficient is the row's value, above 1",
                    capacity.ToString(CultureInfo.InvariantCulture));
                bool noGift = true;
                foreach (float fill in new[] { 0f, .1f, .5f, .85f, 1f })
                    noGift &= r.Near(InkReserveState308.FillAfter(fill, 1f, capacity) * capacity, fill, 1e-5f);
                r.Check("엄-1", noGift, "widening keeps the amount of ink (the fill fraction drops): the cast pours nothing in");
                r.Check("엄-1", r.Near(InkReserveState308.FillAfter(1f, 1f, capacity), 1f / capacity) && 1f * capacity > 1f, "the widened pool holds more than the normal maximum when filled");

                // the moment the buff ends
                r.Check("엄-2", r.Near(InkReserveState308.Excess(1f, capacity), capacity - 1f, 1e-5f) && InkReserveState308.Excess(.5f, capacity) == 0f &&
                    r.Near(InkReserveState308.Excess(1f / capacity, capacity), 0f, 1e-5f), "excess = ink above the normal maximum: a full widened pool carries capacity - 1, a pool at or under the normal maximum none");
                state.Expire(1f);
                r.Check("엄-2", !state.Expanded && r.Near(state.Reserve, capacity - 1f, 1e-5f) && r.Near(InkReserveState308.FillAfter(1f, capacity, state.Capacity), 1f, 1e-5f),
                    "a full pool at the end: the whole excess moves into the reserve, nothing is lost at that moment", state.Reserve.ToString(CultureInfo.InvariantCulture));
                var half = new InkReserveState308(); half.Begin(capacity); half.Expire(.5f);
                r.Check("엄-2", half.Reserve == 0f && half.Capacity == 1f && half.Idle && r.Near(InkReserveState308.FillAfter(.5f, capacity, 1f), .5f * capacity, 1e-5f),
                    "a pool under the normal maximum at the end: no reserve, the coefficient is back to 1 and the ink amount is unchanged");
                var never = new InkReserveState308(); never.Expire(1f);
                r.Check("엄-2", never.Idle && never.Capacity == 1f, "an end without a running buff changes nothing");

                // the reserve dwindles
                float reserve0 = capacity - 1f, life = reserve0 / decay;
                foreach (float fps in frameRates)
                {
                    var s = new InkReserveState308(); s.Begin(capacity); s.Expire(1f);
                    float fill = 1f, coefficient = s.Capacity, previous = s.Reserve, atHalf = -1f; bool monotone = true;
                    int frames = Mathf.CeilToInt((life + 1f) * fps), halfFrame = Mathf.RoundToInt(life * .5f * fps);
                    for (int i = 1; i <= frames; i++)
                    {
                        s.Decay(fill, decay, 1f / fps);
                        fill = InkReserveState308.FillAfter(fill, coefficient, s.Capacity); coefficient = s.Capacity;
                        monotone &= s.Reserve <= previous && s.Reserve >= 0f; previous = s.Reserve;
                        if (i == halfFrame) atHalf = s.Reserve;
                    }
                    r.Check("엄-3", monotone && r.Near(atHalf, reserve0 * .5f, 2e-3f) && s.Reserve == 0f && s.Capacity == 1f && s.Idle && r.Near(fill, 1f),
                        "the reserve only shrinks, at the row's rate, down to the normal maximum (" + fps + " fps)",
                        "half-way " + atHalf.ToString(CultureInfo.InvariantCulture) + " of " + reserve0.ToString(CultureInfo.InvariantCulture) + ", life " + life.ToString(CultureInfo.InvariantCulture) + " s");
                }
                r.Check("엄-3", decay > 0f, "the reserve does dwindle (decay per second above 0)", decay.ToString(CultureInfo.InvariantCulture));
                var spend = new InkReserveState308(); spend.Begin(capacity); spend.Expire(1f);
                float spentFill = 1f - .1f / spend.Capacity;                               // 0.1 of the normal maximum spent out of the full reserve pool
                spend.Decay(spentFill, decay, 0f);
                r.Check("엄-3", r.Near(spend.Reserve, reserve0 - .1f, 1e-4f) && r.Near(InkReserveState308.FillAfter(spentFill, capacity, spend.Capacity), 1f, 1e-4f),
                    "ink spent during the reserve comes out of the reserve first (the normal pool stays full)", spend.Reserve.ToString(CultureInfo.InvariantCulture));
                float deepFill = InkReserveState308.FillAfter(spentFill, capacity, spend.Capacity) - .5f / spend.Capacity, before = spend.Capacity;
                spend.Decay(deepFill, decay, 0f);
                r.Check("엄-3", spend.Reserve == 0f && spend.Idle && r.Near(InkReserveState308.FillAfter(deepFill, before, 1f), 1f + (reserve0 - .1f) - .5f, 1e-4f),
                    "spending more than the reserve empties it and leaves the rest in the normal pool: nothing is created or lost");
                var again = new InkReserveState308(); again.Begin(capacity); again.Expire(1f); again.Decay(1f, decay, 1f); again.Begin(capacity);
                r.Check("엄-1", again.Expanded && again.Capacity == capacity && again.Reserve == 0f, "a recast during the reserve widens the pool again (the remaining reserve stays inside as ink)");
                again.Clear();
                r.Check("엄-3", again.Idle && again.Capacity == 1f, "a clear drops the coefficient back to 1 (nothing is saved)");
            }

            // ---- reflect and slowback: the melee attacker, and only the attacker ----
            {
                var actors = new[]
                {
                    new SpellActorSnap(0, new Vector3(0f, 0f, 2f), true, 4, 1f, false),
                    new SpellActorSnap(1, new Vector3(1f, 0f, 1f), true, 9, .5f, true),
                    new SpellActorSnap(2, new Vector3(3f, 0f, 0f), false, 2, 0f, false),
                };
                float power = reflect.F("reflect.power", 0f), now = 42.5f;
                var orders = new List<SpellHitOrder>();
                BuffRule308.Reflect(true, true, actors, 1, now, power, reflect.Element, orders);
                r.Check("먼-1", orders.Count == 1 && orders[0].TargetId == 1, "a melee hit while the buff runs: the answer names the attacker and nobody else");
                r.Check("먼-2", orders.Count == 1 && power > 0f && orders[0].Power.Equals(power) && orders[0].At.Equals(now) && orders[0].Element == reflect.Element,
                    "exactly one hit of the row's power, at the moment of the strike, in the buff's element", power.ToString(CultureInfo.InvariantCulture));
                var none = new List<SpellHitOrder>();
                BuffRule308.Reflect(true, false, actors, 1, now, power, reflect.Element, none);     // ranged, unspecified
                BuffRule308.Reflect(false, true, actors, 1, now, power, reflect.Element, none);     // buff not running
                BuffRule308.Reflect(true, true, actors, -1, now, power, reflect.Element, none);     // no attacker (environment)
                BuffRule308.Reflect(true, true, actors, 2, now, power, reflect.Element, none);      // the attacker is already dead
                BuffRule308.Reflect(true, true, actors, 7, now, power, reflect.Element, none);      // not one of this fight's enemies
                BuffRule308.Reflect(true, true, actors, 1, now, 0f, reflect.Element, none);         // no power in the row
                r.Check("먼-1", none.Count == 0, "no answer for a hit that is not melee, without the buff, without a living attacker of this fight");
                var twice = new List<SpellHitOrder>();
                BuffRule308.Reflect(true, true, actors, 0, now, power, reflect.Element, twice);
                BuffRule308.Reflect(true, true, actors, 0, now + 1f, power, reflect.Element, twice);
                r.Check("먼-2", twice.Count == 2 && twice.All(o => o.TargetId == 0 && o.Power.Equals(power)), "each strike is answered once (two strikes, two answers); the answer holds enemy damage only, nothing for the player");

                float speed = slowback.F("slow.speed", 0f), seconds = slowback.F("slow.duration", 0f);
                bool slowed = BuffRule308.Slowback(true, true, actors, 1, now, seconds, speed, out float until, out float scale);
                r.Check("멈-1", slowed, "a melee hit while the buff runs slows the attacker (a boss too: movement only, no action block)");
                bool refused = !BuffRule308.Slowback(true, false, actors, 1, now, seconds, speed, out _, out float s1) && s1 == 1f &&
                    !BuffRule308.Slowback(false, true, actors, 1, now, seconds, speed, out _, out _) &&
                    !BuffRule308.Slowback(true, true, actors, -1, now, seconds, speed, out _, out _) &&
                    !BuffRule308.Slowback(true, true, actors, 2, now, seconds, speed, out _, out _) &&
                    !BuffRule308.Slowback(true, true, actors, 7, now, seconds, speed, out _, out _);
                r.Check("멈-1", refused, "no slow for a hit that is not melee, without the buff, without a living attacker of this fight");
                r.Check("멈-2", slowed && scale == speed && scale < 1f && scale >= 0f && until.Equals(now + seconds) && seconds > 0f,
                    "movement scale = the row's value, below 1, until the strike time + the row's seconds", scale.ToString(CultureInfo.InvariantCulture) + " for " + seconds.ToString(CultureInfo.InvariantCulture) + " s");
                r.Check("멈-2", BuffRule308.SlowAt(now, until, scale) == scale && BuffRule308.SlowAt(until - .01f, until, scale) == scale &&
                    BuffRule308.SlowAt(until, until, scale) == 1f && BuffRule308.SlowAt(until + 5f, until, scale) == 1f, "the slow ends by itself: full speed again from that time on");
                BuffRule308.Slowback(true, true, actors, 1, now + 1f, seconds, speed, out float refreshed, out _);
                r.Check("멈-2", refreshed.Equals(now + 1f + seconds), "a second strike refreshes the same slow (a new end time, no stacking of the scale)");
            }

            // ---- insight: state and signal only (what it makes readable is presentation, Play) ----
            {
                float duration = insight.F("duration", 0f); char buff = insight.Letter[0];
                var buffs = new SpellBuffState308(); buffs.Activate(buff, 3f, duration);
                r.Check("W08", insight.Params.Length == 1 && buffs.Active(buff, 3f + duration * .5f) && !buffs.Active(buff, 3f + duration) && buffs.Charges(buff, 4f) == 0,
                    "the insight row carries a duration and nothing else: its rule is the buff state alone (legibility itself is a Play clause)");
            }
        }
    }
}
