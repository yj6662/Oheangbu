using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.execute (SPEC-SPELL-120-308 WP-02): the single shot of its body (SpellPrimary308: the glyph without a final
    // decides how it flies, here a shot with a range); when it lands on a target whose HP ratio just
    // before the impact was at or below the threshold, one extra hit follows under the same attack identity
    // (ExecuteRule308). The same identity means the pair counts once for the five-element completion. No control, no
    // groggy, no reward: the extra hit is plain damage. The single-target shot is presented by the stroke adapter.
    public sealed class ExecuteEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "single.execute";
        const string ThresholdKey = "execute.hp01", BonusKey = "execute.bonus";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("execute.hp01", 0.01f, 1f),   // HP ratio at or below which the hit executes
            new SpellParamSpec("execute.bonus", 0f, 10f),    // extra hit as a multiple of the cast power
        };
        readonly ExecuteMarks308 _marks = new ExecuteMarks308();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingMarks => _marks.Count;   // read only, for checks

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var shot = SpellPrimary308.Plan(ctx, true);
            if (!shot.Launched) return false;
            // no target, or a target beyond the shot's reach: the ink is spent and there is nothing to execute
            if (shot.CanLand)
                _marks.Add(shot.Hit.AttackId, shot.LatestImpact, ctx.Cast.Power, ctx.Row.F(BonusKey, 0f), ctx.Row.F(ThresholdKey, 0f), ctx.Cast.Letter);
            return true;
        }

        // A planned hit of this handler just landed. The HP before it = the HP now + what it took.
        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            var target = result.Target;
            if (target == null || result.Attack.Source != DamageSource.PlayerDirect || !_marks.TryTake(result.Attack.AttackId, out var mark)) return;
            if (result.Killed || !target.IsAlive || ctx.Host == null || !result.Attack.Element.HasValue) return;
            float before = ExecuteRule308.Hp01Before(target.Hp, result.AppliedDamage, target.MaxHp);
            // an immediate hit does not pass the scheduler, so the equipment scale is multiplied here
            float power = ExecuteRule308.BonusPower(before, mark.Threshold01, mark.CastPower, mark.Bonus) * ctx.Host.DamageScale(result.Attack.Element.Value);
            if (!(power > 0f)) return;
            ctx.Host.ApplyDirectHit(target, target.LifeRevision, power, ctx.Host.PlayerPosition + Vector3.up * .4f, result.Attack, mark.Letter);
        }

        // Marks whose hit never landed (the target died or went out of sight first) are dropped.
        public void Tick(in SpellTickContext ctx) { _marks.Expire(ctx.Now); }
        public void Clear(SpellClearReason reason) { _marks.Clear(); }
    }
}
