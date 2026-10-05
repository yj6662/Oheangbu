using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 1b′ 남문 전 봉인 — save relocation on load (SPEC-WORLD-ENCLOSURE-305 §1b′ "세이브 이전", D308-3) [TEST].
    // RepairProgress() ends here. Without SealProfile (W_Demo_Compact and every scene the seal builder has not touched) nothing
    // changes. The save format is unchanged: the moved ledger is written by the next save. No UI text, one log line.
    // No static state (domain reload is off): the counters below are per-instance diagnostics for the checks.
    public sealed partial class WorldMacroPlaytestSession
    {
        [Tooltip("#308 1b′: beyond-the-seal polygons and EA rests (set by the editor seal builder). Empty = no relocation.")]
        public WorldSealProfileSO SealProfile;
        public int SealRelocations{get;private set;}
        public string LastSealRelocation{get;private set;}
        readonly RaycastHit[] sealSurfaceHits308=new RaycastHit[32];

        void ApplySealRelocation308()
        {
            if(SealProfile==null||Progress?.ledger==null)return;
            if(!WorldSealRules308.TryPlan(SealProfile,Content,Progress,SealSurfaceY308,TrySafeFeet,out var plan,out string reason))
            {
                if(reason=="no resolvable EA rest")Debug.LogWarning("[WorldMacroPlaytest] #308 seal: a save beyond the seal found no resolvable EA rest; left in place.");
                return;
            }
            WorldSealRules308.Apply(plan,Progress.ledger);
            SealRelocations++;LastSealRelocation=plan.Describe();
            Debug.Log("[WorldMacroPlaytest] #308 seal: moved a save from beyond the seal to "+LastSealRelocation);
        }

        // Highest static surface over the point (layer 1, the safe-feet ground layer), the player's own body excluded. NaN = none.
        float SealSurfaceY308(Vector3 p)
        {
            if(!float.IsFinite(p.x)||!float.IsFinite(p.z))return float.NaN;
            float top=Mathf.Max(p.y,0f)+800f;
            int n=Physics.RaycastNonAlloc(new Vector3(p.x,top,p.z),Vector3.down,sealSurfaceHits308,top+2000f,1,QueryTriggerInteraction.Ignore);
            var hits=sealSurfaceHits308;
            if(n>=hits.Length){hits=Physics.RaycastAll(new Vector3(p.x,top,p.z),Vector3.down,top+2000f,1,QueryTriggerInteraction.Ignore);n=hits.Length;}
            var self=Walker!=null&&Walker.Body!=null?Walker.Body.transform:null;
            float best=float.NaN;
            for(int i=0;i<n;i++)
            {
                var h=hits[i];if(self!=null&&h.transform.IsChildOf(self))continue;
                if(float.IsNaN(best)||h.point.y>best)best=h.point.y;
            }
            return best;
        }
    }
}
