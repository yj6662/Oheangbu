using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App
{
    // The 결계 (domain) handlers that move enemies or keep them out (SPEC-SPELL-120-308 WP-17, D308-121). Bosses are never
    // moved; 굼 shuts a boss out only for a moment. Movement goes through the enemy's own NavMeshAgent (Move: walls and
    // ledges stop it as they stop its walk), straight through the transform for an enemy without one (test targets).
    static class DomainMoves308
    {
        public static NavMeshAgent Agent(EnemyVitals enemy) => enemy != null ? enemy.GetComponent<NavMeshAgent>() : null;

        // Moves the enemy by a flat step. Returns false when nothing could move it.
        public static bool Step(EnemyVitals enemy, NavMeshAgent agent, Vector3 step)
        {
            if (enemy == null || step.sqrMagnitude < 1e-8f) return false;
            if (agent != null && agent.isActiveAndEnabled)
            {
                if (!agent.isOnNavMesh) return false;                  // off its mesh (falling, warping): leave it be
                agent.Move(step);
                return true;
            }
            enemy.transform.position += step;
            return true;
        }

        public static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        public static float Body(EnemyVitals enemy) { float body = enemy.BodyRadius; return body > 0f ? body : 0f; }
    }

    // 군: the moment the domain is placed, every ordinary enemy whose body is inside is shoved out to the edge plus
    // domain.margin (straight away from the centre; one on the centre goes along the caster's facing), sliding at
    // domain.push.speed so the shove reads. A telegraph or an attack already started is not cancelled.
    public sealed class DomainPushEffect308 : DomainEffect308
    {
        public const string HandlerId = "domain.push";
        static readonly SpellParamSpec MarginSpec = new SpellParamSpec("domain.margin", 0f, 5f);
        static readonly SpellParamSpec SpeedSpec = new SpellParamSpec("domain.push.speed", 1f, 60f);
        static readonly SpellParamSpec[] Specs = With(MarginSpec, SpeedSpec);

        struct Shove { public EnemyVitals Enemy; public NavMeshAgent Agent; public Vector3 To; public uint Life; }
        readonly List<Shove> _shoves = new List<Shove>();
        float _speed;

        public DomainPushEffect308() : this(null, null) { }
        public DomainPushEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public int Pushed { get; private set; }                 // enemies shoved by the last cast (checks read this)
        public int Sliding => _shoves.Count;

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _shoves.Clear(); Pushed = 0;
            _speed = ctx.Row.F(SpeedSpec.Key, 12f);
            float margin = ctx.Row.F(MarginSpec.Key, 0f);
            var host = ctx.Host; var targets = host.Targets;
            for (int i = 0; targets != null && i < targets.Count; i++)
            {
                var enemy = targets[i];
                if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled || enemy.IsBoss) continue;
                Vector3 at = enemy.transform.position;
                float body = DomainMoves308.Body(enemy);
                if (!SpellZoneRule308.Covers(zone, at, ctx.Now, body)) continue;
                Vector3 to = DomainRule308.PushOut(zone.Centre, at, zone.Radius + body, margin, host.PlayerForward);
                if (DomainMoves308.Flat(at, to) < 1e-3f) continue;
                _shoves.Add(new Shove { Enemy = enemy, Agent = DomainMoves308.Agent(enemy), To = to, Life = enemy.LifeRevision });
                Pushed++;
            }
        }

        protected override void Step(in SpellTickContext ctx, in SpellZone308 zone) => Slide(ctx.Delta);

        // The slide also ends what is left of it when the domain ends first.
        protected override void Ended(in SpellZone308 zone) { _shoves.Clear(); }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _shoves.Clear();
        }

        void Slide(float dt)
        {
            for (int i = _shoves.Count - 1; i >= 0; i--)
            {
                var shove = _shoves[i];
                var enemy = shove.Enemy;
                if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled || enemy.LifeRevision != shove.Life) { _shoves.RemoveAt(i); continue; }
                Vector3 at = enemy.transform.position;
                float left = DomainMoves308.Flat(at, shove.To);
                if (left < 0.02f || !(dt > 0f)) { if (left < 0.02f) _shoves.RemoveAt(i); continue; }
                float move = Mathf.Min(left, _speed * dt);
                Vector3 step = new Vector3(shove.To.x - at.x, 0f, shove.To.z - at.z) * (move / left);
                if (!DomainMoves308.Step(enemy, shove.Agent, step)) { _shoves.RemoveAt(i); continue; }
                // a wall stopped it: what is left of the shove is spent
                if (DomainMoves308.Flat(enemy.transform.position, at) < move * 0.25f) _shoves.RemoveAt(i);
            }
        }
    }

    // 뭉: an ordinary enemy inside drifts toward the centre at domain.pull.speed, at most domain.pull.budget metres per cast
    // and never closer than domain.pull.core; not while it telegraphs or attacks (the pull never spoils a read).
    public sealed class DomainPullEffect308 : DomainEffect308
    {
        public const string HandlerId = "domain.pull";
        static readonly SpellParamSpec SpeedSpec = new SpellParamSpec("domain.pull.speed", 0.1f, 20f);
        static readonly SpellParamSpec BudgetSpec = new SpellParamSpec("domain.pull.budget", 0.1f, 50f);
        static readonly SpellParamSpec CoreSpec = new SpellParamSpec("domain.pull.core", 0f, 10f);
        static readonly SpellParamSpec[] Specs = With(SpeedSpec, BudgetSpec, CoreSpec);

        struct Pulled { public EnemyVitals Enemy; public EnemyController Controller; public NavMeshAgent Agent; public float Used; }
        readonly List<Pulled> _pulled = new List<Pulled>();
        float _speed, _budget, _core;

        public DomainPullEffect308() : this(null, null) { }
        public DomainPullEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        // Metres this cast has pulled the enemy so far (checks read this).
        public float PulledOf(EnemyVitals enemy)
        {
            for (int i = 0; i < _pulled.Count; i++) if (_pulled[i].Enemy == enemy) return _pulled[i].Used;
            return 0f;
        }

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _pulled.Clear();
            _speed = ctx.Row.F(SpeedSpec.Key, 0f);
            _budget = ctx.Row.F(BudgetSpec.Key, 0f);
            _core = ctx.Row.F(CoreSpec.Key, 0f);
        }

        protected override void Enemy(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy, bool inside)
        {
            if (!inside || enemy.IsBoss) return;
            int at = -1;
            for (int i = 0; i < _pulled.Count; i++) if (_pulled[i].Enemy == enemy) { at = i; break; }
            if (at < 0)
            {
                _pulled.Add(new Pulled { Enemy = enemy, Controller = enemy.GetComponent<EnemyController>(), Agent = DomainMoves308.Agent(enemy) });
                at = _pulled.Count - 1;
            }
            var pulled = _pulled[at];
            var controller = pulled.Controller;
            if (controller != null && (controller.IsTelegraphing || controller.AttackInProgress)) return;
            Vector3 step = DomainRule308.PullStep(zone.Centre, enemy.transform.position, _speed, ctx.Delta, _budget - pulled.Used, _core, out float used);
            if (!(used > 0f) || !DomainMoves308.Step(enemy, pulled.Agent, step)) return;
            pulled.Used += used;
            _pulled[at] = pulled;
        }

        protected override void Ended(in SpellZone308 zone) { _pulled.Clear(); }
    }

    // 굼: a ring the enemies outside cannot cross while it stands (D308-121 TEST, ring reading). An enemy that stood inside
    // when it rose walks free inside, and is shut out like the others once it steps out. One shut out walks around the
    // ring (its brain is handed points outside it, and is put back on the edge if pushed in). A boss is held back only
    // for domain.boss.hold seconds from the moment it first reaches the edge, then walks through, once per domain.
    public sealed class DomainWallEffect308 : DomainEffect308
    {
        public const string HandlerId = "domain.wall";
        static readonly SpellParamSpec BossHoldSpec = new SpellParamSpec("domain.boss.hold", 0f, 10f);
        static readonly SpellParamSpec[] Specs = With(BossHoldSpec);
        const float Touch = 0.1f;                               // metres past the edge that count as reaching it

        struct Ring { public EnemyVitals Enemy; public bool Out, Done; public float Reached; }
        readonly List<Ring> _rings = new List<Ring>();
        float _bossHold;

        public DomainWallEffect308() : this(null, null) { }
        public DomainWallEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _rings.Clear();
            _bossHold = ctx.Row.F(BossHoldSpec.Key, 0f);
        }

        protected override void Enemy(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy, bool inside)
        {
            int at = -1;
            for (int i = 0; i < _rings.Count; i++) if (_rings[i].Enemy == enemy) { at = i; break; }
            var ring = at >= 0 ? _rings[at] : new Ring { Enemy = enemy, Reached = float.NaN };
            if (!enemy.IsAlive || !enemy.isActiveAndEnabled) { Holds.Release(enemy, zone.Token); Keep(at, ring); return; }
            if (!ring.Out && !inside) ring.Out = true;           // stepped out (or was never in): shut out from now on
            if (ring.Out && !ring.Done)
            {
                float body = DomainMoves308.Body(enemy), edge = zone.Radius + body;
                float until = zone.Until;
                if (enemy.IsBoss)
                {
                    if (float.IsNaN(ring.Reached) && DomainMoves308.Flat(enemy.transform.position, zone.Centre) <= edge + Touch) ring.Reached = ctx.Now;
                    if (!float.IsNaN(ring.Reached)) until = System.Math.Min(until, ring.Reached + _bossHold);
                    if (!(ctx.Now < until)) { ring.Done = true; Holds.Release(enemy, zone.Token); Keep(at, ring); return; }
                }
                Holds.Hold(enemy, zone.Token, enemy.Modifiers.Domain.Exclude(zone.Token, until, zone.Centre, edge));
            }
            Keep(at, ring);
        }

        protected override void Ended(in SpellZone308 zone) { _rings.Clear(); }

        void Keep(int at, Ring ring) { if (at >= 0) _rings[at] = ring; else _rings.Add(ring); }
    }
}
