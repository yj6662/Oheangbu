using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    [InitializeOnLoad]
    public static class DemoEscortRuntimeChecks
    {
        const string Key="DemoEscortRuntimeChecks.Run",SuffixKey="PlaytestUiReviewSuffix",Missing="__escort_missing__";
        static string DiagnosticScene=>SceneManager.GetActiveScene().path==WorldMacroCompactAuthoring.TargetScene?WorldMacroCompactAuthoring.TargetScene:DemoFoundationAuthoring.Scene;
        static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,DiagnosticScene==WorldMacroCompactAuthoring.TargetScene?"../../Art/Demo/ActsTerrain/DemoEscortRuntimeChecks.json":"../../Art/Demo/Chapter4/escort_runtime_tests.json"));
        [Serializable] sealed class CheckResult{public string category,name,status,detail;}
        [Serializable] sealed class ProtectedFile{public string path,hash;}
        [Serializable] sealed class NavSample
        {public float radius;public bool found;public Vector3 position;public float distance,heightDelta;}
        [Serializable] sealed class NavTarget
        {public string name,pathStatus;public Vector3 target;public bool pathCalculated;public NavSample[] samples;public Vector3[] corners;}
        [Serializable] sealed class NavSurfaceEvidence
        {
            public string name,collectObjects,dataAsset;public bool enabled,active,hasData;public int agentTypeId;
            public Vector3 transformPosition,volumeCentreWorld,volumeSizeLocal,sourceBoundsCentre,sourceBoundsSize;
        }
        [Serializable] sealed class AgentMotionEvidence
        {
            public int frame,actualAgentId,presenterAgentId,seamAgentId,completedSeams;
            public float scaledTime,deltaTime,remainingDistance,speed,acceleration,radius,height,baseOffset,stoppingDistance,nextPathAt,combatUntil,seamMetres,seamLargestStep;
            public bool remainingDistanceFinite,agentActive,onMesh,isStopped,hasPath,pathPending,isPathStale,onOffMeshLink,updatePosition,updateRotation,autoTraverse,
                presenterEnabled,presenterActive,presenterBound,presenterInitialized,pendingBoard,exitPending,failedBoard,
                seamExists,seamEnabled,seamActive,seamAgentMatches,seamSessionMatches,seamPresenterMatches,gameplayBlocked;
            public Vector3 actor,nextPosition,velocity,desiredVelocity,steeringTarget,destination,linkStart,linkEnd;
            public string nativePathStatus,offMeshLinkType,presenterState,presenterIssue,seamIssue,navMeshOwner;
            public Vector3[] nativeCorners;public string[] actorComponents,proposedStepSweep;
        }
        [Serializable] sealed class NavigationEvidence
        {
            public string scope="Read-only live scene/NavMesh queries. Wider samples are diagnostic only; runtime safety radii are unchanged.",scene,presenterState,presenterIssue;
            public bool playing,agentExists,agentEnabled,agentOnMesh,cargoCarried,ready;
            public int agentTypeId;public Vector3 companion,cargo,player,passengerSocket,cargoSocket;
            public NavSample[] companionSamples;public NavTarget[] targets;public string[] groundHits,blockingCapsules;public NavSurfaceEvidence[] surfaces;
            public AgentMotionEvidence currentAgent;public AgentMotionEvidence[] recentAgentFrames;
            public string lastObstruction;public Bounds lastObstructionBounds;public Vector3 lastObstructionPoint;
        }
        [Serializable] sealed class Run
        {
            public string status,phase,suffix,savePath,priorSuffix,contentPath,campaignPath,error,lastInputDetails;
            public bool active,stopping,cleaned,priorBackground,priorCombat,restoredProfile,fixtureInstalled;
            public int stage=-1,contentCloneId,campaignCloneId,boards,exits,startReceipts,boardReceipts,recallReceipts,contractCurrency;
            public double deadline,phaseDeadline,readyAt,nextFocusAt;
            public int focusRecoveries;
            public NavigationEvidence navigation;
            public double nextAgentProbeAt;public List<AgentMotionEvidence> agentFrames=new List<AgentMotionEvidence>();
            public Vector3 companionStart,companionLast,exitOrigin;
            public float shortWalkMetres,companionSampleTime;
            public List<CheckResult> checks=new List<CheckResult>();public List<ProtectedFile> protectedFiles=new List<ProtectedFile>();
            public string scope="Bounded Play API integration using actual Session.Interact, Seat.TryBoard/TryExit, presenter NavMesh movement and automatic vehicle recall. Runtime-only five-stage campaign fixture omits earlier chapters; player safe teleports are setup, never journey evidence. No direct escort/campaign state setters. Not a full escort journey, inspection/delivery playthrough, native input or app restart.";
            public string[] unverified={"Full road journey and both inspections/delivery", "Native input and seated visual fit", "Injected write-failure rollback", "Actual application restart", "Active combat and death recovery"};
        }
        static Run run;
        static WorldMacroPlaytestSession session;
        static PlaytestUiRoot ui;
        static DemoEscortPresentation presenter;
        static DemoEscortSceneRoute route;
        static WorldMacroPlaytestSO original,contentClone;
        static DemoCampaignProfile campaignClone;
        static bool subscribed;
        static WorldMacroPalanquinSeat Seat=>session.DemoEscortSeat;
        static Transform Companion=>session.DemoEscortCompanion;
        static Transform Cargo=>session.DemoEscortCargo;
        static DemoEscortStop Start=>route.Stops.Single(s=>s.Id=="escort_start");
        static bool InputReady=>!session.GameplayInputBlocked&&ui!=null&&!ui.Busy&&!ui.Pause.IsPaused;
        static void FocusGameView()
        {
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if(type==null)throw new InvalidOperationException("Game View type unavailable; cannot establish real input focus.");
            var view=EditorWindow.GetWindow(type,false,"Game",true);view.Focus();view.Repaint();
        }
        static void MaintainInputFocus()
        {
            if(run==null||!run.active||session==null||ui?.Pause==null||InputReady||EditorApplication.timeSinceStartup<run.nextFocusAt)return;
            if(Application.isFocused&&EditorWindow.focusedWindow?.GetType().FullName=="UnityEditor.GameView")return;
            run.nextFocusAt=EditorApplication.timeSinceStartup+1;run.focusRecoveries++;FocusGameView();
        }
        static NavSample[] SampleNav(Vector3 position)
        {
            return new[]{.4f,1.2f,2f,3f,8f,10f}.Select(radius=>{
                bool found=NavMesh.SamplePosition(position,out var hit,radius,NavMesh.AllAreas);
                return new NavSample{radius=radius,found=found,position=found?hit.position:Vector3.zero,
                    distance=found?Vector3.Distance(position,hit.position):-1,heightDelta=found?hit.position.y-position.y:0};
            }).ToArray();
        }
        static NavigationEvidence NavigationProbe()
        {
            var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var evidence=new NavigationEvidence{playing=EditorApplication.isPlaying,scene=SceneManager.GetActiveScene().path};
            if(s==null||s.DemoEscortCompanion==null||s.DemoEscortCargo==null)return evidence;
            var actor=s.DemoEscortCompanion;var load=s.DemoEscortCargo;var agent=actor.GetComponent<NavMeshAgent>();
            var p=Object.FindObjectsByType<DemoEscortPresentation>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(v=>v.Session==s);
            evidence.currentAgent=ReadAgentMotion(s,p);evidence.recentAgentFrames=run?.agentFrames?.ToArray()??Array.Empty<AgentMotionEvidence>();
            if(p?.LastBlockingCollider!=null){var obstruction=p.LastBlockingCollider.transform;var names=new System.Collections.Generic.List<string>();while(obstruction!=null){names.Add(obstruction.name);obstruction=obstruction.parent;}names.Reverse();evidence.lastObstruction=string.Join("/",names);evidence.lastObstructionBounds=p.LastBlockingCollider.bounds;evidence.lastObstructionPoint=p.LastBlockedPoint;}
            evidence.ready=s.DemoEscortReady;evidence.companion=actor.position;evidence.cargo=load.position;evidence.player=s.Walker.Body.transform.position;
            evidence.agentExists=agent!=null;evidence.agentEnabled=agent!=null&&agent.enabled;evidence.agentOnMesh=agent!=null&&agent.enabled&&agent.isOnNavMesh;
            evidence.agentTypeId=agent!=null?agent.agentTypeID:-1;evidence.presenterState=p?.State.ToString();evidence.presenterIssue=p?.LastIssue;evidence.cargoCarried=p!=null&&p.CargoCarried;
            evidence.passengerSocket=s.DemoEscortPassengerSocket!=null?s.DemoEscortPassengerSocket.position:Vector3.zero;
            evidence.cargoSocket=s.DemoEscortCargoSocket!=null?s.DemoEscortCargoSocket.position:Vector3.zero;evidence.companionSamples=SampleNav(actor.position);
            evidence.surfaces=Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(surface=>surface.gameObject.scene==s.gameObject.scene)
                .Select(surface=>new NavSurfaceEvidence{name=surface.name,enabled=surface.enabled,active=surface.gameObject.activeInHierarchy,hasData=surface.navMeshData!=null,
                    agentTypeId=surface.agentTypeID,collectObjects=surface.collectObjects.ToString(),transformPosition=surface.transform.position,
                    volumeCentreWorld=surface.transform.TransformPoint(surface.center),volumeSizeLocal=surface.size,dataAsset=AssetDatabase.GetAssetPath(surface.navMeshData),
                    sourceBoundsCentre=surface.navMeshData!=null?surface.navMeshData.sourceBounds.center:Vector3.zero,sourceBoundsSize=surface.navMeshData!=null?surface.navMeshData.sourceBounds.size:Vector3.zero}).ToArray();
            var targets=new List<NavTarget>();
            void Target(string name,Vector3 target)
            {var path=new NavMeshPath();bool ok=NavMesh.CalculatePath(actor.position,target,NavMesh.AllAreas,path);
                targets.Add(new NavTarget{name=name,target=target,pathCalculated=ok,pathStatus=path.status.ToString(),corners=path.corners,samples=SampleNav(target)});}
            Vector3 delta=load.position-actor.position;Target("actual cargo pickup approach",load.position-delta.normalized*1.1f);
            var start=p?.Route?.Stops?.FirstOrDefault(v=>v.Id=="escort_start");
            if(start?.CompanionWait!=null)Target("authored start companion wait",start.CompanionWait.position);
            Target("actual follow target",s.Walker.Body.transform.position-s.Walker.Body.transform.forward*2.2f);
            evidence.targets=targets.ToArray();
            bool Ignore(Collider c)=>c==null||c.gameObject.scene!=s.gameObject.scene||c.transform.IsChildOf(actor)||c.transform.IsChildOf(load);
            evidence.groundHits=Physics.RaycastAll(actor.position+Vector3.up*1.5f,Vector3.down,3,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance)
                .Select(h=>$"{h.collider.name}; point={h.point:F3}; normal={h.normal:F3}; ignoredActorOrOtherScene={Ignore(h.collider)}; rigidbody={h.collider.attachedRigidbody!=null}").ToArray();
            Vector3 probe=evidence.companionSamples[0].found?evidence.companionSamples[0].position+Vector3.up*.03f:actor.position;
            evidence.blockingCapsules=Physics.OverlapCapsule(probe+Vector3.up*.3f,probe+Vector3.up*1.4f,.28f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>!Ignore(c)).Select(c=>$"{c.name}; path={c.transform.parent?.name}/{c.name}; bounds={c.bounds}").ToArray();
            return evidence;
        }
        static object PrivateValue(object target,string field)=>target?.GetType().GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        static AgentMotionEvidence ReadAgentMotion(WorldMacroPlaytestSession s,DemoEscortPresentation p)
        {
            var result=new AgentMotionEvidence{frame=Time.frameCount,scaledTime=Time.time,deltaTime=Time.deltaTime,remainingDistance=-1};
            if(s?.DemoEscortCompanion==null)return result;var actor=s.DemoEscortCompanion;var agent=actor.GetComponent<NavMeshAgent>();
            var seam=actor.GetComponent<DemoEscortSeamWalker>();var cached=PrivateValue(seam,"agent") as NavMeshAgent;var presentationAgent=PrivateValue(p,"agent") as NavMeshAgent;
            result.actor=actor.position;result.actualAgentId=agent!=null?agent.GetInstanceID():0;result.presenterAgentId=presentationAgent!=null?presentationAgent.GetInstanceID():0;
            result.actorComponents=actor.GetComponents<Component>().Select(c=>c!=null?c.GetType().FullName:"Missing component").ToArray();
            result.presenterEnabled=p!=null&&p.enabled;result.presenterActive=p!=null&&p.isActiveAndEnabled;
            result.presenterBound=PrivateValue(p,"bound") is bool bound&&bound;result.presenterInitialized=PrivateValue(p,"initialized") is bool initialized&&initialized;
            result.pendingBoard=p!=null&&p.PendingAttachment;result.exitPending=PrivateValue(p,"exitPending") is bool exiting&&exiting;result.failedBoard=PrivateValue(p,"failedBoard") is bool failed&&failed;
            result.nextPathAt=PrivateValue(p,"nextPath") is float nextPath?nextPath:0;result.combatUntil=PrivateValue(p,"combatUntil") is float combat?combat:0;
            result.presenterState=p?.State.ToString();result.presenterIssue=p?.LastIssue;result.gameplayBlocked=s.GameplayInputBlocked;
            result.seamExists=seam!=null;result.seamEnabled=seam!=null&&seam.enabled;result.seamActive=seam!=null&&seam.isActiveAndEnabled;
            result.seamAgentId=cached!=null?cached.GetInstanceID():0;result.seamAgentMatches=cached!=null&&cached==agent;
            result.seamSessionMatches=seam!=null&&seam.Session==s;result.seamPresenterMatches=seam!=null&&seam.Presentation==p;
            result.seamIssue=seam?.LastIssue;result.completedSeams=seam!=null?seam.CompletedTraversals:0;result.seamMetres=seam!=null?seam.TraversedMetres:0;result.seamLargestStep=seam!=null?seam.LargestWorldStep:0;
            if(agent==null)return result;result.agentActive=agent.isActiveAndEnabled;result.onMesh=result.agentActive&&agent.isOnNavMesh;
            result.updatePosition=agent.updatePosition;result.updateRotation=agent.updateRotation;result.autoTraverse=agent.autoTraverseOffMeshLink;
            result.speed=agent.speed;result.acceleration=agent.acceleration;result.radius=agent.radius;result.height=agent.height;result.baseOffset=agent.baseOffset;result.stoppingDistance=agent.stoppingDistance;
            if(!result.onMesh)return result;
            result.isStopped=agent.isStopped;result.hasPath=agent.hasPath;result.pathPending=agent.pathPending;result.isPathStale=agent.isPathStale;result.onOffMeshLink=agent.isOnOffMeshLink;
            result.nextPosition=agent.nextPosition;result.velocity=agent.velocity;result.desiredVelocity=agent.desiredVelocity;result.steeringTarget=agent.steeringTarget;result.destination=agent.destination;
            result.remainingDistanceFinite=float.IsFinite(agent.remainingDistance);result.remainingDistance=result.remainingDistanceFinite?agent.remainingDistance:-1;result.nativePathStatus=agent.pathStatus.ToString();
            result.nativeCorners=result.hasPath?agent.path.corners:Array.Empty<Vector3>();
            if(result.onOffMeshLink){var link=agent.currentOffMeshLinkData;result.linkStart=link.startPos;result.linkEnd=link.endPos;result.offMeshLinkType=link.linkType.ToString();}
            var owner=typeof(NavMeshAgent).GetProperty("navMeshOwner")?.GetValue(agent) as Object;result.navMeshOwner=owner!=null?owner.name:null;
            Vector3 delta=result.nextPosition-actor.position;float limit=Mathf.Min(.5f,agent.speed*Mathf.Max(0,Time.deltaTime)*1.25f);if(delta.magnitude>limit)delta=delta.normalized*limit;
            result.proposedStepSweep=delta.sqrMagnitude>.000001f?Physics.CapsuleCastAll(actor.position+Vector3.up*.3f,actor.position+Vector3.up*1.4f,.27f,delta.normalized,delta.magnitude+.02f,~0,QueryTriggerInteraction.Ignore)
                .Select(h=>$"{h.collider.transform.parent?.name}/{h.collider.name}; distance={h.distance}; normal={h.normal}; sameScene={h.collider.gameObject.scene==s.gameObject.scene}; selfOrCargo={h.transform.IsChildOf(actor)||s.DemoEscortCargo!=null&&h.transform.IsChildOf(s.DemoEscortCargo)}").ToArray():Array.Empty<string>();
            return result;
        }
        static string SaveNavigationProbe()
        {
            var evidence=NavigationProbe();if(run!=null&&run.active)run.navigation=evidence;
            string json=JsonUtility.ToJson(evidence,true);Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Output),"escort_nav_probe.json"),json);return json;
        }
        static string InputDetails()
        {
            // Some Unity versions do not expose EditorApplication.isFocused publicly. Report its
            // actual reflected value when available, alongside the supported Game/application focus APIs.
            var focus=typeof(EditorApplication).GetProperty("isFocused",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            object editorFocused=focus!=null?focus.GetValue(null):"unavailable";
            var gate=ui?.Pause?.Gate;
            string detail=$"timeScale={Time.timeScale}; ui.Page={ui?.Page}; ui.Busy={ui?.Busy}; Pause.IsPaused={ui?.Pause?.IsPaused}; GameplayInputBlocked={session?.GameplayInputBlocked}; RuntimeState.InputBlocked={session?.RuntimeState?.InputBlocked}; EditorApplication.isFocused={editorFocused}; Application.isFocused={Application.isFocused}; focusedWindow={EditorWindow.focusedWindow?.GetType().FullName}; gate.InputBlocked={gate?.InputBlocked}; gate.ReleasePending={gate?.ReleasePending}; gate.FocusOwnsBlock={gate?.FocusOwnsBlock}; gate.NeutralFrames={gate?.NeutralFrames}";
            if(run!=null)run.lastInputDetails=detail;return detail;
        }
        static DemoEscortRuntimeChecks()
        {
            string saved=SessionState.GetString(Key,"");if(!string.IsNullOrEmpty(saved))run=JsonUtility.FromJson<Run>(saved);
            EditorApplication.playModeStateChanged+=PlayChanged;EditorApplication.quitting+=Quitting;
            if(run!=null&&(run.active||run.stopping))EditorApplication.update+=Tick;
        }
        public static string Execute(string command)
        {
            if((command=="begin"||command=="begin-hold")&&Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("System commit >=85%");
            if(command=="probe")return SaveNavigationProbe();
            if(command=="poll")return run!=null?JsonUtility.ToJson(run,true):File.Exists(Output)?File.ReadAllText(Output):"NOT_RUN";
            if(command=="abort"||command=="stop"){if(run==null||run.cleaned)return "NOT_RUNNING";Finish("ABORTED","Explicit stop");return "STOPPING";}
            if(command!="begin")throw new ArgumentException("Use begin, poll, probe, stop or abort.");
            if(run!=null&&(run.active||run.stopping)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle compiled Edit mode required.");
            if(SceneManager.GetActiveScene().path!=DiagnosticScene)throw new InvalidOperationException("Dedicated demo scene required; this runner does not open/edit scene assets.");
            var authored=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var authoredPresenter=Object.FindFirstObjectByType<DemoEscortPresentation>();
            if(authored==null||authoredPresenter==null||authoredPresenter.Session!=authored||authoredPresenter.Route==null||authored.DemoEscortSeat==null||
                AssetDatabase.GetAssetPath(authored.Content)!=(DiagnosticScene==WorldMacroCompactAuthoring.TargetScene?WorldMacroCompactAuthoring.Folder+"/Data/03_Content.asset":DemoFoundationAuthoring.Folder+"/Content.asset"))throw new InvalidOperationException("Apply the real escort scene/presenter first.");
            string suffix="_escort_"+Guid.NewGuid().ToString("N"),save=Path.Combine(Application.persistentDataPath,authored.Content.SaveSlot+suffix+".json");
            if(File.Exists(save)||File.Exists(save+".bak")||File.Exists(save+".tmp"))throw new IOException("Fresh UUID slot already exists");
            run=new Run{active=true,status="RUNNING",phase="entering isolated Play",suffix=suffix,savePath=save,priorSuffix=SessionState.GetString(SuffixKey,Missing),
                priorBackground=Application.runInBackground,contentPath=AssetDatabase.GetAssetPath(authored.Content),campaignPath=AssetDatabase.GetAssetPath(authored.Content.Campaign),deadline=EditorApplication.timeSinceStartup+180};
            foreach(string path in ExistingSaves().Concat(new[]{run.contentPath,run.campaignPath,DiagnosticScene}).Distinct())
                run.protectedFiles.Add(new ProtectedFile{path=path,hash=Hash(path)});
            SessionState.SetString(SuffixKey,suffix);Application.runInBackground=true;session=null;ui=null;subscribed=false;Persist();
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            try{EditorApplication.EnterPlaymode();}catch(Exception e){Finish("FINDINGS",e.ToString());throw;}
            return "RUNNING; UUID-only saves; runtime-only five-stage FIXTURE; automatically exits Play and restores profile/suffix on success or failure.";
        }
        static IEnumerable<string> ExistingSaves()=>Directory.Exists(Application.persistentDataPath)?Directory.GetFiles(Application.persistentDataPath,"*.json*",SearchOption.TopDirectoryOnly)
            .Where(p=>run==null||!(p==run.savePath||p==run.savePath+".bak"||p==run.savePath+".tmp")):Array.Empty<string>();
        static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        static void Persist(){if(run==null)return;string json=JsonUtility.ToJson(run,true);SessionState.SetString(Key,json);Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,json);}
        static void Check(bool pass,string name,string detail,string category="ACTUAL")
        {run.checks.Add(new CheckResult{category=category,name=name,status=pass?"PASS":"FAIL",detail=detail});if(!pass)throw new InvalidOperationException(name+": "+detail);}
        static void Next(int stage,string phase,double timeout=12,double delay=.15)
        {run.stage=stage;run.phase=phase;run.phaseDeadline=EditorApplication.timeSinceStartup+timeout;run.readyAt=EditorApplication.timeSinceStartup+delay;Persist();}
        static void Isolation()
        {
            if(session.TestSaveSuffix!=run.suffix||SessionState.GetString(SuffixKey,Missing)!=run.suffix||
                Path.Combine(Application.persistentDataPath,session.Content.SaveSlot+session.TestSaveSuffix+".json")!=run.savePath)
                throw new InvalidOperationException("UUID isolation lost; no further save/action is allowed.");
        }
        static void Protect()
        {
            foreach(var file in run.protectedFiles)Check(File.Exists(file.path)&&Hash(file.path)==file.hash,"protected file unchanged",file.path,"ISOLATION");
            var existing=new HashSet<string>(run.protectedFiles.Select(f=>f.path),StringComparer.OrdinalIgnoreCase);
            Check(ExistingSaves().All(existing.Contains),"no non-test save created","Only the exact allocated UUID primary/backup/temp are excluded.","ISOLATION");
        }
        static void Bind()
        {
            session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();ui=PlaytestUiRoot.Instance;
            if(session==null)return;presenter=Object.FindObjectsByType<DemoEscortPresentation>(FindObjectsSortMode.None).SingleOrDefault(p=>p.Session==session);route=presenter?.Route;
            if(subscribed||presenter==null)return;
            Seat.Boarded+=Boarded;Seat.Exited+=Exited;session.DemoEscortChanged+=Changed;subscribed=true;
        }
        static void Boarded(){run.boards++;}
        static void Exited(){run.exits++;}
        static void Changed(DemoEscortState state,DemoEscortReceipt receipt)
        {if(receipt.Command==DemoEscortCommand.StartEscort)run.startReceipts++;if(receipt.Command==DemoEscortCommand.Board)run.boardReceipts++;if(receipt.Command==DemoEscortCommand.VehicleRecalled)run.recallReceipts++;}
        static void Fixture()
        {
            original=session.Content;run.priorCombat=session.CombatActive;
            contentClone=Object.Instantiate(original);contentClone.hideFlags=HideFlags.DontSave;contentClone.name="EscortOnly_FIXTURE_"+run.suffix;
            campaignClone=Object.Instantiate(original.Campaign);campaignClone.hideFlags=HideFlags.DontSave;campaignClone.name="EscortFiveStages_FIXTURE_"+run.suffix;
            string[] ids={"cargo_contract","escort","checkpoint_one","checkpoint_two","delivery"};
            campaignClone.Stages=campaignClone.Stages.Where(s=>ids.Contains(s.Id)).ToArray();foreach(var stage in campaignClone.Stages)stage.Implemented=true;
            Check(campaignClone.Stages.Length==5&&campaignClone.IsValid,"five-stage isolated campaign fixture","Only the in-memory clone enables escort stages; earlier chapters are omitted, not claimed completed.","FIXTURE");
            contentClone.Campaign=campaignClone;contentClone.Opening=null;session.Content=contentClone;session.CombatActive=false;session.Cull();
            run.contentCloneId=contentClone.GetInstanceID();run.campaignCloneId=campaignClone.GetInstanceID();run.fixtureInstalled=true;
            Check(session.Progress.escort.Stage==DemoEscortStage.None&&session.Progress.campaign.Completed.Count==0,"no quest-state seed","Fresh runtime state remains empty; only the profile defines a bounded test scenario.","FIXTURE");
            ui.CloseMenu();
            FocusGameView();
            Check(true,"focus actual Game View","Normal EditorWindow.Focus only; no key events, gate-state writes or forced neutral-frame release.","FIXTURE");
            run.companionStart=Companion.position;InputDetails();Persist();
        }
        static void SafeTeleport(Vector3 candidate,float yaw,string reason)
        {Check(session.TrySafeFeet(candidate,out var feet),"safe player setup",reason,"FIXTURE");session.Teleport(feet,yaw);Physics.SyncTransforms();}
        static void ApproachContract()
        {
            var point=session.Content.Points.Single(p=>p.Id=="wangso_w1");
            for(int i=0;i<16;i++)
            {float a=i*Mathf.PI/8;Vector3 candidate=point.Position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.8f;
                if(!session.TrySafeFeet(candidate,out var feet))continue;session.Teleport(feet,Quaternion.LookRotation(point.Position-feet).eulerAngles.y);Physics.SyncTransforms();
                if(session.CanInteract(point.Id)){Check(true,"contract approach","Player-only safe API setup; NPC and quest state remain authored.","FIXTURE");return;}}
            throw new InvalidOperationException("Cannot reach real Wangso contract interaction");
        }
        static void ApproachCar()
        {
            foreach(float side in new[]{-1f,1f})foreach(float z in new[]{0f,.5f,-.5f})
            {Vector3 p=Seat.Vehicle.transform.TransformPoint(new Vector3(side*2.05f,0,z));if(!session.TrySafeFeet(p,out var feet))continue;
                session.Teleport(feet,Seat.Vehicle.transform.eulerAngles.y);Physics.SyncTransforms();if(Seat.CanBoard)return;}
            throw new InvalidOperationException("Actual seat cannot be boarded from either safe door");
        }
        static void PrepareSummon()
        {
            foreach(float side in new[]{-1f,1f})
            {Vector3 p=Start.Parking.position+Start.Parking.right*side*8;
                if(!session.TrySafeFeet(p,out var feet)||!session.DemoEscortSummon.TryFindPlacement(feet,Start.Parking.forward,out var pose,out _))continue;
                Vector3 delta=pose.Position-Start.Interaction.position;delta.y=0;if(delta.magnitude>3)continue;
                SafeTeleport(feet,Start.Parking.eulerAngles.y,"Choose a ground position whose actual summon query reaches the authored start.");return;}
            throw new InvalidOperationException("No summon placement reaches the authored escort_start interaction radius");
        }
        static void SetupRecallDistance()
        {
            var car=Seat.Vehicle.transform;var path=new NavMeshPath();
            foreach(var direction in new[]{car.forward,-car.forward,car.right,-car.right})
            {Vector3 p=car.position+direction*(session.DemoEscortSummon.RecallDistance+1);
                if(!session.TrySafeFeet(p,out var feet)||Vector3.Distance(feet,Companion.position)>31.9f||
                    !NavMesh.CalculatePath(Companion.position,feet,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)continue;
                SafeTeleport(feet,Quaternion.LookRotation(direction).eulerAngles.y,"Bounded player-only recall-distance setup; Wangso walks a short real path and is never teleported.");return;}
            throw new InvalidOperationException("No bounded safe recall-distance setup with a complete companion path");
        }
        static WorldMacroProgress Disk()
        {Isolation();var store=new AtomicJsonStore<WorldMacroProgress>(run.savePath,WorldMacroProgress.Valid);var loaded=store.Load();Check(loaded!=null&&store.LoadStatus=="primary","independent save reader","Fresh AtomicJsonStore reads the actual UUID primary; not an app restart.");return loaded;}
        static void Tick()
        {
            if(run==null)return;if(run.stopping){if(!EditorApplication.isPlayingOrWillChangePlaymode)Cleanup();return;}if(!run.active)return;
            try
            {
                if(EditorApplication.timeSinceStartup>run.deadline)throw new TimeoutException("180 second overall bound exceeded; "+InputDetails());
                if(!EditorApplication.isPlaying||EditorApplication.isPaused)return;
                if(session==null||presenter==null)Bind();MaintainInputFocus();
                // Read-only bounded history: native agent motion, cached component references and Presenter guards.
                if(session!=null&&presenter!=null&&EditorApplication.timeSinceStartup>=run.nextAgentProbeAt)
                {run.nextAgentProbeAt=EditorApplication.timeSinceStartup+.25;run.agentFrames??=new List<AgentMotionEvidence>();run.agentFrames.Add(ReadAgentMotion(session,presenter));if(run.agentFrames.Count>48)run.agentFrames.RemoveAt(0);}
                if(EditorApplication.timeSinceStartup<run.readyAt||session==null||!session.DemoEscortReady||presenter==null||ui==null||ui.Busy)return;
                Isolation();if(run.stage>=0&&EditorApplication.timeSinceStartup>run.phaseDeadline)throw new TimeoutException(run.phase+"; presenter="+presenter.State+"; "+presenter.LastIssue+"; "+InputDetails());
                switch(run.stage)
                {
                    case -1:
                        Check(session.LoadStatus=="new"&&session.Progress.ledger.currency==0,"fresh UUID session","No existing user save loaded or edited.","ISOLATION");Fixture();Next(0,"safe contract approach");break;
                    case 0:if(!InputReady)return;ApproachContract();Next(1,"actual contract interaction");break;
                    case 1:
                        if(!InputReady)return;Check(session.Interact("wangso_w1"),"actual contract accepted","Session.Interact runs registered NPC/campaign/escort owner checks.");
                        Check(session.EscortSnapshot.Stage==DemoEscortStage.Contracted&&session.Progress.campaign.Completed.SequenceEqual(new[]{"cargo_contract"}),"contract persisted once","No direct state setters; real transaction owns progression.");run.contractCurrency=session.Progress.ledger.currency;ui.CloseMenu();Next(2,"prepare actual vehicle summon");break;
                    case 2:if(!InputReady)return;PrepareSummon();Next(3,"settle grounded setup",8,.5);break;
                    case 3:if(!InputReady)return;ui.OpenPage("일시정지");Next(4,"actual menu summon");break;
                    case 4:
                        Check(session.DemoEscortSummon.TrySummonFromMenu(ui.Pause,out var reason),"actual vehicle summon",reason??"Existing summon service chose four-wheel/hull/exit-safe placement.");ui.CloseMenu();Next(5,"short actual cargo pickup and staging",30);break;
                    case 5:
                        if(!InputReady)return;
                        if(!presenter.CargoCarried||Vector3.Distance(Companion.position,Start.CompanionWait.position)>1.1f)return;
                        var nav=Companion.GetComponent<NavMeshAgent>();
                        Check(nav!=null&&nav.enabled&&nav.isOnNavMesh&&Vector3.Distance(run.companionStart,Companion.position)>1,"actual bounded NavMesh staging","Original actor walked to cargo and start; no test setter moved NPC/cargo.");
                        ApproachCar();Next(6,"board via actual seat API",12,.4);break;
                    case 6:
                        if(!InputReady)return;Check(Seat.TryBoard(),"actual first boarding",Seat.LastInteraction);Next(7,"owner observes loaded hierarchy");break;
                    case 7:
                        if(session.EscortSnapshot.Stage!=DemoEscortStage.Escorting)return;
                        Check(run.boards==1&&run.startReceipts==1&&Companion.IsChildOf(session.DemoEscortPassengerSocket)&&Cargo.IsChildOf(session.DemoEscortCargoSocket),"real Boarded to loaded transaction","Actual seat event, actual actors beneath their owned sockets, and exactly one saved StartEscort receipt.");
                        Check(session.Progress.campaign.Completed.SequenceEqual(new[]{"cargo_contract","escort"})&&session.EscortSnapshot.CheckpointId=="escort_start","start is the only new campaign stage","No inspections or delivery are awarded.");
                        Check(session.Progress.ledger.currency==run.contractCurrency+campaignClone.Stages.Single(s=>s.Id=="escort").TongboReward,"exact start reward","Only the configured one-time start reward may change currency.");
                        Check(session.SaveNow(out var err),"save riding state",err??"UUID SaveNow");var riding=Disk();Check(riding.escort.CompanionMode==DemoEscortCompanionMode.Riding&&riding.escort.OwnsSealedCargo,"riding restore data","Durable state contains quest-owned cargo, companion mode and authored checkpoint.");
                        run.exitOrigin=Seat.Vehicle.transform.position;Check(Seat.TryExit(),"actual nearby exit",Seat.LastInteraction);Next(8,"saved disembark");break;
                    case 8:
                        if(session.EscortSnapshot.CompanionMode==DemoEscortCompanionMode.Riding)return;
                        Check(!Companion.IsChildOf(Seat.Vehicle.transform)&&!Cargo.IsChildOf(Seat.Vehicle.transform)&&Companion.gameObject.activeInHierarchy&&Cargo.gameObject.activeInHierarchy&&Vector3.Distance(Companion.position,run.exitOrigin)<8,"nearby physical disembark","Original actors active outside vehicle; no distant checkpoint warp.");
                        Check(run.exits==1,"one real exit event","Seat.TryExit emitted Exited once.");ApproachCar();Next(9,"actual reboarding",12,.4);break;
                    case 9:if(!InputReady)return;Check(Seat.TryBoard(),"actual reboarding",Seat.LastInteraction);Next(10,"saved board without reward");break;
                    case 10:
                        if(session.EscortSnapshot.CompanionMode!=DemoEscortCompanionMode.Riding||run.boardReceipts!=1)return;
                        Check(run.boards==2&&run.startReceipts==1&&session.Progress.campaign.Completed.Count==2&&session.Progress.ledger.currency==run.contractCurrency+campaignClone.Stages.Single(s=>s.Id=="escort").TongboReward,"reboarding does not repeat start","One StartEscort plus one Board receipt; no duplicated completion or currency.");
                        Check(Seat.TryExit(),"second actual exit",Seat.LastInteraction);Next(11,"prepare bounded automatic recall");break;
                    case 11:if(!InputReady||session.EscortSnapshot.CompanionMode==DemoEscortCompanionMode.Riding)return;SetupRecallDistance();run.companionLast=Companion.position;run.companionSampleTime=Time.time;Next(12,"short follow and actual recall",18);break;
                    case 12:
                        float moved=Vector3.Distance(run.companionLast,Companion.position);
                        if(moved>6*Mathf.Max(0,Time.time-run.companionSampleTime)+.35f)throw new InvalidOperationException("Companion warped during ordinary following/recall: "+moved+"m");
                        run.shortWalkMetres+=moved;run.companionLast=Companion.position;run.companionSampleTime=Time.time;
                        if(!session.DemoEscortSummon.IsRecalled||run.recallReceipts==0)return;
                        Check(!Seat.Vehicle.gameObject.activeInHierarchy&&Companion.gameObject.activeInHierarchy&&Cargo.gameObject.activeInHierarchy&&!Companion.IsChildOf(Seat.Vehicle.transform)&&!Cargo.IsChildOf(Seat.Vehicle.transform),"actual recall preserves original actors","Summon.TickRecall deactivated vehicle; no passenger/cargo vanished beneath it.");
                        Check(session.EscortSnapshot.OwnsSealedCargo&&session.EscortSnapshot.InspectionsCleared==0&&session.Progress.campaign.Completed.Count==2,"recall cannot grant inspection or delivery","Actual durable owner retains sealed cargo and ordered stage state.");
                        Next(13,"save current walking pair",12,.7);break;
                    case 13:
                        Check(session.SaveNow(out var saveError),"actual walking save",saveError??"UUID SaveNow");var loaded=Disk();
                        Check(WorldMacroProgress.Valid(loaded)&&loaded.escort.Stage==DemoEscortStage.Escorting&&loaded.escort.OwnsSealedCargo&&!loaded.escort.DeliveryRewardRecorded&&loaded.escort.InspectionsCleared==0,"save restoration data remains valid","New reader preserves contract/start/checkpoint/cargo, with no unearned inspection/delivery.");
                        Check(Vector3.Distance(loaded.escort.CompanionFeet,Companion.position)<.6f,"walking pair save uses measured companion feet","Owner samples actual on-foot actor in the same UUID player save.");
                        Protect();Finish("PASS_BOUNDED_API_INTEGRATION",null);break;
                }
            }
            catch(Exception e){try{SaveNavigationProbe();}catch(Exception probeError){run.error="Read-only navigation probe failed: "+probeError.Message;}Finish("FINDINGS",e+"; NavMesh evidence: escort_nav_probe.json");}
        }
        static void RestoreProfile()
        {
            if(session==null)session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(original==null)original=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(run.contentPath);
            if(run.fixtureInstalled&&session!=null&&session.TestSaveSuffix==run.suffix){session.Content=original;session.CombatActive=run.priorCombat;}
            if(subscribed&&session!=null){Seat.Boarded-=Boarded;Seat.Exited-=Exited;session.DemoEscortChanged-=Changed;}subscribed=false;
            var content=EditorUtility.InstanceIDToObject(run.contentCloneId);var campaign=EditorUtility.InstanceIDToObject(run.campaignCloneId);
            if(content!=null&&!AssetDatabase.Contains(content))Object.DestroyImmediate(content);if(campaign!=null&&!AssetDatabase.Contains(campaign))Object.DestroyImmediate(campaign);
            run.restoredProfile=session==null||session.Content==original;
        }
        static void Finish(string status,string error)
        {
            if(run==null||run.cleaned)return;run.status=status;run.error=error;run.active=false;run.stopping=true;run.phase="restoring runtime profiles and exiting Play";
            try{RestoreProfile();}catch(Exception e){run.error=(run.error??"")+"\nProfile cleanup: "+e;run.status="FINDINGS";}
            Persist();if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else Cleanup();
        }
        static void PlayChanged(PlayModeStateChange state)
        {
            if(run==null||run.cleaned)return;
            if(state==PlayModeStateChange.ExitingPlayMode){if(run.active){run.status="ABORTED";run.error="Play exited before completion";}RestoreProfile();Persist();}
            if(state==PlayModeStateChange.EnteredEditMode)Cleanup();
        }
        static void Quitting(){if(run!=null&&!run.cleaned){run.status="ABORTED";run.error="Editor quit before cleanup";Cleanup();}}
        static void Cleanup()
        {
            if(run==null||run.cleaned)return;
            try{Protect();}catch(Exception e){run.error=(run.error??"")+"\nIsolation: "+e;run.status="FINDINGS";}
            if(run.priorSuffix==Missing)SessionState.EraseString(SuffixKey);else SessionState.SetString(SuffixKey,run.priorSuffix);
            Application.runInBackground=run.priorBackground;run.active=false;run.stopping=false;run.cleaned=true;run.phase="Edit mode; source profile, prior suffix and background restored";
            EditorApplication.update-=Tick;Persist();
        }
    }
}
