using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string A295="Assets/_Project/Art/World/Watershed295";
  const string O295="../Art/World/Compact/Rebuild/Watershed295";
  const string G295=O295+"/Generated";
  const string Scene295=A295+"/W_Demo_Compact_Watershed295.unity";
  static string Run295(string command)
  {
   Directory.CreateDirectory(O295);
   if(command.StartsWith("qa:"))return Review295(command.Substring(3));
   if(command.StartsWith("mumplay:"))return MumPlay295(command.Substring(8));
   if(command=="prepare")return Prepare295();
   if(command=="open"){RequireClean292();EditorSceneManager.OpenScene(Scene295);return Scene295;}
   if(command.StartsWith("capture:"))return Capture295(command.Substring(8));
   if(SceneManager.GetActiveScene().path!=Scene295||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("295 candidate Edit scene required");
   if(command=="build")return Build295();
   if(command=="resume")return Build295(false);
   if(command=="water"||command=="content"||command=="population")return RefreshPart295(command);
   if(command=="check")return Check295();
   if(command=="nav")return Navigation295();
   if(command=="actors")return ActorNavigation295();
   if(command=="routes")return RefreshRoutes295();
   if(command=="mum-profile")return RefreshMumProfile295();
   if(command=="check293s")return HighlandEvidence295(false);
   if(command=="walk293")return HighlandEvidence295(true);
   if(command=="artcull")return ArtCulling();
   if(command.StartsWith("perf293:")){var a=command.Substring(8).Split(':');return Perf293(int.Parse(a[0]),a.Length>1&&a[1]=="noart");}
   throw new ArgumentException(command);
  }
  static string HighlandEvidence295(bool walk)
  {
   string original=O293+(walk?"/walk-fixture.txt":"/sections-checks.txt");
   byte[] prior=File.Exists(original)?File.ReadAllBytes(original):null;
   try
   {
    string result=walk?WalkSections293():CheckSections293();
    File.WriteAllText(O295+(walk?"/walk-highlands293.txt":"/checks-highlands293.txt"),result);
    return result;
   }
   finally
   {
    if(prior!=null)File.WriteAllBytes(original,prior);
    else if(File.Exists(original))File.Delete(original);
   }
  }
  static string RefreshMumProfile295()
  {
   RequireClean292();var profile=Session292().MumBridgeProfile;
   if(profile==null||AssetDatabase.GetAssetPath(profile)!=A295+"/Data/MumBridgeProfile.asset")
    throw new InvalidOperationException("295 requires its private Mum profile.");
   float previous=profile.MaximumBankEmbedApproach;profile.MaximumBankEmbedApproach=6f;
   if(!profile.IsValid){profile.MaximumBankEmbedApproach=previous;throw new InvalidOperationException("295 Mum profile is invalid.");}
   EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);
   string result="Mum permanent bank underside approach="+profile.MaximumBankEmbedApproach+"m maximum, still capped at one quarter of span per end; walking/head space and the central half remain clear. Prior="+previous+"m. Scene and terrain unchanged.";
   File.WriteAllText(O295+"/mum-profile.txt",result);return result;
  }
  static string RefreshRoutes295()
  {
   RequireClean292();var session=Session292();
   var generated=ScriptableObject.CreateInstance<CompactWorldLayoutSO>();
   try
   {
    JsonUtility.FromJsonOverwrite(File.ReadAllText(G295+"/layout.json"),generated);
    session.MountainLayout.Routes=generated.Routes;EditorUtility.SetDirty(session.MountainLayout);
    var routes=JsonUtility.FromJson<Routes292>(File.ReadAllText(G295+"/routes.json"));
    foreach(var ui in session.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Oheangbu.App.World.UI.PlaytestUiRoot>(true)))
    {
     var map=ui.MapData;if(map==null)continue;
     if(!AssetDatabase.GetAssetPath(map).StartsWith(A295+"/",StringComparison.Ordinal))throw new Exception("295 route refresh requires a private map asset");
     map.Lines=routes.routes.Select(r=>new WorldMapLineSpec{Id=r.id,Kind=WorldMapLineKind.Trail,Points=r.points.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1}).ToArray();
     EditorUtility.SetDirty(map);
    }
    Save292();return "Updated candidate route controls and map lines; actor placements and terrain retained.";
   }
   finally{Object.DestroyImmediate(generated);}
  }
  static T Asset295<T>(string relative,Func<T> create) where T:Object
  {
   string path=A295+"/"+relative;DevSceneKit.EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));
   var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null){asset=create();AssetDatabase.CreateAsset(asset,path);}return asset;
  }
  static string RefreshPart295(string part)
  {
   RequireClean292();var session=Session292();var field=new CompactWorldSurface(session.MountainLayout);
   var original=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(A292+"/Surface/height.bytes"));var report=new List<string>();
   if(part=="water")Water295(field,session.MountainLayout.Hydrology,report);
   if(part=="content")Content295(original,field,report);
   if(part=="population")Population295(original,field,session.MountainLayout.Hydrology,AssetDatabase.LoadAssetAtPath<Material>(A295+"/Materials/KoreanGround.mat"),report);
   Physics.SyncTransforms();Save292();File.WriteAllLines(O295+"/refresh-"+part+".txt",report);return string.Join("\n",report);
  }
  static string Prepare295()
  {
   RequireClean292();
   if(File.Exists(Scene295))return "Existing 295 candidate retained: "+Scene295;
   if(!Directory.Exists(O295+"/Baseline"))throw new Exception("295 baseline must exist before scene preparation");
   DevSceneKit.EnsureFolder(A295+"/Data");
   var scene=EditorSceneManager.OpenScene(Scene292);var roots=scene.GetRootGameObjects();
   var remap=new Dictionary<Object,Object>();
   foreach(string path in AssetDatabase.GetDependencies(Scene292,true))
   {
    if(!path.StartsWith("Assets/_Project/"))continue;
    var source=AssetDatabase.LoadMainAssetAtPath(path);if(!(source is ScriptableObject)||source is MonoScript)continue;
    string dest=A295+"/Data/"+AssetDatabase.AssetPathToGUID(path)+"_"+Path.GetFileName(path);
    if(AssetDatabase.LoadMainAssetAtPath(dest)==null&&!AssetDatabase.CopyAsset(path,dest))throw new Exception("Cannot clone "+path);
    remap[source]=AssetDatabase.LoadMainAssetAtPath(dest);
   }
   void Remap(Object value)
   {
    var serialized=new SerializedObject(value);var p=serialized.GetIterator();bool dirty=false;
    while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&p.objectReferenceValue!=null&&remap.TryGetValue(p.objectReferenceValue,out var next)){p.objectReferenceValue=next;dirty=true;}
    if(dirty){serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(value);}
   }
   foreach(var a in remap.Values)Remap(a);
   foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Component>(true)))if(c!=null)Remap(c);
   var session=Session292();session.Content.SaveSlot="world-compact-watershed-295";session.Content.TerrainRevision="watershed-295";EditorUtility.SetDirty(session.Content);
   File.WriteAllText(O295+"/content-before.json",JsonUtility.ToJson(session.Content,true));
   File.WriteAllText(O295+"/layout-before.json",JsonUtility.ToJson(session.MountainLayout,true));
   var ground=Asset295("Materials/KoreanGround.mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/KoreanGround.mat")));
   foreach(var r in roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
   {
    var materials=r.sharedMaterials;bool changed=false;
    for(int i=0;i<materials.Length;i++)if(AssetDatabase.GetAssetPath(materials[i])==A292+"/Materials/KoreanGround.mat"){materials[i]=ground;changed=true;}
    if(changed)r.sharedMaterials=materials;
   }
   foreach(var m in roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildSceneManifest>(true))){m.SourceScene=Scene292;m.Generation="watershed-295";m.TraversalVerified=false;}
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene,Scene295,true);EditorSceneManager.OpenScene(Scene295);
   File.WriteAllText(O295+"/cloned-data.json","["+string.Join(",",remap.Select(p=>"{\"source\":\""+AssetDatabase.GetAssetPath(p.Key)+"\",\"target\":\""+AssetDatabase.GetAssetPath(p.Value)+"\"}"))+"]");
   return "295 candidate created with "+remap.Count+" private data assets and isolated save slot.";
  }
  static Texture2D Texture295(string file,bool data)
  {
   string path=A295+"/Surface/"+file;AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
   var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=!data;importer.isReadable=true;importer.npotScale=TextureImporterNPOTScale.None;
   importer.maxTextureSize=4096;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
   return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }
  static TextAsset Bytes295(string file)
  {var path=A295+"/Surface/"+file;AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);return AssetDatabase.LoadAssetAtPath<TextAsset>(path);}
  static string Build295(bool requireClean=true)
  {
   if(requireClean)RequireClean292();if(!File.Exists(G295+"/hydro.json"))throw new Exception("Generate and review watershed first");
   var session=Session292();var layout=session.MountainLayout;
   var oldField=new CompactWorldSurface(1001,1501,4,File.ReadAllBytes(A292+"/Surface/height.bytes"));
   DevSceneKit.EnsureFolder(A295+"/Surface");
   foreach(string name in new[]{"height.bytes","surface.png","cartography.png","layout.json","routes.json","waterlevel.bytes","protected.bytes","diagnostics.json"})
    if(File.Exists(G295+"/"+name))File.Copy(G295+"/"+name,A295+"/Surface/"+name,true);
   JsonUtility.FromJsonOverwrite(File.ReadAllText(G295+"/layout.json"),layout);
   layout.FinalSurface=Bytes295("height.bytes");layout.SurfaceWidth=1001;layout.SurfaceHeight=1501;layout.SurfaceCell=4;
   layout.SurfaceDistribution=Texture295("surface.png",true);layout.Revision="watershed-295";
   var hydro=Asset295("Data/Hydrology.asset",()=>ScriptableObject.CreateInstance<CompactHydrologySO>());
   JsonUtility.FromJsonOverwrite(File.ReadAllText(G295+"/hydro.json"),hydro);hydro.WaterLevels=Bytes295("waterlevel.bytes");
   if(File.Exists(A295+"/Surface/protected.bytes"))hydro.ProtectedMask=Bytes295("protected.bytes");
   hydro.GenerationReport=Bytes295("diagnostics.json");layout.Hydrology=hydro;EditorUtility.SetDirty(hydro);EditorUtility.SetDirty(layout);
   var field=new CompactWorldSurface(layout);
   var ground=AssetDatabase.LoadAssetAtPath<Material>(A295+"/Materials/KoreanGround.mat");ground.SetTexture("_StrataField276",layout.SurfaceDistribution);EditorUtility.SetDirty(ground);
   var report=new List<string>();
   Surface295(field,ground,report);
   Water295(field,hydro,report);
   Content295(oldField,field,report);
   Population295(oldField,field,hydro,ground,report);
   Physics.SyncTransforms();Save292();File.WriteAllLines(O295+"/build.txt",report);
   return string.Join("\n",report);
  }
  static void Surface295(CompactWorldSurface field,Material ground,List<string> report)
  {
   var root=GameObject.Find("Reworld292_Terrain");if(root==null)throw new Exception("Source terrain root missing");
   int count=0,shoreTiles=0;var waterLevels=Session292().MountainLayout.Hydrology.WaterLevels.bytes;
   foreach(Transform tile in root.transform)
   {
    var filters=tile.GetComponentsInChildren<MeshFilter>(true);
    foreach(var filter in filters)
    {
     string id=tile.name+"_"+filter.name;
     var mesh=Asset295("Meshes/"+id+".asset",()=>Object.Instantiate(filter.sharedMesh));
     var v=mesh.vertices;var n=new Vector3[v.Length];
     for(int i=0;i<v.Length;i++){float x=tile.position.x+v[i].x,z=tile.position.z+v[i].z;v[i].y=field.Sample(x,z);n[i]=field.Normal(x,z);}
     mesh.vertices=v;mesh.normals=n;mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);filter.sharedMesh=mesh;filter.GetComponent<Renderer>().sharedMaterial=ground;
    }
    var collider=tile.GetComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=filters.First(f=>f.name=="LOD0").sharedMesh;
    // Water edges and canyon lips must not change shape when a coarse terrain LOD takes over.
    bool shore=false;int originX=Mathf.RoundToInt(tile.position.x/4),originZ=Mathf.RoundToInt(tile.position.z/4);
    for(int z=0;z<=125&&!shore;z+=2)for(int x=0;x<=125;x+=2)
    {float y=BitConverter.ToSingle(waterLevels,((originZ+z)*1001+originX+x)*4);if(y>-9000&&y>field.Sample(tile.position.x+x*4,tile.position.z+z*4)+.03f){shore=true;break;}}
    var lod=tile.GetComponent<LODGroup>();var levels=new List<LOD>();
    foreach(var f in filters){int level=int.Parse(f.name.Substring(3));f.gameObject.SetActive(!shore||level==0);if(!shore||level==0)levels.Add(new LOD(shore?.001f:level==0?.08f:level==1?.025f:.001f,new[]{f.GetComponent<Renderer>()}));}
    lod.SetLODs(levels.OrderByDescending(l=>l.screenRelativeTransitionHeight).ToArray());lod.RecalculateBounds();if(shore)shoreTiles++;
    var data=Asset295("TerrainData/"+tile.name+".asset",()=>new TerrainData());data.heightmapResolution=129;data.size=new Vector3(500,600,500);
    var h=new float[129,129];for(int z=0;z<129;z++)for(int x=0;x<129;x++)h[z,x]=field.Sample(tile.position.x+x*500/128f,tile.position.z+z*500/128f)/600;
    data.SetHeights(0,0,h);EditorUtility.SetDirty(data);count++;
   }
   report.Add("Private terrain/collision tiles="+count+"; source292 meshes and TerrainData unchanged. Shore/canyon tiles retain identical 4m geometry across viewing distance="+shoreTiles);
  }
 }
}
