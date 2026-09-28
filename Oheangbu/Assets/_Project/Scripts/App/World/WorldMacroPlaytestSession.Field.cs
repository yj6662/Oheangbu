using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.App.Prologue;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using System;
using System.Linq;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public Vfx120Profile DemoWoodLiftProfile;
        FieldSpellService demoField;
        DemoGukRevisitSite gukSite, provenGukSite;
        DemoGukRevisitSite[] gukSites=Array.Empty<DemoGukRevisitSite>();
        public FieldSpellService DemoField => demoField;

        void BindDemoField()
        {
            if (!DemoCampaignActive || DemoWoodLiftProfile == null) return;
            demoField = GetComponent<FieldSpellService>();
            if (demoField == null) demoField = gameObject.AddComponent<FieldSpellService>();
            demoField.Configure(Walker.Body, Walker.Motor, vitals, DemoWoodLiftProfile,
                () => ready && isActiveAndEnabled && HasDemoGuk,
                () => Walker.Seated, () => GameplayInputBlocked);
            demoField.DryPlacementAllowed=p=>Traversal==null||Traversal.IsDry(p);
            demoField.PermanentSupportAllowed=(p,c)=>Traversal==null||Traversal.IsPermanentDrySupport(p,c);
            Walker.Wiring.FieldSpells = demoField;
            var sites=FindObjectsByType<DemoGukRevisitSite>(FindObjectsSortMode.None)
                .Where(s=>s.gameObject.scene==gameObject.scene).ToArray();
            gukSites=sites;
            demoField.CombatBlocked=()=>MountainLayout!=null&&Actors.Any(a=>a!=null&&a.isActiveAndEnabled&&a.Current==Oheangbu.App.Prologue.PrologueEncounter.Behaviour.Chase);
            demoField.StateChanged-=OnDemoFieldState;demoField.StateChanged+=OnDemoFieldState;
            vitals.Died-=ClearGukProof;vitals.Died+=ClearGukProof;
        }

        void ClearGukProof(){provenGukSite=null;}
        void OnDemoFieldState(FieldLiftState state)
        {
            var nearby=state==FieldLiftState.Holding?gukSites.Where(s=>s!=null&&s.LiftPad!=null&&Vector3.Distance(demoField.BasePoint,s.LiftPad.position)<=1f).ToArray():Array.Empty<DemoGukRevisitSite>();
            gukSite=nearby.Length==1?nearby[0]:null;
            if(state!=FieldLiftState.Holding||!HasDemoGuk||gukSite==null||gukSite.LiftPad==null||
                !demoField.PassengerSupported||demoField.CurrentHeight<2.1f)return;
            if(Vector3.Distance(demoField.BasePoint,gukSite.LiftPad.position)<=1f)provenGukSite=gukSite;
        }

        bool TryHandleGukRevisit(PrologueContentSO.Point point,out bool success)
        {
            success=false;
            var matches=gukSites.Where(s=>s!=null&&s.RewardId==point.Id).ToArray();
            if(matches.Length==0)return false;
            if(matches.Length!=1)return true;
            gukSite=matches[0];
            if(Progress.ledger.completed.Contains(point.Id)){Show("이미 살펴본 보따리다.");success=true;return true;}
            if(!HasDemoGuk||gukSite==null||provenGukSite!=gukSite||gukSite.UpperSurface==null||
                gukSite.gameObject.scene!=gameObject.scene||Walker.Seated||vitals.Hp01<=0||
                Vector3.Distance(point.Position,gukSite.UpperSurface.position)>.3f||
                Mathf.Abs(FieldSpellService.Feet(Walker.Body).y-gukSite.UpperSurface.position.y)>.25f)
            {Show("바위 앞에서 국으로 받침을 올려 보따리에 다가가자.");return true;}
            if(!TryPrepareGukReward(Progress,Content.Campaign,point.Id,out var proposal,out string error)||
                !TryCommitInteraction(proposal,out error)){Show(error??DemoObjective);return true;}
            // One short receipt; the "which way next" directive is dropped — guidance is environmental (#664).
            success=true;Show("높은 바위의 보따리를 챙겼다.");
            InteractionResolved?.Invoke(point.Kind,point.Position);return true;
        }

        static bool TryPrepareGukRevisit(WorldMacroProgress source,DemoCampaignProfile profile,out WorldMacroProgress proposal,out string error)
            =>TryPrepareGukReward(source,profile,DemoGukRevisitSite.Id,out proposal,out error);

        static bool TryPrepareGukReward(WorldMacroProgress source,DemoCampaignProfile profile,string rewardId,out WorldMacroProgress proposal,out string error)
        {
            proposal=null;error=null;
            if(!WorldMacroProgress.Valid(source)||profile==null||!profile.IsValid||
                !source.ledger.completed.Contains(GiyeokUnlockId)||source.ledger.completed.Contains(rewardId))return false;
            var stages=profile.Stages.Where(s=>s.TriggerId==rewardId&&s.Event==DemoEventKind.FieldUsed).ToArray();
            if(stages.Length!=1)return false;
            bool optional=stages[0].Optional;
            DemoCampaignState next;int reward;
            bool accepted=optional
                ?DemoCampaignProgression.TryCompleteOptional(profile,source.campaign,DemoEventKind.FieldUsed,rewardId,out next,out reward)
                :DemoCampaignProgression.TryAdvance(profile,source.campaign,DemoEventKind.FieldUsed,rewardId,out next,out reward,source.defeated);
            if(!accepted)return false;
            if(reward>int.MaxValue-source.ledger.currency){error="통보 보유량을 확인한다.";return false;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.campaign=next;candidate.ledger.completed.Add(rewardId);candidate.ledger.currency+=reward;
            proposal=candidate;return true;
        }

        void SuspendDemoField() { ClearGukProof();if (demoField != null) demoField.enabled = false; }
        void UnbindDemoField()
        {
            if(demoField!=null)demoField.StateChanged-=OnDemoFieldState;
            if(vitals!=null)vitals.Died-=ClearGukProof;
            if(Walker!=null&&Walker.Wiring!=null&&Walker.Wiring.FieldSpells==demoField)Walker.Wiring.FieldSpells=null;
        }
        void OnEnable() { if (demoField != null) demoField.enabled = true; if(MumBridges!=null)MumBridges.enabled=true; if(ready){BindDemoEscort();BindDemoSouthGate();} }
    }
}
