using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 mod.pierce (wood single + ieung, fire single + ieung: the same act in two bodies).
    // The hit of this cast ignores the target's defence (pierce.defence = the share ignored, 1 = all of it). The enemy is
    // told which attack id to let through when the hit is scheduled and uses the entry up when it takes that hit; an entry
    // whose hit never lands is taken back. Nothing stays on the enemy afterwards, and a damage brand still applies.
    public sealed class DefencePierceEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.pierce";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("pierce.defence", 0f, 1f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration)
        { value = row.F("pierce.defence", 0f); duration = 0f; }

        protected override void AtCast(EnemyVitals target, long attackId, float value) => target.Modifiers.IgnoreDefence(attackId, value);
        protected override void AtDrop(EnemyVitals target, long attackId) => target.Modifiers.ForgetIgnore(attackId);
    }
}
