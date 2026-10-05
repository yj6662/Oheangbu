using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App
{
    // WP-08 earth buff with the nieun final: an enemy that strikes the player in melee while it runs takes one hit back
    // (DamageSource.Retaliation: no groggy, no five-element credit). Retaliation only: the player-hit hook records an order
    // and gives the player nothing; the order is delivered on the next Tick, outside the enemy's own strike call.
    public sealed class ReflectEffect308 : SelfBuffEffect308, IPlayerHitHook
    {
        public const string HandlerId = "buff.reflect";
        static readonly SpellParamSpec[] Specs = { new SpellParamSpec("duration", .1f, 600f), new SpellParamSpec("reflect.power", 0f, 1000f) };

        struct Pending { public int Id; public uint Life; public float Power; }
        readonly List<Pending> _pending = new List<Pending>();
        readonly List<SpellHitOrder> _orders = new List<SpellHitOrder>();
        SpellActorSnap[] _snaps;
        float _power;

        public override string Id => HandlerId;
        public override IReadOnlyList<SpellParamSpec> Params => Specs;
        public int PendingCount => _pending.Count;

        protected override bool OnActivating(in SpellCastContext ctx, float duration)
        {
            _power = ctx.Row.F("reflect.power", 0f);
            return true;
        }

        public void OnPlayerHit(in PlayerHitInfo hit, in SpellTickContext ctx, ref bool interruptsDrawing)
        {
            // every hit on the player comes through here, buff or not: leave before anything is looked at
            if (ctx.Host == null || hit.Attacker == null || !Active(ctx.Now) || !Melee(hit.Kind)) return;
            var targets = ctx.Host.Targets;
            var actors = SpellSnapshots308.Fill(targets, ref _snaps);
            _orders.Clear();
            BuffRule308.Reflect(true, true, actors, SpellSnapshots308.IndexOf(targets, hit.Attacker), ctx.Now, _power, Element, _orders);
            foreach (var order in _orders)
                _pending.Add(new Pending { Id = order.TargetId, Life = actors[order.TargetId].Life, Power = order.Power });
        }

        protected override void OnTick(in SpellTickContext ctx)
        {
            if (_pending.Count == 0 || ctx.Host == null) return;
            var targets = ctx.Host.Targets;
            var instigator = Instigator(ctx.Host);
            Vector3 origin = ctx.Host.PlayerPosition + Vector3.up * .4f;
            for (int i = 0; i < _pending.Count; i++)
            {
                var order = _pending[i];
                var target = SpellSnapshots308.Target(targets, order.Id, order.Life);   // null when that life is over
                if (target == null || instigator == null) continue;
                ctx.Host.ApplyDirectHit(target, order.Life, order.Power * ctx.Host.DamageScale(Element), origin,
                    AttackProvenance.Create(instigator, DamageSource.Retaliation, Element), Letter);
            }
            _pending.Clear();
        }

        protected override void OnCleared(SpellClearReason reason) { _pending.Clear(); _orders.Clear(); _power = 0f; }
    }
}
