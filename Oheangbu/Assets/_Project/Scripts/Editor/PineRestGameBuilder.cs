using System;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Oheangbu.EditorTools.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  public const string Scene="Assets/_Project/Scenes/World/W_PineRest_Journey.unity";
  const string Folder="Assets/_Project/Art/World/PineRestGame";
  public static string Run(string command){
   if(command.StartsWith("CompactRebuild:"))return (string)Type.GetType("Oheangbu.EditorTools.WorldMacro.CompactRebuildAuthoring, Oheangbu.EditorTools.WorldMacro",true).GetMethod("Run").Invoke(null,new object[]{command.Substring(15)});
   if(command=="RestoreJourneyBuildSettings")return PineRestPlayerBuild.RestoreSettings();
   if(command=="BuildJourneyDiagnostics")return PineRestPlayerBuild.Diagnostics();
   if(command=="BuildJourney")return PineRestPlayerBuild.Build();
   if(command=="Play"){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Open Journey first");EditorApplication.isPlaying=true;return "Starting Journey";}
   if(command=="Stop"){EditorApplication.isPlaying=false;return "Stopping";}
   if(command=="GateParry")return GateParry();
   if(command=="GateParryPoll")return File.ReadAllText(GateParryReport);
   if(command=="GateDodgeCounter")return GateDodgeCounter();
   if(command=="GateDodgePoll")return File.ReadAllText(GateDodgeReport);
   if(command=="GateInputPrepare")return GateInputPrepare();
   if(command=="GateInputCast")return GateInputCast();
   if(command=="GateInputResult")return GateInputResult();
   if(command=="JourneyAudioRecord")return JourneyAudioRecord();
   if(command=="JourneyAudioFinish")return JourneyAudioFinish();
   if(command=="RenewAllAudio")return RenewAllAudio();
   if(command=="JourneyAudio")return JourneyAudio();
   if(command=="JourneyAudioState")return JourneyAudioState();
   if(command=="MineFight")return MineFight();
   if(command=="MineFightPoll")return File.ReadAllText(MineFightReport);
   if(command=="FieldRewardCheck")return FieldRewardCheck();
   if(command=="State")return State();
   if(command=="EscortFollowResume")return EscortFollowAuthor(true);
   if(command=="EscortFollowAuthor")return EscortFollowAuthor();
   if(command=="SouthGateWalls")return SouthGateWalls();
   if(command=="SouthGateBindings")return SouthGateBindings();
   if(command=="SouthGateView")return SouthGateView();
   if(command=="SouthGateGrounding")return SouthGateGrounding();
   if(command=="SouthGateCheck")return SouthGateCheck();
   if(command=="SouthGatePoll")return File.ReadAllText(GateReport);
   if(command=="SouthGateWalk")return SouthGateWalk();
   if(command=="SouthGateWalkPoll")return File.ReadAllText(GateWalkReport);
   if(command=="SouthGateReload")return SouthGateReload();
   if(command=="SouthGateAuthor")return SouthGateAuthor();
   if(command=="SouthGateSurvey")return SouthGateSurvey();
   if(command=="RoadDriveDebug")return RoadDriveDebug();
   if(command=="RoadDepartureAlign")return RoadDepartureAlign();
   if(command=="RoadDriveStart")return RoadDriveStart();
   if(command=="RoadDrivePoll")return File.ReadAllText(DriveReport);
   if(command=="RoadRestAuthor")return RoadRestAuthor();
   if(command=="RoadRestPrepare")return RoadRestPrepare();
   if(command=="RoadRestCheck")return RoadRestCheck();
   if(command=="RoadRestReload")return RoadRestReload();
   if(command=="RoadExit")return RoadExit();
   if(command=="RoadInteractionCheck")return RoadInteractionCheck();
   if(command=="RoadReloadCheck")return RoadReloadCheck();
   if(command=="RoadRewards")return RoadRewards();
   if(command=="RoadFinish")return RoadFinish();
   if(command=="RoadAuthor")return RoadAuthor();
   if(command=="RoadAudit")return RoadAudit();
   if(command=="EscortFollowCheck")return EscortFollowCheck();
   if(command=="EscortFollowPoll")return File.ReadAllText(FollowReport);
   if(command=="TransportCheck")return TransportCheck();
   if(command=="TransportPoll")return File.ReadAllText(TransportReport);
   if(command=="TransportLoadout")return TransportLoadout();
   if(command=="EscortLoadReloadCheck")return EscortLoadReloadCheck();
   if(command=="TransportSite")return TransportSite();
   if(command=="EscortContractReloadCheck")return EscortContractReloadCheck();
   if(command=="EscortView"){
    var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
    s.Teleport(s.EscortCompanion.position+new Vector3(-.5f,1,-2.3f),0);return "At merchant visual review position";
   }
   if(command=="EscortPromptAnchor")return EscortPromptAnchor();
   if(command=="EscortContractSite")return EscortContractSite();
   if(command=="EscortContractCheck")return EscortContractCheck();
   if(command=="MotionAudit")return MotionAudit();
   if(command=="SaveFailureChecks")return SaveFailureChecks();
   if(command=="GukInput")return Vfx120PlayerInputAudit.StartJourneyGuk();
   if(command=="GukInputPrepare")return GukInputPrepare();
   if(command=="GrowthView")return GrowthView();
   if(command=="GrowthCheck")return GrowthCheck();
   if(command=="GrowthPoll")return File.ReadAllText(GrowthReport);
   if(command=="GrowthLesson")return GrowthLesson();
   if(command=="RenReloadCheck")return RenReloadCheck();
   if(command=="RenCheck")return RenCheck();
   if(command=="RenPoll")return File.ReadAllText(RenReport);
   if(command=="GukCoreCheck"){var report=DemoFieldSpellChecks.Run();File.WriteAllText("../Art/World/PineRest/guk_core_checks.json",report);return report;}
   if(command=="GukCheck")return GukCheck();
   if(command=="GukPoll")return File.ReadAllText(GukReport);
   if(command=="GukSite")return GukSite();
   if(command=="ForestReloadCheck")return ForestReloadCheck();
   if(command=="ForestCheck")return ForestCheck();
   if(command=="ForestPoll")return File.ReadAllText(ForestReport);
   if(command=="ForestBindings")return ForestBindings();
   if(command=="Forest")return Forest();
   if(command=="ForestSurvey")return ForestSurvey();
   if(command=="InteractionCheck")return InteractionCheck();
   if(command=="InteractionPoll")return File.ReadAllText(InteractionReport);
   if(command=="MenuInputCheck")return MenuInputCheck();
   if(command=="MenuInputPoll")return File.ReadAllText(MenuInputReport);
   if(command=="CollectibleViews")return CollectibleViews();
   if(command=="CollectibleViewCheck")return CollectibleViewCheck();
   if(command=="MenuIcons")return MenuIcons();
   if(command=="Needles")return Needles();
   if(command=="Trees")return Trees();
   if(command=="MenuOpen"){Object.FindFirstObjectByType<ProloguePauseMenu>().SetOpen(true);return "opened";}
   if(command=="VisualAudit")return VisualAudit();
   if(command=="Atmosphere")return Atmosphere();
   if(command=="NpcAttention")return NpcAttention();
   if(command=="EvidenceProps")return EvidenceProps();
   if(command=="EvidenceState")return EvidenceState();
   if(command=="SafeRest")return SafeRest();
   if(command=="CityEntry")return CityEntry();
   if(command=="CityEntryView")return CityEntryView();
   if(command=="CityEntryPrepare")return CityEntryPrepare();
   if(command=="CityEntryWalk")return CityEntryWalk();
   if(command=="CityEntryPoll")return File.ReadAllText(CityWalkReport);
   if(command=="SafeRestInput")return SafeRestInput();
   if(command=="SafeRestPoll")return File.ReadAllText(SafeRestReport);
   if(command=="EvidenceCapture")return EvidenceCapture();
   if(command=="NpcAttentionState")return NpcAttentionState(false);
   if(command=="NpcAttentionInteract")return NpcAttentionState(true);
   if(command=="RelayPaths")return RelayPaths();
   if(command=="RelayShelter")return RelayShelter();
   if(command=="RelayActor")return RelayActor();
   if(command=="RelayView")return RelayView();
   if(command=="EnemyMotion")return EnemyMotion();
   if(command=="HudSkin")return HudSkin();
   if(command=="ArtProps")return ArtProps();
   if(command=="TargetPrepare") {
    var targetSession=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||targetSession.Progress==null||!targetSession.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
    var targetEnemy=targetSession.Encounters[0];targetSession.Teleport(targetEnemy.transform.position-Vector3.forward*14,0);
    var targetLock=targetSession.Player.GetComponent<LockOn>();if(targetLock.IsLocked)targetLock.Toggle();targetLock.Toggle();SessionState.SetFloat("JourneyTargetHp",targetEnemy.GetComponent<EnemyVitals>().Hp);
    return "locked="+targetLock.IsLocked+" hp="+targetEnemy.GetComponent<EnemyVitals>().Hp;
   }
   if(command=="TargetAttack")return Vfx120PlayerInputAudit.StartJourneyAttack();
   if(command=="TargetResult") {var targetHp=Object.FindFirstObjectByType<PrologueSession>().Encounters[0].GetComponent<EnemyVitals>().Hp;string hitResult="before="+SessionState.GetFloat("JourneyTargetHp",-1)+" after="+targetHp+" actualDamage="+(SessionState.GetFloat("JourneyTargetHp",-1)-targetHp);File.WriteAllText("../Art/World/PineRest/target_damage.txt",hitResult);return hitResult;}

   if(command=="PlayerVisual") {
    if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey edit only");
    var visualCamera=new SerializedObject(Object.FindFirstObjectByType<CameraRigController>());visualCamera.FindProperty("_config").objectReferenceValue=AssetDatabase.LoadMainAssetAtPath(DevSceneKit.DefaultConfigPath);visualCamera.ApplyModifiedPropertiesWithoutUndo();
    string installed=DosaV2PlayableDraftBuilder.InstallJourneyVisual(GameObject.Find("PlayerRig"));
    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return installed;
   }
   if(command=="Capture") {if(!EditorApplication.isPlaying)throw new Exception("Play required");string path=Path.GetFullPath("../Art/World/PineRest/journey_game.png");ScreenCapture.CaptureScreenshot(path);return "Capture queued: "+path;}

   if(command=="Commission")return Commission();
   if(command=="CommissionCheck")return CommissionCheck();
   if(command=="Menu") {if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey edit only");var menuSession=Object.FindFirstObjectByType<PrologueSession>();var menu=menuSession.GetComponent<ProloguePauseMenu>()??menuSession.gameObject.AddComponent<ProloguePauseMenu>();menu.Session=menuSession;EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());return "Pause/save/quit menu connected";}
   if(command=="MenuCheck") {if(!EditorApplication.isPlaying)throw new Exception("Play only");var menu=Object.FindFirstObjectByType<ProloguePauseMenu>();menu.SetOpen(true);bool paused=menu.IsOpen&&Time.timeScale==0;bool saved=menu.SaveProgress();menu.SetOpen(false);return "paused="+paused+" saved="+saved+" resumed="+(!menu.IsOpen&&Time.timeScale>0);}

   if(command=="Walk") {if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Scene)throw new Exception("Journey only");return ProloguePlayAudit.Begin();}
   if(command=="WalkPoll")return File.Exists("../Art/World/PineRest/journey_walk.json")?File.ReadAllText("../Art/World/PineRest/journey_walk.json"):"Pending";
   if(command=="InputStart")return Vfx120PlayerInputAudit.StartJourney();
   if(command=="InputPoll")return Vfx120PlayerInputAudit.Poll();
   if(command=="Opening")return Opening();
   if(command=="AuditStart")return AuditStart();
   if(command=="RuntimeChecks")return RuntimeChecks();
   if(command!="Build")throw new ArgumentException(command);
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Save current scene first");
   if(AssetDatabase.LoadMainAssetAtPath(Scene)!=null){EditorSceneManager.OpenScene(Scene);return "Existing journey opened; no regeneration";}
   if(!AssetDatabase.CopyAsset("Assets/_Project/Scenes/World/W_ArtStudy_PineRest.unity",Scene))throw new Exception("Source copy failed");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/_Project/Art/World","PineRestGame");
   var scene=EditorSceneManager.OpenScene(Scene);
   foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))Object.DestroyImmediate(c.gameObject);
   var marker=GameObject.Find("ART STUDY ONLY - Pine Rest - no gameplay");if(marker!=null)Object.DestroyImmediate(marker);
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).First(c=>c.name=="Valley");
   Vector3 Ground(float x,float z,float lift=0){if(!ground.Raycast(new Ray(new Vector3(x,200,z),Vector3.down),out var h,400))throw new Exception("No ground "+x+","+z);return h.point+Vector3.up*lift;}
   var start=Ground(0,8,1.1f);DevSceneKit.InstantiateRig(scene,start,0);
   var player=Object.FindFirstObjectByType<PlayerMotor>().transform;
   var content=ScriptableObject.CreateInstance<PrologueContentSO>();content.SaveSlot="pine-rest-journey-v1";content.StartPosition=start;content.StartYaw=0;
   content.Points=new[]{
    new PrologueContentSO.Point{Id="InnRest",Kind=PrologueInteractionKind.Rest,Position=Ground(-14,33),Prompt="주막에서 숨 고르기",Text="",Radius=3},
    new PrologueContentSO.Point{Id="BurntCord",Kind=PrologueInteractionKind.Evidence,Position=Ground(2,15),Prompt="그을린 끈 살피기",Text="끈 안쪽까지 검게 탔다. 광산에서 본 매듭과 같다."},
    new PrologueContentSO.Point{Id="WorkerSatchel",Kind=PrologueInteractionKind.Currency,Position=Ground(-8,62),Prompt="해진 주머니 살피기",Text="벌목꾼의 이름이 안감에 적혀 있다.",Currency=24}
   };
   content.MainPath=new[]{start,Ground(0,20,1),Ground(-14,33,1),Ground(4,55,1),Ground(10,75,1)};
   AssetDatabase.CreateAsset(content,Folder+"/Content.asset");
   var root=new GameObject("Journey Gameplay");var session=root.AddComponent<PrologueSession>();session.Content=content;session.Player=player;session.Wiring=Object.FindFirstObjectByType<CombatLoopWiring>();root.AddComponent<PrologueInteraction>().Session=session;
   var config=Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath));AssetDatabase.CreateAsset(config,Folder+"/Combat.asset");
   var enemies=new PrologueEncounter[2];
   for(int i=0;i<2;i++){
    var pos=Ground(i==0?8:14,i==0?72:100,1);var go=DevSceneKit.CreateEnemy("Journey_Beast_"+i,pos,config,i==0?Element.Wood:Element.Fire,player,player.GetComponent<PlayerVitals>(),true);go.transform.SetParent(root.transform);go.layer=2;
    var ec=go.GetComponent<EnemyController>();PrologueBuilder.Set(ec,"_attackMode",i==0?EnemyController.AttackMode.MeleeOnly:EnemyController.AttackMode.RangedOnly);PrologueBuilder.Set(ec,"_environmentOcclusion",true);
    var nav=go.AddComponent<NavMeshAgent>();nav.radius=.4f;nav.height=2;nav.baseOffset=1;nav.angularSpeed=180;nav.acceleration=8;
    var encounter=go.AddComponent<PrologueEncounter>();encounter.Id="JourneyBeast"+i;encounter.Session=session;encounter.Player=player;encounter.Ranged=i==1;encounter.PatrolPoints=new[]{pos,pos+Vector3.forward*3};enemies[i]=encounter;
   }
   session.Encounters=enemies;DevSceneKit.WireRigSeam(enemies[0].GetComponent<EnemyController>(),enemies[0].GetComponent<EnemyVitals>(),enemies.Select(e=>e.GetComponent<EnemyVitals>()).ToArray());
   PrologueBuilder.Set(Object.FindFirstObjectByType<CombatLifetimeScope>(),"_prologue",session);
   var driver=Object.FindFirstObjectByType<WorldLookDriver>();PrologueBuilder.Set(driver,"_skyCamera",Camera.main);driver.Apply();
   var surface=root.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Volume;surface.center=new Vector3(0,20,65);surface.size=new Vector3(110,80,145);surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.layerMask=1;surface.overrideVoxelSize=true;surface.voxelSize=.25f;surface.BuildNavMesh();
   if(surface.navMeshData==null)throw new Exception("Navigation bake failed");surface.RemoveData();var data=Object.Instantiate(surface.navMeshData);AssetDatabase.CreateAsset(data,Folder+"/Navigation.asset");surface.navMeshData=data;surface.AddData();
   EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();return State();
  }
  static string State(){var s=Object.FindFirstObjectByType<PrologueSession>();return "scene="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path+"; playing="+EditorApplication.isPlaying+"; "+DevSceneKit.AssertSingleRig()+"; session="+(s!=null)+"; progress="+(s!=null&&s.Progress!=null)+"; player="+(s!=null?s.Player.position.ToString():"none")+"; encounters="+(s!=null?s.Encounters.Length:0);}
 }
}
