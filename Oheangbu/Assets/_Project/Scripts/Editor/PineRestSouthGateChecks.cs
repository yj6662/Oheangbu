using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string GateReport="../Art/World/PineRest/south_gate_checks.txt";
  static readonly List<string> gateChecks=new List<string>();static int gateStep,gateAttacks;static double gateAt;static float gateInitialHp;
  static SouthGateGeneralController observedGeneral;
  static void GateAttack(SouthGateGeneralAttackPlan plan){gateAttacks++;}
  static bool GateBlocked(SouthGateDoorPresentation door)=>Physics.SphereCast(door.transform.position+Vector3.forward*4+Vector3.up*1.2f,.35f,Vector3.back,out _,8,1,QueryTriggerInteraction.Ignore);
  static string SouthGateCheck(){
   var s=RoadRestSession();if(s.Progress.completed.Contains("cargo_delivery"))throw new Exception("Fresh isolated gate prerequisite required");
   var actor=s.Encounters.Single(x=>x.Id=="south_gate_general");var door=Object.FindFirstObjectByType<JourneyVictoryGate>().Door;
   gateChecks.Clear();gateChecks.Add(!actor.gameObject.activeSelf&&!door.IsOpenRequested&&GateBlocked(door)?"PASS undelivered cargo keeps boss inactive and passage blocked":"FAIL initial gate condition");
   // Prerequisite fixture only; the actual road/escort sequence is verified by RoadDriveStart.
   PrologueProgressStore.Complete(s.Progress,"cargo_delivery",0);s.Save();gateStep=0;gateAttacks=0;gateAt=EditorApplication.timeSinceStartup+1;EditorApplication.update+=SouthGateTick;File.WriteAllText(GateReport,"RUNNING");return "General activation, actual attacks, victory transaction and physical gate checks started";
  }
  static void SouthGateTick(){
   if(EditorApplication.timeSinceStartup<gateAt)return;
   try{
    var s=RoadRestSession();var actor=s.Encounters.Single(x=>x.Id=="south_gate_general");var general=actor.GetComponent<SouthGateGeneralController>();var vitals=actor.GetComponent<EnemyVitals>();var bridge=Object.FindFirstObjectByType<JourneyVictoryGate>();var door=bridge.Door;
    void Check(bool value,string label)=>gateChecks.Add((value?"PASS ":"FAIL ")+label);
    if(gateStep==0){
     Check(actor.gameObject.activeSelf&&actor.GetComponent<NavMeshAgent>().isOnNavMesh,"delivery activates supported general");Check(actor.GetComponent<SouthGateGeneralPresentation>().HasGraph,"general has running humanoid presentation graph");
     observedGeneral=general;general.AttackStarted+=GateAttack;gateInitialHp=s.Player.GetComponent<PlayerVitals>().Hp01;s.Teleport(actor.transform.position+Vector3.forward*3+Vector3.up*.15f,180);gateStep=1;gateAt=EditorApplication.timeSinceStartup+6;return;
    }
    if(gateStep==1){
     Check(gateAttacks>0,"configured general starts real attacks: "+gateAttacks);Check(s.Player.GetComponent<PlayerVitals>().Hp01<gateInitialHp,"general attack damages Journey player through combat wiring");
     var retreat=s.Content.Points.Single(x=>x.Id=="SouthGateRest");s.Teleport(retreat.Position+Vector3.up*1.1f,0);gateStep=11;gateAt=EditorApplication.timeSinceStartup+1;return;
    }
    if(gateStep==11){
     if(actor.Current==PrologueEncounter.Behaviour.Chase)throw new Exception("General failed to disengage at approach rest");
     var rest=s.Content.Points.Single(x=>x.Id=="SouthGateRest");s.Interact(rest.Id);Check(s.Progress.checkpoint==rest.Id&&vitals.IsAlive,"approach rest after disengagement resets living boss and records checkpoint");
     var field=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var repository=(PrologueProgressStore)field.GetValue(s);int money=s.Progress.currency;string blocker=Path.GetFullPath("../Art/World/PineRest/gate_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit");
     try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));vitals.TakeDamage(100000);Check(!s.Progress.defeated.Contains(actor.Id)&&s.Progress.currency==money&&!door.IsOpenRequested&&GateBlocked(door),"victory IOException leaves reward and gate unpublished");}finally{field.SetValue(s,repository);File.Delete(blocker);}
     s.Save();Check(s.Progress.defeated.Contains(actor.Id)&&s.Progress.currency==money+120,"pending victory commits durable defeat and reward120 atomically");
     s.EnemyDefeated(actor.Id);s.Save();Check(s.Progress.currency==money+120,"duplicate defeat cannot award again");Check(repository.Load().defeated.Contains(actor.Id)&&repository.Load().currency==money+120,"victory and reward survive disk load");
     gateStep=2;gateAt=EditorApplication.timeSinceStartup+3.2;return;
    }
    Check(door.OpenAnimationComplete&&!bridge.NavigationObstacle.enabled&&!GateBlocked(door),"saved victory opens animation and clears central SphereCast");
    var point=s.Content.Points.Single(x=>x.Id=="SouthGateRest");s.Teleport(point.Position+Vector3.up*1.1f,0);s.Interact(point.Id);Check(!actor.gameObject.activeSelf&&s.Progress.defeated.Contains(actor.Id),"rest does not resurrect defeated general");FinishSouthGate();
   }catch(Exception e){gateChecks.Add("FAIL "+e);FinishSouthGate();}
  }
  static void FinishSouthGate(){EditorApplication.update-=SouthGateTick;if(observedGeneral!=null)observedGeneral.AttackStarted-=GateAttack;observedGeneral=null;gateChecks.Add("Delivery prerequisite fixture and audit teleport; real timed attacks, actual IOException and direct lethal damage. Not manual boss combat, parry or final general art approval.");File.WriteAllText(GateReport,string.Join("\n",gateChecks));}
  static string SouthGateReload(){var s=RoadRestSession();var actor=s.Encounters.Single(x=>x.Id=="south_gate_general");var door=Object.FindFirstObjectByType<JourneyVictoryGate>().Door;string result=(s.Progress.defeated.Contains(actor.Id)&&!actor.gameObject.activeSelf&&door.OpenAnimationComplete&&!GateBlocked(door)?"PASS ":"FAIL ")+"new Play restores defeated general and traversable south gate";File.WriteAllText("../Art/World/PineRest/south_gate_reload.txt",result);return result;}
 }
}
