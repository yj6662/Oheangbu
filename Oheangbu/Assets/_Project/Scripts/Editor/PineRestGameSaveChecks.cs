using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string SaveFailureChecks(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   var field=typeof(PrologueSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic);var original=field.GetValue(s);
   string blocker=Path.GetFullPath("../Art/World/PineRest/save_failure_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"Audit-only file prevents directory creation");
   var broken=new PrologueProgressStore(Path.Combine(blocker,"state.json"));var report=new List<string>();void Check(bool v,string text){report.Add((v?"PASS ":"FAIL ")+text);}
   var hp=s.Player.GetComponent<PlayerVitals>();
   try{
    var reward=s.Content.Points.Single(p=>p.Id=="WorkerSatchel");s.Teleport(reward.Position+Vector3.up,0);s.Interact(reward.Id);int funds=s.Progress.currency;
    hp.TakeDamage(10);float damaged=hp.Hp01;string checkpoint=s.Progress.checkpoint;
    field.SetValue(s,broken);var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);
    Check(s.Progress.checkpoint==checkpoint&&Mathf.Approximately(hp.Hp01,damaged),"failed rest preserves checkpoint and health");
    field.SetValue(s,original);hp.ApplyFatalFall();Check(s.Progress.dropCurrency==funds&&s.Progress.currency==0,"successful death creates drop");
    s.Teleport(s.Progress.dropPosition,0);field.SetValue(s,broken);s.Interact("CurrencyDrop");Check(s.Progress.dropCurrency==funds&&s.Progress.currency==0,"failed retrieval preserves drop");
    field.SetValue(s,original);s.Interact("CurrencyDrop");Check(s.Progress.currency==funds&&s.Progress.dropCurrency==0,"retrieval retry pays once");
    field.SetValue(s,broken);hp.ApplyFatalFall();Check(hp.Hp01==0&&s.Progress.currency==funds&&s.Progress.dropCurrency==0,"failed death defers respawn and state transition");
    Check(!Object.FindFirstObjectByType<ProloguePauseMenu>().SaveProgress(),"menu reports failed pending death save");
    field.SetValue(s,original);s.Save();Check(hp.Hp01==1&&s.Progress.currency==0&&s.Progress.dropCurrency==funds,"manual retry commits death and respawns");
    var stored=((PrologueProgressStore)original).Load();Check(stored.currency==0&&stored.dropCurrency==funds&&stored.hp==1&&Vector3.Distance(stored.position,stored.checkpointPosition)<.01f,"disk contains complete respawn transaction");
   }finally{field.SetValue(s,original);File.Delete(blocker);}
   report.Add("Real IOException via audit-owned invalid directory; direct interactions/damage, not manual playthrough.");var text=string.Join("\n",report);File.WriteAllText("../Art/World/PineRest/save_failure_checks.txt",text);return text;
  }
 }
}
