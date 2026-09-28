using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 [InitializeOnLoad]
 public static class CompactRebuildRuntimeChecks
 {
  const string Key="CompactRebuild.RuntimeSuffix";
  const string Prior="CompactRebuild.PriorSuffix";
  static CompactRebuildRuntimeChecks(){EditorApplication.playModeStateChanged+=Changed;}
  static WorldMacroPlaytestSession Session()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  static void Changed(PlayModeStateChange state)
  {
   if(state!=PlayModeStateChange.EnteredEditMode||string.IsNullOrEmpty(SessionState.GetString(Key,"")))return;
   if(SceneManager.GetActiveScene().path==CompactRebuildAuthoring.Scene)Session().TestSaveSuffix=SessionState.GetString(Prior,"");
   SessionState.EraseString(Key);SessionState.EraseString(Prior);
  }
  public static string Run(string command)
  {
   if(SceneManager.GetActiveScene().path!=CompactRebuildAuthoring.Scene)throw new Exception("Canonical Compact required");
   if(command=="play")
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Already playing");
    var session=Session();
    if(session.Content.SaveSlot!="world-demo-compact-rebuild-v2")throw new Exception("Isolated rebuild slot required");
    string suffix="_rebuild_check_"+Guid.NewGuid().ToString("N");
    SessionState.SetString(Prior,session.TestSaveSuffix);SessionState.SetString(Key,suffix);session.TestSaveSuffix=suffix;
    EditorApplication.EnterPlaymode();return "Entering Compact with isolated diagnostic save "+suffix;
   }
   if(string.IsNullOrEmpty(SessionState.GetString(Key,"")))throw new Exception("No diagnostic run");
   if(command=="stop"){EditorApplication.ExitPlaymode();return "Stopping diagnostic Play; transient suffix will be restored";}
   if(command!="runtime")throw new ArgumentException(command);
   var s=Session();var p=s.Progress;
   if(!EditorApplication.isPlaying||p==null)throw new Exception("Play startup not ready");
   var lines=new System.Collections.Generic.List<string>();
   void Check(bool value,string name)=>lines.Add((value?"PASS ":"FAIL ")+name);
   Check(s.TestSaveSuffix==SessionState.GetString(Key,""),"diagnostic save namespace applied before startup");
   Check(s.DemoCampaignActive&&s.Content.Campaign.UseExplicitPrerequisites,"explicit campaign active in actual Compact runtime");
   Check(WorldMacroProgress.Valid(p)&&!s.SaveBlocked&&string.IsNullOrEmpty(s.SaveError),"runtime progress valid without save error");
   Check(p.campaign.Completed.Contains("commission")&&p.campaign.Facts.Contains("case:mine_commissioned"),"fresh game starts commissioned");
   Check(!p.campaign.Completed.Contains("office_report"),"office report is not auto-completed");
   Check(string.IsNullOrEmpty(s.DemoObjective),"persistent campaign objective suppressed");
   Check(s.Content.Opening==null,"forced office opening removed");
   Check(Vector3.Distance(s.Walker.Body.transform.position,s.Content.StartFeet)<10,"fresh spawn remains at authored mine");
   lines.Add("Startup only. No claim of combat, escort traversal, disk-failure recovery, GPU timing or visual approval.");
   string result=string.Join("\n",lines);File.WriteAllText("../Art/World/Compact/Rebuild/runtime_checks.txt",result);return result;
  }
 }
}
