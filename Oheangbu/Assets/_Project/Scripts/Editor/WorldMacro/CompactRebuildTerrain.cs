using System;
using System.IO;
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
  // Fresh absolute height field. It never reads the compressed map or its height data.
  sealed class RebuildHeight
  {
   readonly CompactWorldLayoutSO layout;
   readonly CompactReliefGrid relief;
   readonly Dictionary<string,CompactWorldLayoutSO.Place> places;
   readonly Dictionary<string,float> floors;
   readonly List<(Vector2 a,Vector2 b,float ha,float hb,float width)> paths=new();
   public RebuildHeight(CompactWorldLayoutSO source)
   {
    layout=source;relief=new CompactReliefGrid(source);places=source.Places.ToDictionary(p=>p.Id);floors=source.Places.ToDictionary(p=>p.Id,p=>Raw(p.XZ));
    foreach(var route in source.Routes)
    {
     // Realm connections are topology, not straight roads carved across mountains.
     if(!route.GradeForVehicle)continue;
     var points=new[]{places[route.From].XZ}.Concat(route.Bends).Concat(new[]{places[route.To].XZ}).ToArray();
     float length=0;for(int i=1;i<points.Length;i++)length+=Vector2.Distance(points[i-1],points[i]);
     float travelled=0;
     for(int i=1;i<points.Length;i++)
     {
      float next=travelled+Vector2.Distance(points[i-1],points[i]);
      paths.Add((points[i-1],points[i],Mathf.Lerp(floors[route.From],floors[route.To],travelled/length),Mathf.Lerp(floors[route.From],floors[route.To],next/length),route.Width));travelled=next;
     }
    }
   }
   static float Distance(Vector2 p,Vector2 a,Vector2 b,out float t)
   {t=Mathf.Clamp01(Vector2.Dot(p-a,b-a)/Mathf.Max(.001f,(b-a).sqrMagnitude));return Vector2.Distance(p,Vector2.Lerp(a,b,t));}
   float Raw(Vector2 p)
   {
    float h=layout.BaseHeight+layout.ReliefHeight*(Mathf.PerlinNoise(p.x/layout.ReliefWavelength+17,p.y/layout.ReliefWavelength+41)-.5f);
    h+=10*(Mathf.PerlinNoise(p.x/340+23,p.y/340+9)-.5f);
    foreach(var ridge in layout.Ridges)
    {
     float d=float.MaxValue;for(int i=1;i<ridge.Spine.Length;i++)d=Mathf.Min(d,Distance(p,ridge.Spine[i-1],ridge.Spine[i],out _));
     h+=ridge.Height*Mathf.Exp(-d*d/(ridge.Width*ridge.Width));
    }
    float riverDistance=float.MaxValue;
    for(int i=1;i<layout.River.Length;i++)riverDistance=Mathf.Min(riverDistance,Distance(p,layout.River[i-1],layout.River[i],out _));
    float valley=Mathf.Exp(-riverDistance*riverDistance/(95*95));
    return Mathf.Lerp(h,layout.BaseHeight-8+6*p.y/layout.Extent.y,valley);
   }
   public float Sample(float x,float z)
   {
    var p=new Vector2(x,z);float h=Raw(p),best=0,target=h;
    foreach(var path in paths)
    {
     float d=Distance(p,path.a,path.b,out float t),w=1-Mathf.SmoothStep(0,1,Mathf.Max(0,d-path.width*.5f)/45);
     if(w>best){best=w;target=Mathf.Lerp(path.ha,path.hb,t);}
    }
    h=Mathf.Lerp(h,target,best);
    foreach(var place in layout.Places)
    {
     float d=Vector2.Distance(p,place.XZ),w=1-Mathf.SmoothStep(0,1,Mathf.Max(0,d-place.GroundRadius)/40);
     h=Mathf.Lerp(h,floors[place.Id],w);
    }
    return h+relief.Sample(x,z);
   }
   public Vector3 Normal(float x,float z)=>new Vector3(Sample(x-1,z)-Sample(x+1,z),2,Sample(x,z-1)-Sample(x,z+1)).normalized;
  }
  static string Terrain()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene)throw new Exception("Compact edit required");
   var layout=AssetDatabase.LoadAssetAtPath<CompactWorldLayoutSO>(Folder+"/WorldLayout.asset");
   if(layout==null)throw new Exception("Layout required");
   if(layout.TileSegments<8||layout.TileSegments>128||layout.TileSize<=0)throw new Exception("Invalid terrain sampling");
   string meshFolder=Folder+"/Terrain";if(!AssetDatabase.IsValidFolder(meshFolder))AssetDatabase.CreateFolder(Folder,"Terrain");
   var field=new RebuildHeight(layout);var root=new GameObject("Compact_Rebuild_Terrain_Staging");
   var terrainRenderer=SceneManager.GetActiveScene().GetRootGameObjects().Where(g=>g!=root).SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).FirstOrDefault(r=>r.name.IndexOf("Terrain",StringComparison.OrdinalIgnoreCase)>=0&&r.bounds.size.x>100&&r.sharedMaterial!=null);
   if(terrainRenderer==null){Object.DestroyImmediate(root);throw new Exception("Existing Compact terrain material not found");}
   var sourceMaterial=terrainRenderer.sharedMaterial;
   int n=layout.TileSegments,side=n+1,tiles=0,triangles=0;float step=layout.TileSize/n;
   var triangleIndices=new int[n*n*6];int at=0;
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){int i=z*side+x;triangleIndices[at++]=i;triangleIndices[at++]=i+side;triangleIndices[at++]=i+1;triangleIndices[at++]=i+1;triangleIndices[at++]=i+side;triangleIndices[at++]=i+side+1;}
   try
   {
    for(int tz=0;tz<Mathf.CeilToInt(layout.Extent.y/layout.TileSize);tz++)for(int tx=0;tx<Mathf.CeilToInt(layout.Extent.x/layout.TileSize);tx++)
    {
     var vertices=new Vector3[side*side];var normals=new Vector3[vertices.Length];var uv=new Vector2[vertices.Length];
     for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
     {
      int i=z*side+x;float wx=tx*layout.TileSize+x*step,wz=tz*layout.TileSize+z*step;
      vertices[i]=new Vector3(x*step,field.Sample(wx,wz),z*step);normals[i]=field.Normal(wx,wz);uv[i]=new Vector2(wx/64,wz/64);
     }
     string name=$"Terrain_{tx:D2}_{tz:D2}",path=meshFolder+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
     if(mesh==null){mesh=new Mesh{name=name};AssetDatabase.CreateAsset(mesh,path);}else mesh.Clear();
     mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangleIndices;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
     var tile=new GameObject(name);tile.transform.SetParent(root.transform,false);tile.transform.localPosition=new Vector3(tx*layout.TileSize,0,tz*layout.TileSize);
     tile.AddComponent<MeshFilter>().sharedMesh=mesh;tile.AddComponent<MeshRenderer>().sharedMaterial=sourceMaterial;tile.AddComponent<MeshCollider>().sharedMesh=mesh;
     tiles++;triangles+=triangleIndices.Length/3;
    }
    PrefabUtility.SaveAsPrefabAsset(root,Folder+"/TerrainStaging.prefab");AssetDatabase.SaveAssets();
    var image=new Texture2D(401,601,TextureFormat.RGB24,false);float min=float.MaxValue,max=float.MinValue;
    for(int z=0;z<=600;z++)for(int x=0;x<=400;x++)
    {float h=field.Sample(x*10,z*10);min=Mathf.Min(min,h);max=Mathf.Max(max,h);float shade=Mathf.Clamp01(h/300);image.SetPixel(x,z,new Color(shade,shade,shade));}
    image.Apply();File.WriteAllBytes(Output+"/height.png",image.EncodeToPNG());Object.DestroyImmediate(image);
    string report=$"Fresh terrain staging: {tiles} tiles / {triangles} triangles / {step:F4} m sampling / height {min:F2}..{max:F2} m\nMaterial reused: {AssetDatabase.GetAssetPath(sourceMaterial)}\nNo old height map sampled. Shared-edge world samples and central-difference normals. Staging prefab only: canonical scene geometry, routes, map and NavMesh have not yet migrated. No traversal or art approval.";
    File.WriteAllText(Output+"/terrain_staging.txt",report);return report;
   }
   finally{Object.DestroyImmediate(root);}
  }
 }
}
