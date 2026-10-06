using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // #308 WP-07 (SPEC-SPELL-120-308 section 13, user answer Q4 = D308-13): shared body of the "attack that leaves a
    // modifier on the enemies it really hit" handlers (mod.brand, mod.armorbreak, mod.pierce, mod.dull, mod.weaken).
    //  - The judgement is the row's own basic shape. An area shape (cone, circle, path, volley) is the host's Plan* body,
    //    unchanged; a single shot flies as its body flies (SpellPrimary308: range, unguided fall or turn-limited guidance of
    //    the glyph without a final, the lock-on single judgement when that glyph has none). Both are presented by the
    //    stroke adapter like the glyph without a final.
    //  - Every scheduled hit is remembered by its attack id. The modifier goes on the enemy when the wiring confirms that
    //    hit (ISpellHitHook), never at cast time: an air cast, a hit that lost its target or a killed target leaves nothing.
    //  - The modifier itself lives on the enemy (EnemyVitals.Modifiers, cleared with that enemy's life). This class keeps
    //    the hits still on their way and remembers which enemies carry its modifier, so that Clear (player death, rest,
    //    scene leave, disable) takes back exactly what it hung there, as the control and veil handlers do.
    // None of these handlers touches groggy, the weak point or the five-element count beyond what the plain hit already does.
    public abstract class ModifierEffect308 : ISpellEffect, ISpellHitHook
    {
        struct Pending { public long AttackId; public EnemyVitals Enemy; }
        struct Carrier { public EnemyVitals Enemy; public float Until; }

        readonly ModifierHitLedger308 _ledger = new ModifierHitLedger308();
        readonly List<ModifierHitLedger308.Entry> _dropped = new List<ModifierHitLedger308.Entry>();
        readonly List<Pending> _pending = new List<Pending>();     // what AtCast set up for hits still on their way
        readonly List<Carrier> _carriers = new List<Carrier>();    // enemies that carry this handler's timed modifier

        public abstract string Id { get; }
        public abstract IReadOnlyList<SpellParamSpec> Params { get; }

        // The numbers of this modifier, read from the row at cast time: a hit on its way keeps what it was cast with.
        protected abstract void Read(SpellRow row, out float value, out float duration);
        // A hit was scheduled on this enemy (it has not landed yet).
        protected virtual void AtCast(EnemyVitals target, long attackId, float value) { }
        // That hit landed and the enemy lived: put the modifier on it.
        protected virtual void AtImpact(EnemyVitals target, float until, float value) { }
        // That hit will never land: undo what AtCast set up.
        protected virtual void AtDrop(EnemyVitals target, long attackId) { }
        // The caster died, rested or left while this enemy carried the modifier AtImpact put on it: take it back.
        protected virtual void AtClear(EnemyVitals target) { }

        public int HitsOnTheWay => _ledger.Count;
        public int Carriers => _carriers.Count;            // enemies carrying this handler's modifier (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            Read(ctx.Row, out float value, out float duration);
            var targets = ctx.Host.Targets;
            if (ctx.Cast.AreaShape == AreaShape.None)
            {
                var shot = SpellPrimary308.Plan(ctx, true);
                if (!shot.Launched) return false;
                if (shot.CanLand) Remember(targets, shot.Hit, shot.LatestImpact, value, duration);
                return true; // nobody aimed at, or out of reach: the ink is spent and nothing is marked
            }
            var plan = PlanArea(ctx);
            if (plan == null) return false;
            for (int i = 0; i < plan.Hits.Count; i++)
            {
                var hit = plan.Hits[i];
                if (hit != null) Remember(targets, hit, hit.ImpactTime, value, duration);
            }
            return true; // nobody in the shape: the ink is spent and nothing is marked
        }

        // One hit on its way: the modifier waits for it until `latest` (a guided shot may land after its expected time).
        void Remember(IReadOnlyList<EnemyVitals> targets, PlannedHit hit, float latest, float value, float duration)
        {
            if (hit.Target == null || hit.AttackId <= 0) return;
            _ledger.Add(hit.AttackId, SpellSnapshots308.IndexOf(targets, hit.Target), hit.Target.LifeRevision, latest, value, duration);
            AtCast(hit.Target, hit.AttackId, value);
            _pending.Add(new Pending { AttackId = hit.AttackId, Enemy = hit.Target });
        }

        void Forget(long attackId)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].AttackId == attackId) _pending.RemoveAt(i);
        }

        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            if (!_ledger.Take(result.Attack.AttackId, out var entry)) return;
            Forget(entry.AttackId);
            var target = result.Target;
            if (result.Killed || target == null || !target.IsAlive || target.LifeRevision != entry.Life) return;
            float until = ctx.Now + entry.Duration;
            AtImpact(target, until, entry.Value);
            if (!(entry.Duration > 0f)) return;            // nothing stays on the enemy (a pierce is used up by its own hit)
            for (int i = 0; i < _carriers.Count; i++)
                if (_carriers[i].Enemy == target) { if (until > _carriers[i].Until) _carriers[i] = new Carrier { Enemy = target, Until = until }; return; }
            _carriers.Add(new Carrier { Enemy = target, Until = until });
        }

        public void Tick(in SpellTickContext ctx)
        {
            // a modifier that ran out is no longer this handler's to take back
            for (int i = _carriers.Count - 1; i >= 0; i--)
                if (_carriers[i].Enemy == null || ctx.Now >= _carriers[i].Until) _carriers.RemoveAt(i);
            if (_ledger.Count == 0) return;
            _dropped.Clear();
            _ledger.Sweep(ctx.Now, _dropped);
            var targets = ctx.Host != null ? ctx.Host.Targets : null;
            for (int i = 0; i < _dropped.Count; i++)
            {
                var enemy = SpellSnapshots308.Target(targets, _dropped[i].TargetId, _dropped[i].Life);
                if (enemy != null) AtDrop(enemy, _dropped[i].AttackId);
                Forget(_dropped[i].AttackId);
            }
            _dropped.Clear();
        }

        // Death, rest, scene leave, disable: nothing this handler hung on an enemy outlives the caster's fight. A hit still
        // on its way loses what AtCast set up; an enemy that carries the modifier loses it now. (An enemy that was
        // destroyed with its scene is skipped.)
        public void Clear(SpellClearReason reason)
        {
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i].Enemy != null) AtDrop(_pending[i].Enemy, _pending[i].AttackId);
            for (int i = 0; i < _carriers.Count; i++)
                if (_carriers[i].Enemy != null) AtClear(_carriers[i].Enemy);
            _pending.Clear(); _carriers.Clear(); _ledger.Clear(); _dropped.Clear();
        }

        // The basic judgement of an area shape, as the glyph without a final casts it (the stroke adapter presents it).
        static CastPlan PlanArea(in SpellCastContext ctx)
        {
            switch (ctx.Cast.AreaShape)
            {
                case AreaShape.Cone: return ctx.Host.PlanCone(ctx.Cast, true);
                case AreaShape.Circle: return ctx.Host.PlanCircle(ctx.Cast, true);
                case AreaShape.Path: return ctx.Host.PlanPath(ctx.Cast, true);
                default: return ctx.Host.PlanVolley(ctx.Cast, true);
            }
        }
    }
}
