using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class BridgeSupportLayoutRow296
  {
   public string Asset,Export,BeforeAssetSHA256,FirstSaveSHA256,SecondSaveSHA256;
   public string BeforePhysicalSHA256,ExportPhysicalSHA256,AfterPhysicalSHA256;
   public string[] BeforeAttributes,AfterAttributes;
   public int Vertices,Indices,ColliderBindings,BeforeUvValues,BeforeNonzeroUvValues,BeforeNonFiniteUvValues;
   public bool ExportMatches,PhysicalBytesUnchanged,BoundsUnchanged,ColliderStateUnchanged;
   public bool OnlyPositionAndNormal,NormalsFinite,SecondSaveIdentical;
  }
  [Serializable] sealed class BridgeSupportLayoutReceipt296
  {
   public string Utc,Status,Error,SceneSHA256Before,SceneSHA256After;
   public string Scope="Normalize only the two candidate physics-only approach meshes with Mesh.Clear(false). Preserve their exact position/index bytes and collider bindings. Compare each with its separate generated mesh JSON and verify two serialized writes. No renderer mesh, route, scene, terrain, water or vehicle setting is changed.";
   public bool SceneFileUnchanged,SceneRemainsClean,RoutesUnchanged;
   public List<BridgeSupportLayoutRow296> Meshes=new List<BridgeSupportLayoutRow296>();
  }
  static string BridgeSupportGeometryHash296(Vector3[] vertices,int[] indices)
  {
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
   {foreach(var p in vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}foreach(int i in indices)writer.Write(i);writer.Flush();return HashArchitecture296(stream.ToArray());}
  }
  static string[] BridgeSupportAttributes296(Mesh mesh)
   =>mesh.GetVertexAttributes().Select(a=>a.attribute+":"+a.format+":"+a.dimension+":stream"+a.stream).ToArray();
  static string BridgeSupportColliderState296(MeshCollider collider)
   =>EditorJsonUtility.ToJson(collider)+"|"+collider.transform.localToWorldMatrix.ToString("R")+"|"+collider.gameObject.activeSelf+"|"+collider.gameObject.activeInHierarchy;
  public static string NormalizeBridgeSupportLayout296()
  {
   var scene=SceneManager.GetActiveScene();
   if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Scene296||scene.isDirty)
    throw new InvalidOperationException("Saved Architecture296 Edit scene required");
   var receipt=new BridgeSupportLayoutReceipt296{Utc=DateTime.UtcNow.ToString("O"),Status="PREFLIGHT",SceneSHA256Before=HashArchitecture296(File.ReadAllBytes(scene.path))};
   string folder=O296+"/Analysis/SupportLayout";Directory.CreateDirectory(folder);
   string runFolder=folder+"/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff");Directory.CreateDirectory(runFolder);
   var suffixes=new[]{"jeokro__cheolong_bridge_0_support.asset","hyeongang__temple_bridge_0_approach_support.asset"};
   var exports=new[]{"jeokro__cheolong-mesh.json","hyeongang__temple-mesh.json"};
   var routePaths=new[]{G296+"/routes.json",G296+"/crossings.json"};
   var routeHashes=routePaths.Select(p=>HashArchitecture296(File.ReadAllBytes(p))).ToArray();
   var filters=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).ToArray();
   var colliders=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(true)).ToArray();
   var targets=new List<(Mesh Mesh,Vector3[] Vertices,int[] Indices,Bounds Bounds,MeshCollider[] Colliders,string[] States,BridgeSupportLayoutRow296 Row)>();
   try
   {
    // Validate both targets and archive the exact input bytes before any write.
    for(int i=0;i<suffixes.Length;i++)
    {
     string path=A296+"/Meshes/Crossings/"+suffixes[i],export=O296+"/BridgeApproach/"+exports[i];
     var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
     if(mesh==null||!mesh.isReadable||mesh.subMeshCount!=1||mesh.GetTopology(0)!=MeshTopology.Triangles)
      throw new InvalidOperationException("Missing or unexpected physics support mesh: "+path);
     if(filters.Any(f=>f.sharedMesh==mesh))throw new InvalidOperationException("Support mesh also renders; normalization refused: "+path);
     var bound=colliders.Where(c=>c.sharedMesh==mesh).ToArray();
     if(bound.Length==0||bound.Any(c=>!c.enabled||c.isTrigger||c.convex||!c.gameObject.activeInHierarchy||c.attachedRigidbody!=null))
      throw new InvalidOperationException("Unexpected support collider bindings: "+path);
     var vertices=mesh.vertices;var indices=mesh.triangles;
     if(vertices.Any(p=>!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))||indices.Any(n=>n<0||n>=vertices.Length))
      throw new InvalidOperationException("Invalid support geometry: "+path);
     var expected=JsonUtility.FromJson<VehicleRoadMesh296>(File.ReadAllText(export));
     var row=new BridgeSupportLayoutRow296{Asset=path,Export=export,Vertices=vertices.Length,Indices=indices.Length,ColliderBindings=bound.Length,
      BeforeAssetSHA256=HashArchitecture296(File.ReadAllBytes(path)),BeforePhysicalSHA256=BridgeSupportGeometryHash296(vertices,indices),
      ExportPhysicalSHA256=BridgeSupportGeometryHash296(expected.Vertices,expected.Triangles),BeforeAttributes=BridgeSupportAttributes296(mesh)};
     row.ExportMatches=row.BeforePhysicalSHA256==row.ExportPhysicalSHA256;
     if(mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
     {
      var uv=new List<Vector2>();mesh.GetUVs(0,uv);row.BeforeUvValues=uv.Count*2;
      foreach(var p in uv)foreach(float n in new[]{p.x,p.y}){if(n!=0)row.BeforeNonzeroUvValues++;if(!float.IsFinite(n))row.BeforeNonFiniteUvValues++;}
     }
     receipt.Meshes.Add(row);
     if(!row.ExportMatches)throw new InvalidOperationException("Support geometry differs from its independent exported JSON: "+path);
     File.Copy(path,runFolder+"/before-"+suffixes[i]);
     targets.Add((mesh,vertices,indices,mesh.bounds,bound,bound.Select(BridgeSupportColliderState296).ToArray(),row));
    }
    foreach(var target in targets)
    {
     var mesh=target.Mesh;var row=target.Row;
     for(int pass=0;pass<2;pass++)
     {
      // Identical to the canonical physics-only generators. Clear all old
      // attributes, then initialize every retained channel explicitly.
      mesh.Clear(false);mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=target.Vertices;mesh.triangles=target.Indices;
      mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);AssetDatabase.SaveAssetIfDirty(mesh);
      string hash=HashArchitecture296(File.ReadAllBytes(row.Asset));
      if(pass==0)row.FirstSaveSHA256=hash;else row.SecondSaveSHA256=hash;
      string physical=BridgeSupportGeometryHash296(mesh.vertices,mesh.triangles);
      if(physical!=row.BeforePhysicalSHA256)throw new InvalidOperationException("Physical geometry changed during support normalization: "+row.Asset);
     }
     row.AfterPhysicalSHA256=BridgeSupportGeometryHash296(mesh.vertices,mesh.triangles);
     row.PhysicalBytesUnchanged=row.AfterPhysicalSHA256==row.BeforePhysicalSHA256;
     row.BoundsUnchanged=mesh.bounds.Equals(target.Bounds);
     row.ColliderStateUnchanged=target.Colliders.Select((c,i)=>BridgeSupportColliderState296(c)==target.States[i]).All(s=>s);
     row.AfterAttributes=BridgeSupportAttributes296(mesh);
     var attributes=mesh.GetVertexAttributes();
     row.OnlyPositionAndNormal=attributes.Length==2&&attributes.All(a=>a.dimension==3&&a.format==VertexAttributeFormat.Float32&&(a.attribute==VertexAttribute.Position||a.attribute==VertexAttribute.Normal));
     row.NormalsFinite=mesh.normals.All(n=>float.IsFinite(n.x)&&float.IsFinite(n.y)&&float.IsFinite(n.z));
     row.SecondSaveIdentical=row.FirstSaveSHA256==row.SecondSaveSHA256;
     File.Copy(row.Asset,runFolder+"/after-"+Path.GetFileName(row.Asset));
     if(!row.PhysicalBytesUnchanged||!row.BoundsUnchanged||!row.ColliderStateUnchanged||!row.OnlyPositionAndNormal||!row.NormalsFinite||!row.SecondSaveIdentical)
      throw new InvalidOperationException("Support normalization invariant failed: "+row.Asset);
    }
    receipt.SceneSHA256After=HashArchitecture296(File.ReadAllBytes(scene.path));
    receipt.SceneFileUnchanged=receipt.SceneSHA256Before==receipt.SceneSHA256After;receipt.SceneRemainsClean=!scene.isDirty;
    receipt.RoutesUnchanged=routePaths.Select((p,i)=>HashArchitecture296(File.ReadAllBytes(p))==routeHashes[i]).All(s=>s);
    if(!receipt.SceneFileUnchanged||!receipt.SceneRemainsClean||!receipt.RoutesUnchanged)throw new InvalidOperationException("Support normalization changed scene or routes");
    receipt.Status="PASS";
   }
   catch(Exception e){receipt.Status="FAIL";receipt.Error=e.ToString();throw;}
   finally
   {
    File.WriteAllText(runFolder+"/checks.json",JsonUtility.ToJson(receipt,true));
    File.WriteAllText(folder+"/latest.json",JsonUtility.ToJson(receipt,true));
   }
   return "PASS: two physics-only support layouts normalized; exact physical bytes/collider state/bounds/routes/scene preserved; no unused channels; two serialized saves identical. "+folder+"/latest.json";
  }
 }
}
