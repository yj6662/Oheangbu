// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-05, handler path.undertow (SPEC-SPELL-120-308 section 13): the sinking flood. The strike is the host's
    // corridor judgement (the advancing front of the base glyph); this rule says who the front sweeps, when, and what the
    // sweep does: every swept enemy is slowed for a while, and an enemy that is in the air is pulled to the ground for the
    // same time (question Q5: the pull-down is built against a TEST target; no enemy of the game flies yet).
    public static class UndertowRule308
    {
        // What one cast carries from its row to the moment one of its hits is confirmed.
        public readonly struct Spec
        {
            public readonly float Duration, SlowSpeed;
            public Spec(float duration, float slowSpeed) { Duration = duration; SlowSpeed = slowSpeed; }
        }

        // One enemy the front sweeps (At = the time the front reaches it).
        public readonly struct Order
        {
            public readonly int TargetId;
            public readonly uint Life;
            public readonly float At;
            public Order(int targetId, uint life, float at) { TargetId = targetId; Life = life; At = at; }
        }

        // Every living enemy inside the corridor, at the time the front reaches it: cast time + rise delay + distance along
        // the corridor / front speed.
        public static void Plan(SpellActorSnap[] actors, Vector3 start, Vector3 direction, float halfWidth, float length, float speed,
            float castAt, float delay, List<Order> orders)
        {
            if (actors == null || orders == null || !(speed > 0f) || !(length > 0f) || !(halfWidth > 0f)) return;
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (!actor.Alive || !AreaGeometry.InCorridor(start, direction, actor.Position, halfWidth, length, out float along, out _)) continue;
                orders.Add(new Order(actor.Id, actor.Life, castAt + delay + along / speed));
            }
        }

        // What a confirmed sweep does. speed = the movement scale until now + duration (never a block: the enemy still acts).
        // pullDown = the enemy was in the air: it is grounded until the same time. false = nothing is applied.
        public static bool Sweep(bool airborne, bool killed, float now, in Spec spec, out float until, out float speed, out bool pullDown)
        {
            until = now + spec.Duration;
            speed = Mathf.Clamp01(spec.SlowSpeed);
            pullDown = airborne;
            return !killed && spec.Duration > 0f;
        }
    }
}
