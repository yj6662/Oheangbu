using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // WP-08 fire buff with the nieun final: the next attack cast, once, is amplified (EmpowerState308). The multiplier is
    // applied by the cast hook before dispatch; the single charge is used when that cast lands (D308-123, TEST), so a cast
    // that fails for ink or lands nothing keeps the buff. Parry, summon, ward, field, buff and install casts neither take nor
    // use it.
    public sealed class EmpowerEffect308 : SelfBuffEffect308, ISpellCastHook, ISpellAcceptHook, ISpellHitHook
    {
        public const string HandlerId = "buff.empower";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("empower.mul", 1f, 10f) };
        readonly EmpowerState308 _state = new EmpowerState308();
        float _mul = 1f;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override int Charges => 1;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _mul = ctx.Row.F("empower.mul", 1f);
            _state.Clear();
            return true;
        }

        public void ModifyCast(ref SpellCastDraft draft, float now)
        {
            _state.Modify(Buffs, Letter, draft.Source.Letter, draft.Source.Kind, _mul, now, ref draft.Power, ref draft.SpeedMul);
        }

        public void OnCastAccepted(in SpellCastContext ctx)
        {
            if (_state.Accept(Buffs, Letter, ctx.Cast.Letter, ctx.Cast.Kind, ctx.Attacks, ctx.Now)) Finish(ctx.Fx, SpellFxCue.Consume);
        }

        // A companion buff's extra shot is handed out inside the same dispatch, but it is not the amplified cast.
        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Attack.Source == DamageSource.Companion) return;
            if (_state.Hit(Buffs, Letter, result.Attack.AttackId, ctx.Now)) Finish(ctx.Fx, SpellFxCue.Consume);
        }

        protected override void OnCleared(SpellClearReason reason) { _state.Clear(); _mul = 1f; }
    }
}
