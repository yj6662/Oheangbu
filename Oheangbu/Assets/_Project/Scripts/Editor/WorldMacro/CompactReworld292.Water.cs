using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class DrainagePacket292{public CompactWorldLayoutSO.Drainage[] Drainages;}
  static string Water292()
  {
   var s=Session292();var layout=s.MountainLayout;var field=new CompactWorldSurface(layout);var scene=s.gameObject.scene;
   layout.Drainages=JsonUtility.FromJson<DrainagePacket292>(File.ReadAllText(A292+"/Surface/layout.json")).Drainages;EditorUtility.SetDirty(layout);
   foreach(var g in scene.GetRootGameObjects().Where(g=>g.name=="Compact_MountainDetails_290"||g.name=="JointedMountain275"))g.SetActive(false);
   var old=GameObject.Find("Reworld292_Water");if(old!=null)Object.DestroyImmediate(old);
   var root=new GameObject("Reworld292_Water");root.AddComponent<CompactMountainAccess>().Layout=layout;
   var mat=Asset292("Materials/RiverInk.mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/WorldMacro/MacroRiver.mat")));
   var water=new List<WorldTerrainQuery.WaterTriangle>();
   foreach(var river in layout.Drainages)
   {
    var points=new List<Vector2>();for(int i=1;i<river.Centreline.Length;i++){var a=river.Centreline[i-1];var b=river.Centreline[i];int count=Mathf.CeilToInt(Vector2.Distance(a,b)/8);for(int k=0;k<count;k++)points.Add(Vector2.Lerp(a,b,k/(float)count));}points.Add(river.Centreline.Last());
    var v=new Vector3[points.Count*2];var uv=new Vector2[v.Length];var triangles=new List<int>();
    for(int i=0;i<points.Count;i++)
    {
     var tangent=(points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(i-1,0)]).normalized;var side=new Vector2(-tangent.y,tangent.x)*river.HalfWidth;
     float y=field.Sample(points[i].x,points[i].y)+.32f;
     for(int j=0;j<2;j++){var p=points[i]+side*(j==0?-1:1);v[i*2+j]=new Vector3(p.x,y,p.y);uv[i*2+j]=new Vector2(j,i*.2f);}
     if(i>0){int a=(i-1)*2,b=i*2;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
    }
    var mesh=Asset292("Water/"+river.Id+".asset",()=>new Mesh());mesh.Clear();mesh.vertices=v;mesh.uv=uv;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
    var go=new GameObject(river.Id);go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
    for(int i=0;i<triangles.Count;i+=3)water.Add(new WorldTerrainQuery.WaterTriangle{A=v[triangles[i]],B=v[triangles[i+1]],C=v[triangles[i+2]]});
   }
   foreach(var query in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true))){query.Water=water.ToArray();query.Reindex();EditorUtility.SetDirty(query);}
   Save292();File.WriteAllText(O292+"/water.txt",layout.Drainages.Length+" water meshes; "+water.Count+" matching query triangles. Ford/bridge traversal pending.");return File.ReadAllText(O292+"/water.txt");
  }
 }
}
