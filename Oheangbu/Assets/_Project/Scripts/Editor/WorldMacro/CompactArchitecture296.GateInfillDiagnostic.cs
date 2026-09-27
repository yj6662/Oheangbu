using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.Demo;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string DiagnoseGateInfill296()
  {
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||Session292().gameObject.scene.path!=Scene296)throw new Exception("Gate diagnostic requires saved296 Edit scene");
   var root=Root296("CapitalSouthGate253/VictoryGate253/FixedArchInfill296");
   var gate=Root296("CapitalSouthGate253/VictoryGate253").GetComponent<SouthGateDoorPresentation>();
   var group=root.GetComponent<LODGroup>();var renderers=root.GetComponentsInChildren<MeshRenderer>(true);var originals=renderers.Select(r=>r.sharedMaterial).ToArray();
   string folder=O296+"/Analysis/GateInfillDiagnostic";Directory.CreateDirectory(folder);var report=new List<string>();
   var view=ArchitectureViews296().Views.Single(v=>v.Id=="south-gate-eye");
   var flat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));flat.SetColor("_BaseColor",new Color(.72f,.38f,.12f,1));flat.SetFloat("_Cull",0);
   var cameras=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None);var occlusion=cameras.Select(c=>c.useOcclusionCulling).ToArray();
   try
   {
    gate.SetOpened(true,true);Physics.SyncTransforms();group.ForceLOD(0);Shot("01-force-lod0");
    group.enabled=false;foreach(var r in renderers){r.enabled=true;r.forceRenderingOff=false;r.allowOcclusionWhenDynamic=false;}foreach(var c in cameras)c.useOcclusionCulling=false;Shot("02-no-lod-no-occlusion");
    foreach(var r in renderers)r.sharedMaterial=flat;Shot("03-flat-double-sided");
    // Original material on meshes detached from every gate and LOD parent.
    var detached=new GameObject("GateInfillDiagnosticDetached");
    try
    {
     for(int i=0;i<renderers.Length;i++)
     {
      var r=renderers[i];var go=new GameObject("Detached_"+i);go.transform.SetParent(detached.transform,false);go.transform.SetPositionAndRotation(r.transform.position,r.transform.rotation);go.transform.localScale=r.transform.lossyScale;
      go.AddComponent<MeshFilter>().sharedMesh=r.GetComponent<MeshFilter>().sharedMesh;go.AddComponent<MeshRenderer>().sharedMaterial=originals[i];r.enabled=false;
     }
     Shot("04-detached-original-material");
    }
    finally{Object.DestroyImmediate(detached);}
    var rebuilt=new List<Mesh>();var markers=new List<GameObject>();
    try
    {
     foreach(var r in renderers)
     {
      var filter=r.GetComponent<MeshFilter>();var source=filter.sharedMesh;var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.uv=source.uv;mesh.triangles=source.triangles;mesh.RecalculateBounds();mesh.UploadMeshData(false);rebuilt.Add(mesh);filter.sharedMesh=mesh;r.enabled=true;r.sharedMaterial=flat;
      report.Add("fresh mesh vertex="+mesh.vertexCount+" index="+mesh.GetIndexCount(0)+" topology="+mesh.GetTopology(0));
     }
     Shot("05-fresh-mesh-api-flat");
     foreach(var r in renderers)r.enabled=false;
     foreach(var position in new[]{new Vector3(2000,100.6f,2555.05f),new Vector3(2000,98.2f,2554.5f)})
     {var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="TemporaryVisibilityControl";cube.transform.position=position;cube.transform.localScale=new Vector3(2,.6f,.12f);cube.GetComponent<MeshRenderer>().sharedMaterial=flat;Object.DestroyImmediate(cube.GetComponent<Collider>());markers.Add(cube);}
     Shot("06-reference-cubes");
    }
    finally{foreach(var marker in markers)Object.DestroyImmediate(marker);foreach(var mesh in rebuilt)Object.DestroyImmediate(mesh);}
   }
   finally
   {
    eye293=null;target293=null;output293=null;raw293=false;for(int i=0;i<cameras.Length;i++)if(cameras[i]!=null)cameras[i].useOcclusionCulling=occlusion[i];Object.DestroyImmediate(flat);EditorSceneManager.OpenScene(Scene296);
   }
   File.WriteAllLines(folder+"/diagnostic.txt",report);return string.Join("\n",report);
   void Shot(string name)
   {
    foreach(var r in renderers)report.Add(name+" "+r.name+" enabled="+r.enabled+" active="+r.gameObject.activeInHierarchy+" forcedOff="+r.forceRenderingOff+" worldBounds="+r.bounds+" material="+r.sharedMaterial.name+" scale="+r.transform.lossyScale+" lod="+group.enabled);
    eye293=view.Eye;target293=view.Target;output293=folder+"/"+name+".png";raw293=false;Capture292(6);report.Add("CAPTURE "+name);
   }
  }
 }
}
