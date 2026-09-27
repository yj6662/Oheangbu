using System;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.Demo
{
    public enum DemoEscortPresentationState { Waiting, FetchingCargo, Staging, Following, InspectionWait, BoardingPending, Riding, ExitPending, Delivered, Blocked }

    // Uses existing standing proxies. No animation-complete character or fitted seated pose is claimed.
    [DefaultExecutionOrder(250), DisallowMultipleComponent]
    public sealed class DemoEscortPresentation : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public DemoEscortSceneRoute Route;
        public DemoEscortPresentationState State { get; private set; }
        public string LastIssue { get; private set; }
        public Collider LastBlockingCollider { get; private set; }
        public Vector3 LastBlockedPoint { get; private set; }
        public const string VisualStatus = "TEST: existing standing NPC proxy; seated pose and character motion unfinished";
        public const bool SupportsGroundSeamWalker = true;
        public bool PendingAttachment => pendingBoard;
        // The escort observes the shared car, but owns boarding only during its departure/journey.
        public bool RequiresEscortBoarding
        {
            get
            {
                if(Session==null||!Session.DemoEscortReady||!Session.DemoCampaignActive)return false;
                var snapshot=Session.EscortSnapshot;
                return snapshot!=null&&OwnsEscortStage(snapshot.Stage);
            }
        }
        bool OwnsEscortStage(DemoEscortStage stage)=>
            (stage==DemoEscortStage.Contracted&&CurrentStart())||
            (stage>=DemoEscortStage.Escorting&&stage<DemoEscortStage.Delivered);
        public bool CargoCarried => cargo != null && carrySocket != null && cargo.IsChildOf(carrySocket);
        public bool TryCaptureWalkingFeet(out Vector3 feet)
        {
            feet=default;
            if(!bound||pendingBoard||exitPending||seat.Occupied||!Alive()||companion==null||cargo==null||
                !companion.gameObject.activeInHierarchy||!cargo.gameObject.activeInHierarchy||
                companion.IsChildOf(seat.Vehicle.transform)||Vector3.Distance(companion.position,Session.Walker.Body.transform.position)>32||
                Vector3.Distance(companion.position,cargo.position)>2||agent==null||!agent.enabled||!agent.isOnNavMesh||
                !SafeNav(companion.position,.2f,out var support)||Vector3.Distance(support,companion.position)>.22f)return false;
            feet=companion.position;return true;
        }

        Transform companion,cargo,carrySocket;
        WorldMacroPalanquinSeat seat;
        PlayerVitals vitals;
        NavMeshAgent agent;
        Actor originalCompanion,originalCargo;
        Pose boardingCompanion,boardingCargo;
        bool bound,initialized,pendingBoard,exitPending,failedBoard,createdAgent;
        float combatUntil,nextPath;
        bool stagingApproachReached;
        NavMeshPath path;
        struct Pose
        {
            public Transform parent; public Vector3 position,scale; public Quaternion rotation;
            public static Pose Capture(Transform t)=>new Pose{parent=t.parent,position=t.position,rotation=t.rotation,scale=t.localScale};
            public void Restore(Transform t){t.SetParent(parent,true);t.SetPositionAndRotation(position,rotation);t.localScale=scale;}
        }
        sealed class Actor
        {
            public Pose pose;public Collider[] colliders;public bool[] enabled;
            public Actor(Transform t){pose=Pose.Capture(t);colliders=t.GetComponentsInChildren<Collider>(true);enabled=colliders.Select(c=>c.enabled).ToArray();}
            public void Collision(bool restore){for(int i=0;i<colliders.Length;i++)if(colliders[i]!=null)colliders[i].enabled=restore&&enabled[i];}
        }
        public bool Configure(WorldMacroPlaytestSession session,DemoEscortSceneRoute route)
        {
            if(Application.isPlaying && bound)return false;
            Session=session;Route=route;return ReferencesReady();
        }
        bool ReferencesReady()=>Session!=null&&Route!=null&&Route.gameObject.scene==Session.gameObject.scene&&Session.DemoEscortCompanion!=null&&
            Session.DemoEscortCargo!=null&&Session.DemoEscortSeat!=null&&Session.DemoEscortPassengerSocket!=null&&Session.DemoEscortCargoSocket!=null&&Session.Walker?.Body!=null;
        void OnEnable(){if(Application.isPlaying)Bind();}
        void Start(){if(Application.isPlaying&&!bound)Bind();}
        void Bind()
        {
            if(bound||!ReferencesReady())return;
            // NavMeshPath owns native data; Unity forbids constructing it during scene deserialization.
            path ??= new NavMeshPath();
            companion=Session.DemoEscortCompanion;cargo=Session.DemoEscortCargo;seat=Session.DemoEscortSeat;
            if(originalCompanion==null)
            {
                originalCompanion=new Actor(companion);originalCargo=new Actor(cargo);
                agent=companion.GetComponent<NavMeshAgent>();createdAgent=agent==null;
                if(createdAgent)agent=companion.gameObject.AddComponent<NavMeshAgent>();
                agent.enabled=false;agent.height=1.7f;agent.radius=.27f;agent.baseOffset=0;agent.speed=4.5f;agent.acceleration=10;
                agent.angularSpeed=360;agent.stoppingDistance=.45f;agent.autoBraking=true;agent.autoTraverseOffMeshLink=false;
                agent.updatePosition=false;agent.updateRotation=false;
                carrySocket=new GameObject("DemoEscort_CarriedCargo").transform;carrySocket.SetParent(companion,false);
                carrySocket.localPosition=new Vector3(0,.55f,-.4f);
            }
            vitals=Session.Walker.Body.GetComponent<PlayerVitals>();
            seat.Boarded+=Boarded;seat.Exited+=Exited;Session.DemoEscortChanged+=Saved;Session.DemoEscortAttempted+=Attempted;
            if(vitals!=null){vitals.Damaged+=Damaged;vitals.Died+=Died;}
            bound=true;
        }
        void OnDisable()
        {
            if(!bound)return;
            bool ownedVehicle=pendingBoard||failedBoard||Loaded();
            StopWalk();if(pendingBoard)RollbackBoard();
            if(Loaded()) {exitPending=true;ReleaseLocally();}
            if(seat!=null){seat.Boarded-=Boarded;seat.Exited-=Exited;if(ownedVehicle&&seat.Occupied)seat.Vehicle.SetDriverPresent(false);}
            if(Session!=null){Session.DemoEscortChanged-=Saved;Session.DemoEscortAttempted-=Attempted;}
            if(vitals!=null){vitals.Damaged-=Damaged;vitals.Died-=Died;}bound=false;
        }
        void OnDestroy()
        {
            // Parent restoration preserves world position; disabling presentation must not send an escort back to the contract site.
            if(companion!=null&&originalCompanion!=null){companion.SetParent(originalCompanion.pose.parent,true);originalCompanion.Collision(true);}
            if(cargo!=null&&originalCargo!=null){cargo.SetParent(originalCargo.pose.parent,true);cargo.localScale=originalCargo.pose.scale;originalCargo.Collision(true);}
            if(createdAgent&&agent!=null)Destroy(agent);if(carrySocket!=null)Destroy(carrySocket.gameObject);
        }
        bool Alive()=>vitals!=null&&vitals.Hp01>0;
        bool CurrentStart()
        {
            if(!Session.DemoCampaignActive)return false;
            var current=DemoCampaignProgression.ForEvent(Session.Content.Campaign,Session.Progress.campaign,
                DemoEventKind.Interaction,"escort_start",Session.Progress.defeated);
            return current!=null&&current.Implemented&&current.TriggerId=="escort_start";
        }
        DemoEscortStop Stop(string id)=>Route.Stops.FirstOrDefault(s=>s!=null&&s.Id==id);
        void Damaged(float amount){if(amount>0)combatUntil=Time.time+6;}
        void Died(){if(!Alive()){StopWalk();if(pendingBoard)RollbackBoard();if(Loaded()){exitPending=true;ReleaseLocally();}}}
        void Update()
        {
            if(!bound){Bind();return;}
            var snapshot=Session.EscortSnapshot;if(!Session.DemoEscortReady||snapshot==null||!Session.DemoCampaignActive)return;
            if(!initialized){initialized=true;InitializeSaved(snapshot);}
            if(!OwnsEscortStage(snapshot.Stage)&&!pendingBoard&&!exitPending&&!Loaded())
            {failedBoard=false;StopWalk();State=snapshot.Stage==DemoEscortStage.Delivered?DemoEscortPresentationState.Delivered:DemoEscortPresentationState.Waiting;return;}
            // Recall may disable the entire vehicle hierarchy. This component lives on the independent route root.
            if(Loaded()&&(!seat.Vehicle.gameObject.activeInHierarchy||!seat.Occupied))exitPending=true;
            if(exitPending){ReleaseLocally();if(exitPending){Brake();return;}}
            if(!Session.isActiveAndEnabled||!Alive()){StopWalk();Brake();return;}
            if(Session.GameplayInputBlocked){StopWalk();if(pendingBoard)Brake();return;}
            if(pendingBoard){Brake();return;}
            if(snapshot.Stage==DemoEscortStage.Delivered){StopWalk();State=DemoEscortPresentationState.Delivered;return;}
            if(snapshot.CompanionMode==DemoEscortCompanionMode.Riding&&Loaded()){StopWalk();State=DemoEscortPresentationState.Riding;return;}
            if(failedBoard&&seat.Occupied){Brake();return;}
            if(Time.time<combatUntil||Session.Walker.Motor!=null&&Session.Walker.Motor.IsDrawing){StopWalk();State=DemoEscortPresentationState.Waiting;return;}
            // The ground seam walker owns physical movement until the existing path reaches its far side.
            // Pause/combat checks above still stop it, preserving the unfinished link rather than completing it.
            if(agent!=null&&agent.enabled&&agent.isOnNavMesh&&agent.isOnOffMeshLink)
            {
                bool nearby=Vector3.Distance(companion.position,Session.Walker.Body.transform.position)<=32;
                agent.isStopped=seat.Occupied||!nearby;
                return;
            }
            if(snapshot.Stage==DemoEscortStage.Contracted&&CurrentStart())
            {
                var start=Stop("escort_start");if(start==null){Block("상경 출발 대기 위치 연결이 없다.");return;}
                if(Vector3.Distance(Session.Walker.Body.transform.position,start.CompanionWait.position)>40&&Vector3.Distance(Session.Walker.Body.transform.position,companion.position)>25){StopWalk();return;}
                if(!EnsureCarriedCargo()){if(string.IsNullOrEmpty(LastIssue))State=DemoEscortPresentationState.FetchingCargo;return;}
                State=DemoEscortPresentationState.Staging;
                if(start.StagingApproach!=null&&!stagingApproachReached)
                {
                    if(Vector3.Distance(companion.position,start.StagingApproach.position)>.65f){Walk(start.StagingApproach.position);return;}
                    stagingApproachReached=true;
                }
                Walk(start.CompanionWait.position);return;
            }
            if(snapshot.Stage<DemoEscortStage.Escorting){StopWalk();State=DemoEscortPresentationState.Waiting;return;}
            if(seat.Occupied){StopWalk();return;}
            if(!EnsureCarriedCargo())return;
            Vector3 player=Session.Walker.Body.transform.position;
            if(Vector3.Distance(player,companion.position)>32){Block("왕소가 뒤에서 기다리고 있다.");return;}
            string next=snapshot.InspectionsCleared==0?"checkpoint_1":snapshot.InspectionsCleared==1?"checkpoint_2":"cargo_delivery";
            var stop=Stop(next);
            if(stop!=null&&Vector3.Distance(player,stop.Interaction.position)<7&&Vector3.Distance(companion.position,stop.CompanionWait.position)<9)
            {State=DemoEscortPresentationState.InspectionWait;Walk(stop.CompanionWait.position);}
            else {State=DemoEscortPresentationState.Following;Walk(player-Session.Walker.Body.transform.forward*2.2f);}
        }
        void LateUpdate()
        {
            if(!bound||agent==null||!agent.enabled||!agent.isOnNavMesh||agent.isStopped||agent.isOnOffMeshLink)return;
            if(Session.GameplayInputBlocked||!Alive()){agent.nextPosition=companion.position;return;}
            Vector3 next=agent.nextPosition,delta=next-companion.position;
            float limit=Mathf.Min(.5f,agent.speed*Mathf.Max(0,Time.deltaTime)*1.25f);
            if(delta.magnitude>limit)next=companion.position+delta.normalized*limit;
            delta=next-companion.position;
            // NavMesh cannot see newly introduced scene colliders; sweep the actual capsule as well.
            if(delta.sqrMagnitude>.000001f)
                foreach(var hit in Physics.CapsuleCastAll(companion.position+Vector3.up*.3f,companion.position+Vector3.up*1.4f,.27f,delta.normalized,delta.magnitude+.02f,~0,QueryTriggerInteraction.Ignore))
                    if(!Ignore(hit.collider)&&hit.normal.y<.65f){LastBlockingCollider=hit.collider;LastBlockedPoint=hit.point;next=companion.position;Block("왕소의 앞길이 막혀 있다.");break;}
            companion.position=next;agent.nextPosition=next;
            Vector3 flat=Vector3.ProjectOnPlane(delta,Vector3.up);if(flat.sqrMagnitude>.0001f)companion.rotation=Quaternion.RotateTowards(companion.rotation,Quaternion.LookRotation(flat),360*Time.deltaTime);
        }
        bool Ignore(Collider c)=>c==null||c.gameObject.scene!=gameObject.scene||c.transform.IsChildOf(companion)||c.transform.IsChildOf(cargo);
        bool ClearAt(Vector3 feet,bool ignoreVehicle)
        {
            return !Physics.OverlapCapsule(feet+Vector3.up*.3f,feet+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>!Ignore(c)&&!(ignoreVehicle&&c.transform.IsChildOf(seat.Vehicle.transform)));
        }
        bool SafeNav(Vector3 near,float radius,out Vector3 feet,bool ignoreVehicle=false)
        {
            feet=default;if(!NavMesh.SamplePosition(near,out var nav,radius,NavMesh.AllAreas)||Mathf.Abs(nav.position.y-near.y)>1.5f)return false;
            foreach(var h in Physics.RaycastAll(nav.position+Vector3.up*1.5f,Vector3.down,3,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            {
                if(Ignore(h.collider)||h.collider.attachedRigidbody!=null||h.collider.transform.IsChildOf(seat.Vehicle.transform)||h.normal.y<.85f)continue;
                if(Mathf.Abs(h.point.y-nav.position.y)>.2f)return false;
                if(Session.Traversal!=null&&!Session.Traversal.IsPermanentDrySupport(h.point,h.collider))return false;
                feet=h.point+Vector3.up*.03f;return ClearAt(feet,ignoreVehicle);
            }
            return false;
        }
        bool EnableAgent()
        {
            if(agent.enabled&&agent.isOnNavMesh)return true;
            if(!SafeNav(companion.position,.4f,out var feet)){Block("왕소가 설 수 있는 연결된 길을 찾지 못했다.");return false;}
            agent.enabled=true;
            if(!agent.Warp(feet)){agent.enabled=false;return false;}
            companion.position=feet;agent.nextPosition=feet;return true;
        }
        void Walk(Vector3 target)
        {
            if(!EnableAgent())return;
            if(Vector3.Distance(target,companion.position)<.65f){StopWalk();LastIssue=null;LastBlockingCollider=null;return;}
            if(Time.time<nextPath)return;nextPath=Time.time+.3f;
            if(!SafeNav(target,1.2f,out var destination)||!agent.CalculatePath(destination,path)||path.status!=NavMeshPathStatus.PathComplete)
            {Block("왕소가 목적지까지 걸어갈 길이 없다.");return;}
            if(!agent.SetPath(path)){Block("왕소의 이동 경로를 적용하지 못했다.");return;}
            agent.isStopped=false;LastIssue=null;
        }
        void StopWalk(){if(agent!=null&&agent.enabled&&agent.isOnNavMesh){agent.isStopped=true;if(!agent.isOnOffMeshLink){agent.ResetPath();agent.nextPosition=companion.position;}}}
        void DisableAgent(){StopWalk();if(agent!=null)agent.enabled=false;}
        void Brake(){if(seat?.Vehicle!=null)seat.Vehicle.StopDriverInputForUi();}
        void Block(string reason){StopWalk();LastIssue=reason;State=DemoEscortPresentationState.Blocked;}
        bool Sight(Vector3 from,Vector3 to,bool ignoreVehicle)
        {
            Vector3 delta=to-from;
            return !Physics.RaycastAll(from,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).Any(h=>!Ignore(h.collider)&&
                !(ignoreVehicle&&h.collider.transform.IsChildOf(seat.Vehicle.transform))&&!h.collider.transform.IsChildOf(Session.Walker.Body.transform));
        }
        bool EnsureCarriedCargo()
        {
            if(CargoCarried)return true;
            if(cargo.IsChildOf(Session.DemoEscortCargoSocket))return false;
            Vector3 delta=cargo.position-companion.position;
            if(delta.magnitude>1.8f){Walk(cargo.position-delta.normalized*1.1f);return false;}
            if(!Sight(companion.position+Vector3.up,cargo.position+Vector3.up*.4f,false)){Block("봉인 화물까지 가는 길이 가려져 있다.");return false;}
            originalCargo.Collision(false);cargo.SetParent(carrySocket,false);cargo.localPosition=Vector3.zero;cargo.localRotation=Quaternion.identity;cargo.localScale=originalCargo.pose.scale*.55f;return true;
        }
        bool Loaded()=>companion!=null&&cargo!=null&&seat!=null&&Session.DemoEscortPassengerSocket!=null&&Session.DemoEscortCargoSocket!=null&&
            companion.IsChildOf(Session.DemoEscortPassengerSocket)&&cargo.IsChildOf(Session.DemoEscortCargoSocket);
        bool SocketsSafe()
        {
            var passenger=Session.DemoEscortPassengerSocket;var load=Session.DemoEscortCargoSocket;
            if(!passenger.IsChildOf(seat.Vehicle.transform)||!load.IsChildOf(seat.Vehicle.transform)||Vector3.Distance(passenger.position,load.position)<.8f)return false;
            Vector3 local=seat.Vehicle.transform.InverseTransformPoint(passenger.position);var hull=seat.Vehicle.Hull;
            if(hull==null||Mathf.Abs(local.x-hull.center.x)+.27f>hull.size.x*.5f+.05f||local.y+1.7f>hull.center.y+hull.size.y*.5f)return false;
            bool cargoClear=!Physics.OverlapBox(load.position+load.up*.35f,new Vector3(.35f,.35f,.35f),load.rotation,~0,QueryTriggerInteraction.Ignore)
                .Any(c=>!Ignore(c)&&!c.transform.IsChildOf(seat.Vehicle.transform)&&!c.transform.IsChildOf(Session.Walker.Body.transform));
            return cargoClear&&ClearAt(passenger.position,true)&&Sight(companion.position+Vector3.up,passenger.position+Vector3.up,true);
        }
        void Boarded()
        {
            failedBoard=false;var state=Session.EscortSnapshot;
            if(!RequiresEscortBoarding)return;
            if(state==null||!Alive()||Session.GameplayInputBlocked||Time.time<combatUntil||seat.Vehicle.Speed>.5f||
                state.Stage<DemoEscortStage.Contracted||state.Stage>=DemoEscortStage.Delivered||state.Stage==DemoEscortStage.Contracted&&!CurrentStart()||
                Vector3.Distance(companion.position,seat.SeatSocket.position)>7.5f||!CargoCarried||!SocketsSafe())
            {failedBoard=true;seat.Vehicle.SetDriverPresent(false);Block("왕소와 화물을 가까운 안전한 정차소에 모은 뒤 다시 탑승한다.");return;}
            if(state.Stage==DemoEscortStage.Contracted)
            {var start=Stop("escort_start");if(start==null||Vector3.Distance(seat.SeatSocket.position,start.Interaction.position)>4.5f){failedBoard=true;seat.Vehicle.SetDriverPresent(false);return;}}
            boardingCompanion=Pose.Capture(companion);boardingCargo=Pose.Capture(cargo);DisableAgent();originalCompanion.Collision(false);originalCargo.Collision(false);
            companion.SetParent(Session.DemoEscortPassengerSocket,false);companion.localPosition=Vector3.zero;companion.localRotation=Quaternion.identity;companion.localScale=originalCompanion.pose.scale;
            cargo.SetParent(Session.DemoEscortCargoSocket,false);cargo.localPosition=Vector3.zero;cargo.localRotation=Quaternion.identity;cargo.localScale=originalCargo.pose.scale*.55f;
            pendingBoard=true;State=DemoEscortPresentationState.BoardingPending;Brake();
        }
        void Exited(){failedBoard=false;if(pendingBoard)RollbackBoard();if(Loaded()){exitPending=true;ReleaseLocally();}}
        void Attempted(DemoEscortCommand command,DemoEscortStatus status)
        {
            if(!pendingBoard||(command!=DemoEscortCommand.StartEscort&&command!=DemoEscortCommand.Board)||status==DemoEscortStatus.Saved)return;
            if(status==DemoEscortStatus.Duplicate&&Session.EscortSnapshot?.CompanionMode==DemoEscortCompanionMode.Riding)
            {pendingBoard=false;failedBoard=false;State=DemoEscortPresentationState.Riding;if(seat.Occupied)seat.Vehicle.SetDriverPresent(true);return;}
            RollbackBoard();failedBoard=true;seat.Vehicle.SetDriverPresent(false);LastIssue="호송 출발을 확정하지 못했다. 왕소 곁에서 하차한 뒤 다시 탑승한다.";
        }
        void RollbackBoard()
        {
            if(!pendingBoard)return;pendingBoard=false;DisableAgent();
            if(SafeNav(boardingCompanion.position,.5f,out var feet))
            {boardingCompanion.Restore(companion);companion.position=feet;boardingCargo.Restore(cargo);originalCompanion.Collision(true);originalCargo.Collision(!CargoCarried);}
            else {exitPending=true;ReleaseLocally();}
            State=DemoEscortPresentationState.Waiting;
        }
        void Saved(DemoEscortState state,DemoEscortReceipt receipt)
        {
            if(receipt.Command==DemoEscortCommand.StartEscort||receipt.Command==DemoEscortCommand.Board)
            {pendingBoard=false;failedBoard=false;State=DemoEscortPresentationState.Riding;if(seat.Occupied)seat.Vehicle.SetDriverPresent(true);}
            if(receipt.RestoreAtCheckpoint){pendingBoard=false;exitPending=false;RecoverAt(receipt.RestoreFeet);return;}
            if(receipt.Command==DemoEscortCommand.Disembark||receipt.Command==DemoEscortCommand.VehicleRecalled)
            {if(Loaded()||exitPending){exitPending=true;ReleaseLocally();}}
            if(receipt.Command==DemoEscortCommand.Deliver){StopWalk();DropDeliveredCargo();State=DemoEscortPresentationState.Delivered;}
        }
        void ReleaseLocally()
        {
            DisableAgent();pendingBoard=false;Brake();
            // Detach even when the recalled vehicle is inactive. Preserve world pose until a local exit is verified.
            companion.SetParent(originalCompanion.pose.parent,true);cargo.SetParent(originalCargo.pose.parent,true);
            companion.gameObject.SetActive(true);cargo.gameObject.SetActive(true);originalCompanion.Collision(false);originalCargo.Collision(false);
            Vector3 origin=seat.Vehicle.transform.position;
            var candidates=(seat.ExitSockets??Array.Empty<Transform>()).Where(t=>t!=null).Select(t=>t.position).Concat(
                new[]{origin+seat.Vehicle.transform.right*2.5f,origin-seat.Vehicle.transform.right*2.5f,origin-seat.Vehicle.transform.forward*3,
                    Session.Walker.Body.transform.position+Session.Walker.Body.transform.right*.9f}).ToArray();
            foreach(var candidate in candidates)
            {
                if(Vector3.Distance(candidate,origin)>8||!SafeNav(candidate,1,out var feet))continue;
                companion.SetPositionAndRotation(feet,Quaternion.Euler(0,seat.Vehicle.transform.eulerAngles.y,0));companion.localScale=originalCompanion.pose.scale;
                originalCompanion.Collision(true);cargo.SetParent(carrySocket,false);cargo.localPosition=Vector3.zero;cargo.localRotation=Quaternion.identity;cargo.localScale=originalCargo.pose.scale*.55f;
                exitPending=false;State=DemoEscortPresentationState.Waiting;return;
            }
            exitPending=true;State=DemoEscortPresentationState.ExitPending;LastIssue="왕소가 안전하게 내릴 가까운 빈 공간을 확인하고 있다.";
        }
        void RecoverAt(Vector3 feet)
        {
            DisableAgent();companion.SetParent(originalCompanion.pose.parent,true);cargo.SetParent(originalCargo.pose.parent,true);
            companion.gameObject.SetActive(true);cargo.gameObject.SetActive(true);originalCompanion.Collision(false);originalCargo.Collision(false);
            if(!SafeNav(feet,.4f,out var safe)&&!SafeNav(feet+Vector3.right*.8f,.6f,out safe)){Block("호송 복구 지점의 보행 연결을 확인해야 한다.");return;}
            companion.position=safe;companion.localScale=originalCompanion.pose.scale;originalCompanion.Collision(true);
            cargo.SetParent(carrySocket,false);cargo.localPosition=Vector3.zero;cargo.localRotation=Quaternion.identity;cargo.localScale=originalCargo.pose.scale*.55f;
            State=DemoEscortPresentationState.Waiting;
        }
        void InitializeSaved(DemoEscortState state)
        {
            if(state.Stage<DemoEscortStage.Escorting)return;
            // Only restore a durable location; never warp to the moving player's current checkpoint/inspection.
            // The owner normalizes uncertain saved Riding to the checkpoint for the entire party.
            // A normal walking save contains measured companion feet captured with the same player save.
            if(state.HasCheckpoint)RecoverAt(state.CompanionFeet);
            if(state.Stage==DemoEscortStage.Delivered){DropDeliveredCargo();State=DemoEscortPresentationState.Delivered;}
        }
        void DropDeliveredCargo()
        {
            var delivery=Stop("cargo_delivery");if(delivery==null)return;
            if(Vector3.Distance(companion.position,delivery.CargoWait.position)>8||!SafeNav(delivery.CargoWait.position,1,out var feet))return;
            cargo.SetParent(originalCargo.pose.parent,true);cargo.SetPositionAndRotation(feet,delivery.CargoWait.rotation);cargo.localScale=originalCargo.pose.scale;originalCargo.Collision(true);
        }
    }
}
