using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string EscortDrive252(string stopId)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_")||!s.DemoEscortSeat.Occupied)throw new Exception("Private seated Play required");
   if(Object.FindFirstObjectByType<CompactEscortDriver252>()!=null)throw new Exception("Existing driver still active");
   var route=Object.FindFirstObjectByType<DemoEscortSceneRoute>();var stop=route.Stops.Single(x=>x.Id==stopId);
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(EscortOutput252+"/route-plan.json"));
   string from=stopId=="checkpoint_1"?"escort_departure":stopId=="checkpoint_2"?"inspection_one":"inspection_two";
   string to=stopId=="checkpoint_1"?"inspection_one":stopId=="checkpoint_2"?"inspection_two":"capital_delivery";
   int begin=Array.FindIndex(plan.routes,p=>p.fromId==from),end=Array.FindIndex(plan.routes,p=>p.toId==to);if(begin<0||end<begin)throw new Exception("Unknown route");
   var ground=FinalSurface(SceneManager.GetActiveScene());var points=plan.routes.Skip(begin).Take(end-begin+1).SelectMany(r=>r.points).Select(p=>ground(p.x,p.y).point).ToList();points.Add(stop.Parking.position);
   var runner=new GameObject("TemporaryEscort252Driver").AddComponent<CompactEscortDriver252>();runner.Begin(s,points.ToArray(),Path.GetFullPath(EscortOutput252+"/drive-"+stopId+".json"));
   return "Live WheelCollider driving started to "+stopId+". Physics/API input; no position setters.";
  }
 }
 [DefaultExecutionOrder(30000)]
 public sealed class CompactEscortDriver252:MonoBehaviour
 {
  WorldMacroPlaytestSession session;Vector3[] path;string output;int next;float started,stalled,distance;Vector3 prior;List<Vector3> trail=new List<Vector3>();float nextSample;
  [Serializable]sealed class Receipt{public string status,detail;public float seconds,metres;public Vector3 end;public Vector3[] trail;}
  public void Begin(WorldMacroPlaytestSession s,Vector3[] p,string file){session=s;path=p;output=file;next=1;started=Time.time;prior=s.DemoEscortSeat.Vehicle.transform.position;}
  void Update()
  {
   if(session==null||path==null)return;
   var car=session.DemoEscortSeat.Vehicle;var position=car.transform.position;
   if(!session.DemoEscortSeat.Occupied){Finish(false,"Seat ownership lost");return;}
   if(session.GameplayInputBlocked){car.StopDriverInputForUi();return;}
   float moved=Vector3.Distance(position,prior);distance+=moved;prior=position;
   stalled=moved<.001f?stalled+Time.deltaTime:0;
   if(Time.time-started>420||stalled>12||Vector3.Angle(car.transform.up,Vector3.up)>35){Finish(false,"Timeout, stall or tilt");return;}
   while(next<path.Length-1&&Vector3.ProjectOnPlane(path[next]-position,Vector3.up).magnitude<6)next++;
   var end=Vector3.ProjectOnPlane(path.Last()-position,Vector3.up);bool arrival=end.magnitude<3.2f&&next>=path.Length-1;
   if(arrival&&car.Speed<.15f){Finish(true,"Actual wheel physics reached stop");return;}
   var delta=Vector3.ProjectOnPlane(path[next]-position,Vector3.up);float angle=Vector3.SignedAngle(car.transform.forward,delta,Vector3.up);
   float targetSpeed=Mathf.Lerp(5,2,Mathf.Clamp01(Mathf.Abs(angle)/65));
   if(end.magnitude<10&&next>=path.Length-1)targetSpeed=Mathf.Min(targetSpeed,Mathf.Max(.7f,end.magnitude*.35f));
   car.SetDriverInput(arrival?0:car.Speed<targetSpeed?.55f:0,Mathf.Clamp(angle/26,-1,1),arrival||car.Speed>targetSpeed+1);
   if(Time.time>=nextSample){nextSample=Time.time+1;trail.Add(position);Write("RUNNING","automatic driver using live physics");}
  }
  void Write(string state,string detail){File.WriteAllText(output,JsonUtility.ToJson(new Receipt{status=state,detail=detail,seconds=Time.time-started,metres=distance,end=prior,trail=trail.ToArray()},true));}
  void Finish(bool pass,string reason){session.DemoEscortSeat.Vehicle.StopDriverInputForUi();Write(pass?"PASS":"FAIL",reason);Destroy(gameObject);}
  void OnDisable(){if(session!=null)session.DemoEscortSeat.Vehicle.StopDriverInputForUi();}
 }
 [DefaultExecutionOrder(30000)]
 public sealed class CompactEscortWalker252:MonoBehaviour
 {
  WorldMacroPlaytestSession session;Vector3[] path;int next;string output;float started;bool priorMotor;
  public void Begin(WorldMacroPlaytestSession s,Vector3[] p,string file){session=s;path=p;output=file;next=1;started=Time.time;priorMotor=s.Walker.Motor.enabled;s.Walker.Motor.enabled=false;}
  void Update()
  {
   if(session==null)return;var body=session.Walker.Body;
   if(Time.time-started>25){Finish(false);return;}
   while(next<path.Length&&Vector3.ProjectOnPlane(path[next]-body.transform.position,Vector3.up).magnitude<.2f)next++;
   if(next>=path.Length){Finish(true);return;}
   var d=Vector3.ProjectOnPlane(path[next]-body.transform.position,Vector3.up);body.transform.rotation=Quaternion.LookRotation(d);
   body.Move(Vector3.ClampMagnitude(d,2.5f*Time.deltaTime)+Vector3.down*3*Time.deltaTime);
  }
  void Finish(bool pass){File.WriteAllText(output,"{\"status\":\""+(pass?"PASS":"FAIL")+"\",\"scope\":\"live collision-resolved controller approach\"}");Destroy(gameObject);}
  void OnDisable(){if(session!=null)session.Walker.Motor.enabled=priorMotor;}
 }
}
