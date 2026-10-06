// PURE308
using UnityEngine;

namespace Oheangbu.App
{
    public enum HomingStatus308 { Flying, Hit, Missed }

    // One slow guided shot in flight (SPEC-SPELL-120-308 WP-11, handler single.homing).
    public struct HomingShot308
    {
        public Vector3 Position;
        public Vector3 Direction;        // unit vector
        public float Turned;             // degrees turned so far
        public float Age;                // seconds in flight
        public HomingStatus308 Status;
    }

    // The slow guided shot: it leaves towards the spot its target stands on, then steers after the target frame by frame.
    // Steering is limited twice: turn.rate degrees per second, and turn.limit degrees in total over the whole flight. A
    // shot that has used up its total keeps flying straight; when its target is no longer ahead of it, or when it has
    // flown flight.max seconds, it has missed. No Quaternion call here: the offline runner executes this file.
    public static class HomingRule308
    {
        const float Tiny = 1e-4f;

        public static HomingShot308 Launch(Vector3 origin, Vector3 targetAtCast, Vector3 fallbackForward)
        {
            Vector3 to = targetAtCast - origin;
            Vector3 direction = to.sqrMagnitude > Tiny ? to.normalized : fallbackForward.sqrMagnitude > Tiny ? fallbackForward.normalized : Vector3.forward;
            return new HomingShot308 { Position = origin, Direction = direction, Status = HomingStatus308.Flying };
        }

        // One step of dt seconds towards where the target is now. Returns the status after the step.
        public static HomingStatus308 Step(ref HomingShot308 shot, Vector3 target, float dt, float speed, float turnRate, float turnLimit,
            float hitRadius, float maxAge)
        {
            if (shot.Status != HomingStatus308.Flying || !(dt > 0f) || !(speed > 0f)) return shot.Status;
            Vector3 to = target - shot.Position;
            float distance = to.magnitude;
            if (distance <= hitRadius) { shot.Status = HomingStatus308.Hit; return shot.Status; }

            // steer: at most turnRate x dt this step, at most what is left of turnLimit
            Vector3 want = to / distance;
            float angle = Vector3.Angle(shot.Direction, want);
            float budget = Mathf.Max(0f, turnLimit - shot.Turned);
            float turn = Mathf.Min(angle, Mathf.Min(Mathf.Max(0f, turnRate) * dt, budget));
            if (turn > 0f)
            {
                shot.Direction = Towards(shot.Direction, want, angle, turn);
                shot.Turned += turn;
            }

            // fly: the closest point of this step's segment decides the hit, so a long frame cannot jump over the target
            float step = speed * dt;
            float along = Vector3.Dot(to, shot.Direction);
            float reach = Mathf.Clamp(along, 0f, step);
            Vector3 closest = shot.Position + shot.Direction * reach;
            if ((target - closest).magnitude <= hitRadius)
            {
                shot.Position = closest; shot.Age += reach / speed; shot.Status = HomingStatus308.Hit;
                return shot.Status;
            }
            shot.Position += shot.Direction * step;
            shot.Age += dt;
            bool spent = shot.Turned >= turnLimit - Tiny;
            bool behind = Vector3.Dot(target - shot.Position, shot.Direction) <= 0f;
            if (shot.Age >= maxAge || (spent && behind)) shot.Status = HomingStatus308.Missed;
            return shot.Status;
        }

        // `from` turned by `turn` degrees towards `to` (both unit vectors, `angle` degrees apart).
        static Vector3 Towards(Vector3 from, Vector3 to, float angle, float turn)
        {
            if (turn >= angle) return to;
            float omega = angle * Mathf.Deg2Rad, sine = Mathf.Sin(omega);
            if (sine > Tiny)
            {
                float t = turn / angle;
                Vector3 mixed = from * (Mathf.Sin((1f - t) * omega) / sine) + to * (Mathf.Sin(t * omega) / sine);
                return mixed.sqrMagnitude > Tiny ? mixed.normalized : to;
            }
            // the target is straight behind: turn in the horizontal plane (any side; the left is taken)
            Vector3 side = Vector3.Cross(Vector3.up, from);
            if (side.sqrMagnitude < Tiny) side = Vector3.Cross(Vector3.right, from);
            side = side.normalized;
            float radians = turn * Mathf.Deg2Rad;
            return (from * Mathf.Cos(radians) + side * Mathf.Sin(radians)).normalized;
        }
    }
}
