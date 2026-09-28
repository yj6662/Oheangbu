using System;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    public static class MumBridgeUnlock295
    {
        // Reserved late-game proof IDs. No existing candidate boss or ordinary discovery grants them.
        public const string CodaId="world_unlock_coda_mieum", BossId="fallen_hwangryong";
        public static bool HasProof(WorldMacroProgress progress)=>progress?.ledger?.completed!=null&&
            progress.ledger.completed.Contains(CodaId)&&progress.campaign?.EncounterEvidence!=null&&
            progress.campaign.EncounterEvidence.Contains(BossId)&&progress.defeated!=null&&progress.defeated.Contains(BossId);
    }
    public sealed partial class WorldMacroPlaytestSession
    {
        public MumBridgeProfileSO MumBridgeProfile;
        public MumBridgeService MumBridges {get;private set;}
        public bool HasMumBridge=>DemoCampaignActive&&MumBridgeUnlock295.HasProof(Progress);
#if UNITY_EDITOR
        [NonSerialized] bool mum295TestUnlock;
        // Captured when the store is opened, so changing TestSaveSuffix later cannot unlock the ordinary store.
        [NonSerialized] bool mum295IsolatedStore;
        public void SetMum295TestUnlock(bool enabled)
        {
            if(enabled&&(!Application.isPlaying||!mum295IsolatedStore))
                throw new InvalidOperationException("Mum test unlock requires a session opened in an explicitly allowed isolated Mum test store.");
            mum295TestUnlock=enabled;
        }
#endif
        bool MumBridgeUnlocked()
        {
            if(HasMumBridge)return true;
#if UNITY_EDITOR
            return mum295TestUnlock&&mum295IsolatedStore;
#else
            return false;
#endif
        }
        void BindMumBridges()
        {
            if(MumBridgeProfile==null||Walker==null)return;
            MumBridges=GetComponent<MumBridgeService>()??gameObject.AddComponent<MumBridgeService>();
            MumBridges.Configure(Walker.Body,Walker.Motor,vitals,Walker.ViewCamera,Traversal,MumBridgeProfile,
                ()=>ready&&isActiveAndEnabled&&MumBridgeUnlocked(),()=>Walker.Seated,()=>GameplayInputBlocked);
            MumBridges.CombatBlocked=()=>Actors!=null&&Actors.Any(a=>a!=null&&a.isActiveAndEnabled&&
                a.Current==Oheangbu.App.Prologue.PrologueEncounter.Behaviour.Chase);
            MumBridges.CurrentArea=()=>MountainLayout!=null&&Walker.Body!=null?
                MountainLayout.RealmAt(new Vector2(Walker.Body.transform.position.x,Walker.Body.transform.position.z))?.Id:gameObject.scene.name;
            MumBridges.PreviewRequested=()=>Walker.Wiring!=null&&Walker.Wiring.FieldPlacementPreviewRequested;
            MumBridges.PlacementRejected-=OnMumPlacementRejected;MumBridges.PlacementRejected+=OnMumPlacementRejected;
            Walker.Wiring.MumBridges=MumBridges;
        }
        void OnMumPlacementRejected(string reason){if(!string.IsNullOrEmpty(reason))Show(reason);}
        void SuspendMumBridges(){if(MumBridges!=null)MumBridges.enabled=false;}
        void UnbindMumBridges()
        {
            if(MumBridges!=null){MumBridges.PlacementRejected-=OnMumPlacementRejected;MumBridges.enabled=false;}
            if(Walker!=null&&Walker.Wiring!=null&&Walker.Wiring.MumBridges==MumBridges)Walker.Wiring.MumBridges=null;
        }
    }
}
