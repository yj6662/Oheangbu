using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string GateParryReport="../Art/World/PineRest/south_gate_parry.txt";
  static SouthGateGeneralController parryGeneral;
  static SouthGateGeneralAttackPlan parryPlan;
  static InkPool parryInk;
  static CombatConfigSO parryConfig;
  static float parryInkBefore,parryInkAtImpact,parryGroggyBefore,parryGroggyAtImpact,parryDamage;
  static ParryOutcome parryOutcome;
  static bool parryInputStarted,parryImpactSeen;
  static double parryDeadline;
  static readonly List<string> parryTimeline=new List<string>();
  static void GateParryAttack(SouthGateGeneralAttackPlan plan){if(parryPlan==null){parryPlan=plan;parryTimeline.Add(Time.time+" attack="+plan.Kind+" release="+plan.Pulses[0].ReleaseAt);}}
  static void GateParryImpact(SouthGateGeneralImpact impact){if(impact.Plan!=parryPlan)return;parryImpactSeen=true;parryOutcome=impact.Outcome;parryDamage=impact.AppliedDamage;parryInkAtImpact=parryInk.Value;parryGroggyAtImpact=parryGeneral.Vitals.Groggy.Value01;parryTimeline.Add(Time.time+" contact="+impact.Contact+" outcome="+impact.Outcome+" damage="+parryDamage+" ink="+parryInkAtImpact+" groggy="+parryGroggyAtImpact);}
  static string GateParry(){
   var s=RoadRestSession();if(parryGeneral!=null||s.Progress.completed.Contains("cargo_delivery"))throw new Exception("Fresh isolated pre-delivery required");
   var actor=s.Encounters.Single(a=>a.Id=="south_gate_general");PrologueProgressStore.Complete(s.Progress,"cargo_delivery",0);s.Save();s.Teleport(actor.transform.position+Vector3.forward*14+Vector3.up*.2f,180);
   parryGeneral=actor.GetComponent<SouthGateGeneralController>();parryPlan=null;parryInputStarted=parryImpactSeen=false;parryOutcome=ParryOutcome.None;parryTimeline.Clear();parryDamage=parryInkAtImpact=parryGroggyAtImpact=0;
   parryInk=(InkPool)typeof(CombatLoopWiring).GetField("_ink",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s.Wiring);parryConfig=(CombatConfigSO)typeof(CombatLoopWiring).GetField("_config",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s.Wiring);
   parryInkBefore=parryInk.Value;parryGroggyBefore=parryGeneral.Vitals.Groggy.Value01;parryGeneral.AttackStarted+=GateParryAttack;parryGeneral.AttackImpactResolved+=GateParryImpact;
   parryDeadline=EditorApplication.timeSinceStartup+40;EditorApplication.update+=GateParryTick;File.WriteAllText(GateParryReport,"RUNNING");return "Waiting for live earth wave before real wood guard input";
  }
  static void GateParryTick(){
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play ended");if(EditorApplication.timeSinceStartup>parryDeadline)throw new Exception("Guard input/impact timeout");
    if(!parryInputStarted&&parryPlan!=null){
     if(parryPlan.Kind!=SouthGateAttackKind.EarthShockwave)throw new Exception("Expected live earth wave");
     var s=RoadRestSession();var target=s.Player.GetComponent<LockOn>();if(target.IsLocked)target.Toggle();target.Toggle();if(target.Target!=parryGeneral.Vitals)throw new Exception("General lock unavailable");
     parryInputStarted=true;parryTimeline.Add(Time.time+" guard input begins");Vfx120PlayerInputAudit.StartJourneyWoodGuard();
    }else if(parryInputStarted&&parryImpactSeen&&Vfx120PlayerInputAudit.Poll().Contains("\"FINISHED\"")){
     var evidence=JsonUtility.FromJson<GateCastEvidence>(Vfx120PlayerInputAudit.Poll());bool input=evidence.completed==1&&evidence.failed==0&&evidence.errors==0&&evidence.cases[0].recognitionSuccess&&evidence.cases[0].recognized=="거"&&evidence.cleanupStatus=="OBSERVED_INPUT_AND_EFFECT_CLEANUP";
     bool reward=Mathf.Abs(parryInkAtImpact-Mathf.Min(1,parryInkBefore-parryConfig.ParryInkCost+parryConfig.ParryInkRefund))<.001f&&parryGroggyAtImpact>parryGroggyBefore;
     FinishGateParry(input&&parryOutcome==ParryOutcome.Success&&parryDamage==0&&reward?null:"Guard input, successful impact or owned rewards not proven");
    }
   }catch(Exception e){FinishGateParry(e.Message);}
  }
  static void FinishGateParry(string error){
   EditorApplication.update-=GateParryTick;if(parryGeneral!=null){parryGeneral.AttackStarted-=GateParryAttack;parryGeneral.AttackImpactResolved-=GateParryImpact;}parryGeneral=null;
   File.WriteAllText(GateParryReport,(error==null?"PASS real wood guard input parries live earth wave with owned rewards":"FAIL "+error)+"\n"+string.Join("\n",parryTimeline)+"\nFixture delivery/position/lock; real recognition/guard/contact/rewards; no guard or damage injection. Not human timing/full boss fight.\n"+Vfx120PlayerInputAudit.Poll());
  }
 }
}
