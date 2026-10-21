using System;
using System.Collections.Generic;
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
        // #307 phase 1 item 2: one cached view per point Id (was one slot, overwritten by every other point each frame). A view is
        // reused only for the same source point, campaign stage and campaign state object (explicit mode also the same Facts count,
        // so an in-place fact still refreshes the testimony text); otherwise it is rebuilt exactly as before.
        sealed class DemoPointView{public PrologueContentSO.Point Source,View;public DemoCampaignProfile.Stage Stage;public DemoCampaignState Facts;public int FactCount;public bool Explicit;}
        readonly Dictionary<string,DemoPointView> demoPointViews=new Dictionary<string,DemoPointView>(StringComparer.Ordinal);
        PrologueContentSO.Point DemoInteractionPoint(PrologueContentSO.Point original)
        {
            if(original==null||!DemoCampaignActive)return original;
            var step=DemoCampaignProgression.ForEvent(Content.Campaign,Progress.campaign,DemoEventKind.Interaction,original.Id,Progress.defeated);
            DemoPointView cached=null;if(original.Id!=null)demoPointViews.TryGetValue(original.Id,out cached);
            if(Content.Campaign.UseExplicitPrerequisites)
            {
                int facts=Progress.campaign.Facts?.Count??-1;
                if(cached!=null&&cached.Explicit&&ReferenceEquals(cached.Source,original)&&ReferenceEquals(cached.Stage,step)&&ReferenceEquals(cached.Facts,Progress.campaign)&&cached.FactCount==facts)return cached.View;
                var text=Content.Campaign.DialogueFor(original.Id,Progress.campaign,step?.Dialogue??original.Text);
                return RememberDemoPointView(original,step,true,facts,new PrologueContentSO.Point{Id=original.Id,Kind=original.Kind,Position=original.Position,Radius=original.Radius,
                    Currency=original.Currency,Prompt=string.IsNullOrEmpty(step?.Prompt)?original.Prompt:step.Prompt,Text=text,
                    RequiredCompleted=original.RequiredCompleted,RequiredDefeated=original.RequiredDefeated,LockedText=original.LockedText,
                    Speaker=original.Speaker,Lines=text==original.Text?original.Lines:Array.Empty<string>(),Services=original.Services});   // #306: authored pages only for the authored text
            }
            if(step==null||!step.Implemented||step.Event!=DemoEventKind.Interaction||step.TriggerId!=original.Id)return original;
            if(cached!=null&&!cached.Explicit&&ReferenceEquals(cached.Source,original)&&ReferenceEquals(cached.Stage,step))return cached.View;
            return RememberDemoPointView(original,step,false,0,new PrologueContentSO.Point{Id=original.Id,Kind=original.Kind,Position=original.Position,Radius=original.Radius,
                Currency=original.Currency,Prompt=string.IsNullOrEmpty(step.Prompt)?original.Prompt:step.Prompt,
                Text=string.IsNullOrEmpty(step.Dialogue)?original.Text:step.Dialogue,
                Speaker=original.Speaker,Lines=string.IsNullOrEmpty(step.Dialogue)?original.Lines:Array.Empty<string>(),Services=original.Services});
        }
        PrologueContentSO.Point RememberDemoPointView(PrologueContentSO.Point original,DemoCampaignProfile.Stage step,bool explicitMode,int facts,PrologueContentSO.Point view)
        {
            if(original.Id==null)return view;
            if(!demoPointViews.TryGetValue(original.Id,out var entry)){entry=new DemoPointView();demoPointViews.Add(original.Id,entry);}
            entry.Source=original;entry.Stage=step;entry.Facts=Progress.campaign;entry.FactCount=facts;entry.Explicit=explicitMode;entry.View=view;
            return view;
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
            string given=GrantStageDocuments308(Progress.campaign,next,proposal.ui);   // D308-117 B2: the commission letter rides the same save
            if(!TryCommitInteraction(proposal,out var error)){Show(error);return true;}
            string receipt=reward>0?"조선통보 +"+reward:null;
            if(given!=null)receipt=receipt==null?given:receipt+"\n"+given;
            if(point.Kind==PrologueInteractionKind.Conversation)Speak306(point,point.Prompt,point.Text,receipt);   // #306
            else Present(point.Prompt,point.Text+(receipt!=null?"\n"+receipt:""));
            InteractionResolved?.Invoke(point.Kind,point.Position);
            success=true;return true;
        }

        /// <summary>D308-117 B2 (SPEC-FIELD-FIRST-HOUR-308): every configured document whose GrantOnStage this step completed goes
        /// into the bundle once. Returns the receipt line ("&lt;제목&gt;을 받았다") or null.</summary>
        string GrantStageDocuments308(DemoCampaignState before,DemoCampaignState after,Oheangbu.App.World.UI.UiProgress ui)
        {
            if(Content.Documents==null||ui==null||after?.Completed==null)return null;
            string receipt=null;
            foreach(var d in Content.Documents)
            {
                if(d==null||!d.IsConfigured||string.IsNullOrEmpty(d.GrantOnStage))continue;
                if(!after.Completed.Contains(d.GrantOnStage)||(before?.Completed?.Contains(d.GrantOnStage)??false)||ui.HasItem(d.Id))continue;
                ui.AddItem(d.Id);
                char last=d.Title[d.Title.Length-1];bool batchim=last>='가'&&last<='힣'&&(last-'가')%28!=0;
                receipt=(receipt==null?"":receipt+"\n")+d.Title+(batchim?"을":"를")+" 받았다";
            }
            return receipt;
        }
    }
}
