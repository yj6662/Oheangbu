using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoGrowthLessonChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string scope = "Actual detached lesson proposal and session store. Authored enemy and native combat remain unverified.";
        }
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static void Set(WorldMacroPlaytestSession s, string key, object value) => typeof(WorldMacroPlaytestSession).GetField(key, Flags).SetValue(s, value);
        static bool Prepare(WorldMacroProgress source, DemoCampaignProfile profile, out WorldMacroProgress proposal)
        {
            object[] args = { source, profile, null, null };
            bool result = (bool)typeof(WorldMacroPlaytestSession).GetMethod("TryPrepareGrowthLesson", Flags).Invoke(null, args);
            proposal = (WorldMacroProgress)args[2]; return result;
        }
        static bool Commit(WorldMacroPlaytestSession session, WorldMacroProgress proposal)
        {
            object[] args = { proposal, null };
            return (bool)typeof(WorldMacroPlaytestSession).GetMethod("TryCommitInteraction", Flags).Invoke(session, args);
        }
        public static string Run()
        {
            var report = new Report();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuGrowthLesson_" + Guid.NewGuid().ToString("N")));
            var profile = ScriptableObject.CreateInstance<DemoCampaignProfile>();
            var host = new GameObject("Isolated lesson transaction");
            var session = host.AddComponent<WorldMacroPlaytestSession>(); session.Actors = Array.Empty<PrologueEncounter>();
            try
            {
                Directory.CreateDirectory(directory);
                profile.Stages = new[] {
                    new DemoCampaignProfile.Stage { Id = "logging", TriggerId = "logging_inquiry", Objective = "logging", Event = DemoEventKind.Interaction, Implemented = true },
                    new DemoCampaignProfile.Stage { Id = "deep_forest", TriggerId = DemoGrowthLessonLink.LessonId, Objective = "metal interrupts growth", Event = DemoEventKind.GrowthInterrupted, Implemented = true, TongboReward = 40 },
                    new DemoCampaignProfile.Stage { Id = "cheongryong", TriggerId = "cheongryong", Objective = "boss", Event = DemoEventKind.BossDefeated, Implemented = false }
                };
                var source = WorldMacroProgress.CreateNew("test", Vector3.zero, 0);
                source.campaign.CampaignId = profile.CampaignId; source.ledger.currency = 10;
                Check(!Prepare(source, profile, out _), "Out-of-order growth cannot skip logging");
                source.campaign.Completed.Add("logging");
                Check(!DemoCampaignProgression.TryAdvance(profile, source.campaign, DemoEventKind.Interaction, DemoGrowthLessonLink.LessonId, out _, out _), "Ordinary interaction cannot complete the confirmed-hit lesson");
                Check(!DemoCampaignProgression.TryAdvance(profile, source.campaign, DemoEventKind.GrowthInterrupted, "other", out _, out _), "Unrelated interrupt ID cannot complete lesson");
                string before = JsonUtility.ToJson(source);
                Check(Prepare(source, profile, out var proposal) && proposal.campaign.Completed.Contains("deep_forest") && proposal.ledger.currency == 50, "Lesson and reward proposed together");
                Check(JsonUtility.ToJson(source) == before && !ReferenceEquals(source, proposal), "Proposal does not mutate current progress");
                Set(session, "ready", true); typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, source);
                string blocker = Path.Combine(directory, "file-not-directory"); File.WriteAllText(blocker, "block writes");
                Set(session, "store", new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "save.json"), WorldMacroProgress.Valid));
                Check(!Commit(session, proposal) && ReferenceEquals(session.Progress, source) && JsonUtility.ToJson(source) == before, "Failed actual save grants no lesson or reward");
                string save = Path.Combine(directory, "accepted.json");
                Set(session, "store", new AtomicJsonStore<WorldMacroProgress>(save, WorldMacroProgress.Valid));
                Check(Commit(session, proposal) && ReferenceEquals(session.Progress, proposal), "Retry publishes only accepted state");
                var loaded = new AtomicJsonStore<WorldMacroProgress>(save, WorldMacroProgress.Valid).Load();
                Check(loaded.campaign.Completed.Contains("deep_forest") && loaded.ledger.completed.Contains(DemoGrowthLessonLink.LessonId) && loaded.ledger.currency == 50, "Disk records proof and exact reward");
                Check(!Prepare(loaded, profile, out _), "Repeated interruption cannot duplicate reward");
                source.ledger.currency = int.MaxValue;
                Check(!Prepare(source, profile, out _) && !source.campaign.Completed.Contains("deep_forest"), "Overflow leaves lesson claimable");
                Check(!session.TryCompleteGrowthLesson(null), "Missing runtime proof is rejected");
                Check(DemoCampaignProgression.Current(profile, loaded.campaign).Implemented == false, "Lesson completion does not enable unfinished boss");
            }
            catch (Exception exception) { report.failed.Add(exception.ToString()); }
            finally
            {
                Set(session, "ready", false); UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(profile);
                if (Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(directory).StartsWith("OheangbuGrowthLesson_", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }
    }
}
