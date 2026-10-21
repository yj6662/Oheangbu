using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // The 결계 (domain) handlers that take enemy shots out of the air over the circle (SPEC-SPELL-120-308 WP-17, D308-121).
    // A shot is taken when it is over the circle and its shooter is not (a shot fired from inside flies on: the circle
    // shuts the outside out, it does not shut the inside in); a shot nobody fired (a test target) is taken over the circle.
    // At most domain.cap shots per domain. While the player's own guard stands nothing is taken: the guard and its parry are
    // judged first, as always. The burst where the shot died is presentation only.
    public abstract class DomainShotEffect308 : DomainEffect308
    {
        protected static readonly SpellParamSpec CapSpec = new SpellParamSpec("domain.cap", 1f, 20f);

        readonly List<ISpellInterceptable308> _found = new List<ISpellInterceptable308>();
        Collider[] _buffer = new Collider[16];
        int _cap, _taken;

        protected DomainShotEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }

        public int Downed => _taken;                            // shots taken by the standing domain (checks read this)

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _cap = Mathf.Max(1, Mathf.RoundToInt(ctx.Row.F(CapSpec.Key, 1f)));
            _taken = 0;
            Opened(ctx);
        }

        protected override void Step(in SpellTickContext ctx, in SpellZone308 zone)
        {
            var host = ctx.Host;
            if (host == null || host.Player == null || _taken >= _cap) return;
            if (host is IPlayerDomainHost308 guard && guard.GuardStands308) return;
            Vector3 player = host.PlayerPosition;
            float dx = zone.Centre.x - player.x, dz = zone.Centre.z - player.z;
            Intercepts308.Gather(host, Mathf.Sqrt(dx * dx + dz * dz) + zone.Radius + 1f, _found, ref _buffer);
            for (int i = 0; i < _found.Count && _taken < _cap; i++)
            {
                var shot = _found[i];
                if (!shot.InFlight) continue;
                Vector3 point = shot.Position;
                if (!SpellZoneRule308.Inside(zone, point)) continue;
                var shooter = (shot as EnemyProjectileIntercept308)?.Controller;
                if (shooter != null && SpellZoneRule308.Inside(zone, shooter.transform.position)) continue;
                if (!shot.Intercept(zone.Token)) continue;
                _taken++;
                (ctx.Fx as ISpellBurstPresenter308)?.Burst(zone.Letter, point);
                Took(ctx, zone, shooter, point);
            }
            _found.Clear();
        }

        // The domain was placed (the counter is already back at 0).
        protected virtual void Opened(in SpellCastContext ctx) { }

        // One shot was taken at point; shooter = the enemy that fired it (null for a shot nobody fired).
        protected virtual void Took(in SpellTickContext ctx, in SpellZone308 zone, EnemyController shooter, Vector3 point) { }
    }

    // 눗: the fire circle burns up to domain.cap shots that come in from outside.
    public sealed class DomainBurnShotEffect308 : DomainShotEffect308
    {
        public const string HandlerId = "domain.burnshot";
        static readonly SpellParamSpec[] Specs = With(CapSpec);
        public DomainBurnShotEffect308() : this(null, null) { }
        public DomainBurnShotEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
    }

    // 숭: the metal circle turns up to domain.cap shots back on their shooters, each for domain.reflect of the shot's own
    // damage (DamageSource.Retaliation: no groggy, no five-element credit). The hit is an order delivered after the
    // enemies of the frame were handled, from the point where the shot was taken (it needs a clear line to the shooter).
    public sealed class DomainReflectShotEffect308 : DomainShotEffect308
    {
        public const string HandlerId = "domain.reflectshot";
        static readonly SpellParamSpec ReflectSpec = new SpellParamSpec("domain.reflect", 0f, 5f);
        static readonly SpellParamSpec[] Specs = With(CapSpec, ReflectSpec);

        struct Order { public EnemyVitals Target; public uint Life; public float Power; public Vector3 Origin; public Element Element; public char Letter; }
        readonly List<Order> _orders = new List<Order>();
        float _reflect;

        public DomainReflectShotEffect308() : this(null, null) { }
        public DomainReflectShotEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingCount => _orders.Count;               // checks read this

        protected override void Opened(in SpellCastContext ctx) { _reflect = ctx.Row.F(ReflectSpec.Key, 0f); }

        protected override void Took(in SpellTickContext ctx, in SpellZone308 zone, EnemyController shooter, Vector3 point)
        {
            if (shooter == null || !(_reflect > 0f)) return;
            var target = shooter.GetComponent<EnemyVitals>();
            if (target == null || !target.IsAlive) return;
            _orders.Add(new Order
            {
                Target = target, Life = target.LifeRevision, Power = shooter.AttackDamage * _reflect, Origin = point,
                Element = zone.Element, Letter = zone.Letter,
            });
        }

        protected override void Act(in SpellTickContext ctx)
        {
            base.Act(ctx);
            if (_orders.Count == 0) return;
            var host = ctx.Host;
            var instigator = host != null ? Instigator(host) : null;
            for (int i = 0; host != null && instigator != null && i < _orders.Count; i++)
            {
                var order = _orders[i];
                if (order.Target == null || !(order.Power > 0f)) continue;
                host.ApplyDirectHit(order.Target, order.Life, order.Power * host.DamageScale(order.Element), order.Origin,
                    AttackProvenance.Create(instigator, DamageSource.Retaliation, order.Element), order.Letter);
                (ctx.Fx as ISpellBurstPresenter308)?.Burst(order.Letter, order.Target.transform.position + Vector3.up);
            }
            _orders.Clear();
        }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _orders.Clear();
        }
    }
}
