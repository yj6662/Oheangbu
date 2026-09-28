using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
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
  [Serializable] sealed class Routes292{public Route292[] routes;}
  [Serializable] sealed class Route292{public string id;public float width;public bool vehicle;public Vector3[] points;}
  static Texture2D Texture292(string file,bool data=false)
  {
   string path=A292+"/Surface/"+file;AssetDatabase.ImportAsset(path);
   var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=!data;importer.isReadable=true;importer.npotScale=TextureImporterNPOTScale.None;
   importer.maxTextureSize=4096;importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
   return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
  }
  static Material GroundMaterial292(CompactWorldLayoutSO layout)
  {
   var m=Asset292("Materials/KoreanGround.mat",()=>new Material(AssetDatabase.LoadAssetAtPath<Material>(InkGroundSource291)));
   m.shader=Shader.Find("Oheangbu/Reworld292/KoreanInkGround");if(m.shader==null)throw new Exception("292 ground shader missing");
   foreach(string p in new[]{"_PaintedGeoEnabled","_PaintedFormEnabled","_PaintedFormAuthored","_PaintedRoadBank","_PaintedFarPath","_PaintedFarPathAir","_PaintedPathMountain","_PaintedValleyReserve","_GrassCoverStrength267","_RealmTintStrength","_PaintedWashEnabled","_WashStrength","_GroundPath"})SetInkFloat291(m,p,0);
   SetInkFloat291(m,"_StrataStrength276",1);SetInkFloat291(m,"_RebuildScreenFog",1);SetInkFloat291(m,"_InkChromaRetain268",1);
   m.SetVector("_StrataRange276",new Vector4(240,1300,0,0));m.SetTexture("_StrataField276",layout.SurfaceDistribution);
   m.SetColor("_CIInk",new Color(.085f,.083f,.073f));m.SetColor("_CIPaper",new Color(.90f,.875f,.80f));m.SetFloat("_Saturation",.2f);
   m.SetTexture("_DirtMap",AssetDatabase.LoadAssetAtPath<Texture2D>(A285+"/Textures/roots_diff_4k.jpg"));m.SetTexture("_DirtNormal",AssetDatabase.LoadAssetAtPath<Texture2D>(A285+"/Textures/roots_nor_gl_4k.jpg"));
   if(layout.Realms.Length==5&&m.HasProperty("_Realm293")){m.SetTexture("_Realm293",Texture292("realm293.png",true));m.SetFloat("_RealmStrength293",.30f);}
   // #293 near-field realm floor (CC0 forest ground, Highlands293/sources.json); weight map from refine_highlands293.py
   if(m.HasProperty("_Floor293")&&File.Exists(A292+"/Surface/floor293.png"))
   {m.SetTexture("_Floor293",Texture292("floor293.png",true));m.SetTexture("_FloorMap293",AssetDatabase.LoadAssetAtPath<Texture2D>(A292+"/Highlands293/Textures/forrest_ground_01_diff_2k.jpg"));
    m.SetFloat("_FloorStrength293",.85f);m.SetFloat("_FloorScale293",1/3.2f);m.SetFloat("_FloorChroma293",.52f);m.SetVector("_FloorRange293",new Vector4(45,260,0,0));m.SetColor("_FloorTint293",new Color(.84f,.95f,.88f));m.SetFloat("_FloorWash293",.6f);m.SetVector("_FloorFar293",new Vector4(1100,2600,0,0));
    RealmFloors293(m);}
   m.SetVector("_CIAirStrengths",Vector4.zero);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
  }
  // #293 Step 2 realm near floors (CC0 Poly Haven, Highlands293/sources.json): floor map g/b/a + remainder. The tint also
  // carries each realm's mid-field value and hue (ART-SKY seeds: 적로 탄 주홍, 철옹 쇠빛 회청, 현강 물안개 청묵, 황경 황토) — TEST.
  static void RealmFloors293(Material m)
  {
   if(!m.HasProperty("_FloorMapJ293"))return;
   Texture2D F(string slug)=>AssetDatabase.LoadAssetAtPath<Texture2D>(A292+"/Highlands293/Textures/"+slug+"_diff_2k.jpg");
   m.SetTexture("_FloorMapJ293",F("burned_ground_01"));m.SetVector("_FloorTintJ293",new Vector4(.84f,.74f,.68f,.55f));
   m.SetTexture("_FloorMapC293",F("rocks_ground_06"));m.SetVector("_FloorTintC293",new Vector4(.86f,.90f,.95f,.40f));
   m.SetTexture("_FloorMapH293",F("brown_mud_rocks_01"));m.SetVector("_FloorTintH293",new Vector4(.80f,.90f,.88f,.50f));
   m.SetTexture("_FloorMapW293",F("dry_ground_rocks"));m.SetVector("_FloorTintW293",new Vector4(1f,.93f,.80f,.45f));
   m.SetFloat("_FloorRealms293",1);
   // #293 tone review: charred masses on Jeokro only; default shader strength zero preserves older materials.
   if(m.HasProperty("_Scorch293"))m.SetVector("_Scorch293",new Vector4(.68f,.5f,.9f,.45f));
   // stronger realm pigment on the painted ground so the five realms separate at distance (was .30 in the 292 pass)
   if(m.HasProperty("_RealmStrength293"))m.SetFloat("_RealmStrength293",.55f);
   EditorUtility.SetDirty(m);
  }
  static string Terrain292()
  {
   RequireClean292();if(File.Exists(O292+"/terrain-installed.json"))throw new Exception("Terrain already installed; preserve/revise from the immutable source, never accumulate relocation");
   var scene=SceneManager.GetActiveScene();var session=Session292();var layout=session.MountainLayout;
   var old=JsonUtility.FromJson<CompactLayoutSnapshot292>(JsonUtility.ToJson(layout));
   var sourceGround=FinalSurface(scene);var originalHeights=new Dictionary<string,float>();
   foreach(var p in old.Places){try{originalHeights[p.Id]=sourceGround(p.XZ.x,p.XZ.y).point.y;}catch{originalHeights[p.Id]=p.SourceAnchor.y;}}
   var roots=scene.GetRootGameObjects();
   JsonUtility.FromJsonOverwrite(File.ReadAllText(A292+"/Surface/layout.json"),layout);
   AssetDatabase.ImportAsset(A292+"/Surface/height.bytes");layout.FinalSurface=AssetDatabase.LoadAssetAtPath<TextAsset>(A292+"/Surface/height.bytes");
   layout.SurfaceWidth=1001;layout.SurfaceHeight=1501;layout.SurfaceCell=4;layout.SurfaceDistribution=Texture292("surface.png",true);
   var surface=new CompactWorldSurface(layout);var material=GroundMaterial292(layout);
   var deltas=new Dictionary<string,Vector3>();
   foreach(var p in old.Places){var now=layout.Places.First(q=>q.Id==p.Id);deltas[p.Id]=surface.Point(now.XZ)-new Vector3(p.XZ.x,originalHeights[p.Id],p.XZ.y);}
   Vector3 Map(Vector3 p)
   {
    var village=old.Places.FirstOrDefault(q=>q.Id=="village");
    if(village!=null&&Vector2.Distance(village.XZ,new Vector2(p.x,p.z))<180)return p+deltas["village"];
    var nearest=old.Places.Where(q=>!q.Id.StartsWith("mountain_")).OrderBy(q=>(q.XZ-new Vector2(p.x,p.z)).sqrMagnitude).First();
    float distance=Vector2.Distance(nearest.XZ,new Vector2(p.x,p.z));float rigid=nearest.Id=="mine"?210:nearest.Id=="village"||nearest.Id.StartsWith("village_")||nearest.Id=="merchant"?100:38;
    if(distance<rigid)return p+deltas[nearest.Id];
    float oldY;try{oldY=sourceGround(p.x,p.z).point.y;}catch{oldY=p.y;}
    return new Vector3(p.x,surface.Sample(p.x,p.z)+Mathf.Clamp(p.y-oldY,-80,80),p.z);
   }
   // Resolve overlapping binding ownership before moving any root or data entry.
   var ownership=new Dictionary<string,string>();var conflicts=new List<string>();
   foreach(var p in old.Places.OrderBy(p=>p.SceneRoots.Length).ThenBy(p=>p.Id))foreach(string id in p.InteractionIds)
    if(!ownership.TryAdd(id,p.Id))conflicts.Add(id+": "+ownership[id]+" owns; container alias "+p.Id);
   File.WriteAllText(O292+"/binding-ownership.txt",string.Join("\n",ownership.Select(p=>p.Key+" => "+p.Value).Concat(conflicts)));
   var content=session.Content;
   foreach(var p in content.Points)p.Position=Map(p.Position);
   foreach(var e in content.Encounters){e.Feet=Map(e.Feet);if(e.Patrol!=null)e.Patrol=e.Patrol.Select(Map).ToArray();}
   foreach(var c in content.Checkpoints)c.Feet=Map(c.Feet);
   content.StartFeet=Map(content.StartFeet);content.InnCheckpointFeet=Map(content.InnCheckpointFeet);
   content.MainPath=content.MainPath.Select(Map).ToArray();content.BranchPath=content.BranchPath.Select(Map).ToArray();
   var moved=new HashSet<Transform>();var rows=new List<string>();
   bool Excluded(Transform t)=>t.GetComponent<Camera>()!=null||t.GetComponent<Light>()!=null||t.GetComponent<Canvas>()!=null||t.GetComponent<WorldMacroPlaytestSession>()!=null||t.GetComponent<CompactRebuildArtRenderer>()!=null||t.name.Contains("Terrain")||t.name.Contains("Navigation")||t.name=="Compact_Mountains_290"||t.name=="Cheongrim_AssetPass_290"||t.name=="Compact_MountainContent_290";
   void Move(Transform t)
   {
    if(!t.gameObject.activeSelf||Excluded(t))return;
    // A place container owns its children, including lights and invisible triggers.
    // Moving individual child meshes would shear buildings and shift their interactions twice.
    if(deltas.TryGetValue(t.name,out var placeDelta)&&!t.name.StartsWith("mountain_"))
    {t.position+=placeDelta;moved.Add(t);rows.Add(HierarchyPath(t)+" | owned container "+placeDelta);return;}
    if(t.name=="Village245"||t.name=="Village249Frontage")
    {var d=deltas["village"];t.position+=d;moved.Add(t);rows.Add(HierarchyPath(t)+" | village "+d);return;}
    if(t.GetComponentInChildren<Canvas>(true)!=null||t.GetComponentInChildren<WorldMacroPlaytestSession>(true)!=null){foreach(Transform c in t)Move(c);return;}
    var rr=t.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)&&!(r is TrailRenderer)&&!(r is LineRenderer)).ToArray();
    if(rr.Length==0)return;var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);
    bool cave=t.name=="Playtest_NaturalCave"||t.name=="mine";
    if((b.size.x<95&&b.size.z<95)||cave)
    {
     var pivot=new Vector3(b.center.x,b.min.y,b.center.z);var d=Map(pivot)-pivot;t.position+=d;moved.Add(t);
     rows.Add(HierarchyPath(t)+" | "+d.ToString("F2"));return;
    }
    foreach(Transform c in t)Move(c);
   }
   foreach(var root in roots)Move(root.transform);
   foreach(var marker in roots.SelectMany(r=>r.GetComponentsInChildren<WorldMacroContentPoint>(true)))
   {
    var p=content.Points.FirstOrDefault(p=>p.Id==marker.Id);if(p==null)continue;
    if(!moved.Any(t=>marker.transform==t||marker.transform.IsChildOf(t)))marker.transform.position=p.Position;
   }
   foreach(var arrival in roots.SelectMany(r=>r.GetComponentsInChildren<WorldLocationArrival>(true)))
   {
    foreach(var e in arrival.Catalog.Entries.Where(e=>e.Priority>0))
    {var before=e.Centre;e.Centre=Map(e.Centre);float dy=e.Centre.y-before.y;if(e.MinimumY>-900)e.MinimumY+=dy;if(e.MaximumY<1900)e.MaximumY+=dy;if(e.FloorPath!=null)e.FloorPath=e.FloorPath.Select(Map).ToArray();if(e.Polygon!=null)e.Polygon=e.Polygon.Select(q=>q+new Vector2(e.Centre.x-before.x,e.Centre.z-before.z)).ToArray();}
    EditorUtility.SetDirty(arrival.Catalog);
   }
   foreach(var ui in roots.SelectMany(r=>r.GetComponentsInChildren<PlaytestUiRoot>(true)))
   {
    var map=ui.MapData;foreach(var marker in map.Markers){var p=Map(new Vector3(marker.WorldXZ.x,0,marker.WorldXZ.y));marker.WorldXZ=new Vector2(p.x,p.z);}
    map.IllustratedMap=Texture292("cartography.png");map.ExploredMap=map.IllustratedMap;map.RegionTiles=Array.Empty<WorldMapRegionTile>();map.Revision="reworld-292";
    var routeData=JsonUtility.FromJson<Routes292>(File.ReadAllText(A292+"/Surface/routes.json"));
    map.Lines=routeData.routes.Select(r=>new WorldMapLineSpec{Id=r.id,Kind=WorldMapLineKind.Trail,Points=r.points.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1}).ToArray();EditorUtility.SetDirty(map);
   }
   File.WriteAllText(O292+"/relocated-roots.txt",string.Join("\n",rows));
   // Old environment geometry is retained inactive inside this isolated candidate.
   foreach(var root in roots.Where(g=>g.name=="Compact_Rebuild_Terrain"||g.name=="Compact_Mountains_290"||g.name=="Cheongrim_AssetPass_290"||g.name=="Compact_MountainContent_290"))root.SetActive(false);
   foreach(var r in roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Where(r=>r.bounds.size.x>300||r.bounds.size.z>300))
    if(r.name.Contains("Terrain")||r.name.Contains("Backdrop")||r.name.Contains("Road"))r.gameObject.SetActive(false);
   var terrainRoot=new GameObject("Reworld292_Terrain");
   for(int tz=0;tz<12;tz++)for(int tx=0;tx<8;tx++)
   {
    float ox=tx*500,oz=tz*500;string id=$"Terrain_{tx:D2}_{tz:D2}";var tile=new GameObject(id);tile.transform.SetParent(terrainRoot.transform,false);tile.transform.position=new Vector3(ox,0,oz);
    var td=Asset292("TerrainData/"+id+".asset",()=>new TerrainData());td.heightmapResolution=129;td.size=new Vector3(500,600,500);var heights=new float[129,129];
    for(int z=0;z<=128;z++)for(int x=0;x<=128;x++)heights[z,x]=surface.Sample(ox+x*500/128f,oz+z*500/128f)/600;td.SetHeights(0,0,heights);EditorUtility.SetDirty(td);
    var lods=new List<LOD>();
    for(int level=0;level<3;level++)
    {
     int n=level==0?125:level==1?50:25,side=n+1;var verts=new Vector3[side*side];var norms=new Vector3[verts.Length];var uv=new Vector2[verts.Length];var triangles=new int[n*n*6];int at=0;
     for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){int i=z*side+x;float wx=ox+x*500f/n,wz=oz+z*500f/n;verts[i]=new Vector3(wx-ox,surface.Sample(wx,wz),wz-oz);norms[i]=surface.Normal(wx,wz);uv[i]=new Vector2(wx/4,wz/4);}
     for(int z=0;z<n;z++)for(int x=0;x<n;x++){int i=z*side+x;foreach(int v in new[]{i,i+side,i+1,i+1,i+side,i+side+1})triangles[at++]=v;}
     var mesh=Asset292("Meshes/"+id+"_"+level+".asset",()=>new Mesh());mesh.Clear();mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=verts;mesh.normals=norms;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);
     var go=new GameObject("LOD"+level);go.transform.SetParent(tile.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=material;
     lods.Add(new LOD(level==0?.23f:level==1?.055f:.001f,new[]{mr}));if(level==0)tile.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
    var lg=tile.AddComponent<LODGroup>();lg.SetLODs(lods.ToArray());lg.RecalculateBounds();
   }
   EditorUtility.SetDirty(content);EditorUtility.SetDirty(layout);Physics.SyncTransforms();Save292();
   File.WriteAllText(O292+"/terrain-installed.json","{\"tiles\":96,\"heightSamples\":1502501,\"relocatedAssemblies\":"+rows.Count+",\"navigation\":\"pending\",\"manualPlay\":\"not run\"}");
   return "96 connected terrain tiles and independent TerrainData sources installed; "+rows.Count+" assemblies relocated. Navigation/play/art acceptance pending.";
  }
  [Serializable] sealed class CompactLayoutSnapshot292{public CompactWorldLayoutSO.Place[] Places;}
  static string UpdateSurface292()
  {
   RequireClean292();var s=Session292();var layout=s.MountainLayout;
   var installedPath=O292+"/installed-height.bytes";
   if(!File.Exists(installedPath))throw new Exception("The installed surface snapshot is required for non-accumulating relocation");
   var oldField=new CompactWorldSurface(layout.SurfaceWidth,layout.SurfaceHeight,layout.SurfaceCell,File.ReadAllBytes(installedPath));
   JsonUtility.FromJsonOverwrite(File.ReadAllText(A292+"/Surface/layout.json"),layout);
   AssetDatabase.ImportAsset(A292+"/Surface/height.bytes",ImportAssetOptions.ForceUpdate);layout.FinalSurface=AssetDatabase.LoadAssetAtPath<TextAsset>(A292+"/Surface/height.bytes");layout.SurfaceDistribution=Texture292("surface.png",true);
   var field=new CompactWorldSurface(layout);GroundMaterial292(layout);
   Vector3 Map(Vector3 p)
   {
    Vector2 xz=new Vector2(p.x,p.z);
    var rigid=layout.Places.FirstOrDefault(q=>(q.Id=="village"&&Vector2.Distance(q.XZ,xz)<180)||(q.Id=="mine"&&Vector2.Distance(q.XZ,xz)<210));
    var at=rigid!=null?rigid.XZ:xz;
    return p+Vector3.up*(field.Sample(at.x,at.y)-oldField.Sample(at.x,at.y));
   }
   var sceneRoots=s.gameObject.scene.GetRootGameObjects();
   var moved=new HashSet<Transform>();
   foreach(string line in File.ReadAllLines(O292+"/relocated-roots.txt"))
   {
    string path=line.Split('|')[0].Trim();var parts=path.Split('/');
    var go=sceneRoots.FirstOrDefault(g=>g.name==parts[0]);if(go==null)continue;
    var t=go.transform;for(int i=1;i<parts.Length&&t!=null;i++)t=t.Find(parts[i]);if(t==null||!moved.Add(t))continue;
    var rr=t.GetComponentsInChildren<Renderer>(true).Where(r=>!(r is ParticleSystemRenderer)&&!(r is LineRenderer)&&!(r is TrailRenderer)).ToArray();
    Vector3 pivot=t.position;
    if(rr.Length>0){var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);pivot=b.center;}
    if(t.name=="Village245"||t.name=="Village249Frontage")pivot=field.Point(layout.Places.First(p=>p.Id=="village").XZ);
    t.position+=Map(pivot)-pivot;
   }
   var content=s.Content;
   foreach(var p in content.Points)p.Position=Map(p.Position);
   foreach(var e in content.Encounters){e.Feet=Map(e.Feet);if(e.Patrol!=null)e.Patrol=e.Patrol.Select(Map).ToArray();}
   foreach(var c in content.Checkpoints)c.Feet=Map(c.Feet);
   content.StartFeet=Map(content.StartFeet);content.InnCheckpointFeet=Map(content.InnCheckpointFeet);
   content.MainPath=content.MainPath.Select(Map).ToArray();content.BranchPath=content.BranchPath.Select(Map).ToArray();EditorUtility.SetDirty(content);
   foreach(var catalog in sceneRoots.SelectMany(g=>g.GetComponentsInChildren<WorldLocationArrival>(true)).Select(a=>a.Catalog).Where(c=>c!=null).Distinct())
   {foreach(var e in catalog.Entries.Where(e=>e.Priority>0)){var p=e.Centre;e.Centre=Map(p);float dy=e.Centre.y-p.y;if(e.MinimumY>-900)e.MinimumY+=dy;if(e.MaximumY<1900)e.MaximumY+=dy;if(e.FloorPath!=null)e.FloorPath=e.FloorPath.Select(Map).ToArray();}EditorUtility.SetDirty(catalog);}
   var root=GameObject.Find("Reworld292_Terrain");if(root==null)throw new Exception("Install terrain first");
   foreach(Transform tile in root.transform)
   {
    foreach(var filter in tile.GetComponentsInChildren<MeshFilter>(true))
    {var mesh=filter.sharedMesh;var v=mesh.vertices;var n=mesh.normals;
     for(int i=0;i<v.Length;i++){float x=tile.position.x+v[i].x,z=tile.position.z+v[i].z;v[i].y=field.Sample(x,z);n[i]=field.Normal(x,z);}mesh.vertices=v;mesh.normals=n;mesh.RecalculateBounds();mesh.RecalculateTangents();EditorUtility.SetDirty(mesh);}
    var collider=tile.GetComponent<MeshCollider>();var cm=collider.sharedMesh;collider.sharedMesh=null;collider.sharedMesh=cm;tile.GetComponent<LODGroup>().RecalculateBounds();
    var td=AssetDatabase.LoadAssetAtPath<TerrainData>(A292+"/TerrainData/"+tile.name+".asset");var h=new float[129,129];for(int z=0;z<129;z++)for(int x=0;x<129;x++)h[z,x]=field.Sample(tile.position.x+x*500/128f,tile.position.z+z*500/128f)/600;td.SetHeights(0,0,h);EditorUtility.SetDirty(td);
   }
   var routes=JsonUtility.FromJson<Routes292>(File.ReadAllText(A292+"/Surface/routes.json"));
   foreach(var ui in sceneRoots.SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true))){ui.MapData.IllustratedMap=Texture292("cartography.png");ui.MapData.ExploredMap=ui.MapData.IllustratedMap;ui.MapData.Lines=routes.routes.Select(r=>new WorldMapLineSpec{Id=r.id,Kind=WorldMapLineKind.Trail,Points=r.points.Select(p=>new Vector2(p.x,p.z)).ToArray(),PixelWidth=1}).ToArray();EditorUtility.SetDirty(ui.MapData);}
   EditorUtility.SetDirty(layout);Physics.SyncTransforms();Save292();File.Copy(A292+"/Surface/height.bytes",installedPath,true);
   return "Terrain/collision/map refreshed; "+moved.Count+" owned assemblies and private content reprojected from the installed surface snapshot. Navigation and gameplay need new validation.";
  }
 }
}
