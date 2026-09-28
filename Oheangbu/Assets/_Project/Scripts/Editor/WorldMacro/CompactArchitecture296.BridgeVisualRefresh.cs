using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class BridgePhysicsStamp296
  {public string Path,Type,Transform,ComponentSHA256,MeshSHA256;public int InstanceId;public bool Enabled;}
  [Serializable] sealed class BridgeVisualEvidence296
  {
   public string Utc,Scope="Only bridge renderers, visual meshes/materials and LOD assignments replaced. No new collider or top-facing exterior platform. Existing complete scene collider state and functional route JSON compared before/after.";
   public bool AllColliderStateIdentical,AllRouteBytesIdentical;public string BeforePhysicsSHA256,AfterPhysicsSHA256;
   public string BeforeRouteSHA256,AfterRouteSHA256,BeforeCrossingSHA256,AfterCrossingSHA256;
   public BridgePhysicsStamp296[] BeforeColliders,AfterColliders;public BridgeRecord296[] Bridges;
  }
  static string BridgeSha296(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
  static string BridgePath296(Transform transform){string path=transform.name;while(transform.parent!=null){transform=transform.parent;path=transform.name+"/"+path;}return path;}
  static BridgePhysicsStamp296[] BridgePhysicsState296(Scene scene)
  {
   var meshes=new Dictionary<Mesh,string>();var assets=new Dictionary<string,string>();
   string MeshHash(Mesh mesh)
   {
    if(mesh==null)return "none";if(meshes.TryGetValue(mesh,out string prior))return prior;
    string path=AssetDatabase.GetAssetPath(mesh),value;
    if(!string.IsNullOrEmpty(path)&&File.Exists(path))
    {
     if(!assets.TryGetValue(path,out string asset)){asset=BridgeSha296(File.ReadAllBytes(path));assets[path]=asset;}
     AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh,out string guid,out long localId);
     value=guid+":"+localId+":"+asset;
    }
    else
    {
     using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
     {foreach(var v in mesh.vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}foreach(int i in mesh.triangles)writer.Write(i);writer.Flush();value=BridgeSha296(stream.ToArray());}
    }
    meshes[mesh]=value;return value;
   }
   return scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>(true)).Select(c=>new BridgePhysicsStamp296{
    Path=BridgePath296(c.transform),Type=c.GetType().FullName,InstanceId=c.GetInstanceID(),Enabled=c.enabled,
    Transform=c.transform.localToWorldMatrix.ToString("R")+" activeSelf="+c.gameObject.activeSelf+" activeInHierarchy="+c.gameObject.activeInHierarchy,
    ComponentSHA256=BridgeSha296(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(c))),MeshSHA256=c is MeshCollider mesh?MeshHash(mesh.sharedMesh):"not-mesh"
   }).OrderBy(c=>c.Path,StringComparer.Ordinal).ThenBy(c=>c.Type,StringComparer.Ordinal).ThenBy(c=>c.InstanceId).ToArray();
  }
  static string BridgePhysicsHash296(BridgePhysicsStamp296[] stamps)
   =>BridgeSha296(Encoding.UTF8.GetBytes(string.Join("\n",stamps.Select(s=>JsonUtility.ToJson(s)))));
  static int BridgeDeckModuleCount296(Vector3[] points,float width,bool timber,Vector3[] skipShared)
  {
   var source=CrossingSource(timber?TimberFloor296:StoneFloor296);int columns=Mathf.Max(1,Mathf.CeilToInt(width/3.6f));
   float scale=width/columns/source.Bounds.size.x,step=Mathf.Min(2.8f,Mathf.Max(.04f,source.Bounds.size.z*scale)*.98f),total=BridgeLength296(points);int count=0;
   for(float d=0;d<total;d+=step){var p=BridgeSample296(points,Mathf.Min(total,d+step*.5f),out _);if(skipShared==null||DistanceBridgePolyline296(p,skipShared)>=.25f)count+=columns;}
   return count;
  }
  public static string RefreshBridgeStructureVisuals296()
  {
   var scene=SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene296||scene.isDirty)throw new InvalidOperationException("Saved296 Edit scene required for visual-only bridge refresh");
   var evidence=new BridgeVisualEvidence296{Utc=DateTime.UtcNow.ToString("O"),BeforeColliders=BridgePhysicsState296(scene)};
   evidence.BeforePhysicsSHA256=BridgePhysicsHash296(evidence.BeforeColliders);
   evidence.BeforeRouteSHA256=BridgeSha296(File.ReadAllBytes(O296+"/Generated/routes.json"));
   evidence.BeforeCrossingSHA256=BridgeSha296(File.ReadAllBytes(O296+"/Generated/crossings.json"));
   var receipt=JsonUtility.FromJson<BridgeReceipt296>(File.ReadAllText(O296+"/crossings.json"));
   var root=scene.GetRootGameObjects().Single(g=>g.name=="Architecture296_Crossings");
   var field=new CompactWorldSurface(Session292().MountainLayout);
   foreach(var bridge in receipt.Bridges)
   {
    var holder=root.transform.Find(bridge.Id);if(holder==null)throw new InvalidOperationException("Missing physical bridge "+bridge.Id);
    var group=holder.GetComponent<LODGroup>();var levels=group.GetLODs();var oldNear=levels[0].renderers;
    var oldStructure=oldNear.Where(r=>r.name.StartsWith(bridge.Id+"_structure_",StringComparison.Ordinal)||r.name.StartsWith(bridge.Id+"_loadbearing_",StringComparison.Ordinal)).ToArray();
    if(oldStructure.Length==0)throw new InvalidOperationException("Missing old bridge structure renderers "+bridge.Id);
    if(oldStructure.Any(r=>r.GetComponents<Collider>().Length!=0))throw new InvalidOperationException("Visual structure shares a collider GameObject; refresh refused");
    var kept=oldNear.Except(oldStructure).ToList();
    // Load persisted source QEM levels after a domain reload. Do not silently
    // fall back to expensive seam-preserving clustering for the retained deck.
    foreach(var renderer in kept)
    {
     var near=renderer.GetComponent<MeshFilter>().sharedMesh;string path=AssetDatabase.GetAssetPath(near);
     var lods=new[]{near,AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace(".asset","_sourceLOD1.asset")),AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace(".asset","_sourceLOD2.asset"))};
     if(lods.Any(m=>m==null))throw new InvalidOperationException("Retained source QEM level missing: "+path);
     combinedCrossingLods296[near]=lods;
    }
    var batch=new CrossingBatch296();bool trestle=bridge.Id=="mountain_hwanggyeong_main_bridge",timber=trestle||bridge.Id=="jeokro__cheolong_bridge";int deckModules=0;
    for(int index=0;index<bridge.Routes.Length;index++)
    {
     var route=bridge.Routes[index];var skip=index==1?bridge.Routes[0].Points:null;
     BuildBridgeLoadBearing296(batch,route.Points,bridge.Width,timber,trestle,field,bridge.Routes.Where(r=>r!=route).Select(r=>r.Points).ToArray(),skip,TempleRailProjector296(route.Id,route.Points));
     deckModules+=BridgeDeckModuleCount296(route.Points,bridge.Width,timber,skip);
    }
    var added=batch.Finish(holder,bridge.Id+"_loadbearing",false);kept.AddRange(added);
    var obsolete=levels.Skip(1).SelectMany(l=>l.renderers).Concat(oldStructure).Where(r=>r!=null&&!kept.Contains(r)).Select(r=>r.gameObject).Distinct().ToArray();
    foreach(var go in obsolete){if(go.GetComponents<Collider>().Length!=0)throw new Exception("LOD visual shares a collider");Object.DestroyImmediate(go);}
    CrossingLods296(holder,bridge.Id,kept.ToArray());
    bridge.SourceModules=deckModules+batch.Instances;bridge.Renderers=kept.Count;
    bridge.Kind=trestle?"CC0 braced timber trestle":timber?"KCISA masonry piers and braced CC0 timber deck":"KCISA masonry arched bridge";
    bridge.LodTriangles=group.GetLODs().Select(l=>l.renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)).ToArray();
    bridge.CollisionTriangles=holder.GetComponentsInChildren<MeshCollider>().Sum(c=>c.sharedMesh.triangles.Length/3);
    bridge.CollisionBoxes=holder.GetComponentsInChildren<BoxCollider>().Length;
   }
   evidence.AfterColliders=BridgePhysicsState296(scene);evidence.AfterPhysicsSHA256=BridgePhysicsHash296(evidence.AfterColliders);
   evidence.AfterRouteSHA256=BridgeSha296(File.ReadAllBytes(O296+"/Generated/routes.json"));
   evidence.AfterCrossingSHA256=BridgeSha296(File.ReadAllBytes(O296+"/Generated/crossings.json"));
   evidence.AllColliderStateIdentical=evidence.BeforePhysicsSHA256==evidence.AfterPhysicsSHA256;
   evidence.AllRouteBytesIdentical=evidence.BeforeRouteSHA256==evidence.AfterRouteSHA256&&evidence.BeforeCrossingSHA256==evidence.AfterCrossingSHA256;
   evidence.Bridges=receipt.Bridges;Directory.CreateDirectory(O296+"/Analysis");File.WriteAllText(O296+"/Analysis/bridge-structure-visual.json",JsonUtility.ToJson(evidence,true));
   if(!evidence.AllColliderStateIdentical||!evidence.AllRouteBytesIdentical)throw new InvalidOperationException("Visual refresh changed retained physics or route ledger; inspect evidence and reload saved scene");
   File.WriteAllText(O296+"/crossings.json",JsonUtility.ToJson(receipt,true));
   var sheet=Sheet296();foreach(var b in receipt.Bridges){var item=sheet.Structures.FirstOrDefault(s=>s.Id=="crossing296-"+b.Id);if(item!=null)item.Label=b.Kind;}EditorUtility.SetDirty(sheet);
   EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
   return "Four bridge load-bearing visuals refreshed; "+evidence.BeforeColliders.Length+" original scene colliders and both functional route JSON hashes identical. "+O296+"/Analysis/bridge-structure-visual.json";
  }
 }
}
