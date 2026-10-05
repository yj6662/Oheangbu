using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // single.unblockable (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): the metal single shot that cannot be stopped.
    // The judgement is the base glyph's single shot, unchanged. The hit of this cast ignores the target's guard stance
    // (EnemyGuardStance308: a guard, a ward or a reflect stance that would stop a share of the hit): the enemy is told
    // which attack id to let through when the hit is scheduled and uses the entry up when it takes that hit; an entry
    // whose hit never lands is taken back. An enemy that is not guarding takes the plain hit: nothing else changes.
    // It ignores the stance only, not the enemy's defence stat (that is the piercing glyphs' act).
    public sealed class UnblockableEffect308 : ModifierEffect308
    {
        public const string HandlerId = "single.unblockable";
        static readonly SpellParamSpec[] Specs = Array.Empty<SpellParamSpec>();
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration) { value = 1f; duration = 0f; }

        protected override void AtCast(EnemyVitals target, long attackId, float value) => target.Modifiers.Stance.LetThrough(attackId);
        protected override void AtDrop(EnemyVitals target, long attackId) => target.Modifiers.Stance.Forget(attackId);
    }
}
