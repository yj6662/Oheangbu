using System;
using System.IO;
using System.Linq;
using Oheangbu.Combat;
using Oheangbu.App.Demo;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using VContainer;
namespace Oheangbu.App.Prologue
{
    public sealed partial class PrologueSession : MonoBehaviour
    {
        public PrologueContentSO Content; public Transform Player; public PrologueEncounter[] Encounters;
        public CombatLoopWiring Wiring;
        public PrologueProgress Progress {get;private set;}
        public string TestSaveSuffix="";
        public event Action<string> Feedback;
        public enum InteractionResult { Success, Waiting, SaveFailed }
        public event Action<InteractionResult> InteractionResolved;
        public event Action<PrologueInteractionKind,Vector3,InteractionResult> InteractionPresented;
        string interactionContext;
        void Resolve(InteractionResult result){
            InteractionResolved?.Invoke(result);
            if(interactionContext==null)return;
            var point=Array.Find(Content.Points,p=>p.Id==interactionContext);
            if(point!=null)InteractionPresented?.Invoke(point.Kind,point.Position,result);
            else if(interactionContext=="CurrencyDrop")InteractionPresented?.Invoke(PrologueInteractionKind.Currency,Player.position,result);
        }
        InkPool ink;GroggyMeter groggy;ParryJudge parry;PrologueProgressStore store;PlayerVitals vitals;CharacterController cc;
        PrologueProgress pendingDeath;float nextDeathRetry;
        readonly HashSet<string> pendingFieldDefeats=new HashSet<string>();
        readonly HashSet<string> rewardedFieldLives=new HashSet<string>();
        readonly HashSet<string> pendingBossDefeats=new HashSet<string>();float nextBossRetry;
        Vector3 lastGrounded;float nextSave;bool ready,respawning;GameObject drop;Material dropMaterial;
        [Inject] public void Construct(InkPool ink,GroggyMeter groggy,ParryJudge parry){this.ink=ink;this.groggy=groggy;this.parry=parry;}
        void Start()
        {
            if(Content==null||Player==null) {enabled=false;return;}
            vitals=Player.GetComponent<PlayerVitals>();cc=Player.GetComponent<CharacterController>();
            store=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,Content.SaveSlot+TestSaveSuffix+".json"));
            Progress=store.Load()??new PrologueProgress{checkpointPosition=Content.StartPosition,position=Content.StartPosition,yaw=Content.StartYaw};
            if(Progress.defeated==null)Progress.defeated=new System.Collections.Generic.List<string>();
            var position=Progress.hasPosition?Progress.position:Progress.checkpointPosition;
            if(!IsSafe(position))position=Progress.checkpointPosition;
            Teleport(position,Progress.yaw);vitals.Restore(Progress.hp>0?Progress.hp:1);ink?.Restore(Progress.ink);
            foreach(var actor in Encounters){var boss=actor!=null?actor.GetComponent<CheongryongCombatController>():null;if(boss!=null)boss.Configure(Player,vitals,parry);var general=actor!=null?actor.GetComponent<SouthGateGeneralController>():null;if(general!=null)general.Configure(Player,vitals,parry);}
            RefreshEncounterRules();BindJourneyTransport();BindJourneyField();vitals.BindLethalDamageGuard(TryJourneyRen);vitals.Died+=OnDeath;ready=true;nextSave=Time.time+Content.AutosaveSeconds;RefreshDrop();
        }
        bool IsSafe(Vector3 p)
        {
            if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))return false;
            foreach(var hit in Physics.RaycastAll(p+Vector3.up*2,Vector3.down,5,1,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(Player)&&hit.normal.y>.5f)return true;
            return false;
        }
        void Update()
        {
            if(!ready)return;
            RefreshEncounterRules();
            if(!respawning&&(pendingBossDefeats.Count>0||pendingFieldDefeats.Count>0)&&Time.timeScale>0&&Time.unscaledTime>=nextBossRetry){nextBossRetry=Time.unscaledTime+1;TryCommit(CopyProgress());}
            if(respawning){if(pendingDeath!=null&&Time.timeScale>0&&Time.unscaledTime>=nextDeathRetry){nextDeathRetry=Time.unscaledTime+2;if(TryCommit(pendingDeath,false))FinishRespawn();}return;}
            TickJourneyEscort();TickJourneyEscortFollow();
            if(JourneySeated){TrackJourneyTransportGround();if(Time.time>=nextSave){TryCommit(CopyProgress());nextSave=Time.time+Content.AutosaveSeconds;}return;}
            if(cc.isGrounded){if(lastGrounded.y-Player.position.y>=Content.FallDeathHeight){vitals.ApplyFatalFall();return;}lastGrounded=Player.position;}
            else if(lastGrounded.y-Player.position.y>=Content.FallDeathHeight){vitals.ApplyFatalFall();return;}
            if(Time.time>=nextSave && cc.isGrounded){TryCommit(CopyProgress());nextSave=Time.time+Content.AutosaveSeconds;}
        }
        void OnDestroy(){UnbindJourneyTransport();if(vitals!=null)vitals.UnbindLethalDamageGuard(TryJourneyRen);UnbindJourneyField();if(vitals!=null)vitals.Died-=OnDeath;if(drop!=null)Destroy(drop);if(dropMaterial!=null)Destroy(dropMaterial);}
        void OnApplicationQuit(){SaveOnExit();}
        void OnDisable(){SaveOnExit();}
        void SaveOnExit(){if(!ready||Player==null||vitals==null)return;try{Save();}catch(IOException e){Debug.LogWarning("[Prologue] Exit save failed: "+e.Message);}catch(UnauthorizedAccessException e){Debug.LogWarning("[Prologue] Exit save denied: "+e.Message);}}
        public void Save()
        {
            if(!ready)return;
            var candidate=pendingDeath??CopyProgress();if(pendingDeath==null)Snapshot(candidate);
            store.Save(candidate);Progress=candidate;pendingFieldDefeats.Clear();pendingBossDefeats.RemoveWhere(id=>Progress.defeated.Contains(id));if(pendingDeath!=null)FinishRespawn();
        }
        public void Interact(string id)
        {
            string prior=interactionContext;interactionContext=id;
            try{InteractCore(id);}finally{interactionContext=prior;}
        }
        void InteractCore(string id)
        {
            if(!ready||respawning||JourneySeated)return;
            if(id=="CurrencyDrop") {if(Vector3.Distance(Player.position,Progress.dropPosition)>2.5f)return;var recovered=CopyProgress();int sum=PrologueProgressStore.Retrieve(recovered);if(!TryCommit(recovered)){Resolve(InteractionResult.SaveFailed);return;}Resolve(InteractionResult.Success);RefreshDrop();Feedback?.Invoke("조선통보 "+sum+" 회수");return;}
            var p=Array.Find(Content.Points,x=>x.Id==id);if(p==null)return;
            if(Vector3.Distance(Player.position,p.Position)>p.Radius+1f)return;
                        if(!CanCollectJourneyRevisit(p)){Resolve(InteractionResult.Waiting);return;}
            if((p.RequiredCompleted??Array.Empty<string>()).Any(key=>!Progress.completed.Contains(key)) || (p.RequiredDefeated??Array.Empty<string>()).Any(key=>!Progress.defeated.Contains(key))){Resolve(InteractionResult.Waiting);Feedback?.Invoke(p.LockedText);return;}
            if(p.Kind==PrologueInteractionKind.Rest){Rest(p);return;}
            if(TryInteractJourneyEscortStop(p)||TryInteractJourneyEscort(p))return;
            var commission=(Content.Commissions??Array.Empty<PrologueContentSO.Commission>()).FirstOrDefault(q=>q.GiverId==id);
            if(commission!=null){InteractCommission(commission);return;}
            var candidate=CopyProgress();
            bool first=PrologueProgressStore.Complete(candidate,id,p.Currency);
            if(!TryCommit(candidate)){Resolve(InteractionResult.SaveFailed);return;}Resolve(first?InteractionResult.Success:InteractionResult.Waiting);
            Feedback?.Invoke(p.Kind==PrologueInteractionKind.Currency&&!first?"이미 챙긴 물품이다.":p.Text+(first&&p.Currency>0?"\n조선통보 +"+p.Currency:""));
        }
        void Rest(PrologueContentSO.Point p)
        {
            if(Content.RestRequiresSafety && Encounters.Any(actor=>actor!=null && actor.isActiveAndEnabled && actor.Current==PrologueEncounter.Behaviour.Chase && actor.GetComponent<EnemyVitals>().IsAlive))
            {
                Resolve(InteractionResult.Waiting);
                return;
            }
            var candidate=CopyProgress();Snapshot(candidate);if(!PrepareJourneyEscortRest(candidate,p)){Resolve(InteractionResult.Waiting);return;}candidate.checkpoint=p.Id;candidate.checkpointPosition=p.Position+Vector3.up;candidate.hp=1;candidate.ink=1;candidate.renUsed=false;
            PrologueProgressStore.Complete(candidate,p.Id,0);if(!TryCommit(candidate,false)){Resolve(InteractionResult.SaveFailed);return;}Resolve(InteractionResult.Success);
            ResetCombat();vitals.Restore();ink?.Restore();
            Feedback?.Invoke("잠시 숨을 고른다.\n체력과 먹을 회복했다.  조선통보 "+Progress.currency);
        }
        public void EnemyDefeated(string id){
            if(!ready||respawning)return;
            var actor=Encounters.FirstOrDefault(a=>a!=null&&a.Id==id);
            if(actor==null||actor.GetComponent<EnemyVitals>().IsAlive)return;
            var rule=(Content.EncounterRules??Array.Empty<PrologueContentSO.EncounterRule>()).FirstOrDefault(r=>r.Id==id);
            if(rule!=null&&rule.PersistentDefeat){
                if(Progress.defeated.Contains(id))return;
                pendingBossDefeats.Add(id);TryCommit(CopyProgress());return;
            }
            if(!rewardedFieldLives.Add(id))return;
            pendingFieldDefeats.Add(id);TryCommit(CopyProgress());
        }
        void RefreshEncounterRules(){
            foreach(var rule in Content.EncounterRules??Array.Empty<PrologueContentSO.EncounterRule>()){
                var actor=Encounters.FirstOrDefault(a=>a!=null&&a.Id==rule.Id);if(actor==null)continue;
                bool active=(!rule.PersistentDefeat||!Progress.defeated.Contains(rule.Id))&&(rule.RequiredCompleted??Array.Empty<string>()).All(Progress.completed.Contains);
                if(actor.gameObject.activeSelf!=active)actor.gameObject.SetActive(active);
            }
        }
        int RewardForDefeat(string id){var rule=(Content.EncounterRules??Array.Empty<PrologueContentSO.EncounterRule>()).FirstOrDefault(r=>r.Id==id);return rule!=null&&rule.OverrideDefeatReward?Mathf.Max(0,rule.DefeatReward):Content.EnemyReward;}
        PrologueProgress CopyProgress(){
            var copy=JsonUtility.FromJson<PrologueProgress>(JsonUtility.ToJson(Progress));
            foreach(var id in pendingBossDefeats)if(!copy.defeated.Contains(id)){copy.defeated.Add(id);copy.currency+=RewardForDefeat(id);}
            foreach(var id in pendingFieldDefeats){if(!copy.defeated.Contains(id))copy.defeated.Add(id);copy.currency+=RewardForDefeat(id);}
            return copy;
        }
        void Snapshot(PrologueProgress candidate){candidate.position=lastGrounded;candidate.yaw=Player.eulerAngles.y;candidate.hp=vitals.Hp01;candidate.ink=ink!=null?ink.Value:1;candidate.hasPosition=true;SnapshotJourneyTransport(candidate);}
        bool TryCommit(PrologueProgress candidate,bool snapshot=true)
        {
            if(snapshot)Snapshot(candidate);
            try{store.Save(candidate);}catch(IOException e){Debug.LogWarning("[Prologue] Progress save failed: "+e.Message);Feedback?.Invoke("기록하지 못했다. 다시 시도해 주세요.");return false;}
            catch(UnauthorizedAccessException e){Debug.LogWarning("[Prologue] Progress save denied: "+e.Message);Feedback?.Invoke("기록하지 못했다. 저장 위치를 확인해 주세요.");return false;}
            Progress=candidate;pendingFieldDefeats.Clear();pendingBossDefeats.RemoveWhere(id=>Progress.defeated.Contains(id));return true;
        }
        void InteractCommission(PrologueContentSO.Commission q)
        {
            string accepted=q.Id+":accepted",reported=q.Id+":reported";
            if(Progress.completed.Contains(reported)){Resolve(InteractionResult.Waiting);Feedback?.Invoke(q.CompletedText);return;}
            var candidate=CopyProgress();
            if(!candidate.completed.Contains(accepted)){candidate.completed.Add(accepted);if(TryCommit(candidate)){Resolve(InteractionResult.Success);Feedback?.Invoke(q.OfferText);}else Resolve(InteractionResult.SaveFailed);return;}
            if(!candidate.completed.Contains(q.EvidenceId)){Resolve(InteractionResult.Waiting);Feedback?.Invoke(q.WaitingText);return;}
            PrologueProgressStore.Complete(candidate,reported,q.Reward);
            if(TryCommit(candidate)){Resolve(InteractionResult.Success);Feedback?.Invoke(q.ReportText+"\n조선통보 +"+q.Reward);}else Resolve(InteractionResult.SaveFailed);
        }
        void ResetCombat()
        {
            var wiring=Wiring;if(wiring!=null){wiring.enabled=false;wiring.enabled=true;}
            var lockOn=Player.GetComponent<LockOn>();if(lockOn!=null&&lockOn.IsLocked)lockOn.Toggle();
            groggy?.Reset();parry?.ClearGuard();foreach(var actor in Encounters)if(actor!=null&&actor.gameObject.activeInHierarchy&&!((Content.EncounterRules??Array.Empty<PrologueContentSO.EncounterRule>()).Any(r=>r.Id==actor.Id&&r.PersistentDefeat&&Progress.defeated.Contains(r.Id)))){actor.ResetEncounter();rewardedFieldLives.Remove(actor.Id);}
        }
        void OnDeath()
        {
            if(!ready||respawning)return;respawning=true;
            TrackJourneyTransportGround();pendingDeath=CopyProgress();PrepareJourneyEscortDeath(pendingDeath);PrologueProgressStore.Drop(pendingDeath,lastGrounded);
            pendingDeath.position=pendingDeath.checkpointPosition;pendingDeath.yaw=0;pendingDeath.hp=1;pendingDeath.ink=1;pendingDeath.hasPosition=true;
            nextDeathRetry=Time.unscaledTime+2;
            if(TryCommit(pendingDeath,false))FinishRespawn();
        }
        void FinishRespawn()
        {
            pendingDeath=null;ReleaseJourneyTransport();RestoreJourneyEscortAfterDeath();ResetCombat();Teleport(Progress.checkpointPosition,0);
            vitals.Restore();ink?.Restore();RefreshDrop();respawning=false;Feedback?.Invoke("마지막으로 쉬었던 곳에서 다시 눈을 떴다. 남긴 통보를 되찾을 수 있다.");
        }
        public void Teleport(Vector3 p,float yaw)
        {
            if(cc==null)cc=Player.GetComponent<CharacterController>();
            bool was=cc.enabled;cc.enabled=false;Player.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));cc.enabled=was;lastGrounded=p;
            Player.GetComponent<PlayerMotor>()?.ResetMotion();
        }
        void RefreshDrop()
        {
            if(drop!=null)Destroy(drop);if(dropMaterial!=null)Destroy(dropMaterial);if(Progress.dropCurrency<=0)return;
            drop=GameObject.CreatePrimitive(PrimitiveType.Sphere);drop.name="LostTongbo";drop.transform.position=Progress.dropPosition-Vector3.up*.7f;drop.transform.localScale=new Vector3(.3f,.15f,.3f);
            Destroy(drop.GetComponent<Collider>());var r=drop.GetComponent<Renderer>();dropMaterial=r.material;dropMaterial.color=new Color(.3f,.25f,.16f);
        }
    }
}
