using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ContinuationOutput258=Output+"/Continuation258";
  const string KeeperGuidance258="주막 주인: 산길에서 오셨구려. 방이 비었으니 저 문으로 들어가 몸부터 녹이시오.\n\n장비를 구하거나 손보려거든 역참 너머 벌목마을로 가시오. 주민들이 아직 작업장을 지키고 있소. 숲 사정은 벌목꾼과 약초꾼에게 물어보면 될 게요.";
  static Keyboard journeyKeyboard258, priorKeyboard258;
  static Mouse journeyMouse258, priorMouse258;
  static Key[] journeyKeys258=Array.Empty<Key>();
  static Vector2 journeyLook258;
  static readonly List<Vector3> journeyRoute258=new List<Vector3>();
  static readonly List<Vector3> journeyTrail258=new List<Vector3>();
  static int journeyNode258;
  static double journeyBegin258, journeyMovedAt258, journeySample258;
  static Vector3 journeyPrevious258, journeyMotionPoint258;
  static float journeyDistance258;
  static string journeyStatus258="NOT_STARTED", journeyLeg258;
  static bool journeyBackground258;
  [Serializable] sealed class JourneyReceipt258
  {
   public string status,leg,scope; public int node,totalNodes; public float seconds,metres;
   public Vector3 position,target; public Vector3[] trail; public string[] contacts;
  }
  static readonly HashSet<string> journeyContacts258=new HashSet<string>();
  static void ConfigurePace258(WorldMacroPlaytestSession session)
  {
   var scene=FrontageScene249();
   string folder=Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Continuation258";
   Directory.CreateDirectory(folder);
   string path=folder+"/PlayerLocomotion.asset";
   var profile=AssetDatabase.LoadAssetAtPath<PlayerLocomotionProfileSO>(path);
   if(profile==null)
   {
    profile=Object.Instantiate(session.Walker.Motor.LocomotionProfile);
    profile.name="CompactCandidateLocomotion258";
    AssetDatabase.CreateAsset(profile,path);
   }
   profile.WalkSpeed=4.5f;
   session.Walker.Motor.ConfigureLocomotion(profile);
   EditorUtility.SetDirty(profile);EditorUtility.SetDirty(session.Walker.Motor);
  }
  static WorldMacroPlaytestSession JourneySession258()
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(!EditorApplication.isPlaying||s==null||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   return s;
  }
  public static string Journey258(string command)
  {
   Directory.CreateDirectory(ContinuationOutput258);
   if(command=="status")return journeyStatus258;
   if(command=="stop"){FinishJourney258("STOPPED");return journeyStatus258;}
   if(command=="pace")
   {
    var scene=FrontageScene249();var s=VillageSession();ConfigurePace258(s);
    AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    string result="Candidate-only walk 4.5m/s; run "+s.Walker.Motor.LocomotionProfile.RunSpeed+"m/s; shared profile unchanged. Animation/route timing requires fresh Play.";
    File.WriteAllText(ContinuationOutput258+"/pace.txt",result);return result;
   }
   if(command=="dialogue")
   {
    var scene=FrontageScene249();var s=VillageSession();
    var keeper=s.Content.Points.Single(p=>p.Id=="geumpyo_innkeeper");
    keeper.Text=KeeperGuidance258;
    EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();
    File.WriteAllText(ContinuationOutput258+"/dialogue.txt","PASS candidate keeper now points to the actual rest door and village services; canonical content unchanged.");
    return File.ReadAllText(ContinuationOutput258+"/dialogue.txt");
   }
   var session=JourneySession258();
   if(journeyKeyboard258!=null)throw new Exception("Journey already running");
   if(command!="mine"&&command!="village"&&command!="sanctuary"&&command!="herb"&&command!="herb-reverse")throw new ArgumentException(command);
   var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseWhenNeutral();
   var points=new List<Vector3>();
   if(command=="mine")
   {
    var cave=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
    points.AddRange(cave.main.Skip(1));points.AddRange(cave.mouth.Skip(1));points.Add(session.Content.InnCheckpointFeet);
   }
   else if(command=="village")
   {
    var ledger=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(VillageOutput+"/ledger.json"));
    for(int i=0;i<ledger.main.Length;i+=8)points.Add(ledger.main[i]);points.Add(ledger.main.Last());
   }
   else
   {
    var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText((command.StartsWith("herb",StringComparison.Ordinal)?BranchOutput259:ProgressionOutput251)+"/route-plan.json"));
    var ground=FinalSurface(SceneManager.GetActiveScene());
    foreach(var route in plan.routes)foreach(var p in route.points)points.Add(ground(p.x,p.y).point);
    if(command=="herb-reverse")points.Reverse();
   }
   journeyRoute258.Clear();journeyTrail258.Clear();journeyContacts258.Clear();
   var from=session.Walker.Body.transform.position;
   if(!NavMesh.SamplePosition(from,out var source,3,NavMesh.AllAreas))throw new Exception("Player is off navigation: "+from);
   journeyRoute258.Add(source.position);
   foreach(var point in points)
   {
    if(!NavMesh.SamplePosition(point,out var dest,3,NavMesh.AllAreas))throw new Exception("Missing navigation at "+point);
    var path=new NavMeshPath();
    if(!NavMesh.CalculatePath(journeyRoute258.Last(),dest.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)throw new Exception("Disconnected navigation to "+point);
    foreach(var corner in path.corners.Skip(1))if(Vector3.Distance(journeyRoute258.Last(),corner)>.2f)journeyRoute258.Add(corner);
   }
   journeyLeg258=command;journeyNode258=1;journeyDistance258=0;journeyPrevious258=journeyMotionPoint258=from;
   journeyBegin258=journeyMovedAt258=journeySample258=EditorApplication.timeSinceStartup;
   journeyBackground258=Application.runInBackground;Application.runInBackground=true;
   priorKeyboard258=Keyboard.current;priorMouse258=Mouse.current;
   journeyKeyboard258=InputSystem.AddDevice<Keyboard>("CompactJourney258");journeyKeyboard258.MakeCurrent();
   journeyMouse258=InputSystem.AddDevice<Mouse>("CompactJourney258Look");journeyMouse258.MakeCurrent();
   journeyKeys258=Array.Empty<Key>();journeyLook258=Vector2.zero;
   EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
   InputSystem.onBeforeUpdate+=JourneyInput258;EditorApplication.update+=JourneyTick258;
   journeyStatus258="RUNNING "+command;return journeyStatus258+" navigation corners="+journeyRoute258.Count+"; no player position writes";
  }
  static void JourneyInput258()
  {
   if(journeyKeyboard258==null||InputState.currentUpdateType==InputUpdateType.BeforeRender)return;
   InputSystem.QueueStateEvent(journeyKeyboard258,new KeyboardState(journeyKeys258));
   InputSystem.QueueStateEvent(journeyMouse258,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),delta=journeyLook258});
  }
  static void JourneyTick258()
  {
   try
   {
    var s=JourneySession258();var p=s.Walker.Body.transform.position;double now=EditorApplication.timeSinceStartup;
    if(now-journeyBegin258<.5){journeyKeys258=Array.Empty<Key>();journeyLook258=Vector2.zero;return;}
    if(!Application.isFocused){journeyKeys258=Array.Empty<Key>();journeyLook258=Vector2.zero;if(now-journeyMovedAt258>25)throw new Exception("Game/application focus unavailable");return;}
    float moved=Vector3.Distance(p,journeyPrevious258);if(moved>5)throw new Exception("Noncontinuous displacement/death: "+moved);
    journeyDistance258+=moved;journeyPrevious258=p;
    if(Vector3.Distance(p,journeyMotionPoint258)>.4f){journeyMotionPoint258=p;journeyMovedAt258=now;}
    if(now-journeyMovedAt258>14)throw new Exception("Stalled at "+p+" toward "+journeyRoute258[journeyNode258]+"; blocked="+s.Walker.Motor.EnvironmentalInputBlocked+" grounded="+s.Walker.Body.isGrounded);
    if(now-journeyBegin258>1000)throw new Exception("Route timeout");
    while(journeyNode258<journeyRoute258.Count)
    {
     var waypoint=journeyRoute258[journeyNode258];float arrival=.65f;
     // POI waypoints may be the occupied actor centre. Reach its physical approach,
     // then continue on the next leg instead of demanding passage through its collider.
     foreach(var actor in s.Actors)
      if(actor!=null&&actor.gameObject.activeInHierarchy&&Vector3.ProjectOnPlane(actor.transform.position-waypoint,Vector3.up).magnitude<.7f)
       arrival=1.25f;
     if(Vector3.ProjectOnPlane(waypoint-p,Vector3.up).magnitude>=arrival)break;
     journeyNode258++;
    }
    if(journeyNode258>=journeyRoute258.Count){FinishJourney258("PASS_INPUT_TRAVERSAL");return;}
    var delta=journeyRoute258[journeyNode258]-p;delta.y=0;
    float angle=Vector3.SignedAngle(s.Walker.Body.transform.forward,delta,Vector3.up);
    // Queue normal input through the existing PlayerMotor. No teleport, motor override or time scaling.
    journeyLook258=new Vector2(Mathf.Clamp(angle*.35f,-25,25),0);
    journeyKeys258=Mathf.Abs(angle)<55?new[]{Key.W}:Array.Empty<Key>();
    if(!string.IsNullOrEmpty(s.FocusedId))journeyContacts258.Add(s.FocusedId);
    journeyStatus258="RUNNING "+journeyLeg258+" "+journeyNode258+"/"+journeyRoute258.Count+" "+journeyDistance258.ToString("F1")+"m; pos="+p;
    if(now-journeySample258>2){journeySample258=now;journeyTrail258.Add(p);WriteJourney258();}
   }
   catch(Exception e){FinishJourney258("FAIL "+e.Message);}
  }
  static void WriteJourney258()
  {
   var receipt=new JourneyReceipt258{status=journeyStatus258,leg=journeyLeg258,scope="Virtual keyboard/mouse through live PlayerMotor; NavMesh-guided automatic route. Not human wayfinding, manual combat, GPU timing or visual approval.",node=journeyNode258,totalNodes=journeyRoute258.Count,seconds=(float)(EditorApplication.timeSinceStartup-journeyBegin258),metres=journeyDistance258,position=journeyPrevious258,target=journeyRoute258.Count>0?journeyRoute258[Math.Min(journeyNode258,journeyRoute258.Count-1)]:Vector3.zero,trail=journeyTrail258.ToArray(),contacts=journeyContacts258.ToArray()};
   File.WriteAllText(ContinuationOutput258+"/journey-"+journeyLeg258+".json",JsonUtility.ToJson(receipt,true));
  }
  static void FinishJourney258(string status)
  {
   EditorApplication.update-=JourneyTick258;InputSystem.onBeforeUpdate-=JourneyInput258;
   journeyKeys258=Array.Empty<Key>();journeyLook258=Vector2.zero;
   if(journeyKeyboard258!=null){InputSystem.RemoveDevice(journeyKeyboard258);journeyKeyboard258=null;}
   if(journeyMouse258!=null){InputSystem.RemoveDevice(journeyMouse258);journeyMouse258=null;}
   if(priorKeyboard258!=null&&priorKeyboard258.added)priorKeyboard258.MakeCurrent();
   if(priorMouse258!=null&&priorMouse258.added)priorMouse258.MakeCurrent();
   Application.runInBackground=journeyBackground258;journeyStatus258=status;WriteJourney258();
  }
 }
}
