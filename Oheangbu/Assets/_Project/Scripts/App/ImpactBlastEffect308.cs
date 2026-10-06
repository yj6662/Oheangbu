using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // single.blast (SPEC-SPELL-120-308 WP-03): the base glyph's single shot, and a small burst where it lands.
    //  - primary = the single shot of its body (SpellPrimary308: the glyph without a final decides how it flies, here an
    //    unguided fall on the spot the enemy stood on), presented by the stroke adapter like the base glyph;
    //  - second stage = at the moment the primary really lands (the hit hook hears the primary's own attack id), a circle
    //    of blast.radius around the point of impact (the spot the shot came down on; for a guided shot, where its target
    //    stands then), with the cast power x blast.power, on everyone but the target.
    // A shot that has no target, that misses, or whose target is gone before it lands, bursts nothing (Spec section 15 default).
    public sealed class ImpactBlastEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "single.blast";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("blast.radius", .1f, 20f),
            new SpellParamSpec("blast.power", 0f, 5f),
        };

        sealed class Stage
        {
            public TwoStageCast308 Cast;
            public long PrimaryAttack;
            public int PrimaryId;
            public float PrimaryAt, Power, Radius;
            public bool HasPoint;                   // the primary comes down on a fixed spot (an unguided shot)
            public Vector3 Point;
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
            if (!shot.CanLand) return true;                 // nothing aimed at: the ink is spent, nothing lands, nothing bursts
            var first = shot.Hit;
            _stages.Add(new Stage
            {
                Cast = seen, PrimaryAttack = first.AttackId, PrimaryId = seen.IndexOf(first.Target), PrimaryAt = shot.LatestImpact,
                Power = ctx.Cast.Power * ctx.Row.F("blast.power", 0f), Radius = ctx.Row.F("blast.radius", 0f),
                HasPoint = first.HasImpactPoint, Point = first.ImpactPoint,
            });
            return true;
        }

        // The primary of one of this effect's casts landed: burst now, around where its target stands now.
        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            int at = Find(result.Attack.AttackId);
            if (at < 0) return;                             // not a primary of this effect (its own burst hits come through here too)
            var stage = _stages[at];
            _stages.RemoveAt(at);
            if (stage.PrimaryId < 0) return;
            var now = stage.Cast.Now();
            Vector3 point = stage.HasPoint ? stage.Point : result.Target != null ? result.Target.transform.position : stage.Cast.AtCast[stage.PrimaryId].Position;
            _orders.Clear();
            ImpactBlastRule308.Plan(stage.Cast.AtCast, now, stage.PrimaryId, point, ctx.Now, stage.Power, stage.Radius, stage.Cast.Element, _orders);
            stage.Cast.Apply(ctx.Host, _orders);
        }

        // A primary that did not land (its target died, changed life or went out of sight first) leaves its record behind:
        // one frame after its impact time the record is dropped, so nothing can burst later.
        public void Tick(in SpellTickContext ctx)
        {
            float judged = _clock.Advance(ctx.Now);
            for (int i = _stages.Count - 1; i >= 0; i--)
                if (TwoStageRule308.Due(_stages[i].PrimaryAt, judged)) _stages.RemoveAt(i);
        }

        public void Clear(SpellClearReason reason)
        {
            _stages.Clear(); _orders.Clear(); _clock.Reset();
        }

        int Find(long attackId)
        {
            if (attackId > 0)
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].PrimaryAttack == attackId) return i;
            return -1;
        }
    }
}
