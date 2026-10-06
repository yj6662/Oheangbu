using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // zone.spikes (SPEC-SPELL-120-308 WP-06): the first hit is the wood circle's own judgement (same circle, delay and power;
    // the staggered rise is that glyph's own trait and is not copied). The risen spikes then stay for the zone's life and hurt every enemy standing
    // in the circle, one step every zone.tick seconds. A step is damage over time (PersistentSpell source): it never feeds
    // the five-element completion or groggy. zone.dps = damage per second as a share of the cast power.
    public sealed class SpikeZoneEffect308 : ZoneEffect308
    {
        public const string HandlerId = "zone.spikes";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("zone.life", 0.5f, 120f), new SpellParamSpec("zone.dps", 0f, 10f),
            new SpellParamSpec("zone.tick", 0.05f, 10f), new SpellParamSpec("zone.max", 1f, 8f),
        };

        public SpikeZoneEffect308() : this(null) { }
        public SpikeZoneEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            CastPlan plan = host.PlanCircle(cast, false);                          // the first hit: today's circle judgement
            if (plan == null || plan.Area == null) return false;
            float life = row.F("zone.life", 0f), tick = row.F("zone.tick", 0f);
            float from = ctx.Now + plan.Area.Delay;
            var zone = new SpellZone308
            {
                Owner = HandlerId, Cast = Zones.NextCast(), Letter = cast.Letter, Element = cast.Element,
                Centre = plan.Area.Point, Radius = plan.Area.Radius, From = from, Until = from + life, Life = life, TickEvery = tick,
                // power of one step; the equipment scale is taken at cast time, like a scheduled hit
                Value = SpellZoneRule308.StepPower(cast.Power, row.F("zone.dps", 0f), tick) * host.DamageScale(cast.Element),
                Token = AttackProvenance.Create(Instigator(host), DamageSource.PersistentSpell, cast.Element).AttackId,
            };
            host.PresentationOwned();
            EnemyVitals aimed = plan.Hits.Count > 0 ? plan.Hits[0].Target : null;
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Zone, Element = cast.Element, Origin = zone.Centre, FallbackPoint = zone.Centre,
                Target = aimed != null ? aimed.transform : null, ImpactClock = plan.Area.Delay, Duration = plan.Area.Delay + life,
                Radius = zone.Radius, Grade01 = cast.Brush01, Area = plan.Area, AttackId = zone.Token,
            });
            zone.Fx = handle.Id;
            // the first hit is already scheduled: a refused zone (cannot happen for a fresh cast) only loses the lingering part
            if (Place(zone, Mathf.RoundToInt(row.F("zone.max", 1f)), ctx.Now, ctx.Fx) == 0) ctx.Fx.End(handle, true);
            return true;
        }

        protected override void Act(in SpellTickContext ctx)
        {
            var host = ctx.Host;
            var targets = host != null ? host.Targets : null;
            if (targets == null) return;
            var zones = Zones.Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                var zone = zones[z];
                if (zone.Owner != HandlerId) continue;
                int due = Zones.Ticks(zone.Id, ctx.Now);
                if (due <= 0 || zone.Value <= 0f) continue;
                var attack = new AttackProvenance(zone.Token, Instigator(host), DamageSource.PersistentSpell, zone.Element);
                for (int i = 0; i < targets.Count; i++)
                {
                    var enemy = targets[i];
                    if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled || !SpellZoneRule308.Inside(zone, enemy.transform.position)) continue;
                    // the spikes are under the enemy's feet: the origin is the enemy's own spot, nothing stands in between
                    host.ApplyPersistentHit(enemy, zone.Value * due, enemy.transform.position + Vector3.up * .4f, attack, zone.Letter);
                }
            }
        }
    }
}
