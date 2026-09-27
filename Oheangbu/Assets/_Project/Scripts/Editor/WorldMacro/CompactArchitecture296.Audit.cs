using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  [Serializable] sealed class OwnedCatalogue296 {public OwnedCatalogueRow296[] entries;}
  [Serializable] sealed class OwnedCatalogueRow296 {public string path,name,pack,category,label,reviewStatus;public int highestLodTriangles,colliders,lodGroups;public float[] size;public string[] materials;}
  [Serializable] sealed class ArchitectureInventoryRow296
  {
   public string ScenePath,Prefab,Kind;public bool Active;public Vector3 Position,BoundsCentre,BoundsSize;
   public int Renderers,Colliders,LodGroups,SourceTriangles,MissingMeshes,MissingMaterials,MaterialRepairs,FoundationPieces,VegetationOverlaps;
   public float GroundMinimum,GroundMaximum,LowestVisual;public string[] SourceMeshes,Materials,Notes;
  }
  [Serializable] sealed class ArchitectureInventory296
  {
   public string Scene,Utc,Scope;public int OwnedCatalogueCount,OwnedArchitectureCount;public ArchitectureInventoryRow296[] Buildings;
  }
  static bool IsArchitectureAsset296(string path)
  {
   if(string.IsNullOrEmpty(path))return false;
   return path.Contains("Hwaseong")||path.Contains("Jejumok")||path.Contains("Seyeonjeong")||path.Contains("House_1/")||path.Contains("House_2/")||path.Contains("ThatchedInn/")||path.Contains("/Architecture/")||path.Contains("/Architecture296/")||path.Contains("Kaesong_Hall");
  }
  static string ScenePathVenue296(Transform t)
  {string value=t.name;while(t.parent!=null){t=t.parent;value=t.name+"/"+value;}return value;}
  static bool ExplicitArchitectureScene296(Transform t)
  {
   string path=ScenePathVenue296(t);
   return new[]{"Reworld292_Sansa/","Watershed295_SunkenCapital/","Watershed295_Crossings/","CapitalSouthGate253/","Playtest_OriginalC2Inn/","Architecture296_Venues/","Architecture296_Gates/","Architecture296_Crossings/"}.Any(prefix=>path.StartsWith(prefix,StringComparison.Ordinal));
  }
  static void AuditArchitecture296(List<string> report)
  {
   var scene=Session292().gameObject.scene;if(scene.path!=Scene296)throw new Exception("Architecture audit/reinforcement must use the296 candidate.");
   var old=Root296("Architecture296_LivingFoundations");if(old!=null)Object.DestroyImmediate(old);
   var supportRoot=new GameObject("Architecture296_LivingFoundations");var field=new Oheangbu.Data.World.CompactWorldSurface(Session292().MountainLayout);
   var catalog=JsonUtility.FromJson<OwnedCatalogue296>(File.ReadAllText("../Docs/Assets/ModelCatalog.reviewed.json"));
   var owned=catalog.entries.Where(e=>IsArchitectureAsset296(e.path)).ToArray();
   File.WriteAllText(O296+"/owned-architecture-inventory.json",JsonUtility.ToJson(new OwnedCatalogue296{entries=owned},true));
   var groups=new Dictionary<GameObject,HashSet<Renderer>>();
   foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)))
   {
    if(r.GetComponentInParent<Oheangbu.Combat.EnemyVitals>()!=null)continue;
    var filter=r.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
    string meshPath=AssetDatabase.GetAssetPath(filter.sharedMesh);string prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r.gameObject);
    // Candidate preparation clones every material, including terrain/vegetation. A candidate
    // material path alone is therefore not evidence of architecture. Include explicit procedural
    // gate/bridge/precinct roots so source-less authored geometry is still audited.
    if(!IsArchitectureAsset296(meshPath)&&!IsArchitectureAsset296(prefab)&&!ExplicitArchitectureScene296(r.transform))continue;
    if(r.transform.IsChildOf(supportRoot.transform))continue;
    var group=PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
    if(group==null)
    {
     var ancestor=r.transform;while(ancestor.parent!=null&&ancestor.parent.parent!=null&&!ancestor.parent.name.StartsWith("Architecture296_",StringComparison.Ordinal)&&!ancestor.parent.name.StartsWith("Playtest_",StringComparison.Ordinal))ancestor=ancestor.parent;
     group=ancestor.gameObject;
    }
    if(!groups.TryGetValue(group,out var set)){set=new HashSet<Renderer>();groups.Add(group,set);}set.Add(r);
   }
   var sheets=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Where(r=>r.isActiveAndEnabled&&r.Sheet!=null).Select(r=>r.Sheet).Distinct().ToArray();
   var rows=new List<ArchitectureInventoryRow296>();int repairs=0,footings=0;
   foreach(var pair in groups.OrderBy(p=>ScenePathVenue296(p.Key.transform),StringComparer.Ordinal))
   {
    var group=pair.Key;var renderers=pair.Value.ToArray();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);var notes=new List<string>();int missing=0,missingMesh=0,restored=0;
    foreach(var r in renderers)
    {
     var mesh=r.GetComponent<MeshFilter>().sharedMesh;if(mesh==null){missingMesh++;continue;}var materials=r.sharedMaterials;
     if(ScenePathVenue296(r.transform).Contains("KoreanHall_Distant")&&(materials.Length<mesh.subMeshCount||materials.Take(mesh.subMeshCount).Any(m=>m==null)))
     {
      // The reviewed legacy OBJ has no imported renderer slots. Recover only this known proxy
      // by its unique submesh face counts, using the original material ledger read-only.
      materials=KaesongProxyMaterials295(mesh,materials).Select(m=>
      {string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m));var copy=Asset296("Materials/Venues/RecoveredHall_"+guid+".mat",()=>new Material(m));copy.CopyPropertiesFromMaterial(m);copy.shader=m.shader;copy.name="296_Recovered_"+m.name;EditorUtility.SetDirty(copy);return copy;}).ToArray();restored+=materials.Length;
      notes.Add("Recovered distant hall material slots using reviewed OBJ face counts and original material ledger; candidate copies only.");
     }
     var source=PrefabUtility.GetCorrespondingObjectFromOriginalSource(r) as Renderer;
     for(int i=0;i<mesh.subMeshCount;i++)
     {
      var current=i<materials.Length?materials[i]:null;var original=source!=null&&i<source.sharedMaterials.Length?source.sharedMaterials[i]:null;
      bool lostTexture=current!=null&&original!=null&&original.HasProperty("_BaseMap")&&original.GetTexture("_BaseMap")!=null&&(!current.HasProperty("_BaseMap")||current.GetTexture("_BaseMap")==null);
      bool broken=current==null||current.shader==null||current.shader.name=="Hidden/InternalErrorShader";
      if((broken||lostTexture)&&original!=null)
      {if(materials.Length<mesh.subMeshCount)Array.Resize(ref materials,mesh.subMeshCount);materials[i]=VenueMaterial296(original);restored++;}
      else if(broken)missing++;
     }
     if(restored>0){r.sharedMaterials=materials;EditorUtility.SetDirty(r);}
    }
    float lo=float.PositiveInfinity,hi=float.NegativeInfinity;
    foreach(float x in new[]{bounds.min.x,bounds.center.x,bounds.max.x})foreach(float z in new[]{bounds.min.z,bounds.center.z,bounds.max.z}){float y=GroundVenue296(field,x,z);lo=Mathf.Min(lo,y);hi=Mathf.Max(hi,y);}
    string sourcePath=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(group)??string.Empty;bool living=sourcePath.Contains("House_")||sourcePath.Contains("ThatchedInn")||group.name.IndexOf("house",StringComparison.OrdinalIgnoreCase)>=0||group.name.IndexOf("Inn",StringComparison.OrdinalIgnoreCase)>=0||group.name.Contains("LivingQuarters");int fixedCount=0;
    if(living&&group.activeInHierarchy&&bounds.size.x<45&&bounds.size.z<45&&bounds.size.y<20)
    {
     var support=new GameObject("Foundation_"+rows.Count.ToString("D3"));support.transform.SetParent(supportRoot.transform,false);var batch=new VenueBatch296("Living_"+rows.Count.ToString("D3"),support.transform);
     foreach(int side in new[]{-1,1})
     {
      for(float x=bounds.min.x+.5f;x<bounds.max.x-.5f;x+=3)
      {float z=side<0?bounds.min.z+.25f:bounds.max.z-.25f;float ground=GroundVenue296(field,x,z);float rise=bounds.min.y-ground-.04f;if(rise>.12f&&rise<2.5f)batch.Add(GateKit296+"SM_Bastion_001.prefab",new Vector3(x,ground-.1f,z),new Vector3(Mathf.Min(3,bounds.max.x-x),rise+.1f,.5f));}
      for(float z=bounds.min.z+.5f;z<bounds.max.z-.5f;z+=3)
      {float x=side<0?bounds.min.x+.25f:bounds.max.x-.25f;float ground=GroundVenue296(field,x,z);float rise=bounds.min.y-ground-.04f;if(rise>.12f&&rise<2.5f)batch.Add(GateKit296+"SM_Bastion_001.prefab",new Vector3(x,ground-.1f,z),new Vector3(Mathf.Min(3,bounds.max.z-z),rise+.1f,.5f),90);}
     }
     fixedCount=batch.Pieces;if(fixedCount>0)batch.Save();else Object.DestroyImmediate(support);footings+=fixedCount;
    }
    int overlaps=0;
    foreach(var sheet in sheets)
    {
     var types=sheet.Prototypes.ToDictionary(p=>p.Id,p=>p);
     foreach(var p in sheet.FixedPlacements)
     {if(!types.TryGetValue(p.PrototypeId,out var proto)||((int)proto.Category)>1)continue;var body=new Bounds(p.Position+Vector3.up*(proto.Size.y*p.Scale*.5f),proto.Size*p.Scale);if(bounds.Intersects(body))overlaps++;}
    }
    if(overlaps>0)notes.Add("Vegetation bounds overlap; inspect actual trunks/canopy. Audit does not delete original or protected vegetation.");
    if(hi>bounds.min.y+.6f)notes.Add("Terrain enters visual AABB; inspect foundation footprint/door thresholds before moving the building.");
    if(bounds.min.y-lo>2.5f)notes.Add("Large footing gap left for explicit site review; no tall automatic wall added.");
    if(missing>0||missingMesh>0)notes.Add("Unresolved source geometry/material coverage; review required.");
    rows.Add(new ArchitectureInventoryRow296{ScenePath=ScenePathVenue296(group.transform),Prefab=sourcePath,Kind=living?"Living/inn":"Owned architectural assembly",Active=group.activeInHierarchy,Position=group.transform.position,BoundsCentre=bounds.center,BoundsSize=bounds.size,Renderers=renderers.Length,Colliders=group.GetComponentsInChildren<Collider>(true).Length,LodGroups=group.GetComponentsInChildren<LODGroup>(true).Length,SourceTriangles=renderers.Sum(r=>{var m=r.GetComponent<MeshFilter>().sharedMesh;return m==null?0:m.triangles.Length/3;}),MissingMeshes=missingMesh,MissingMaterials=missing,MaterialRepairs=restored,FoundationPieces=fixedCount,VegetationOverlaps=overlaps,GroundMinimum=lo,GroundMaximum=hi,LowestVisual=bounds.min.y,SourceMeshes=renderers.Select(r=>AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)).Distinct().ToArray(),Materials=renderers.SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Select(AssetDatabase.GetAssetPath).Distinct().ToArray(),Notes=notes.ToArray()});repairs+=restored;
   }
   File.WriteAllText(O296+"/architecture-inventory.json",JsonUtility.ToJson(new ArchitectureInventory296{Scene=scene.path,Utc=DateTime.UtcNow.ToString("O"),Scope="All scene renderers linked to reviewed Korean architecture/building assets, including inactive legacy buildings; gameplay enemy hierarchies excluded. Material recovery from corresponding original source or the uniquely mapped reviewed HallLOD OBJ ledger; small living-building footing gaps reinforced with owned stone. No source/actor/quest/terrain edits.",OwnedCatalogueCount=catalog.entries.Length,OwnedArchitectureCount=owned.Length,Buildings=rows.ToArray()},true));
   report.Add("Architecture inventory: "+owned.Length+" owned entries / "+rows.Count+" scene assemblies; source material repairs="+repairs+", grounded living footing pieces="+footings+", unresolved missing slots="+rows.Sum(r=>r.MissingMaterials)+"; vegetation overlap evidence recorded.");
  }
 }
}
