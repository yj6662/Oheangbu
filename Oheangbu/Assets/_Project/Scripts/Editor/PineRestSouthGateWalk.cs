using System;
using System.IO;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string GateWalkReport="../Art/World/PineRest/south_gate_walk.txt";
  static Vector3 gateWalkHome,gateWalkPrevious;
  static float gateWalkDistance;
  static int gateWalkLeg;
  static double gateWalkDeadline;
  static string SouthGateWalk(){
   var s=RoadRestSession();var bridge=Object.FindFirstObjectByType<JourneyVictoryGate>();
   if(!bridge.Door.OpenAnimationComplete||roadKeyboard!=null)throw new Exception("Open gate and idle input audit required");
   gateWalkHome=bridge.Door.transform.position+Vector3.forward*10+Vector3.up*1.1f;
   s.Teleport(gateWalkHome,180);
   BeginRoadWalk(s,bridge.Door.transform.position+Vector3.back*10);
   gateWalkPrevious=s.Player.position;gateWalkDistance=0;gateWalkLeg=0;
   gateWalkDeadline=EditorApplication.timeSinceStartup+40;
   priorRoadKeyboard=Keyboard.current;roadKeyboard=InputSystem.AddDevice<Keyboard>("JourneyGateWalkAudit");roadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();
   InputSystem.onBeforeUpdate+=RoadDriveInput;EditorApplication.update+=GateWalkTick;
   File.WriteAllText(GateWalkReport,"RUNNING virtual WASD gate traversal");return "Gate walk started; no pose writes after initial fixture";
  }
  static void GateWalkTick(){
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play ended");
    if(EditorApplication.timeSinceStartup>gateWalkDeadline)throw new Exception("Gate walking timeout on leg "+gateWalkLeg);
    var s=RoadRestSession();gateWalkDistance+=Vector3.Distance(s.Player.position,gateWalkPrevious);gateWalkPrevious=s.Player.position;
    if(RoadWalking(s)){
     if(gateWalkLeg==0){gateWalkLeg=1;BeginRoadWalk(s,gateWalkHome);gateWalkDeadline=EditorApplication.timeSinceStartup+40;}
     else FinishGateWalk(null);
    }
   }catch(Exception e){FinishGateWalk(e.Message);}
  }
  static void FinishGateWalk(string error){
   EditorApplication.update-=GateWalkTick;InputSystem.onBeforeUpdate-=RoadDriveInput;
   if(roadKeyboard!=null){InputSystem.RemoveDevice(roadKeyboard);roadKeyboard=null;}
   if(priorRoadKeyboard!=null&&priorRoadKeyboard.added)priorRoadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();
   File.WriteAllText(GateWalkReport,(error==null?"PASS actual player walked through open gate and returned":"FAIL "+error)+"; distance="+gateWalkDistance+"; leg="+gateWalkLeg+"; final="+gateWalkPrevious+"\nInitial teleport fixture only, then virtual WASD through runtime motor; no pose writes during traversal. Not manual combat or final art approval.");
  }
 }
}
