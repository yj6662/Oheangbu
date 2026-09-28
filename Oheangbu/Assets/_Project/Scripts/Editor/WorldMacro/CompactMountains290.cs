using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string A290="Assets/_Project/Art/World/MountainIntegration290";
  const string O290="../Art/World/Compact/Rebuild/Mountain290";
  public const string Scene290=A290+"/W_Demo_Compact_Mountains.unity";
  [MenuItem("Oheangbu/Compact Rebuild/고산 통합 290 후보 열기")]
  public static void Open290()=>Mountain290("open");
  [MenuItem("Oheangbu/Compact Rebuild/고산 통합 290 후보 재조립")]
  public static void Assemble290()=>Mountain290("assemble");
  public static string Mountain290(string command)
  {
   Directory.CreateDirectory(O290);
   if(command=="recover"&&!EditorApplication.isPlayingOrWillChangePlaymode&&SceneManager.GetActiveScene().path==Scene290){EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),A290+"/FailedAssemblyRecovery.unity",true);EditorSceneManager.OpenScene(Scene290);return "Generated assembly failure preserved before reopen";}
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Edit scene required; current work preserved");
   if(command=="prepare")return Prepare290();
   if(command=="assemble"){BuildGeometry290();BuildContent290();Art290();Dress290();Apply291();return Check290()+"\n"+Check291();}
   if(command=="geometry")return BuildGeometry290();
   if(command=="content")return BuildContent290();
   if(command=="check")return Check290();
   if(command=="capture")return Capture290();
   if(command=="art-bridge")return BridgeCollision290();
   if(command=="art"){var result=Art290();return result+"\n"+Apply291();}
   if(command=="art-capture")return ArtCapture290();
   if(command=="dress")return Dress290();
   if(command=="repair-turns")return RepairTurns290();
   if(command=="guk-scene")return GukCandidate290();
   if(command=="walk")return Walk290();
   if(command=="nav-inventory")return Navigation290(false);
   if(command=="nav")return Navigation290(true);
   if(command=="map-export")return Map290(false);
   if(command=="map-apply")return Map290(true);
   if(command=="open"){EditorSceneManager.OpenScene(Scene290);return Scene290;}
   throw new ArgumentException(command);
  }
  static string Prepare290()
  {
   if(File.Exists(Scene290))throw new Exception("Candidate already exists; do not overwrite it with the old source");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));
   string source=receipt.scene,sourceFolder=Path.GetDirectoryName(source).Replace('\\','/');
   var dependencies=AssetDatabase.GetDependencies(source,true);
   string backup=O290+"/before-assets.zip";
   if(!File.Exists(backup))using(var archive=ZipFile.Open(backup,ZipArchiveMode.Create))
   {
    foreach(string asset in dependencies.Concat(new[]{Folder+"/WorldLayout.asset", "ProjectSettings/EditorBuildSettings.asset"}).Distinct())
     foreach(string file in new[]{asset,asset+".meta"})if(File.Exists(file))archive.CreateEntryFromFile(file,file,System.IO.Compression.CompressionLevel.Fastest);
    foreach(var file in Directory.GetFiles(Application.persistentDataPath,"*.json*"))archive.CreateEntryFromFile(file,"Saves/"+Path.GetFileName(file),System.IO.Compression.CompressionLevel.Fastest);
   }
   File.WriteAllText(O290+"/source-dependencies.txt",string.Join("\n",dependencies));
   DevSceneKit.EnsureFolder(A290);DevSceneKit.EnsureFolder(A290+"/Data");DevSceneKit.EnsureFolder(A290+"/Meshes");
   var scene=EditorSceneManager.OpenScene(source);
   var roots=scene.GetRootGameObjects();var session=roots.SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
   var manifest=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true)).Single();
   var mapping=new Dictionary<Object,Object>();
   foreach(string asset in dependencies)
   {
    var value=AssetDatabase.LoadMainAssetAtPath(asset);
    if(!(value is ScriptableObject)||(!asset.StartsWith(sourceFolder+"/")&&value!=manifest.Layout))continue;
    string relative=asset.StartsWith(sourceFolder+"/")?asset.Substring(sourceFolder.Length+1):"WorldLayout.asset";
    string target=A290+"/Data/"+relative;DevSceneKit.EnsureFolder(Path.GetDirectoryName(target).Replace('\\','/'));
    if(!AssetDatabase.CopyAsset(asset,target))throw new Exception("Cannot clone "+asset);
    mapping.Add(value,AssetDatabase.LoadMainAssetAtPath(target));
   }
   void Remap(Object value)
   {
    var serialized=new SerializedObject(value);var p=serialized.GetIterator();bool changed=false;
    while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&mapping.TryGetValue(p.objectReferenceValue,out var replacement))
    {p.objectReferenceValue=replacement;changed=true;}
    if(changed){serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(value);}
   }
   foreach(var value in mapping.Values)Remap(value);
   foreach(var component in roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)))if(component!=null)Remap(component);
   session.Content.SaveSlot="world-compact-mountains-290";session.Content.TerrainRevision="mountains-290-v1";
   manifest.Generation=session.Content.TerrainRevision;manifest.SourceScene=source;manifest.TraversalVerified=false;
   EditorUtility.SetDirty(session.Content);EditorUtility.SetDirty(manifest);
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Scene290,true);EditorSceneManager.OpenScene(Scene290);
   File.WriteAllText(O290+"/candidate.json",JsonUtility.ToJson(new Candidate290{scene=Scene290,source=source,saveSlot="world-compact-mountains-290",backup=backup},true));
   return "Independent candidate created; original scene/save/build unchanged: "+Scene290+"; cloned data="+mapping.Count;
  }
  [Serializable]sealed class Candidate290{public string scene,source,saveSlot,backup;}
 }
}

