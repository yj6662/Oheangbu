using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void Check253(bool ok,string label,string file="runtime")
  {Directory.CreateDirectory(GateOutput253);File.AppendAllText(GateOutput253+"/"+file+".txt",(ok?"PASS ":"FAIL ")+label+"\n");if(!ok)throw new Exception(label);}
  public static string SouthGateSurvey253()
  {
   var scene=FrontageScene249();var stop=Object.FindFirstObjectByType<DemoEscortSceneRoute>().Stops.Single(p=>p.Id=="cargo_delivery");
   var lines=new System.Collections.Generic.List<string>();
   foreach(var n in new[]{stop.Interaction,stop.CompanionWait,stop.CargoWait,stop.Checkpoint})
   {
    bool nav=NavMesh.SamplePosition(n.position,out var hit,4,NavMesh.AllAreas);
    lines.Add(n.name+" at="+n.position.ToString("F3")+" nav="+nav+" "+hit.position.ToString("F3")+" distance="+Vector3.Distance(n.position,hit.position));
    foreach(var c in Physics.OverlapCapsule(n.position+Vector3.up*.3f,n.position+Vector3.up*1.6f,.32f))lines.Add(" overlap "+c.name+" bounds="+c.bounds+" parent="+c.transform.parent);
   }
   foreach(var c in stop.Interaction.parent.GetComponentsInChildren<Collider>(true))lines.Add("stop collider "+c.name+" "+c.bounds);
   File.WriteAllText(GateOutput253+"/survey.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
  public static string SouthGateAudit253()
  {
   var scene=FrontageScene249();var s=VillageSession();var root=scene.GetRootGameObjects().Single(g=>g.name==GateRoot253);
   File.WriteAllText(GateOutput253+"/audit.txt","");void C(bool b,string label)=>Check253(b,label,"audit");
   var door=root.GetComponentInChildren<SouthGateDoorPresentation>();var actor=s.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SouthGateGeneralId);
   C(s.DemoSouthGateGeneral==actor.GetComponent<SouthGateGeneralController>(),"general registered in candidate session");
   C(Vector3.Dot(door.transform.up,Vector3.up)>.999f,"copied gate preserves upright orientation");
   C(s.Content.Encounters.Count(e=>e.Id==actor.Id)==1&&!s.Content.Encounters.Single(e=>e.Id==actor.Id).RespawnOnRest,"one persistent encounter");
   C(s.Content.Campaign.IsValid&&s.Content.Campaign.Stages.All(p=>p.Implemented),"all twenty representative stages wired; not EA completion");
   C(door.IsConfigured&&door.GetComponent<BoxCollider>().enabled&&door.GetComponent<NavMeshObstacle>().carving,"closed gate has physical and navigation blockers");
   C(door.GetComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild,"dynamic door excluded from static bake");
   C(NavMesh.SamplePosition(actor.transform.position,out _,2,NavMesh.AllAreas),"general stands on candidate navigation");
   C(root.GetComponentsInChildren<Renderer>(true).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported)),"gate/general materials supported");
   var stop=Object.FindFirstObjectByType<DemoEscortSceneRoute>().Stops.Single(p=>p.Id=="cargo_delivery");
   var house=stop.Interaction.parent.Find("Guesthouse252");
   C(Vector3.Dot(house.position-stop.Parking.position,Vector3.Cross(Vector3.up,stop.Parking.forward))<0,"guesthouse on road's opposite side");
   C(house.GetComponentsInChildren<MeshCollider>().Length>0,"guesthouse owns geometry collision");
   var clerkApproach=stop.Interaction.position+(stop.Parking.position-stop.Interaction.position).normalized*1.7f;
   C(NavMesh.SamplePosition(clerkApproach,out var clerkNav,1,NavMesh.AllAreas)&&Vector3.Distance(clerkApproach,clerkNav.position)<.6f,"relocated clerk has navigable interaction approach outside its body");
   foreach(var node in new[]{stop.CompanionWait,stop.CargoWait,stop.Checkpoint})
    C(NavMesh.SamplePosition(node.position,out var hit,1,NavMesh.AllAreas)&&Vector3.Distance(node.position,hit.position)<.6f,"relocated stop support "+node.name);
   C(Vector3.Distance(s.Content.Points.Single(p=>p.Id==stop.Id).Position,stop.Interaction.position)<.01f&&Vector3.Distance(s.Content.Checkpoints.Single(p=>p.Id==stop.CheckpointId).Feet,stop.Checkpoint.position)<.01f,"delivery and rest data follow relocated nodes");
   var ground=FinalSurface(scene);var gate=door.transform.position;
   var a=ground(gate.x,gate.z-5).point;var b=ground(gate.x,gate.z+5).point;
   C(Physics.RaycastAll(a+Vector3.up,Vector3.ProjectOnPlane(b-a,Vector3.up).normalized,10,~0,QueryTriggerInteraction.Ignore).Any(h=>h.collider==door.GetComponent<BoxCollider>()),"closed door physically spans walking threshold");
   File.WriteAllText(GateOutput253+"/geometry.txt","gate="+gate+" approach="+a+" exit="+b+" house="+house.position);
   return File.ReadAllText(GateOutput253+"/audit.txt");
  }
  public static string SouthGateDeliveredStart253()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Clean candidate required");var s=VillageSession();
   string source=EscortOutput252+"/reload-expected.json";var recorded=File.ReadAllText(source);var p=JsonUtility.FromJson<WorldMacroProgress>(recorded);
   if(!WorldMacroProgress.Valid(p)||p.escort.Stage!=DemoEscortStage.Delivered||!p.escort.DeliveryRewardRecorded||p.defeated.Contains(WorldMacroPlaytestSession.SouthGateGeneralId))throw new Exception("Authentic prior delivery evidence required");
   string suffix="_compact_slice_"+Guid.NewGuid().ToString("N");
   File.WriteAllText(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+suffix+".json"),recorded);
   SessionState.SetString("CompactSlice.CheckSuffix",suffix);SessionState.SetString("CompactSlice.CheckPhase","initial");
   SessionState.SetString("CompactSlice.PriorUiSuffix",SessionState.GetString("PlaytestUiReviewSuffix",""));SessionState.SetString("PlaytestUiReviewSuffix",suffix);s.TestSaveSuffix=suffix;
   File.WriteAllText(GateOutput253+"/setup.txt","Copied unmodified actual #252 delivery snapshot into new private namespace "+suffix+". Prior route driving is setup, not re-measured #253 evidence.");
   UnityEditor.EditorApplication.EnterPlaymode();return "Entering private post-delivery gate test "+suffix;
  }
  public static string SouthGateRuntime253(string command)
  {
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   var ui=PlaytestUiRoot.Instance;var door=Object.FindFirstObjectByType<SouthGateDoorPresentation>();var general=s.DemoSouthGateGeneral;var life=general.GetComponent<EnemyVitals>();
   var box=door.GetComponent<BoxCollider>();var obstacle=door.GetComponent<NavMeshObstacle>();
   string path=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   void C(bool b,string label)=>Check253(b,label);ui.CloseMenu();ui.Gate.ReleaseImmediately();
   if(command=="status")return "available="+s.DemoSouthGateEncounterAvailable+" alive="+life.IsAlive+" pending="+s.DemoSouthGateSavePending+" opened="+s.DemoSouthGateOpen+" animated="+door.OpenAnimationComplete+" collision="+box.enabled+" navigation="+obstacle.enabled+" player="+s.Walker.Body.transform.position;
   if(command=="negative")
   {
    C(s.Progress.escort.Stage<DemoEscortStage.Delivered&&!s.DemoSouthGateEncounterAvailable,"pre-delivery general inaccessible");s.Cull();C(!life.enabled&&!general.enabled,"pre-delivery combat actually disabled");
    int currency=s.Progress.ledger.currency;s.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);
    C(!s.DemoSouthGateOpen&&currency==s.Progress.ledger.currency&&box.enabled&&obstacle.enabled,"string-only death cannot open or reward before delivery");return "Pre-delivery guards verified";
   }
   if(command=="view")
   {s.CombatActive=false;var p=door.transform.position-Vector3.forward*44;p=FinalSurface(SceneManager.GetActiveScene())(p.x,p.z).point;s.Teleport(p,0);return "Diagnostic player-only approach setup";}
   if(command=="defeat")
   {
    C(s.DemoSouthGateEncounterAvailable&&life.IsAlive&&!s.DemoSouthGateOpen,"actual saved delivery unlocks registered general");
    s.CombatActive=false;s.Cull();general.enabled=true;general.AttackEnabled=false;C(life.enabled,"live general accepts damage only after delivery");
    int before=s.Progress.ledger.currency;var equipment=JsonUtility.ToJson(s.Progress.equipment);var facts=s.Progress.campaign.Facts.ToArray();
    s.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);C(life.IsAlive&&!s.DemoSouthGateOpen&&s.Progress.ledger.currency==before,"string-only defeat after delivery is rejected");
    using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
    {
     life.TakeDamage(float.MaxValue);C(!life.IsAlive&&s.DemoSouthGateSavePending&&!s.DemoSouthGateOpen&&s.Progress.ledger.currency==before,"failed actual death save grants no reward or gate");
     C(box.enabled&&obstacle.enabled&&!door.IsOpenRequested,"failed save keeps physical and navigation barrier closed");
     C(!s.SaveNow(out _),"pending death prevents partial ordinary save");
     C(!s.TryEquipment(EquipmentAction.Unequip,null,EquipmentSlot.Brush,s.Progress.equipment.Revision,"pending-gate253",out _),"pending gate blocks equipment transaction");
    }
    C(s.SaveNow(out _),"pending gate retries through full save after lock release");
    int reward=s.Content.Campaign.Stages.Where(p=>p.Id=="south_gate"||p.Id=="ending").Sum(p=>p.TongboReward);
    C(s.DemoSouthGateOpen&&!s.DemoSouthGateSavePending&&s.Progress.ledger.currency==before+reward,"death gate and combined reward commit atomically");
    C(door.IsOpenRequested&&!door.OpenAnimationComplete&&box.enabled&&obstacle.enabled,"barriers remain through opening animation");
    s.EnemyDefeated(WorldMacroPlaytestSession.SouthGateGeneralId);C(s.Progress.ledger.currency==before+reward,"duplicate death cannot repay");
    C(JsonUtility.ToJson(s.Progress.equipment)==equipment&&facts.All(f=>s.Progress.campaign.Facts.Contains(f))&&s.HasDemoGuk,"gate transaction retains equipment ability and prior facts");
    File.WriteAllText(GateOutput253+"/expected.json",JsonUtility.ToJson(s.Progress,true));return "Actual EnemyVitals API death committed; wait for gate animation. Not player combat proof.";
   }
   if(command=="open-check")
   {
    C(s.DemoSouthGateOpen&&door.OpenAnimationComplete&&!box.enabled&&!obstacle.enabled,"animation finished; physical and navigation passage released");
    var p=door.transform.position;var ground=FinalSurface(SceneManager.GetActiveScene());var a=ground(p.x,p.z-5).point;var b=ground(p.x,p.z+5).point;
    C(NavMesh.SamplePosition(a,out var na,1,NavMesh.AllAreas)&&NavMesh.SamplePosition(b,out var nb,1,NavMesh.AllAreas)&&!NavMesh.Raycast(na.position,nb.position,out _,NavMesh.AllAreas),"fresh baked navigation crosses opened threshold directly");return "Open passage state verified";
   }
   if(command=="walk-closed"||command=="walk-open")
   {
    bool open=command=="walk-open";C(s.DemoSouthGateOpen==open,"walking probe starts in requested durable state");
    if(Object.FindFirstObjectByType<CompactGateWalker253>()!=null)throw new Exception("Walker active");
    s.CombatActive=false;var p=door.transform.position;var g=FinalSurface(SceneManager.GetActiveScene());s.Teleport(g(p.x,p.z-5).point,0);
    new GameObject("TemporaryGate253Walker").AddComponent<CompactGateWalker253>().Begin(s,g(p.x,p.z+5).point,open,Path.GetFullPath(GateOutput253+"/"+command+".json"));return "Live CharacterController movement begun";
   }
   if(command=="reload")
   {
    var expected=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(GateOutput253+"/expected.json"));
    C(s.DemoSouthGateOpen&&!s.DemoSouthGateEncounterAvailable&&door.OpenAnimationComplete&&!box.enabled&&!obstacle.enabled,"restart restores gate open and persistent general defeat");
    C(expected.ledger.currency==s.Progress.ledger.currency&&expected.campaign.Facts.All(f=>s.Progress.campaign.Facts.Contains(f))&&JsonUtility.ToJson(expected.equipment)==JsonUtility.ToJson(s.Progress.equipment),"restart retains measured currency equipment and facts");return "Restart verified";
   }
   if(command=="rest-death")
   {
    C(s.DemoSouthGateOpen,"post-victory recovery fixture");
    C(ApproachVillage("capital_escort_rest")&&s.Interact("capital_escort_rest"),"actual relocated roadside rest after victory");
    C(s.DemoSouthGateOpen&&!s.DemoSouthGateEncounterAvailable,"rest cannot resurrect persistent general");
    s.Walker.Body.GetComponent<PlayerVitals>().ApplyFatalFall();return "Actual environmental death requested; wait for checkpoint recovery";
   }
   if(command=="recovery")
   {
    var expected=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(GateOutput253+"/expected.json"));
    C(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0&&s.DemoSouthGateOpen&&!s.DemoSouthGateEncounterAvailable,"death restores living player without resurrecting general or closing gate");
    C(expected.campaign.Facts.All(f=>s.Progress.campaign.Facts.Contains(f))&&JsonUtility.ToJson(expected.equipment)==JsonUtility.ToJson(s.Progress.equipment)&&s.HasDemoGuk,"death retains facts equipment and Guk");
    C(s.SaveNow(out _),"post-death complete snapshot persisted");File.WriteAllText(GateOutput253+"/expected.json",JsonUtility.ToJson(s.Progress,true));return "Recovery verified; ordinary death currency drop rule retained";
   }
   throw new ArgumentException(command);
  }
 }
 [DefaultExecutionOrder(30000)]
 public sealed class CompactGateWalker253:MonoBehaviour
 {
  WorldMacroPlaytestSession session;Vector3 target,start;bool expectOpen,priorMotor;string file;float started;
  public void Begin(WorldMacroPlaytestSession s,Vector3 t,bool open,string path){session=s;target=t;expectOpen=open;file=path;start=s.Walker.Body.transform.position;started=Time.time;priorMotor=s.Walker.Motor.enabled;s.Walker.Motor.enabled=false;}
  void Update()
  {
   if(session==null)return;var body=session.Walker.Body;var delta=Vector3.ProjectOnPlane(target-body.transform.position,Vector3.up);
   if(Time.time-started<7&&delta.magnitude>.3f){body.Move(Vector3.ClampMagnitude(delta,2.5f*Time.deltaTime)+Vector3.down*3*Time.deltaTime);return;}
   bool arrived=delta.magnitude<.3f,blocked=body.transform.position.z<(start.z+target.z)*.5f&&Vector3.Distance(start,body.transform.position)>1;
   File.WriteAllText(file,JsonUtility.ToJson(new Receipt{status=(expectOpen?arrived:blocked)?"PASS":"FAIL",expectOpen=expectOpen,arrived=arrived,blocked=blocked,start=start,end=body.transform.position,target=target,seconds=Time.time-started},true));Destroy(gameObject);
  }
  [Serializable]sealed class Receipt{public string status;public bool expectOpen,arrived,blocked;public Vector3 start,end,target;public float seconds;}
  void OnDisable(){if(session!=null)session.Walker.Motor.enabled=priorMotor;}
 }
}
