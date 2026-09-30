using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string StoneFloor296="Assets/JejumokGwana/Prefabs/Floors/Floor_stone_2.prefab";
  const string TimberFloor296="Assets/_Project/Art/World/MountainTrail285/Meshes/Pier289_planks_0.asset";
  const string Rampart296="Assets/HwaseongForteressGate/Prefabs/SM_CW.prefab";
  const string Parapet296="Assets/HwaseongForteressGate/Prefabs/SM_CW_Parapet.prefab";
  const string TimberBeam296="Assets/_Project/Art/World/MountainTrail285/Meshes/Pier289_planks_3.asset";
  const string TimberColumn296="Assets/_Project/Art/World/MountainTrail285/Meshes/Pier289_poles_1.asset";
  const string StoneColumn296="Assets/JejumokGwana/Prefabs/Building_Pillars/StonePillar.prefab";
  sealed class CrossingSource296
  {
   public Bounds Bounds;
   public readonly List<(Mesh mesh,int submesh,Material material,Matrix4x4 matrix)> Parts=new List<(Mesh,int,Material,Matrix4x4)>();
  }
  static readonly Dictionary<string,CrossingSource296> crossingSources296=new Dictionary<string,CrossingSource296>();
  static CrossingSource296 CrossingSource(string path)
  {
   if(crossingSources296.TryGetValue(path,out var cached))return cached;
   if(path.EndsWith(".asset",StringComparison.Ordinal)&&path.Contains("Pier289_"))
   {
    var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null)throw new FileNotFoundException("Owned CC0 pier source",path);
    // The existing normalized source derivatives retain original UVs. Restore
    // the measured source dimensions before applying uniform assembly scales.
    Vector3 size=path==TimberFloor296?new Vector3(2.238508f,.05523532f,.25362998f):path==TimberBeam296?new Vector3(2.4551847f,.04667248f,.2217046f):new Vector3(.2538054f,3.3296452f,.25294876f);
    string kind=path.Contains("planks")?"planks":"poles";
    var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/MountainTrail285/Materials/Pier289_"+kind+".mat");
    if(material==null)throw new InvalidOperationException("CC0 pier source texture material missing");
    var result=new CrossingSource296{Bounds=new Bounds(Vector3.Scale(mesh.bounds.center,size),Vector3.Scale(mesh.bounds.size,size))};
    result.Parts.Add((mesh,0,material,Matrix4x4.Scale(size)));crossingSources296[path]=result;return result;
   }
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(prefab==null)throw new FileNotFoundException("296 architectural source",path);
   var instance=Object.Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;
   instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   try
   {
    var result=new CrossingSource296();bool first=true;
    var groups=instance.GetComponentsInChildren<LODGroup>(true);
    var excluded=new HashSet<Renderer>(groups.SelectMany(g=>g.GetLODs().Skip(1)).SelectMany(l=>l.renderers).Where(r=>r!=null));
    var near=new HashSet<Renderer>(groups.SelectMany(g=>g.GetLODs().Take(1)).SelectMany(l=>l.renderers).Where(r=>r!=null));
    foreach(var filter in instance.GetComponentsInChildren<MeshFilter>(true))
    {
     var renderer=filter.GetComponent<Renderer>();var mesh=filter.sharedMesh;
     if(renderer==null||mesh==null||excluded.Contains(renderer)&&!near.Contains(renderer))continue;
     var matrix=filter.transform.localToWorldMatrix;
     for(int c=0;c<8;c++)
     {
      var p=matrix.MultiplyPoint3x4(mesh.bounds.center+Vector3.Scale(mesh.bounds.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1)));
      if(first){result.Bounds=new Bounds(p,Vector3.zero);first=false;}else result.Bounds.Encapsulate(p);
     }
     for(int s=0;s<mesh.subMeshCount;s++)
     {
      if(s>=renderer.sharedMaterials.Length||renderer.sharedMaterials[s]==null)throw new InvalidOperationException("Missing source material: "+path);
      result.Parts.Add((mesh,s,renderer.sharedMaterials[s],matrix));
     }
    }
    if(first||result.Parts.Count==0)throw new InvalidOperationException("Empty architectural source: "+path);
    crossingSources296[path]=result;return result;
   }
   finally{Object.DestroyImmediate(instance);}
  }
  static Material CrossingMaterial296(Material source)
  {
   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
   var material=Asset296("Materials/Crossings/"+guid+"_"+localId+".mat",()=>
   {
    var m=new Material(source){name=source.name+"_Architecture296",enableInstancing=true};
    return m;
   });
   // Keep the source maps and authored UV transforms. The candidate's zero
   // indirect illumination otherwise turns URP source stone into black shadow.
   var shader=Shader.Find("Oheangbu/Reworld292/KoreanArchitecture");
   if(shader==null)throw new InvalidOperationException("Reviewed architecture shader missing");
   material.CopyPropertiesFromMaterial(source);material.shader=shader;
   material.SetFloat("_Saturation",.35f);material.SetFloat("_AmbientFloor",.46f);material.SetFloat("_LightResponse",.7f);
   material.SetFloat("_BumpScale",source.HasProperty("_BumpScale")?source.GetFloat("_BumpScale"):.65f);
   material.SetFloat("_WashStrength",0);material.SetFloat("_FadeInStart",-1);material.SetFloat("_FadeInEnd",0);
   material.SetFloat("_FadeOutStart",8000);material.SetFloat("_FadeOutEnd",10000);
   material.SetFloat("_WindAmplitude",0);material.SetFloat("_Leaf279",0);material.SetFloat("_SimpleLighting",0);
   string sourcePath=AssetDatabase.GetAssetPath(source);
   if(sourcePath=="Assets/JejumokGwana/Materials/M_Stone_03.mat")
   {
    // The original paving is nearly white limestone. Keep its authored stone
    // joints/UVs, with a weathered warm-grey tint rather than white concrete.
    material.SetColor("_BaseColor",new Color(.48f,.455f,.405f,1));
    material.SetFloat("_LightResponse",.52f);material.SetFloat("_Saturation",.24f);
   }
   if(sourcePath=="Assets/JejumokGwana/Materials/M_Concrete_Light.mat")
   {
    // Existing CC0 seamless rock replaces the pale concrete appearance only.
    // Source mesh UVs and their material transform remain unchanged.
    const string rock="Assets/_Project/Art/World/MountainTrail285/Textures/rock_face_03_";
    var diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(rock+"diff_4k.jpg");
    var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(rock+"nor_gl_4k.jpg");
    if(diffuse==null||normal==null)throw new InvalidOperationException("Owned CC0 rock_face_03 appearance maps missing");
    material.SetTexture("_BaseMap",diffuse);material.SetTexture("_BumpMap",normal);
    material.SetColor("_BaseColor",new Color(.66f,.67f,.65f,1));
    material.SetFloat("_LightResponse",.52f);material.SetFloat("_Saturation",.20f);material.SetFloat("_BumpScale",.8f);
   }
   material.name=source.name+"_Architecture296";material.enableInstancing=true;EditorUtility.SetDirty(material);return material;
  }
  static Material CrossingApronMasonryMaterial296(Material source)
  {
   var path=AssetDatabase.GetAssetPath(source);
   if(!path.StartsWith("Assets/HwaseongForteressGate/Materials/",StringComparison.Ordinal))return CrossingMaterial296(source);
   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
   var material=Asset296("Materials/Crossings/ApronMasonry_"+guid+"_"+localId+".mat",()=>new Material(CrossingMaterial296(source)));
   material.CopyPropertiesFromMaterial(CrossingMaterial296(source));
   material.SetColor("_BaseColor",new Color(.52f,.49f,.44f,1));material.SetFloat("_LightResponse",.52f);material.SetFloat("_Saturation",.24f);
   material.name=source.name+"_Architecture296_ApronMasonry";EditorUtility.SetDirty(material);return material;
  }
  // Material-only repair for existing candidate renderers/Mum profile. The
  // shared private asset references remain stable; no mesh/collider is rebuilt.
  public static string UpdateCrossingMaterials296()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Material update requires Edit mode");
   var originals=new[]{StoneFloor296,TimberFloor296,Rampart296,Parapet296,TimberBeam296,TimberColumn296,StoneColumn296}
    .SelectMany(path=>CrossingSource(path).Parts.Select(p=>p.material)).Distinct().ToArray();
   var rows=new List<string>{"Candidate-only crossing materials "+DateTime.UtcNow.ToString("O"),"Geometry/colliders/progression unchanged. Source mesh UVs and material UV scale/offset preserved. Pale KCISA paving retains its source maps with a warm grey tint. Pale concrete pier maps are replaced by owned Poly Haven CC0 rock_face_03 diffuse/normal maps; no source assets are modified.","CC0 stone source: https://polyhaven.com/a/rock_face_03 ; license https://polyhaven.com/license ; local ledger Assets/_Project/Art/World/MountainTrail285/SOURCE_LICENSES.md"};
   foreach(var source in originals)
   {
    var material=CrossingMaterial296(source);AssetDatabase.SaveAssetIfDirty(material);
    bool rock=AssetDatabase.GetAssetPath(source)=="Assets/JejumokGwana/Materials/M_Concrete_Light.mat";
    if(source.HasProperty("_BaseMap")&&(source.GetTextureScale("_BaseMap")!=material.GetTextureScale("_BaseMap")||source.GetTextureOffset("_BaseMap")!=material.GetTextureOffset("_BaseMap")))throw new Exception("Source UV transform changed");
    if(!rock&&source.HasProperty("_BaseMap")&&source.GetTexture("_BaseMap")!=material.GetTexture("_BaseMap"))throw new Exception("Source base map changed");
    if(!rock&&source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap")!=material.GetTexture("_BumpMap"))throw new Exception("Source normal map changed");
    if(material.GetTexture("_BaseMap")==null||material.GetTexture("_BumpMap")==null)throw new Exception("Crossing appearance map missing");
    rows.Add(AssetDatabase.GetAssetPath(material)+" <- "+AssetDatabase.GetAssetPath(source)+" shader="+material.shader.name+" base="+AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))+" normal="+AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap"))+" tint="+material.GetColor("_BaseColor")+" ambient="+material.GetFloat("_AmbientFloor")+" response="+material.GetFloat("_LightResponse")+" saturation="+material.GetFloat("_Saturation"));
    if(AssetDatabase.GetAssetPath(source).StartsWith("Assets/HwaseongForteressGate/Materials/",StringComparison.Ordinal))
    {var apron=CrossingApronMasonryMaterial296(source);AssetDatabase.SaveAssetIfDirty(apron);rows.Add("Apron-only masonry "+AssetDatabase.GetAssetPath(apron)+" tint="+apron.GetColor("_BaseColor")+" source UV/maps retained; capital walls unchanged.");}
   }
   Directory.CreateDirectory(O296);File.WriteAllLines(O296+"/crossing-materials-detail.txt",rows);return originals.Length+" private crossing materials updated; meshes/colliders unchanged. "+O296+"/crossing-materials-detail.txt";
  }
  // Load-or-create a mesh asset at an explicit Assets/ path (CrossingBatch296.Finish meshFolder); same contract as Asset296.
  static Mesh FolderMesh296(string path)
  {
   if(!path.StartsWith("Assets/",StringComparison.Ordinal))throw new ArgumentException("Mesh folder must be under Assets/: "+path);
   string directory=Path.GetDirectoryName(path).Replace('\\','/');if(!Directory.Exists(directory))DevSceneKit.EnsureFolder(directory);
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh==null){mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);}return mesh;
  }
  static void CrossingLods296(Transform holder,string id,Renderer[] near)
  {
   var levels=new List<LOD>{new LOD(.30f,near)};
   for(int level=1;level<=2;level++)
   {
    var renderers=new List<Renderer>();int part=0;
    foreach(var source in near)
    {
     var full=source.GetComponent<MeshFilter>().sharedMesh;
     Mesh mesh;
     if(combinedCrossingLods296.TryGetValue(full,out var assembled))mesh=assembled[level];
     else
     {
      var reduced=ClusterVenueMesh296(full,level==1?.18f:.65f);
      mesh=Asset296("Meshes/Crossings/"+id+"_LOD"+level+"_"+part+".asset",()=>new Mesh());EditorUtility.CopySerialized(reduced,mesh);Object.DestroyImmediate(reduced);EditorUtility.SetDirty(mesh);
     }
     if(mesh==null||mesh.vertexCount==0)continue;
     var go=new GameObject("LOD"+level+"_"+part++);go.transform.SetParent(holder,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
     var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=source.sharedMaterials;go.isStatic=true;renderers.Add(renderer);
    }
    levels.Add(new LOD(level==1?.085f:.001f,renderers.ToArray()));
   }
   var group=holder.GetComponent<LODGroup>();if(group==null)group=holder.gameObject.AddComponent<LODGroup>();group.SetLODs(levels.ToArray());group.RecalculateBounds();
  }
  sealed partial class CrossingBatch296
  {
   readonly Dictionary<Material,List<CombineInstance>>[] parts=Enumerable.Range(0,3).Select(_=>new Dictionary<Material,List<CombineInstance>>()).ToArray();
   readonly List<Mesh> temporary=new List<Mesh>();
   public int Instances;
   public void Add(string source,Matrix4x4 matrix)
   {
    var data=CrossingSource(source);Instances++;
    for(int index=0;index<data.Parts.Count;index++)for(int level=0;level<3;level++)
    {
     var part=CrossingPart296(source,index,level);var material=data.Parts[index].material;
     if(!parts[level].TryGetValue(material,out var list)){list=new List<CombineInstance>();parts[level][material]=list;}
     list.Add(new CombineInstance{mesh=part.mesh,subMeshIndex=part.submesh,transform=matrix*part.matrix});
    }
   }
   public void Uniform(string source,Vector3 bottom,Quaternion rotation,float scale)
   {
    var b=CrossingSource(source).Bounds;
    Add(source,Matrix4x4.TRS(bottom,rotation,Vector3.one*scale)*Matrix4x4.Translate(-new Vector3(b.center.x,b.min.y,b.center.z)));
   }
   public void WarpSurface(string source,Matrix4x4 matrix,Func<Vector3,Vector3> warp)
   {
    Instances++;var data=CrossingSource(source);
    for(int index=0;index<data.Parts.Count;index++)for(int level=0;level<3;level++)
    {
     var part=CrossingPart296(source,index,level);var material=data.Parts[index].material;
     var mesh=Object.Instantiate(part.mesh);mesh.subMeshCount=1;mesh.SetTriangles(part.mesh.GetTriangles(part.submesh),0);
     var transform=matrix*part.matrix;mesh.vertices=part.mesh.vertices.Select(p=>warp(transform.MultiplyPoint3x4(p))).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();temporary.Add(mesh);
     if(!parts[level].TryGetValue(material,out var list)){list=new List<CombineInstance>();parts[level][material]=list;}
     list.Add(new CombineInstance{mesh=mesh,subMeshIndex=0,transform=Matrix4x4.identity});
    }
   }
   public void Wall(string source,Vector3 bottom,Quaternion rotation,float scale,float width,float maximumY=float.PositiveInfinity,Func<Vector3,Vector3> worldWarp=null,CrossingBatch296 cutCaps=null)
   {
    var data=CrossingSource(source);Instances++;
    var normalize=Matrix4x4.Scale(Vector3.one*scale)*Matrix4x4.Translate(-new Vector3(data.Bounds.center.x,data.Bounds.min.y,data.Bounds.center.z));
    for(int index=0;index<data.Parts.Count;index++)for(int level=0;level<3;level++)
    {
     var part=CrossingPart296(source,index,level);var material=data.Parts[index].material;
     var mesh=CropWall296(part.mesh,part.submesh,normalize*part.matrix,width,maximumY);temporary.Add(mesh);
     if(mesh.vertexCount==0||mesh.GetIndexCount(0)==0)continue;
     if(cutCaps!=null)
     {
      var caps=CrossingCutCaps296(mesh,width,maximumY);cutCaps.temporary.Add(caps);
      if(caps.vertexCount>0)
      {
       var capPlacement=Matrix4x4.TRS(bottom,rotation,Vector3.one);
       if(worldWarp!=null){caps.vertices=caps.vertices.Select(p=>worldWarp(capPlacement.MultiplyPoint3x4(p))).ToArray();caps.RecalculateNormals();caps.RecalculateTangents();caps.RecalculateBounds();capPlacement=Matrix4x4.identity;}
       if(!cutCaps.parts[level].TryGetValue(material,out var capList)){capList=new List<CombineInstance>();cutCaps.parts[level][material]=capList;}
       capList.Add(new CombineInstance{mesh=caps,subMeshIndex=0,transform=capPlacement});if(level==0)cutCaps.Instances++;
      }
     }
     if(!parts[level].TryGetValue(material,out var list)){list=new List<CombineInstance>();parts[level][material]=list;}
     var placement=Matrix4x4.TRS(bottom,rotation,Vector3.one);
     if(worldWarp!=null){mesh.vertices=mesh.vertices.Select(p=>worldWarp(placement.MultiplyPoint3x4(p))).ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();placement=Matrix4x4.identity;}
     list.Add(new CombineInstance{mesh=mesh,subMeshIndex=0,transform=placement});
    }
   }
   public void MasonryFacing(string source,Vector3 bottom,Quaternion rotation,float scale,float width,float maximumY,Func<Vector3,Vector3> worldWarp)
   {
    var data=CrossingSource(source);Instances++;
    var normalize=Matrix4x4.Scale(Vector3.one*scale)*Matrix4x4.Translate(-new Vector3(data.Bounds.center.x,data.Bounds.min.y,data.Bounds.center.z));
    for(int index=0;index<data.Parts.Count;index++)for(int level=0;level<3;level++)
    {
     var part=CrossingPart296(source,index,level);var material=data.Parts[index].material;
     var clipped=CropWall296(part.mesh,part.submesh,normalize*part.matrix,width,maximumY);temporary.Add(clipped);
     var mesh=CrossingMasonryFacing296(clipped);temporary.Add(mesh);if(mesh.vertexCount==0)continue;
     var placement=Matrix4x4.TRS(bottom,rotation,Vector3.one);mesh.vertices=mesh.vertices.Select(p=>worldWarp(placement.MultiplyPoint3x4(p))).ToArray();mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
     if(!parts[level].TryGetValue(material,out var list)){list=new List<CombineInstance>();parts[level][material]=list;}
     list.Add(new CombineInstance{mesh=mesh,subMeshIndex=0,transform=Matrix4x4.identity});
    }
   }
   // meshFolder: null = A296/Meshes/Crossings (unchanged #296 behaviour); otherwise an explicit Assets/ folder for the meshes.
   // The combined LOD levels are still registered below, so CrossingLods296 writes no A296 LOD assets for them either.
   public Renderer[] Finish(Transform parent,string id,bool collision=false,string meshFolder=null)
   {
    var renderers=new List<Renderer>();int part=0;
    foreach(var entry in parts[0])
    {
     string name=id+"_"+part++;var levels=new Mesh[3];
     for(int level=0;level<3;level++)
     {
      string file=name+(level==0?"":"_sourceLOD"+level)+".asset";
      var levelMesh=meshFolder==null?Asset296("Meshes/Crossings/"+file,()=>new Mesh()):FolderMesh296(meshFolder+"/"+file);
      levelMesh.Clear();levelMesh.indexFormat=IndexFormat.UInt32;
      if(parts[level].TryGetValue(entry.Key,out var list))levelMesh.CombineMeshes(list.ToArray(),true,true);
      levelMesh.RecalculateBounds();EditorUtility.SetDirty(levelMesh);levels[level]=levelMesh;
     }
     var mesh=levels[0];combinedCrossingLods296[mesh]=levels;
     var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.isStatic=true;
     go.GetComponent<MeshFilter>().sharedMesh=mesh;var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=id.Contains("_loadbearing")?CrossingBridgeStructureMaterial296(entry.Key):id.Contains("_apron")?CrossingApronMasonryMaterial296(entry.Key):CrossingMaterial296(entry.Key);renderers.Add(r);
     if(collision)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    foreach(var mesh in temporary)Object.DestroyImmediate(mesh);temporary.Clear();return renderers.ToArray();
   }
  }
  struct WallVertex296
  {
   public Vector3 P,N;public Vector2 UV;
   public static WallVertex296 Lerp(WallVertex296 a,WallVertex296 b,float t)=>new WallVertex296{P=Vector3.Lerp(a.P,b.P,t),N=Vector3.Lerp(a.N,b.N,t).normalized,UV=Vector2.Lerp(a.UV,b.UV,t)};
  }
  static Mesh CropWall296(Mesh source,int sub,Matrix4x4 matrix,float width,float maximumY=float.PositiveInfinity)
  {
   var vertices=source.vertices;var normals=source.normals;var uvs=source.uv;var input=source.GetTriangles(sub);
   var output=new List<Vector3>();var uv=new List<Vector2>();var normal=new List<Vector3>();var triangles=new List<int>();
   List<WallVertex296> Clip(List<WallVertex296> polygon,float edge,bool greater,bool vertical=false)
   {
    float Axis(WallVertex296 v)=>vertical?v.P.y:v.P.x;
    var result=new List<WallVertex296>();if(polygon.Count==0)return result;var a=polygon[polygon.Count-1];bool ain=greater?Axis(a)>=edge:Axis(a)<=edge;
    foreach(var b in polygon){bool bin=greater?Axis(b)>=edge:Axis(b)<=edge;if(ain!=bin)result.Add(WallVertex296.Lerp(a,b,(edge-Axis(a))/(Axis(b)-Axis(a))));if(bin)result.Add(b);a=b;ain=bin;}return result;
   }
   for(int i=0;i<input.Length;i+=3)
   {
    var polygon=new List<WallVertex296>();for(int j=0;j<3;j++){int k=input[i+j];polygon.Add(new WallVertex296{P=matrix.MultiplyPoint3x4(vertices[k]),N=normals.Length==vertices.Length?matrix.MultiplyVector(normals[k]).normalized:Vector3.up,UV=uvs.Length==vertices.Length?uvs[k]:Vector2.zero});}
    polygon=Clip(Clip(polygon,-width*.5f,true),width*.5f,false);if(!float.IsPositiveInfinity(maximumY))polygon=Clip(polygon,maximumY,false,true);if(polygon.Count<3)continue;
    int first=output.Count;foreach(var v in polygon){output.Add(v.P);normal.Add(v.N);uv.Add(v.UV);}for(int j=1;j<polygon.Count-1;j++)triangles.AddRange(new[]{first,first+j,first+j+1});
   }
   var mesh=new Mesh{indexFormat=IndexFormat.UInt32};mesh.SetVertices(output);mesh.SetNormals(normal);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
  }
  static void RegisterCrossingSources296()
  {
   var sheet=Sheet296();var rows=new[]{
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-stone-floor",AssetPath=StoneFloor296,Publisher="KCISA / Jejumok Gwana",License="Previously owned Unity Asset Store distribution; original EULA retained, not CC0",Use="Repeated source stone paving; private merged meshes and materials"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-timber",AssetPath=TimberFloor296,Publisher="Rico Cilliers / Poly Haven",Url="https://polyhaven.com/a/modular_wooden_pier",License="CC0-1.0; previously imported original source, UV, diffuse/normal/ARM retained",Use="Weathered timber bridge boards, restored source dimensions then uniform scale; candidate QEM LODs"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-rampart",AssetPath=Rampart296,Publisher="KCISA / Hwaseong Fortress Gate",License="Previously owned Unity Asset Store distribution; original EULA retained, not CC0",Use="Source lower masonry courses at fixed uniform .65 scale for walls, solid bridge piers and arched bridge faces; original UV and metric stone size preserved"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-parapet",AssetPath=Parapet296,Publisher="KCISA / Hwaseong Fortress Gate",License="Previously owned Unity Asset Store distribution; original EULA retained, not CC0",Use="Stone balustrades"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-column",AssetPath=StoneColumn296,Publisher="KCISA / Jejumok Gwana",License="Previously owned Unity Asset Store distribution; original EULA retained, not CC0",Use="Retained original pier collision reference; visible masonry bridge piers now use fortress stone courses"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-pier-stone-surface",AssetPath="Assets/_Project/Art/World/MountainTrail285/Textures/rock_face_03_diff_4k.jpg",Publisher="Poly Haven",Url="https://polyhaven.com/a/rock_face_03",License="CC0-1.0; existing owned diffuse/normal maps; SOURCE_LICENSES.md retained",Use="Private weathered pier appearance replacing pale concrete maps; original KCISA mesh UVs, dimensions and physics retained"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-gate",AssetPath="Assets/HwaseongForteressGate/Prefabs/SM_G_Arch.prefab",Publisher="KCISA / Hwaseong Fortress Gate",License="Previously owned Unity Asset Store distribution; original EULA retained, not CC0",Use="Existing canonical gate asset assembly cloned with unchanged source proportions and original saved opening state"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-timber-column",AssetPath=TimberColumn296,Publisher="Rico Cilliers / Poly Haven",Url="https://polyhaven.com/a/modular_wooden_pier",License="CC0-1.0; source UV/normal/material retained",Use="Weathered timber trestle posts, transverse bents, longitudinal stringers and diagonal bracing; native section with repeated cut lengths"},
    new Oheangbu.Data.World.CompactArchitectureSheetSO.Source{Id="crossing296-timber-beam",AssetPath=TimberBeam296,Publisher="Rico Cilliers / Poly Haven",Url="https://polyhaven.com/a/modular_wooden_pier",License="CC0-1.0; source UV/normal/material retained",Use="Timber bridge handrails"}
   };
   sheet.Sources=sheet.Sources.Where(s=>!s.Id.StartsWith("crossing296-",StringComparison.Ordinal)).Concat(rows).ToArray();EditorUtility.SetDirty(sheet);
  }
 }
}
