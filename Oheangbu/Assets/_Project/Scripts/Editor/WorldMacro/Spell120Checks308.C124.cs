using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-124 fixtures (debate decision Codex · Fable · Claude, TEST): inside the weak-point window a player spell whose
    // element overcomes the enemy's attack element hurts x CounterElementWindowMultiplier. The enemy's element is read through
    // EnemyVitals.TryAttackElement, the same field the sword counter reads. The rule itself is in Pure308/Cases_Counter124.cs;
    // here the real damage path, the sources it covers and the boss registration slot.
    public static partial class Spell120Checks308
    {
        static partial void FixturesC124(Report r)
        {
            char single = SpellGrammar308.LetterAt(1);   // wood single attack
            if (!r.Wants(single.ToString())) return;
            using (var f = NewFixture(r, SpellEffectInstaller308.CreateAll(), FoundationSheets))
            {
                float m = f.Config.CounterElementWindowMultiplier;
                r.Check("C124", Near(m, 1.25f), "the config carries the TEST multiplier 1.25 (" + m + ")");
                if (!r.Check("C124", f.Resolver.TryRow(single, out var row), "the wood single attack resolves")) return;
                var elements = Enum.GetValues(typeof(Element)).Cast<Element>().ToArray();
                Element beaten = elements.First(e => ElementRelations.Overcomes(row.Element, e));
                Element other = elements.First(e => !ElementRelations.Overcomes(row.Element, e) && e != row.Element);
                var target = f.Inside[0];
                var controller = target.gameObject.AddComponent<EnemyController>();
                try
                {
                    // ---- the drawn shot: resolver -> cast -> landing hit (PlayerDirect) ----
                    float Shot(Element enemy, bool window)
                    {
                        f.RestoreAll(); f.Wiring.ClearSpells308(SpellClearReason.Rest);
                        SpellFixture308.Set(controller, "_rangedElement", enemy);
                        if (window) target.OpenWeakPoint();
                        f.Aim(target); f.Cast(single); f.Land();
                        return f.Hits.Where(h => h.Target == target && h.Attack.Source == DamageSource.PlayerDirect).Sum(h => h.AppliedDamage);
                    }
                    float openBeaten = Shot(beaten, true), openOther = Shot(other, true);
                    float window = target.DamageMultiplier;
                    float shutBeaten = Shot(beaten, false), shutOther = Shot(other, false);
                    r.Check("C124", shutBeaten > 0f && Near(shutBeaten, shutOther), "outside the window the enemy's element changes nothing (" + shutBeaten + " / " + shutOther + ")");
                    r.Check("C124", window > 1f && Near(openOther, shutOther * window, .01f), "inside the window a shot that does not overcome gets the window multiplier only (x" + window + ")");
                    r.Check("C124", Near(openBeaten, openOther * m, .01f), "inside the window a drawn shot whose element overcomes the enemy's hurts x" + m + " (" + openBeaten + " / " + openOther + ")");
                    SpellFixture308.Set(controller, "_rangedElement", beaten);
                    r.Check("C124", target.TryAttackElement(out var read) && read == beaten, "the sword counter and the scale read one field (EnemyVitals.TryAttackElement = " + read + ")");

                    // ---- every source, straight into the vitals ----
                    float Direct(DamageSource source, Element? element)
                    {
                        f.RestoreAll(); if (controller != null) SpellFixture308.Set(controller, "_rangedElement", beaten);
                        target.OpenWeakPoint();
                        float hp = target.Hp;
                        target.TakeDamage(10f, AttackProvenance.Create(f.Player, source, element));
                        return hp - target.Hp;
                    }
                    float plain = 10f * window;   // the window multiplier read while the window was open
                    foreach (var source in new[] { DamageSource.PlayerDirect, DamageSource.Companion, DamageSource.PersistentSpell, DamageSource.Harmony })
                        r.Check("C124", Near(Direct(source, row.Element), plain * m, .01f), source + " (a player spell) is scaled");
                    foreach (var source in new[] { DamageSource.Summon, DamageSource.Retaliation })
                        r.Check("C124", Near(Direct(source, row.Element), plain, .01f), source + " is not a player spell: not scaled");
                    r.Check("C124", Near(Direct(DamageSource.PlayerDirect, null), plain, .01f), "a neutral hit is not scaled");
                    Object.DestroyImmediate(controller); controller = null;
                    r.Check("C124", !target.TryAttackElement(out _) && Near(Direct(DamageSource.PlayerDirect, row.Element), plain, .01f),
                        "an enemy without an attack element is not scaled");
                }
                finally { if (controller != null) Object.DestroyImmediate(controller); }

                // ---- bosses: the controller registers itself on the vitals it is wired to (they may sit on another object) ----
                var boss = f.Inside[1];
                var general = f.Obj("Spell308_C124General", SpellFixture308.Origin + Vector3.left * 40f).AddComponent<SouthGateGeneralController>();
                r.Check("C124", !boss.TryAttackElement(out _), "before the wiring the enemy carries no attack element");
                SpellFixture308.Set(general, "_vitals", boss);
                general.Configure(null, null, null);
                r.Check("C124", boss.TryAttackElement(out var earth) && earth == SouthGateGeneralController.AttackElement308 && earth == Element.Earth,
                    "the south gate general registers its earth on the vitals it is wired to (another object)");
                f.RestoreAll(); boss.OpenWeakPoint();
                float plainBoss = 10f * boss.DamageMultiplier, hpBoss = boss.Hp;
                boss.TakeDamage(10f, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                float wood = hpBoss - boss.Hp; f.RestoreAll(); boss.OpenWeakPoint(); hpBoss = boss.Hp;
                boss.TakeDamage(10f, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Metal));
                float metal = hpBoss - boss.Hp;
                r.Check("C124", Near(wood, plainBoss * m, .01f) && Near(metal, plainBoss, .01f), "wood (overcomes earth) is scaled on the general, metal is not (" + wood + " / " + metal + ")");
                Object.DestroyImmediate(general);
                r.Check("C124", !boss.TryAttackElement(out _), "a destroyed controller drops its registration");
                var dragon = f.Obj("Spell308_C124Dragon", SpellFixture308.Origin + Vector3.left * 60f).AddComponent<CheongryongCombatController>();
                r.Check("C124", dragon.GetComponent<EnemyVitals>().TryAttackElement(out var wood2) && wood2 == CheongryongCombatController.AttackElement308 && wood2 == Element.Wood,
                    "the blue dragon on its own vitals answers wood (no registration needed)");
            }
        }
    }
}