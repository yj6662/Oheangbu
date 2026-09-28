using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Dress296=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class LandscapeClone296 { public string source,target; }
  [Serializable] sealed class LandscapeClones296 { public LandscapeClone296[] rows; }
  [Serializable] sealed class LandscapeRemoval296
  {
   public int SourceIndex;public string ArenaId,Reason,ColliderPath,Category,CorridorId;public int SegmentIndex=-1;public float Penetration;
   public Vector3 ProbeCentre,ProbeSize;public Quaternion ProbeRotation;public string VisualControlId,CompoundId;
   public Bounds SourceVisualBounds;
   public Dress296.FixedPlacement Placement;
  }
  [Serializable] sealed class LandscapeSheet296
  {
   public string CandidatePath,SourcePath,SourceSha256,RetainedSha256;
   public int SourceCount,RetainedCount,RemainingConflicts,ProtectedRetained,SanctuaryRetained,UnknownPrototypes;
   public LandscapeRemoval296[] Removed;
  }
  [Serializable] sealed class LandscapeLedger296
  {
   public string Revision="architecture-296",Method="Private active fixed-placement sheets only. Explicit arena volumes/colliders, generated bridge decks and 6m aprons, active gate apertures and their actual route paving. Tree body uses authored trunk radius; source LOD0 bounds for other natural objects. Sanctuary and named sacred placements preserved. Original placement order retained.";
   public int Version=2,ActiveRenderers,ActiveSheets,ArenaCount,BridgeCount,GateCount,Removed,RemainingConflicts;
   public int ArenaRemovals,BridgeRemovals,GateRemovals,ReviewedTreeRemovals,CompoundTreeRemovals;
   public bool OriginalSourcesUnchanged;public LandscapeSheet296[] Sheets;public LandscapeCollider296[] ColliderBindings;
   public LandscapeCorridor296[] Corridors;public LandscapeInput296[] Inputs;public LandscapeVisualControl296[] VisualControls;public LandscapeCompound296[] Compounds;
  }
  [Serializable] sealed class LandscapeCompound296
  {
   public string Id,Realm,SceneRoot;public Vector3 Position;public string[] ColliderPaths;
   [NonSerialized] public Collider[] Colliders;[NonSerialized] public Bounds Bounds;
  }
  [Serializable] sealed class LandscapeVisualControls296{public int Version;public LandscapeVisualControl296[] Controls;}
  [Serializable] sealed class LandscapeVisualControl296
  {
   public string Id,SourcePath,SourceSha256,CorridorId,Reason,EvidencePath,EvidenceSha256,CaptureMetadataSha256;
   public Dress296.FixedPlacement Placement;
  }
  [Serializable] sealed class LandscapeInput296 {public string Path,Sha256;}
  [Serializable] sealed class LandscapeCorridor296
  {
   public string Id,Kind,OwnerId,RouteId,ScenePath;public float Width,Height=2.4f;public Vector3[] Points;
   [NonSerialized] public Bounds Bounds;
  }
  [Serializable] sealed class LandscapeCollider296
  {
   public string CandidateSheet,PlacementId,ScenePath,Type;public int ComponentIndex;public bool EnabledBefore;
   public Vector3 Position,Euler,Scale;
  }
  [Serializable] sealed class LandscapeRows296 { public Dress296.FixedPlacement[] rows; }
  sealed class LandscapeArena296
  {
   public CompactArchitectureSheetSO.Arena Data;public Collider[] Colliders;public Bounds Bounds;
  }
  struct LandscapeBody296
  {
   public Vector3 Centre,Size;public Quaternion Rotation;public Bounds Bounds;
  }
  static Bounds LandscapeTransformBounds296(Bounds source,Matrix4x4 matrix)
  {
   var c=matrix.MultiplyPoint3x4(source.center);var e=source.extents;
   var x=matrix.MultiplyVector(new Vector3(e.x,0,0));var y=matrix.MultiplyVector(new Vector3(0,e.y,0));var z=matrix.MultiplyVector(new Vector3(0,0,e.z));
   return new Bounds(c,new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z))*2);
  }
  static Bounds LandscapePrototypeBounds296(Dress296.Prototype prototype,bool trunkOnly=true)
  {
   var result=new Bounds(Vector3.up*prototype.Size.y*.5f,prototype.Size);bool found=false;
   if(prototype.Lods!=null&&prototype.Lods.Length>0&&prototype.Lods[0].Parts!=null)
    foreach(var part in prototype.Lods[0].Parts)
    {
     if(part?.Mesh==null)continue;var bounds=LandscapeTransformBounds296(part.Mesh.bounds,part.Local);
     if(found)result.Encapsulate(bounds);else{result=bounds;found=true;}
    }
   // The authored tree Radius is the trunk collision radius, not its canopy.
   if(trunkOnly&&prototype.Category==Dress296.Kind.Tree)
   {
    float r=Mathf.Max(.08f,prototype.Radius);
    result=new Bounds(new Vector3(0,result.center.y,0),new Vector3(r*2,result.size.y,r*2));
   }
   return result;
  }
  static LandscapeVisualControl296[] ReadLandscapeVisualControls296()
  {
   string file=O296+"/Controls/landscape-visual-clearance.json";
   if(!File.Exists(file))return Array.Empty<LandscapeVisualControl296>();
   var document=JsonUtility.FromJson<LandscapeVisualControls296>(File.ReadAllText(file));
   var controls=document?.Controls;
   var approved=new HashSet<string>(new[]{"292_tree_1178_2630"},StringComparer.Ordinal);
   if(document?.Version!=1||controls==null||controls.Length!=1||!approved.SetEquals(controls.Select(c=>c.Placement?.Id)))
    throw new InvalidOperationException("296 visual clearance is limited to the single reviewed tree with actual bridge passage overlap.");
   string reference=Path.GetFullPath(O296+"/Landscape/Reference/bridge-before-canopy-clearance.png");
   foreach(var c in controls)
   {
    if(c.SourcePath!="Assets/_Project/Art/World/Watershed295/Dressing/DryLandscape.asset"||c.CorridorId!="bridge:jeokro__cheolong"||
      !string.Equals(Path.GetFullPath(c.EvidencePath),reference,StringComparison.OrdinalIgnoreCase)||!File.Exists(reference)||
      SurfaceSha296(reference)!=c.EvidenceSha256||SurfaceSha296(reference+".json")!=c.CaptureMetadataSha256)
     throw new InvalidOperationException("Visual clearance evidence/source differs from the reviewed bridge capture: "+c.Id);
   }
   return controls;
  }
  static LandscapeCompound296[] LandscapeCompounds296(CompactArchitectureSheetSO architecture,out LandscapeInput296 input)
  {
   string file=O296+"/venue-complexes.json";input=null;
   if(!File.Exists(file))return Array.Empty<LandscapeCompound296>();
   input=new LandscapeInput296{Path=file,Sha256=SurfaceSha296(file)};
   var rows=JsonUtility.FromJson<ComplexLedger296>(File.ReadAllText(file)).Buildings;var result=new List<LandscapeCompound296>();
   foreach(var row in rows.OrderBy(r=>r.SceneRoot,StringComparer.Ordinal))
   {
    var arena=architecture.Arenas.SingleOrDefault(a=>string.Equals(a.Realm,row.Realm,StringComparison.OrdinalIgnoreCase));
    if(arena==null||!row.SceneRoot.StartsWith(arena.SceneRoot+"/",StringComparison.Ordinal)||row.SceneRoot==arena.SceneRoot)
     throw new InvalidOperationException("Compound clearance requires a recorded new child building, not an entire arena: "+row.SceneRoot);
    var root=Root296(row.SceneRoot);if(root==null||!root.activeInHierarchy)throw new InvalidOperationException("Missing new compound building: "+row.SceneRoot);
    if((root.transform.position-row.Position).sqrMagnitude>.0001f)throw new InvalidOperationException("Compound receipt position changed: "+row.SceneRoot);
    var colliders=root.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy).OrderBy(c=>LandscapeScenePath296(c.transform),StringComparer.Ordinal).ToArray();
    if(colliders.Length==0)throw new InvalidOperationException("New compound has no actual collider for clearance: "+row.SceneRoot);
    var bounds=colliders[0].bounds;foreach(var c in colliders)bounds.Encapsulate(c.bounds);
    result.Add(new LandscapeCompound296{Id=row.Id,Realm=row.Realm,SceneRoot=row.SceneRoot,Position=row.Position,ColliderPaths=colliders.Select(c=>LandscapeScenePath296(c.transform)).ToArray(),Colliders=colliders,Bounds=bounds});
   }
   return result.ToArray();
  }
  static LandscapeRemoval296 LandscapeCompoundConflict296(Dress296.FixedPlacement placement,Dress296.Prototype prototype,LandscapeCompound296[] compounds,BoxCollider body)
  {
   if(prototype.Category!=Dress296.Kind.Tree||LandscapeSacred296(placement))return null;
   var local=LandscapePrototypeBounds296(prototype,false);var shape=LandscapePlacementBody296(placement,local);body.size=shape.Size;
   foreach(var compound in compounds)
   {
    if(!compound.Bounds.Intersects(shape.Bounds))continue;
    foreach(var collider in compound.Colliders)
    {
     if(!collider.bounds.Intersects(shape.Bounds))continue;
     if(Physics.ComputePenetration(body,shape.Centre,shape.Rotation,collider,collider.transform.position,collider.transform.rotation,out _,out float depth)&&depth>.025f)
      return new LandscapeRemoval296{CompoundId=compound.Id,Reason="actual new compound collider penetration",ColliderPath=LandscapeScenePath296(collider.transform),Penetration=depth,
       ProbeCentre=shape.Centre,ProbeSize=shape.Size,ProbeRotation=shape.Rotation,SourceVisualBounds=local};
    }
   }
   return null;
  }
  static LandscapeBody296 LandscapePlacementBody296(Dress296.FixedPlacement placement,Bounds local)
  {
   if(!float.IsFinite(placement.Scale)||placement.Scale<=0)throw new InvalidOperationException("Invalid natural placement scale: "+placement.Id);
   var rotation=Quaternion.Euler(placement.Euler);var centre=placement.Position+rotation*(local.center*placement.Scale);
   var size=Vector3.Max(local.size*placement.Scale,Vector3.one*.02f);
   return new LandscapeBody296{Centre=centre,Size=size,Rotation=rotation,Bounds=LandscapeTransformBounds296(new Bounds(Vector3.zero,size),Matrix4x4.TRS(centre,rotation,Vector3.one))};
  }
  static string LandscapeScenePath296(Transform t)
  {
   string path=t.name;while(t.parent!=null){t=t.parent;path=t.name+"/"+path;}return path;
  }
  static bool LandscapeSacred296(Dress296.FixedPlacement p)
  {
   string key=(p.Id+" "+p.ClusterId+" "+p.PrototypeId).ToLowerInvariant();
   return key.Contains("sinmok")||key.Contains("cheongryong")||key.Contains("sacred");
  }
  static bool LandscapeSamePlacement296(Dress296.FixedPlacement a,Dress296.FixedPlacement b)=>
   a.Id==b.Id&&a.ClusterId==b.ClusterId&&a.PrototypeId==b.PrototypeId&&a.Position.Equals(b.Position)&&a.Euler.Equals(b.Euler)&&a.Scale.Equals(b.Scale)&&a.Preserve==b.Preserve;
  static Dress296.FixedPlacement LandscapeCopyPlacement296(Dress296.FixedPlacement p)=>new Dress296.FixedPlacement{
   Id=p.Id,ClusterId=p.ClusterId,PrototypeId=p.PrototypeId,Position=p.Position,Euler=p.Euler,Scale=p.Scale,Preserve=p.Preserve};
  static bool LandscapeVolume296(BoxCollider body,LandscapeBody296 shape,BoxCollider volume,Vector3 centre,Quaternion rotation,Vector3 size,out float depth)
  {
   depth=0;var bounds=LandscapeTransformBounds296(new Bounds(Vector3.zero,size),Matrix4x4.TRS(centre,rotation,Vector3.one));
   if(!bounds.Intersects(shape.Bounds))return false;
   volume.size=size;body.size=shape.Size;
   return Physics.ComputePenetration(body,shape.Centre,shape.Rotation,volume,centre,rotation,out _,out depth)&&depth>.02f;
  }
  static LandscapeRemoval296 LandscapeConflict296(Dress296.FixedPlacement placement,Dress296.Prototype prototype,Bounds local,LandscapeArena296[] arenas,LandscapeCorridor296[] corridors,BoxCollider body,BoxCollider volume)
  {
   if(prototype.Category==Dress296.Kind.Prop||LandscapeSacred296(placement))return null;
   var shape=LandscapePlacementBody296(placement,local);
   foreach(var arena in arenas)
   {
    if(!arena.Bounds.Intersects(shape.Bounds))continue;
    var a=arena.Data;float height=a.Interior?Mathf.Max(2.4f,a.ClearHeight):2.4f;
    if(LandscapeVolume296(body,shape,volume,a.Centre+Vector3.up*(height*.5f+.025f),Quaternion.Euler(0,a.Yaw,0),new Vector3(a.ClearSize.x,height,a.ClearSize.y),out float depth))
     return new LandscapeRemoval296{ArenaId=a.Id,Reason=a.Interior?"authored interior clear volume":"authored combat floor volume",Penetration=depth};
    for(int i=1;i<a.Approach.Length;i++)
    {
     var from=a.Approach[i-1];var to=a.Approach[i];var delta=to-from;float length=delta.magnitude;if(length<.001f)continue;
     if(LandscapeVolume296(body,shape,volume,(from+to)*.5f+Vector3.up*1.2f,Quaternion.LookRotation(delta,Vector3.up),new Vector3(4,2.4f,length+.03f),out depth))
      return new LandscapeRemoval296{ArenaId=a.Id,Reason="authored 4m approach segment "+(i-1),Penetration=depth};
    }
    // Completely concealed objects below the masonry floor do not obstruct this venue.
    if(shape.Bounds.max.y<a.Centre.y+.025f)continue;
    body.size=shape.Size;
    foreach(var collider in arena.Colliders)
    {
     if(!collider.bounds.Intersects(shape.Bounds))continue;
     if(Physics.ComputePenetration(body,shape.Centre,shape.Rotation,collider,collider.transform.position,collider.transform.rotation,out _,out depth)&&depth>.025f)
      return new LandscapeRemoval296{ArenaId=a.Id,Reason="actual venue collider penetration",ColliderPath=LandscapeScenePath296(collider.transform),Penetration=depth};
    }
   }
   foreach(var corridor in corridors)
   {
    if(!corridor.Bounds.Intersects(shape.Bounds))continue;
    for(int i=1;i<corridor.Points.Length;i++)
    {
     var from=corridor.Points[i-1];var to=corridor.Points[i];var delta=to-from;float length=delta.magnitude;if(length<.001f)continue;
     if(LandscapeVolume296(body,shape,volume,(from+to)*.5f+Vector3.up*(corridor.Height*.5f),Quaternion.LookRotation(delta,Vector3.up),new Vector3(corridor.Width,corridor.Height,length+.03f),out float depth))
      return new LandscapeRemoval296{CorridorId=corridor.Id,SegmentIndex=i-1,Reason="generated "+corridor.Kind+" passage",Penetration=depth,ProbeCentre=shape.Centre,ProbeSize=shape.Size,ProbeRotation=shape.Rotation};
    }
   }
   return null;
  }
  static LandscapeCorridor296[] LandscapeCorridors296(CompactWorldSurface field,out LandscapeInput296[] inputs,out int bridgeCount,out int gateCount)
  {
   string[] files={O296+"/crossings.json",O296+"/Generated/crossings.json",O296+"/Generated/routes.json",O296+"/gates.json"};
   foreach(string path in files)if(!File.Exists(path))throw new InvalidOperationException("Build current candidate crossings and gates before Landscape296: "+path);
   inputs=files.Select(path=>new LandscapeInput296{Path=path,Sha256=SurfaceSha296(path)}).ToArray();
   var bridges=JsonUtility.FromJson<BridgeReceipt296>(File.ReadAllText(files[0])).Bridges;
   var generated=JsonUtility.FromJson<Crossings295>(File.ReadAllText(files[1])).Crossings;
   var routes=JsonUtility.FromJson<Routes292>(File.ReadAllText(files[2])).routes;
   var loops=JsonUtility.FromJson<GateReceipt296>(File.ReadAllText(files[3])).Loops;
   var result=new List<LandscapeCorridor296>();bridgeCount=0;gateCount=0;
   void Add(string id,string kind,string owner,string route,string path,float width,Vector3[] points)
   {
    if(points==null||points.Length<2||!float.IsFinite(width)||width<=0)throw new InvalidOperationException("Invalid generated landscape corridor "+id);
    var bounds=new Bounds(points[0],Vector3.zero);
    foreach(var p in points)
    {
     if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new InvalidOperationException("Non-finite landscape corridor "+id);
     bounds.Encapsulate(p);
    }
    // Broad phase only. Every removal still requires an oriented segment overlap.
    bounds.Expand(new Vector3(width+5,10,width+5));
    result.Add(new LandscapeCorridor296{Id=id,Kind=kind,OwnerId=owner,RouteId=route,ScenePath=path,Width=width,Points=points,Bounds=bounds});
   }
   foreach(var bridge in bridges.OrderBy(b=>b.Id,StringComparer.Ordinal))
   {
    var root=Root296(bridge.SceneRoot);if(root==null||!root.activeInHierarchy)throw new InvalidOperationException("Missing active generated bridge: "+bridge.SceneRoot);
    bridgeCount++;
    foreach(var route in bridge.Routes.OrderBy(r=>r.Id,StringComparer.Ordinal))
    {
     var crossing=generated.Single(c=>c.RouteId==route.Id);
     if(Mathf.Abs(crossing.RouteWidth-bridge.Width)>.001f||!crossing.Points.SequenceEqual(route.Points))throw new InvalidOperationException("Bridge receipts disagree: "+route.Id);
     Add("bridge:"+route.Id,"bridge",bridge.Id,route.Id,bridge.SceneRoot,crossing.RouteWidth,crossing.Points);
     // Match BridgeAprons296: 6m beyond each actual route end, not an arbitrary
     // large surrounding area. Heights follow the same ground/deck blend.
     foreach(bool start in new[]{true,false})
     {
      var end=crossing.Points[start?0:crossing.Points.Length-1]+Vector3.up*.04f;
      var forward=crossing.Points[start?1:crossing.Points.Length-2]-end;forward.y=0;forward.Normalize();
      var points=new Vector3[7];
      for(int i=0;i<points.Length;i++)
      {
       var p=end-forward*(6-i);float ground=field.Sample(p.x,p.z)+.03f;
       p.y=Mathf.Max(ground,Mathf.Lerp(ground,end.y,Mathf.SmoothStep(0,1,i/6f)));points[i]=p;
      }
      Add("apron:"+route.Id+(start?":start":":end"),"bridge apron",bridge.Id,route.Id,bridge.SceneRoot,crossing.RouteWidth,points);
     }
    }
   }
   foreach(var loop in loops.OrderBy(l=>l.Id,StringComparer.Ordinal))foreach(var gate in loop.Gates.OrderBy(g=>g.Id,StringComparer.Ordinal))
   {
    var root=Root296(gate.Path);if(root==null||!root.activeInHierarchy)continue;
    if(string.Equals(loop.Realm,"cheongrim",StringComparison.OrdinalIgnoreCase))continue;
    gateCount++;var inward=Vector3.ProjectOnPlane(gate.Inward,Vector3.up).normalized;
    if(inward.sqrMagnitude<.9f)throw new InvalidOperationException("Invalid gate direction: "+gate.Id);
    // The aperture is 8m to each side. Route paving below extends only over
    // the same +/-60m source arc used by BuildGateApproaches296.
    var aperture=new Vector3[17];
    for(int i=0;i<aperture.Length;i++){var p=gate.Centre+inward*(i-8);p.y=Mathf.Max(field.Sample(p.x,p.z)+.035f,gate.Centre.y+.025f);aperture[i]=p;}
    Add("gate:"+gate.Id,"gate aperture",gate.Id,"",gate.Path,gate.Width,aperture);
    foreach(string id in gate.Routes.OrderBy(r=>r,StringComparer.Ordinal))
    {
     var route=routes.SingleOrDefault(r=>r.id==id);if(route==null)throw new InvalidOperationException("Missing generated gate route: "+id);
     int near=NearestBridgePoint296(route.points,gate.Centre),first=near,last=near;float length=0;
     while(first>0&&length<60){length+=Vector3.Distance(route.points[first],route.points[first-1]);first--;}
     length=0;while(last<route.points.Length-1&&length<60){length+=Vector3.Distance(route.points[last],route.points[last+1]);last++;}
     var points=route.points.Skip(first).Take(last-first+1).Select(p=>
     {
      p.y=Mathf.Max(field.Sample(p.x,p.z)+.035f,p.y+.01f);
      if(Vector3.ProjectOnPlane(p-gate.Centre,Vector3.up).magnitude<8)p.y=Mathf.Max(p.y,gate.Centre.y+.025f);return p;
     }).ToArray();
     if(points.Length>=2)Add("gate-route:"+gate.Id+":"+id,"gate approach",gate.Id,id,gate.Path,Mathf.Min(gate.Width-.8f,Mathf.Max(2.6f,route.width)),points);
    }
   }
   return result.ToArray();
  }
  static void Landscape296(List<string> report)
  {
   var session=Session292();if(session.gameObject.scene.path!=Scene296||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Landscape296 requires the private candidate Edit scene.");
   var architecture=Sheet296();if(architecture.Arenas.Length!=5)throw new InvalidOperationException("Build all five authored venues before clearing their precise footprints.");
   var renderers=session.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true))
    .Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.Sheet!=null).ToArray();
   var sheets=renderers.Select(r=>r.Sheet).Distinct().OrderBy(AssetDatabase.GetAssetPath,StringComparer.Ordinal).ToArray();
   var mapping=JsonUtility.FromJson<LandscapeClones296>("{\"rows\":"+File.ReadAllText(O296+"/cloned-data.json")+"}").rows.ToDictionary(r=>r.target,r=>r.source,StringComparer.Ordinal);
   var sourceBySheet=new Dictionary<Dress296,Dress296>();var sourceHashes=new Dictionary<string,string>();
   foreach(var sheet in sheets)
   {
    string path=AssetDatabase.GetAssetPath(sheet);
    if(!path.StartsWith(A296+"/Data/",StringComparison.Ordinal)||!mapping.TryGetValue(path,out string original)||original.StartsWith(A296+"/",StringComparison.Ordinal))
     throw new InvalidOperationException("Landscape296 requires a mapped private sheet: "+path);
    var source=AssetDatabase.LoadAssetAtPath<Dress296>(original);if(source==null||source==sheet)throw new InvalidOperationException("Missing immutable source dressing: "+original);
    sourceBySheet.Add(sheet,source);sourceHashes[original]=SurfaceSha296(original);
    // A rerun may start from an already-cleared subset. Never overwrite unrelated candidate edits.
    var rows=source.FixedPlacements.ToDictionary(p=>p.Id,StringComparer.Ordinal);
    foreach(var current in sheet.FixedPlacements)
     if(!rows.TryGetValue(current.Id,out var baseline)||!LandscapeSamePlacement296(current,baseline))throw new InvalidOperationException("Unrecognized candidate placement edit; preserve it before Landscape296: "+path+" / "+current.Id);
   }
   var targets=new List<LandscapeArena296>();
   foreach(var arena in architecture.Arenas.Where(a=>!string.Equals(a.Realm,"cheongrim",StringComparison.OrdinalIgnoreCase)&&a.Id!="sanctuary296"))
   {
    var root=Root296(arena.SceneRoot);if(root==null||!root.activeInHierarchy)throw new InvalidOperationException("Missing active authored arena: "+arena.SceneRoot);
    var colliders=root.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger&&c.gameObject.activeInHierarchy).OrderBy(c=>LandscapeScenePath296(c.transform),StringComparer.Ordinal).ToArray();
    var bounds=LandscapeTransformBounds296(new Bounds(Vector3.up*(arena.ClearHeight*.5f),new Vector3(arena.ClearSize.x,Mathf.Max(2.4f,arena.ClearHeight),arena.ClearSize.y)),Matrix4x4.TRS(arena.Centre,Quaternion.Euler(0,arena.Yaw,0),Vector3.one));
    foreach(var c in colliders)bounds.Encapsulate(c.bounds);
    foreach(var p in arena.Approach){bounds.Encapsulate(p+new Vector3(2,2.4f,2));bounds.Encapsulate(p-new Vector3(2,.1f,2));}
    targets.Add(new LandscapeArena296{Data=arena,Colliders=colliders,Bounds=bounds});
   }
   var targetArray=targets.ToArray();var sanctuary=architecture.Arenas.Single(a=>a.Id=="sanctuary296");
   var corridors=LandscapeCorridors296(new CompactWorldSurface(session.MountainLayout),out var corridorInputs,out int bridgeCount,out int gateCount);
   var visualControls=ReadLandscapeVisualControls296();var visualByPlacement=visualControls.ToDictionary(c=>c.Placement.Id,StringComparer.Ordinal);
   var compounds=LandscapeCompounds296(architecture,out var compoundInput);
   if(compoundInput!=null)corridorInputs=corridorInputs.Concat(new[]{compoundInput}).ToArray();
   foreach(var c in visualControls)
   {
    var source=sourceBySheet.Values.SingleOrDefault(s=>AssetDatabase.GetAssetPath(s)==c.SourcePath);
    if(source==null||sourceHashes[c.SourcePath]!=c.SourceSha256||!source.FixedPlacements.Any(p=>LandscapeSamePlacement296(p,c.Placement)))
     throw new InvalidOperationException("Visual clearance must match its exact immutable source placement: "+c.Id);
   }
   if(visualControls.Length>0)corridorInputs=corridorInputs.Concat(new[]{new LandscapeInput296{Path=O296+"/Controls/landscape-visual-clearance.json",Sha256=SurfaceSha296(O296+"/Controls/landscape-visual-clearance.json")}}).ToArray();
   bool InSanctuary(Dress296.FixedPlacement p){var q=Quaternion.Euler(0,-sanctuary.Yaw,0)*(p.Position-sanctuary.Centre);return Mathf.Abs(q.x)<=sanctuary.ClearSize.x*.5f+20&&Mathf.Abs(q.z)<=sanctuary.ClearSize.y*.5f+20;}
   bool InSanctuaryCore(Dress296.FixedPlacement p){var q=Quaternion.Euler(0,-sanctuary.Yaw,0)*(p.Position-sanctuary.Centre);return Mathf.Abs(q.x)<=sanctuary.ClearSize.x*.5f&&Mathf.Abs(q.z)<=sanctuary.ClearSize.y*.5f;}
   var plans=new Dictionary<Dress296,Dress296.FixedPlacement[]>();var records=new List<LandscapeSheet296>();
   // Inactive GameObjects do not create native shapes for ComputePenetration.
   // Keep separate active query shapes far outside the authored world. Explicit
   // poses below are used for the query; neither physical object is moved there.
   var probe=new GameObject("Landscape296_TemporaryQueries"){hideFlags=HideFlags.HideAndDontSave};probe.transform.position=new Vector3(-100000,-100000,-100000);
   var bodyObject=new GameObject("Body"){hideFlags=HideFlags.HideAndDontSave};bodyObject.transform.SetParent(probe.transform,false);
   var volumeObject=new GameObject("Volume"){hideFlags=HideFlags.HideAndDontSave};volumeObject.transform.SetParent(probe.transform,false);volumeObject.transform.localPosition=Vector3.right*20;
   var body=bodyObject.AddComponent<BoxCollider>();var volume=volumeObject.AddComponent<BoxCollider>();body.isTrigger=volume.isTrigger=true;
   try
   {
    body.size=volume.size=Vector3.one;Physics.SyncTransforms();
    if(!Physics.ComputePenetration(body,Vector3.zero,Quaternion.identity,volume,Vector3.right*.5f,Quaternion.identity,out _,out float queryDepth)||queryDepth<.49f||
      Physics.ComputePenetration(body,Vector3.zero,Quaternion.identity,volume,Vector3.right*2,Quaternion.identity,out _,out _))
     throw new InvalidOperationException("Landscape296 temporary narrow-phase query self-check failed.");
    foreach(var sheet in sheets)
    {
     string path=AssetDatabase.GetAssetPath(sheet),original=mapping[path];var source=sourceBySheet[sheet];
     var prototypes=sheet.Prototypes.ToDictionary(p=>p.Id,StringComparer.Ordinal);var bounds=prototypes.ToDictionary(p=>p.Key,p=>LandscapePrototypeBounds296(p.Value),StringComparer.Ordinal);
     var removed=new List<LandscapeRemoval296>();var retained=new List<Dress296.FixedPlacement>();int sacred=0,sanctuaryKept=0,unknown=0;
     for(int i=0;i<source.FixedPlacements.Length;i++)
     {
      var p=source.FixedPlacements[i];LandscapeRemoval296 conflict=null;
      if(LandscapeSacred296(p))sacred++;
      bool protectedSanctuary=InSanctuary(p);
      if(prototypes.TryGetValue(p.PrototypeId,out var prototype))
      {
       if(!protectedSanctuary)
       {
        conflict=LandscapeConflict296(p,prototype,bounds[p.PrototypeId],targetArray,corridors,body,volume);
        if(conflict==null&&visualByPlacement.TryGetValue(p.Id,out var visual)&&visual.SourcePath==original)
        {
         if(prototype.Category!=Dress296.Kind.Tree||LandscapeSacred296(p)||!LandscapeSamePlacement296(p,visual.Placement))throw new InvalidOperationException("Visual tree clearance identity changed: "+p.Id);
         var fullBounds=LandscapePrototypeBounds296(prototype,false);
         conflict=LandscapeConflict296(p,prototype,fullBounds,Array.Empty<LandscapeArena296>(),corridors.Where(c=>c.Id==visual.CorridorId).ToArray(),body,volume);
         if(conflict==null)throw new InvalidOperationException("Reviewed canopy no longer overlaps the exact bridge corridor: "+p.Id);
         conflict.VisualControlId=visual.Id;conflict.SourceVisualBounds=fullBounds;conflict.Reason="reviewed canopy/branch passage intersection";
        }
       }
       // Only newly recorded building children may clear an overlapping tree
       // in the outer sanctuary buffer. The original central70x70/dome ground,
       // named sacred trees and unrelated natural objects remain untouched.
       if(conflict==null&&!InSanctuaryCore(p))conflict=LandscapeCompoundConflict296(p,prototype,compounds,body);
      }
      else unknown++;
      if(conflict==null){retained.Add(LandscapeCopyPlacement296(p));if(protectedSanctuary)sanctuaryKept++;}
      else{conflict.SourceIndex=i;conflict.Category=prototype.Category.ToString();conflict.Placement=LandscapeCopyPlacement296(p);removed.Add(conflict);}
     }
     int remaining=0;
     foreach(var p in retained)if(prototypes.TryGetValue(p.PrototypeId,out var prototype))
     {
      if(!InSanctuary(p)&&LandscapeConflict296(p,prototype,bounds[p.PrototypeId],targetArray,corridors,body,volume)!=null)remaining++;
      else if(!InSanctuaryCore(p)&&LandscapeCompoundConflict296(p,prototype,compounds,body)!=null)remaining++;
     }
     if(remaining!=0)throw new InvalidOperationException("Landscape296 retained a conflicting placement: "+path);
     var kept=retained.ToArray();plans.Add(sheet,kept);
     string text=JsonUtility.ToJson(new LandscapeRows296{rows=kept});
     using(var sha=System.Security.Cryptography.SHA256.Create())
      records.Add(new LandscapeSheet296{CandidatePath=path,SourcePath=original,SourceSha256=sourceHashes[original],SourceCount=source.FixedPlacements.Length,RetainedCount=kept.Length,
       RetainedSha256=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant(),RemainingConflicts=remaining,ProtectedRetained=sacred,SanctuaryRetained=sanctuaryKept,UnknownPrototypes=unknown,Removed=removed.ToArray()});
    }
   }
   finally{Object.DestroyImmediate(probe);}
   foreach(var original in sourceHashes)if(SurfaceSha296(original.Key)!=original.Value)throw new InvalidOperationException("Original dressing changed during landscape planning: "+original.Key);
   foreach(var input in corridorInputs)if(SurfaceSha296(input.Path)!=input.Sha256)throw new InvalidOperationException("Generated passage changed during landscape planning: "+input.Path);
   string ledgerPath=O296+"/Landscape/clearance.json";
   var prior=File.Exists(ledgerPath)?JsonUtility.FromJson<LandscapeLedger296>(File.ReadAllText(ledgerPath)).ColliderBindings:null;
   string ColliderKey(LandscapeCollider296 row)=>row.ScenePath+":"+row.ComponentIndex+":"+row.Type;
   var previous=(prior??Array.Empty<LandscapeCollider296>()).ToDictionary(ColliderKey,StringComparer.Ordinal);
   var bindings=new List<LandscapeCollider296>();var disable=new HashSet<Collider>();
   foreach(var renderer in renderers)
   {
    string path=AssetDatabase.GetAssetPath(renderer.Sheet);var record=records.Single(r=>r.CandidatePath==path);
    var removed=record.Removed.ToDictionary(r=>r.Placement.Id,r=>r.Placement,StringComparer.Ordinal);
    foreach(var collider in renderer.GetComponentsInChildren<Collider>(true))
    {
     if(collider.isTrigger||!removed.TryGetValue(collider.name,out var placement)||LandscapeSacred296(placement)||InSanctuaryCore(placement))continue;
     if((collider.transform.position-placement.Position).sqrMagnitude>.0025f)continue;
     var row=new LandscapeCollider296{CandidateSheet=path,PlacementId=placement.Id,ScenePath=LandscapeScenePath296(collider.transform),Type=collider.GetType().Name,
      ComponentIndex=Array.IndexOf(collider.GetComponents<Collider>(),collider),EnabledBefore=collider.enabled,Position=collider.transform.position,Euler=collider.transform.eulerAngles,Scale=collider.transform.lossyScale};
     if(previous.TryGetValue(ColliderKey(row),out var before))row.EnabledBefore=before.EnabledBefore;
     if(!row.EnabledBefore)continue;
     disable.Add(collider);bindings.Add(row);
    }
   }
   // Apply only after every private sheet and every geometry query has passed.
   foreach(var plan in plans)
   {
    if(!plan.Key.FixedPlacements.SequenceEqual(plan.Value,new LandscapePlacementComparer296())){plan.Key.FixedPlacements=plan.Value;EditorUtility.SetDirty(plan.Key);}
   }
   // Keep original enable state for the exact named children, so a later venue
   // revision can restore its own earlier removals without touching other objects.
   var ownedKeys=new HashSet<string>(bindings.Select(ColliderKey),StringComparer.Ordinal);
   foreach(var before in previous.Values.Where(r=>!ownedKeys.Contains(ColliderKey(r))))
   {
    var go=Root296(before.ScenePath);if(go==null)continue;var colliders=go.GetComponents<Collider>();
    if(before.ComponentIndex>=colliders.Length)throw new InvalidOperationException("Landscape296 owned collider binding changed: "+before.ScenePath);
    var collider=colliders[before.ComponentIndex];
    if(collider.GetType().Name!=before.Type||(collider.transform.position-before.Position).sqrMagnitude>.0025f)throw new InvalidOperationException("Landscape296 owned collider transform changed: "+before.ScenePath);
    collider.enabled=before.EnabledBefore;EditorUtility.SetDirty(collider);
   }
   foreach(var collider in disable){collider.enabled=false;EditorUtility.SetDirty(collider);}
   foreach(var renderer in renderers)renderer.Invalidate();
   var allRemoved=records.SelectMany(r=>r.Removed).ToArray();
   var ledger=new LandscapeLedger296{ActiveRenderers=renderers.Length,ActiveSheets=sheets.Length,ArenaCount=targets.Count,BridgeCount=bridgeCount,GateCount=gateCount,Corridors=corridors,Inputs=corridorInputs,VisualControls=visualControls,Compounds=compounds,
    ArenaRemovals=allRemoved.Count(r=>!string.IsNullOrEmpty(r.ArenaId)),BridgeRemovals=allRemoved.Count(r=>string.IsNullOrEmpty(r.VisualControlId)&&(r.CorridorId?.StartsWith("bridge:")==true||r.CorridorId?.StartsWith("apron:")==true)),
    GateRemovals=allRemoved.Count(r=>r.CorridorId?.StartsWith("gate:")==true||r.CorridorId?.StartsWith("gate-route:")==true),ReviewedTreeRemovals=allRemoved.Count(r=>!string.IsNullOrEmpty(r.VisualControlId)),CompoundTreeRemovals=allRemoved.Count(r=>!string.IsNullOrEmpty(r.CompoundId)),
    Removed=allRemoved.Length,RemainingConflicts=records.Sum(r=>r.RemainingConflicts),OriginalSourcesUnchanged=true,Sheets=records.ToArray(),ColliderBindings=bindings.OrderBy(ColliderKey,StringComparer.Ordinal).ToArray()};
   Directory.CreateDirectory(O296+"/Landscape");File.WriteAllText(ledgerPath,JsonUtility.ToJson(ledger,true));
   report.Add("Landscape296 active private sheets="+sheets.Length+", explicit arenas="+targets.Count+", active bridges="+bridgeCount+", active gates="+gateCount+", generated passage corridors="+corridors.Length+", removed natural placements="+ledger.Removed+", matched child colliders disabled="+disable.Count+", remaining geometric conflicts="+ledger.RemainingConflicts+". Sanctuary/sacred objects and all source assets preserved; full source order and removed transforms recorded. Actual Play traversal remains a separate check.");
   report.Add("Removal reasons: arena="+ledger.ArenaRemovals+", bridge="+ledger.BridgeRemovals+", gate="+ledger.GateRemovals+", reviewed canopy trees="+ledger.ReviewedTreeRemovals+", new compound actual collider trees="+ledger.CompoundTreeRemovals+". Existing central sanctuary/dome/sacred trees retained; outer sanctuary removal requires a specific new-building collider.");
  }
  sealed class LandscapePlacementComparer296:IEqualityComparer<Dress296.FixedPlacement>
  {
   public bool Equals(Dress296.FixedPlacement a,Dress296.FixedPlacement b)=>ReferenceEquals(a,b)||a!=null&&b!=null&&LandscapeSamePlacement296(a,b);
   public int GetHashCode(Dress296.FixedPlacement p)=>p.Id?.GetHashCode()??0;
  }
 }
}
