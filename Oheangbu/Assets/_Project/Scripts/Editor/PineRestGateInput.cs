using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  [Serializable] sealed class GateCastEvidence { public string cleanupStatus; public int completed,failed,errors; public bool sceneFileUnchanged,cameraPropertiesRestored,timeScaleReturned,cameraPoseReturned; public GateCastRow[] cases; }
  [Serializable] sealed class GateCastRow { public string status,recognized; public bool recognitionSuccess; public float inkBefore,inkAfter,expectedInkCost; public int issuedHits; }
  static float gateInputBefore,gateInputDamage;
  static void GateInputDamaged(EnemyDamageResult result){if(result.Target==gateInputEnemy&&result.Attack.Source==DamageSource.PlayerDirect&&result.Attack.Element==Element.Wood)gateInputDamage+=result.AppliedDamage;}
  static void GateInputTick(){if(!EditorApplication.isPlaying){StopGateInputObservation();return;}var report=Vfx120PlayerInputAudit.Poll();if(report.Contains("\"FINISHED\"")){GateInputResult();StopGateInputObservation();}}
  static void StopGateInputObservation(){EditorApplication.update-=GateInputTick;if(gateInputEnemy!=null)gateInputEnemy.DamageResolved-=GateInputDamaged;}
  static EnemyVitals gateInputEnemy;
  static string GateInputPrepare(){
   var s=RoadRestSession();if(s.Progress.defeated.Contains("south_gate_general"))throw new Exception("Fresh isolated living boss required");
   PrologueProgressStore.Complete(s.Progress,"cargo_delivery",0);s.Save();
   var actor=s.Encounters.Single(a=>a.Id=="south_gate_general");s.Teleport(actor.transform.position+Vector3.forward*8+Vector3.up*.2f,180);
   return "Delivery/approach fixture prepared. Wait for runtime activation and camera settle before casting.";
  }
  static string GateInputCast(){
   var s=RoadRestSession();var actor=s.Encounters.Single(a=>a.Id=="south_gate_general");gateInputEnemy=actor.GetComponent<EnemyVitals>();
   if(!actor.gameObject.activeInHierarchy||!gateInputEnemy.IsAlive)throw new Exception("Active living general required");
   var target=s.Player.GetComponent<LockOn>();if(target.IsLocked)target.Toggle();target.Toggle();
   if(target.Target!=gateInputEnemy)throw new Exception("Existing target selection did not select general");
   StopGateInputObservation();gateInputBefore=gateInputEnemy.Hp;gateInputDamage=0;gateInputEnemy.DamageResolved+=GateInputDamaged;EditorApplication.update+=GateInputTick;return Vfx120PlayerInputAudit.StartJourneyWoodAttack();
  }
  static string GateInputResult(){
   var s=RoadRestSession();if(gateInputEnemy==null)throw new Exception("Run GateInputCast first");
   string input=Vfx120PlayerInputAudit.Poll();var evidence=JsonUtility.FromJson<GateCastEvidence>(input);
   bool recognized=evidence!=null&&evidence.completed==1&&evidence.failed==0&&evidence.errors==0&&evidence.sceneFileUnchanged&&evidence.cameraPropertiesRestored&&evidence.timeScaleReturned&&evidence.cleanupStatus=="OBSERVED_INPUT_AND_EFFECT_CLEANUP"&&evidence.cases!=null&&evidence.cases.Length==1&&evidence.cases[0].recognitionSuccess&&evidence.cases[0].recognized=="가"&&evidence.cases[0].status=="PASS_OBSERVED_INPUT_CAST_VFX"&&evidence.cases[0].issuedHits>0&&Mathf.Abs(evidence.cases[0].inkBefore-evidence.cases[0].inkAfter-evidence.cases[0].expectedInkCost)<.001f;
   float damage=gateInputDamage;
   string result=(recognized&&damage>0?"PASS ":"FAIL ")+"registered wood glyph through real input/recognition deals general damage="+damage+"; before="+gateInputBefore+"; after="+gateInputEnemy.Hp+"; fixedCameraPoseReturned="+evidence.cameraPoseReturned+"; playerHp="+s.Player.GetComponent<PlayerVitals>().Hp01+"\nInitial delivery/approach/lock fixture; live combat AI. No direct damage, ink refill, synthetic recognition or cast injection. Not manual handwriting, parry or full fight.\n"+input;
   File.WriteAllText("../Art/World/PineRest/south_gate_input.txt",result);return result;
  }
 }
}
