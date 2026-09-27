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
        public bool IconPresentation;
        public EABuffProfileSO EABuffProfile;
        public EAWardProfileSO EAWardProfile;
        public EAGiyeokProfileSO EAGiyeokProfile;
        public WorldMacroInnRestPresentation InnRestPresentation;
        public WorldMacroInnRestPresentation VillageRestPresentation;
        public bool RestPresentationActive => InnRestPresentation != null && InnRestPresentation.IsActive || VillageRestPresentation != null && VillageRestPresentation.IsActive;
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
                if(string.IsNullOrEmpty(FocusedId))return AmbientObjective;
                var point=FindInteractionPoint(FocusedId);
                return point!=null?"[F] "+point.Prompt:AmbientObjective;
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
        public bool InitializationComplete {get;private set;}
        public bool GameplayInputBlocked=>Time.timeScale<=.0001f||(RuntimeState!=null&&RuntimeState.InputBlocked);
        [Inject] public void Construct(InkPool ink,GroggyMeter groggy,ParryJudge parry){this.ink=ink;this.groggy=groggy;this.parry=parry;}
        void Start()
        {
            if(Content==null||Content.TestRules==null||Walker==null||ink==null)throw new InvalidOperationException("Macro playtest wiring is incomplete");
            BindActorsToLoadedNavigation();
            BindOpeningPoints();
            vitals=Walker.Body.GetComponent<PlayerVitals>();
            BindDemoBosses();
            BindDemoSouthGate();
            store=new AtomicJsonStore<WorldMacroProgress>(Path.Combine(Application.persistentDataPath,Content.SaveSlot+TestSaveSuffix+".json"),WorldMacroProgress.Valid);
#if UNITY_EDITOR
            mum295IsolatedStore=TestSaveSuffix=="_mum295_test"||
                (TestSaveSuffix=="_mum296_test"&&Content.SaveSlot=="world-compact-architecture-296"&&
                 gameObject.scene.path=="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity");
#endif
            var loaded=store.Load();
            LoadStatus=store.LoadStatus;
            if(LoadStatus=="primary"&&loaded!=null&&loaded.version==WorldMacroProgress.CurrentVersion)lastSuccessfulSnapshot=JsonUtility.ToJson(loaded);
            bool migrated=loaded!=null&&loaded.version<WorldMacroProgress.CurrentVersion;
            saveBlockedByInvalidLoad=LoadStatus=="invalid";
            Progress=WorldMacroProgress.MigrateToCurrent(loaded)??CreateFreshProgress();
            if(!TryNormalizeDemoEscortReload(out string escortReloadError))throw new InvalidOperationException(escortReloadError);
            RepairProgress();Teleport(Progress.ledger.position,Progress.ledger.yaw);
            InitializeDemoEconomy();
            BindDemoRen();
            BindDemoField();
            BindMumBridges();
            if(EABuffProfile!=null)Walker.Wiring.ConfigureEABuffs(EABuffProfile,()=>ready&&isActiveAndEnabled&&HasDemoGuk);
            if(EAWardProfile!=null)Walker.Wiring.ConfigureEAWards(EAWardProfile);
            if(EAGiyeokProfile!=null)Walker.Wiring.ConfigureEAGiyeok(EAGiyeokProfile,()=>ready&&isActiveAndEnabled&&HasDemoGuk);
            vitals.Restore(Progress.ledger.hp>0?Progress.ledger.hp:1);ink.Restore(Progress.ledger.ink);
            foreach(var actor in Actors){actor.Defeated+=EnemyDefeated;if(Progress.defeated.Contains(actor.Id))actor.GetComponent<EnemyVitals>().TakeDamage(float.MaxValue);}
            vitals.Died+=OnDeath;ready=true;InitializeEquipment();nextSave=Time.unscaledTime+Content.TestRules.AutosaveSeconds;RefreshDrop();RefreshFragmentPickups();
            BindDemoEscort();
            if(LoadStatus=="invalid")SaveError="저장 파일이 손상되었다. 제목 화면에서 백업을 복구하거나 보관 후 새로 시작한다.";
            else if(LoadStatus=="backup")Show("이전 백업에서 진행을 복구했다.");
            else if(LoadStatus=="temporary")Show("중단된 저장 파일에서 진행을 복구했다.");
            if((migrated||LoadStatus=="temporary"||LoadStatus=="new"&&OpeningJourneyActive)&&!saveBlockedByInvalidLoad)SaveNow(out _);
            InitializationComplete=true;
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
            if(!WorldMacroCheckpointRules.TryResolve(Content,Progress,l.checkpoint,out _))l.checkpoint="mine_start";
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
                if(Traversal!=null&&!Traversal.IsPermanentDrySupport(h.point,h.collider))continue;
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
            if(Oheangbu.App.World.UI.PlaytestUiRoot.Instance?.LoadingInProgress==true)return;
            if(!ready||respawning)return;
            FlushPendingDefeats();
            if(encounterDeathPending){if(!HasPendingDefeats){encounterDeathPending=false;OnDeath();}return;}
            TickActShortcuts();
            TickActEntrance();
            TickDemoEscort();
            if(Time.unscaledTime>=nextSave){Save();nextSave=Time.unscaledTime+Content.TestRules.AutosaveSeconds;}
            if(GameplayInputBlocked){FocusedId=null;FocusedCollectionBundleId=null;return;}
            if(TickTraversal())return;
            if(Traversal==null&&!Walker.Seated){
                if(lastSafe.y-Walker.Body.transform.position.y>=Content.TestRules.FallDeathHeight){vitals.ApplyFatalFall();return;}
                if(Walker.Body.isGrounded&&TrySafeFeet(Walker.Body.transform.position,out var safe))lastSafe=safe;
            }
            if(Time.unscaledTime>=nextCull){Cull();nextCull=Time.unscaledTime+.2f;}
            FocusedId=null;FocusedCollectionBundleId=null;float closest=float.MaxValue;
            if(Walker.Seated||Walker.Motor.IsDrawing)return;
            foreach(var p in InteractionPoints)if(CanInteract(p.Id)){float distance=Vector3.Distance(FindInteractionPoint(p.Id).Position,Walker.Body.transform.position);if(distance<closest){FocusedId=p.Id;closest=distance;}}
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
        // Resolve from the same focused interaction that consumes F, including live NPC positions.
        public bool TryGetFocusedInteractionPosition(out Vector3 position)
        {
            position = default;
            if (vitals == null || vitals.Hp01 <= 0 || !CanUseInteractionInput()) return false;
            if (!string.IsNullOrEmpty(FocusedCollectionBundleId))
            {
                var pickup = fragmentPickups.Find(p => p != null && p.BundleId == FocusedCollectionBundleId);
                if (!CanInteract(pickup)) return false;
                position = pickup.InteractionPosition;
                return true;
            }
            if (string.IsNullOrEmpty(FocusedId) || !CanInteract(FocusedId)) return false;
            var point = FindInteractionPoint(FocusedId);
            position = FocusedId == "CurrencyDrop" ? Progress.ledger.dropPosition : point.Position;
            return true;
        }

        public bool CanInteract(string id)
        {
            if(!ready||HasPendingDefeats||GameplayInputBlocked||Walker.Seated||!Walker.Motor.enabled||Walker.Motor.IsDrawing||Walker.Drawing.InDrawMode)return false;
            var p=FindInteractionPoint(id);
            bool lost=id=="CurrencyDrop"&&Progress.ledger.dropCurrency>0;
            if(p==null&&!lost)return false;
            if(!lost&&ShortcutDoorOpened(id))return false;
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
            if(id=="CurrencyDrop"){
                Vector3 dropPoint=Progress.ledger.dropPosition;
                if(!TryPrepareCurrencyRecovery(Progress,out var recovery,out int amount,out string recoveryError)||!TryCommitInteraction(recovery,out recoveryError))
                {Show(recoveryError);return false;}
                RefreshDrop();Show("조선통보 "+amount+" 회수");InteractionResolved?.Invoke(PrologueInteractionKind.Currency,dropPoint);return true;
            }
            var p=FindInteractionPoint(id);
            if(TryHandleShortcutDoor(p,out bool doorHandled))return doorHandled;
            if(TryHandleMountainInteraction(p,out bool mountainHandled))return mountainHandled;
            if(TryVillageService(id))return EquipmentReady;
            if(p.Kind!=PrologueInteractionKind.Rest&&TryHandleDemoInteraction(p,out bool handled))return handled;
            if(p.Kind==PrologueInteractionKind.Rest)return Rest(p);
            if(!TryPreparePointInteraction(Progress,p,out var proposal,out bool first,out bool newRecord,out string error)||!TryCommitInteraction(proposal,out error))
            {Show(error);return false;}
            // One text surface per interaction (SPEC-PLAYTEST-TEXT-DIET): readable dialogue/evidence
            // routes to the detail folio via Present; short receipts stay in the HUD prompt.
            if(id==WorldMacroOpeningProfileSO.CommissionId)Show("폐광 폭파 조사 의뢰를 확인했다.");
            else if(p.Kind==PrologueInteractionKind.Currency&&!first)Show("이미 챙긴 물품이다.");
            else if(p.Kind==PrologueInteractionKind.Evidence||p.Kind==PrologueInteractionKind.Conversation)Present(p.Prompt,p.Text+(first&&p.Currency>0&&!IconPresentation?"\n조선통보 +"+p.Currency:""));
            else Show(p.Text+(first&&p.Currency>0&&!IconPresentation?"\n조선통보 +"+p.Currency:""));
            InteractionResolved?.Invoke(p.Kind,p.Position);
            if(newRecord)Debug.Log("[WorldMacroPlaytest] Record discovered for interaction: "+id);
            return true;
        }
        // Detached proposals keep failed writes from consuming a one-time interaction or currency drop.
        static bool TryPreparePointInteraction(WorldMacroProgress source,PrologueContentSO.Point point,out WorldMacroProgress proposal,
            out bool first,out bool newRecord,out string error)
        {
            proposal=null;first=false;newRecord=false;error=null;
            if(!WorldMacroProgress.Valid(source)||point==null||string.IsNullOrEmpty(point.Id))
            {error="상호작용 진행 데이터를 확인할 수 없다.";return false;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            first=!candidate.ledger.completed.Contains(point.Id);
            if(first&&Math.Max(0,point.Currency)>int.MaxValue-candidate.ledger.currency)
            {error="통보 보유량을 확인한 뒤 다시 시도한다.";return false;}
            PrologueProgressStore.Complete(candidate.ledger,point.Id,point.Currency);
            newRecord=WorldMacroCollectionCatalog.TryGetRecordForInteraction(point.Id,out var record)&&candidate.ui.AddRecord(record.Id);
            proposal=candidate;return true;
        }
        static bool TryPrepareCurrencyRecovery(WorldMacroProgress source,out WorldMacroProgress proposal,out int amount,out string error)
        {
            proposal=null;amount=0;error=null;
            if(!WorldMacroProgress.Valid(source)||source.ledger.dropCurrency<=0)
            {error="회수할 통보가 없다.";return false;}
            if(source.ledger.dropCurrency>int.MaxValue-source.ledger.currency)
            {error="통보 보유량을 확인한 뒤 다시 시도한다.";return false;}
            var candidate=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(source)));
            amount=PrologueProgressStore.Retrieve(candidate.ledger);proposal=candidate;return true;
        }
        bool TryCommitInteraction(WorldMacroProgress proposal,out string error,bool preserveRecoveryPose=false)
        {
            if(!ready){error="저장 세션이 아직 준비되지 않았다.";return false;}
            if(SaveBlocked){error=SaveError??"손상된 저장 원본을 먼저 복구해야 한다.";return false;}
            var ledger=proposal.ledger;
            if(!preserveRecoveryPose){ledger.position=lastSafe;ledger.hasPosition=true;
            if(Walker!=null&&Walker.Body!=null)ledger.yaw=Walker.Body.transform.eulerAngles.y;
            if(vitals!=null)ledger.hp=vitals.Hp01;
            if(ink!=null)ledger.ink=ink.Value;}
            string snapshot=JsonUtility.ToJson(proposal);
            try
            {
                if(RequiresSnapshotWrite(lastSuccessfulSnapshot,snapshot))store.Save(proposal);
                // No successful event, UI notice, or live progress mutation precedes durable acceptance.
                Progress=proposal;lastSuccessfulSnapshot=snapshot;SaveError=null;error=null;return true;
            }
            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||exception is ArgumentException)
            {SaveError="저장 실패: "+exception.Message;error=SaveError;return false;}
        }
        public void EnemyDefeated(string id)
        {
            if(!ready||!Content.Encounters.Any(e=>e.Id==id))return;
            if(TryHandleDemoSouthGateDefeat(id))return;
            if(TryHandleDemoBossDefeat(id))return;
            QueueEncounterDefeat(id);
        }
        void ResetCombat()
        {
            Walker.Drawing.enabled=false;Walker.Drawing.enabled=true;
            Walker.Wiring.enabled=false;Walker.Wiring.enabled=true;
            groggy.Reset();parry.ClearGuard();
            var target=Walker.Motor.GetComponent<LockOn>();if(target!=null&&target.IsLocked)target.Toggle();
            foreach(var actor in Actors){
                var spec=Array.Find(Content.Encounters,x=>x.Id==actor.Id);
                bool livingBoss=(actor.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>()!=null||actor.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>()!=null)&&actor.GetComponent<EnemyVitals>().IsAlive;
                if(!spec.RespawnOnRest&&!livingBoss)continue;
                Progress.defeated.Remove(actor.Id);actor.gameObject.SetActive(true);actor.GetComponent<NavMeshAgent>().enabled=true;actor.enabled=true;actor.ResetEncounter();
            }
            Cull();
        }
        void OnDeath()
        {
            if(!ready||respawning)return;
            FlushPendingDefeats(true);
            if(HasPendingDefeats){encounterDeathPending=true;return;}
            if(TryHandleDemoEscortDeath())return;
            if(Traversal!=null){BeginEnvironmentRecovery();return;}
            if(Walker.Seated)return;respawning=true;
            PrologueProgressStore.Drop(Progress.ledger,lastSafe);ResetCombat();Teleport(Checkpoint(),CheckpointYaw());
            vitals.Restore();ink.Restore();RefreshDrop();respawning=false;Show("마지막 쉼터에서 눈을 떴다. 남긴 통보를 되찾을 수 있다.");Save();
        }
        public void Teleport(Vector3 feet,float yaw)
        {
            var body=Walker.Body;bool prior=body.enabled;body.enabled=false;body.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));body.enabled=prior;Walker.Motor.ResetMotion();lastSafe=feet;ResetTraversal(feet);
            MumBridges?.ResetForWorldBoundary();
        }
        public bool Save()
        {
            return SaveNow(out _);
        }
        public bool SaveNow(out string error)
        {
            if(!ready){error="저장 세션이 아직 준비되지 않았다.";return false;}
            FlushPendingDefeats(true);
            if(HasPendingDefeats){error=SaveError??"처치 결과 저장을 다시 시도하는 중이다.";return false;}
            if(pendingEnvironmentRecovery!=null||escortDeathPending){error=SaveError??"사망 복구 저장을 다시 시도하는 중이다.";return false;}
            if(saveBlockedByInvalidLoad){error=SaveError??"손상된 저장 파일을 먼저 복구하거나 보관해야 한다.";return false;}
            var l=Progress.ledger;if(!RestPresentationActive)l.position=lastSafe;l.hasPosition=true;
            // Scene unload may destroy the player before this component's OnDisable.
            if(!RestPresentationActive&&Walker!=null&&Walker.Body!=null)l.yaw=Walker.Body.transform.eulerAngles.y;
            if(vitals!=null)l.hp=vitals.Hp01;
            if(ink!=null)l.ink=ink.Value;
            var saveCandidate=CreateDemoEscortSaveSnapshot(Progress);
            string snapshot=JsonUtility.ToJson(saveCandidate);
            if(!RequiresSnapshotWrite(lastSuccessfulSnapshot,snapshot)){Progress=saveCandidate;SaveError=null;error=null;return true;}
            try{store.Save(saveCandidate);Progress=saveCandidate;lastSuccessfulSnapshot=snapshot;SaveError=null;error=null;return true;}
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){SaveError="저장 실패: "+e.Message;error=SaveError;Debug.LogError(SaveError);return false;}
        }
        internal static bool RequiresSnapshotWrite(string previousSnapshot,string currentSnapshot)
        {return !string.Equals(previousSnapshot,currentSnapshot,StringComparison.Ordinal);}
        public void Cull()
        {
            if(GameplayInputBlocked)return;
            foreach(var actor in Actors){
                var spec=Array.Find(Content.Encounters,x=>x.Id==actor.Id);var enemyVitals=actor.GetComponent<EnemyVitals>();bool alive=enemyVitals.IsAlive;
                bool available=DemoEncounterAvailable(actor.Id,spec.ContentId);
                if(actor.Id==CheongryongId||actor.Id==SouthGateGeneralId||spec.ContentId==Oheangbu.App.Demo.DemoGrowthLessonLink.LessonId)
                {
                    enemyVitals.enabled=available;
                    foreach(var c in actor.GetComponentsInChildren<Collider>())c.enabled=available&&alive;
                }
                bool active=available&&CombatActive&&!Walker.Seated&&alive&&Vector3.Distance(Walker.Body.transform.position,actor.transform.position)<spec.Activation;
                var attack=actor.GetComponent<EnemyController>();var agent=actor.GetComponent<NavMeshAgent>();
                var boss=actor.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>();
                if(boss!=null){boss.AttackEnabled=active;boss.enabled=active;}
                var general=actor.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>();
                if(general!=null){general.AttackEnabled=active;general.enabled=active;}
                if(!active){attack.AttackEnabled=false;attack.enabled=false;actor.enabled=false;if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;}
                else{actor.enabled=true;attack.enabled=boss==null&&general==null;}
                actor.SetPresentationVisibility(available&&CombatActive&&!Walker.Seated&&Vector3.Distance(Walker.Body.transform.position,actor.transform.position)<spec.Activation);
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
        // One interaction surfaces text in exactly one place (SPEC-PLAYTEST-TEXT-DIET / ArtAudio:53):
        // a short line stays in the achromatic HUD prompt, a long reading (lore, 석경) opens the detail
        // folio. Never both. A newline or a length past one prompt line routes to the detail page.
        const int PromptLineMaxChars=42;
        void Present(string title,string body)
        {
            if(string.IsNullOrEmpty(body))return;
            if(IconPresentation||body.Length>PromptLineMaxChars||body.IndexOf('\n')>=0)DetailRequested?.Invoke(title,body);
            else Show(body);
        }
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
        void OnDisable(){UnbindDemoSouthGate();UnbindDemoEscort();SuspendDemoField();SuspendMumBridges();Save();}
        void OnDestroy(){UnbindDemoSouthGate();UnbindDemoEscort();UnbindDemoField();UnbindMumBridges();UnbindDemoRen();if(vitals!=null)vitals.Died-=OnDeath;foreach(var a in Actors)if(a!=null)a.Defeated-=EnemyDefeated;if(drop!=null)Destroy(drop);if(font!=null)Destroy(font);}
    }
}
