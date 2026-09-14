using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.Prologue
{
 public static class PrologueFinalChecks
 {
  [Serializable]class SavedRun{public string saveSlot;}
  public static string Resume()
  {
   if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
   var report=JsonUtility.FromJson<SavedRun>(File.ReadAllText(PrologueBuilder.Output+"/walk_status.json"));var s=Object.FindFirstObjectByType<PrologueSession>();
   s.TestSaveSuffix=report.saveSlot.Substring(s.Content.SaveSlot.Length);EditorApplication.isPlaying=true;return "Reloading the previous isolated play save through normal Session.Start.";
  }
  public static string CheckResumeAndWalls()
  {
   if(!EditorApplication.isPlaying)throw new Exception("Play required");var s=Object.FindFirstObjectByType<PrologueSession>();if(s.Progress==null)throw new Exception("Session not ready");
   var checks=new List<string>();Action<bool,string> check=(ok,name)=>checks.Add((ok?"PASS ":"FAIL ")+name);
   check(s.Progress.checkpoint=="InnRest"&&s.Progress.completed.Contains("WorkerSatchel"),"Play re-entry restored checkpoint and completed loot");
   check(Vector3.Distance(s.Player.position,s.Progress.position)<.5f,"Play re-entry restored saved position");
   check(s.Encounters.All(e=>e.GetComponent<NavMeshAgent>().isOnNavMesh),"serialized NavMesh active after reload");
   var prior=s.Player.position;var enemy=s.Encounters[0];s.Teleport(enemy.transform.position+Vector3.forward*6,180);Physics.SyncTransforms();
   var controller=enemy.GetComponent<EnemyController>();check(controller.HasLineOfSight(),"enemy open line of sight");
   var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="DiagnosticOnly_Wall";wall.transform.position=(s.Player.position+enemy.transform.position)*.5f;wall.transform.localScale=new Vector3(4,4,.4f);Physics.SyncTransforms();
   check(!controller.HasLineOfSight(),"enemy blocked by terrain-layer collider");
   var visible=typeof(CombatLoopWiring).GetMethod("TargetVisible",BindingFlags.Instance|BindingFlags.NonPublic);
   check(!(bool)visible.Invoke(s.Wiring,new object[]{enemy.GetComponent<EnemyVitals>(),s.Player.position+Vector3.up*.4f}),"player spell target occluded by collider");
   Object.DestroyImmediate(wall);s.Teleport(prior,0);Physics.SyncTransforms();
   checks.Add("DIAGNOSTIC collider and teleport used only for LOS assertions. Play re-entry is not an executable/application restart test.");
   var result=string.Join("\n",checks);File.WriteAllText(PrologueBuilder.Output+"/reload_and_occlusion.txt",result);return result;
  }
  [Serializable]class Row{public string scene,status;public int width=1920,height=1080,samples;public double cameraRenderCpuMs;public long unityAllocatedBytes;public float systemCommit;public int sampledBatches,sampledTriangles;}
  [Serializable]class Bench{public string meaning="Editor synchronous Camera.Render CPU duration, representative view, 1080p; excludes gameplay. Not GPU frame time or standalone FPS. Unity allocated memory includes Editor caches; system commit is all processes.";public List<Row> rows=new List<Row>();}
  public static string Benchmark(bool productionOnly=false)
  {
   if(EditorApplication.isPlaying)throw new Exception("Edit mode required");var bench=new Bench();RenderTexture rt=null;
   try{
    foreach(string path in productionOnly?new[]{PrologueBuilder.ScenePath}:new[]{CodexWorldSceneBuilder.ScenePath,PrologueBuilder.ScenePath}){
     if(PrologueAudit.CommitRatio()>=.85f){bench.rows.Add(new Row{scene=path,status="NOT_RUN: system commit >=85%"});break;}
     EditorSceneManager.OpenScene(path,OpenSceneMode.Single);Object.FindFirstObjectByType<WorldLookDriver>().Apply();var c=Camera.main;
     bool production=path==PrologueBuilder.ScenePath;var p=production?new Vector3(-92,5,62):new Vector3(-35,4,20);var target=production?new Vector3(-72,2,38):new Vector3(-15,2,38);c.transform.SetPositionAndRotation(p,Quaternion.LookRotation(target-p));
     rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var old=c.targetTexture;c.targetTexture=rt;double total=0;int count=0;
     try{for(int i=0;i<27;i++){if(PrologueAudit.CommitRatio()>=.85f)break;var timer=System.Diagnostics.Stopwatch.StartNew();c.Render();timer.Stop();if(i>=3){total+=timer.Elapsed.TotalMilliseconds;count++;}}}
     finally{c.targetTexture=old;rt.Release();Object.DestroyImmediate(rt);rt=null;}
     bench.rows.Add(new Row{scene=path,status=count==24?"SAMPLED":"PARTIAL",samples=count,cameraRenderCpuMs=count>0?total/count:0,unityAllocatedBytes=UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),systemCommit=PrologueAudit.CommitRatio(),sampledBatches=UnityStats.batches,sampledTriangles=UnityStats.triangles});
    }
   }finally{if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}EditorSceneManager.OpenScene(PrologueBuilder.ScenePath,OpenSceneMode.Single);}
   string result=JsonUtility.ToJson(bench,true);File.WriteAllText(PrologueBuilder.Output+(productionOnly?"/render_prologue.json":"/render_comparison.json"),result);return result;
  }
 }
}
