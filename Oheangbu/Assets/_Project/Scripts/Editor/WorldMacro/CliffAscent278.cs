using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string A278="Assets/_Project/Art/World/CliffAscent278";
  const string O278="../Art/World/Compact/Rebuild/Ascent278";
  public const string Scene278=A278+"/W_Cheongrim_CliffAscent_Prototype.unity";
  [Serializable] sealed class Route278 { public Vector3[] points;public float length,rise; }
  static Route278 RouteData278()=>JsonUtility.FromJson<Route278>(File.ReadAllText(O278+"/route.json"));
  static Vector3 RouteAt278(float z){var p=RouteData278().points;int i=Mathf.Clamp(Mathf.FloorToInt(z),0,p.Length-2);return Vector3.Lerp(p[i],p[i+1],Mathf.Clamp01(z-i));}
  [MenuItem("Oheangbu/별도 맵/절벽 산길 278 열기 (이 씬에서 Play)")]
  public static void OpenCliff278()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Save current edits before opening prototype");
   EditorSceneManager.OpenScene(Scene278);CompactLoadingStartup270.UseCurrent();
   if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.LookAt(new Vector3(30,65,110),Quaternion.Euler(16,55,0),145);
  }
  [MenuItem("Oheangbu/별도 맵/절벽 산길 278 재조립")]
  public static void BuildCliff278()=>Ascent278("build");
  public static string Ascent278(string command)
  {
   Directory.CreateDirectory(O278);
   if(command=="open"){OpenCliff278();return Scene278+"; direct play selected for prototype inspection";}
   if(command=="capture")return CaptureAscent278();
   if(command=="check")return CheckAscent278();
   if(command=="walk")return BeginWalk278();
   if(command=="walk-play"){
    if(SceneManager.GetActiveScene().path!=Scene278||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Prototype in Edit required");
    if(File.Exists(O278+"/walk-check.txt"))File.Copy(O278+"/walk-check.txt",O278+"/walk-editor.txt",true);
    SessionState.SetBool("Ascent278.Probe",true);CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "Play mode walking probe scheduled; exits Play when finished";
   }
   if(command=="tune"){SurfaceMaterial278("Granite",true);SurfaceMaterial278("Trail",false);AssetDatabase.SaveAssets();return "Local surface tuning saved";}
   if(command=="grade"){var volume=Object.FindFirstObjectByType<Volume>();volume.sharedProfile=Grade278();AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Persistent grading subassets repaired";}
   if(command!="build"||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Edit Mode required");
   var previous=SceneManager.GetActiveScene();
   if(previous.path==Scene278){EditorSceneManager.OpenScene(CompactLoadingStartup270.Lobby);previous=SceneManager.GetActiveScene();}
   DevSceneKit.EnsureFolder(A278);DevSceneKit.EnsureFolder(A278+"/Meshes");DevSceneKit.EnsureFolder(A278+"/Materials");
   if(File.Exists(Scene278)){Directory.CreateDirectory(O278+"/Recovery");File.Copy(Scene278,O278+"/Recovery/scene-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity");}
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
   var root=new GameObject("Cliff_Ascent_278");
   try
   {
    var rock=SurfaceMaterial278("Granite",true);var trail=SurfaceMaterial278("Trail",false);
    for(int i=0;i<6;i++){var cliff=MeshObject278("Cliff_"+i,Import278("Cliff_"+i),rock,root.transform,false);cliff.AddComponent<MeshCollider>().sharedMesh=Import278("Cliff_"+i+"_Collision");}
    for(int i=0;i<6;i++)MeshObject278("Cliff_Facade_"+i,Import278("FractureFacade_"+i),rock,root.transform,true);
    MeshObject278("Trail",Import278("Trail"),trail,root.transform,false).AddComponent<MeshCollider>().sharedMesh=Import278("Trail_Collision");
    for(int i=0;i<3;i++)
    {
     var distant=new Material(rock){name="Ridge_"+i};distant.SetFloat("_WashStart",80);distant.SetFloat("_WashEnd",900+i*350);distant.SetFloat("_WashStrength",.92f);distant.SetFloat("_BumpScale",0);
     distant=SurveyMaterial(distant,A278+"/Materials/Ridge_"+i+".mat");MeshObject278("Valley_Ridge_"+i,Import278("Ridge_"+i),distant,root.transform,false);
    }
    Physics.SyncTransforms();
    InstallMeshy282(root.transform,rock);
    Physics.SyncTransforms();
    var sheet=AssetDatabase.LoadAssetAtPath<Sheet>("Assets/_Project/Art/World/Rock275/Placements.asset");
    var materialCache=new Dictionary<Material,Material>();
    DressAscent279(root.transform,sheet,materialCache);
    var kit279=Enumerable.Range(0,8).Select(i=>Import278("FractureKit_"+i)).ToArray();
    var rockMesh=kit279[0];
    var smallMesh=rockMesh;
    var rubble=new GameObject("Scree_and_retaining_stones");rubble.transform.SetParent(root.transform);
    var random279=new System.Random(319);
    for(int i=0;i<420;i++){
     float z=(float)random279.NextDouble()*330;var p=RouteAt278(z);float side=i%2==0?-1:1;float offset=side*(3.05f+(float)random279.NextDouble()*1.2f);
     var g=MeshObject278("Fractured_scree_"+i,kit279[i%8],rock,rubble.transform,false);
     var ground=Sample278(p.x+offset,z);float size=.12f+(float)random279.NextDouble()*.43f;g.transform.position=ground-Vector3.up*size*.35f;g.transform.localScale=new Vector3(size,size*.7f,size*1.2f);g.transform.rotation=Quaternion.Euler(i*17,i*43,i*7);
    }
    var wood=new Material(Shader.Find("Universal Render Pipeline/Lit"));wood.SetColor("_BaseColor",new Color(.15f,.12f,.08f));wood.SetFloat("_Smoothness",0);wood=SurveyMaterial(wood,A278+"/Materials/WeatheredWood.mat");
    // Rail only on a few exposed outer bends. It is not an invisible boundary.
    foreach(var span in new[]{new Vector2(42,64),new Vector2(137,160),new Vector2(245,267)})
    for(float z=span.x;z<span.y;z+=3.2f)
    {
     var a=RouteAt278(z)+Vector3.left*3;var b=RouteAt278(Mathf.Min(z+3.2f,span.y))+Vector3.left*3;
     Log278("Rail_post",a-Vector3.up*.1f,a+Vector3.up*1.05f,.10f,wood,root.transform);
     Log278("Handrail",a+Vector3.up*.85f,b+Vector3.up*.85f,.075f,wood,root.transform);
    }
    var inn=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab"),scene);
    inn.name="Lower_terrace_inn";inn.transform.SetParent(root.transform);inn.transform.position=Vector3.zero;
    foreach(var c in inn.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
    foreach(var tr in inn.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Ground")).ToArray())if(tr!=null)Object.DestroyImmediate(tr.gameObject);
    var renderers=inn.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
    inn.transform.localScale=Vector3.one*(13/bounds.size.x);bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
    var innFeet=RouteAt278(10)+Vector3.left*32;inn.transform.position+=new Vector3(innFeet.x-bounds.center.x,innFeet.y-bounds.min.y+.1f,innFeet.z-bounds.center.z);
    var solid=inn.AddComponent<BoxCollider>();bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);solid.center=inn.transform.InverseTransformPoint(bounds.center);solid.size=bounds.size/inn.transform.lossyScale.x;
    foreach(var r in renderers)r.sharedMaterials=r.sharedMaterials.Select(m=>CloneProp278(m,materialCache)).ToArray();
    foreach(var behaviour in inn.GetComponentsInChildren<MonoBehaviour>())Object.DestroyImmediate(behaviour);
    for(int i=0;i<18;i++){var g=MeshObject278("Inn_footing_"+i,smallMesh,rock,root.transform,false);g.transform.position=innFeet+new Vector3(-6+(i%9)*1.5f,-1.15f,(i/9==0?-4:4));g.transform.localScale=new Vector3(.5f,.6f,.5f);}
    var lamp=new GameObject("Warm_inn_light");lamp.transform.SetParent(root.transform);lamp.transform.position=innFeet+new Vector3(4,2,0);var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.64f,.32f);light.intensity=3;light.range=10;
    var sun=new GameObject("Soft_mountain_sun");sun.transform.rotation=Quaternion.Euler(37,-45,0);var sunlight=sun.AddComponent<Light>();sunlight.type=LightType.Directional;sunlight.intensity=1.25f;sunlight.color=new Color(1,.95f,.86f);sunlight.shadows=LightShadows.Soft;
    RenderSettings.sun=sunlight;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.52f,.55f,.56f);RenderSettings.ambientEquatorColor=new Color(.34f,.35f,.33f);RenderSettings.ambientGroundColor=new Color(.18f,.18f,.16f);
    var sky=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/World/PineRestGame/Sky.mat"));sky.SetColor("_Horizon",new Color(.80f,.79f,.73f));sky.SetColor("_Zenith",new Color(.55f,.58f,.59f));sky.SetColor("_Cloud",new Color(.75f,.75f,.71f));RenderSettings.skybox=SurveyMaterial(sky,A278+"/Materials/Sky.mat");RenderSettings.fog=false;
    var mistMaterial=new Material(Shader.Find("Oheangbu/Prototype/ValleyMist278"));mistMaterial=SurveyMaterial(mistMaterial,A278+"/Materials/Mist.mat");
    for(int i=0;i<26;i++)
    {
     var fog=GameObject.CreatePrimitive(PrimitiveType.Quad);fog.name="Valley_mist_"+i;fog.transform.SetParent(root.transform);Object.DestroyImmediate(fog.GetComponent<Collider>());
     fog.transform.position=new Vector3(-110-(i%5)*135,-23-(i%3)*22,-90+(i/5)*145);fog.transform.rotation=Quaternion.Euler(70,0,(i%3-1)*9);fog.transform.localScale=new Vector3(340,175,1);fog.GetComponent<Renderer>().sharedMaterial=mistMaterial;
    }
    var player=new GameObject("Prototype_Walker");var cc=player.AddComponent<CharacterController>();cc.height=1.8f;cc.center=Vector3.up*.9f;cc.radius=.28f;cc.slopeLimit=42;cc.stepOffset=.28f;cc.skinWidth=.035f;
    var explorer=player.AddComponent<CliffAscentExplorer278>();explorer.StartFeet=RouteAt278(4);player.transform.position=explorer.StartFeet+Vector3.up*.12f;player.transform.rotation=Quaternion.Euler(0,Quaternion.LookRotation(RouteAt278(8)-RouteAt278(4)).eulerAngles.y,0);
    var cameraGo=new GameObject("Main Camera");cameraGo.tag="MainCamera";cameraGo.transform.SetParent(player.transform,false);cameraGo.transform.localPosition=Vector3.up*1.64f;var camera=cameraGo.AddComponent<Camera>();camera.nearClipPlane=.08f;camera.farClipPlane=2400;camera.fieldOfView=65;camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;cameraGo.AddComponent<AudioListener>();explorer.View=camera;
    int rendererIndex=Renderer278();var extra=camera.GetUniversalAdditionalCameraData();extra.SetRenderer(rendererIndex);extra.requiresDepthTexture=true;extra.renderPostProcessing=true;extra.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
    var volumeGo=new GameObject("Local_grade");var volume=volumeGo.AddComponent<Volume>();volume.isGlobal=true;volume.priority=5;
    volume.sharedProfile=Grade278();
    Physics.SyncTransforms();
    var nav=root.transform.Find("Trail").gameObject.AddComponent<NavMeshSurface>();nav.collectObjects=CollectObjects.Children;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.center=new Vector3(40,80,165);nav.size=new Vector3(330,300,350);nav.overrideVoxelSize=true;nav.voxelSize=.16f;nav.BuildNavMesh();
    if(nav.navMeshData!=null){var data=Object.Instantiate(nav.navMeshData);nav.RemoveData();var oldNav=AssetDatabase.LoadAssetAtPath<NavMeshData>(A278+"/Navigation.asset");if(oldNav==null)AssetDatabase.CreateAsset(data,A278+"/Navigation.asset");else{EditorUtility.CopySerialized(data,oldNav);Object.DestroyImmediate(data);data=oldNav;EditorUtility.SetDirty(data);}nav.navMeshData=data;nav.AddData();}
    EditorSceneManager.SaveScene(scene,Scene278);AssetDatabase.SaveAssets();
    File.WriteAllText(O278+"/build.txt","Separate scene; route "+RouteData278().length+"m, rise "+RouteData278().rise+"m. Blender rock shelf and connected mesh trail; no campaign/save components. Renderer index "+rendererIndex);
   }
   finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
   return File.ReadAllText(O278+"/build.txt");
  }
  static VolumeProfile Grade278(){
   string path=A278+"/Grade.asset";var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
   if(profile==null){profile=ScriptableObject.CreateInstance<VolumeProfile>();profile.name="CliffAscent278_Grade";AssetDatabase.CreateAsset(profile,path);}
   foreach(var old in profile.components)if(old!=null)Object.DestroyImmediate(old,true);profile.components.Clear();
   var colors=profile.Add<ColorAdjustments>();colors.saturation.Override(-48);colors.contrast.Override(6);colors.postExposure.Override(.25f);colors.colorFilter.Override(Color.white);
   var tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
   AssetDatabase.AddObjectToAsset(colors,profile);AssetDatabase.AddObjectToAsset(tone,profile);EditorUtility.SetDirty(profile);return profile;
  }
  [Serializable] sealed class MeshAscent279 {public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles;public Color[] colors;}
  static Mesh Import278(string name)
  {
   var data=JsonUtility.FromJson<MeshAscent279>(File.ReadAllText(O278+"/"+name+".json"));var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.vertices=data.vertices;mesh.normals=data.normals;mesh.uv=data.uv;mesh.triangles=data.triangles;mesh.colors=data.colors!=null&&data.colors.Length==data.vertices.Length?data.colors:Enumerable.Repeat(Color.white,data.vertices.Length).ToArray();mesh.RecalculateBounds();mesh.RecalculateTangents();return ArtMesh(mesh,A278+"/Meshes/"+name+".asset");
  }
  static GameObject MeshObject278(string name,Mesh mesh,Material material,Transform parent,bool collide)
  {
   var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
   if(collide)go.AddComponent<MeshCollider>().sharedMesh=mesh;return go;
  }
  static Material SurfaceMaterial278(string name,bool rock)
  {
   var m=new Material(Shader.Find("Oheangbu/Prototype/CliffRock278")){name=name,enableInstancing=true};string surface="Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/";
   m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(rock?A278+"/Textures/Granite280.png":surface+"T_Dirt_1_BC.png"));
   m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(surface+(rock?"T_RockGround_1_N.png":"T_Dirt_1_N.png")));
   m.SetColor("_BaseColor",rock?new Color(1.25f,1.21f,1.12f):new Color(1.9f,1.72f,1.42f));m.SetFloat("_RockScale",rock?.4f:.85f);m.SetFloat("_Saturation",.12f);m.SetFloat("_BumpScale",rock?.85f:.65f);m.SetFloat("_JointStrength",0);m.SetFloat("_Granite279",rock?1:0);m.SetFloat("_AmbientFloor",.55f);m.SetFloat("_LightResponse",.85f);m.SetFloat("_WashStart",60);m.SetFloat("_WashEnd",750);m.SetFloat("_WashStrength",.85f);m.SetFloat("_FadeOutStart",2200);m.SetFloat("_FadeOutEnd",2400);return SurveyMaterial(m,A278+"/Materials/"+name+".mat");
  }
  static Material CloneProp278(Material source,Dictionary<Material,Material> cache)
  {
   if(cache.TryGetValue(source,out var result))return result;
   result=new Material(source){shader=Shader.Find("Oheangbu/Prototype/CliffProp278"),enableInstancing=true};result.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):new Color(.22f,.18f,.13f));result.SetFloat("_AmbientFloor",.6f);result.SetFloat("_BumpScale",.35f);result.SetFloat("_Leaf279",source.HasProperty("_AlphaClip")&&source.GetFloat("_AlphaClip")>.5f||source.HasProperty("_BaseMap")&&source.GetTexture("_BaseMap")==null?1:0);result.SetFloat("_Saturation",.15f);result.SetFloat("_WashStart",80);result.SetFloat("_WashEnd",700);result.SetFloat("_WashStrength",.78f);result.SetFloat("_FadeInStart",-1);result.SetFloat("_FadeInEnd",0);result.SetFloat("_FadeOutStart",1600);result.SetFloat("_FadeOutEnd",2000);
   result=SurveyMaterial(result,A278+"/Materials/Prop_"+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source))+".mat");cache.Add(source,result);return result;
  }
  static Vector3 Sample278(float x,float z)
  {
   var hits=Physics.RaycastAll(new Vector3(x,500,z),Vector3.down,800).Where(h=>h.collider.name.StartsWith("Cliff_")||h.collider.name=="Trail").OrderByDescending(h=>h.point.y).ToArray();return hits.Length>0?hits[0].point:new Vector3(x,0,z);
  }
  static void Log278(string name,Vector3 a,Vector3 b,float radius,Material material,Transform parent)
  {
   var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=name;go.transform.SetParent(parent);go.transform.position=(a+b)*.5f;go.transform.up=(b-a).normalized;go.transform.localScale=new Vector3(radius*2,Vector3.Distance(a,b)*.5f,radius*2);go.GetComponent<Renderer>().sharedMaterial=material;
  }
  static int Renderer278()
  {
   string path=A278+"/Renderer.asset";var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);if(data==null){data=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset"));data.rendererFeatures.Clear();data.name="CliffAscent278_Renderer";AssetDatabase.CreateAsset(data,path);}
   int selected=-1;
   foreach(string assetPath in new[]{"Assets/Settings/PC_RPAsset.asset","Assets/Settings/Mobile_RPAsset.asset"}){
    var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);var serialized=new SerializedObject(pipeline);var array=serialized.FindProperty("m_RendererDataList");int index=-1;
    for(int i=0;i<array.arraySize;i++)if(array.GetArrayElementAtIndex(i).objectReferenceValue==data)index=i;
    if(index<0){index=array.arraySize;array.InsertArrayElementAtIndex(index);array.GetArrayElementAtIndex(index).objectReferenceValue=data;serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);}
    if(selected>=0&&selected!=index)throw new Exception("Prototype renderer index must match quality pipelines");selected=index;
   }return selected;
  }
  static string CheckAscent278()
  {
   var scene=SceneManager.GetActiveScene();if(scene.path!=Scene278)throw new Exception("Open prototype first");var roots=scene.GetRootGameObjects();var lines=new List<string>();void C(bool value,string name)=>lines.Add((value?"PASS ":"FAIL ")+name);
   var route=RouteData278();var meshes=roots.SelectMany(g=>g.GetComponentsInChildren<MeshFilter>()).ToArray();var renderers=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).ToArray();
   C(route.length>400&&route.length<500&&route.rise>80,"continuous 400-500m ascent with over 80m rise");
   C(meshes.All(m=>m.sharedMesh!=null)&&renderers.All(r=>r.sharedMaterials.All(m=>m!=null)),"no missing mesh/material references");
   C(renderers.SelectMany(r=>r.sharedMaterials).Distinct().All(m=>m.shader!=null&&m.shader.isSupported&&!ShaderUtil.ShaderHasError(m.shader)),"all scene shaders supported without errors");
   Physics.SyncTransforms();int supported=0,blocked=0;float maxSlope=0;
   for(int i=2;i<route.points.Length-2;i++)
   {
    var p=route.points[i];if(Physics.Raycast(p+Vector3.up*.8f,Vector3.down,out var h,1.6f)&&Mathf.Abs(h.point.y-p.y)<.12f)supported++;
    if(Physics.OverlapCapsule(p+Vector3.up*.42f,p+Vector3.up*1.5f,.26f).Any(c=>!(c is CharacterController)))blocked++;
    var d=route.points[i+1]-p;maxSlope=Mathf.Max(maxSlope,Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg);
   }
   C(supported==route.points.Length-4,"every metre of route has continuous physical ground ("+supported+")");C(blocked==0,"standing capsule clearance along full centreline, blocked="+blocked);C(maxSlope<35,"walking grade max "+maxSlope+"deg");
   var grading=Object.FindFirstObjectByType<Volume>().sharedProfile;C(grading!=null&&grading.components.Count==2&&grading.components.All(c=>c!=null&&AssetDatabase.Contains(c)),"grading profile has persisted components");
   var nav=roots.SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>()).Single();C(nav.navMeshData!=null,"separate baked navigation asset");
   var path=new NavMeshPath();bool navComplete=NavMesh.SamplePosition(route.points[4],out var a,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(route.points[325],out var b,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;C(navComplete,"navigation connects lower inn to upper saddle; status="+path.status+", corners="+path.corners.Length+", triangles="+NavMesh.CalculateTriangulation().indices.Length/3);
   C(!roots.SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>()).Any(m=>m!=null&&(m.GetType().Name.Contains("Campaign")||m.GetType().Name=="WorldDemoSession")),"no campaign or live save connection");
   var facades=meshes.Where(m=>m.name.StartsWith("Cliff_Facade_")).ToArray();
   C(facades.Length==6&&facades.All(m=>m.GetComponent<MeshCollider>()?.sharedMesh==m.sharedMesh),"six fracture facades use matching collision meshes");
   C(facades.All(m=>m.sharedMesh.colors.Length==m.sharedMesh.vertexCount&&m.sharedMesh.colors.Min(c=>c.r)<.95f),"geometric occlusion colour baked on each facade");
   var cover=roots.SelectMany(g=>g.GetComponentsInChildren<LODGroup>()).Where(g=>g.name.StartsWith("Groundcover_packet_")).ToArray();
   C(cover.Length==12&&cover.All(g=>g.GetLODs().Length==2&&g.GetComponentsInChildren<Collider>().Length==0&&g.GetComponentsInChildren<Renderer>().All(r=>r.sharedMaterial.GetFloat("_Billboard")==0)),"12 groundcover packets with rooted mesh LODs, no merged billboards or plant colliders");
   var groves=GameObject.Find("Ledge_groves_279");
   C(groves!=null&&groves.GetComponentsInChildren<LODGroup>().Length>=50&&groves.GetComponentsInChildren<Collider>().Length==0,"ledge plants use source LODs without individual physics");
   C(meshes.All(m=>m.sharedMesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z))),"all render mesh coordinates finite");
   File.WriteAllLines(O278+"/checks.txt",lines);return string.Join("\n",lines);
  }
  [InitializeOnLoadMethod] static void RegisterProbe278(){EditorApplication.playModeStateChanged-=ProbeMode278;EditorApplication.playModeStateChanged+=ProbeMode278;}
  static void ProbeMode278(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Ascent278.Probe",false))BeginWalk278();if(state==PlayModeStateChange.EnteredEditMode)SessionState.SetBool("Ascent278.Probe",false);}
  static GameObject probe278;
  static CharacterController probeBody278,disabledPlayer278;
  static Vector3[] probePoints278;
  static int probeTarget278,probeStall278,probeMoves278;
  static Vector3 probePrevious278;
  static string BeginWalk278()
  {
   if(SceneManager.GetActiveScene().path!=Scene278||probe278!=null)throw new Exception("Open prototype; no other probe may be running");
   disabledPlayer278=Object.FindFirstObjectByType<CliffAscentExplorer278>().GetComponent<CharacterController>();disabledPlayer278.enabled=false;
   probe278=new GameObject("Continuous_physical_probe");probe278.AddComponent<CliffAscentContactProbe278>();probeBody278=probe278.AddComponent<CharacterController>();probeBody278.enabled=false;probeBody278.height=1.8f;probeBody278.center=Vector3.up*.9f;probeBody278.radius=.28f;probeBody278.slopeLimit=42;probeBody278.stepOffset=.28f;probeBody278.skinWidth=.035f;
   probePoints278=RouteData278().points;probe278.transform.position=probePoints278[3]+Vector3.up*.12f;probeBody278.enabled=true;probeTarget278=4;probeStall278=probeMoves278=0;probePrevious278=probe278.transform.position;Physics.SyncTransforms();
   if(EditorApplication.isPlaying){var driver=probe278.GetComponent<CliffAscentContactProbe278>();driver.Points=probePoints278;Application.runInBackground=true;Time.timeScale=4;}
   File.WriteAllText(O278+"/walk-check.txt","RUNNING CharacterController probe; no native input");EditorApplication.update+=TickWalk278;return "Continuous CharacterController probe started";
  }
  static void TickWalk278()
  {
   bool finished=false;string reason="";
   try
   {
    if(probe278==null){finished=true;reason="FAIL interrupted";return;}
    if(EditorApplication.isPlaying){var driver=probe278.GetComponent<CliffAscentContactProbe278>();probeTarget278=driver.Target;probeMoves278=driver.Moves;if(driver.Result!=null){finished=true;reason=driver.Result;}return;}
    for(int k=0;k<8&&probeTarget278<327;k++)
    {
     var p=probe278.transform.position;var d=probePoints278[probeTarget278]-p;d.y=0;
     if(d.magnitude<.14f){probeTarget278++;continue;}
     probeBody278.Move(Vector3.ClampMagnitude(d,.075f)+Vector3.down*.025f);Physics.SyncTransforms();probeMoves278++;
    }
    var delta=probe278.transform.position-probePrevious278;delta.y=0;probeStall278=delta.magnitude<.002f?probeStall278+1:0;probePrevious278=probe278.transform.position;
    if(probeTarget278>=327){finished=true;reason="PASS";}else if(probeStall278>100){finished=true;reason="FAIL stuck";}
   }
   catch(Exception e){finished=true;reason="FAIL "+e.Message;}
   finally
   {
    if(finished){EditorApplication.update-=TickWalk278;File.WriteAllText(O278+"/walk-check.txt",reason+" continuous CharacterController probe; Play="+EditorApplication.isPlaying+"; target="+probeTarget278+"/327, moves="+probeMoves278+", feet="+(probe278!=null?probe278.transform.position.ToString():"missing")+"; collision="+(probe278!=null?string.Join(";",probe278.GetComponent<CliffAscentContactProbe278>().HitNames):"none")+". Not manual input or frame-time measurement.");if(probe278!=null)Object.DestroyImmediate(probe278);probe278=null;if(disabledPlayer278!=null)disabledPlayer278.enabled=true;if(SessionState.GetBool("Ascent278.Probe",false))EditorApplication.delayCall+=()=>EditorApplication.isPlaying=false;}
   }
  }
  static string CaptureAscent278(string scenePath=Scene278,string output=O278,Vector3[] routeOverride=null)
  {
   if(SceneManager.GetActiveScene().path!=scenePath)throw new Exception("Open prototype first");
   var source=Object.FindFirstObjectByType<CliffAscentExplorer278>().View;var g=new GameObject("Capture278");var camera=g.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.useOcclusionCulling=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   Func<float,Vector3> at=z=>{if(routeOverride==null)return RouteAt278(z);int i=Mathf.Clamp(Mathf.FloorToInt(z),0,routeOverride.Length-2);return Vector3.Lerp(routeOverride[i],routeOverride[i+1],Mathf.Clamp01(z-i));};
   var p=at(55);var q=at(175);var top=at(305);
   var eyes=new[]{new Vector3(-108,104,-28),p+new Vector3(-.5f,1.65f,0),q+new Vector3(-.4f,1.65f,0),top+new Vector3(-.6f,1.65f,0),at(1)+new Vector3(-3,4,-4)};
   var targets=new[]{new Vector3(20,92,166),at(78)+Vector3.up*4,at(200)+Vector3.up*4,at(80),at(10)+new Vector3(-24,3,0)};
   if(routeOverride!=null){eyes[4]=at(1)+new Vector3(-2,6,-12);targets[4]=at(10)+new Vector3(-26,3,0);}
   var rt=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;bool priorAsync=ShaderUtil.allowAsyncCompilation;
   try{ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=63;for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(output+"/view-"+i+".png",texture.EncodeToPNG());}}
   finally{ShaderUtil.allowAsyncCompilation=priorAsync;camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(g);}return "Five 1080p Unity offscreen views";
  }
 }
}
