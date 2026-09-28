using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    /// <summary>Real PlayerVitals -> Session gate -> AtomicJsonStore, with isolated temporary saves.</summary>
    public static class DemoRenSurvivalChecks
    {
        [Serializable] sealed class Result
        {
            public string status="PASS",scope="Edit-mode lethal hit, real persistence failure/retry, reload and rest proposals. No production save or live scene mutation.";
            public List<string> checks=new List<string>();
        }
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Field(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
        static void SetProgress(WorldMacroPlaytestSession session,WorldMacroProgress value)=>typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session,value);
        static void Call(WorldMacroPlaytestSession session,string name)=>typeof(WorldMacroPlaytestSession).GetMethod(name,Private).Invoke(session,null);
        static bool Near(float a,float b)=>Mathf.Abs(a-b)<.0001f;

        public static string Run()
        {
            var result=new Result();
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException(name);result.checks.Add(name);};
            string folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"OheangbuRenChecks_"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(folder);
            var scene=EditorSceneManager.NewPreviewScene();
            var host=new GameObject("IsolatedRenSession");SceneManager.MoveGameObjectToScene(host,scene);
            var session=host.AddComponent<WorldMacroPlaytestSession>();session.Actors=Array.Empty<PrologueEncounter>();
            var player=new GameObject("IsolatedRenPlayer");SceneManager.MoveGameObjectToScene(player,scene);
            var vitals=player.AddComponent<PlayerVitals>();
            var content=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();
            var campaign=ScriptableObject.CreateInstance<DemoCampaignProfile>();
            try
            {
                session.Content=content;content.Campaign=campaign;
                content.Checkpoints=new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id="ren_test_rest",Label="검증 쉼터",Feet=new Vector3(1,2,3),Yaw=25}};
                content.Points=new[]{new PrologueContentSO.Point{Id="ren_test_rest",Kind=PrologueInteractionKind.Rest,Radius=3}};
                var source=WorldMacroProgress.CreateNew("ren-test",new Vector3(1,2,3),25);
                source.campaign.CampaignId=campaign.CampaignId;source.ledger.currency=43;source.ledger.dropCurrency=17;
                source.ledger.completed.Add("prior-evidence");source.ui.LearnSpellLetter("국");
                source.ledger.hp=.8f;source.ledger.ink=.6f;source.defeated.Add("prior-boss");
                Field(session,"ready",true);Field(session,"vitals",vitals);Field(session,"lastSafe",new Vector3(4,5,6));
                SetProgress(session,source);
                string path=Path.Combine(folder,"progress.json");
                var store=new AtomicJsonStore<WorldMacroProgress>(path,WorldMacroProgress.Valid);Field(session,"store",store);
                Call(session,"BindDemoRen");Call(session,"BindDemoRen");
                int deaths=0,damages=0;vitals.Died+=()=>deaths++;vitals.Damaged+=_=>damages++;
                vitals.Restore();vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&deaths==1&&!source.renUsed&&!File.Exists(path),"locked virtue cannot prevent death or create a consume save");

                source.ui.LearnVirtue("仁");vitals.Restore();vitals.TakeDamage(5);
                check(Near(vitals.Hp01,.95f)&&!source.renUsed&&!File.Exists(path),"nonlethal hit keeps original damage and does not consume Ren");
                Field(session,"saveBlockedByInvalidLoad",true);vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&!source.renUsed&&!File.Exists(path),"invalid-load save block cannot grant survival");
                Field(session,"saveBlockedByInvalidLoad",false);

                string blocker=Path.Combine(folder,"not_a_directory");File.WriteAllText(blocker,"intentional write blocker");
                Field(session,"store",new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,"progress.json"),WorldMacroProgress.Valid));
                string before=JsonUtility.ToJson(source);vitals.Restore();int deathsBefore=deaths;
                vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&deaths==deathsBefore+1&&!string.IsNullOrEmpty(session.SaveError),"real filesystem failure denies survival and reports save error");
                check(ReferenceEquals(session.Progress,source)&&JsonUtility.ToJson(source)==before,"failed consume publishes no progress or HP benefit");

                Field(session,"store",store);vitals.SetMaxHpMultiplier(1.2f);vitals.Restore(.5f);
                bool inspectHit=true,durableBeforeHpEvent=false;
                Action inspect=()=>
                {
                    if(!inspectHit)return;
                    var onDisk=store.Load();
                    durableBeforeHpEvent=onDisk!=null&&onDisk.renUsed&&Near(onDisk.ledger.hp,1f/vitals.MaxHp);
                };
                vitals.HpChanged+=inspect;deathsBefore=deaths;int damagesBefore=damages;
                vitals.TakeDamage(vitals.MaxHp);inspectHit=false;vitals.HpChanged-=inspect;
                check(Near(vitals.Hp01*vitals.MaxHp,1)&&deaths==deathsBefore&&damages==damagesBefore+1,"lethal hit survives at exactly one physical HP with one damage event and no death");
                check(durableBeforeHpEvent&&session.Progress.renUsed&&!session.RenAvailable&&session.SaveError==null,"used flag and post-hit HP are durable before HP event, independent of old HP");
                var disk=store.Load();
                check(disk.ledger.currency==43&&disk.ledger.dropCurrency==17&&disk.ledger.completed.Contains("prior-evidence")&&disk.ui.knownSpellLetters.Contains("국")&&disk.ui.knownVirtues.Contains("仁")&&disk.defeated.Contains("prior-boss"),"consume transaction preserves prior rewards, drop, spells and boss state");
                check(disk.ledger.position==new Vector3(4,5,6)&&Near(disk.ledger.ink,.6f),"consume saves safe position and preserves unrelated resource state");

                vitals.TakeDamage(1);check(vitals.Hp01==0&&deaths==deathsBefore+1,"second lethal hit cannot reuse the charge");
                vitals.Restore();vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&session.Progress.renUsed,"death-style Restore does not recharge Ren");
                SetProgress(session,WorldMacroProgress.MigrateToCurrent(store.Load()));vitals.Restore(session.Progress.ledger.hp);
                vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&session.Progress.renUsed,"reloaded used flag still prevents repeat survival");

                var spent=session.Progress;before=JsonUtility.ToJson(spent);
                check(WorldMacroCheckpointRules.TryProposeRest(content,spent,"ren_test_rest",new Vector3(1,2,3),out var rest,out _),"real checkpoint rest creates recharge proposal");
                check(!rest.renUsed&&rest.ledger.hp==1&&spent.renUsed&&JsonUtility.ToJson(spent)==before,"recharge is detached until persistence accepts it");
                var badStore=new AtomicJsonStore<WorldMacroProgress>(Path.Combine(blocker,"rest.json"),WorldMacroProgress.Valid);
                check(!WorldMacroCheckpointRules.TryCommitRest(rest,badStore.Save,out var rejected,out string error)&&rejected==null&&!string.IsNullOrEmpty(error),"actual failed rest write publishes no accepted recharge");
                vitals.Restore();vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&session.Progress.renUsed&&store.Load().renUsed,"failed rest plus Restore cannot manufacture a second charge");
                check(WorldMacroCheckpointRules.TryCommitRest(rest,store.Save,out var accepted,out _),"successful rest persists recharge");
                SetProgress(session,accepted);vitals.Restore();
                check(session.RenAvailable&&!store.Load().renUsed,"durable accepted rest rearms the virtue");
                vitals.TakeDamage(vitals.MaxHp);
                check(Near(vitals.Hp01*vitals.MaxHp,1)&&store.Load().renUsed,"recharged lethal hit consumes exactly one new durable use");

                var fresh=WorldMacroProgress.CreateNew("fall-test",Vector3.zero,0);fresh.campaign.CampaignId=campaign.CampaignId;fresh.ui.LearnVirtue("仁");
                SetProgress(session,fresh);vitals.Restore();vitals.ApplyFatalFall();
                check(vitals.Hp01==0&&!fresh.renUsed,"fatal fall retains existing death behavior and does not consume Ren");
                Func<float,bool> foreign=_=>false;bool refused=false;
                try{vitals.BindLethalDamageGuard(foreign);}catch(InvalidOperationException){refused=true;}
                check(refused,"a second guard owner cannot overwrite session binding");
                vitals.UnbindLethalDamageGuard(foreign);vitals.Restore();vitals.TakeDamage(vitals.MaxHp);
                check(Near(vitals.Hp01*vitals.MaxHp,1),"foreign cleanup cannot remove the session-owned guard");
                Call(session,"UnbindDemoRen");fresh.renUsed=false;SetProgress(session,fresh);vitals.Restore();vitals.TakeDamage(vitals.MaxHp);
                check(vitals.Hp01==0&&!fresh.renUsed,"owner unbinding restores ordinary lethal behavior");

                var legacy=JsonUtility.FromJson<WorldMacroProgress>(before);legacy.version=4;legacy.renUsed=false;
                // Match a real v4 file: this field did not exist in its JSON.
                string legacyJson=JsonUtility.ToJson(legacy).Replace("\"renUsed\":false,","");
                check(!legacyJson.Contains("renUsed"),"legacy fixture omits the new consumption field");
                var migrated=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(legacyJson));
                check(migrated!=null&&migrated.version==WorldMacroProgress.CurrentVersion&&!migrated.renUsed&&migrated.ui.knownVirtues.Contains("仁"),"v4 save migrates to current schema with acquired Ren initially available");
                check(migrated.ledger.currency==43&&migrated.ledger.dropCurrency==17&&migrated.defeated.Contains("prior-boss")&&migrated.ui.knownSpellLetters.Contains("국")&&migrated.ledger.completed.Contains("prior-evidence"),"v4 migration preserves inventory, drop, evidence and boss tombstones");
                return JsonUtility.ToJson(result,true);
            }
            finally
            {
                Field(session,"ready",false);
                EditorSceneManager.ClosePreviewScene(scene);UnityEngine.Object.DestroyImmediate(content);UnityEngine.Object.DestroyImmediate(campaign);
                string parent=Path.GetDirectoryName(folder).TrimEnd(Path.DirectorySeparatorChar);
                if(string.Equals(parent,Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(folder).StartsWith("OheangbuRenChecks_",StringComparison.Ordinal))
                    Directory.Delete(folder,true);
            }
        }
    }
}
