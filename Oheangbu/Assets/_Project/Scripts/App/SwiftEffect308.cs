using System.Collections.Generic;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 metal buff with the nieun final: the player's projectiles fly faster while it runs; the power is not changed.
    // D308-123: every real player projectile. The cast hook multiplies SpellCast.SpeedMul of attack casts (single shots,
    // the giyeok trace, the volley); shots whose speed is their own row number (bounces, droplets, companion shots) read
    // the shared timers' SpeedScale at launch. Install marks, summons and the cast interval are not projectiles of it.
    public sealed class SwiftEffect308 : SelfBuffEffect308, ISpellCastHook
    {
        public const string HandlerId = "buff.swift";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("speed.mul", 1f, 5f) };
        float _mul = 1f;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override float SpeedScale => _mul;

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
