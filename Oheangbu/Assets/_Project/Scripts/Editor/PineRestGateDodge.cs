using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string GateDodgeReport="../Art/World/PineRest/south_gate_dodge_counter.txt";
  static SouthGateGeneralController dodgeGeneral;
  static PlayerVitals dodgePlayer;
  static SouthGateGeneralAttackPlan dodgePlan;
  static int dodgePhase;
  static double dodgeDeadline;
  static bool dodgeContactResolved,dodgeSawDash;
  static float dodgeHitDamage,dodgeDistance;
  static Vector3 dodgeOrigin;
  static readonly List<string> dodgeTimeline=new List<string>();
  static void DodgeLog(string text)=>dodgeTimeline.Add(Time.time.ToString("F3")+" "+text);
  static void DodgeAttack(SouthGateGeneralAttackPlan plan){DodgeLog("attack "+plan.Kind+" release="+plan.Pulses[0].ReleaseAt);if(dodgePlan==null)dodgePlan=plan;}
  static void DodgeImpact(SouthGateGeneralImpact impact){DodgeLog("impact "+impact.Plan.Kind+" contact="+impact.Contact+" damage="+impact.AppliedDamage);if(impact.Plan==dodgePlan){dodgeContactResolved=true;dodgeHitDamage+=impact.AppliedDamage;}}
  static void DodgePlayerDamage(float damage)=>DodgeLog("player damage="+damage);
  static string GateDodgeCounter(){
   var s=RoadRestSession();if(roadKeyboard!=null||dodgeGeneral!=null)throw new Exception("Input audit already active");
   if(s.Progress.completed.Contains("cargo_delivery"))throw new Exception("Fresh isolated pre-delivery state required");
   var actor=s.Encounters.Single(a=>a.Id=="south_gate_general");PrologueProgressStore.Complete(s.Progress,"cargo_delivery",0);s.Save();
   s.Teleport(actor.transform.position+Vector3.forward*3+Vector3.up*.2f,180);
   dodgeGeneral=actor.GetComponent<SouthGateGeneralController>();dodgePlayer=s.Player.GetComponent<PlayerVitals>();dodgePlan=null;dodgePhase=0;dodgeContactResolved=dodgeSawDash=false;dodgeHitDamage=dodgeDistance=0;dodgeTimeline.Clear();dodgeOrigin=s.Player.position;
   dodgeGeneral.AttackStarted+=DodgeAttack;dodgeGeneral.AttackImpactResolved+=DodgeImpact;dodgePlayer.Damaged+=DodgePlayerDamage;
   priorRoadKeyboard=Keyboard.current;roadKeyboard=InputSystem.AddDevice<Keyboard>("GateDodgeAudit");roadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();InputSystem.onBeforeUpdate+=RoadDriveInput;
   dodgeDeadline=EditorApplication.timeSinceStartup+40;EditorApplication.update+=GateDodgeTick;DodgeLog("initial approach fixture; input observation begins");File.WriteAllText(GateDodgeReport,"RUNNING");return "Observing live thrust for virtual dodge and wood countercast";
  }
  static void GateDodgeTick(){
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play ended");if(EditorApplication.timeSinceStartup>dodgeDeadline)throw new Exception("Phase timeout "+dodgePhase);
    var s=RoadRestSession();var dash=s.Player.GetComponent<DodgeAction>();
    if(dodgePhase==0&&dodgePlan!=null){if(dodgePlan.Kind!=SouthGateAttackKind.Thrust)throw new Exception("Expected first live thrust");if(Time.time>=dodgePlan.Pulses[0].ReleaseAt-.15f){roadKeys=new[]{Key.D,Key.LeftShift};dodgePhase=1;DodgeLog("D+LeftShift pressed");}}
    else if(dodgePhase==1){if(dash.IsDashing){dodgeSawDash=true;roadKeys=Array.Empty<Key>();dodgePhase=2;DodgeLog("runtime DodgeAction active");}else if(Time.time>dodgePlan.Pulses[0].ActiveEndAt)throw new Exception("Dodge input did not start dash");}
    else if(dodgePhase==2&&!dash.IsDashing&&dodgeContactResolved){
     dodgeDistance=Vector3.Distance(dodgeOrigin,s.Player.position);DodgeLog("dash finished displacement="+dodgeDistance);
     if(dodgeHitDamage>0||dodgeDistance<1)throw new Exception("Dodge failed to avoid initial thrust");
     ReleaseDodgeKeyboard();DodgeLog("wood input begins");GateInputCast();dodgePhase=3;
    }else if(dodgePhase==3&&Vfx120PlayerInputAudit.Poll().Contains("\"FINISHED\"")){
     string cast=GateInputResult();DodgeLog("wood observed damage="+gateInputDamage);FinishGateDodge(cast.StartsWith("PASS ")?null:"Countercast did not meet damage/input checks");
    }
   }catch(Exception e){FinishGateDodge(e.Message);}
  }
  static void ReleaseDodgeKeyboard(){InputSystem.onBeforeUpdate-=RoadDriveInput;if(roadKeyboard!=null){InputSystem.RemoveDevice(roadKeyboard);roadKeyboard=null;}if(priorRoadKeyboard!=null&&priorRoadKeyboard.added)priorRoadKeyboard.MakeCurrent();roadKeys=Array.Empty<Key>();}
  static void FinishGateDodge(string error){
   EditorApplication.update-=GateDodgeTick;ReleaseDodgeKeyboard();if(dodgeGeneral!=null){dodgeGeneral.AttackStarted-=DodgeAttack;dodgeGeneral.AttackImpactResolved-=DodgeImpact;}if(dodgePlayer!=null)dodgePlayer.Damaged-=DodgePlayerDamage;
   dodgeGeneral=null;dodgePlayer=null;
   File.WriteAllText(GateDodgeReport,(error==null?"PASS live thrust dodge followed by real wood input damage":"FAIL "+error)+"\ndashObserved="+dodgeSawDash+" displacement="+dodgeDistance+" initialAttackDamage="+dodgeHitDamage+"\n"+string.Join("\n",dodgeTimeline)+"\nInitial prerequisite/position/lock fixture; virtual timed dodge and registered strokes; live AI. Not human reaction difficulty, full fight, parry or art approval.");
  }
 }
}
