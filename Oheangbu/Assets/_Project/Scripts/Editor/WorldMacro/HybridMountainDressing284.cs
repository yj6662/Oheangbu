using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Object=UnityEngine.Object;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Material Rock284;
  static Mesh NormalizedRock284(int variant,int lod)
  {
   string path=A284+"/Meshes/Meshy_"+variant+"_"+lod+".asset";
   var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved!=null)return saved;
   var source=AssetDatabase.LoadAssetAtPath<Mesh>(A278+"/Meshy282/Bedrock_"+variant+"_LOD"+lod+".asset");
   var mesh=Object.Instantiate(source);var bounds=AssetDatabase.LoadAssetAtPath<Mesh>(A278+"/Meshy282/Bedrock_"+variant+"_LOD0.asset").bounds;
   var basePoint=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
   mesh.vertices=mesh.vertices.Select(v=>(v-basePoint)/bounds.size.y).ToArray();mesh.RecalculateBounds();
   return ArtMesh(mesh,path);
  }
  static float Steep284(UnityEngine.Terrain terrain,Vector3 p)=>terrain.terrainData.GetSteepness((p.x-Origin284.x)/Size284.x,(p.z-Origin284.z)/Size284.z);
  static Vector3 Ground284(UnityEngine.Terrain terrain,Vector3 p){p.y=terrain.SampleHeight(p)+Origin284.y;return p;}
  static void DressHybrid284(Transform root,UnityEngine.Terrain terrain,Vector3[] route)
  {
   DevSceneKit.EnsureFolder(A284+"/Meshes");DevSceneKit.EnsureFolder(A284+"/Materials");
   var mat=new Material(AssetDatabase.LoadAssetAtPath<Material>(A278+"/Materials/Granite.mat"));
   mat.SetFloat("_RockScale",.16f);mat.SetFloat("_JointStrength",.25f);mat.SetFloat("_AmbientFloor",.30f);mat.SetFloat("_LightResponse",.78f);mat.SetFloat("_WashStart",180);mat.SetFloat("_WashEnd",1450);mat.SetFloat("_WashStrength",.75f);mat.SetColor("_BaseColor",new Color(.88f,.89f,.86f));
   Rock284=SurveyMaterial(mat,A284+"/Materials/Outcrop.mat");
   // Isolated exposed shoulders. Most of each generated mass remains inside the mountain.
   var rocks=new GameObject("Embedded_outcrops_284");rocks.transform.SetParent(root,false);
   var placements=new[]{new Vector3(24,34,35),new Vector3(44,28,102),new Vector3(42,44,170),new Vector3(23,27,237),new Vector3(34,39,294),new Vector3(68,43,320),new Vector3(86,35,79),new Vector3(105,32,218)};
   int index=0;
   foreach(var placement in placements){
    int variant=index%2;var p=At284(route,placement.z);p.x+=placement.x;p=Ground284(terrain,p);
    var go=new GameObject("Bedrock_"+index);go.transform.SetParent(rocks.transform,false);go.transform.position=p+Vector3.right*placement.y*.12f-Vector3.up*placement.y*.86f;go.transform.rotation=Quaternion.Euler(0,17+index*71,0);go.transform.localScale=Vector3.one*placement.y;
    var levels=new List<LOD>();
    for(int lod=0;lod<3;lod++){var child=MeshObject278("LOD"+lod,NormalizedRock284(variant,lod),Rock284,go.transform,false);levels.Add(new LOD(lod==0?.22f:lod==1?.075f:.005f,new[]{child.GetComponent<Renderer>()}));}
    go.AddComponent<LODGroup>().SetLODs(levels.ToArray());go.GetComponent<LODGroup>().RecalculateBounds();
    var solid=go.AddComponent<MeshCollider>();solid.sharedMesh=NormalizedRock284(variant,2);index++;
   }
   // Remove the old evenly spaced roadside gravel stripe. Accumulation now follows gullies.
   var oldScree=root.Find("Scree_and_retaining_stones");if(oldScree!=null)Object.DestroyImmediate(oldScree.gameObject);
   var talus=new GameObject("Talus_fans_284");talus.transform.SetParent(root,false);
   var rng=new System.Random(284);float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   var meshes=Enumerable.Range(0,8).Select(i=>AssetDatabase.LoadAssetAtPath<Mesh>(A278+"/Meshes/FractureKit_"+i+".asset")).ToArray();
   var instances=new List<CombineInstance>();
   for(int i=0;i<520;i++){
    float az=new[]{38f,131f,243f,310f}[i%4];float z=az+R(-18,18);var p=At284(route,z);p.x+=i%3==0?R(4.3f,11):R(-32,-5);p.z=z;p=Ground284(terrain,p);
    if(Steep284(terrain,p)>48)continue;
    float size=R(.15f,1.05f);p.y-=size*.23f;
    instances.Add(new CombineInstance{mesh=meshes[i%8],transform=Matrix4x4.TRS(p,Quaternion.Euler(R(-22,22),R(0,360),R(-22,22)),new Vector3(size,size*.6f,size*.9f))});
   }
   // Spatial batches keep culling useful. Tiny debris has no individual colliders.
   for(int cell=0;cell<10;cell++){
    var selected=instances.Where(c=>(int)(c.transform.GetColumn(3).z/35)==cell).ToArray();if(selected.Length==0)continue;
    var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(selected,true,true);mesh.RecalculateBounds();mesh=ArtMesh(mesh,A284+"/Meshes/Talus_"+cell+".asset");
    var go=MeshObject278("Talus_cell_"+cell,mesh,Rock284,talus.transform,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
    var group=go.AddComponent<LODGroup>();group.SetLODs(new[]{new LOD(.014f,new[]{go.GetComponent<Renderer>()})});group.RecalculateBounds();
   }
   var oldGroves=root.Find("Ledge_groves_279");var templates=oldGroves.GetComponentsInChildren<LODGroup>();
   var pines=templates.Where(t=>t.name.Contains("Pinus")).GroupBy(t=>t.name).Select(g=>g.First()).ToArray();var shrub=templates.First(t=>t.name.Contains("Ulmus"));
   var groves=new GameObject("Slope_woodland_284");groves.transform.SetParent(root,false);
   int planted=0;
   for(int attempt=0;attempt<5000&&planted<340;attempt++){
    float z=R(-14,395);var p=At284(route,z);p.x+=R(-110,170);p.z=z;
    if(Mathf.Abs(p.x-At284(route,z).x)<8||Vector2.Distance(new Vector2(p.x,z),new Vector2(At284(route,10).x-26,10))<22)continue;
    p=Ground284(terrain,p);float slope=Steep284(terrain,p);if(slope>52||p.y<-72||p.y>210||Mathf.PerlinNoise(p.x*.025f+4,z*.025f)<.39f)continue;
    var copy=Object.Instantiate(pines[planted%12==0?pines.Length-1:0].gameObject,groves.transform);copy.name="Slope_pine_"+planted;copy.transform.position=p-Vector3.up*.18f;copy.transform.rotation=Quaternion.Euler(R(-4,4),R(0,360),R(-4,4));copy.transform.localScale*=R(.65f,1.20f);planted++;
   }
   for(int i=0;i<240;i++){
    float z=R(0,355);var p=At284(route,z);p.x+=R(-38,65);p.z=z;p=Ground284(terrain,p);
    if(Mathf.Abs(p.x-At284(route,z).x)<5||Steep284(terrain,p)>48)continue;
    var copy=Object.Instantiate(shrub.gameObject,groves.transform);copy.name="Sheltered_shrub_"+i;copy.transform.position=p-Vector3.up*.07f;copy.transform.rotation=Quaternion.Euler(0,R(0,360),0);copy.transform.localScale*=R(.9f,2.6f);
   }
   Object.DestroyImmediate(oldGroves.gameObject);
   CoverHybrid284(root,terrain,route);
   File.WriteAllText(O284+"/dressing.txt","Embedded Meshy outcrops="+placements.Length+" (3 LODs, LOD2 collision each); pines="+planted+"; talus instances="+instances.Count+" (spatial batches, no debris collider). Terrain collision owns the road.");
  }
  static void CoverHybrid284(Transform root,UnityEngine.Terrain terrain,Vector3[] route)
  {
   var sheet=AssetDatabase.LoadAssetAtPath<Sheet>("Assets/_Project/Art/World/Rock275/Placements.asset");
   var fern=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_Deparia_1");var grass=sheet.Prototypes.Single(p=>p.Id=="Cheongrim_SM_Grass");
   var rng=new System.Random(1284);float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   var parent=new GameObject("Groundcover_284");parent.transform.SetParent(root,false);
   for(int cell=0;cell<12;cell++){
    var packet=new GameObject("Groundcover_cell_"+cell);packet.transform.SetParent(parent.transform,false);
    var batches=new Dictionary<Material,List<CombineInstance>>();
    for(int i=0;i<90;i++){
     float z=cell*28+R(0,28);var p=At284(route,z);p.x+=i%2==0?R(-13,-4.5f):R(4.5f,18);p.z=z;p=Ground284(terrain,p);
     if(Steep284(terrain,p)>40)continue;
     var species=i%3==0?fern:grass;var matrix=Matrix4x4.TRS(p-Vector3.up*.03f,Quaternion.Euler(0,R(0,360),0),Vector3.one*R(.5f,1.3f));
     foreach(var part in species.Lods[0].Parts){
      if(!batches.TryGetValue(part.Material,out var list)){list=new List<CombineInstance>();batches[part.Material]=list;}
      list.Add(new CombineInstance{mesh=part.Mesh,subMeshIndex=part.Submesh,transform=matrix*part.Local});
     }
    }
    var renderers=new List<Renderer>();int bi=0;
    foreach(var entry in batches){
     var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(entry.Value.ToArray(),true,true);mesh.RecalculateBounds();mesh=ArtMesh(mesh,A284+"/Meshes/Cover_"+cell+"_"+bi+".asset");
     var material=new Material(entry.Key);material.shader=Shader.Find("Oheangbu/Prototype/CliffProp278");material.SetFloat("_WindAmplitude",0);material.SetFloat("_Billboard",0);material.SetFloat("_Saturation",.18f);material.SetFloat("_Leaf279",.4f);material.SetFloat("_AmbientFloor",.25f);material.SetFloat("_FadeOutStart",75);material.SetFloat("_FadeOutEnd",110);material=SurveyMaterial(material,A284+"/Materials/Cover_"+cell+"_"+bi+".mat");
     var go=MeshObject278("Rooted_cover",mesh,material,packet.transform,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;renderers.Add(go.GetComponent<Renderer>());bi++;
    }
    var lod=packet.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.025f,renderers.ToArray())});lod.RecalculateBounds();
   }
  }
  static void LocalizeShared284(Transform root)
  {
   var cache=new Dictionary<Material,Material>();
   foreach(var r in root.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>{
    if(m==null||AssetDatabase.GetAssetPath(m).StartsWith(A284))return m;
    if(cache.TryGetValue(m,out var local))return local;
    local=new Material(m);string path=A284+"/Materials/Shared_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m))+".mat";
    if(local.HasProperty("_Leaf279")&&local.GetFloat("_Leaf279")>.1f){local.SetColor("_BaseColor",local.GetColor("_BaseColor")*new Color(.62f,.70f,.59f,1));local.SetFloat("_AmbientFloor",.23f);local.SetFloat("_LightResponse",.65f);}
    local.enableInstancing=true;local=SurveyMaterial(local,path);cache[m]=local;return local;
   }).ToArray();
  }
  static void TidyHybrid284()
  {
   var terrain=Object.FindFirstObjectByType<UnityEngine.Terrain>();
   var innRenderers=GameObject.Find("Lower_terrace_inn").GetComponentsInChildren<Renderer>();var innBounds=innRenderers[0].bounds;
   foreach(var r in innRenderers)innBounds.Encapsulate(r.bounds);innBounds.Expand(new Vector3(4,0,4));
   foreach(var group in GameObject.Find("Slope_woodland_284").GetComponentsInChildren<LODGroup>()){
    var p=group.transform.position;
    if(p.x>innBounds.min.x&&p.x<innBounds.max.x&&p.z>innBounds.min.z&&p.z<innBounds.max.z)Object.DestroyImmediate(group.gameObject);
   }
   foreach(var t in GameObject.Find("Cliff_Ascent_278").GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Inn_footing_"))){
    var r=t.GetComponent<Renderer>();var p=r.bounds.center;float ground=terrain.SampleHeight(p)+Origin284.y;
    t.position+=Vector3.up*(ground-.16f-r.bounds.min.y);r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(A284+"/Materials/Outcrop.mat");
   }
   foreach(var group in GameObject.Find("Slope_woodland_284").GetComponentsInChildren<LODGroup>()){
    var levels=group.GetLODs();for(int i=0;i<levels.Length;i++)levels[i].screenRelativeTransitionHeight=i==0?.18f:i==1?.045f:.003f;
    group.SetLODs(levels);group.RecalculateBounds();
   }
  }
  static void CheckDressing284(UnityEngine.Terrain terrain,Vector3[] route,Action<bool,string> check)
  {
   var outcrops=GameObject.Find("Embedded_outcrops_284").GetComponentsInChildren<LODGroup>();
   check(outcrops.Length==8&&outcrops.All(g=>g.GetLODs().Length==3),"eight embedded Meshy outcrops, three visual LODs each");
   check(outcrops.All(g=>g.GetComponent<MeshCollider>().sharedMesh.triangles.Length/3<2500),"each outcrop uses simplified static collision under 2500 triangles");
   var plants=GameObject.Find("Slope_woodland_284").GetComponentsInChildren<LODGroup>();
   check(plants.Length>=150,"slope-filtered woodland population="+plants.Length);
   check(plants.All(p=>Mathf.Abs(terrain.SampleHeight(p.transform.position)+Origin284.y-p.transform.position.y)<.25f),"all woodland roots grounded within 25cm");
   check(plants.All(p=>p.GetLODs()[0].screenRelativeTransitionHeight>=.18f),"high-detail woodland restricted to near screen sizes");
   check(GameObject.Find("Talus_fans_284").GetComponentsInChildren<Collider>().Length==0&&GameObject.Find("Groundcover_284").GetComponentsInChildren<Collider>().Length==0,"no individual debris or cover colliders");
   check(terrain.materialTemplate.shader.name=="Oheangbu/Prototype/HybridTerrain284","Terrain-compatible triplanar surface selected");
   int treeTriangles=plants.Sum(g=>g.GetLODs()[0].renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3));
   int triangles=outcrops.Sum(g=>g.GetLODs()[0].renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3));
   File.WriteAllText(O284+"/geometry-budget.txt","Outcrop LOD0 triangle upper bound="+triangles+"; all woodland LOD0 upper bound="+treeTriangles+"; Terrain dynamic tessellation and groundcover not included. Counts are not GPU timing.");
  }
 }
}
