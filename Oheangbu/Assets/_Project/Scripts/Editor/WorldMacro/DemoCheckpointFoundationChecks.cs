using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Edit-mode data/transaction tests. No scene, live player, or production save changes.</summary>
    public static class DemoCheckpointFoundationChecks
    {
        [Serializable] sealed class Result
        {
            public string status="PASS",scope="Edit-mode checkpoint/rest proposal tests; live navigation, healing and death replay require Play verification.";
            public List<string> checks=new List<string>();
        }
        public static string Run()
        {
            var report=new Result();
            Action<bool,string> check=(ok,name)=>{if(!ok)throw new InvalidOperationException(name);report.checks.Add(name);};
            var content=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();
            var campaign=ScriptableObject.CreateInstance<DemoCampaignProfile>();
            try
            {
                content.StartFeet=new Vector3(1,2,3);content.StartYaw=37;content.InnCheckpointFeet=new Vector3(4,5,6);
                var state=WorldMacroProgress.CreateNew("test",content.StartFeet,content.StartYaw);
                check(WorldMacroCheckpointRules.TryResolve(content,state,"mine_start",out var mine)&&mine.Feet==content.StartFeet&&mine.Yaw==37,"legacy mine position/yaw unchanged");
                check(WorldMacroCheckpointRules.TryResolve(content,state,"geumpyo_inn",out var inn)&&inn.Feet==content.InnCheckpointFeet&&inn.Label=="금표 주막"&&inn.Yaw==37,"legacy inn unchanged without checkpoint array");
                state.ledger.checkpoint="village_office";state.ledger.checkpointPosition=new Vector3(7,8,9);state.ledger.yaw=81;
                check(WorldMacroCheckpointRules.TryResolve(content,state,"village_office",out var office)&&office.Feet==state.ledger.checkpointPosition&&office.Yaw==81,"legacy office saved position/yaw retained without optional opening");
                check(!WorldMacroCheckpointRules.TryResolve(content,state,"unknown_rest",out _),"unknown checkpoint rejected");
                content.Checkpoints=new WorldMacroPlaytestSO.CheckpointSpec[6];
                string[] ids={"geumpyo_inn","relay_rest","logging_rest","deep_rest","road_rest","south_gate_rest"};
                for(int i=0;i<ids.Length;i++)content.Checkpoints[i]=new WorldMacroPlaytestSO.CheckpointSpec{Id=ids[i],Label="쉼터 "+i,Feet=new Vector3(i*10,5,i*20),Yaw=15+i*25,Shop=i%2==0};
                for(int i=0;i<ids.Length;i++)
                    check(WorldMacroCheckpointRules.TryResolve(content,state,ids[i],out var cp)&&cp.Feet==content.Checkpoints[i].Feet&&cp.Yaw==content.Checkpoints[i].Yaw&&WorldMacroCheckpointRules.Label(content,state,ids[i])=="쉼터 "+i,"authored checkpoint resolves "+ids[i]);
                content.Points=new[]{
                    new PrologueContentSO.Point{Id="geumpyo_inn",Kind=PrologueInteractionKind.Rest,Position=new Vector3(100,5,100),Radius=3},
                    new PrologueContentSO.Point{Id="relay_rest",Kind=PrologueInteractionKind.Rest,Position=new Vector3(120,5,100),Radius=3},
                    new PrologueContentSO.Point{Id="logging_rest",Kind=PrologueInteractionKind.Rest,Position=new Vector3(140,5,100),Radius=3}};
                campaign.Stages=new[]{
                    new DemoCampaignProfile.Stage{Id="inn_rest",TriggerId="geumpyo_inn",Event=DemoEventKind.Rest,Objective="주막에서 쉰다",Implemented=true,TongboReward=4},
                    new DemoCampaignProfile.Stage{Id="logging_rest_stage",TriggerId="logging_rest",Event=DemoEventKind.Rest,Objective="벌목장에서 쉰다",Implemented=true,TongboReward=7}};
                content.Campaign=campaign;state.campaign.CampaignId=campaign.CampaignId;
                state.ledger.currency=60;state.ledger.hp=.25f;state.ledger.ink=.1f;
                state.ledger.dropCurrency=19;state.ledger.dropPosition=new Vector3(8,9,10);
                state.ledger.completed.Add("evidence_done");state.defeated.Add("normal");state.defeated.Add("boss");
                content.Encounters=new[]{new WorldMacroPlaytestSO.Encounter{Id="normal",RespawnOnRest=true},new WorldMacroPlaytestSO.Encounter{Id="boss",RespawnOnRest=false}};
                check(WorldMacroCheckpointRules.FindShop(content,state,content.Points[0].Position)==content.Points[0],"shop uses actual Rest interaction position, not respawn feet");
                check(WorldMacroCheckpointRules.FindShop(content,state,content.Checkpoints[0].Feet)==null,"checkpoint location alone cannot grant remote shop access");
                check(WorldMacroCheckpointRules.FindShop(content,state,content.Points[1].Position)==null,"Shop=false shelter cannot sell upgrades");
                content.Campaign=null;
                check(WorldMacroCheckpointRules.FindShop(content,state,content.Points[0].Position)==null,"legacy playtest never gains a demo shop");
                content.Campaign=campaign;
                string before=JsonUtility.ToJson(state);
                check(WorldMacroCheckpointRules.TryProposeRest(content,state,"logging_rest",new Vector3(20,5.05f,40),out var early,out _),"future shelter remains physically usable");
                check(early.ledger.checkpoint=="logging_rest"&&early.campaign.Completed.Count==0&&early.ledger.currency==60,"out-of-order rest cannot skip stage or grant reward");
                check(JsonUtility.ToJson(state)==before,"proposal leaves live checkpoint/resources/campaign unchanged");
                check(!WorldMacroCheckpointRules.TryCommitRest(early,_=>throw new IOException("disk unavailable"),out var failed,out var failure)&&failed==null&&!string.IsNullOrEmpty(failure),"failed persistence publishes no accepted rest");
                check(JsonUtility.ToJson(state)==before&&state.ledger.hp==.25f&&state.defeated.Contains("normal"),"failed rest leaves live healing and defeated state untouched");
                WorldMacroProgress disk=null;
                check(WorldMacroCheckpointRules.TryProposeRest(content,state,"geumpyo_inn",new Vector3(0,5.05f,0),out var proposal,out _),"current rest proposes authored ID");
                check(WorldMacroCheckpointRules.TryCommitRest(proposal,p=>disk=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(p)),out var accepted,out _),"successful persistence accepts exact proposal");
                check(disk!=null&&disk.ledger.checkpoint=="geumpyo_inn"&&disk.ledger.currency==64&&disk.campaign.Completed.Contains("inn_rest"),"serialized rest retains checkpoint/campaign/reward together");
                check(disk.ledger.hp==1&&disk.ledger.ink==1&&!disk.defeated.Contains("normal")&&disk.defeated.Contains("boss"),"saved recovery and normal enemy reset preserve boss defeat");
                check(disk.ledger.dropCurrency==19&&disk.ledger.dropPosition==state.ledger.dropPosition&&disk.ledger.completed.Contains("evidence_done"),"rest preserves unrecovered drop and prior evidence");
                check(WorldMacroCheckpointRules.TryProposeRest(content,accepted,"geumpyo_inn",proposal.ledger.checkpointPosition,out var repeat,out _)&&repeat.ledger.currency==64&&repeat.campaign.Completed.Count==1,"repeated rest does not repeat reward");
                check(WorldMacroCheckpointRules.TryProposeRest(content,accepted,"logging_rest",new Vector3(20,5.05f,40),out var second,out _)&&second.ledger.checkpoint=="logging_rest"&&second.ledger.currency==71&&second.campaign.Completed.Count==2,"second shelter uses its own ID and matching rest stage");
                check(!WorldMacroCheckpointRules.TryProposeRest(content,state,"missing",Vector3.zero,out _,out _),"unregistered Rest ID cannot silently select inn");
                check(!WorldMacroCheckpointRules.TryProposeRest(content,state,"geumpyo_inn",new Vector3(float.NaN,0,0),out _,out _),"non-finite respawn feet rejected");
                state.version=2;state.campaign=null;state.economy=null;
                var old=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(state)));
                check(old!=null&&old.ledger.currency==60&&old.ledger.dropCurrency==19&&old.ledger.completed.Contains("evidence_done"),"v2 migration preserves resources and evidence");
                check(WorldMacroCheckpointRules.TryResolve(content,old,"village_office",out office)&&office.Feet==state.ledger.checkpointPosition,"old office restore survives new optional checkpoint data");
                content.Checkpoints=new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id="geumpyo_inn",Feet=new Vector3(float.NaN,0,0)}};
                check(!WorldMacroCheckpointRules.TryResolve(content,old,"geumpyo_inn",out _),"broken explicit legacy override does not silently fallback");
                content.Checkpoints=new[]{new WorldMacroPlaytestSO.CheckpointSpec{Id="logging_rest"},new WorldMacroPlaytestSO.CheckpointSpec{Id="logging_rest"}};
                check(!WorldMacroCheckpointRules.TryResolve(content,old,"logging_rest",out _),"duplicate checkpoint IDs rejected");
                return JsonUtility.ToJson(report,true);
            }
            finally{UnityEngine.Object.DestroyImmediate(content);UnityEngine.Object.DestroyImmediate(campaign);}
        }
    }
}
