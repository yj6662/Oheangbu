using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum TigerAttackStage { Preparing, Leaping, LandingRecovery, ClawLeft, ClawRight, Finished, Cancelled }

    /// <summary>Immutable world route, target life and clip schedule. Only sampled time/cancellation advance.</summary>
    public sealed class SummonTigerAttackPlan
    {
        readonly Vector3[] ground;
        public IReadOnlyList<Vector3> GroundPositions { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Vector3 Direction { get; }
        public int TargetId { get; }
        public uint TargetLifeRevision { get; }
        public bool UsesLeap { get; }
        public float StartedAt { get; }
        public float TakeoffAt { get; }
        public float LandAt { get; }
        public float LeapEndAt { get; }
        public float LeftStartedAt { get; }
        public float LeftContactAt { get; }
        public float RightStartedAt { get; }
        public float RightContactAt { get; }
        public float EndAt { get; }
        public float ArcHeight { get; }
        public float SampleTime { get; private set; }
        public bool IsCancelled { get; private set; }
        public bool IsFinished => IsCancelled || SampleTime >= EndAt;
        public float LeapProgress => !UsesLeap ? 1 : Mathf.Clamp01((SampleTime - TakeoffAt) / (LandAt - TakeoffAt));
        public TigerAttackStage Stage => IsCancelled ? TigerAttackStage.Cancelled : SampleTime >= EndAt ? TigerAttackStage.Finished :
            SampleTime >= RightStartedAt ? TigerAttackStage.ClawRight : SampleTime >= LeftStartedAt ? TigerAttackStage.ClawLeft :
            SampleTime >= LandAt ? TigerAttackStage.LandingRecovery : SampleTime >= TakeoffAt ? TigerAttackStage.Leaping : TigerAttackStage.Preparing;
        public float StageTime => Mathf.Max(0, SampleTime - (Stage == TigerAttackStage.ClawLeft ? LeftStartedAt :
            Stage == TigerAttackStage.ClawRight ? RightStartedAt : StartedAt));

        public SummonTigerAttackPlan(IReadOnlyList<Vector3> groundPositions, Vector3 direction, int targetId, uint lifeRevision,
            bool usesLeap, float startedAt, float preparation, float landing, float leapSeconds,
            float clawSeconds, float clawContact, float arcHeight)
        {
            if (groundPositions == null || groundPositions.Count < 2 || groundPositions.Count > 128 || targetId == 0)
                throw new ArgumentException("A tiger plan requires a bounded ground route and target identity.");
            foreach (float f in new[] { startedAt, preparation, landing, leapSeconds, clawSeconds, clawContact, arcHeight })
                if (!float.IsFinite(f) || f < 0) throw new ArgumentOutOfRangeException(nameof(startedAt));
            if (preparation >= landing || landing >= leapSeconds || clawContact <= 0 || clawContact >= clawSeconds)
                throw new ArgumentException("Tiger clip times must be ordered.");
            direction.y = 0;
            if (!Finite(direction) || direction.sqrMagnitude < .00001f) throw new ArgumentException("A fixed horizontal direction is required.");
            ground = new Vector3[groundPositions.Count];
            for (int i = 0; i < ground.Length; i++)
            { if (!Finite(groundPositions[i])) throw new ArgumentException("Ground route must be finite."); ground[i] = groundPositions[i]; }
            GroundPositions = Array.AsReadOnly(ground);
            Start = ground[0]; End = ground[ground.Length - 1]; Direction = direction.normalized;
            TargetId = targetId; TargetLifeRevision = lifeRevision; UsesLeap = usesLeap; StartedAt = startedAt;
            TakeoffAt = startedAt + (usesLeap ? preparation : 0); LandAt = startedAt + (usesLeap ? landing : 0);
            LeapEndAt = startedAt + (usesLeap ? leapSeconds : 0); LeftStartedAt = LeapEndAt;
            LeftContactAt = LeftStartedAt + clawContact; RightStartedAt = LeftStartedAt + clawSeconds;
            RightContactAt = RightStartedAt + clawContact; EndAt = RightStartedAt + clawSeconds;
            ArcHeight = usesLeap ? arcHeight : 0; SampleTime = startedAt;
        }

        public Vector3 GroundAt(float progress)
        {
            float sample = Mathf.Clamp01(progress) * (ground.Length - 1);
            int index = Mathf.Min(ground.Length - 2, Mathf.FloorToInt(sample));
            return Vector3.Lerp(ground[index], ground[index + 1], sample - index);
        }
        public void AdvanceTo(float elapsed)
        {
            if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (!IsCancelled) SampleTime = Mathf.Max(SampleTime, elapsed);
        }
        public void Cancel() { IsCancelled = true; }
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
