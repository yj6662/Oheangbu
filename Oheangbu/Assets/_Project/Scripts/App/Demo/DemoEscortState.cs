using System;
using System.Threading;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    public enum DemoEscortStage { None, Contracted, Escorting, FirstInspectionCleared, SecondInspectionCleared, Delivered }
    public enum DemoEscortCompanionMode { Waiting, Following, Riding }
    public enum DemoEscortCommand { AcceptContract, StartEscort, Board, Disembark, Rejoin, VehicleRecalled, FirstInspection, SecondInspection, SaveCheckpoint, RecoverAfterDeath, Deliver }
    public enum DemoEscortStatus { Prepared, Saved, Duplicate, InvalidState, InvalidEvidence, OutOfOrder, Busy, SaveFailed, Conflict }

    [Serializable]
    public sealed class DemoEscortState
    {
        public long Revision;
        public DemoEscortStage Stage;
        public DemoEscortCompanionMode CompanionMode;
        public string LastEvidenceId = "";
        public string CheckpointId = "";
        public Vector3 CheckpointFeet, CompanionFeet;
        public float CheckpointYaw;
        public bool DeliveryRewardRecorded;
        public bool OwnsSealedCargo => Stage >= DemoEscortStage.Contracted && Stage < DemoEscortStage.Delivered;
        public int InspectionsCleared => Stage >= DemoEscortStage.SecondInspectionCleared ? 2 : Stage >= DemoEscortStage.FirstInspectionCleared ? 1 : 0;
        public bool HasCheckpoint => !string.IsNullOrWhiteSpace(CheckpointId);
        public bool IsValid() => Revision >= 0 && Enum.IsDefined(typeof(DemoEscortStage), Stage) && Enum.IsDefined(typeof(DemoEscortCompanionMode), CompanionMode) &&
            LastEvidenceId != null && LastEvidenceId.Length <= 128 && (LastEvidenceId.Length == 0 || !string.IsNullOrWhiteSpace(LastEvidenceId)) &&
            CheckpointId != null && CheckpointId.Length <= 128 && (CheckpointId.Length == 0 || HasCheckpoint) &&
            Finite(CheckpointFeet) && Finite(CompanionFeet) && float.IsFinite(CheckpointYaw) &&
            (Stage < DemoEscortStage.Escorting || HasCheckpoint) &&
            (Stage != DemoEscortStage.None || CompanionMode == DemoEscortCompanionMode.Waiting && !HasCheckpoint) &&
            (Stage != DemoEscortStage.Contracted || CompanionMode == DemoEscortCompanionMode.Waiting && !HasCheckpoint) &&
            (Stage == DemoEscortStage.Delivered) == DeliveryRewardRecorded &&
            (Stage != DemoEscortStage.Delivered || CompanionMode == DemoEscortCompanionMode.Waiting);
        public DemoEscortState Copy() => (DemoEscortState)MemberwiseClone();
        internal static bool Finite(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
    }

    /// <summary>
    /// A snapshot captured by the actual session owner, never constructed from a UI-supplied point ID.
    /// Session must verify current campaign stage, live interaction target, NPC/cargo presence,
    /// active vehicle seat events, and a safe authored checkpoint against its own scene objects.
    /// Pure rules additionally check revision, expected ID and measured physical proximity.
    /// </summary>
    public sealed class DemoEscortEvidence
    {
        public DemoEscortCommand Command;
        public long ExpectedRevision;
        public string EventId, InteractionId, CheckpointId;
        public bool SessionVerified, CompanionPresent, CargoPresent, PlayerInVehicle, VehiclePresent,
            CompanionLoaded, CargoLoaded, VehicleRecallVerified, SafeExitVerified, CheckpointVerified, DeathRecoveryVerified;
        public Vector3 PlayerFeet, PointFeet, CompanionFeet, SafeExitFeet, CheckpointFeet;
        public float VehicleSpeed, CheckpointYaw;
    }

    public sealed class DemoEscortReceipt
    {
        public DemoEscortCommand Command { get; }
        public string CampaignTriggerId { get; }
        public string RewardKey { get; }
        public bool RestoreAtCheckpoint { get; }
        public Vector3 RestoreFeet { get; }
        public float RestoreYaw { get; }
        internal DemoEscortReceipt(DemoEscortCommand command, string trigger = null, string reward = null, DemoEscortState restore = null)
        { Command = command; CampaignTriggerId = trigger; RewardKey = reward; RestoreAtCheckpoint = restore != null; RestoreFeet = restore?.CheckpointFeet ?? Vector3.zero; RestoreYaw = restore?.CheckpointYaw ?? 0; }
    }

    /// <summary>No scene access, currency mutation, saving, callbacks, or vehicle-object ownership.</summary>
    public static class DemoEscortRules
    {
        public const float MaximumInteractionDistance = 4.5f, MaximumCompanionDistance = 8f;
        public static DemoEscortStatus TryPrepare(DemoEscortState state, DemoEscortCommand command, DemoEscortEvidence proof,
            out DemoEscortState next, out DemoEscortReceipt receipt)
        {
            next = null; receipt = null;
            if (state == null || !state.IsValid() || state.Revision == long.MaxValue) return DemoEscortStatus.InvalidState;
            if (!Enum.IsDefined(typeof(DemoEscortCommand), command)) return DemoEscortStatus.InvalidEvidence;
            if (proof == null || !proof.SessionVerified || proof.Command != command || proof.ExpectedRevision != state.Revision ||
                string.IsNullOrWhiteSpace(proof.EventId) || proof.EventId.Length > 128) return DemoEscortStatus.InvalidEvidence;
            if (proof.EventId == state.LastEvidenceId) return DemoEscortStatus.Duplicate;
            string trigger = null, reward = null; bool restore = false;
            DemoEscortState candidate = state.Copy();
            bool active = state.Stage >= DemoEscortStage.Escorting && state.Stage < DemoEscortStage.Delivered;
            switch (command)
            {
                case DemoEscortCommand.AcceptContract:
                    if (state.Stage != DemoEscortStage.None) return DemoEscortStatus.Duplicate;
                    if (!At(proof, "wangso_w1", false)) return DemoEscortStatus.InvalidEvidence;
                    candidate.Stage = DemoEscortStage.Contracted; trigger = "wangso_w1"; break;
                case DemoEscortCommand.StartEscort:
                    if (state.Stage >= DemoEscortStage.Escorting) return DemoEscortStatus.Duplicate;
                    if (state.Stage != DemoEscortStage.Contracted) return DemoEscortStatus.OutOfOrder;
                    if (!At(proof, "escort_start", true, true) || !Loaded(proof) || !Checkpoint(proof)) return DemoEscortStatus.InvalidEvidence;
                    candidate.Stage = DemoEscortStage.Escorting; candidate.CompanionMode = DemoEscortCompanionMode.Riding;
                    PutCheckpoint(candidate, proof); candidate.CompanionFeet = proof.CompanionFeet; trigger = "escort_start"; break;
                case DemoEscortCommand.FirstInspection:
                case DemoEscortCommand.SecondInspection:
                    int inspection = command == DemoEscortCommand.FirstInspection ? 1 : 2;
                    if (state.InspectionsCleared >= inspection) return DemoEscortStatus.Duplicate;
                    if (!active || state.InspectionsCleared != inspection - 1) return DemoEscortStatus.OutOfOrder;
                    trigger = inspection == 1 ? "checkpoint_1" : "checkpoint_2";
                    if (!At(proof, trigger, true) || state.CompanionMode == DemoEscortCompanionMode.Riding) return DemoEscortStatus.InvalidEvidence;
                    candidate.Stage = inspection == 1 ? DemoEscortStage.FirstInspectionCleared : DemoEscortStage.SecondInspectionCleared; break;
                case DemoEscortCommand.Deliver:
                    if (state.Stage == DemoEscortStage.Delivered) return DemoEscortStatus.Duplicate;
                    if (state.Stage != DemoEscortStage.SecondInspectionCleared) return DemoEscortStatus.OutOfOrder;
                    if (!At(proof, "cargo_delivery", true) || state.CompanionMode == DemoEscortCompanionMode.Riding) return DemoEscortStatus.InvalidEvidence;
                    candidate.Stage = DemoEscortStage.Delivered; candidate.CompanionMode = DemoEscortCompanionMode.Waiting;
                    candidate.DeliveryRewardRecorded = true; candidate.CompanionFeet = proof.CompanionFeet;
                    trigger = "cargo_delivery"; reward = "escort_delivery"; break;
                case DemoEscortCommand.Board:
                    if (!active) return DemoEscortStatus.OutOfOrder;
                    if (state.CompanionMode == DemoEscortCompanionMode.Riding) return DemoEscortStatus.Duplicate;
                    if (!Loaded(proof) || !NearbyCompanion(proof)) return DemoEscortStatus.InvalidEvidence;
                    candidate.CompanionMode = DemoEscortCompanionMode.Riding; candidate.CompanionFeet = proof.CompanionFeet; break;
                case DemoEscortCommand.Disembark:
                    if (!active || state.CompanionMode != DemoEscortCompanionMode.Riding) return DemoEscortStatus.OutOfOrder;
                    if (proof.PlayerInVehicle || !proof.SafeExitVerified || !DemoEscortState.Finite(proof.SafeExitFeet)) return DemoEscortStatus.InvalidEvidence;
                    candidate.CompanionMode = DemoEscortCompanionMode.Waiting; candidate.CompanionFeet = proof.SafeExitFeet; break;
                case DemoEscortCommand.Rejoin:
                    if (!active) return DemoEscortStatus.OutOfOrder;
                    if (state.CompanionMode == DemoEscortCompanionMode.Following) return DemoEscortStatus.Duplicate;
                    if (state.CompanionMode != DemoEscortCompanionMode.Waiting || proof.PlayerInVehicle || !NearbyCompanion(proof)) return DemoEscortStatus.InvalidEvidence;
                    candidate.CompanionMode = DemoEscortCompanionMode.Following; candidate.CompanionFeet = proof.CompanionFeet; break;
                case DemoEscortCommand.VehicleRecalled:
                    if (!active || !proof.VehicleRecallVerified || proof.PlayerInVehicle || !proof.SafeExitVerified || !DemoEscortState.Finite(proof.SafeExitFeet)) return DemoEscortStatus.InvalidEvidence;
                    candidate.CompanionMode = DemoEscortCompanionMode.Waiting; candidate.CompanionFeet = proof.SafeExitFeet; break;
                case DemoEscortCommand.SaveCheckpoint:
                    if (!active) return DemoEscortStatus.OutOfOrder;
                    if (!Checkpoint(proof) || proof.PlayerInVehicle || state.CompanionMode == DemoEscortCompanionMode.Riding || !NearbyCompanion(proof) ||
                        Vector3.Distance(proof.PlayerFeet, proof.CheckpointFeet) > MaximumInteractionDistance) return DemoEscortStatus.InvalidEvidence;
                    if (state.CheckpointId == proof.CheckpointId && Vector3.Distance(state.CheckpointFeet, proof.CheckpointFeet) < .001f) return DemoEscortStatus.Duplicate;
                    PutCheckpoint(candidate, proof); break;
                case DemoEscortCommand.RecoverAfterDeath:
                    if (!active || !state.HasCheckpoint) return DemoEscortStatus.OutOfOrder;
                    if (!proof.DeathRecoveryVerified || !Checkpoint(proof) || proof.CheckpointId != state.CheckpointId) return DemoEscortStatus.InvalidEvidence;
                    // Session re-resolves this saved ID against its current authored safe point.
                    // This also repairs legitimate checkpoint coordinate changes between terrain revisions.
                    PutCheckpoint(candidate, proof);
                    candidate.CompanionMode = DemoEscortCompanionMode.Waiting; candidate.CompanionFeet = candidate.CheckpointFeet; restore = true; break;
            }
            candidate.Revision++; candidate.LastEvidenceId = proof.EventId;
            if (!candidate.IsValid()) return DemoEscortStatus.InvalidState;
            next = candidate; receipt = new DemoEscortReceipt(command, trigger, reward, restore ? candidate : null); return DemoEscortStatus.Prepared;
        }
        private static bool At(DemoEscortEvidence p, string id, bool cargo, bool ridingAllowed = false) => p.InteractionId == id &&
            (ridingAllowed || !p.PlayerInVehicle) && DemoEscortState.Finite(p.PlayerFeet) && DemoEscortState.Finite(p.PointFeet) &&
            Vector3.Distance(p.PlayerFeet, p.PointFeet) <= MaximumInteractionDistance && NearbyCompanion(p) && (!cargo || p.CargoPresent);
        private static bool NearbyCompanion(DemoEscortEvidence p) => p.CompanionPresent && DemoEscortState.Finite(p.CompanionFeet) &&
            DemoEscortState.Finite(p.PlayerFeet) && Vector3.Distance(p.PlayerFeet, p.CompanionFeet) <= MaximumCompanionDistance;
        private static bool Loaded(DemoEscortEvidence p) => p.VehiclePresent && p.PlayerInVehicle && p.CompanionLoaded && p.CargoLoaded &&
            p.CargoPresent && float.IsFinite(p.VehicleSpeed) && Mathf.Abs(p.VehicleSpeed) <= .5f;
        private static bool Checkpoint(DemoEscortEvidence p) => p.CheckpointVerified && !string.IsNullOrWhiteSpace(p.CheckpointId) &&
            p.CheckpointId.Length <= 128 && DemoEscortState.Finite(p.CheckpointFeet) && float.IsFinite(p.CheckpointYaw);
        private static void PutCheckpoint(DemoEscortState s, DemoEscortEvidence p)
        { s.CheckpointId = p.CheckpointId; s.CheckpointFeet = p.CheckpointFeet; s.CheckpointYaw = Mathf.Repeat(p.CheckpointYaw, 360); }
    }

    /// <summary>
    /// Implement only on the actual session's adapter. Read returns a detached state. Capture uses
    /// current session-owned actors/points/seat events; UI supplies only a command, never coordinates.
    /// TryCommit compares expected Revision AND state, saves escort + campaign + currency as one
    /// WorldMacroProgress candidate, then publishes it. A failure/throw must publish nothing.
    /// RewardKey is an idempotency marker; only this atomic owner resolves the campaign reward amount.
    /// Apply passenger visuals/teleport only after Saved. A captured interaction ID alone is not proof.
    /// </summary>
    public interface IDemoEscortSessionOwner
    {
        DemoEscortState ReadEscort();
        bool TryCaptureEscortEvidence(DemoEscortCommand command, DemoEscortState expected, out DemoEscortEvidence evidence);
        DemoEscortStatus TryCommitEscort(DemoEscortState expected, DemoEscortState candidate, DemoEscortReceipt receipt, out string error);
    }

    public sealed class DemoEscortService
    {
        private readonly IDemoEscortSessionOwner _owner;
        private int _busy;
        public DemoEscortService(IDemoEscortSessionOwner owner) { _owner = owner ?? throw new ArgumentNullException(nameof(owner)); }
        public DemoEscortStatus Execute(DemoEscortCommand command, out DemoEscortReceipt receipt, out string error)
        {
            receipt = null; error = null;
            if (Interlocked.Exchange(ref _busy, 1) != 0) return DemoEscortStatus.Busy;
            try
            {
                DemoEscortState expected = _owner.ReadEscort();
                if (expected == null || !expected.IsValid()) return DemoEscortStatus.InvalidState;
                if (!_owner.TryCaptureEscortEvidence(command, expected.Copy(), out var proof)) return DemoEscortStatus.InvalidEvidence;
                DemoEscortStatus status = DemoEscortRules.TryPrepare(expected, command, proof, out var candidate, out var proposedReceipt);
                if (status != DemoEscortStatus.Prepared) return status;
                status = _owner.TryCommitEscort(expected, candidate, proposedReceipt, out error);
                if (status == DemoEscortStatus.Saved) receipt = proposedReceipt;
                return status;
            }
            catch (Exception e) { error = e.Message; return DemoEscortStatus.SaveFailed; }
            finally { Volatile.Write(ref _busy, 0); }
        }
    }
}
