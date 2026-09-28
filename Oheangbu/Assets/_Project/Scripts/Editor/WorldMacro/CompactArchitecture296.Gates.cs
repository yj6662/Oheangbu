using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class GateAperture296
  {
   public string Id,Path,OpenFact;public int Edge;public float Distance,Width;public Vector3 Centre,Inward;public string[] Routes;
  }
  [Serializable] sealed class GateLoop296
  {
   public string Id,Realm;public Vector3[] Points;public GateAperture296[] Gates;public int WallModules;public float MinimumWallHeight;
  }
  [Serializable] sealed class GateReceipt296 {public string Revision="architecture-296";public GateLoop296[] Loops;}
  static void Gates296(List<string> report)
  {
   var session=Session292();var scene=session.gameObject.scene;var field=new CompactWorldSurface(session.MountainLayout);var sheet=Sheet296();
   var original=scene.GetRootGameObjects().Single(g=>g.name=="CapitalSouthGate253");
   var canonical=original.GetComponentInChildren<SouthGateDoorPresentation>(true);
   if(canonical==null||!canonical.IsConfigured)throw new InvalidOperationException("Canonical south gate binding is required");
   var architecture=original.transform.Find("OriginalCompactGateArchitecture");if(architecture==null)throw new InvalidOperationException("Canonical Korean gate architecture missing");
   foreach(var old in scene.GetRootGameObjects().Where(g=>g.name=="Architecture296_Gates").ToArray())Object.DestroyImmediate(old);
   var root=new GameObject("Architecture296_Gates").transform;var loops=new List<GateLoop296>();
   var gate=canonical.transform.position;var southBox=canonical.GetComponent<BoxCollider>();
   if(southBox==null)throw new InvalidOperationException("Canonical closed gate support missing");
   float width=Mathf.Abs(southBox.size.x*canonical.transform.lossyScale.x);
   // A closed loop surrounds the actual capital destinations. Its only openings
   // are the canonical gate and route-aligned exits sharing that same saved fact.
   var capital=new[]{new Vector3(gate.x-350,0,gate.z),new Vector3(gate.x+250,0,gate.z),
    new Vector3(gate.x+360,0,gate.z+450),new Vector3(gate.x+310,0,gate.z+805),
    new Vector3(gate.x+170,0,gate.z+1045),new Vector3(gate.x-330,0,gate.z+1045),
    new Vector3(gate.x-390,0,gate.z+780),new Vector3(gate.x-350,0,gate.z+360)};
   for(int i=0;i<capital.Length;i++)capital[i].y=field.Sample(capital[i].x,capital[i].z);
   var apertures=new List<GateAperture296>{new GateAperture296{Id="south_gate",Path="CapitalSouthGate253/VictoryGate253",OpenFact="hwanggyeong_south_gate",Edge=0,
    Distance=350,Width=width,Centre=gate,Inward=Vector3.forward,Routes=new[]{"south_gate_threshold253","south_gate__capital_center"}}};
   var routePath=O296+"/Generated/routes.json";var routes=JsonUtility.FromJson<Routes292>(File.ReadAllText(File.Exists(routePath)?routePath:G295+"/routes.json"));
   foreach(var route in routes.routes)
   {
    var rule=session.MountainLayout.Routes.FirstOrDefault(r=>r.Id==route.id);
    if(rule!=null&&rule.Role==CompactRouteRole.AbilityGate)continue;
    for(int i=1;i<route.points.Length;i++)for(int edge=0;edge<capital.Length;edge++)
    {
     var a=capital[edge];var b=capital[(edge+1)%capital.Length];
     if(!SegmentGateIntersection296(a,b,route.points[i-1],route.points[i],out float t))continue;
     var p=Vector3.Lerp(a,b,t);float edgeLength=new Vector2(b.x-a.x,b.z-a.z).magnitude;
     var existing=apertures.FirstOrDefault(g=>g.Edge==edge&&Mathf.Abs(g.Distance-t*edgeLength)<Mathf.Max(.5f,(g.Width-route.width)*.5f-.6f));
     if(existing!=null){existing.Routes=existing.Routes.Concat(new[]{route.id}).Distinct().ToArray();continue;}
     // A gate cannot straddle a polygon corner; keep enough wall on both sides.
     float length=Vector3.Distance(new Vector3(a.x,0,a.z),new Vector3(b.x,0,b.z));float along=Mathf.Clamp(t*length,width*.6f,length-width*.6f);
     p=Vector3.Lerp(a,b,along/length);var routeDelta=route.points[i]-route.points[i-1];
     float routeT=Mathf.Clamp01(Vector2.Dot(new Vector2(p.x-route.points[i-1].x,p.z-route.points[i-1].z),new Vector2(routeDelta.x,routeDelta.z))/Mathf.Max(.00001f,new Vector2(routeDelta.x,routeDelta.z).sqrMagnitude));
     p.y=Mathf.Max(field.Sample(p.x,p.z),Mathf.Lerp(route.points[i-1].y,route.points[i].y,routeT));var tangent=(b-a);tangent.y=0;tangent.Normalize();
     apertures.Add(new GateAperture296{Id="capital_exit_"+apertures.Count,OpenFact="hwanggyeong_south_gate",Edge=edge,Distance=along,Width=width,
      Centre=p,Inward=Vector3.Cross(tangent,Vector3.up),Routes=new[]{route.id}});
    }
   }
   MergeNeighbourGates296(apertures,routes,field,canonical,architecture,report);
   File.WriteAllText(routePath,JsonUtility.ToJson(routes,true));
   var cityRoot=new GameObject("CapitalPerimeter").transform;cityRoot.SetParent(root,false);
   foreach(var opening in apertures.Where(g=>g.Id!="south_gate"))
   {
    var holder=new GameObject(opening.Id).transform;holder.SetParent(cityRoot,false);
    var rotation=Quaternion.LookRotation(opening.Inward,Vector3.up)*Quaternion.Inverse(canonical.transform.rotation);
    var archCopy=Object.Instantiate(architecture.gameObject,holder);archCopy.name="KoreanGateArchitecture";
    archCopy.transform.SetPositionAndRotation(opening.Centre+rotation*(architecture.position-canonical.transform.position),rotation*architecture.rotation);archCopy.transform.localScale=architecture.lossyScale;
    var copy=Object.Instantiate(canonical.gameObject,holder);copy.name="VictoryGate296";
    copy.transform.SetPositionAndRotation(opening.Centre,rotation*canonical.transform.rotation);copy.transform.localScale=canonical.transform.lossyScale;
    var presentation=copy.GetComponent<SouthGateDoorPresentation>();presentation.ConfigureSession(session);presentation.SetOpened(false,true);
    opening.Path=root.name+"/CapitalPerimeter/"+holder.name+"/"+copy.name;
    foreach(var renderer in archCopy.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(CrossingMaterial296).ToArray();
    foreach(var renderer in copy.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(CrossingMaterial296).ToArray();
    GateFoundation296(holder,opening,field);
   }
   foreach(var opening in apertures)BuildGateApproaches296(cityRoot,opening,routes,field);
   loops.Add(BuildGateLoop296(cityRoot,"capital296","Hwanggyeong",capital,apertures.ToArray(),10f,field));
   // Reserved regional venues receive an architectural entrance, without new
   // campaign locks or artificial boss completion facts.
   foreach(var arena in sheet.Arenas.Where(a=>string.Equals(a.Realm,"Cheolong",StringComparison.OrdinalIgnoreCase)||string.Equals(a.Realm,"Hwanggyeong",StringComparison.OrdinalIgnoreCase)))
   {
    var rotation=Quaternion.Euler(0,arena.Yaw,0);float hx=Mathf.Max(52,arena.ClearSize.x*.5f+22),hz=Mathf.Max(50,arena.ClearSize.y*.5f+25);
    var polygon=new[]{new Vector3(-hx,0,-hz),new Vector3(hx,0,-hz),new Vector3(hx,0,hz),new Vector3(-hx,0,hz)}.Select(p=>arena.Centre+rotation*p).ToArray();
    foreach(var p in Enumerable.Range(0,polygon.Length))polygon[p].y=field.Sample(polygon[p].x,polygon[p].z);
    var centre=arena.Centre+rotation*new Vector3(0,0,-hz);centre.y=field.Sample(centre.x,centre.z);
    if(arena.Approach!=null&&arena.Approach.Length>0)
    {
     var nearest=arena.Approach.OrderBy(p=>new Vector2(p.x-centre.x,p.z-centre.z).sqrMagnitude).First();
     if(Vector2.Distance(new Vector2(nearest.x,nearest.z),new Vector2(centre.x,centre.z))<1)centre.y=Mathf.Max(centre.y,nearest.y);
    }
    var aperture=new GateAperture296{Id=arena.Id+"_entrance",Edge=0,Distance=hx,Width=9,Centre=centre,Inward=rotation*Vector3.forward,OpenFact="",Routes=Array.Empty<string>()};
    var holder=new GameObject(arena.Id+"_PrecinctWall").transform;holder.SetParent(root,false);
    var archCopy=Object.Instantiate(architecture.gameObject,holder);archCopy.name="OpenKoreanGate";var delta=rotation*Quaternion.Inverse(canonical.transform.rotation);
    archCopy.transform.SetPositionAndRotation(centre+delta*(architecture.position-canonical.transform.position),delta*architecture.rotation);archCopy.transform.localScale=architecture.lossyScale;
    foreach(var renderer in archCopy.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(CrossingMaterial296).ToArray();
    aperture.Path=root.name+"/"+holder.name+"/OpenKoreanGate";
    GateFoundation296(holder,aperture,field);
    BuildGateApproaches296(holder,aperture,new Routes292{routes=new[]{new Route292{id=arena.Id+"_gate_approach",width=4,points=arena.Approach}}},field,12,true);
    loops.Add(BuildGateLoop296(holder,arena.Id+"_precinct296",arena.Realm,polygon,new[]{aperture},7.5f,field));
   }
   sheet.Perimeters=loops.Select(loop=>new CompactArchitectureSheetSO.Perimeter{Id=loop.Id,Realm=loop.Realm,Points=loop.Points,
    GateCentre=loop.Gates[0].Centre,GateWidth=loop.Gates[0].Width,GateScenePath=loop.Gates[0].Path,
    OpenFact=loop.Gates[0].OpenFact,ActiveGate=loop.Id=="capital296",Height=loop.MinimumWallHeight}).ToArray();
   sheet.Structures=sheet.Structures.Where(s=>!s.Id.StartsWith("gate296-",StringComparison.Ordinal)).Concat(loops.SelectMany(loop=>loop.Gates.Select(g=>
    new CompactArchitectureSheetSO.Structure{Id="gate296-"+g.Id,Realm=loop.Realm,Label=string.IsNullOrEmpty(g.OpenFact)?"Open precinct entrance":"Capital gate synchronized to saved south-gate victory",
     SourceId="crossing296-rampart",SceneRoot=g.Path,Position=g.Centre,Yaw=Quaternion.LookRotation(g.Inward).eulerAngles.y,Size=new Vector3(g.Width,loop.MinimumWallHeight,4),RouteIds=g.Routes}))).ToArray();
   GateInfill296(report);
   EditorUtility.SetDirty(sheet);RegisterCrossingSources296();Physics.SyncTransforms();Directory.CreateDirectory(O296);
   File.WriteAllText(O296+"/gates.json",JsonUtility.ToJson(new GateReceipt296{Loops=loops.ToArray()},true));
   report.Add("Perimeters="+loops.Count+"; source rampart modules="+loops.Sum(l=>l.WallModules)+"; capital openings="+apertures.Count+" all read hwanggyeong_south_gate. Inactive mountain gate roots retained inactive. Existing SouthGate transaction and animation timing retained.");
  }
  static void GateFoundation296(Transform parent,GateAperture296 gate,CompactWorldSurface field)
  {
   float ground=field.Sample(gate.Centre.x,gate.Centre.z),rise=gate.Centre.y-ground;
   if(rise<.15f)return;
   var source=CrossingSource(Rampart296);const float scale=.65f;float course=source.Bounds.size.y*scale*.8f;
   var batch=new CrossingBatch296();var rotation=Quaternion.LookRotation(gate.Inward,Vector3.up);
   int count=Mathf.Max(1,Mathf.CeilToInt((gate.Width+.1f)/Mathf.Max(.1f,source.Bounds.size.x*scale*.98f)));float step=(gate.Width+.1f)/count;
   for(int i=0;i<count;i++)for(float y=ground-.4f;y<gate.Centre.y;y+=course-.025f)
    batch.Wall(Rampart296,new Vector3(gate.Centre.x,y,gate.Centre.z)+rotation*Vector3.right*(step*(i+.5f)-(gate.Width+.1f)*.5f),rotation,scale,step+.02f,Mathf.Min(course,gate.Centre.y-y));
   // The filled masonry below a raised threshold is permanent. It does not
   // participate in the dynamic leaf state and never protrudes above the path.
   var renderers=batch.Finish(parent,"gate296_"+gate.Id+"_foundation",true);
   var solid=new GameObject("RaisedThresholdMasonry").AddComponent<BoxCollider>();solid.transform.SetParent(parent,false);solid.transform.SetPositionAndRotation(gate.Centre,rotation);
   solid.center=new Vector3(0,-rise*.5f,0);solid.size=new Vector3(gate.Width+.06f,rise,Mathf.Max(1.1f,source.Bounds.size.z*scale*.82f));solid.gameObject.isStatic=true;
  }
  static GateLoop296 BuildGateLoop296(Transform root,string id,string realm,Vector3[] polygon,GateAperture296[] apertures,float height,CompactWorldSurface field)
  {
   int modules=0;var source=CrossingSource(Rampart296);
   // A fixed source scale retains human-sized stone courses on flat and steep
   // ground alike. Height is assembled from courses, not enlarged merlons.
   const float masonryScale=.65f,parapetHeight=1.25f;
   float courseHeight=source.Bounds.size.y*masonryScale*.80f;
   float nominalWidth=source.Bounds.size.x*masonryScale*.98f;
   for(int edge=0;edge<polygon.Length;edge++)
   {
    var a=polygon[edge];var b=polygon[(edge+1)%polygon.Length];a.y=b.y=0;var tangent=(b-a).normalized;float length=Vector3.Distance(a,b);
    var cuts=apertures.Where(g=>g.Edge==edge).OrderBy(g=>g.Distance).ToArray();float cursor=0;int run=0;
    foreach(var cut in cuts)
    {
     float end=Mathf.Max(cursor,cut.Distance-cut.Width*.5f);WallRun(cursor,end);cursor=Mathf.Max(cursor,cut.Distance+cut.Width*.5f);
    }
    WallRun(cursor,length);
    void WallRun(float from,float to)
    {
     float span=to-from;if(span<.05f)return;
     if(span>64f){int chunks=Mathf.CeilToInt(span/64f);for(int chunk=0;chunk<chunks;chunk++)WallRun(from+span*chunk/chunks,from+span*(chunk+1)/chunks);return;}
     int count=Mathf.CeilToInt(span/nominalWidth);float step=span/count;
     var batch=new CrossingBatch296();var holder=new GameObject("Wall_"+edge+"_"+run++).transform;holder.SetParent(root,false);
     for(int i=0;i<count;i++)
     {
      var p=a+tangent*(from+step*(i+.5f));var normal=Vector3.Cross(tangent,Vector3.up);float lo=float.MaxValue,hi=float.MinValue;
      foreach(float along in new[]{-.5f,0,.5f})foreach(float side in new[]{-5f,0,5f})
      {var sample=p+tangent*step*along+normal*side;float y=field.Sample(sample.x,sample.z);lo=Mathf.Min(lo,y);hi=Mathf.Max(hi,y);}
      var bottom=new Vector3(p.x,lo-.35f,p.z);var rotation=Quaternion.LookRotation(normal,Vector3.up);
      float solidHeight=hi-lo+height+.35f;
      for(float y=0;y<solidHeight;y+=courseHeight-.025f)
       batch.Wall(Rampart296,bottom+Vector3.up*y,rotation,masonryScale,step+.06f,Mathf.Min(courseHeight,solidHeight-y));
      // Separate coping tiles close cut masonry tops; the original stone UVs
      // and source material survive the private mesh clipping/combination.
      var cap=CrossingSource(StoneFloor296);float capScale=source.Bounds.size.z*masonryScale/cap.Bounds.size.z;
      float capWidth=cap.Bounds.size.x*capScale;int caps=Mathf.Max(1,Mathf.CeilToInt(step/capWidth));float capStep=step/caps;
      for(int c=0;c<caps;c++)batch.Wall(StoneFloor296,bottom+Vector3.up*(solidHeight-.015f)+tangent*((c+.5f)*capStep-step*.5f),rotation,capScale,capStep+.03f);
      var parapet=CrossingSource(Parapet296);float parapetScale=parapetHeight/parapet.Bounds.size.y;
      // SM_CW_Parapet is a complete, low crenellated module, unlike an enlarged
      // castle-wall body. Crop its ends so adjacent runs meet without overlap.
      batch.Wall(Parapet296,bottom+Vector3.up*solidHeight,rotation,parapetScale,step+.06f);
      var wall=new GameObject("WallSolid_"+i).AddComponent<BoxCollider>();wall.transform.SetParent(holder,false);wall.transform.SetPositionAndRotation(bottom,rotation);
      wall.center=new Vector3(0,solidHeight*.5f,0);wall.size=new Vector3(step+.055f,solidHeight,source.Bounds.size.z*masonryScale*.82f);wall.gameObject.isStatic=true;modules++;
     }
     // Continuous WallSolid boxes own collision. Repeating every source stone
     // triangle adds no body closure and needlessly enters physics/NavMesh.
     var renderers=batch.Finish(holder,id+"_wall_"+edge+"_"+run,false);
     CrossingLods296(holder,id+"_wall_"+edge+"_"+run,renderers);
    }
   }
   return new GateLoop296{Id=id,Realm=realm,Points=polygon,Gates=apertures,WallModules=modules,MinimumWallHeight=height};
  }
  static bool SegmentGateIntersection296(Vector3 a,Vector3 b,Vector3 c,Vector3 d,out float t)
  {
   float ax=b.x-a.x,az=b.z-a.z,bx=d.x-c.x,bz=d.z-c.z,den=ax*bz-az*bx;t=0;
   if(Mathf.Abs(den)<.00001f)return false;float cx=c.x-a.x,cz=c.z-a.z;t=(cx*bz-cz*bx)/den;float u=(cx*az-cz*ax)/den;
   return t>=0&&t<=1&&u>=0&&u<=1;
  }
  static void MergeNeighbourGates296(List<GateAperture296> gates,Routes292 routes,CompactWorldSurface field,SouthGateDoorPresentation canonical,Transform architecture,List<string> report)
  {
   var renderers=architecture.GetComponentsInChildren<Renderer>(true);var bounds=renderers[0].bounds;
   foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
   var right=canonical.transform.right;float architectureWidth=2*(Mathf.Abs(right.x)*bounds.extents.x+Mathf.Abs(right.y)*bounds.extents.y+Mathf.Abs(right.z)*bounds.extents.z);
   float mergeDistance=Mathf.Max(architectureWidth+4,21f);
   foreach(var gate in gates.ToArray())
   {
    if(!gates.Contains(gate))continue;
    foreach(var close in gates.Where(g=>g!=gate&&g.Edge==gate.Edge&&Mathf.Abs(g.Distance-gate.Distance)<mergeDistance).ToArray())
    {
     // The first gate is canonical when this is the southern opening. Other
     // close routes share the first already-authored threshold on their edge.
     foreach(string id in close.Routes)
     {
      var route=routes.routes.FirstOrDefault(r=>r.id==id);if(route==null)throw new InvalidOperationException("Missing gate route "+id);
      route.points=RedirectGateRoute296(route.points,gate,field);
      ReplaceBridgeRoute296(id,route.points);
      report.Add("Merged "+id+" into "+gate.Id+"; previous aperture distance="+Mathf.Abs(gate.Distance-close.Distance).ToString("F2")+"m; original gate architecture width="+architectureWidth.ToString("F2")+"m.");
     }
     gate.Routes=gate.Routes.Concat(close.Routes).Distinct().ToArray();gates.Remove(close);
    }
   }
   for(int i=0;i<gates.Count;i++)for(int j=i+1;j<gates.Count;j++)
    if(gates[i].Edge==gates[j].Edge&&Mathf.Abs(gates[i].Distance-gates[j].Distance)<architectureWidth+.5f)
     throw new InvalidOperationException("Remaining gate architecture bounds overlap: "+gates[i].Id+" / "+gates[j].Id);
   report.Add("Gate source bounds="+bounds.size+"; adjacent threshold roof bounds checked after route consolidation.");
  }
  static Vector3[] RedirectGateRoute296(Vector3[] points,GateAperture296 gate,CompactWorldSurface field)
  {
   float Side(Vector3 p)=>Vector3.Dot(Vector3.ProjectOnPlane(p-gate.Centre,Vector3.up),gate.Inward);
   int crossing=-1;for(int i=1;i<points.Length;i++)if(Side(points[i-1])*Side(points[i])<=0){crossing=i;break;}
   if(crossing<0)throw new InvalidOperationException("Route does not cross requested gate plane: "+gate.Id);
   int first=crossing-1,last=crossing;float distance=0;
   while(first>0&&distance<55){distance+=Vector3.Distance(points[first],points[first-1]);first--;}
   distance=0;while(last<points.Length-1&&distance<55){distance+=Vector3.Distance(points[last],points[last+1]);last++;}
   var forward=gate.Inward*Mathf.Sign(Side(points[last])-Side(points[first]));
   var before=gate.Centre-forward*8;var after=gate.Centre+forward*8;
   before.y=Mathf.Max(gate.Centre.y,field.Sample(before.x,before.z)+.025f);after.y=Mathf.Max(gate.Centre.y,field.Sample(after.x,after.z)+.025f);
   var replacement=new List<Vector3>();
   AppendGateCurve296(replacement,points[first],points[Mathf.Min(first+1,points.Length-1)]-points[first],before,forward,field);
   for(int i=1;i<=32;i++)replacement.Add(Vector3.Lerp(before,after,i/32f));
   AppendGateCurve296(replacement,after,forward,points[last],points[last]-points[Mathf.Max(0,last-1)],field);
   return points.Take(first).Concat(replacement).Concat(points.Skip(last+1)).ToArray();
  }
  static void AppendGateCurve296(List<Vector3> output,Vector3 a,Vector3 da,Vector3 b,Vector3 db,CompactWorldSurface field)
  {
   da.y=db.y=0;float distance=Vector3.ProjectOnPlane(b-a,Vector3.up).magnitude,handle=Mathf.Min(20,distance*.36f);
   var c=a+da.normalized*handle;var d=b-db.normalized*handle;int count=Mathf.Max(4,Mathf.CeilToInt(distance*2));
   for(int i=output.Count==0?0:1;i<=count;i++)
   {
    float t=i/(float)count,u=1-t;var p=u*u*u*a+3*u*u*t*c+3*u*t*t*d+t*t*t*b;
    // Permanent visible source paving follows this exact line. No terrain data
    // is changed and route endpoints remain the incumbent destinations.
    p.y=Mathf.Max(field.Sample(p.x,p.z)+.025f,Mathf.Lerp(a.y,b.y,Mathf.SmoothStep(0,1,t)));output.Add(p);
   }
  }
  static void BuildGateApproaches296(Transform parent,GateAperture296 gate,Routes292 routes,CompactWorldSurface field,float approachMetres=60,bool allRoutes=false)
  {
   var batch=new CrossingBatch296();var floor=CrossingSource(StoneFloor296);
   var approaches=new List<GatePaving296>();
   foreach(string id in allRoutes?routes.routes.Select(r=>r.id):gate.Routes)
   {
    var route=routes.routes.FirstOrDefault(r=>r.id==id);if(route==null)continue;
    int near=NearestBridgePoint296(route.points,gate.Centre),first=near,last=near;float distance=0;
    while(first>0&&distance<approachMetres){distance+=Vector3.Distance(route.points[first],route.points[first-1]);first--;}
    distance=0;while(last<route.points.Length-1&&distance<approachMetres){distance+=Vector3.Distance(route.points[last],route.points[last+1]);last++;}
    var points=route.points.Skip(first).Take(last-first+1).ToArray();if(points.Length<2)continue;
    var along=new float[points.Length];for(int i=1;i<points.Length;i++)along[i]=along[i-1]+Vector3.ProjectOnPlane(points[i]-points[i-1],Vector3.up).magnitude;
    approaches.Add(new GatePaving296{Points=points,Along=along,Width=Mathf.Min(gate.Width-.8f,Mathf.Max(2.6f,route.width))});
   }
   float GateJoinHeight(Vector3 p,float support)
   {
    float along=Mathf.Abs(Vector3.Dot(p-gate.Centre,gate.Inward));
    float blend=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((along-1.25f)/8.75f));
    return Mathf.Max(support,Mathf.Lerp(support,gate.Centre.y+.025f,blend));
   }
   float SupportHeight(Vector3 p)
   {
    float ground=field.Sample(p.x,p.z)+.035f,support=ground;
    // Every overlapping approach samples one common top surface. Otherwise a
    // branch's raised edge becomes a half-metre step across the other route.
    foreach(var approach in approaches)
    {
     GatePavingProfile296(approach,p,out float height,out float lateral,out float along);
     float shoulder=Mathf.Min(1.5f,approach.Width*.4f);
     float edge=Mathf.SmoothStep(0,1,Mathf.Clamp01((approach.Width*.5f-lateral)/shoulder));
     float ends=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(along,approach.Along[approach.Along.Length-1]-along)/2.5f));
     support=Mathf.Max(support,Mathf.Lerp(ground,Mathf.Max(ground,height+.01f),edge*ends));
    }
    return GateJoinHeight(p,support);
   }
   foreach(var approach in approaches)
   {
    var path=approach.Points;
    // Sub-metre pieces sample both sides of the flat threshold. Two-metre
    // slabs previously bridged over that short plateau and left a high lip.
    float width=approach.Width;int columns=Mathf.Max(1,Mathf.CeilToInt(width/.65f));float scale=width/columns/floor.Bounds.size.x;
    float step=floor.Bounds.size.z*scale*.97f,total=BridgeLength296(path);
    for(float s=0;s<total;s+=step)
    {
     var p=BridgeSample296(path,Mathf.Min(total,s+step*.5f),out var forward);forward.y=0;forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
     for(int column=0;column<columns;column++)
     {
      var centre=p+right*(width*(column+.5f)/columns-width*.5f);centre.y=0;
      var matrix=Matrix4x4.TRS(centre,Quaternion.LookRotation(forward),Vector3.one*scale)*Matrix4x4.Translate(-new Vector3(floor.Bounds.center.x,floor.Bounds.max.y,floor.Bounds.center.z));
      batch.WarpSurface(StoneFloor296,matrix,v=>
      {
       float y=SupportHeight(v);
       v.y+=y;return v;
      });
     }
    }
    // Raised paving follows existing route grades, so visibly fill the masonry
    // underneath instead of leaving thin source slabs suspended over a bank.
    var masonry=CrossingSource(Rampart296);const float masonryScale=.65f;
    float foundationStep=masonry.Bounds.size.z*masonryScale*.94f,course=masonry.Bounds.size.y*masonryScale*.80f;
    int piers=Mathf.Max(1,Mathf.CeilToInt(width/(masonry.Bounds.size.x*masonryScale*.98f)));float pierWidth=width/piers;
    for(float s=0;s<total;s+=foundationStep)
    {
     var p=BridgeSample296(path,Mathf.Min(total,s+foundationStep*.5f),out var forward);forward.y=0;forward.Normalize();var right=Vector3.Cross(Vector3.up,forward);
     for(int column=0;column<piers;column++)
     {
      var centre=p+right*(pierWidth*(column+.5f)-width*.5f);float ground=float.MaxValue;
      foreach(float along in new[]{-.5f,.5f})foreach(float side in new[]{-.5f,.5f})
      {var q=centre+forward*foundationStep*along+right*pierWidth*side;ground=Mathf.Min(ground,field.Sample(q.x,q.z));}
      float top=SupportHeight(centre);
      if(top-ground<.18f)continue;centre.y=ground-.1f;float height=top-centre.y;
      for(float y=0;y<height;y+=course-.025f)batch.Wall(Rampart296,centre+Vector3.up*y,Quaternion.LookRotation(forward),masonryScale,pierWidth+.025f,Mathf.Min(course,height-y),v=>
      {
       float ceiling=SupportHeight(v)-floor.Bounds.size.y*scale-.02f;
       v.y=Mathf.Min(v.y,ceiling);return v;
      });
     }
    }
   }
   if(batch.Instances==0)return;
   var holder=new GameObject(gate.Id+"_RouteApproaches").transform;holder.SetParent(parent,false);
   var renderers=batch.Finish(holder,"gate296_"+gate.Id+"_approaches",true);CrossingLods296(holder,"gate296_"+gate.Id+"_approaches",renderers);
  }
  sealed class GatePaving296 {public Vector3[] Points;public float[] Along;public float Width;}
  static void GatePavingProfile296(GatePaving296 path,Vector3 p,out float height,out float lateral,out float along)
  {
   height=path.Points[0].y;lateral=float.MaxValue;along=0;
   for(int i=1;i<path.Points.Length;i++)
   {
    var delta=Vector3.ProjectOnPlane(path.Points[i]-path.Points[i-1],Vector3.up);
    float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(p-path.Points[i-1],Vector3.up),delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
    float distance=Vector3.ProjectOnPlane(p-Vector3.Lerp(path.Points[i-1],path.Points[i],t),Vector3.up).magnitude;
    if(distance>=lateral)continue;lateral=distance;height=Mathf.Lerp(path.Points[i-1].y,path.Points[i].y,t);along=Mathf.Lerp(path.Along[i-1],path.Along[i],t);
   }
  }
  static float GateApproachY296(Vector3[] path,Vector3 p)
  {
   float best=float.MaxValue,height=path[0].y;
   for(int i=1;i<path.Length;i++)
   {
    var delta=Vector3.ProjectOnPlane(path[i]-path[i-1],Vector3.up);float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(p-path[i-1],Vector3.up),delta)/Mathf.Max(.0001f,delta.sqrMagnitude));
    float d=Vector3.ProjectOnPlane(p-Vector3.Lerp(path[i-1],path[i],t),Vector3.up).sqrMagnitude;
    if(d<best){best=d;height=Mathf.Lerp(path[i-1].y,path[i].y,t);}
   }
   return height;
  }
 }
}
