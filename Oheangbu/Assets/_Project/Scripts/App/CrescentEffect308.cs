using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // hit.crescent (SPEC-SPELL-120-308 WP-04): a single shot; after its confirmed hit two crescents leave the point of
    // impact, mirrored about the shot's direction, and each hits the enemies inside its own cone once. The crescents keep
    // the element of the cast. A shot that hits nothing derives nothing.
    // Presentation: the cast owns one flying body (ISpellPresenter). Cues: Split (the enemies the crescents will reach,
    // Normal = the shot's direction, Value = the half spread in degrees), Hit (the first impact), Secondary (each crescent hit,
    // Index 0 = left, 1 = right).
    public sealed class CrescentEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "hit.crescent";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("split.angle", 0f, 90f),     // degrees between the shot's direction and each crescent
            new SpellParamSpec("split.half", 0f, 90f),      // half angle of one crescent's cone
            new SpellParamSpec("split.range", 0f, 50f),     // reach of a crescent from the point of impact
            new SpellParamSpec("split.power", 0f, 5f),      // crescent power as a share of the shot's power
            new SpellParamSpec("split.delay", 0f, 10f),     // seconds from the hit to the crescents' judgement
        };

        readonly DerivedHitWatchList308 _watches = new DerivedHitWatchList308();
        readonly DerivedTimedQueue308<DerivedShot308> _queue = new DerivedTimedQueue308<DerivedShot308>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        readonly List<DerivedShot308> _due = new List<DerivedShot308>();
        int _generation;
        bool _ticking;

        SpellActorSnap[] _snaps;

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int Waiting => _watches.Count;
        public int Flying => _queue.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            // nobody aimed at: the usual missed single shot (the stroke adapter shows it), nothing to derive
            if (ctx.Host.AimedTarget() == null) return ctx.Host.PlanSingle(ctx.Cast, true) != null;
            var plan = ctx.Host.PlanSingle(ctx.Cast, false);
            if (plan == null) return false;
            ctx.Host.PresentationOwned();
            if (plan.Hits.Count == 0) return true;
            var hit = plan.Hits[0];
            _watches.Add(DerivedHits308.Watch(ctx, hit, 0, DerivedHits308.BeginShot(ctx, hit, 0f)));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Target == null || ctx.Host == null || !_watches.TryTake(result.Attack.AttackId, out var watch)) return;
            var targets = ctx.Host.Targets; var actors = SpellSnapshots308.Fill(targets, ref _snaps);
            Vector3 apex = result.Target.transform.position;
            Vector3 travel = AreaGeometry.Flat(apex - watch.Origin);
            if (travel.sqrMagnitude < .0001f) travel = ctx.Host.PlayerForward;
            float angle = watch.Row.F("split.angle", 0f);
            float at = ctx.Now + watch.Row.F("split.delay", 0f);
            _orders.Clear();
            int left = CrescentRule308.Plan(actors, SpellSnapshots308.IndexOf(targets, result.Target), apex, travel, angle,
                watch.Row.F("split.half", 0f), watch.Row.F("split.range", 0f), at, watch.Power * watch.Row.F("split.power", 0f), watch.Element, _orders);
            var reached = new Transform[_orders.Count];
            for (int i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i]; var enemy = targets[order.TargetId];
                reached[i] = enemy.transform;
                _queue.Add(order.At, new DerivedShot308 { Target = enemy, Life = actors[order.TargetId].Life, Power = order.Power, Element = order.Element,
                    Letter = watch.Letter, From = apex, Fx = watch.Fx, Part = i < left ? 0 : 1 });
            }
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Split, new SpellFxCueArgs { Target = reached.Length > 0 ? reached[0] : null, Targets = reached,
                Point = apex, Normal = travel.normalized, At = at, Value = angle });
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Hit, new SpellFxCueArgs { Point = apex, At = ctx.Now });
        }

        public void Tick(in SpellTickContext ctx)
        {
            if (_ticking) return;
            _watches.Sweep(ctx.Now);
            if (_queue.Count == 0) return;
            _ticking = true;
            try
            {
                _due.Clear(); _queue.TakeDue(ctx.Now, _due);
                int generation = _generation;
                // a damage callback may clear this effect: stop as soon as that happened
                for (int i = 0; i < _due.Count && generation == _generation; i++)
                {
                    var shot = _due[i];
                    var hit = DerivedHits308.Apply(ctx, shot);
                    if (hit.AppliedDamage > 0f && hit.Target != null)
                        DerivedHits308.Cue(ctx.Fx, shot.Fx, SpellFxCue.Secondary, new SpellFxCueArgs { Target = hit.Target.transform,
                            Point = hit.Target.transform.position, At = ctx.Now, Index = shot.Part });
                }
            }
            finally { _ticking = false; _due.Clear(); }
        }

        public void Clear(SpellClearReason reason)
        {
            _generation++;
            _watches.Clear(); _queue.Clear(); _orders.Clear(); _due.Clear();
        }
    }
}
