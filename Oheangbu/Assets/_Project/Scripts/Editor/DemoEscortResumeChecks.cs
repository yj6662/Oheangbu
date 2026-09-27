using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    public static class DemoEscortResumeChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Live scene NavMesh/support/capsule probes and actor startup order", "Native save while driving, application restart and visible cargo reattachment" };
        }
        static MethodInfo Method(string name) => typeof(WorldMacroPlaytestSession).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new MissingMethodException(name);
        static WorldMacroProgress Fixture()
        {
            var p = WorldMacroProgress.CreateNew("terrain-A", Vector3.zero, 37);
            p.ledger.currency = 83; p.ledger.dropCurrency = 19; p.ledger.dropPosition = new Vector3(7, 1, 4); p.ledger.hasPosition = true;
            p.ledger.hp = .43f; p.ledger.ink = .62f; p.renUsed = true; p.defeated.Add("retained_enemy"); p.ledger.completed.Add("checkpoint_1");
            p.campaign.CampaignId = "escort-test"; p.campaign.Completed.AddRange(new[] { "cargo_contract", "escort", "checkpoint_one" });
            p.escort = new DemoEscortState { Revision = 9, Stage = DemoEscortStage.FirstInspectionCleared, CompanionMode = DemoEscortCompanionMode.Following,
                CompanionFeet = Vector3.right * 4, CheckpointId = "road_rest_1", CheckpointFeet = new Vector3(100, 0, 20), CheckpointYaw = 80, LastEvidenceId = "retained-event" };
            return p;
        }
        static bool Walking(WorldMacroProgress source, Vector3 companion, Vector3 cargo, out WorldMacroProgress candidate)
        {
            object[] args = { source, companion, cargo, null }; bool result = (bool)Method("TryPrepareDemoEscortWalkingSnapshot").Invoke(null, args);
            candidate = (WorldMacroProgress)args[3]; return result;
        }
        static bool Resume(WorldMacroProgress source, bool walking, Vector3 player, Vector3 companion, string cp, float yaw, string terrain, out WorldMacroProgress candidate)
        {
            object[] args = { source, walking, player, companion, cp, yaw, terrain, null }; bool result = (bool)Method("TryPrepareDemoEscortResume").Invoke(null, args);
            candidate = (WorldMacroProgress)args[7]; return result;
        }
        static bool Persist(WorldMacroProgress proposal, Action<WorldMacroProgress> persist, out WorldMacroProgress accepted)
        {
            object[] args = { proposal, persist, null, null }; bool result = (bool)Method("TryPersistDemoEscortCandidate").Invoke(null, args);
            accepted = (WorldMacroProgress)args[2]; return result;
        }
        static bool GameplayPreserved(WorldMacroProgress source, WorldMacroProgress result) => source.ledger.currency == result.ledger.currency &&
            source.ledger.dropCurrency == result.ledger.dropCurrency && source.ledger.dropPosition == result.ledger.dropPosition &&
            source.ledger.hp == result.ledger.hp && source.ledger.ink == result.ledger.ink && source.renUsed == result.renUsed &&
            JsonUtility.ToJson(source.campaign) == JsonUtility.ToJson(result.campaign) &&
            string.Join("|", source.ledger.completed) == string.Join("|", result.ledger.completed) && string.Join("|", source.defeated) == string.Join("|", result.defeated) &&
            source.escort.Stage == result.escort.Stage && source.escort.Revision == result.escort.Revision && source.escort.LastEvidenceId == result.escort.LastEvidenceId &&
            source.escort.OwnsSealedCargo == result.escort.OwnsSealedCargo;
        public static string Run()
        {
            var report = new Report(); void Check(bool ok, string name) => (ok ? report.passed : report.failed).Add(name);
            string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuEscortResume_" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            try
            {
                var source = Fixture(); string original = JsonUtility.ToJson(source);
                Check(Walking(source, Vector3.right * 20, Vector3.right * 20 + Vector3.up, out var walking), "On-foot candidate records the actual companion alongside saved player feet");
                Check(JsonUtility.ToJson(source) == original && !ReferenceEquals(source, walking) && walking.escort.CompanionFeet == Vector3.right * 20 && walking.ledger.position == source.ledger.position,
                    "Walking snapshot is detached and never pulls player to companion");
                Check(GameplayPreserved(source, walking), "Position snapshot preserves contract, inspections, exact wallet, HP, ink, virtue consumption, drop and encounter records");
                Check(!Walking(source, Vector3.right * 33, Vector3.right * 33, out _), "Out-of-range companion is not invented at the player's save position");
                Check(!Walking(source, Vector3.right * 4, Vector3.right * 7, out _), "A cargo object left behind cannot certify a walking snapshot");
                Check(!Walking(source, new Vector3(float.NaN, 0, 0), Vector3.zero, out _), "Non-finite measured feet are rejected");
                Check(Resume(walking, true, walking.ledger.position, walking.escort.CompanionFeet, "road_rest_1", 37, "terrain-A", out var resumed),
                    "Verified walking pair resumes at its saved locations even beyond inspection range");
                Check(resumed.ledger.position == walking.ledger.position && resumed.escort.CompanionFeet == walking.escort.CompanionFeet &&
                    resumed.escort.CompanionMode == DemoEscortCompanionMode.Waiting && GameplayPreserved(walking, resumed), "Walking restore clears transient following mode without reward, rest or progression");
                Check(resumed.ledger.checkpoint == walking.ledger.checkpoint && resumed.escort.CheckpointFeet == walking.escort.CheckpointFeet,
                    "Walking resume preserves normal checkpoint selection and latest escort checkpoint");
                Check(!Resume(walking, true, Vector3.right * 8, walking.escort.CompanionFeet, "road_rest_1", 37, "terrain-A", out _),
                    "Walking normalization cannot relocate player to an arbitrary point");
                Check(!Resume(walking, true, walking.ledger.position, Vector3.right * 23, "road_rest_1", 37, "terrain-A", out _),
                    "Walking normalization cannot invent remote companion travel");
                Check(!Resume(walking, true, walking.ledger.position, walking.escort.CompanionFeet, "road_rest_1", 37, "terrain-B", out _),
                    "Changed terrain requires authored checkpoint recovery instead of old walking coordinates");

                source.escort.CompanionMode = DemoEscortCompanionMode.Riding; original = JsonUtility.ToJson(source);
                Check(!Walking(source, Vector3.right * 4, Vector3.right * 4, out _), "A seated state is not written as a grounded walking snapshot");
                Check(!Resume(source, true, source.ledger.position, source.escort.CompanionFeet, "road_rest_1", 37, "terrain-A", out _),
                    "Riding cannot silently rejoin the old pre-boarding player location");
                Vector3 cp = source.escort.CheckpointFeet, npc = cp + Vector3.right * 1.5f;
                Check(Resume(source, false, cp, npc, "road_rest_1", 80, "terrain-B", out var recovery), "Riding restarts player and companion together at the verified latest escort checkpoint");
                Check(recovery.ledger.position == cp && recovery.ledger.checkpointPosition == cp && recovery.ledger.checkpoint == "road_rest_1" &&
                    recovery.escort.CompanionFeet == npc && recovery.escort.CompanionMode == DemoEscortCompanionMode.Waiting && recovery.terrainRevision == "terrain-B",
                    "Checkpoint normalization stores coherent player and separate companion feet with current terrain revision");
                Check(GameplayPreserved(source, recovery) && JsonUtility.ToJson(source) == original, "Checkpoint resume grants no rest healing, currency drop, inspection, reward or virtue reset");
                Check(!Resume(source, false, cp, npc, "future_checkpoint", 80, "terrain-A", out _), "Resume cannot select an unearned future checkpoint ID");
                Check(!Resume(source, false, cp, cp, "road_rest_1", 80, "terrain-A", out _), "Player and companion cannot resume in the same capsule footprint");
                Check(!Resume(source, false, cp, cp + Vector3.right * 40, "road_rest_1", 80, "terrain-A", out _), "Checkpoint proposal rejects a separated escort pair");

                string blocker = Path.Combine(folder, "not_a_directory"); File.WriteAllText(blocker, "intentional filesystem failure");
                var failedStore = new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "resume.json"), WorldMacroProgress.Valid);
                Check(!Persist(recovery, failedStore.Save, out var accepted) && accepted == null && JsonUtility.ToJson(source) == original,
                    "Actual failed normalization write publishes no replacement state or resumable relocation");
                string path = Path.Combine(folder, "resume.json"); var store = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid);
                Check(Persist(recovery, store.Save, out accepted) && ReferenceEquals(accepted, recovery), "Successful retry publishes the complete accepted resume candidate");
                var disk = store.Load(); Check(disk != null && disk.ledger.position == cp && disk.escort.CompanionFeet == npc && GameplayPreserved(source, disk),
                    "Disk contains matching resume feet and all previous gameplay records atomically");
                Check(Resume(disk, true, disk.ledger.position, disk.escort.CompanionFeet, "road_rest_1", disk.ledger.yaw, "terrain-B", out var duplicate) &&
                    JsonUtility.ToJson(disk) == JsonUtility.ToJson(duplicate), "Repeating normalized load is idempotent and pays nothing");
                var contract = Fixture(); contract.escort = new DemoEscortState { Stage = DemoEscortStage.Contracted };
                Check(!Walking(contract, Vector3.right, Vector3.right, out _) && !Resume(contract, false, cp, npc, "road_rest_1", 80, "terrain-A", out _),
                    "A normal accepted contract is outside escort travel normalization");
                var uncontracted = Fixture(); uncontracted.escort = new DemoEscortState();
                Check(!Walking(uncontracted, Vector3.right, Vector3.right, out _), "Ordinary walking without an escort contract uses the original save path");
            }
            catch (Exception e) { report.failed.Add(e.ToString()); }
            finally
            {
                string parent = Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(parent, Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(folder).StartsWith("OheangbuEscortResume_", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
