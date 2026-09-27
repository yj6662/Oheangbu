using System;
using System.IO;
using System.Linq;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Oheangbu.EditorTools {
 public sealed class PineRestPlayerBuild : IProcessSceneWithReport {
  public int callbackOrder=>1000;
  public void OnProcessScene(Scene scene,BuildReport report){
   if(report==null||scene.path!=PineRestGameBuilder.Scene||Path.GetFileName(report.summary.outputPath)!="PineRestJourney.exe")return;
   var session=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PrologueSession>(true)).Single();
   session.TestSaveSuffix="-standalone";
   var launch=session.gameObject.AddComponent<JourneyPlayerLaunch>();launch.Session=session;launch.Menu=session.GetComponent<ProloguePauseMenu>();
   if(launch.Menu==null||!launch.Menu.Textless)throw new BuildFailedException("Journey textless menu missing");
  }
  [Serializable] sealed class SettingsSnapshot { public SceneRow[] scenes; }
  [Serializable] sealed class SceneRow { public string path,guid; public bool enabled; }
  static void RestoreSettingsBytes(byte[] bytes){
   const string path="ProjectSettings/EditorBuildSettings.asset";
   if(File.ReadAllBytes(path).SequenceEqual(bytes))return;
   string temporary=path+".journey-"+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllBytes(temporary,bytes);try{File.Replace(temporary,path,null);}finally{if(File.Exists(temporary))File.Delete(temporary);}
   if(!File.ReadAllBytes(path).SequenceEqual(bytes))throw new IOException("Build settings snapshot did not restore");
  }
  public static string RestoreSettings(){
   var snapshot=JsonUtility.FromJson<SettingsSnapshot>(File.ReadAllText("../Art/World/PineRest/build_settings_recovered.json"));
   EditorBuildSettings.scenes=snapshot.scenes.Select(r=>new EditorBuildSettingsScene(r.path,r.enabled){guid=new GUID(r.guid)}).ToArray();
   RestoreSettingsBytes(File.ReadAllBytes("../Art/World/PineRest/build_settings_recovered.asset"));
   return "Restored original "+EditorBuildSettings.scenes.Length+" scene entries and exact verified file snapshot";
  }
  public static string Diagnostics(){var r=BuildReport.GetLatestReport();if(r==null)return "No report";string text=string.Join("\n",r.steps.SelectMany(s=>s.messages).Where(m=>m.type==LogType.Error||m.type==LogType.Exception).Select(m=>m.content));File.WriteAllText("../Art/World/PineRest/player_build_errors.txt",text);return text;}
  public static string Build(){
   if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new Exception("Compiled Edit Mode required");
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Save editor scenes before building");
   string folder=Path.GetFullPath("../Builds/PineRestJourney/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
   var originalScenes=EditorBuildSettings.scenes;var settingsBytes=File.ReadAllBytes("ProjectSettings/EditorBuildSettings.asset");File.WriteAllBytes(Path.Combine(folder,"EditorBuildSettings.before.asset"),settingsBytes);BuildReport report;
   try{EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(PineRestGameBuilder.Scene,true)};
    report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PineRestGameBuilder.Scene},locationPathName=Path.Combine(folder,"PineRestJourney.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
   }finally{EditorBuildSettings.scenes=originalScenes;RestoreSettingsBytes(settingsBytes);}

   File.WriteAllText(Path.Combine(folder,"errors.txt"),string.Join("\n",report.steps.SelectMany(s=>s.messages).Where(m=>m.type==LogType.Error||m.type==LogType.Exception).Select(m=>m.content)));
   string result=report.summary.result+"; errors="+report.summary.totalErrors+"; bytes="+report.summary.totalSize+"; time="+report.summary.totalTime+"; output="+folder;
   File.WriteAllText(Path.Combine(folder,"build.txt"),result);File.WriteAllText("../Art/World/PineRest/player_build.txt",result);
   if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(result);return result;
  }
 }
}
