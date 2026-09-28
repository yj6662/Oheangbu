using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        readonly HashSet<string> pendingEncounterDefeats=new HashSet<string>();
        float nextEncounterCommit;
        bool encounterDeathPending;

        // All witnessed deaths must cross the same save boundary before rest,
        // player recovery, equipment transactions or an ordinary snapshot.
        bool HasPendingDefeats => pendingEncounterDefeats.Count > 0 ||
            pendingBossDefeats.Count > 0 || southGateDeathWitness != null;
        void FlushPendingDefeats(bool force = false)
        {
            TryCommitPendingEncounterDefeats(force);
            TryCommitPendingDemoBosses(force);
            TryCommitPendingDemoSouthGate(force);
        }

        void QueueEncounterDefeat(string id)
        {
            var actor=Actors.FirstOrDefault(a=>a!=null&&a.Id==id&&a.gameObject.scene==gameObject.scene);
            if(actor==null||actor.GetComponent<EnemyVitals>().IsAlive||Progress.defeated.Contains(id))return;
            pendingEncounterDefeats.Add(id);
            TryCommitPendingEncounterDefeats(true);
        }

        // Durable discovery is separate from the respawnable living/dead state.
        // Capture the death witness once; retry from the latest progress after a failed write.
        void TryCommitPendingEncounterDefeats(bool force=false)
        {
            if(!ready||pendingEncounterDefeats.Count==0||!force&&Time.unscaledTime<nextEncounterCommit)return;
            nextEncounterCommit=Time.unscaledTime+1;
            foreach(string id in pendingEncounterDefeats.ToArray())
            {
                if(Progress.defeated.Contains(id)){pendingEncounterDefeats.Remove(id);continue;}
                bool accepted=TryPrepareMountainDefeat(id,out var proposal,out var error,out bool mountainOwned);
                if(!mountainOwned)accepted=TryPrepareEncounterDefeat(Progress,id,Content.TestRules.EnemyReward,out proposal,out error);
                if(!accepted){Show(error);continue;}
                if(!TryCommitInteraction(proposal,out error)){Show(error);continue;}
                pendingEncounterDefeats.Remove(id);
            }
        }

        public static bool TryPrepareEncounterDefeat(WorldMacroProgress source,string id,int reward,out WorldMacroProgress proposal,out string error)
        {
            proposal=null;error=null;
            if(!WorldMacroProgress.Valid(source)||string.IsNullOrWhiteSpace(id)||source.defeated.Contains(id))return false;
            if(reward<0||reward>int.MaxValue-source.ledger.currency){error="통보 보유량을 확인한 뒤 다시 시도한다.";return false;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            candidate.defeated.Add(id);candidate.ledger.currency+=reward;
            if(!candidate.campaign.EncounterEvidence.Contains(id))candidate.campaign.EncounterEvidence.Add(id);
            string fact="defeated:"+id;if(!candidate.campaign.Facts.Contains(fact))candidate.campaign.Facts.Add(fact);
            proposal=candidate;return true;
        }
    }
}
