using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Geometry stays fixed to the sampled world surface. Tapered leaf boundaries,
  // irregular pebbles and thin twigs provide actual relief at foot scale.
  // Render-only litter has no collisions and never changes the walkable floor.
  static void BuildDetailLitter261(GameObject parent,string folder,Vector3[][] paths,Func<float,float,RaycastHit> ground)
  {
   var mats=new Material[3];var colors=new[]{new Color(.33f,.30f,.24f),new Color(.38f,.37f,.33f),new Color(.25f,.23f,.19f)};
   for(int i=0;i<3;i++)
   {
    string path=folder+"/Litter"+i+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
    if(m==null){m=new Material(Shader.Find("Oheangbu/WorldMacroVegetation"));AssetDatabase.CreateAsset(m,path);}
    m.SetColor("_BaseColor",colors[i]);m.SetFloat("_AmbientFloor",.72f);m.SetFloat("_LightResponse",.32f);m.SetFloat("_AlphaClip",0);m.SetFloat("_WindAmplitude",0);m.SetFloat("_Cull",0);m.SetFloat("_WashStart",90);m.SetFloat("_WashEnd",500);m.SetFloat("_WashStrength",.58f);m.SetFloat("_Saturation",.3f);m.enableInstancing=true;EditorUtility.SetDirty(m);mats[i]=m;
   }
   var random=new System.Random(26103);float R(float lo,float hi)=>(float)(lo+random.NextDouble()*(hi-lo));
   var cells=new Dictionary<Vector2Int,List<Vector3>>();
   foreach(var path in paths)for(int segment=1;segment<path.Length;segment++)
   {
    var delta=path[segment]-path[segment-1];float length=delta.magnitude;var forward=delta.normalized;var side=Vector3.Cross(forward,Vector3.up);
    for(float d=0;d<length;d+=1.2f)for(int k=0;k<70;k++)
    {
     float offset=R(1.2f,17)*(k%2==0?1:-1);var p=path[segment-1]+forward*(d+R(0,1.2f))+side*offset;
     if(Mathf.PerlinNoise(p.x*.16f+3,p.z*.12f)<.39f||paths.Any(line=>FlatPathDistance(p,line)<1.1f))continue;
     var hit=ground(p.x,p.z);if(hit.normal.y<.77f)continue;
     var key=new Vector2Int(Mathf.FloorToInt(p.x/32),Mathf.FloorToInt(p.z/32));
     if(!cells.TryGetValue(key,out var points)){points=new List<Vector3>();cells.Add(key,points);}points.Add(hit.point);
    }
   }
   int total=0,totalTriangles=0;
   foreach(var pair in cells)
   {
    var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new[]{new List<int>(),new List<int>(),new List<int>()};
    foreach(var p in pair.Value)
    {
     int kind=total++%13==0?2:total%4==0?1:0;float angle=R(0,Mathf.PI*2);var f=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=Vector3.Cross(f,Vector3.up);
     float length=kind==2?R(.08f,.22f):kind==1?R(.02f,.06f):R(.035f,.075f),width=kind==2?.006f:length*R(.32f,.47f);
     int start=vertices.Count;var shape=kind==2?new[]{new Vector2(-1,0),new Vector2(0,-1),new Vector2(1,0),new Vector2(0,1)}:
       new[]{new Vector2(-1,0),new Vector2(-.55f,-.65f),new Vector2(-.1f,-1),new Vector2(.55f,-.7f),new Vector2(1,0),new Vector2(.5f,.8f),new Vector2(-.15f,.95f),new Vector2(-.65f,.5f)};
     for(int v=0;v<=shape.Length;v++)
     {
      var q=v==shape.Length?p:p+f*shape[v].x*length+side*shape[v].y*width;
      q.y=ground(q.x,q.z).point.y+(v==shape.Length?(kind==1?length*.55f:kind==2?.012f:.005f):.003f);vertices.Add(q);uv.Add(v==shape.Length?new Vector2(.5f,.5f):(shape[v]+Vector2.one)*.5f);
     }
     for(int i=0;i<shape.Length;i++)triangles[kind].AddRange(new[]{start+i,start+shape.Length,start+(i+1)%shape.Length});totalTriangles+=shape.Length;
    }
    var mesh=new Mesh{name="Ground detail "+pair.Key,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=3;
    for(int i=0;i<3;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();
    mesh=ArtMesh(mesh,folder+"/Litter_"+pair.Key.x+"_"+pair.Key.y+".asset");
    var go=new GameObject("GroundLitter_"+pair.Key.x+"_"+pair.Key.y,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent.transform,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();r.sharedMaterials=mats;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=true;
   }
   File.WriteAllText(DetailOutput261+"/litter.txt","Surface-fixed leaves/pebbles/twigs="+total+"; spatial chunks="+cells.Count+"; triangles="+totalTriangles+"; leaves 7-15cm, stones 4-12cm, twigs 16-44cm; visual microrelief only, no collision floor change.");
  }
 }
}
