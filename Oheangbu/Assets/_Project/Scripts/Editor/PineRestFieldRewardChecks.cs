using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string FieldRewardCheck(){
   var s=RoadRestSession();var actor=s.Encounters.First(a=>a!=null&&a.gameObject.activeInHierarchy&&!s.Content.EncounterRules.Any(r=>r.Id==a.Id&&r.PersistentDefeat));
   var field=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var repository=(PrologueProgressStore)field.GetValue(s);
   var report=new List<string>();void Check(bool ok,string label)=>report.Add((ok?"PASS ":"FAIL ")+label);
   string blocker=Path.GetFullPath("../Art/World/PineRest/field_reward_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit");
   var broken=new PrologueProgressStore(Path.Combine(blocker,"state.json"));var vitals=actor.GetComponent<EnemyVitals>();int reward=s.Content.EnemyReward;
   try{
    if(!vitals.IsAlive)throw new Exception("Fresh living field encounter required");
    int money=s.Progress.currency;s.EnemyDefeated(actor.Id);s.EnemyDefeated("missing-field-enemy");Check(s.Progress.currency==money,"living or unknown enemy cannot mint currency");
    field.SetValue(s,broken);vitals.TakeDamage(100000);s.EnemyDefeated(actor.Id);
    Check(!vitals.IsAlive&&s.Progress.currency==money,"actual death with IOException leaves reward unpublished");
    field.SetValue(s,repository);s.Save();Check(s.Progress.currency==money+reward&&repository.Load().currency==money+reward&&repository.Load().defeated.Contains(actor.Id),"retry persists pending field reward and defeat once");
    s.EnemyDefeated(actor.Id);s.Save();Check(s.Progress.currency==money+reward,"duplicate dead-life notification cannot pay twice");
    var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);s.Interact(rest.Id);Check(vitals.IsAlive,"successful rest respawns ordinary enemy");
    vitals.TakeDamage(100000);Check(s.Progress.currency==money+reward*2,"new life awards again despite historical defeat record");
    s.Interact(rest.Id);field.SetValue(s,broken);vitals.TakeDamage(100000);s.Player.GetComponent<PlayerVitals>().ApplyFatalFall();
    Check(s.Progress.currency==money+reward*2&&s.Player.GetComponent<PlayerVitals>().Hp01==0,"failed death holds previous state and respawn");
    field.SetValue(s,repository);s.Save();Check(s.Progress.currency==0&&s.Progress.dropCurrency==money+reward*3&&s.Player.GetComponent<PlayerVitals>().Hp01==1,"pending field reward joins atomic death drop and respawn");
    s.Save();Check(repository.Load().currency==0&&repository.Load().dropCurrency==money+reward*3,"repeated save does not repay pending reward after death");
   }finally{field.SetValue(s,repository);File.Delete(blocker);}
   report.Add("Isolated save; actual EnemyVitals death and IOException; direct damage/rest/death, not manual combat. Pending evidence is in memory until storage succeeds.");
   var result=string.Join("\n",report);File.WriteAllText("../Art/World/PineRest/field_reward_checks.txt",result);return result;
  }
 }
}
