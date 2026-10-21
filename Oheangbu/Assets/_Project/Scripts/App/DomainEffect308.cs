using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // The half every 결계 (domain) handler shares (SPEC-SPELL-120-308 WP-17, D308-121). A domain is one circle of
    // domain.radius at the caster's feet for domain.life seconds (a following domain moves with the caster). Common rules:
    //  - one standing domain per player, whatever its glyph: placing one makes the handler that held the slot drop its own
    //    (DomainSlot308); a recast of the same glyph replaces the old circle;
    //  - no damage, no groggy, no weak point, no emission (presentation = ink on the ground, the presenter's business);
    //  - what it does to an enemy is an entry on EnemyVitals.Modifiers.Domain refreshed every frame the enemy is inside and
    //    taken back the frame it leaves, when the domain ends and on a clear (DomainHolds308);
    //  - it never changes whom an enemy targets.
    // A subclass says what an enemy inside gets (Inside), and may act on the player (Player) or on each frame (Step).
    public abstract class DomainEffect308 : ZoneEffect308, IDomainHolder308
    {
        protected static readonly SpellParamSpec RadiusSpec = new SpellParamSpec("domain.radius", 0.5f, 20f);
        protected static readonly SpellParamSpec LifeSpec = new SpellParamSpec("domain.life", 0.1f, 60f);

        readonly DomainSlot308 _slot;
        protected readonly DomainHolds308 Holds = new DomainHolds308();

        protected DomainEffect308(SpellZoneRuntime308 zones, DomainSlot308 slot) : base(zones) { _slot = slot ?? new DomainSlot308(); }

        public DomainSlot308 Slot => _slot;
        public int HeldEnemies => Holds.Count;                  // enemies that carry an entry of this handler (checks read this)

        // The circle moves with the caster (궁).
        protected virtual bool Follows => false;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            float life = row.F(LifeSpec.Key, 0f), radius = row.F(RadiusSpec.Key, 0f);
            if (!(life > 0f) || !(radius > 0f) || host.Player == null) return false;
            Vector3 centre = host.PlayerPosition;
            var zone = new SpellZone308
            {
                Owner = Id, Cast = Zones.NextCast(), Letter = cast.Letter, Element = cast.Element, Centre = centre, Radius = radius,
                From = ctx.Now, Until = ctx.Now + life, Life = life,
                Token = AttackProvenance.Create(Instigator(host), DamageSource.PersistentSpell, cast.Element).AttackId,
            };
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Zone, Element = cast.Element, Origin = centre, FallbackPoint = centre,
                Follow = Follows ? host.Player : null, ImpactClock = 0f, Duration = life, Radius = radius, Grade01 = cast.Brush01,
                AttackId = zone.Token,
            });
            zone.Fx = handle.Id;
            _slot.Take(this, ctx.Fx);                                   // the other glyph's domain goes first
            if (Place(zone, 1, ctx.Now, ctx.Fx) == 0) { ctx.Fx.End(handle, true); return true; }
            Placed(ctx, zone);
            return true;
        }

        protected override void Act(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            var zones = Zones.Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                if (zones[z].Owner != Id) continue;
                if (Follows && host != null && host.Player != null) Zones.Move(zones[z].Id, host.PlayerPosition);
                var zone = zones[z];
                if (!SpellZoneRule308.Active(zone, ctx.Now)) continue;
                Step(ctx, zone);
                if (host != null && host.Player != null) Player(ctx, zone, SpellZoneRule308.Inside(zone, host.PlayerPosition));
                var targets = host != null ? host.Targets : null;
                for (int i = 0; targets != null && i < targets.Count; i++)
                {
                    var enemy = targets[i];
                    if (enemy == null) continue;
                    bool inside = enemy.IsAlive && enemy.isActiveAndEnabled
                        && SpellZoneRule308.Covers(zone, enemy.transform.position, ctx.Now, enemy.BodyRadius);
                    Enemy(ctx, zone, enemy, inside);
                }
            }
        }

        // Default enemy rule: inside = the subclass writes its entries (Inside answers whether one was written), outside =
        // the entries of this domain on that enemy end now.
        protected virtual void Enemy(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy, bool inside)
        {
            if (!inside) { Holds.Release(enemy, zone.Token); return; }
            Holds.Hold(enemy, zone.Token, Inside(ctx, zone, enemy));
        }

        // What an enemy inside gets this frame (entries end at zone.Until at the latest). true = an entry was written.
        protected virtual bool Inside(in SpellTickContext ctx, in SpellZone308 zone, EnemyVitals enemy) => false;

        // The caster stands inside (or not) this frame.
        protected virtual void Player(in SpellTickContext ctx, in SpellZone308 zone, bool inside) { }

        // Once per frame per active domain, before the enemies.
        protected virtual void Step(in SpellTickContext ctx, in SpellZone308 zone) { }

        // The domain was just placed.
        protected virtual void Placed(in SpellCastContext ctx, in SpellZone308 zone) { }

        // The domain ended (ran out, replaced, yielded, cleared): nobody keeps its entries.
        protected virtual void Ended(in SpellZone308 zone) { }

        protected sealed override void OnRemoved(in SpellZone308 zone)
        {
            Holds.ReleaseAll(zone.Token);
            Ended(zone);
            if (Zones.CountOf(Id) == 0) _slot.Release(this);
        }

        public void Yield(ISpellPresenter fx) => DropAll(fx);

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            Holds.Clear();
            _slot.Release(this);
        }

        protected static SpellParamSpec[] With(params SpellParamSpec[] own)
        {
            var all = new SpellParamSpec[own.Length + 2];
            all[0] = RadiusSpec; all[1] = LifeSpec;
            for (int i = 0; i < own.Length; i++) all[i + 2] = own[i];
            return all;
        }
    }
}
