using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    /// <summary>Isolated registered EnemyVitals death events and actual transaction IO, never a live scene/save.</summary>
    public static class DemoSouthGateCompletionChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Authored general model/navigation, native combat and current-stage activation", "Physical gate blocker removal/animation and completion screen", "Application startup and live rest traversal" };
        }
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static MethodInfo Method(string name) => typeof(WorldMacroPlaytestSession).GetMethod(name, Private) ?? throw new MissingMethodException(name);
        static void Field(WorldMacroPlaytestSession session, string name, object value) => typeof(WorldMacroPlaytestSession).GetField(name, Private).SetValue(session, value);
        static void Progress(WorldMacroPlaytestSession session, WorldMacroProgress value) => typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, value);
        static bool Prepare(WorldMacroProgress source, DemoCampaignProfile profile, out WorldMacroProgress proposal)
        {
            object[] args = { source, profile, null, null }; bool ok = (bool)Method("TryPrepareSouthGateCompletion").Invoke(null, args); proposal = (WorldMacroProgress)args[2]; return ok;
        }
        static void Retry(WorldMacroPlaytestSession session) => Method("TryCommitPendingDemoSouthGate").Invoke(session, new object[] { true });
        static WorldMacroProgress Fixture(DemoCampaignProfile profile)
        {
            var source = WorldMacroProgress.CreateNew("isolated-south-gate", Vector3.zero, 27); source.campaign.CampaignId = profile.CampaignId;
            source.campaign.Completed.Add("delivery"); source.ledger.completed.Add("cargo_delivery"); source.ledger.currency = 11; source.ledger.dropCurrency = 7;
            source.ledger.hp = .4f; source.ledger.ink = .7f; source.renUsed = true; source.defeated.Add("previous_boss");
            source.escort = new DemoEscortState { Stage = DemoEscortStage.Delivered, CompanionMode = DemoEscortCompanionMode.Waiting,
                CheckpointId = "capital_escort_rest", CheckpointFeet = new Vector3(2, 0, 3), DeliveryRewardRecorded = true, Revision = 8, LastEvidenceId = "delivered-once" };
            return source;
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run isolated south gate completion checks in Edit mode.");
            var report = new Report(); void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuSouthGateCompletion_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            Scene scene = default; WorldMacroPlaytestSession session = null;
            var content = ScriptableObject.CreateInstance<WorldMacroPlaytestSO>(); var profile = ScriptableObject.CreateInstance<DemoCampaignProfile>();
            try
            {
                profile.CampaignId = "isolated-completion";
                profile.Stages = new[] {
                    new DemoCampaignProfile.Stage { Id = "delivery", TriggerId = "cargo_delivery", Objective = "delivered", Event = DemoEventKind.CargoDelivered, Implemented = true },
                    new DemoCampaignProfile.Stage { Id = "south_gate", TriggerId = WorldMacroPlaytestSession.SouthGateGeneralId, Objective = "general", Event = DemoEventKind.BossDefeated, TongboReward = 37, Implemented = true },
                    new DemoCampaignProfile.Stage { Id = "ending", TriggerId = WorldMacroPlaytestSession.SouthGateOpenedId, Objective = "gate", Event = DemoEventKind.GateOpened, TongboReward = 13, Implemented = true,
                        RequiredDefeatedIds = new[] { WorldMacroPlaytestSession.SouthGateGeneralId } } };
                content.Campaign = profile; content.TerrainRevision = "isolated-south-gate";
                var source = Fixture(profile); string before = JsonUtility.ToJson(source);
                Check(Prepare(source, profile, out var proposal) && !ReferenceEquals(source, proposal) && JsonUtility.ToJson(source) == before,
                    "General defeat and gate completion are prepared without mutating source");
                Check(proposal.campaign.Completed.Contains("south_gate") && proposal.campaign.Completed.Contains("ending") && proposal.defeated.Contains(WorldMacroPlaytestSession.SouthGateGeneralId) &&
                    proposal.ledger.completed.Contains(WorldMacroPlaytestSession.SouthGateOpenedId) && proposal.ledger.currency == 61,
                    "One candidate includes both configured rewards, defeated general, gate-open marker and ordered ending");
                Check(JsonUtility.ToJson(proposal.escort) == JsonUtility.ToJson(source.escort) && proposal.ledger.hp == .4f && proposal.ledger.ink == .7f &&
                    proposal.ledger.dropCurrency == 7 && proposal.renUsed && proposal.defeated.Contains("previous_boss"), "Completion preserves delivered cargo, resources, drop, virtue consumption and previous defeats");
                Check(!Prepare(proposal, profile, out _), "Already completed state cannot create another payment candidate");
                var legacy = Fixture(profile); legacy.version = 5; legacy.escort = null;
                Check(!Prepare(legacy, profile, out _), "Unmigrated legacy progress without escort delivery evidence is rejected safely");
                source.campaign.Completed.Clear(); Check(!Prepare(source, profile, out _), "Earlier campaign stage cannot be skipped by a boss ID"); source.campaign.Completed.Add("delivery");
                source.escort.Stage = DemoEscortStage.SecondInspectionCleared; source.escort.DeliveryRewardRecorded = false;
                Check(!Prepare(source, profile, out _), "Campaign text alone cannot replace durable cargo delivery state"); source = Fixture(profile);
                profile.Stages[1].Implemented = false; Check(!Prepare(source, profile, out _), "An unimplemented general stage cannot complete"); profile.Stages[1].Implemented = true;
                profile.Stages[2].Implemented = false; Check(!Prepare(source, profile, out _), "An unimplemented gate stage prevents partial boss/completion publication"); profile.Stages[2].Implemented = true;
                profile.Stages[2].Event = DemoEventKind.Interaction; Check(!Prepare(source, profile, out _), "An interaction cannot impersonate gate opening"); profile.Stages[2].Event = DemoEventKind.GateOpened;
                profile.Stages[2].TriggerId = "unrelated_gate"; Check(!Prepare(source, profile, out _), "A different gate trigger cannot complete the demo"); profile.Stages[2].TriggerId = WorldMacroPlaytestSession.SouthGateOpenedId;
                source.ledger.currency = int.MaxValue - 40; Check(!Prepare(source, profile, out _) && !source.defeated.Contains(WorldMacroPlaytestSession.SouthGateGeneralId),
                    "Combined reward overflow consumes neither general death nor ending"); source = Fixture(profile);

                scene = EditorSceneManager.NewPreviewScene();
                var host = new GameObject("Isolated South Gate Session"); host.SetActive(false); SceneManager.MoveGameObjectToScene(host, scene);
                session = host.AddComponent<WorldMacroPlaytestSession>(); session.Content = content; Field(session, "ready", true); Field(session, "lastSafe", Vector3.zero); Progress(session, source);
                var general = new GameObject("Registered General"); general.SetActive(false); SceneManager.MoveGameObjectToScene(general, scene);
                var life = general.AddComponent<EnemyVitals>(); life.Restore(); var controller = general.AddComponent<SouthGateGeneralController>();
                var actor = general.AddComponent<PrologueEncounter>(); actor.Id = WorldMacroPlaytestSession.SouthGateGeneralId;
                session.Actors = new[] { actor }; var spec = new WorldMacroPlaytestSO.Encounter { Id = actor.Id, RespawnOnRest = false }; content.Encounters = new[] { spec };
                Check(session.ConfigureDemoSouthGate(controller), "Only the registered scene general configures the death owner");
                session.Actors = new[] { actor, actor }; Check(!session.ConfigureDemoSouthGate(controller), "Duplicate registered general IDs are rejected"); session.Actors = new[] { actor };
                spec.RespawnOnRest = true; Check(!session.ConfigureDemoSouthGate(controller), "A respawning encounter cannot own the persistent final gate"); spec.RespawnOnRest = false;
                var outsider = new GameObject("Unregistered General"); outsider.SetActive(false); SceneManager.MoveGameObjectToScene(outsider, scene);
                var other = outsider.AddComponent<SouthGateGeneralController>(); Check(!session.ConfigureDemoSouthGate(other), "An unregistered controller cannot provide a death witness");
                int opens = 0; session.DemoSouthGateOpened += () => opens++;
                Check((bool)Method("TryHandleDemoSouthGateDefeat").Invoke(session, new object[] { actor.Id }) && !session.DemoSouthGateSavePending && !session.DemoSouthGateOpen,
                    "Calling the registered ID while alive is consumed without generic reward or gate opening");
                life.TakeDamage(float.MaxValue);
                Check((bool)Method("TryHandleDemoSouthGateDefeat").Invoke(session, new object[] { actor.Id }) && !session.DemoSouthGateSavePending && !session.DemoSouthGateOpen,
                    "A dead component without a subscribed actual death event is not retroactive proof");
                life.Restore(); Method("BindDemoSouthGate").Invoke(session, null);
                string blocker = Path.Combine(folder, "not_a_directory"); File.WriteAllText(blocker, "intentional write failure");
                Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "completion.json"), WorldMacroProgress.Valid));
                before = JsonUtility.ToJson(source); life.TakeDamage(float.MaxValue);
                Check(!life.IsAlive && session.DemoSouthGateSavePending && JsonUtility.ToJson(source) == before && ReferenceEquals(session.Progress, source) && !session.DemoSouthGateOpen && opens == 0,
                    "Actual registered death with disk failure leaves the general dead and gate/rewards unpublished");
                Method("UnbindDemoSouthGate").Invoke(session, null); Method("BindDemoSouthGate").Invoke(session, null); Retry(session);
                Check(session.DemoSouthGateSavePending && !session.DemoSouthGateOpen && opens == 0, "Rebinding and failed retry preserve the same still-dead witness without reopening");
                life.Restore(); Retry(session);
                Check(!session.DemoSouthGateSavePending && !session.DemoSouthGateOpen && session.Progress.ledger.currency == 11,
                    "A new enemy life invalidates the previous pending death witness");
                life.TakeDamage(float.MaxValue); Check(session.DemoSouthGateSavePending, "A new actual death can establish a fresh pending witness");
                string path = Path.Combine(folder, "completion.json"); Field(session, "store", new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid));
                Retry(session);
                Check(!session.DemoSouthGateSavePending && session.DemoSouthGateOpen && session.DemoCampaignCompleted && opens == 1 && session.Progress.ledger.currency == 61,
                    "Successful same-death retry opens the gate and publishes completion once after durable save");
                Retry(session); Method("TryHandleDemoSouthGateDefeat").Invoke(session, new object[] { actor.Id }); life.TakeDamage(float.MaxValue);
                Check(opens == 1 && session.Progress.ledger.currency == 61, "Duplicate death notifications and retries cannot pay or replay gate opening");
                var loaded = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid).Load(); Progress(session, loaded); Method("BindDemoSouthGate").Invoke(session, null);
                Check(loaded != null && session.DemoSouthGateOpen && session.DemoCampaignCompleted && opens == 1 && !session.DemoSouthGateEncounterAvailable,
                    "Loaded durable state exposes the open gate/completion without emitting another reward event or respawning fight");
                content.Points = new[] { new PrologueContentSO.Point { Id = "south_gate_approach_rest", Kind = PrologueInteractionKind.Rest } };
                content.Checkpoints = new[] { new WorldMacroPlaytestSO.CheckpointSpec { Id = "south_gate_approach_rest", Label = "test rest", Feet = Vector3.zero, Yaw = 0 } };
                Check(WorldMacroCheckpointRules.TryProposeRest(content, loaded, "south_gate_approach_rest", Vector3.zero, out var rested, out _) &&
                    rested.defeated.Contains(actor.Id) && rested.ledger.completed.Contains(WorldMacroPlaytestSession.SouthGateOpenedId) && rested.ledger.currency == 61,
                    "Normal rest preserves the nonrespawning general, gate marker, ending and exact reward");
                Progress(session, rested); Check(session.DemoCampaignCompleted && session.DemoSouthGateOpen, "Rested progress keeps completion and gate state readable");
                var originalStages = profile.Stages; Array.Resize(ref originalStages, 4); originalStages[3] = new DemoCampaignProfile.Stage {
                    Id = "future", TriggerId = "future", Objective = "not built", Event = DemoEventKind.Interaction, Implemented = false }; profile.Stages = originalStages;
                Check(session.DemoSouthGateOpen && !session.DemoCampaignCompleted, "An appended unfinished stage is not falsely reported as whole-campaign completion");
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally
            {
                if (session != null) Field(session, "ready", false);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(content); UnityEngine.Object.DestroyImmediate(profile);
                string parent = Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(folder).StartsWith("OheangbuSouthGateCompletion_", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
