using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string DriveReport="../Art/World/PineRest/road_drive.txt";
  static Keyboard roadKeyboard,priorRoadKeyboard;
  static Key[] roadKeys=Array.Empty<Key>();
  static Vector3[] driveLine;
  static int driveIndex,driveLeg,drivePhase,walkCorner;
  static double driveStarted,driveDeadline,driveLastReport,driveMotionAt;
  static Vector3 drivePrevious,driveMotionPoint;
  static float driveMetres,driveDeviation;
  static NavMeshPath roadWalk;
  static readonly List<string> driveChecks=new List<string>();
  static string RoadDriveDebug(){
   var s=RoadRestSession();var p=s.EscortCompanion.position;float r=s.EscortFollowProfile.ClearanceRadius,h=s.EscortFollowProfile.ClearanceHeight;
   var colliders=Physics.OverlapCapsule(p+Vector3.up*(r+.05f),p+Vector3.up*(Mathf.Max(r,h-r)+.05f),r,1,QueryTriggerInteraction.Ignore);
   var report="player="+s.Player.position+" npc="+p+" cargo="+s.EscortCargo.position+" vehicle="+s.JourneySeat.Vehicle.transform.position+" mode="+s.Progress.escort.CompanionMode+" issue="+s.EscortWalkIssue+" overlaps="+string.Join(",",colliders.Select(c=>c.name));
   var path=new NavMeshPath();if(NavMesh.SamplePosition(p,out var a,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(s.Player.position,out var b,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path))report+=" corners="+string.Join(";",path.corners.Select(x=>x.ToString()));return report;
  }
  static string RoadDepartureAlign(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var s=Object.FindFirstObjectByType<PrologueSession>();s.JourneySeat.Vehicle.transform.rotation=Quaternion.Euler(0,90,0);EditorSceneManager.SaveScene(scene);return "Parked palanquin faces eastbound departure road";
  }
  static string RoadDriveStart(){
   var s=RoadRestSession();if(!s.JourneySeated||s.Progress.escort.Stage!=DemoEscortStage.Escorting||s.JourneySeat.Vehicle.transform.position.z<0)throw new Exception("Fresh loaded escort near merchant required");
   var valley=GameObject.Find("Valley").GetComponent<MeshCollider>();valley.Raycast(new Ray(new Vector3(20,100,43),Vector3.down),out var hit,200);driveLine=RoadSamples(hit.point.y);
   driveChecks.Clear();driveIndex=driveLeg=drivePhase=0;driveMetres=driveDeviation=0;driveStarted=driveLastReport=driveMotionAt=EditorApplication.timeSinceStartup;driveDeadline=driveStarted+480;drivePrevious=driveMotionPoint=s.JourneySeat.Vehicle.transform.position;
   priorRoadKeyboard=Keyboard.current;roadKeyboard=InputSystem.AddDevice<Keyboard>("JourneyRoadAudit");roadKeyboard.MakeCurrent();InputSystem.onBeforeUpdate+=RoadDriveInput;EditorApplication.update+=RoadDriveTick;File.WriteAllText(DriveReport,"RUNNING actual keyboard road traverse");return "Actual road input audit started; no actor/vehicle pose writes";
  }
  static void RoadDriveInput(){if(roadKeyboard!=null&&InputState.currentUpdateType!=InputUpdateType.BeforeRender)InputSystem.QueueStateEvent(roadKeyboard,new KeyboardState(roadKeys));}
  static void RoadDriveTick(){
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play ended");var s=Object.FindFirstObjectByType<PrologueSession>();var seat=s.JourneySeat;var vehicle=seat.Vehicle;var now=EditorApplication.timeSinceStartup;
    if(now>driveDeadline)throw new Exception("Phase timeout "+drivePhase);
    var stop=s.EscortStops.Single(x=>x.PointId==(driveLeg==0?"checkpoint_1":driveLeg==1?"checkpoint_2":"cargo_delivery"));
    if(drivePhase==0){
     var position=vehicle.transform.position;driveMetres+=Vector3.Distance(position,drivePrevious);drivePrevious=position;
     for(int i=driveIndex;i<Mathf.Min(driveLine.Length,driveIndex+24);i++)if(Flat(position,driveLine[i])<Flat(position,driveLine[driveIndex]))driveIndex=i;
     RoadNearest(position,driveLine,out float deviation,out _);driveDeviation=Mathf.Max(driveDeviation,deviation);
     if(deviation>16||Vector3.Dot(vehicle.transform.up,Vector3.up)<.65f)throw new Exception("Left corridor/overturned: "+position);
     var parking=stop.transform.parent.TransformPoint(new Vector3(0,0,-13));float remaining=Flat(position,parking);
     if(remaining<2.8f){roadKeys=new[]{Key.Space};drivePhase=1;driveDeadline=now+12;}
     else{
      var target=remaining<12?parking:driveLine[Mathf.Min(driveIndex+5,driveLine.Length-1)];var direction=target-position;direction.y=0;float angle=Vector3.SignedAngle(Vector3.ProjectOnPlane(vehicle.transform.forward,Vector3.up),direction,Vector3.up);
      var keys=new List<Key>();float limit=remaining<12?2:5;if(vehicle.Speed<limit)keys.Add(Key.W);if(vehicle.Speed>limit+1.2f)keys.Add(Key.Space);if(angle>2)keys.Add(Key.D);else if(angle< -2)keys.Add(Key.A);roadKeys=keys.ToArray();
      if(Flat(position,driveMotionPoint)>1){driveMotionAt=now;driveMotionPoint=position;}else if(now-driveMotionAt>18)throw new Exception("Drive stuck at "+position+" speed="+vehicle.Speed+" angle="+angle);
     }
    }else if(drivePhase==1){
     roadKeys=new[]{Key.Space};if(vehicle.Speed<.35f&&seat.TryExit()){driveChecks.Add("PASS leg "+(driveLeg+1)+" physical arrival and safe disembark at "+vehicle.transform.position);roadKeys=Array.Empty<Key>();BeginRoadWalk(s,stop.transform.position+stop.transform.right*1.8f);drivePhase=2;driveDeadline=now+65;}
    }else if(drivePhase==2){
     if(RoadWalking(s)){roadKeys=Array.Empty<Key>();drivePhase=3;driveDeadline=now+35;}
    }else if(drivePhase==3){
     roadKeys=Array.Empty<Key>();s.Interact(stop.PointId);
     if(s.Progress.escort.Stage>=stop.ClearedStage){driveChecks.Add("PASS walked inspection/delivery "+stop.PointId+" companion distance="+Vector3.Distance(s.Player.position,s.EscortCompanion.position));
      if(driveLeg==2){FinishRoadDrive(null);return;}
      if(!seat.TryGetSafeExit(out var exit))throw new Exception("No return boarding destination");BeginRoadWalk(s,exit);drivePhase=4;driveDeadline=now+65;
     }
    }else if(drivePhase==4){
     if(RoadWalking(s)){roadKeys=Array.Empty<Key>();drivePhase=5;driveDeadline=now+35;}
    }else if(drivePhase==5){
     roadKeys=Array.Empty<Key>();if(seat.TryBoard()){driveChecks.Add("PASS physical return and reboard after "+stop.PointId);driveLeg++;drivePhase=0;driveDeadline=now+480;drivePrevious=vehicle.transform.position;driveMotionPoint=drivePrevious;driveMotionAt=now;}
    }
    if(now-driveLastReport>1){driveLastReport=now;File.WriteAllText(DriveReport,"RUNNING leg="+(driveLeg+1)+" phase="+drivePhase+" distance="+driveMetres.ToString("F1")+" index="+driveIndex+" position="+vehicle.transform.position+" speed="+vehicle.Speed.ToString("F2")+" companion="+s.EscortWalkIssue+"\n"+string.Join("\n",driveChecks));}
   }catch(Exception e){FinishRoadDrive(e.Message);}
  }
  static float Flat(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
  static void BeginRoadWalk(PrologueSession s,Vector3 target){roadWalk=new NavMeshPath();walkCorner=1;if(!NavMesh.SamplePosition(s.Player.position,out var a,3,NavMesh.AllAreas)||!NavMesh.SamplePosition(target,out var b,3,NavMesh.AllAreas)||!NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,roadWalk)||roadWalk.status!=NavMeshPathStatus.PathComplete)throw new Exception("Walking path unavailable");}
  static bool RoadWalking(PrologueSession s){
   while(walkCorner<roadWalk.corners.Length&&Flat(s.Player.position,roadWalk.corners[walkCorner])<.65f)walkCorner++;
   if(walkCorner>=roadWalk.corners.Length)return true;var delta=roadWalk.corners[walkCorner]-s.Player.position;delta.y=0;delta.Normalize();var cam=s.JourneySeat.ViewCamera.transform;var forward=Vector3.ProjectOnPlane(cam.forward,Vector3.up).normalized;var right=Vector3.ProjectOnPlane(cam.right,Vector3.up).normalized;
   var keys=new List<Key>();float f=Vector3.Dot(delta,forward),r=Vector3.Dot(delta,right);if(f>.35f)keys.Add(Key.W);else if(f< -.35f)keys.Add(Key.S);if(r>.35f)keys.Add(Key.D);else if(r< -.35f)keys.Add(Key.A);roadKeys=keys.ToArray();return false;
  }
  static void FinishRoadDrive(string error){
   EditorApplication.update-=RoadDriveTick;InputSystem.onBeforeUpdate-=RoadDriveInput;if(roadKeyboard!=null){InputSystem.RemoveDevice(roadKeyboard);roadKeyboard=null;}if(priorRoadKeyboard!=null&&priorRoadKeyboard.added)priorRoadKeyboard.MakeCurrent();
   var s=Object.FindFirstObjectByType<PrologueSession>();if(s?.JourneySeat!=null)s.JourneySeat.Vehicle.SetDriverInput(0,0,true);
   driveChecks.Add((error==null?"PASS full road physically driven and delivered":"FAIL "+error)+"; metres="+driveMetres+" maximum center deviation="+driveDeviation+" elapsed="+(EditorApplication.timeSinceStartup-driveStarted));
   driveChecks.Add("Virtual WASD vehicle/walking; direct TryBoard/TryExit/Interact; no pose writes during traversal. Not manual controls or final art/performance approval.");File.WriteAllText(DriveReport,string.Join("\n",driveChecks));
  }
 }
}
