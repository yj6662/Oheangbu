using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // hit.spread (SPEC-SPELL-120-308 WP-04): a single shot; its confirmed hit leaves an ember in the target. The ember does
    // nothing while it sits. When its carrier falls, or when its time is up, it moves to the nearest other enemy in reach and
    // deals the damage of the hit that planted it; it may move a limited number of times. A shot that hits nothing leaves
    // no ember; an ember with nobody in reach goes out.
    // Presentation: the cast owns one body that stays on the carrier (ISpellPresenter). Cues: Hit (the ember is planted),
    // TargetDefeated / Expire (the ember leaves; Target = the enemy it moves to, or none), Secondary (the move's damage landed).
    public sealed class EmberSpreadEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "hit.spread";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("spread.timer", 0.1f, 120f), // seconds the ember sits in a standing carrier
            new SpellParamSpec("spread.radius", 0f, 50f),   // how far from its carrier the ember finds the next enemy
            new SpellParamSpec("spread.max", 0f, 8f),       // moves one ember may make
        };

        // One planted ember: the pure state plus who carries it.
        sealed class Ember
        {
            public EmberSpread308 State;
            public EnemyVitals Carrier;
            public SpellRow Row;
            public Element Element;
            public char Letter;
            public int Fx;
        }

        readonly DerivedHitWatchList308 _watches = new DerivedHitWatchList308();
        readonly List<Ember> _embers = new List<Ember>();
        readonly List<Ember> _frame = new List<Ember>();
        readonly List<int> _scratch = new List<int>();
        SpellActorSnap[] _snaps;                 // one buffer for the per-frame look at the targets (no garbage while an ember sits)
        int _generation;
        bool _ticking;

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int Waiting => _watches.Count;
        public int Embers => _embers.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var shot = DerivedHits308.Primary(ctx);
            if (!shot.Launched) return false;
            // nobody aimed at, or a target beyond the shot's reach: the usual missed single shot (the stroke adapter shows it), no ember
            if (!shot.CanLand) return true;
            ctx.Host.PresentationOwned();
            var hit = shot.Hit;
            // the presentation must outlive the ember: the flight plus one sitting time per move
            float life = Mathf.Max(0f, hit.ImpactTime - ctx.Now) + ctx.Row.F("spread.timer", 0f) * Mathf.Max(1f, ctx.Row.F("spread.max", 0f));
            _watches.Add(DerivedHits308.Watch(ctx, shot, DerivedHits308.BeginShot(ctx, hit, life)));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Target == null || ctx.Host == null || !_watches.TryTake(result.Attack.AttackId, out var watch)) return;
            Vector3 point = result.Target.transform.position;
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Hit, new SpellFxCueArgs { Point = point, At = ctx.Now });
            // A carrier the hit itself felled already has another life number: the next tick sees it as fallen and moves the ember.
            if (!EmberSpreadRule308.Plant(point, result.Target.LifeRevision, ctx.Now, watch.Power, watch.Row.F("spread.timer", 0f),
                Mathf.RoundToInt(watch.Row.F("spread.max", 0f)), out var state)) return;
            _embers.Add(new Ember { State = state, Carrier = result.Killed ? null : result.Target, Row = watch.Row, Element = watch.Element,
                Letter = watch.Letter, Fx = watch.Fx });
        }

        public void Tick(in SpellTickContext ctx)
        {
            if (_ticking) return;
            _watches.Sweep(ctx.Now);
            if (_embers.Count == 0 || ctx.Host == null) return;
            _ticking = true;
            try
            {
                _frame.Clear(); _frame.AddRange(_embers);
                int generation = _generation;
                // a damage callback may clear this effect: stop as soon as that happened
                for (int i = 0; i < _frame.Count && generation == _generation; i++)
                {
                    var ember = _frame[i];
                    var targets = ctx.Host.Targets;
                    // refilled for every ember: a move earlier in this frame may have felled somebody
                    var step = EmberSpreadRule308.Step(ref ember.State, SpellSnapshots308.Fill(targets, ref _snaps), SpellSnapshots308.IndexOf(targets, ember.Carrier), ctx.Now,
                        ember.Row.F("spread.timer", 0f), ember.Row.F("spread.radius", 0f), _scratch, out int nextId, out bool defeated, out Vector3 from);
                    if (step == EmberStep308.Stay) continue;
                    var next = step == EmberStep308.Jump ? targets[nextId] : null;
                    DerivedHits308.Cue(ctx.Fx, ember.Fx, defeated ? SpellFxCue.TargetDefeated : SpellFxCue.Expire, new SpellFxCueArgs {
                        Target = next != null ? next.transform : null, Point = from, At = ctx.Now });
                    if (next == null) { _embers.Remove(ember); continue; }
                    ember.Carrier = next;
                    var hit = DerivedHits308.Apply(ctx, new DerivedShot308 { Target = next, Life = ember.State.Life, Power = ember.State.Power, Element = ember.Element,
                        Letter = ember.Letter, From = from, Fx = ember.Fx });
                    if (generation != _generation) break;
                    if (hit.AppliedDamage > 0f)
                        DerivedHits308.Cue(ctx.Fx, ember.Fx, SpellFxCue.Secondary, new SpellFxCueArgs { Target = next.transform, Point = next.transform.position, At = ctx.Now });
                    // a move a wall stopped, or the last move: the ember is gone. A move that felled the new carrier goes on next tick.
                    if (hit.AppliedDamage <= 0f || ember.State.Left <= 0) _embers.Remove(ember);
                    else if (hit.Killed) ember.Carrier = null;
                }
            }
            finally { _ticking = false; _frame.Clear(); }
        }

        public void Clear(SpellClearReason reason)
        {
            _generation++;
            _watches.Clear(); _embers.Clear(); _frame.Clear();
        }
    }
}
