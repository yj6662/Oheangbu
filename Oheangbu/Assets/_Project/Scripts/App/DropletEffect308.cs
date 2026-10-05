using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // hit.droplets (SPEC-SPELL-120-308 WP-04): a single shot; its confirmed hit breaks into droplets, and each droplet
    // chases a different enemy near the point of impact (never the enemy that was hit). The droplets keep the element of
    // the cast. A shot that hits nothing derives nothing; with nobody near, the droplets are lost.
    // Presentation: the cast owns one flying body (ISpellPresenter). Cues: Split (the enemies the droplets chase), Hit (the
    // first impact), Secondary (each droplet landing, Index = droplet number).
    public sealed class DropletEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "hit.droplets";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("drop.count", 0f, 16f),      // droplets one hit breaks into
            new SpellParamSpec("drop.radius", 0f, 50f),     // how far from the point of impact a droplet finds an enemy
            new SpellParamSpec("drop.power", 0f, 5f),       // droplet power as a share of the shot's power
            new SpellParamSpec("drop.speed", 0.1f, 200f),   // droplet speed (metres per second)
        };

        readonly DerivedHitWatchList308 _watches = new DerivedHitWatchList308();
        readonly DerivedTimedQueue308<DerivedShot308> _queue = new DerivedTimedQueue308<DerivedShot308>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        readonly List<DerivedShot308> _due = new List<DerivedShot308>();
        readonly List<int> _scratch = new List<int>();
        SpellActorSnap[] _snaps;
        int _generation;
        bool _ticking;

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int Waiting => _watches.Count;
        public int Flying => _queue.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var shot = DerivedHits308.Primary(ctx);
            if (!shot.Launched) return false;
            // nobody aimed at: the usual missed single shot (the stroke adapter shows it), nothing to derive
            if (!shot.CanLand) return true;
            ctx.Host.PresentationOwned();
            _watches.Add(DerivedHits308.Watch(ctx, shot, DerivedHits308.BeginShot(ctx, shot.Hit, 0f)));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Target == null || ctx.Host == null || !_watches.TryTake(result.Attack.AttackId, out var watch)) return;
            var targets = ctx.Host.Targets; var actors = SpellSnapshots308.Fill(targets, ref _snaps);
            Vector3 point = result.Target.transform.position;
            _orders.Clear();
            DropletRule308.Plan(actors, SpellSnapshots308.IndexOf(targets, result.Target), point, Mathf.RoundToInt(watch.Row.F("drop.count", 0f)),
                watch.Row.F("drop.radius", 0f), watch.Row.F("drop.speed", 0f), ctx.Now, watch.Power * watch.Row.F("drop.power", 0f), watch.Element, _scratch, _orders);
            var chased = new Transform[_orders.Count];
            for (int i = 0; i < _orders.Count; i++)
            {
                var order = _orders[i]; var enemy = targets[order.TargetId];
                chased[i] = enemy.transform;
                _queue.Add(order.At, new DerivedShot308 { Target = enemy, Life = actors[order.TargetId].Life, Power = order.Power, Element = order.Element,
                    Letter = watch.Letter, From = point, Fx = watch.Fx, Part = i });
            }
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Split, new SpellFxCueArgs { Target = chased.Length > 0 ? chased[0] : null, Targets = chased,
                Point = point, At = ctx.Now, Value = chased.Length });
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Hit, new SpellFxCueArgs { Point = point, At = ctx.Now });
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
