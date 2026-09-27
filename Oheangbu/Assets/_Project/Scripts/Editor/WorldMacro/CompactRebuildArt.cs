using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string ArtInventory()
  {
   var roots=SceneManager.GetActiveScene().GetRootGameObjects();var lines=new List<string>();
   foreach(var component in roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c!=null&&c.GetType().Name=="WorldMacroDressingRenderer"))
   {
    var so=new SerializedObject(component);var prop=so.GetIterator();while(prop.Next(true))
     if(prop.propertyType==SerializedPropertyType.ObjectReference&&prop.objectReferenceValue is WorldMacroDressingSheetSO sheet){
      lines.Add("SHEET "+AssetDatabase.GetAssetPath(sheet));
      foreach(var p in sheet.Prototypes.Where(p=>(int)p.Realm==1||p.SourcePath.Contains("Pine")||p.Category==WorldMacroDressingSheetSO.Kind.Rock).Take(80))
       lines.Add(p.Id+" | "+p.Category+" | realm="+p.Realm+" | size="+p.Size+" | "+p.SourcePath+" | LOD="+string.Join(",",p.Lods.Select(l=>l.Parts.Sum(t=>t.Mesh.GetIndexCount(t.Submesh)/3)))+" | "+p.Lods[0].Parts[0].Material.shader.name);
     }
   }
   var tree=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/World/PineRestTrees/PhotorealPine/Tree.prefab");
   foreach(var f in tree.GetComponentsInChildren<MeshFilter>(true))lines.Add("MESHY "+f.name+" vertices="+f.sharedMesh.vertexCount+" triangles="+f.sharedMesh.triangles.Length/3+" bounds="+f.sharedMesh.bounds+" transform="+f.transform.localToWorldMatrix);
   string report=string.Join("\n",lines);File.WriteAllText(Output+"/art_inventory.txt",report);return report;
  }
 }
}
