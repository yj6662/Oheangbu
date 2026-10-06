// PURE308
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // A front that runs a corridor at constant speed (SPEC-SPELL-120-308 WP-03). The corridor is the one the wiring's path
    // judgement uses: start point, flat unit direction, half width, length. Outbound = from the start to the far end,
    // returning = from the far end back to the start. Later packages read the same type for anything that travels a path
    // (where the front is at a time, when it reaches a place).
    public readonly struct PathFront308
    {
        public readonly Vector3 Start;
        public readonly Vector3 Direction;
        public readonly float HalfWidth, Length, Speed, StartsAt;
        public readonly bool Returning;

        public PathFront308(Vector3 start, Vector3 direction, float halfWidth, float length, float speed, float startsAt, bool returning)
        {
            Vector3 flat = AreaGeometry.Flat(direction);
            Start = start;
            Direction = flat.sqrMagnitude > .0001f ? flat.normalized : Vector3.forward;
            HalfWidth = halfWidth; Length = length; Speed = speed; StartsAt = startsAt; Returning = returning;
        }

        public bool Valid => HalfWidth > 0f && Length > 0f && Speed > 0f && !float.IsNaN(StartsAt) && !float.IsInfinity(StartsAt);
        public float EndsAt => StartsAt + Length / Speed;
        public bool Started(float time) => time >= StartsAt;
        public bool Finished(float time) => time >= EndsAt;

        // Distance the front has run since it started, 0..Length.
        public float Travelled(float time)
        {
            if (!(time > StartsAt)) return 0f;
            if (time >= EndsAt) return Length;
            return Mathf.Clamp((time - StartsAt) * Speed, 0f, Length);
        }

        // Where the front is on the corridor axis (0 = start, Length = far end).
        public float Along(float time) => Returning ? Length - Travelled(time) : Travelled(time);
        public Vector3 Point(float time) => Start + Direction * Along(time);

        // When the front reaches a place on the axis (a place outside 0..Length is never reached).
        public float ArrivalAt(float along) => StartsAt + (Returning ? Length - along : along) / Speed;

        // The front is at this place of the axis, or already beyond it.
        public bool Reached(float along, float time) => Started(time) && (Returning ? along >= Along(time) : along <= Along(time));
    }

    public static class PathFrontRule308
    {
        // One judgement step at `time`, on the enemies as they are now. passed[i] = the front has dealt with actor i: it hit
        // the actor, or it went by while the actor stood outside the corridor, or the actor left the life the cast saw.
        // Such an actor is never hit by this front again, so every enemy is hit at most once whatever the frame rate, and an
        // enemy that walks against the front cannot slip through between two frames (the test is "the front has reached
        // you", not "you stand in the strip the front crossed this frame"). An enemy that steps into the corridor behind
        // the front is not hit: the front had already gone by.
        public static int Sweep(in PathFront308 front, SpellActorSnap[] atCast, SpellActorSnap[] now, bool[] passed, float time,
            float power, Element element, List<SpellHitOrder> orders)
        {
            if (!front.Valid || atCast == null || now == null || passed == null || orders == null || !front.Started(time)) return 0;
            int count = Mathf.Min(atCast.Length, Mathf.Min(now.Length, passed.Length)), added = 0;
            for (int i = 0; i < count; i++)
            {
                if (passed[i]) continue;
                // a life number only goes up: an actor that left the cast's life never comes back to it
                if (!TwoStageRule308.SameLife(atCast[i], now[i])) { passed[i] = true; continue; }
                bool inside = AreaGeometry.InCorridor(front.Start, front.Direction, now[i].Position, front.HalfWidth, front.Length, out float along, out _);
                if (!front.Reached(along, time)) continue;
                passed[i] = true;
                if (!inside || !(power > 0f)) continue;
                orders.Add(new SpellHitOrder(i, time, power, element));
                added++;
            }
            return added;
        }
    }
}
