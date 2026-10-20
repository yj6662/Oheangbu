using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;

namespace Oheangbu.App
{
    // The 결계 (domain) handlers that lessen the hits reaching a player who stands inside (SPEC-SPELL-120-308 WP-17, D308-121).
    // One scale on PlayerVitals' incoming damage, bound when the domain is placed and unbound when it ends (ran out, replaced,
    // yielded, cleared). domain.ward.scale = what is left of a hit (0 = nothing lands, and a hit scaled to nothing makes no hit
    // reaction); domain.elemental = 1 takes only elemental hits (the hits the 구 ward takes), 0 takes every hit. No parry
    // judgement and no parry reward: the player's own guard is judged before, as always.
    public abstract class DomainWardEffect308 : DomainEffect308
    {
        protected static readonly SpellParamSpec WardScaleSpec = new SpellParamSpec("domain.ward.scale", 0f, 1f);
        protected static readonly SpellParamSpec ElementalSpec = new SpellParamSpec("domain.elemental", 0f, 1f);

        readonly Func<IncomingDamageKind, float> _incoming;
        PlayerVitals _bound;
        float _scale = 1f;
        bool _elementalOnly, _inside;

        protected DomainWardEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { _incoming = Incoming; }

        public bool Bound => _bound != null;                    // checks read this

        protected override void Placed(in SpellCastContext ctx, in SpellZone308 zone)
        {
            float scale = ctx.Row.F(WardScaleSpec.Key, 1f);
            _scale = scale < 0f ? 0f : scale > 1f ? 1f : scale;
            _elementalOnly = ctx.Row.F(ElementalSpec.Key, 0f) >= 0.5f;
            _inside = true;                                      // placed at the caster's feet
            Bind(ctx.Host.PlayerVitals);
        }

        protected override void Player(in SpellTickContext ctx, in SpellZone308 zone, bool inside) { _inside = inside; }

        // A recast ends the old circle first (unbound here) and binds again for the new one.
        protected override void Ended(in SpellZone308 zone) { Unbind(); }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            Unbind();
        }

        // What is left of a hit of this kind (1 = untouched).
        public float Incoming(IncomingDamageKind kind)
        {
            if (_bound == null || !_inside) return 1f;
            if (_elementalOnly && kind != IncomingDamageKind.ElementalMelee && kind != IncomingDamageKind.ElementalRanged) return 1f;
            return _scale;
        }

        void Bind(PlayerVitals vitals)
        {
            if (vitals == _bound) return;
            Unbind();
            if (vitals == null) return;
            vitals.BindIncomingDamageScale(_incoming);
            _bound = vitals;
        }

        void Unbind()
        {
            if (_bound != null) _bound.UnbindIncomingDamageScale(_incoming);   // null = destroyed with its scene
            _bound = null;
        }
    }

    // 궁: a ring that walks with the player; the hits the 구 ward takes are lessened by half as much (domain.ward.scale).
    public sealed class DomainWalkWardEffect308 : DomainWardEffect308
    {
        public const string HandlerId = "domain.ward.walk";
        static readonly SpellParamSpec[] Specs = With(WardScaleSpec, ElementalSpec);
        public DomainWalkWardEffect308() : this(null, null) { }
        public DomainWalkWardEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        protected override bool Follows => true;
    }

    // 문: for a moment (domain.life) nothing reaches the player standing in the earth door; then it falls.
    public sealed class DomainShellEffect308 : DomainWardEffect308
    {
        public const string HandlerId = "domain.shell";
        static readonly SpellParamSpec[] Specs = With(WardScaleSpec, ElementalSpec);
        public DomainShellEffect308() : this(null, null) { }
        public DomainShellEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones, slot) { }
        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
    }
}
