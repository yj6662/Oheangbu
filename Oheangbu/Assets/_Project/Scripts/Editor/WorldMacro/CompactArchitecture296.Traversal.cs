// Candidate-specific traversal diagnostics derived from validated #295; source remains unchanged.
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
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Route292[] RequiredRoutesQa296()
  {
   var layout=Session292().MountainLayout;var required=layout.Routes.Where(r=>r.Role==CompactRouteRole.Main&&string.IsNullOrEmpty(r.RequiredAbility)).ToArray();
   var exported=JsonUtility.FromJson<Routes292>(File.ReadAllText(G296+"/routes.json")).routes;var result=new List<Route292>();routeBindingsQa296.Clear();
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
    routeBindingsQa296.Add(route.Id+" from "+merged.id+" samples "+first+".."+last+" endpoint gaps="+fromGap.ToString("F2")+","+toGap.ToString("F2")+"m");
   }
   foreach(var arena in Sheet296().Arenas.Where(a=>a.TestOnly&&a.Approach!=null&&a.Approach.Length>1))
   {
    var approach=arena.Approach.Concat(new[]{arena.Entrance,arena.Centre}).ToArray();
    result.Add(new Route292{id=arena.Id+"_approach296",width=4,vehicle=false,points=approach});
   }
   if(File.Exists(O296+"/venue-complexes.json"))
   foreach(var path in JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(O296+"/venue-complexes.json")).Paths)
    if(path.Points!=null&&path.Points.Length>1)result.Add(new Route292{id="compound_"+path.Id,width=path.Width,vehicle=false,points=path.Points});
   return result.ToArray();
  }
  static readonly List<string> routeBindingsQa296=new List<string>();
  static bool ActorColliderQa296(Collider c)=>c.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()!=null||
   c.GetComponentInParent<Oheangbu.Combat.EnemyController>()!=null||c.GetComponentInParent<Oheangbu.App.Demo.CheongryongCombatController>()!=null||
   c.GetComponentInParent<Oheangbu.App.Demo.SouthGateGeneralController>()!=null;
  static bool SupportQa296(Vector3 p,CompactWorldSurface field,WorldTerrainQuery query,out RaycastHit support)
  {
   float expected=field.Sample(p.x,p.z);float top=Mathf.Max(expected,p.y);if(query.TryWaterHeight(p,out float water))top=Mathf.Max(top,water);
   var scene=SceneManager.GetActiveScene();
   RaycastHit[] Cast(Vector3 origin,float distance)=>Physics.RaycastAll(origin,Vector3.down,distance,~0,QueryTriggerInteraction.Ignore)
    .Where(h=>h.collider.gameObject.scene==scene&&h.collider.attachedRigidbody==null&&!(h.collider is CharacterController)&&!ActorColliderQa296(h.collider)&&h.collider.GetComponentInParent<WorldTemporarySupport>()==null&&h.normal.y>.55f).ToArray();
   // PhysX returns only the first face of a given MeshCollider. Casting from
   // above the roof hides its floor when both belong to a source asset. Query
   // the authored walking elevation first, like the runtime safe-feet query.
   var hits=Cast(p+Vector3.up*1.5f,4);
   if(hits.Length>0){support=hits.OrderBy(h=>h.distance).First();return true;}
   hits=Cast(new Vector3(p.x,top+80,p.z),240);
   var bridge=hits.Where(h=>h.collider.transform.root.name=="Architecture296_Crossings"&&Mathf.Abs(h.point.y-p.y)<=1.25f).OrderBy(h=>Mathf.Abs(h.point.y-p.y)).ToArray();
   if(bridge.Length>0){support=bridge[0];return true;}
   // Authored stair/boardwalk profiles are up to several metres above the
   // coarse heightfield. Match the route elevation, retaining those surfaces.
   if(hits.Length>0){support=hits.OrderBy(h=>Mathf.Abs(h.point.y-p.y)).First();return true;}
   support=default;return false;
  }
  static Vector3[] SamplesQa296(Vector3[] path,float spacing)
  {
   var result=new List<Vector3>();if(path.Length==0)return result.ToArray();result.Add(path[0]);
   for(int i=1;i<path.Length;i++){int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(path[i-1],path[i])/spacing));for(int k=1;k<=n;k++)result.Add(Vector3.Lerp(path[i-1],path[i],k/(float)n));}
   return result.ToArray();
  }
  static List<string> RouteSupportQa296()
  {
   var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();var result=new List<string>();
   var routes=RequiredRoutesQa296();var required=Session292().MountainLayout.Routes.Where(r=>r.Role==CompactRouteRole.Main&&string.IsNullOrEmpty(r.RequiredAbility)).Select(r=>r.Id).ToArray();
   var absent=required.Where(id=>!routes.Any(r=>r.id==id)).ToArray();result.Add((absent.Length==0&&routes.Length>0?"PASS ":"FAIL ")+"route export coverage="+routes.Length+" (first-visit main="+required.Length+", additional arena/compound="+(routes.Length-required.Length)+") missing="+string.Join(",",absent));
   result.AddRange(routeBindingsQa296.Select(b=>"INFO consolidated mountain route: "+b));
   foreach(var route in routes)
   {
    int samples=0,missing=0,deep=0,steep=0,bridged=0;Vector3 previous=Vector3.zero;bool hasPrevious=false;var detail=new List<string>();
    foreach(var p in SamplesQa296(route.points,3))
    {
     samples++;if(!SupportQa296(p,field,query,out var hit)){missing++;if(detail.Count<3)detail.Add("missing "+p.ToString("F1"));continue;}
     if(hit.collider.transform.root.name=="Architecture296_Crossings")bridged++;
     if(query.Immersion(hit.point)>(query.Rules!=null?query.Rules.MaximumWadingDepth:.55f)){deep++;if(detail.Count<3)detail.Add("deep "+hit.point.ToString("F1"));}
     if(hasPrevious){var d=hit.point-previous;float flat=new Vector2(d.x,d.z).magnitude;if(flat>.02f&&Mathf.Abs(d.y)>Session292().Walker.Body.stepOffset+.02f&&Mathf.Abs(d.y)/flat>1.05f){steep++;if(detail.Count<3)detail.Add("slope "+hit.point.ToString("F1")+" rise="+d.y.ToString("F3")+" run="+flat.ToString("F3")+" hit="+ScenePathVenue296(hit.collider.transform));}}previous=hit.point;hasPrevious=true;
    }
    result.Add((missing+deep+steep==0?"PASS ":"FAIL ")+"physical route support "+route.id+" samples="+samples+" bridge="+bridged+" missing="+missing+" deep="+deep+" steep="+steep+(detail.Count>0?" ["+string.Join(";",detail)+"]":""));
   }
   return result;
  }
  sealed class RouteEndpointQa296
  {public string Id,Kind;public Vector3 Target,SightTarget;public float Radius,ProjectileRange;public Transform Actor;}
  static RouteEndpointQa296 EndpointQa296(string routeId,bool start,Vector3 original,WorldMacroPlaytestSession session)
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
     if(boss!=null&&boss.Profile!=null&&flat<=boss.Profile.EngageRange)
     {
      var head=new SerializedObject(boss).FindProperty("_head")?.objectReferenceValue as Transform;
      return new RouteEndpointQa296{Id=actor.Id,Kind="authored engage range plus head projectile range/sight",Target=actor.transform.position,Radius=boss.Profile.EngageRange,Actor=actor.transform,
       SightTarget=head!=null?head.position:actor.transform.TransformPoint(boss.Profile.HeadOriginOffset),ProjectileRange=boss.Profile.ProjectileRange};
     }
     var profile=attacks!=null?new SerializedObject(attacks).FindProperty("_attackProfile")?.objectReferenceValue as Oheangbu.Combat.EnemyAttackProfileSO:null;
     float reach=boss!=null&&boss.Profile!=null?boss.Profile.BiteRange:general!=null&&general.Profile!=null?general.Profile.ThrustRange:profile!=null?profile.Range:attacks!=null?attacks.AttackRange:0;
     if(reach>0&&flat<=reach)
      return new RouteEndpointQa296{Id=actor.Id,Kind="authored combat reach",Target=actor.transform.position,Radius=reach,Actor=actor.transform};
    }
   }
   var ids=new[]{place.InteractionId}.Concat(place.InteractionIds??Array.Empty<string>()).Where(id=>!string.IsNullOrEmpty(id)).Distinct();
   var find=typeof(WorldMacroPlaytestSession).GetMethod("FindInteractionPoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   var points=ids.Select(id=>find?.Invoke(session,new object[]{id}) as PrologueContentSO.Point).Where(p=>p!=null&&p.Radius>0)
    .OrderBy(p=>new Vector2(p.Position.x-original.x,p.Position.z-original.z).sqrMagnitude);
   foreach(var point in points)
    if(Vector2.Distance(new Vector2(original.x,original.z),new Vector2(point.Position.x,point.Position.z))<=point.Radius)
     return new RouteEndpointQa296{Id=point.Id,Kind="CanInteract geometry",Target=point.Position,Radius=point.Radius};
   return null;
  }
  static bool EndpointVisibleQa296(RouteEndpointQa296 endpoint,Vector3 feet,CharacterController body,WorldMacroPlaytestSession session,Transform ignored=null,float margin=0)
  {
   Vector3 origin=feet+Vector3.up*(body.height*.5f-body.center.y+.06f);
   if(Vector3.Distance(origin,endpoint.Target)>endpoint.Radius-margin)return false;
   Vector3 eye=origin+Vector3.up*(endpoint.ProjectileRange>0?.8f:session.Walker.EyeHeight);
   Vector3 aim=endpoint.ProjectileRange>0?endpoint.SightTarget:endpoint.Target+Vector3.up*(endpoint.Actor==null?1.25f:.4f),to=aim-eye;
   if(endpoint.ProjectileRange>0&&to.magnitude>endpoint.ProjectileRange-margin)return false;
   foreach(var h in Physics.RaycastAll(eye,to.normalized,to.magnitude,~0,QueryTriggerInteraction.Ignore))
   {
    if(h.transform.IsChildOf(body.transform)||h.transform.IsChildOf(session.Walker.Body.transform)||(ignored!=null&&h.transform.IsChildOf(ignored)))continue;
    if(endpoint.Actor!=null&&h.transform.IsChildOf(endpoint.Actor))continue;
    var point=h.transform.GetComponentInParent<WorldMacroContentPoint>();if(point!=null&&point.Id==endpoint.Id)continue;
    return false;
   }
   return true;
  }
  static bool EndpointBodyClearQa296(Vector3 feet,CharacterController body,WorldMacroPlaytestSession session)
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
  static Vector3[] ApproachEndpointsQa296(Route292 route,Vector3[] source,WorldMacroPlaytestSession session,CompactWorldSurface field,WorldTerrainQuery query,CharacterController body,List<string> evidence,out RouteEndpointQa296 finalEndpoint)
  {
   var points=source;finalEndpoint=EndpointQa296(route.id,false,source.Last(),session);
   foreach(bool start in new[]{true,false})
   {
    var endpoint=start?EndpointQa296(route.id,true,source.First(),session):finalEndpoint;if(endpoint==null)continue;
    var ordered=start?points.Reverse().ToArray():points;int chosen=-1;
    // Trim only the final local approach to the authored interaction/encounter.
    // Every earlier route anchor and every obstacle stays in the path.
    for(int i=ordered.Length-1;i>=0;i--)
    {
     if(Vector3.Distance(ordered[i],ordered.Last())>endpoint.Radius*2+2)break;
     if(!SupportQa296(ordered[i],field,query,out var support)||query.Immersion(support.point)>.55f||!EndpointBodyClearQa296(support.point,body,session)||!EndpointVisibleQa296(endpoint,support.point,body,session,margin:.3f))continue;
     if(!NavMesh.SamplePosition(support.point,out _,1.2f,NavMesh.AllAreas))continue;chosen=i;
    }
    if(chosen<0){evidence.Add("INFO endpoint approach unavailable "+route.id+" "+(start?"start":"end")+" "+endpoint.Id+"; retained exact route for failure diagnosis");continue;}
    var approach=ordered[chosen];ordered=ordered.Take(chosen+1).ToArray();points=start?ordered.Reverse().ToArray():ordered;
    evidence.Add("INFO endpoint approach "+route.id+" "+(start?"start":"end")+" target="+endpoint.Id+" rule="+endpoint.Kind+" radius="+endpoint.Radius.ToString("F2")+" support="+approach.ToString("F3")+" trim="+Vector3.Distance(approach,start?source.First():source.Last()).ToString("F2")+"m sight=clear"+(endpoint.ProjectileRange>0?" head="+endpoint.SightTarget.ToString("F3")+" projectileRange="+endpoint.ProjectileRange.ToString("F2"):"")+"; progression state untested");
   }
   return points;
  }
  static string ColliderPathQa296(Transform t)
  {var names=new List<string>();while(t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
  static IEnumerable<string> WalkContactQa296(Route292 route,CharacterController body,Vector3 target,CompactWorldSurface field,WorldTerrainQuery query,Vector3? at=null)
  {
   var feet=at??(body.transform.TransformPoint(body.center)-Vector3.up*body.height*.5f);var center=feet+Vector3.up*body.height*.5f;
   var bottom=center-Vector3.up*(body.height*.5f-body.radius);var top=center+Vector3.up*(body.height*.5f-body.radius);
   var direction=target-feet;direction.y=0;direction.Normalize();
   var direct=SamplesQa296(route.points,.75f).OrderBy(p=>new Vector2(p.x-feet.x,p.z-feet.z).sqrMagnitude).First();
   yield return "  CONTACT route="+route.id+" feet="+feet.ToString("F3")+" target="+target.ToString("F3")+" authored nearest="+direct.ToString("F3")+" planarOffset="+Vector2.Distance(new Vector2(feet.x,feet.z),new Vector2(direct.x,direct.z)).ToString("F3")+" routeWidth="+route.width.ToString("F2");
   if(SupportQa296(feet,field,query,out var support))yield return "  SUPPORT "+ColliderPathQa296(support.transform)+" at="+support.point.ToString("F3")+" normal="+support.normal.ToString("F3");
   foreach(var collider in Physics.OverlapCapsule(bottom,top,body.radius+.12f,~0,QueryTriggerInteraction.Ignore).Where(c=>c!=body).Take(12))
    yield return "  OVERLAP "+ColliderPathQa296(collider.transform)+" type="+collider.GetType().Name+" bounds="+collider.bounds+
     ((collider is BoxCollider||collider is SphereCollider||collider is CapsuleCollider||(collider is MeshCollider mesh&&mesh.convex))?" closest="+collider.ClosestPoint(center).ToString("F3"):" closest=unsupported; use actual SUPPORT/AHEAD ray contacts");
   foreach(var hit in Physics.CapsuleCastAll(bottom,top,body.radius,direction,2,~0,QueryTriggerInteraction.Ignore).Where(h=>h.collider!=body).OrderBy(h=>h.distance).Take(12))
    yield return "  AHEAD "+ColliderPathQa296(hit.transform)+" distance="+hit.distance.ToString("F3")+" at="+hit.point.ToString("F3")+" normal="+hit.normal.ToString("F3");
   if(NavMesh.SamplePosition(feet,out var nav,3,NavMesh.AllAreas))yield return "  NAV nearest="+nav.position.ToString("F3")+" distance="+nav.distance.ToString("F3");
  }
  static string WalkQa296()
  {
   var session=Session292();var field=new CompactWorldSurface(session.MountainLayout);var query=Components295<WorldTerrainQuery>().First();var source=session.Walker.Body;
   var go=new GameObject("Architecture296_first_visit_walker"){hideFlags=HideFlags.HideAndDontSave};go.SetActive(false);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.center=source.center;cc.radius=source.radius;cc.skinWidth=source.skinWidth;cc.stepOffset=source.stepOffset;cc.slopeLimit=source.slopeLimit;
   bool hasCandidateNav=Root296("Architecture296_Navigation")!=null;
   var lines=new List<string>{"Architecture296 Edit CharacterController first-visit routes; actual player capsule; no Mum bridge is created. "+DateTime.Now.ToString("s"),
    "INFO candidate PhysicsColliders NavMesh="+hasCandidateNav+"; endpoint approaches use authored interaction/combat radius and sight; gates retain their current progression state.",
    "INFO copied body height="+cc.height+" radius="+cc.radius+" centre="+cc.center+" stepOffset="+cc.stepOffset+" slopeLimit="+cc.slopeLimit+" skinWidth="+cc.skinWidth+"; routed Nav XZ is recast onto actual physical support before movement."};
   try
   {
    foreach(var route in RequiredRoutesQa296())foreach(bool reverse in new[]{false,true})
    {
     var points=SamplesQa296(route.points,1.5f);int unavailable=0;
     RouteEndpointQa296 arrival=null;
     if(hasCandidateNav){points=ApproachEndpointsQa296(route,points,session,field,query,cc,lines,out arrival);if(reverse)arrival=EndpointQa296(route.id,true,route.points.First(),session);}
     if(reverse)Array.Reverse(points);
     for(int i=0;i<points.Length;i++)if(SupportQa296(points[i],field,query,out var hit))points[i]=hit.point;else unavailable++;
     if(points.Length<2||unavailable>0){lines.Add("FAIL "+route.id+" missing physical route support="+unavailable);continue;}
     if(hasCandidateNav)
     {
      var routed=new List<Vector3>();var anchors=points.Where((p,i)=>i==0||i==points.Length-1||i%12==0).ToArray();string navFailure=null;
      for(int i=0;i<anchors.Length;i++)
      {
       if(!SupportQa296(anchors[i],field,query,out var anchorSupport)||!NavMesh.SamplePosition(anchorSupport.point,out var navHit,1.2f,NavMesh.AllAreas)){navFailure="no navigation near route anchor "+i;break;}
       if(routed.Count==0){routed.Add(navHit.position);continue;}var path=new NavMeshPath();
       if(!NavMesh.CalculatePath(routed.Last(),navHit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete){navFailure="incomplete navigation near route anchor "+i;break;}
       ObserveDynamicPathQa296(route.id,path.corners);
       routed.AddRange(path.corners.Skip(1));
      }
      if(navFailure!=null){lines.Add("FAIL "+route.id+(reverse?" reverse":" forward")+" "+navFailure+"; gate/progression state unchanged");continue;}
      points=SamplesQa296(routed.ToArray(),1.5f);unavailable=0;
      // Recast's voxel surface can sit above the collider. The controller must
      // follow the collider elevation while retaining the routed horizontal path.
      for(int i=0;i<points.Length;i++)if(SupportQa296(points[i],field,query,out var hit))points[i]=hit.point;else unavailable++;
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
      if(target==points.Length-1&&arrival!=null&&EndpointVisibleQa296(arrival,feet,cc,session,go.transform))
      {target++;nearest=float.PositiveInfinity;stalled=0;continue;}
      if(d.magnitude<.18f)
      {
       if(target==points.Length-1&&arrival!=null&&!EndpointVisibleQa296(arrival,feet,cc,session,go.transform)){failure="endpoint outside authored range or sight "+arrival.Id+" feet="+feet.ToString("F2");break;}
       target++;nearest=float.PositiveInfinity;stalled=0;continue;
      }
      if(d.magnitude<nearest-.015f){nearest=d.magnitude;stalled=0;}else stalled++;
      if(stalled>240){failure="stuck at "+feet.ToString("F2")+" target="+target+" dy="+vertical.ToString("F2");break;}
      var motion=Vector3.ClampMagnitude(d,4.5f/60);motion.y=-.10f;cc.Move(motion);
     }
     lines.Add((failure==null&&target==points.Length?"PASS ":"FAIL ")+route.id+(reverse?" reverse":" forward")+" moves="+ticks+" target="+target+"/"+points.Length+(failure!=null?" "+failure:""));
     if(failure!=null)lines.AddRange(WalkContactQa296(route,cc,points[Mathf.Min(target,points.Length-1)],field,query));
    }
   }
   finally{Object.DestroyImmediate(go);}
   lines.Add("Automated Edit movement only. Manual input, NPC/vehicle traversal and full progression are separate checks.");File.WriteAllLines(O296+"/walk-first-visit.txt",lines);return string.Join("\n",lines);
  }
  static string NavigationQa296(float voxel=.2f)
  {
   RequireClean292();var scene=SceneManager.GetActiveScene();var surfaces=Components295<NavMeshSurface>();var active=surfaces.Where(n=>n.isActiveAndEnabled).ToArray();
   using var gateScope=new GateProbeScope296();
   var old=Root296("Architecture296_Navigation");var temp=new GameObject("Architecture296_Navigation_Pending");var nav=temp.AddComponent<NavMeshSurface>();
   nav.agentTypeID=surfaces.Length>0?surfaces[0].agentTypeID:0;nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
   nav.center=new Vector3(2000,300,3000);nav.size=new Vector3(4000,800,6000);nav.overrideVoxelSize=true;nav.voxelSize=voxel;nav.overrideTileSize=true;nav.tileSize=256;nav.buildHeightMesh=true;
   var dynamic=Components295<Collider>().Where(c=>c.enabled&&(c.attachedRigidbody!=null||c is CharacterController||ActorColliderQa296(c))).ToArray();var waterSources=new List<GameObject>();var bakeMeshes=new List<Mesh>();var bakeEvidence=new List<string>();bool installed=false;
   try
   {
    foreach(var n in active)n.RemoveData();foreach(var c in dynamic)c.enabled=false;
    var decks=BakeDeckFootprintsQa296(waterSources,bakeMeshes,bakeEvidence);int clippedTriangles=0;
    // Temporary bake-only water surfaces prevent the submerged riverbed from
    // becoming walkable. They are removed before saving and never stop falling.
    foreach(var filter in Root296("Watershed295_Water").GetComponentsInChildren<MeshFilter>())
    {
     var mesh=WaterBakeMeshQa296(filter,decks,bakeMeshes,out int clipped);clippedTriangles+=clipped;if(mesh.vertexCount<3)continue;
     var go=new GameObject("Water_NotWalkable_BakeOnly");
     go.AddComponent<MeshCollider>().sharedMesh=mesh;var modifier=go.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=1;waterSources.Add(go);
    }
    bakeEvidence.Add("INFO bake-water triangles clipped inside low permanent decks="+clippedTriangles+"; physical deck edge inset0.1m; runtime water unchanged");
    Physics.SyncTransforms();nav.BuildNavMesh();if(nav.navMeshData==null)throw new Exception("NavMesh returned no data");
    string path=A296+"/Navigation.asset";var data=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
    if(data==null)AssetDatabase.CreateAsset(nav.navMeshData,path);
    else{var built=nav.navMeshData;nav.RemoveData();EditorUtility.CopySerialized(built,data);nav.navMeshData=data;nav.AddData();Object.DestroyImmediate(built);EditorUtility.SetDirty(data);}
    foreach(var n in surfaces){n.RemoveData();n.enabled=false;}if(old!=null)Object.DestroyImmediate(old);temp.name="Architecture296_Navigation";installed=true;
    foreach(var go in waterSources)Object.DestroyImmediate(go);waterSources.Clear();foreach(var mesh in bakeMeshes)Object.DestroyImmediate(mesh);bakeMeshes.Clear();foreach(var c in dynamic)if(c!=null)c.enabled=true;Physics.SyncTransforms();
    var lines=new List<string>{"Architecture296 NavMesh from PhysicsColliders; voxel="+voxel.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"m; water bake geometry=NotWalkable; no retained solid water."};
    lines.Add("INFO buildHeightMesh=true; original agent radius/climb/slope unchanged; moving/combat actor colliders excluded only during bake="+dynamic.Length+" then restored. Static293 walkway colliders unchanged.");
    lines.AddRange(bakeEvidence);
    var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();
    foreach(var route in RequiredRoutesQa296())
    {
     int missing=0,incomplete=0;NavMeshHit previous=default;bool havePrevious=false;
     var approach=ApproachEndpointsQa296(route,SamplesQa296(route.points,1.5f),Session292(),field,query,Session292().Walker.Body,lines,out _);
     foreach(var p in SamplesQa296(approach,12))
     {
      if(!SupportQa296(p,field,query,out var support)||!NavMesh.SamplePosition(support.point,out var hit,1.2f,NavMesh.AllAreas)){missing++;havePrevious=false;continue;}
      if(havePrevious){var pathResult=new NavMeshPath();if(!NavMesh.CalculatePath(previous.position,hit.position,NavMesh.AllAreas,pathResult)||pathResult.status!=NavMeshPathStatus.PathComplete)incomplete++;}previous=hit;havePrevious=true;
     }
     lines.Add((missing+incomplete==0?"PASS ":"FAIL ")+route.id+" nav samples missing="+missing+" adjacent incomplete="+incomplete);
    }
    lines.Add("Adjacent sampled navigation with all durable gates in a temporary open probe state. Closed-perimeter checks are separate.");gateScope.Dispose();Save292();File.WriteAllLines(O296+"/navigation.txt",lines);return string.Join("\n",lines);
   }
   finally
   {
    foreach(var go in waterSources)if(go!=null)Object.DestroyImmediate(go);foreach(var mesh in bakeMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);foreach(var c in dynamic)if(c!=null)c.enabled=true;
    if(!installed){var data=nav!=null?nav.navMeshData:null;if(temp!=null)Object.DestroyImmediate(temp);if(data!=null&&!AssetDatabase.Contains(data))Object.DestroyImmediate(data);foreach(var n in active)if(n!=null)n.AddData();}
   }
  }
  static string NavigationDiagnosticsQa296()
  {
   var root=Root296("Architecture296_Navigation");if(root==null)throw new Exception("Bake candidate navigation before diagnostics");var nav=root.GetComponent<NavMeshSurface>();var settings=nav.GetBuildSettings();
   var lines=new List<string>{"Architecture296 read-only navigation diagnosis "+DateTime.Now.ToString("s"),
    "agentType="+settings.agentTypeID+" radius="+settings.agentRadius+" height="+settings.agentHeight+" slope="+settings.agentSlope+" climb="+settings.agentClimb+" minRegionArea="+settings.minRegionArea+" voxel="+settings.voxelSize+" tile="+settings.tileSize,
    "surface geometry="+nav.useGeometry+" collection="+nav.collectObjects+" center="+nav.center+" size="+nav.size};
   string PathOf(Transform t){var names=new List<string>();while(t!=null){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
   string Modifiers(Transform t)
   {var mods=new List<string>();while(t!=null){foreach(var m in t.GetComponents<NavMeshModifier>())mods.Add(PathOf(t)+" enabled="+m.isActiveAndEnabled+" agent="+m.AffectsAgentType(settings.agentTypeID)+" ignore="+m.ignoreFromBuild+" overrideArea="+m.overrideArea+" area="+m.area);t=t.parent;}return string.Join(" | ",mods);}
   var activeMods=Components295<NavMeshModifier>().Where(m=>m.isActiveAndEnabled).ToArray();lines.Add("active modifiers="+activeMods.Length);
   foreach(var m in activeMods.Where(m=>m.ignoreFromBuild||m.overrideArea))lines.Add("MODIFIER "+PathOf(m.transform)+" agent="+m.AffectsAgentType(settings.agentTypeID)+" ignore="+m.ignoreFromBuild+" overrideArea="+m.overrideArea+" area="+m.area);
   var field=new CompactWorldSurface(Session292().MountainLayout);var query=Components295<WorldTerrainQuery>().First();var filter=new NavMeshQueryFilter{agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas};
   foreach(var route in RequiredRoutesQa296())
   {
    int missing=0,shown=0,incomplete=0;NavMeshHit previous=default;Vector3 previousSource=default;bool havePrevious=false;
    var approach=ApproachEndpointsQa296(route,SamplesQa296(route.points,1.5f),Session292(),field,query,Session292().Walker.Body,lines,out _);
    foreach(var p in SamplesQa296(approach,12))
    {
     bool physical=SupportQa296(p,field,query,out var support);var point=physical?support.point:p;
     if(physical&&NavMesh.SamplePosition(point,out var current,1.2f,filter))
     {
      if(havePrevious)
      {
       var path=new NavMeshPath();if(!NavMesh.CalculatePath(previous.position,current.position,filter,path)||path.status!=NavMeshPathStatus.PathComplete)
       {
        incomplete++;lines.Add("GAP "+route.id+" physicalFrom="+previousSource.ToString("F3")+" physicalTo="+point.ToString("F3")+" navFrom="+previous.position.ToString("F3")+" navTo="+current.position.ToString("F3")+" status="+path.status+" corners="+string.Join(";",path.corners.Select(c=>c.ToString("F3")))+" targetCollider="+ColliderPathQa296(support.transform));
        lines.AddRange(WalkContactQa296(route,Session292().Walker.Body,point,field,query,previousSource));
        lines.AddRange(WalkContactQa296(route,Session292().Walker.Body,previousSource,field,query,point));
       }
       if(path.status==NavMeshPathStatus.PathComplete)ObserveDynamicPathQa296(route.id,path.corners);
      }
      previous=current;previousSource=point;havePrevious=true;continue;
     }
     havePrevious=false;missing++;if(shown++>=6)continue;
     bool near=NavMesh.SamplePosition(point,out var nearest,30,filter);
     lines.Add("MISSING "+route.id+" route="+p.ToString("F3")+" support="+physical+(physical?" hit="+support.point.ToString("F3")+" normal="+support.normal.ToString("F3")+" collider="+PathOf(support.collider.transform)+" bounds="+support.collider.bounds+" modifiers=["+Modifiers(support.collider.transform)+"]":"")+" nearestNav30="+near+(near?" position="+nearest.position.ToString("F3")+" distance="+nearest.distance.ToString("F3")+" dy="+(nearest.position.y-point.y).ToString("F3"):""));
     lines.AddRange(WalkContactQa296(route,Session292().Walker.Body,near?nearest.position:point+Vector3.forward,field,query,point));
    }
    lines.Add((missing+incomplete==0?"PASS ":"FAIL ")+route.id+" missing navigation samples="+missing+" adjacent incomplete="+incomplete);
   }
   File.WriteAllLines(O296+"/nav-diagnostics.txt",lines);return string.Join("\n",lines);
  }
  [Serializable] sealed class TraversalRequestQa296
  {public string Command,Status,Error,ResultPath,QueryMode,NavDataSha256;public bool Active,Finished,Restored;public int NavigationPlayerLoops;public double Began;}
  static TraversalRequestQa296 traversalRequestQa296;
  static GateProbeScope296 traversalGatesQa296;
  [Serializable] sealed class TraversalDynamicObstacleQa296
  {
   public string Path,Shape;public Vector3 Position,LocalCentre,LocalSize,LossyScale;public Bounds WorldBounds,ExpandedBodyBounds;
   public float MinimumAuthoredBodyClearance=float.MaxValue,MinimumNavBodyClearance=float.MaxValue;
   public int AuthoredSegments,NavSegments,AuthoredIntersections,NavIntersections;
   public List<string> Intersections=new List<string>();
  }
  [Serializable] sealed class TraversalDynamicEvidenceQa296
  {public string Scope;public bool Passed;public TraversalDynamicObstacleQa296[] Obstacles;}
  static TraversalDynamicObstacleQa296[] traversalDynamicQa296=Array.Empty<TraversalDynamicObstacleQa296>();
  static float SegmentBoundsDistanceQa296(Vector3 a,Vector3 b,Bounds bounds)
  {
   var d=b-a;float length=d.magnitude;
   if(bounds.Contains(a)||bounds.Contains(b)||(length>.00001f&&bounds.IntersectRay(new Ray(a,d/length),out float t)&&t<=length))return 0;
   // Squared distance to a convex box is convex along a line segment.
   float lo=0,hi=1;for(int k=0;k<32;k++){float l=(lo*2+hi)/3,r=(lo+hi*2)/3;if(bounds.SqrDistance(Vector3.Lerp(a,b,l))<bounds.SqrDistance(Vector3.Lerp(a,b,r)))hi=r;else lo=l;}
   return Mathf.Sqrt(Mathf.Min(bounds.SqrDistance(a),bounds.SqrDistance(b),bounds.SqrDistance(Vector3.Lerp(a,b,(lo+hi)*.5f))));
  }
  static void ObserveDynamicPathQa296(string route,Vector3[] points,bool authored=false)
  {
   float halfHeight=Session292().Walker.Body.height*.5f;
   foreach(var obstacle in traversalDynamicQa296)for(int i=1;i<points.Length;i++)
   {
    float distance=SegmentBoundsDistanceQa296(points[i-1]+Vector3.up*halfHeight,points[i]+Vector3.up*halfHeight,obstacle.ExpandedBodyBounds);
    if(authored){obstacle.AuthoredSegments++;obstacle.MinimumAuthoredBodyClearance=Mathf.Min(obstacle.MinimumAuthoredBodyClearance,distance);if(distance<=.0001f)obstacle.AuthoredIntersections++;}
    else{obstacle.NavSegments++;obstacle.MinimumNavBodyClearance=Mathf.Min(obstacle.MinimumNavBodyClearance,distance);if(distance<=.0001f){obstacle.NavIntersections++;if(obstacle.Intersections.Count<8)obstacle.Intersections.Add(route+": "+points[i-1].ToString("F3")+" -> "+points[i].ToString("F3"));}}
   }
  }
  static List<string> DynamicEvidenceQa296()
  {
   var lines=new List<string>{"INFO Open-gate baked NavData snapshot. Moving vehicle carving is excluded from the static query; all physical colliders and obstacle enabled states remain unchanged. Every complete NavPath segment is independently tested against its current body-expanded world bounds."};
   foreach(var o in traversalDynamicQa296)
    lines.Add((o.NavSegments>0&&o.NavIntersections==0?"PASS ":"FAIL ")+"dynamic carve clearance "+o.Path+" bounds="+o.WorldBounds+" bodyExpanded="+o.ExpandedBodyBounds+" authoredSegments="+o.AuthoredSegments+" authoredHits="+o.AuthoredIntersections+" authoredMin="+o.MinimumAuthoredBodyClearance.ToString("F3")+"m navSegments="+o.NavSegments+" navHits="+o.NavIntersections+" navMin="+o.MinimumNavBodyClearance.ToString("F3")+"m "+string.Join(";",o.Intersections));
   File.WriteAllText(O296+"/traversal-dynamic-obstacles.json",JsonUtility.ToJson(new TraversalDynamicEvidenceQa296{Scope=lines[0],Passed=traversalDynamicQa296.All(o=>o.NavSegments>0&&o.NavIntersections==0),Obstacles=traversalDynamicQa296},true));
   return lines;
  }
  sealed class TraversalNavRegistrationQa296:IDisposable
  {
   readonly NavMeshSurface surface;readonly NavMeshData data;readonly string path,hash;bool restored;
   public TraversalNavRegistrationQa296()
   {
    var root=Root296("Architecture296_Navigation");surface=root!=null?root.GetComponent<NavMeshSurface>():null;
    if(surface==null||!surface.isActiveAndEnabled||surface.navMeshData==null)throw new InvalidOperationException("Active candidate navigation data required");
    if(Components295<NavMeshSurface>().Any(n=>n!=surface&&n.isActiveAndEnabled&&n.navMeshData!=null))throw new InvalidOperationException("Unexpected additional active navigation surface; refusing ambiguous snapshot query");
    var carving=Components295<NavMeshObstacle>().Where(o=>o.isActiveAndEnabled&&o.carving).ToArray();
    var unexpected=carving.Where(o=>o.GetComponentInParent<Oheangbu.App.World.Vehicle.WorldMacroPalanquinController>()==null).ToArray();
    if(unexpected.Length>0)throw new InvalidOperationException("Open-gate snapshot cannot discard unclassified active carving: "+string.Join(", ",unexpected.Select(o=>ColliderPathQa296(o.transform))));
    traversalDynamicQa296=carving.Select(o=>
    {
     Vector3 size=o.shape==NavMeshObstacleShape.Box?o.size:new Vector3(o.radius*2,o.height,o.radius*2);
     var bounds=new Bounds(o.transform.TransformPoint(o.center),Vector3.zero);
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f})foreach(float z in new[]{-.5f,.5f})bounds.Encapsulate(o.transform.TransformPoint(o.center+Vector3.Scale(size,new Vector3(x,y,z))));
     var expanded=bounds;var body=Session292().Walker.Body;expanded.Expand(new Vector3(body.radius*2,body.height,body.radius*2));
     return new TraversalDynamicObstacleQa296{Path=ColliderPathQa296(o.transform),Shape=o.shape.ToString(),Position=o.transform.position,LocalCentre=o.center,LocalSize=size,LossyScale=o.transform.lossyScale,WorldBounds=bounds,ExpandedBodyBounds=expanded};
    }).ToArray();
    foreach(var route in RequiredRoutesQa296())ObserveDynamicPathQa296(route.id,route.points,true);
    data=surface.navMeshData;path=AssetDatabase.GetAssetPath(data);
    if(path!=A296+"/Navigation.asset")throw new InvalidOperationException("Unexpected candidate NavData asset: "+path);
    hash=SurfaceSha296(path);traversalRequestQa296.NavDataSha256=hash;
    // Carving belongs to a live data instance. Register the unchanged baked
    // asset afresh after all durable doors have opened, then query within this
    // same editor callback. No native player-loop scheduling is assumed.
    surface.RemoveData();surface.AddData();
   }
   public void Dispose()
   {
    if(restored)return;restored=true;
    if(surface==null||surface.navMeshData!=data)throw new InvalidOperationException("Candidate NavData reference changed during snapshot query");
    surface.RemoveData();surface.AddData();
    if(SurfaceSha296(path)!=hash)throw new InvalidOperationException("Saved NavData changed during read-only snapshot query");
   }
  }
  static TraversalNavRegistrationQa296 traversalNavQa296;
  static SouthGateDoorPresentation[] traversalGateObjectsQa296;
  static bool[] traversalGateRequestsQa296;
  static void PersistTraversalQa296()=>File.WriteAllText(O296+"/traversal-status.json",JsonUtility.ToJson(traversalRequestQa296,true));
  static string ScheduleTraversalQa296(string command)
  {
   if(command=="status")return traversalRequestQa296==null?"No traversal request":JsonUtility.ToJson(traversalRequestQa296,true);
   if(command=="abort"){AbortTraversalQa296();return "Traversal request aborted; gate request states restored.";}
   if(command!="walk"&&command!="nav-diagnose")throw new ArgumentException(command);
   if(traversalRequestQa296?.Active==true)throw new InvalidOperationException("A gate-open traversal query is already pending");
   RequireClean292();if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene296)throw new InvalidOperationException("Saved candidate Edit scene required");
   traversalRequestQa296=new TraversalRequestQa296{Command=command,Status="Queued for open-gate baked-data snapshot",QueryMode="Unchanged baked NavData re-registered for synchronous Edit query; live carving progression tested separately",Active=true,Began=EditorApplication.timeSinceStartup};
   try
   {
    EditorApplication.update+=TraversalTickQa296;AssemblyReloadEvents.beforeAssemblyReload+=AbortTraversalQa296;PersistTraversalQa296();EditorApplication.QueuePlayerLoopUpdate();
    return "Scheduled "+command+"; synchronous open-gate baked-data snapshot. Poll "+O296+"/traversal-status.json; no save/progression mutation.";
   }
   catch{AbortTraversalQa296();throw;}
  }
  static void AbortTraversalQa296()
  {
   if(traversalRequestQa296?.Active!=true)return;
   traversalRequestQa296.Status="Aborted";traversalRequestQa296.Error="Traversal aborted before completion";FinishTraversalQa296();
  }
  static void FinishTraversalQa296()
  {
   EditorApplication.update-=TraversalTickQa296;AssemblyReloadEvents.beforeAssemblyReload-=AbortTraversalQa296;
   try
   {
    try{traversalGatesQa296?.Dispose();}finally{traversalNavQa296?.Dispose();}
    if(traversalGateObjectsQa296!=null&&traversalGateObjectsQa296.Where((g,i)=>g==null||g.IsOpenRequested!=traversalGateRequestsQa296[i]).Any())throw new InvalidOperationException("Gate request restoration mismatch");
    traversalRequestQa296.Restored=true;
   }
   catch(Exception e){traversalRequestQa296.Status="FAIL";traversalRequestQa296.Error+=(string.IsNullOrEmpty(traversalRequestQa296.Error)?"":"\n")+"Restoration: "+e;}
   finally{traversalGatesQa296=null;traversalNavQa296=null;traversalGateObjectsQa296=null;traversalGateRequestsQa296=null;traversalRequestQa296.Active=false;traversalRequestQa296.Finished=true;PersistTraversalQa296();EditorApplication.QueuePlayerLoopUpdate();}
  }
  static void TraversalTickQa296()
  {
   if(traversalRequestQa296?.Active!=true)return;
   try
   {
    if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene296)throw new InvalidOperationException("Scene/play mode changed while traversal query waited");
    EditorApplication.update-=TraversalTickQa296;traversalRequestQa296.Status="Running";PersistTraversalQa296();
    traversalGateObjectsQa296=Components295<SouthGateDoorPresentation>().Where(g=>g.isActiveAndEnabled).ToArray();
    traversalGateRequestsQa296=traversalGateObjectsQa296.Select(g=>g.IsOpenRequested).ToArray();traversalGatesQa296=new GateProbeScope296();
    if(traversalGateObjectsQa296.Any(g=>!g.IsOpenRequested))throw new InvalidOperationException("Durable gate failed to enter open probe state");
    traversalNavQa296=new TraversalNavRegistrationQa296();PersistTraversalQa296();
    string result=traversalRequestQa296.Command=="walk"?WalkQa296():NavigationDiagnosticsQa296();
    traversalRequestQa296.ResultPath=O296+(traversalRequestQa296.Command=="walk"?"/walk-first-visit.txt":"/nav-diagnostics.txt");
    var dynamicEvidence=DynamicEvidenceQa296();File.AppendAllLines(traversalRequestQa296.ResultPath,dynamicEvidence);result+="\n"+string.Join("\n",dynamicEvidence);
    traversalRequestQa296.Status=result.Split('\n').Any(line=>line.StartsWith("FAIL ",StringComparison.Ordinal))?"FAIL":"PASS";
   }
   catch(Exception e){traversalRequestQa296.Status="FAIL";traversalRequestQa296.Error=e.ToString();}
   finally{FinishTraversalQa296();}
  }
  static MountainTrailProbe285 probeQa296;
  static WorldTerrainQuery probeWaterQa296;
  static CharacterController probeBodyQa296;
  static double probeStartedQa296;
  static string PerfQa296(string arg)
  {
   var args=arg.Split(':');string kind=args[0];if(!new[]{"shore","crossing","gorge","gate","cheolong","hwanggyeong"}.Contains(kind))throw new ArgumentException("perf:shore|crossing|gorge|gate|cheolong|hwanggyeong[:noart]");
   if(SessionState.GetBool("Architecture296.Restore",false))throw new Exception("Previous Qa296 performance run still requires cleanup");
   string output=O296+"/Perf/"+kind+(args.Length>1&&args[1]=="noart"?"-noart":"");
   var previous=new[]{"frame-times.txt","walk-play.txt","provenance.json"}.Where(n=>File.Exists(output+"/"+n)).ToArray();
   if(previous.Length>0){string history=output+"/History/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");Directory.CreateDirectory(history);foreach(string name in previous)File.Move(output+"/"+name,history+"/"+name);}
   SessionState.SetBool("Architecture296.Perf",true);SessionState.SetString("Architecture296.PerfKind",kind);SessionState.SetBool("Architecture296.PerfNoArt",args.Length>1&&args[1]=="noart");
   SessionState.SetString("Architecture296.PerfPriorSuffix",Session292().TestSaveSuffix);
   SessionState.SetString("Architecture296.PerfPriorUi",SessionState.GetString("PlaytestUiReviewSuffix","__unset__"));
   SessionState.SetString("Architecture296.PerfPriorStartup",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
   SessionState.SetBool("Architecture296.PerfPriorDirect",SessionState.GetBool("Compact270.DirectPlay",false));
   SessionState.SetBool("Architecture296.PerfPriorDirectPresent",SessionState.GetBool("Compact270.DirectPlay",false)==SessionState.GetBool("Compact270.DirectPlay",true));
   SessionState.SetBool("Architecture296.Restore",true);Session292().TestSaveSuffix="_perfQa296_test";SessionState.SetString("PlaytestUiReviewSuffix","_perfQa296_test");
   try{CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "Qa296 actual Play probe scheduled in isolated _perfQa296_test slot: "+kind;}
   catch{RestorePerfQa296();throw;}
  }
  [InitializeOnLoadMethod] static void RegisterPerfQa296(){EditorApplication.playModeStateChanged-=PerfModeQa296;EditorApplication.playModeStateChanged+=PerfModeQa296;}
  static void PerfModeQa296(PlayModeStateChange state)
  {
   if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool("Architecture296.Restore",false))RestorePerfQa296();
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Architecture296.Perf",false))return;
   SessionState.SetBool("Architecture296.Perf",false);SessionState.SetBool("Architecture296.Restore",true);probeQa296=null;probeWaterQa296=null;probeBodyQa296=null;probeStartedQa296=EditorApplication.timeSinceStartup;EditorApplication.update-=PerfTickQa296;EditorApplication.update+=PerfTickQa296;
  }
  static void RestorePerfStartupQa296()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)return;
   if(SessionState.GetBool("Architecture296.PerfPriorDirectPresent",false))SessionState.SetBool("Compact270.DirectPlay",SessionState.GetBool("Architecture296.PerfPriorDirect",false));else SessionState.EraseBool("Compact270.DirectPlay");
   string path=SessionState.GetString("Architecture296.PerfPriorStartup","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
  }
  static void RestorePerfQa296()
  {
   SessionState.SetBool("Architecture296.Perf",false);SessionState.SetBool("Architecture296.Restore",false);
   Session292().TestSaveSuffix=SessionState.GetString("Architecture296.PerfPriorSuffix","");string ui=SessionState.GetString("Architecture296.PerfPriorUi","__unset__");
   if(ui=="__unset__")SessionState.EraseString("PlaytestUiReviewSuffix");else SessionState.SetString("PlaytestUiReviewSuffix",ui);
   RestorePerfStartupQa296();EditorApplication.delayCall+=RestorePerfStartupQa296;
   probeQa296=null;probeWaterQa296=null;probeBodyQa296=null;probeStartedQa296=0;EditorApplication.update-=PerfTickQa296;
  }
  static void PerfTickQa296()
  {
   if(!EditorApplication.isPlaying){EditorApplication.update-=PerfTickQa296;return;}
   string kind=SessionState.GetString("Architecture296.PerfKind","shore");string output=O296+"/Perf/"+kind+(SessionState.GetBool("Architecture296.PerfNoArt",false)?"-noart":"");
   try
   {
    if(probeQa296==null)
    {
     if(EditorApplication.timeSinceStartup-probeStartedQa296<5)return;
     var session=Session292();if(session.TestSaveSuffix!="_perfQa296_test")throw new Exception("Performance session did not retain its isolated save suffix");var field=new CompactWorldSurface(session.MountainLayout);var query=Components295<WorldTerrainQuery>().First();var routes=RequiredRoutesQa296();
     string sourcePath=O295+"/cameras.json",sourceId=kind=="shore"?"entry":"gorge",crossingId="",routeId="hyeongang__capital_center";
     var anchor=CameraViewsQa296().Views.Single(v=>v.Id==sourceId).Eye;
     if(kind=="crossing")
     {var crossing=JsonUtility.FromJson<Crossings295>(File.ReadAllText(G296+"/crossings.json")).Crossings.Single(c=>c.Id=="hyeongang__temple_water_0"&&c.Kind=="FixedCrossing");anchor=(crossing.Start+crossing.End)*.5f;routeId=crossing.RouteId;crossingId=crossing.Id;sourcePath=G296+"/crossings.json";sourceId=crossingId;}
     var selected=routes.Single(r=>r.id==routeId);var all=SamplesQa296(selected.points,.75f);int centre=Enumerable.Range(0,all.Length).OrderBy(i=>new Vector2(all[i].x-anchor.x,all[i].z-anchor.z).sqrMagnitude).First();
     int start=Mathf.Clamp(centre-80,0,Mathf.Max(0,all.Length-160));var points=all.Skip(start).Take(161).ToArray();
     if(kind=="cheolong"||kind=="hwanggyeong")
     {
      var arena=Sheet296().Arenas.First(a=>a.Realm==kind&&a.Interior);var q=Quaternion.Euler(0,arena.Yaw,0);
      sourcePath=O296+"/architecture.json";sourceId=arena.Id;anchor=arena.Centre;
      points=SamplesQa296(new[]{arena.Centre+q*new Vector3(-8,0,-arena.ClearSize.y*.33f),arena.Centre+q*new Vector3(8,0,arena.ClearSize.y*.33f)},.75f);
      selected=new Route292{id=arena.Id+"_interior",width=4,points=points};
     }
     else if(kind=="gate")
     {
      var gate=Sheet296().Perimeters.Single(p=>p.Id=="capital296"&&p.ActiveGate).GateCentre;sourcePath=O296+"/architecture.json";sourceId="capital296";anchor=gate;
      points=SamplesQa296(new[]{gate+new Vector3(0,0,-60),gate+new Vector3(0,0,-12)},.75f);selected=new Route292{id="south_gate_approach296",width=6,points=points};
     }
     for(int i=0;i<points.Length;i++){if(!SupportQa296(points[i],field,query,out var support))throw new Exception("Probe path missing support "+points[i]);points[i]=support.point;if(query.Immersion(points[i])>.55f)throw new Exception("Probe path enters deep water "+points[i]);}
     var profile=ScriptableObject.CreateInstance<MountainTrailProfile>();profile.points=points;profile.widths=Enumerable.Repeat(selected.width,points.Length).ToArray();profile.distances=new float[points.Length];profile.types=new int[points.Length];profile.knots=Array.Empty<Vector3>();
     for(int i=1;i<points.Length;i++)profile.distances[i]=profile.distances[i-1]+Vector3.Distance(points[i-1],points[i]);profile.length=profile.distances.Last();profile.rise=points.Last().y-points.First().y;
     var cameraGo=new GameObject("Architecture296_perf_camera");var camera=cameraGo.AddComponent<Camera>();var source=Camera.main;if(source!=null){camera.CopyFrom(source);EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());}
     foreach(var c in Components295<Camera>())if(c!=camera)c.enabled=false;camera.enabled=true;camera.farClipPlane=Mathf.Max(4000,camera.farClipPlane);
     foreach(var art in Components295<CompactRebuildArtRenderer>()){art.Observer=camera;if(SessionState.GetBool("Architecture296.PerfNoArt",false))art.enabled=false;}
     if(SessionState.GetBool("Architecture296.PerfNoArt",false))foreach(var grass in Components295<CompactGrassRenderer266>())grass.enabled=false;
     var go=new GameObject("Architecture296_actual_Play_probe");var cc=go.AddComponent<CharacterController>();cc.enabled=false;var body=session.Walker.Body;cc.height=body.height;cc.center=body.center;cc.radius=body.radius;cc.skinWidth=body.skinWidth;cc.stepOffset=body.stepOffset;cc.slopeLimit=body.slopeLimit;
     go.transform.position=points[0]+Vector3.up*(cc.height*.5f-cc.center.y+.06f);probeQa296=go.AddComponent<MountainTrailProbe285>();probeQa296.Profile=profile;probeQa296.View=camera;probeQa296.Output=Path.GetFullPath(output);cc.enabled=true;probeBodyQa296=cc;probeWaterQa296=query;Application.runInBackground=true;
     Directory.CreateDirectory(output);File.WriteAllText(output+"/provenance.json",JsonUtility.ToJson(new PerfReceiptQa296{Kind=kind,Route=selected.id,Start=points.First(),End=points.Last(),Length=profile.length,Time=DateTime.Now.ToString("s"),Scene=SceneManager.GetActiveScene().path,SaveSuffix=session.TestSaveSuffix,
      SourcePath=sourcePath,SourceId=sourceId,SourceSha256=SurfaceSha296(sourcePath),Anchor=anchor,CrossingId=crossingId,NoArt=SessionState.GetBool("Architecture296.PerfNoArt",false),PathSha256=HashArchitecture296(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Route292{id=selected.id,width=selected.width,points=points})))},true));return;
    }
    if(probeQa296.Result==null&&probeBodyQa296!=null&&probeWaterQa296!=null)
    {
     var feet=probeBodyQa296.transform.TransformPoint(probeBodyQa296.center)-Vector3.up*probeBodyQa296.height*.5f;
     float depth=probeWaterQa296.Immersion(feet),limit=probeWaterQa296.Rules!=null?probeWaterQa296.Rules.MaximumWadingDepth:.55f;
     int i=Mathf.Clamp(probeQa296.Target,1,probeQa296.Profile.points.Length-1);float floor=Mathf.Min(probeQa296.Profile.points[i-1].y,probeQa296.Profile.points[i].y);
     if(depth>limit+.06f)probeQa296.Result="FAIL actual Play probe entered deep water depth="+depth+" feet="+feet;
     else if(feet.y<floor-2)probeQa296.Result="FAIL actual Play probe fell below route support feet="+feet+" expected floor="+floor;
    }
    if(probeQa296.Result==null&&EditorApplication.timeSinceStartup-probeStartedQa296<240)return;
    Directory.CreateDirectory(output);File.WriteAllText(output+"/walk-play.txt",probeQa296.Result??"FAIL timeout 240s");EditorApplication.update-=PerfTickQa296;EditorApplication.isPlaying=false;
   }
   catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(output+"/walk-play.txt","FAIL probe setup/runtime: "+ex);EditorApplication.update-=PerfTickQa296;EditorApplication.isPlaying=false;}
  }
  [Serializable] sealed class PerfReceiptQa296{public string Kind,Route,Time,Scene,SaveSuffix,SourcePath,SourceId,SourceSha256,CrossingId,PathSha256;public Vector3 Start,End,Anchor;public float Length;public bool NoArt;}
 }
}
