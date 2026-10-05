// SPEC-SPELL-120-308 L2 cases of WP-07 (enemy modifiers, user answer Q4 = D308-13): the new defence stat and the intake of
// EnemyVitals (untouched enemy = the intake before WP-07, bit for bit), damage-taken brand, defence shred, defence-ignoring
// hit, dulled next attack, weakened attack, and the ledger that puts a modifier only on enemies a hit really landed on.
// What this cannot see: EnemyVitals.TakeDamage itself and the handlers' Unity half (L3 / Play).
// 2026-10-04: the enemy -> player doorway runs on a pure rule (EnemyStrikeRule308), so what the player takes from a dulled
// or weakened enemy, and from an enemy nobody touched, is checked here on the lines the doorway itself runs. The call of
// PlayerVitals.TakeAttackDamage with that amount stays L3.
using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    public static partial class Program
    {
        sealed class WP07Gate : ISpellGate
        {
            public readonly HashSet<SpellFinal> Unlocked = new HashSet<SpellFinal>();
            public SpellResolveStatus Judge(SpellRow row) => SpellUnlockPolicy308.JudgeNew(row, true, Unlocked.Contains(row.Final));
        }

        static int WP07Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        static partial void RunWP07(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W07", false, "WP-07 cases need the table"); return; }

            // grid cells by index (no glyph is named): 6 wood single + ieung, 30 fire single + ieung -> mod.pierce /
            // 53 earth single + siot, 41 fire cone + siot -> mod.armorbreak / 75 metal single + nieun, 63 earth path + nieun -> mod.brand /
            // 65 earth path + siot -> mod.dull / 113 water path + siot -> mod.weaken
            int[] cells = { 6, 30, 53, 41, 75, 63, 65, 113 };
            string[] handlers = { "mod.pierce", "mod.pierce", "mod.armorbreak", "mod.armorbreak", "mod.brand", "mod.brand", "mod.dull", "mod.weaken" };
            SpellRow pierceWood = c.RowAt(6), pierceFire = c.RowAt(30), breakOne = c.RowAt(53), breakCone = c.RowAt(41);
            SpellRow brandOne = c.RowAt(75), brandPath = c.RowAt(63), dullPath = c.RowAt(65), weakenPath = c.RowAt(113);

            // ---- the rows ----
            var wrong = new List<string>();
            var gate = new WP07Gate();
            for (int i = 0; i < cells.Length; i++)
            {
                var row = c.RowAt(cells[i]);
                var parent = c.Row(SpellGrammar308.BaseOf(row.Letter[0]));
                bool ok = row.Handler == handlers[i] && row.Feature == SpellLegacyFeature.None && string.IsNullOrEmpty(row.Pending) &&
                    row.Gate == SpellGateMode.Final && row.Final != SpellFinal.None && row.Category == SpellCategory.Attack && parent != null &&
                    row.Kind == parent.Kind && row.AreaShape == parent.AreaShape && row.BasePower == parent.BasePower && row.BasePower > 0f &&
                    row.ProjectileSpeedMul == parent.ProjectileSpeedMul && row.AreaRadius == parent.AreaRadius && row.AreaLength == parent.AreaLength &&
                    row.AreaSpeed == parent.AreaSpeed && row.AreaImpactDelay == parent.AreaImpactDelay && row.Element == parent.Element;
                var locked = SpellResolveCore308.Resolve(row.Letter[0], .5f, .5f, row, gate, out _);
                gate.Unlocked.Add(row.Final);
                var open = SpellResolveCore308.Resolve(row.Letter[0], .5f, .5f, row, gate, out SpellCast cast);
                gate.Unlocked.Clear();
                ok &= locked == SpellResolveStatus.Locked && open == SpellResolveStatus.Ok && cast.Power == row.BasePower * .5f && cast.Kind == row.Kind &&
                    cast.AreaShape == row.AreaShape && cast.Element == row.Element;
                if (!ok) wrong.Add(cells[i] + ":" + row.Handler + ":" + locked + "/" + open);
            }
            r.Check("W07", wrong.Count == 0, "the eight rows carry their mod.* handler and no legacy feature, inherit kind / shape / power / speed from the glyph " +
                "without a final, stay Locked behind their final consonant and cast with base power x brush once it is open", string.Join(" ", wrong));
            r.Check("W07", brandOne.Has("brand.scale") && brandOne.Has("brand.duration") && brandPath.Has("brand.scale") && brandPath.Has("brand.duration") &&
                breakOne.Has("break.defence") && breakOne.Has("break.duration") && breakCone.Has("break.defence") && breakCone.Has("break.duration") &&
                pierceWood.Has("pierce.defence") && pierceFire.Has("pierce.defence") && dullPath.Has("dull.scale") && dullPath.Has("dull.duration") &&
                weakenPath.Has("weaken.scale") && weakenPath.Has("weaken.duration"), "every number the handlers read is in the row (rule sheet), none in code");

            // ---- an untouched enemy: the intake before WP-07, bit for bit ----
            // before: damage = amount x weak-point multiplier, hp = max(min(floor, hp), hp - damage - bonus)
            // now:    damage = Modifiers.Intake(amount x multiplier, Defence, attack id, now), the rest unchanged
            {
                int cases = 0, different = 0;
                var random = new System.Random(308);
                float[] multipliers = { 1f, 1.5f, 2f, .75f };
                var amounts = new List<float> { float.Epsilon, 1e-20f, .1f, 1f / 3f, 1f, 7.7f, 22f, 480f, 1e9f, float.MaxValue, float.PositiveInfinity };
                for (int i = 0; i < 5000; i++) amounts.Add((float)(random.NextDouble() * Math.Pow(10, random.Next(-3, 4))));
                var states = new List<KeyValuePair<string, EnemyModifierState308>>();
                states.Add(new KeyValuePair<string, EnemyModifierState308>("never touched", new EnemyModifierState308()));
                var cleared = new EnemyModifierState308(); cleared.Brand(99f, 2f); cleared.ShredDefence(99f, .5f); cleared.IgnoreDefence(5, 1f); cleared.Clear();
                states.Add(new KeyValuePair<string, EnemyModifierState308>("cleared (new life)", cleared));
                var expired = new EnemyModifierState308(); expired.Brand(1f, 2f); expired.ShredDefence(1f, .5f); expired.Weaken(1f, .5f); expired.Dull(1f, .5f);
                states.Add(new KeyValuePair<string, EnemyModifierState308>("everything expired", expired));
                var unrelated = new EnemyModifierState308(); unrelated.Weaken(1e9f, .5f); unrelated.Dull(1e9f, .5f); unrelated.IgnoreDefence(999999, 1f); unrelated.ShredDefence(1e9f, .5f);
                states.Add(new KeyValuePair<string, EnemyModifierState308>("only entries that do not act on this hit (outgoing, another hit's pierce, shred without armour)", unrelated));
                var perState = new List<string>();
                foreach (var state in states)
                {
                    int before = different;
                    foreach (float amount in amounts)
                        foreach (float multiplier in multipliers)
                            foreach (float floor in new[] { 0f, 1f })
                            {
                                float hp = (float)(random.NextDouble() * 520.0) + .5f, bonus = random.Next(4) == 0 ? 30f : 0f;
                                long id = random.Next(3) == 0 ? 0 : random.Next(1, 100000);
                                float oldDamage = amount * multiplier;
                                float newDamage = state.Value.Intake(amount * multiplier, 0f, id, 5f);
                                float oldHp = Mathf.Max(Mathf.Min(floor, hp), hp - oldDamage - bonus), newHp = Mathf.Max(Mathf.Min(floor, hp), hp - newDamage - bonus);
                                cases++;
                                if (WP07Bits(oldDamage) != WP07Bits(newDamage) || WP07Bits(oldHp) != WP07Bits(newHp)) different++;
                            }
                    perState.Add(state.Key + ": " + (different - before));
                }
                r.Fact("wp07IntakeEquivalenceCases", cases); r.Fact("wp07IntakeEquivalenceMismatches", different);
                r.Check("W07", different == 0 && cases > 100000, "an enemy with defence 0 takes exactly the damage and keeps exactly the hp it did before WP-07 (" + cases +
                    " cases, bit comparison)", string.Join(" | ", perState));
                r.Check("W07", float.IsNaN(new EnemyModifierState308().Intake(float.NaN, 0f, 1, 0f)) && new EnemyModifierState308().Intake(-3f, 0f, 1, 0f) == -3f,
                    "the intake does not launder a bad input: the vitals' own guards (NaN, not positive) still see it");
                r.Check("W07", EnemyIntakeRule308.Defence(0f, 0f, 0f) == 0f && EnemyIntakeRule308.Defence(-1f, 0f, 0f) == 0f && EnemyIntakeRule308.Defence(float.NaN, 0f, 0f) == 0f &&
                    EnemyIntakeRule308.Defence(7f, 0f, 0f) == 1f && EnemyIntakeRule308.Defence(.4f, float.NaN, float.NaN) == .4f,
                    "defence is read as a share 0..1: no, negative or broken data is no defence, above 1 is 1, a broken modifier changes nothing");
                r.Check("W07", new EnemyModifierState308().Intake(10f, 1f, 1, 0f) == 0f, "a full defence leaves 0 (the vitals then refuse the hit); only a hit that ignores defence gets through");
            }

            const float Armour = .4f;   // TEST target: an armoured enemy. Enemy data; no enemy of the project has defence yet (default 0)

            // ---- mod.pierce: the hit ignores defence ----
            foreach (var row in new[] { pierceWood, pierceFire })
            {
                string clause = row.Letter + "-1";
                float share = row.F("pierce.defence", 0f);
                var state = new EnemyModifierState308();
                float plain = state.Intake(12f, Armour, 41, 0f);
                bool told = state.IgnoreDefence(42, share);
                float otherHit = state.Intake(12f, Armour, 43, 0f);        // another spell's hit while the pierce entry waits
                float pierced = state.Intake(12f, Armour, 42, 0f);
                float again = state.Intake(12f, Armour, 42, 0f);           // the entry was used up by its hit
                r.Check(clause, share == 1f && told && r.Near(plain, 12f * (1f - Armour)) && pierced == 12f && r.Near(otherHit, plain) && r.Near(again, plain) && state.IsEmpty,
                    "armoured target: a plain hit loses the defence share, the hit of this cast lands whole, exactly once and only for its own attack id",
                    "plain " + plain + " pierced " + pierced + " other " + otherHit + " again " + again);
                var bare = new EnemyModifierState308(); bare.IgnoreDefence(42, share);
                r.Check(clause, WP07Bits(bare.Intake(12f, 0f, 42, 0f)) == WP07Bits(12f), "target without defence: the same hit as the glyph without a final (nothing to ignore)");
                var full = new EnemyModifierState308(); full.IgnoreDefence(42, share);
                r.Check(clause, full.Intake(12f, 1f, 42, 0f) == 12f && full.Intake(12f, 1f, 42, 0f) == 0f, "even a full defence is ignored by that hit (and by no other)");
                var marked = new EnemyModifierState308(); marked.Brand(9f, 1.5f); marked.ShredDefence(9f, .1f); marked.IgnoreDefence(42, share);
                r.Check(clause, r.Near(marked.Intake(12f, Armour, 42, 0f), 18f) && r.Near(marked.Intake(12f, Armour, 44, 0f), 12f * (1f - (Armour - .1f)) * 1.5f),
                    "ignoring defence is not ignoring a brand: damage-taken increase still applies to the piercing hit");
                var dropped = new EnemyModifierState308(); dropped.IgnoreDefence(42, share); dropped.ForgetIgnore(42);
                r.Check(clause, dropped.IsEmpty && r.Near(dropped.Intake(12f, Armour, 42, 0f), plain), "an entry taken back (its hit never landed) leaves nothing on the enemy");
            }
            {
                var half = new EnemyModifierState308(); half.IgnoreDefence(1, .5f);
                r.Check("W07", r.Near(half.Intake(10f, Armour, 1, 0f), 10f * (1f - Armour * .5f)), "pierce.defence is a share: 0.5 ignores half of the defence");
                r.Check("W07", !half.IgnoreDefence(0, 1f) && !half.IgnoreDefence(5, 0f) && !half.IgnoreDefence(5, float.NaN) && half.IsEmpty, "no attack id or no share: nothing is registered");
                var many = new EnemyModifierState308();
                for (long id = 1; id <= 100; id++) many.IgnoreDefence(id, 1f);
                r.Check("W07", many.IgnoredShare(1) == 0f && many.IgnoredShare(36) == 0f && many.IgnoredShare(37) == 1f && many.IgnoredShare(100) == 1f,
                    "remembered hit ids are bounded: the oldest are dropped (a bound, not a balance number)");
                r.Check("W07", pierceWood.Handler == pierceFire.Handler && pierceWood.F("pierce.defence", 0f) == pierceFire.F("pierce.defence", -1f) && pierceWood.Element != pierceFire.Element,
                    "the two pierce glyphs do the same thing in two elements (canon: same act, different body)");
            }

            // ---- mod.armorbreak: defence lowered for a while ----
            foreach (var row in new[] { breakOne, breakCone })
            {
                string clause = row.Letter + "-2";
                float amount = row.F("break.defence", 0f), seconds = row.F("break.duration", 0f);
                var state = new EnemyModifierState308();
                float plain = state.Intake(10f, Armour, 1, 5f);
                bool put = state.ShredDefence(5f + seconds, amount);
                float left = Mathf.Max(0f, Armour - amount);
                r.Check(clause, amount > 0f && seconds > 0f && put && r.Near(plain, 10f * (1f - Armour)) && r.Near(state.DefenceShred(5f), amount) &&
                    r.Near(state.DefenceNow(Armour, 5f), left) && r.Near(state.Intake(10f, Armour, 2, 5f), 10f * (1f - left)) && state.Intake(10f, Armour, 2, 5f) > plain,
                    "armoured target: after the break the next hit loses less to defence (defence - break.defence)", "plain " + plain + " after " + state.Intake(10f, Armour, 2, 5f));
                r.Check(clause, r.Near(state.DefenceNow(Armour, 5f + seconds - .01f), left) && state.DefenceShred(5f + seconds) == 0f && r.Near(state.Intake(10f, Armour, 3, 5f + seconds), plain) && state.IsEmpty,
                    "the break ends at its duration: the defence is back");
                var bare = new EnemyModifierState308(); bare.ShredDefence(99f, amount);
                r.Check(clause, WP07Bits(bare.Intake(10f, 0f, 1, 5f)) == WP07Bits(10f) && bare.DefenceNow(0f, 5f) == 0f && bare.DefenceNow(amount * .5f, 5f) == 0f,
                    "defence never goes below 0: on a target without armour the break adds no damage (a counter to armour, not a second brand)");
            }
            {
                var state = new EnemyModifierState308();
                bool first = state.ShredDefence(20f, .2f), stronger = state.ShredDefence(14f, .3f), weaker = state.ShredDefence(12f, .1f);
                r.Check("W07", first && stronger && !weaker && r.Near(state.DefenceShred(10f), .3f) && r.Near(state.DefenceShred(15f), .2f) && state.DefenceShred(20f) == 0f,
                    "breaks do not stack: the strongest running one counts, a weaker and shorter one is refused, a longer one takes over when the stronger ends");
                r.Check("W07", !state.ShredDefence(30f, 0f) && !state.ShredDefence(30f, -1f) && !state.ShredDefence(float.NaN, .2f) && !state.ShredDefence(30f, float.NaN),
                    "a break that would change nothing (0, negative, broken) is refused");
                r.Check("W07", breakOne.F("break.defence", 0f) >= breakCone.F("break.defence", 0f) && breakOne.Kind == SpellKind.AttackSingle && breakCone.AreaShape == AreaShape.Cone,
                    "the single break is the heavy one, the cone its area partner (row data)");
            }

            // ---- mod.brand: damage taken raised for a while ----
            foreach (var row in new[] { brandOne, brandPath })
            {
                string clause = row.Letter + "-2";
                float scale = row.F("brand.scale", 0f), seconds = row.F("brand.duration", 0f);
                var state = new EnemyModifierState308();
                float marking = state.Intake(20f, 0f, 1, 10f);             // the marking hit itself lands before the brand exists
                bool put = state.Brand(10f + seconds, scale);
                r.Check(clause, scale > 1f && seconds > 0f && put && marking == 20f && state.DamageTakenScale(10f) == scale && r.Near(state.Intake(20f, 0f, 2, 10f), 20f * scale) &&
                    r.Near(state.Intake(20f * 1.5f, 0f, 3, 10f), 20f * 1.5f * scale),
                    "a branded enemy takes brand.scale more from every later hit (also on top of the weak-point multiplier); the marking hit itself is not raised");
                r.Check(clause, state.DamageTakenScale(10f + seconds - .01f) == scale && state.DamageTakenScale(10f + seconds) == 1f &&
                    WP07Bits(state.Intake(20f, 0f, 4, 10f + seconds)) == WP07Bits(20f) && state.IsEmpty, "the brand ends at its duration: the next hit is the plain hit again");
                r.Check(clause, r.Near(state.Intake(20f, Armour, 5, 99f), 20f * (1f - Armour)) && state.Brand(200f, scale) && r.Near(state.Intake(20f, Armour, 6, 99f), 20f * (1f - Armour) * scale),
                    "brand and defence are separate factors: an armoured, branded enemy takes (1 - defence) x brand.scale");
            }
            {
                r.Check("산-2", brandOne.F("brand.duration", 0f) < brandPath.F("brand.duration", 0f), "short: the single mark runs shorter than the area mark (row data)",
                    brandOne.F("brand.duration", 0f) + " < " + brandPath.F("brand.duration", 0f));
                float fastest = c.Build.Rows.Where(x => x.HasHandler && x.Kind == SpellKind.AttackSingle).Max(x => x.ProjectileSpeedMul);
                r.Check("산-2", brandOne.ProjectileSpeedMul == fastest && brandOne.Kind == SpellKind.AttackSingle, "fastest mark: it flies with the fastest projectile of the single-target rows",
                    brandOne.ProjectileSpeedMul + " of " + fastest);
                var state = new EnemyModifierState308();
                bool wide = state.Brand(20f, 1.2f), sharp = state.Brand(14f, 1.3f), same = state.Brand(14f, 1.3f), longer = state.Brand(30f, 1.2f);
                r.Check("W07", wide && sharp && !same && longer && state.DamageTakenScale(12f) == 1.3f && state.DamageTakenScale(15f) == 1.2f && state.DamageTakenScale(25f) == 1.2f && state.DamageTakenScale(30f) == 1f,
                    "brands do not stack: the strongest running one counts, casting again refreshes the time");
                r.Check("W07", !state.Brand(50f, 1f) && !state.Brand(50f, .5f) && !state.Brand(50f, float.NaN) && !state.Brand(float.PositiveInfinity, 2f),
                    "a brand can only raise damage taken: 1, below 1 and broken values are refused (it can never work as hidden armour)");
            }

            // ---- mod.dull: the next attack, once ----
            {
                float scale = dullPath.F("dull.scale", 1f), seconds = dullPath.F("dull.duration", 0f);
                var state = new EnemyModifierState308();
                bool put = state.Dull(3f + seconds, scale);
                float peek = state.OutgoingScale(3f); bool dulled = state.Dulled(3f);
                float first = state.TakeStrike(10f, 3f), second = state.TakeStrike(10f, 3f);
                r.Check("못-2", scale >= 0f && scale < 1f && seconds > 0f && put && dulled && peek == scale && r.Near(first, 10f * scale) && WP07Bits(second) == WP07Bits(10f) && !state.Dulled(3f) && state.IsEmpty,
                    "the enemy's next strike is scaled by dull.scale, the one after it is whole again: one charge", "first " + first + " second " + second);
                var twice = new EnemyModifierState308(); twice.Dull(3f + seconds, scale); twice.Dull(5f + seconds, scale);
                r.Check("못-2", r.Near(twice.TakeStrike(10f, 6f), 10f * scale) && twice.TakeStrike(10f, 6f) == 10f, "casting again before the enemy attacks refreshes the one charge, it never adds a second");
                var late = new EnemyModifierState308(); late.Dull(3f + seconds, scale);
                r.Check("못-2", late.Dulled(3f + seconds - .01f) && late.TakeStrike(10f, 3f + seconds) == 10f && late.IsEmpty, "an unused charge runs out at dull.duration");
                var idle = new EnemyModifierState308(); idle.Dull(99f, scale);
                r.Check("못-2", idle.TakeStrike(0f, 3f) == 0f && idle.Dulled(3f) && WP07Bits(idle.Intake(10f, 0f, 1, 3f)) == WP07Bits(10f) && idle.Dulled(3f),
                    "only a real strike uses the charge; the damage the enemy takes is not touched by it");
            }

            // ---- mod.weaken: every attack for a while ----
            {
                float scale = weakenPath.F("weaken.scale", 1f), seconds = weakenPath.F("weaken.duration", 0f);
                var state = new EnemyModifierState308();
                bool put = state.Weaken(3f + seconds, scale);
                float a = state.TakeStrike(10f, 3f), b = state.TakeStrike(10f, 3f + seconds * .5f), d = state.TakeStrike(10f, 3f + seconds - .01f), after = state.TakeStrike(10f, 3f + seconds);
                r.Check("옷-2", scale >= 0f && scale < 1f && seconds > 0f && put && r.Near(a, 10f * scale) && r.Near(b, 10f * scale) && r.Near(d, 10f * scale) && WP07Bits(after) == WP07Bits(10f) && state.IsEmpty,
                    "every strike inside the window is scaled by weaken.scale (nothing is used up); after weaken.duration the strike is whole again", a + " " + b + " " + d + " " + after);
                var both = new EnemyModifierState308(); both.Weaken(99f, scale); both.Dull(99f, .5f);
                r.Check("옷-2", r.Near(both.TakeStrike(10f, 3f), 10f * scale * .5f) && r.Near(both.TakeStrike(10f, 3f), 10f * scale), "weaken and a dull charge multiply on the first strike; the weaken stays");
                var two = new EnemyModifierState308(); two.Weaken(20f, .8f); two.Weaken(14f, .6f);
                r.Check("W07", two.OutgoingScale(10f) == .6f && two.OutgoingScale(15f) == .8f && two.OutgoingScale(20f) == 1f, "weakens do not stack: the strongest running one counts");
                r.Check("W07", !two.Weaken(50f, 1f) && !two.Weaken(50f, 1.5f) && !two.Weaken(50f, -1f) && !two.Dull(50f, 1f) && !two.Dull(50f, 2f) && !two.Dull(50f, float.NaN),
                    "an outgoing modifier can only lower an enemy's strike: 1, above 1, negative and broken values are refused");
                var random = new System.Random(7); bool neverMore = true;
                for (int i = 0; i < 2000; i++)
                {
                    var any = new EnemyModifierState308();
                    any.Weaken((float)random.NextDouble() * 10f, (float)random.NextDouble() * 1.5f); any.Dull((float)random.NextDouble() * 10f, (float)random.NextDouble() * 1.5f);
                    any.Brand(9f, 1f + (float)random.NextDouble()); any.ShredDefence(9f, (float)random.NextDouble());
                    float amount = (float)random.NextDouble() * 50f + .01f, now = (float)random.NextDouble() * 10f;
                    float strike = any.TakeStrike(amount, now);
                    neverMore &= strike <= amount && strike >= 0f;
                }
                r.Check("A10", neverMore, "2000 random states: an enemy's strike never grows and never turns negative (the modifiers give the player nothing but a lighter hit)");
            }

            // ---- who gets the modifier: only enemies a hit of the cast really landed on ----
            // The seven actors of the L3 fixture: three ahead, one behind, one far, one far to the side, one boss ahead to the right.
            var positions = new[]
            {
                new Vector3(0f, 0f, 3f), new Vector3(.8f, 0f, 4.5f), new Vector3(-.8f, 0f, 6f),
                new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 60f), new Vector3(40f, 0f, 2f), new Vector3(2.5f, 0f, 6.5f),
            };
            EnemyModifierState308[] Fresh() => positions.Select(_ => new EnemyModifierState308()).ToArray();
            // what ModifierEffect308 does around the ledger: remember each scheduled hit at cast, apply when that id is confirmed
            void Cast(ModifierHitLedger308 ledger, IEnumerable<int> swept, long firstId, float impactAt, float value, float duration)
            { foreach (int id in swept) ledger.Add(firstId + id, id, 1, impactAt, value, duration); }
            bool Land(ModifierHitLedger308 ledger, long attackId, float now, EnemyModifierState308[] states, Action<EnemyModifierState308, float, float> put)
            {
                if (!ledger.Take(attackId, out var entry)) return false;
                put(states[entry.TargetId], now + entry.Duration, entry.Value);
                return true;
            }
            string Marked(EnemyModifierState308[] states) => string.Join("", states.Select(s => s.IsEmpty ? "0" : "1"));

            // single target (the aimed enemy = actor 1)
            foreach (var pair in new[] { new KeyValuePair<string, SpellRow>(breakOne.Letter + "-1", breakOne), new KeyValuePair<string, SpellRow>(brandOne.Letter + "-1", brandOne) })
            {
                var row = pair.Value; bool brand = row.Handler == "mod.brand";
                float value = brand ? row.F("brand.scale", 0f) : row.F("break.defence", 0f), seconds = brand ? row.F("brand.duration", 0f) : row.F("break.duration", 0f);
                Action<EnemyModifierState308, float, float> put = (s, until, v) => { if (brand) s.Brand(until, v); else s.ShredDefence(until, v); };
                var states = Fresh(); var ledger = new ModifierHitLedger308();
                Cast(ledger, new[] { 1 }, 500, 2f, value, seconds);
                bool nothingAtCast = Marked(states) == "0000000" && ledger.Count == 1;
                bool stranger = Land(ledger, 9999, 2f, states, put);            // another spell's confirmed hit
                bool landed = Land(ledger, 501, 2f, states, put), twice = Land(ledger, 501, 2f, states, put);
                r.Check(pair.Key, row.Kind == SpellKind.AttackSingle && nothingAtCast && !stranger && landed && !twice && Marked(states) == "0100000" && ledger.Count == 0 &&
                    (brand ? states[1].DamageTakenScale(2f) == value : r.Near(states[1].DefenceShred(2f), value)),
                    "single shape: nothing at cast time, the modifier goes on the one enemy the hit landed on, once, and on nobody else", Marked(states));
                var air = Fresh(); var empty = new ModifierHitLedger308();
                Cast(empty, new int[0], 500, 2f, value, seconds);
                r.Check(pair.Key, empty.Count == 0 && !Land(empty, 500, 2f, air, put) && Marked(air) == "0000000", "an air cast (no scheduled hit) marks nobody");
            }

            // area shapes: everyone the shape swept, nobody outside it
            var origin = Vector3.zero; var forward = Vector3.forward;
            var areas = new[]
            {
                new KeyValuePair<string, SpellRow>(breakCone.Letter + "-1", breakCone), new KeyValuePair<string, SpellRow>(brandPath.Letter + "-1", brandPath),
                new KeyValuePair<string, SpellRow>(dullPath.Letter + "-1", dullPath), new KeyValuePair<string, SpellRow>(weakenPath.Letter + "-1", weakenPath),
            };
            foreach (var pair in areas)
            {
                var row = pair.Value;
                var swept = new List<int>();
                for (int i = 0; i < positions.Length; i++)
                {
                    // the corridor is the row's own geometry (inherited); the cone's angle and range are CombatConfig's, any cone shows the rule
                    bool inside = row.AreaShape == AreaShape.Path
                        ? AreaGeometry.InCorridor(origin, forward, positions[i], row.AreaRadius, row.AreaLength, out _, out _)
                        : AreaGeometry.InCone(origin, forward, positions[i], 30f, 8f, out _, out _);
                    if (inside) swept.Add(i);
                }
                string expected = string.Join("", Enumerable.Range(0, positions.Length).Select(i => swept.Contains(i) ? "1" : "0"));
                float value, seconds; Action<EnemyModifierState308, float, float> put;
                switch (row.Handler)
                {
                    case "mod.brand": value = row.F("brand.scale", 0f); seconds = row.F("brand.duration", 0f); put = (s, until, v) => s.Brand(until, v); break;
                    case "mod.armorbreak": value = row.F("break.defence", 0f); seconds = row.F("break.duration", 0f); put = (s, until, v) => s.ShredDefence(until, v); break;
                    case "mod.dull": value = row.F("dull.scale", 1f); seconds = row.F("dull.duration", 0f); put = (s, until, v) => s.Dull(until, v); break;
                    default: value = row.F("weaken.scale", 1f); seconds = row.F("weaken.duration", 0f); put = (s, until, v) => s.Weaken(until, v); break;
                }
                var states = Fresh(); var ledger = new ModifierHitLedger308();
                Cast(ledger, swept, 700, 2f, value, seconds);
                bool nothingAtCast = Marked(states) == "0000000";
                foreach (int id in swept) Land(ledger, 700 + id, 2f, states, put);
                r.Check(pair.Key, row.Kind == SpellKind.AttackArea && (row.AreaShape == AreaShape.Path || row.AreaShape == AreaShape.Cone) && expected == "1110001" && nothingAtCast &&
                    Marked(states) == expected && ledger.Count == 0, "area shape: every enemy the shape swept carries the modifier after its hit landed, nobody outside the shape does",
                    row.AreaShape + " swept " + expected + " marked " + Marked(states));
                // one swept enemy dies before the front reaches it: its hit is never confirmed
                var partial = Fresh(); var second = new ModifierHitLedger308();
                Cast(second, swept, 800, 2f, value, seconds);
                foreach (int id in swept.Skip(1)) Land(second, 800 + id, 2f, partial, put);
                var gone = new List<ModifierHitLedger308.Entry>();
                second.Sweep(2f); second.Sweep(2.02f, gone);
                r.Check(pair.Key, partial[swept[0]].IsEmpty && Marked(partial).Count(ch => ch == '1') == swept.Count - 1 && second.Count == 0 && gone.Count == 1 && gone[0].TargetId == swept[0],
                    "a swept enemy whose hit never landed (died, lost) gets nothing and its entry is dropped");
            }

            // ---- the ledger against the wiring's frame order (effects tick, then that frame's hits land) ----
            {
                var ledger = new ModifierHitLedger308();
                ledger.Add(7, 0, 1, 2f, 1.3f, 4f);
                ledger.Sweep(1.9f); bool early = ledger.Count == 1;
                ledger.Sweep(2f); bool due = ledger.Count == 1;                 // due this frame: the hit lands right after this sweep
                bool taken = ledger.Take(7, out var entry) && entry.Value == 1.3f && entry.Duration == 4f && entry.TargetId == 0 && entry.Life == 1;
                r.Check("W07", early && due && taken && ledger.Count == 0, "a hit landing in the frame its impact time is reached still finds its entry (the sweep of that frame keeps it)");
                bool allClocks = true; var clockDetail = new List<string>();
                foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 120f })
                {
                    var perClock = new ModifierHitLedger308(); perClock.Add(1, 0, 1, .4321f, 1f, 1f); perClock.Add(2, 1, 1, .4321f, 1f, 1f);
                    bool landed = false, lost = true; float now = 0f; int frames = 0;
                    while (now < 1f)
                    {
                        now += dt; frames++;
                        var gone = new List<ModifierHitLedger308.Entry>();
                        perClock.Sweep(now, gone);                                   // TickSpells308
                        foreach (var e in gone) if (e.AttackId == 1) lost = false;   // id 1 must never be dropped before it lands
                        if (!landed && now >= .4321f) landed = perClock.Take(1, out _); // TickPendingCasts: id 1 lands, id 2 never does
                    }
                    bool ok = landed && lost && perClock.Count == 0;
                    allClocks &= ok; clockDetail.Add((1f / dt).ToString("0") + "fps " + (ok ? "ok" : "FAIL"));
                }
                r.Check("W07", allClocks, "30 / 60 / 120 fps clocks: the landing hit is always taken, the hit that never lands is dropped one frame later", string.Join(" ", clockDetail));
                var none = new ModifierHitLedger308(); none.Add(0, 0, 1, 1f, 1f, 1f); none.Add(-4, 0, 1, 1f, 1f, 1f);
                r.Check("W07", none.Count == 0, "a visual-only shot (attack id 0) is never remembered");
                var wiped = new ModifierHitLedger308(); wiped.Add(3, 0, 1, 1f, 1f, 1f); wiped.Clear();
                r.Check("W07", wiped.Count == 0 && !wiped.Take(3, out _), "Clear (death, rest, scene leave, disable) forgets the hits on their way");
            }

            // ---- the enemy -> player doorway (EnemyStrike308.Deliver = EnemyStrikeRule308.Resolve, then PlayerVitals.TakeAttackDamage) ----
            {
                float dullScale = dullPath.F("dull.scale", 1f), dullSeconds = dullPath.F("dull.duration", 0f);
                float weakScale = weakenPath.F("weaken.scale", 1f), weakSeconds = weakenPath.F("weaken.duration", 0f);
                // an enemy nobody touched, and a strike without an attacker: every amount arrives bit for bit as it left
                float[] amounts = { .01f, 1f, 3.5f, 7f, 12f, 18.75f, 40f, 250f, 0f, -3f, float.NaN };
                int same = 0, doorCases = 0;
                var untouched = new EnemyModifierState308();
                foreach (float amount in amounts)
                    foreach (bool ranged in new[] { false, true })
                        foreach (float now in new[] { 0f, 4.2f, 9999f })
                        {
                            doorCases += 2;
                            if (EnemyStrikeRule308.Resolve(untouched, amount, ranged, now, out float got) && WP07Bits(got) == WP07Bits(amount)) same++;
                            if (EnemyStrikeRule308.Resolve(null, amount, ranged, now, out float bare) && WP07Bits(bare) == WP07Bits(amount)) same++;
                        }
                r.Fact("wp07DoorwayCases", doorCases);
                r.Check("W07", same == doorCases && untouched.IsEmpty,
                    "the doorway delivers every strike of an enemy without a modifier (and a strike without an attacker) bit for bit as it came in: melee and ranged, " + doorCases + " cases",
                    same + " of " + doorCases);

                // the dulled enemy: the player takes dull.scale of its next strike, once
                var dulled = new EnemyModifierState308();
                dulled.Dull(10f + dullSeconds, dullScale);
                bool hit1 = EnemyStrikeRule308.Resolve(dulled, 20f, false, 11f, out float firstStrike);
                bool hit2 = EnemyStrikeRule308.Resolve(dulled, 20f, false, 11.5f, out float secondStrike);
                r.Check("못-2", hit1 && hit2 && dullScale < 1f && r.Near(firstStrike, 20f * dullScale) && firstStrike < 20f && WP07Bits(secondStrike) == WP07Bits(20f) && dulled.IsEmpty,
                    "through the doorway: the player takes the dulled enemy's next strike x dull.scale (less than it would have), and the strike after that in full",
                    firstStrike.ToString("R") + " then " + secondStrike.ToString("R"));
                var dulledRanged = new EnemyModifierState308(); dulledRanged.Dull(10f + dullSeconds, dullScale);
                var stale = new EnemyModifierState308(); stale.Dull(10f + dullSeconds, dullScale);
                r.Check("못-2", EnemyStrikeRule308.Resolve(dulledRanged, 8f, true, 11f, out float rangedStrike) && r.Near(rangedStrike, 8f * dullScale) &&
                    EnemyStrikeRule308.Resolve(stale, 8f, false, 10f + dullSeconds, out float lateStrike) && WP07Bits(lateStrike) == WP07Bits(8f),
                    "the one charge dulls a ranged strike as well as a melee one; a charge that ran out (dull.duration) dulls nothing");

                // the weakened enemy: the player takes weaken.scale of every strike while it runs
                var weak = new EnemyModifierState308();
                weak.Weaken(10f + weakSeconds, weakScale);
                var taken = new List<float>();
                foreach (float at in new[] { 10f, 10f + weakSeconds * .5f, 10f + weakSeconds - .01f, 10f + weakSeconds, 10f + weakSeconds + 5f })
                {
                    EnemyStrikeRule308.Resolve(weak, 20f, false, at, out float strike);
                    taken.Add(strike);
                }
                r.Check("옷-2", weakScale < 1f && r.Near(taken[0], 20f * weakScale) && r.Near(taken[1], 20f * weakScale) && r.Near(taken[2], 20f * weakScale) && taken[0] < 20f &&
                    WP07Bits(taken[3]) == WP07Bits(20f) && WP07Bits(taken[4]) == WP07Bits(20f),
                    "through the doorway: the player takes every strike of the weakened enemy x weaken.scale (less than it would have) until weaken.duration is over, then in full again",
                    string.Join(" ", taken.Select(x => x.ToString("R"))));
                var both = new EnemyModifierState308(); both.Weaken(99f, weakScale); both.Dull(99f, dullScale);
                EnemyStrikeRule308.Resolve(both, 20f, false, 11f, out float bothFirst); EnemyStrikeRule308.Resolve(both, 20f, false, 11f, out float bothSecond);
                r.Check("W07", r.Near(bothFirst, 20f * weakScale * dullScale) && r.Near(bothSecond, 20f * weakScale),
                    "a weakened and dulled enemy: the first strike carries both, the next the weakening alone");
                // the doorway never gives the player anything and never raises a strike
                bool neverMore = true;
                foreach (float scale in new[] { 0f, .25f, .5f, .7f, .999f })
                    foreach (float amount in new[] { .5f, 9f, 60f })
                    {
                        var s = new EnemyModifierState308(); s.Weaken(99f, scale); s.Dull(99f, scale);
                        neverMore &= EnemyStrikeRule308.Resolve(s, amount, false, 1f, out float got) && got <= amount && got >= 0f;
                    }
                r.Check("W07", neverMore, "the doorway only ever lowers a strike (0 .. the amount that left the enemy); it gives the player nothing");
                // the veil sits behind the same door: a screened ranged strike does not arrive, and it still uses up the dull charge
                var veiled = new EnemyModifierState308(); veiled.Veil.Screen(7, 99f); veiled.Dull(99f, dullScale);
                bool stopped = !EnemyStrikeRule308.Resolve(veiled, 20f, true, 1f, out _);
                bool meleeArrives = EnemyStrikeRule308.Resolve(veiled, 20f, false, 1f, out float meleeAfter);
                r.Check("W07", stopped && meleeArrives && WP07Bits(meleeAfter) == WP07Bits(20f),
                    "cover stops a ranged strike at the same door (the stopped strike was that enemy's next strike: it used the dull charge up); a melee strike arrives");
            }

            // ---- review B, M4: the handler takes its modifier back when the caster dies, rests or leaves ----
            {
                var state = new EnemyModifierState308();
                state.Brand(99f, 1.5f); state.ShredDefence(99f, .3f); state.Weaken(99f, .5f); state.Dull(99f, .5f);
                state.Stance.Raise(.4f); state.Veil.Blur(5, 99f, .5f);
                state.EndBrand();
                bool brandGone = state.DamageTakenScale(1f) == 1f && state.DefenceShred(1f) == .3f && state.OutgoingScale(1f) == .25f;
                state.EndShred();
                bool shredGone = state.DefenceShred(1f) == 0f && state.OutgoingScale(1f) == .25f;
                state.EndDull();
                bool dullGone = !state.Dulled(1f) && state.OutgoingScale(1f) == .5f;
                state.EndWeaken();
                bool weakGone = state.OutgoingScale(1f) == 1f;
                r.Check("W07", brandGone && shredGone && dullGone && weakGone && WP07Bits(state.TakeStrike(10f, 1f)) == WP07Bits(10f),
                    "a modifier handler takes back exactly its own kind (brand, defence shred, dull, weaken): the other kinds on the same enemy stay until their own handler clears");
                r.Check("W07", state.Stance.Raised && state.Veil.MissShare(1f) == .5f && !state.IsEmpty,
                    "taking the spell modifiers back does not touch the enemy's own guard stance or what veils its strikes (their owners take those back)");
                var twice = new EnemyModifierState308(); twice.Brand(50f, 1.2f); twice.Brand(99f, 1.1f); twice.EndBrand();
                r.Check("W07", twice.IsEmpty && twice.DamageTakenScale(1f) == 1f, "every running entry of the kind ends (a strong short one and a weak long one together)");
            }

            // ---- a new life starts clean ----
            {
                var state = new EnemyModifierState308();
                state.Brand(99f, 1.5f); state.ShredDefence(99f, .3f); state.Weaken(99f, .5f); state.Dull(99f, .5f); state.IgnoreDefence(8, 1f);
                bool loaded = !state.IsEmpty && state.DamageTakenScale(1f) == 1.5f && state.OutgoingScale(1f) == .25f;
                state.Clear();
                r.Check("W07", loaded && state.IsEmpty && state.DamageTakenScale(1f) == 1f && state.DefenceShred(1f) == 0f && state.OutgoingScale(1f) == 1f && state.IgnoredShare(8) == 0f,
                    "Clear (the enemy died, was restored or disabled) removes every modifier");
            }
        }
    }
}
