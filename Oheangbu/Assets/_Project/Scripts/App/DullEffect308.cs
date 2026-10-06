using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 mod.dull (earth path + siot): the next attack of every enemy the storm swept is dulled, once.
    // One charge per enemy with a time limit: casting again refreshes it, it never adds a second one. The charge is used up
    // by the enemy's next strike that reaches the enemy -> player doorway (EnemyStrikeModifiers308). It only lowers what the
    // player takes from that enemy; the player gains nothing from being hit.
    public sealed class DullEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.dull";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("dull.scale", 0f, 1f), new SpellParamSpec("dull.duration", 0f, 120f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration)
        { value = row.F("dull.scale", 1f); duration = row.F("dull.duration", 0f); }

        protected override void AtImpact(EnemyVitals target, float until, float value) => target.Modifiers.Dull(until, value);
        protected override void AtClear(EnemyVitals target) => target.Modifiers.EndDull();
    }
}
