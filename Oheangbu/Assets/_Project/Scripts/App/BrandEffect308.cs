using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 mod.brand (metal single + nieun: the fastest mark, short; earth path + nieun: everyone the storm swept).
    // The enemy a hit of this cast really landed on takes more damage from every source for a while. The marking hit itself
    // is not raised (the brand starts when it lands). Brands do not stack: the strongest running one counts.
    public sealed class BrandEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.brand";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("brand.scale", 1f, 5f), new SpellParamSpec("brand.duration", 0f, 120f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration)
        { value = row.F("brand.scale", 1f); duration = row.F("brand.duration", 0f); }

        protected override void AtImpact(EnemyVitals target, float until, float value) => target.Modifiers.Brand(until, value);
        protected override void AtClear(EnemyVitals target) => target.Modifiers.EndBrand();
    }
}
