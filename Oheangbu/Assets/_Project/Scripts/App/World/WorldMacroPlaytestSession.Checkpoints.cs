using System;
using System.IO;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Checkpoint selection and detached rest proposals. Never changes source progress or live actors.</summary>
    public static class WorldMacroCheckpointRules
    {
        public static bool TryResolve(WorldMacroPlaytestSO content,WorldMacroProgress progress,string id,out WorldMacroPlaytestSO.CheckpointSpec checkpoint)
        {
            checkpoint=null;
            if(content==null||string.IsNullOrWhiteSpace(id))return false;
            var configured=content.Checkpoints??Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>();
            // A broken/duplicated explicit entry must not silently turn into a different legacy checkpoint.
            int found=0;
            foreach(var entry in configured)if(entry!=null&&entry.Id==id){checkpoint=entry;found++;}
            if(found>0)return found==1&&checkpoint.IsConfigured;
            if(!WorldMacroOpeningProgress.KnownCheckpoint(id))return false;
            var lookup=progress??WorldMacroProgress.CreateNew(content.TerrainRevision,content.StartFeet,content.StartYaw);
            var legacy=new WorldMacroProgress{ledger=new Oheangbu.App.Prologue.PrologueProgress{
                checkpoint=id,checkpointPosition=lookup.ledger.checkpointPosition,yaw=lookup.ledger.yaw}};
            checkpoint=new WorldMacroPlaytestSO.CheckpointSpec{Id=id,
                Label=id=="geumpyo_inn"?"금표 주막":id==WorldMacroOpeningProfileSO.CheckpointId?"마을 관청":"폐광",
                Feet=WorldMacroOpeningProgress.CheckpointFeet(content,legacy),Yaw=WorldMacroOpeningProgress.CheckpointYaw(content,legacy),
                Shop=id=="geumpyo_inn"};
            return checkpoint.IsConfigured;
        }
        public static string Label(WorldMacroPlaytestSO content,WorldMacroProgress progress,string id)
        {
            return TryResolve(content,progress,id,out var checkpoint)?string.IsNullOrWhiteSpace(checkpoint.Label)?checkpoint.Id:checkpoint.Label:"폐광";
        }
        public static PrologueContentSO.Point FindShop(WorldMacroPlaytestSO content,WorldMacroProgress progress,Vector3 feet)
        {
            if(content==null||content.Campaign==null||progress?.campaign==null||progress.campaign.CampaignId!=content.Campaign.CampaignId||content.Points==null)return null;
            PrologueContentSO.Point nearest=null;float distance=float.MaxValue;
            foreach(var point in content.Points)
            {
                if(point==null||point.Kind!=PrologueInteractionKind.Rest||point.Radius<=0||!TryResolve(content,progress,point.Id,out var checkpoint)||!checkpoint.Shop)continue;
                if(!string.IsNullOrEmpty(checkpoint.ShopRequiredStageId)&&!progress.campaign.Completed.Contains(checkpoint.ShopRequiredStageId))continue;
                float current=Vector3.Distance(feet,point.Position);
                if(current<=point.Radius&&current<distance){nearest=point;distance=current;}
            }
            return nearest;
        }
        public static bool TryProposeRest(WorldMacroPlaytestSO content,WorldMacroProgress source,string pointId,Vector3 safeFeet,
            out WorldMacroProgress proposal,out string error)
        {
            proposal=null;error=null;
            var point=content?.Points==null?null:Array.Find(content.Points,p=>p!=null&&p.Id==pointId&&p.Kind==PrologueInteractionKind.Rest);
            if(point==null||!TryResolve(content,source,pointId,out _)||!Finite(safeFeet)||!WorldMacroProgress.Valid(source))
            {error="쉼터와 체크포인트 설정을 확인해야 한다.";return false;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.ledger.checkpoint=pointId;candidate.ledger.checkpointPosition=safeFeet;
            candidate.ledger.hp=1;candidate.ledger.ink=1;candidate.renUsed=false;
            if(!candidate.ledger.completed.Contains(pointId))candidate.ledger.completed.Add(pointId);
            if(content.Campaign!=null&&candidate.campaign.CampaignId==content.Campaign.CampaignId&&
                DemoCampaignProgression.TryAdvance(content.Campaign,candidate.campaign,DemoEventKind.Rest,pointId,out var next,out int reward))
            {
                if(reward>int.MaxValue-candidate.ledger.currency){error="통보 보유량을 확인한 뒤 다시 시도한다.";return false;}
                candidate.campaign=next;candidate.ledger.currency+=reward;
            }
            // A future or repeated rest remains a usable shelter, but cannot skip narrative stages or repay rewards.
            foreach(var encounter in content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>())
                if(encounter!=null&&encounter.RespawnOnRest)candidate.defeated.Remove(encounter.Id);
            proposal=candidate;return true;
        }
        public static bool TryCommitRest(WorldMacroProgress proposal,Action<WorldMacroProgress> persist,out WorldMacroProgress accepted,out string error)
        {
            accepted=null;error=null;
            if(!WorldMacroProgress.Valid(proposal)||persist==null){error="유효하지 않은 휴식 저장 요청입니다.";return false;}
            try{persist(proposal);accepted=proposal;return true;}
            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||exception is ArgumentException)
            {error="휴식 저장 실패: "+exception.Message;return false;}
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    }

    public sealed partial class WorldMacroPlaytestSession
    {
        bool Rest(PrologueContentSO.Point point)
        {
            if(!ready||SaveBlocked){Show(SaveError??"저장 세션을 준비하는 중이다.");return false;}
            FlushPendingDefeats(true);
            if(HasPendingDefeats){Show(SaveError??"처치 결과를 저장하는 중이다.");return false;}
            if(!WorldMacroCheckpointRules.TryResolve(Content,Progress,point.Id,out var checkpoint)||!TrySafeFeet(checkpoint.Feet,out var feet))
            {Show("이 쉼터의 안전한 재시작 위치를 확인할 수 없다.");return false;}
            if(!WorldMacroCheckpointRules.TryProposeRest(Content,Progress,point.Id,feet,out var proposal,out string error))
            {Show(error);return false;}
            proposal.ledger.position=lastSafe;proposal.ledger.hasPosition=true;proposal.ledger.yaw=Walker.Body.transform.eulerAngles.y;
            var restPresentation=VillageRestPresentation!=null&&VillageRestPresentation.PointId==point.Id?VillageRestPresentation:InnRestPresentation;
            bool doorway=restPresentation!=null&&restPresentation.CanPresent(point.Id);
            if(doorway){proposal.ledger.position=feet;proposal.ledger.yaw=restPresentation.ReturnYaw;}
            IncludeDemoEscortCheckpointInRest(proposal,point.Id,feet);
            // Save the fully recovered proposal before any observable checkpoint, campaign, reward, or actor mutation.
            DrainAutosave();   // #307 item 3: an autosave in flight lands and reports first
            if(!WorldMacroCheckpointRules.TryCommitRest(proposal,store.Save,out var accepted,out error))
            {SaveError=error;Show(error);return false;}
            Progress=accepted;lastSuccessfulSnapshot=JsonUtility.ToJson(accepted);SaveError=null;
            MumBridges?.ResetForWorldBoundary();
            ResetCombat();vitals.Restore();ink.Restore();
            if(doorway)restPresentation.BeginCommittedRest(feet,proposal.ledger.yaw);
            if(!IconPresentation)Show(CheckpointDisplayName+"에서 숨을 고른다.\n체력과 먹을 회복했다.");
            InteractionResolved?.Invoke(PrologueInteractionKind.Rest,point.Position);return true;
        }
    }
}
