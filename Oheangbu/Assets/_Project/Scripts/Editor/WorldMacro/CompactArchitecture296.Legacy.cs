using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string LegacyChild296="Architecture296_LegacySurface";
  [Serializable] sealed class LegacyRecord296
  {
   public string Id,ScenePath,Kind,Source,ChildPath,OriginalMesh,ColliderBefore,ColliderAfter;
   public Vector3 Position,LossyScale,LocalPosition,LocalScale;public Quaternion Rotation,LocalRotation;
   public bool OriginalRendererDisabled,TransformUnchanged,CollidersUnchanged;public float TerrainBefore,TerrainAfter;public string[] Meshes,Materials;public int[] Triangles;
  }
  [Serializable] sealed class LegacySource296
  {public string Path,Sha256,Publisher,License,Use;}
  [Serializable] sealed class LegacyLedger296
  {
   public string Scope="Candidate-only renderer replacement. Original transforms and BoxColliders remain exact; no gameplay object, lantern, sacred object, terrain or source asset changes.";
   public string BaselineInventorySha256,HeightSha256Before,HeightSha256After,WaterSha256Before,WaterSha256After;public int FoundationStones,HouseholdTimbers,RetainedLanternCaps;
   public LegacyRecord296[] Items;public LegacySource296[] Sources;public string[] RetainedLanternPaths;
  }
  sealed class LegacyMeshes296
  {public Mesh[][] Levels;public Material[] Materials;public string Source;}

  static Material LegacyMaterial296(Material source)
  {
   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
   var material=Asset296("Materials/Legacy/"+guid+"_"+localId+".mat",()=>new Material(source));
   var shader=Shader.Find("Oheangbu/Reworld292/KoreanArchitecture");if(shader==null)throw new InvalidOperationException("Architecture surface shader missing");
   material.CopyPropertiesFromMaterial(source);material.shader=shader;material.name=source.name+"_Legacy296";material.enableInstancing=true;
   material.SetFloat("_Saturation",.35f);material.SetFloat("_AmbientFloor",.46f);material.SetFloat("_LightResponse",.7f);
   material.SetFloat("_WashStrength",0);material.SetFloat("_FadeInStart",-1);material.SetFloat("_FadeInEnd",0);material.SetFloat("_FadeOutStart",8000);material.SetFloat("_FadeOutEnd",10000);
   material.SetFloat("_WindAmplitude",0);material.SetFloat("_Leaf279",0);material.SetFloat("_SimpleLighting",0);EditorUtility.SetDirty(material);
   if(source.HasProperty("_BaseMap")&&(material.GetTexture("_BaseMap")!=source.GetTexture("_BaseMap")||material.GetTextureScale("_BaseMap")!=source.GetTextureScale("_BaseMap")||material.GetTextureOffset("_BaseMap")!=source.GetTextureOffset("_BaseMap")))throw new InvalidOperationException("Legacy source diffuse/UV changed");
   if(source.HasProperty("_BumpMap")&&material.GetTexture("_BumpMap")!=source.GetTexture("_BumpMap"))throw new InvalidOperationException("Legacy source normal changed");
   return material;
  }

  static LegacyMeshes296 LegacyMeshesFor296(bool timber)
  {
   string source=timber?TimberFloor296:StoneFloor296;var data=CrossingSource(source);var b=data.Bounds;
   if(Mathf.Min(b.size.x,b.size.y,b.size.z)<=0)throw new InvalidOperationException("Invalid legacy source bounds");
   string kind=timber?"HouseholdTimber":"FoundationStone";
   var result=new LegacyMeshes296{Source=source,Materials=data.Parts.Select(p=>LegacyMaterial296(p.material)).ToArray(),Levels=new Mesh[3][]};
   // Work in the original Cube's local -0.5..0.5 volume. Each source part keeps
   // its original UVs. The wood pile has fourteen human-scale planks, avoiding
   // a thin plank stretched into a solid 40cm beam.
   for(int level=0;level<3;level++)
   {
    result.Levels[level]=new Mesh[data.Parts.Count];
    for(int partIndex=0;partIndex<data.Parts.Count;partIndex++)
    {
     var part=CrossingPart296(source,partIndex,level);var instances=new List<CombineInstance>();
     int rows=timber?7:1,columns=timber?2:1;
     for(int row=0;row<rows;row++)for(int col=0;col<columns;col++)
     {
      var size=new Vector3(1,1f/rows,1f/columns);
      var centre=new Vector3(0,-.5f+(row+.5f)/rows,-.5f+(col+.5f)/columns);
      var scale=new Vector3(size.x/b.size.x,size.y/b.size.y,size.z/b.size.z);
      var matrix=Matrix4x4.TRS(centre,Quaternion.identity,scale)*Matrix4x4.Translate(-b.center)*part.matrix;
      instances.Add(new CombineInstance{mesh=part.mesh,subMeshIndex=part.submesh,transform=matrix});
     }
     string path="Meshes/Legacy/"+kind+"_LOD"+level+"_"+partIndex+".asset";
     var mesh=Asset296(path,()=>new Mesh());mesh.Clear();mesh.name=kind+"_LOD"+level;mesh.indexFormat=IndexFormat.UInt32;
     mesh.CombineMeshes(instances.ToArray(),true,true);mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
     if(mesh.vertexCount==0||mesh.GetIndexCount(0)==0||mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new InvalidOperationException("Legacy replacement mesh invalid");
     if(mesh.bounds.min.x<-.502f||mesh.bounds.max.x>.502f||mesh.bounds.min.y<-.502f||mesh.bounds.max.y>.502f||mesh.bounds.min.z<-.502f||mesh.bounds.max.z>.502f)
      throw new InvalidOperationException("Legacy LOD extends beyond original foot collision: "+path);
     result.Levels[level][partIndex]=mesh;
    }
   }
   return result;
  }

  static string LegacyColliders296(Transform root)=>string.Join("\n",root.GetComponents<Collider>().Select(c=>c.GetType().FullName+":"+EditorJsonUtility.ToJson(c)).OrderBy(s=>s,StringComparer.Ordinal));
  static bool LegacyPose296(Transform t,BaselineRenderer296 row)=>(t.position-row.Position).sqrMagnitude<.000001f&&(t.lossyScale-row.LossyScale).sqrMagnitude<.000001f&&Quaternion.Angle(t.rotation,row.Rotation)<.01f;
  static void LegacyArchitecture296(List<string> report)
  {
   if(SceneManager.GetActiveScene().path!=Scene296||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Legacy replacement requires candidate296 Edit scene");
   string inventoryPath=O296+"/Baseline/architecture-inventory295.json";
   var baseline=JsonUtility.FromJson<BaselineInventory296>(File.ReadAllText(inventoryPath));
   if(!baseline.SourceBytesUnchanged||baseline.Scene!=Scene295)throw new InvalidOperationException("Verified295 renderer inventory required");
   var layout=Session292().MountainLayout;var field=new CompactWorldSurface(layout);
   string heightPath=AssetDatabase.GetAssetPath(layout.FinalSurface),waterPath=AssetDatabase.GetAssetPath(layout.Hydrology.WaterLevels);
   string heightBefore=CrossingHash296(heightPath),waterBefore=CrossingHash296(waterPath);
   var foundation=baseline.Items.Where(r=>r.Active&&r.Enabled&&r.Primitive&&r.ScenePath.StartsWith("Reworld292_Sansa/",StringComparison.Ordinal)&&r.ScenePath.EndsWith("/FoundationStone",StringComparison.Ordinal)).ToArray();
   var timber=baseline.Items.Where(r=>r.Active&&r.Enabled&&r.Primitive&&r.ScenePath=="Village245/HouseholdTimber").ToArray();
   var caps=baseline.Items.Where(r=>r.Active&&r.Enabled&&r.Primitive&&r.ScenePath.EndsWith("/Warm_Porch_Lantern/Timber_Cap",StringComparison.Ordinal)).ToArray();
   if(foundation.Length!=45||timber.Length!=28||caps.Length!=40)throw new InvalidOperationException("Legacy review scope changed; expected45 foundation,28 wood piles,40 retained lantern caps");
   var all=Components295<MeshRenderer>().Where(r=>r.gameObject.activeInHierarchy).ToArray();
   var matched=new List<(BaselineRenderer296 row,MeshRenderer renderer,bool timber)>();
   foreach(var row in foundation.Concat(timber))
   {
    var candidates=all.Where(r=>LandscapeScenePath296(r.transform)==row.ScenePath&&LegacyPose296(r.transform,row)).ToArray();
    if(candidates.Length!=1)throw new InvalidOperationException("Legacy source must match exact path and pose once: "+row.ScenePath+" @ "+row.Position);
    var r=candidates[0];var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
    if(mesh==null||mesh.name!="Cube"||r.GetComponents<Collider>().Any(c=>!(c is BoxCollider))||r.GetComponents<Collider>().Length!=row.ColliderCount)
     throw new InvalidOperationException("Legacy reviewed primitive/collision changed: "+row.ScenePath);
    if(r.GetComponentInParent<LODGroup>()!=null)throw new InvalidOperationException("Legacy object belongs to an existing LOD owner: "+row.ScenePath);
    matched.Add((row,r,row.ScenePath=="Village245/HouseholdTimber"));
   }
   // Validate lantern identity before and after authoring, preserving their
   // gameplay/light objects as well as the small decorative timber caps.
   var lanterns=caps.Select(row=>all.Single(r=>r.enabled&&LandscapeScenePath296(r.transform)==row.ScenePath&&LegacyPose296(r.transform,row))).ToArray();
   string[] lanternBefore=lanterns.Select(r=>EditorJsonUtility.ToJson(r)+EditorJsonUtility.ToJson(r.transform)).ToArray();
   string[] originals={StoneFloor296,TimberFloor296};var originalHashes=originals.ToDictionary(p=>p,CrossingHash296,StringComparer.Ordinal);
   var stoneMeshes=LegacyMeshesFor296(false);var timberMeshes=LegacyMeshesFor296(true);var records=new List<LegacyRecord296>();
   foreach(var target in matched)
   {
    var row=target.row;var renderer=target.renderer;var t=renderer.transform;var geometry=target.timber?timberMeshes:stoneMeshes;
    string colliderBefore=LegacyColliders296(t);var localPosition=t.localPosition;var localRotation=t.localRotation;var localScale=t.localScale;float terrainBefore=field.Sample(t.position.x,t.position.z);
    var existing=t.Find(LegacyChild296);if(existing!=null)Object.DestroyImmediate(existing.gameObject);
    var holder=new GameObject(LegacyChild296);holder.transform.SetParent(t,false);holder.isStatic=true;
    var levels=new List<LOD>();var meshPaths=new List<string>();var materials=new List<string>();var triangles=new List<int>();
    for(int level=0;level<3;level++)
    {
     var renderers=new List<Renderer>();int count=0;
     for(int part=0;part<geometry.Levels[level].Length;part++)
     {
      var mesh=geometry.Levels[level][part];var go=new GameObject("LOD"+level+"_"+part,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(holder.transform,false);go.isStatic=true;
      go.GetComponent<MeshFilter>().sharedMesh=mesh;var replacement=go.GetComponent<MeshRenderer>();replacement.sharedMaterial=geometry.Materials[part];
      replacement.shadowCastingMode=renderer.shadowCastingMode;replacement.receiveShadows=renderer.receiveShadows;replacement.lightProbeUsage=renderer.lightProbeUsage;replacement.reflectionProbeUsage=renderer.reflectionProbeUsage;
      renderers.Add(replacement);count+=(int)mesh.GetIndexCount(0)/3;meshPaths.Add(AssetDatabase.GetAssetPath(mesh));materials.Add(AssetDatabase.GetAssetPath(replacement.sharedMaterial));
     }
     levels.Add(new LOD(level==0?.16f:level==1?.045f:.001f,renderers.ToArray()));triangles.Add(count);
    }
    var lod=holder.AddComponent<LODGroup>();lod.SetLODs(levels.ToArray());lod.RecalculateBounds();renderer.enabled=false;
    string colliderAfter=LegacyColliders296(t);
    bool pose=(t.position-row.Position).sqrMagnitude<.000001f&&LegacyPose296(t,row)&&t.localPosition==localPosition&&t.localRotation==localRotation&&t.localScale==localScale;
    if(!pose||colliderBefore!=colliderAfter||holder.GetComponentsInChildren<Collider>(true).Length!=0||t.GetComponentsInChildren<Transform>(true).Count(x=>x.name==LegacyChild296)!=1)
     throw new InvalidOperationException("Legacy replacement changed foot collision/transform or duplicated child");
    records.Add(new LegacyRecord296{Id=(target.timber?"timber":"foundation")+"_"+records.Count.ToString("D3"),ScenePath=row.ScenePath,Kind=target.timber?"CC0 stacked wood boards":"KCISA stone foundation",Source=geometry.Source,
     ChildPath=LandscapeScenePath296(holder.transform),OriginalMesh=row.Mesh,Position=t.position,Rotation=t.rotation,LossyScale=t.lossyScale,LocalPosition=localPosition,LocalRotation=localRotation,LocalScale=localScale,
     ColliderBefore=colliderBefore,ColliderAfter=colliderAfter,OriginalRendererDisabled=!renderer.enabled,TransformUnchanged=pose,CollidersUnchanged=colliderBefore==colliderAfter,
     TerrainBefore=terrainBefore,TerrainAfter=field.Sample(t.position.x,t.position.z),
     Meshes=meshPaths.ToArray(),Materials=materials.Distinct().ToArray(),Triangles=triangles.ToArray()});
   }
   if(lanterns.Where((r,i)=>EditorJsonUtility.ToJson(r)+EditorJsonUtility.ToJson(r.transform)!=lanternBefore[i]).Any())throw new InvalidOperationException("Retained lantern component/transform changed");
   if(originalHashes.Any(p=>CrossingHash296(p.Key)!=p.Value))throw new InvalidOperationException("Original stone/timber source changed");
   string heightAfter=CrossingHash296(heightPath),waterAfter=CrossingHash296(waterPath);if(heightBefore!=heightAfter||waterBefore!=waterAfter||records.Any(r=>r.TerrainBefore!=r.TerrainAfter))throw new InvalidOperationException("Legacy authoring changed terrain/water");
   var ledger=new LegacyLedger296{BaselineInventorySha256=CrossingHash296(inventoryPath),HeightSha256Before=heightBefore,HeightSha256After=heightAfter,WaterSha256Before=waterBefore,WaterSha256After=waterAfter,
    FoundationStones=foundation.Length,HouseholdTimbers=timber.Length,RetainedLanternCaps=caps.Length,Items=records.ToArray(),RetainedLanternPaths=caps.Select(r=>r.ScenePath).ToArray(),
    Sources=new[]{new LegacySource296{Path=StoneFloor296,Sha256=originalHashes[StoneFloor296],Publisher="KCISA / Jejumok Gwana",License="Owned Unity Asset Store EULA; not CC0",Use="Existing foundation footprints; source stone UV and materials"},
     new LegacySource296{Path=TimberFloor296,Sha256=originalHashes[TimberFloor296],Publisher="Rico Cilliers / Poly Haven modular wooden pier",License="CC0-1.0",Use="Fourteen source planks per wood pile; original UV grain, private3LOD; existing BoxCollider retained"}}};
   Directory.CreateDirectory(O296+"/Legacy");File.WriteAllText(O296+"/Legacy/replacements.json",JsonUtility.ToJson(ledger,true));
   report.Add("Legacy296 replaced stone foundations="+foundation.Length+", CC0 wood piles="+timber.Length+", retained lantern caps="+caps.Length+"; original transforms/colliders unchanged, replacement colliders=0,3LOD private shared meshes; central sanctuary/sacred/gameplay objects untouched.");
  }
 }
}
