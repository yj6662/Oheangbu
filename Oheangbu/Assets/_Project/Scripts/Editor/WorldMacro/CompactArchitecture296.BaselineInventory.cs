using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class BaselineMaterial296
  {public int Slot;public string Name,Asset,Shader,BaseMap,MainTex;public bool Missing;}
  [Serializable] sealed class BaselineRenderer296
  {
   public string ScenePath,RendererType,NearestPrefab,OriginalSource,Mesh,MeshName,Classification,ArchitectureReason;
   public bool Architecture,Primitive,Active,Enabled,EnemyHierarchy;public Vector3 Position,LossyScale,BoundsCentre,BoundsSize;
   public Quaternion Rotation;public int Vertices,Triangles,Submeshes,ColliderCount;public string LodGroup;public int[] LodLevels;public BaselineMaterial296[] Materials;
  }
  [Serializable] sealed class BaselineInventory296
  {public string Scene,SceneSha256Before,SceneSha256After,Utc,Scope,RestoredScene;public bool SourceBytesUnchanged,NoSceneOrAssetSave;public int Renderers,ArchitectureRenderers,PrimitiveArchitecture,InactiveArchitecture;public BaselineRenderer296[] Items;}
  public static string InventoryBaseline296()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Baseline inventory requires Edit mode.");
   for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("All open scenes must be saved before read-only baseline inspection.");
   var setup=EditorSceneManager.GetSceneManagerSetup();string beforeScene=SceneManager.GetActiveScene().path,beforeHash=HashVenue296(Scene295);var rows=new List<BaselineRenderer296>();
   try
   {
    var scene=EditorSceneManager.OpenScene(Scene295,OpenSceneMode.Single);
    foreach(var renderer in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).OrderBy(r=>ScenePathVenue296(r.transform),StringComparer.Ordinal))
    {
     var filter=renderer.GetComponent<MeshFilter>();Mesh mesh=filter!=null?filter.sharedMesh:(renderer as SkinnedMeshRenderer)?.sharedMesh;
     string meshPath=mesh==null?"":AssetDatabase.GetAssetPath(mesh),prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer.gameObject)??"";
     var original=PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);string originalPath=original==null?"":AssetDatabase.GetAssetPath(original);
     bool enemy=renderer.GetComponentInParent<Oheangbu.Combat.EnemyVitals>(true)!=null;
     bool asset=IsArchitectureAsset296(meshPath)||IsArchitectureAsset296(prefab)||IsArchitectureAsset296(originalPath),explicitRoot=ExplicitArchitectureScene296(renderer.transform);
     // Original295 materials can identify built-in primitive walls; unlike cloned296 materials,
     // their actual original source path does not classify every terrain renderer as architecture.
     bool material=renderer.sharedMaterials.Any(m=>m!=null&&IsArchitectureAsset296(AssetDatabase.GetAssetPath(m)));
     string path=ScenePathVenue296(renderer.transform);bool namedPrimitive=path.IndexOf("bridge",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("gate",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("house",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("inn/",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("palace",StringComparison.OrdinalIgnoreCase)>=0;
     bool primitive=mesh!=null&&(meshPath=="Resources/unity_builtin_extra"||meshPath=="Library/unity default resources"||string.IsNullOrEmpty(meshPath)&&new[]{"Cube","Cylinder","Plane","Quad","Sphere","Capsule"}.Contains(mesh.name));
     bool architecture=!enemy&&(asset||explicitRoot||material||primitive&&namedPrimitive);
     string reason=asset?"Original mesh/prefab/source architecture path":explicitRoot?"Explicit authored architecture root":material?"Original architecture material on primitive/authored mesh":primitive&&namedPrimitive?"Named built-in primitive assembly":"Other scene renderer, retained for coverage audit";
     var mats=renderer.sharedMaterials;int slotCount=Math.Max(mats.Length,mesh==null?0:mesh.subMeshCount);var slots=new List<BaselineMaterial296>();
     for(int i=0;i<slotCount;i++)
     {var m=i<mats.Length?mats[i]:null;slots.Add(new BaselineMaterial296{Slot=i,Name=m==null?"":m.name,Asset=m==null?"":AssetDatabase.GetAssetPath(m),Shader=m==null||m.shader==null?"":m.shader.name,Missing=m==null||m.shader==null||m.shader.name=="Hidden/InternalErrorShader",BaseMap=m!=null&&m.HasProperty("_BaseMap")?AssetDatabase.GetAssetPath(m.GetTexture("_BaseMap")):"",MainTex=m!=null&&m.HasProperty("_MainTex")?AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")):""});}
     LODGroup lod=renderer.GetComponentInParent<LODGroup>(true);int[] levels=lod==null?Array.Empty<int>():lod.GetLODs().Select((l,i)=>new{l,i}).Where(p=>p.l.renderers.Contains(renderer)).Select(p=>p.i).ToArray();
     int triangles=0;if(mesh!=null)for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)==MeshTopology.Triangles)triangles+=(int)mesh.GetIndexCount(i)/3;
     rows.Add(new BaselineRenderer296{ScenePath=path,RendererType=renderer.GetType().Name,NearestPrefab=prefab,OriginalSource=originalPath,Mesh=meshPath,MeshName=mesh==null?"":mesh.name,Classification=enemy?"Gameplay actor":architecture?"Architecture":"Other renderer",ArchitectureReason=reason,Architecture=architecture,Primitive=primitive,Active=renderer.gameObject.activeInHierarchy,Enabled=renderer.enabled,EnemyHierarchy=enemy,Position=renderer.transform.position,LossyScale=renderer.transform.lossyScale,Rotation=renderer.transform.rotation,BoundsCentre=renderer.bounds.center,BoundsSize=renderer.bounds.size,Vertices=mesh==null?0:mesh.vertexCount,Triangles=triangles,Submeshes=mesh==null?0:mesh.subMeshCount,ColliderCount=renderer.GetComponents<Collider>().Length,LodGroup=lod==null?"":ScenePathVenue296(lod.transform),LodLevels=levels,Materials=slots.ToArray()});
    }
   }
   finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
   string afterHash=HashVenue296(Scene295);bool unchanged=beforeHash==afterHash;if(!unchanged)throw new InvalidOperationException("Original295 scene bytes changed during read-only inventory.");
   var ledger=new BaselineInventory296{Scene=Scene295,SceneSha256Before=beforeHash,SceneSha256After=afterHash,Utc=DateTime.UtcNow.ToString("O"),Scope="Pure read of saved original295 scene used by before captures. Every Renderer retained, including inactive/disabled, primitive and other-renderer rows; Architecture flag selects source/explicit-root matches and excludes enemies. No material recovery, no object/asset creation, no transform change, no scene or asset save.",RestoredScene=SceneManager.GetActiveScene().path,SourceBytesUnchanged=unchanged,NoSceneOrAssetSave=true,Renderers=rows.Count,ArchitectureRenderers=rows.Count(r=>r.Architecture),PrimitiveArchitecture=rows.Count(r=>r.Architecture&&r.Primitive),InactiveArchitecture=rows.Count(r=>r.Architecture&&(!r.Active||!r.Enabled)),Items=rows.ToArray()};
   if(ledger.RestoredScene!=beforeScene)throw new InvalidOperationException("Read-only baseline did not restore the previous active scene.");
   Directory.CreateDirectory(O296+"/Baseline");string output=O296+"/Baseline/architecture-inventory295.json";File.WriteAllText(output,JsonUtility.ToJson(ledger,true));
   return "Baseline295 read-only: "+ledger.Renderers+" renderers, "+ledger.ArchitectureRenderers+" architecture, "+ledger.PrimitiveArchitecture+" primitive architecture, source bytes unchanged, previous scene restored. "+output;
  }
 }
}

