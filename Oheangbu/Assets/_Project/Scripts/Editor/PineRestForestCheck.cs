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
  static double forestAt;static int forestStep;static bool forestAttack;
  static readonly List<string> forestChecks=new List<string>();
  const string ForestReport="../Art/World/PineRest/forest_runtime_checks.txt";
  static string ForestReloadCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   var boss=s.Encounters.Single(a=>a.Id=="cheongryong");bool passed=s.Progress.defeated.Contains(boss.Id)&&!boss.gameObject.activeSelf&&s.Progress.checkpoint=="ForestRest";
   string result=(passed?"PASS ":"FAIL ")+"fresh Play session restored defeated boss and ForestRest checkpoint; suffix="+s.TestSaveSuffix;File.WriteAllText("../Art/World/PineRest/forest_reload_check.txt",result);return result;
  }
  static string ForestCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-")||s.Progress.completed.Contains("j1:reported"))throw new Exception("Fresh isolated Play required");
   forestChecks.Clear();forestStep=0;forestAttack=false;forestAt=EditorApplication.timeSinceStartup;EditorApplication.update+=ForestTick;File.WriteAllText(ForestReport,"RUNNING");return "Forest lifecycle and live attack observation running";
  }
  static void ForestTick(){
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play stopped");
    var s=Object.FindFirstObjectByType<PrologueSession>();var actor=s.Encounters.Single(a=>a.Id=="cheongryong");var boss=actor.GetComponent<CheongryongCombatController>();
    void Check(bool v,string n){forestChecks.Add((v?"PASS ":"FAIL ")+n);}
    if(forestStep==0){Check(!actor.gameObject.activeSelf,"boss gated before J1 report");CommissionCheck();forestAt=EditorApplication.timeSinceStartup+.5;forestStep++;return;}
    if(forestStep==1&&EditorApplication.timeSinceStartup>=forestAt){
     Check(actor.gameObject.activeSelf&&actor.GetComponent<NavMeshAgent>().isOnNavMesh,"boss activates on navigation after report");
     var rest=s.Content.Points.Single(p=>p.Id=="ForestRest");var path=new NavMeshPath();bool pathOk=NavMesh.SamplePosition(rest.Position,out var from,3,NavMesh.AllAreas)&&NavMesh.SamplePosition(actor.transform.position,out var to,3,NavMesh.AllAreas)&&NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;Check(pathOk,"rest to boss navigation connection");
     s.Teleport(actor.transform.position+Vector3.back*10,0);forestAt=EditorApplication.timeSinceStartup+12;forestStep++;return;
    }
    if(forestStep==2){forestAttack|=boss.AttackInProgress;if(EditorApplication.timeSinceStartup<forestAt)return;Check(forestAttack,"live boss AI enters attack plan");ScreenCapture.CaptureScreenshot(Path.GetFullPath("../Art/World/PineRest/forest_game.png"));forestAt=EditorApplication.timeSinceStartup+.5;forestStep++;return;}
    if(forestStep==3&&EditorApplication.timeSinceStartup>=forestAt){
     var field=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var original=field.GetValue(s);var blocker=Path.GetFullPath("../Art/World/PineRest/boss_save_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit file");int before=s.Progress.currency;
     try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"save.json")));actor.GetComponent<EnemyVitals>().TakeDamage(100000);Check(!s.Progress.defeated.Contains(actor.Id),"failed boss save leaves durable progress unchanged");}
     finally{field.SetValue(s,original);File.Delete(blocker);}
     s.Save();Check(s.Progress.defeated.Contains(actor.Id)&&s.Progress.currency==before+s.Content.EnemyReward,"save retry commits boss reward once");s.EnemyDefeated(actor.Id);Check(s.Progress.currency==before+s.Content.EnemyReward,"duplicate death reward ignored");
     var rest=s.Content.Points.Single(p=>p.Id=="ForestRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);Check(!actor.GetComponent<EnemyVitals>().IsAlive,"rest does not restore defeated boss");
     var loaded=((PrologueProgressStore)original).Load();Check(loaded.defeated.Contains(actor.Id)&&loaded.checkpoint=="ForestRest","boss and approach checkpoint persist on disk");forestAt=EditorApplication.timeSinceStartup+.3;forestStep++;return;
    }
    if(forestStep==4&&EditorApplication.timeSinceStartup>=forestAt){Check(!actor.gameObject.activeSelf,"committed boss removed from active encounters");FinishForestCheck();}
   }catch(Exception e){forestChecks.Add("FAIL "+e);FinishForestCheck();}
  }
  static void FinishForestCheck(){EditorApplication.update-=ForestTick;forestChecks.Add("J1 setup/direct interactions/synthetic final damage; live attack observation only. Not full boss input combat or application restart.");File.WriteAllText(ForestReport,string.Join("\n",forestChecks));}
 }
}
