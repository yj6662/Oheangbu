using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static double growthAt;static int growthStep,growthBalance;
  static readonly List<string> growthChecks=new List<string>();const string GrowthReport="../Art/World/PineRest/growth_lesson_checks.txt";
  static string GrowthCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-")||s.Progress.completed.Contains("j1:reported"))throw new Exception("Fresh isolated Play required");
   growthChecks.Clear();growthStep=0;growthAt=EditorApplication.timeSinceStartup;EditorApplication.update+=GrowthTick;File.WriteAllText(GrowthReport,"RUNNING");return "Growth proof and persistence checks running";
  }
  static void GrowthTick(){
   if(EditorApplication.timeSinceStartup<growthAt)return;
   try{
    if(!EditorApplication.isPlaying)throw new Exception("Play stopped");var s=Object.FindFirstObjectByType<PrologueSession>();var actor=s.Encounters.Single(a=>a.Id=="journey_growth_lesson");var link=actor.GetComponent<JourneyGrowthLesson>();var growth=actor.GetComponent<CheongryongGrowthController>();var v=actor.GetComponent<EnemyVitals>();
    void Check(bool pass,string label){growthChecks.Add((pass?"PASS ":"FAIL ")+label);}
    AttackProvenance Hit(Element element)=>AttackProvenance.Create(s.Player,DamageSource.PlayerDirect,element);
    if(growthStep==0){Check(!actor.gameObject.activeSelf,"lesson dormant before J1 report");CommissionCheck();growthStep++;growthAt=EditorApplication.timeSinceStartup+.5;return;}
    if(growthStep==1){
     Check(actor.gameObject.activeSelf&&actor.GetComponent<NavMeshAgent>().isOnNavMesh,"reported quest activates lesson on navigation");Check(!s.RecordGrowthLesson(link),"interaction cannot fabricate interruption proof");
     v.TakeDamage(100000);Check(!s.Progress.completed.Contains(JourneyGrowthLesson.CompletionId),"kill alone does not complete lesson");var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);
     v.TakeDamage(v.MaxHp*.5f,Hit(Element.Metal));Check(growth.IsWindingUp&&!link.HasProof,"threshold Metal hit starts rather than cancels growth");v.TakeDamage(1,Hit(Element.Wood));v.TakeDamage(0,Hit(Element.Metal));v.TakeDamage(1,new AttackProvenance(0,s.Player,DamageSource.PlayerDirect,Element.Metal));Check(growth.IsWindingUp&&!link.HasProof,"Wood zero-damage and unconfirmed Metal cannot prove interruption");
     var storeField=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var original=storeField.GetValue(s);string blocker=Path.GetFullPath("../Art/World/PineRest/growth_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit only");
     try{storeField.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));v.TakeDamage(1,Hit(Element.Metal));Check(growth.WasInterrupted&&link.HasProof&&!s.Progress.completed.Contains(JourneyGrowthLesson.CompletionId),"confirmed interruption retained while save fails");}
     finally{storeField.SetValue(s,original);File.Delete(blocker);}
     s.Interact(rest.Id);growthBalance=s.Progress.currency;Check(link.HasProof,"rest preserves pending proof");growthStep++;growthAt=EditorApplication.timeSinceStartup+1.3;return;
    }
    Check(s.Progress.completed.Contains(JourneyGrowthLesson.CompletionId),"pending proof automatically committed after recovery");Check(s.Progress.currency==growthBalance,"lesson adds no duplicate currency reward");var disk=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();Check(disk.completed.Contains(JourneyGrowthLesson.CompletionId),"lesson proof persisted to disk");FinishGrowthCheck();
   }catch(Exception e){growthChecks.Add("FAIL "+e);FinishGrowthCheck();}
  }
  static void FinishGrowthCheck(){EditorApplication.update-=GrowthTick;growthChecks.Add("Direct gameplay damage with real provenance and IOException. Not actual mouse casting or manual combat learning.");File.WriteAllText(GrowthReport,string.Join("\n",growthChecks));}
 }
}
