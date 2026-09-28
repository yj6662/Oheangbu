using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.App.World;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string VehicleRoadId296="jeokro__cheolong";
  const float VehicleRoadStep296=.25f,VehicleRoadWidth296=6f,VehicleRoadGuard296=.2f;
  const float VehicleRoadGrade296=.25f,VehicleRoadFeather296=8f;
  static VehicleRoadPlan296 vehicleRoad296;

  [Serializable] sealed class VehicleRoadRow296
  {
   public float Distance,OriginalY,EnvelopeY;public Vector3 Centre,Right;public bool DryNext;
   public float[] TerrainY,SurfaceY;
  }
  [Serializable] sealed class VehicleRoadPlan296
  {
   public string RouteId=VehicleRoadId296;
   public string Scope="Candidate-only raised roadway. Immutable 295 terrain/water; original vehicle profile and success thresholds unchanged. Actual top vertices, route centre heights and dry-bank foundations use this one surface.";
   public string HeightSha256,WaterSha256,SourceRoutesSha256,SourceCrossingsSha256,SurfaceMeshSha256;
   public float Width=VehicleRoadWidth296,Guard=VehicleRoadGuard296,MaximumEnvelopeGrade=VehicleRoadGrade296,EndFeather=VehicleRoadFeather296;
   public float StartExtension=35,EndExtension,Length,MaximumRise,MaximumDrySupport,MaximumSurfaceDegrees,MaximumCrossGrade,MinimumTerrainClearance;
   public int DryFoundationSegments,WetOpenSegments,PierBoxes,RailBoxes,SurfaceTriangles;
   public Vector3[] Points;public VehicleRoadRow296[] Rows;
   [NonSerialized] public CompactWorldSurface Field;
   [NonSerialized] public WorldTerrainQuery Query;
  }
  [Serializable] sealed class VehicleRoadMesh296
  {public string RouteId,Scope="Identical vertices/triangles assigned to the continuous physical road support.";public Vector3[] Vertices;public int[] Triangles;}

  // Called from Crossings296 before any route mesh is assembled. The input
  // JSON is immutable; recomputing from 295 also makes repeated builds stable.
  static Vector3[] BridgeVehicleApproach296(string routeId,Vector3[] originalPoints,CompactWorldSurface field)
  {
   if(routeId!=VehicleRoadId296)return originalPoints;
   var session=Session292();var layout=session.MountainLayout;
   string sourceHash=CrossingHash296(G295+"/height.bytes");
   if(HashArchitecture296(layout.FinalSurface.bytes)!=sourceHash)
    throw new InvalidOperationException("Vehicle roadway requires the unchanged 295 height field");
   var baseline=JsonUtility.FromJson<Routes292>(File.ReadAllText(G295+"/routes.json")).routes.Single(r=>r.id==routeId).points;
   var arc=new double[baseline.Length];for(int i=1;i<arc.Length;i++)arc[i]=arc[i-1]+Vector2.Distance(XzVehicleRoad296(baseline[i-1]),XzVehicleRoad296(baseline[i]));
   int first=NearestBridgePoint296(baseline,originalPoints[0]),last=NearestBridgePoint296(baseline,originalPoints[originalPoints.Length-1]);
   if(first>=last)throw new InvalidOperationException("Vehicle crossing must retain its approved route direction");
   double begin=arc[first]-35,end=arc[first]+178;
   if(begin<=0||end>=arc[arc.Length-1])throw new InvalidOperationException("Vehicle road extension leaves its source route");
   int count=853;var original=new Vector3[count];
   for(int i=0;i<count;i++)original[i]=SampleVehicleBaseline296(baseline,arc,begin+i*VehicleRoadStep296);
   var query=session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldTerrainQuery>(true)).First();
   var plan=new VehicleRoadPlan296{Field=field,Query=query,Rows=new VehicleRoadRow296[count],Points=new Vector3[count],Length=213,
    EndExtension=(float)(end-arc[last]),HeightSha256=sourceHash,WaterSha256=HashArchitecture296(layout.Hydrology.WaterLevels.bytes),
    SourceRoutesSha256=CrossingHash296(G295+"/routes.json"),SourceCrossingsSha256=CrossingHash296(G295+"/crossings.json")};
   var envelope=new float[count];var travel=new float[count-1];
   const int guardColumns=65;const float guardHalf=3.2f;
   for(int i=0;i<count;i++)
   {
    var tangent=Vector3.ProjectOnPlane(original[Mathf.Min(count-1,i+1)]-original[Mathf.Max(0,i-1)],Vector3.up).normalized;
    var row=new VehicleRoadRow296{Distance=i*VehicleRoadStep296,OriginalY=original[i].y,Centre=original[i],Right=Vector3.Cross(Vector3.up,tangent),TerrainY=new float[25],SurfaceY=new float[25]};
    plan.Rows[i]=row;float ceiling=original[i].y+.035f;
    for(int j=0;j<guardColumns;j++)
    {
     var p=original[i]+row.Right*(-guardHalf+j*.1f);ceiling=Mathf.Max(ceiling,TriangleVehicleRoad296(field,p.x,p.z)+.04f);
    }
    envelope[i]=ceiling;
    if(i>0)
    {
     float shortest=float.MaxValue;var before=plan.Rows[i-1];
     for(int j=0;j<guardColumns;j++)
     {
      float offset=-guardHalf+j*.1f;
      shortest=Mathf.Min(shortest,Vector2.Distance(XzVehicleRoad296(before.Centre+before.Right*offset),XzVehicleRoad296(row.Centre+row.Right*offset)));
     }
     if(shortest<.04f)throw new InvalidOperationException("Vehicle road offset turns back on itself");
     travel[i-1]=shortest;
    }
   }
   // Use each curve's shortest offset distance, including its inside edge.
   // Limiting only the centreline leaves the inner tyre path too steep.
   for(int i=1;i<count;i++)envelope[i]=Mathf.Max(envelope[i],envelope[i-1]-VehicleRoadGrade296*travel[i-1]);
   for(int i=count-2;i>=0;i--)envelope[i]=Mathf.Max(envelope[i],envelope[i+1]-VehicleRoadGrade296*travel[i]);
   for(int i=0;i<count;i++)
   {
    var row=plan.Rows[i];row.EnvelopeY=envelope[i];
    float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(row.Distance,plan.Length-row.Distance)/VehicleRoadFeather296));
    for(int j=0;j<25;j++)
    {
     var p=row.Centre+row.Right*(-3+j*.25f);float ground=TriangleVehicleRoad296(field,p.x,p.z);
     row.TerrainY[j]=ground;row.SurfaceY[j]=Mathf.Lerp(ground+.035f,envelope[i],blend);
     if(!WetVehicleRoad296(plan,p,ground,.15f))plan.MaximumDrySupport=Mathf.Max(plan.MaximumDrySupport,row.SurfaceY[j]-ground);
    }
    row.Centre.y=row.SurfaceY[12];plan.Points[i]=row.Centre;
    plan.MaximumRise=Mathf.Max(plan.MaximumRise,row.Centre.y-row.OriginalY);
   }
   vehicleRoad296=plan;ValidateVehicleRoad296(plan);
   return plan.Points;
  }
  static bool IsBridgeVehicleApproach296(Vector3[] points)=>vehicleRoad296!=null&&ReferenceEquals(points,vehicleRoad296.Points);
  static void UpdateBridgeVehicleMetadata296(Crossing295 crossing)
  {
   if(crossing.RouteId!=VehicleRoadId296||vehicleRoad296==null)return;
   crossing.Start=ProjectVehicleRoad296(vehicleRoad296,crossing.Start);
   crossing.End=ProjectVehicleRoad296(vehicleRoad296,crossing.End);
   crossing.DeckHeight=vehicleRoad296.Points.Min(p=>p.y);
   crossing.Span=Vector3.Distance(crossing.Start,crossing.End);
  }
  static Vector2 XzVehicleRoad296(Vector3 p)=>new Vector2(p.x,p.z);
  static Vector3 SampleVehicleBaseline296(Vector3[] points,double[] arc,double distance)
  {
   int i=Array.BinarySearch(arc,distance);if(i>=0)return points[i];i=~i;
   return Vector3.Lerp(points[i-1],points[i],(float)((distance-arc[i-1])/(arc[i]-arc[i-1])));
  }
  static float TriangleVehicleRoad296(CompactWorldSurface field,float x,float z)
  {
   float x0=Mathf.Floor(x/field.Cell)*field.Cell,z0=Mathf.Floor(z/field.Cell)*field.Cell;
   float u=(x-x0)/field.Cell,v=(z-z0)/field.Cell;
   if(u+v<=1){float a=field.Sample(x0,z0);return a+(field.Sample(x0+field.Cell,z0)-a)*u+(field.Sample(x0,z0+field.Cell)-a)*v;}
   float b=field.Sample(x0+field.Cell,z0+field.Cell);
   return b+(field.Sample(x0,z0+field.Cell)-b)*(1-u)+(field.Sample(x0+field.Cell,z0)-b)*(1-v);
  }
  static bool WetVehicleRoad296(VehicleRoadPlan296 plan,Vector3 p,float ground,float margin)
   =>plan.Query.TryWaterHeight(p,out float water)&&ground<water+margin;
  static Vector3 PointVehicleRoad296(VehicleRoadPlan296 plan,float distance,float across)
  {
   float q=Mathf.Clamp(distance/VehicleRoadStep296,0,plan.Rows.Length-1);int i=Mathf.Min((int)q,plan.Rows.Length-2);float t=q-i;
   var a=plan.Rows[i];var b=plan.Rows[i+1];
   float col=Mathf.Clamp((across+3)/.25f,0,24);int j=Mathf.Min((int)col,23);float u=col-j;
   // Same diagonal and barycentric interpolation as the actual physical grid.
   // Cell corners are (a,j),(b,j),(a,j+1),(b,j+1).
   Vector3 Corner(VehicleRoadRow296 row,int k){var p=row.Centre+row.Right*(-3+k*.25f);p.y=row.SurfaceY[k];return p;}
   var al=Corner(a,j);var ar=Corner(a,j+1);var bl=Corner(b,j);var br=Corner(b,j+1);
   var p=t>=u?al+(bl-al)*t+(br-bl)*u:al+(br-ar)*t+(ar-al)*u;
   p+=Vector3.Lerp(a.Right,b.Right,t)*(across-Mathf.Clamp(across,-3,3));
   return p;
  }
  static Vector3 ProjectVehicleRoad296(VehicleRoadPlan296 plan,Vector3 p)
  {
   float best=float.MaxValue,distance=0,across=0;
   for(int i=1;i<plan.Points.Length;i++)
   {
    var a=XzVehicleRoad296(plan.Points[i-1]);var delta=XzVehicleRoad296(plan.Points[i])-a;
    float t=Mathf.Clamp01(Vector2.Dot(XzVehicleRoad296(p)-a,delta)/Mathf.Max(1e-6f,delta.sqrMagnitude));
    var q=a+delta*t;float error=(q-XzVehicleRoad296(p)).sqrMagnitude;if(error>=best)continue;
    best=error;distance=(i-1+t)*VehicleRoadStep296;var right=Vector3.Lerp(plan.Rows[i-1].Right,plan.Rows[i].Right,t).normalized;
    across=Vector3.Dot(p-new Vector3(q.x,p.y,q.y),right);
   }
   return PointVehicleRoad296(plan,distance,across);
  }
  static void VehicleRoadGrid296(VehicleRoadPlan296 plan,out Vector3[] vertices,out int[] triangles)
  {
   vertices=new Vector3[plan.Rows.Length*25];triangles=new int[(plan.Rows.Length-1)*24*6];int at=0;
   for(int i=0;i<plan.Rows.Length;i++)for(int j=0;j<25;j++)
   {var row=plan.Rows[i];var p=row.Centre+row.Right*(-3+j*.25f);p.y=row.SurfaceY[j];vertices[i*25+j]=p;}
   for(int i=0;i<plan.Rows.Length-1;i++)for(int j=0;j<24;j++)
   {int a=i*25+j,b=a+25;triangles[at++]=a;triangles[at++]=b;triangles[at++]=b+1;triangles[at++]=a;triangles[at++]=b+1;triangles[at++]=a+1;}
  }
  static void ValidateVehicleRoad296(VehicleRoadPlan296 plan)
  {
   VehicleRoadGrid296(plan,out var v,out var t);plan.SurfaceTriangles=t.Length/3;plan.MinimumTerrainClearance=float.MaxValue;
   for(int i=0;i<t.Length;i+=3)
   {
    var a=v[t[i]];var b=v[t[i+1]];var c=v[t[i+2]];var normal=Vector3.Cross(b-a,c-a).normalized;
    if(normal.y<=0)throw new InvalidOperationException("Vehicle road reversed/degenerate surface triangle");
    plan.MaximumSurfaceDegrees=Mathf.Max(plan.MaximumSurfaceDegrees,Vector3.Angle(normal,Vector3.up));
    foreach(var p in new[]{a,(a+b+c)/3})plan.MinimumTerrainClearance=Mathf.Min(plan.MinimumTerrainClearance,p.y-TriangleVehicleRoad296(plan.Field,p.x,p.z));
   }
   foreach(var row in plan.Rows)for(int j=1;j<25;j++)plan.MaximumCrossGrade=Mathf.Max(plan.MaximumCrossGrade,Mathf.Abs(row.SurfaceY[j]-row.SurfaceY[j-1])/.25f);
   if(plan.MaximumSurfaceDegrees>15||plan.MaximumCrossGrade>.1f||plan.MinimumTerrainClearance<.005f)
    throw new InvalidOperationException($"Vehicle road geometric preflight failed: slope={plan.MaximumSurfaceDegrees:F4}, cross={plan.MaximumCrossGrade:F4}, terrain clearance={plan.MinimumTerrainClearance:F4}");
   var bytes=new List<byte>(v.Length*12+t.Length*4);foreach(var p in v){bytes.AddRange(BitConverter.GetBytes(p.x));bytes.AddRange(BitConverter.GetBytes(p.y));bytes.AddRange(BitConverter.GetBytes(p.z));}foreach(int i in t)bytes.AddRange(BitConverter.GetBytes(i));
   plan.SurfaceMeshSha256=HashArchitecture296(bytes.ToArray());
  }

  // Functional upper surface and founded dry-bank masonry. The shared bridge
  // helper adds its timber/stone load-bearing artwork after this returns.
  static Renderer[] BuildVehicleApproach296(Transform root,string id,Vector3[] points,float width,CompactWorldSurface field,CrossingBatch296 deck,CrossingBatch296 detail)
  {
   if(!IsBridgeVehicleApproach296(points)||Mathf.Abs(width-VehicleRoadWidth296)>.001f)throw new InvalidOperationException("Vehicle road plan mismatch");
   var plan=vehicleRoad296;VehicleRoadGrid296(plan,out var vertices,out var triangles);
   // Physics-only support assigns no UVs. Reset the old Ribbon layout too;
   // retaining unassigned channels serializes stale bytes on repeated builds.
   var mesh=Asset296("Meshes/Crossings/"+id+"_support.asset",()=>new Mesh());mesh.Clear(false);mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var support=new GameObject(id+"_continuous_support").AddComponent<MeshCollider>();support.transform.SetParent(root,false);support.sharedMesh=mesh;support.gameObject.isStatic=true;
   var masonry=new CrossingBatch296();var dry=new bool[plan.Rows.Length-1];
   for(int i=0;i<dry.Length;i++)
   {
    dry[i]=true;
    for(int j=0;j<25&&dry[i];j++)foreach(int row in new[]{i,i+1})
    {var p=PointVehicleRoad296(plan,row*VehicleRoadStep296,-3+j*.25f);if(WetVehicleRoad296(plan,p,plan.Rows[row].TerrainY[j],.2f)){dry[i]=false;break;}}
    plan.Rows[i].DryNext=dry[i];if(dry[i])plan.DryFoundationSegments++;else plan.WetOpenSegments++;
   }
   // A source paving tile is bent onto the same recorded top field. Its
   // original UVs are retained; only this candidate's derived vertices move.
   for(float d=0;d<plan.Length;)
   {
    int index=Mathf.Min((int)(d/VehicleRoadStep296),dry.Length-1);bool stone=dry[index];string floor=stone?StoneFloor296:TimberFloor296;
    var source=CrossingSource(floor);int columns=stone?6:2;float tileWidth=width/columns,scale=tileWidth/source.Bounds.size.x;
    float length=source.Bounds.size.z*scale;float end=Mathf.Min(plan.Length,d+length*.98f);float midpoint=(d+end)*.5f;
    for(int column=0;column<columns;column++)
    {
     float across=-width*.5f+(column+.5f)*tileWidth;
     var matrix=Matrix4x4.Scale(Vector3.one*scale)*Matrix4x4.Translate(-new Vector3(source.Bounds.center.x,source.Bounds.max.y,source.Bounds.center.z));
     float longitudinal=(end-d)/length;
     deck.WarpSurface(floor,matrix,p=>{var result=PointVehicleRoad296(plan,midpoint+p.z*longitudinal,across+p.x);result.y+=p.y;return result;});
    }
    d=end;
   }
   BuildVehicleRoadFoundations296(root,id,plan,dry,masonry);
   BuildVehicleRoadBoxes296(root,plan,width);
   Directory.CreateDirectory(O296+"/BridgeApproach");
   File.WriteAllText(O296+"/BridgeApproach/jeokro__cheolong.json",JsonUtility.ToJson(plan,true));
   File.WriteAllText(O296+"/BridgeApproach/jeokro__cheolong-mesh.json",JsonUtility.ToJson(new VehicleRoadMesh296{RouteId=VehicleRoadId296,Vertices=vertices,Triangles=triangles},true));
   return masonry.Finish(root,id+"_vehicle_aprons",false);
  }
  static void BuildVehicleRoadFoundations296(Transform root,string id,VehicleRoadPlan296 plan,bool[] dry,CrossingBatch296 masonry)
  {
   var foundation=new BridgeMesh295();var source=CrossingSource(Rampart296);const float scale=.65f;
   float course=source.Bounds.size.y*scale*.8f;const float slab=.14f;
   for(int i=0;i<dry.Length;i++)if(dry[i])
   {
    var a=plan.Rows[i];var b=plan.Rows[i+1];
    var al=PointVehicleRoad296(plan,a.Distance,-3);var ar=PointVehicleRoad296(plan,a.Distance,3);
    var bl=PointVehicleRoad296(plan,b.Distance,-3);var br=PointVehicleRoad296(plan,b.Distance,3);
    al.y=ar.y=a.SurfaceY.Min()-slab;bl.y=br.y=b.SurfaceY.Min()-slab;
    float bottom=Mathf.Min(a.TerrainY.Min(),b.TerrainY.Min())-.16f;
    float depth=Mathf.Max(.01f,Mathf.Max(al.y,bl.y)-bottom);
    foundation.Surface(al,ar,bl,br,depth);
   }
   var mesh=Asset296("Meshes/Crossings/"+id+"_dry_foundations.asset",()=>new Mesh());foundation.Apply(mesh);EditorUtility.SetDirty(mesh);
   var collider=new GameObject(id+"_dry_founded_abutments").AddComponent<MeshCollider>();collider.transform.SetParent(root,false);collider.sharedMesh=mesh;collider.gameObject.isStatic=true;
   // Source masonry face courses follow both outside edges down to actual
   // dry terrain. Skip every section that touches the visible water footprint.
   for(float d=0;d<plan.Length;d+=1)
   {
    float to=Mathf.Min(plan.Length,d+1);int first=(int)(d/VehicleRoadStep296),last=Mathf.Min(dry.Length-1,(int)(to/VehicleRoadStep296));
    if(Enumerable.Range(first,last-first+1).Any(i=>!dry[i]))continue;
    foreach(float side in new[]{-1f,1f})
    {
     float mid=(d+to)*.5f;var edge=PointVehicleRoad296(plan,mid,side*3);var ahead=PointVehicleRoad296(plan,Mathf.Min(plan.Length,mid+.1f),0)-PointVehicleRoad296(plan,Mathf.Max(0,mid-.1f),0);ahead.y=0;ahead.Normalize();
     var outward=Vector3.Cross(Vector3.up,ahead)*side;var rotation=Quaternion.LookRotation(outward);float bottom=float.MaxValue,top=float.MinValue;
     foreach(float station in new[]{d,mid,to}){var p=PointVehicleRoad296(plan,station,side*3);bottom=Mathf.Min(bottom,TriangleVehicleRoad296(plan.Field,p.x,p.z)-.16f);top=Mathf.Max(top,p.y-slab);}
     for(float y=bottom;y<top;y+=course-.025f)
     {
      var basePoint=new Vector3(edge.x,y,edge.z);
      masonry.MasonryFacing(Rampart296,basePoint,rotation,scale,to-d+.06f,Mathf.Min(course,top-y),p=>
      {
       float along=Vector3.Dot(p-basePoint,ahead);var target=PointVehicleRoad296(plan,Mathf.Clamp(mid+along,0,plan.Length),side*3);
       var result=target+outward*.012f;result.y=Mathf.Min(p.y,target.y-slab);return result;
      });
     }
    }
   }
   // Close the two abutment ends at the actual dry/wet transitions. These
   // faces remain on dry ground; a foundation is never extruded over water.
   for(int i=0;i<dry.Length;i++)if(dry[i])foreach(bool start in new[]{true,false})
   {
    if(start?(i>0&&dry[i-1]):(i<dry.Length-1&&dry[i+1]))continue;
    float station=(i+(start?0:1))*VehicleRoadStep296;var centre=PointVehicleRoad296(plan,station,0);
    var before=PointVehicleRoad296(plan,Mathf.Max(0,station-.1f),0);var after=PointVehicleRoad296(plan,Mathf.Min(plan.Length,station+.1f),0);
    var forward=Vector3.ProjectOnPlane(after-before,Vector3.up).normalized;var right=Vector3.Cross(Vector3.up,forward);var outward=forward*(start?-1:1);
    float bottom=float.MaxValue,top=float.MinValue;
    foreach(float across in new[]{-3f,-1.5f,0,1.5f,3f}){var p=PointVehicleRoad296(plan,station,across);bottom=Mathf.Min(bottom,TriangleVehicleRoad296(plan.Field,p.x,p.z)-.16f);top=Mathf.Max(top,p.y-slab);}
    for(float y=bottom;y<top;y+=course-.025f)foreach(float offset in new[]{-1.5f,1.5f})
    {
     var basePoint=new Vector3(centre.x,y,centre.z)+right*offset;
     masonry.MasonryFacing(Rampart296,basePoint,Quaternion.LookRotation(outward),scale,3.04f,Mathf.Min(course,top-y),p=>
     {
      float across=Mathf.Clamp(offset+Vector3.Dot(p-basePoint,right),-3,3);var surface=PointVehicleRoad296(plan,station,across);var result=surface+outward*.006f;result.y=Mathf.Min(p.y,surface.y-slab);return result;
     });
    }
   }
  }
  static void BuildVehicleRoadBoxes296(Transform root,VehicleRoadPlan296 plan,float width)
  {
   var points=plan.Points;float total=BridgeLength296(points);var rail=CrossingSource(TimberBeam296);
   float scale=Mathf.Min(.2f/Mathf.Max(.01f,rail.Bounds.size.y),4f/rail.Bounds.size.x),length=Mathf.Max(.25f,rail.Bounds.size.x*scale);
   for(float d=4+length*.5f;d<total-4;d+=length*.98f)
   {
    var p=BridgeSample296(points,d,out var forward);var right=Vector3.Cross(Vector3.up,forward).normalized;var rotation=Quaternion.LookRotation(forward,Vector3.up);
    foreach(float side in new[]{-1f,1f})
    {
     var edge=p+right*side*(width*.5f+.12f);SourceBox296(root,"RailCollision",TimberBeam296,edge+Vector3.up*.76f,rotation*Quaternion.Euler(0,90,0),scale);plan.RailBoxes++;
     SourceBox296(root,"RailPostCollision",TimberColumn296,edge-forward*length*.49f,Quaternion.identity,.95f/CrossingSource(TimberColumn296).Bounds.size.y);plan.RailBoxes++;
    }
   }
   for(float d=6;d<total;d+=6)
   {
    var p=BridgeSample296(points,d,out var forward);var right=Vector3.Cross(Vector3.up,forward).normalized;
    foreach(float side in new[]{-.32f,.32f})
    {
     var at=p+right*side*width;float ground=TriangleVehicleRoad296(plan.Field,at.x,at.z);if(!WetVehicleRoad296(plan,at,ground,.2f)||p.y-ground<1.2f)continue;
     var pier=new GameObject("PierCollision").AddComponent<BoxCollider>();pier.transform.SetParent(root,false);pier.gameObject.isStatic=true;
     pier.transform.position=new Vector3(at.x,(ground-.3f+p.y-.45f)*.5f,at.z);pier.size=new Vector3(.902f,p.y-.45f-ground+.3f,.902f);plan.PierBoxes++;
    }
   }
  }
 }
}
