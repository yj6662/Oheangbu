using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-10 fixtures (SPEC-SPELL-120-308, harmony install): the real wiring, real EnemyVitals and the registered
    // InstallEffect308. Every cast comes from the resolver; attack hits land through the wiring's own tick; the non-detonating
    // sources go through the wiring's own entry points. The clock of the effect is explicit (Edit Mode time does not advance).
    // What stays for Play: the flying mark and its attach presentation, lock-on and free aim with a real camera, enemy AI.
    public static partial class Spell120Checks308
    {
        static readonly string[] WP10Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP10" };

        static partial void FixturesWP10(Report r)
        {
            // grid cells by index: 4 / 28 / 52 / 76 / 100 = the five installs (a + mieum), 1 = wood single attack
            int[] cells = { 4, 28, 52, 76, 100 };
            char single = SpellGrammar308.LetterAt(1);
            if (!r.Wants(new string(cells.Select(SpellGrammar308.LetterAt).ToArray()))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var install = effects.OfType<InstallEffect308>().FirstOrDefault();
            if (!r.Check("W10", install != null, "InstallEffect308 is registered (SpellEffectInstaller308.WP10)")) return;
            using (var f = NewFixture(r, effects, WP10Sheets))
            {
                ISpellCastHost host = f.Wiring;
                f.Finals.Open.Add(SpellFinal.Mieum);
                float t = Time.time, cost = f.Config.SpellInkCost, brush = f.Brush();
                SpellTickContext At(float seconds) => new SpellTickContext(t + seconds, 0f, host, f.Fx);
                Vector3 chest = SpellFixture308.Origin + Vector3.up * .4f;
                var summoner = f.Obj("Spell308_WP10Summoned", SpellFixture308.Origin + Vector3.right);
                void Reset() { f.RestoreAll(); f.Wiring.ClearSpells308(SpellClearReason.Rest); }
                int HarmonyHits() => f.Hits.Count(h => h.Attack.Source == DamageSource.Harmony);

                foreach (int cell in cells)
                {
                    char letter = SpellGrammar308.LetterAt(cell);
                    if (!r.Check(letter + "-1", f.Resolver.TryRow(letter, out var row) && row.Handler == InstallEffect308.HandlerId, "the glyph's row runs install.mark")) continue;
                    float life = row.F("install.life", 0f), power = row.F("detonate.power", 0f) * brush;
                    int steps = Mathf.RoundToInt(row.F("detonate.groggy", 0f));
                    var target = f.Inside[0];

                    // ---- clause 1: one enemy carries the mark, attaching deals no damage ----
                    Reset(); f.Aim(target);
                    float hp = target.Hp, ink = f.Ink.Value; int begun = f.Fx.Begun;
                    f.Cast(letter);
                    r.Check(letter + "-1", f.Misfires.Count == 0 && f.Accepted.Count == 1 && f.Accepted[0].Kind == SpellKind.Install && f.Accepted[0].Power == 0f &&
                        Near(ink - f.Ink.Value, cost) && f.PendingCount == 0 && install.InFlight == 1 && install.MarkCount == 0 && f.Fx.Begun == begun + 1,
                        "a drawn install: one spell cost, accepted as Install with power 0, no scheduled hit, the mark is in flight, presented through ISpellPresenter");
                    install.Tick(At(5f));
                    r.Check(letter + "-1", install.InFlight == 0 && install.MarkCount == 1 && install.Marked(target) && !f.Inside.Skip(1).Concat(f.Outside).Any(install.Marked) &&
                        !install.Marked(f.Boss) && target.Hp == hp && f.Hits.Count == 0 && target.Groggy.Value01 == 0f,
                        "on arrival exactly the aimed enemy carries the mark; no damage, no groggy");

                    // ---- clause 2: summon, persistent, companion and retaliation damage do not detonate ----
                    f.Wiring.ApplySummonHit(target, target.LifeRevision, 5f, chest, AttackProvenance.Create(summoner, DamageSource.Summon, Element.Fire), letter);
                    f.Wiring.ApplyPersistentSpellHit(target, 5f, chest, AttackProvenance.Create(f.Player, DamageSource.PersistentSpell, Element.Fire), letter);
                    host.ApplyDirectHit(target, target.LifeRevision, 5f, chest, AttackProvenance.Create(f.Player, DamageSource.Companion, Element.Fire), letter);
                    host.ApplyDirectHit(target, target.LifeRevision, 5f, chest, AttackProvenance.Create(f.Player, DamageSource.Retaliation, Element.Fire), letter);
                    r.Check(letter + "-2", f.Hits.Count == 4 && HarmonyHits() == 0 && install.MarkCount == 1 && target.Groggy.Value01 == 0f,
                        "four applied hits (summon, persistent, companion, retaliation) leave the mark in place: no detonation, no groggy");
                    // an attack spell that hits another enemy does not detonate either
                    f.Aim(f.Inside[1]); f.Cast(single); f.Land();
                    r.Check(letter + "-2", f.Hits.Count == 5 && f.Hits[4].Target == f.Inside[1] && HarmonyHits() == 0 && install.MarkCount == 1,
                        "an attack spell that hits another enemy leaves the mark in place");
                    // the attack spell's hit on the marked enemy detonates
                    f.ClearLog(); f.Aim(target); f.Cast(single); f.Land();
                    bool fired = f.Hits.Count == 2 && f.Hits[0].Attack.Source == DamageSource.PlayerDirect && f.Hits[1].Attack.Source == DamageSource.Harmony && f.Hits[1].Target == target;
                    r.Check(letter + "-2", fired && install.MarkCount == 0 && !install.Marked(target), "the hit of a drawn attack spell on the marked enemy detonates the mark (after the attack's own hit)");

                    // ---- clause 3: groggy steps from the row, through the harmony source ----
                    r.Check(letter + "-3", fired && Near(target.Groggy.Value01, Mathf.Clamp01(steps / (float)f.Config.ParriesToBlossom)) && steps >= 2,
                        "the detonation raises the enemy's groggy by the row's steps (" + steps + " of " + f.Config.ParriesToBlossom + ")");

                    // ---- clause 4: one hit of the installed element ----
                    r.Check(letter + "-4", fired && f.Hits[1].Attack.Element == row.Element && Near(f.Hits[1].AppliedDamage, power) && f.Hits[1].Attack.AttackId != f.Hits[0].Attack.AttackId,
                        "the detonation is one hit of the installing glyph's element with the row's power x brush (" + power.ToString("0.###") + "), under its own attack identity");
                    float groggy = target.Groggy.Value01;
                    f.ClearLog(); f.Cast(single); f.Land();
                    r.Check(letter + "-4", f.Hits.Count == 1 && HarmonyHits() == 0 && target.Groggy.Value01 == groggy, "the next attack hit finds no mark: one detonation per install, and an attack alone adds no groggy");

                    // ---- an unfired mark expires; ink is not refunded ----
                    Reset(); f.Aim(target); ink = f.Ink.Value; f.Cast(letter); install.Tick(At(5f));
                    install.Tick(At(5f + life + 1f));
                    f.ClearLog(); float inkAfter = f.Ink.Value; f.Cast(single); f.Land();
                    r.Check("W10", install.MarkCount == 0 && HarmonyHits() == 0 && Near(ink - inkAfter, cost), letter + ": an unfired mark expires after install.life; only the ink is gone");
                }

                // ---- the cap (TEST 1): a second install replaces the first ----
                char first = SpellGrammar308.LetterAt(cells[0]), second = SpellGrammar308.LetterAt(cells[1]);
                Reset(); f.Aim(f.Inside[0]); f.Cast(first); install.Tick(At(5f));
                f.Aim(f.Inside[1]); f.Cast(second); install.Tick(At(10f));
                r.Check("W10", install.MarkCount == 1 && install.Marked(f.Inside[1]) && !install.Marked(f.Inside[0]), "cap 1: installing on a second enemy drops the older mark");
                f.ClearLog(); f.Aim(f.Inside[0]); f.Cast(single); f.Land();
                r.Check("W10", HarmonyHits() == 0 && install.MarkCount == 1, "the replaced mark is gone unfired: hitting its old enemy detonates nothing");

                // ---- a mark does not outlive its enemy's life ----
                Reset(); f.Aim(f.Inside[0]); f.Cast(first); install.Tick(At(5f));
                f.Inside[0].Restore();                                                          // another life in the same slot
                install.Tick(At(6f));
                r.Check("W10", install.MarkCount == 0, "a mark is lost when its enemy starts another life");
                Reset(); f.Aim(f.Inside[0]); f.Cast(first);
                f.Inside[0].Restore(); install.Tick(At(5f));
                r.Check("W10", install.MarkCount == 0 && install.InFlight == 0, "a mark in flight does not attach to another life of its target");

                // ---- no enemy to aim at: ink only ----
                Reset();
                foreach (var enemy in f.Inside.Concat(f.Outside).Append(f.Boss)) enemy.TakeDamage(100000f, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, null));
                float inkBefore = f.Ink.Value; f.Aim(null); f.ClearLog(); f.Cast(first);
                r.Check("W10", f.Accepted.Count == 1 && install.InFlight == 0 && install.MarkCount == 0 && Near(inkBefore - f.Ink.Value, cost) && f.Misfires.Count == 0,
                    "cast with no living enemy to carry it: the ink is spent, nothing is installed, no misfire ink on top");

                // ---- A10: the detonation is not a five-element credit and opens no weak-point bit ----
                Reset(); f.Aim(f.Inside[0]); f.Cast(second); install.Tick(At(5f));
                f.Inside[0].OpenWeakPoint();
                f.ClearLog(); f.Cast(single); f.Land();
                f.Resolver.TryRow(single, out var attackRow); f.Resolver.TryRow(second, out var installRow);
                r.Check("A10", HarmonyHits() == 1 && attackRow != null && installRow != null && attackRow.Element != installRow.Element &&
                    f.Inside[0].WeakPointElementMask == 1 << (int)attackRow.Element,
                    "inside a weak-point window the attack's element is credited and the harmony detonation's element is not");

                // ---- clear ----
                Reset(); f.Aim(f.Inside[0]); f.Cast(first); install.Tick(At(5f)); f.Cast(second);
                f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                r.Check("W10", install.MarkCount == 0 && install.InFlight == 0, "death, rest, scene leave and disable drop every mark and every mark in flight");
            }
        }
    }
}
