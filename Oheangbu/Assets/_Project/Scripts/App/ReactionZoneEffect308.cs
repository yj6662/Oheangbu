using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // zone.react (#308 DECISIONS D308-128 B, D308-132): five-element reactions between the player's lingering zones and a
    // later spell. At the end of every accepted cast's dispatch (ISpellDispatchHook) the cast's ground (its plans, flat) is laid
    // over the zone list; a zone that is material (ReactionRule308.Eligible) and whose element pairs with the cast's element in
    // the reaction table is consumed (SpellZoneRuntime308.React: it stops at once, its owner sweeps it and takes back its slow
    // or blur) and this handler lays the product: a wildfire that hurts, a mud that slows, a steam with no rule. A product's
    // damage is damage over time (PersistentSpell): no detonation of an installed mark, no five-element completion, no groggy.
    // No row names this handler: it is never cast. The table comes from Resources (SpellReactionSet308SO); without it nothing
    // reacts.
    public sealed class ReactionZoneEffect308 : ZoneEffect308, ISpellDispatchHook
    {
        public const string HandlerId = ReactionRule308.ProductOwner;
        const int MaxProducts = 3;   // D308-133 guard: at most three products at once (a fourth replaces the oldest)
        static readonly SpellParamSpec[] Specs = Array.Empty<SpellParamSpec>();

        IReadOnlyList<ReactionRow308> _rows;
        bool _looked;
        readonly List<int> _candidates = new List<int>();
        readonly List<ReactionFootprint308> _feet = new List<ReactionFootprint308>();

        struct Held { public EnemyVitals Enemy; public long Token; }
        readonly List<Held> _held = new List<Held>();   // enemies slowed by a mud of this handler

        public ReactionZoneEffect308() : this(null) { }
        public ReactionZoneEffect308(SpellZoneRuntime308 zones) : base(zones) { }

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public IReadOnlyList<ReactionRow308> Rows => Table();

        // Checks hand the table in directly (null = read Resources again).
        public void UseTable(IReadOnlyList<ReactionRow308> rows) { _rows = rows; _looked = rows != null; }

        public override bool Prepare(in SpellCastContext ctx) => false;
        public override bool Commit(in SpellCastContext ctx) => false;

        public void OnCastDispatched(in SpellCastContext ctx, IReadOnlyList<CastPlan> plans)
        {
            var rows = Table();
            var host = ctx.Host;
            if (rows == null || rows.Count == 0 || host == null || plans == null) return;
            _feet.Clear();
            // only the cast's own plans: another effect that plans inside the same dispatch does not lend its ground
            for (int i = 0; i < plans.Count; i++)
                if (plans[i] != null && plans[i].Cast.Letter == ctx.Cast.Letter) AddFootprints(plans[i], _feet);
            if (_feet.Count == 0) return;
            float now = ctx.Now;
            Vector3 caster = host.PlayerPosition;
            Element trigger = ctx.Cast.Element;
            // the zones of the list as they stand now: a product laid below is never material of this same cast
            _candidates.Clear();
            var zones = Zones.Zones;
            for (int z = 0; z < zones.Count; z++)
            {
                var zone = zones[z];
                if (ReactionRule308.Find(rows, zone.Element, trigger) == null || !ReactionRule308.Eligible(zone, now, caster, ctx.Attacks)) continue;
                for (int f = 0; f < _feet.Count; f++)
                    if (ReactionRule308.Overlaps(_feet[f], zone)) { _candidates.Add(zone.Id); break; }
            }
            for (int i = 0; i < _candidates.Count; i++) React(ctx, rows, _candidates[i]);
        }

        void React(in SpellCastContext ctx, IReadOnlyList<ReactionRow308> rows, int id)
        {
            SpellZone308 material = default; bool found = false;
            var zones = Zones.Zones;
            for (int z = 0; z < zones.Count; z++) if (zones[z].Id == id) { material = zones[z]; found = true; break; }
            if (!found) return;
            var row = ReactionRule308.Find(rows, material.Element, ctx.Cast.Element);
            if (row == null || !Zones.React(id, ctx.Now)) return;
            var host = ctx.Host;
            var product = ReactionRule308.Product(row, material, ctx.Now, ctx.Cast.Power, Zones.NextCast());
            product.Token = AttackProvenance.Create(Instigator(host), DamageSource.PersistentSpell, row.Product).AttackId;
            if (row.Kind == ReactionProductKind308.Damage) product.Value *= host.DamageScale(row.Product);
            HitMaterial(ctx, row, material, product.Token);
            if (product.Life > 0f) Place(product, MaxProducts, ctx.Now, ctx.Fx);
        }

        // The instant blow of a breaking reaction: every enemy standing in the material (body edge counts), once.
        void HitMaterial(in SpellCastContext ctx, ReactionRow308 row, in SpellZone308 material, long token)
        {
            var host = ctx.Host;
            float power = ReactionRule308.HitPower(row, ctx.Cast.Power);
            var targets = host.Targets;
            if (power <= 0f || targets == null) return;
            power *= host.DamageScale(row.Product);
            var attack = new AttackProvenance(token, Instigator(host), DamageSource.PersistentSpell, row.Product);
            for (int i = 0; i < targets.Count; i++)
            {
                var enemy = targets[i];
                if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled || !SpellZoneRule308.Inside(material, enemy.transform.position, enemy.BodyRadius)) continue;
                host.ApplyPersistentHit(enemy, power, enemy.transform.position + Vector3.up * .4f, attack, ctx.Cast.Letter);
            }
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
                if (zone.TickEvery > 0f)
                {
                    // wildfire: the spikes' steps (SpikeZoneEffect308)
                    int due = Zones.Ticks(zone.Id, ctx.Now);
                    if (due <= 0 || zone.Value <= 0f) continue;
                    var attack = new AttackProvenance(zone.Token, Instigator(host), DamageSource.PersistentSpell, zone.Element);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var enemy = targets[i];
                        if (enemy == null || !enemy.IsAlive || !enemy.isActiveAndEnabled || !SpellZoneRule308.Inside(zone, enemy.transform.position)) continue;
                        host.ApplyPersistentHit(enemy, zone.Value * due, enemy.transform.position + Vector3.up * .4f, attack, zone.Letter);
                    }
                }
                else if (zone.Value > 0f && zone.Value < 1f)
                {
                    // mud: the vines' slow (VineZoneEffect308)
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
        }

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

        // The ground of one plan: a circle, a corridor, a cone, or the point a single cast hits. A volley is not an area.
        static void AddFootprints(CastPlan plan, List<ReactionFootprint308> into)
        {
            if (plan == null) return;
            var area = plan.Area;
            if (area == null)
            {
                for (int i = 0; i < plan.Hits.Count; i++)
                {
                    var hit = plan.Hits[i];
                    if (hit.HasImpactPoint) into.Add(ReactionFootprint308.Point(hit.ImpactPoint));
                    else if (hit.Target != null) into.Add(ReactionFootprint308.Point(hit.Target.transform.position));
                }
                return;
            }
            switch (area.Shape)
            {
                case AreaShape.Circle: into.Add(ReactionFootprint308.Circle(area.Point, area.Radius)); break;
                case AreaShape.Path: into.Add(ReactionFootprint308.Corridor(area.Point, area.Direction, area.Radius, area.Length)); break;
                case AreaShape.Cone: into.Add(ReactionFootprint308.Cone(area.Point, area.Direction, area.Angle, area.Length)); break;
            }
        }

        IReadOnlyList<ReactionRow308> Table()
        {
            if (_looked) return _rows;
            _looked = true;
            var set = Resources.Load<SpellReactionSet308SO>(SpellReactionSet308SO.ResourcesPath);
            if (set == null) return _rows = null;
            var errors = new List<string>();
            _rows = ReactionTable308.Parse(set.Csv, errors);
            for (int i = 0; i < errors.Count; i++) Debug.LogError("[Spell308] reaction table: " + errors[i], set);
            return _rows;
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
