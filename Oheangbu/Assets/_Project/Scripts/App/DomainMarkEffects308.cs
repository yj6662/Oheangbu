using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // The 결계 (domain) handlers that only mark the enemies standing inside (SPEC-SPELL-120-308 WP-17, D308-121). Each writes one
    // kind of entry on EnemyVitals.Modifiers.Domain (EnemyDomainState308) every frame an enemy is inside; the enemy's own code
    // reads it at its seam. domain.scale is the value for an ordinary enemy, domain.boss the value for a boss (absent = the
    // same). A row's numbers live in the rule sheet; nothing here names a glyph.
    public abstract class DomainMarkEffect308 : DomainEffect308
    {
        protected static readonly SpellParamSpec ScaleSpec = new SpellParamSpec("domain.scale", 0f, 5f);
        protected static readonly SpellParamSpec BossSpec = new SpellParamSpec("domain.boss", 0f, 5f);

        float _scale = 1f, _boss = 1f;

        protected DomainMarkEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _scale = ctx.Row.F(ScaleSpec.Key, 1f);
            _boss = ctx.Row.F(BossSpec.Key, _scale);
        }

        protected override bool Inside(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy)
        {
            bool boss = enemy.IsBoss;
            return Mark(enemy.Modifiers.Domain, zone, boss, DomainRule308.Pick(boss, _scale, _boss));
        }

        // Writes this domain's entry (ends at zone.Until at the latest). false = nothing was written.
        protected abstract bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value);
    }

    // 눅: no regeneration and no revival of an enemy inside, and no new floor hazard of any enemy forms inside the circle
    // (the hazard's place counts, not where its caster stands: D308-125 적용 2). Hazards already laid stay. Bosses the same.
    public sealed class DomainSealEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.seal";
        static readonly SpellParamSpec[] Specs = With();
        public DomainSealEffect308() : this(null, null) { }
        public DomainSealEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Seal(zone.Token, zone.Until);

        // Every living enemy gets the floor circle; only one inside is sealed. One that steps out drops the seal, not the floor.
        protected override void Enemy(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy, bool inside)
        {
            if (!enemy.IsAlive || !enemy.isActiveAndEnabled) { Holds.Release(enemy, zone.Token); return; }
            var domain = enemy.Modifiers.Domain;
            bool floor = domain.Floor(zone.Token, zone.Until, zone.Centre, zone.Radius);
            if (inside) Holds.Hold(enemy, zone.Token, Inside(ctx, zone, enemy) | floor);
            else { domain.Remove(zone.Token, EnemyDomainState308.Kind.Seal); Holds.Hold(enemy, zone.Token, floor); }
        }
    }

    // 묵: an enemy inside cannot dash or leap (it walks); a boss dashes at domain.boss of its speed.
    public sealed class DomainGroundEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.ground";
        static readonly SpellParamSpec[] Specs = With(ScaleSpec, BossSpec);
        public DomainGroundEffect308() : this(null, null) { }
        public DomainGroundEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Dash(zone.Token, zone.Until, value);
    }

    // 뭇: an enemy inside cannot call its allies.
    public sealed class DomainHushEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.hush";
        static readonly SpellParamSpec[] Specs = With();
        public DomainHushEffect308() : this(null, null) { }
        public DomainHushEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Call(zone.Token, zone.Until);
    }

    // 순: an enemy inside cannot enter a guard (or reflect) stance; a stance already raised stays.
    public sealed class DomainUnguardEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.unguard";
        static readonly SpellParamSpec[] Specs = With();
        public DomainUnguardEffect308() : this(null, null) { }
        public DomainUnguardEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Guard(zone.Token, zone.Until);
    }

    // 욱: the attack telegraph of an enemy inside lasts domain.scale times as long (a boss domain.boss).
    public sealed class DomainSlowTellEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.slowtell";
        static readonly SpellParamSpec[] Specs = With(ScaleSpec, BossSpec);
        public DomainSlowTellEffect308() : this(null, null) { }
        public DomainSlowTellEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Telegraph(zone.Token, zone.Until, value);
    }

    // 웃: an enemy inside turns at domain.scale of its rate (a boss domain.boss). An enemy that turns at once counts as
    // turning at domain.turn.instant degrees per second.
    public sealed class DomainSlowTurnEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.slowturn";
        static readonly SpellParamSpec InstantSpec = new SpellParamSpec("domain.turn.instant", 0f, 2000f);
        static readonly SpellParamSpec[] Specs = With(ScaleSpec, BossSpec, InstantSpec);
        float _instant;
        public DomainSlowTurnEffect308() : this(null, null) { }
        public DomainSlowTurnEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone) { base.Placed(ctx, zone); _instant = ctx.Row.F(InstantSpec.Key, 0f); }
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Turn(zone.Token, zone.Until, value, _instant);
    }

    // 눙: a ranged enemy inside locks its aim at domain.scale of its telegraph (0 = at the start; a boss domain.boss), so a
    // player who moves after that is missed. The aim line is ink on the ground (the presenter's business), never HUD. A shot
    // with no radius of its own misses a player more than domain.aim.radius metres off the locked point.
    public sealed class DomainAimLockEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.aimlock";
        static readonly SpellParamSpec RadiusMissSpec = new SpellParamSpec("domain.aim.radius", 0f, 5f);
        static readonly SpellParamSpec[] Specs = With(ScaleSpec, BossSpec, RadiusMissSpec);
        float _miss;
        public DomainAimLockEffect308() : this(null, null) { }
        public DomainAimLockEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone) { base.Placed(ctx, zone); _miss = ctx.Row.F(RadiusMissSpec.Key, 0f); }
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value) => domain.Aim(zone.Token, zone.Until, value, _miss);
    }

    // 숙: an ordinary enemy inside cannot leave the circle (free inside; projectiles pass). A boss is not caged: it walks at
    // domain.boss of its speed instead.
    public sealed class DomainCageEffect308 : DomainMarkEffect308
    {
        public const string HandlerId = "domain.cage";
        static readonly SpellParamSpec[] Specs = With(BossSpec);
        public DomainCageEffect308() : this(null, null) { }
        public DomainCageEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(EnemyDomainState308 domain, in SpellZone308 zone, bool boss, float value)
            => boss ? domain.Move(zone.Token, zone.Until, value) : domain.Leash(zone.Token, zone.Until, zone.Centre, zone.Radius);
    }

    // 굿: an enemy that crosses the edge INTO the domain cannot start an attack for domain.hold seconds (a boss
    // domain.boss.hold); an attack already started goes on. Once per enemy per domain; an enemy that stood inside when the
    // domain came never counts as crossing.
    public sealed class DomainThresholdEffect308 : DomainEffect308
    {
        public const string HandlerId = "domain.threshold";
        static readonly SpellParamSpec HoldSpec = new SpellParamSpec("domain.hold", 0.05f, 10f);
        static readonly SpellParamSpec BossHoldSpec = new SpellParamSpec("domain.boss.hold", 0f, 10f);
        static readonly SpellParamSpec[] Specs = With(HoldSpec, BossHoldSpec);

        struct Seen { public EnemyVitals Enemy; public bool Inside, Spent; }
        readonly List<Seen> _seen = new List<Seen>();
        float _hold, _boss;

        public DomainThresholdEffect308() : this(null, null) { }
        public DomainThresholdEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _seen.Clear();
            _hold = ctx.Row.F(HoldSpec.Key, 0f);
            _boss = ctx.Row.F(BossHoldSpec.Key, _hold);
        }

        protected override void Enemy(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy, bool inside)
        {
            int at = -1;
            for (int i = 0; i < _seen.Count; i++) if (_seen[i].Enemy == enemy) { at = i; break; }
            bool seen = at >= 0, was = seen && _seen[at].Inside, spent = seen && _seen[at].Spent;
            if (!inside) Holds.Release(enemy, zone.Token);
            else if (!spent && DomainRule308.Entered(seen, was, true))
            {
                float hold = DomainRule308.Pick(enemy.IsBoss, _hold, _boss);
                spent = true;
                if (hold > 0f) Holds.Hold(enemy, zone.Token, enemy.Modifiers.Domain.Hold(zone.Token, System.Math.Min(ctx.Now + hold, zone.Until)));
            }
            var next = new Seen { Enemy = enemy, Inside = inside, Spent = spent };
            if (seen) _seen[at] = next; else _seen.Add(next);
        }

        protected override void Ended(in SpellZone308 zone) { _seen.Clear(); }
    }
}
