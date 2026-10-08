using System;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 그림 시네마틱 — 세션 쪽 [TEST, SPEC-CINEMATIC-STILLS-308 5.1 · 7.4 · 9, D308-26 / D308-28].
    // 하는 일: 새 저장 등록(cinematic.enrolled) · 면 넘기 판정기 돌리기 · Requested 울리기 · Started 에서 "본 것" 적기 ·
    // Finished 에서 판정기 닫기 · 도착 이름 붙들기(#262) 물음에 답하기. 그림 · 캔버스 · 시간 멈춤은 표현기의 몫이다.
    // 카탈로그 자산(Resources, git 비추적)이 없으면 판정기가 0개다: 틱은 첫 줄에서 돌아가고 아무것도 달라지지 않는다.
    // 조각 1 은 PlaneCrossing 만 돈다. 보스 신호(EncounterDetected / Defeated) · 상세 미루기 · 익힘 카드 순서는 조각 2 – 3.
    public sealed partial class WorldMacroPlaytestSession : ICinematicStillsHost
    {
        CinematicStillsCatalogSO cinematicCatalog;
        CinematicTriggerEvaluator[] cinematicTriggers = Array.Empty<CinematicTriggerEvaluator>();
        ICinematicStillsPlayer cinematicPlayer;
        bool cinematicSubscribed;

        /// <summary>The UI root hands its presenter over at scene bind and takes it back (null) before destroying it.</summary>
        public void BindCinematicPlayer308(ICinematicStillsPlayer player) { cinematicPlayer = player; }

        ICinematicStillsPlayer CinematicPlayer308
        {
            get
            {
                if (cinematicPlayer is UnityEngine.Object unityObject && unityObject == null) cinematicPlayer = null;   // destroyed without a hand-back
                return cinematicPlayer;
            }
        }

        void BindCinematic308()
        {
            UnbindCinematic308();
            cinematicCatalog = Resources.Load<CinematicStillsCatalogSO>(CinematicStillsCatalogSO.ResourcePath);
            if (cinematicCatalog == null || cinematicCatalog.Style == null || cinematicCatalog.Sequences == null) { cinematicCatalog = null; return; }
            int count = 0;
            foreach (var s in cinematicCatalog.Sequences) if (Triggered308(s)) count++;
            cinematicTriggers = new CinematicTriggerEvaluator[count]; count = 0;
            foreach (var s in cinematicCatalog.Sequences)
                if (Triggered308(s)) cinematicTriggers[count++] = new CinematicTriggerEvaluator(s, cinematicCatalog.Style.RequestTimeoutSeconds);
            if (cinematicCatalog.Started != null && cinematicCatalog.Finished != null)
            {
                cinematicCatalog.Started.Subscribe(OnCinematicStarted308); cinematicCatalog.Finished.Subscribe(OnCinematicFinished308); cinematicSubscribed = true;
            }
        }

        static bool Triggered308(CinematicStillsCatalogSO.Sequence s) =>
            s != null && !s.Disabled && s.Trigger != null && s.Trigger.Kind == CinematicTriggerKind.PlaneCrossing && s.Stills != null && s.Stills.Length > 0;

        void UnbindCinematic308()
        {
            if (cinematicSubscribed && cinematicCatalog != null)
            {
                if (cinematicCatalog.Started != null) cinematicCatalog.Started.Unsubscribe(OnCinematicStarted308);
                if (cinematicCatalog.Finished != null) cinematicCatalog.Finished.Unsubscribe(OnCinematicFinished308);
            }
            cinematicSubscribed = false; cinematicTriggers = Array.Empty<CinematicTriggerEvaluator>(); cinematicCatalog = null;
        }

        // Only a brand-new save sees the start / mine-exit stills (precedent: EnrollMineTutorial306). A plain string in
        // ledger.completed: the save version stays. Written whether or not the catalogue exists - the marker alone does nothing.
        void EnrollCinematic308(WorldMacroProgress progress)
        {
            if (LoadStatus == "new" && progress?.ledger?.completed != null && !progress.ledger.completed.Contains(CinematicStillsCatalogSO.EnrolledId))
                progress.ledger.completed.Add(CinematicStillsCatalogSO.EnrolledId);
        }

        public CinematicVerdict CinematicVerdict308(string sequenceId)
        {
            if (cinematicCatalog == null) return CinematicVerdict.NoCatalogue;
            var player = CinematicPlayer308;
            var verdict = CinematicEligibility.Judge(cinematicCatalog, sequenceId, Progress?.ledger?.completed, TestSaveSuffix, player != null && player.AllowInHarness);
            if (verdict != CinematicVerdict.Ok) return verdict;
            if (player == null) return CinematicVerdict.NoPlayer;
            return player.PictureMissing(sequenceId) ? CinematicVerdict.PictureMissing : CinematicVerdict.Ok;
        }

        public string CinematicPreloadId308
        {
            get
            {
                foreach (var e in cinematicTriggers) if (e.WantsPreload) return e.SequenceId;
                return null;
            }
        }

        /// <summary>#262: while a stills sequence that holds this arrival name is armed, pending or playing, the name waits; it
        /// shows once after Finished. When the sequence is off, seen, dropped (Spent) or done, nothing is held.</summary>
        public bool CinematicHoldsArrival308(string arrivalId)
        {
            foreach (var e in cinematicTriggers) if (e.HoldsArrival(arrivalId)) return true;
            return false;
        }

        /// <summary>Checks: "Idle" / "Armed" / ... of a plane-triggered sequence, "" when it has no evaluator.</summary>
        public string CinematicState308(string sequenceId)
        {
            foreach (var e in cinematicTriggers) if (e.SequenceId == sequenceId) return e.Current.ToString();
            return "";
        }

        // The start conditions (5.1): alive, no pending defeat save, no rest / death presentation, no menu, time scale exactly 1
        // (riposte hitstop and the drawing slow-down own the time scale meanwhile), not drawing, nobody chasing.
        bool CinematicStartAllowed308
        {
            get
            {
                if (!LocationDiscoveryAllowed || Walker == null || !Mathf.Approximately(Time.timeScale, 1f)) return false;
                if (Walker.Motor != null && Walker.Motor.IsDrawing || Walker.Drawing != null && Walker.Drawing.InDrawMode) return false;
                if (DeathPresentation != null && DeathPresentation.IsActive) return false;
                var player = CinematicPlayer308; if (player == null || player.IsPlaying) return false;
                foreach (var actor in Actors) if (actor != null && actor.isActiveAndEnabled && actor.Current == PrologueEncounter.Behaviour.Chase) return false;
                return true;
            }
        }

        void TickCinematic308()
        {
            if (cinematicTriggers.Length == 0 || Walker == null || Walker.Body == null) return;
            Vector3 feet = Walker.Body.transform.position; float now = Time.unscaledTime;
            foreach (var e in cinematicTriggers)
            {
                var state = e.Current;
                if (state == CinematicTriggerEvaluator.State.Done) continue;
                // far away and idle: keep the last sample fresh, skip the ledger searches
                bool eligible = (state != CinematicTriggerEvaluator.State.Idle || e.Near(feet)) && CinematicVerdict308(e.SequenceId) == CinematicVerdict.Ok;
                // Armed -> Pending -> Requested can be one step, so the start conditions are read for both
                bool start = eligible && (state == CinematicTriggerEvaluator.State.Pending || state == CinematicTriggerEvaluator.State.Armed) && CinematicStartAllowed308;
                if (e.Step(new CinematicTriggerEvaluator.Input { Feet = feet, Now = now, Eligible = eligible, StartAllowed = start }) && cinematicCatalog.Requested != null)
                    cinematicCatalog.Requested.Raise(e.SequenceId);
            }
        }

        void OnCinematicStarted308(string sequenceId)
        {
            if (!ready || cinematicCatalog == null || cinematicCatalog.Find(sequenceId) == null) return;
            foreach (var e in cinematicTriggers) if (e.SequenceId == sequenceId) e.NotifyStarted();
            var completed = Progress?.ledger?.completed; string seen = CinematicStillsCatalogSO.SeenId(sequenceId);
            if (completed == null || completed.Contains(seen)) return;
            // seen = shown (a skip is seen too). A failed save takes the marker back and the stills still play; the evaluator's
            // Done keeps them from playing again in this session.
            completed.Add(seen);
            if (SaveBlocked || !SaveNow(out _)) completed.Remove(seen);
        }

        void OnCinematicFinished308(string sequenceId)
        {
            foreach (var e in cinematicTriggers) if (e.SequenceId == sequenceId) e.NotifyFinished();
        }
    }
}
