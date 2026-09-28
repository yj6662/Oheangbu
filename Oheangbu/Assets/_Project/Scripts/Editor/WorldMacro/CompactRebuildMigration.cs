using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Transform FindBoundRoot(string path)
  {
   var parts=path.Split('/');var t=SceneManager.GetSceneByPath(Scene).GetRootGameObjects().SingleOrDefault(g=>g.name==parts[0])?.transform;
   for(int i=1;i<parts.Length&&t!=null;i++)t=t.Find(parts[i]);return t;
  }
  static string BindSlice()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Compact edit required");
   var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(Folder+"/WorldLayout.asset");
   var content=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>("Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset");
   var mine=layout.Places.Single(p=>p.Id=="mine");var inn=layout.Places.Single(p=>p.Id=="geumpyo_inn");
   mine.SceneRoots=new[]{"Playtest_NaturalCave","Playtest_EarlyArt/Mine_Supports","Playtest_EarlyArt/Mine_BlastDebris","Playtest_EarlyArt/Mine_Workplace","Playtest_EarlyArt/Mine_Transport","Playtest_EarlyArt/Branch_WorkerTrace","Playtest_EarlyArt/Blast_Cord","Playtest_EarlyArt/Exit_Transport_Basket","Playtest_OwnedAssets/Worker_Straw_Bag","Playtest_OwnedAssets/Mine_Work_Basket","Playtest_OwnedAssets/Mine_Work_Ambient","Playtest_OwnedAssets/Mine_Work_Lantern"};
   inn.SceneRoots=new[]{"Playtest_OriginalC2Inn","Playtest_EarlyArt/Inn_FuelStore","Demo_NpcRoles/geumpyo_innkeeper"};
   mine.InteractionIds=new[]{"mine_inquiry","worker_satchel"};inn.InteractionIds=new[]{"geumpyo_inn","logger","herbalist","geumpyo_innkeeper"};
   mine.EncounterIds=content.Encounters.Where(e=>e.Id.StartsWith("mine_")).Select(e=>e.Id).ToArray();inn.EncounterIds=Array.Empty<string>();
   mine.SourceAnchor=content.Points.Single(p=>p.Id=="mine_inquiry").Position;inn.SourceAnchor=content.Points.Single(p=>p.Id=="geumpyo_inn").Position;
   mine.YawDelta=180;
   foreach(var place in new[]{mine,inn})
   {
    foreach(string path in place.SceneRoots)if(FindBoundRoot(path)==null)throw new Exception("Missing scene root "+path);
    foreach(string id in place.InteractionIds)if(content.Points.Count(p=>p.Id==id)!=1)throw new Exception("Interaction binding "+id);
    place.HasSourceBinding=true;
   }
   EditorUtility.SetDirty(layout);AssetDatabase.SaveAssets();File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(layout,true));
   return "Bound mine/inn to 15 scene roots, 6 interactions and "+mine.EncounterIds.Length+" encounters. Source geometry unchanged.";
  }
  [Serializable] class MigrationReceipt
  {public string generation,status,scene,sourceScene;public int movedRoots,interactions,encounters,colliders;public string[] checks;}
  static string MigrateSlice()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Compact edit required");
   var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(Folder+"/WorldLayout.asset");
   var selected=layout.Places.Where(p=>p.HasSourceBinding).ToArray();if(selected.Length!=2)throw new Exception("Bound mine/inn slice required");
   string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(layout)))).Replace("-","").ToLowerInvariant();
   string generation="slice-"+hash.Substring(0,12),folder=Folder+"/"+generation;
   if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(Folder,generation);
   string targetScene=folder+"/W_Demo_Compact_MigrationCheck.unity";
   var sourceScene=SceneManager.GetActiveScene();var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
   var checks=new List<string>();int moved=0;
   try
   {
    var terrain=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/TerrainStaging.prefab"),preview);
    terrain.name="Compact_Rebuild_Terrain";
    var field=new RebuildHeight(layout);
    var original=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>("Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset");
    var content=Object.Instantiate(original);content.name="SliceContent";content.SaveSlot="world-demo-compact-"+generation;content.TerrainRevision=generation;
    var points=new List<PrologueContentSO.Point>();var encounters=new List<WorldMacroPlaytestSO.Encounter>();
    foreach(var place in selected)
    {
     var target=new Vector3(place.XZ.x,field.Sample(place.XZ.x,place.XZ.y),place.XZ.y);var rotation=Quaternion.Euler(0,place.YawDelta,0);
     Vector3 Map(Vector3 p)=>target+rotation*(p-place.SourceAnchor);
     var group=new GameObject(place.Id);SceneManager.MoveGameObjectToScene(group,preview);
     foreach(var path in place.SceneRoots)
     {
      var source=FindBoundRoot(path);if(source==null)throw new Exception("Binding no longer resolves "+path);
      var clone=Object.Instantiate(source.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);clone.name=source.name;
      clone.transform.SetPositionAndRotation(Map(source.position),rotation*source.rotation);clone.transform.localScale=source.lossyScale;clone.transform.SetParent(group.transform,true);moved++;
      if(clone.transform.childCount!=source.childCount)throw new Exception("Hierarchy mismatch "+path);
      var before=source.GetComponentsInChildren<Transform>(true);var after=clone.GetComponentsInChildren<Transform>(true);
      if(before.Length!=after.Length||before.Where((t,i)=>Vector3.Distance(Map(t.position),after[i].position)>.02f).Any())throw new Exception("Child world offset mismatch "+path);
      checks.Add("PASS root translation and child count "+path);
     }
     foreach(var id in place.InteractionIds)
     {
      var point=JsonUtility.FromJson<PrologueContentSO.Point>(JsonUtility.ToJson(original.Points.Single(p=>p.Id==id)));point.Position=Map(point.Position);points.Add(point);
     }
     foreach(var id in place.EncounterIds)
     {
      var enemy=JsonUtility.FromJson<WorldMacroPlaytestSO.Encounter>(JsonUtility.ToJson(original.Encounters.Single(e=>e.Id==id)));enemy.Feet=Map(enemy.Feet);
      if(enemy.Patrol!=null)for(int i=0;i<enemy.Patrol.Length;i++)enemy.Patrol[i]=Map(enemy.Patrol[i]);encounters.Add(enemy);
     }
     if(place.Id=="mine"){content.StartFeet=Map(original.StartFeet);content.StartYaw=original.StartYaw+place.YawDelta;}
     if(place.Id=="geumpyo_inn")content.InnCheckpointFeet=Map(original.InnCheckpointFeet);
    }
    content.Points=points.ToArray();content.Encounters=encounters.ToArray();content.Checkpoints=Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>();
    ConformSliceTerrain(preview,terrain,folder,layout,content,checks);
    content.MainPath=layout.Routes.Where(r=>r.From=="mine"||r.From=="mine_overlook").SelectMany(r=>new[]{layout.Places.Single(p=>p.Id==r.From).XZ}.Concat(r.Bends).Concat(new[]{layout.Places.Single(p=>p.Id==r.To).XZ})).Select(p=>new Vector3(p.x,field.Sample(p.x,p.y),p.y)).ToArray();content.BranchPath=Array.Empty<Vector3>();
    string contentPath=folder+"/Content.asset";var prior=AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(contentPath);
    if(prior==null)AssetDatabase.CreateAsset(content,contentPath);else{EditorUtility.CopySerialized(content,prior);Object.DestroyImmediate(content);content=prior;EditorUtility.SetDirty(content);}
    var provenance=new GameObject("Rebuild_Manifest");SceneManager.MoveGameObjectToScene(provenance,preview);var manifest=provenance.AddComponent<CompactRebuildSceneManifest>();manifest.Generation=generation;manifest.Layout=layout;manifest.Content=content;manifest.SourceScene=Scene;
    foreach(string name in new[]{"Macro_Sun","Macro_WorldLook","Macro_Subtle_Post"})
    {var source=sourceScene.GetRootGameObjects().SingleOrDefault(g=>g.name==name);if(source!=null){var clone=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(clone,preview);clone.name=name;}}
    var cameraObject=new GameObject("Migration_Review_Camera");SceneManager.MoveGameObjectToScene(cameraObject,preview);var camera=cameraObject.AddComponent<Camera>();
    var minePlace=selected.Single(p=>p.Id=="mine");camera.transform.position=new Vector3(minePlace.XZ.x-180,field.Sample(minePlace.XZ.x,minePlace.XZ.y)+120,minePlace.XZ.y-200);camera.transform.LookAt(new Vector3(minePlace.XZ.x,field.Sample(minePlace.XZ.x,minePlace.XZ.y),minePlace.XZ.y));camera.farClipPlane=6500;
    AssetDatabase.SaveAssets();if(!EditorSceneManager.SaveScene(preview,targetScene))throw new Exception("Candidate scene save failed");
    var report=new MigrationReceipt{generation=generation,status="GEOMETRY_AND_DATA_CANDIDATE_NOT_PLAY_READY",sourceScene=sourceScene.path,scene=targetScene,movedRoots=moved,interactions=points.Count,encounters=encounters.Count,colliders=preview.GetRootGameObjects().Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),checks=checks.ToArray()};
    File.WriteAllText(Output+"/migration_slice.json",JsonUtility.ToJson(report,true));return JsonUtility.ToJson(report);
   }
   finally{EditorSceneManager.CloseScene(preview,true);SceneManager.SetActiveScene(sourceScene);}
  }
 }
}
