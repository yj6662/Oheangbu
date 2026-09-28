using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class CrossingMeshData296
  {public Vector3[] Vertices,Normals;public Vector2[] UV;public int[] Triangles;}
  [Serializable] sealed class CrossingLodPart296
  {
   public string Source,Material,SourceSha256;public int Part;public CrossingMeshData296 Mesh;
   public CrossingMeshData296[] Levels;public int[] ActualTriangles;public float[] BoundsDrift;
  }
  [Serializable] sealed class CrossingLodFile296
  {public string Method,ExportSha256;public CrossingLodPart296[] Sources;}
  static readonly Dictionary<string,Mesh> crossingLodMeshes296=new Dictionary<string,Mesh>();
  static readonly Dictionary<Mesh,Mesh[]> combinedCrossingLods296=new Dictionary<Mesh,Mesh[]>();
  public static string ExportCrossingLods296()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Source export requires Edit mode");
   var sources=new List<CrossingLodPart296>();
   foreach(string path in new[]{StoneFloor296,TimberFloor296,Rampart296,Parapet296,TimberBeam296,TimberColumn296,StoneColumn296})
   {
    var data=CrossingSource(path);
    for(int part=0;part<data.Parts.Count;part++)
    {
     var p=data.Parts[part];var used=p.mesh.GetTriangles(p.submesh).Distinct().ToArray();var indices=used.Select((v,i)=>(v,i)).ToDictionary(x=>x.v,x=>x.i);
     var normals=p.mesh.normals;var uv=p.mesh.uv;var vertices=p.mesh.vertices;var normalMatrix=p.matrix.inverse.transpose;
     sources.Add(new CrossingLodPart296{Source=path,Part=part,Material=AssetDatabase.GetAssetPath(p.material),SourceSha256=CrossingHash296(path),
      Mesh=new CrossingMeshData296{Vertices=used.Select(i=>p.matrix.MultiplyPoint3x4(vertices[i])).ToArray(),
       Normals=used.Select(i=>normals.Length==p.mesh.vertexCount?normalMatrix.MultiplyVector(normals[i]).normalized:Vector3.up).ToArray(),
       UV=used.Select(i=>uv.Length==p.mesh.vertexCount?uv[i]:Vector2.zero).ToArray(),Triangles=p.mesh.GetTriangles(p.submesh).Select(i=>indices[i]).ToArray()}});
    }
   }
   Directory.CreateDirectory(O296+"/CrossingLods");string file=O296+"/CrossingLods/sources.json";
   File.WriteAllText(file,JsonUtility.ToJson(new CrossingLodFile296{Method="Owned KCISA source parts in neutral Unity coordinates; original source UV, normal, material and triangle topology. Original assets read-only.",Sources=sources.ToArray()}));
   return Path.GetFullPath(file)+"; source parts="+sources.Count+"; source triangles="+sources.Sum(s=>s.Mesh.Triangles.Length/3);
  }
  public static string ImportCrossingLods296()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Source LOD import requires Edit mode");
   string folder=O296+"/CrossingLods/";var file=JsonUtility.FromJson<CrossingLodFile296>(File.ReadAllText(folder+"lods.json"));
   if(file.ExportSha256!=CrossingHash296(folder+"sources.json"))throw new InvalidOperationException("Source LOD export hash mismatch");
   foreach(var source in file.Sources)
   {
    if(source.SourceSha256!=CrossingHash296(source.Source)||source.Levels==null||source.Levels.Length!=3)
     throw new InvalidOperationException("Source changed or three independent LODs missing: "+source.Source);
    for(int level=0;level<3;level++)
    {
     var data=source.Levels[level];if(data.Vertices.Length==0||data.Triangles.Length==0||data.Normals.Length!=data.Vertices.Length||data.UV.Length!=data.Vertices.Length||data.Triangles.Any(i=>i<0||i>=data.Vertices.Length))
      throw new InvalidOperationException("Invalid source LOD geometry: "+source.Source+"/"+level);
     var mesh=Asset296(CrossingSourceLodPath296(source.Source,source.Part,level),()=>new Mesh());
     mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=data.Vertices;mesh.normals=data.Normals;mesh.uv=data.UV;mesh.triangles=data.Triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
    }
   }
   crossingLodMeshes296.Clear();combinedCrossingLods296.Clear();AssetDatabase.SaveAssets();
   return "Imported source QEM LOD parts="+file.Sources.Length+"; aggregate LOD triangles="+string.Join("/",Enumerable.Range(0,3).Select(l=>file.Sources.Sum(s=>s.Levels[l].Triangles.Length/3)))+". Rebuild candidate crossings and gates to apply.";
  }
  static string CrossingHash296(string path)
  {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
  static string CrossingSourceLodPath296(string source,int part,int level)=>"Meshes/Crossings/SourceLods/"+Path.GetFileNameWithoutExtension(source)+"_part"+part+"_LOD"+level+".asset";
  static (Mesh mesh,int submesh,Matrix4x4 matrix) CrossingPart296(string path,int index,int level)
  {
   string key=path+":"+index+":"+level;
   if(!crossingLodMeshes296.TryGetValue(key,out var mesh))
   {mesh=AssetDatabase.LoadAssetAtPath<Mesh>(A296+"/"+CrossingSourceLodPath296(path,index,level));if(mesh!=null)crossingLodMeshes296[key]=mesh;}
   if(mesh!=null)return(mesh,0,Matrix4x4.identity);
   var part=CrossingSource(path).Parts[index];return(part.mesh,part.submesh,part.matrix);
  }
 }
}
