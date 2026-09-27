using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  [Serializable] sealed class AudioMigration { public string[] oldPaths,referencePaths; }
  static string RenewAllAudio(){
   if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Saved Edit Mode required");
   var migration=JsonUtility.FromJson<AudioMigration>(File.ReadAllText("../Art/Audio/JourneyRenewal/migration.json"));
   if(!File.Exists("../Art/Audio/JourneyRenewal/retired_sfx_recovery.zip"))throw new Exception("Recovery archive missing");
   var replacements=new Dictionary<string,AudioClip>();
   foreach(string old in migration.oldPaths){
    if(!old.StartsWith("Assets/_Project/Audio/",StringComparison.Ordinal)||!old.EndsWith(".wav",StringComparison.Ordinal))throw new Exception("Unexpected deletion path");
    string path="Assets/_Project/Audio/JourneyRenewal/"+Path.GetFileName(old);
    var clip=AssetDatabase.LoadAssetAtPath<AudioClip>(path);if(clip==null||clip.length<.05f)throw new Exception("Missing new clip "+path);
    replacements[old]=clip;
   }
   int links=0;
   foreach(string path in migration.referencePaths){
    foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path)){
     if(asset==null)continue;var so=new SerializedObject(asset);var property=so.GetIterator();bool changed=false;
     while(property.Next(true)){
      if(property.propertyType!=SerializedPropertyType.ObjectReference||property.objectReferenceValue==null)continue;
      string source=AssetDatabase.GetAssetPath(property.objectReferenceValue);
      if(replacements.TryGetValue(source,out var clip)){property.objectReferenceValue=clip;changed=true;links++;}
     }
     if(changed){so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);}
    }
   }
   AssetDatabase.SaveAssets();
   foreach(string path in migration.referencePaths)foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path)){
    if(asset==null)continue;var so=new SerializedObject(asset);var p=so.GetIterator();while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&replacements.ContainsKey(AssetDatabase.GetAssetPath(p.objectReferenceValue)))throw new Exception("Old reference remains "+path);
   }
   int deleted=0;foreach(string old in migration.oldPaths){if(!File.Exists(old))continue;if(!AssetDatabase.DeleteAsset(old))throw new Exception("Could not delete "+old);deleted++;}
   AssetDatabase.SaveAssets();string report="Migrated references="+links+"; removed old WAV/meta assets="+deleted+"; replacement roles="+replacements.Values.Distinct().Count();File.WriteAllText("../Art/Audio/JourneyRenewal/migration_result.txt",report);return report;
  }
 }
}
