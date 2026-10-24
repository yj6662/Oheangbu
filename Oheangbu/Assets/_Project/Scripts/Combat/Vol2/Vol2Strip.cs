using System;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.Combat
{
    // S5-b slice 6 (D308-149 D4): the 가른다 strip seam. "가드 · 갑주를 잠시 벗긴다" is the SAME armour state the old spells hang on an enemy
    // (EnemyModifierState308): the guard stance is broken for the length (Stance.Break: it drops now and cannot be raised again before
    // the end) and the armour is lowered by its whole share (ShredDefence 1: defence minus 1 never stays positive). EnemyVitals.
    // TakeResolvedVol2Damage already reads both through Modifiers.Intake, once, so no hit of the vol.2 damage entry needs to know about
    // a strip. Nothing here touches HP, groggy, the weak point or the 5-element mask, and the strip is NOT damage: its 0.4 is a value
    // conversion the cast's budget books once (EffectBudgetV3, EffectKind.Strip).
    // The time base of the modifiers is Time.time (float seconds), like every other modifier; the caller passes it in so this file is
    // plain C# over the modifier state (an edit-mode fixture drives it with its own clock).

    public readonly struct Vol2StripResult
    {
        public readonly bool Applied;
        public readonly bool GuardBroken, ArmourStripped;
        public readonly float UntilSeconds;
        public readonly string Reason;
        public Vol2StripResult(bool applied, bool guardBroken, bool armourStripped, float until, string reason)
        { Applied = applied; GuardBroken = guardBroken; ArmourStripped = armourStripped; UntilSeconds = until; Reason = reason ?? ""; }
        public override string ToString() => Applied ? "strip ok guard=" + (GuardBroken ? "broken" : "?") + " armour=" + (ArmourStripped ? "off" : "?") + " until " + UntilSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "strip refused " + Reason;
    }

    public static class Vol2Strip
    {
        /// <summary>The whole armour share (TE-S6-5: the data gives the strip's LENGTH, not a share, so "벗긴다" is read as all of it).</summary>
        public const float FullShare = 1f;

        /// <summary>Breaks the guard and strips the armour of <paramref name="v"/> from <paramref name="now"/> for <paramref name="seconds"/>. A strip never
        /// shortens one that is already running (a 숫 strip of 4.5 s is not cut to 3 s by an ordinary one): the later end wins.</summary>
        public static Vol2StripResult Apply(EnemyVitals v, float seconds, float now)
        {
            if (v == null) return new Vol2StripResult(false, false, false, 0f, "NoTarget");
            if (!v.IsAlive) return new Vol2StripResult(false, false, false, 0f, "Dead");
            if (!(seconds > 0f) || float.IsInfinity(seconds) || float.IsNaN(now)) return new Vol2StripResult(false, false, false, 0f, "BadLength");
            float until = now + seconds;
            var stance = v.Modifiers.Stance;
            float guardUntil = stance.Broken && stance.BreakUntil > until ? stance.BreakUntil : until;
            stance.Break(guardUntil);
            v.Modifiers.ShredDefence(until, FullShare);
            bool guard = !stance.CanRaise(now), armour = v.Modifiers.DefenceShred(now) >= FullShare - 1e-4f;
            return new Vol2StripResult(guard && armour, guard, armour, until, guard && armour ? "" : "NotHeld");
        }

        /// <summary>Is the guard broken and the armour stripped at <paramref name="now"/>?</summary>
        public static bool IsStripped(EnemyVitals v, float now)
            => v != null && v.IsAlive && !v.Modifiers.Stance.CanRaise(now) && v.Modifiers.DefenceShred(now) >= FullShare - 1e-4f;

        /// <summary>Does <paramref name="body"/> overcome the element this enemy's attacks carry? (The 날붓 asks it; same test as the damage entry's K.)
        /// false when the enemy carries no element.</summary>
        public static bool CounterOf(EnemyVitals v, Body body)
        {
            if (v == null || !v.TryAttackElement(out var element)) return false;
            return ReactionRulesV3.Beats(body, Vol2Mapping.ToBody(element));
        }
    }
}
