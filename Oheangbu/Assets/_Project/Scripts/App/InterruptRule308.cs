// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-05, handler circle.interrupt (SPEC-SPELL-120-308 section 13): the mitigated area interrupt.
    // The strike itself is the host's circle judgement with a reduced power; this rule says who is interrupted, when, and
    // what the control is. Boss rule = the giyeok precedent (Q8 default): a boss is slowed, never blocked.
    public static class InterruptRule308
    {
        // What one cast carries from its row to the moment one of its hits is confirmed.
        public readonly struct Spec
        {
            public readonly float Duration, BossSpeed;
            public Spec(float duration, float bossSpeed) { Duration = duration; BossSpeed = bossSpeed; }
        }

        // One enemy the cast interrupts when the circle's hit lands on it (At = the single impact time of the circle).
        public readonly struct Order
        {
            public readonly int TargetId;
            public readonly uint Life;
            public readonly float At;
            public Order(int targetId, uint life, float at) { TargetId = targetId; Life = life; At = at; }
        }

        // Strike power = the cast power times the row's factor (the glyph is the weak, wide interrupt).
        public static float Power(float castPower, float scale)
        {
            return castPower > 0f && scale > 0f ? castPower * scale : 0f;
        }

        // Every living enemy inside the circle. Dead slots and enemies outside get nothing.
        public static void Plan(SpellActorSnap[] actors, Vector3 centre, float radius, float impactAt, List<Order> orders)
        {
            if (actors == null || orders == null || !(radius > 0f)) return;
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor.Alive && AreaGeometry.InCircle(centre, actor.Position, radius, out _))
                    orders.Add(new Order(actor.Id, actor.Life, impactAt));
            }
        }

        // The control a confirmed hit applies. Ordinary enemy: no action and no movement until now + duration (its attack in
        // progress is cancelled by the caller). Boss: movement scaled, actions untouched. false = nothing is applied.
        public static bool Control(bool isBoss, bool killed, float now, in Spec spec, out float until, out float speed, out bool blocksActions)
        {
            until = now + spec.Duration;
            RootBindRule308.Resolve(isBoss, spec.BossSpeed, out speed, out blocksActions);
            return !killed && spec.Duration > 0f;
        }
    }
}
