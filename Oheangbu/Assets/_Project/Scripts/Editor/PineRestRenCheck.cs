using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static double renAt;const string RenReport="../Art/World/PineRest/ren_checks.txt";
  static string RenReloadCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");
   var hp=s.Player.GetComponent<PlayerVitals>();bool pass=s.HasGuk&&s.Progress.renUsed&&!s.RenAvailable&&Mathf.Approximately(hp.Hp01*hp.MaxHp,1);
   var result=(pass?"PASS ":"FAIL ")+"new Play restores HP1 and used Ren charge; suffix="+s.TestSaveSuffix;File.WriteAllText("../Art/World/PineRest/ren_reload_check.txt",result);return result;
  }
  static string RenCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s?.Progress==null||!s.TestSaveSuffix.StartsWith("-audit-")||s.HasGuk)throw new Exception("Fresh isolated Play required");
   CommissionCheck();renAt=EditorApplication.timeSinceStartup+.4;EditorApplication.update+=RenTick;File.WriteAllText(RenReport,"RUNNING");return "Ren transaction audit running";
  }
  static void RenTick(){
   if(EditorApplication.timeSinceStartup<renAt)return;EditorApplication.update-=RenTick;
   var checks=new List<string>();void Check(bool v,string n){checks.Add((v?"PASS ":"FAIL ")+n);}
   var s=Object.FindFirstObjectByType<PrologueSession>();var hp=s.Player.GetComponent<PlayerVitals>();
   var field=typeof(PrologueSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic);var original=field.GetValue(s);
   string blocker=Path.GetFullPath("../Art/World/PineRest/ren_block_"+Guid.NewGuid().ToString("N"));int deaths=0;Action died=()=>deaths++;hp.Died+=died;
   try{
    Check(!s.RenAvailable,"Ren locked before boss");
    s.Encounters.Single(a=>a.Id=="cheongryong").GetComponent<EnemyVitals>().TakeDamage(100000);Check(s.RenAvailable,"boss record unlocks Ren");
    var groggy=(GroggyMeter)typeof(PrologueSession).GetField("groggy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);groggy.AddFromParry();float meter=groggy.Value01;int funds=s.Progress.currency;
    hp.TakeDamage(hp.MaxHp*10);Check(Mathf.Approximately(hp.Hp01*hp.MaxHp,1)&&deaths==0,"lethal damage survives at HP1 without death");Check(s.Progress.currency==funds&&s.Progress.dropCurrency==0&&Mathf.Approximately(groggy.Value01,meter),"Ren preserves currency and groggy");
    var disk=((PrologueProgressStore)original).Load();Check(disk.renUsed&&Mathf.Approximately(disk.hp*hp.MaxHp,1),"HP1 and consumed charge committed together");
    hp.TakeDamage(hp.MaxHp*10);Check(deaths==1&&!s.RenAvailable&&s.Progress.dropCurrency==funds,"second lethal hit causes ordinary death");
    var rest=s.Content.Points.Single(p=>p.Id=="InnRest");s.Teleport(rest.Position+Vector3.up,0);
    File.WriteAllText(blocker,"audit-only invalid directory");field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"save.json")));s.Interact(rest.Id);Check(!s.RenAvailable,"failed rest cannot recharge");
    field.SetValue(s,original);s.Interact(rest.Id);Check(s.RenAvailable&&!((PrologueProgressStore)original).Load().renUsed,"successful rest recharges on disk");
    hp.ApplyFatalFall();Check(deaths==2&&s.RenAvailable,"environmental death bypasses Ren without consuming it");
    field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"save.json")));hp.TakeDamage(hp.MaxHp*10);Check(hp.Hp01==0&&!s.Progress.renUsed&&deaths==3,"failed survival save grants no HP benefit or consumed charge");field.SetValue(s,original);s.Save();
    hp.TakeDamage(hp.MaxHp*10);Check(Mathf.Approximately(hp.Hp01*hp.MaxHp,1)&&s.Progress.renUsed,"recovered store permits one subsequent survival");
   }catch(Exception e){checks.Add("FAIL "+e);}
   finally{hp.Died-=died;field.SetValue(s,original);if(File.Exists(blocker))File.Delete(blocker);}
   checks.Add("Direct gameplay damage/interaction and real IOException; not live input combat or application restart.");File.WriteAllText(RenReport,string.Join("\n",checks));
  }
 }
}
