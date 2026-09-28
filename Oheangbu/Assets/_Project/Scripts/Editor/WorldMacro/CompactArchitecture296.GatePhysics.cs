using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class GateWallPhysicsRow296
  {public string Path;public int Boxes,RemovedMeshes;public long RemovedTriangles;}
  [Serializable] sealed class GateWallPhysicsReceipt296
  {
   public string Utc,Status,Scene;
   public string Scope="Existing continuous WallSolid boxes own perimeter body collision. Detailed coping/parapet render meshes no longer contribute triangle collision. Doors, gatehouses, approaches, thresholds and foundations retain every collider. This preflight checks actual BoxCollider volume coverage; live movement is a separate GateMobility check. The caller saves the scene after its remaining build steps.";
   public int WallRuns,WallBoxes,RemovedMeshes,PreservedColliders,BodySamples,GukHeadSamples;
   public long RemovedTriangles;public bool ProtectedCollidersIdentical;
   public List<string> Failures=new List<string>();public List<GateWallPhysicsRow296> Walls=new List<GateWallPhysicsRow296>();
  }
  // Also accepts the explicitly authorized unsaved296 build continuation. It
  // stages and validates every target before removing components, and does not
  // save/discard the caller's other in-memory construction work.
  public static string SimplifyGateWallCollision296()
  {
   var scene=SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene296)throw new InvalidOperationException("Architecture296 Edit scene required");
   var root=scene.GetRootGameObjects().Single(g=>g.name=="Architecture296_Gates");
   var receipt=new GateWallPhysicsReceipt296{Utc=DateTime.UtcNow.ToString("O"),Scene=scene.path,Status="PREFLIGHT"};
   var holders=root.GetComponentsInChildren<Transform>(true).Where(t=>t.parent!=null&&t.name.StartsWith("Wall_",StringComparison.Ordinal)&&
    (t.parent.name=="CapitalPerimeter"||t.parent.name.EndsWith("_PrecinctWall",StringComparison.Ordinal))).ToArray();
   var targets=new List<MeshCollider>();var boxes=new List<BoxCollider>();
   foreach(var holder in holders)
   {
    var solids=holder.GetComponentsInChildren<BoxCollider>(true).Where(c=>c.transform.parent==holder&&c.name.StartsWith("WallSolid_",StringComparison.Ordinal)).ToArray();
    if(solids.Length==0||solids.Any(c=>!c.enabled||c.isTrigger||!c.gameObject.activeInHierarchy||c.attachedRigidbody!=null||c.size.x<=0||c.size.y<=0||c.size.z<=0))
     throw new InvalidOperationException("Missing/invalid continuous solid wall boxes: "+holder.name);
    var meshes=holder.GetComponentsInChildren<MeshCollider>(true);
    var lod=holder.GetComponent<LODGroup>();if(lod==null)throw new InvalidOperationException("Wall renderer LOD group missing: "+holder.name);
    var near=lod.GetLODs()[0].renderers;
    foreach(var mesh in meshes)
    {
     var filter=mesh.GetComponent<MeshFilter>();var renderer=mesh.GetComponent<Renderer>();
     if(mesh.transform.parent!=holder||filter==null||filter.sharedMesh!=mesh.sharedMesh||renderer==null||!near.Contains(renderer)||mesh.attachedRigidbody!=null)
      throw new InvalidOperationException("Unexpected wall child collider; retained without mutation: "+GatePhysicsPath296(mesh.transform));
    }
    boxes.AddRange(solids);targets.AddRange(meshes);
    receipt.Walls.Add(new GateWallPhysicsRow296{Path=GatePhysicsPath296(holder),Boxes=solids.Length,RemovedMeshes=meshes.Length,
     RemovedTriangles=meshes.Sum(c=>GatePhysicsTriangles296(c.sharedMesh))});
   }
   if(holders.Length==0||boxes.Count==0)throw new InvalidOperationException("No generated perimeter Wall_ holders/WallSolid boxes");
   receipt.WallRuns=holders.Length;receipt.WallBoxes=boxes.Count;receipt.RemovedMeshes=targets.Count;receipt.RemovedTriangles=receipt.Walls.Sum(w=>w.RemovedTriangles);
   var targetSet=new HashSet<Collider>(targets);
   var protectedColliders=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(true)).Where(c=>!targetSet.Contains(c)).ToArray();
   var protectedJson=protectedColliders.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();receipt.PreservedColliders=protectedColliders.Length;
   Physics.SyncTransforms();
   var loops=JsonUtility.FromJson<GateReceipt296>(File.ReadAllText(O296+"/gates.json"));var field=new CompactWorldSurface(Session292().MountainLayout);
   bool Covered(Vector3 point)
   {
    foreach(var box in boxes)
    {
     var b=box.bounds;if(point.x<b.min.x-.01f||point.x>b.max.x+.01f||point.z<b.min.z-.01f||point.z>b.max.z+.01f||point.y<b.min.y-.01f||point.y>b.max.y+.01f)continue;
     if((box.ClosestPoint(point)-point).sqrMagnitude<.0001f)return true;
    }
    return false;
   }
   foreach(var loop in loops.Loops)for(int edge=0;edge<loop.Points.Length;edge++)
   {
    var a=loop.Points[edge];var b=loop.Points[(edge+1)%loop.Points.Length];a.y=b.y=0;float length=Vector3.Distance(a,b);var along=(b-a).normalized;var inward=Vector3.Cross(along,Vector3.up);
    for(float distance=.5f;distance<length;distance+=2)
    {
     if(loop.Gates.Any(g=>g.Edge==edge&&Mathf.Abs(g.Distance-distance)<g.Width*.5f+.5f))continue;
     var point=a+along*distance;var outside=point-inward*12;var inside=point+inward*12;
     float ground=Mathf.Max(field.Sample(point.x,point.z),Mathf.Max(field.Sample(outside.x,outside.z),field.Sample(inside.x,inside.z)));
     point.y=ground+1;receipt.BodySamples++;if(!Covered(point))receipt.Failures.Add(loop.Id+" body "+point.ToString("F3"));
     point.y=ground+3.4f;receipt.GukHeadSamples++;if(!Covered(point))receipt.Failures.Add(loop.Id+" 2.4m lift+head "+point.ToString("F3"));
    }
   }
   string folder=O296+"/Collision";Directory.CreateDirectory(folder);
   if(receipt.Failures.Count>0)
   {
    receipt.Status="FAIL_UNCHANGED";File.WriteAllText(folder+"/wall-physics.json",JsonUtility.ToJson(receipt,true));
    throw new InvalidOperationException("WallSolid-only coverage failed before any mutation: "+string.Join("; ",receipt.Failures.Take(5)));
   }
   // All candidate targets and replacement body coverage have now passed.
   foreach(var mesh in targets)Object.DestroyImmediate(mesh);Physics.SyncTransforms();
   receipt.ProtectedCollidersIdentical=protectedColliders.Select((c,i)=>c!=null&&EditorJsonUtility.ToJson(c)==protectedJson[i]).All(x=>x);
   if(!receipt.ProtectedCollidersIdentical){receipt.Status="FAIL_PROTECTED_COLLIDER_CHANGED";File.WriteAllText(folder+"/wall-physics.json",JsonUtility.ToJson(receipt,true));throw new Exception("Unexpected protected collider mutation");}
   if(targets.Count>0)EditorSceneManager.MarkSceneDirty(scene);
   receipt.Status="PASS";File.WriteAllText(folder+"/wall-physics.json",JsonUtility.ToJson(receipt,true));
   return "Wall physics: removed "+receipt.RemovedMeshes+" duplicate mesh colliders / "+receipt.RemovedTriangles+" triangles; retained "+boxes.Count+" WallSolid boxes and "+protectedColliders.Length+" other colliders. Actual box volume samples="+receipt.BodySamples+" body + "+receipt.GukHeadSamples+" lift/head. Caller saves scene. "+folder+"/wall-physics.json";
  }
  static long GatePhysicsTriangles296(Mesh mesh){if(mesh==null)return 0;long total=0;for(int i=0;i<mesh.subMeshCount;i++)total+=(long)mesh.GetIndexCount(i)/3;return total;}
  static string GatePhysicsPath296(Transform t){string path=t.name;while(t.parent!=null){t=t.parent;path=t.name+"/"+path;}return path;}
 }
}
