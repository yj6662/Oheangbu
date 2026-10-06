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
        // #306 contract (DialogueRequest306.cs): every NPC utterance goes to the one dialogue surface through this event (track S raises it, track U shows it)
        public event Action<Oheangbu.App.World.UI.DialogueRequest306> DialogueRequested;
        // raised once when a conversation opened through DialogueRequested ends (NPC job actors return to work on it)
        public event Action<string> DialogueEnded;
        bool RaiseDialogue306(Oheangbu.App.World.UI.DialogueRequest306 r){var h=DialogueRequested;if(h==null||r==null)return false;var id=r.SourceId;var closed=r.Closed;r.Closed=()=>{closed?.Invoke();DialogueEnded?.Invoke(id);};h(r);return true;}
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
                    var pickup=FindFragmentPickup(FocusedCollectionBundleId);
                    return pickup!=null?PromptText(pickup.Prompt):null;
                }
                if(FocusedId=="CurrencyDrop")return "[F] 남긴 통보 회수";
                if(string.IsNullOrEmpty(FocusedId))return VehiclePromptText308??AmbientObjective;   // #308 D308-8: "[F] 탑승" / "[F] 하차"
                var point=FindInteractionPoint(FocusedId);
                return point!=null?PromptText(point.Prompt):AmbientObjective;
            }
        }
        // #307 phase 1 item 12: "[F] "+prompt once per distinct prompt string (the same text, no per-frame concatenation).
        string promptSource,promptText;bool promptKnown;
        string PromptText(string prompt)
        {
            if(!promptKnown||!ReferenceEquals(prompt,promptSource)){promptSource=prompt;promptText="[F] "+prompt;promptKnown=true;}
            return promptText;
        }
        // First registered pickup with this bundle (List.Find's order) without a capturing lambda.
        WorldMacroFragmentPickup FindFragmentPickup(string bundleId)
        {
            for(int i=0;i<fragmentPickups.Count;i++){var x=fragmentPickups[i];if(x!=null&&x.BundleId==bundleId)return x;}
            return null;
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
        bool ready,respawning,saveBlockedByInvalidLoad;int canvasHudPresenters;GameObject drop;
        WorldMacroPlaytestSessionHudFallback hudFallback;   // #307 item 12: the former OnGUI prompt, enabled only while no canvas HUD presenter is
        public bool SaveBlocked=>saveBlockedByInvalidLoad;
        public bool InitializationComplete {get;private set;}
        public bool GameplayInputBlocked=>Time.timeScale<=.0001f||(RuntimeState!=null&&RuntimeState.InputBlocked);
        [Inject] public void Construct(InkPool ink,GroggyMeter groggy,ParryJudge parry){this.ink=ink;this.groggy=groggy;this.parry=parry;}
#if UNITY_EDITOR
        /// <summary>#307: an editor-harness save suffix without its harness (SessionState "PlaytestUiReviewSuffix" differs).</summary>
        public static bool StaleHarnessSuffix307(string suffix)
        {
            if(string.IsNullOrEmpty(suffix))return false;
            // timestamped suffixes of the editor harnesses (Harness303 / Map307Capture, Restart307, main-start, ReviewPlay, Vehicle296,
            // Architecture296 runtime checks); fixed test suffixes (_mum296_test, _perf295_test, ...) are not harness leftovers
            string[] harness={"_c303","_r307","_main297_","_review297_","_vehicle296_","_architecture296_"};
            if(!harness.Any(h=>suffix.StartsWith(h,StringComparison.Ordinal)))return false;
            return UnityEditor.SessionState.GetString("PlaytestUiReviewSuffix","")!=suffix;
        }
#endif
        void Start()
        {
            EnsureHudFallback();
            if(Content==null||Content.TestRules==null||Walker==null||ink==null)throw new InvalidOperationException("Macro playtest wiring is incomplete");
            BindActorsToLoadedNavigation();
            BindOpeningPoints();
            vitals=Walker.Body.GetComponent<PlayerVitals>();
            BindDemoBosses();
            BindDemoSouthGate();
            BindMineTutorial306();   // #306 #11
#if UNITY_EDITOR
            // #307 (user 2026-10-01 "새 게임 시작해도 이어하기로 들어가고"): Play-exit restores the open scene from its pre-Play backup, which
            // brings back an editor harness suffix (_c303 / _r307_ / _main297_) the harness had already cleaned up. A player's lobby run then
            // loaded and saved the harness slot while the title inspected the real one, so 새 게임 archived an empty slot and the session
            // resumed. A harness suffix counts only while its harness is running (every such harness mirrors it into the UI suffix key).
            if(StaleHarnessSuffix307(TestSaveSuffix)){Debug.LogWarning("[WorldMacroPlaytest] ignored a stale editor-harness save suffix '"+TestSaveSuffix+"' (no harness running); using the real slot "+Content.SaveSlot);TestSaveSuffix="";}
#endif
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
            BindMumBridges();BindPerf307();
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
            ApplySealRelocation308();   // #308 1b′: a save beyond the seal without the south-gate fact moves to the nearest EA rest
        }
        public bool TrySafeFeet(Vector3 candidate,out Vector3 feet)
        {
            feet=default;
            using(Perf307Markers.SessionSafeFeet.Auto()){if(!float.IsFinite(candidate.x)||!float.IsFinite(candidate.y)||!float.IsFinite(candidate.z))return false;
            if(candidate.x<PreviewSheetBoundsMin.x||candidate.x>PreviewSheetBoundsMax.x||candidate.z<PreviewSheetBoundsMin.y||candidate.z>PreviewSheetBoundsMax.y)return false;
            bool away=ProbeNaturalSolids(candidate);   // #306: trunks at the candidate must exist before the overlap test
            try{return SafeFeetCore(candidate,out feet);}
            finally{if(away)RestoreNaturalFocus();}}
        }
        // #307 phase 1 item 5: the former loop without allocation. Hits are ordered by a stable insertion sort on distance (the
        // order LINQ OrderBy gave, same float.CompareTo keys); the overlap is a set test; a full buffer repeats the array query.
        readonly RaycastHit[] safeFeetHits=new RaycastHit[32];
        readonly Collider[] safeFeetOverlap=new Collider[32];
        static readonly Vector3[] SafeFeetSupportOffsets={Vector3.right*.25f,Vector3.left*.25f,Vector3.forward*.25f,Vector3.back*.25f};
        bool SafeFeetCore(Vector3 candidate,out Vector3 feet)
        {
            feet=default;
            var hits=safeFeetHits;int n=Physics.RaycastNonAlloc(candidate+Vector3.up*1.5f,Vector3.down,hits,4,1,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length){hits=Physics.RaycastAll(candidate+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore);n=hits.Length;}
            for(int i=1;i<n;i++){var h=hits[i];int j=i-1;while(j>=0&&hits[j].distance.CompareTo(h.distance)>0){hits[j+1]=hits[j];j--;}hits[j+1]=h;}
            var self=Walker.Body.transform;
            for(int i=0;i<n;i++){
                var h=hits[i];
                if(Traversal!=null&&!Traversal.IsPermanentDrySupport(h.point,h.collider))continue;
                if(h.transform.IsChildOf(self)||h.normal.y<Mathf.Cos(Walker.Body.slopeLimit*Mathf.Deg2Rad))continue;
                var p=h.point+Vector3.up*.05f;
                if(SafeFeetBlocked(p,self))continue;
                bool supported=true;
                foreach(var offset in SafeFeetSupportOffsets)
                    supported&=Physics.Raycast(p+offset+Vector3.up*.3f,Vector3.down,.65f,1,QueryTriggerInteraction.Ignore);
                if(supported){feet=p;return true;}
            }
            return false;
        }
        bool SafeFeetBlocked(Vector3 p,Transform self)
        {
            var overlap=safeFeetOverlap;int n=Physics.OverlapCapsuleNonAlloc(p+Vector3.up*.31f,p+Vector3.up*1.47f,.27f,overlap,~0,QueryTriggerInteraction.Ignore);
            if(n>=overlap.Length){overlap=Physics.OverlapCapsule(p+Vector3.up*.31f,p+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore);n=overlap.Length;}
            for(int i=0;i<n;i++)if(!overlap[i].transform.IsChildOf(self))return true;
            return false;
        }
        public Vector2 PreviewSheetBoundsMin=new Vector2(-4500,-4200),PreviewSheetBoundsMax=new Vector2(4500,8200);
        void Update()
        {
            CollectAutosave();   // #307 item 3: a finished background autosave reports here (SaveError on failure, as before)
            if(Oheangbu.App.World.UI.PlaytestUiRoot.Instance?.LoadingInProgress==true){DisarmInteract();return;}
            if(!ready||respawning){DisarmInteract();return;}
            FlushPendingDefeats();
            if(encounterDeathPending){DisarmInteract();if(!HasPendingDefeats){encounterDeathPending=false;OnDeath();}return;}
            TickActShortcuts();
            TickActEntrance();
            TickDemoEscort();
            TickMineTutorial306();   // #306 #11: cards wait for CanShowCard (no drawing, hitstop, menu)
            if(Time.unscaledTime>=nextSave){SaveCore(out _,true);nextSave=Time.unscaledTime+Content.TestRules.AutosaveSeconds;}
            if(GameplayInputBlocked){FocusedId=null;FocusedCollectionBundleId=null;vehicleFocus308=0;DisarmInteract();return;}
            TrackInteractArm();
            if(TickDialogueRest306())return;   // #306: a Rest row picked in the talk menu rests once the view has closed
            if(TickTraversal())return;
            if(Traversal==null&&!Walker.Seated){
                if(lastSafe.y-Walker.Body.transform.position.y>=Content.TestRules.FallDeathHeight){vitals.ApplyFatalFall();return;}
                if(Walker.Body.isGrounded&&TrySafeFeet(Walker.Body.transform.position,out var safe))lastSafe=safe;
            }
            if(Time.unscaledTime>=nextCull){Cull();nextCull=Time.unscaledTime+.2f;}
            using(Perf307Markers.SessionFocus.Auto()){FocusedId=null;FocusedCollectionBundleId=null;vehicleFocus308=0;
            if(Walker.Seated){TickSeatedVehicle308();return;}   // #308 D308-8: F exits the stopped car (fresh press only)
            if(Walker.Motor.IsDrawing)return;
            ResolveFocus(Walker.Body.transform.position,out var focusedId,out var focusedBundle);FocusedId=focusedId;FocusedCollectionBundleId=focusedBundle;
            // #308 D308-8: the boardable car takes F when its seat is nearer than the focused point / pickup
            if(ResolveVehicleFocus308(Walker.Body.transform.position)){if(TakeFreshInteractPress(VehicleInteractTarget308))BoardFocusedVehicle308();return;}}
            string target=!string.IsNullOrEmpty(FocusedCollectionBundleId)?FocusedCollectionBundleId:FocusedId;
            if(target!=null&&TakeFreshInteractPress(target))   // #306 §2-2: fresh press only, never the [F] that closed the last modal
            {
                if(!string.IsNullOrEmpty(FocusedCollectionBundleId))TryCollectFragmentBundle(FocusedCollectionBundleId,out _);
                else Interact(FocusedId);
            }
        }
        // The former Update loop from an explicit body position (Perf307Checks compares it with the pre-#307 loop). The gate is
        // read once (nothing in the loop changes it); points use geometry only (no view objects); the first Id wins as before.
        void ResolveFocus(Vector3 body,out string focusedId,out string focusedBundle)
        {
            focusedId=null;focusedBundle=null;float closest=float.MaxValue;bool open=InteractionGateOpen();
            if(open)
                foreach(var p in InteractionPoints)if(CanInteractAt(body,p.Id)){TryInteractionGeometry(p.Id,out var at,out _);float distance=Vector3.Distance(at,body);if(distance<closest){focusedId=p.Id;closest=distance;}}
            if(open&&CanInteractAt(body,"CurrencyDrop")){focusedId="CurrencyDrop";closest=Vector3.Distance(Progress.ledger.dropPosition,body);}
            foreach(var pickup in fragmentPickups)
            {
                if(!CanInteractAt(body,pickup))continue;float distance=Vector3.Distance(pickup.InteractionPosition,body);
                if(distance<closest){focusedId=null;focusedBundle=pickup.BundleId;closest=distance;}
            }
        }
        // Resolve from the same focused interaction that consumes F, including live NPC positions.
        public bool TryGetFocusedInteractionPosition(out Vector3 position)
        {
            position = default;
            if (vitals == null || vitals.Hp01 <= 0 || !CanUseInteractionInput()) return false;
            if (!string.IsNullOrEmpty(FocusedCollectionBundleId))
            {
                var pickup = FindFragmentPickup(FocusedCollectionBundleId);
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
            if(!InteractionGateOpen())return false;
            return CanInteractAt(Walker.Body.transform.position,id);
        }
        bool InteractionGateOpen()=>ready&&!HasPendingDefeats&&!GameplayInputBlocked&&!Walker.Seated&&Walker.Motor.enabled&&!Walker.Motor.IsDrawing&&!Walker.Drawing.InDrawMode;
        // #307 phase 1 item 2: CanInteract after its gate, cheapest test first (every test was already required, so the answer is
        // unchanged): geometry (no view object) -> distance -> ShortcutDoorOpened -> line of sight (RaycastNonAlloc; any foreign hit
        // blocks, so hit order is irrelevant; a full buffer repeats the query with RaycastAll).
        readonly RaycastHit[] sightHits=new RaycastHit[32];
        bool CanInteractAt(Vector3 body,string id)
        {
            bool lost=id=="CurrencyDrop"&&Progress.ledger.dropCurrency>0;
            bool known=TryInteractionGeometry(id,out var target,out float radius);
            if(!known&&!lost)return false;
            if(!lost&&!string.IsNullOrEmpty(id))EnsureShortcutDoors();   // the door list is still first gathered on this call, as before
            if(lost){target=Progress.ledger.dropPosition;radius=2.5f;}
            if(Vector3.Distance(body,target)>radius)return false;
            if(!lost&&ShortcutDoorOpened(id))return false;
            Vector3 eye=body+Vector3.up*Walker.EyeHeight,to=target+Vector3.up*1.25f-eye;
            var self=Walker.Body.transform;
            var hits=sightHits;int n=Physics.RaycastNonAlloc(eye,to.normalized,hits,to.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length){hits=Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore);n=hits.Length;}
            for(int i=0;i<n;i++){
                var h=hits[i];
                if(h.transform.IsChildOf(self))continue;
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
            if(TryHandleCommission(p,out bool commissionHandled))return commissionHandled;
            if(p.Kind!=PrologueInteractionKind.Rest&&TryHandleDemoInteraction(p,out bool handled))return handled;
            if(p.Kind==PrologueInteractionKind.Rest)return Rest(p);
            if(!TryPreparePointInteraction(Progress,p,out var proposal,out bool first,out bool newRecord,out string error)||!TryCommitInteraction(proposal,out error))
            {Show(error);return false;}
            // One text surface per interaction (SPEC-PLAYTEST-TEXT-DIET): evidence routes to the detail folio via Present, NPC speech
            // to the dialogue surface via Speak306 (#306, Present when no surface is bound); short receipts stay in the HUD prompt.
            if(id==WorldMacroOpeningProfileSO.CommissionId)Show("폐광 폭파 조사 의뢰를 확인했다.");
            else if(p.Kind==PrologueInteractionKind.Currency&&!first)Show("이미 챙긴 물품이다.");
            else if(p.Kind==PrologueInteractionKind.Conversation)Speak306(p,p.Prompt,p.Text,first&&p.Currency>0&&!IconPresentation?"조선통보 +"+p.Currency:null);
            else if(p.Kind==PrologueInteractionKind.Evidence)Present(p.Prompt,p.Text+(first&&p.Currency>0&&!IconPresentation?"\n조선통보 +"+p.Currency:""));
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
            DrainAutosave();   // #307 item 3: "saved means committed" - an autosave in flight lands (and reports) first
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
                bool livingBoss=(actor.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>()!=null||actor.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>()!=null||actor.GetComponent<Oheangbu.App.Demo.MineBossController>()!=null)&&actor.GetComponent<EnemyVitals>().IsAlive;
                if(!spec.RespawnOnRest&&!livingBoss)continue;
                Progress.defeated.Remove(actor.Id);actor.gameObject.SetActive(true);actor.GetComponent<NavMeshAgent>().enabled=true;actor.enabled=true;actor.ResetEncounter();
            }
            Cull();
            ResetMineTutorial306();   // #306 #11: the fight starts over; seen cards stay seen
        }
        void OnDeath()
        {
            if(!ready||respawning)return;
            FlushPendingDefeats(true);
            if(HasPendingDefeats){encounterDeathPending=true;return;}
            if(TryHandleDemoEscortDeath())return;
            if(Traversal!=null)
            {
                // #303: a combat death falls and the veil closes first; drowning and fatal falls recover at once as before
                if(vitals.LastEnvironmentDeath==Oheangbu.Combat.EnvironmentDeathCause.None&&TryBeginDeathPresentation(BeginEnvironmentRecovery))return;
                BeginEnvironmentRecovery();return;
            }
            if(Walker.Seated)return;
            if(TryBeginDeathPresentation(CompleteDeathRespawn))return;   // #303: the same respawn, under the veil
            CompleteDeathRespawn();
        }
        public void Teleport(Vector3 feet,float yaw)
        {
            EnsureNaturalSolidsForTeleport(feet);   // #306: death, escort, terrain recovery and inn all arrive here
            var body=Walker.Body;bool prior=body.enabled;body.enabled=false;body.transform.SetPositionAndRotation(feet,Quaternion.Euler(0,yaw,0));body.enabled=prior;Walker.Motor.ResetMotion();lastSafe=feet;ResetTraversal(feet);
            MumBridges?.ResetForWorldBoundary();
        }
        public bool Save()
        {
            return SaveNow(out _);
        }
        public bool SaveNow(out string error)=>SaveCore(out error,false);
        // background: the periodic autosave only. Identical checks, ledger sampling and snapshot; the pretty JSON is made here on the
        // main thread and the file commit runs on the store's writer. Every synchronous path first waits for that writer.
        bool SaveCore(out string error,bool background)
        {
            using(Perf307Markers.SessionSave.Auto()){if(!ready){error="저장 세션이 아직 준비되지 않았다.";return false;}
            if(!background||autosaveStore!=null&&!ReferenceEquals(autosaveStore,store))DrainAutosave();   // a replaced store (checks swap it) never loses a result
            if(deathRespawnPending&&DeathPresentation!=null&&DeathPresentation.IsActive)DeathPresentation.CompleteNow();   // never save a body mid-fall
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
            if(!RequiresSnapshotWrite(autosaveNewest!=null?autosaveNewest.Snapshot:lastSuccessfulSnapshot,snapshot)){Progress=saveCandidate;SaveError=null;error=null;return true;}
            if(background)
            {
                // Serialize throws InvalidDataException for an invalid state exactly where store.Save did; the result arrives in Update.
                var ticket=new AutosaveTicket{Snapshot=snapshot};string json=store.Serialize(saveCandidate);
                store.SaveInBackground(json,ticket);autosaveStore=store;autosaveNewest=ticket;
                Progress=saveCandidate;error=null;return true;
            }
            try{store.Save(saveCandidate);Progress=saveCandidate;lastSuccessfulSnapshot=snapshot;SaveError=null;error=null;return true;}
            catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){SaveError="저장 실패: "+e.Message;error=SaveError;Debug.LogError(SaveError);return false;}}
        }
        internal static bool RequiresSnapshotWrite(string previousSnapshot,string currentSnapshot)
        {return !string.Equals(previousSnapshot,currentSnapshot,StringComparison.Ordinal);}
        // ---- #307 phase 1 item 3: background autosave bookkeeping (main thread only) ----
        sealed class AutosaveTicket{public string Snapshot;}
        AtomicJsonStore<WorldMacroProgress> autosaveStore;   // the store holding an unfinished or unreported autosave
        AutosaveTicket autosaveNewest;                        // newest enqueued autosave not yet reported (the write baseline meanwhile)
        public bool AutosaveInFlight=>autosaveStore!=null;
        // Non-blocking: apply finished results in order (success = the former success bookkeeping, failure = the former SaveError).
        void CollectAutosave()
        {
            var s=autosaveStore;if(s==null||!s.HasBackgroundResults)return;
            while(s.TryTakeBackgroundResult(out var result))ApplyAutosaveResult(result);
            if(!s.BackgroundBusy&&!s.HasBackgroundResults){autosaveStore=null;autosaveNewest=null;}
        }
        // Blocking: TryCommitInteraction, every synchronous save, OnDisable (scene unload), quit and destroy wait for the writer.
        void DrainAutosave()
        {
            var s=autosaveStore;if(s==null)return;
            s.WaitForBackground();
            while(s.TryTakeBackgroundResult(out var result))ApplyAutosaveResult(result);
            autosaveStore=null;autosaveNewest=null;
        }
        void ApplyAutosaveResult(AtomicJsonStore<WorldMacroProgress>.BackgroundResult result)
        {
            var ticket=result.Tag as AutosaveTicket;
            if(ReferenceEquals(ticket,autosaveNewest))autosaveNewest=null;
            var e=result.Error;
            if(e==null){if(ticket!=null)lastSuccessfulSnapshot=ticket.Snapshot;SaveError=null;return;}
            if(e is IOException||e is UnauthorizedAccessException||e is ArgumentException){SaveError="저장 실패: "+e.Message;Debug.LogError(SaveError);}
            else Debug.LogException(e);   // formerly thrown out of Update: logged, SaveError untouched
        }
        // #307 phase 1 items 7/10: Cull reads each actor's spec and components from a cache built once per Actors/Encounters array
        // (a destroyed component is fetched again), writes Behaviour/Collider enabled and SetActive only on a change, and the dragon /
        // general / lesson colliders are on only while that actor is also within its Activation distance (+ margin) - the distance
        // at which SetPresentationVisibility shows it. Nothing beyond it can be hit: player reach is lock-on 20 m, pierce 18 m, cone
        // 10 m against Activation 120-160 m in every content asset (checked 2026-09-30).
        [Tooltip("#307 [TEST] metres added to Activation before the dragon/general/lesson colliders switch off (0 = the visibility distance).")]
        public float BossColliderActivationMargin=0f;
        sealed class CullActor
        {
            public PrologueEncounter Actor;public WorldMacroPlaytestSO.Encounter Spec;
            public EnemyVitals Vitals;public EnemyController Attack;public NavMeshAgent Agent;
            public Oheangbu.App.Demo.CheongryongCombatController Boss;public Oheangbu.App.Demo.SouthGateGeneralController General;
            public bool HasVitals,HasAttack,HasAgent,HasBoss,HasGeneral;
        }
        CullActor[] cullActors;PrologueEncounter[] cullActorsSource;WorldMacroPlaytestSO.Encounter[] cullSpecsSource;
        readonly List<Collider> cullColliders=new List<Collider>();
        CullActor[] CullActors()
        {
            if(cullActors!=null&&ReferenceEquals(cullActorsSource,Actors)&&ReferenceEquals(cullSpecsSource,Content.Encounters)&&cullActors.Length==Actors.Length)return cullActors;
            var list=new CullActor[Actors.Length];
            for(int i=0;i<Actors.Length;i++)list[i]=NewCullActor(Actors[i]);
            cullActors=list;cullActorsSource=Actors;cullSpecsSource=Content.Encounters;return list;
        }
        CullActor NewCullActor(PrologueEncounter actor)=>new CullActor{Actor=actor,Spec=Array.Find(Content.Encounters,x=>x.Id==actor.Id)};   // a null actor throws as before
        // GetComponent once; a destroyed component is fetched again (absent stays absent: nothing adds these at runtime).
        static T CachedComponent<T>(ref T cached,ref bool fetched,Component owner)where T:Component
        {
            if(!fetched||!ReferenceEquals(cached,null)&&cached==null){var c=owner.GetComponent<T>();cached=c==null?null:c;fetched=true;}
            return cached;
        }
        readonly Dictionary<string,WorldMacroContentSheetSO.Entry> previewEntries=new Dictionary<string,WorldMacroContentSheetSO.Entry>(StringComparer.Ordinal);
        WorldMacroContentSheetSO.Entry[] previewEntriesSource;bool previewEntriesHaveNull;WorldMacroContentSheetSO.Entry previewNullIdEntry;
        // Array.Find(PreviewSheet.Entries,x=>x.Id==id) through a first-Id index (a null entry keeps the scan and its throw).
        WorldMacroContentSheetSO.Entry PreviewEntry(string id)
        {
            var entries=PreviewSheet.Entries;
            if(entries==null)return Array.Find(entries,x=>x.Id==id);
            if(!ReferenceEquals(previewEntriesSource,entries))
            {
                previewEntries.Clear();previewEntriesHaveNull=false;previewNullIdEntry=null;
                foreach(var x in entries){if(x==null){previewEntriesHaveNull=true;continue;}if(x.Id==null){if(previewNullIdEntry==null)previewNullIdEntry=x;continue;}if(!previewEntries.ContainsKey(x.Id))previewEntries.Add(x.Id,x);}
                previewEntriesSource=entries;
            }
            if(previewEntriesHaveNull)return Array.Find(entries,x=>x.Id==id);
            if(id==null)return previewNullIdEntry;
            return previewEntries.TryGetValue(id,out var entry)?entry:null;
        }
        public void Cull()
        {
            using(Perf307Markers.SessionCull.Auto()){if(GameplayInputBlocked)return;
            var actors=CullActors();Vector3 body=Walker.Body.transform.position;
            for(int i=0;i<actors.Length;i++){
                var a=actors[i];if(!ReferenceEquals(a.Actor,Actors[i]))a=actors[i]=NewCullActor(Actors[i]);var actor=a.Actor;
                var spec=a.Spec;var enemyVitals=CachedComponent(ref a.Vitals,ref a.HasVitals,actor);bool alive=enemyVitals.IsAlive;
                bool available=DemoEncounterAvailable(actor.Id,spec.ContentId);
                float distance=Vector3.Distance(body,actor.transform.position);
                if(actor.Id==CheongryongId||actor.Id==SouthGateGeneralId||spec.ContentId==Oheangbu.App.Demo.DemoGrowthLessonLink.LessonId)
                {
                    if(enemyVitals.enabled!=available)enemyVitals.enabled=available;
                    bool solid=available&&alive&&distance<spec.Activation+Mathf.Max(0,BossColliderActivationMargin);
                    actor.GetComponentsInChildren(false,cullColliders);   // the same active-object set as GetComponentsInChildren<Collider>()
                    foreach(var c in cullColliders)if(c.enabled!=solid)c.enabled=solid;
                    cullColliders.Clear();
                }
                else SeatGhost308(actor,enemyVitals,alive);   // #308 KE6: an ordinary (non-boss) enemy is not a solid capsule on the road while the player rides; off unless the scene's session switch is on (WorldMacroPlaytestSession.SeatGhost308.cs)
                bool active=available&&CombatActive&&!Walker.Seated&&alive&&distance<spec.Activation;
                var attack=CachedComponent(ref a.Attack,ref a.HasAttack,actor);var agent=CachedComponent(ref a.Agent,ref a.HasAgent,actor);
                var boss=CachedComponent(ref a.Boss,ref a.HasBoss,actor);
                if(boss!=null){boss.AttackEnabled=active;if(boss.enabled!=active)boss.enabled=active;}
                var general=CachedComponent(ref a.General,ref a.HasGeneral,actor);
                if(general!=null){general.AttackEnabled=active;if(general.enabled!=active)general.enabled=active;}
                if(!active){attack.AttackEnabled=false;if(attack.enabled)attack.enabled=false;if(actor.enabled)actor.enabled=false;if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;}
                else{if(!actor.enabled)actor.enabled=true;bool attacks=boss==null&&general==null;if(attack.enabled!=attacks)attack.enabled=attacks;}
                actor.SetPresentationVisibility(available&&CombatActive&&!Walker.Seated&&distance<spec.Activation);
            }
            foreach(var point in PreviewPoints){if(point==null||point.Visual==null||point.CombatConnected)continue;
                var entry=PreviewEntry(point.Id);
                if(entry!=null){bool show=ShowUnconnectedPreviewMarkers&&Vector3.Distance(body,point.transform.position)<entry.ActivationDistance;var go=point.Visual.gameObject;if(go.activeSelf!=show)go.SetActive(show);}
            }}
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
            return CanInteractAt(Walker.Body.transform.position,pickup);
        }
        bool CanInteractAt(Vector3 body,WorldMacroFragmentPickup pickup)
        {
            if(pickup==null||!pickup.isActiveAndEnabled||!CanUseInteractionInput())return false;
            if(!WorldMacroCollectionCatalog.TryGetBundle(pickup.BundleId,out var bundle)||WorldMacroInventoryService.IsBundleCollected(Progress.ui,bundle))return false;
            Vector3 target=pickup.InteractionPosition;
            if(Vector3.Distance(body,target)>pickup.Radius)return false;
            Vector3 eye=body+Vector3.up*Walker.EyeHeight,to=target+Vector3.up*.5f-eye;
            var self=Walker.Body.transform;
            var hits=sightHits;int n=Physics.RaycastNonAlloc(eye,to.normalized,hits,to.magnitude,~0,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length){hits=Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore);n=hits.Length;}
            for(int i=0;i<n;i++)
            {
                var hit=hits[i];
                if(hit.transform.IsChildOf(self)||hit.transform.IsChildOf(pickup.transform))continue;
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
        {canvasHudPresenters=Mathf.Max(0,canvasHudPresenters+(active?1:-1));SyncHudFallback();}
        // #307 phase 1 item 12: the former OnGUI lives on WorldMacroPlaytestSessionHudFallback (same text, style and rect), added in
        // Play and enabled only while no canvas HUD presenter is registered, so a canvas HUD no longer pays IMGUI every frame.
        void EnsureHudFallback()
        {
            if(!Application.isPlaying||hudFallback!=null)return;
            hudFallback=GetComponent<WorldMacroPlaytestSessionHudFallback>();
            if(hudFallback==null){hudFallback=gameObject.AddComponent<WorldMacroPlaytestSessionHudFallback>();hudFallback.hideFlags=HideFlags.DontSave;}
            hudFallback.Session=this;SyncHudFallback();
        }
        void SyncHudFallback(){if(hudFallback!=null){bool on=isActiveAndEnabled&&!CanvasHudPresenterActive;if(hudFallback.enabled!=on)hudFallback.enabled=on;}}
        void OnApplicationQuit(){Save();DrainAutosave();}
        void OnDisable(){DisarmInteract();UnbindDemoSouthGate();UnbindDemoEscort();SuspendDemoField();SuspendMumBridges();Save();DrainAutosave();if(hudFallback!=null)hudFallback.enabled=false;}
        void OnDestroy(){UnbindMineTutorial306();UnbindVehicle308();DrainAutosave();UnbindDemoSouthGate();UnbindDemoEscort();UnbindDemoField();UnbindMumBridges();UnbindDemoRen();if(vitals!=null)vitals.Died-=OnDeath;foreach(var a in Actors)if(a!=null)a.Defeated-=EnemyDefeated;if(drop!=null)Destroy(drop);}
    }
}
