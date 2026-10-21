using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // The 결계 (domain) handlers that act on the player standing in them (SPEC-SPELL-120-308 WP-17, D308-121). Each writes
    // one entry on the wiring's PlayerDomainState308 every frame the player is inside (it ends at the domain's end) and ends
    // it the frame the player leaves, when the domain ends and on a clear. A host without IPlayerDomainHost308 (checks, a
    // lab) gets nothing. Nothing here heals, deals damage or changes HP.
    public abstract class DomainSelfEffect308 : DomainEffect308
    {
        PlayerDomainState308 _state;

        protected DomainSelfEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            _state = (ctx.Host as IPlayerDomainHost308)?.PlayerDomain308;
            Read(ctx);
            if (_state != null) Mark(_state, zone);              // placed at the caster's feet: it counts from this frame
        }

        protected override void Player(in SpellTickContext ctx, in SpellZone308 zone, bool inside)
        {
            if (_state == null) _state = (ctx.Host as IPlayerDomainHost308)?.PlayerDomain308;
            if (_state == null) return;
            if (inside) Mark(_state, zone); else _state.End(zone.Token);
        }

        protected override void Ended(in SpellZone308 zone) { _state?.End(zone.Token); }

        // The row's own values, read once per cast.
        protected virtual void Read(in SpellCastContext ctx) { }

        // This frame's entry (ends at zone.Until at the latest).
        protected abstract bool Mark(PlayerDomainState308 state, in SpellZone308 zone);
    }

    // 움: natural ink regeneration inside runs domain.scale times as fast (the pause while drawing and after a spend stays).
    public sealed class DomainRegenEffect308 : DomainSelfEffect308
    {
        public const string HandlerId = "domain.regen";
        static readonly SpellParamSpec ScaleSpec = new SpellParamSpec("domain.scale", 1f, 5f);
        static readonly SpellParamSpec[] Specs = With(ScaleSpec);
        float _scale = 1f;
        public DomainRegenEffect308() : this(null, null) { }
        public DomainRegenEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override void Read(in SpellCastContext ctx) { _scale = ctx.Row.F(ScaleSpec.Key, 1f); }
        protected override bool Mark(PlayerDomainState308 state, in SpellZone308 zone) => state.Regen(zone.Token, zone.Until, _scale);
    }

    // 눔: the sword form inside spends no ink while it stands (its swing and damage are unchanged).
    public sealed class DomainSwordHoldEffect308 : DomainSelfEffect308
    {
        public const string HandlerId = "domain.swordhold";
        static readonly SpellParamSpec[] Specs = With();
        public DomainSwordHoldEffect308() : this(null, null) { }
        public DomainSwordHoldEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(PlayerDomainState308 state, in SpellZone308 zone) => state.HoldDrain(zone.Token, zone.Until);
    }

    // 숨: the player inside cannot be pushed or pulled. The reader is CombatLoopWiring.Shove308 (D308-129): the 적로 불승냥이
    // charge's push neither starts inside nor goes on once the player stands inside; the damage is unchanged.
    public sealed class DomainAnchorEffect308 : DomainSelfEffect308
    {
        public const string HandlerId = "domain.anchor";
        static readonly SpellParamSpec[] Specs = With();
        public DomainAnchorEffect308() : this(null, null) { }
        public DomainAnchorEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(PlayerDomainState308 state, in SpellZone308 zone) => state.Anchor(zone.Token, zone.Until);
    }

    // 운: no lasting enemy status sticks to the player inside. No enemy sets one yet: the entry is written and waits for its
    // reader (Gate test until such an enemy exists).
    public sealed class DomainStatusWardEffect308 : DomainSelfEffect308
    {
        public const string HandlerId = "domain.ward.status";
        static readonly SpellParamSpec[] Specs = With();
        public DomainStatusWardEffect308() : this(null, null) { }
        public DomainStatusWardEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Mark(PlayerDomainState308 state, in SpellZone308 zone) => state.Ward(zone.Token, zone.Until);
    }
}
