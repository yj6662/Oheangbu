using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-07 fixtures (SPEC-SPELL-120-308, user answer Q4 = D308-13): enemy modifiers on the real EnemyVitals, wiring and
    // handlers. The armoured enemy is a TEST target (a profile made in memory with defence 0.4): no enemy of the project has
    // defence yet. Every cast comes from the resolver; every hit lands through the wiring's own tick.
    // What stays for Play: time running out on a modifier (Edit Mode has no clock; the pure runner covers expiry), real enemy
    // attacks reaching the doorway, the mark's presentation on the enemy.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP07Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP07" };

        static partial void FixturesWP07(Report r)
        {
            // grid cells by index: 6 wood / 30 fire single + ieung (pierce), 53 earth single + siot (break), 41 fire cone + siot (break),
            // 75 metal single + nieun (brand), 63 earth path + nieun (brand), 65 earth path + siot (dull), 113 water path + siot (weaken),
            // 1 = wood single (an existing glyph: the plain hit), 16 = wood summon (letter of a summon-source hit)
            char pierceWood = SpellGrammar308.LetterAt(6), pierceFire = SpellGrammar308.LetterAt(30), breakOne = SpellGrammar308.LetterAt(53), breakCone = SpellGrammar308.LetterAt(41);
            char brandOne = SpellGrammar308.LetterAt(75), brandPath = SpellGrammar308.LetterAt(63), dullPath = SpellGrammar308.LetterAt(65), weakenPath = SpellGrammar308.LetterAt(113);
            char single = SpellGrammar308.LetterAt(1), summon = SpellGrammar308.LetterAt(16);
            if (!r.Wants(new string(new[] { pierceWood, pierceFire, breakOne, breakCone, brandOne, brandPath, dullPath, weakenPath }))) return;

            const float Armour = .4f, Strike = 10f;
            var profile = ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();
            try
            {
                SpellFixture308.Set(profile, "_defence", Armour); SpellFixture308.Set(profile, "_maxHp", 1000f);
                // only the basic shapes and this package's handlers: another package's hooks cannot colour these results
                var effects = SpellEffectInstaller308.CreateAll().Where(e => e is ModifierEffect308 || e is CoreShapeEffect308).ToArray();
                using (var f = NewFixture(r, effects, WP07Sheets))
                {
                    f.Finals.Open.Add(SpellFinal.Nieun); f.Finals.Open.Add(SpellFinal.Siot); f.Finals.Open.Add(SpellFinal.Ieung);
                    var armoured = f.Inside[0];
                    armoured.ConfigureProfile(profile); armoured.Restore();
                    var all = new List<EnemyVitals>(f.Inside); all.AddRange(f.Outside); all.Add(f.Boss);
                    SpellRow Row(char letter) { f.Resolver.TryRow(letter, out var row); return row; }
                    // one hit of the existing wood single glyph on a target, landed through the wiring: what it applied
                    float planned = 0f;
                    float PlainHit(EnemyVitals target)
                    {
                        f.ClearLog(); f.Ink.Restore(); f.Aim(target); f.Cast(single);
                        planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : -1f;
                        f.Land();
                        return f.Hits.Count == 1 ? f.Hits[0].AppliedDamage : -1f;
                    }
                    bool OnlyThese(IEnumerable<EnemyVitals> marked) => all.All(e => marked.Contains(e) != e.Modifiers.IsEmpty);

                    // ---- the new stat: data, default 0, and the intake of an existing glyph ----
                    f.RestoreAll();
                    r.Check("W07", f.Inside[1].Defence == 0f && f.Boss.Defence == 0f && f.Boss.Profile != null && armoured.Defence == Armour && all.All(e => e.Modifiers.IsEmpty),
                        "defence is enemy data: 0 without a profile, 0 on a profile that does not set it, " + Armour + " on the TEST target; no enemy starts with a modifier");
                    float open = PlainHit(f.Inside[1]); float openPlanned = planned;
                    r.Check("W07", openPlanned > 0f && Near(open, openPlanned), "an enemy without defence takes the planned power of an existing glyph, as before WP-07");
                    float shut = PlainHit(armoured);
                    r.Check("W07", Near(shut, planned * (1f - Armour)), "the armoured TEST target takes (1 - defence) of the same hit  [" + shut + " of " + planned + "]");

                    // ---- mod.pierce ----
                    foreach (char letter in new[] { pierceWood, pierceFire })
                    {
                        f.RestoreAll(); f.Aim(armoured); f.Cast(letter);
                        bool cast = f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Target == armoured;
                        float power = cast ? f.Plans[0].Hits[0].Power : 0f; long id = cast ? f.Plans[0].Hits[0].AttackId : 0;
                        bool told = cast && armoured.Modifiers.IgnoredShare(id) == Row(letter).F("pierce.defence", 0f) && Near(power, Row(letter).BasePower * f.Brush());
                        f.Land();
                        r.Check(letter + "-1", told && f.Hits.Count == 1 && Near(f.Hits[0].AppliedDamage, power) && armoured.Modifiers.IsEmpty,
                            "armoured target: the hit of this cast lands with its whole power (defence ignored) and leaves nothing on the enemy");
                        float after = PlainHit(armoured);
                        r.Check(letter + "-1", Near(after, planned * (1f - Armour)), "the next ordinary hit meets the defence again");
                        f.RestoreAll(); f.Aim(f.Inside[1]); f.Cast(letter); power = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : -1f; f.Land();
                        r.Check(letter + "-1", f.Hits.Count == 1 && Near(f.Hits[0].AppliedDamage, power) && f.Inside[1].Modifiers.IsEmpty, "target without defence: a plain hit of the row's power");
                    }
                    {
                        // a piercing hit that never lands is taken back (two effect ticks at or after its impact time), and Clear forgets the rest
                        var effect = f.Registry.Find(DefencePierceEffect308.HandlerId) as ModifierEffect308;
                        f.RestoreAll(); f.Aim(armoured); f.Cast(pierceWood);
                        long id = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].AttackId : 0;
                        float impact = id > 0 ? f.Plans[0].Hits[0].ImpactTime : 0f;
                        bool waiting = effect != null && effect.HitsOnTheWay == 1 && armoured.Modifiers.IgnoredShare(id) > 0f;
                        var late = new SpellTickContext(impact + 1f, 0f, f.Wiring, f.Fx);
                        if (effect != null) { effect.Tick(late); effect.Tick(late); }
                        r.Check("W07", waiting && effect.HitsOnTheWay == 0 && armoured.Modifiers.IsEmpty, "a piercing hit that never landed is taken back from the enemy");
                        f.RestoreAll(); f.Aim(armoured); f.Cast(pierceWood);
                        bool again = effect != null && effect.HitsOnTheWay == 1;
                        f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                        r.Check("W07", again && effect.HitsOnTheWay == 0, "ClearSpells308 forgets the modifier hits still on their way");
                    }

                    // ---- mod.armorbreak: the target that was hit ----
                    {
                        float amount = Row(breakOne).F("break.defence", 0f);
                        f.RestoreAll(); f.Aim(armoured); f.Cast(breakOne);
                        bool nothingAtCast = all.All(e => e.Modifiers.IsEmpty) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1;
                        float power = nothingAtCast ? f.Plans[0].Hits[0].Power : 0f;
                        f.Land();
                        r.Check(breakOne + "-1", nothingAtCast && f.Hits.Count == 1 && f.Hits[0].Target == armoured && Near(f.Hits[0].AppliedDamage, power * (1f - Armour)) &&
                            Near(armoured.Modifiers.DefenceShred(Time.time), amount) && OnlyThese(new[] { armoured }),
                            "single shape: nothing at cast time; when the hit lands (itself still against the full defence) the break is on that enemy and on nobody else");
                        float after = PlainHit(armoured);
                        r.Check(breakOne + "-2", amount > 0f && Near(after, planned * (1f - Mathf.Max(0f, Armour - amount))) && after > shut,
                            "armoured target: the next hit loses less to defence (defence - break.defence)  [" + after + " after, " + shut + " before]");
                        armoured.Restore();
                        r.Check(breakOne + "-2", armoured.Modifiers.IsEmpty && Near(PlainHit(armoured), planned * (1f - Armour)), "a new life starts with the full defence again");
                        f.RestoreAll(); f.Aim(f.Inside[1]); f.Cast(breakOne); f.Land();
                        r.Check(breakOne + "-2", Near(PlainHit(f.Inside[1]), planned), "target without defence: the break adds no damage (a counter to armour, not a brand)");
                    }

                    // ---- mod.armorbreak: everyone the cone swept ----
                    {
                        float amount = Row(breakCone).F("break.defence", 0f);
                        f.RestoreAll(); f.Cast(breakCone);
                        var swept = f.Plans.Count == 1 ? f.Plans[0].Hits.Select(h => h.Target).ToArray() : new EnemyVitals[0];
                        bool nothingAtCast = all.All(e => e.Modifiers.IsEmpty);
                        f.Land();
                        r.Check(breakCone + "-1", swept.Length >= 2 && swept.Contains(armoured) && !swept.Contains(f.Outside[0]) && nothingAtCast && OnlyThese(swept) &&
                            swept.All(e => Near(e.Modifiers.DefenceShred(Time.time), amount)),
                            "cone shape: every enemy the cone swept carries the break after its hit landed (" + swept.Length + "), nobody outside the cone does");
                        float after = PlainHit(armoured);
                        r.Check(breakCone + "-2", amount > 0f && Near(after, planned * (1f - Mathf.Max(0f, Armour - amount))) && after > shut,
                            "armoured target inside the cone: the next hit loses less to defence");
                    }

                    // ---- mod.brand: the marked target ----
                    {
                        float scale = Row(brandOne).F("brand.scale", 0f); var marked = f.Inside[1];
                        f.RestoreAll(); f.Aim(marked); f.Cast(brandOne);
                        bool nothingAtCast = all.All(e => e.Modifiers.IsEmpty) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1;
                        float power = nothingAtCast ? f.Plans[0].Hits[0].Power : 0f;
                        f.Land();
                        r.Check(brandOne + "-1", nothingAtCast && f.Hits.Count == 1 && f.Hits[0].Target == marked && Near(f.Hits[0].AppliedDamage, power) &&
                            marked.Modifiers.DamageTakenScale(Time.time) == scale && OnlyThese(new[] { marked }),
                            "single shape: the brand is on the enemy the hit landed on and on nobody else; the marking hit itself is not raised");
                        float after = PlainHit(marked);
                        r.Check(brandOne + "-2", scale > 1f && Near(after, planned * scale), "the branded enemy takes brand.scale more from the next hit  [" + after + " of " + planned + "]");
                        var summoner = f.Obj("Spell308_WP07Summoned", SpellFixture308.Origin + Vector3.right);
                        var other = f.Wiring.ApplySummonHit(marked, marked.LifeRevision, 5f, SpellFixture308.Origin + Vector3.up * .4f,
                            AttackProvenance.Create(summoner, DamageSource.Summon, Element.Wood), summon);
                        r.Check(brandOne + "-2", Near(other.AppliedDamage, 5f * scale), "the brand raises damage from every source (a summon-source hit of 5 applied " + other.AppliedDamage + ")");
                        marked.Restore();
                        r.Check(brandOne + "-2", marked.Modifiers.IsEmpty && Near(PlainHit(marked), planned), "a new life is not branded");
                    }

                    // ---- mod.brand / mod.dull / mod.weaken: everyone the path swept ----
                    foreach (char letter in new[] { brandPath, dullPath, weakenPath })
                    {
                        var row = Row(letter);
                        f.RestoreAll(); f.Aim(f.Inside[0]); f.Cast(letter);
                        var swept = f.Plans.Count == 1 ? f.Plans[0].Hits.Select(h => h.Target).ToArray() : new EnemyVitals[0];
                        bool nothingAtCast = all.All(e => e.Modifiers.IsEmpty);
                        f.Land();
                        bool everyone = swept.Length >= 2 && swept.Contains(f.Inside[0]) && !swept.Contains(f.Outside[0]) && nothingAtCast && OnlyThese(swept);
                        float now = Time.time;
                        if (row.Handler == BrandEffect308.HandlerId)
                        {
                            float scale = row.F("brand.scale", 0f);
                            r.Check(letter + "-1", everyone && swept.All(e => e.Modifiers.DamageTakenScale(now) == scale),
                                "path shape: every enemy the path swept is branded after its hit landed (" + swept.Length + "), nobody outside is");
                            float after = PlainHit(f.Inside[1]);
                            r.Check(letter + "-2", scale > 1f && swept.Contains(f.Inside[1]) && Near(after, planned * scale), "a swept enemy takes brand.scale more from the next hit");
                        }
                        else if (row.Handler == DullEffect308.HandlerId)
                        {
                            float scale = row.F("dull.scale", 1f);
                            r.Check(letter + "-1", everyone && swept.All(e => e.Modifiers.Dulled(now)), "path shape: every swept enemy carries the dull charge (" + swept.Length + "), nobody outside does");
                            float first = EnemyStrikeModifiers308.Apply(swept[0], Strike), second = EnemyStrikeModifiers308.Apply(swept[0], Strike), outside = EnemyStrikeModifiers308.Apply(f.Outside[0], Strike);
                            r.Check(letter + "-2", scale < 1f && Near(first, Strike * scale) && second == Strike && outside == Strike && !swept[0].Modifiers.Dulled(now),
                                "a swept enemy's next strike is scaled by dull.scale once, its second strike and an unswept enemy's strike are whole  [" + first + " / " + second + " / " + outside + "]");
                            float hp = f.Player.Hp01 * f.Player.MaxHp, ink = f.Ink.Value;
                            bool struck = EnemyStrike308.Deliver(f.Player, swept[1], Strike, IncomingDamageKind.Melee);
                            float lost = hp - f.Player.Hp01 * f.Player.MaxHp;
                            r.Check(letter + "-2", struck && Near(lost, Strike * scale) && f.Ink.Value == ink,
                                "doorway: the dulled strike costs the player strike x dull.scale and gives nothing back  [lost " + lost + "] " +
                                "(a FAIL with lost = " + Strike + " means EnemyStrike308.Deliver does not call EnemyStrikeModifiers308.Apply: the phase B integration put that call in)");
                        }
                        else
                        {
                            float scale = row.F("weaken.scale", 1f);
                            r.Check(letter + "-1", everyone && swept.All(e => e.Modifiers.OutgoingScale(now) == scale), "path shape: every swept enemy is weakened (" + swept.Length + "), nobody outside is");
                            float first = EnemyStrikeModifiers308.Apply(swept[0], Strike), second = EnemyStrikeModifiers308.Apply(swept[0], Strike), outside = EnemyStrikeModifiers308.Apply(f.Outside[0], Strike);
                            r.Check(letter + "-2", scale < 1f && Near(first, Strike * scale) && Near(second, Strike * scale) && outside == Strike,
                                "every strike of a swept enemy is scaled by weaken.scale while it runs (nothing is used up), an unswept enemy's strike is whole");
                            float hp = f.Player.Hp01 * f.Player.MaxHp, ink = f.Ink.Value;
                            bool struck = EnemyStrike308.Deliver(f.Player, swept[1], Strike, IncomingDamageKind.Melee);
                            float lost = hp - f.Player.Hp01 * f.Player.MaxHp;
                            r.Check(letter + "-2", struck && Near(lost, Strike * scale) && f.Ink.Value == ink,
                                "doorway: the weakened strike costs the player strike x weaken.scale and gives nothing back  [lost " + lost + "] " +
                                "(a FAIL with lost = " + Strike + " means EnemyStrike308.Deliver does not call EnemyStrikeModifiers308.Apply: the phase B integration put that call in)");
                        }
                    }

                    // ---- canon: a modifier never feeds groggy and never opens a weak point ----
                    r.Check("A10", all.All(e => e.Groggy.Value01 == 0f && !e.WeakPointActive), "after every WP-07 cast above: no groggy on any enemy, no weak point opened");
                    r.Check("A9", f.Fx.Begun == 0 && f.Fx.Cues == 0, "the handlers ask nothing of the presenter (the stroke adapter presents the cast): rules cannot depend on presentation");
                }
            }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
