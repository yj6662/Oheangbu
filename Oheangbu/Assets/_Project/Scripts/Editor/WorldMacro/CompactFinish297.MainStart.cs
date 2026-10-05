using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 promotion check: Play from the lobby exactly like the player (lobby start scene, the lobby's own "new journey"
 // entry), with an isolated diagnostic save suffix; wait for W_Demo_Main to load and record what arrived. Deletes the
 // isolated save files and restores the start-scene / suffix state on exit. finish297 main-start / main-status.
 [InitializeOnLoad]
 public static class Finish297MainStart
 {
  [Serializable] sealed class State
  {
   public bool Active;public string Status="",Suffix="",PriorUi="",PriorStartup="";public bool PriorDirect,PriorDirectPresent;
   public double Began,At;public int Phase,Exceptions,Errors;public List<string> Checks=new List<string>(),Log=new List<string>();public List<float> Frames=new List<float>();
  }
  const string Key="Finish297.MainStart",Lobby="Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity",Main="W_Demo_Main";
  static State state;
  static string Folder=>Path.GetFullPath(Application.dataPath+"/../../Art/World/Compact/Rebuild/Finish297/Promote");
  static Finish297MainStart()
  {
   var json=SessionState.GetString(Key,"");if(!string.IsNullOrEmpty(json))state=JsonUtility.FromJson<State>(json);
   EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;Application.logMessageReceived+=OnLog;
  }
  static void Persist(){SessionState.SetString(Key,JsonUtility.ToJson(state));Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/main-start.json",JsonUtility.ToJson(state,true));}
  static void Check(bool ok,string detail)=>state.Checks.Add((ok?"PASS ":"FAIL ")+detail);
  static string Summary()=>state==null?"no main-start run":state.Status+" phase="+state.Phase+" exceptions="+state.Exceptions+" errors="+state.Errors+" checks: "+string.Join(" | ",state.Checks);
  public static string Run(string command)
  {
   if(command=="status")return Summary();
   if(state?.Active==true||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("already running");
   if(SceneManager.GetActiveScene().isDirty)throw new Exception("save the open scene first");
   state=new State{Active=true,Status="Running",Began=EditorApplication.timeSinceStartup,At=EditorApplication.timeSinceStartup,Suffix="_main297_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmss"),
    PriorUi=SessionState.GetString("PlaytestUiReviewSuffix","__missing__"),PriorStartup=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
    PriorDirect=SessionState.GetBool("Compact270.DirectPlay",false),PriorDirectPresent=SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true)};
   SessionState.SetString("PlaytestUiReviewSuffix",state.Suffix);SessionState.SetBool("Compact270.DirectPlay",false);
   EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(Lobby);
   Persist();EditorApplication.isPlaying=true;return Summary();
  }
  static void OnLog(string message,string stack,LogType type)
  {
   if(state?.Active!=true||!EditorApplication.isPlaying)return;
   if(type==LogType.Exception){state.Exceptions++;if(state.Log.Count<40)state.Log.Add("EXC "+message);}
   else if(type==LogType.Error||type==LogType.Assert){state.Errors++;if(state.Log.Count<40)state.Log.Add("ERR "+message);}
  }
  static void Tick()
  {
   if(state?.Active!=true||state.Status!="Running"||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   double now=EditorApplication.timeSinceStartup;
   try
   {
    if(now-state.Began>300)throw new TimeoutException("300 s deadline (phase "+state.Phase+")");
    var ui=PlaytestUiRoot.Instance;
    if(state.Phase==0)
    {
     if(ui==null||!ui.IsTitle||ui.Busy||now-state.At<2)return;
     Check(ui.PlaySceneName==Main&&ui.ActiveSlotName=="world-main"+state.Suffix,"lobby targets "+ui.PlaySceneName+" slot "+ui.ActiveSlotName);
     typeof(PlaytestUiRoot).GetMethod("StartNew",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
     state.Phase=1;state.At=now;Persist();return;
    }
    if(state.Phase==1)
    {
     var scene=SceneManager.GetSceneByName(Main);var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
     if(!scene.isLoaded||s==null||!s.InitializationComplete||ui==null||ui.LoadingInProgress){if(now-state.At>200)throw new Exception("main scene did not finish loading");return;}
     Check(s.gameObject.scene.name==Main,"session runs in "+s.gameObject.scene.name+" (loaded from the lobby in "+(now-state.At).ToString("F1")+" s)");
     Check(s.Content.SaveSlot=="world-main"&&s.TestSaveSuffix==state.Suffix,"save slot "+s.Content.SaveSlot+s.TestSaveSuffix+" (isolated for this check)");
     var feet=s.Walker.Body.transform.position;var start=s.Content.StartFeet;
     Check(Vector3.Distance(new Vector3(feet.x,0,feet.z),new Vector3(start.x,0,start.z))<2.5f,"new journey starts at the relocated mine "+feet.ToString("F1"));
     var roots=scene.GetRootGameObjects().Select(g=>g.name).ToArray();
     foreach(var r in new[]{"Finish297_Cheolong","Folklore298_Encounters"})Check(roots.Contains(r),"scene root "+r);
     Check(s.Actors.Count(a=>a!=null&&a.Id.StartsWith("folklore298/"))==6&&s.Actors.Length==16,"enemies (15 + #306 mine tutorial boss): "+s.Actors.Length+" (folklore298 "+s.Actors.Count(a=>a!=null&&a.Id.StartsWith("folklore298/"))+")");
     Check(Object.FindObjectsByType<WorldShortcutDoor>(FindObjectsSortMode.None).Length==3&&s.Content.Checkpoints.Any(c=>c.Id=="cheolong_fortress_rest297"),"shortcut doors 3 (산성 2 + 궁성 1) + 성황당 checkpoint present");
     state.Phase=2;state.At=now;Persist();return;
    }
    if(state.Phase==2){state.Frames.Add(Time.unscaledDeltaTime*1000f);if(now-state.At>6){state.Phase=3;state.Status="Stopping";Persist();EditorApplication.isPlaying=false;}}
   }
   catch(Exception e){Check(false,"main-start: "+e.Message);state.Status="Stopping";Persist();EditorApplication.isPlaying=false;}
  }
  static void Changed(PlayModeStateChange mode)
  {
   if(state?.Active!=true||mode!=PlayModeStateChange.EnteredEditMode)return;
   if(state.PriorUi=="__missing__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",state.PriorUi);
   if(state.PriorDirectPresent)SessionState.SetBool("Compact270.DirectPlay",state.PriorDirect);else SessionState.EraseBool("Compact270.DirectPlay");
   EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.PriorStartup)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
   int deleted=0;foreach(var f in Directory.GetFiles(Application.persistentDataPath,"*"+state.Suffix+"*",SearchOption.TopDirectoryOnly)){File.Delete(f);deleted++;}
   var frames=state.Frames.Skip(20).OrderBy(x=>x).ToArray();
   if(frames.Length>0)state.Checks.Add("INFO Editor Play frame ms after arrival (n="+frames.Length+"): median="+frames[frames.Length/2].ToString("F2")+" (Editor, not a build)");
   Check(state.Exceptions==0,"no exceptions lobby → main ("+state.Exceptions+"; errors logged "+state.Errors+")");state.Checks.Add("INFO isolated saves deleted="+deleted);
   state.Active=false;state.Status=state.Checks.Any(c=>c.StartsWith("FAIL"))?"FAIL":"PASS";Persist();
  }
 }
}
