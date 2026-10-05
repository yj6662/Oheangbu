using System;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 WP-05 fixtures (SPEC-SPELL-120-308 section 13): enemy control. Every cast is a drawn letter that goes through the
    // resolver and the real wiring; hits land through the wiring's own tick. What the fixture adds to the offline rule cases:
    // the real EnemyControlState, the cancelled telegraph of a real EnemyController, the extra damage of a broken freeze as
    // the wiring applies it, the airborne TEST target, and that a clear takes every control back.
    // What stays for Play: enemy AI under control (movement really slows, attacks really resume), presentation, real input.
    public static partial class Spell120Checks308
    {
        // An enemy attack in its telegraph, without Play: the controller is a plain component here (Edit Mode runs no Awake).
        static EnemyController WP05Telegraphing(SpellFixture308 f, EnemyVitals vitals)
        {
            var controller = vitals.gameObject.AddComponent<EnemyController>();
            SpellFixture308.Set(controller, "_vitals", vitals); SpellFixture308.Set(controller, "_config", f.Config);
            WP05Telegraph(controller);
            return controller;
        }

        static void WP05Telegraph(EnemyController controller)
        {
            var state = typeof(EnemyController).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
            if (state == null) throw new MissingFieldException(nameof(EnemyController), "_state");
            state.SetValue(controller, Enum.Parse(state.FieldType, "Telegraph"));
        }

        static partial void FixturesWP05(Report r)
        {
            // grid cells by index: 15 = wood circle + nieun final (interrupt), 101 = water single + siot final (freeze),
            // 114 = water path + ieung final (undertow), 1 = the plain wood single used as "another hit"
            char interrupt = SpellGrammar308.LetterAt(15), freeze = SpellGrammar308.LetterAt(101), undertow = SpellGrammar308.LetterAt(114), single = SpellGrammar308.LetterAt(1);
            if (!r.Wants(new string(new[] { interrupt, freeze, undertow }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var interruptFx = effects.OfType<InterruptEffect308>().Single();
            var freezeFx = effects.OfType<FreezeEffect308>().Single();
            var undertowFx = effects.OfType<UndertowEffect308>().Single();
            using (var f = NewFixture(r, effects, new[] { "Rules308_Base", "Rules308_WP00", "Rules308_WP05" }))
            {
                ISpellCastHost host = f.Wiring;
                f.Finals.Open.Add(SpellFinal.Nieun); f.Finals.Open.Add(SpellFinal.Siot); f.Finals.Open.Add(SpellFinal.Ieung);
                f.Resolver.TryRow(interrupt, out var iRow); f.Resolver.TryRow(freeze, out var fRow); f.Resolver.TryRow(undertow, out var uRow);
                f.Book.TryGet(single, out var plain);
                float cost = f.Config.SpellInkCost, brush = f.Brush(), now = Time.time;
                // the giyeok precedent (root duration, boss root speed): the profile's own defaults
                var giyeok = f.Own(ScriptableObject.CreateInstance<EAGiyeokProfileSO>());   // destroyed with the fixture, whatever throws
                float rootDuration = giyeok.RootDuration, bossRootSpeed = giyeok.BossRootSpeed;
                if (!r.Check("W05", iRow != null && fRow != null && uRow != null && iRow.Handler == InterruptEffect308.HandlerId && fRow.Handler == FreezeEffect308.HandlerId &&
                    uRow.Handler == UndertowEffect308.HandlerId, "the three WP-05 rows are in the fixture table with their handlers")) return;

                // ================= interrupt =================
                f.RestoreAll(); f.Aim(f.Inside[1]);
                var controller = WP05Telegraphing(f, f.Inside[0]);
                float ink = f.Ink.Value; float[] hp = f.Inside.Select(e => e.Hp).ToArray();
                f.Cast(interrupt);
                float strike = iRow.BasePower * brush * iRow.F("power.scale", 1f);
                r.Check("곤-1", f.Misfires.Count == 0 && f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 3 &&
                    f.Inside.All(e => f.Plans[0].Hits.Any(h => h.Target == e)) && interruptFx.WaitingHits == 3,
                    "a drawn cast: one spell cost, the circle around the aimed enemy schedules a hit on each of the three enemies inside, and each is watched");
                r.Check("곤-4", f.Plans[0].Hits.All(h => Near(h.Power, strike)) && strike < iRow.BasePower * brush,
                    "the strike is the cast power x power.scale (weaker than the base circle)");
                r.Check("곤-3", controller.IsTelegraphing && f.Inside.All(e => !e.Control.BlocksActions(now)), "before the hit lands the telegraph runs and nobody is controlled");
                f.Land();
                r.Check("곤-1", f.Hits.Count == 3 && Enumerable.Range(0, 3).All(i => Near(hp[i] - f.Inside[i].Hp, strike)) &&
                    f.Inside.All(e => e.Control.BlocksActions(now) && e.Control.MovementScale(now) == 0f) &&
                    f.Outside.All(e => !e.Control.BlocksActions(now) && e.Control.MovementScale(now) == 1f) && f.Boss.Control.MovementScale(now) == 1f,
                    "when the hits land every enemy inside is struck and controlled; the three outside and the boss outside the circle are untouched");
                r.Check("곤-3", !controller.IsTelegraphing && !controller.AttackInProgress, "the attack that was in its telegraph is cancelled when the hit lands");
                r.Check("A10", f.Inside.All(e => e.Groggy.Value01 == 0f && !e.WeakPointActive && e.DamageMultiplier == 1f && e.WeakPointElementMask == 0),
                    "control only: no groggy, no weak point, no damage bonus");
                float iDuration = iRow.F("interrupt.duration", 0f);
                r.Check("곤-4", iDuration > 0f && iDuration < rootDuration, "the control is shorter than the giyeok root (" + iDuration + " < " + rootDuration + ")");
                r.Check("곤-2", f.Inside.All(e => e.Control.BlocksActions(now + iDuration - .01f)) && interruptFx.HeldControls == 3,
                    "the block holds for the row's duration");
                r.Check("곤-2", f.Inside.All(e => !e.Control.BlocksActions(now + iDuration + .01f) && e.Control.MovementScale(now + iDuration + .01f) == 1f),
                    "after the duration the enemy acts and moves again");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Boss);
                f.Cast(interrupt); f.Land();
                r.Check("곤-2", f.Hits.Count == 1 && f.Hits[0].Target == f.Boss && !f.Boss.Control.BlocksActions(now) && Near(f.Boss.Control.MovementScale(now), iRow.F("boss.speed", 1f)),
                    "boss (profile data): slowed to boss.speed, actions not blocked");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Inside[1]);
                f.Cast(interrupt); f.Inside[0].TakeDamage(f.Inside[0].MaxHp * 2f); f.Land();
                bool waiting = interruptFx.WaitingHits == 1;
                interruptFx.Tick(new SpellTickContext(now + 10f, 0f, host, f.Fx)); interruptFx.Tick(new SpellTickContext(now + 10.02f, 0f, host, f.Fx));
                r.Check("W05", waiting && f.Hits.Count == 2 && interruptFx.WaitingHits == 0 && !f.Inside[0].Control.BlocksActions(now),
                    "a hit that never lands (its target died first) controls nothing and its watch entry is dropped");
                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Inside[1]);
                f.Cast(interrupt); f.Land();
                bool held = f.Inside.All(e => e.Control.BlocksActions(now));
                f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                r.Check("W05", held && interruptFx.HeldControls == 0 && f.Inside.All(e => !e.Control.BlocksActions(now) && e.Control.MovementScale(now) == 1f),
                    "a clear (player death) takes the interrupt's controls back");

                // ================= freeze =================
                f.RestoreAll(); f.Aim(f.Inside[0]);
                ink = f.Ink.Value; float before = f.Inside[0].Hp;
                f.Cast(freeze);
                float castPower = fRow.BasePower * brush, bonus = castPower * fRow.F("shatter.power", 0f), plainPower = plain.BasePower * brush;
                r.Check("앗-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && f.Plans[0].Hits[0].Target == f.Inside[0] &&
                    Near(f.Plans[0].Hits[0].Power, castPower) && freezeFx.WaitingHits == 1 && !f.Inside[0].Control.BlocksActions(now),
                    "a drawn cast: one spell cost, one scheduled hit on the aimed enemy; nothing is frozen before it lands");
                f.Land();
                r.Check("앗-1", f.Hits.Count == 1 && Near(before - f.Inside[0].Hp, castPower) && f.Inside[0].Control.BlocksActions(now) && f.Inside[0].Control.MovementScale(now) == 0f &&
                    freezeFx.FrozenCount == 1 && !f.Inside[1].Control.BlocksActions(now) && !f.Inside[2].Control.BlocksActions(now),
                    "the confirmed hit freezes its one target: it cannot act or move; nobody else is frozen");
                r.Check("A10", f.Inside[0].Groggy.Value01 == 0f && !f.Inside[0].WeakPointActive && f.Inside[0].DamageMultiplier == 1f, "the freeze opens no weak point and adds no groggy");
                before = f.Inside[0].Hp; f.ClearLog();
                f.Cast(single); f.Land();
                r.Check("앗-2", !f.Inside[0].Control.BlocksActions(now) && f.Inside[0].Control.MovementScale(now) == 1f && freezeFx.FrozenCount == 0 && freezeFx.HeldControls == 0,
                    "the next confirmed hit breaks the ice: the control is released at once");
                r.Check("앗-3", f.Hits.Count == 2 && Near(before - f.Inside[0].Hp, plainPower + bonus) && bonus > 0f &&
                    f.Hits.Any(h => Near(h.AppliedDamage, bonus) && h.Attack.Source == DamageSource.PlayerDirect && h.Attack.Element == Element.Water),
                    "the break adds the extra damage once (cast power x shatter.power, water) on top of the hit that broke it");
                before = f.Inside[0].Hp; f.ClearLog();
                f.Cast(single); f.Land();
                r.Check("앗-3", f.Hits.Count == 1 && Near(before - f.Inside[0].Hp, plainPower), "a further hit finds no ice: no second bonus");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Inside[0]);
                f.Cast(freeze); f.Land();
                float fDuration = fRow.F("freeze.duration", 0f);
                bool frozen = f.Inside[0].Control.BlocksActions(now + fDuration - .01f) && freezeFx.FrozenCount == 1;
                freezeFx.Tick(new SpellTickContext(now + fDuration + .01f, 0f, host, f.Fx));
                bool thawed = freezeFx.FrozenCount == 0 && !f.Inside[0].Control.BlocksActions(now + fDuration + .01f);
                before = f.Inside[0].Hp; f.ClearLog();
                f.Cast(single); f.Land();
                r.Check("앗-2", frozen && thawed && f.Hits.Count == 1 && Near(before - f.Inside[0].Hp, plainPower),
                    "a freeze nobody breaks ends by itself after the row's duration, and a later hit gets no bonus");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Boss);
                f.Cast(freeze); f.Land();
                r.Check("앗-4", f.Boss.IsBoss && !f.Boss.Control.BlocksActions(now) && Near(f.Boss.Control.MovementScale(now), fRow.F("boss.speed", 1f)) && freezeFx.FrozenCount == 1 &&
                    Near(fRow.F("boss.speed", 1f), bossRootSpeed),
                    "boss (profile data): slowed to boss.speed (= the giyeok boss root speed), actions not blocked");
                before = f.Boss.Hp; f.ClearLog();
                f.Cast(single); f.Land();
                r.Check("앗-4", Near(before - f.Boss.Hp, plainPower + bonus) && f.Boss.Control.MovementScale(now) == 1f && freezeFx.FrozenCount == 0,
                    "the boss's ice breaks the same way: bonus once, slow released");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Inside[0]);
                f.Cast(freeze); f.Land();
                held = f.Inside[0].Control.BlocksActions(now);
                f.Wiring.ClearSpells308(SpellClearReason.PlayerDied);
                r.Check("W05", held && freezeFx.FrozenCount == 0 && freezeFx.HeldControls == 0 && !f.Inside[0].Control.BlocksActions(now), "a clear (player death) thaws every freeze");

                // ================= undertow =================
                f.RestoreAll(); f.Aim(f.Inside[0]);
                var flyer = f.Inside[2].gameObject.AddComponent<SpellAirborneTestTarget308>();      // the TEST target of the pull-down clause (Q5)
                var swept = f.Inside.Concat(new[] { f.Boss }).ToArray();
                ink = f.Ink.Value; hp = swept.Select(e => e.Hp).ToArray();
                f.Cast(undertow);
                float wave = uRow.BasePower * brush, slow = uRow.F("slow.speed", 1f), uDuration = uRow.F("slow.duration", 0f);
                r.Check("옹-1", f.Accepted.Count == 1 && Near(ink - f.Ink.Value, cost) && f.Plans.Count == 1 && f.Plans[0].Hits.Count == 4 &&
                    swept.All(e => f.Plans[0].Hits.Any(h => h.Target == e && Near(h.Power, wave))) && undertowFx.WaitingHits == 4 &&
                    swept.All(e => e.Control.MovementScale(now) == 1f) && flyer.IsAirborne(now),
                    "a drawn cast: one spell cost, the corridor schedules its hit on the four enemies inside (boss included); nobody is slowed before the front arrives");
                f.Land();
                r.Check("옹-1", f.Hits.Count == 4 && Enumerable.Range(0, 4).All(i => Near(hp[i] - swept[i].Hp, wave)) &&
                    swept.All(e => !e.Control.BlocksActions(now) && Near(e.Control.MovementScale(now), slow)) && slow < 1f &&
                    f.Outside.All(e => e.Control.MovementScale(now) == 1f) && undertowFx.HeldControls == 4,
                    "every swept enemy is slowed to slow.speed and still acts; the three outside the corridor keep their speed");
                r.Check("옹-2", !flyer.IsAirborne(now) && flyer.PullCount == 1 && !flyer.IsAirborne(now + uDuration - .01f) && flyer.IsAirborne(now + uDuration + .01f),
                    "the airborne TEST target is pulled to the ground for the slow's duration and is airborne again afterwards");
                r.Check("옹-2", Near(f.Inside[2].Control.MovementScale(now), slow) && f.Inside[2].GetComponent<ISpellAirborne308>() != null && f.Inside[0].GetComponent<ISpellAirborne308>() == null,
                    "the pulled enemy is slowed like the others; an enemy without the airborne seam is only slowed");
                r.Check("A10", swept.All(e => e.Groggy.Value01 == 0f && !e.WeakPointActive && e.DamageMultiplier == 1f), "the undertow opens no weak point and adds no groggy");
                r.Check("옹-1", swept.All(e => Near(e.Control.MovementScale(now + uDuration - .01f), slow)) && swept.All(e => e.Control.MovementScale(now + uDuration + .01f) == 1f),
                    "the slow lasts the row's duration; afterwards every enemy has its own speed back");

                f.Wiring.ClearSpells308(SpellClearReason.Rest); f.RestoreAll(); f.Aim(f.Inside[0]);
                f.Cast(undertow); f.Land();
                held = swept.All(e => Near(e.Control.MovementScale(now), slow)) && !flyer.IsAirborne(now) && flyer.PullCount == 2;
                f.Wiring.ClearSpells308(SpellClearReason.SceneLeave);
                r.Check("W05", held && undertowFx.HeldControls == 0 && swept.All(e => e.Control.MovementScale(now) == 1f) && flyer.IsAirborne(now) && flyer.ActivePulls == 0,
                    "a clear (scene leave) takes the slow back and lets the pulled target go");
            }
        }
    }
}
