using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static class CompactArchitecture296 { public static string Run(string command)=>CompactRebuildAuthoring.Architecture296(command); }
 public static partial class CompactRebuildAuthoring
 {
  const string A296="Assets/_Project/Art/World/Architecture296";
  const string O296="../Art/World/Compact/Rebuild/Architecture296";
  const string G296=O296+"/Generated";
  const string Scene296=A296+"/W_Demo_Compact_Architecture296.unity";
  public static string Architecture296(string command)
  {
   Directory.CreateDirectory(O296);
   if(command=="status")return "play="+EditorApplication.isPlaying+" dirty="+SceneManager.GetActiveScene().isDirty+" scene="+SceneManager.GetActiveScene().path;
   if(command.StartsWith("runtime:"))return ArchitectureRuntimeChecks296.Run(command.Substring(8));
   if(command.StartsWith("mum296:"))return MumBridge296PlayChecks.Run(command.Substring(7));
   if(command.StartsWith("vehicle:"))return ArchitectureVehicleChecks296.Run(command.Substring(8));
   if(command.StartsWith("reproduction:"))return Reproduction296(command.Substring(13));
   if(command=="cameras")return ReframeArchitecture296();
   if(command=="baseline-inventory")return InventoryBaseline296();
   if(command=="prepare")return Prepare296();
   if(command=="reload-generated")
   {
    if(SceneManager.GetActiveScene().path!=Scene296||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("296 Edit scene required");
    EditorSceneManager.OpenScene(Scene296);return "Discarded unsaved296 authoring attempt; saved candidate reopened.";
   }
   if(command=="open"){RequireClean292();EditorSceneManager.OpenScene(Scene296);return Scene296;}
   if(command.StartsWith("capture-all:"))return CaptureAll296(command.Substring(12));
   if(command.StartsWith("capture:"))return (string)typeof(CompactRebuildAuthoring).GetMethod("Capture296",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{command.Substring(8)});
   if(command.StartsWith("perf:"))return (string)typeof(CompactRebuildAuthoring).GetMethod("PerfArchitecture296",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{command.Substring(5)});
   if(SceneManager.GetActiveScene().path!=Scene296||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("296 candidate Edit scene required");
   if(command=="nav-current"){RequireClean292();return NavigationDiagnosticsQa296();}
   if(command=="gate-mobility"){RequireClean292();return GateMobility296();}
   if(command=="nav"||command=="nav-diagnose"||command=="walk"||command=="check"||command=="culling"||command=="runtime")
    return (string)typeof(CompactRebuildAuthoring).GetMethod("ReviewArchitecture296",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{command});
   RequireClean292();var report=new List<string>{"Architecture296 "+command+" "+DateTime.UtcNow.ToString("O")};
   if(command=="audit")CallPart296("AuditArchitecture296",report);
   else if(command=="surface")CallPart296("Surface296",report);
   else if(command=="venues")CallPart296("Venues296",report);
   else if(command=="roof-export")CallPart296("ExportRoofTiles296",report);
   else if(command=="crossings")CallPart296("Crossings296",report);
   else if(command=="crossing-lod-export")report.Add(ExportCrossingLods296());
   else if(command=="crossing-lod-import")report.Add(ImportCrossingLods296());
   else if(command=="crossing-materials")report.Add(UpdateCrossingMaterials296());
   else if(command=="gate-collision")report.Add(SimplifyGateWallCollision296());
   else if(command=="gates")CallPart296("Gates296",report);
   else if(command=="landscape")CallPart296("Landscape296",report);
   else if(command=="map")CallPart296("MapArchitecture296",report);
   else if(command=="legacy")CallPart296("LegacyArchitecture296",report);
   else if(command=="build")
   {
    Session292().Content.TerrainRevision="watershed-295";EditorUtility.SetDirty(Session292().Content);
    CallPart296("Venues296",report);CallPart296("Crossings296",report);CallPart296("Gates296",report);CallPart296("Surface296",report);CallPart296("Landscape296",report);CallPart296("MapArchitecture296",report);
    CallPart296("LegacyArchitecture296",report);CallPart296("AuditArchitecture296",report);
   }
   else throw new ArgumentException(command);
   // Construction and materials preserve the295 heightfield. Existing valid
   // save positions must not be discarded merely because architecture changed.
   Session292().Content.TerrainRevision="watershed-295";EditorUtility.SetDirty(Session292().Content);
   Physics.SyncTransforms();Save292();File.WriteAllLines(O296+"/"+command+".txt",report);
   File.WriteAllText(O296+"/architecture.json",JsonUtility.ToJson(Sheet296(),true));return string.Join("\n",report);
  }
  static void CallPart296(string name,List<string> report)
  {
   var method=typeof(CompactRebuildAuthoring).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static);
   if(method==null)throw new InvalidOperationException("Architecture module not compiled: "+name);
   try {method.Invoke(null,new object[]{report});}
   catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
  }
  static T Asset296<T>(string relative,Func<T> create) where T:Object
  {
   string path=A296+"/"+relative;string directory=Path.GetDirectoryName(path).Replace('\\','/');
   if(!Directory.Exists(directory))DevSceneKit.EnsureFolder(directory);
   var asset=AssetDatabase.LoadAssetAtPath<T>(path);
   if(asset==null){asset=create();AssetDatabase.CreateAsset(asset,path);}return asset;
  }
  static CompactArchitectureSheetSO Sheet296()=>Asset296("Data/Architecture.asset",()=>ScriptableObject.CreateInstance<CompactArchitectureSheetSO>());
  static GameObject Root296(string name)
  {
   int slash=name.IndexOf('/');string rootName=slash<0?name:name.Substring(0,slash);
   var root=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==rootName);
   return slash<0?root:root?.transform.Find(name.Substring(slash+1))?.gameObject;
  }
  static string Prepare296()
  {
   RequireClean292();if(File.Exists(Scene296))return "Existing candidate retained: "+Scene296;
   if(!File.Exists(O296+"/Baseline/protected.json"))throw new Exception("296 protection baseline required before preparation");
   DevSceneKit.EnsureFolder(A296+"/Data");DevSceneKit.EnsureFolder(A296+"/Materials");
   var scene=EditorSceneManager.OpenScene(Scene295);var roots=scene.GetRootGameObjects();
   var remap=new Dictionary<Object,Object>();var receipts=new List<string>();
   foreach(string path in AssetDatabase.GetDependencies(Scene295,true).OrderBy(p=>p,StringComparer.Ordinal))
   {
    if(!path.StartsWith("Assets/",StringComparison.Ordinal))continue;
    var source=AssetDatabase.LoadMainAssetAtPath(path);
    if(!(source is ScriptableObject)&&!(source is Material)&&!(source is NavMeshData))continue;
    if(source is MonoScript)continue;
    string folder=source is Material?"Materials":"Data";
    string dest=A296+"/"+folder+"/"+AssetDatabase.AssetPathToGUID(path)+"_"+Path.GetFileName(path);
    if(AssetDatabase.LoadMainAssetAtPath(dest)==null&&!AssetDatabase.CopyAsset(path,dest))throw new Exception("Cannot clone "+path);
    remap[source]=AssetDatabase.LoadMainAssetAtPath(dest);
    receipts.Add("{\"source\":\""+path+"\",\"target\":\""+dest+"\"}");
   }
   void Remap(Object value)
   {
    var so=new SerializedObject(value);var p=so.GetIterator();bool changed=false;
    while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&remap.TryGetValue(p.objectReferenceValue,out var next)){p.objectReferenceValue=next;changed=true;}
    if(changed){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(value);}
   }
   foreach(var asset in remap.Values)Remap(asset);
   foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Component>(true)))if(c!=null)Remap(c);
   // Terrain is byte-identical. Keep its revision so valid imported saves retain
   // their position; the existing TrySafeFeet repair handles new wall/floor overlaps.
   var session=Session292();session.Content.SaveSlot="world-compact-architecture-296";session.MountainLayout.Revision="architecture-296";
   EditorUtility.SetDirty(session.Content);EditorUtility.SetDirty(session.MountainLayout);
   foreach(var m in roots.SelectMany(r=>r.GetComponentsInChildren<CompactRebuildSceneManifest>(true))){m.SourceScene=Scene295;m.Generation="architecture-296";m.TraversalVerified=false;}
   Sheet296();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Scene296,true);EditorSceneManager.OpenScene(Scene296);
   File.WriteAllText(O296+"/cloned-data.json","["+string.Join(",\n",receipts)+"]");
   File.WriteAllText(O296+"/content-before.json",JsonUtility.ToJson(Session292().Content,true));
   File.WriteAllText(O296+"/layout-before.json",JsonUtility.ToJson(Session292().MountainLayout,true));
   File.WriteAllText(O296+"/prepare.txt","Copied scene; private ScriptableObjects/Materials/NavMeshData="+remap.Count+". Geometry and water remain read-only references to295.");
   return File.ReadAllText(O296+"/prepare.txt");
  }
 }
}
