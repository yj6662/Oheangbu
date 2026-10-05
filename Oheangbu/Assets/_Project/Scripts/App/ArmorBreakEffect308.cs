using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 mod.armorbreak (earth single + siot: the target that was hit; fire cone + siot: everyone it swept).
    // The enemy's defence (enemy data, default 0) is lowered for a while, never below 0. On an enemy without defence the
    // hit is a plain hit: this is a counter to armour, not a second damage brand. Does not stack: the strongest counts.
    public sealed class ArmorBreakEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.armorbreak";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("break.defence", 0f, 1f), new SpellParamSpec("break.duration", 0f, 120f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration)
        { value = row.F("break.defence", 0f); duration = row.F("break.duration", 0f); }

        protected override void AtImpact(EnemyVitals target, float until, float value) => target.Modifiers.ShredDefence(until, value);
        protected override void AtClear(EnemyVitals target) => target.Modifiers.EndShred();
    }
}
