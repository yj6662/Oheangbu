using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Detached proposal and durable transaction checks; never edits scene/campaign or a player save.</summary>
    public static class DemoGukRewardChecks
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        const int TestReward = 73; // Deliberately fixture-only: verifies that the configured amount is used.
        static readonly string[] PriorStages = { "commission", "mine_evidence", "office_report", "inn_rest", "relay", "cargo_contract", "logging", "deep_forest", "cheongryong" };
        [Serializable] sealed class Report
        {
            public string status, outputPath;
            public string scope = "Edit-mode detached TryPrepareGukRevisit and actual TryCommitInteraction/AtomicJsonStore writes in a UUID temporary directory. Fixture reward 73 is not a production reward choice. No stage enable, scene mutation or real player save.";
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Actual Guk lift/passenger support and authored location proof", "Native interaction and UI presentation", "Application restart", "Gameplay traversal and reward balance" };
        }
        static MethodInfo Method(string name) => typeof(WorldMacroPlaytestSession).GetMethod(name, Private) ?? throw new MissingMethodException(name);
        static void Set(WorldMacroPlaytestSession value, string name, object field) => typeof(WorldMacroPlaytestSession).GetField(name, Private).SetValue(value, field);
        static WorldMacroProgress Copy(WorldMacroProgress source) => JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source));
        static bool Prepare(WorldMacroProgress source, DemoCampaignProfile profile, out WorldMacroProgress proposal, out string error)
        {
            object[] args = { source, profile, null, null };
            bool accepted = (bool)Method("TryPrepareGukRevisit").Invoke(null, args);
            proposal = (WorldMacroProgress)args[2]; error = (string)args[3]; return accepted;
        }
        static bool Commit(WorldMacroPlaytestSession session, WorldMacroProgress proposal, out string error)
        {
            object[] args = { proposal, null }; bool accepted = (bool)Method("TryCommitInteraction").Invoke(session, args);
            error = (string)args[1]; return accepted;
        }
        public static string Execute(string command = "run")
        {
            if (command != "run") throw new ArgumentException("Use run for detached Guk reward checks.");
            return Run();
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run detached Guk checks in Edit mode; do not disturb a live session.");
            var report = new Report();
            void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); report.passed.Add(name); }
            string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuGukReward_" + Guid.NewGuid().ToString("N")));
            GameObject host = null; WorldMacroPlaytestSession session = null; DemoCampaignProfile profile = null;
            try
            {
                Directory.CreateDirectory(directory);
                profile = ScriptableObject.CreateInstance<DemoCampaignProfile>(); profile.hideFlags = HideFlags.HideAndDontSave;
                var stages = PriorStages.Select(id => new DemoCampaignProfile.Stage { Id = id, TriggerId = id,
                    Objective = "fixture " + id, Event = DemoEventKind.Interaction, Implemented = true }).ToList();
                stages.Add(new DemoCampaignProfile.Stage { Id = "guk_return", TriggerId = DemoGukRevisitSite.Id,
                    Objective = "fixture Guk revisit", Event = DemoEventKind.FieldUsed, Implemented = true, TongboReward = TestReward });
                stages.Add(new DemoCampaignProfile.Stage { Id = "escort", TriggerId = "escort_start", Objective = "unfinished escort",
                    Event = DemoEventKind.Interaction, Implemented = false }); profile.Stages = stages.ToArray();
                var source = WorldMacroProgress.CreateNew("guk-transaction-fixture", new Vector3(10, 20, 30), 61);
                source.campaign.CampaignId = profile.CampaignId; source.campaign.Completed.AddRange(PriorStages);
                source.ledger.completed.Add(WorldMacroPlaytestSession.GiyeokUnlockId); source.ledger.completed.Add("prior_evidence");
                source.ledger.currency = 19; source.ledger.dropCurrency = 31; source.ledger.dropPosition = new Vector3(5, 6, 7);
                source.ledger.hp = .42f; source.ledger.ink = .37f; source.ui.AddItem("existing_fragment", 3);
                source.ui.DiscoverMarker("known_mine"); source.ui.knownSpellLetters.Add("국"); source.ui.LearnVirtue("仁");
                source.renUsed = true; source.defeated.Add(WorldMacroPlaytestSession.CheongryongId);
                Check(profile.IsValid && WorldMacroProgress.Valid(source), "Fixture profile and complete pre-revisit progress are valid");
                string profileBefore = JsonUtility.ToJson(profile), before = JsonUtility.ToJson(source);
                var locked = Copy(source); locked.ledger.completed.Remove(WorldMacroPlaytestSession.GiyeokUnlockId);
                Check(!Prepare(locked, profile, out var rejected, out _) && rejected == null,
                    "Known 国 text and boss defeat alone do not replace the explicit unlock token");
                var early = Copy(source); early.campaign.Completed.Remove("deep_forest");
                Check(!Prepare(early, profile, out rejected, out _) && rejected == null, "Cannot skip an earlier incomplete stage even with unlock");
                early = Copy(source); early.campaign.CampaignId = "other-campaign";
                Check(!Prepare(early, profile, out rejected, out _), "Foreign campaign state cannot claim the revisit");
                var stage = profile.Stages[9]; stage.Implemented = false;
                Check(!Prepare(source, profile, out rejected, out _) && rejected == null, "Unimplemented FieldUsed stage cannot pay");
                stage.Implemented = true; stage.Event = DemoEventKind.Interaction;
                Check(!Prepare(source, profile, out rejected, out _), "Interaction event cannot impersonate FieldUsed");
                stage.Event = DemoEventKind.FieldUsed; stage.TriggerId = "unrelated_field_site";
                Check(!Prepare(source, profile, out rejected, out _), "Wrong field site trigger cannot advance");
                stage.TriggerId = DemoGukRevisitSite.Id;
                Check(!Prepare(null, profile, out rejected, out _) && !Prepare(source, null, out rejected, out _), "Missing source/profile rejected safely");
                var invalid = Copy(source); invalid.ledger.currency = -1;
                Check(!Prepare(invalid, profile, out rejected, out _), "Invalid progress cannot create a reward proposal");
                Check(Prepare(source, profile, out var proposal, out _) && proposal != null && !ReferenceEquals(proposal, source), "Valid Guk revisit creates a detached proposal");
                Check(JsonUtility.ToJson(source) == before, "Preparation does not mutate live currency, completion or inventory");
                Check(proposal.ledger.currency == 19 + TestReward && proposal.ledger.completed.Contains(DemoGukRevisitSite.Id) &&
                    proposal.campaign.Completed.Count == 10 && proposal.campaign.Completed.Last() == "guk_return", "One configured reward and ordered completion are proposed together");
                Check(proposal.ui.GetItemCount("existing_fragment") == 3 && proposal.ui.discoveredMarkers.Contains("known_mine") &&
                    proposal.ledger.dropCurrency == 31 && proposal.ledger.dropPosition == source.ledger.dropPosition &&
                    proposal.ledger.hp == source.ledger.hp && proposal.ledger.ink == source.ledger.ink && proposal.renUsed &&
                    proposal.defeated.SequenceEqual(source.defeated), "Existing items, map discovery, unrecovered drop, resources, Ren and defeated IDs preserved");
                var isolated = Copy(proposal); isolated.ui.AddItem("existing_fragment", 1);
                Check(source.ui.GetItemCount("existing_fragment") == 3 && proposal.ui.GetItemCount("existing_fragment") == 3,
                    "Nested proposal data remains independent of source and later copies");
                host = new GameObject("Detached Guk reward transaction") { hideFlags = HideFlags.HideAndDontSave };
                host.SetActive(false); session = host.AddComponent<WorldMacroPlaytestSession>(); session.Actors = Array.Empty<PrologueEncounter>();
                Set(session, "ready", true); Set(session, "lastSafe", source.ledger.position);
                typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, source);
                string blocker = Path.Combine(directory, "file-not-directory"); File.WriteAllText(blocker, "deliberate write failure");
                Set(session, "store", new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker, "save.json"), WorldMacroProgress.Valid));
                Check(!Commit(session, proposal, out string error) && !string.IsNullOrEmpty(error), "Real filesystem write failure is reported");
                Check(ReferenceEquals(session.Progress, source) && JsonUtility.ToJson(source) == before,
                    "Failed disk write publishes no reward, completion, discovery or inventory change");
                Check(Prepare(source, profile, out var retry, out _), "Failed claim can prepare a fresh retry");
                string path = Path.Combine(directory, "accepted.json");
                Set(session, "store", new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid));
                Check(Commit(session, retry, out error) && error == null && ReferenceEquals(session.Progress, retry), "Recovered storage accepts and publishes retry");
                string acceptedDisk = File.ReadAllText(path);
                var loaded = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid).Load();
                Check(loaded != null && loaded.ledger.currency == 19 + TestReward && loaded.ledger.completed.Contains(DemoGukRevisitSite.Id) &&
                    loaded.campaign.Completed.SequenceEqual(retry.campaign.Completed) && loaded.ui.GetItemCount("existing_fragment") == 3 &&
                    loaded.ledger.dropCurrency == 31 && loaded.ledger.dropPosition == source.ledger.dropPosition && loaded.renUsed,
                    "Independent disk reload retains exact one-time reward and prior state");
                Check(!Prepare(session.Progress, profile, out rejected, out _) && !Prepare(loaded, profile, out rejected, out _),
                    "Live and freshly reloaded duplicate interactions cannot pay again");
                Check(Commit(session, retry, out _) && session.Progress.ledger.currency == 19 + TestReward && File.ReadAllText(path) == acceptedDisk,
                    "Repeated commit of accepted proposal neither repays nor rewrites differing data");
                var consumed = Copy(source); consumed.ledger.completed.Add(DemoGukRevisitSite.Id);
                Check(!Prepare(consumed, profile, out rejected, out _), "Pickup receipt blocks replay even if campaign completion is absent");
                consumed = Copy(source); consumed.campaign.Completed.Add("guk_return");
                Check(!Prepare(consumed, profile, out rejected, out _), "Campaign receipt blocks replay even if pickup receipt is absent");
                var overflow = Copy(source); overflow.ledger.currency = int.MaxValue - TestReward + 1;
                string overflowBefore = JsonUtility.ToJson(overflow);
                Check(!Prepare(overflow, profile, out rejected, out error) && rejected == null && !string.IsNullOrEmpty(error) &&
                    JsonUtility.ToJson(overflow) == overflowBefore, "Overflow creates no partial reward or consumed receipt");
                overflow.ledger.currency = int.MaxValue - TestReward;
                Check(Prepare(overflow, profile, out var boundary, out _) && boundary.ledger.currency == int.MaxValue,
                    "Exact integer maximum succeeds without overflow");
                Check(DemoCampaignProgression.Current(profile, loaded.campaign).Id == "escort" &&
                    !DemoCampaignProgression.Current(profile, loaded.campaign).Implemented && JsonUtility.ToJson(profile) == profileBefore,
                    "Revisit does not enable the unfinished next stage or mutate campaign configuration");
            }
            catch (Exception exception) { report.failed.Add(exception.ToString()); }
            finally
            {
                if (session != null) Set(session, "ready", false);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
                string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if (string.Equals(Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar), expectedParent, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(directory).StartsWith("OheangbuGukReward_", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            report.outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/Chapter3/guk_reward_tests.json"));
            string json = JsonUtility.ToJson(report, true); Directory.CreateDirectory(Path.GetDirectoryName(report.outputPath));
            File.WriteAllText(report.outputPath, json); return json;
        }
    }
}
