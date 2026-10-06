using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // volley.ricochet (SPEC-SPELL-120-308 WP-04): the volley of its base row; every shot that really hit bounces to another
    // enemy near the one it hit. A shot never returns to an enemy it has touched, bounces a limited number of times, and
    // loses power with every bounce. Shots that hit nothing do not bounce.
    // Presentation: the cast owns the volley (ISpellPresenter, with the judgement plan). Cues: Split (a bounce leaves, Target
    // = the enemy it flies to, Index = the shot), Secondary (a bounce landed).
    public sealed class RicochetEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "volley.ricochet";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("bounce.count", 0f, 8f),     // bounces one shot may make
            new SpellParamSpec("bounce.range", 0f, 50f),    // how far from the enemy it hit a shot finds the next one
            new SpellParamSpec("bounce.power", 0f, 5f),     // power kept per bounce (share of the shot's power)
            new SpellParamSpec("bounce.speed", 0.1f, 200f), // speed of a bounced shot (metres per second)
        };

        readonly DerivedHitWatchList308 _watches = new DerivedHitWatchList308();
        readonly DerivedTimedQueue308<DerivedShot308> _queue = new DerivedTimedQueue308<DerivedShot308>();
        readonly List<DerivedShot308> _due = new List<DerivedShot308>();
        readonly List<int> _visited = new List<int>();
        readonly List<int> _scratch = new List<int>();
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
            var plan = ctx.Host.PlanVolley(ctx.Cast, false);
            if (plan == null) return false;
            ctx.Host.PresentationOwned();
            // the volley's own presentation follows the judgement plan; like the stroke adapter it leads with the first
            // candidate only when the shots are dealt to candidates (a scattered fan has no lead target)
            var lead = !ctx.Cast.Area.ScatterVolley && plan.Hits.Count > 0 ? plan.Hits[0] : null;
            var fx = DerivedHits308.BeginShot(ctx, lead, 0f, plan.Area);
            // every scheduled shot is watched; the ones that never land (target gone, wall) are swept away
            for (int i = 0; i < plan.Hits.Count; i++) _watches.Add(DerivedHits308.Watch(ctx, plan.Hits[i], i, fx));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Target == null || ctx.Host == null || !_watches.TryTake(result.Attack.AttackId, out var watch)) return;
            Launch(ctx, watch.Row, result.Target, new[] { result.Target }, watch.Shot, 1, watch.Power, watch.Element, watch.Letter, watch.Fx);
        }

        // The shot just hit `from` (its depth-1 th bounce, or its first hit when depth is 1): queue the next bounce if there is one.
        void Launch(in SpellTickContext ctx, SpellRow row, EnemyVitals from, EnemyVitals[] visited, int shot, int depth, float shotPower,
            Element element, char letter, int fx)
        {
            var targets = ctx.Host.Targets; var actors = SpellSnapshots308.Fill(targets, ref _snaps);
            DerivedHits308.Ids(targets, visited, _visited);
            Vector3 point = from.transform.position;
            if (!RicochetRule308.Bounce(actors, point, _visited, shot, depth, Mathf.RoundToInt(row.F("bounce.count", 0f)), row.F("bounce.range", 0f),
                row.F("bounce.speed", 0f), ctx.Now, shotPower, row.F("bounce.power", 0f), element, _scratch, out SpellHitOrder order)) return;
            var next = targets[order.TargetId];
            var touched = new EnemyVitals[visited.Length + 1];
            visited.CopyTo(touched, 0); touched[visited.Length] = next;
            _queue.Add(order.At, new DerivedShot308 { Target = next, Life = actors[order.TargetId].Life, Power = order.Power, Element = order.Element,
                Letter = letter, From = point, Fx = fx, Part = shot, Row = row, Depth = depth, ShotPower = shotPower, Visited = touched });
            DerivedHits308.Cue(ctx.Fx, fx, SpellFxCue.Split, new SpellFxCueArgs { Target = next.transform, Targets = new[] { next.transform },
                Point = point, At = order.At, Index = shot });
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
                    if (hit.AppliedDamage <= 0f || hit.Target == null || generation != _generation) continue;
                    DerivedHits308.Cue(ctx.Fx, shot.Fx, SpellFxCue.Secondary, new SpellFxCueArgs { Target = hit.Target.transform,
                        Point = hit.Target.transform.position, At = ctx.Now, Index = shot.Part });
                    Launch(ctx, shot.Row, hit.Target, shot.Visited, shot.Part, shot.Depth + 1, shot.ShotPower, shot.Element, shot.Letter, shot.Fx);
                }
            }
            finally { _ticking = false; _due.Clear(); }
        }

        public void Clear(SpellClearReason reason)
        {
            _generation++;
            _watches.Clear(); _queue.Clear(); _due.Clear();
        }
    }
}
