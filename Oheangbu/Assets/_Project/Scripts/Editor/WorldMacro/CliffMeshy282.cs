using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  const string O282="../Art/World/Compact/Rebuild/Ascent282";
  const string A282=A278+"/Meshy282";
  static bool HasMeshy282()=>File.Exists(O282+"/mesh-report.json");
  static Mesh RecessFoundation282(Mesh source,string name){
   var mesh=Object.Instantiate(source);mesh.name=name;var vertices=mesh.vertices;var routePoints=RouteData278().points;
   for(int i=0;i<vertices.Length;i++){
    var p=vertices[i];float z=Mathf.Clamp(p.z,0,330);int ri=Mathf.Clamp(Mathf.FloorToInt(z),0,routePoints.Length-2);
    var route=Vector3.Lerp(routePoints[ri],routePoints[ri+1],Mathf.Clamp01(z-ri));float offset=p.x-route.x;
    float blend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,12,offset));
    float h=p.y-route.y;if(h>0)p.y=route.y+Mathf.Lerp(h,h*.12f,blend);
    vertices[i]=p;
   }
   mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();
   return ArtMesh(mesh,A282+"/"+name+".asset");
  }
  static Mesh ImportMeshy282(string name){
   var data=JsonUtility.FromJson<MeshAscent279>(File.ReadAllText(O282+"/Meshes/"+name+".json"));
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
   mesh.vertices=data.vertices;mesh.normals=data.normals;mesh.uv=data.uv;mesh.triangles=data.triangles;mesh.colors=data.colors;
   mesh.RecalculateBounds();mesh.RecalculateTangents();return ArtMesh(mesh,A282+"/"+name+".asset");
  }
  static void InstallMeshy282(Transform root,Material rock){
   if(!HasMeshy282())return;
   DevSceneKit.EnsureFolder(A282);
   // Generated masses now own the exposed mountainside. Lower the old smooth
   // backing volume beneath them, keeping the trail and valley side unchanged.
   for(int i=0;i<6;i++){
    var foundation=root.Find("Cliff_"+i);var filter=foundation.GetComponent<MeshFilter>();var collider=foundation.GetComponent<MeshCollider>();
    filter.sharedMesh=RecessFoundation282(filter.sharedMesh,"Foundation_"+i);
    collider.sharedMesh=RecessFoundation282(collider.sharedMesh,"Foundation_Collision_"+i);
   }
   var previous=root.Find("Cliff_Meshy_282");if(previous!=null)Object.DestroyImmediate(previous.gameObject);
   // Keep previous authoring data for rollback, with neither visible surfaces
   // nor invisible obsolete collision over the generated rock.
   for(int i=0;i<6;i++){var old=root.Find("Cliff_Facade_"+i);if(old!=null){old.GetComponent<Renderer>().enabled=false;old.GetComponent<Collider>().enabled=false;}}
   var assembly=new GameObject("Cliff_Meshy_282");assembly.transform.SetParent(root,false);
   for(int i=0;i<7;i++){
    var go=new GameObject("Meshy_bedrock_"+i);go.transform.SetParent(assembly.transform,false);
    var levels=new List<LOD>();Mesh collision=null;
    for(int level=0;level<3;level++){
     var mesh=ImportMeshy282("Bedrock_"+i+"_LOD"+level);
     var child=MeshObject278("Rock_LOD"+level,mesh,rock,go.transform,false);
     levels.Add(new LOD(level==0?.28f:level==1?.10f:.004f,new[]{child.GetComponent<Renderer>()}));
     if(level==1)collision=mesh;
    }
    var solid=new GameObject("Cliff_Meshy_collision_"+i);solid.transform.SetParent(go.transform,false);solid.AddComponent<MeshCollider>().sharedMesh=collision;
    var group=go.AddComponent<LODGroup>();group.SetLODs(levels.ToArray());group.RecalculateBounds();
   }
  }
  public static string Meshy282(string command){
   if(command=="build")return Ascent278("build");
   if(command!="check")throw new ArgumentException("Use build or check");
   if(SceneManager.GetActiveScene().path!=Scene278)throw new Exception("Open prototype first");
   var lines=new List<string>(CheckAscent278().Split('\n'));
   void C(bool value,string label)=>lines.Add((value?"PASS ":"FAIL ")+label);
   var root=GameObject.Find("Cliff_Meshy_282");var groups=root!=null?root.GetComponentsInChildren<LODGroup>():Array.Empty<LODGroup>();
   C(groups.Length==7&&groups.All(g=>g.GetLODs().Length==3),"seven generated bedrock masses, each with three visual LODs");
   C(groups.All(g=>g.GetComponentsInChildren<MeshCollider>().Length==1&&g.GetComponentInChildren<MeshCollider>().sharedMesh.triangles.Length/3<=10100),"one static simplified collision mesh per generated mass");
   C(groups.All(g=>g.GetLODs().SelectMany(l=>l.renderers).All(r=>r.sharedMaterial==AssetDatabase.LoadAssetAtPath<Material>(A278+"/Materials/Granite.mat"))),"generated geometry uses preserved prototype granite material");
   C(Enumerable.Range(0,6).All(i=>{var g=GameObject.Find("Cliff_Facade_"+i);return g!=null&&!g.GetComponent<Renderer>().enabled&&!g.GetComponent<Collider>().enabled;}),"previous procedural facades retained but rendering and collision disabled");
   C(groups.All(g=>{var counts=g.GetLODs().Select(l=>l.renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)).ToArray();return counts[0]>counts[1]&&counts[1]>counts[2];}),"each LOD reduces actual triangle count");
   File.WriteAllLines(O282+"/checks.txt",lines);return string.Join("\n",lines);
  }
 }
}
