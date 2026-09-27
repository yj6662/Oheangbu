using System;
using System.Linq;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public CompactWorldLayoutSO MountainLayout;
        bool TryHandleMountainInteraction(PrologueContentSO.Point point,out bool success)
        {
            success=false;if(point==null||MountainLayout==null)return false;
            var action=MountainLayout.Mountains.SelectMany(m=>m.Actions).FirstOrDefault(a=>a.Id==point.Id);
            if(action==null)return false;
            if(Progress.ledger.completed.Contains(action.Id)){Present(action.Title,action.Text);success=true;return true;}
            if(!CompactMountainProgression.TryPrepare(Progress,action,out var proposal,out var error)||!TryCommitInteraction(proposal,out error))
            {Show(error);return true;}
            success=true;Present(action.Title,action.Text);InteractionResolved?.Invoke(point.Kind,point.Position);return true;
        }
        bool MountainEncounterAvailable(string actorId)
        {
            if(MountainLayout==null)return true;
            var mountain=Array.Find(MountainLayout.Mountains,m=>m.BossId==actorId);
            return mountain==null||string.IsNullOrEmpty(mountain.RequiredStageId)||Progress.campaign.Completed.Contains(mountain.RequiredStageId);
        }
        bool TryPrepareMountainDefeat(string id,out WorldMacroProgress proposal,out string error,out bool owned)
        {
            proposal=null;error=null;
            var mountain=MountainLayout!=null?Array.Find(MountainLayout.Mountains,m=>m.BossId==id):null;
            owned=mountain!=null;if(!owned)return false;
            if(!MountainEncounterAvailable(id)||!DemoCampaignActive||Progress.defeated.Contains(id))return false;
            if(!DemoCampaignProgression.TryAdvance(Content.Campaign,Progress.campaign,DemoEventKind.BossDefeated,id,out var next,out int reward,Progress.defeated))return false;
            if(reward<0||reward>int.MaxValue-Progress.ledger.currency){error="통보 보유량을 확인한다.";return false;}
            var copy=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            copy.campaign=next;copy.defeated.Add(id);copy.ledger.currency+=reward;
            if(!copy.campaign.EncounterEvidence.Contains(id))copy.campaign.EncounterEvidence.Add(id);
            if(!copy.campaign.Facts.Contains("defeated:"+id))copy.campaign.Facts.Add("defeated:"+id);
            if(!string.IsNullOrEmpty(mountain.UnlockFact)&&!copy.ledger.completed.Contains(mountain.UnlockFact))copy.ledger.completed.Add(mountain.UnlockFact);
            if(!string.IsNullOrEmpty(mountain.UnlockSpell))copy.ui.LearnSpellLetter(mountain.UnlockSpell);
            if(!string.IsNullOrEmpty(mountain.UnlockVirtue))copy.ui.LearnVirtue(mountain.UnlockVirtue);
            proposal=copy;return true;
        }
    }
}
