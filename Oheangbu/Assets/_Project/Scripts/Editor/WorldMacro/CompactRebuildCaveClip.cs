using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
 static float CaveAir(Vector3 world,CaveLayoutData data){
 var p=world-data.origin;float result=100000;
 float Smooth(float a,float b,float k){float h=Mathf.Max(k-Mathf.Abs(a-b),0)/k;return Mathf.Min(a,b)-h*h*k*.25f;}
 void Route(Vector3[] points,float radius){for(int i=1;i<points.Length;i++){var a=points[i-1]-data.origin;var b=points[i]-data.origin;var d=new Vector2(b.x-a.x,b.z-a.z);float t=Mathf.Clamp01(Vector2.Dot(new Vector2(p.x-a.x,p.z-a.z),d)/d.sqrMagnitude);float dx=p.x-a.x-t*d.x,dz=p.z-a.z-t*d.y;float value=Mathf.Sqrt(dx*dx+dz*dz+Mathf.Pow((p.y-2)*.91f,2))-radius;result=Smooth(result,value,.7f);}}
 Route(data.mouth,8f);Route(data.main,4.8f);Route(data.branch,3.8f);Route(data.recess,4.2f);
 foreach(var c in new[]{new Vector3(3518,1800,6.3f),new Vector3(3549,1778,5.8f),new Vector3(3540,1837,5),new Vector3(3495,1775,5.2f)}){
 float value=Mathf.Sqrt(Mathf.Pow(world.x-c.x,2)+Mathf.Pow(world.z-c.y,2)+Mathf.Pow((p.y-2.4f)*.85f,2))-c.z;result=Smooth(result,value,.8f);}
 result+=.36f*Mathf.Sin(p.x*.27f+p.z*.31f)*Mathf.Cos(p.y*.81f-p.z*.17f)+.15f*Mathf.Sin(p.y*2.1f+p.x*.7f)+.10f*Mathf.Sin(p.x*1.3f-p.z*.8f);
 // Keep the exterior approach beyond the open mouth and the supporting floor.
 return Mathf.Max(result,Mathf.Max(.08f-p.y,world.z-1890f));
 }
 static void ClipOldPortalToGallery(Transform mine,CaveLayoutData data,string folder){
 int index=0;
 var filters=mine.GetComponentsInChildren<MeshFilter>(false);
 foreach(var filter in filters.Where(f=>f.name.StartsWith("Solid_Mountain_Portal_")).Concat(filters.Where(f=>f.name=="Mountain_Rock_Transition"||f.name=="Natural_Mine_Broad_Apron")).ToArray()){
 string stem=folder+"/Portal_"+index++;var source=AssetDatabase.LoadAssetAtPath<Mesh>(stem+"_Source.asset");
 if(source==null){source=Object.Instantiate(filter.sharedMesh);AssetDatabase.CreateAsset(source,stem+"_Source.asset");}
 var old=source.vertices;var vertices=new List<Vector3>();var triangles=new List<int>();
 void Emit(Vector3 a,Vector3 b,Vector3 c){int start=vertices.Count;vertices.Add(filter.transform.InverseTransformPoint(a));vertices.Add(filter.transform.InverseTransformPoint(b));vertices.Add(filter.transform.InverseTransformPoint(c));triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);}
 void Cut(Vector3 a,Vector3 b,Vector3 c,int depth){
 var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);
 if(bounds.min.y>data.origin.y+11||bounds.max.y<data.origin.y+.08f||bounds.min.z>1890||bounds.max.z<1764||bounds.max.x<3395||bounds.min.x>3563){Emit(a,b,c);return;}
 if(depth<7&&Mathf.Max((a-b).sqrMagnitude,Mathf.Max((b-c).sqrMagnitude,(c-a).sqrMagnitude))>4){
 var ab=(a+b)*.5f;var bc=(b+c)*.5f;var ca=(c+a)*.5f;Cut(a,ab,ca,depth+1);Cut(ab,b,bc,depth+1);Cut(ca,bc,c,depth+1);Cut(ab,bc,ca,depth+1);return;}
 var points=new[]{a,b,c};var polygon=new List<Vector3>();
 for(int i=0;i<3;i++){var v=points[i];var w=points[(i+1)%3];float av=CaveAir(v,data)-.1f,bv=CaveAir(w,data)-.1f;if(av>=0)polygon.Add(v);if((av>=0)!=(bv>=0))polygon.Add(Vector3.Lerp(v,w,av/(av-bv)));}
 for(int i=2;i<polygon.Count;i++)Emit(polygon[0],polygon[i-1],polygon[i]);
 }
 var indices=source.triangles;for(int i=0;i<indices.Length;i+=3)Cut(filter.transform.TransformPoint(old[indices[i]]),filter.transform.TransformPoint(old[indices[i+1]]),filter.transform.TransformPoint(old[indices[i+2]]),0);
 var mesh=new Mesh{name=filter.name+"_CaveV4",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.uv=vertices.Select(v=>new Vector2(v.x,v.z)*.2f).ToArray();mesh.RecalculateTangents();
 var asset=AssetDatabase.LoadAssetAtPath<Mesh>(stem+"_Clipped.asset");if(asset==null){AssetDatabase.CreateAsset(mesh,stem+"_Clipped.asset");asset=mesh;}else{EditorUtility.CopySerialized(mesh,asset);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(asset);}
 filter.sharedMesh=asset;var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=asset;
 }
 }
 }
}
