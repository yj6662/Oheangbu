using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Render-only load paths, all below the retained walk surface. Existing
  // physics and path records remain authoritative and are fingerprinted by
  // the narrow refresh command before and after visual replacement.
  static void BuildBridgeLoadBearing296(CrossingBatch296 batch,Vector3[] points,float width,bool timber,bool trestle,
   CompactWorldSurface field,Vector3[][] otherRoutes,Vector3[] skipShared,Func<Vector3,Vector3> railGround=null)
  {
   float total=BridgeLength296(points);bool Skip(Vector3 p,float margin=.25f)=>skipShared!=null&&DistanceBridgePolyline296(p,skipShared)<margin;
   string rail=timber?TimberBeam296:Parapet296;var source=CrossingSource(rail);
   float railScale=timber?Mathf.Min(.2f/Mathf.Max(.01f,source.Bounds.size.y),4f/source.Bounds.size.x):.88f/source.Bounds.size.y;
   float railLength=Mathf.Max(.25f,source.Bounds.size.x*railScale);
   // Same retained rail volumes/placements. Do not invent new blocking posts
   // at the free branch throat or leave invisible old rail colliders behind.
   for(float d=4+railLength*.5f;d<total-4;d+=railLength*.98f)
   {
    var p=BridgeSample296(points,d,out var f);var r=Vector3.Cross(Vector3.up,f).normalized;if(Skip(p))continue;
    foreach(float sign in new[]{-1f,1f})
    {
     var edge=p+r*sign*(width*.5f+.12f);
     bool junction=new[]{-.48f,0,.48f}.Any(along=>otherRoutes.Any(route=>DistanceBridgePolyline296(edge+f*(along*railLength),route)<width*.5f-.18f));
     if(junction)continue;
     var railBase=railGround==null?edge:railGround(edge);
     batch.Uniform(rail,railBase+Vector3.up*(timber?.76f:0),Quaternion.LookRotation(f,Vector3.up)*Quaternion.Euler(0,90,0),railScale);
     if(timber)batch.Uniform(TimberColumn296,edge-f*railLength*.49f,Quaternion.identity,.95f/CrossingSource(TimberColumn296).Bounds.size.y);
    }
   }
   var stations=new List<float>();float spacing=timber?6:9;
   for(float d=6;d<total;d+=spacing)stations.Add(d);
   foreach(float d in stations)
   {
    var p=BridgeSample296(points,d,out var f);f=Vector3.ProjectOnPlane(f,Vector3.up).normalized;
    var r=Vector3.Cross(Vector3.up,f);if(Skip(p,.5f)||p.y-field.Sample(p.x,p.z)<=1.2f)continue;
    float ground=field.Sample(p.x,p.z);
    if(trestle)
    {
     foreach(float side in new[]{-.32f,.32f})
     {
      var x=p+r*width*side;float floor=field.Sample(x.x,x.z)-.3f;
      BridgeTimber296(batch,new Vector3(x.x,floor,x.z),new Vector3(x.x,p.y-.43f,x.z),.36f);
     }
     // A transverse bent and repeated X braces make tall posts legible as a
     // connected timber frame. The top stays under the ordinary deck support.
     var left=p-r*width*.32f;var right=p+r*width*.32f;
     BridgeTimber296(batch,left-Vector3.up*.49f,right-Vector3.up*.49f,.36f);
     float bottom=Mathf.Max(field.Sample(left.x,left.z),field.Sample(right.x,right.z))+.45f,top=p.y-.78f;
     for(float y=bottom;y<top-.7f;y+=4.2f)
     {
      float upper=Mathf.Min(top,y+4.2f);
      BridgeTimber296(batch,new Vector3(left.x,y,left.z),new Vector3(right.x,upper,right.z),.18f);
      BridgeTimber296(batch,new Vector3(right.x,y,right.z),new Vector3(left.x,upper,left.z),.18f);
     }
    }
    else
    {
     // A wall pier joins the old pair of decorative column footprints. Four
     // real source masonry faces close its sides; there is no exposed top
     // ledge. The retained deck covers the pier head completely.
     float across=width-(timber?.7f:.3f),depth=timber?1.65f:2.0f;
     float floor=ground-.35f;
     foreach(float x in new[]{-across*.5f,across*.5f})foreach(float z in new[]{-depth*.5f,depth*.5f})
     {var q=p+r*x+f*z;floor=Mathf.Min(floor,field.Sample(q.x,q.z)-.25f);}
     BridgeMasonryPier296(batch,p,r,f,across,depth,floor,p.y-.11f);
     if(timber)BridgeTimber296(batch,p-r*(width*.42f)-Vector3.up*.36f,p+r*(width*.42f)-Vector3.up*.36f,.38f);
    }
   }
   if(timber)
   {
    // Longitudinal stringers retain their native pole diameter. Only segment
    // ends are cut; there is no nonuniform scale along the source's long axis.
    for(float d=0;d<total;d+=2.8f)
    {
     var a=BridgeSample296(points,d,out var fa);var b=BridgeSample296(points,Mathf.Min(total,d+2.83f),out var fb);
     if(Skip((a+b)*.5f))continue;
     var ra=Vector3.Cross(Vector3.up,fa).normalized;var rb=Vector3.Cross(Vector3.up,fb).normalized;
     foreach(float side in new[]{-.30f,0,.30f})BridgeTimber296(batch,a+ra*width*side-Vector3.up*.20f,b+rb*width*side-Vector3.up*.20f,.30f);
    }
    for(int i=1;i<stations.Count;i++)
    {
     var a=BridgeSample296(points,stations[i-1],out var fa);var b=BridgeSample296(points,stations[i],out var fb);
     if(Skip((a+b)*.5f,.5f))continue;
     float clearance=Mathf.Min(a.y-field.Sample(a.x,a.z),b.y-field.Sample(b.x,b.z));if(clearance<2.4f)continue;
     float drop=Mathf.Min(4.5f,clearance-.6f);
     var ra=Vector3.Cross(Vector3.up,fa).normalized;var rb=Vector3.Cross(Vector3.up,fb).normalized;
     foreach(float side in new[]{-.32f,.32f})
     {
      BridgeTimber296(batch,a+ra*width*side-Vector3.up*.7f,b+rb*width*side-Vector3.up*drop,.20f);
      BridgeTimber296(batch,a+ra*width*side-Vector3.up*drop,b+rb*width*side-Vector3.up*.7f,.20f);
     }
    }
   }
   else
   {
    // Retain the deck's grade, while providing actual arched masonry depth
    // below it. Source stones retain scale/UVs; only boundary faces are cut.
    for(int bay=1;bay<stations.Count;bay++)
    {
     // Seat the vault inside each two-metre pier, leaving .3 m overlap for
     // the curve's changing tangent instead of an exposed butt joint.
     float a=stations[bay-1]+.7f,b=stations[bay]-.7f;if(b<=a)continue;
     var middle=BridgeSample296(points,(a+b)*.5f,out _);
     float clearance=middle.y-field.Sample(middle.x,middle.z);
     if(Skip(middle,.5f)||clearance<1.1f)continue;
     float rise=Mathf.Min((b-a)*.34f,Mathf.Max(.65f,clearance-.75f));
     int segments=Mathf.Max(8,Mathf.CeilToInt((b-a)/.65f));
     for(int j=0;j<segments;j++)
     {
      float t0=j/(float)segments,t1=(j+1f)/segments;
      var p0=BridgeSample296(points,Mathf.Lerp(a,b,t0),out var f0);var p1=BridgeSample296(points,Mathf.Lerp(a,b,t1),out var f1);
      var r0=Vector3.Cross(Vector3.up,f0).normalized;var r1=Vector3.Cross(Vector3.up,f1).normalized;
      float low0=p0.y-.76f-rise*(1-Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow(2*t0-1,2))));
      float low1=p1.y-.76f-rise*(1-Mathf.Sqrt(Mathf.Max(0,1-Mathf.Pow(2*t1-1,2))));
      float across=width-.46f;
      foreach(float sign in new[]{-1f,1f})
      {
       var q0=p0+r0*sign*across*.5f;var q1=p1+r1*sign*across*.5f;
       if(sign<0)batch.MasonryFaceStrip(q0,q1,low0,low1,p0.y-.08f,p1.y-.08f);
       else batch.MasonryFaceStrip(q1,q0,low1,low0,p1.y-.08f,p0.y-.08f);
      }
      // The underside closes the arch vault; it never creates a top-facing
      // platform. Two strips preserve the masonry source's metric scale.
      var lowA=new Vector3(p0.x,low0,p0.z);var lowB=new Vector3(p1.x,low1,p1.z);
      var tangent=(lowB-lowA).normalized;float run=Vector3.Distance(lowA,lowB);
      var right=Vector3.Cross(Vector3.up,(f0+f1).normalized).normalized;
      for(int strip=0;strip<2;strip++)
       batch.MasonryPanel(lowA+right*((strip+.5f)*across/2-across*.5f),right,tangent,across/2+.025f,run+.015f);
     }
    }
   }
  }
  static void BridgeTimber296(CrossingBatch296 batch,Vector3 a,Vector3 b,float diameter)
  {
   var source=CrossingSource(TimberColumn296);float scale=diameter/Mathf.Max(source.Bounds.size.x,source.Bounds.size.z);
   float native=source.Bounds.size.y*scale,length=Vector3.Distance(a,b);if(length<.08f)return;
   var direction=(b-a)/length;var rotation=Quaternion.FromToRotation(Vector3.up,direction);
   for(float d=0;d<length;d+=native*.97f)
    batch.Wall(TimberColumn296,a+direction*d,rotation,scale,diameter+.01f,Mathf.Min(native,length-d),null,batch);
  }
  static void BridgeMasonryPier296(CrossingBatch296 batch,Vector3 p,Vector3 r,Vector3 f,float width,float depth,float bottom,float top)
  {
   var corners=new[]{p-r*width*.5f-f*depth*.5f,p+r*width*.5f-f*depth*.5f,p+r*width*.5f+f*depth*.5f,p-r*width*.5f+f*depth*.5f};
   for(int i=0;i<4;i++)batch.MasonryFaceStrip(corners[(i+1)%4],corners[i],bottom,bottom,top,top);
  }
  static Material CrossingBridgeStructureMaterial296(Material source)
  {
   string path=AssetDatabase.GetAssetPath(source);
   if(!path.StartsWith("Assets/HwaseongForteressGate/Materials/",StringComparison.Ordinal))return CrossingMaterial296(source);
   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
   var material=Asset296("Materials/Crossings/BridgeStructure_"+guid+"_"+localId+".mat",()=>new Material(CrossingMaterial296(source)));
   material.CopyPropertiesFromMaterial(CrossingMaterial296(source));
   material.SetColor("_BaseColor",new Color(.57f,.555f,.515f,1));material.SetFloat("_LightResponse",.52f);material.SetFloat("_Saturation",.22f);
   material.name=source.name+"_Architecture296_BridgeStructure";EditorUtility.SetDirty(material);return material;
  }
  sealed partial class CrossingBatch296
  {
   // Flat source stone faces assembled at fixed .65 scale. Clipping preserves
   // original UV interpolation, while avoiding the tapered wall-module holes.
   public void MasonryFaceStrip(Vector3 from,Vector3 to,float bottom0,float bottom1,float top0,float top1)
   {
    var direction=Vector3.ProjectOnPlane(to-from,Vector3.up);float length=direction.magnitude;if(length<.005f)return;direction/=length;
    float nativeWidth=CrossingSource(Rampart296).Bounds.size.x*.65f,course=CrossingSource(Rampart296).Bounds.size.y*.65f;
    int count=Mathf.Max(1,Mathf.CeilToInt(length/(nativeWidth-.04f)));
    for(int tile=0;tile<count;tile++)
    {
     float t0=tile/(float)count,t1=(tile+1f)/count,w=length/count+.015f;
     float lower0=Mathf.Lerp(bottom0,bottom1,t0),lower1=Mathf.Lerp(bottom0,bottom1,t1);
     float upper0=Mathf.Lerp(top0,top1,t0),upper1=Mathf.Lerp(top0,top1,t1);
     var centre=Vector3.Lerp(from,to,(t0+t1)*.5f);
     float start=Mathf.Floor(Mathf.Min(lower0,lower1)/course)*course;
     for(float y=start;y<Mathf.Max(upper0,upper1);y+=course-.006f)
      MasonryPanel(new Vector3(centre.x,y,centre.z),direction,Vector3.up,w,course,
       p=>p.y+y-Mathf.Lerp(lower0,lower1,(p.x+w*.5f)/w),p=>Mathf.Lerp(upper0,upper1,(p.x+w*.5f)/w)-p.y-y);
    }
   }
   public void MasonryPanel(Vector3 bottom,Vector3 horizontal,Vector3 vertical,float width,float height,
    Func<Vector3,float> lower=null,Func<Vector3,float> upper=null)
   {
    var data=CrossingSource(Rampart296);Instances++;
    var normalize=Matrix4x4.Scale(Vector3.one*.65f)*Matrix4x4.Translate(-new Vector3(data.Bounds.center.x,data.Bounds.min.y,data.Bounds.center.z));
    for(int index=0;index<data.Parts.Count;index++)for(int level=0;level<3;level++)
    {
     var part=CrossingPart296(Rampart296,index,level);var material=data.Parts[index].material;
     var clipped=CropWall296(part.mesh,part.submesh,normalize*part.matrix,width,height);temporary.Add(clipped);
     var face=CrossingMasonryFacing296(clipped);temporary.Add(face);if(face.vertexCount==0)continue;
     if(lower!=null||upper!=null){var bounded=ClipBridgeMasonryFace296(face,lower,upper);temporary.Add(bounded);face=bounded;if(face.vertexCount==0)continue;}
     face.vertices=face.vertices.Select(p=>bottom+horizontal*p.x+vertical*p.y).ToArray();face.RecalculateNormals();face.RecalculateTangents();face.RecalculateBounds();
     if(!parts[level].TryGetValue(material,out var list)){list=new List<CombineInstance>();parts[level][material]=list;}
     list.Add(new CombineInstance{mesh=face,subMeshIndex=0,transform=Matrix4x4.identity});
    }
   }
  }
  static Mesh ClipBridgeMasonryFace296(Mesh mesh,Func<Vector3,float> lower,Func<Vector3,float> upper)
  {
   var vertices=mesh.vertices;var normals=mesh.normals;var uvs=mesh.uv;var input=mesh.triangles;
   var output=new List<Vector3>();var uv=new List<Vector2>();var normal=new List<Vector3>();var triangles=new List<int>();
   List<WallVertex296> Clip(List<WallVertex296> polygon,Func<Vector3,float> plane)
   {
    if(plane==null||polygon.Count==0)return polygon;var result=new List<WallVertex296>();var a=polygon[polygon.Count-1];float da=plane(a.P);
    foreach(var b in polygon){float db=plane(b.P);if((da>=0)!=(db>=0))result.Add(WallVertex296.Lerp(a,b,da/(da-db)));if(db>=0)result.Add(b);a=b;da=db;}return result;
   }
   for(int i=0;i<input.Length;i+=3)
   {
    var polygon=new List<WallVertex296>();for(int j=0;j<3;j++){int k=input[i+j];polygon.Add(new WallVertex296{P=vertices[k],N=normals[k],UV=uvs[k]});}
    polygon=Clip(Clip(polygon,lower),upper);if(polygon.Count<3)continue;
    int first=output.Count;foreach(var p in polygon){output.Add(p.P);normal.Add(p.N);uv.Add(p.UV);}for(int j=1;j<polygon.Count-1;j++)triangles.AddRange(new[]{first,first+j,first+j+1});
   }
   var resultMesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};resultMesh.SetVertices(output);resultMesh.SetNormals(normal);resultMesh.SetUVs(0,uv);resultMesh.SetTriangles(triangles,0);resultMesh.RecalculateBounds();return resultMesh;
  }
 }
}
