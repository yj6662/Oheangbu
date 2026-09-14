using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using VContainer;

namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession:MonoBehaviour
    {
        public WorldMacroPlaytestSO Content;
        public WorldMacroCombatWalker Walker;
        public PrologueEncounter[] Actors;
        public WorldMacroContentPoint[] PreviewPoints;
        public WorldMacroContentSheetSO PreviewSheet;
        public GameplayRuntimeStateSO RuntimeState;
        public string TestSaveSuffix="";
        public WorldMacroProgress Progress {get;private set;}
        public string SaveError {get;private set;}
        public string LoadStatus {get;private set;}
        public string FocusedId {get;private set;}
        public string FocusedCollectionBundleId {get;private set;}
        public string LastFeedback {get;private set;}
        public WorldMacroCollectionNotice LastCollectionNotice {get;private set;}
        public event Action<PrologueInteractionKind,Vector3> InteractionResolved;
        public event Action<string> NoticeRaised;
        public event Action<string,string> DetailRequested;
        public event Action<WorldMacroCollectionNotice> CollectionChanged;
        public string CurrentHudText
        {
            get
            {
                if(!string.IsNullOrEmpty(SaveError))return SaveError;
                if(Time.unscaledTime<until&&!string.IsNullOrEmpty(LastFeedback))return LastFeedback;
                if(!string.IsNullOrEmpty(FocusedCollectionBundleId))
                {
                    var pickup=fragmentPickups.Find(x=>x!=null&&x.BundleId==FocusedCollectionBundleId);
                    return pickup!=null?"[F] "+pickup.Prompt:null;
                }
                if(FocusedId=="CurrencyDrop")return "[F] 남긴 통보 회수";
                if(string.IsNullOrEmpty(FocusedId))return OpeningObjective;
                var point=FindInteractionPoint(FocusedId);
                return point!=null?"[F] "+point.Prompt:OpeningObjective;
            }
        }
        public bool CanvasHudPresenterActive=>canvasHudPresenters>0;
        public bool CombatActive=true;
        [Tooltip("Authoring markers for reserved content only; keep off for external playtests.")]
        public bool ShowUnconnectedPreviewMarkers=false;
        public Vector3 LastSafeFeet=>lastSafe;
        InkPool ink;GroggyMeter groggy;ParryJudge parry;
        AtomicJsonStore<WorldMacroProgress> store;
        PlayerVitals vitals;Vector3 lastSafe;float nextSave,nextCull,until;
        string lastSuccessfulSnapshot;
        readonly List<WorldMacroFragmentPickup> fragmentPickups=new List<WorldMacroFragmentPickup>();
        bool ready,respawning,saveBlockedByInvalidLoad;int canvasHudPresenters;GameObject drop;Font font;GUIStyle style;
        public bool SaveBlocked=>saveBlockedByInvalidLoad;
        public bool GameplayInputBlocked=>Time.timeScale<=.0001f||(RuntimeState!=null&&RuntimeState.InputBlocked);
        [Inject] public void Construct(InkPool ink,GroggyMeter groggy,ParryJudge parry){this.ink=ink;this.groggy=groggy;this.parry=parry;}
        void Start()
        {
            if(Content==null||Content.TestRules==null||Walker==null||ink==null)throw new InvalidOperationException("Macro playtest wiring is incomplete");
            BindActorsToLoadedNavigation();
            BindOpeningPoints();
            vitals=Walker.Body.GetComponent<PlayerVitals>();
            store=new AtomicJsonStore<WorldMacroProgress>(Path.Combine(Application.persistentDataPath,Content.SaveSlot+TestSaveSuffix+".json"),WorldMacroProgress.Valid);
            var loaded=store.Load();
            LoadStatus=store.LoadStatus;
            if(LoadStatus=="primary"&&loaded!=null&&loaded.version==WorldMacroProgress.CurrentVersion)lastSuccessfulSnapshot=JsonUtility.ToJson(loaded);
            bool migrated=loaded!=null&&loaded.version<WorldMacroProgress.CurrentVersion;
            saveBlockedByInvalidLoad=LoadStatus=="invalid";
            Progress=WorldMacroProgress.MigrateToCurrent(loaded)??CreateFreshProgress();
            RepairProgress();Teleport(Progress.ledger.position,Progress.ledger.yaw);
            vitals.Restore(Progress.ledger.hp>0?Progress.ledger.hp:1);ink.Restore(Progress.ledger.ink);
            foreach(var actor in Actors){actor.Defeated+=EnemyDefeated;if(Progress.defeated.Contains(actor.Id))actor.GetComponent<EnemyVitals>().TakeDamage(float.MaxValue);}
            vitals.Died+=OnDeath;ready=true;nextSave=Time.unscaledTime+Content.TestRules.AutosaveSeconds;RefreshDrop();RefreshFragmentPickups();
            if(LoadStatus=="invalid")SaveError="저장 파일이 손상되었습니다. 제목 화면에서 백업을 복구하거나 보관 후 새로 시작하십시오.";
            else if(LoadStatus=="backup")Show("이전 백업에서 진행을 복구했습니다.");
            else if(LoadStatus=="temporary")Show("중단된 저장 파일에서 진행을 복구했습니다.");
            if((migrated||LoadStatus=="temporary"||LoadStatus=="new"&&OpeningJourneyActive)&&!saveBlockedByInvalidLoad)SaveNow(out _);
        }
        void BindActorsToLoadedNavigation()
        {
            if(Actors==null)throw new InvalidOperationException("Macro playtest actors are missing");
            int bound=0;
            foreach(var actor in Actors)
            {
                if(actor==null)throw new InvalidOperationException("Macro playtest actor reference is missing");
                var agent=actor.GetComponent<NavMeshAgent>();
                if(agent==null)throw new InvalidOperationException("Macro playtest actor has no NavMeshAgent: "+actor.Id);
                if(!agent.enabled)
                {
                    Vector3 floorProbe=actor.transform.position-Vector3.up*agent.baseOffset;
                    if(!NavMesh.SamplePosition(floorProbe,out _,2f,agent.areaMask))
                        throw new InvalidOperationException("Macro playtest actor has no loaded NavMesh at startup: "+actor.Id);
                    agent.enabled=true;
                }
                if(!agent.isOnNavMesh)
                {
                    agent.enabled=false;
                    throw new InvalidOperationException("Macro playtest actor failed to bind to loaded NavMesh: "+actor.Id);
                }
                agent.ResetPath();agent.isStopped=false;bound++;
            }
            Debug.Log("[WorldMacroPlaytest] Bound "+bound+" encounter agents after scene NavMesh activation.");
        }
        Vector3 Checkpoint()
        {
            Vector3 preferred=AuthoredCheckpointFeet();
            if(TrySafeFeet(preferred,out var feet))return feet;
            if(TrySafeFeet(Content.StartFeet,out feet))return feet;
            throw new InvalidOperationException("No safe authored checkpoint. Repair playtest terrain before continuing.");
        }
        void RepairProgress()
        {
            var l=Progress.ledger;
            if(!WorldMacroOpeningProgress.KnownCheckpoint(l.checkpoint))l.checkpoint="mine_start";
            l.checkpointPosition=Checkpoint();
            // Unknown completed/reward IDs are retained as tombstones: removing them could repay renamed content.
            if(!l.hasPosition||Progress.terrainRevision!=Content.TerrainRevision||!TrySafeFeet(l.position,out var feet))feet=l.checkpointPosition;
            l.position=feet;l.hasPosition=true;
            if(!float.IsFinite(l.yaw))l.yaw=CheckpointYaw();
            if(l.dropCurrency>0){if(!TrySafeFeet(l.dropPosition,out var dropFeet))dropFeet=l.checkpointPosition;l.dropPosition=dropFeet;}
            Progress.terrainRevision=Content.TerrainRevision;
        }
        public bool TrySafeFeet(Vector3 candidate,out Vector3 feet)
        {
            feet=default;
            if(!float.IsFinite(candidate.x)||!float.IsFinite(candidate.y)||!float.IsFinite(candidate.z))return false;
            if(candidate.x<PreviewSheetBoundsMin.x||candidate.x>PreviewSheetBoundsMax.x||candidate.z<PreviewSheetBoundsMin.y||candidate.z>PreviewSheetBoundsMax.y)return false;
            foreach(var h in Physics.RaycastAll(candidate+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)){
                if(h.transform.IsChildOf(Walker.Body.transform)||h.normal.y<Mathf.Cos(Walker.Body.slopeLimit*Mathf.Deg2Rad))continue;
                var p=h.point+Vector3.up*.05f;
                bool blocked=Physics.OverlapCapsule(p+Vector3.up*.31f,p+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(Walker.Body.transform));
                if(blocked)continue;
                bool supported=true;
                foreach(var offset in new[]{Vector3.right*.25f,Vector3.left*.25f,Vector3.forward*.25f,Vector3.back*.25f})
                    supported&=Physics.Raycast(p+offset+Vector3.up*.3f,Vector3.down,.65f,1,QueryTriggerInteraction.Ignore);
                if(supported){feet=p;return true;}
            }
            return false;
        }
        public Vector2 PreviewSheetBoundsMin=new Vector2(-4500,-4200),PreviewSheetBoundsMax=new Vector2(4500,8200);
        void Update()
        {
            if(!ready||respawning)return;
            if(Time.unscaledTime>=nextSave){Save();nextSave=Time.unscaledTime+Content.TestRules.AutosaveSeconds;}
            if(GameplayInputBlocked){FocusedId=null;FocusedCollectionBundleId=null;return;}
            if(!Walker.Seated){
                if(lastSafe.y-Walker.Body.transform.position.y>=Content.TestRules.FallDeathHeight){vitals.ApplyFatalFall();return;}
                if(Walker.Body.isGrounded&&TrySafeFeet(Walker.Body.transform.position,out var safe))lastSafe=safe;
            }
            if(Time.unscaledTime>=nextCull){Cull();nextCull=Time.unscaledTime+.2f;}
            FocusedId=null;FocusedCollectionBundleId=null;float closest=float.MaxValue;
            if(Walker.Seated||Walker.Motor.IsDrawing)return;
            foreach(var p in InteractionPoints)if(CanInteract(p.Id)&&Vector3.Distance(p.Position,Walker.Body.transform.position)<closest){FocusedId=p.Id;closest=Vector3.Distance(p.Position,Walker.Body.transform.position);}
            if(CanInteract("CurrencyDrop")){FocusedId="CurrencyDrop";closest=Vector3.Distance(Progress.ledger.dropPosition,Walker.Body.transform.position);}
            foreach(var pickup in fragmentPickups)
            {
                if(!CanInteract(pickup))continue;float distance=Vector3.Distance(pickup.InteractionPosition,Walker.Body.transform.position);
                if(distance<closest){FocusedId=null;FocusedCollectionBundleId=pickup.BundleId;closest=distance;}
            }
            if(Keyboard.current!=null&&Keyboard.current.fKey.wasPressedThisFrame)
            {
                if(!string.IsNullOrEmpty(FocusedCollectionBundleId))TryCollectFragmentBundle(FocusedCollectionBundleId,out _);
                else if(FocusedId!=null)Interact(FocusedId);
            }
        }
        public bool CanInteract(string id)
        {
            if(!ready||GameplayInputBlocked||Walker.Seated||!Walker.Motor.enabled||Walker.Motor.IsDrawing||Walker.Drawing.InDrawMode)return false;
            var p=FindInteractionPoint(id);
            bool lost=id=="CurrencyDrop"&&Progress.ledger.dropCurrency>0;
            if(p==null&&!lost)return false;
            Vector3 target=lost?Progress.ledger.dropPosition:p.Position;
            if(Vector3.Distance(Walker.Body.transform.position,target)>(lost?2.5f:p.Radius))return false;
            Vector3 eye=Walker.Body.transform.position+Vector3.up*Walker.EyeHeight,to=target+Vector3.up*1.25f-eye;
            foreach(var h in Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore)){
                if(h.transform.IsChildOf(Walker.Body.transform))continue;
                var point=h.transform.GetComponentInParent<WorldMacroContentPoint>();if(point!=null&&point.Id==id)continue;
                return false;
            }
            return true;
        }
        public bool Interact(string id)
        {
            if(!CanInteract(id))return false;
            if(id=="CurrencyDrop"){Vector3 point=Progress.ledger.dropPosition;int amount=PrologueProgressStore.Retrieve(Progress.ledger);RefreshDrop();Show("조선통보 "+amount+" 회수");Save();InteractionResolved?.Invoke(PrologueInteractionKind.Currency,point);return true;}
            var p=FindInteractionPoint(id);
            if(p.Kind==PrologueInteractionKind.Rest){Rest();return true;}
            bool first=PrologueProgressStore.Complete(Progress.ledger,id,p.Currency);
            bool newRecord=WorldMacroCollectionCatalog.TryGetRecordForInteraction(id,out var record)&&Progress.ui.AddRecord(record.Id);
            Show(id==WorldMacroOpeningProfileSO.CommissionId?"폐광 폭파 조사 의뢰를 확인했다.":p.Kind==PrologueInteractionKind.Currency&&!first?"이미 챙긴 물품이다.":p.Text+(first&&p.Currency>0?"\n조선통보 +"+p.Currency:""));
            bool saved=Save();
            if(!saved&&id==WorldMacroOpeningProfileSO.CommissionId&&first)Progress.ledger.completed.Remove(id);
            InteractionResolved?.Invoke(p.Kind,p.Position);
            if(p.Kind==PrologueInteractionKind.Evidence||p.Kind==PrologueInteractionKind.Conversation)DetailRequested?.Invoke(p.Prompt,p.Text);
            if(newRecord)Debug.Log("[WorldMacroPlaytest] Record discovered: "+record.Id);
            return true;
        }
        public void EnemyDefeated(string id)
        {
            if(!ready||!Content.Encounters.Any(e=>e.Id==id))return;
            if(Progress.Defeat(id,Content.TestRules.EnemyReward))Save();
        }
        void ResetCombat()
        {
            Walker.Drawing.enabled=false;Walker.Drawing.enabled=true;
            Walker.Wiring.enabled=false;Walker.Wiring.enabled=true;
            groggy.Reset();parry.ClearGuard();
            var target=Walker.Motor.GetComponent<LockOn>();if(target!=null&&target.IsLocked)target.Toggle();
            foreach(var actor in Actors){
                var spec=Array.Find(Content.Encounters,x=>x.Id==actor.Id);if(!spec.RespawnOnRest)continue;
                Progress.defeated.Remove(actor.Id);actor.gameObject.SetActive(true);actor.GetComponent<NavMeshAgent>().enabled=true;actor.enabled=true;actor.ResetEncounter();
            }
            Cull();
        }
        void Rest()
        {
            Progress.ledger.checkpoint="geumpyo_inn";Progress.ledger.checkpointPosition=Checkpoint();
            ResetCombat();vitals.Restore();ink.Restore();PrologueProgressStore.Complete(Progress.ledger,"geumpyo_inn",0);
            Show("금표 주막에서 숨을 고른다.\n체력과 먹을 회복했다.");Save();InteractionResolved?.Invoke(PrologueInteractionKind.Rest,Progress.ledger.checkpointPosition);
        }
        void OnDeath()
        {
            if(!ready||respawning||Walker.Seated)return;respawning=true;
            PrologueProgressStore.Drop(Progress.ledger,lastSafe);ResetCombat();Teleport(Checkpoint(),CheckpointYaw());
            vitals.Restore();ink.Restore();RefreshDrop();respawning=false;Show("마지막 쉼터에서 눈을 떴다. 남긴 통보를 되찾을 수 있다.");Save();
        }
        public void Teleport(Vector3 feet,float yaw)
        {
            var body=Walker.Body;bool prior=body.enabled;body.enabled=false;body.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));body.enabled=prior;Walker.Motor.ResetMotion();lastSafe=feet;
        }
        public bool Save()
        {
            return SaveNow(out _);
        }
        public bool SaveNow(out string error)
        {
            if(!ready){error="저장 세션이 아직 준비되지 않았습니다.";return false;}
            if(saveBlockedByInvalidLoad){error=SaveError??"손상된 저장 파일을 먼저 복구하거나 보관해야 합니다.";return false;}
            var l=Progress.ledger;l.position=lastSafe;l.hasPosition=true;
            // Scene unload may destroy the player before this component's OnDisable.
            if(Walker!=null&&Walker.Body!=null)l.yaw=Walker.Body.transform.eulerAngles.y;
            if(vitals!=null)l.hp=vitals.Hp01;
            if(ink!=null)l.ink=ink.Value;
            string snapshot=JsonUtility.ToJson(Progress);
            if(!RequiresSnapshotWrite(lastSuccessfulSnapshot,snapshot)){SaveError=null;error=null;return true;}
            try{store.Save(Progress);lastSuccessfulSnapshot=snapshot;SaveError=null;error=null;return true;}
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){SaveError="저장 실패: "+e.Message;error=SaveError;Debug.LogError(SaveError);return false;}
        }
        internal static bool RequiresSnapshotWrite(string previousSnapshot,string currentSnapshot)
        {return !string.Equals(previousSnapshot,currentSnapshot,StringComparison.Ordinal);}
        public void Cull()
        {
            if(GameplayInputBlocked)return;
            foreach(var actor in Actors){
                var spec=Array.Find(Content.Encounters,x=>x.Id==actor.Id);bool alive=actor.GetComponent<EnemyVitals>().IsAlive;
                bool active=CombatActive&&!Walker.Seated&&alive&&Vector3.Distance(Walker.Body.transform.position,actor.transform.position)<spec.Activation;
                var attack=actor.GetComponent<EnemyController>();var agent=actor.GetComponent<NavMeshAgent>();
                if(!active){attack.AttackEnabled=false;attack.enabled=false;actor.enabled=false;if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;}
                else{actor.enabled=true;attack.enabled=true;}
                foreach(var r in actor.GetComponentsInChildren<Renderer>())r.enabled=active;
            }
            foreach(var point in PreviewPoints){if(point==null||point.Visual==null||point.CombatConnected)continue;
                var entry=Array.Find(PreviewSheet.Entries,x=>x.Id==point.Id);if(entry!=null)point.Visual.gameObject.SetActive(ShowUnconnectedPreviewMarkers&&Vector3.Distance(Walker.Body.transform.position,point.transform.position)<entry.ActivationDistance);
            }
        }
        void RefreshDrop()
        {
            if(drop!=null)Destroy(drop);if(Progress.ledger.dropCurrency<=0)return;
            drop=GameObject.CreatePrimitive(PrimitiveType.Sphere);drop.name="Playtest_LostTongbo";drop.transform.position=Progress.ledger.dropPosition+Vector3.up*.1f;drop.transform.localScale=new Vector3(.3f,.15f,.3f);Destroy(drop.GetComponent<Collider>());
        }
        void Show(string message){LastFeedback=message;until=Time.unscaledTime+7;NoticeRaised?.Invoke(message);}
        public void RegisterFragmentPickup(WorldMacroFragmentPickup pickup)
        {
            if(pickup==null||fragmentPickups.Contains(pickup))return;fragmentPickups.Add(pickup);
            if(Progress!=null)RefreshFragmentPickup(pickup);
        }
        public void UnregisterFragmentPickup(WorldMacroFragmentPickup pickup)
        {if(pickup!=null)fragmentPickups.Remove(pickup);}
        public bool TryCollectFragmentBundle(string bundleId,out WorldMacroCollectionNotice notice)
        {
            notice=null;if(GameplayInputBlocked||Progress==null||Progress.ui==null)return false;
            var pickup=fragmentPickups.Find(x=>x!=null&&x.BundleId==bundleId);
            if(!CanInteract(pickup))return false;
            var before=Progress.ui.Copy();
            if(!WorldMacroInventoryService.TryCollectBundle(Progress.ui,bundleId,out notice))return false;
            if(!SaveNow(out var error))
            {
                Progress.ui.CopyFrom(before);notice=null;Show(error);return false;
            }
            LastCollectionNotice=notice;Show(notice.Message);RefreshFragmentPickups();CollectionChanged?.Invoke(notice);return true;
        }
        bool CanInteract(WorldMacroFragmentPickup pickup)
        {
            if(pickup==null||!pickup.isActiveAndEnabled||!CanUseInteractionInput())return false;
            if(!WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId,out var bundle)||WorldMacroInventoryService.IsBundleCollected(Progress.ui,bundle))return false;
            Vector3 target=pickup.InteractionPosition;
            if(Vector3.Distance(Walker.Body.transform.position,target)>pickup.Radius)return false;
            Vector3 eye=Walker.Body.transform.position+Vector3.up*Walker.EyeHeight,to=target+Vector3.up*.5f-eye;
            foreach(var hit in Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.transform.IsChildOf(Walker.Body.transform)||hit.transform.IsChildOf(pickup.transform))continue;
                return false;
            }
            return true;
        }
        bool CanUseInteractionInput()
        {return ready&&!GameplayInputBlocked&&!Walker.Seated&&Walker.Motor.enabled&&!Walker.Motor.IsDrawing&&!Walker.Drawing.InDrawMode;}
        void RefreshFragmentPickups(){foreach(var pickup in fragmentPickups.ToArray())RefreshFragmentPickup(pickup);}
        void RefreshFragmentPickup(WorldMacroFragmentPickup pickup)
        {
            if(pickup==null||Progress==null||Progress.ui==null)return;
            bool collected=WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId,out var bundle)&&WorldMacroInventoryService.IsBundleCollected(Progress.ui,bundle);
            pickup.RefreshCollectedState(collected);
        }
        public void SetCanvasHudPresenterActive(bool active)
        {canvasHudPresenters=Mathf.Max(0,canvasHudPresenters+(active?1:-1));}
        void OnGUI()
        {
            if(CanvasHudPresenterActive)return;
            string text=CurrentHudText;
            if(string.IsNullOrEmpty(text))return;
            if(style==null){font=Font.CreateDynamicFontFromOSFont("Malgun Gothic",20);style=new GUIStyle(GUI.skin.box){font=font,fontSize=20,wordWrap=true,alignment=TextAnchor.MiddleCenter};}
            GUI.Box(new Rect(Screen.width*.2f,Screen.height-140,Screen.width*.6f,95),text,style);
        }
        void OnApplicationQuit(){Save();}
        void OnDisable(){Save();}
        void OnDestroy(){if(vitals!=null)vitals.Died-=OnDeath;foreach(var a in Actors)if(a!=null)a.Defeated-=EnemyDefeated;if(drop!=null)Destroy(drop);if(font!=null)Destroy(font);}
    }
}
