using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-05, handler circle.interrupt (SPEC-SPELL-120-308 section 13): the wide, mitigated interrupt.
    // The strike is today's circle judgement of the base glyph with the row's reduced power (the stroke adapter presents it).
    // When one of those hits is confirmed on an enemy the rule named, that enemy is controlled for the row's short duration:
    // an ordinary enemy cannot act or move and its attack in progress is cancelled (telegraph included); a boss is only
    // slowed (giyeok precedent, Q8 default). Control only: no weak point, no damage bonus, no groggy.
    public sealed class InterruptEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "circle.interrupt";
        const string PowerKey = "power.scale", DurationKey = "interrupt.duration", BossKey = "boss.speed";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("power.scale", 0.01f, 5f),
            new SpellParamSpec("interrupt.duration", 0.05f, 30f),
            new SpellParamSpec("boss.speed", 0f, 1f),
        };

        readonly ControlHitWatch308<InterruptRule308.Spec> _watch = new ControlHitWatch308<InterruptRule308.Spec>();
        readonly SpellControlLedger308 _ledger = new SpellControlLedger308();
        readonly List<InterruptRule308.Order> _orders = new List<InterruptRule308.Order>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int WaitingHits => _watch.Count;        // read only (checks)
        public int HeldControls => _ledger.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var cast = ctx.Cast; var row = ctx.Row;
            var strike = new SpellCast(cast.Letter, cast.Kind, cast.Element, InterruptRule308.Power(cast.Power, row.F(PowerKey, 1f)),
                cast.Area, cast.SpeedMul, cast.HoldScale, cast.Brush, cast.Brush01);
            var plan = ctx.Host.PlanCircle(strike, true);
            if (plan == null) return false;
            if (plan.Area == null || plan.Hits.Count == 0) return true;      // nobody inside: the ink is spent, nothing to interrupt
            var targets = ctx.Host.Targets;
            _orders.Clear();
            InterruptRule308.Plan(SpellSnapshots308.Take(targets), plan.Area.Point, plan.Area.Radius, ctx.Now + plan.Area.Delay, _orders);
            var spec = new InterruptRule308.Spec(row.F(DurationKey, 0f), row.F(BossKey, 1f));
            for (int h = 0; h < plan.Hits.Count; h++)
            {
                var hit = plan.Hits[h];
                if (hit.Target == null) continue;
                int id = SpellSnapshots308.IndexOf(targets, hit.Target);
                for (int i = 0; i < _orders.Count; i++)
                {
                    if (_orders[i].TargetId != id || _orders[i].Life != hit.Target.LifeRevision) continue;
                    _watch.Add(hit.AttackId, hit.ImpactTime, spec);
                    break;
                }
            }
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (_watch.Count == 0 || !_watch.TryTake(result.Attack.AttackId, out var spec)) return;
            var target = result.Target;
            if (target == null) return;
            if (!InterruptRule308.Control(target.IsBoss, result.Killed || !target.IsAlive, ctx.Now, spec, out float until, out float speed, out bool blocks)) return;
            _ledger.Apply(target, result.Attack.AttackId, until, speed, blocks);
        }

        public void Tick(in SpellTickContext ctx)
        {
            _watch.Sweep(ctx.Now);
            _ledger.Expire(ctx.Now);
        }

        public void Clear(SpellClearReason reason)
        {
            _watch.Clear();
            _ledger.Clear();
            _orders.Clear();
        }
    }
}
