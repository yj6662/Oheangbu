using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static readonly string[] ComplexNativeSources296={Haeng296+"SM_Naknamhyeon.prefab",Jeju296+"Decoration/Prop_House_01.prefab",Jeju296+"Decoration/Prop_House_02.prefab"};
  [Serializable] sealed class ComplexPartData296 {public string Material;public RoofPatchData296 Mesh;public RoofPatchData296[] Levels;}
  [Serializable] sealed class ComplexAssetData296 {public string Id,Source,SourceSha256,ExportSha256,Method;public bool NearFromSource;public RoofPatchData296 Mesh;public Vector3 Min,Max;public ComplexPartData296[] Parts;}
  sealed class NativeExportPart296 {public readonly List<Vector3> Vertices=new List<Vector3>();public readonly List<Vector2> UV=new List<Vector2>();public readonly List<int> Triangles=new List<int>();}
  static void ExportComplexSources296(List<string> report)
  {
   Directory.CreateDirectory(O296+"/Complex/Source");
   foreach(string path in ComplexNativeSources296)
   {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new Exception("Native source missing "+path);
    var accepted=new HashSet<Renderer>(prefab.GetComponentsInChildren<Renderer>(true));foreach(var group in prefab.GetComponentsInChildren<LODGroup>(true))foreach(var lod in group.GetLODs().Skip(1))foreach(var renderer in lod.renderers)accepted.Remove(renderer);
    var groups=new Dictionary<Material,NativeExportPart296>();Bounds bounds=default;bool haveBounds=false;
    foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
    {
     var renderer=filter.GetComponent<Renderer>();var mesh=filter.sharedMesh;if(mesh==null||renderer==null||!accepted.Contains(renderer))continue;var material=renderer.sharedMaterials;
     if(material.Length<mesh.subMeshCount||material.Take(mesh.subMeshCount).Any(m=>m==null))throw new Exception("Native source material coverage "+filter.name);
     var vertices=mesh.vertices;var uv=mesh.uv;var matrix=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
     for(int sub=0;sub<mesh.subMeshCount;sub++)
     {
      if(!groups.TryGetValue(material[sub],out var part)){part=new NativeExportPart296();groups.Add(material[sub],part);}var map=new Dictionary<int,int>();
      foreach(int index in mesh.GetTriangles(sub))
      {
       if(!map.TryGetValue(index,out int mapped))
       {mapped=part.Vertices.Count;map[index]=mapped;var point=matrix.MultiplyPoint3x4(vertices[index]);part.Vertices.Add(point);part.UV.Add(uv.Length==vertices.Length?uv[index]:Vector2.zero);if(!haveBounds){bounds=new Bounds(point,Vector3.zero);haveBounds=true;}else bounds.Encapsulate(point);}
       part.Triangles.Add(mapped);
      }
     }
    }
    string id=AssetDatabase.AssetPathToGUID(path);var parts=groups.OrderBy(p=>AssetDatabase.GetAssetPath(p.Key),StringComparer.Ordinal).Select(pair=>new ComplexPartData296{Material=AssetDatabase.GetAssetPath(VenueMaterial296(pair.Key)),Mesh=new RoofPatchData296{Vertices=pair.Value.Vertices.ToArray(),UV=pair.Value.UV.ToArray(),Triangles=pair.Value.Triangles.ToArray()}}).ToArray();
    var data=new ComplexAssetData296{Id=id,Source=path,SourceSha256=HashVenue296(path),Min=bounds.min,Max=bounds.max,Parts=parts};File.WriteAllText(O296+"/Complex/Source/"+id+".json",JsonUtility.ToJson(data));
    report.Add("Compact raw native source "+path+" size="+bounds.size+" triangles="+parts.Sum(p=>p.Mesh.Triangles.Length/3)+" referenced vertices="+parts.Sum(p=>p.Mesh.Vertices.Length));
   }
  }
  static RoofPatchData296[] OriginalComplexParts296(string source,ComplexPartData296[] parts)
  {
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(source);var accepted=new HashSet<Renderer>(prefab.GetComponentsInChildren<Renderer>(true));
   foreach(var group in prefab.GetComponentsInChildren<LODGroup>(true))foreach(var lod in group.GetLODs().Skip(1))foreach(var renderer in lod.renderers)accepted.Remove(renderer);
   var result=new RoofPatchData296[parts.Length];
   for(int part=0;part<parts.Length;part++)
   {
    string materialGuid=Path.GetFileNameWithoutExtension(parts[part].Material);var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
    foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
    {
     var renderer=filter.GetComponent<Renderer>();var mesh=filter.sharedMesh;if(mesh==null||renderer==null||!accepted.Contains(renderer))continue;
     var sourceVertices=mesh.vertices;var sourceNormals=mesh.normals;var sourceUv=mesh.uv;var matrix=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
     for(int sub=0;sub<mesh.subMeshCount;sub++)
     {
      if(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sharedMaterials[sub]))!=materialGuid)continue;
      var map=new Dictionary<int,int>();foreach(int index in mesh.GetTriangles(sub))
      {
       if(!map.TryGetValue(index,out int mapped)){mapped=vertices.Count;map.Add(index,mapped);vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[index]));normals.Add(normalMatrix.MultiplyVector(sourceNormals[index]).normalized);uv.Add(sourceUv[index]);}triangles.Add(mapped);
      }
     }
    }
    if(triangles.Count==0)throw new Exception("Original native material has no source faces: "+parts[part].Material);
    result[part]=new RoofPatchData296{Vertices=vertices.ToArray(),Normals=normals.ToArray(),UV=uv.ToArray(),Triangles=triangles.ToArray()};
   }
   return result;
  }
  static Mesh ComplexCollision296(string source,bool burned,Bounds sourceBounds)
  {
   string guid=AssetDatabase.AssetPathToGUID(source),file=O296+"/Complex/Collision/"+guid+".json";if(!File.Exists(file))throw new Exception("Independent native collision export missing: "+file);
   var data=JsonUtility.FromJson<ComplexAssetData296>(File.ReadAllText(file));if(data.SourceSha256!=HashVenue296(source)||data.ExportSha256!=HashVenue296(O296+"/Complex/Source/"+guid+".json"))throw new Exception("Native collision source changed: "+source);
   var input=data.Mesh;var triangles=new List<int>();
   for(int t=0;t<input.Triangles.Length;t+=3)
   {
    var centre=(input.Vertices[input.Triangles[t]]+input.Vertices[input.Triangles[t+1]]+input.Vertices[input.Triangles[t+2]])/3;
    bool lost=burned&&centre.y>sourceBounds.min.y+sourceBounds.size.y*.57f&&centre.x>sourceBounds.center.x+sourceBounds.size.x*.1f&&centre.z<sourceBounds.center.z+sourceBounds.size.z*.12f;
    if(!lost)triangles.AddRange(new[]{input.Triangles[t],input.Triangles[t+1],input.Triangles[t+2]});
   }
   var mesh=Asset296("Meshes/Complex/"+guid+(burned?"_burnt":"")+"_IndependentCollision.asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=input.Vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
  }
  static readonly Dictionary<string,VenueModule296> complexReduced296=new Dictionary<string,VenueModule296>();
  static VenueModule296 ComplexModule296(string source)
  {
   if(complexReduced296.TryGetValue(source,out var existing))return existing;string guid=AssetDatabase.AssetPathToGUID(source),path=O296+"/Complex/Reduced/"+guid+".json";
   if(!File.Exists(path))throw new Exception("Native complex LOD must be generated before venues: "+path);
   var data=JsonUtility.FromJson<ComplexAssetData296>(File.ReadAllText(path));if(data.SourceSha256!=HashVenue296(source)||data.ExportSha256!=HashVenue296(O296+"/Complex/Source/"+guid+".json"))throw new Exception("Native complex source changed: "+source);
   var originals=data.NearFromSource?OriginalComplexParts296(source,data.Parts):null;var parts=new List<VenuePart296>();
   for(int p=0;p<data.Parts.Length;p++)
   {
    var input=data.Parts[p];if(input.Levels==null||input.Levels.Length!=(data.NearFromSource?2:3))throw new Exception("Native complex needs three LODs.");var meshes=new Mesh[3];
    for(int level=0;level<3;level++)
    {
     var item=data.NearFromSource?(level==0?originals[p]:input.Levels[level-1]):input.Levels[level];var mesh=Asset296("Meshes/Complex/"+guid+"_"+p+"_"+level+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=item.Vertices;mesh.uv=item.UV;mesh.triangles=item.Triangles;
     if(item.Normals!=null&&item.Normals.Length==item.Vertices.Length)mesh.normals=item.Normals;else mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);meshes[level]=mesh;
    }
    var material=AssetDatabase.LoadAssetAtPath<Material>(input.Material);if(material==null)throw new Exception("Native complex material missing "+input.Material);parts.Add(new VenuePart296{Meshes=meshes,Material=material});
   }
   var module=new VenueModule296{Id=guid,Path=source,Bounds=new Bounds((data.Min+data.Max)*.5f,data.Max-data.Min),Parts=parts.ToArray()};complexReduced296[source]=module;venueModules296[source]=module;return module;
  }
 }
}
