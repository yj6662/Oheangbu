using System;
using Oheangbu.App.Prologue;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public bool DemoCampaignActive=>Content!=null&&Content.Campaign!=null&&
            Progress?.campaign!=null&&Progress.campaign.CampaignId==Content.Campaign.CampaignId;
        public string DemoObjective
        {
            get
            {
                if(!DemoCampaignActive||Content.Campaign.UseExplicitPrerequisites)return null;
                if(DemoCampaignCompleted)return "황경 남문이 열렸다. 이번 여정을 마쳤다.";
                var step=DemoCampaignProgression.Current(Content.Campaign,Progress.campaign);
                if(step==null||!step.Implemented)return Content.Campaign.WorkInProgressText;
                int remaining=0;foreach(var id in step.RequiredDefeatedIds??Array.Empty<string>())if(!Progress.defeated.Contains(id)&&!Progress.campaign.EncounterEvidence.Contains(id))remaining++;
                return step.Objective+(remaining>0?" · 주변 위협 "+remaining+"체":"");
            }
        }
        PrologueContentSO.Point demoPointSource,demoPointView;
        DemoCampaignProfile.Stage demoPointStage;
        DemoCampaignState demoPointFacts;
        PrologueContentSO.Point DemoInteractionPoint(PrologueContentSO.Point original)
        {
            if(original==null||!DemoCampaignActive)return original;
            var step=DemoCampaignProgression.ForEvent(Content.Campaign,Progress.campaign,DemoEventKind.Interaction,original.Id,Progress.defeated);
            if(Content.Campaign.UseExplicitPrerequisites)
            {
                if(ReferenceEquals(demoPointSource,original)&&ReferenceEquals(demoPointStage,step)&&ReferenceEquals(demoPointFacts,Progress.campaign))return demoPointView;
                demoPointSource=original;demoPointStage=step;demoPointFacts=Progress.campaign;
                var text=Content.Campaign.DialogueFor(original.Id,Progress.campaign,step?.Dialogue??original.Text);
                return demoPointView=new PrologueContentSO.Point{Id=original.Id,Kind=original.Kind,Position=original.Position,Radius=original.Radius,
                    Currency=original.Currency,Prompt=string.IsNullOrEmpty(step?.Prompt)?original.Prompt:step.Prompt,Text=text,
                    RequiredCompleted=original.RequiredCompleted,RequiredDefeated=original.RequiredDefeated,LockedText=original.LockedText,
                    Speaker=original.Speaker,Lines=text==original.Text?original.Lines:Array.Empty<string>(),Services=original.Services};   // #306: authored pages only for the authored text
            }
            if(step==null||!step.Implemented||step.Event!=DemoEventKind.Interaction||step.TriggerId!=original.Id)return original;
            if(ReferenceEquals(demoPointSource,original)&&ReferenceEquals(demoPointStage,step))return demoPointView;
            demoPointSource=original;demoPointStage=step;
            return demoPointView=new PrologueContentSO.Point{Id=original.Id,Kind=original.Kind,Position=original.Position,Radius=original.Radius,
                Currency=original.Currency,Prompt=string.IsNullOrEmpty(step.Prompt)?original.Prompt:step.Prompt,
                Text=string.IsNullOrEmpty(step.Dialogue)?original.Text:step.Dialogue,
                Speaker=original.Speaker,Lines=string.IsNullOrEmpty(step.Dialogue)?original.Lines:Array.Empty<string>(),Services=original.Services};
        }

        // Return true when this is a campaign interaction (including blocked out-of-order attempts).
        bool TryHandleDemoInteraction(PrologueContentSO.Point point,out bool success)
        {
            success=false;
            if(!DemoCampaignActive)return false;
            if(TryHandleDemoEscortInteraction(point,out success))return true;
            if(TryRetryGrowthLessonInteraction(point.Id,out success))return true;
            if(TryHandleGukRevisit(point,out success))return true;
            bool owned=Array.Exists(Content.Campaign.Stages,s=>s.Event==DemoEventKind.Interaction&&s.TriggerId==point.Id);
            if(!owned)return false;
            if(!DemoCampaignProgression.TryAdvance(Content.Campaign,Progress.campaign,DemoEventKind.Interaction,point.Id,out var next,out var reward,Progress.defeated))
            {
                // #306: an NPC says it on the dialogue surface (Speak306 falls back to the same Present); a place keeps Present
                bool npc=point.Kind==PrologueInteractionKind.Conversation;
                if(Content.Campaign.UseExplicitPrerequisites){if(npc)Speak306(point,point.Prompt,point.Text);else Present(point.Prompt,point.Text);}
                else if(npc)Speak306(point,"다음 여정",DemoTravelAdvice);else Present("다음 여정",DemoTravelAdvice);
                return true;
            }
            var proposal=WorldMacroProgress.MigrateToCurrent(UnityEngine.JsonUtility.FromJson<WorldMacroProgress>(UnityEngine.JsonUtility.ToJson(Progress)));
            bool first=!proposal.ledger.completed.Contains(point.Id);
            try{proposal.ledger.currency=checked(proposal.ledger.currency+reward+(first?Math.Max(0,point.Currency):0));}
            catch(OverflowException){Show("통보 보유량을 확인한 뒤 다시 시도한다.");return true;}
            if(first)proposal.ledger.completed.Add(point.Id);
            proposal.campaign=next;
            if(!TryCommitInteraction(proposal,out var error)){Show(error);return true;}
            if(point.Kind==PrologueInteractionKind.Conversation)Speak306(point,point.Prompt,point.Text,reward>0?"조선통보 +"+reward:null);   // #306
            else Present(point.Prompt,point.Text+(reward>0?"\n조선통보 +"+reward:""));
            InteractionResolved?.Invoke(point.Kind,point.Position);
            success=true;return true;
        }
    }
}
