using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  sealed class DeckFootprint295
  {
   public Vector3 A,B;public Vector3[] Quad;public Bounds Bounds;public float Orientation;
   public bool LowOver(Vector3 water)
   {
    var delta=new Vector2(B.x-A.x,B.z-A.z);float t=delta.sqrMagnitude>.00001f?Mathf.Clamp01(Vector2.Dot(new Vector2(water.x-A.x,water.z-A.z),delta)/delta.sqrMagnitude):0;
    float rise=Mathf.Lerp(A.y,B.y,t)-water.y;
    return rise>=-.50f&&rise<=2.5f;
   }
  }
  static Dictionary<Vector2Int,List<DeckFootprint295>> BakeDeckFootprints295(List<GameObject> objects,List<Mesh> meshes,List<string> evidence)
  {
   var cells=new Dictionary<Vector2Int,List<DeckFootprint295>>();int segments=0,rails=0;
   var crossings=JsonUtility.FromJson<Crossings295>(File.ReadAllText(G295+"/crossings.json")).Crossings;
   foreach(var crossing in crossings.Where(c=>c.Kind!="Mum"&&c.Kind!="AbilityGate"))
   {
    var points=crossing.Points!=null&&crossing.Points.Length>1?crossing.Points:new[]{crossing.Start,crossing.End};
    float width=Mathf.Clamp(crossing.RouteWidth,3.2f,8);var right=new Vector3[points.Length];var build=new BridgeMesh295();
    for(int i=0;i<points.Length;i++){var direction=points[Math.Min(points.Length-1,i+1)]-points[Math.Max(0,i-1)];direction.y=0;right[i]=Vector3.Cross(Vector3.up,direction.normalized);}
    for(int i=1;i<points.Length;i++)
    {
     var a=points[i-1]+Vector3.up*.025f;var b=points[i]+Vector3.up*.025f;if(Vector3.Distance(a,b)<.001f)continue;
     var r0=right[i-1];var r1=right[i];
     foreach(int sign in new[]{-1,1})
      build.Ribbon(a+r0*(width*.5f-.14f)*sign+Vector3.up*2.5f,b+r1*(width*.5f-.14f)*sign+Vector3.up*2.5f,r0,r1,.5f,2.6f);
     // The clipping quad stays inside the physical deck by 0.1m. It changes
     // only temporary water bake sources, never runtime water or collision.
     float half=width*.5f-.1f;var q=new[]{a-r0*half,b-r1*half,b+r1*half,a+r0*half};
     var bounds=new Bounds(q[0],Vector3.zero);foreach(var vertex in q)bounds.Encapsulate(vertex);
     // Local coordinates avoid cancellation of large world-coordinate products
     // on the final centimetre-length approach segments.
     float area=0;for(int k=0;k<4;k++){var u=q[k]-q[0];var v=q[(k+1)%4]-q[0];area+=u.x*v.z-v.x*u.z;}
     if(Mathf.Abs(area)<.00001f)continue;
     var footprint=new DeckFootprint295{A=a,B=b,Quad=q,Bounds=bounds,Orientation=Mathf.Sign(area)};segments++;
     for(int z=Mathf.FloorToInt(bounds.min.z/32);z<=Mathf.FloorToInt(bounds.max.z/32);z++)
      for(int x=Mathf.FloorToInt(bounds.min.x/32);x<=Mathf.FloorToInt(bounds.max.x/32);x++)
      {var key=new Vector2Int(x,z);if(!cells.TryGetValue(key,out var list))cells[key]=list=new List<DeckFootprint295>();list.Add(footprint);}
    }
    var mesh=new Mesh{name="RailBoundary_BakeOnly_"+crossing.Id,hideFlags=HideFlags.HideAndDontSave};build.Apply(mesh);meshes.Add(mesh);
    var go=new GameObject("RailBoundary_BakeOnly_"+crossing.Id);objects.Add(go);
    go.AddComponent<MeshCollider>().sharedMesh=mesh;var modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=1;rails++;
   }
   var field=new Oheangbu.Data.World.CompactWorldSurface(Session292().MountainLayout);int fences=0;
   foreach(var source in Components295<BoxCollider>().Where(c=>c.enabled&&c.gameObject.activeInHierarchy&&c.name=="YardFenceRail"&&c.transform.root.name=="Village245"))
   {
    var center=source.transform.TransformPoint(source.center);center.y=field.Sample(center.x,center.z)+1.25f;
    var go=new GameObject("FenceBoundary_BakeOnly_"+fences++);objects.Add(go);go.transform.SetPositionAndRotation(center,source.transform.rotation);
    var scale=source.transform.lossyScale;go.AddComponent<BoxCollider>().size=new Vector3(Mathf.Max(.4f,Mathf.Abs(source.size.x*scale.x)),2.5f,Mathf.Max(.4f,Mathf.Abs(source.size.z*scale.z)));
    var modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=1;
   }
   evidence.Add("INFO bake-only physical rail boundaries="+rails+"; deck footprint segments="+segments+"; thickness0.5m height2.5m, removed before scene save");
   evidence.Add("INFO bake-only existing village fence boundaries="+fences+"; exact sourceBoxCollider horizontal span, minimum thickness0.4m and height2.5m; removed before scene save");return cells;
  }
  static float HalfPlane295(Vector3 a,Vector3 b,Vector3 p,float orientation)=>(b.x-a.x)*(p.z-a.z)*orientation-(b.z-a.z)*(p.x-a.x)*orientation;
  static List<Vector3> ClipHalfPlane295(List<Vector3> source,Vector3 a,Vector3 b,float orientation,bool inside)
  {
   var result=new List<Vector3>();if(source.Count==0)return result;
   Vector3 previous=source.Last();float previousDistance=HalfPlane295(a,b,previous,orientation)*(inside?1:-1);bool previousKeep=previousDistance>=0;
   foreach(var current in source)
   {
    float currentDistance=HalfPlane295(a,b,current,orientation)*(inside?1:-1);bool currentKeep=currentDistance>=0;
    if(currentKeep!=previousKeep)
    {float divisor=previousDistance-currentDistance;float t=Mathf.Abs(divisor)>.000001f?previousDistance/divisor:0;result.Add(Vector3.Lerp(previous,current,t));}
    if(currentKeep)result.Add(current);previous=current;previousDistance=currentDistance;previousKeep=currentKeep;
   }
   return result;
  }
  static List<List<Vector3>> SubtractDeck295(List<Vector3> polygon,DeckFootprint295 deck)
  {
   var result=new List<List<Vector3>>();var remaining=polygon;
   for(int i=0;i<4&&remaining.Count>=3;i++)
   {
    var a=deck.Quad[i];var b=deck.Quad[(i+1)%4];var outside=ClipHalfPlane295(remaining,a,b,deck.Orientation,false);
    if(outside.Count>=3)result.Add(outside);remaining=ClipHalfPlane295(remaining,a,b,deck.Orientation,true);
   }
   return result;
  }
  static Mesh WaterBakeMesh295(MeshFilter filter,Dictionary<Vector2Int,List<DeckFootprint295>> cells,List<Mesh> owned,out int clipped)
  {
   var source=filter.sharedMesh;var vertices=source.vertices.Select(filter.transform.TransformPoint).ToArray();var triangles=source.triangles;
   var output=new List<Vector3>();var indices=new List<int>();clipped=0;
   for(int t=0;t<triangles.Length;t+=3)
   {
    var a=vertices[triangles[t]];var b=vertices[triangles[t+1]];var c=vertices[triangles[t+2]];var centre=(a+b+c)/3;
    var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);var candidates=new HashSet<DeckFootprint295>();
    for(int z=Mathf.FloorToInt(bounds.min.z/32);z<=Mathf.FloorToInt(bounds.max.z/32);z++)
     for(int x=Mathf.FloorToInt(bounds.min.x/32);x<=Mathf.FloorToInt(bounds.max.x/32);x++)
      if(cells.TryGetValue(new Vector2Int(x,z),out var list))foreach(var deck in list)
       if(bounds.min.x<=deck.Bounds.max.x&&bounds.max.x>=deck.Bounds.min.x&&bounds.min.z<=deck.Bounds.max.z&&bounds.max.z>=deck.Bounds.min.z&&deck.LowOver(centre))candidates.Add(deck);
    var pieces=new List<List<Vector3>>{new List<Vector3>{a,b,c}};
    foreach(var deck in candidates)
    {
     var next=new List<List<Vector3>>();foreach(var piece in pieces)next.AddRange(SubtractDeck295(piece,deck));pieces=next;if(pieces.Count==0)break;
    }
    float areaBefore=Vector3.Cross(b-a,c-a).magnitude*.5f,areaAfter=0;
    foreach(var piece in pieces)for(int i=1;i<piece.Count-1;i++)
    {
     if(Vector3.Cross(piece[i]-piece[0],piece[i+1]-piece[0]).sqrMagnitude<.00000001f)continue;
     areaAfter+=Vector3.Cross(piece[i]-piece[0],piece[i+1]-piece[0]).magnitude*.5f;
     int index=output.Count;output.Add(piece[0]);output.Add(piece[i]);output.Add(piece[i+1]);indices.Add(index);indices.Add(index+1);indices.Add(index+2);
    }
    if(areaAfter<areaBefore-.0001f)clipped++;
   }
   var mesh=new Mesh{name="WaterSource_BakeOnly_"+source.name,hideFlags=HideFlags.HideAndDontSave,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
   mesh.SetVertices(output);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();owned.Add(mesh);return mesh;
  }
 }
}
