using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  const string A285="Assets/_Project/Art/World/MountainTrail285",O285="../Art/World/Compact/Rebuild/Mountain285";
  public const string Scene285=A285+"/W_Cheongrim_GraniteTrail.unity";
  [Serializable]sealed class Heights285 {public float[] heights;}
  static MountainTrailProfile Profile285=>AssetDatabase.LoadAssetAtPath<MountainTrailProfile>(A285+"/TrailProfile.asset");
  [MenuItem("Oheangbu/별도 맵/암릉 산길 285 열기 (이 씬에서 Play)")]
  public static void Open285()=>Mountain285("open");
  public static string Mountain285(string command){
   Directory.CreateDirectory(O285);
   if(command!="build"&&command!="open"&&SceneManager.GetActiveScene().path!=Scene285)throw new InvalidOperationException("Open the isolated 285 scene first.");
   if(command=="check")return Check285();
   if(command=="capture")return Capture285(false);
   if(command=="clay")return Capture285(true);
   if(command=="raw")return Capture285(false,false);
   if(command=="walk-play"){
    if(SceneManager.GetActiveScene().path!=Scene285||SceneManager.GetActiveScene().isDirty||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Clean 285 Edit scene required");
    SessionState.SetBool("Mountain285.Probe",true);CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "Two-way Play physics/camera/frame timing probe scheduled";
   }
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Edit scene required; current changes preserved");
   if(command=="open"){EditorSceneManager.OpenScene(Scene285);CompactLoadingStartup270.UseCurrent();return Scene285;}
   if(command!="build")throw new ArgumentException(command);
   DevSceneKit.EnsureFolder(A285+"/Meshes");DevSceneKit.EnsureFolder(A285+"/Materials");
   foreach(string path in Directory.GetFiles(A285+"/Textures","*.jpg")){
    var ti=(TextureImporter)AssetImporter.GetAtPath(path.Replace('\\','/'));if(ti==null)continue;
    ti.textureType=path.Contains("nor_gl")?TextureImporterType.NormalMap:TextureImporterType.Default;
    ti.sRGBTexture=!path.Contains("nor_gl")&&!path.Contains("_arm_");ti.maxTextureSize=4096;ti.mipmapEnabled=true;ti.anisoLevel=8;ti.wrapMode=TextureWrapMode.Repeat;ti.alphaSource=TextureImporterAlphaSource.None;ti.SaveAndReimport();
   }
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var root=new GameObject("Granite_Trail_285");
   var profile=Profile285;if(profile==null){profile=ScriptableObject.CreateInstance<MountainTrailProfile>();AssetDatabase.CreateAsset(profile,A285+"/TrailProfile.asset");}
   JsonUtility.FromJsonOverwrite(File.ReadAllText(O285+"/route.json"),profile);EditorUtility.SetDirty(profile);
   var spline=new GameObject("Authored_trail_centreline").AddComponent<SplineContainer>();spline.transform.SetParent(root.transform);spline.Spline.Clear();
   for(int i=0;i<profile.knots.Length;i++){var p=profile.knots[i];var tangent=(profile.knots[Mathf.Min(profile.knots.Length-1,i+1)]-profile.knots[Mathf.Max(0,i-1)]) /6; spline.Spline.Add(new BezierKnot(p,-tangent,tangent),TangentMode.Broken);}
   var rock=RockMat285("Granite","rock_face_03",1/3.5f,new Color(.91f,.92f,.9f));
   var steps=RockMat285("Worn_steps","rock_face_03",1/2.2f,new Color(1.05f,1.04f,1.0f));steps.SetFloat("_AmbientFloor",.38f);steps.SetFloat("_SurfaceLift",.055f);EditorUtility.SetDirty(steps);
   var soil=RockMat285("Rooted_soil","roots",1/1.7f,new Color(1,1,.95f));
   var td=AssetDatabase.LoadAssetAtPath<TerrainData>(A285+"/Mountain.asset");if(td==null){td=new TerrainData();AssetDatabase.CreateAsset(td,A285+"/Mountain.asset");}
   td.heightmapResolution=1025;td.alphamapResolution=512;td.size=new Vector3(1200,400,1000);
   var flat=JsonUtility.FromJson<Heights285>(File.ReadAllText(O285+"/terrain.json")).heights;var h=new float[1025,1025];for(int z=0;z<1025;z++)for(int x=0;x<1025;x++)h[z,x]=flat[z*1025+x];td.SetHeights(0,0,h);
   var layers=new TerrainLayer[2];for(int i=0;i<2;i++){
    string slug=i==0?"rock_face_03":"roots",path=A285+"/Layer"+i+".terrainlayer";var l=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);if(l==null){l=new TerrainLayer();AssetDatabase.CreateAsset(l,path);}
    l.diffuseTexture=Tex285(slug,"diff");l.normalMapTexture=Tex285(slug,"nor_gl");l.maskMapTexture=Tex285(slug,"arm");l.tileSize=Vector2.one*(i==0?2.7f:1.7f);l.normalScale=.7f;l.smoothness=0;layers[i]=l;EditorUtility.SetDirty(l);
   }td.terrainLayers=layers;
   var splat=new float[512,512,2];for(int z=0;z<512;z++)for(int x=0;x<512;x++){float slope=td.GetSteepness(x/511f,z/511f);float dirt=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(20,45,slope)))*.78f;splat[z,x,0]=1-dirt;splat[z,x,1]=dirt;}td.SetAlphamaps(0,0,splat);EditorUtility.SetDirty(td);
   var tg=UnityEngine.Terrain.CreateTerrainGameObject(td);tg.name="Native_lower_mountain";tg.transform.SetParent(root.transform);tg.transform.position=new Vector3(-650,-140,-350);
   var terrain=tg.GetComponent<Terrain>();terrain.heightmapPixelError=3;terrain.basemapDistance=2200;terrain.drawInstanced=true;
   terrain.materialTemplate=SurveyMaterial(new Material(Shader.Find("Oheangbu/Prototype/MountainTerrain285")),A285+"/Terrain.mat");terrain.materialTemplate.EnableKeyword("_NORMALMAP");
   foreach(string family in new[]{"Granite_face_","Granite_foundation_"})for(int i=0;i<3;i++){
    var parent=new GameObject(family+i);parent.transform.SetParent(root.transform);var lods=new List<LOD>();
    for(int level=0;level<3;level++){var child=MeshObject278("LOD"+level,Import285(family+i+"_LOD"+level),rock,parent.transform,false);lods.Add(new LOD(level==0?.30f:level==1?.09f:.002f,new[]{child.GetComponent<Renderer>()}));}
    parent.AddComponent<LODGroup>().SetLODs(lods.ToArray());parent.GetComponent<LODGroup>().RecalculateBounds();parent.AddComponent<MeshCollider>().sharedMesh=Import285(family+i+"_LOD2");
   }
   for(int i=0;i<2;i++)MeshObject278("Carved_trail_"+i,Import285("Carved_trail_"+i),steps,root.transform,true);
   for(int i=0;i<2;i++)MeshObject278("Trail_shoulder_"+i,Import285("Trail_shoulder_"+i),rock,root.transform,true);
   var wood=PierMat289("planks");var poleMaterial=PierMat289("poles");
   var plankMeshes=Enumerable.Range(0,10).Select(i=>Import285("Pier289_planks_"+i)).ToArray();var poleMeshes=Enumerable.Range(0,3).Select(i=>Import285("Pier289_poles_"+i)).ToArray();int poleIndex=0;
   var bridge=new GameObject("Timber_bridge");bridge.transform.SetParent(root.transform);
   void TimberLog(string name,Vector3 a,Vector3 b,float radius,bool collide){var go=MeshObject278(name,poleMeshes[poleIndex++%3],poleMaterial,bridge.transform,false);go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);go.transform.localScale=new Vector3(radius*2,(b-a).magnitude,radius*2);if(collide){var c=go.AddComponent<CapsuleCollider>();c.direction=1;c.radius=.45f;c.height=1;}var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.006f,new[]{go.GetComponent<Renderer>()})});lod.RecalculateBounds();}
   var timberRng=new System.Random(288);float T(float a,float b)=>Mathf.Lerp(a,b,(float)timberRng.NextDouble());int plank=0;
   for(float s=profile.bridgeStart;s<profile.bridgeEnd;){
    float span=Mathf.Min(T(.16f,.29f),profile.bridgeEnd-s),mid=s+span*.5f;var p=profile.At(mid);
    var mesh=plankMeshes[plank++%10];var go=MeshObject278("Timber_tread",mesh,wood,bridge.transform,false);
    go.transform.position=p-Vector3.up*(.061f+T(-.008f,.008f))+profile.Right(mid)*T(-.08f,.08f);
    go.transform.rotation=Quaternion.LookRotation(Vector3.Cross(profile.Right(mid),Vector3.up))*Quaternion.Euler(T(-1.8f,1.8f),T(-.7f,.7f),T(-.5f,.5f));
    go.transform.localScale=new Vector3(profile.Width(mid)+T(.16f,.43f),T(.10f,.14f),span+.04f);
    var box=go.AddComponent<BoxCollider>();box.center=Vector3.zero;box.size=new Vector3(.98f,.94f,1);s+=span;
   }
   for(float s=profile.bridgeStart;s<=profile.bridgeEnd;s+=1.2f){var p=profile.At(s);var right=profile.Right(s);TimberLog("Braced_support",p+right*1.25f-Vector3.up*2.8f,p-right*.88f-Vector3.up*.23f,.11f,false);TimberLog("Cross_bearer",p+right*1.2f-Vector3.up*.23f,p-right*1.15f-Vector3.up*.23f,.10f,false);}
   foreach(float offset in new[]{-.75f,.75f})for(float s=profile.bridgeStart;s<profile.bridgeEnd;s+=1.0f){float end=Mathf.Min(profile.bridgeEnd,s+1.08f);TimberLog("Longitudinal_bearer",profile.At(s)+profile.Right(s)*offset-Vector3.up*.15f,profile.At(end)+profile.Right(end)*offset-Vector3.up*.15f,.08f,false);}
   Vector3? previousRail=null;
   for(float s=profile.bridgeStart;;s=Mathf.Min(profile.bridgeEnd,s+T(1.05f,1.65f))){
    var p=profile.At(s)-profile.Right(s)*(profile.Width(s)*.5f+T(.06f,.17f));
    var tip=p+Vector3.up*T(.87f,1.09f)-profile.Right(s)*T(-.045f,.11f)+Vector3.forward*T(-.09f,.09f);
    TimberLog("Rail_post",p-Vector3.up*T(.18f,.32f),tip+Vector3.up*T(.06f,.13f),T(.055f,.086f),true);
    if(previousRail.HasValue){var bend=Vector3.Lerp(previousRail.Value,tip,.52f)-Vector3.up*T(.025f,.07f);TimberLog("Handrail",previousRail.Value,bend,T(.045f,.062f),true);TimberLog("Handrail",bend,tip,T(.044f,.06f),true);}
    previousRail=tip;if(s>=profile.bridgeEnd)break;
   }
   Dress285(root.transform,terrain,profile,rock,soil);
   var light=new GameObject("Overcast_sun").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(43,112,0);light.color=new Color(1,.96f,.89f);light.intensity=1.25f;light.shadows=LightShadows.Soft;light.shadowBias=.04f;light.shadowNormalBias=.3f;light.shadowStrength=.72f;
   RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.49f,.52f,.55f);RenderSettings.ambientEquatorColor=new Color(.38f,.40f,.40f);RenderSettings.ambientGroundColor=new Color(.25f,.26f,.25f);
   var sky=new Material(Shader.Find("Oheangbu/Prototype/CloudSky285"));sky.SetTexture("_Tex",AssetDatabase.LoadAssetAtPath<Texture2D>(A285+"/Textures/kloofendal_48d_partly_cloudy_puresky_4k.hdr"));sky.SetFloat("_Exposure",.7f);sky.SetFloat("_Rotation",75);RenderSettings.skybox=SurveyMaterial(sky,A285+"/Materials/Sky.mat");

   RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.66f,.71f,.73f);RenderSettings.fogStartDistance=110;RenderSettings.fogEndDistance=650;
   var mist=SurveyMaterial(new Material(Shader.Find("Oheangbu/Prototype/ValleyMist278")),A285+"/Materials/Mist.mat");mist.SetColor("_Color",new Color(.76f,.80f,.81f,.37f));
   for(int i=0;i<9;i++){var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="Valley_cloud_"+i;go.transform.SetParent(root.transform);go.transform.position=new Vector3(-100-i*52,-20+i%3*24,40+i*36);go.transform.rotation=Quaternion.Euler(72,0,0);go.transform.localScale=new Vector3(220,160,1);go.GetComponent<Renderer>().sharedMaterial=mist;Object.DestroyImmediate(go.GetComponent<Collider>());}
   var volume=new GameObject("Local_grade").AddComponent<Volume>();volume.isGlobal=true;
   var vp=AssetDatabase.LoadAssetAtPath<VolumeProfile>(A285+"/Grade.asset");if(vp==null){vp=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(vp,A285+"/Grade.asset");var tone=vp.Add<Tonemapping>();AssetDatabase.AddObjectToAsset(tone,vp);var c=vp.Add<ColorAdjustments>();AssetDatabase.AddObjectToAsset(c,vp);}vp.TryGet<Tonemapping>(out var tonemap);tonemap.mode.Override(TonemappingMode.ACES);vp.TryGet<ColorAdjustments>(out var grade);grade.saturation.Override(-20);grade.contrast.Override(-7);grade.postExposure.Override(.35f);EditorUtility.SetDirty(tonemap);EditorUtility.SetDirty(grade);EditorUtility.SetDirty(vp);volume.sharedProfile=vp;
   var player=new GameObject("Mountain_explorer");var cc=player.AddComponent<CharacterController>();cc.height=1.8f;cc.center=Vector3.up*.9f;cc.radius=.28f;cc.stepOffset=.26f;cc.slopeLimit=42;cc.skinWidth=.025f;
   var explorer=player.AddComponent<CliffAscentExplorer278>();explorer.StartFeet=profile.At(1);player.transform.position=explorer.StartFeet+Vector3.up*.1f;player.transform.rotation=Quaternion.LookRotation(profile.At(5)-profile.At(1));
   var cam=new GameObject("Main Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.transform.SetParent(player.transform,false);cam.transform.localPosition=Vector3.up*1.65f;cam.nearClipPlane=.06f;cam.farClipPlane=2200;cam.fieldOfView=60;cam.clearFlags=CameraClearFlags.Skybox;cam.gameObject.AddComponent<AudioListener>();explorer.View=cam;
   var extra=cam.GetUniversalAdditionalCameraData();extra.SetRenderer(Renderer278());extra.requiresDepthTexture=true;extra.renderPostProcessing=true;extra.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
   Physics.SyncTransforms();var nav=new GameObject("Trail_navigation").AddComponent<NavMeshSurface>();nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.center=new Vector3(10,30,43);nav.size=new Vector3(55,45,110);nav.overrideVoxelSize=true;nav.voxelSize=.08f;nav.BuildNavMesh();
   if(nav.navMeshData!=null){var data=Object.Instantiate(nav.navMeshData);nav.RemoveData();var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(A285+"/Navigation.asset");if(old==null)AssetDatabase.CreateAsset(data,A285+"/Navigation.asset");else{EditorUtility.CopySerialized(data,old);EditorUtility.SetDirty(old);Object.DestroyImmediate(data);data=old;}nav.navMeshData=data;nav.AddData();}
   foreach(string guid in AssetDatabase.FindAssets("",new[]{A285})){var a=AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));if(a!=null)AssetDatabase.SaveAssetIfDirty(a);}
   EditorSceneManager.SaveScene(scene,Scene285);CompactLoadingStartup270.UseCurrent();return "Saved native Terrain + Blender granite/steps + timber bridge + CC0 sources; route="+profile.length;
  }
  static Texture2D Tex285(string slug,string kind)=>AssetDatabase.LoadAssetAtPath<Texture2D>(A285+"/Textures/"+slug+"_"+kind+"_4k.jpg");
  static Material PierMat289(string part){var m=new Material(Shader.Find("Oheangbu/Prototype/Timber289")){name="Pier289_"+part,enableInstancing=true};foreach(var entry in new[]{("_BaseMap","diff"),("_BumpMap","nor_gl"),("_MaskMap","arm")})m.SetTexture(entry.Item1,AssetDatabase.LoadAssetAtPath<Texture2D>(A285+"/Textures/modular_wooden_pier_"+part+"_"+entry.Item2+"_2k.jpg"));m.SetColor("_BaseColor",new Color(.90f,.86f,.80f));m.SetFloat("_AmbientFloor",.32f);m.SetFloat("_SurfaceLift",.018f);return SurveyMaterial(m,A285+"/Materials/Pier289_"+part+".mat");}
  static Material RockMat285(string name,string slug,float scale,Color tint){var m=new Material(Shader.Find("Oheangbu/Prototype/Granite285")){name=name,enableInstancing=true};m.SetTexture("_BaseMap",Tex285(slug,"diff"));m.SetTexture("_BumpMap",Tex285(slug,"nor_gl"));m.SetTexture("_MaskMap",Tex285(slug,"arm"));m.SetFloat("_WorldScale",scale);m.SetColor("_BaseColor",tint);m.SetFloat("_AmbientFloor",.28f);m.SetFloat("_SurfaceLift",.025f);return SurveyMaterial(m,A285+"/Materials/"+name+".mat");}
  static Mesh Import285(string name){var path=A285+"/Meshes/"+name+".asset";var d=JsonUtility.FromJson<MeshAscent279>(File.ReadAllText(O285+"/Meshes/"+name+".json"));var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=d.vertices;mesh.normals=d.normals;mesh.uv=d.uv;mesh.triangles=d.triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();return ArtMesh(mesh,path);}
  static void Log285(string name,Vector3 a,Vector3 b,float r,Material m,Transform parent,bool collider){Log278(name,a,b,r,m,parent);if(!collider)Object.DestroyImmediate(parent.GetChild(parent.childCount-1).GetComponent<Collider>());}
  static Mesh TimberPlank288(int index,System.Random rng){
   float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
   // Split, skewed ends and shallow cupping, retained in the silhouette.
   const int n=7;for(int layer=0;layer<2;layer++)for(int x=0;x<n;x++)for(int edge=0;edge<2;edge++){
    float u=x/(float)(n-1),xx=u-.5f;float z=(edge==0?-.5f:.5f)+R(-.035f,.035f);
    if(x==0||x==n-1)xx+=R(-.028f,.028f);
    vertices.Add(new Vector3(xx,(layer==0?-.5f:.5f)+.065f*Mathf.Sin(u*Mathf.PI)+R(-.035f,.035f),z));uv.Add(new Vector2(u,edge));
   }
   void Quad(int a,int b,int c,int d){triangles.AddRange(new[]{a,b,c,a,c,d});}
   for(int x=0;x<n-1;x++){int a=x*2;Quad(a,a+2,a+3,a+1);Quad(a+14,a+15,a+17,a+16);Quad(a,a+14,a+16,a+2);Quad(a+1,a+3,a+17,a+15);}
   Quad(0,1,15,14);Quad(12,26,27,13);
   var mesh=new Mesh{name="Weathered_plank_"+index};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();return ArtMesh(mesh,A285+"/Meshes/Weathered_plank_"+index+".asset");
  }
  static void Dress285(Transform root,Terrain terrain,MountainTrailProfile route,Material rock,Material soil){
   Mesh canopyCover=null;
   var sheet=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>("Assets/_Project/Art/World/Rock275/Placements.asset");var pine=sheet.Prototypes.First(p=>p.Id.Contains("Pinus")&&p.Lods.Length>=2);var grass=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_Grass");var fern=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_Deparia_1");
   var broadFern=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_Deparia_3");var groundCover=sheet.Prototypes.First(p=>p.Id=="Cheongrim_LowGroundFill");var elm=sheet.Prototypes.First(p=>p.Id=="Cheongrim_SM_UlmusDavidiana_Summer_2");
   var current=SceneManager.GetActiveScene();var sourceScene=EditorSceneManager.OpenScene(Scene284,OpenSceneMode.Additive);
   var treeSource=sourceScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LODGroup>(true)).First(g=>g.name=="Slope_pine_0");
   var treeTemplate=Object.Instantiate(treeSource.gameObject);SceneManager.MoveGameObjectToScene(treeTemplate,current);treeTemplate.SetActive(false);EditorSceneManager.CloseScene(sourceScene,true);SceneManager.SetActiveScene(current);
   var mats=new Dictionary<Material,Material>();Material PlantMat(Material source){if(mats.TryGetValue(source,out var cached))return cached;var m=new Material(source);m.shader=Shader.Find("Oheangbu/Prototype/CliffProp278");m.SetFloat("_AmbientFloor",.55f);m.SetFloat("_Leaf279",.7f);if(source.GetTexture("_BaseMap")==null)m.SetFloat("_Cull",0);m.SetFloat("_Saturation",.15f);m.SetFloat("_WindAmplitude",.045f);m.SetFloat("_WashStart",160);m.SetFloat("_WashEnd",1200);m.SetFloat("_WashStrength",.65f);m.SetFloat("_FadeInStart",-1);m.SetFloat("_FadeInEnd",0);m.SetFloat("_DressingFadeOverride",0);m.SetFloat("_FadeOutStart",1100);m.SetFloat("_FadeOutEnd",1400);m.enableInstancing=true;m=SurveyMaterial(m,A285+"/Materials/Plant_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+".mat");mats[source]=m;return m;}
   void Plant(WorldMacroDressingSheetSO.Prototype species,Vector3 p,float scale,float yaw,string name){
    if(species==pine){var copy=Object.Instantiate(treeTemplate,root);copy.name=name;copy.SetActive(true);copy.transform.position=p;copy.transform.rotation=Quaternion.Euler(0,yaw,0);copy.transform.localScale=treeTemplate.transform.localScale*scale;var levels=copy.GetComponent<LODGroup>().GetLODs();for(int j=0;j<levels.Length;j++)levels[j].screenRelativeTransitionHeight=j==0?.055f:j==1?.012f:.001f;copy.GetComponent<LODGroup>().SetLODs(levels);
     foreach(var r in copy.GetComponentsInChildren<Renderer>(true)){
      if(r.sharedMaterial!=null&&r.sharedMaterial.HasProperty("_BaseMap")&&r.sharedMaterial.GetTexture("_BaseMap")==null&&r.TryGetComponent<MeshFilter>(out var mf)){
       if(canopyCover==null)canopyCover=NeedleCover285(mf.sharedMesh);mf.sharedMesh=canopyCover;
      }
      r.sharedMaterials=r.sharedMaterials.Select(PlantMat).ToArray();
     }
     var bounds=copy.GetComponentsInChildren<Renderer>().Select(r=>r.bounds.min.y);if(bounds.Any())copy.transform.position+=Vector3.up*(p.y-bounds.Min());return;
    }
    var go=new GameObject(name);go.transform.SetParent(root);go.transform.position=p;go.transform.rotation=Quaternion.Euler(0,yaw,0);go.transform.localScale=Vector3.one*scale;var lods=new List<LOD>();for(int l=0;l<Mathf.Min(3,species.Lods.Length);l++){var rs=new List<Renderer>();foreach(var part in species.Lods[l].Parts){var child=MeshObject278("LOD"+l,part.Mesh,PlantMat(part.Material),go.transform,false);child.transform.localPosition=part.Local.GetColumn(3);child.transform.localRotation=part.Local.rotation;child.transform.localScale=part.Local.lossyScale;rs.Add(child.GetComponent<Renderer>());}lods.Add(new LOD(l==Mathf.Min(3,species.Lods.Length)-1?.002f:l==0?.045f:.01f,rs.ToArray()));}go.AddComponent<LODGroup>().SetLODs(lods.ToArray());go.GetComponent<LODGroup>().RecalculateBounds();}
   var rng=new System.Random(285);float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   for(int i=0;i<34;i++){float z=R(-20,130);float x=R(35,140);var p=new Vector3(x,0,z);p.y=terrain.SampleHeight(p)+terrain.transform.position.y;if(terrain.terrainData.GetSteepness((x+650)/1200,(z+350)/1000)>42)continue;Plant(pine,p-Vector3.up*.08f,R(.7f,1.25f),R(0,360),"Crest_pine_"+i);}
   Physics.SyncTransforms();
   for(int i=0;i<32;i++){float distance=R(6,route.length-6);var at=route.At(distance)+route.Right(distance)*R(5,16);var hits=Physics.RaycastAll(new Vector3(at.x,150,at.z),Vector3.down,160).Where(h=>h.normal.y>.48f&&h.point.y>at.y+10).OrderBy(h=>h.distance).ToArray();if(hits.Length>0)Plant(pine,hits[0].point-Vector3.up*.10f,R(.65f,.95f),R(0,360),"Joint_pine_"+i);}
   foreach(float s in new[]{5f,47f,98f}){
    var p=route.At(s)-route.Right(s)*(route.Width(s)*.5f+.5f);p.y-=.2f;
    var mound=MeshObject278("Pine_root_rock",Import285("Scanned_boulder_LOD1"),rock,root,false);mound.transform.position=p-Vector3.up*1.1f;mound.transform.localScale=new Vector3(2.0f,.8f,1.4f);mound.AddComponent<MeshCollider>().sharedMesh=mound.GetComponent<MeshFilter>().sharedMesh;
    Physics.SyncTransforms();if(mound.GetComponent<Collider>().Raycast(new Ray(p+Vector3.up*7,Vector3.down),out var hit,12))p=hit.point;Plant(pine,p-Vector3.up*.07f,R(.85f,1.1f),s*9,"Ledge_pine_"+s);
   }
   var debris=Import285("Scanned_boulder_LOD2");var batches=new List<CombineInstance>[5];for(int i=0;i<5;i++)batches[i]=new List<CombineInstance>();
   for(int i=0;i<220;i++){float s=R(1,route.length-1);if(s>53&&s<60)continue;var right=route.Right(s);float offset=route.Width(s)*.5f-R(.05f,.24f);var p=route.At(s)+right*offset-Vector3.up*.06f;float sc=R(.045f,.18f);batches[Mathf.Min(4,(int)(s/22))].Add(new CombineInstance{mesh=debris,transform=Matrix4x4.TRS(p,Quaternion.Euler(R(-20,20),R(0,360),0),Vector3.one*sc)});if(i%8==0)Plant(i%3==0?fern:grass,p,R(.25f,.55f),R(0,360),"Rock_pocket_plant_"+i);}
   for(int i=0;i<5;i++){var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(batches[i].ToArray());m=ArtMesh(m,A285+"/Meshes/Debris"+i+".asset");var go=MeshObject278("Debris_batch_"+i,m,rock,root,false);go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;var lod=go.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.035f,new[]{go.GetComponent<Renderer>()})});lod.RecalculateBounds();}
   // Valley-side shoulder: gaps and plant pockets, rather than a continuous wall.
   var edgeRng=new System.Random(287);float E(float a,float b)=>Mathf.Lerp(a,b,(float)edgeRng.NextDouble());
   var edgeRock=Import285("Scanned_boulder_LOD1");int rockIndex=0,plantIndex=0;
   for(float s=3;s<route.length-3;s+=E(1.8f,3.8f)){
    if(s>route.bridgeStart-1&&s<route.bridgeEnd+1)continue;
    var p=route.At(s)-route.Right(s)*(route.Width(s)*.5f+E(.58f,.82f));
    var go=MeshObject278("Outer_bedrock_"+rockIndex++,edgeRock,rock,root,false);go.transform.position=p-Vector3.up*E(.36f,.55f);go.transform.rotation=Quaternion.Euler(E(-14,14),E(0,360),E(-9,9));go.transform.localScale=new Vector3(E(.65f,1.05f),E(.62f,.94f),E(.65f,1.15f));
    var box=go.AddComponent<BoxCollider>();box.center=edgeRock.bounds.center;box.size=edgeRock.bounds.size*.88f;
   }
   Physics.SyncTransforms();
   var clusterRng=new System.Random(288);float C(float a,float b)=>Mathf.Lerp(a,b,(float)clusterRng.NextDouble());
   void RootPlant(WorldMacroDressingSheetSO.Prototype species,Vector3 p,float scale,string name){
    Plant(species,p,scale,C(0,360),name);var planted=root.Find(name);var bounds=planted.GetComponentsInChildren<Renderer>(true).Select(r=>r.bounds.min.y).ToArray();if(bounds.Length>0)planted.position+=Vector3.up*(p.y-bounds.Min());
   }
   // Separate habitats, clustered seed centres and exposed intervals of bare rock.
   for(float centre=3;centre<route.length-3;centre+=C(4.5f,9.5f)){
    bool shade=clusterRng.NextDouble()<.38;int count=clusterRng.Next(7,17);float spread=C(.65f,1.8f);
    for(int k=0;k<count;k++){
     float s=Mathf.Clamp(centre+C(-spread,spread),1,route.length-1);if(s>route.bridgeStart-1&&s<route.bridgeEnd+1)continue;
     float outward=C(.2f,1.0f);var p=route.At(s)+route.Right(s)*(shade?route.Width(s)*.5f-.09f:-(route.Width(s)*.5f+outward));
     var hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,5).Where(h=>shade?h.collider.name.StartsWith("Carved_trail"):h.collider.name.StartsWith("Trail_shoulder")||h.collider.name.StartsWith("Granite_foundation")).OrderBy(h=>h.distance).ToArray();
     if(hits.Length==0)continue;p=hits[0].point-Vector3.up*.035f;
     var species=shade?(k%3==0?broadFern:fern):(k%4==0?groundCover:k%7==0?broadFern:grass);
     RootPlant(species,p,C(.40f,.85f),"Habitat_"+species.Id+"_"+plantIndex++);
    }
   }
   // Lower rock shelves support sparse deciduous saplings, away from the tread.
   for(int i=0;i<9;i++){
    float s=C(5,route.length-5);if(s>52&&s<61)continue;var p=route.At(s)-route.Right(s)*C(4.2f,8.0f);
    var hits=Physics.RaycastAll(p+Vector3.up*3,Vector3.down,24).Where(h=>h.collider.name.StartsWith("Granite_foundation")&&h.normal.y>.35f).OrderBy(h=>h.distance).ToArray();
    if(hits.Length>0)RootPlant(elm,hits[0].point-Vector3.up*.12f,C(.20f,.36f),"Apron_elm_"+i);
   }
   Object.DestroyImmediate(treeTemplate);
  }
  static Mesh NeedleCover285(Mesh source){
   // Preserve branch/needle placement while keeping thin needles visible at
   // walking distance. Shared source geometry is never changed.
   var result=Object.Instantiate(source);var v=source.vertices;var sums=new Vector3[v.Length];var counts=new int[v.Length];
   foreach(int sub in Enumerable.Range(0,source.subMeshCount)){var indices=source.GetTriangles(sub);for(int j=0;j<indices.Length;j+=3){var a=v[indices[j]];var b=v[indices[j+1]];var c=v[indices[j+2]];var centre=(a+b+c)/3;var axis=new[]{b-a,c-b,a-c}.OrderByDescending(e=>e.sqrMagnitude).First().normalized;for(int k=0;k<3;k++){int index=indices[j+k];var delta=v[index]-centre;var along=axis*Vector3.Dot(delta,axis);sums[index]+=centre+along+(delta-along)*3.2f;counts[index]++;}}}
   for(int i=0;i<v.Length;i++)if(counts[i]>0)v[i]=sums[i]/counts[i];result.vertices=v;result.RecalculateBounds();return ArtMesh(result,A285+"/Meshes/NeedleCover.asset");
  }
  static string Check285(){
   var rows=new List<string>();void C(bool ok,string s)=>rows.Add((ok?"PASS ":"FAIL ")+s);var p=Profile285;C(SceneManager.GetActiveScene().path==Scene285,"isolated scene");C(p!=null&&p.points.Length==601,"persistent distance-based route");C(p.length>=90&&p.length<=110&&p.rise>=18&&p.rise<=25,"length="+p.length+" rise="+p.rise);C(p.widths.Min()>=1.2f,"minimum walk width="+p.widths.Min());
   var terrain=Object.FindFirstObjectByType<Terrain>();C(terrain!=null&&terrain.terrainData==terrain.GetComponent<TerrainCollider>().terrainData,"native Terrain collision/data match");
   foreach(string name in new[]{"Oheangbu/Prototype/Granite285","Oheangbu/Prototype/MountainTerrain285","Oheangbu/Prototype/CloudSky285","Oheangbu/Prototype/CliffProp278"}){var shader=Shader.Find(name);C(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"shader "+name);if(shader!=null)foreach(var msg in ShaderUtil.GetShaderMessages(shader))rows.Add("SHADER "+msg.message);}
   Physics.SyncTransforms();int hits=0,blocked=0;float error=0;for(int i=4;i<p.points.Length-4;i++){
    var point=p.points[i];foreach(float offset in new[]{0f,-.6f,.6f}){var at=point+p.Right(p.distances[i])*offset;var all=Physics.RaycastAll(at+Vector3.up*.8f,Vector3.down,1.7f).Where(x=>!(x.collider is CharacterController)).ToArray();if(all.Length>0){hits++;error=Mathf.Max(error,Mathf.Abs(all.OrderBy(x=>Mathf.Abs(x.point.y-point.y)).First().point.y-point.y));}}
    var obstacles=Physics.OverlapCapsule(point+Vector3.up*.4f,point+Vector3.up*1.5f,.26f).Where(x=>!(x is CharacterController)&&!x.name.StartsWith("Carved_trail")&&!x.name.StartsWith("Timber_tread")).ToArray();if(obstacles.Length>0){blocked++;if(blocked<8)rows.Add("OBSTRUCTION "+i+" "+string.Join(",",obstacles.Select(x=>x.name)));}
   }C(hits==(p.points.Length-8)*3,"centre/left/right ground rays="+hits+"/"+((p.points.Length-8)*3));C(error<.24f,"maximum surface discrepancy="+error);C(blocked==0,"standing capsule obstructions="+blocked);
   int seams=0,seamHits=0;for(float s=2;s<p.length-2;s+=.5f){if(s>=p.bridgeStart-.5f&&s<=p.bridgeEnd+.5f)continue;seams++;var at=p.At(s)+p.Right(s)*(p.Width(s)*.5f-.08f);if(Physics.RaycastAll(at+Vector3.up*.4f,Vector3.down,.8f).Any(h=>h.collider.name.StartsWith("Carved_trail")))seamHits++;}C(seams==seamHits,"buried inner trail edge supported="+seamHits+"/"+seams);
   var path=new NavMeshPath();bool nav=NavMesh.SamplePosition(p.At(1),out var start,1,NavMesh.AllAreas)&&NavMesh.SamplePosition(p.At(p.length-1),out var end,1,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;C(nav,"NavMesh route connected");
   C(GameObject.Find("Timber_bridge").GetComponentsInChildren<Collider>().Length>20,"bridge has tread/rail collision");C(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(b=>b!=null&&!b.GetType().Name.Contains("Campaign")&&!b.GetType().Name.Contains("Save")),"no missing scripts/campaign/save components");
   C(GameObject.Find("Granite_face_0").GetComponent<LODGroup>().GetLODs().Length==3,"large cliff three visual LODs and independent collision");
   var objects=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
   C(objects.All(g=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g)==0),"all scene objects free of missing scripts");
   C(objects.SelectMany(g=>g.GetComponents<MeshFilter>()).All(f=>f.sharedMesh!=null&&f.sharedMesh.vertexCount>0),"all MeshFilters contain valid mesh assets");
   C(objects.SelectMany(g=>g.GetComponents<Renderer>()).All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader))),"all renderer materials valid and shader-error free");
   var navigation=Object.FindFirstObjectByType<NavMeshSurface>();C(navigation!=null&&AssetDatabase.GetAssetPath(navigation.navMeshData)==A285+"/Navigation.asset","NavMesh uses persistent prototype asset");
   C(!EditorBuildSettings.scenes.Any(s=>s.path==Scene285),"prototype excluded from game build list");
   var habitats=objects.Where(g=>g.name.StartsWith("Habitat_")).ToArray();
   C(habitats.Length>30&&habitats.All(g=>g.GetComponentsInChildren<Collider>().Length==0),"cluster vegetation retains no per-plant collision; count="+habitats.Length);
   C(new[]{"Grass","Deparia_1","Deparia_3","LowGroundFill"}.All(id=>habitats.Any(g=>g.name.Contains(id))),"four understory forms in terrain-specific patches");
   var treads=objects.Where(g=>g.name=="Timber_tread").ToArray();C(treads.Max(g=>g.transform.localScale.z)-treads.Min(g=>g.transform.localScale.z)>.07f,"variable plank widths with bounded walking joints");
   var apronAngles=new List<float>();int apronHits=0;for(float s=20;s<=36;s+=4){var at=p.At(s)-p.Right(s)*6;var apronSamples=Physics.RaycastAll(at+Vector3.up,Vector3.down,18).Where(h=>h.collider.name.StartsWith("Granite_foundation")).ToArray();if(apronSamples.Any(h=>h.point.y<p.At(s).y-1&&h.normal.y>.3f))apronHits++;if(apronSamples.Length>0)apronAngles.Add(Vector3.Angle(apronSamples[0].normal,Vector3.up));}
   C(apronHits==5,"visible inclined apron below stair edge="+apronHits+"/5");
   rows.Add("MEASURE apron surface angle at five outer 6m samples: "+string.Join(", ",apronAngles.Select(a=>a.ToString("F1")))+" degrees from horizontal");
   C(treads.All(g=>g.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("Pier289_planks_")&&g.GetComponent<Renderer>().sharedMaterial.shader.name=="Oheangbu/Prototype/Timber289"),"CC0 imported plank geometry and original-UV timber material installed");
   File.WriteAllText(O285+"/dressing288-counts.json",JsonUtility.ToJson(new DressingCounts288{habitats=habitats.Length,elms=objects.Count(g=>g.name.StartsWith("Apron_elm_")),planks=treads.Length},true));
   File.WriteAllLines(O285+"/unity-checks.txt",rows);return string.Join("\n",rows);
  }
  static string Capture285(bool clay,bool post=true){
   var p=Profile285;var source=Object.FindFirstObjectByType<CliffAscentExplorer278>().View;var go=new GameObject("Capture285");var cam=go.AddComponent<Camera>();cam.CopyFrom(source);cam.enabled=false;cam.useOcclusionCulling=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
   cam.GetUniversalAdditionalCameraData().renderPostProcessing=post;
   var eyes=new[]{p.At(20)+Vector3.up*1.65f,new Vector3(-38,43,-2),p.At(92)+Vector3.up*1.65f,p.At(52)+Vector3.up*1.65f,p.At(27)+Vector3.up*1.65f,p.At(47)+Vector3.up*1.65f};var targets=new[]{p.At(37)+Vector3.up*3,new Vector3(-8,38,45),p.At(69)+Vector3.up,p.At(64)+Vector3.up*2,p.At(30)-p.Right(30)*7-Vector3.up*7,new Vector3(-215,60,225)};
   var saved=new Dictionary<Renderer,Material[]>();var neutral=new Material(Shader.Find("Oheangbu/Prototype/Granite285"));neutral.SetColor("_BaseColor",new Color(.6f,.6f,.6f));neutral.SetTexture("_BaseMap",Texture2D.whiteTexture);neutral.SetTexture("_MaskMap",Texture2D.whiteTexture);bool priorFog=RenderSettings.fog;
   if(clay){RenderSettings.fog=false;foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.sharedMaterial!=null&&(r.sharedMaterial.shader.name.Contains("Granite285")||r.sharedMaterial.shader.name.Contains("Timber289")))){saved[r]=r.sharedMaterials;r.sharedMaterials=r.sharedMaterials.Select(_=>neutral).ToArray();}}
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
   try{ShaderUtil.allowAsyncCompilation=false;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=60;for(int i=0;i<eyes.Length;i++){cam.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));cam.Render();cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(O285+"/"+(clay?"clay-":post?"view-":"raw-")+i+".png",tex.EncodeToPNG());}}
   finally{foreach(var kv in saved)kv.Key.sharedMaterials=kv.Value;RenderSettings.fog=priorFog;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(neutral);Object.DestroyImmediate(go);}
   EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Six 1080p "+(clay?"shape":"material")+" views";
  }
  [Serializable]sealed class DressingCounts288 {public int habitats,elms,planks;}
  static MountainTrailProbe285 probe285;
  [InitializeOnLoadMethod]static void Register285(){EditorApplication.playModeStateChanged-=Mode285;EditorApplication.playModeStateChanged+=Mode285;}
  static void Mode285(PlayModeStateChange state){
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool("Mountain285.Probe",false);EditorApplication.update-=Tick285;}
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Mountain285.Probe",false))return;
   var explorer=Object.FindFirstObjectByType<CliffAscentExplorer278>();explorer.enabled=false;explorer.GetComponent<CharacterController>().enabled=false;var g=new GameObject("Two_way_contact_probe");var cc=g.AddComponent<CharacterController>();cc.enabled=false;cc.height=1.8f;cc.center=Vector3.up*.9f;cc.radius=.28f;cc.stepOffset=.26f;cc.skinWidth=.025f;cc.slopeLimit=42;g.transform.position=Profile285.At(0)+Vector3.up*.15f;probe285=g.AddComponent<MountainTrailProbe285>();probe285.Profile=Profile285;probe285.View=explorer.View;probe285.Output=Path.GetFullPath(O285);cc.enabled=true;Application.runInBackground=true;EditorApplication.update+=Tick285;
  }
  static void Tick285(){if(probe285==null||probe285.Result==null)return;File.WriteAllText(O285+"/walk-check.txt",probe285.Result);EditorApplication.update-=Tick285;EditorApplication.isPlaying=false;}
 }
}
// Revision 288: supplementary slope and habitat audit.
