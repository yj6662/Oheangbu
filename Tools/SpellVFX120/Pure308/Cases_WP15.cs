using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Pure308
{
    // SPEC-SPELL-120-308 WP-15 (D308-113): the guard break (낭), the blinding cloud (몽) and the defence cap.
    // The wiring (a cast reaching a real enemy) is checked in the editor (Spell120Checks308.WP15.cs); here only the rules.
    public static partial class Program
    {
        static partial void RunWP15(Report r, Context c)
        {
            if (c.Build == null || !c.Build.Ok) { r.Check("W15", false, "the table did not build"); return; }
            // grid cells (no glyph is named): 30 fire a + ieung, 66 earth o + ieung; their base glyphs 25 and 61
            SpellRow breaker = c.RowAt(30), blind = c.RowAt(66);
            float lockSeconds = breaker.F("guard.lock", 0f);
            float radius = blind.F("haze.radius", 0f), life = blind.F("haze.life", 0f), miss = blind.F("haze.miss", 0f), max = blind.F("haze.max", 0f);
            if (!r.Check("W15", breaker.Handler == "mod.guardbreak" && blind.Handler == "zone.blind",
                "the two WP-15 rows name their own handlers (they are no longer copies of their pair)", breaker.Handler + " / " + blind.Handler)) return;
            r.Check("W15", lockSeconds > 0f && radius > 0f && life > 0f && miss > 0f && miss <= 1f && max >= 1f,
                "their parameters are in the sheet (guard.lock; haze.radius, haze.life, haze.miss, haze.max)", lockSeconds + " / " + radius + " " + life + " " + miss + " " + max);
            r.Check("W15", c.RowAt(42).Handler == "zone.haze" && c.RowAt(29).Handler != breaker.Handler && c.RowAt(65).Handler != blind.Handler,
                "the pairs keep their own act: the steam haze stays zone.haze, the neighbours do not take the new handlers");
            r.Check("W15", breaker.Gate != SpellGateMode.Test && blind.Gate == SpellGateMode.Test,
                "the guard break is open (a plain hit where no enemy guards); the blinding cloud is TEST-unlock only until the searching field enemies exist", breaker.Gate + " / " + blind.Gate);
            r.Check("W15", breaker.BasePower == c.RowAt(25).BasePower && breaker.ProjectileSpeedMul == c.RowAt(25).ProjectileSpeedMul && breaker.Kind == SpellKind.AttackSingle,
                "the guard break is its base glyph's single shot (same power, same speed)");
            r.Check("W15", blind.AreaShape == c.RowAt(61).AreaShape && blind.AreaLength == c.RowAt(61).AreaLength && blind.BasePower == c.RowAt(61).BasePower,
                "the blinding cloud's first hit is its base glyph's own path (same shape, length and power)");

            // ---- the guard break ----
            {
                var stance = new EnemyGuardStance308();
                bool raised = stance.Raise(1f, 10f);
                bool broke = stance.Break(10f + lockSeconds);
                r.Check("낭-1", raised && broke && !stance.Raised && stance.Broken && r.Near(stance.BreakUntil, 10f + lockSeconds),
                    "a break drops a raised guard at once and remembers until when it stays down");
                r.Check("낭-2", !stance.Raise(1f, 10f + lockSeconds * .5f) && !stance.Raised && !stance.CanRaise(10f + lockSeconds - .01f),
                    "while broken the guard cannot be raised again (the enemy's brain asks Raise(share, now) and is refused)");
                float plain = stance.Intake(12f, 77);
                r.Check("낭-2", BitConverter.SingleToInt32Bits(plain) == BitConverter.SingleToInt32Bits(12f), "every hit during the break lands whole");
                stance.Break(10f + lockSeconds * 2f);
                r.Check("낭-2", r.Near(stance.BreakUntil, 10f + lockSeconds * 2f) && !stance.CanRaise(10f + lockSeconds + .01f),
                    "a second break only moves the time (the latest hit decides, nothing stacks)");
                r.Check("낭-2", stance.CanRaise(10f + lockSeconds * 2f) && !stance.Broken && stance.Raise(.5f, 10f + lockSeconds * 2f) && stance.Raised,
                    "from the break's time on the guard may be raised again and the break is forgotten");
                r.Check("낭-1", !stance.Break(float.NaN) && !stance.Break(float.PositiveInfinity) && stance.Raised,
                    "a break without a real end is refused and changes nothing");
                stance.Break(100f); stance.EndBreak();
                r.Check("낭-2", !stance.Broken && stance.CanRaise(50f), "the caster's clear (death, rest, leaving) ends the break at once");
                stance.Lower();
                r.Check("낭-1", stance.IsEmpty, "a lowered, unbroken guard with no entry is empty");
                var broken = new EnemyGuardStance308(); broken.Break(5f);
                r.Check("낭-1", !broken.IsEmpty, "a broken guard is not empty (the enemy keeps the lock with its life)");
                broken.Clear();
                r.Check("낭-1", broken.IsEmpty && !broken.Broken, "a new life starts without a break");
                // the break's own hit goes past the guard (registered when the hit is scheduled), like the unblockable thrust
                var guarded = new EnemyGuardStance308(); guarded.Raise(1f, 0f); guarded.LetThrough(31);
                float through = guarded.Intake(20f, 31), stopped = guarded.Intake(20f, 32);
                r.Check("낭-1", BitConverter.SingleToInt32Bits(through) == BitConverter.SingleToInt32Bits(20f) && stopped == 0f,
                    "the guard break's own hit lands whole on a raised guard; another hit is still stopped before the break lands");
                var untimed = new EnemyGuardStance308(); untimed.Break(10f);
                r.Check("낭-2", untimed.Raise(1f) && untimed.Raised,
                    "the clockless Raise of the callers before WP-15 does not see the break (kept for them; every WP-15 caller passes the clock)");
            }

            // ---- the blinding cloud ----
            {
                var veil = new EnemyStrikeVeil308();
                bool blinded = veil.Blind(66, 10f + life);
                r.Check("몽-2", blinded && veil.Blinded(11f) && veil.MissShare(11f) == 0f && !veil.Screened(11f),
                    "a blind entry makes the enemy lose the player; on its own it neither blurs nor screens");
                veil.Blur(66, 10f + life, miss);
                r.Check("몽-2", veil.Blinded(11f) && r.Near(veil.MissShare(11f), miss),
                    "the cloud hangs two entries on one owner: blind and the blur share (haze.miss); neither overwrites the other", veil.MissShare(11f).ToString("R"));
                int strikes = 20, missed = 0;
                for (int i = 0; i < strikes; i++) if (veil.Misses(i % 2 == 0, 11f)) missed++;
                r.Check("몽-2", missed == Mathf.FloorToInt(strikes * miss + 1e-4f), "of " + strikes + " strikes the stated share miss, melee and ranged alike (an enemy without a brain still misses)",
                    missed + " of " + strikes);
                veil.Remove(66);
                r.Check("몽-3", veil.IsEmpty && !veil.Blinded(11f), "an enemy that walks out of the cloud sees again at once (both entries go with their owner)");
                veil.Blind(66, 10f + life);
                r.Check("몽-3", !veil.Blinded(10f + life) && veil.IsEmpty, "the blindness ends with the cloud");
                var mixed = new EnemyStrikeVeil308();
                mixed.Blur(42, 50f, .25f); mixed.Blind(66, 50f);
                mixed.Remove(66);
                r.Check("몽-3", r.Near(mixed.MissShare(0f), .25f) && !mixed.Blinded(0f), "taking the cloud away leaves another owner's haze standing");
                r.Check("몽-2", !mixed.Blind(0, 50f) && !mixed.Blind(5, float.NaN), "a blind entry without an owner or an end is refused");
                var state = new EnemyModifierState308(); state.Veil.Blind(66, 50f); state.Stance.Break(50f);
                state.Clear();
                r.Check("몽-3", state.IsEmpty && !state.Veil.Blinded(0f) && !state.Stance.Broken, "a new life starts neither blind nor broken");
                Vector3 start = new Vector3(2f, 0f, -3f), direction = new Vector3(1f, .5f, 0f);
                Vector3 centre = HazeRule308.Centre(start, direction, blind.AreaLength);
                r.Check("몽-1", r.Near((centre - (start + Vector3.right * (blind.AreaLength * .5f))).magnitude, 0f),
                    "the cloud stands at the middle of the path, on its flat axis");
            }

            // ---- the defence cap ----
            {
                const float cap = .6f;
                r.Check("방어-1", r.Near(EnemyIntakeRule308.Defence(.9f, 0f, 0f, cap), cap) && r.Near(EnemyIntakeRule308.Defence(.4f, 0f, 0f, cap), .4f),
                    "a defence above the cap is cut to the cap; one below it is untouched");
                r.Check("방어-1", r.Near(EnemyIntakeRule308.Defence(.9f, 0f, 0f, 1f), .9f) && r.Near(EnemyIntakeRule308.Defence(.9f, 0f, 0f, float.NaN), .9f) &&
                    r.Near(EnemyIntakeRule308.Defence(.9f, 0f, 0f, 2f), .9f) && r.Near(EnemyIntakeRule308.Defence(.9f, 0f, 0f), .9f),
                    "a cap of one or more, or not a number, is no cap (the three-argument rule of the callers before WP-15 is uncapped)");
                r.Check("방어-1", r.Near(EnemyIntakeRule308.Defence(.9f, .2f, 0f, cap), .4f) && r.Near(EnemyIntakeRule308.Defence(.9f, 0f, .5f, cap), .3f) &&
                    EnemyIntakeRule308.Defence(.9f, .7f, 0f, cap) == 0f,
                    "the shred and the ignored share act on what the cap left (the cap first, then the shred, then the ignored share)");
                r.Check("방어-1", EnemyIntakeRule308.Defence(0f, 0f, 0f, cap) == 0f && EnemyIntakeRule308.Defence(.9f, 0f, 0f, 0f) == 0f,
                    "no defence stays no defence; a cap of zero leaves no defence at all");
                var heavy = new EnemyModifierState308();
                float capped = heavy.Intake(100f, .9f, 5, 0f, cap, true), uncapped = heavy.Intake(100f, .9f, 6, 0f);
                r.Check("방어-1", r.Near(capped, 100f * (1f - cap), 1e-3f) && r.Near(uncapped, 10f, 1e-3f),
                    "through the enemy's state: a heavy enemy takes at least (1 - cap) of a hit; the four-argument intake keeps the uncapped rule", capped.ToString("R"));
                var open = new EnemyModifierState308(); bool same = true;
                foreach (float damage in new[] { .001f, 1f, 7.3f, 250.5f })
                    same &= BitConverter.SingleToInt32Bits(open.Intake(damage, 0f, 7, 0f, cap, true)) == BitConverter.SingleToInt32Bits(damage);
                r.Check("방어-2", same && open.IsEmpty, "an enemy without defence takes every hit back bit for bit, cap or no cap");
                var guard = new EnemyModifierState308(); guard.Stance.Raise(1f, 0f);
                float spell = guard.Intake(10f, 0f, 8, 0f, cap, true), summon = guard.Intake(10f, 0f, 9, 0f, cap, false);
                r.Check("가드-1", spell == 0f && r.Near(summon, 10f),
                    "the guard acts on the caster's own spell shot only; a hit that is not guardable (summon, lasting damage, retaliation) goes past it");
            }
        }
    }
}