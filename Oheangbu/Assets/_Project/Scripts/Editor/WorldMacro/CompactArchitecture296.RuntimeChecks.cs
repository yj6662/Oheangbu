using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 [InitializeOnLoad]
 public static class ArchitectureRuntimeChecks296
 {
  const string StateKey="Architecture296.Runtime",ScenePath="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  static string Folder=>Path.GetFullPath(Application.dataPath+"/../../Art/World/Compact/Rebuild/Architecture296/Runtime");
  static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
  static CompactArchitectureSheetSO Sheet=>AssetDatabase.LoadAssetAtPath<CompactArchitectureSheetSO>("Assets/_Project/Art/World/Architecture296/Data/Architecture.asset");
  [Serializable] sealed class Stamp {public string Path,Hash;}
  [Serializable] sealed class MoveTrace {public string Arena,Keys;public int Leg,Frame;public Vector3 Feet;}
  [Serializable] sealed class State
  {
   public bool Active,Restart,Finished,Restored,PriorDirect,PriorDirectPresent;
   public string Status,Suffix,PriorSuffix,PriorUi,PriorStartup,SavePath,SceneHash,Expected;
   public int Phase,Arena,WalkLeg,MoveSamples,PlayStarts;public double Began,At,Last;
   public Vector3 WalkStart,WalkTarget,UnsafePosition;public List<string> Checks=new List<string>(),Failures=new List<string>();
   public List<MoveTrace> Path=new List<MoveTrace>();
   public List<Stamp> Saves=new List<Stamp>();
   public string Scope="Actual candidate Play boots and virtual WASD through unchanged PlayerMotor/CharacterController. Initial arena placement uses Teleport. Boss deaths use EnemyVitals damage API; death recovery uses fatal-fall API. This is not native drawing, hand-played combat or story completion.";
  }
  static State state;
  static Keyboard inputKeyboard,priorKeyboard;
  static Mouse inputMouse,priorMouse;
  static InputActionAsset inputActions;
  static InputDevice[] priorDevices;
  static Key[] keys=Array.Empty<Key>();
  static ArchitectureRuntimeChecks296()
  {
   string data=SessionState.GetString(StateKey,"");if(!string.IsNullOrEmpty(data))state=JsonUtility.FromJson<State>(data);
   EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;
  }
  public static string Run(string command)
  {
   if(command=="status")return Summary();
   if(command=="abort"){if(state?.Active==true){Check(false,"explicit abort");Stop(false);}return Summary();}
   if(command!="start")throw new ArgumentException(command);
   if(state?.Active==true||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty||SceneManager.GetActiveScene().path!=ScenePath)throw new Exception("Saved296 Edit scene without active runtime test required");
   var s=Session;Directory.CreateDirectory(Folder);
   state=new State{Active=true,Status="Starting",Began=EditorApplication.timeSinceStartup,At=EditorApplication.timeSinceStartup,
    Suffix="_architecture296_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),PriorSuffix=s.TestSaveSuffix,PriorUi=SessionState.GetString("PlaytestUiReviewSuffix","__missing__"),
    PriorStartup=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),PriorDirect=SessionState.GetBool("Compact270.DirectPlay",false),
    PriorDirectPresent=SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true),SceneHash=Hash(File.ReadAllBytes(ScenePath))};
   foreach(var path in Directory.GetFiles(Application.persistentDataPath,"*.json*",SearchOption.TopDirectoryOnly))state.Saves.Add(new Stamp{Path=path,Hash=Hash(File.ReadAllBytes(path))});
   state.SavePath=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+state.Suffix+".json");StartPlay();return Summary();
  }
  static string Summary()=>state==null?"No296 runtime run":state.Status+" phase="+state.Phase+" checks="+state.Checks.Count+" failures="+state.Failures.Count+" moves="+state.MoveSamples+" boots="+state.PlayStarts+" restored="+state.Restored;
  static void Persist(){SessionState.SetString(StateKey,JsonUtility.ToJson(state));File.WriteAllText(Folder+"/checks.json",JsonUtility.ToJson(state,true));}
  static void Check(bool ok,string detail){state.Checks.Add((ok?"PASS ":"FAIL ")+detail);if(!ok)state.Failures.Add(detail);}
  static void StartPlay()
  {
   var s=Session;s.TestSaveSuffix=state.Suffix;SessionState.SetString("PlaytestUiReviewSuffix",state.Suffix);CompactLoadingStartup270.UseCurrent();
   state.Restart=false;state.Status="Running";state.At=EditorApplication.timeSinceStartup;state.Last=state.At;state.PlayStarts++;Persist();EditorApplication.isPlaying=true;
  }
  static void Stop(bool restart){EndInput();state.Restart=restart;state.Status=restart?"Restarting":"Stopping";Persist();EditorApplication.isPlaying=false;}
  static void Changed(PlayModeStateChange mode)
  {
   if(state?.Active!=true||mode!=PlayModeStateChange.EnteredEditMode)return;
   if(state.Restart)
   {
    if(state.Phase==32)
    {
     var saved=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(state.SavePath));saved.ledger.position=state.UnsafePosition;saved.ledger.hasPosition=true;
     File.WriteAllText(state.SavePath,JsonUtility.ToJson(saved));File.WriteAllText(Folder+"/invalid-position-fixture.json",JsonUtility.ToJson(saved,true));
    }
    if(state.Phase==4)
    {
     string source=Path.GetFullPath(Application.dataPath+"/../../Art/World/Compact/Rebuild/Escort252/reload-expected.json");
     if(!File.Exists(source)){Check(false,"authentic delivery snapshot missing: "+source);state.Restart=false;Finish();return;}
     var progress=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(source));
     if(!WorldMacroProgress.Valid(progress)||progress.escort.Stage!=DemoEscortStage.Delivered||!progress.escort.DeliveryRewardRecorded){Check(false,"invalid delivery source");state.Restart=false;Finish();return;}
     // A separate second namespace keeps the first measured boss/reload evidence.
     state.Suffix+="_gate";state.SavePath=Path.Combine(Application.persistentDataPath,Session.Content.SaveSlot+state.Suffix+".json");File.WriteAllText(state.SavePath,JsonUtility.ToJson(progress));
    }
    EditorApplication.delayCall+=StartPlay;
   }
   else Finish();
  }
  static void Finish()
  {
   var s=Session;if(s!=null){s.TestSaveSuffix=state.PriorSuffix;if(s.Walker?.Motor!=null)s.Walker.Motor.enabled=true;}
   if(state.PriorUi=="__missing__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",state.PriorUi);
   if(state.PriorDirectPresent)SessionState.SetBool("Compact270.DirectPlay",state.PriorDirect);else SessionState.EraseBool("Compact270.DirectPlay");
   EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.PriorStartup)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
   Check(state.Saves.All(x=>File.Exists(x.Path)&&Hash(File.ReadAllBytes(x.Path))==x.Hash),"all pre-existing save files byte-identical");
   Check(Hash(File.ReadAllBytes(ScenePath))==state.SceneHash,"candidate scene file restored byte-identical");
   state.Active=false;state.Finished=true;state.Restored=true;state.Status=state.Failures.Count==0?"PASS":"FAIL";Persist();
  }
  static void Tick()
  {
   if(state?.Active!=true||state.Status=="Stopping"||state.Status=="Restarting"||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   try
   {
    double now=EditorApplication.timeSinceStartup;if(now-state.Began>900)throw new TimeoutException("296 runtime900s deadline");
    var s=Session;if(s==null||!s.InitializationComplete){if(now-state.At>60)throw new Exception("session initialization timeout");return;}
    if(s.TestSaveSuffix!=state.Suffix)throw new Exception("isolated suffix lost");
    var ui=PlaytestUiRoot.Instance;if(ui!=null){ui.CloseMenu();ui.Gate.ReleaseImmediately();}Time.timeScale=1;s.CombatActive=false;
    var doors=Object.FindObjectsByType<SouthGateDoorPresentation>(FindObjectsSortMode.None).Where(d=>d.isActiveAndEnabled).ToArray();
    if(state.Phase==0)
    {
     Check(!s.HasMumBridge,"normal Mum unlock remains locked");Check(!s.DemoSouthGateOpen&&doors.All(d=>!d.IsOpenRequested),"fresh durable gates closed");
     CheckDoorState(doors,false,"fresh closed");
     Check(Sheet.Arenas.Count(a=>a.TestOnly)==4,"four reserved arenas cannot award progression");
     int currency=s.Progress.ledger.currency;s.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);
     Check(!s.DemoSouthGateOpen&&s.Progress.ledger.currency==currency,"string-only gate death rejected");
     state.Arena=0;state.Phase=1;BeginArena(s);Persist();return;
    }
    if(state.Phase==1)
    {
     var body=s.Walker.Body;var delta=Vector3.ProjectOnPlane(state.WalkTarget-body.transform.position,Vector3.up);
     if(state.Path.Count==0||state.Path.Last().Arena!=Sheet.Arenas[state.Arena].Id||state.Path.Last().Leg!=state.WalkLeg||Vector3.Distance(state.Path.Last().Feet,body.transform.position)>.75f)
      state.Path.Add(new MoveTrace{Arena=Sheet.Arenas[state.Arena].Id,Leg=state.WalkLeg,Frame=Time.frameCount,Feet=body.transform.position,Keys=string.Join("+",keys.Select(k=>k.ToString()))});
     if(delta.magnitude<.35f)
     {
      keys=Array.Empty<Key>();Check(true,"virtual WASD / actual motor arena "+Sheet.Arenas[state.Arena].Id+" leg="+state.WalkLeg);
      if(state.WalkLeg==0){state.WalkLeg=1;state.WalkTarget=state.WalkStart;state.At=now;}
      else {state.Arena++;if(state.Arena<Sheet.Arenas.Length)BeginArena(s);else{EndInput();state.Phase=2;state.At=now;}}
      Persist();return;
     }
     if(now-state.At>60){Check(false,"arena input timeout "+Sheet.Arenas[state.Arena].Id+" at="+body.transform.position);state.Arena++;if(state.Arena<Sheet.Arenas.Length)BeginArena(s);else{EndInput();state.Phase=2;}Persist();return;}
     var direction=delta.normalized;float f=Vector3.Dot(direction,body.transform.forward),r=Vector3.Dot(direction,body.transform.right);
     var pressed=new List<Key>();if(f>.35f)pressed.Add(Key.W);else if(f<-.35f)pressed.Add(Key.S);if(r>.35f)pressed.Add(Key.D);else if(r<-.35f)pressed.Add(Key.A);keys=pressed.ToArray();state.MoveSamples++;return;
    }
    if(state.Phase==2)
    {
     File.WriteAllText(Folder+"/rest-diagnostics.txt","Rest interaction diagnosis "+DateTime.UtcNow.ToString("o")+"\n");
     foreach(string id in new[]{WorldMacroPlaytestSession.CheongryongId,WorldMacroPlaytestSession.SinmokId})KillBoss(s,id);
     RestDiagnostic296(s,"sanctuary_rest251","after both committed boss deaths",s.Walker.Body.transform.position);
     // Committed boss rewards synchronously open the lore folio and block gameplay.
     // Resume on the next Editor tick after the normal UI close at the top of Tick.
     state.Phase=21;state.At=now;Persist();return;
    }
    if(state.Phase==21)
    {
     var rest=s.Content.Points.First(p=>p.Id=="sanctuary_rest251");var checkpoint=s.Content.Checkpoints.First(p=>p.Id==rest.Id);
     // Respawn feet are deliberately2.8m away from the2m interaction radius.
     // Set up on real permanent ground within reach, then use normal visibility
     // and interaction validation. Never treat the respawn point as the prompt.
     bool reachedRest=false;
     foreach(float angle in new[]{225f,180f,270f,0f,90f,45f,135f,315f})
     {
      var candidate=rest.Position+Quaternion.Euler(0,angle,0)*Vector3.forward*1.1f;
      bool safe=s.TrySafeFeet(candidate,out var feet);RestDiagnostic296(s,rest.Id,"angle="+angle+" TrySafeFeet="+safe+" resolved="+feet.ToString("F4"),candidate);
      if(!safe)continue;s.Teleport(feet,checkpoint.Yaw);Physics.SyncTransforms();
      bool can=s.CanInteract(rest.Id);RestDiagnostic296(s,rest.Id,"angle="+angle+" after teleport CanInteract="+can,candidate);
      if(can){reachedRest=true;break;}
     }
     bool checkpointSafe=s.TrySafeFeet(checkpoint.Feet,out var checkpointFeet);
     bool interacted=reachedRest&&s.Interact(rest.Id);
     File.AppendAllText(Folder+"/rest-diagnostics.txt","RESULT reached="+reachedRest+" Interact="+interacted+" checkpointSafe="+checkpointSafe+" checkpoint="+checkpoint.Feet.ToString("F4")+" resolved="+checkpointFeet.ToString("F4")+" SaveError="+s.SaveError+"\n");
     Check(interacted,"actual sanctuary rest after regional construction");
     Check(new[]{"cheongryong","sinmok263"}.All(id=>s.Progress.defeated.Contains(id)),"rest preserves both boss defeats");
     Check(s.TrySafeFeet(s.Walker.Body.transform.position,out _),"checkpoint has dry permanent capsule clearance");
     s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();state.Phase=3;state.At=now;Persist();return;
    }
    if(state.Phase==3&&now-state.At>3)
    {
     Check(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0&&new[]{"cheongryong","sinmok263"}.All(id=>s.Progress.defeated.Contains(id)),"death returns living player without resurrecting bosses");
     Check(s.SaveNow(out _),"complete boss/recovery snapshot saved");state.Expected=JsonUtility.ToJson(s.Progress);File.WriteAllText(Folder+"/boss-expected.json",state.Expected);
     state.Phase=31;Stop(true);return;
    }
    if(state.Phase==31)
    {
     Check(new[]{"cheongryong","sinmok263"}.All(id=>s.Progress.defeated.Contains(id)&&!s.Actors.First(a=>a.Id==id).GetComponent<EnemyVitals>().IsAlive),"second Play restores persistent regional bosses");
     var expected=JsonUtility.FromJson<WorldMacroProgress>(state.Expected);Check(expected.ledger.currency==s.Progress.ledger.currency&&JsonUtility.ToJson(expected.equipment)==JsonUtility.ToJson(s.Progress.equipment),"boss reload retains currency and equipment");
     Check(Vector3.Distance(expected.ledger.position,s.Progress.ledger.position)<.25f,"unchanged terrain revision preserves valid saved position");
     state.UnsafePosition=doors.First().GetComponent<BoxCollider>().bounds.center;Check(!s.TrySafeFeet(state.UnsafePosition,out _),"new closed-wall overlap is rejected as a saved position");
     state.Phase=32;Stop(true);return;
    }
    if(state.Phase==32)
    {
     Check(Vector3.Distance(s.Progress.ledger.position,s.Progress.ledger.checkpointPosition)<.25f&&s.TrySafeFeet(s.Progress.ledger.position,out _),"invalid wall-interior saved position returns to unlocked safe checkpoint");
     state.Phase=4;Stop(true);return;
    }
    if(state.Phase==4)
    {
     var general=s.DemoSouthGateGeneral;var life=general.GetComponent<EnemyVitals>();
     Check(s.DemoSouthGateEncounterAvailable&&life.IsAlive&&!s.DemoSouthGateOpen,"authentic delivered snapshot activates general");
     s.Teleport(general.transform.position+Vector3.back*8,0);s.Cull();general.enabled=true;general.AttackEnabled=false;life.enabled=true;int currency=s.Progress.ledger.currency;
     using(var locked=new FileStream(state.SavePath+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
     {
      life.TakeDamage(float.MaxValue);Check(s.DemoSouthGateSavePending&&!s.DemoSouthGateOpen&&s.Progress.ledger.currency==currency,"failed death save leaves rewards and durable gate closed");
      Check(doors.All(d=>!d.IsOpenRequested),"all perimeter doors stay closed after failed save");Check(!s.SaveNow(out _),"partial save blocked while gate death pending");
     }
     Check(s.SaveNow(out _)&&s.DemoSouthGateOpen,"retry commits actual general death and gate");
     Check(doors.All(d=>d.IsOpenRequested&&!d.OpenAnimationComplete),"all doors wait for their opening animation");
     CheckDoorState(doors,false,"opening still blocks");
     int accepted=s.Progress.ledger.currency;s.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);Check(s.Progress.ledger.currency==accepted,"duplicate general death cannot repay");
     state.Phase=5;state.At=now;Persist();return;
    }
    if(state.Phase==5&&now-state.At>3)
    {
     Check(doors.All(d=>d.OpenAnimationComplete),"all perimeter gates finish actual2.4s opening");
     CheckDoorState(doors,true,"animation finished");
     Check(s.SaveNow(out _),"open gate snapshot saved");state.Expected=JsonUtility.ToJson(s.Progress);File.WriteAllText(Folder+"/gate-expected.json",state.Expected);state.Phase=6;Stop(true);return;
    }
    if(state.Phase==6)
    {
     Check(s.DemoSouthGateOpen&&!s.DemoSouthGateEncounterAvailable&&doors.All(d=>d.OpenAnimationComplete),"final Play restores every perimeter opening and persistent general defeat");
     CheckDoorState(doors,true,"saved opening restored");
     var expected=JsonUtility.FromJson<WorldMacroProgress>(state.Expected);Check(expected.ledger.currency==s.Progress.ledger.currency&&expected.campaign.Facts.All(f=>s.Progress.campaign.Facts.Contains(f)),"gate reload retains currency and facts");Stop(false);
    }
   }
   catch(Exception e){Check(false,e.ToString());Stop(false);}
  }
  static void BeginArena(WorldMacroPlaytestSession s)
  {
   var a=Sheet.Arenas[state.Arena];var q=Quaternion.Euler(0,a.Yaw,0);var start=a.Centre+q*new Vector3(-6,0,-a.ClearSize.y*.25f);var end=a.Centre+q*new Vector3(6,0,a.ClearSize.y*.25f);
   if(!a.TestOnly)
   {
    start=a.Centre+q*new Vector3(-a.ClearSize.x*.3f,0,-a.ClearSize.y*.3f);end=a.Centre+q*new Vector3(a.ClearSize.x*.3f,0,-a.ClearSize.y*.3f);
    var field=new CompactWorldSurface(s.MountainLayout);start.y=field.Sample(start.x,start.z);end.y=field.Sample(end.x,end.z);
   }
   if(!s.TrySafeFeet(start,out start)||!s.TrySafeFeet(end,out end))throw new Exception("Unsafe arena walk endpoints "+a.Id);
   s.Teleport(start,a.Yaw);s.Walker.Motor.enabled=true;BeginInput(s);state.WalkStart=start;state.WalkTarget=end;state.WalkLeg=0;state.At=EditorApplication.timeSinceStartup;state.Last=state.At;
  }
  static void CheckDoorState(SouthGateDoorPresentation[] doors,bool open,string phase)
  {
   int blockers=0,navigation=0;bool valid=doors.Length>0;
   foreach(var door in doors)
   {
    var serialized=new SerializedObject(door);var physical=serialized.FindProperty("_closedBlockers");var nav=serialized.FindProperty("_closedNavigation");
    valid&=physical!=null&&physical.arraySize>0&&nav!=null&&nav.arraySize>0;
    if(physical!=null)for(int i=0;i<physical.arraySize;i++){var c=physical.GetArrayElementAtIndex(i).objectReferenceValue as Collider;valid&=c!=null&&c.enabled==!open;blockers++;}
    if(nav!=null)for(int i=0;i<nav.arraySize;i++){var o=nav.GetArrayElementAtIndex(i).objectReferenceValue as UnityEngine.AI.NavMeshObstacle;valid&=o!=null&&o.enabled==!open;navigation++;}
   }
   Check(valid,"gate collision/navigation synchronized "+phase+" doors="+doors.Length+" physical="+blockers+" carving="+navigation);
  }
  static void BeginInput(WorldMacroPlaytestSession s)
  {
   keys=Array.Empty<Key>();if(inputKeyboard!=null)return;
   priorKeyboard=Keyboard.current;priorMouse=Mouse.current;
   inputKeyboard=InputSystem.AddDevice<Keyboard>("Architecture296Walk");inputMouse=InputSystem.AddDevice<Mouse>("Architecture296Look");inputKeyboard.MakeCurrent();inputMouse.MakeCurrent();
   inputActions=new SerializedObject(s.Walker.Motor).FindProperty("_actions").objectReferenceValue as InputActionAsset;
   if(inputActions!=null){priorDevices=inputActions.devices?.ToArray();inputActions.devices=new InputDevice[]{inputKeyboard,inputMouse};}
   InputSystem.onBeforeUpdate+=FeedInput;
  }
  static void FeedInput()
  {
   if(inputKeyboard==null||InputState.currentUpdateType==InputUpdateType.BeforeRender)return;
   InputSystem.QueueStateEvent(inputKeyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(inputMouse,new MouseState());
  }
  static void EndInput()
  {
   InputSystem.onBeforeUpdate-=FeedInput;keys=Array.Empty<Key>();if(inputActions!=null)inputActions.devices=priorDevices;
   if(inputKeyboard!=null)InputSystem.RemoveDevice(inputKeyboard);if(inputMouse!=null)InputSystem.RemoveDevice(inputMouse);inputKeyboard=null;inputMouse=null;inputActions=null;priorDevices=null;
   if(priorKeyboard!=null&&priorKeyboard.added)priorKeyboard.MakeCurrent();if(priorMouse!=null&&priorMouse.added)priorMouse.MakeCurrent();
  }
  static void KillBoss(WorldMacroPlaytestSession s,string id)
  {
   var actor=s.Actors.First(a=>a.Id==id);var life=actor.GetComponent<EnemyVitals>();s.Teleport(actor.transform.position+Vector3.back*10,0);s.Cull();actor.gameObject.SetActive(true);actor.enabled=true;life.enabled=true;
   var controller=actor.GetComponent<CheongryongCombatController>();controller.enabled=true;controller.AttackEnabled=false;int before=s.Progress.ledger.currency;
   s.EnemyDefeated(id);Check(life.IsAlive&&!s.Progress.defeated.Contains(id)&&s.Progress.ledger.currency==before,"string-only regional death rejected "+id);
   using(var locked=new FileStream(state.SavePath+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
   {
    life.TakeDamage(float.MaxValue,AttackProvenance.Create(s.Walker.Body.gameObject,DamageSource.PlayerDirect,Element.Metal));
    Check(!life.IsAlive&&s.Progress.ledger.currency==before&&!s.SaveNow(out _),"actual regional death waits for atomic save "+id);
   }
   Check(s.SaveNow(out _)&&s.Progress.defeated.Contains(id),"regional death retry persists "+id);int accepted=s.Progress.ledger.currency;s.EnemyDefeated(id);Check(accepted==s.Progress.ledger.currency,"no duplicate boss reward "+id);
  }
  static void RestDiagnostic296(WorldMacroPlaytestSession s,string id,string stage,Vector3 candidate)
  {
   const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   var type=typeof(WorldMacroPlaytestSession);var point=(PrologueContentSO.Point)type.GetMethod("FindInteractionPoint",flags).Invoke(s,new object[]{id});
   var body=s.Walker.Body;var lines=new List<string>{"STAGE "+stage+" candidate="+candidate.ToString("F4")+" player="+body.transform.position.ToString("F4"),
    "FLAGS ready="+type.GetField("ready",flags).GetValue(s)+" pending="+type.GetProperty("HasPendingDefeats",flags).GetValue(s)+" inputBlocked="+s.GameplayInputBlocked+" timeScale="+Time.timeScale+" seated="+s.Walker.Seated+" motor="+s.Walker.Motor.enabled+" motorDrawing="+s.Walker.Motor.IsDrawing+" drawing="+s.Walker.Drawing.InDrawMode+" SaveError="+s.SaveError};
   string PathOf(Transform t){var names=new List<string>();while(t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
   if(point==null)lines.Add("POINT missing");else
   {
    var eye=body.transform.position+Vector3.up*s.Walker.EyeHeight;var target=point.Position+Vector3.up*1.25f;var delta=target-eye;
    lines.Add("POINT "+point.Position.ToString("F4")+" radius="+point.Radius+" distance3D="+Vector3.Distance(body.transform.position,point.Position).ToString("F4")+" eye="+eye.ToString("F4")+" target="+target.ToString("F4"));
    foreach(var hit in Physics.RaycastAll(eye,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
    {var identity=hit.transform.GetComponentInParent<WorldMacroContentPoint>();bool ignored=hit.transform.IsChildOf(body.transform)||identity!=null&&identity.Id==id;lines.Add("LOS "+(ignored?"IGNORED ":"BLOCKER ")+PathOf(hit.transform)+" at="+hit.point.ToString("F4")+" normal="+hit.normal.ToString("F3")+" distance="+hit.distance.ToString("F4")+" identity="+(identity==null?"none":identity.Id)+" bounds="+hit.collider.bounds.ToString("F3"));}
   }
   foreach(var hit in Physics.RaycastAll(candidate+Vector3.up*1.5f,Vector3.down,4,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance).Take(8))
   {
    var feet=hit.point+Vector3.up*.05f;lines.Add("GROUND "+PathOf(hit.transform)+" at="+hit.point.ToString("F4")+" normal="+hit.normal.ToString("F3")+" dry="+(s.Traversal==null||s.Traversal.IsPermanentDrySupport(hit.point,hit.collider)));
    foreach(var c in Physics.OverlapCapsule(feet+Vector3.up*.31f,feet+Vector3.up*1.47f,.27f,~0,QueryTriggerInteraction.Ignore).Where(c=>!c.transform.IsChildOf(body.transform)).Take(8))lines.Add("SAFE_CAPSULE "+PathOf(c.transform)+" bounds="+c.bounds.ToString("F3"));
   }
   File.AppendAllLines(Folder+"/rest-diagnostics.txt",lines);
  }
  static string Hash(byte[] data){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(data)).Replace("-","");}
 }
}
