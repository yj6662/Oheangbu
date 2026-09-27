using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    /// <summary>Actual Session commit boundary and isolated disk writes. Fixtures inject measured proof;
    /// they do not claim a native character/vehicle journey or validate scene geometry.</summary>
    public static class DemoEscortSessionChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Native CanInteract/LOS and player/vehicle/NPC movement", "Authored safe checkpoints, passenger/cargo parenting and scene reload", "Live death input freeze and visible recovery" };
        }
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static MethodInfo Method(string name) => typeof(WorldMacroPlaytestSession).GetMethod(name, Private) ?? throw new MissingMethodException(name);
        static void Field(WorldMacroPlaytestSession session, string name, object value) => typeof(WorldMacroPlaytestSession).GetField(name, Private).SetValue(session, value);
        static void Progress(WorldMacroPlaytestSession session, WorldMacroProgress value) => typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, value);
        static DemoEscortEvidence Proof(DemoEscortState state, DemoEscortCommand command)
        {
            string id = command == DemoEscortCommand.AcceptContract ? "wangso_w1" : command == DemoEscortCommand.StartEscort ? "escort_start" :
                command == DemoEscortCommand.FirstInspection ? "checkpoint_1" : command == DemoEscortCommand.SecondInspection ? "checkpoint_2" : command == DemoEscortCommand.Deliver ? "cargo_delivery" : "";
            return new DemoEscortEvidence { Command = command, ExpectedRevision = state.Revision, EventId = Guid.NewGuid().ToString("N"),
                InteractionId = id, SessionVerified = true, CompanionPresent = true, CargoPresent = true, VehiclePresent = true,
                PlayerInVehicle = command == DemoEscortCommand.StartEscort || command == DemoEscortCommand.Board,
                CompanionLoaded = true, CargoLoaded = true, VehicleRecallVerified = true, SafeExitVerified = true, CheckpointVerified = true,
                DeathRecoveryVerified = true, CheckpointId = state.HasCheckpoint ? state.CheckpointId : "escort_start", CheckpointFeet = state.CheckpointFeet,
                CheckpointYaw = 45, PlayerFeet = Vector3.zero, PointFeet = Vector3.zero, CompanionFeet = Vector3.right, SafeExitFeet = Vector3.right };
        }
        static bool Prepare(WorldMacroProgress source, WorldMacroPlaytestSO content, DemoEscortState next, DemoEscortReceipt receipt, out WorldMacroProgress proposal)
        {
            object[] args = { source, content, next, receipt, new Vector3(3, 0, 4), null, null };
            bool result = (bool)Method("TryPrepareDemoEscortProgress").Invoke(null, args); proposal = (WorldMacroProgress)args[5]; return result;
        }
        static void Candidate(DemoEscortState state, DemoEscortCommand command, out DemoEscortEvidence proof, out DemoEscortState next, out DemoEscortReceipt receipt)
        {
            proof = Proof(state, command);
            if (DemoEscortRules.TryPrepare(state, command, proof, out next, out receipt) != DemoEscortStatus.Prepared) throw new InvalidOperationException("Fixture cannot prepare " + command);
        }
        public static string Run()
        {
            var report = new Report(); void Check(bool value, string name) => (value ? report.passed : report.failed).Add(name);
            string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuEscortChecks_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            var host = new GameObject("IsolatedEscortSessionChecks"); var session = host.AddComponent<WorldMacroPlaytestSession>();
            session.Actors = Array.Empty<PrologueEncounter>();
            var content = ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();
            var campaign = ScriptableObject.CreateInstance<DemoCampaignProfile>();
            try
            {
                string[] ids = { "wangso_w1", "escort_start", "checkpoint_1", "checkpoint_2", "cargo_delivery" };
                int[] rewards = { 10, 0, 15, 20, 100 };
                campaign.CampaignId = "isolated-escort"; campaign.Stages = new DemoCampaignProfile.Stage[ids.Length];
                content.Points = new PrologueContentSO.Point[ids.Length]; content.Campaign = campaign;
                for (int i = 0; i < ids.Length; i++)
                {
                    campaign.Stages[i] = new DemoCampaignProfile.Stage { Id = "stage_" + i, Objective = "test", TriggerId = ids[i], Implemented = true,
                        TongboReward = rewards[i], Event = i == 4 ? DemoEventKind.CargoDelivered : DemoEventKind.Interaction };
                    content.Points[i] = new PrologueContentSO.Point { Id = ids[i], Radius = 4, Currency = 1, Prompt = "test", Text = "test", Kind = PrologueInteractionKind.Conversation };
                }
                session.Content = content; Field(session, "ready", true);
                var source = WorldMacroProgress.CreateNew("test", Vector3.zero, 0); source.campaign.CampaignId = campaign.CampaignId; source.ledger.currency = 11;
                source.ledger.completed.Add("old_record"); source.ledger.dropCurrency = 7;
                Progress(session, source); var owner = (IDemoEscortSessionOwner)session;
                Check(!owner.TryCaptureEscortEvidence(DemoEscortCommand.AcceptContract, source.escort.Copy(), out _), "Actual Session rejects capture without configured live walker and companion");
                foreach (var point in content.Points)
                {
                    string before = JsonUtility.ToJson(source); object[] guard = { point, false };
                    Check((bool)Method("TryHandleDemoEscortInteraction").Invoke(session, guard) && !(bool)guard[1] && before == JsonUtility.ToJson(source),
                        "Owned but unverified " + point.Id + " cannot reach generic fallback or mutate progress");
                }
                object[] unrelated = { new PrologueContentSO.Point { Id = "unrelated" }, false };
                Check(!(bool)Method("TryHandleDemoEscortInteraction").Invoke(session, unrelated), "Unrelated interaction remains available to its original owner");
                string blocker = Path.Combine(folder, "not_a_directory"); File.WriteAllText(blocker, "intentional failure fixture");
                string path = Path.Combine(folder, "escort.json"); int changeCount = 0; session.DemoEscortChanged += (s, r) => changeCount++;
                var sequence = new[] { DemoEscortCommand.AcceptContract, DemoEscortCommand.StartEscort, DemoEscortCommand.Disembark,
                    DemoEscortCommand.FirstInspection, DemoEscortCommand.SecondInspection, DemoEscortCommand.Deliver };
                int total = 11, stages = 0;
                foreach (var command in sequence)
                {
                    source = session.Progress; string before = JsonUtility.ToJson(source); int notices = changeCount;
                    var expected = owner.ReadEscort(); Candidate(expected, command, out var proof, out var next, out var receipt);
                    Check(Prepare(source, content, next, receipt, out var proposal) && JsonUtility.ToJson(source) == before && !ReferenceEquals(source, proposal), "Detached atomic proposal for " + command);
                    Field(session, "escortCapturedProof", null);
                    Check(owner.TryCommitEscort(expected, next, receipt, out _) == DemoEscortStatus.InvalidEvidence && ReferenceEquals(session.Progress, source), "Missing captured owner evidence cannot commit " + command);
                    expected = owner.ReadEscort(); Field(session, "escortCapturedProof", proof);
                    Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "escort.json"), WorldMacroProgress.Valid));
                    Check(owner.TryCommitEscort(expected, next, receipt, out string error) == DemoEscortStatus.SaveFailed && !string.IsNullOrEmpty(error), "Actual disk failure rejected for " + command);
                    Check(ReferenceEquals(session.Progress, source) && before == JsonUtility.ToJson(source) && changeCount == notices, "Failed " + command + " publishes no escort state, campaign, reward or success notification");
                    expected = owner.ReadEscort(); Field(session, "escortCapturedProof", proof);
                    Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid));
                    Check(owner.TryCommitEscort(expected, next, receipt, out error) == DemoEscortStatus.Saved && error == null && changeCount == notices + 1, "Successful retry commits and notifies once for " + command);
                    if (!string.IsNullOrEmpty(receipt.CampaignTriggerId)) { total += rewards[stages] + 1; stages++; }
                    var loaded = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid).Load();
                    Check(loaded != null && loaded.escort.Revision == next.Revision && loaded.escort.Stage == next.Stage && loaded.campaign.Completed.Count == stages &&
                        loaded.ledger.currency == total && loaded.ledger.completed.Contains("old_record") && loaded.ledger.dropCurrency == 7, "Disk keeps escort, campaign and exact reward together for " + command);
                    Field(session, "escortCapturedProof", proof);
                    Check(owner.TryCommitEscort(expected, next, receipt, out _) == DemoEscortStatus.Conflict && session.Progress.ledger.currency == total && changeCount == notices + 1,
                        "Stale duplicate cannot pay or notify twice for " + command);
                }
                Check(session.Progress.escort.DeliveryRewardRecorded && !session.Progress.escort.OwnsSealedCargo && total == 161, "One contract, two inspections and delivery pay exactly the authored aggregate");

                source = WorldMacroProgress.CreateNew("test", Vector3.zero, 0); source.campaign.CampaignId = campaign.CampaignId; source.ledger.currency = int.MaxValue;
                Candidate(source.escort, DemoEscortCommand.AcceptContract, out _, out var contract, out var contractReceipt);
                Check(!Prepare(source, content, contract, contractReceipt, out _) && source.escort.Stage == DemoEscortStage.None && source.campaign.Completed.Count == 0,
                    "Overflow consumes neither contract nor campaign stage");
                source.ledger.currency = 12; Progress(session, source); var snapshot = owner.ReadEscort();
                Candidate(snapshot, DemoEscortCommand.AcceptContract, out var captured, out contract, out contractReceipt);
                source.ledger.currency++; Field(session, "escortCapturedProof", captured);
                Check(owner.TryCommitEscort(snapshot, contract, contractReceipt, out _) == DemoEscortStatus.Conflict && source.escort.Stage == DemoEscortStage.None,
                    "Concurrent non-escort wallet mutation rejects the stale full-progress transaction");

                source = WorldMacroProgress.CreateNew("test", Vector3.zero, 0); source.campaign.CampaignId = campaign.CampaignId; source.ledger.currency = 40; source.ledger.hp = 0;
                source.escort = new DemoEscortState { Stage = DemoEscortStage.FirstInspectionCleared, CompanionMode = DemoEscortCompanionMode.Riding,
                    Revision = 8, CheckpointId = "road_rest_1", CheckpointFeet = new Vector3(8, 0, 9), CheckpointYaw = 90 };
                source.campaign.Completed.AddRange(new[] { "stage_0", "stage_1", "stage_2" });
                Progress(session, source); source.campaign.CampaignId = "other-campaign";
                Check(!(bool)Method("TryHandleDemoEscortDeath").Invoke(session, null), "Stale escort data in another campaign cannot swallow normal death recovery");
                source.campaign.CampaignId = campaign.CampaignId;
                Candidate(source.escort, DemoEscortCommand.RecoverAfterDeath, out captured, out var restored, out var recoveryReceipt);
                string deadBefore = JsonUtility.ToJson(source);
                Check(Prepare(source, content, restored, recoveryReceipt, out var recovery) && recovery.ledger.hp == 1 && recovery.ledger.ink == 1 &&
                    recovery.ledger.position == restored.CheckpointFeet && recovery.ledger.checkpoint == "road_rest_1" && recovery.escort.InspectionsCleared == 1 &&
                    recovery.escort.OwnsSealedCargo && recovery.escort.CompanionMode == DemoEscortCompanionMode.Waiting && JsonUtility.ToJson(source) == deadBefore,
                    "Death proposal restores the latest escort checkpoint and preserves cargo and cleared inspection");
                Progress(session, source); snapshot = owner.ReadEscort(); Field(session, "escortCapturedProof", captured);
                Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "death.json"), WorldMacroProgress.Valid));
                Check(owner.TryCommitEscort(snapshot, restored, recoveryReceipt, out _) == DemoEscortStatus.SaveFailed && JsonUtility.ToJson(source) == deadBefore,
                    "Failed death save does not publish loss, respawn, or restored HP");
                snapshot = owner.ReadEscort(); Field(session, "escortCapturedProof", captured);
                string deathPath = Path.Combine(folder, "death.json"); Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(deathPath, WorldMacroProgress.Valid));
                Check(owner.TryCommitEscort(snapshot, restored, recoveryReceipt, out _) == DemoEscortStatus.Saved && session.Progress.ledger.hp == 1 && session.Progress.ledger.position == restored.CheckpointFeet,
                    "Death commit preserves recovered HP and position instead of sampling the dead player's transform");
                var recoveredDisk = new AtomicJsonStore<WorldMacroProgress>(deathPath, WorldMacroProgress.Valid).Load();
                Check(recoveredDisk != null && recoveredDisk.escort.InspectionsCleared == 1 && recoveredDisk.ledger.hp == 1 && recoveredDisk.ledger.dropCurrency > 0,
                    "Durable recovery includes currency drop and retained escort checkpoint");
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally
            {
                Field(session, "ready", false); UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(content); UnityEngine.Object.DestroyImmediate(campaign);
                string parent = Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(folder).StartsWith("OheangbuEscortChecks_", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
