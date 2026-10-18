using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // mod.guardbreak (SPEC-SPELL-120-308 WP-15, D308-113; 낭 = the fire single shot + ieung). The judgement is the base
    // glyph's single shot, unchanged. The hit of this cast goes past a raised guard (like the unblockable thrust: the enemy
    // is told which attack id to let through when the hit is scheduled), and when the wiring confirms the hit on a living
    // enemy, that enemy's guard drops and cannot be raised again for guard.lock seconds (EnemyGuardStance308.Break).
    // A second hit only moves that time (no stacking). No groggy, no weak point, no damage of its own beyond the plain hit.
    public sealed class GuardBreakEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.guardbreak";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("guard.lock", 0.1f, 60f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration) { value = 1f; duration = row.F("guard.lock", 0f); }

        protected override void AtCast(EnemyVitals target, long attackId, float value) => target.Modifiers.Stance.LetThrough(attackId);
        protected override void AtDrop(EnemyVitals target, long attackId) => target.Modifiers.Stance.Forget(attackId);
        protected override void AtImpact(EnemyVitals target, float until, float value) => target.Modifiers.Stance.Break(until);
        protected override void AtClear(EnemyVitals target) => target.Modifiers.Stance.EndBreak();
    }
}
