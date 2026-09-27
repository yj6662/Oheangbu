using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 new-game start check (SPEC-WORLD-FINISH-297 §2): Play the saved candidate with an isolated save suffix, wait for the
 // session, then record: player feet vs content start, the mine map zone at the feet, cave NavMesh binding of the relocated
 // mine actors, errors/exceptions logged, and 8 s of frame times (Editor Play, not a 120 fps judgement). Exits Play, restores
 // the suffix/startup state, deletes the isolated save files. finish297 runtime-start / runtime-status.
 [InitializeOnLoad]
 public static class Finish297Runtime
 {
  [Serializable] sealed class State
  {
   public bool Active,Finished;public string Status="",Suffix="",PriorSuffix="",PriorUi="",PriorStartup="";public bool PriorDirect,PriorDirectPresent;
   public double Began,At;public int Phase,Errors,Exceptions,Warnings;public List<string> Checks=new List<string>(),Log=new List<string>();
   public List<float> Frames=new List<float>();public Vector3 Feet,Start;public string Zone="";public int Step;
  }
  const string Key="Finish297.Runtime",ScenePath="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  static State state;
  static string Folder=>Path.GetFullPath(Application.dataPath+"/../../Art/World/Compact/Rebuild/Finish297/Runtime");
  static Finish297Runtime()
  {
   var json=SessionState.GetString(Key,"");if(!string.IsNullOrEmpty(json))state=JsonUtility.FromJson<State>(json);
   EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;Application.logMessageReceived+=OnLog;
  }
  public static string Run(string command)
  {
   if(command=="status")return Summary();
   if(command!="start")throw new ArgumentException(command);
   if(state?.Active==true||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("runtime check already active");
   var scene=SceneManager.GetActiveScene();if(scene.isDirty||scene.path!=ScenePath)throw new Exception("saved #296 candidate scene required");
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   state=new State{Active=true,Status="Starting",Began=EditorApplication.timeSinceStartup,At=EditorApplication.timeSinceStartup,Suffix="_finish297_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmss"),
    PriorSuffix=s.TestSaveSuffix,PriorUi=SessionState.GetString("PlaytestUiReviewSuffix","__missing__"),PriorStartup=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
    PriorDirect=SessionState.GetBool("Compact270.DirectPlay",false),PriorDirectPresent=SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true),Start=s.Content.StartFeet};
   s.TestSaveSuffix=state.Suffix;SessionState.SetString("PlaytestUiReviewSuffix",state.Suffix);CompactLoadingStartup270.UseCurrent();
   state.Status="Running";Persist();EditorApplication.isPlaying=true;return Summary();
  }
  static string Summary()=>state==null?"no #297 runtime run":state.Status+" phase="+state.Phase+" errors="+state.Errors+" exceptions="+state.Exceptions+" checks: "+string.Join(" | ",state.Checks);
  static void Persist(){SessionState.SetString(Key,JsonUtility.ToJson(state));Directory.CreateDirectory(Folder);File.WriteAllText(Folder+"/start.json",JsonUtility.ToJson(state,true));}
  static void Check(bool ok,string detail){state.Checks.Add((ok?"PASS ":"FAIL ")+detail);}
  static void OnLog(string message,string stack,LogType type)
  {
   if(state?.Active!=true||!EditorApplication.isPlaying)return;
   if(type==LogType.Exception){state.Exceptions++;if(state.Log.Count<40)state.Log.Add("EXC "+message);}
   else if(type==LogType.Error||type==LogType.Assert){state.Errors++;if(state.Log.Count<40)state.Log.Add("ERR "+message);}
   else if(type==LogType.Warning)state.Warnings++;
  }
  static void Tick()
  {
   if(state?.Active!=true||state.Status!="Running"||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   double now=EditorApplication.timeSinceStartup;
   try
   {
    if(now-state.Began>240)throw new TimeoutException("240 s deadline");
    var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
    if(s==null||!s.InitializationComplete){if(now-state.At>120)throw new Exception("session initialization timeout (see log)");return;}
    if(state.Phase==0)
    {
     var feet=s.Walker.Body.transform.position;state.Feet=feet;
     Check(Vector3.Distance(new Vector3(feet.x,0,feet.z),new Vector3(state.Start.x,0,state.Start.z))<2.5f,"new game starts at the relocated mine start "+feet.ToString("F2")+" (content "+state.Start.ToString("F2")+")");
     var ui=PlaytestUiRoot.Instance;var zone=ui!=null&&ui.MapData!=null?ui.MapData.ZoneAt(feet):null;state.Zone=zone?.Id??"";
     Check(zone!=null&&zone.Id=="mine_interior","map zone at the start = mine_interior (was "+(zone?.Id??"none")+")");
     foreach(var a in s.Actors.Where(a=>a!=null&&(a.Id.StartsWith("mine_"))))
     {var agent=a.GetComponent<NavMeshAgent>();Check(agent!=null&&agent.enabled&&agent.isOnNavMesh,"cave actor "+a.Id+" bound to NavMesh at "+a.transform.position.ToString("F1"));}
     state.Phase=1;state.At=now;Persist();return;
    }
    if(state.Phase==1)
    {
     state.Frames.Add(Time.unscaledDeltaTime*1000f);
     if(now-state.At>8){state.Phase=2;state.Step=0;state.At=now;Persist();}
     return;
    }
    if(state.Phase==2&&Interactions(s,now))return;
    if(state.Phase==2){state.Phase=3;Persist();state.Status="Stopping";EditorApplication.isPlaying=false;}
   }
   catch(Exception e){Check(false,"runtime: "+e.Message);state.Status="Stopping";Persist();EditorApplication.isPlaying=false;}
  }
  // #297 compound interactions in Play (isolated save): each 빗장 door cannot be focused from outside, unbars from inside,
  // commits the fact and opens (blockers/obstacle off); the 성황당 altar rests and sets the checkpoint. Returns true while busy.
  static bool Interactions(WorldMacroPlaytestSession s,double now)
  {
   var doors=Object.FindObjectsByType<WorldShortcutDoor>(FindObjectsSortMode.None).OrderBy(d=>d.Id).ToArray();
   var rest=s.Content.Checkpoints.FirstOrDefault(c=>c!=null&&c.Id=="cheolong_fortress_rest297");
   int k=state.Step/4,sub=state.Step%4;double wait=now-state.At;
   Vector3 Ground(Vector3 p){var hits=Physics.RaycastAll(p+Vector3.up*1.5f,Vector3.down,6f,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.5f&&!h.transform.IsChildOf(s.Walker.Body.transform)).OrderByDescending(h=>h.point.y).ToArray();return hits.Length>0?hits[0].point:p;}
   void Next(){state.Step++;state.At=now;Persist();}
   if(k<doors.Length)
   {
    var d=doors[k];float yaw=d.transform.eulerAngles.y;
    if(sub==0){s.Teleport(Ground(d.transform.TransformPoint(new Vector3(0,0,-1.1f))),yaw);Next();return true;}
    if(sub==1){if(wait<.6)return true;Check(!s.CanInteract(d.Id)&&!d.Opened&&d.Blockers.All(b=>b.enabled),"door "+d.Id+" closed and not focusable from outside");
     s.Teleport(Ground(d.transform.TransformPoint(new Vector3(0,0,1.1f))),yaw+180);Next();return true;}
    if(sub==2){if(wait<.6)return true;bool can=s.CanInteract(d.Id);bool done=can&&s.Interact(d.Id);
     Check(can&&done&&s.Progress.ledger.completed.Contains(d.Id),"door "+d.Id+" unbarred from inside (focus="+can+", committed="+s.Progress.ledger.completed.Contains(d.Id)+")");Next();return true;}
    if(wait<d.Duration+.8)return true;
    Check(d.Passable&&d.Blockers.All(b=>!b.enabled)&&(d.Obstacle==null||!d.Obstacle.enabled)&&!s.CanInteract(d.Id),"door "+d.Id+" open after "+d.Duration+" s: blockers off, obstacle off, no longer an interaction");Next();return true;
   }
   if(rest==null){Check(false,"rest checkpoint cheolong_fortress_rest297 missing");return false;}
   int r=state.Step-4*doors.Length;
   if(r==0){s.Teleport(rest.Feet,rest.Yaw);Next();return true;}
   if(r==1){if(wait<.6)return true;bool can=s.CanInteract(rest.Id);bool done=can&&s.Interact(rest.Id);Check(can&&done,"rest "+rest.Id+" focusable and used from its feet (focus="+can+")");Next();return true;}
   if(wait<3)return true;
   Check(s.Progress.ledger.checkpoint==rest.Id,"rest sets the checkpoint (now "+s.Progress.ledger.checkpoint+")");return false;
  }
  static void Changed(PlayModeStateChange mode)
  {
   if(state?.Active!=true||mode!=PlayModeStateChange.EnteredEditMode)return;
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s!=null){s.TestSaveSuffix=state.PriorSuffix;EditorUtility.ClearDirty(s);}
   if(state.PriorUi=="__missing__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",state.PriorUi);
   if(state.PriorDirectPresent)SessionState.SetBool("Compact270.DirectPlay",state.PriorDirect);else SessionState.EraseBool("Compact270.DirectPlay");
   EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.PriorStartup)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
   int deleted=0;foreach(var f in Directory.GetFiles(Application.persistentDataPath,"*"+state.Suffix+"*",SearchOption.TopDirectoryOnly)){File.Delete(f);deleted++;}
   var frames=state.Frames.Skip(30).OrderBy(x=>x).ToArray();
   if(frames.Length>0)state.Checks.Add("INFO Editor Play frame ms (after 30 warm-up frames, n="+frames.Length+"): median="+frames[frames.Length/2].ToString("F2")+" p95="+frames[(int)(frames.Length*.95f)].ToString("F2")+" (Editor, not a build)");
   Check(state.Exceptions==0,"no exceptions during start ("+state.Exceptions+")");state.Checks.Add("INFO errors logged="+state.Errors+" warnings="+state.Warnings+"; isolated saves deleted="+deleted);
   state.Active=false;state.Finished=true;state.Status=state.Checks.Any(c=>c.StartsWith("FAIL"))?"FAIL":"PASS";Persist();
  }
 }
 public static partial class CompactRebuildAuthoring
 {
  static string Runtime297(string arg)=>Finish297Runtime.Run(arg);
 }
}
