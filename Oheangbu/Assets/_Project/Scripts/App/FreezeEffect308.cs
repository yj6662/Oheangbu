using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    // #308 WP-05, handler single.freeze (SPEC-SPELL-120-308 section 13): the icicle.
    // The strike is the single shot of its body (SpellPrimary308: the glyph without a final decides how it flies, here a
    // turn-limited guided shot that can lose its target); the stroke adapter flies it. When that hit is
    // confirmed the target is frozen for the row's duration: an ordinary enemy cannot act or move, a boss is only slowed
    // (giyeok precedent, Q8 default). The next confirmed hit on the frozen enemy breaks the ice: the control is released
    // and the row's extra damage is applied once, under the attack identity of the freezing hit (so it adds no second
    // five-element credit). A freeze nobody breaks ends by itself without the extra damage.
    // Control only: no weak point is opened and no groggy is added.
    public sealed class FreezeEffect308 : ISpellEffect, ISpellHitHook
    {
        public const string HandlerId = "single.freeze";
        const string DurationKey = "freeze.duration", ShatterKey = "shatter.power", BossKey = "boss.speed";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("freeze.duration", 0.05f, 60f),
            new SpellParamSpec("shatter.power", 0f, 5f),
            new SpellParamSpec("boss.speed", 0f, 1f),
        };

        readonly ControlHitWatch308<FreezeRule308.Spec> _watch = new ControlHitWatch308<FreezeRule308.Spec>();
        readonly FreezeRule308.State _state = new FreezeRule308.State();
        readonly SpellControlLedger308 _ledger = new SpellControlLedger308();

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int WaitingHits => _watch.Count;        // read only (checks)
        public int FrozenCount => _state.Count;
        public int HeldControls => _ledger.Count;

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var shot = SpellPrimary308.Plan(ctx, true);
            if (!shot.Launched) return false;
            if (!shot.CanLand) return true;                       // cast into the air: the ink is spent, nothing is frozen
            var row = ctx.Row; var hit = shot.Hit;
            _watch.Add(hit.AttackId, shot.LatestImpact, new FreezeRule308.Spec(row.F(DurationKey, 0f), row.F(BossKey, 1f),
                FreezeRule308.ShatterPower(ctx.Cast.Power, row.F(ShatterKey, 0f)), ctx.Cast.Element, ctx.Cast.Letter));
            return true;
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            var target = result.Target;
            if (target == null || ctx.Host == null || (_state.Count == 0 && _watch.Count == 0)) return;
            long hitId = result.Attack.AttackId;
            int id = SpellSnapshots308.IndexOf(ctx.Host.Targets, target);

            // 1. a confirmed hit on a frozen enemy breaks the ice: release first, then the extra damage once. The extra
            //    damage comes back through this hook; by then its entry is gone, so it cannot break anything again.
            if (_state.Count > 0 && _state.TryShatter(id, target.LifeRevision, hitId, result.Killed || !target.IsAlive, ctx.Now, out var broken))
            {
                _ledger.Release(broken.Owner);
                if (broken.Bonus > 0f)
                {
                    Object caster = ctx.Host.PlayerVitals != null ? (Object)ctx.Host.PlayerVitals : ctx.Host.Player;
                    ctx.Host.ApplyDirectHit(target, broken.Life, broken.Bonus * ctx.Host.DamageScale(broken.Element), target.transform.position + Vector3.up,
                        new AttackProvenance(broken.Owner, caster, DamageSource.PlayerDirect, broken.Element), broken.Letter);
                }
            }

            // 2. this handler's own hit freezes the enemy it struck (unless the hit, or the break above, killed it)
            if (_watch.Count == 0 || !_watch.TryTake(hitId, out var spec)) return;
            if (_state.Freeze(id, target.LifeRevision, hitId, target.IsBoss, result.Killed || !target.IsAlive, ctx.Now, spec,
                out float until, out float speed, out bool blocks))
                _ledger.Apply(target, hitId, until, speed, blocks);
        }

        public void Tick(in SpellTickContext ctx)
        {
            _watch.Sweep(ctx.Now);
            _state.Expire(ctx.Now);
            _ledger.Expire(ctx.Now);
        }

        public void Clear(SpellClearReason reason)
        {
            _watch.Clear();
            _state.Clear();
            _ledger.Clear();
        }
    }
}
