using System;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static void ConformSliceTerrain(UnityEngine.SceneManagement.Scene scene,GameObject terrain,string folder,CompactWorldLayoutSO layout,WorldMacroPlaytestSO content,List<string> checks)
  {
   float blend=layout.Places.Single(p=>p.Id=="mine").SurfaceBlendDistance;if(blend<20)throw new Exception("Invalid floor transition width");
   PrefabUtility.UnpackPrefabInstance(terrain,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   Physics.SyncTransforms();
   var floors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(false)).Where(c=>c.name=="Natural_Cave_Floor"||c.name=="Exterior_Mine_Approach"||c.name=="Continuous_Approach_Soil"||c.name=="Portal_Outer_Soil").ToArray();
   if(floors.Length==0)throw new Exception("No cave floor for terrain conformance");
   var samples=new List<Vector3>();
   foreach(var floor in floors)
   {
    var bounds=floor.bounds;
    for(float z=bounds.min.z;z<=bounds.max.z;z+=4)for(float x=bounds.min.x;x<=bounds.max.x;x+=4)
     if(floor.Raycast(new Ray(new Vector3(x,bounds.max.y+2,z),Vector3.down),out var hit,bounds.size.y+5))samples.Add(hit.point);
   }
   if(samples.Count==0)throw new Exception("Floor sampling failed");
   int changed=0;
   foreach(var filter in terrain.GetComponentsInChildren<MeshFilter>())
   {
    var bounds=filter.GetComponent<Renderer>().bounds;bounds.Expand(blend*2+20);
    var nearby=samples.Where(p=>p.x>=bounds.min.x&&p.x<=bounds.max.x&&p.z>=bounds.min.z&&p.z<=bounds.max.z).ToArray();if(nearby.Length==0)continue;
    var mesh=Object.Instantiate(filter.sharedMesh);var vertices=mesh.vertices;bool dirty=false;
    for(int i=0;i<vertices.Length;i++)
    {
     var world=filter.transform.TransformPoint(vertices[i]);float distance=float.MaxValue;Vector3 nearest=default;
     foreach(var p in nearby){float d=(p.x-world.x)*(p.x-world.x)+(p.z-world.z)*(p.z-world.z);if(d<distance){distance=d;nearest=p;}}
     float weight=1-Mathf.SmoothStep(0,1,Mathf.Max(0,Mathf.Sqrt(distance)-10)/blend);if(weight<=0)continue;
     world.y=Mathf.Lerp(world.y,nearest.y-.18f,weight);vertices[i]=filter.transform.InverseTransformPoint(world);dirty=true;
    }
    if(!dirty){Object.DestroyImmediate(mesh);continue;}
    mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();string path=folder+"/"+filter.name+"_Conformed.asset";
    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}
    filter.sharedMesh=mesh;filter.GetComponent<MeshCollider>().sharedMesh=mesh;changed++;
   }
   Physics.SyncTransforms();var satchel=content.Points.Single(p=>p.Id=="worker_satchel");
   var grounds=terrain.GetComponentsInChildren<MeshCollider>().Select(c=>c.Raycast(new Ray(satchel.Position+Vector3.up*500,Vector3.down),out var hit,1000)?hit.point.y:float.NegativeInfinity).Where(float.IsFinite).ToArray();
   if(grounds.Length==0)throw new Exception("Satchel has no destination terrain");
   float correction=grounds.Max()+.12f-satchel.Position.y;satchel.Position+=Vector3.up*correction;
   var bag=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Worker_Straw_Bag");bag.position+=Vector3.up*correction;
   checks.Add("PASS private terrain conformed to placed cave floor: "+changed+" tiles; original terrain assets untouched");checks.Add("PASS satchel interaction and visual use same ground correction");
  }
 }
}
