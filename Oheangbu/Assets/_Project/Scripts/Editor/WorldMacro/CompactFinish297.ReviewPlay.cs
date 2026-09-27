using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 review Play (editor only): plays the saved candidate scene with an isolated save suffix and places the player at a
 // review spot of a compound once the session is ready. On exit it restores the suffix / start-scene state and deletes
 // the isolated save files, so the user's own save never sees the teleport. Menu: Oheangbu/#297 검토/...
 [InitializeOnLoad]
 public static class Finish297ReviewPlay
 {
  [Serializable] sealed class State{public bool Active,Placed;public string Spot="",Suffix="",PriorSuffix="",PriorUi="",PriorStartup="";public bool PriorDirect,PriorDirectPresent;}
  const string Key="Finish297.ReviewPlay",ScenePath="Assets/_Project/Art/World/Architecture296/W_Demo_Compact_Architecture296.unity";
  static State state;
  static Finish297ReviewPlay()
  {
   var json=SessionState.GetString(Key,"");if(!string.IsNullOrEmpty(json))state=JsonUtility.FromJson<State>(json);
   EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;
  }
  [MenuItem("Oheangbu/#297 검토/철옹 산성 — 정문 밖 접근에서 Play")] static void Approach()=>Start("approach");
  [MenuItem("Oheangbu/#297 검토/철옹 산성 — 성황당(큰 계단 아래)에서 Play")] static void Rest()=>Start("rest");
  [MenuItem("Oheangbu/#297 검토/철옹 산성 — 내성 북문 안쪽(빗장 앞)에서 Play")] static void NorthGate()=>Start("northgate");
  [MenuItem("Oheangbu/#297 검토/철옹 산성 — 동장대 앞에서 Play")] static void Jangdae()=>Start("jangdae");
  [MenuItem("Oheangbu/#297 검토/철옹 산성 — 군기전 입구에서 Play")] static void Hall()=>Start("hall");

  static void Persist()=>SessionState.SetString(Key,JsonUtility.ToJson(state));
  internal static string Status()
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   return "active="+(state?.Active==true)+" placed="+(state?.Placed==true)+" spot="+state?.Spot+" playing="+EditorApplication.isPlaying+
    (EditorApplication.isPlaying&&s!=null&&s.Walker!=null?" player="+s.Walker.Body.transform.position.ToString("F1"):"");
  }
  internal static void Start(string spot)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode){EditorUtility.DisplayDialog("#297 검토","Play 중에는 시작할 수 없습니다. Play를 끝낸 뒤 다시 고르세요.","확인");return;}
   var scene=SceneManager.GetActiveScene();
   if(scene.path!=ScenePath){EditorUtility.DisplayDialog("#297 검토","후보 씬을 먼저 여세요:\n"+ScenePath,"확인");return;}
   if(scene.isDirty&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s==null)return;
   state=new State{Active=true,Spot=spot,Suffix="_review297_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmss"),PriorSuffix=s.TestSaveSuffix,
    PriorUi=SessionState.GetString("PlaytestUiReviewSuffix","__missing__"),PriorStartup=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
    PriorDirect=SessionState.GetBool("Compact270.DirectPlay",false),PriorDirectPresent=SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true)};
   s.TestSaveSuffix=state.Suffix;SessionState.SetString("PlaytestUiReviewSuffix",state.Suffix);CompactLoadingStartup270.UseCurrent();
   Persist();EditorApplication.isPlaying=true;
  }
  static void Tick()
  {
   if(state?.Active!=true||state.Placed||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(s==null||!s.InitializationComplete||PlaytestUiRoot.Instance?.LoadingInProgress==true)return;
   try
   {
    var (feet,yaw,label)=CompactRebuildAuthoring.CheolongReviewSpot297(state.Spot);
    s.Teleport(feet,yaw);Debug.Log("[#297 검토] "+label+" "+feet.ToString("F1")+" (격리 저장 "+state.Suffix+", Play 종료 시 삭제)");
   }
   catch(Exception e){Debug.LogException(e);}
   state.Placed=true;Persist();
  }
  static void Changed(PlayModeStateChange mode)
  {
   if(state?.Active!=true||mode!=PlayModeStateChange.EnteredEditMode)return;
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s!=null){s.TestSaveSuffix=state.PriorSuffix;EditorUtility.ClearDirty(s);}
   if(state.PriorUi=="__missing__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",state.PriorUi);
   if(state.PriorDirectPresent)SessionState.SetBool("Compact270.DirectPlay",state.PriorDirect);else SessionState.EraseBool("Compact270.DirectPlay");
   EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(state.PriorStartup)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(state.PriorStartup);
   foreach(var f in Directory.GetFiles(Application.persistentDataPath,"*"+state.Suffix+"*",SearchOption.TopDirectoryOnly))File.Delete(f);
   state.Active=false;Persist();
  }
 }
 public static partial class CompactRebuildAuthoring
 {
  // review spots from the built compound (Finish297/Cheolong/unity.json + the scene's door objects), on physical support
  internal static (Vector3 feet,float yaw,string label) CheolongReviewSpot297(string spot)
  {
   var data=CompoundData297("Cheolong");
   Vector3[] Route(string id){var r=data.routes.First(x=>x.id==id);return Enumerable.Range(0,r.points.Length/3).Select(i=>V297(r.points,3*i)).ToArray();}
   float Yaw(Vector3 from,Vector3 to)=>Quaternion.LookRotation(Vector3.ProjectOnPlane(to-from,Vector3.up)).eulerAngles.y;
   switch(spot)
   {
    case "approach":
    {
     var p=Route("approach");var gate=p[p.Length-2];var at=p.Take(p.Length-2).Reverse().FirstOrDefault(q=>Vector3.Distance(q,gate)>=16f);if(at==default)at=p[0];
     return (Support297(at),Yaw(at,gate),"철옹 산성 정문 밖");
    }
    case "rest":return (Support297(V297(data.rest.feet)),data.rest.feetYaw,"철옹 산성 성황당");
    case "northgate":
    {
     var door=GameObject.Find(data.root+"/Door_InnerNorthGate")?.transform??throw new Exception("Door_InnerNorthGate missing");
     return (Support297(door.TransformPoint(new Vector3(0,0,2.4f)),1.5f,door),door.eulerAngles.y+180,"내성 북문 안쪽(빗장 앞)");
    }
    case "jangdae":{var p=Route("jangdae_climb");return (Support297(p[0]),Yaw(p[0],p[1]),"동장대 앞");}
    case "hall":
    {
     var p=Route("jangdae_to_hall");var e=p.Last();var at=p.Reverse().FirstOrDefault(q=>Vector3.Distance(q,e)>=14f);if(at==default)at=p[0];
     return (Support297(at),Yaw(at,e),"군기전 입구 계단 아래");
    }
   }
   throw new ArgumentException(spot);
  }
 }
}
