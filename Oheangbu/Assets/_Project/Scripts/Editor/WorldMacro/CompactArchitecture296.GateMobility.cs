using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Oheangbu.App.Demo;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class GateMotion296
  {
   public string Id,Kind,Status,Detail,StartSupport,EndSupport,Barrier,ContactMethod;
   public Vector3 Boundary,Start,End,Target,BlockingNormal;public float Rise,Travel,BoundaryProgress,ContactDistance,BlockingSlope;public int Steps,StartAttempts;
  }
  [Serializable] sealed class GateSummon296
  {
   public string Id,Status,Reason,Diagnostic,Scope;public Vector3 Player,Direction,Placement;public bool Accepted;public float ApproachDistance;
  }
  [Serializable] sealed class GateLift296
  {
   public string Id,Status,Detail,Source;public Vector3 Lower,Upper,NearestWall;public float Height,WallDistance;
   public bool Valid,LowerInside,UpperInside,Enabled;
  }
  [Serializable] sealed class GateMobilityReceipt296
  {
   public string Revision="architecture-296",Utc,Scope="Actual temporary CharacterController.Move in the candidate Edit scene, using player dimensions and permanent scene collision. No support fixtures. Gate state is restored. Summon checks call the existing read-only placement service; they do not test wheel driving. Amplified lift checks audit authored nodes, not a global reachability proof.";
   public float PlayerHeight,PlayerRadius,PlayerStepOffset,PlayerSlopeLimit;public bool SummonReady;
   public GateMotion296[] Motions;public GateSummon296[] Summons;public GateLift296[] Lifts;
  }
  sealed class GateSample296
  {public string Id;public Vector3 Point,Normal;public float HalfDepth;}

  public static string GateMobility296()
  {
   var report=new List<string>();GateMobility296(report);return string.Join("\n",report);
  }
  static void GateMobility296(List<string> report)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Scene296)
    throw new InvalidOperationException("Gate mobility requires the Architecture296 candidate in Edit mode.");
   var session=Session292();var scene=session.gameObject.scene;var source=session.Walker.Body;
   if(source==null||Vector3.Distance(source.transform.lossyScale,Vector3.one)>.001f)
    throw new InvalidOperationException("Actual unscaled player CharacterController is required.");
   var receipt=JsonUtility.FromJson<GateReceipt296>(File.ReadAllText(O296+"/gates.json"));
   var field=new CompactWorldSurface(session.MountainLayout);
   var gateRoot=scene.GetRootGameObjects().Single(g=>g.name=="Architecture296_Gates");
   var doors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SouthGateDoorPresentation>(true)).Where(g=>g.gameObject.activeInHierarchy).ToArray();
   var doorStates=doors.Select(d=>(door:d,requested:d.IsOpenRequested,complete:d.OpenAnimationComplete)).ToArray();
   var rotations=doors.SelectMany(d=>d.GetComponentsInChildren<Transform>(true)).Distinct().Select(t=>(transform:t,rotation:t.localRotation)).ToArray();
   var motions=new List<GateMotion296>();var summons=new List<GateSummon296>();var lifts=new List<GateLift296>();
   var samples=new List<GateSample296>();var keys=new HashSet<string>();
   bool Barrier(Collider c)=>c!=null&&c.enabled&&c.gameObject.activeInHierarchy&&c.gameObject.scene==scene&&
    (c.transform.root.name=="Architecture296_Gates"||c.transform.root.name=="CapitalSouthGate253");
   foreach(var wall in gateRoot.GetComponentsInChildren<BoxCollider>().Where(c=>c.name.StartsWith("WallSolid_",StringComparison.Ordinal)))
   {
    var positions=new[]{-.5f,-.25f,0,.25f,.5f}.Select(x=>wall.transform.TransformPoint(new Vector3(x*wall.size.x,0,0))).ToArray();
    var low=positions.OrderBy(p=>field.Sample(p.x,p.z)).First();
    foreach(var p in new[]{positions[0],low,positions[4]})
    {
     string key=Mathf.RoundToInt(p.x*5)+":"+Mathf.RoundToInt(p.z*5);
     if(!keys.Add(key))continue;
     samples.Add(new GateSample296{Id=wall.transform.parent.parent.name+"/"+wall.transform.parent.name+"/"+wall.name+"/"+key,
      Point=p,Normal=wall.transform.forward,HalfDepth=wall.size.z*.5f});
    }
   }
   foreach(var loop in receipt.Loops)for(int i=0;i<loop.Points.Length;i++)
   {
    var p=loop.Points[i];var before=loop.Points[(i+loop.Points.Length-1)%loop.Points.Length];var after=loop.Points[(i+1)%loop.Points.Length];
    var incoming=Vector3.ProjectOnPlane(p-before,Vector3.up).normalized;var outgoing=Vector3.ProjectOnPlane(after-p,Vector3.up).normalized;
    var normal=(Vector3.Cross(incoming,Vector3.up)+Vector3.Cross(outgoing,Vector3.up)).normalized;
    // A normal from only one edge places the inner start capsule inside the
    // adjoining edge. Approach a polygon corner on its true bisector instead.
    samples.RemoveAll(s=>Vector3.ProjectOnPlane(s.Point-p,Vector3.up).magnitude<1.2f);
    samples.Add(new GateSample296{Id=loop.Id+"/corner_"+i,Point=p,Normal=normal,HalfDepth=1});
   }
   var go=new GameObject("TemporaryArchitecture296GateController"){hideFlags=HideFlags.HideAndDontSave};
   SceneManager.MoveGameObjectToScene(go,scene);var cc=go.AddComponent<CharacterController>();
   cc.height=source.height;cc.radius=source.radius;cc.center=source.center;cc.stepOffset=source.stepOffset;
   cc.slopeLimit=source.slopeLimit;cc.skinWidth=source.skinWidth;cc.minMoveDistance=source.minMoveDistance;
   var contact=go.AddComponent<ArchitectureGateContact296>();
   var summon=session.DemoEscortSummon;bool summonReady=false;string readyReason="Missing summon service";
   string originalResult=summon!=null?summon.LastResult:null,originalDiagnostic=summon!=null?summon.LastPlacementDiagnostic:null;
   try
   {
    foreach(var door in doors)door.SetOpened(false,true);Physics.SyncTransforms();
    if(summon!=null)
    {
     var ready=typeof(WorldMacroPalanquinSummon).GetMethod("Ready",BindingFlags.Instance|BindingFlags.NonPublic);
     if(ready==null)throw new InvalidOperationException("Existing summon readiness validation not found.");
     var args=new object[]{null};summonReady=(bool)ready.Invoke(summon,args);readyReason=(string)args[0];
    }
    report.Add((summonReady?"PASS ":"FAIL ")+"existing summon service configuration ready: "+(summonReady?"actual Ready validation":readyReason));
    foreach(var sample in samples)
    {
     // Both sides are tested because mountain ground can rise inside or outside.
     for(int sign=-1;sign<=1;sign+=2)
      Move(sample.Id,"closed-wall",sample.Point,sample.Normal*sign,sample.HalfDepth+3.25f,0,false);
     Summon(sample.Id,sample.Point,sample.Normal,sample.HalfDepth+3f);
    }
    foreach(var opening in receipt.Loops.SelectMany(l=>l.Gates))
    {
     bool locked=!string.IsNullOrEmpty(opening.OpenFact);
     for(int sign=-1;sign<=1;sign+=2)
      Move(opening.Id,locked?"closed-gate":"open-precinct",opening.Centre,opening.Inward*sign,6,0,!locked);
     if(locked)Summon(opening.Id,opening.Centre,opening.Inward,3);
    }
    foreach(var door in doors)door.SetOpened(true,true);Physics.SyncTransforms();
    foreach(var opening in receipt.Loops.SelectMany(l=>l.Gates).Where(g=>!string.IsNullOrEmpty(g.OpenFact)))
     for(int sign=-1;sign<=1;sign+=2)Move(opening.Id,"opened-gate",opening.Centre,opening.Inward*sign,6,0,true);
    var capital=receipt.Loops.Single(l=>l.Id=="capital296");
    foreach(var site in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GukLiftSite>(true)))
    {
     var row=new GateLift296{Id=site.Id,Valid=site.Valid,Height=site.Height,Enabled=site.isActiveAndEnabled,Source="scene component"};lifts.Add(row);
     if(!site.Valid){row.Status="FAIL";row.Detail="Invalid enabled amplified lift node";continue;}
     row.Lower=site.Lower.position;row.Upper=site.Upper.position;AuditLift(row);
    }
    foreach(var mountain in session.MountainLayout.Mountains)
    {
     float height=mountain.LiftLanding.y-mountain.LiftBase.y;if(height<=0||lifts.Any(l=>l.Id==mountain.Id+"_guk"))continue;
     var row=new GateLift296{Id=mountain.Id+"_guk",Height=height,Lower=mountain.LiftBase,Upper=mountain.LiftLanding,Source="authored MountainLayout; no current scene component",Enabled=false,
      Valid=height>=8&&height<=24&&Vector3.ProjectOnPlane(mountain.LiftLanding-mountain.LiftBase,Vector3.up).magnitude<=2};lifts.Add(row);AuditLift(row);
    }
    void AuditLift(GateLift296 row)
    {
     row.LowerInside=GateInside296(capital.Points,row.Lower);row.UpperInside=GateInside296(capital.Points,row.Upper);
     row.WallDistance=float.PositiveInfinity;
     for(int i=0;i<capital.Points.Length;i++)
     {
      var a=capital.Points[i];var b=capital.Points[(i+1)%capital.Points.Length];var delta=b-a;delta.y=0;
      float t=Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(row.Upper-a,Vector3.up),delta)/delta.sqrMagnitude);
      var point=Vector3.Lerp(a,b,t);float d=Vector3.ProjectOnPlane(row.Upper-point,Vector3.up).magnitude;
      if(d<row.WallDistance){row.WallDistance=d;row.NearestWall=point;}
     }
     // A nearby elevated landing needs a real spell/movement fixture; do not infer
     // safety from the ordinary 2.4m ray tests or from a vertical-only node.
     bool distant=row.WallDistance>session.MumBridgeProfile.MaximumSpan+cc.radius;
     row.Status=row.Valid&&distant&&row.LowerInside==row.UpperInside?"PASS":"REVIEW";
     row.Detail=row.Status=="PASS"?"Same side of capital; landing farther than maximum Mum span from perimeter. No direct amplified-lift-to-wall link.":"Amplified landing is near/crosses capital perimeter; actual movement and spell placement require a separate live probe.";
    }
    void Move(string id,string kind,Vector3 boundary,Vector3 inward,float distance,float rise,bool expectCross)
    {
     inward=Vector3.ProjectOnPlane(inward,Vector3.up).normalized;
     var row=new GateMotion296{Id=id,Kind=kind,Boundary=boundary,Rise=rise,Status="FAIL"};motions.Add(row);
     var start=boundary-inward*distance;var target=boundary+inward*distance;RaycastHit startHit=default,targetHit=default;bool startGood=false,extendedApproach=false;
     // Keep the exact seam crossing line while seeking a valid start farther
     // back on the same side. An invalid start never becomes a successful test.
     foreach(float candidateDistance in kind=="closed-wall"?new[]{distance,distance+2,distance+4,distance+8}:new[]{distance})
     {
      row.StartAttempts++;start=boundary-inward*candidateDistance;
      if(!Support(start,boundary.y,out startHit))continue;
      if(!EmptyStart(startHit.point+Vector3.up*(.08f+rise)))continue;
      startGood=true;distance=candidateDistance;break;
     }
     if(!startGood&&kind=="closed-wall")foreach(float candidateDistance in new[]{16f,20f,24f})
     {
      row.StartAttempts++;start=boundary-inward*candidateDistance;
      if(!Support(start,boundary.y,out startHit)||!EmptyStart(startHit.point+Vector3.up*.08f))continue;
      startGood=true;extendedApproach=true;distance=candidateDistance;break;
     }
     bool targetGood=Support(target,boundary.y,out targetHit);
     if(!startGood||expectCross&&!targetGood)
     {row.Detail=!startGood?"No clear supported start on the seam line within 12m":"No supported opposite approach for opened crossing";row.Start=start;row.Target=target;return;}
     start=startHit.point;
     if(targetGood){target=targetHit.point;row.EndSupport=Path296(targetHit.collider.transform);}else{target.y=field.Sample(target.x,target.z);row.EndSupport="No walkable target floor; a boundary crossing still fails a closed-wall test";}
     row.StartSupport=Path296(startHit.collider.transform);
     row.Start=start;row.Target=target;contact.Barriers.Clear();contact.TerrainContacts.Clear();
     cc.enabled=false;go.transform.position=start+Vector3.up*(.08f+rise);cc.enabled=true;Physics.SyncTransforms();
     if(!EmptyStart(go.transform.position)){row.Detail="Approach capsule is obstructed before movement";row.End=go.transform.position;return;}
     float vertical=0;var prior=go.transform.position;int maximum=Mathf.CeilToInt(distance*2/.12f)+90;
     for(int i=0;i<maximum;i++)
     {
      var delta=Vector3.ProjectOnPlane(target-go.transform.position,Vector3.up);row.Steps++;
      if(delta.magnitude<.25f)break;
      vertical=cc.isGrounded?-2:Mathf.Max(-24,vertical-20*.02f);
      cc.Move(Vector3.ClampMagnitude(delta,.12f)+Vector3.up*(vertical*.02f));
      row.Travel+=Vector3.ProjectOnPlane(go.transform.position-prior,Vector3.up).magnitude;prior=go.transform.position;
      if(go.transform.position.y<Mathf.Min(start.y,target.y)-5)break;
     }
     row.End=go.transform.position;row.BoundaryProgress=Vector3.Dot(Vector3.ProjectOnPlane(row.End-boundary,Vector3.up),inward);
     bool reached=Vector3.ProjectOnPlane(row.End-target,Vector3.up).magnitude<.35f&&Mathf.Abs(row.End.y-target.y)<.6f;
     row.Barrier=contact.Barriers.FirstOrDefault()??"";
     bool actualContact=contact.Barriers.Count>0;
     if(actualContact)row.ContactMethod="OnControllerColliderHit";
     if(!actualContact)
     {
      var bottom=row.End+cc.center+Vector3.down*(cc.height*.5f-cc.radius);
      var top=row.End+cc.center+Vector3.up*(cc.height*.5f-cc.radius);
      var nearby=Physics.OverlapCapsule(bottom,top,cc.radius+cc.skinWidth+.08f,~0,QueryTriggerInteraction.Ignore).FirstOrDefault(Barrier);
      if(nearby!=null){actualContact=true;row.Barrier=Path296(nearby.transform);row.ContactMethod="Final capsule overlap at controller skin";}
      else
      {
       var hit=Physics.CapsuleCastAll(bottom,top,cc.radius*.98f,inward,.30f+cc.skinWidth,~0,QueryTriggerInteraction.Ignore)
        .Where(h=>h.collider!=cc&&h.normal.y<Mathf.Cos(cc.slopeLimit*Mathf.Deg2Rad)).OrderBy(h=>h.distance).FirstOrDefault();
       if(Barrier(hit.collider)){actualContact=true;row.Barrier=Path296(hit.collider.transform);row.ContactMethod="First obstruction in final capsule sweep";row.ContactDistance=hit.distance;}
      }
     }
     bool stoppedAtWall=!reached&&row.BoundaryProgress<cc.radius&&row.Travel>.5f&&actualContact;
     row.Status=(expectCross?reached:stoppedAtWall)?"PASS":"FAIL";
     row.Detail=expectCross?(reached?"Actual controller reached opposite supported approach":"Opened passage did not reach opposite approach"):
      (stoppedAtWall?"Actual controller approached and stopped in contact with visible perimeter/door":"No proven perimeter stop: crossed, pre-obstructed or lost supporting ground");
     if(extendedApproach&&!reached&&!stoppedAtWall&&row.BoundaryProgress<0&&row.Travel>.5f)
     {
      var steep=contact.TerrainContacts.LastOrDefault(h=>h.Normal.y>0&&h.Normal.y<Mathf.Cos(cc.slopeLimit*Mathf.Deg2Rad)-.001f&&Vector3.ProjectOnPlane(h.Point-row.End,Vector3.up).magnitude<1.25f);
      float wallY=Mathf.Max(field.Sample(boundary.x,boundary.z),Mathf.Max(field.Sample(boundary.x-inward.x*12,boundary.z-inward.z*12),field.Sample(boundary.x+inward.x*12,boundary.z+inward.z*12)));
      bool wallBody=Physics.RaycastAll(new Vector3(boundary.x,wallY+1,boundary.z)-inward*12,inward,24,~0,QueryTriggerInteraction.Ignore).Any(h=>Barrier(h.collider));
      bool wallLift=Physics.RaycastAll(new Vector3(boundary.x,wallY+3.4f,boundary.z)-inward*12,inward,24,~0,QueryTriggerInteraction.Ignore).Any(h=>Barrier(h.collider));
      if(steep.Collider!=null&&wallBody&&wallLift)
      {
       row.Kind="terrain-blocked-approach";row.Status="PASS";row.Barrier=Path296(steep.Collider.transform);row.BlockingNormal=steep.Normal;
       row.BlockingSlope=Vector3.Angle(steep.Normal,Vector3.up);row.ContactMethod="OnControllerColliderHit on terrain steeper than actual controller slope limit";
       row.Detail="Actual controller started on valid distant ground and stopped against steep terrain before the perimeter; separate body and 2.4m-lift wall rays remain blocked. This is not a wall-contact probe.";
      }
     }
    }
    void Summon(string id,Vector3 boundary,Vector3 inward,float distance)
    {
     var row=new GateSummon296{Id=id,Direction=inward,Status="FAIL",Scope="Near-wall summon rejection",ApproachDistance=distance};summons.Add(row);
     var p=boundary-inward*distance;
     bool extended=false;RaycastHit ground;
     if(!Support(p,boundary.y,out ground))
     {
      bool found=false;
      foreach(float candidateDistance in new[]{16f,20f,24f})
      {
       p=boundary-inward*candidateDistance;if(!Support(p,boundary.y,out ground)||!EmptyStart(ground.point+Vector3.up*.08f))continue;
       found=true;extended=true;row.ApproachDistance=candidateDistance;row.Scope="Nearest valid outer approach; same-side placement is allowed, cross-perimeter placement is rejected";break;
      }
      if(!found){row.Reason="No permanent player approach support";row.Player=p;return;}
     }
     row.Player=ground.point;
     if(!summonReady){row.Reason="Service not ready: "+readyReason;return;}
     cc.enabled=false;Physics.SyncTransforms();
     row.Accepted=summon.TryFindPlacement(row.Player,inward,out var placement,out var reason);row.Placement=placement.Position;row.Reason=reason;row.Diagnostic=summon.LastPlacementDiagnostic;
     bool sameSide=false;
     if(extended&&row.Accepted)
     {
      var loop=receipt.Loops.OrderBy(l=>Enumerable.Range(0,l.Points.Length).Min(i=>
      {
       var a=l.Points[i];var d=Vector3.ProjectOnPlane(l.Points[(i+1)%l.Points.Length]-a,Vector3.up);
       var q=a+d*Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(boundary-a,Vector3.up),d)/d.sqrMagnitude);
       return Vector3.ProjectOnPlane(q-boundary,Vector3.up).sqrMagnitude;
      })).First();
      bool side=GateInside296(loop.Points,row.Player);var hull=summon.Vehicle.Hull;var centre=hull.center;var half=hull.size*.5f;
      sameSide=GateInside296(loop.Points,placement.Position)==side;
      foreach(float x in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})sameSide&=GateInside296(loop.Points,placement.Position+placement.Rotation*(centre+new Vector3(x*half.x,0,z*half.z)))==side;
     }
     row.Status=!row.Accepted||extended&&sameSide?"PASS":"FAIL";
    }
    bool Support(Vector3 xz,float expected,out RaycastHit chosen)
    {
     chosen=default;float ground=field.Sample(xz.x,xz.z);float top=Mathf.Max(ground,expected)+3;
     var hits=Physics.RaycastAll(new Vector3(xz.x,top,xz.z),Vector3.down,Mathf.Max(20,top-ground+8),~0,QueryTriggerInteraction.Ignore)
      .Where(h=>h.collider!=cc&&h.collider.gameObject.scene==scene&&h.rigidbody==null&&!(h.collider is CharacterController)&&h.normal.y>=Mathf.Cos(cc.slopeLimit*Mathf.Deg2Rad))
      .OrderBy(h=>h.distance).ToArray();
     if(hits.Length==0)return false;chosen=hits[0];return true;
    }
    bool EmptyStart(Vector3 p)
    {
     var centre=p+cc.center;var a=centre+Vector3.down*(cc.height*.5f-cc.radius);var b=centre+Vector3.up*(cc.height*.5f-cc.radius);
     return !Physics.OverlapCapsule(a,b,Mathf.Max(.05f,cc.radius-cc.skinWidth),~0,QueryTriggerInteraction.Ignore)
      .Any(c=>c!=cc&&c.gameObject.scene==scene&&!(c is CharacterController));
    }
   }
   finally
   {
    Object.DestroyImmediate(go);
    foreach(var state in doorStates)if(state.door!=null)state.door.SetOpened(state.requested,state.complete||!state.requested);
    foreach(var item in rotations)if(item.transform!=null)item.transform.localRotation=item.rotation;
    if(summon!=null)
    {
     typeof(WorldMacroPalanquinSummon).GetProperty("LastResult").SetValue(summon,originalResult);
     typeof(WorldMacroPalanquinSummon).GetProperty("LastPlacementDiagnostic").SetValue(summon,originalDiagnostic);
    }
    Physics.SyncTransforms();
   }
   var output=new GateMobilityReceipt296{Utc=DateTime.UtcNow.ToString("O"),PlayerHeight=source.height,PlayerRadius=source.radius,PlayerStepOffset=source.stepOffset,
    PlayerSlopeLimit=source.slopeLimit,SummonReady=summonReady,Motions=motions.ToArray(),Summons=summons.ToArray(),Lifts=lifts.ToArray()};
   Directory.CreateDirectory(O296);File.WriteAllText(O296+"/gate-mobility.json",JsonUtility.ToJson(output,true));
   foreach(var group in motions.GroupBy(m=>m.Kind))report.Add((group.All(m=>m.Status=="PASS")?"PASS ":"FAIL ")+group.Key+" actual CharacterController probes="+group.Count()+" failed="+group.Count(m=>m.Status!="PASS"));
   report.Add((summons.All(s=>s.Status=="PASS")&&summons.Count>0?"PASS ":"FAIL ")+"actual summon placement rejection="+summons.Count+" failed="+summons.Count(s=>s.Status!="PASS"));
   report.Add((lifts.Count>0&&lifts.All(l=>l.Status=="PASS")?"PASS ":"FAIL ")+"authored amplified lift nodes="+lifts.Count+" currently enabled="+lifts.Count(l=>l.Enabled)+" requiring review="+lifts.Count(l=>l.Status!="PASS"));
   foreach(var failure in motions.Where(m=>m.Status!="PASS").Take(15))report.Add("DETAIL "+failure.Id+" "+failure.Kind+" at "+failure.Boundary+": "+failure.Detail);
   report.Add("INFO Full receipt includes every coordinate, permanent support and measured endpoint. This finite movement audit does not claim exhaustive player reachability, native spell drawing, or vehicle driving.");
   File.WriteAllLines(O296+"/gate-mobility.txt",report);
  }
  static string Path296(Transform t)=>t.parent==null?t.name:Path296(t.parent)+"/"+t.name;
  static bool GateInside296(Vector3[] loop,Vector3 p)
  {
   bool inside=false;for(int i=0,j=loop.Length-1;i<loop.Length;j=i++)
    if((loop[i].z>p.z)!=(loop[j].z>p.z)&&p.x<(loop[j].x-loop[i].x)*(p.z-loop[i].z)/(loop[j].z-loop[i].z)+loop[i].x)inside=!inside;
   return inside;
  }
 }
 [ExecuteAlways] public sealed class ArchitectureGateContact296:MonoBehaviour
 {
  public readonly List<string> Barriers=new List<string>();
  public readonly List<(Collider Collider,Vector3 Point,Vector3 Normal)> TerrainContacts=new List<(Collider,Vector3,Vector3)>();
  void OnControllerColliderHit(ControllerColliderHit hit)
  {
   string root=hit.transform.root.name;
   if(root=="Reworld292_Terrain")TerrainContacts.Add((hit.collider,hit.point,hit.normal));
   if(root!="Architecture296_Gates"&&root!="CapitalSouthGate253")return;
   string path=hit.transform.name;for(var t=hit.transform.parent;t!=null;t=t.parent)path=t.name+"/"+path;
   if(!Barriers.Contains(path))Barriers.Add(path);
  }
 }
}
