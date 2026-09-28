using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void Check252(bool ok,string label,string report="runtime")
  {Directory.CreateDirectory(EscortOutput252);File.AppendAllText(EscortOutput252+"/"+report+".txt",(ok?"PASS ":"FAIL ")+label+"\n");if(!ok)throw new Exception(label);}
  public static string EscortAudit252()
  {
   var scene=FrontageScene249();var s=VillageSession();File.WriteAllText(EscortOutput252+"/audit.txt","");
   void C(bool ok,string label)=>Check252(ok,label,"audit");
   var route=scene.GetRootGameObjects().Single(g=>g.name==EscortRoot252).GetComponent<DemoEscortSceneRoute>();
   C(route.Stops.Length==4,"four unique physical escort stops");
   C(s.DemoEscortCargo!=null&&s.DemoEscortCompanion!=null&&s.DemoEscortSeat!=null,"live cargo, companion and shared vehicle references");
   C(route.GetComponent<DemoEscortPresentation>().Session==s,"presentation uses candidate session");
   C(s.Content.Campaign.IsValid,"explicit campaign prerequisites valid");
   C(s.Content.Campaign.Stages.Where(p=>new[]{"escort","checkpoint_one","checkpoint_two","delivery"}.Contains(p.Id)).All(p=>p.Implemented),"four escort stages connected");
   C(s.Content.Campaign.Stages.Where(p=>p.Id=="south_gate"||p.Id=="ending").All(p=>!p.Implemented),"unbuilt south gate remains disabled");
   foreach(var stop in route.Stops)
   {
    C(s.Content.Points.Count(p=>p.Id==stop.Id)==1&&Vector3.Distance(s.Content.Points.Single(p=>p.Id==stop.Id).Position,stop.Interaction.position)<.01f,"unique interaction matches "+stop.Id);
    C(s.Content.Checkpoints.Count(p=>p.Id==stop.CheckpointId)==1&&Vector3.Distance(s.Content.Checkpoints.Single(p=>p.Id==stop.CheckpointId).Feet,stop.Checkpoint.position)<.01f,"recovery position matches "+stop.Id);
    foreach(var node in new[]{stop.CompanionWait,stop.CargoWait,stop.Checkpoint})C(NavMesh.SamplePosition(node.position,out var h,1,NavMesh.AllAreas)&&Vector3.Distance(h.position,node.position)<.5f,"walk support "+stop.Id+"/"+node.name);
   }
   var plan=JsonUtility.FromJson<ProgressionPlan251>(File.ReadAllText(EscortOutput252+"/route-plan.json"));var ground=FinalSurface(scene);
   foreach(var r in plan.routes)
   {
    bool walk=true;int accepted=0,total=0;var nav=new NavMeshPath();
    for(int i=1;i<r.points.Length;i++)
    {
     var a=ground(r.points[i-1].x,r.points[i-1].y).point;var b=ground(r.points[i].x,r.points[i].y).point;
     walk&=NavMesh.SamplePosition(a,out var na,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var nb,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(na.position,nb.position,NavMesh.AllAreas,nav)&&nav.status==NavMeshPathStatus.PathComplete;
     var forward=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(a,b)/8));
     for(int j=0;j<count;j++){var p=Vector3.Lerp(a,b,j/(float)count);total++;if(s.DemoEscortSummon.TryFindPlacement(p-forward*s.DemoEscortSummon.MinimumPlayerDistance,forward,out _,out _))accepted++;}
    }
    C(walk,"continuous walk route "+r.id);
    File.AppendAllText(EscortOutput252+"/audit.txt","DETAIL vehicle placement "+r.id+" "+accepted+"/"+total+" (strict static summon probe; continuous driving separate)\n");
   }
   return File.ReadAllText(EscortOutput252+"/audit.txt");
  }
  public static string EscortRuntime252(string command)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   var route=Object.FindFirstObjectByType<DemoEscortSceneRoute>();var presenter=route.GetComponent<DemoEscortPresentation>();var ui=PlaytestUiRoot.Instance;
   void C(bool ok,string label)=>Check252(ok,label);
   string disk=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   if(command=="status")return "stage="+s.Progress.escort.Stage+" mode="+s.Progress.escort.CompanionMode+" presenter="+presenter.State+" issue="+presenter.LastIssue+" cargo="+presenter.CargoCarried+" seat="+s.DemoEscortSeat.Occupied+" speed="+s.DemoEscortSeat.Vehicle.Speed+" player="+s.Walker.Body.transform.position+" npc="+s.DemoEscortCompanion.position+" car="+s.DemoEscortSeat.Vehicle.transform.position+" recalled="+s.DemoEscortSummon.IsRecalled+" call="+s.DemoEscortSummon.LastResult;
   if(command=="prepare")
   {
    File.WriteAllText(EscortOutput252+"/runtime.txt","");ui.CloseMenu();ui.Gate.ReleaseImmediately();s.CombatActive=false;
    C(ApproachVillage("checkpoint_1")&&!s.Interact("checkpoint_1")&&s.Progress.escort.Stage==DemoEscortStage.None,"inspection cannot complete without actual escort");
    C(ApproachVillage("wangso_w1")&&s.Interact("wangso_w1")&&s.Progress.escort.Stage==DemoEscortStage.None,"Wangso first greeting leaves contract locked before Jeongdam");
    C(ApproachVillage("jeongdam_j1")&&s.Interact("jeongdam_j1"),"actual first Jeongdam contact");
    C(ApproachVillage("wangso_w1")&&s.Interact("wangso_w1")&&s.Progress.escort.Stage==DemoEscortStage.Contracted,"actual Wangso contract");
    ui.CloseMenu();ui.Gate.ReleaseImmediately();
    C(!presenter.RequiresEscortBoarding,"no departure ownership before Cheongryong unlock");
    var boss=s.Actors.Single(a=>a.Id=="cheongryong");boss.enabled=true;boss.GetComponent<EnemyVitals>().enabled=true;boss.GetComponent<CheongryongCombatController>().enabled=true;boss.GetComponent<CheongryongCombatController>().AttackEnabled=false;
    boss.GetComponent<EnemyVitals>().TakeDamage(float.MaxValue);C(s.HasDemoGuk,"actual registered boss death unlocks escort prerequisite");
    C(!s.Progress.campaign.Completed.Contains("deep_forest")&&!s.Progress.campaign.Completed.Contains("office_report")&&presenter.RequiresEscortBoarding,"departure ignores unfinished optional report and lesson");
    s.Teleport(FinalSurface(SceneManager.GetActiveScene())(2706,2154).point,270);return "Wait for real NPC cargo pickup/staging; then summon";
   }
   if(command=="summon")
   {ui.CloseMenu();ui.Gate.ReleaseImmediately();C(s.DemoEscortSummon.TryBeginShortcut(out var error),"existing front summon accepts departure: "+error);return "Wait for summon and real NPC staging";}
   if(command=="departure")
   {ui.CloseMenu();ui.Gate.ReleaseImmediately();s.CombatActive=false;s.Teleport(FinalSurface(SceneManager.GetActiveScene())(2706,2154).point,270);return "Diagnostic player-only departure setup; campaign state unchanged";}
   if(command.StartsWith("park:"))
   {
    var stop=route.Stops.Single(p=>p.Id==command.Substring(5));ui.CloseMenu();ui.Gate.ReleaseImmediately();
    var wanted=stop.Parking.position-stop.Parking.forward*s.DemoEscortSummon.MinimumPlayerDistance;
    C(s.TrySafeFeet(wanted,out var feet),"parking approach has safe player support");
    C(s.DemoEscortSummon.TryFindPlacement(feet,stop.Parking.forward,out var pose,out var issue)&&Vector3.Distance(pose.Position,stop.Parking.position)<1,"front probe reaches authored parking: "+issue+" / "+s.DemoEscortSummon.LastPlacementDiagnostic);
    s.Teleport(feet,stop.Parking.eulerAngles.y);return "Player-only parking approach setup; call gesture and car placement still required";
   }
   if(command=="vehicle-probe")
   {
    var rows=new List<string>();var call=s.DemoEscortSummon;var p=s.Walker.Body.transform.position;
    foreach(var stop in route.Stops)
    {
     var near=stop.Parking.position-stop.Parking.forward*call.MinimumPlayerDistance;
     bool safe=s.TrySafeFeet(near,out var feet);bool found=safe&&call.TryFindPlacement(feet,stop.Parking.forward,out _,out _);
     rows.Add(stop.Id+" safe="+safe+" placement="+found+" reason="+call.LastPlacementDiagnostic);
    }
    foreach(var f in new[]{s.Walker.Body.transform.forward,Vector3.forward,Vector3.right,Vector3.back,Vector3.left})
     rows.Add("current "+f+" placement="+call.TryFindPlacement(p,f,out _,out _)+" reason="+call.LastPlacementDiagnostic);
    return string.Join("\n",rows)+"\nlastCall="+call.LastResult;
   }
   if(command=="summon-check")
   {
    C(!s.DemoEscortSummon.Calling&&!s.DemoEscortSummon.IsRecalled&&s.DemoEscortSeat.Vehicle.gameObject.activeInHierarchy,"call gesture actually materialized existing car: "+s.DemoEscortSummon.LastResult);
    return "Actual call completion verified";
   }
   if(command=="board-approach")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();C(presenter.CargoCarried,"NPC physically collected cargo before boarding");
    var seat=s.DemoEscortSeat;C(seat.TryGetSafeExit(out var feet),"departure has safe entry/exit ground");s.Teleport(feet,seat.Vehicle.transform.eulerAngles.y);Physics.SyncTransforms();
    return "Player-only entry setup; allow locomotion grounding before boarding";
   }
   if(command=="board")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();var seat=s.DemoEscortSeat;
    File.AppendAllText(EscortOutput252+"/runtime.txt","DETAIL boarding canBoard="+seat.CanBoard+" walker="+s.Walker.CanBoard+" draw="+s.Walker.Motor.CanBeginDrawing+" standing="+s.Walker.Motor.CanStandForBoarding+" allowed="+(seat.BoardingAllowed==null||seat.BoardingAllowed())+" distance="+Vector3.Distance(s.Walker.Body.bounds.center,seat.SeatSocket.position)+"\n");
    C(seat.TryBoard(),"actual shared vehicle boarding: "+seat.LastInteraction);return "Wait for save-confirmed attachment";
   }
   if(command=="board-check")
   {
    C(s.Progress.escort.Stage>=DemoEscortStage.Escorting&&s.Progress.escort.Stage<DemoEscortStage.Delivered&&s.Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding,"real boarding commits active escort");
    C(s.DemoEscortCompanion.IsChildOf(s.DemoEscortPassengerSocket)&&s.DemoEscortCargo.IsChildOf(s.DemoEscortCargoSocket),"NPC and cargo attached to dedicated sockets");
    C(JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(disk)).escort.Stage==s.Progress.escort.Stage,"boarding stage saved on disk");return "Boarding verified";
   }
   if(command=="exit")
   {C(s.DemoEscortSeat.TryExit(),"actual safe exit");return "Wait for passenger exit commit";}
   if(command=="exit-check")
   {C(s.Progress.escort.CompanionMode!=DemoEscortCompanionMode.Riding&&!s.DemoEscortCompanion.IsChildOf(s.DemoEscortSeat.Vehicle.transform),"NPC released on actual disembark");C(s.Walker.Body.GetComponent<PlayerVitals>().LastEnvironmentDeath==EnvironmentDeathCause.None&&Vector3.Distance(s.Walker.Body.transform.position,s.DemoEscortSeat.Vehicle.transform.position)<8,"downhill safe exit does not trigger false fall recovery");return "Exit verified";}
   if(command.StartsWith("approach:"))
   {
    string id=command.Substring(9);var target=id=="wangso_w1"?s.DemoEscortCompanion.position:s.Content.Points.Single(p=>p.Id==id).Position;
    // Stand short of the attendant's own collider. This is collision-resolved
    // walking from the actual exit, not a remote interaction teleport.
    var toward=Vector3.ProjectOnPlane(s.Walker.Body.transform.position-target,Vector3.up).normalized;
    float radius=s.Content.Points.Single(p=>p.Id==id).Radius;
    float standOff=Mathf.Min(1.7f,Mathf.Max(.8f,radius-.45f));
    if(!NavMesh.SamplePosition(target+toward*standOff,out var hit,1,NavMesh.AllAreas))throw new Exception("No approach target");
    var nav=new NavMeshPath();C(NavMesh.CalculatePath(s.Walker.Body.transform.position,hit.position,NavMesh.AllAreas,nav)&&nav.status==NavMeshPathStatus.PathComplete,"actual exit to "+id+" path");
    if(Object.FindFirstObjectByType<CompactEscortWalker252>()!=null)throw new Exception("Existing walker");
    new GameObject("TemporaryEscort252Walker").AddComponent<CompactEscortWalker252>().Begin(s,nav.corners,Path.GetFullPath(EscortOutput252+"/approach-"+id+".json"));return "Live walking approach started";
   }
   if(command.StartsWith("inspect:"))
   {
    string id=command.Substring(8);ui.CloseMenu();ui.Gate.ReleaseImmediately();
    var before=JsonUtility.ToJson(s.Progress.escort);int currency=s.Progress.ledger.currency;
    C(s.CanInteract(id),"actual walk reaches interaction "+id);
    C(Vector3.Distance(s.DemoEscortCompanion.position,s.Walker.Body.transform.position)<8&&Vector3.Distance(s.DemoEscortCargo.position,s.Walker.Body.transform.position)<8,"actual companion and cargo present at "+id);
    foreach(var absent in new[]{s.DemoEscortCargo.gameObject,s.DemoEscortCompanion.gameObject})
    {
     bool active=absent.activeSelf;try{absent.SetActive(false);C(!s.Interact(id)&&JsonUtility.ToJson(s.Progress.escort)==before&&s.Progress.ledger.currency==currency,"missing "+absent.name+" blocks "+id);}
     finally{absent.SetActive(active);ui.CloseMenu();ui.Gate.ReleaseImmediately();}
    }
    using(var locked=new FileStream(disk+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {C(!s.Interact(id)&&JsonUtility.ToJson(s.Progress.escort)==before&&s.Progress.ledger.currency==currency,"failed "+id+" save leaves stage and reward unchanged");}
    ui.CloseMenu();ui.Gate.ReleaseImmediately();
    C(s.Interact(id),"actual "+id+" commits after lock release");
    int paid=s.Progress.ledger.currency;ui.CloseMenu();ui.Gate.ReleaseImmediately();s.Interact(id);
    C(s.Progress.ledger.currency==paid,"repeated "+id+" cannot repay");ui.CloseMenu();ui.Gate.ReleaseImmediately();
    C(s.SaveNow(out _),"full progress snapshot after "+id);return "Verified "+id;
   }
   if(command=="reload")
   {
    var saved=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(disk));
    C(s.Progress.escort.Stage==saved.escort.Stage&&s.Progress.escort.InspectionsCleared==saved.escort.InspectionsCleared,"reload preserves escort stage and inspections");
    C(s.DemoEscortCompanion.gameObject.activeInHierarchy&&s.DemoEscortCargo.gameObject.activeInHierarchy,"reload restores actual companion and cargo");
    C(s.HasDemoGuk&&s.Progress.equipment.Owned.Count>=2,"escort saves retain ability and equipment");return "Reload verified";
   }
   if(command=="remember")
   {C(s.SaveNow(out _),"full progress before restart");File.WriteAllText(EscortOutput252+"/reload-expected.json",JsonUtility.ToJson(s.Progress,true));return "Actual progress snapshot archived";}
   if(command=="reload-exact")
   {
    var expected=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(EscortOutput252+"/reload-expected.json"));
    C(s.Progress.escort.Stage==expected.escort.Stage&&s.Progress.escort.DeliveryRewardRecorded==expected.escort.DeliveryRewardRecorded&&s.Progress.ledger.currency==expected.ledger.currency,"restart retains measured stage, delivery and currency");
    C(expected.campaign.Facts.All(x=>s.Progress.campaign.Facts.Contains(x))&&expected.campaign.Completed.All(x=>s.Progress.campaign.Completed.Contains(x)),"restart retains every recorded fact and stage");
    C(JsonUtility.ToJson(expected.equipment)==JsonUtility.ToJson(s.Progress.equipment)&&s.HasDemoGuk,"restart retains equipment and earned ability");return "Exact restart snapshot verified";
   }
   if(command.StartsWith("rest-death:"))
   {
    string id=command.Substring(11);ui.CloseMenu();ui.Gate.ReleaseImmediately();var stage=s.Progress.escort.Stage;
    C(stage>=DemoEscortStage.Escorting&&stage<DemoEscortStage.Delivered,"active escort recovery fixture");
    C(s.CanInteract(id)&&s.Interact(id),"actual roadside rest "+id);
    C(s.Progress.escort.Stage==stage&&s.Progress.escort.CheckpointId==id,"rest records whole-party checkpoint without losing inspection");
    C(s.SaveNow(out _),"rest snapshot saved");File.WriteAllText(EscortOutput252+"/recovery-expected.json",JsonUtility.ToJson(s.Progress,true));
    s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();return "Actual environmental death invoked; wait for recovery";
   }
   if(command=="death-check")
   {
    var expected=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(EscortOutput252+"/recovery-expected.json"));var p=s.Walker.Body.transform.position;
    C(s.Progress.escort.Stage==expected.escort.Stage&&s.Progress.escort.InspectionsCleared==expected.escort.InspectionsCleared,"death retains actual inspections");
    C(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0&&Vector3.Distance(p,s.Progress.escort.CheckpointFeet)<2,"player restored alive at roadside checkpoint");
    C(Vector3.Distance(p,s.DemoEscortCompanion.position)<8&&presenter.CargoCarried,"NPC and carried cargo restored at same checkpoint");
    C(expected.campaign.Facts.All(x=>s.Progress.campaign.Facts.Contains(x))&&JsonUtility.ToJson(expected.equipment)==JsonUtility.ToJson(s.Progress.equipment),"death retains facts and equipment");
    C(s.SaveNow(out _),"post-death snapshot saves");return "Whole-party death recovery verified";
   }
   if(command=="wangso")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();int currency=s.Progress.ledger.currency;var stage=s.Progress.escort.Stage;
    C(Vector3.Distance(s.Content.Points.Single(p=>p.Id=="wangso_w1").Position,s.DemoEscortCompanion.position)>50,"Wangso physically travelled beyond original contract site");
    C(s.CanInteract("wangso_w1")&&s.Interact("wangso_w1"),"F conversation follows live travelling NPC");
    C(s.Progress.ledger.currency==currency&&s.Progress.escort.Stage==stage,"follow-up dialogue never repeats contract reward");ui.CloseMenu();ui.Gate.ReleaseImmediately();return "Follow-up conversation verified";
   }
   throw new Exception("Unknown escort command");
  }
 }
}
