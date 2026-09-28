using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Fixed body pose and left-to-right club arc. Targets retain the contact time of their captured bearing.
    public sealed class SummonClubAttackPlan
    {
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public float Range { get; }
        public float HalfAngleDegrees { get; }
        public float VerticalTolerance { get; }
        public float StartedAt { get; }
        public float SweepAt { get; }
        public float SweepEndAt { get; }
        public float EndAt { get; }
        public float SampleTime { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool IsSweeping => !IsCancelled && SampleTime >= SweepAt && SampleTime <= SweepEndAt;
        public bool IsFinished => IsCancelled || SampleTime >= EndAt;
        public float SweepAngle => Mathf.Lerp(-HalfAngleDegrees, HalfAngleDegrees,
            Mathf.InverseLerp(SweepAt, SweepEndAt, SampleTime));

        public SummonClubAttackPlan(Vector3 origin, Vector3 direction, float range, float halfAngle,
            float height, float startedAt, float windup, float sweep, float recovery)
        {
            if (!Finite(origin) || !Finite(direction) || !Positive(range) || !Positive(halfAngle) || halfAngle >= 90 ||
                !Positive(height) || !NonNegative(startedAt) || !NonNegative(windup) || !Positive(sweep) || !NonNegative(recovery))
                throw new ArgumentOutOfRangeException(nameof(range), "Club geometry and time must be finite and ordered.");
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (direction.sqrMagnitude < .000001f) throw new ArgumentException("Club requires a horizontal direction.");
            Origin = origin; Direction = direction.normalized; Range = range; HalfAngleDegrees = halfAngle;
            VerticalTolerance = height; StartedAt = SampleTime = startedAt;
            SweepAt = startedAt + windup; SweepEndAt = SweepAt + sweep; EndAt = SweepEndAt + recovery;
            if (!float.IsFinite(EndAt) || SweepEndAt <= SweepAt) throw new ArgumentOutOfRangeException(nameof(sweep));
        }
        public float SignedAngle(Vector3 point)
        {
            if (!Finite(point)) throw new ArgumentOutOfRangeException(nameof(point));
            var offset = Vector3.ProjectOnPlane(point - Origin, Vector3.up);
            return offset.sqrMagnitude < .000001f ? 0 : Vector3.SignedAngle(Direction, offset, Vector3.up);
        }
        public bool Contains(Vector3 point)
        {
            if (!Finite(point) || Mathf.Abs(point.y - Origin.y) > VerticalTolerance) return false;
            var offset = Vector3.ProjectOnPlane(point - Origin, Vector3.up);
            return offset.sqrMagnitude <= Range * Range + .00001f && Mathf.Abs(SignedAngle(point)) <= HalfAngleDegrees + .0001f;
        }
        public float ContactAt(Vector3 point)
        {
            if (!Contains(point)) throw new ArgumentOutOfRangeException(nameof(point), "Point lies outside the captured club sector.");
            return Mathf.Lerp(SweepAt, SweepEndAt, Mathf.InverseLerp(-HalfAngleDegrees, HalfAngleDegrees, SignedAngle(point)));
        }
        public void AdvanceTo(float time)
        {
            if (!float.IsFinite(time) || time < SampleTime) throw new ArgumentOutOfRangeException(nameof(time));
            SampleTime = time;
        }
        public void Cancel() { IsCancelled = true; }
        static bool Positive(float v) => float.IsFinite(v) && v > 0;
        static bool NonNegative(float v) => float.IsFinite(v) && v >= 0;
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
