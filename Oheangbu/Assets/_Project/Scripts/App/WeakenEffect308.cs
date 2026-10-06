using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 mod.weaken (water path + siot): every enemy the wave swept strikes weaker for a while.
    // Every strike inside the window is scaled (nothing is used up); it does not stack: the strongest running one counts.
    // Acts at the enemy -> player doorway (EnemyStrikeModifiers308). It only lowers what the player takes from that enemy.
    public sealed class WeakenEffect308 : ModifierEffect308
    {
        public const string HandlerId = "mod.weaken";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("weaken.scale", 0f, 1f), new SpellParamSpec("weaken.duration", 0f, 120f) };
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Read(SpellRow row, out float value, out float duration)
        { value = row.F("weaken.scale", 1f); duration = row.F("weaken.duration", 0f); }

        protected override void AtImpact(EnemyVitals target, float until, float value) => target.Modifiers.Weaken(until, value);
        protected override void AtClear(EnemyVitals target) => target.Modifiers.EndWeaken();
    }
}
