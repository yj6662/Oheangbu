using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static class CompactReworld292 {public static string Run(string command)=>CompactRebuildAuthoring.Reworld292(command);}
 public static partial class CompactRebuildAuthoring
 {
  const string A292="Assets/_Project/Art/World/Reworld292",O292="../Art/World/Compact/Rebuild/Reworld292";
  public const string Scene292=A292+"/W_Demo_Compact_Reworld.unity";
  [MenuItem("Oheangbu/Compact Rebuild/한국 전체맵 292 후보 열기")]
  public static void Open292()=>Reworld292("open");
  public static string Reworld292(string command)
  {
   if(command.StartsWith("watershed295:"))return Run295(command.Substring(13));
   Directory.CreateDirectory(O292);
   if(command=="prepare")return Prepare292();
   if(command=="open") {RequireClean292();EditorSceneManager.OpenScene(Scene292);return Scene292;}
   if(command=="survey")return Survey292();
   if(SceneManager.GetActiveScene().path!=Scene292||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("292 candidate Edit scene required");
   if(command=="terrain")return Terrain292();
   if(command=="surface-update")return UpdateSurface292();
   if(command=="dress")return Dress292();
   if(command=="temples")return Temples292();
   if(command=="water")return Water292();
   if(command=="atmosphere")return Atmosphere292();
   if(command=="export-hall")return ExportHall292();
   if(command=="hall-lod")return HallLod292();
   if(command=="check")return Check292();
   if(command.StartsWith("capture:"))return Capture292(int.Parse(command.Substring(8)));
   if(command=="nav")return Navigation292();
   if(command=="quarantine-nav")return QuarantineNavigation292();
   if(command=="highlands293")return Highlands293();
   if(command=="cleanup293")return Cleanup293();
   if(command=="themes293")return Themes293();
   if(command=="sections293-dress")return DressSections293();
   if(command=="check293s")return CheckSections293();
   if(command=="walk293")return WalkSections293();
   if(command=="look293")return Look293(false);
   if(command.StartsWith("perf293:")){var perf=command.Substring(8).Split(':');return Perf293(int.Parse(perf[0]),perf.Length>1&&perf[1]=="noart");}
   if(command=="artcull")return ArtCulling();
   if(command=="rivers294")return Rivers294();
   if(command=="check294")return CheckRiver294();
   if(command.StartsWith("capture294:"))return CaptureRiver294(command.Substring(11));
   if(command=="look293-revert")return Look293(true);
   if(command=="atmos293")return Atmos293(false);
   if(command=="materials293")return Materials293();
   if(command=="atmos293-revert")return Atmos293(true);
   if(command.StartsWith("probe293:"))return ProbeMain293(command.Substring(9));
   if(command.StartsWith("capture293s:"))return CaptureSections293(command.Substring(12));
   if(command.StartsWith("capture293:"))return Capture293(int.Parse(command.Substring(11)));
   throw new ArgumentException(command);
  }
  static void RequireClean292(){if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Current work preserved: clean Edit scene required");}
  static T Asset292<T>(string relative,Func<T> create) where T:Object
  {
   string path=A292+"/"+relative;string directory=Path.GetDirectoryName(path).Replace('\\','/');
   // IsValidFolder can lag inside StartAssetEditing; do not create numbered twins.
   if(!Directory.Exists(directory))DevSceneKit.EnsureFolder(directory);
   var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a==null){a=create();AssetDatabase.CreateAsset(a,path);}return a;
  }
  static WorldMacroPlaytestSession Session292()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  static void Save292(){AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());}
  static string Prepare292()
  {
   RequireClean292();if(File.Exists(Scene292))throw new Exception("Candidate already exists; source cannot overwrite it");
   if(!File.Exists(O292+"/recovery.json"))throw new Exception("Recovery receipt required first");
   var scene=EditorSceneManager.OpenScene(Scene290);var roots=scene.GetRootGameObjects();
   var dependencies=AssetDatabase.GetDependencies(Scene290,true);var remap=new Dictionary<Object,Object>();
   foreach(string path in dependencies)
   {
    if(!path.StartsWith("Assets/_Project/"))continue;
    var source=AssetDatabase.LoadMainAssetAtPath(path);if(!(source is ScriptableObject)||source is MonoScript)continue;
    string destination=A292+"/Data/"+AssetDatabase.AssetPathToGUID(path)+"_"+Path.GetFileName(path);
    DevSceneKit.EnsureFolder(A292+"/Data");if(!AssetDatabase.CopyAsset(path,destination))throw new Exception("Cannot clone "+path);
    remap[source]=AssetDatabase.LoadMainAssetAtPath(destination);
   }
   void Remap(Object o){var so=new SerializedObject(o);var p=so.GetIterator();bool dirty=false;
    while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&remap.TryGetValue(p.objectReferenceValue,out var next)){p.objectReferenceValue=next;dirty=true;}
    if(dirty){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(o);}}
   foreach(var o in remap.Values)Remap(o);
   foreach(var c in roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)))if(c!=null)Remap(c);
   var s=Session292();s.Content.SaveSlot="world-compact-reworld-292";s.Content.TerrainRevision="reworld-292";EditorUtility.SetDirty(s.Content);
   foreach(var manifest in roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true))){manifest.SourceScene=Scene290;manifest.Generation="reworld-292";manifest.TraversalVerified=false;}
   File.WriteAllText(O292+"/source-layout.json",JsonUtility.ToJson(s.MountainLayout,true));
   File.WriteAllText(O292+"/source-content.json",JsonUtility.ToJson(s.Content,true));
   File.WriteAllText(O292+"/cloned-data.json","["+string.Join(",",remap.Select(p=>"{\"source\":\""+AssetDatabase.GetAssetPath(p.Key)+"\",\"target\":\""+AssetDatabase.GetAssetPath(p.Value)+"\"}"))+"]");
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Scene292,true);EditorSceneManager.OpenScene(Scene292);
   return "Isolated candidate and "+remap.Count+" data assets cloned; save slot world-compact-reworld-292.\n"+Survey292();
  }
  static string Survey292()
  {
   var scene=SceneManager.GetActiveScene();var roots=scene.GetRootGameObjects();
   var rows=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.parent==null||t.parent.parent==null).Select(t=>HierarchyPath(t)+" | "+t.position+" | children="+t.childCount+" | "+string.Join(",",t.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType().Name)));
   File.WriteAllText(O292+"/hierarchy.txt",string.Join("\n",rows));
   var s=Session292();File.WriteAllText(O292+"/current-content.json",JsonUtility.ToJson(s.Content,true));
   File.WriteAllText(O292+"/renderers.txt",string.Join("\n",roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).Where(r=>r.bounds.size.x>150||r.bounds.size.z>150).Select(r=>HierarchyPath(r.transform)+" | "+r.bounds)));
   var templeRoot=GameObject.Find("Reworld292_Sansa");if(templeRoot!=null){var details=new List<string>();foreach(Transform temple in templeRoot.transform)foreach(Transform building in temple){var rr=building.GetComponentsInChildren<Renderer>();if(rr.Length==0)continue;var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);details.Add(HierarchyPath(building)+" position="+building.position+" bounds="+b+" renderers="+rr.Length+" active="+building.gameObject.activeInHierarchy+" mat="+rr[0].sharedMaterial.name);}File.WriteAllText(O292+"/temple-bounds.txt",string.Join("\n",details));}
   return "Survey saved: "+scene.path+"; roots="+roots.Length;
  }
 }
}
