using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Combat;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroActAuthoring
    {
        static string Checks()
        {
            var report=new Report{status="PASS",scope="Edit-mode production progression/transaction proposal/geometry API tests. Native movement, actual scene water depth and full traversal are separate, unverified here."};
            Action<bool,string> check=(ok,name)=>{(ok?report.checks:report.failures).Add(name);};
            var profile=Object.Instantiate(Session.Content.Campaign);GameObject queryGo=null;
            try
            {
                check(profile.IsValid,"Campaign schema valid");
                var actualWater=Session.Traversal;
                check(actualWater!=null&&actualWater.Water.Length>0,"Actual water geometry is assigned");
                int covered=0;for(int i=0;i<actualWater.Water.Length;i+=Math.Max(1,actualWater.Water.Length/100))
                {var t=actualWater.Water[i];if(actualWater.TryWaterHeight((t.A+t.B+t.C)/3,out _))covered++;}
                check(covered>=100,"Actual serialized water centroids remain queryable after domain reload");
                check(profile.Stages.Length==16,"16 existing stage IDs preserved");
                check(profile.Acts.Length==9&&profile.Acts.Count(a=>a.Reserved)==6,"Acts 4–9 reserved only");
                var checkpoint=Session.Content.Checkpoints.Single(c=>c.Id=="road_rest_2");
                var restPoint=Session.Content.Points.Single(c=>c.Id=="road_rest_2");
                var shopProgress=WorldMacroProgress.CreateNew("shop",Vector3.zero,0);shopProgress.campaign.CampaignId=profile.CampaignId;
                check(checkpoint.Shop&&checkpoint.ShopRequiredStageId=="checkpoint_two","Second inspection rest has explicit shop prerequisite");
                check(WorldMacroCheckpointRules.FindShop(Session.Content,shopProgress,restPoint.Position)==null,"Second inspection shop unavailable before inspection");
                shopProgress.campaign.Completed.Add("checkpoint_two");
                check(WorldMacroCheckpointRules.FindShop(Session.Content,shopProgress,restPoint.Position)?.Id=="road_rest_2","Second inspection shop unlocks using existing rest point");
                foreach(var stage in profile.Stages)stage.Implemented=true; // Test fixture, never the authored activation flags.
                var state=new DemoCampaignState{CampaignId=profile.CampaignId};int money=0;
                foreach(var expected in profile.Stages.Where(s=>!s.Optional))
                {
                    foreach(var id in expected.RequiredDefeatedIds)if(!state.EncounterEvidence.Contains(id))state.EncounterEvidence.Add(id);
                    check(DemoCampaignProgression.CurrentRequired(profile,state)?.Id==expected.Id,"Required order "+expected.Id);
                    bool accepted=DemoCampaignProgression.TryAdvance(profile,state,expected.Event,expected.TriggerId,out var next,out int reward,new List<string>());
                    check(accepted,"Advance with persisted proof "+expected.Id);
                    if(!accepted)break;
                    money+=reward;state=next;
                }
                check(money==840&&DemoCampaignProgression.CurrentRequired(profile,state)==null,"Complete all mandatory stages without optional Guk: 840");
                var optional=profile.Stages.Single(s=>s.Id=="guk_return");
                check(DemoCampaignProgression.TryCompleteOptional(profile,state,optional.Event,optional.TriggerId,out var withOptional,out int optionalMoney)&&optionalMoney==80,"Late Guk optional reward after ending: 80");
                check(!DemoCampaignProgression.TryCompleteOptional(profile,withOptional,optional.Event,optional.TriggerId,out _,out _),"Optional duplicate rejected");
                check(!DemoCampaignProgression.TryCompleteOptional(profile,new DemoCampaignState{CampaignId=profile.CampaignId},optional.Event,optional.TriggerId,out _,out _),"Early Guk rejected before dragon");
                check(DemoCampaignProgression.CurrentAct(profile,null)==null,"Null progress has no act");
                check(profile.Stages.Where(s=>s.Id=="checkpoint_one"||s.Id=="checkpoint_two"||s.Id=="delivery").Sum(s=>s.TongboReward)==340,"Escort remains 60+60+220=340");
                var legacy=WorldMacroProgress.CreateNew("legacy",Vector3.zero,0);legacy.version=6;
                legacy.campaign.CampaignId=profile.CampaignId;legacy.campaign.Completed.Add("guk_return");legacy.ledger.currency=347;
                legacy.defeated.Add("demo_logging_01");legacy.renUsed=true;
                var migrated=WorldMacroProgress.MigrateToCurrent(legacy);
                check(migrated.version==7&&migrated.ledger.currency==347&&migrated.renUsed&&migrated.ledger.completed.Contains("guk_high_reward"),"v6 migration retains wallet/Ren and paid Guk tombstone; no difference payment");
                check(migrated.campaign.EncounterEvidence.Contains("demo_logging_01"),"v6 defeated evidence migration");
                migrated.defeated.Clear();check(migrated.campaign.EncounterEvidence.Count==1,"Encounter proof survives respawn list reset");
                var copy=migrated.campaign.Copy();copy.EncounterEvidence.Add("isolated");check(!migrated.campaign.EncounterEvidence.Contains("isolated"),"Detached campaign collections");
                var late=WorldMacroProgress.CreateNew("test",Vector3.zero,0);late.campaign=state;late.ledger.completed.Add("demo_unlock_coda_giyeok");late.ledger.currency=840;
                var method=typeof(WorldMacroPlaytestSession).GetMethod("TryPrepareGukRevisit",BindingFlags.Static|BindingFlags.NonPublic);
                object[] args={late,profile,null,null};bool prepared=(bool)method.Invoke(null,args);
                check(prepared&&((WorldMacroProgress)args[2]).ledger.currency==920&&late.ledger.currency==840&&!late.ledger.completed.Contains("guk_high_reward"),"Actual late Guk proposal adds reward without mutating live source");
                bool saved=WorldMacroCheckpointRules.TryCommitRest((WorldMacroProgress)args[2],_=>throw new System.IO.IOException("injected"),out var rejected,out _);
                check(!saved&&rejected==null&&late.ledger.currency==840,"Failed atomic write accepts no reward proposal");
                object[] retry={late,profile,null,null};check((bool)method.Invoke(null,retry)&&((WorldMacroProgress)retry[2]).ledger.currency==920,"Retry yields same single payment");
                var beforeOptional=state.Copy();beforeOptional.Completed.Remove("escort");beforeOptional.Completed.Remove("checkpoint_one");beforeOptional.Completed.Remove("checkpoint_two");beforeOptional.Completed.Remove("delivery");beforeOptional.Completed.Remove("south_gate");beforeOptional.Completed.Remove("ending");
                check(DemoCampaignProgression.RequiredPrefixComplete(profile,beforeOptional,"escort"),"Escort availability skips optional Guk");
                queryGo=new GameObject("ActsTerrain_TestFixture");queryGo.hideFlags=HideFlags.HideAndDontSave;
                var query=queryGo.AddComponent<WorldTerrainQuery>();query.Rules=Session.Traversal.Rules;
                query.Water=new[]{new WorldTerrainQuery.WaterTriangle{A=new Vector3(-10,2,-10),B=new Vector3(10,2,-10),C=new Vector3(-10,2,10)}};query.Reindex();
                check(query.TryWaterHeight(new Vector3(-2,5,-2),out float height)&&Mathf.Abs(height-2)<.0001f,"Actual triangle XZ contains water independent of query height");
                check(!query.TryWaterHeight(new Vector3(15,0,15),out _),"Outside actual water mesh excluded");
                check(!query.IsDeep(new Vector3(-2,3,-2))&&query.IsDry(new Vector3(-2,3,-2)),"Bridge deck above water is dry/safe");
                check(!query.IsDeep(new Vector3(-2,2.1f,-2)),"Jump above water is not drowning");
                check(!query.IsDeep(new Vector3(-2,1.451f,-2))&&query.IsDeep(new Vector3(-2,1.449f,-2)),"0.55 m actual foot immersion boundary");
                check(Mathf.Abs(query.MovementScale(new Vector3(-2,1.45f,-2))-.65f)<.0001f,"Wading slowdown maximum 35 percent");
                var collider=queryGo.AddComponent<BoxCollider>();check(query.IsPermanentDrySupport(new Vector3(-2,3,-2),collider),"Permanent dry support accepted");
                queryGo.AddComponent<WorldTemporarySupport>();check(!query.IsPermanentDrySupport(new Vector3(-2,3,-2),collider),"Temporary Guk deck excluded from permanent safe anchor");
                var vitals=queryGo.AddComponent<PlayerVitals>();vitals.Restore();int guarded=0,deaths=0;vitals.BindLethalDamageGuard(_=>{guarded++;return true;});vitals.Died+=()=>deaths++;
                vitals.ApplyEnvironmentalDeath(EnvironmentDeathCause.DeepWater);vitals.ApplyEnvironmentalDeath(EnvironmentDeathCause.DeepWater);
                check(vitals.Hp01==0&&guarded==0&&deaths==1&&vitals.LastEnvironmentDeath==EnvironmentDeathCause.DeepWater,"Drowning bypasses Ren and emits one death");
                vitals.Restore();vitals.ApplyFatalFall();check(deaths==2&&guarded==0&&vitals.LastEnvironmentDeath==EnvironmentDeathCause.Fall,"Legacy fatal fall API preserved");
            }
            catch(Exception e){report.failures.Add(e.ToString());}
            finally{Object.DestroyImmediate(profile);if(queryGo!=null)Object.DestroyImmediate(queryGo);}
            if(report.failures.Count>0)report.status="FAIL";
            return Write("technical_checks.json",report);
        }
    }
}
