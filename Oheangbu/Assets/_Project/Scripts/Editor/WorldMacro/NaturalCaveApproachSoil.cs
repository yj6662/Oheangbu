using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class NaturalCaveApproachSoil
 {
  [Serializable] class RouteRecord {public int prefixCount;public Vector3[] exteriorTail;}
  public static string Execute(string command)
  {
   if((command!="apply"&&command!="refine")||EditorApplication.isPlaying)throw new ArgumentException(command);
   var root=GameObject.Find(WorldMacroNaturalCave.RootName).transform;
   foreach(Transform previous in root)if(previous.name.StartsWith("Continuous_Approach_Soil")){if(command!="refine")throw new Exception("Already installed");previous.name="Continuous_Approach_Soil_Previous_"+previous.GetInstanceID();previous.gameObject.SetActive(false);}
   var strip=root.Find("Exterior_Mine_Approach");var stripCollider=strip.GetComponent<MeshCollider>();
   var solid=root.Find("Solid_Mountain_Portal").GetComponentsInChildren<MeshCollider>();
   var terrain=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Where(c=>c.name.StartsWith("Terrain_")).ToArray();
   var nodes=new[]{new Vector3(-54,0,5),new Vector3(-43,0,5),new Vector3(-26,0,-1),new Vector3(-9,0,-15),new Vector3(0,0,-26),new Vector3(8,0,-38)};
   float Surface(Vector3 p){float y=float.NegativeInfinity;var ray=new Ray(root.TransformPoint(new Vector3(p.x,3,p.z)),Vector3.down);foreach(var c in solid.Concat(terrain))if(c.Raycast(ray,out var h,40)&&h.normal.y>.4f)y=Mathf.Max(y,root.InverseTransformPoint(h.point).y);return float.IsNegativeInfinity(y)?float.NaN:y-.04f;}
   float Road(Vector3 p){if(p.x< -26)return .025f;var ray=new Ray(root.TransformPoint(new Vector3(p.x,10,p.z)),Vector3.down);if(stripCollider.Raycast(ray,out var h,30))return root.InverseTransformPoint(h.point).y;var y=Surface(p);return float.IsNaN(y)?.025f:Mathf.Max(y,.025f);}
   var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();var valid=new List<bool>();
   bool was=strip.gameObject.activeSelf;strip.gameObject.SetActive(true);Physics.SyncTransforms();
   try{
   for(int x=0;x<=62;x++)for(int z=0;z<=84;z++)
   {var p=new Vector3(-54+x,0,-45+z);float distance=float.MaxValue;Vector3 nearest=default;
    for(int i=1;i<nodes.Length;i++){var d=nodes[i]-nodes[i-1];var q=nodes[i-1]+d*Mathf.Clamp01(Vector3.Dot(p-nodes[i-1],d)/d.sqrMagnitude);var dist=Vector3.Distance(p,q);if(dist<distance){distance=dist;nearest=q;}}
    float core=Road(nearest),ground=Surface(p);bool supported=!float.IsNaN(ground);valid.Add((supported&&distance<12)||distance<4);float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,12,distance));p.y=supported?Mathf.Lerp(ground,core,weight):core;if(p.x< -45&&distance<11)p.y=.022f;vertices.Add(p);uv.Add(new Vector2(p.x,p.z)*.3f);
   }
   }finally{strip.gameObject.SetActive(was);}
   for(int x=0;x<62;x++)for(int z=0;z<84;z++){int a=x*85+z,b=a+85;if(valid[a]&&valid[a+1]&&valid[b])triangles.AddRange(new[]{a,a+1,b});if(valid[b]&&valid[a+1]&&valid[b+1])triangles.AddRange(new[]{b,a+1,b+1});}
   var mesh=new Mesh{name="Continuous_Approach_Soil"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(WorldMacroNaturalCave.Folder+"/Meshes/Continuous_Approach_Soil.asset"));
   var go=new GameObject("Continuous_Approach_Soil");go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=root.Find("Natural_Cave_Floor").GetComponent<Renderer>().sharedMaterial;go.AddComponent<MeshCollider>().sharedMesh=mesh;
   foreach(var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.Contains("Apron")||f.name=="Exterior_Mine_Approach"||f.name=="Portal_Outer_Soil"))f.gameObject.SetActive(false);
   var s=Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroPlaytestSession>();var record=JsonUtility.FromJson<RouteRecord>(File.ReadAllText(WorldMacroNaturalCave.Output+"/gameplay_integration.json"));
   for(int i=0;i<record.exteriorTail.Length;i++)s.Content.MainPath[record.prefixCount+i]=record.exteriorTail[i];
   Physics.SyncTransforms();for(int i=0;i<record.prefixCount;i++){var p=root.InverseTransformPoint(s.Content.MainPath[i]);if(p.x> -43){var ray=new Ray(root.TransformPoint(new Vector3(p.x,6,p.z)),Vector3.down);if(go.GetComponent<MeshCollider>().Raycast(ray,out var h,20))s.Content.MainPath[i]=h.point+Vector3.up*.10f;}}
   EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());var text="Continuous soil: 62m x84m, "+triangles.Count/3+" tris. Same mesh for surface/collision; path flanks blend over 4–12m to carved ground. Old raised strips and underlays archived inactive. Original exterior route tail restored exactly.";File.WriteAllText(WorldMacroNaturalCave.Output+"/continuous_approach.txt",text);return text;
  }
 }
}
