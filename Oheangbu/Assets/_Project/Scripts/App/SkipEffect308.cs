using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.skip (SPEC-SPELL-120-308 WP-03): the base glyph's single shot, which skips once more after it lands.
    //  - primary = the single shot of its body (SpellPrimary308: the glyph without a final decides how it flies, here an
    //    unguided fall on the spot the enemy stood on), presented by the stroke adapter like the base glyph;
    //  - second stage = when the primary really lands (the hit hook hears the primary's own attack id) the second point is
    //    fixed: skip.distance further along the direction the shot travelled. skip.delay seconds later everyone inside
    //    skip.radius of that point is hit once with the cast power x skip.power.
    // A shot that has no target, or whose target is gone before it lands, does not skip (the reading of the canonical
    // text taken here, by analogy with the burst glyph; reported as a question).
    public sealed class SkipEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "single.skip";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("skip.distance", 0f, 50f),
            new SpellParamSpec("skip.delay", 0f, 10f),
            new SpellParamSpec("skip.radius", .1f, 20f),
            new SpellParamSpec("skip.power", 0f, 5f),
        };

        sealed class Stage
        {
            public TwoStageCast308 Cast;
            public long PrimaryAttack;
            public float PrimaryAt, CastPower, Distance, Delay, Radius, PowerScale;
            public bool Landed;
            public bool HasPoint;                   // the primary comes down on a fixed spot (an unguided shot)
            public Vector3 Point;
            public SkipPlan308 Plan;
        }

        readonly List<Stage> _stages = new List<Stage>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        readonly TwoStageClock308 _clock = new TwoStageClock308();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingStages => _stages.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var seen = TwoStageCast308.Take(ctx);
            var shot = SpellPrimary308.Plan(ctx, true);
            if (!shot.Launched) return false;
            if (!shot.CanLand) return true;                 // nothing aimed at: the ink is spent, nothing lands, nothing skips
            var first = shot.Hit;
            _stages.Add(new Stage
            {
                Cast = seen, PrimaryAttack = first.AttackId, PrimaryAt = shot.LatestImpact, CastPower = ctx.Cast.Power,
                Distance = ctx.Row.F("skip.distance", 0f), Delay = ctx.Row.F("skip.delay", 0f),
                Radius = ctx.Row.F("skip.radius", 0f), PowerScale = ctx.Row.F("skip.power", 0f),
                HasPoint = first.HasImpactPoint, Point = first.ImpactPoint,
            });
            return true;
        }

        // The primary of one of this effect's casts landed: fix the second point and its time. Nothing is judged here.
        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            long attackId = result.Attack.AttackId;
            if (attackId <= 0 || result.Target == null) return;
            for (int i = 0; i < _stages.Count; i++)
            {
                var stage = _stages[i];
                if (stage.Landed || stage.PrimaryAttack != attackId) continue;
                stage.Landed = true;
                stage.Plan = SkipRule308.Plan(stage.Cast.CasterPosition, stage.Cast.CasterForward, stage.HasPoint ? stage.Point : result.Target.transform.position, ctx.Now,
                    stage.CastPower, stage.Distance, stage.Delay, stage.Radius, stage.PowerScale, stage.Cast.Element);
                return;
            }
        }

        public void Tick(in SpellTickContext ctx)
        {
            float judged = _clock.Advance(ctx.Now);
            for (int i = _stages.Count - 1; i >= 0; i--)
            {
                if (i >= _stages.Count) continue;
                var stage = _stages[i];
                if (!stage.Landed)
                {
                    // the primary did not land (its target died, changed life or went out of sight first): no second point
                    if (TwoStageRule308.Due(stage.PrimaryAt, judged)) _stages.RemoveAt(i);
                    continue;
                }
                if (!TwoStageRule308.Due(stage.Plan.At, judged)) continue;
                _stages.RemoveAt(i);
                _orders.Clear();
                SkipRule308.Resolve(stage.Plan, stage.Cast.AtCast, stage.Cast.Now(), _orders);
                stage.Cast.Apply(ctx.Host, _orders);
            }
        }

        public void Clear(SpellClearReason reason)
        {
            _stages.Clear(); _orders.Clear(); _clock.Reset();
        }
    }
}
