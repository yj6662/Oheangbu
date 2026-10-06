using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // zone.slow (SPEC-SPELL-120-308 WP-06): a circle of vines stays on the ground for its life and slows every enemy inside.
    // The centre is the wood circle's (the aimed enemy, else the aim point at the row's range); it forms after the row's
    // formation delay. No hit of its own (section 15 default). An enemy that walks out moves at full speed again at once.
    // The slow is one EnemyControlState entry per zone (owner id = the zone's token): no action block, no weak point,
    // no damage bonus, and bosses are slowed like anyone else (a slow is already the mild form).
    public sealed class VineZoneEffect308 : ZoneEffect308
    {
        public const string HandlerId = "zone.slow";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("zone.radius", 0.5f, 30f), new SpellParamSpec("zone.life", 0.5f, 120f),
            new SpellParamSpec("slow.speed", 0.05f, 1f), new SpellParamSpec("zone.max", 1f, 8f),
        };

        struct Held { public EnemyVitals Enemy; public long Token; }
        readonly List<Held> _held = new List<Held>();   // enemies that carry a slow of one of this handler's zones

        public VineZoneEffect308() : this(null) { }
        public VineZoneEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            EnemyVitals aimed = host.AimedTarget();
            Vector3 centre = aimed != null ? aimed.transform.position : host.AimPoint(cast.Area.Length);
            float delay = cast.Area.ImpactDelay > 0f ? cast.Area.ImpactDelay : host.Config != null ? host.Config.AreaImpactDelay : 0f;
            float life = row.F("zone.life", 0f), radius = row.F("zone.radius", 0f);
            var zone = new SpellZone308
            {
                Owner = HandlerId, Cast = Zones.NextCast(), Letter = cast.Letter, Element = cast.Element, Centre = centre, Radius = radius,
                From = ctx.Now + delay, Until = ctx.Now + delay + life, Life = life, Value = row.F("slow.speed", 1f),
                Token = AttackProvenance.Create(Instigator(host), DamageSource.PersistentSpell, cast.Element).AttackId,
            };
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Zone, Element = cast.Element, Origin = centre, FallbackPoint = centre,
                Target = aimed != null ? aimed.transform : null, ImpactClock = delay, Duration = delay + life, Radius = radius,
                Grade01 = cast.Brush01, Area = new AreaImpactPlan { Shape = AreaShape.Circle, Point = centre, Radius = radius, Delay = delay },
                AttackId = zone.Token,
            });
            zone.Fx = handle.Id;
            if (Place(zone, Mathf.RoundToInt(row.F("zone.max", 1f)), ctx.Now, ctx.Fx) != 0) return true;
            ctx.Fx.End(handle, true);
            return false;
        }

        protected override void Act(in SpellTickContext ctx)
        {
            var targets = ctx.Host != null ? ctx.Host.Targets : null;
            if (targets == null) return;
            var zones = Zones.Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                var zone = zones[z];
                if (zone.Owner != HandlerId) continue;
                for (int i = 0; i < targets.Count; i++)
                {
                    var enemy = targets[i];
                    if (enemy == null) continue;
                    float scale = enemy.IsAlive && enemy.isActiveAndEnabled ? SpellZoneRule308.SlowScale(zone, enemy.transform.position, ctx.Now) : 1f;
                    if (scale < 1f) { enemy.Control.Apply(zone.Token, zone.Until, scale, false); Hold(enemy, zone.Token); }
                    else Release(enemy, zone.Token);
                }
            }
        }

        // The zone is gone: nobody keeps its slow.
        protected override void OnRemoved(in SpellZone308 zone)
        {
            for (int i = _held.Count - 1; i >= 0; i--)
            {
                if (_held[i].Token != zone.Token) continue;
                if (_held[i].Enemy != null) _held[i].Enemy.Control.Remove(zone.Token);
                _held.RemoveAt(i);
            }
        }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy != null) _held[i].Enemy.Control.Remove(_held[i].Token);
            _held.Clear();
        }

        void Hold(EnemyVitals enemy, long token)
        {
            for (int i = 0; i < _held.Count; i++)
                if (_held[i].Enemy == enemy && _held[i].Token == token) return;
            _held.Add(new Held { Enemy = enemy, Token = token });
        }

        void Release(EnemyVitals enemy, long token)
        {
            for (int i = 0; i < _held.Count; i++)
            {
                if (_held[i].Enemy != enemy || _held[i].Token != token) continue;
                enemy.Control.Remove(token);
                _held.RemoveAt(i);
                return;
            }
        }
    }
}
