using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    /// <summary>Fixed mouth ray and finite cylindrical corridor; only sample time and wall clipping advance.</summary>
    public sealed class SummonWaterAttackPlan
    {
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public float Range { get; }
        public float Radius { get; }
        public float TravelSpeed { get; }
        public float StartedAt { get; }
        public float ReleaseAt { get; }
        public float StreamEndAt { get; }
        public float EndAt { get; }
        public float SampleTime { get; private set; }
        public float ClearDistance { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool IsReleased => !IsCancelled && SampleTime >= ReleaseAt;
        public bool IsStreaming => IsReleased && SampleTime < StreamEndAt;
        public bool IsFinished => IsCancelled || SampleTime >= EndAt;
        public float FrontDistance => Mathf.Min(ClearDistance, Mathf.Max(0, SampleTime - ReleaseAt) * TravelSpeed);
        public float TailDistance => Mathf.Min(ClearDistance, Mathf.Max(0, SampleTime - StreamEndAt) * TravelSpeed);
        public bool IsVisible => !IsCancelled && IsReleased && TailDistance < FrontDistance;
        public Vector3 VisibleStart => Origin + Direction * TailDistance;
        public Vector3 Head => Origin + Direction * FrontDistance;
        public Vector3 VisibleEnd => Head;

        public SummonWaterAttackPlan(Vector3 origin, Vector3 direction, float range, float radius, float travelSpeed,
            float startedAt, float windup, float stream, float recovery)
        {
            if (!Finite(origin) || !Finite(direction) || !float.IsFinite(direction.sqrMagnitude) || direction.sqrMagnitude < .0001f ||
                !Positive(range) || !Positive(radius) || radius >= range || !Positive(travelSpeed) ||
                !NonNegative(startedAt) || !NonNegative(windup) || !Positive(stream) || !NonNegative(recovery) ||
                range / travelSpeed >= stream || recovery < range / travelSpeed)
                throw new ArgumentException("Water geometry and timing must be finite, bounded, and allow front travel and tail cleanup.");
            Origin = origin; Direction = direction.normalized; Range = ClearDistance = range; Radius = radius; TravelSpeed = travelSpeed;
            StartedAt = SampleTime = startedAt; ReleaseAt = startedAt + windup; StreamEndAt = ReleaseAt + stream; EndAt = StreamEndAt + recovery;
            if (!float.IsFinite(EndAt)) throw new ArgumentException("Water timeline overflow.");
        }
        public float Longitudinal(Vector3 point) => Vector3.Dot(point - Origin, Direction);
        public bool Contains(Vector3 point)
        {
            if (!Finite(point)) return false;
            float along = Longitudinal(point);
            return along > 0 && along <= ClearDistance && (point - Origin - Direction * along).sqrMagnitude <= Radius * Radius;
        }
        public float ContactAt(Vector3 point) => ReleaseAt + Mathf.Clamp(Longitudinal(point), 0, Range) / TravelSpeed;
        public void AdvanceTo(float actorClockTime)
        {
            if (!float.IsFinite(actorClockTime) || actorClockTime < SampleTime) throw new ArgumentOutOfRangeException(nameof(actorClockTime));
            if (!IsCancelled) SampleTime = actorClockTime;
        }
        // A newly inserted wall may shorten this cast, but removing a wall cannot reveal new targets.
        internal void ClipTo(float distance)
        {
            if (!NonNegative(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
            ClearDistance = Mathf.Min(ClearDistance, distance);
        }
        public void Cancel() { IsCancelled = true; }
        static bool Positive(float value) => float.IsFinite(value) && value > 0;
        static bool NonNegative(float value) => float.IsFinite(value) && value >= 0;
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
