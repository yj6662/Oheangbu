using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class DemoEscortStateChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Actual Session adapter has separate DemoEscortSessionChecks; this suite covers pure state rules only", "Passenger/cargo sockets and vehicle recall visuals",
                "Authored inspection/delivery interactions and safe checkpoint placement", "Runtime death/vehicle/app restart integration" };
        }
        public static string Run()
        {
            var r = new Report(); void Check(bool pass, string name) => (pass ? r.passed : r.failed).Add(name);
            try { StateFlow(Check); } catch (Exception e) { r.failed.Add("Escort state: " + e); }
            try { Transactions(Check); } catch (Exception e) { r.failed.Add("Session owner boundary: " + e); }
            try { Migration(Check); } catch (Exception e) { r.failed.Add("Save migration: " + e); }
            r.status = r.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(r, true);
        }

        private static DemoEscortEvidence Proof(DemoEscortState state, DemoEscortCommand command, string eventId = null)
        {
            string id = command == DemoEscortCommand.AcceptContract ? "wangso_w1" : command == DemoEscortCommand.StartEscort ? "escort_start" :
                command == DemoEscortCommand.FirstInspection ? "checkpoint_1" : command == DemoEscortCommand.SecondInspection ? "checkpoint_2" :
                command == DemoEscortCommand.Deliver ? "cargo_delivery" : "";
            return new DemoEscortEvidence { Command = command, ExpectedRevision = state.Revision, EventId = eventId ?? "event_" + state.Revision + "_" + command,
                InteractionId = id, SessionVerified = true, CompanionPresent = true, CargoPresent = true, VehiclePresent = true,
                PlayerInVehicle = command == DemoEscortCommand.StartEscort || command == DemoEscortCommand.Board,
                CompanionLoaded = true, CargoLoaded = true, VehicleSpeed = 0, VehicleRecallVerified = true, SafeExitVerified = true,
                CheckpointVerified = true, DeathRecoveryVerified = true, CheckpointId = state.HasCheckpoint ? state.CheckpointId : "escort_start", CheckpointFeet = state.CheckpointFeet,
                CheckpointYaw = 45, PlayerFeet = Vector3.zero, PointFeet = Vector3.zero, CompanionFeet = Vector3.right,
                SafeExitFeet = Vector3.right };
        }
        private static DemoEscortState Step(DemoEscortState state, DemoEscortCommand command)
        {
            var status = DemoEscortRules.TryPrepare(state, command, Proof(state, command), out var next, out _);
            if (status != DemoEscortStatus.Prepared) throw new InvalidOperationException(command + " failed: " + status);
            return next;
        }

        private static void StateFlow(Action<bool, string> check)
        {
            var state = new DemoEscortState(); check(state.IsValid() && !state.OwnsSealedCargo, "New state has no unearned contract/cargo");
            var proof = Proof(state, DemoEscortCommand.StartEscort);
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.StartEscort, proof, out _, out _) == DemoEscortStatus.OutOfOrder,
                "Travel cannot start before the contract");
            proof = Proof(state, DemoEscortCommand.AcceptContract); proof.PlayerFeet = Vector3.right * 100;
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.AcceptContract, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "A correct interaction ID with distant actual player coordinates cannot advance");
            proof = Proof(state, DemoEscortCommand.AcceptContract); proof.SessionVerified = false;
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.AcceptContract, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Unverified marker/preview interaction cannot accept a contract");
            string original = JsonUtility.ToJson(state); state = Step(state, DemoEscortCommand.AcceptContract);
            check(state.Stage == DemoEscortStage.Contracted && state.OwnsSealedCargo && original == JsonUtility.ToJson(new DemoEscortState()),
                "Contract candidate owns exactly one quest cargo independently of a vehicle object");
            proof = Proof(state, DemoEscortCommand.StartEscort); proof.CargoLoaded = false;
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.StartEscort, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Vehicle presence alone cannot replace actual cargo/passenger loading evidence");
            state = Step(state, DemoEscortCommand.StartEscort);
            check(state.CompanionMode == DemoEscortCompanionMode.Riding && state.HasCheckpoint, "Start records loaded travel and first safe escort checkpoint");
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.FirstInspection, Proof(state, DemoEscortCommand.FirstInspection), out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Inspection cannot complete while saved companion state remains riding");
            state = Step(state, DemoEscortCommand.Disembark); state = Step(state, DemoEscortCommand.Rejoin);
            check(state.CompanionMode == DemoEscortCompanionMode.Following && state.OwnsSealedCargo, "Safe exit and rejoin preserve cargo ownership");
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.SecondInspection, Proof(state, DemoEscortCommand.SecondInspection), out _, out _) == DemoEscortStatus.OutOfOrder,
                "Second inspection cannot skip the first");
            state = Step(state, DemoEscortCommand.FirstInspection);
            proof = Proof(state, DemoEscortCommand.SaveCheckpoint); proof.CheckpointId = "road_rest"; proof.CheckpointFeet = new Vector3(2, 0, 1);
            DemoEscortRules.TryPrepare(state, DemoEscortCommand.SaveCheckpoint, proof, out var checkpointed, out _); state = checkpointed;
            state = Step(state, DemoEscortCommand.Board); state = Step(state, DemoEscortCommand.VehicleRecalled);
            check(state.OwnsSealedCargo && state.Stage == DemoEscortStage.FirstInspectionCleared && state.CompanionMode == DemoEscortCompanionMode.Waiting,
                "Vehicle recall changes transport mode without losing contract, cargo or completed inspection");
            long beforeDeath = state.Revision;
            DemoEscortRules.TryPrepare(state, DemoEscortCommand.RecoverAfterDeath, Proof(state, DemoEscortCommand.RecoverAfterDeath), out var recovered, out var recovery);
            check(recovered.Revision == beforeDeath + 1 && recovered.Stage == state.Stage && recovered.OwnsSealedCargo && recovery.RestoreAtCheckpoint &&
                recovery.RestoreFeet == state.CheckpointFeet && recovery.RestoreYaw == 45 && recovered.CompanionFeet == state.CheckpointFeet,
                "Death recovery returns companion/player proposal to latest escort checkpoint without rolling back inspection");
            state = recovered;
            proof = Proof(state, DemoEscortCommand.SecondInspection); proof.CompanionFeet = Vector3.right * 50;
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.SecondInspection, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Remote companion cannot pass an inspection from the player's interaction alone");
            state = Step(state, DemoEscortCommand.SecondInspection);
            proof = Proof(state, DemoEscortCommand.Deliver); proof.CargoPresent = false;
            check(DemoEscortRules.TryPrepare(state, DemoEscortCommand.Deliver, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Delivery requires present quest cargo, not just two inspection flags");
            DemoEscortRules.TryPrepare(state, DemoEscortCommand.Deliver, Proof(state, DemoEscortCommand.Deliver), out var delivered, out var receipt);
            check(delivered.IsValid() && !delivered.OwnsSealedCargo && delivered.DeliveryRewardRecorded && receipt.RewardKey == "escort_delivery",
                "Delivery transfers cargo and records its one-time reward in the same candidate");
            check(DemoEscortRules.TryPrepare(delivered, DemoEscortCommand.Deliver, Proof(delivered, DemoEscortCommand.Deliver), out _, out _) == DemoEscortStatus.Duplicate,
                "Repeated delivery cannot produce another reward proposal");
            proof = Proof(delivered, DemoEscortCommand.VehicleRecalled); proof.ExpectedRevision--;
            check(DemoEscortRules.TryPrepare(delivered, DemoEscortCommand.VehicleRecalled, proof, out _, out _) == DemoEscortStatus.InvalidEvidence,
                "Captured evidence from an older revision cannot replay against a later state");
        }

        private sealed class Owner : IDemoEscortSessionOwner
        {
            public DemoEscortState State = new DemoEscortState();
            public bool RejectEvidence, FailSave, Conflict;
            public int Saves, Payments;
            public Action DuringCommit;
            public DemoEscortState ReadEscort() => State.Copy();
            public bool TryCaptureEscortEvidence(DemoEscortCommand command, DemoEscortState expected, out DemoEscortEvidence evidence)
            { evidence = Proof(expected, command); return !RejectEvidence; }
            public DemoEscortStatus TryCommitEscort(DemoEscortState expected, DemoEscortState candidate, DemoEscortReceipt receipt, out string error)
            {
                error = null; DuringCommit?.Invoke();
                if (FailSave) { error = "injected disk failure"; return DemoEscortStatus.SaveFailed; }
                if (Conflict || JsonUtility.ToJson(State) != JsonUtility.ToJson(expected)) return DemoEscortStatus.Conflict;
                State = candidate.Copy(); Saves++; if (receipt.RewardKey != null) Payments++; return DemoEscortStatus.Saved;
            }
        }
        private static void Transactions(Action<bool, string> check)
        {
            var owner = new Owner(); var service = new DemoEscortService(owner);
            owner.RejectEvidence = true;
            check(service.Execute(DemoEscortCommand.AcceptContract, out _, out _) == DemoEscortStatus.InvalidEvidence && owner.Saves == 0,
                "Service requires actual session owner evidence before preparing a transaction");
            owner.RejectEvidence = false; owner.FailSave = true;
            check(service.Execute(DemoEscortCommand.AcceptContract, out var receipt, out _) == DemoEscortStatus.SaveFailed && receipt == null && owner.State.Stage == DemoEscortStage.None,
                "Failed save publishes no state, receipt or reward");
            owner.FailSave = false; owner.Conflict = true;
            check(service.Execute(DemoEscortCommand.AcceptContract, out _, out _) == DemoEscortStatus.Conflict && owner.State.Revision == 0,
                "Revision/state compare conflict does not publish candidate");
            owner.Conflict = false; DemoEscortStatus nested = DemoEscortStatus.Prepared;
            owner.DuringCommit = () => nested = service.Execute(DemoEscortCommand.AcceptContract, out _, out _);
            check(service.Execute(DemoEscortCommand.AcceptContract, out _, out _) == DemoEscortStatus.Saved && nested == DemoEscortStatus.Busy && owner.Saves == 1,
                "Reentrant interaction during save cannot issue a second contract transaction");
            owner.DuringCommit = null;
            foreach (var command in new[] { DemoEscortCommand.StartEscort, DemoEscortCommand.Disembark, DemoEscortCommand.FirstInspection, DemoEscortCommand.SecondInspection })
                if (service.Execute(command, out _, out _) != DemoEscortStatus.Saved) throw new InvalidOperationException("Fixture " + command);
            owner.FailSave = true;
            check(service.Execute(DemoEscortCommand.Deliver, out receipt, out _) == DemoEscortStatus.SaveFailed && receipt == null && owner.Payments == 0 && owner.State.OwnsSealedCargo,
                "Delivery save failure preserves cargo and cannot pay before durable acceptance");
            owner.FailSave = false;
            check(service.Execute(DemoEscortCommand.Deliver, out _, out _) == DemoEscortStatus.Saved && owner.Payments == 1 &&
                service.Execute(DemoEscortCommand.Deliver, out _, out _) == DemoEscortStatus.Duplicate && owner.Payments == 1,
                "Successful delivery pays once through the atomic owner, including duplicate interaction retry");
        }

        private static void Migration(Action<bool, string> check)
        {
            WorldMacroProgress old = WorldMacroProgress.CreateNew("test", Vector3.zero, 0); old.version = 5; old.renUsed = true;
            old.ledger.currency = 123; old.campaign.Completed.Add("cargo_contract"); old.ledger.completed.Add("checkpoint_2");
            var migrated = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(old)));
            check(migrated != null && migrated.version == WorldMacroProgress.CurrentVersion && migrated.escort.Stage == DemoEscortStage.Contracted &&
                migrated.escort.InspectionsCleared == 0 && !migrated.escort.HasCheckpoint && migrated.ledger.currency == 123 && migrated.renUsed,
                "v5 migration preserves wallet/Ren and only imports proven contract, not visited inspection markers");
            var reloaded = WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(migrated)));
            check(reloaded != null && JsonUtility.ToJson(reloaded.escort) == JsonUtility.ToJson(migrated.escort), "Current escort state survives JSON reload without being reinitialized");
            old.campaign.Completed.Clear(); old.ledger.completed.Add("wangso_w1");
            migrated = WorldMacroProgress.MigrateToCurrent(old);
            check(migrated.escort.Stage == DemoEscortStage.None, "Dialogue/visited ID without campaign contract completion does not invent cargo ownership");
            migrated.escort.Stage = DemoEscortStage.Delivered;
            check(!WorldMacroProgress.Valid(migrated), "Current schema rejects delivered state without inspection/checkpoint/reward invariants");
            var invalid = new DemoEscortState { CheckpointFeet = new Vector3(float.NaN, 0, 0) };
            check(!invalid.IsValid(), "Non-finite persisted escort coordinates are rejected");
        }
    }
}
