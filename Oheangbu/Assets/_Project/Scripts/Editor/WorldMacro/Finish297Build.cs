using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #297 demo build: lobby -> loading -> W_Demo_Main as a Windows player (Builds/Demo297/<utc>/Oheangbu.exe).
 // The build list is narrowed to the three scenes only for the synchronous BuildPlayer call and restored byte-for-byte.
 public static class Finish297Build
 {
  static readonly string[] Scenes={"Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity","Assets/_Project/Art/UI/Loading270/W_Compact_Loading.unity","Assets/_Project/Scenes/World/W_Demo_Main.unity"};
  [Serializable] class SceneList297{public string[] paths;public bool[] enabled;}
  public static string Run(string command)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new Exception("Compiled Edit Mode required");
   if(command.StartsWith("restore-list:"))
   {
    // restore-list:<json with paths[]/enabled[]> — e.g. written from the committed EditorBuildSettings
    var o=JsonUtility.FromJson<SceneList297>(File.ReadAllText(command.Substring(13)));
    EditorBuildSettings.scenes=o.paths.Select((p,i)=>new EditorBuildSettingsScene(p,o.enabled[i])).ToArray();AssetDatabase.SaveAssets();
    return "build list restored: "+string.Join(", ",EditorBuildSettings.scenes.Select(x=>Path.GetFileNameWithoutExtension(x.path)+(x.enabled?"":"(off)")));
   }
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new Exception("Save editor scenes before building");
   bool dev=command=="dev";
   string folder=Path.GetFullPath("../Builds/Demo297/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+(dev?"-dev":""));Directory.CreateDirectory(folder);
   var originalScenes=EditorBuildSettings.scenes;var settingsBytes=File.ReadAllBytes("ProjectSettings/EditorBuildSettings.asset");BuildReport report;
   try
   {
    EditorBuildSettings.scenes=Scenes.Select(s=>new EditorBuildSettingsScene(s,true)).ToArray();
    report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=Scenes,locationPathName=Path.Combine(folder,"Oheangbu.exe"),target=BuildTarget.StandaloneWindows64,options=dev?BuildOptions.Development:BuildOptions.None});
   }
   finally{EditorBuildSettings.scenes=originalScenes;AssetDatabase.SaveAssets();}   // API restore only: a Refresh here can compile pending scripts and reload the domain mid-method
   File.WriteAllText(Path.Combine(folder,"errors.txt"),string.Join("\n",report.steps.SelectMany(s=>s.messages).Where(m=>m.type==LogType.Error||m.type==LogType.Exception).Select(m=>m.content)));
   string result=report.summary.result+"; errors="+report.summary.totalErrors+"; bytes="+report.summary.totalSize+"; time="+report.summary.totalTime+"; output="+folder;
   File.WriteAllText(Path.Combine(folder,"build.txt"),result);
   if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(result);return result;
  }
 }
}
