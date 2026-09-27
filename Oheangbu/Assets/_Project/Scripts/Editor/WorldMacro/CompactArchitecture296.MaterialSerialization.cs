using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static readonly string[] UnusedVenueMotion296={"_AddPrecomputedVelocity","_XRMotionVectorsPass"};
  [Serializable] sealed class VenueMaterialSerializationRow296
  {
   public string Asset,BeforeFileSHA256,AfterFileSHA256,ExpectedFileSHA256,SecondSaveSHA256;
   public string BeforeInvariantSHA256,AfterInvariantSHA256;
   public int RemovedFloats;
   public bool RemainingSerializedStateUnchanged,FileOnlyUnusedFloatsChanged,SecondSaveIdentical;
  }
  [Serializable] sealed class VenueMaterialSerializationReceipt296
  {
   public string Utc,Status,Error,SceneSHA256Before,SceneSHA256After,ArchiveFolder;
   public string Scope="Candidate Venue materials only. Remove URP's unused _AddPrecomputedVelocity=0 and _XRMotionVectorsPass=1 saved floats when the current shader declares neither. Preserve shader, maps, UV, colours, keywords, all other serialized state, scene and routes. No rendering or gameplay change.";
   public bool Saved,SceneUnchanged,RoutesUnchanged,SceneRemainsClean;
   public int Materials,ChangedMaterials,RemovedFloats;
   public List<VenueMaterialSerializationRow296> Rows=new List<VenueMaterialSerializationRow296>();
  }
  static string VenueMaterialInvariant296(Material material)
  {
   // EditorJsonUtility preserves every serialized material field. Normalize
   // only these two float entries before comparing the complete JSON string.
   string json=EditorJsonUtility.ToJson(material);
   int label=json.IndexOf("\"m_Floats\":",StringComparison.Ordinal);
   if(label<0)throw new InvalidOperationException("Material float array missing");
   int start=json.IndexOf('[',label),end=json.IndexOf(']',start);
   if(start<0||end<0)throw new InvalidOperationException("Material float array malformed");
   var entries=Regex.Matches(json.Substring(start+1,end-start-1),@"\{[^{}]*\}").Cast<Match>().Select(m=>m.Value);
   string kept=string.Join(",",entries.Where(e=>!UnusedVenueMotion296.Any(name=>e.Contains("\"first\":\""+name+"\""))));
   return json.Substring(0,start+1)+kept+json.Substring(end);
  }
  static VenueMaterialSerializationReceipt296 CanonicalVenueMaterials296(bool saveFiles)
  {
   string folder=A296+"/Materials/Venues";
   var receipt=new VenueMaterialSerializationReceipt296{Utc=DateTime.UtcNow.ToString("O"),Status="PREFLIGHT",Saved=saveFiles};
   var targets=new List<(Material Material,string Json,VenueMaterialSerializationRow296 Row)>();
   if(saveFiles)
   {
    receipt.ArchiveFolder=O296+"/Analysis/MaterialSerialization/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"/before";
    Directory.CreateDirectory(receipt.ArchiveFolder);
   }
   try
   {
    // Preflight every material before touching any of them. Current shader
    // usage or a non-default value must stop the whole operation.
    foreach(string file in Directory.GetFiles(folder,"*.mat").OrderBy(p=>p,StringComparer.Ordinal))
    {
     string path=file.Replace('\\','/');var material=AssetDatabase.LoadAssetAtPath<Material>(path);
     if(material==null||!AssetDatabase.GetAssetPath(material).StartsWith(folder+"/",StringComparison.Ordinal))
      throw new InvalidOperationException("Private Venue material required: "+path);
     if(UnusedVenueMotion296.Any(material.HasProperty))throw new InvalidOperationException("Shader uses motion property; refuse cleanup: "+path);
     var row=new VenueMaterialSerializationRow296{Asset=path,BeforeFileSHA256=HashArchitecture296(File.ReadAllBytes(path))};
     var serialized=new SerializedObject(material);var floats=serialized.FindProperty("m_SavedProperties.m_Floats");
     if(floats==null||!floats.isArray)throw new InvalidOperationException("Saved float array missing: "+path);
     for(int i=0;i<floats.arraySize;i++)
     {
      var entry=floats.GetArrayElementAtIndex(i);string key=entry.FindPropertyRelative("first").stringValue;
      int index=Array.IndexOf(UnusedVenueMotion296,key);if(index<0)continue;
      if(entry.FindPropertyRelative("second").floatValue!=(index==0?0f:1f))
       throw new InvalidOperationException("Unexpected unused motion value in "+path+": "+key);
      row.RemovedFloats++;
     }
     string invariant=VenueMaterialInvariant296(material);row.BeforeInvariantSHA256=HashArchitecture296(Encoding.UTF8.GetBytes(invariant));
     string clean=Regex.Replace(Encoding.UTF8.GetString(File.ReadAllBytes(path)),@"(?m)^    - (?:_AddPrecomputedVelocity|_XRMotionVectorsPass):[^\r\n]*\r?\n","");
     row.ExpectedFileSHA256=HashArchitecture296(Encoding.UTF8.GetBytes(clean));
     if(saveFiles)
     {
      File.Copy(path,receipt.ArchiveFolder+"/"+Path.GetFileName(path));
      if(File.Exists(path+".meta"))File.Copy(path+".meta",receipt.ArchiveFolder+"/"+Path.GetFileName(path)+".meta");
     }
     receipt.Rows.Add(row);targets.Add((material,invariant,row));
    }
    foreach(var target in targets)
    {
     var material=target.Material;var row=target.Row;
     if(row.RemovedFloats>0)
     {
      var serialized=new SerializedObject(material);var floats=serialized.FindProperty("m_SavedProperties.m_Floats");
      for(int i=floats.arraySize-1;i>=0;i--)
       if(UnusedVenueMotion296.Contains(floats.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue))floats.DeleteArrayElementAtIndex(i);
      serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(material);
      receipt.ChangedMaterials++;receipt.RemovedFloats+=row.RemovedFloats;
     }
     string invariant=VenueMaterialInvariant296(material);row.AfterInvariantSHA256=HashArchitecture296(Encoding.UTF8.GetBytes(invariant));
     row.RemainingSerializedStateUnchanged=invariant==target.Json;
     if(!row.RemainingSerializedStateUnchanged)throw new InvalidOperationException("Other material state changed: "+row.Asset);
     if(saveFiles)
     {
      AssetDatabase.SaveAssetIfDirty(material);row.AfterFileSHA256=HashArchitecture296(File.ReadAllBytes(row.Asset));
      row.FileOnlyUnusedFloatsChanged=row.AfterFileSHA256==row.ExpectedFileSHA256;
      EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);row.SecondSaveSHA256=HashArchitecture296(File.ReadAllBytes(row.Asset));
      row.SecondSaveIdentical=row.AfterFileSHA256==row.SecondSaveSHA256;
      if(!row.FileOnlyUnusedFloatsChanged||!row.SecondSaveIdentical)throw new InvalidOperationException("Unexpected serialized bytes changed: "+row.Asset);
     }
    }
    receipt.Materials=targets.Count;receipt.Status="PASS";return receipt;
   }
   catch(Exception error){receipt.Status="FAIL";receipt.Error=error.ToString();throw;}
   finally
   {
    Directory.CreateDirectory(O296+"/Analysis/MaterialSerialization");
    File.WriteAllText(O296+"/Analysis/MaterialSerialization/"+(saveFiles?"latest":"canonical")+".json",JsonUtility.ToJson(receipt,true));
   }
  }
  static void CanonicalizeVenueMaterials296(List<string> report)
  {
   var receipt=CanonicalVenueMaterials296(false);
   report.Add("Venue material serialization: inspected="+receipt.Materials+", removed unused URP floats="+receipt.RemovedFloats+" from "+receipt.ChangedMaterials+" materials; all remaining serialized state identical.");
  }
  public static string NormalizeVenueMaterialSerialization296()
  {
   var scene=SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene296||scene.isDirty)throw new InvalidOperationException("Saved Architecture296 Edit scene required");
   string sceneBefore=HashArchitecture296(File.ReadAllBytes(scene.path));
   var paths=new[]{G296+"/routes.json",G296+"/crossings.json"};var before=paths.Select(p=>HashArchitecture296(File.ReadAllBytes(p))).ToArray();
   var receipt=CanonicalVenueMaterials296(true);
   receipt.SceneSHA256Before=sceneBefore;receipt.SceneSHA256After=HashArchitecture296(File.ReadAllBytes(scene.path));
   receipt.SceneUnchanged=receipt.SceneSHA256Before==receipt.SceneSHA256After;receipt.SceneRemainsClean=!scene.isDirty;
   receipt.RoutesUnchanged=paths.Select((p,i)=>HashArchitecture296(File.ReadAllBytes(p))==before[i]).All(v=>v);
   if(!receipt.SceneUnchanged||!receipt.RoutesUnchanged||!receipt.SceneRemainsClean){receipt.Status="FAIL";receipt.Error="Material-only normalization changed scene or routes";}
   File.WriteAllText(O296+"/Analysis/MaterialSerialization/latest.json",JsonUtility.ToJson(receipt,true));
   if(receipt.Status!="PASS")throw new InvalidOperationException(receipt.Error);
   return "PASS: Venue material metadata only; "+receipt.Materials+" inspected, "+receipt.ChangedMaterials+" normalized, "+receipt.RemovedFloats+" unused floats removed; all other bytes/scene/routes unchanged; two saves identical.";
  }
 }
}
