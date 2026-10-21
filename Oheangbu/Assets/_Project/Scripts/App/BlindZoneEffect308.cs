using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // zone.blind (SPEC-SPELL-120-308 WP-15, D308-113; 몽 = the earth path + ieung). Split from zone.haze, which 농 keeps.
    // The first hit is the base glyph's own path judgement, unchanged. Where it went, a muddy cloud stays for haze.life
    // seconds: one circle of haze.radius at the middle of the path (HazeRule308). An enemy standing in it is blinded
    // (EnemyStrikeVeil308.Blind: a field enemy's brain loses the player and falls back to the last known place, then
    // searches) and haze.miss of its strikes miss. A boss (EnemyVitals.IsBoss, the field bosses' brains included) or an enemy
    // without a brain is not blinded in its behaviour but still misses that share: no enemy is immune. An enemy that walks
    // out sees again at once.
    // No damage of its own, no control, no weak point. It takes the player away from the enemy; it gives the player nothing.
    public sealed class BlindZoneEffect308 : ZoneEffect308
    {
        public const string HandlerId = "zone.blind";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("haze.radius", 0.5f, 30f), new SpellParamSpec("haze.life", 0.5f, 120f),
            new SpellParamSpec("haze.miss", 0f, 1f), new SpellParamSpec("haze.max", 1f, 8f),
        };

        readonly VeilHolds308 _holds = new VeilHolds308();

        public BlindZoneEffect308() : this(null) { }
        public BlindZoneEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public int BlindedEnemies => _holds.Count;          // enemies that carry an entry of one of this handler's clouds (checks read this)

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            // the first hit: today's judgement of the row's own shape (the path; a cone row would spray a cone)
            CastPlan plan = cast.AreaShape == AreaShape.Cone ? host.PlanCone(cast, false) : host.PlanPath(cast, false);
            if (plan == null || plan.Area == null) return false;
            float life = row.F("haze.life", 0f), radius = row.F("haze.radius", 0f);
            float from = ctx.Now + plan.Area.Delay;
            Vector3 centre = HazeRule308.Centre(plan.Area.Point, plan.Area.Direction, plan.Area.Length);
            var zone = new SpellZone308
            {
                Owner = HandlerId, Cast = Zones.NextCast(), Letter = cast.Letter, Element = cast.Element, Centre = centre, Radius = radius,
                From = from, Until = from + life, Life = life, Value = row.F("haze.miss", 0f),
                Token = AttackProvenance.Create(Instigator(host), DamageSource.PersistentSpell, cast.Element).AttackId,
            };
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Zone, Element = cast.Element, Origin = plan.Area.Point, FallbackPoint = centre,
                ImpactClock = plan.Area.Delay, Duration = plan.Area.Delay + life, Radius = radius, Grade01 = cast.Brush01, Area = plan.Area,
                AttackId = zone.Token,
            });
            zone.Fx = handle.Id;
            // the first hit is already scheduled: a refused zone (cannot happen for a fresh cast) only loses the lingering part
            if (Place(zone, Mathf.RoundToInt(row.F("haze.max", 1f)), ctx.Now, ctx.Fx) == 0) ctx.Fx.End(handle, true);
            return true;
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
                    bool inside = enemy.IsAlive && enemy.isActiveAndEnabled && SpellZoneRule308.Covers(zone, enemy.transform.position, ctx.Now, enemy.BodyRadius);   // WP-16: body edge counts
                    if (!inside) { _holds.Release(enemy, zone.Token); continue; }
                    // one owner, two entries (blind, blur): Release and ReleaseAll take both back by owner
                    _holds.Blind(enemy, zone.Token, zone.Until);
                    float share = Mathf.Clamp01(zone.Value);
                    if (share > 0f) _holds.Blur(enemy, zone.Token, zone.Until, share);
                }
            }
        }

        // The cloud is gone: nobody keeps its blindness or its blur.
        protected override void OnRemoved(in SpellZone308 zone) { _holds.ReleaseAll(zone.Token); }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _holds.Clear();
        }
    }
}
