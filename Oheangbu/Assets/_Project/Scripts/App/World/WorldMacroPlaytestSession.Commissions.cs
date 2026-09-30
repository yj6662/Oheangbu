using System;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Side commissions (의뢰, SPEC-ROADSIDE-COMMISSIONS-303). The giver's first talk accepts, later talks wait until the
    // evidence is in hand or the target's defeat is on record, the report pays once, then the completed line repeats.
    // No HUD, banner or map marker: the giver's words are the only guidance (one text surface per interaction).
    // #306: with a dialogue surface bound the talk menu decides instead (맡는다 / 거절한다; a satisfied talk reports, InteractCommission306);
    // the path below stays as is for the old UI.
    public sealed partial class WorldMacroPlaytestSession
    {
        static string AcceptedKey(WorldMacroPlaytestSO.CommissionSpec q)=>q.Id+":accepted";
        static string ReportedKey(WorldMacroPlaytestSO.CommissionSpec q)=>q.Id+":reported";

        WorldMacroPlaytestSO.CommissionSpec CommissionForGiver(string id)=>
            Content.Commissions==null?null:Array.Find(Content.Commissions,q=>q!=null&&q.IsConfigured&&q.GiverId==id);
        WorldMacroPlaytestSO.CommissionSpec CommissionForEvidence(string id)=>
            Content.Commissions==null?null:Array.Find(Content.Commissions,q=>q!=null&&q.IsConfigured&&q.EvidenceId==id);

        public bool CommissionAccepted(string commissionId)=>Progress!=null&&Progress.ledger.completed.Contains(commissionId+":accepted");
        public bool CommissionReported(string commissionId)=>Progress!=null&&Progress.ledger.completed.Contains(commissionId+":reported");

        bool CommissionSatisfied(WorldMacroPlaytestSO.CommissionSpec q)
        {
            bool evidence=string.IsNullOrWhiteSpace(q.EvidenceId)||Progress.ledger.completed.Contains(q.EvidenceId);
            bool target=string.IsNullOrWhiteSpace(q.TargetEncounterId)||Progress.campaign.EncounterEvidence.Contains(q.TargetEncounterId)||Progress.defeated.Contains(q.TargetEncounterId);
            return evidence&&target;
        }

        // Called from Interact before the generic point path. True when the point belongs to a commission (handled = result).
        bool TryHandleCommission(PrologueContentSO.Point point,out bool handled)
        {
            handled=false;
            if(point==null||Content.Commissions==null||Content.Commissions.Length==0)return false;
            var giver=CommissionForGiver(point.Id);
            if(giver!=null){handled=InteractCommission(giver,point);return true;}
            var gated=CommissionForEvidence(point.Id);
            if(gated!=null&&!Progress.ledger.completed.Contains(AcceptedKey(gated)))
            {
                // before the request the place is only a place: describe it, take nothing
                Present(point.Prompt,string.IsNullOrEmpty(point.LockedText)?"아직 손댈 까닭이 없다.":point.LockedText);
                return true;
            }
            return false;
        }

        bool InteractCommission(WorldMacroPlaytestSO.CommissionSpec q,PrologueContentSO.Point giver)
        {
            if(DialogueSurfaceBound)return InteractCommission306(q,giver);
            var ledger=Progress.ledger;
            if(ledger.completed.Contains(ReportedKey(q))){Present(giver.Prompt,q.CompletedText);return true;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            if(!ledger.completed.Contains(AcceptedKey(q)))
            {
                candidate.ledger.completed.Add(AcceptedKey(q));
                if(!TryCommitInteraction(candidate,out string acceptError)){Show(acceptError);return false;}
                Present(giver.Prompt,q.OfferText);InteractionResolved?.Invoke(PrologueInteractionKind.Conversation,giver.Position);return true;
            }
            if(!CommissionSatisfied(q)){Present(giver.Prompt,q.WaitingText);return true;}
            if(q.Reward>int.MaxValue-candidate.ledger.currency){Show("통보 보유량을 확인한 뒤 다시 시도한다.");return false;}
            PrologueProgressStore.Complete(candidate.ledger,ReportedKey(q),q.Reward);
            if(!TryCommitInteraction(candidate,out string reportError)){Show(reportError);return false;}
            Present(giver.Prompt,q.ReportText+(q.Reward>0&&!IconPresentation?"\n조선통보 +"+q.Reward:""));
            InteractionResolved?.Invoke(q.Reward>0?PrologueInteractionKind.Currency:PrologueInteractionKind.Conversation,giver.Position);
            return true;
        }
    }
}
