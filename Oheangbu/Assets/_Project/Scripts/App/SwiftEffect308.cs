using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 metal buff with the nieun final: attack casts fly faster while it runs (projectile speed multiplier of the
    // cast); the power is not changed. The cast hook can only reach SpellCast.SpeedMul, which today drives the single
    // projectile and the giyeok trace flight time; shapes with their own speed in the area data keep it.
    public sealed class SwiftEffect308 : SelfBuffEffect308, ISpellCastHook
    {
        public const string HandlerId = "buff.swift";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("speed.mul", 1f, 5f) };
        float _mul = 1f;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _mul = ctx.Row.F("speed.mul", 1f);
            return true;
        }

        public void ModifyCast(ref SpellCastDraft draft, float now)
        {
            BuffRule308.Swift(Active(now), draft.Source.Kind, _mul, ref draft.Power, ref draft.SpeedMul);
        }

        protected override void OnCleared(SpellClearReason reason) { _mul = 1f; }
    }
}
