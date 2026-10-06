using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // hit.cycle (SPEC-SPELL-120-308 WP-04): a single shot; after its confirmed hit it comes back to the caster and, on
    // arrival, returns a little ink and restores a little health, once. A shot that hits nothing returns nothing. The ink
    // returned is always below the cost of the cast. This rewards hitting; nothing here listens to the player being hit.
    // Presentation: the cast owns one flying body (ISpellPresenter). Cues: Hit (the impact), Return (the shot turns back;
    // Target = the caster, At = when it arrives), Consume (it arrived; Value = ink returned).
    public sealed class CycleEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "hit.cycle";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("return.time", 0f, 10f),     // seconds from the hit until the shot is back at the caster
            new SpellParamSpec("refund.ink", 0f, 1f),       // ink returned on arrival (the unit of the cast cost)
            new SpellParamSpec("heal.hp01", 0f, 1f),        // health restored on arrival, as a share of the maximum
            new SpellParamSpec("cost", 0f, 10f, false, 1f), // the row's ink cost multiplier (the host spends it; read here to cap the refund)
        };

        readonly DerivedHitWatchList308 _watches = new DerivedHitWatchList308();
        readonly DerivedTimedQueue308<CycleReturn308> _returns = new DerivedTimedQueue308<CycleReturn308>();
        readonly List<CycleReturn308> _due = new List<CycleReturn308>();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int Waiting => _watches.Count;
        public int Returning => _returns.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var shot = DerivedHits308.Primary(ctx);
            if (!shot.Launched) return false;
            // nobody aimed at: the usual missed single shot (the stroke adapter shows it), nothing comes back
            if (!shot.CanLand) return true;
            ctx.Host.PresentationOwned();
            var hit = shot.Hit;
            // the presentation lives for the flight out and the flight back
            float life = Mathf.Max(0f, hit.ImpactTime - ctx.Now) + Mathf.Max(0f, ctx.Row.F("return.time", 0f));
            _watches.Add(DerivedHits308.Watch(ctx, shot, DerivedHits308.BeginShot(ctx, hit, life)));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (result.Target == null || ctx.Host == null || !_watches.TryTake(result.Attack.AttackId, out var watch)) return;
            float back = CycleRule308.ReturnAt(ctx.Now, watch.Row.F("return.time", 0f));
            _returns.Add(back, CycleRule308.Plan(watch.Row.F("refund.ink", 0f), watch.Cost, watch.Row.F("heal.hp01", 0f), watch.Fx));
            Vector3 point = result.Target.transform.position;
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Hit, new SpellFxCueArgs { Point = point, At = ctx.Now });
            DerivedHits308.Cue(ctx.Fx, watch.Fx, SpellFxCue.Return, new SpellFxCueArgs { Target = ctx.Host.Player, Point = point, At = back });
        }

        public void Tick(in SpellTickContext ctx)
        {
            _watches.Sweep(ctx.Now);
            if (_returns.Count == 0 || ctx.Host == null) return;
            _due.Clear(); _returns.TakeDue(ctx.Now, _due);
            for (int i = 0; i < _due.Count; i++)
            {
                var back = _due[i];
                var player = ctx.Host.PlayerVitals;
                // nothing returns to a caster who fell (no revival, and the host clears this effect on death anyway)
                if (player == null || player.Hp01 <= 0f) continue;
                if (back.Ink > 0f && ctx.Host.Ink != null) ctx.Host.Ink.Gain(back.Ink);
                player.Heal(CycleRule308.Heal(player.MaxHp, back.Hp01));
                DerivedHits308.Cue(ctx.Fx, back.Fx, SpellFxCue.Consume, new SpellFxCueArgs { Target = ctx.Host.Player, Point = ctx.Host.PlayerPosition, At = ctx.Now, Value = back.Ink });
            }
            _due.Clear();
        }

        public void Clear(SpellClearReason reason)
        {
            _watches.Clear(); _returns.Clear(); _due.Clear();
        }
    }
}
