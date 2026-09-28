using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Mesh CrossingMasonryFacing296(Mesh clipped)
  {
   var positions=clipped.vertices;var sourceUv=clipped.uv;var input=clipped.triangles;
   var vertices=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
   for(int i=0;i<input.Length;i+=3)
   {
    var a=positions[input[i]];var b=positions[input[i+1]];var c=positions[input[i+2]];
    var normal=Vector3.Cross(b-a,c-a);if(normal.sqrMagnitude<.00000001f||normal.normalized.z<.55f)continue;
    int start=vertices.Count;
    for(int k=0;k<3;k++){int index=input[i+k];var p=positions[index];p.z=0;vertices.Add(p);uvs.Add(sourceUv.Length==positions.Length?sourceUv[index]:Vector2.zero);}
    triangles.AddRange(new[]{start,start+1,start+2});
   }
   var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  // Source masonry is convex at these three assembly cuts. Close the new cut
  // planes with the source material and interpolated boundary UVs. Original
  // source faces, source proportions and all existing collision stay intact.
  static Mesh CrossingCutCaps296(Mesh clipped,float width,float maximumY)
  {
   var positions=clipped.vertices;var sourceUv=clipped.uv;var original=clipped.triangles;
   var vertices=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();var triangles=new List<int>();
   void Cap(float edge,bool vertical,Vector3 outward)
   {
    float Axis(Vector3 p)=>vertical?p.y:p.x;
    Vector2 Plane(Vector3 p)=>vertical?new Vector2(p.x,p.z):new Vector2(p.z,p.y);
    // A face already lying on this plane belongs to the original source. It
    // must not acquire a duplicate cap on top of that face.
    for(int i=0;i<original.Length;i+=3)
     if(Mathf.Abs(Axis(positions[original[i]])-edge)<.0001f&&Mathf.Abs(Axis(positions[original[i+1]])-edge)<.0001f&&Mathf.Abs(Axis(positions[original[i+2]])-edge)<.0001f&&
      Vector3.Cross(positions[original[i+1]]-positions[original[i]],positions[original[i+2]]-positions[original[i]]).sqrMagnitude>.00000001f)return;
    var candidates=new List<WallVertex296>();
    for(int i=0;i<positions.Length;i++)
    {
     if(Mathf.Abs(Axis(positions[i])-edge)>.0001f)continue;
     if(candidates.Any(p=>(p.P-positions[i]).sqrMagnitude<.00000001f))continue;
     candidates.Add(new WallVertex296{P=positions[i],N=outward,UV=sourceUv.Length==positions.Length?sourceUv[i]:Vector2.zero});
    }
    if(candidates.Count<3)return;
    candidates=candidates.OrderBy(p=>Plane(p.P).x).ThenBy(p=>Plane(p.P).y).ToList();
    float Cross(WallVertex296 a,WallVertex296 b,WallVertex296 c)
    {var ab=Plane(b.P)-Plane(a.P);var ac=Plane(c.P)-Plane(a.P);return ab.x*ac.y-ab.y*ac.x;}
    var ring=new List<WallVertex296>();
    foreach(var p in candidates){while(ring.Count>=2&&Cross(ring[ring.Count-2],ring[ring.Count-1],p)<=.0000001f)ring.RemoveAt(ring.Count-1);ring.Add(p);}
    int lower=ring.Count;
    for(int i=candidates.Count-2;i>=0;i--){var p=candidates[i];while(ring.Count>lower&&Cross(ring[ring.Count-2],ring[ring.Count-1],p)<=.0000001f)ring.RemoveAt(ring.Count-1);ring.Add(p);}
    if(ring.Count>0)ring.RemoveAt(ring.Count-1);if(ring.Count<3)return;
    if(Vector3.Dot(Vector3.Cross(ring[1].P-ring[0].P,ring[2].P-ring[0].P),outward)<0)ring.Reverse();
    int first=vertices.Count;foreach(var p in ring){vertices.Add(p.P);normals.Add(outward);uvs.Add(p.UV);}
    for(int i=1;i<ring.Count-1;i++)triangles.AddRange(new[]{first,first+i,first+i+1});
   }
   Cap(-width*.5f,false,Vector3.left);Cap(width*.5f,false,Vector3.right);
   if(!float.IsPositiveInfinity(maximumY))Cap(maximumY,true,Vector3.up);
   var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uvs);mesh.SetTriangles(triangles,0);
   mesh.RecalculateBounds();if(vertices.Count>0)mesh.RecalculateTangents();return mesh;
  }
 }
}
