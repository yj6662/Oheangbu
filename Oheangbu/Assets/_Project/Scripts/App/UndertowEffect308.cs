using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-05, handler path.undertow (SPEC-SPELL-120-308 section 13): the sinking flood.
    // The strike is today's corridor judgement of the base glyph (the advancing front; the stroke adapter presents it).
    // When the front's hit is confirmed on an enemy the rule named, that enemy is slowed for the row's duration and comes
    // back to its own speed afterwards. An enemy that is in the air at that moment (ISpellAirborne308) is also pulled to
    // the ground for the same time (question Q5: built against a TEST target; no enemy of the game flies yet).
    // A slow only: actions are never blocked, no weak point, no groggy.
    public sealed class UndertowEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "path.undertow";
        const string SpeedKey = "slow.speed", DurationKey = "slow.duration";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("slow.speed", 0f, 1f),
            new SpellParamSpec("slow.duration", 0.05f, 60f),
        };

        readonly ControlHitWatch308<UndertowRule308.Spec> _watch = new ControlHitWatch308<UndertowRule308.Spec>();
        readonly SpellControlLedger308 _ledger = new SpellControlLedger308();
        readonly List<UndertowRule308.Order> _orders = new List<UndertowRule308.Order>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int WaitingHits => _watch.Count;        // read only (checks)
        public int HeldControls => _ledger.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var plan = ctx.Host.PlanPath(ctx.Cast, true);
            if (plan == null) return false;
            if (plan.Area == null || plan.Hits.Count == 0) return true;      // nobody in the corridor: the ink is spent, nothing is swept
            var row = ctx.Row; var area = plan.Area; var targets = ctx.Host.Targets;
            _orders.Clear();
            UndertowRule308.Plan(SpellSnapshots308.Take(targets), area.Point, area.Direction, area.Radius, area.Length, area.Speed, ctx.Now, area.Delay, _orders);
            var spec = new UndertowRule308.Spec(row.F(DurationKey, 0f), row.F(SpeedKey, 1f));
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
            var flyer = target.GetComponent<ISpellAirborne308>();
            bool airborne = flyer != null && flyer.IsAirborne(ctx.Now);
            if (!UndertowRule308.Sweep(airborne, result.Killed || !target.IsAlive, ctx.Now, spec, out float until, out float speed, out bool pullDown)) return;
            _ledger.Apply(target, result.Attack.AttackId, until, speed, false, pullDown);
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
