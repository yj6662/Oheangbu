using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // cone.healzone (SPEC-SPELL-120-308 WP-06): the first hit is the fire cone's own judgement. Where a hit of this cast
    // really lands on an enemy (applied damage, heard through the hit hook by its attack id), a healing zone opens at that
    // enemy's spot; the player regains HP while standing in one. A cast that hits nobody leaves no zone. This is a reward
    // for landing a hit, never for being hit. Zones of this handler do not add up: standing in two heals like one.
    public sealed class HealZoneEffect308 : ZoneEffect308, ISpellHitHook
    {
        public const string HandlerId = "cone.healzone";
        static readonly SpellParamSpec[] Specs =
        {
            new SpellParamSpec("heal.radius", 0.5f, 30f), new SpellParamSpec("heal.life", 0.5f, 120f),
            new SpellParamSpec("heal.hp01ps", 0f, 1f), new SpellParamSpec("heal.max", 1f, 8f),
        };

        // A scheduled hit of one of this handler's casts that has not landed yet.
        struct Expected
        {
            public long AttackId; public float At, Radius, Life, Rate;
            public int Cast, Max, Fx; public char Letter; public Element Element;
        }
        readonly List<Expected> _expected = new List<Expected>();

        public HealZoneEffect308() : this(null) { }
        public HealZoneEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;

        public override bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host; var cast = ctx.Cast; var row = ctx.Row;
            CastPlan plan = host.PlanCone(cast, false);                            // the first hit: today's cone judgement
            if (plan == null || plan.Area == null) return false;
            float life = row.F("heal.life", 0f);
            host.PresentationOwned();
            var handle = ctx.Fx.Begin(new SpellFxRequest
            {
                Letter = cast.Letter, Role = SpellFxRole.Cast, Element = cast.Element, Origin = plan.Area.Point,
                FallbackPoint = plan.Area.Point + plan.Area.Direction * plan.Area.Length, ImpactClock = plan.Area.Delay,
                Duration = plan.Area.Delay + life, Radius = row.F("heal.radius", 0f), Grade01 = cast.Brush01, Area = plan.Area,
            });
            int serial = Zones.NextCast();
            // a cast into thin air schedules nothing, so nothing is expected and no zone can ever open
            for (int i = 0; i < plan.Hits.Count; i++)
            {
                if (plan.Hits[i].AttackId <= 0) continue;
                _expected.Add(new Expected
                {
                    AttackId = plan.Hits[i].AttackId, At = plan.Hits[i].ImpactTime, Radius = row.F("heal.radius", 0f), Life = life,
                    Rate = row.F("heal.hp01ps", 0f), Cast = serial, Max = Mathf.RoundToInt(row.F("heal.max", 1f)), Fx = handle.Id,
                    Letter = cast.Letter, Element = cast.Element,
                });
            }
            return true;
        }

        // A hit of one of this handler's casts landed (the wiring only reports applied damage): its spot becomes a healing zone.
        public void OnEnemyHit(in EnemyDamageResult result, in SpellTickContext ctx)
        {
            long id = result.Attack.AttackId;
            for (int i = 0; i < _expected.Count; i++)
            {
                if (_expected[i].AttackId != id) continue;
                var expected = _expected[i];
                _expected.RemoveAt(i);
                if (result.Target == null) return;
                Vector3 spot = result.Target.transform.position;
                var zone = new SpellZone308
                {
                    Owner = HandlerId, Cast = expected.Cast, Letter = expected.Letter, Element = expected.Element, Centre = spot,
                    Radius = expected.Radius, From = ctx.Now, Until = ctx.Now + expected.Life, Life = expected.Life, Value = expected.Rate, Fx = expected.Fx,
                };
                if (Place(zone, expected.Max, ctx.Now, ctx.Fx) != 0 && expected.Fx != 0)
                    ctx.Fx.Cue(new SpellFxHandle(expected.Fx), SpellFxCue.Secondary,
                        new SpellFxCueArgs { Target = result.Target.transform, Point = spot, At = ctx.Now, Value = expected.Radius });
                return;
            }
        }

        protected override void Act(in SpellTickContext ctx)
        {
            // hits that never landed (the target died first, or was replaced) stop being expected once their zone would be over
            for (int i = _expected.Count - 1; i >= 0; i--)
                if (ctx.Now > _expected[i].At + _expected[i].Life) _expected.RemoveAt(i);
            var player = ctx.Host != null ? ctx.Host.PlayerVitals : null;
            if (player == null) return;
            float share = SpellZoneRule308.HealShare(Zones.Zones, HandlerId, ctx.Host.PlayerPosition, ctx.Now - ctx.Delta, ctx.Now);
            if (share > 0f) player.Heal(player.MaxHp * share);
        }

        public override void Clear(SpellClearReason reason)
        {
            base.Clear(reason);
            _expected.Clear();
        }
    }
}
