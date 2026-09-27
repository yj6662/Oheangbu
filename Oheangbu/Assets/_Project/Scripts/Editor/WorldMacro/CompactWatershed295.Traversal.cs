using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Route292[] RequiredRoutes295()
  {
   var layout=Session292().MountainLayout;var required=layout.Routes.Where(r=>r.Role==CompactRouteRole.Main&&string.IsNullOrEmpty(r.RequiredAbility)).ToArray();
   var exported=JsonUtility.FromJson<Routes292>(File.ReadAllText(G295+"/routes.json")).routes;var result=new List<Route292>();routeBindings295.Clear();
   foreach(var route in required)
   {
    var exact=exported.FirstOrDefault(r=>r.id==route.Id);if(exact!=null){result.Add(exact);continue;}
    var mountain=layout.Mountains.FirstOrDefault(m=>route.Id.StartsWith(m.Id+"_"));if(mountain==null)continue;
    var merged=exported.FirstOrDefault(r=>r.id==mountain.Id+"_main");var from=layout.Places.FirstOrDefault(p=>p.Id==route.From);var to=layout.Places.FirstOrDefault(p=>p.Id==route.To);
    if(merged==null||from==null||to==null||mountain.MainPath.Length<2||merged.points.Length!=mountain.MainPath.Length)continue;
    if(merged.points.Where((p,i)=>Vector3.Distance(p,mountain.MainPath[i])>.05f).Any())continue;
    int first=Enumerable.Range(0,merged.points.Length).OrderBy(i=>(new Vector2(merged.points[i].x,merged.points[i].z)-from.XZ).sqrMagnitude).First();
    int last=Enumerable.Range(0,merged.points.Length).OrderBy(i=>(new Vector2(merged.points[i].x,merged.points[i].z)-to.XZ).sqrMagnitude).First();
    float fromGap=Vector2.Distance(new Vector2(merged.points[first].x,merged.points[first].z),from.XZ),toGap=Vector2.Distance(new Vector2(merged.points[last].x,merged.points[last].z),to.XZ);
    if(first==last||fromGap>Mathf.Max(8,route.Width*2)||toGap>Mathf.Max(8,route.Width*2))continue;
    var points=merged.points.Skip(Math.Min(first,last)).Take(Math.Abs(last-first)+1).ToArray();if(first>last)Array.Reverse(points);
    result.Add(new Route292{id=route.Id,width=route.Width,vehicle=route.GradeForVehicle,points=points});
    routeBindings295.Add(route.Id+" from "+merged.id+" samples "+first+".."+last+" endpoint gaps="+fromGap.ToString("F2")+","+toGap.ToString("F2")+"m");
   }
   return result.ToArray();
  }
  static readonly List<string> routeBindings295=new List<string>();
  static bool Support295(Vector3 p,CompactWorldSurface field,WorldTerrainQuery query,out RaycastHit support)
  {
   float expected=field.Sample(p.x,p.z);float top=Mathf.Max(expected,p.y);if(query.TryWaterHeight(p,out float water))top=Mathf.Max(top,water);
   var scene=SceneManager.GetActiveScene();var hits=Physics.RaycastAll(new Vector3(p.x,top+80,p.z),Vector3.down,240,~0,QueryTriggerInteraction.Ignore)
    .Where(h=>h.collider.gameObject.scene==scene&&h.collider.attachedRigidbody==null&&!(h.collider is CharacterController)&&h.collider.GetComponentInParent<WorldTemporarySupport>()==null&&h.normal.y>.55f).ToArray();
   var bridge=hits.Where(h=>h.collider.transform.root.name=="Watershed295_Crossings").OrderBy(h=>Mathf.Abs(h.point.y-p.y)).ToArray();
   if(bridge.Length>0){support=bridge[0];return true;}
   // Authored stair/boardwalk profiles are up to several metres above the
   // coarse heightfield. Match the route elevation, retaining those surfaces.
   if(hits.Length>0){support=hits.OrderBy(h=>Mathf.Abs(h.point.y-p.y)).First();return true;}
   support=default;return false;
  }
  static Vector3[] Samples295(Vector3[] path,float spacing)
  {
   var result=new List<Vector3>();if(path.Length==0)return result.ToArray();result.Add(path[0]);
   for(int i=1;i<path.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(path[i-1],path[i])/spacing));for(int k=1;k<=n;k++)result.Add(Vector3.Lerp(path[i-1],path[i],k/(float)n));}
   return result.ToArray();
  }
  static List<string> RouteSupport295()
  {
   var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();var result=new List<string>();
   var routes=RequiredRoutes295();var required=Session292().MountainLayout.Routes.Where(r=>r.Role==CompactRouteRole.Main&&string.IsNullOrEmpty(r.RequiredAbility)).Select(r=>r.Id).ToArray();
   var absent=required.Where(id=>!routes.Any(r=>r.id==id)).ToArray();result.Add((absent.Length==0&&routes.Length>0?"PASS ":"FAIL ")+"first-visit route export coverage="+routes.Length+" missing="+string.Join(",",absent));
   result.AddRange(routeBindings295.Select(b=>"INFO consolidated mountain route: "+b));
   foreach(var route in routes)
   {
    int samples=0,missing=0,deep=0,steep=0,bridged=0;Vector3 previous=Vector3.zero;bool hasPrevious=false;var detail=new List<string>();
    foreach(var p in Samples295(route.points,3))
    {
     samples++;if(!Support295(p,field,query,out var hit)){missing++;if(detail.Count<3)detail.Add("missing "+p.ToString("F1"));continue;}
     if(hit.collider.transform.root.name=="Watershed295_Crossings")bridged++;
     if(query.Immersion(hit.point)>(query.Rules!=null?query.Rules.MaximumWadingDepth:.55f)){deep++;if(detail.Count<3)detail.Add("deep "+hit.point.ToString("F1"));}
     if(hasPrevious){var d=hit.point-previous;float flat=new Vector2(d.x,d.z).magnitude;if(flat>.02f&&Mathf.Abs(d.y)/flat>1.05f){steep++;if(detail.Count<3)detail.Add("slope "+hit.point.ToString("F1"));}}previous=hit.point;hasPrevious=true;
    }
    result.Add((missing+deep+steep==0?"PASS ":"FAIL ")+"first-visit physical support "+route.id+" samples="+samples+" bridge="+bridged+" missing="+missing+" deep="+deep+" steep="+steep+(detail.Count>0?" ["+string.Join(";",detail)+"]":""));
   }
   return result;
  }
  sealed class RouteEndpoint295
  {public string Id,Kind;public Vector3 Target;public float Radius;public Transform Actor;}
  static RouteEndpoint295 Endpoint295(string routeId,bool start,Vector3 original,WorldMacroPlaytestSession session)
  {
   var layout=session.MountainLayout;var route=layout.Routes.FirstOrDefault(r=>r.Id==routeId);if(route==null)return null;
   var place=layout.Places.FirstOrDefault(p=>p.Id==(start?route.From:route.To));if(place==null)return null;
   var encounterIds=new[]{place.EncounterId}.Concat(place.EncounterIds??Array.Empty<string>()).Where(id=>!string.IsNullOrEmpty(id)).ToArray();
   foreach(var actor in session.Actors.Where(a=>a!=null).OrderBy(a=>new Vector2(a.transform.position.x-original.x,a.transform.position.z-original.z).sqrMagnitude))
   {
    float flat=Vector2.Distance(new Vector2(original.x,original.z),new Vector2(actor.transform.position.x,actor.transform.position.z));
    if(flat<.75f||encounterIds.Any(id=>actor.Id==id||actor.Id.StartsWith(id+"/")))
    {
     var boss=actor.GetComponent<Oheangbu.App.Demo.CheongryongCombatController>();var general=actor.GetComponent<Oheangbu.App.Demo.SouthGateGeneralController>();var attacks=actor.GetComponent<Oheangbu.Combat.EnemyController>();
     var profile=attacks!=null?new SerializedObject(attacks).FindProperty("_attackProfile")?.objectReferenceValue as Oheangbu.Combat.EnemyAttackProfileSO:null;
     float reach=boss!=null&&boss.Profile!=null?boss.Profile.BiteRange:general!=null&&general.Profile!=null?general.Profile.ThrustRange:profile!=null?profile.Range:attacks!=null?attacks.AttackRange:0;
     if(reach>0&&flat<=reach)
      return new RouteEndpoint295{Id=actor.Id,Kind="authored combat reach",Target=actor.transform.position,Radius=reach,Actor=actor.transform};
    }
   }
   var ids=new[]{place.InteractionId}.Concat(place.InteractionIds??Array.Empty<string>()).Where(id=>!string.IsNullOrEmpty(id)).Distinct();
   var find=typeof(WorldMacroPlaytestSession).GetMethod("FindInteractionPoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   var points=ids.Select(id=>find?.Invoke(session,new object[]{id}) as PrologueContentSO.Point).Where(p=>p!=null&&p.Radius>0)
    .OrderBy(p=>new Vector2(p.Position.x-original.x,p.Position.z-original.z).sqrMagnitude);
   foreach(var point in points)
    if(Vector2.Distance(new Vector2(original.x,original.z),new Vector2(point.Position.x,point.Position.z))<=point.Radius)
     return new RouteEndpoint295{Id=point.Id,Kind="CanInteract geometry",Target=point.Position,Radius=point.Radius};
   return null;
  }
  static bool EndpointVisible295(RouteEndpoint295 endpoint,Vector3 feet,CharacterController body,WorldMacroPlaytestSession session,Transform ignored=null,float margin=0)
  {
   Vector3 origin=feet+Vector3.up*(body.height*.5f-body.center.y+.06f);
   if(Vector3.Distance(origin,endpoint.Target)>endpoint.Radius-margin)return false;
   Vector3 eye=origin+Vector3.up*session.Walker.EyeHeight,to=endpoint.Target+Vector3.up*(endpoint.Actor==null?1.25f:.4f)-eye;
   foreach(var h in Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore))
   {
    if(h.transform.IsChildOf(body.transform)||h.transform.IsChildOf(session.Walker.Body.transform)||(ignored!=null&&h.transform.IsChildOf(ignored)))continue;
    if(endpoint.Actor!=null&&h.transform.IsChildOf(endpoint.Actor))continue;
    var point=h.transform.GetComponentInParent<WorldMacroContentPoint>();if(point!=null&&point.Id==endpoint.Id)continue;
    return false;
   }
   return true;
  }
  static bool EndpointBodyClear295(Vector3 feet,CharacterController body,WorldMacroPlaytestSession session)
  {
   Vector3 origin=feet+Vector3.up*(body.height*.5f-body.center.y+.06f),center=origin+body.center;
   Vector3 a=center-Vector3.up*(body.height*.5f-body.radius),b=center+Vector3.up*(body.height*.5f-body.radius);
   foreach(var obstacle in Physics.OverlapCapsule(a,b,body.radius,~0,QueryTriggerInteraction.Ignore))
   {
    if(obstacle==body||obstacle.transform.IsChildOf(session.Walker.Body.transform))continue;
    if(Physics.ComputePenetration(body,origin,body.transform.rotation,obstacle,obstacle.transform.position,obstacle.transform.rotation,out _,out float penetration)&&penetration>.01f)return false;
   }
   return true;
  }
  static Vector3[] ApproachEndpoints295(Route292 route,Vector3[] source,WorldMacroPlaytestSession session,CompactWorldSurface field,WorldTerrainQuery query,CharacterController body,List<string> evidence,out RouteEndpoint295 finalEndpoint)
  {
   var points=source;finalEndpoint=Endpoint295(route.id,false,source.Last(),session);
   foreach(bool start in new[]{true,false})
   {
    var endpoint=start?Endpoint295(route.id,true,source.First(),session):finalEndpoint;if(endpoint==null)continue;
    var ordered=start?points.Reverse().ToArray():points;int chosen=-1;
    // Trim only the final local approach to the authored interaction/encounter.
    // Every earlier route anchor and every obstacle stays in the path.
    for(int i=ordered.Length-1;i>=0;i--)
    {
     if(Vector3.Distance(ordered[i],ordered.Last())>endpoint.Radius*2+2)break;
     if(!Support295(ordered[i],field,query,out var support)||query.Immersion(support.point)>.55f||!EndpointBodyClear295(support.point,body,session)||!EndpointVisible295(endpoint,support.point,body,session,margin:.3f))continue;
     if(!NavMesh.SamplePosition(support.point,out _,1.2f,NavMesh.AllAreas))continue;chosen=i;
    }
    if(chosen<0){evidence.Add("INFO endpoint approach unavailable "+route.id+" "+(start?"start":"end")+" "+endpoint.Id+"; retained exact route for failure diagnosis");continue;}
    var approach=ordered[chosen];ordered=ordered.Take(chosen+1).ToArray();points=start?ordered.Reverse().ToArray():ordered;
    evidence.Add("INFO endpoint approach "+route.id+" "+(start?"start":"end")+" target="+endpoint.Id+" rule="+endpoint.Kind+" radius="+endpoint.Radius.ToString("F2")+" support="+approach.ToString("F3")+" trim="+Vector3.Distance(approach,start?source.First():source.Last()).ToString("F2")+"m sight=clear; progression state untested");
   }
   return points;
  }
  static string ColliderPath295(Transform t)
  {var names=new List<string>();while(t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
  static IEnumerable<string> WalkContact295(Route292 route,CharacterController body,Vector3 target,CompactWorldSurface field,WorldTerrainQuery query,Vector3? at=null)
  {
   var feet=at??(body.transform.TransformPoint(body.center)-Vector3.up*body.height*.5f);var center=feet+Vector3.up*body.height*.5f;
   var bottom=center-Vector3.up*(body.height*.5f-body.radius);var top=center+Vector3.up*(body.height*.5f-body.radius);
   var direction=target-feet;direction.y=0;direction.Normalize();
   var direct=Samples295(route.points,.75f).OrderBy(p=>new Vector2(p.x-feet.x,p.z-feet.z).sqrMagnitude).First();
   yield return "  CONTACT route="+route.id+" feet="+feet.ToString("F3")+" target="+target.ToString("F3")+" authored nearest="+direct.ToString("F3")+" planarOffset="+Vector2.Distance(new Vector2(feet.x,feet.z),new Vector2(direct.x,direct.z)).ToString("F3")+" routeWidth="+route.width.ToString("F2");
   if(Support295(feet,field,query,out var support))yield return "  SUPPORT "+ColliderPath295(support.transform)+" at="+support.point.ToString("F3")+" normal="+support.normal.ToString("F3");
   foreach(var collider in Physics.OverlapCapsule(bottom,top,body.radius+.12f,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=body).Take(12))
    yield return "  OVERLAP "+ColliderPath295(collider.transform)+" type="+collider.GetType().Name+" bounds="+collider.bounds+" closest="+collider.ClosestPoint(center).ToString("F3");
   foreach(var hit in Physics.CapsuleCastAll(bottom,top,body.radius,direction,2,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=body).OrderBy(h=>h.distance).Take(12))
    yield return "  AHEAD "+ColliderPath295(hit.transform)+" distance="+hit.distance.ToString("F3")+" at="+hit.point.ToString("F3")+" normal="+hit.normal.ToString("F3");
   if(NavMesh.SamplePosition(feet,out var nav,3,NavMesh.AllAreas))yield return "  NAV nearest="+nav.position.ToString("F3")+" distance="+nav.distance.ToString("F3");
  }
  static string Walk295()
  {
   var session=Session292();var field=new CompactWorldSurface(session.MountainLayout);var query=Components295<WorldTerrainQuery>().First();var source=session.Walker.Body;
   var go=new GameObject("Watershed295_first_visit_walker"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   bool hasCandidateNav=Root295("Watershed295_Navigation")!=null;
   var lines=new List<string>{"Watershed295 Edit CharacterController first-visit routes; actual player capsule; no Mum bridge is created. "+DateTime.Now.ToString("s"),
    "INFO candidate PhysicsColliders NavMesh="+hasCandidateNav+"; endpoint approaches use authored interaction/combat radius and sight; gates retain their current progression state."};
   try
   {
    foreach(var route in RequiredRoutes295())foreach(bool reverse in new[]{false,true})
    {
     var points=Samples295(route.points,1.5f);int unavailable=0;
     RouteEndpoint295 arrival=null;
     if(hasCandidateNav){points=ApproachEndpoints295(route,points,session,field,query,cc,lines,out arrival);if(reverse)arrival=Endpoint295(route.id,true,route.points.First(),session);}
     if(reverse)Array.Reverse(points);
     for(int i=0;i<points.Length;i++)if(Support295(points[i],field,query,out var hit))points[i]=hit.point;else unavailable++;
     if(points.Length<2||unavailable>0){lines.Add("FAIL "+route.id+" missing physical route support="+unavailable);continue;}
     if(hasCandidateNav)
     {
      var routed=new List<Vector3>();var anchors=points.Where((p,i)=>i==0||i==points.Length-1||i%12==0).ToArray();string navFailure=null;
      for(int i=0;i<anchors.Length;i++)
      {
       if(!Support295(anchors[i],field,query,out var anchorSupport)||!NavMesh.SamplePosition(anchorSupport.point,out var navHit,1.2f,NavMesh.AllAreas)){navFailure="no navigation near route anchor "+i;break;}
       if(routed.Count==0){routed.Add(navHit.position);continue;}var path=new NavMeshPath();
       if(!NavMesh.CalculatePath(routed.Last(),navHit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete){navFailure="incomplete navigation near route anchor "+i;break;}
       routed.AddRange(path.corners.Skip(1));
      }
      if(navFailure!=null){lines.Add("FAIL "+route.id+(reverse?" reverse":" forward")+" "+navFailure+"; gate/progression state unchanged");continue;}
      points=Samples295(routed.ToArray(),1.5f);unavailable=0;
      // Recast's voxel surface can sit above the collider. The controller must
      // follow the collider elevation while retaining the routed horizontal path.
      for(int i=0;i<points.Length;i++)if(Support295(points[i],field,query,out var hit))points[i]=hit.point;else unavailable++;
      if(points.Length<2||unavailable>0){lines.Add("FAIL "+route.id+(reverse?" reverse":" forward")+" navigation path missing physical support="+unavailable);continue;}
     }
     cc.enabled=false;go.transform.position=points[0]+Vector3.up*(cc.height*.5f-cc.center.y+.06f);go.SetActive(true);cc.enabled=true;Physics.SyncTransforms();
     int target=1,ticks=0,stalled=0;float nearest=float.PositiveInfinity;string failure=null;
     while(target<points.Length&&ticks++<160000)
     {
      var feet=cc.transform.TransformPoint(cc.center)-Vector3.up*cc.height*.5f;var d=points[target]-feet;float vertical=d.y;d.y=0;
      if(query.Immersion(feet)>(query.Rules!=null?query.Rules.MaximumWadingDepth:.55f)+.06f){failure="deep water "+feet.ToString("F2");break;}
      // Check falling before horizontal arrival so a lower shelf cannot count
      // as reaching a bridge deck or a stair above the player.
      if(feet.y<Mathf.Min(points[target-1].y,points[target].y)-2){failure="fall at "+feet.ToString("F2")+" target="+target+" dy="+vertical.ToString("F2");break;}
      if(target==points.Length-1&&arrival!=null&&EndpointVisible295(arrival,feet,cc,session,go.transform))
      {target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(d.magnitude<.18f)
      {
       if(target==points.Length-1&&arrival!=null&&!EndpointVisible295(arrival,feet,cc,session,go.transform)){failure="endpoint outside authored range or sight "+arrival.Id+" feet="+feet.ToString("F2");break;}
       target++;nearest=float.PositiveInfinity;stalled=0;continue;
      }
      if(d.magnitude<nearest-.015f){nearest=d.magnitude;stalled=0;}else stalled++;
      if(stalled>240){failure="stuck at "+feet.ToString("F2")+" target="+target+" dy="+vertical.ToString("F2");break;}
      var motion=Vector3.ClampMagnitude(d,4.5f/60);motion.y=-.10f;cc.Move(motion);
     }
     lines.Add((failure==null&&target==points.Length?"PASS ":"FAIL ")+route.id+(reverse?" reverse":" forward")+" moves="+ticks+" target="+target+"/"+points.Length+(failure!=null?" "+failure:""));
     if(failure!=null)lines.AddRange(WalkContact295(route,cc,points[Mathf.Min(target,points.Length-1)],field,query));
    }
   }
   finally{Object.DestroyImmediate(go);}
   lines.Add("Automated Edit movement only. Manual input, NPC/vehicle traversal and full progression are separate checks.");File.WriteAllLines(O295+"/walk-first-visit.txt",lines);return string.Join("\n",lines);
  }
  static string Navigation295(float voxel=.4f)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();var surfaces=Components295<NavMeshSurface>();var active=surfaces.Where(n=>n.isActiveAndEnabled).ToArray();
   var old=Root295("Watershed295_Navigation");var temp=new GameObject("Watershed295_Navigation_Pending");var nav=temp.AddComponent<NavMeshSurface>();
   nav.agentTypeID=surfaces.Length>0?surfaces[0].agentTypeID:0;nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
   nav.center=new Vector3(2000,300,3000);nav.size=new Vector3(4000,800,6000);nav.overrideVoxelSize=true;nav.voxelSize=voxel;nav.overrideTileSize=true;nav.tileSize=256;
   var dynamic=Components295<Collider>().Where(c=>c.enabled&&(c.attachedRigidbody!=null||c is CharacterController)).ToArray();var waterSources=new List<GameObject>();var bakeMeshes=new List<Mesh>();var bakeEvidence=new List<string>();bool installed=false;
   try
   {
    foreach(var n in active)n.RemoveData();foreach(var c in dynamic)c.enabled=false;
    var decks=BakeDeckFootprints295(waterSources,bakeMeshes,bakeEvidence);int clippedTriangles=0;
    // Temporary bake-only water surfaces prevent the submerged riverbed from
    // becoming walkable. They are removed before saving and never stop falling.
    foreach(var filter in Root295("Watershed295_Water").GetComponentsInChildren<MeshFilter>())
    {
     var mesh=WaterBakeMesh295(filter,decks,bakeMeshes,out int clipped);clippedTriangles+=clipped;if(mesh.vertexCount<3)continue;
     var go=new GameObject("Water_NotWalkable_BakeOnly");
     go.AddComponent<MeshCollider>().sharedMesh=mesh;var modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=1;waterSources.Add(go);
    }
    bakeEvidence.Add("INFO bake-water triangles clipped inside low permanent decks="+clippedTriangles+"; physical deck edge inset0.1m; runtime water unchanged");
    Physics.SyncTransforms();nav.BuildNavMesh();if(nav.navMeshData==null)throw new Exception("NavMesh returned no data");
    string path=A295+"/Navigation.asset";var data=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
    if(data==null)AssetDatabase.CreateAsset(nav.navMeshData,path);
    else{var built=nav.navMeshData;nav.RemoveData();EditorUtility.CopySerialized(built,data);nav.navMeshData=data;nav.AddData();Object.DestroyImmediate(built);EditorUtility.SetDirty(data);}
    foreach(var n in surfaces){n.RemoveData();n.enabled=false;}if(old!=null)Object.DestroyImmediate(old);temp.name="Watershed295_Navigation";installed=true;
    foreach(var go in waterSources)Object.DestroyImmediate(go);waterSources.Clear();foreach(var mesh in bakeMeshes)Object.DestroyImmediate(mesh);bakeMeshes.Clear();foreach(var c in dynamic)if(c!=null)c.enabled=true;Physics.SyncTransforms();
    var lines=new List<string>{"Watershed295 NavMesh from PhysicsColliders; voxel="+voxel.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"m; water bake geometry=NotWalkable; no retained solid water."};
    lines.AddRange(bakeEvidence);
    var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();
    foreach(var route in RequiredRoutes295())
    {
     int missing=0,incomplete=0;NavMeshHit previous=default;bool havePrevious=false;
     foreach(var p in Samples295(route.points,12))
     {
      if(!Support295(p,field,query,out var support)||!NavMesh.SamplePosition(support.point,out var hit,1.2f,NavMesh.AllAreas)){missing++;havePrevious=false;continue;}
      if(havePrevious){var pathResult=new NavMeshPath();if(!NavMesh.CalculatePath(previous.position,hit.position,NavMesh.AllAreas,pathResult)||pathResult.status!=NavMeshPathStatus.PathComplete)incomplete++;}previous=hit;havePrevious=true;
     }
     lines.Add((missing+incomplete==0?"PASS ":"FAIL ")+route.id+" nav samples missing="+missing+" adjacent incomplete="+incomplete);
    }
    lines.Add("Adjacent sampled navigation only; this does not prove full NPC/vehicle AI following.");Save292();File.WriteAllLines(O295+"/navigation.txt",lines);return string.Join("\n",lines);
   }
   finally
   {
    foreach(var go in waterSources)if(go!=null)Object.DestroyImmediate(go);foreach(var mesh in bakeMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);foreach(var c in dynamic)if(c!=null)c.enabled=true;
    if(!installed){var data=nav!=null?nav.navMeshData:null;if(temp!=null)Object.DestroyImmediate(temp);if(data!=null&&!AssetDatabase.Contains(data))Object.DestroyImmediate(data);foreach(var n in active)if(n!=null)n.AddData();}
   }
  }
  static string NavigationDiagnostics295()
  {
   var root=Root295("Watershed295_Navigation");if(root==null)throw new Exception("Bake candidate navigation before diagnostics");var nav=root.GetComponent<NavMeshSurface>();var settings=nav.GetBuildSettings();
   var lines=new List<string>{"Watershed295 read-only navigation diagnosis "+DateTime.Now.ToString("s"),
    "agentType="+settings.agentTypeID+" radius="+settings.agentRadius+" height="+settings.agentHeight+" slope="+settings.agentSlope+" climb="+settings.agentClimb+" minRegionArea="+settings.minRegionArea+" voxel="+settings.voxelSize+" tile="+settings.tileSize,
    "surface geometry="+nav.useGeometry+" collection="+nav.collectObjects+" center="+nav.center+" size="+nav.size};
   string PathOf(Transform t){var names=new List<string>();while(t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
   string Modifiers(Transform t)
   {var mods=new List<string>();while(t!=null){foreach(var m in t.GetComponents<NavMeshModifier>())mods.Add(PathOf(t)+" enabled="+m.isActiveAndEnabled+" agent="+m.AffectsAgentType(settings.agentTypeID)+" ignore="+m.ignoreFromBuild+" overrideArea="+m.overrideArea+" area="+m.area);t=t.parent;}return string.Join(" | ",mods);}
   var activeMods=Components295<NavMeshModifier>().Where(m=>m.isActiveAndEnabled).ToArray();lines.Add("active modifiers="+activeMods.Length);
   foreach(var m in activeMods.Where(m=>m.ignoreFromBuild||m.overrideArea))lines.Add("MODIFIER "+PathOf(m.transform)+" agent="+m.AffectsAgentType(settings.agentTypeID)+" ignore="+m.ignoreFromBuild+" overrideArea="+m.overrideArea+" area="+m.area);
   var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();var filter=new NavMeshQueryFilter{agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas};
   foreach(var route in RequiredRoutes295())
   {
    int missing=0,shown=0,incomplete=0;NavMeshHit previous=default;Vector3 previousSource=default;bool havePrevious=false;
    foreach(var p in Samples295(route.points,12))
    {
     bool physical=Support295(p,field,query,out var support);var point=physical?support.point:p;
     if(physical&&NavMesh.SamplePosition(point,out var current,1.2f,filter))
     {
      if(havePrevious)
      {
       var path=new NavMeshPath();if(!NavMesh.CalculatePath(previous.position,current.position,filter,path)||path.status!=NavMeshPathStatus.PathComplete)
       {incomplete++;lines.Add("GAP "+route.id+" physicalFrom="+previousSource.ToString("F3")+" physicalTo="+point.ToString("F3")+" navFrom="+previous.position.ToString("F3")+" navTo="+current.position.ToString("F3")+" status="+path.status+" corners="+string.Join(";",path.corners.Select(c=>c.ToString("F3")))+" targetCollider="+ColliderPath295(support.transform));}
      }
      previous=current;previousSource=point;havePrevious=true;continue;
     }
     havePrevious=false;missing++;if(shown++>=6)continue;
     bool near=NavMesh.SamplePosition(point,out var nearest,30,filter);
     lines.Add("MISSING "+route.id+" route="+p.ToString("F3")+" support="+physical+(physical?" hit="+support.point.ToString("F3")+" normal="+support.normal.ToString("F3")+" collider="+PathOf(support.collider.transform)+" bounds="+support.collider.bounds+" modifiers=["+Modifiers(support.collider.transform)+"]":"")+" nearestNav30="+near+(near?" position="+nearest.position.ToString("F3")+" distance="+nearest.distance.ToString("F3")+" dy="+(nearest.position.y-point.y).ToString("F3"):""));
     lines.AddRange(WalkContact295(route,Session292().Walker.Body,near?nearest.position:point+Vector3.forward,field,query,point));
    }
    lines.Add((missing+incomplete==0?"PASS ":"FAIL ")+route.id+" missing navigation samples="+missing+" adjacent incomplete="+incomplete);
   }
   File.WriteAllLines(O295+"/nav-diagnostics.txt",lines);return string.Join("\n",lines);
  }
  static MountainTrailProbe285 probe295;
  static WorldTerrainQuery probeWater295;
  static CharacterController probeBody295;
  static double probeStarted295;
  static string Perf295(string arg)
  {
   var args=arg.Split(':');string kind=args[0];if(kind!="shore"&&kind!="crossing"&&kind!="gorge")throw new ArgumentException("perf:shore|crossing|gorge[:noart]");
   if(SessionState.GetBool("Watershed295.Restore",false))throw new Exception("Previous 295 performance run still requires cleanup");
   string output=O295+"/Perf/"+kind+(args.Length>1&&args[1]=="noart"?"-noart":"");
   var previous=new[]{"frame-times.txt","walk-play.txt","provenance.json"}.Where(n=>File.Exists(output+"/"+n)).ToArray();
   if(previous.Length>0){string history=output+"/History/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");Directory.CreateDirectory(history);foreach(string name in previous)File.Move(output+"/"+name,history+"/"+name);}
   SessionState.SetBool("Watershed295.Perf",true);SessionState.SetString("Watershed295.PerfKind",kind);SessionState.SetBool("Watershed295.PerfNoArt",args.Length>1&&args[1]=="noart");
   SessionState.SetString("Watershed295.PerfPriorSuffix",Session292().TestSaveSuffix);
   SessionState.SetString("Watershed295.PerfPriorUi",SessionState.GetString("PlaytestUiReviewSuffix","__unset__"));
   SessionState.SetString("Watershed295.PerfPriorStartup",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
   SessionState.SetBool("Watershed295.PerfPriorDirect",SessionState.GetBool("Compact270.DirectPlay",false));
   SessionState.SetBool("Watershed295.PerfPriorDirectPresent",SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true));
   SessionState.SetBool("Watershed295.Restore",true);Session292().TestSaveSuffix="_perf295_test";SessionState.SetString("PlaytestUiReviewSuffix","_perf295_test");
   try{CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "295 actual Play probe scheduled in isolated _perf295_test slot: "+kind;}
   catch{RestorePerf295();throw;}
  }
  [InitializeOnLoadMethod] static void RegisterPerf295(){EditorApplication.playModeStateChanged-=PerfMode295;EditorApplication.playModeStateChanged+=PerfMode295;}
  static void PerfMode295(PlayModeStateChange state)
  {
   if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool("Watershed295.Restore",false))RestorePerf295();
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Watershed295.Perf",false))return;
   SessionState.SetBool("Watershed295.Perf",false);SessionState.SetBool("Watershed295.Restore",true);probe295=null;probeWater295=null;probeBody295=null;probeStarted295=EditorApplication.timeSinceStartup;EditorApplication.update-=PerfTick295;EditorApplication.update+=PerfTick295;
  }
  static void RestorePerfStartup295()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return;
   if(SessionState.GetBool("Watershed295.PerfPriorDirectPresent",false))SessionState.SetBool("Compact270.DirectPlay",SessionState.GetBool("Watershed295.PerfPriorDirect",false));else SessionState.EraseBool("Compact270.DirectPlay");
   string path=SessionState.GetString("Watershed295.PerfPriorStartup","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
  }
  static void RestorePerf295()
  {
   SessionState.SetBool("Watershed295.Perf",false);SessionState.SetBool("Watershed295.Restore",false);
   Session292().TestSaveSuffix=SessionState.GetString("Watershed295.PerfPriorSuffix","");string ui=SessionState.GetString("Watershed295.PerfPriorUi","__unset__");
   if(ui=="__unset__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",ui);
   RestorePerfStartup295();EditorApplication.delayCall+=RestorePerfStartup295;
   probe295=null;probeWater295=null;probeBody295=null;probeStarted295=0;EditorApplication.update-=PerfTick295;
  }
  static void PerfTick295()
  {
   if(!EditorApplication.isPlaying){EditorApplication.update-=PerfTick295;return;}
   string kind=SessionState.GetString("Watershed295.PerfKind","shore");string output=O295+"/Perf/"+kind+(SessionState.GetBool("Watershed295.PerfNoArt",false)?"-noart":"");
   try
   {
    if(probe295==null)
    {
     if(EditorApplication.timeSinceStartup-probeStarted295<5)return;
     var session=Session292();if(session.TestSaveSuffix!="_perf295_test")throw new Exception("Performance session did not retain its isolated save suffix");var field=new CompactWorldSurface(session.MountainLayout);var query=Components295<WorldTerrainQuery>().First();var routes=RequiredRoutes295();
     var views=CameraViews295().Views;var anchor=views[kind=="shore"?1:3].Eye;
     if(kind=="crossing")
     {var crossings=JsonUtility.FromJson<Crossings295>(File.ReadAllText(G295+"/crossings.json")).Crossings.Where(c=>c.Kind!="Mum"&&c.Kind!="AbilityGate").ToArray();if(crossings.Length==0)throw new Exception("No permanent crossing to profile");var crossing=crossings.OrderBy(c=>(c.Start-new Vector3(2100,0,4800)).sqrMagnitude).First();anchor=(crossing.Start+crossing.End)*.5f;}
     var selected=routes.OrderBy(r=>r.points.Min(p=>new Vector2(p.x-anchor.x,p.z-anchor.z).sqrMagnitude)).First();var all=Samples295(selected.points,.75f);int centre=Enumerable.Range(0,all.Length).OrderBy(i=>new Vector2(all[i].x-anchor.x,all[i].z-anchor.z).sqrMagnitude).First();
     int start=Mathf.Clamp(centre-80,0,Mathf.Max(0,all.Length-160));var points=all.Skip(start).Take(161).ToArray();
     for(int i=0;i<points.Length;i++){if(!Support295(points[i],field,query,out var support))throw new Exception("Probe path missing support "+points[i]);points[i]=support.point;if(query.Immersion(points[i])>.55f)throw new Exception("Probe path enters deep water "+points[i]);}
     var profile=ScriptableObject.CreateInstance<MountainTrailProfile>();profile.points=points;profile.widths=Enumerable.Repeat(selected.width,points.Length).ToArray();profile.distances=new float[points.Length];profile.types=new int[points.Length];profile.knots=Array.Empty<Vector3>();
     for(int i=1;i<points.Length;i++)profile.distances[i]=profile.distances[i-1]+Vector3.Distance(points[i-1],points[i]);profile.length=profile.distances.Last();profile.rise=points.Last().y-points.First().y;
     var cameraGo=new GameObject("Watershed295_perf_camera");var camera=cameraGo.AddComponent<Camera>();var source=Camera.main;if(source!=null){camera.CopyFrom(source);EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());}
     foreach(var c in Components295<Camera>())if(c!=camera)c.enabled=false;camera.enabled=true;camera.farClipPlane=Mathf.Max(4000,camera.farClipPlane);
     foreach(var art in Components295<CompactRebuildArtRenderer>()){art.Observer=camera;if(SessionState.GetBool("Watershed295.PerfNoArt",false))art.enabled=false;}
     if(SessionState.GetBool("Watershed295.PerfNoArt",false))foreach(var grass in Components295<CompactGrassRenderer266>())grass.enabled=false;
     var go=new GameObject("Watershed295_actual_Play_probe");var cc=go.AddComponent<CharacterController>();cc.enabled=false;var body=session.Walker.Body;cc.height=body.height;cc.center=body.center;cc.radius=body.radius;cc.skinWidth=body.skinWidth;cc.stepOffset=body.stepOffset;cc.slopeLimit=body.slopeLimit;
     go.transform.position=points[0]+Vector3.up*(cc.height*.5f-cc.center.y+.06f);probe295=go.AddComponent<MountainTrailProbe285>();probe295.Profile=profile;probe295.View=camera;probe295.Output=Path.GetFullPath(output);cc.enabled=true;probeBody295=cc;probeWater295=query;Application.runInBackground=true;
     Directory.CreateDirectory(output);File.WriteAllText(output+"/provenance.json",JsonUtility.ToJson(new PerfReceipt295{Kind=kind,Route=selected.id,Start=points.First(),End=points.Last(),Length=profile.length,Time=DateTime.Now.ToString("s"),Scene=SceneManager.GetActiveScene().path,SaveSuffix=session.TestSaveSuffix},true));return;
    }
    if(probe295.Result==null&&probeBody295!=null&&probeWater295!=null)
    {
     var feet=probeBody295.transform.TransformPoint(probeBody295.center)-Vector3.up*probeBody295.height*.5f;
     float depth=probeWater295.Immersion(feet),limit=probeWater295.Rules!=null?probeWater295.Rules.MaximumWadingDepth:.55f;
     int i=Mathf.Clamp(probe295.Target,1,probe295.Profile.points.Length-1);float floor=Mathf.Min(probe295.Profile.points[i-1].y,probe295.Profile.points[i].y);
     if(depth>limit+.06f)probe295.Result="FAIL actual Play probe entered deep water depth="+depth+" feet="+feet;
     else if(feet.y<floor-2)probe295.Result="FAIL actual Play probe fell below route support feet="+feet+" expected floor="+floor;
    }
    if(probe295.Result==null&&EditorApplication.timeSinceStartup-probeStarted295<240)return;
    Directory.CreateDirectory(output);File.WriteAllText(output+"/walk-play.txt",probe295.Result??"FAIL timeout 240s");EditorApplication.update-=PerfTick295;EditorApplication.isPlaying=false;
   }
   catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(output+"/walk-play.txt","FAIL probe setup/runtime: "+ex);EditorApplication.update-=PerfTick295;EditorApplication.isPlaying=false;}
  }
  [Serializable] sealed class PerfReceipt295{public string Kind,Route,Time,Scene,SaveSuffix;public Vector3 Start,End;public float Length;}
 }
}
