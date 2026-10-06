using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public enum CinematicVerdict { Ok, NoCatalogue, UnknownSequence, Disabled, HarnessSlot, NotEnrolled, AlreadySeen, NoPlayer, PictureMissing }

    /// <summary>What the trigger side needs from whoever shows the stills (the uGUI presenter). Null = nobody shows them.</summary>
    public interface ICinematicStillsPlayer
    {
        bool AllowInHarness { get; }
        bool IsPlaying { get; }
        /// <summary>True once a load of this sequence finished and a picture was not there.</summary>
        bool PictureMissing(string sequenceId);
    }

    /// <summary>What the presenter needs from the session: the one rule for "may this sequence play now", and what to load ahead.</summary>
    public interface ICinematicStillsHost
    {
        CinematicVerdict CinematicVerdict308(string sequenceId);
        string CinematicPreloadId308 { get; }
    }

    // #308 그림 시네마틱 — 자격(순수). SPEC-CINEMATIC-STILLS-308 9절: 한 번만 · 새 여정에만 · 시험 슬롯에서는 꺼짐.
    public static class CinematicEligibility
    {
        /// <summary>completed = the save's ledger.completed. saveSuffix = the session's TestSaveSuffix (non-empty = a harness slot:
        /// perf routes, smokes and captures must never stop at a picture unless the check asks with allowInHarness).</summary>
        public static CinematicVerdict Judge(CinematicStillsCatalogSO catalog, string sequenceId, ICollection<string> completed, string saveSuffix, bool allowInHarness)
        {
            if (catalog == null) return CinematicVerdict.NoCatalogue;
            var sequence = catalog.Find(sequenceId);
            if (sequence == null) return CinematicVerdict.UnknownSequence;
            if (sequence.Disabled || sequence.Stills == null || sequence.Stills.Length == 0) return CinematicVerdict.Disabled;
            if (!string.IsNullOrEmpty(saveSuffix) && !allowInHarness) return CinematicVerdict.HarnessSlot;
            if (completed == null) return CinematicVerdict.NotEnrolled;
            if (completed.Contains(CinematicStillsCatalogSO.SeenId(sequenceId))) return CinematicVerdict.AlreadySeen;
            if (sequence.RequiresEnrollment && !completed.Contains(CinematicStillsCatalogSO.EnrolledId)) return CinematicVerdict.NotEnrolled;
            return CinematicVerdict.Ok;
        }
    }

    // #308 그림 시네마틱 — 면 넘기 판정(순수, 정적 가변 칸 0). SPEC-CINEMATIC-STILLS-308 5.1 · 7.2.
    //   Idle -> Armed(면의 안쪽 · PreloadRadius 안: 그림을 미리 올리고 도착 이름을 붙든다)
    //        -> Pending(안 -> 밖으로 넘었다: 시작 조건을 기다린다, PendingRadius 안에서만)
    //        -> Requested(요청을 보냈다) -> Playing(Started 를 받았다) -> Done(Finished: 이 세션에서는 다시 걸지 않는다)
    //   버림: Pending 에서 반경을 벗어남 · Requested 에서 Started 가 제때 안 옴 · 넘지 않고 바깥에 있음 -> Spent.
    //   Spent 는 도착 이름을 놓는다(S13). 다시 안쪽으로 들어오면 Armed 로 돌아가고, 다음에 넘을 때 다시 건다. 본 것으로 적지 않는다.
    public sealed class CinematicTriggerEvaluator
    {
        public enum State { Idle, Armed, Pending, Requested, Playing, Spent, Done }

        public struct Input
        {
            public Vector3 Feet;
            public float Now;            // unscaled seconds
            public bool Eligible;        // CinematicVerdict.Ok
            public bool StartAllowed;    // alive, no chase, not drawing, time scale 1, no rest / death presentation ...
        }

        readonly CinematicStillsCatalogSO.Sequence sequence;
        readonly float requestTimeout;
        bool hasPrevious; float previousSigned; Vector3 previousFeet; float requestedAt;

        public string SequenceId => sequence.Id;
        public State Current { get; private set; }
        public Vector3 FiredAt { get; private set; }

        public CinematicTriggerEvaluator(CinematicStillsCatalogSO.Sequence sequence, float requestTimeoutSeconds)
        {
            this.sequence = sequence; requestTimeout = requestTimeoutSeconds;
        }

        CinematicStillsCatalogSO.Trigger T => sequence.Trigger;

        public float SignedDistance(Vector3 feet) => (feet.x - T.Point.x) * T.OutwardNormalXZ.x + (feet.z - T.Point.z) * T.OutwardNormalXZ.y;
        public float Lateral(Vector3 feet) => (feet.x - T.Point.x) * -T.OutwardNormalXZ.y + (feet.z - T.Point.z) * T.OutwardNormalXZ.x;
        public float PlanarDistance(Vector3 feet) { float dx = feet.x - T.Point.x, dz = feet.z - T.Point.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        /// <summary>Cheap gate for the caller: outside this the eligibility (two list searches) need not be computed.</summary>
        public bool Near(Vector3 feet) => PlanarDistance(feet) <= T.PreloadRadius;

        public bool WantsPreload => Current == State.Armed || Current == State.Pending;
        public bool HoldsArrival(string arrivalId) =>
            !string.IsNullOrEmpty(arrivalId) && sequence.HoldsArrivalId == arrivalId &&
            (Current == State.Armed || Current == State.Pending || Current == State.Requested || Current == State.Playing);

        /// <summary>One sample per frame. True exactly on the frame the request must be raised.</summary>
        public bool Step(Input input)
        {
            float signed = SignedDistance(input.Feet), range = PlanarDistance(input.Feet);
            bool crossed = hasPrevious && previousSigned < 0f && signed >= 0f &&
                Mathf.Abs(Lateral(input.Feet)) <= T.HalfWidth && input.Feet.y >= T.FeetYMin && input.Feet.y <= T.FeetYMax &&
                (input.Feet - previousFeet).magnitude <= T.MaxCrossingStep;
            hasPrevious = true; previousSigned = signed; previousFeet = input.Feet;
            bool fire = false;
            switch (Current)
            {
                case State.Idle:
                    if (input.Eligible && range <= T.PreloadRadius && signed < 0f) Current = State.Armed;
                    break;
                case State.Armed:
                    if (!input.Eligible || range > T.PreloadRadius) Current = State.Idle;
                    else if (crossed) { Current = State.Pending; goto case State.Pending; }
                    else if (signed >= 0f) Current = State.Spent;   // outside without a walked crossing (respawn, another way round)
                    break;
                case State.Pending:
                    if (!input.Eligible) Current = State.Idle;
                    else if (range > T.PendingRadius) Current = State.Spent;
                    else if (input.StartAllowed) { Current = State.Requested; requestedAt = input.Now; FiredAt = input.Feet; fire = true; }
                    break;
                case State.Requested:
                    if (input.Now - requestedAt > requestTimeout) Current = State.Spent;   // nobody showed it: not seen, the name is released
                    break;
                case State.Spent:
                    if (range > T.PreloadRadius) Current = State.Idle;
                    else if (input.Eligible && signed < 0f) Current = State.Armed;
                    break;
            }
            return fire;
        }

        /// <summary>Started arrived (also for a direct request the evaluator did not raise, e.g. the check's PlayInHarness).</summary>
        public void NotifyStarted() { if (Current != State.Done) Current = State.Playing; }
        public void NotifyFinished() { if (Current == State.Playing || Current == State.Requested) Current = State.Done; }
    }
}
