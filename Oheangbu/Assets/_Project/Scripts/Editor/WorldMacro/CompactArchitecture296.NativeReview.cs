using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string RefreshApproachMaterials296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new InvalidOperationException("Saved296 Edit candidate required.");
   int renderers=0;foreach(var arena in Sheet296().Arenas.Where(a=>!a.Id.StartsWith("sanctuary",StringComparison.Ordinal)))
   {
    var root=Root296("Architecture296_Venues/Approach_"+arena.Id);if(root==null)throw new InvalidOperationException("Approach root missing "+arena.Id);
    foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true)){renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>RegionalStoneVenue296(m,arena.Realm)).ToArray();EditorUtility.SetDirty(renderer);renderers++;}
   }
   Save292();return "Updated only regional stone materials on "+renderers+" approach renderers; mesh/transform/collider/navigation geometry unchanged.";
  }
  public static string DiagnoseNativeVisual296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene296)throw new InvalidOperationException("Saved296 Edit candidate required.");
   var current=Root296("Architecture296_Venues/watercourt296/DryPrincipalHall");if(current==null)throw new InvalidOperationException("Native principal hall missing.");
   var group=current.GetComponent<LODGroup>();var view=ArchitectureViews296().Views.First(v=>v.Id=="hyeongang-eye");var folder=O296+"/Analysis/NativeVisual";Directory.CreateDirectory(folder);var report=new List<string>();GameObject original=null;Material flat=null;
   try
   {
    shot("00-default",false);
    for(int level=0;level<3;level++){group.ForceLOD(level);shot("0"+(level+1)+"-lod"+level,false);}
    group.ForceLOD(0);shot("04-lod0-raw",true);
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Haeng296+"SM_Naknamhyeon.prefab");var sourceBounds=prefab.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds;
    original=Object.Instantiate(prefab);original.name="NativeVisual296_TemporaryOriginal";original.transform.SetPositionAndRotation(current.transform.position+current.transform.rotation*new Vector3(-sourceBounds.center.x,-sourceBounds.min.y,-sourceBounds.center.z),current.transform.rotation);
    foreach(var renderer in original.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>AssetDatabase.LoadAssetAtPath<Material>(A296+"/Materials/Venues/"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))+".mat")??throw new InvalidOperationException("Missing private source material "+m.name)).ToArray();
    current.SetActive(false);shot("05-original",false);shot("06-original-raw",true);
    flat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));flat.SetColor("_BaseColor",new Color(.55f,.55f,.55f,1));flat.SetFloat("_Cull",2);
    foreach(var renderer in original.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterials=Enumerable.Repeat(flat,renderer.sharedMaterials.Length).ToArray();shot("07-original-flat",true);
    original.SetActive(false);current.SetActive(true);group.ForceLOD(0);foreach(var renderer in current.GetComponentsInChildren<MeshRenderer>(true))renderer.sharedMaterials=Enumerable.Repeat(flat,renderer.sharedMaterials.Length).ToArray();shot("08-lod0-flat",true);
    report.Add("Same saved hyeongang-eye camera; candidate QEM L0/L1/L2 and untouched native source with the same private textures, then solid unlit geometry comparison. Temporary source is not saved.");
   }
   finally
   {
    eye293=null;target293=null;output293=null;raw293=false;if(original!=null)Object.DestroyImmediate(original);if(flat!=null)Object.DestroyImmediate(flat);EditorSceneManager.OpenScene(Scene296);
   }
   File.WriteAllLines(folder+"/diagnosis.txt",report);return "Captured nine native source/QEM/material diagnostics; saved candidate restored. "+folder;
   void shot(string name,bool raw)
   {eye293=view.Eye;target293=view.Target;output293=folder+"/"+name+".png";raw293=raw;Capture292(6);report.Add(name+" raw="+raw);}
  }
 }
}
