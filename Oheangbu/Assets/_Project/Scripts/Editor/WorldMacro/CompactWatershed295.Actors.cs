using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  sealed class ActorNavPlan295
  {
   public PrologueEncounter Actor;public NavMeshAgent Agent;public WorldMacroPlaytestSO.Encounter Spec;
   public Vector3 OriginalFeet,Feet,Nav;public Vector3[] OriginalPatrol,Patrol;public string Realm;
   public bool MoveActor;public bool[] MovePatrol;
  }
  public static string DiagnoseActorNavigation295()
  {
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene295||scene.isDirty)throw new InvalidOperationException("Saved #295 Edit scene is required for read-only actor/source diagnostics.");
   string[] ids={"mine_beast/1","mine_fire/0"};
   var report=new List<string>{"#295 unresolved actor diagnostics; no content/transform/nav edits", "Candidate and protected source scenes are opened for inspection only. The saved #295 scene is restored in finally.", "UTC "+DateTime.UtcNow.ToString("O")};
   void Inspect(string label)
   {
    var session=Session292();var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();Physics.SyncTransforms();
    report.Add("SCENE "+label+" "+current.path);
    foreach(string id in ids)
    {
     var actor=session.Actors.FirstOrDefault(a=>a!=null&&a.Id==id);var spec=session.Content.Encounters.FirstOrDefault(e=>e.Id==id);
     if(actor==null||spec==null){report.Add("  MISSING "+id);continue;}
     var agent=actor.GetComponent<NavMeshAgent>();if(agent==null){report.Add("  MISSING AGENT "+id);continue;}
     var floor=actor.transform.position-Vector3.up*agent.baseOffset;var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
     report.Add("ACTOR "+id+" content="+spec.ContentId+" world="+ActorPos295(actor.transform.position)+" baseOffset="+agent.baseOffset.ToString("0.###",CultureInfo.InvariantCulture)+" actualFloor="+ActorPos295(floor)+" contentFeet="+ActorPos295(spec.Feet)+" contentMinusActual="+ActorPos295(spec.Feet-floor)+" agentEnabled="+agent.enabled);
     void Point(string pointLabel,Vector3 p)
     {
      report.Add("  "+pointLabel+"="+ActorPos295(p)+" realm="+session.MountainLayout?.RealmAt(new Vector2(p.x,p.z))?.Id);
      foreach(float radius in new[]{2f,12f,16f,24f,32f,64f})
      {
       bool found=NavMesh.SamplePosition(p,out var nav,radius,filter);
       report.Add("    nav radius="+radius+" found="+found+(found?" position="+ActorPos295(nav.position)+" delta="+ActorPos295(nav.position-p)+" distance="+Vector3.Distance(nav.position,p).ToString("0.###",CultureInfo.InvariantCulture)+" areaMask="+nav.mask:""));
      }
      var hits=Physics.RaycastAll(p+Vector3.up*80,Vector3.down,160,~0,QueryTriggerInteraction.Collide).Where(h=>h.collider.gameObject.scene==current).OrderBy(h=>h.distance).ToArray();
      report.Add("    vertical physics hits="+hits.Length+" (+80m through -80m; includes triggers)");
      foreach(var hit in hits)
      {
       string path=hit.transform.name;for(var parent=hit.transform.parent;parent!=null;parent=parent.parent)path=parent.name+"/"+path;
       report.Add("      "+path+" type="+hit.collider.GetType().Name+" point="+ActorPos295(hit.point)+" deltaY="+(hit.point.y-p.y).ToString("0.###",CultureInfo.InvariantCulture)+" normal="+ActorPos295(hit.normal)+" trigger="+hit.collider.isTrigger+" rigidbody="+(hit.collider.attachedRigidbody!=null));
      }
     }
     Point("actual floor",floor);Point("content feet",spec.Feet);
     for(int i=0;i<(actor.PatrolPoints?.Length??0);i++)Point("actual patrol["+i+"] floor",actor.PatrolPoints[i]-Vector3.up*agent.baseOffset);
     for(int i=0;i<(spec.Patrol?.Length??0);i++)report.Add("  content patrol["+i+"]="+ActorPos295(spec.Patrol[i]));
    }
   }
   try{Inspect("candidate");EditorSceneManager.OpenScene(Scene292);Inspect("protected source #294 on Reworld292 scene");}
   catch(Exception e){report.Add("DIAGNOSTIC ERROR: "+e);throw;}
   finally
   {
    EditorSceneManager.OpenScene(Scene295);report.Add("Restored #295 candidate without saving either scene.");Directory.CreateDirectory(O295);File.WriteAllLines(O295+"/actor-navigation-diagnostics.txt",report);
   }
   return "Read-only actor/source report: "+Path.GetFullPath(O295+"/actor-navigation-diagnostics.txt")+"; candidate restored.";
  }
  /// <summary>Repair only candidate actors that cannot use the final baked navigation. Never disables an actor.</summary>
  public static string ActorNavigation295()
  {
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene295)throw new InvalidOperationException("Actor navigation authoring requires the #295 candidate in Edit mode.");
   var session=Session292();var content=session.Content;
   if(content==null||!AssetDatabase.GetAssetPath(content).StartsWith(A295+"/",StringComparison.Ordinal)||content.SaveSlot!="world-compact-watershed-295")
    throw new InvalidOperationException("Actor repairs require the candidate's private content asset and isolated slot.");
   var layout=session.MountainLayout;var query=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<WorldTerrainQuery>(true)).FirstOrDefault();
   if(layout==null||query==null||session.Actors==null)throw new InvalidOperationException("Candidate realm, water query and actor references are required.");
   query.Reindex();Physics.SyncTransforms();
   var report=new List<string>{"#295 actor/navigation audit after final NavMesh bake", "Actor and patrol probes subtract NavMeshAgent.baseOffset. Valid poses are retained. Repairs use static dry support, unchanged realm/content IDs, and bidirectional complete patrol paths.",
    "Search limit is min(encounter.Leash / 2, 12m), measured in 3D from each original floor position. No actor is disabled and no partial repair is applied on failure."};
   var plans=new List<ActorNavPlan295>();var failures=new List<string>();
   foreach(var actor in session.Actors)
   {
    if(actor==null){failures.Add("Missing actor reference.");continue;}
    var matches=(content.Encounters??Array.Empty<WorldMacroPlaytestSO.Encounter>()).Where(e=>e!=null&&e.Id==actor.Id).ToArray();
    var agent=actor.GetComponent<NavMeshAgent>();
    if(actor.gameObject.scene!=scene||agent==null||matches.Length!=1)
    {failures.Add(actor.Id+": candidate scene, one encounter spec and NavMeshAgent required.");continue;}
    var spec=matches[0];var offset=Vector3.up*agent.baseOffset;var original=actor.transform.position-offset;
    var realm=layout.RealmAt(new Vector2(original.x,original.z))?.Id;
    if(string.IsNullOrEmpty(realm)){failures.Add(actor.Id+": original actor feet are outside the authored realms.");continue;}
    var originalPatrol=(actor.PatrolPoints??Array.Empty<Vector3>()).Select(p=>p-offset).ToArray();
    if((spec.Patrol??Array.Empty<Vector3>()).Length!=originalPatrol.Length)
    {failures.Add(actor.Id+": actor/spec patrol lengths differ; resolve authoring ownership first.");continue;}
    float limit=Mathf.Min(spec.Leash*.5f,12);
    if(!float.IsFinite(limit)||limit<=0){failures.Add(actor.Id+": invalid encounter leash.");continue;}
    var filter=new NavMeshQueryFilter{agentTypeID=agent.agentTypeID,areaMask=agent.areaMask};
    bool navFound=NavMesh.SamplePosition(original,out var originalNav,2,filter);
    bool valid=navFound&&ActorDryNav295(originalNav.position,realm,session,query,out _);
    report.Add(actor.Id+" content="+spec.ContentId+" realm="+realm+" floor="+ActorPos295(original)+" baseOffset="+agent.baseOffset.ToString("0.###",CultureInfo.InvariantCulture)+
     " sample2m="+navFound+" dry="+valid+" repairLimit="+limit.ToString("0.###",CultureInfo.InvariantCulture));
    for(int i=0;i<originalPatrol.Length;i++)
    {
     bool sampled=NavMesh.SamplePosition(originalPatrol[i],out var patrolHit,2,filter);
     report.Add("  inspect patrol["+i+"] floor="+ActorPos295(originalPatrol[i])+" sample2m="+sampled+
      " dry="+(sampled&&ActorDryNav295(patrolHit.position,realm,session,query,out _))+
      " connectedToOriginalActor="+(valid&&sampled&&ActorNavConnected295(originalNav.position,patrolHit.position,filter)));
    }
    // Good actors never move to accommodate bad patrols. Their existing navigation component owns the patrol.
    bool diagnosedMine=!valid&&(actor.Id=="mine_beast/1"||actor.Id=="mine_fire/0");
    var options=diagnosedMine?new List<Vector3>():valid?new List<Vector3>{originalNav.position}:ActorNavOptions295(original,limit,realm,filter,session,query);
    ActorNavPlan295 accepted=null;string reason="No same-realm dry NavMesh point within the repair limit.";
    if(diagnosedMine)accepted=ActorMineProjection295(actor,agent,spec,original,originalPatrol,realm,filter,session,query,report,out reason);
    foreach(var nav in options)
    {
     var proposed=new ActorNavPlan295{Actor=actor,Agent=agent,Spec=spec,OriginalFeet=original,Feet=valid?original:nav,Nav=nav,Realm=realm,MoveActor=!valid,
      OriginalPatrol=originalPatrol,Patrol=(Vector3[])originalPatrol.Clone(),MovePatrol=new bool[originalPatrol.Length]};
     bool all=true;
     for(int i=0;i<originalPatrol.Length;i++)
     {
      Vector3 patrol=originalPatrol[i];
      if(layout.RealmAt(new Vector2(patrol.x,patrol.z))?.Id!=realm)
      {all=false;reason="Patrol "+i+" was authored outside actor realm; no automatic realm crossing.";break;}
      bool existing=NavMesh.SamplePosition(patrol,out var patrolNav,2,filter)&&ActorDryNav295(patrolNav.position,realm,session,query,out _)&&ActorNavConnected295(nav,patrolNav.position,filter);
      if(existing)continue;
      var alternatives=ActorNavOptions295(patrol,limit,realm,filter,session,query);
      bool found=false;
      foreach(var alternative in alternatives)
       if(ActorNavConnected295(nav,alternative,filter)){proposed.Patrol[i]=alternative;proposed.MovePatrol[i]=true;found=true;break;}
      if(!found){all=false;reason="Patrol "+i+" has no connected same-realm dry navigation within "+limit.ToString("0.###",CultureInfo.InvariantCulture)+"m.";break;}
     }
     if(all){accepted=proposed;break;}
    }
    if(accepted==null){failures.Add(actor.Id+": "+reason);continue;}
    plans.Add(accepted);
    report.Add("  actor "+(accepted.MoveActor?"REPAIR":"KEEP")+" delta="+ActorPos295(accepted.Feet-original)+" distance="+Vector3.Distance(accepted.Feet,original).ToString("0.###",CultureInfo.InvariantCulture)+" nav="+ActorPos295(accepted.Nav));
    for(int i=0;i<accepted.Patrol.Length;i++)report.Add("  patrol["+i+"] "+(accepted.MovePatrol[i]?"REPAIR":"KEEP")+" from="+ActorPos295(originalPatrol[i])+" to="+ActorPos295(accepted.Patrol[i])+" delta="+ActorPos295(accepted.Patrol[i]-originalPatrol[i])+" path=complete-both-directions");
   }
   Directory.CreateDirectory(O295);string output=O295+"/actor-navigation.txt";
   if(failures.Count>0)
   {
    report.Add("FAIL: "+failures.Count+" unresolved actor(s); no actor or content mutations applied.");report.AddRange(failures.Select(f=>"  "+f));File.WriteAllLines(output,report);
    throw new InvalidOperationException("Actor navigation audit failed; no changes applied. "+string.Join(" | ",failures)+"; report="+Path.GetFullPath(output));
   }
   int movedActors=0,movedPatrols=0;
   foreach(var plan in plans)
   {
    var offset=Vector3.up*plan.Agent.baseOffset;
    if(plan.MoveActor){plan.Actor.transform.position=plan.Feet+offset;plan.Spec.Feet=plan.Feet;EditorUtility.SetDirty(plan.Actor.transform);movedActors++;}
    for(int i=0;i<plan.Patrol.Length;i++)if(plan.MovePatrol[i])
    {plan.Actor.PatrolPoints[i]=plan.Patrol[i]+offset;plan.Spec.Patrol[i]=plan.Patrol[i];movedPatrols++;}
    if(plan.MoveActor||plan.MovePatrol.Any(m=>m))EditorUtility.SetDirty(plan.Actor);
   }
   Physics.SyncTransforms();
   if(movedActors+movedPatrols>0)
   {EditorUtility.SetDirty(content);AssetDatabase.SaveAssetIfDirty(content);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
   report.Add("PASS: actors="+plans.Count+"; actor repairs="+movedActors+"; patrol repairs="+movedPatrols+". Content IDs, realm membership, encounter leash, agent enabled flags and valid positions preserved.");
   File.WriteAllLines(output,report);return report.Last()+" Report: "+Path.GetFullPath(output);
  }
  static string ActorPos295(Vector3 p)=>"("+p.x.ToString("0.###",CultureInfo.InvariantCulture)+", "+p.y.ToString("0.###",CultureInfo.InvariantCulture)+", "+p.z.ToString("0.###",CultureInfo.InvariantCulture)+")";
  static ActorNavPlan295 ActorMineProjection295(PrologueEncounter actor,NavMeshAgent agent,WorldMacroPlaytestSO.Encounter spec,Vector3 original,
   Vector3[] originalPatrol,string realm,NavMeshQueryFilter filter,WorldMacroPlaytestSession session,WorldTerrainQuery query,List<string> report,out string reason)
  {
   reason=null;string provenance=O295+"/actor-navigation-diagnostics.txt";
   if(!File.Exists(provenance)){reason="Documented candidate/source mine suspension diagnosis is required before cave floor projection.";return null;}
   // This corrects two measured legacy indoor height errors. It is not a larger-radius world relocation.
   bool Project(Vector3 interior,out Vector3 nav,out string detail)
   {
    nav=default;detail=null;
    if(!ActorCaveFloor295(interior+Vector3.up*.1f,40.2f,session,query,out var floor))
    {detail="No diagnosed static mine floor directly below the original interior position within40m.";return false;}
    if(!NavMesh.SamplePosition(floor.point,out var hit,2,filter)||Vector2.Distance(new Vector2(hit.position.x,hit.position.z),new Vector2(interior.x,interior.z))>2.001f||Mathf.Abs(hit.position.y-floor.point.y)>.6f)
    {detail="Downward cave floor has no nearby NavMesh within2m / .6m vertical floor agreement.";return false;}
    if(session.MountainLayout.RealmAt(new Vector2(hit.position.x,hit.position.z))?.Id!=realm||!query.IsDry(hit.position)||
     !ActorCaveFloor295(hit.position+Vector3.up*.65f,1.3f,session,query,out var support)||Mathf.Abs(support.point.y-hit.position.y)>.6f)
    {detail="Projected navigation does not remain on the same static dry mine floor and realm.";return false;}
    nav=hit.position;detail="floor="+floor.collider.name+" at="+ActorPos295(floor.point)+" nav="+ActorPos295(nav)+" delta="+ActorPos295(nav-interior);return true;
   }
   if(!Project(original,out var feet,out var actorDetail)){reason=actorDetail;return null;}
   var plan=new ActorNavPlan295{Actor=actor,Agent=agent,Spec=spec,OriginalFeet=original,Feet=feet,Nav=feet,Realm=realm,MoveActor=true,
    OriginalPatrol=originalPatrol,Patrol=(Vector3[])originalPatrol.Clone(),MovePatrol=new bool[originalPatrol.Length]};
   report.Add("  DIAGNOSED MINE FLOOR CORRECTION: "+actorDetail+"; provenance="+Path.GetFullPath(provenance)+"; only these two measured indoor IDs may project vertically beyond ordinary12m repair limit.");
   for(int i=0;i<originalPatrol.Length;i++)
   {
    bool keep=NavMesh.SamplePosition(originalPatrol[i],out var existing,2,filter)&&
     ActorCaveFloor295(existing.position+Vector3.up*.65f,1.3f,session,query,out var support)&&Mathf.Abs(existing.position.y-support.point.y)<.6f&&ActorNavConnected295(feet,existing.position,filter);
    if(keep)continue;
    if(!Project(originalPatrol[i],out var patrol,out var detail)||!ActorNavConnected295(feet,patrol,filter))
    {reason="Mine patrol "+i+" cannot project onto the connected cave floor: "+detail;return null;}
    plan.Patrol[i]=patrol;plan.MovePatrol[i]=true;report.Add("  diagnosed cave patrol["+i+"]: "+detail);
   }
   return plan;
  }
  static bool ActorCaveFloor295(Vector3 rayStart,float distance,WorldMacroPlaytestSession session,WorldTerrainQuery query,out RaycastHit support)
  {
   support=default;
   foreach(var hit in Physics.RaycastAll(rayStart,Vector3.down,distance,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
   {
    if(hit.collider.gameObject.scene!=session.gameObject.scene||!(hit.collider is MeshCollider)||hit.transform.root.name!="mine"||
     hit.normal.y<.7f||!query.IsPermanentDrySupport(hit.point,hit.collider))continue;
    bool approach=hit.transform.name=="Continuous_Approach_Soil"&&hit.transform.parent!=null&&hit.transform.parent.name=="Playtest_NaturalCave";
    if(!approach&&hit.transform.name!="Cave_ExteriorCover237")continue;
    support=hit;return true;
   }
   return false;
  }
  static bool ActorNavConnected295(Vector3 a,Vector3 b,NavMeshQueryFilter filter)
  {
   var path=new NavMeshPath();if(!NavMesh.CalculatePath(a,b,filter,path)||path.status!=NavMeshPathStatus.PathComplete)return false;
   return NavMesh.CalculatePath(b,a,filter,path)&&path.status==NavMeshPathStatus.PathComplete;
  }
  static List<Vector3> ActorNavOptions295(Vector3 origin,float limit,string realm,NavMeshQueryFilter filter,WorldMacroPlaytestSession session,WorldTerrainQuery query)
  {
   var points=new Dictionary<Vector3Int,Vector3>();
   void Probe(Vector3 probe,float distance=2)
   {
    if(!NavMesh.SamplePosition(probe,out var hit,distance,filter)||Vector3.Distance(origin,hit.position)>limit+.0001f||!ActorDryNav295(hit.position,realm,session,query,out _))return;
    var key=new Vector3Int(Mathf.RoundToInt(hit.position.x*20),Mathf.RoundToInt(hit.position.y*20),Mathf.RoundToInt(hit.position.z*20));
    if(!points.TryGetValue(key,out var previous)||Vector3.SqrMagnitude(hit.position-origin)<Vector3.SqrMagnitude(previous-origin))points[key]=hit.position;
   }
   Probe(origin,limit);
   for(float radius=.5f;radius<=limit+.001f;radius+=.5f)
   {
    int count=Mathf.Max(12,Mathf.CeilToInt(2*Mathf.PI*radius/.75f));
    for(int i=0;i<count;i++)
    {
     float angle=2*Mathf.PI*i/count;var probe=origin+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
     // Follow actual collider height on a nearby hillside, while the final 3D radius remains strictly bounded.
     if(ActorGround295(probe,limit+2,session,query,out var ground))probe.y=ground.point.y;
     Probe(probe);
    }
   }
   return points.Values.OrderBy(p=>Vector3.SqrMagnitude(p-origin)).ThenBy(p=>p.x).ThenBy(p=>p.z).ToList();
  }
  static bool ActorDryNav295(Vector3 p,string realm,WorldMacroPlaytestSession session,WorldTerrainQuery query,out RaycastHit support)
  {
   support=default;
   return session.MountainLayout.RealmAt(new Vector2(p.x,p.z))?.Id==realm&&query.IsDry(p)&&
    ActorGround295(p,1.5f,session,query,out support)&&Mathf.Abs(support.point.y-p.y)<=.6f&&query.IsPermanentDrySupport(support.point,support.collider);
  }
  static bool ActorGround295(Vector3 p,float range,WorldMacroPlaytestSession session,WorldTerrainQuery query,out RaycastHit support)
  {
   support=default;
   foreach(var hit in Physics.RaycastAll(p+Vector3.up*range,Vector3.down,range*2,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
   {
    if(hit.collider.gameObject.scene!=session.gameObject.scene||hit.collider.GetComponentInParent<PrologueEncounter>()!=null||
     (session.Walker!=null&&hit.transform.IsChildOf(session.Walker.transform)))continue;
    if(hit.normal.y<.7f||!query.IsPermanentDrySupport(hit.point,hit.collider))continue;
    support=hit;return true;
   }
   return false;
  }
 }
}
