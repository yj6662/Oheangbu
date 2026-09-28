using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class DemoBossRewardChecks
    {
        [Serializable] sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string scope = "Detached boss reward, actual disk transaction, duplicate and retry checks. Native encounter completion is separate.";
        }
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        static void Set(WorldMacroPlaytestSession session, string key, object value) => typeof(WorldMacroPlaytestSession).GetField(key, Flags).SetValue(session, value);
        static bool Prepare(WorldMacroProgress source, DemoCampaignProfile profile, string id, out WorldMacroProgress proposal)
        {
            object[] args = { source, profile, id, null, null };
            bool ok = (bool)typeof(WorldMacroPlaytestSession).GetMethod("TryPrepareCheongryongDefeat", Flags).Invoke(null, args);
            proposal = (WorldMacroProgress)args[3]; return ok;
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
            string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OheangbuBossReward_" + Guid.NewGuid().ToString("N")));
            var profile = ScriptableObject.CreateInstance<DemoCampaignProfile>();
            var host = new GameObject("Isolated boss reward transaction");
            var session = host.AddComponent<WorldMacroPlaytestSession>(); session.Actors = Array.Empty<PrologueEncounter>();
            try
            {
                Directory.CreateDirectory(directory);
                profile.Stages = new[] {
                    new DemoCampaignProfile.Stage { Id="deep_forest", TriggerId="metal_growth_lesson", Objective="lesson", Event=DemoEventKind.GrowthInterrupted, Implemented=true },
                    new DemoCampaignProfile.Stage { Id="cheongryong", TriggerId="cheongryong", Objective="boss", Event=DemoEventKind.BossDefeated, Implemented=true, TongboReward=160 },
                    new DemoCampaignProfile.Stage { Id="guk", TriggerId="guk_ledge", Objective="field", Event=DemoEventKind.FieldUsed, Implemented=false }
                };
                var source = WorldMacroProgress.CreateNew("test", new Vector3(3,4,5), 27);
                source.campaign.CampaignId=profile.CampaignId; source.ledger.currency=17;
                source.ui.AddItem("existing",3); source.ui.DiscoverMarker("old"); source.ledger.dropCurrency=19;
                Check(!Prepare(source,profile,"cheongryong",out _),"Cannot skip growth lesson");
                source.campaign.Completed.Add("deep_forest");
                Check(!Prepare(source,profile,"unrelated",out _),"Other enemy cannot claim boss rewards");
                profile.Stages[1].Implemented=false;
                Check(!Prepare(source,profile,"cheongryong",out _),"Unimplemented stage cannot award");
                profile.Stages[1].Implemented=true;
                string before=JsonUtility.ToJson(source);
                Check(Prepare(source,profile,"cheongryong",out var proposal),"Valid confirmed defeat creates proposal");
                Check(JsonUtility.ToJson(source)==before,"Proposal leaves live progress untouched");
                Check(proposal.defeated.Contains("cheongryong")&&proposal.campaign.Completed.Contains("cheongryong")&&proposal.ledger.currency==177,"Defeat, campaign and exact reward are atomic");
                Check(proposal.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId)&&proposal.ui.knownSpellLetters.Contains("국")&&proposal.ui.knownVirtues.Contains("仁")&&!proposal.renUsed,"Coda, Guk and charged Ren are granted together");
                Check(proposal.ui.GetItemCount("existing")==3&&proposal.ui.discoveredMarkers.Contains("old")&&proposal.ledger.dropCurrency==19,"Existing inventory map and unrecovered drop retained");
                Set(session,"ready",true); typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session,source);
                string blocker=Path.Combine(directory,"file-not-directory"); File.WriteAllText(blocker,"reject writes");
                Set(session,"store",new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,"save.json"),WorldMacroProgress.Valid));
                Check(!Commit(session,proposal)&&ReferenceEquals(session.Progress,source)&&JsonUtility.ToJson(source)==before,"Actual failed write grants nothing");
                string path=Path.Combine(directory,"accepted.json");
                Set(session,"store",new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid));
                Check(Commit(session,proposal),"Same reward retries successfully after storage recovers");
                var loaded=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid).Load();
                Check(loaded.defeated.Contains("cheongryong")&&loaded.ledger.completed.Contains(WorldMacroPlaytestSession.GiyeokUnlockId)&&loaded.ledger.currency==177,"Reload preserves exact defeat and unlocks");
                Check(!Prepare(loaded,profile,"cheongryong",out _),"Reload and duplicate defeat cannot repay");
                source.ui.LearnVirtue("仁"); source.renUsed=true;
                Check(Prepare(source,profile,"cheongryong",out var existing)&&existing.renUsed,"Preexisting consumed Ren is not silently recharged");
                source.ledger.currency=int.MaxValue;
                Check(!Prepare(source,profile,"cheongryong",out _)&&!source.defeated.Contains("cheongryong"),"Overflow retains claim without partial unlock");
                Check(!DemoCampaignProgression.Current(profile,loaded.campaign).Implemented,"Reward does not auto-enable unfinished field service");
            }
            catch(Exception exception){report.failed.Add(exception.ToString());}
            finally
            {
                Set(session,"ready",false); UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(profile);
                if(Path.GetDirectoryName(directory).TrimEnd(Path.DirectorySeparatorChar)==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(directory).StartsWith("OheangbuBossReward_",StringComparison.Ordinal)&&Directory.Exists(directory)) Directory.Delete(directory,true);
            }
            report.status=report.failed.Count==0?"PASS":"FAIL"; return JsonUtility.ToJson(report,true);
        }
    }
}
