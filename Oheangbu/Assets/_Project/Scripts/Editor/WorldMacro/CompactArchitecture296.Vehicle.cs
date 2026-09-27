using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.World.Vehicle;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // This is an isolated diagnostic. Only the actual summon may place the car;
 // every subsequent position comes from its ordinary Rigidbody/WheelColliders.
 [InitializeOnLoad]
 public static class ArchitectureVehicleChecks296
 {
  const string Key="Architecture296.Vehicle",ScenePath="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  static string Output=>Path.GetFullPath(Application.dataPath+"/../../Art/World/Compact/Rebuild/Architecture296/Vehicle");
  static string Root=>Path.GetDirectoryName(Output);
  static WorldMacroPlaytestSession Session=>Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
  [Serializable] sealed class Stamp {public string Path,Hash;}
  [Serializable] sealed class Route {public string id;public float width;public bool vehicle;public Vector3[] points;}
  [Serializable] sealed class Routes {public Route[] routes;}
  [Serializable] sealed class Crossing {public string RouteId;public float RouteWidth;public Vector3[] Points;}
  [Serializable] sealed class CrossingFile {public Crossing[] Crossings;}
  [Serializable] sealed class Case
  {
   public string Id,RouteId,Scope;public bool Closed,Reverse;public float Width;
   public Vector3[] Points,Approach;public Vector3 Gate,Inward;public string GatePath;
  }
  [Serializable] sealed class State
  {
   public bool Active,Finished,Restored,Restart,PriorDirect,PriorDirectPresent;
   public string Status,Suffix,PriorSuffix,PriorUi,PriorStartup,SavePath,SceneHash,LastSetup;
   public string Scope="Two actual candidate Play boots using separately copied authentic closed/open progression snapshots. Player-only setup teleport and ordinary summon/seat APIs; late Update supplies SetDriverInput to unmodified Rigidbody/WheelCollider physics. This is automated steering, not human driving. No vehicle placement by the fixture, no profile or route-permission changes. General attack is suppressed only during the closed-door collision probe; its collider remains active.";
   public int Boot,Phase,CaseIndex,PlayStarts,CallsBefore;public double Began,At,LastPersist;
   public Vector3 PlayerStart,Forward;public Case[] Cases;
   public List<string> Checks=new List<string>(),Failures=new List<string>();
   public List<Stamp> Saves=new List<Stamp>(),Sources=new List<Stamp>();
   public List<ArchitectureVehicleDrive296.Receipt> Drives=new List<ArchitectureVehicleDrive296.Receipt>();
  }
  static State state;
  static ArchitectureVehicleDrive296 driver;
  static ArchitectureVehicleChecks296()
  {
   string json=SessionState.GetString(Key,"");if(!string.IsNullOrEmpty(json))state=JsonUtility.FromJson<State>(json);
   EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;
   Application.logMessageReceived+=Log;
  }
  public static string Run(string command)
  {
   if(command=="status")return Summary();
   if(command=="abort"){if(state?.Active==true){Check(false,"explicit vehicle test abort");Stop(false);}return Summary();}
   if(command!="start")throw new ArgumentException(command);
   if(state?.Active==true||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty||SceneManager.GetActiveScene().path!=ScenePath)
    throw new InvalidOperationException("Saved296 Edit scene required without another running Play fixture");
   var session=Session;if(session==null)throw new Exception("Missing candidate session");
   string closed=Path.GetFullPath(Root+"/../Escort252/reload-expected.json"),opened=Root+"/Runtime/gate-expected.json";
   foreach(var file in new[]{closed,opened})if(!File.Exists(file)||!WorldMacroProgress.Valid(JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file))))throw new Exception("Valid authentic snapshot required: "+file);
   Directory.CreateDirectory(Output);
   if(File.Exists(Output+"/checks.json"))File.Copy(Output+"/checks.json",Output+"/checks-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");
   state=new State{Active=true,Status="Starting",Suffix="_vehicle296_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),
    PriorSuffix=session.TestSaveSuffix,PriorUi=SessionState.GetString("PlaytestUiReviewSuffix","__missing__"),
    PriorStartup=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),PriorDirect=SessionState.GetBool("Compact270.DirectPlay",false),
    PriorDirectPresent=SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true),
    SceneHash=Hash(File.ReadAllBytes(ScenePath)),Began=EditorApplication.timeSinceStartup};
   foreach(string file in Directory.GetFiles(Application.persistentDataPath,"*.json*",SearchOption.TopDirectoryOnly))state.Saves.Add(new Stamp{Path=file,Hash=Hash(File.ReadAllBytes(file))});
   foreach(string file in new[]{closed,opened})state.Sources.Add(new Stamp{Path=file,Hash=Hash(File.ReadAllBytes(file))});
   try {PrepareBoot();StartPlay();}catch(Exception e){Check(false,e.ToString());Finish();}
   return Summary();
  }
  static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static string Summary()=>state==null?"No296 vehicle run":state.Status+" boot="+state.Boot+" case="+state.CaseIndex+" phase="+state.Phase+" drives="+state.Drives.Count+" checks="+state.Checks.Count+" failures="+state.Failures.Count+" restored="+state.Restored;
  static void Persist(){if(state==null)return;SessionState.SetString(Key,JsonUtility.ToJson(state));File.WriteAllText(Output+"/checks.json",JsonUtility.ToJson(state,true));state.LastPersist=EditorApplication.timeSinceStartup;}
  static void Check(bool pass,string detail){state.Checks.Add((pass?"PASS ":"FAIL ")+detail);if(!pass)state.Failures.Add(detail);}
  static void PrepareBoot()
  {
   var s=Session;state.SavePath=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+state.Suffix+"_"+state.Boot+".json");
   File.Copy(state.Sources[state.Boot].Path,state.SavePath,false);
   state.CaseIndex=0;state.Phase=0;state.Cases=BuildCases(s,state.Boot==0);state.Restart=true;state.At=EditorApplication.timeSinceStartup;Persist();
  }
  static void StartPlay()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||state?.Active!=true)return;
   Session.TestSaveSuffix=state.Suffix+"_"+state.Boot;SessionState.SetString("PlaytestUiReviewSuffix",Session.TestSaveSuffix);CompactLoadingStartup270.UseCurrent();
   state.Restart=false;state.Status="Running";state.PlayStarts++;state.At=EditorApplication.timeSinceStartup;Persist();EditorApplication.isPlaying=true;
  }
  static void Stop(bool restart)
  {
   if(driver!=null)
   {
    var receipt=driver.Result;
    if(receipt!=null&&!state.Drives.Any(d=>d.Id==receipt.Id))
    {
     if(!driver.Done){receipt.Pass=false;receipt.Detail="Interrupted vehicle probe: "+(state.Failures.LastOrDefault()??state.Status);}
     state.Drives.Add(receipt);
     File.WriteAllText(Output+"/interrupted-"+receipt.Id+".json",JsonUtility.ToJson(receipt,true));
    }
    driver.Halt();
   }
   var s=Session;if(s?.DemoEscortSeat?.Vehicle!=null)s.DemoEscortSeat.Vehicle.StopDriverInputForUi();
   state.Restart=restart;state.Status=restart?"Restarting":"Stopping";Persist();
   if(EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.isPlaying=false;else if(!restart)Finish();
  }
  static void Changed(PlayModeStateChange mode)
  {
   if(state?.Active!=true||mode!=PlayModeStateChange.EnteredEditMode)return;
   if(!state.Restart){Finish();return;}
   try{state.Boot++;PrepareBoot();EditorApplication.delayCall+=StartPlay;}catch(Exception e){Check(false,e.ToString());Finish();}
  }
  static void Finish()
  {
   var s=Session;if(s!=null)s.TestSaveSuffix=state.PriorSuffix;
   if(state.PriorUi=="__missing__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",state.PriorUi);
   if(state.PriorDirectPresent)SessionState.SetBool("Compact270.DirectPlay",state.PriorDirect);else SessionState.EraseBool("Compact270.DirectPlay");
   EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.PriorStartup)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
   bool saves=state.Saves.All(x=>File.Exists(x.Path)&&Hash(File.ReadAllBytes(x.Path))==x.Hash),scene=Hash(File.ReadAllBytes(ScenePath))==state.SceneHash;
   Check(saves,"all pre-existing saves remain byte-identical");Check(scene,"candidate scene file remains byte-identical");
   Check(state.Sources.All(x=>Hash(File.ReadAllBytes(x.Path))==x.Hash),"authentic closed/open snapshot sources unchanged");
   Check(s!=null&&s.TestSaveSuffix==state.PriorSuffix,"ordinary session suffix restored");
   state.Active=false;state.Finished=true;state.Restored=saves&&scene;state.Status=state.Failures.Count==0?"PASS":"FAIL";Persist();
  }
  static void Log(string message,string stack,LogType type)
  {
   if(state?.Active!=true||state.Status=="Stopping"||type!=LogType.Exception)return;
   Check(false,"Play exception: "+message+"\n"+stack);EditorApplication.delayCall+=()=>{if(state?.Active==true)Stop(false);};
  }
  static void Tick()
  {
   if(state?.Active!=true||EditorApplication.isCompiling)return;
   if(state.Restart&&!EditorApplication.isPlayingOrWillChangePlaymode&&EditorApplication.timeSinceStartup-state.At>1){StartPlay();return;}
   if(state.Status!="Running"||!EditorApplication.isPlaying)return;
   try
   {
    double now=EditorApplication.timeSinceStartup;if(now-state.Began>1200)throw new TimeoutException("Vehicle2961200s total deadline");
    var s=Session;if(s==null||!s.InitializationComplete){if(now-state.At>70)throw new Exception("vehicle fixture session startup timeout");return;}
    if(s.TestSaveSuffix!=state.Suffix+"_"+state.Boot)throw new Exception("vehicle fixture suffix changed");
    var seat=s.DemoEscortSeat;var car=seat.Vehicle;var call=s.DemoEscortSummon;
    if(!call.Calling){var ui=PlaytestUiRoot.Instance;if(ui!=null){ui.CloseMenu();ui.Gate.ReleaseImmediately();}}Time.timeScale=1;s.CombatActive=false;
    if(state.Boot==0&&s.DemoSouthGateGeneral!=null)s.DemoSouthGateGeneral.AttackEnabled=false;
    if(state.Phase==0)
    {
     if(now-state.At<1)return;
     Check(s.DemoSouthGateOpen==(state.Boot==1),"boot"+state.Boot+" restores authentic durable gate state");
     Check(!s.HasMumBridge,"normal Mum progression remains locked");
     if(s.DemoSouthGateOpen!=(state.Boot==1))throw new Exception("wrong source gate state");
     Check(car.IsConfigured&&car.Wheels.Length==4,"one configured four-wheel car");NextSetup();return;
    }
    if(state.Phase==1)
    {
     if(car.Speed>.15f||car.Body.angularVelocity.magnitude>.1f){car.StopDriverInputForUi();if(now-state.At>12)throw new Exception("car did not settle between probes");return;}
     if(seat.Occupied&&!seat.TryExit())throw new Exception("cannot safely exit previous drive: "+seat.LastInteraction);
     var c=state.Cases[state.CaseIndex];
     if(!FindStart(s,c,out var feet,out var direction,out var approach,out string reason)){Check(false,c.Id+" setup has no real summon pose: "+reason);NextCase();return;}
     state.PlayerStart=feet;state.Forward=direction;c.Approach=approach;
     s.Teleport(feet,Quaternion.LookRotation(direction).eulerAngles.y);state.LastSetup=reason;state.CallsBefore=call.SuccessfulCalls;
     state.Phase=2;state.At=now;Persist();return;
    }
    if(state.Phase==2)
    {
     if(now-state.At<.7)return;
     if(!call.TryBeginShortcut(out string reason)){state.LastSetup=reason;if(now-state.At>9){Check(false,state.Cases[state.CaseIndex].Id+" actual call refused: "+reason);NextCase();}return;}
     state.Phase=3;state.At=now;Persist();return;
    }
    if(state.Phase==3)
    {
     if(call.Calling){if(now-state.At>8)throw new Exception("summon gesture timeout");return;}
     if(call.SuccessfulCalls!=state.CallsBefore+1||call.IsRecalled){Check(false,state.Cases[state.CaseIndex].Id+" actual call did not place car: "+call.LastResult+" "+call.LastPlacementDiagnostic);NextCase();return;}
     Check(true,state.Cases[state.CaseIndex].Id+" actual gesture placed the incumbent car");
     if(!seat.TryGetSafeExit(out var feet))throw new Exception("summoned car has no real boarding approach");
     s.Teleport(feet,car.transform.eulerAngles.y);state.Phase=4;state.At=now;Persist();return;
    }
    if(state.Phase==4)
    {
     if(now-state.At<.7)return;
     if(!seat.TryBoard()){if(now-state.At>7)throw new Exception("actual seat refused: "+seat.LastInteraction);return;}
     var c=state.Cases[state.CaseIndex];Check(seat.Occupied&&car.DriverPresent,c.Id+" actual seat owns driver input");
     driver=car.gameObject.AddComponent<ArchitectureVehicleDrive296>();driver.Begin(s,c.Id,c.Approach,c.Width,c.Closed,c.Gate,c.Inward,c.GatePath);
     state.Phase=5;state.At=now;Persist();return;
    }
    if(state.Phase==5)
    {
     if(driver==null)throw new Exception("live vehicle driver was lost");
     if(!driver.Done){if(now-state.LastPersist>5){state.Status="Running";Persist();}return;}
     var receipt=driver.Result;state.Drives.Add(receipt);Check(receipt.Pass,receipt.Id+": "+receipt.Detail);
     File.WriteAllText(Output+"/"+receipt.Id+".json",JsonUtility.ToJson(receipt,true));Object.Destroy(driver);driver=null;
     state.Phase=6;state.At=now;Persist();return;
    }
    if(state.Phase==6)
    {
     car.StopDriverInputForUi();if(car.Speed>.12f||car.Body.angularVelocity.magnitude>.1f){if(now-state.At>12)throw new Exception("vehicle did not stop after drive");return;}
     if(!seat.TryExit()){if(now-state.At>12)throw new Exception("actual post-drive safe exit refused: "+seat.LastInteraction);return;}
     Check(!seat.Occupied,state.Cases[state.CaseIndex].Id+" actual safe exit succeeded");NextCase();
    }
   }
   catch(Exception e){Check(false,e.ToString());Stop(false);}
  }
  static void NextSetup(){state.Phase=1;state.At=EditorApplication.timeSinceStartup;Persist();}
  static void NextCase()
  {
   state.CaseIndex++;if(state.CaseIndex<state.Cases.Length){NextSetup();return;}
   if(state.Boot==0){Stop(true);return;}
   Check(state.Drives.Count==11,"all eleven intended wheel-physics probes executed");Stop(false);
  }
  static Case[] BuildCases(WorldMacroPlaytestSession s,bool closed)
  {
   var gate=s.gameObject.scene.GetRootGameObjects().Single(g=>g.name=="CapitalSouthGate253").transform.Find("VictoryGate253");
   if(gate==null)throw new Exception("canonical gate missing");Vector3 p=gate.position,f=Vector3.ProjectOnPlane(gate.forward,Vector3.up).normalized;
   // The living general remains at the centre of the approach. Use a legal
   // lane inside the actual arch to measure the door, without moving the boss
   // or suppressing its physical collider. Open-state traversals use the centre.
   var lane=closed?Vector3.Cross(Vector3.up,f)*2.5f:Vector3.zero;
   Case Door(bool reverse)=>new Case{Id=closed?"south_gate_closed":"south_gate_open_"+(reverse?"out":"in"),RouteId="south_gate_threshold253",Closed=closed,Reverse=reverse,Width=8.6f,
    Points=new[]{p+lane+(reverse?f:-f)*24,p+lane+(reverse?-f:f)*18},Gate=p,Inward=f,GatePath="CapitalSouthGate253/VictoryGate253",Scope="Canonical actual gate threshold; closed probe uses +2.5m lane within the original8.6m opening, avoiding the retained general collider"};
   if(closed)return new[]{Door(false)};
   var cases=new List<Case>{Door(false),Door(true)};
   var crossing=JsonUtility.FromJson<CrossingFile>(File.ReadAllText(Root+"/Generated/crossings.json"));
   int excluded=0;
   foreach(var c in crossing.Crossings)
   {
    var route=s.MountainLayout.Routes.FirstOrDefault(r=>r.Id==c.RouteId);
    if(route==null||route.Traversal==CompactTraversal.FootOnly)
    {excluded++;Check(c.RouteId=="mountain_hwanggyeong_main","excluded authored FootOnly mountain crossing: "+c.RouteId);continue;}
    foreach(bool reverse in new[]{false,true})cases.Add(new Case{Id=c.RouteId+(reverse?"_reverse":"_forward"),RouteId=c.RouteId,Reverse=reverse,Width=c.RouteWidth,
     Points=reverse?c.Points.Reverse().ToArray():c.Points.ToArray(),Scope="Layout.Traversal.Shared. GradeForVehicle="+route.GradeForVehicle+" only records terrain grading, not vehicle permission."});
   }
   if(cases.Count!=10||excluded!=1)throw new Exception("Expected four Shared route IDs and one FootOnly crossing");
   return cases.ToArray();
  }
  static bool FindStart(WorldMacroPlaytestSession s,Case c,out Vector3 feet,out Vector3 forward,out Vector3[] drive,out string reason)
  {
   feet=forward=default;drive=null;reason="";var first=c.Points[0];var initial=Vector3.ProjectOnPlane(c.Points[1]-first,Vector3.up).normalized;
   Vector3[] source;
   var routes=JsonUtility.FromJson<Routes>(File.ReadAllText(Root+"/Generated/routes.json"));var route=routes.routes.FirstOrDefault(r=>r.id==c.RouteId);
   if(!string.IsNullOrEmpty(c.GatePath)&&!c.Closed)
   {
    // This authored road passes through the shared south gate and bends onto
    // both real approaches. A horizontal extension of the gate's Y leaves
    // inward summon probes 2.7-6.7 m above the descending permanent ground.
    var through=routes.routes.Single(r=>r.id=="capital_center__jeokro");
    source=c.Reverse?through.points.ToArray():through.points.Reverse().ToArray();
   }
   else if(route!=null&&string.IsNullOrEmpty(c.GatePath)){source=c.Reverse?route.points.Reverse().ToArray():route.points;}
   else source=new[]{first-initial*40,first,c.Points.Last()+initial*16};
   float at=NearestDistance(source,first);var last=c.Points.Last();float end=NearestDistance(source,last);
   if(end<=at)source=new[]{first-initial*40}.Concat(c.Points).Concat(new[]{last+Vector3.ProjectOnPlane(last-c.Points[c.Points.Length-2],Vector3.up).normalized*16}).ToArray();
   at=NearestDistance(source,first);end=NearestDistance(source,last);float length=Length(source);
   var field=new CompactWorldSurface(s.MountainLayout);
   foreach(float behind in new[]{18f,22f,26f,30f,14f,34f,38f,48f,64f,80f,100f,120f})
   {
    float distance=Mathf.Max(0,at-behind);var candidate=Sample(source,distance,out var direction);direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
    candidate.y=Mathf.Max(candidate.y,field.Sample(candidate.x,candidate.z));
    foreach(float side in new[]{0f,-.7f,.7f})
    {
     var probe=candidate+Vector3.Cross(Vector3.up,direction)*side;
     if(!s.TrySafeFeet(probe,out var safe)){reason="unsupported player approach "+probe;continue;}
     if(!s.DemoEscortSummon.TryFindPlacement(safe,direction,out var pose,out reason))continue;
     if(!CompactMountainAccess.VehicleAllowed(s.gameObject.scene,pose.Position,s.DemoEscortSeat.Vehicle.Hull.bounds.extents.magnitude))continue;
     // The full car starts before the measured bridge entrance. A failed setup
     // never skips a difficult approach by starting halfway across the bridge.
     if(Vector3.Dot(Vector3.ProjectOnPlane(pose.Position-first,Vector3.up),initial)>-4){reason="summon would skip entrance";continue;}
     var points=new List<Vector3>{pose.Position};
     for(float d=distance+6;d<Mathf.Min(length,end+12);d+=1)points.Add(Sample(source,d,out _));
     points.Add(Sample(source,Mathf.Min(length,end+12),out _));
     feet=safe;forward=direction;drive=points.ToArray();reason="real dry bank/road pose; source path starts "+behind+"m before crossing";return true;
    }
   }
   return false;
  }
  static float Length(Vector3[] p){float d=0;for(int i=1;i<p.Length;i++)d+=Vector3.Distance(p[i-1],p[i]);return d;}
  static Vector3 Sample(Vector3[] p,float distance,out Vector3 forward)
  {
   for(int i=1;i<p.Length;i++){float n=Vector3.Distance(p[i-1],p[i]);if(distance<=n){forward=(p[i]-p[i-1]).normalized;return Vector3.Lerp(p[i-1],p[i],n<.001f?0:distance/n);}distance-=n;}
   forward=(p[p.Length-1]-p[p.Length-2]).normalized;return p[p.Length-1];
  }
  static float NearestDistance(Vector3[] p,Vector3 query)
  {
   float walked=0,best=float.MaxValue,result=0;
   for(int i=1;i<p.Length;i++){var a=Vector3.ProjectOnPlane(p[i-1],Vector3.up);var d=Vector3.ProjectOnPlane(p[i]-p[i-1],Vector3.up);float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(query,Vector3.up)-a,d)/Mathf.Max(.0001f,d.sqrMagnitude));float error=(a+d*t-Vector3.ProjectOnPlane(query,Vector3.up)).sqrMagnitude;float n=Vector3.Distance(p[i-1],p[i]);if(error<best){best=error;result=walked+n*t;}walked+=n;}return result;
  }
 }

 [DefaultExecutionOrder(30000)]
 public sealed class ArchitectureVehicleDrive296:MonoBehaviour
 {
  [Serializable] public sealed class Trace
  {public float Seconds,Speed,Progress,Steer,Throttle;public Vector3 Position;public Quaternion Rotation;public int GroundedWheels;public string[] Supports;}
  [Serializable] public sealed class Receipt
  {
   public string Id,Detail,Scope="Actual ordinary WheelCollider physics with automated SetDriverInput after actual summon/seat. No in-drive teleport or Rigidbody pose writes.";
   public bool Pass,Closed;public float Seconds,Metres,PathMetres,MaximumCrossTrack,MaximumTilt,MaximumStep,MinimumY;public int Frames,WheelSamples,BridgeWheelSamples,GateContacts;public Vector3 Start,End,Gate;
   public List<string> Contacts=new List<string>();public List<Trace> Trace=new List<Trace>();
  }
  public Receipt Result {get;private set;}public bool Done {get;private set;}
  WorldMacroPlaytestSession session;WorldMacroPalanquinController car;Vector3[] path;Vector3 previous,gate,inward;string gatePath;float started,lastTrace,lastProgressAt,progress,width;bool closed;float[] arc;
  public void Begin(WorldMacroPlaytestSession s,string id,Vector3[] points,float clearWidth,bool expectClosed,Vector3 gatePosition,Vector3 inwardDirection,string canonicalPath)
  {
   session=s;car=s.DemoEscortSeat.Vehicle;path=points;gate=gatePosition;inward=inwardDirection;gatePath=canonicalPath;closed=expectClosed;width=clearWidth;
   arc=new float[path.Length];for(int i=1;i<path.Length;i++)arc[i]=arc[i-1]+Vector3.Distance(path[i-1],path[i]);
   started=lastTrace=lastProgressAt=Time.time;previous=car.transform.position;
   Result=new Receipt{Id=id,Start=previous,End=previous,Gate=gate,Closed=closed,PathMetres=arc.Last(),MinimumY=previous.y};
  }
  void Update()
  {
   if(Done||car==null)return;float elapsed=Time.time-started;var p=car.transform.position;
   Result.Seconds=elapsed;Result.Frames++;Result.End=p;float step=Vector3.Distance(previous,p);Result.Metres+=step;Result.MaximumStep=Mathf.Max(Result.MaximumStep,step);previous=p;
   float tilt=Vector3.Angle(car.transform.up,Vector3.up);Result.MaximumTilt=Mathf.Max(Result.MaximumTilt,tilt);Result.MinimumY=Mathf.Min(Result.MinimumY,p.y);
   if(!session.DemoEscortSeat.Occupied||session.DemoSouthGateOpen==closed){Complete(false,"seat or durable gate state changed");return;}
   if(step>3){Complete(false,"unexpected discontinuous vehicle motion");return;}
   if(tilt>35){Complete(false,"vehicle tilt exceeds35degrees");return;}
   if(session.GameplayInputBlocked){car.StopDriverInputForUi();if(elapsed>20)Complete(false,"gameplay input remained blocked");return;}
   float nearest=progress,best=float.MaxValue;
   for(int i=1;i<path.Length;i++)
   {
    if(arc[i]<progress-3||arc[i-1]>progress+15)continue;
    var d=Vector3.ProjectOnPlane(path[i]-path[i-1],Vector3.up);float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(p-path[i-1],Vector3.up),d)/Mathf.Max(.0001f,d.sqrMagnitude));
    float e=Vector3.ProjectOnPlane(p-Vector3.Lerp(path[i-1],path[i],t),Vector3.up).magnitude;
    if(e<best){best=e;nearest=Mathf.Lerp(arc[i-1],arc[i],t);}
   }
   if(nearest>progress+.08f){progress=nearest;lastProgressAt=Time.time;}
   Result.MaximumCrossTrack=Mathf.Max(Result.MaximumCrossTrack,best);
   if(best>width*.5f+.5f){Complete(false,"vehicle left supported route corridor");return;}
   if(closed)
   {
    float side=Vector3.Dot(p-gate,inward);
    if(side>0){Complete(false,"vehicle crossed the closed gate plane");return;}
    if(elapsed>10&&car.Speed<.15f&&Time.time-lastProgressAt>1.0f)
    {Complete(Result.GateContacts>0&&Result.Metres>3&&side>-10,"closed gate physical contact stopped car; contacts="+Result.GateContacts+" centreSide="+side);return;}
   }
   else if(progress>arc.Last()-1.2f){Complete(Result.WheelSamples>20&&Result.Metres>arc.Last()*.85f&&(Result.Id.StartsWith("south_gate",StringComparison.Ordinal)||Result.BridgeWheelSamples>10),"car traversed complete approach, crossing and departure; bridge wheel samples="+Result.BridgeWheelSamples);return;}
   if(elapsed>150||!closed&&Time.time-lastProgressAt>18){Complete(false,"wheel traversal stalled or timed out");return;}
   Vector3 target=PointAt(Mathf.Min(arc.Last(),progress+Mathf.Lerp(3.5f,5f,Mathf.Clamp01(car.Speed/4))));
   var delta=Vector3.ProjectOnPlane(target-p,Vector3.up);
   // Steering measures yaw only. Comparing tilted forward with a horizontal
   // target wrongly turns uphill pitch into alternating left/right input.
   var flatForward=Vector3.ProjectOnPlane(car.transform.forward,Vector3.up);
   float angle=Vector3.SignedAngle(flatForward,delta,Vector3.up);
   float targetSpeed=Mathf.Lerp(3.2f,1.5f,Mathf.Clamp01(Mathf.Abs(angle)/40));
   // The ordinary W key supplies 1. A .7 ceiling cannot climb a legal grade
   // which the unchanged motor can handle at normal full forward input.
   float throttle=Mathf.Clamp01((targetSpeed-car.ForwardSpeed)*.35f),steer=Mathf.Clamp(angle/25,-1,1);
   car.SetDriverInput(throttle,steer,car.Speed>targetSpeed+.65f);
   if(Time.time-lastTrace>.15f)
   {
    lastTrace=Time.time;var supports=new List<string>();for(int i=0;i<4;i++)if(car.GetWheelContact(i,out var hit)){string support=PathOf(hit.collider.transform);supports.Add(support);Result.WheelSamples++;if(support.StartsWith("Architecture296_Crossings/",StringComparison.Ordinal))Result.BridgeWheelSamples++;}
    Result.Trace.Add(new Trace{Seconds=elapsed,Position=p,Rotation=car.transform.rotation,Speed=car.Speed,Progress=progress,Steer=steer,Throttle=throttle,GroundedWheels=car.GroundedWheelCount,Supports=supports.ToArray()});
   }
  }
  Vector3 PointAt(float d){for(int i=1;i<arc.Length;i++)if(d<=arc[i])return Vector3.Lerp(path[i-1],path[i],Mathf.InverseLerp(arc[i-1],arc[i],d));return path.Last();}
  void OnCollisionEnter(Collision c)=>Record(c);
  void OnCollisionStay(Collision c)=>Record(c);
  void Record(Collision collision)
  {
   if(Result==null||Done)return;string name=PathOf(collision.collider.transform);if(!Result.Contacts.Contains(name))Result.Contacts.Add(name);
   if(closed&&!string.IsNullOrEmpty(gatePath)&&(name==gatePath||name.StartsWith(gatePath+"/",StringComparison.Ordinal)))Result.GateContacts++;
  }
  static string PathOf(Transform t){string s=t.name;while(t.parent!=null){t=t.parent;s=t.name+"/"+s;}return s;}
  void Complete(bool pass,string detail){Result.Pass=pass;Result.Detail=detail;Done=true;car.StopDriverInputForUi();}
  public void Halt(){if(car!=null)car.StopDriverInputForUi();}
  void OnDisable()=>Halt();
 }
}
