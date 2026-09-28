using System;
using System.IO;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string GateVehicle258(string command)
  {
   var s=JourneySession258();var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();
   var door=Object.FindFirstObjectByType<SouthGateDoorPresentation>();var seat=s.DemoEscortSeat;var call=s.DemoEscortSummon;
   void Check(bool b,string label){File.AppendAllText(ContinuationOutput258+"/gate-vehicle.txt",(b?"PASS ":"FAIL ")+label+"\n");if(!b)throw new Exception(label);}
   if(command=="setup")
   {
    Check(!seat.Occupied,"player unseated before gate approach fixture");s.CombatActive=false;s.Cull();
    var p=door.transform.position-Vector3.forward*24;var ground=FinalSurface(SceneManager.GetActiveScene());s.Teleport(ground(p.x,p.z).point,0);
    return "Player-only approach fixture; car must use real call/board/physics";
   }
   if(command=="call"){Check(call.TryBeginShortcut(out var error),"front-call gesture starts: "+error);return "Wait for gesture";}
   if(command=="entry")
   {
    Check(!call.Calling&&!call.IsRecalled&&seat.Vehicle.gameObject.activeInHierarchy,"actual call completed: "+call.LastResult);
    Check(seat.TryGetSafeExit(out var feet),"gate approach car has supported entry/exit");s.Teleport(feet,0);return "Player-only seat approach; wait for grounding";
   }
   if(command=="board"){Check(seat.TryBoard(),"actual shared seat boards: "+seat.LastInteraction);return "Seated";}
   if(command=="exit"){Check(seat.TryExit(),"actual safe seat exit: "+seat.LastInteraction);return "Unseated";}
   if(command=="drive")
   {
    Check(seat.Occupied,"driver owns seat before threshold test");
    if(Object.FindFirstObjectByType<CompactGateVehicleProbe258>()!=null)throw new Exception("Gate vehicle test already active");
    var go=new GameObject("TemporaryGateVehicle258");go.AddComponent<CompactGateVehicleProbe258>().Begin(s,door.transform.position,
     Path.GetFullPath(ContinuationOutput258+"/gate-drive-"+(s.DemoSouthGateOpen?"open":"closed")+".json"));return "Live wheel-physics threshold test started";
   }
   if(command=="status")return "seat="+seat.Occupied+" speed="+seat.Vehicle.Speed+" position="+seat.Vehicle.transform.position+" call="+call.LastResult;
   throw new ArgumentException(command);
  }
 }
 [DefaultExecutionOrder(30000)]
 public sealed class CompactGateVehicleProbe258:MonoBehaviour
 {
  WorldMacroPlaytestSession session;Vector3 gate,start,prior;string output;float began,distance;bool open;
  public void Begin(WorldMacroPlaytestSession s,Vector3 p,string file)
  {session=s;gate=p;output=file;start=prior=s.DemoEscortSeat.Vehicle.transform.position;began=Time.time;open=s.DemoSouthGateOpen;}
  void Update()
  {
   var seat=session.DemoEscortSeat;var car=seat.Vehicle;var p=car.transform.position;float elapsed=Time.time-began;
   distance+=Vector3.Distance(prior,p);prior=p;
   if(!seat.Occupied||open!=session.DemoSouthGateOpen){Finish(false,"seat or durable gate state changed");return;}
   if(session.GameplayInputBlocked){car.StopDriverInputForUi();if(elapsed>30)Finish(false,"input blocked timeout");return;}
   if(Vector3.Angle(car.transform.up,Vector3.up)>35){Finish(false,"excessive tilt");return;}
   if(open&&p.z>gate.z+6){Finish(true,"wheel physics crossed opened threshold");return;}
   if(!open&&elapsed>10&&car.Speed<.15f)
   {Finish(distance>3&&p.z<gate.z-1&&p.z>gate.z-8,"closed physical gate stopped car");return;}
   if(elapsed>30){Finish(false,"threshold timeout");return;}
   var delta=Vector3.ProjectOnPlane(gate+Vector3.forward*10-p,Vector3.up);float angle=Vector3.SignedAngle(car.transform.forward,delta,Vector3.up);
   car.SetDriverInput(car.Speed<3?.4f:0,Mathf.Clamp(angle/25,-1,1),car.Speed>4);
  }
  void Finish(bool pass,string reason)
  {
   session.DemoEscortSeat.Vehicle.StopDriverInputForUi();
   File.WriteAllText(output,JsonUtility.ToJson(new Receipt{status=pass?"PASS":"FAIL",detail=reason,scope="Player placement fixture, actual summon/seat and WheelCollider physics; not human driving",expectOpen=open,seconds=Time.time-began,metres=distance,start=start,end=prior,gate=gate},true));
   Destroy(gameObject);
  }
  void OnDisable(){if(session!=null)session.DemoEscortSeat.Vehicle.StopDriverInputForUi();}
  [Serializable]sealed class Receipt{public string status,detail,scope;public bool expectOpen;public float seconds,metres;public Vector3 start,end,gate;}
 }
}
