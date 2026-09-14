using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.Prologue
{
 public static class PrologueBuilder
 {
  public const string ScenePath="Assets/_Project/Scenes/World/W_Cheongrim_Prologue.unity";
  public const string Folder="Assets/_Project/Art/World/Prologue";
  public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/Prologue"));
  public static Vector3[] Main,Branch;
  public static void Set(Object o,string name,object value){var f=o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);if(f==null)throw new Exception(o.GetType()+"."+name);f.SetValue(o,value);EditorUtility.SetDirty(o);}
  static T Asset<T>(T value,string name)where T:Object
  {string p=Folder+"/"+name;var old=AssetDatabase.LoadAssetAtPath<T>(p);if(old==null){AssetDatabase.CreateAsset(value,p);return value;}
   if(old is Mesh existing && value is Mesh incoming){existing.Clear();existing.indexFormat=incoming.indexFormat;existing.vertices=incoming.vertices;existing.normals=incoming.normals;existing.uv=incoming.uv;existing.triangles=incoming.triangles;existing.bounds=incoming.bounds;existing.UploadMeshData(false);}
   else if(old is Texture2D texture && value is Texture2D input){texture.Reinitialize(input.width,input.height,input.format,false);texture.LoadRawTextureData(input.GetRawTextureData<byte>());texture.Apply();}
   else EditorUtility.CopySerialized(value,old);Object.DestroyImmediate(value);EditorUtility.SetDirty(old);return old;}
  static Vector3[] Curve(Vector3[] knots)
  {
   var points=new List<Vector3>();for(int i=0;i<knots.Length-1;i++)for(int k=0;k<16;k++){
    float t=k/16f;Vector3 a=knots[Mathf.Max(0,i-1)],b=knots[i],c=knots[i+1],d=knots[Mathf.Min(knots.Length-1,i+2)];
    points.Add(.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));}
   points.Add(knots[knots.Length-1]);return points.ToArray();
  }
  public static void Layout()
  {
   Main=Curve(new[]{new Vector3(0,0,0),new Vector3(0,0,10),new Vector3(25,0,18),new Vector3(66,2,28),new Vector3(101,5,70),new Vector3(95,8,133),new Vector3(58,11,192),new Vector3(-9,12,220),new Vector3(-75,9,182),new Vector3(-112,5,121),new Vector3(-100,1,65),new Vector3(-72,0,47)});
   Branch=Curve(new[]{new Vector3(100,5,84),new Vector3(77,6,98),new Vector3(69,9,140),new Vector3(81,10,160),new Vector3(92,9,151)});
  }
  public static float Distance(Vector2 q,Vector3[] line,out float y)
  {
   float best=float.MaxValue;y=0;for(int i=1;i<line.Length;i++){
    var a=new Vector2(line[i-1].x,line[i-1].z);var b=new Vector2(line[i].x,line[i].z);var v=b-a;
    float t=Mathf.Clamp01(Vector2.Dot(q-a,v)/Mathf.Max(v.sqrMagnitude,.00001f));float dist=(q-a-v*t).sqrMagnitude;
    if(dist<best){best=dist;y=Mathf.Lerp(line[i-1].y,line[i].y,t);}}
   return Mathf.Sqrt(best);
  }
  public static float Height(float x,float z)
  {
   if(Main==null)Layout();
   float back=22*Mathf.SmoothStep(0,1,(12-z)/40)*Mathf.SmoothStep(0,1,(Mathf.Abs(x)-6)/10)*(.8f+.4f*Mathf.PerlinNoise(x*.035f+10,z*.027f+8));
   if(z<0)return -.03f+back;
   var q=new Vector2(x,z);float dm=Distance(q,Main,out float ym),db=Distance(q,Branch,out float yb);
   float d=dm<db?dm:db,y=dm<db?ym:yb;
   float e=Mathf.Pow(x/52,2)+Mathf.Pow((z-105)/64,2);
   float hill=64*Mathf.Exp(-Mathf.Pow(e,2.5f));
   float rim=32*Mathf.SmoothStep(0,1,Mathf.Max((Mathf.Abs(x)-118)/20,(z-231)/15))*(.65f+.6f*Mathf.PerlinNoise(x*.04f+12,z*.04f+4));
   float spur=38*Mathf.Exp(-Mathf.Pow((x+34)/12,2)-Mathf.Pow((z-26)/46,4));
   float baseY=2+Mathf.PerlinNoise(x*.023f+6,z*.023f)*5+hill*(.8f+.25f*Mathf.PerlinNoise(x*.057f,z*.059f))+rim+spur+back;
   float h=Mathf.Lerp(y,baseY,Mathf.SmoothStep(0,1,(d-4.2f)/9));
   float inn=Vector2.Distance(q,new Vector2(-72,38));h=Mathf.Lerp(0,h,Mathf.SmoothStep(0,1,(inn-16)/10));
   float entry=Vector2.Distance(q,Vector2.zero);return Mathf.Lerp(0,h,Mathf.SmoothStep(0,1,(entry-7)/9));
  }
  static Vector3 Ground(float x,float z,float lift=0)=>new Vector3(x,Height(x,z)+lift,z);
  [MenuItem("Oheangbu/World/Prologue/Build separate playable scene")]
  public static void Menu()=>Debug.Log(Build());
  public static string Build(bool rebuildPrototype=false)
  {
   if(EditorApplication.isPlaying)throw new Exception("Exit Play before build");
   if(File.Exists(ScenePath)&&!rebuildPrototype)throw new Exception("Production scene exists. Edit it in place; full regeneration is disabled. Explicit RebuildPrototype archives it before replacing.");
   Directory.CreateDirectory(Folder);Directory.CreateDirectory(Output);Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
   Layout();var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
   if(File.Exists(ScenePath)){Directory.CreateDirectory(Output+"/Backups");File.Copy(ScenePath,Output+"/Backups/Prologue_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity");}
   if(current.path==CodexWorldSceneBuilder.ScenePath)EditorSceneManager.SaveScene(current,ScenePath,true);
   else {if(current.isDirty)EditorSceneManager.SaveScene(current,Output+"/BeforeBuild_"+DateTime.Now.ToString("HHmmss")+".unity",true);File.Copy(CodexWorldSceneBuilder.ScenePath,ScenePath,true);AssetDatabase.ImportAsset(ScenePath);}
   var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
   var world=scene.GetRootGameObjects().First(x=>x.name.StartsWith("CodexWorld"));
   world.name="Cheongrim_Prologue_Environment";
   var valley=GameObject.Find("Valley");var previous=valley.GetComponent<MeshRenderer>().sharedMaterial;
   var mesh=BuildGround();var ground=Asset(mesh,"Ground.asset");valley.GetComponent<MeshFilter>().sharedMesh=ground;valley.GetComponent<MeshCollider>().sharedMesh=ground;
   var groundMat=new Material(previous){name="Prologue Ground"};var mask=BuildMask();groundMat.SetTexture("_GroundPathMask",Asset(mask,"PathMask.asset"));
   groundMat.SetVector("_GroundPathRect",new Vector4(-140,-35,1/280f,1/290f));groundMat.SetFloat("_GroundPath",1);
   valley.GetComponent<MeshRenderer>().sharedMaterial=Asset(groundMat,"Ground.mat");
   foreach(string groupName in new[]{"02_Granite_Escarpments","03_Windswept_Pines","06_Dry_Grass_and_Path_Edges"}){
    var group=GameObject.Find(groupName);if(group==null)continue;
    foreach(Transform t in group.transform){Vector3 p=t.position;if(p.z<5)continue;float d=Distance(new Vector2(p.x,p.z),Main,out _);if(d<6){t.gameObject.SetActive(false);continue;}p.y+=Height(p.x,p.z)-CodexWorldGeometry.Height(p.x,p.z);t.position=p;}
   }
   var inn=GameObject.Find("Thatched_Inn");var innGroup=GameObject.Find("04_Mountain_Inn");
   if(inn!=null){var delta=new Vector3(-72,0,38)-inn.transform.position;if(innGroup!=null&&inn.transform.IsChildOf(innGroup.transform))innGroup.transform.position+=delta;else inn.transform.position+=delta;}
   foreach(var t in world.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Inn_Flat_Approach")||t.name.StartsWith("Inn_Approach_")).ToArray())Object.DestroyImmediate(t.gameObject);
   // Old C2 bedrock was fitted to a different valley. Remove only intersecting copies.
   Physics.SyncTransforms();
   var rocks=GameObject.Find("02_Granite_Escarpments");
   if(rocks!=null)foreach(Transform t in rocks.transform){
    if(!t.gameObject.activeSelf)continue;
    bool blocked=t.GetComponentsInChildren<Collider>().Any(c=>Main.Concat(Branch).Any(p=>c.bounds.Intersects(new Bounds(new Vector3(p.x,Height(p.x,p.z)+1,p.z),new Vector3(3,1.8f,3)))));
    if(blocked)t.gameObject.SetActive(false);
   }
   var contentRoot=new GameObject("Prologue_Content");
   Material wood=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/Wood.mat");
   if(wood==null)wood=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/Mine.mat");
   Material stone=AssetDatabase.LoadAssetAtPath<Material>(CodexWorldSceneBuilder.AssetFolder+"/Materials/PaleStone.mat");
   // Keep the existing cave construction; props have diegetic purposes, never route arrows.
   Cube("Broken_Mine_Beam",new Vector3(1.8f,.4f,-16),new Vector3(.35f,.35f,3),wood,contentRoot.transform).transform.rotation=Quaternion.Euler(0,25,17);
   Cube("Investigation_Rubble",new Vector3(2,.15f,-14),new Vector3(1.4f,.3f,1),stone,contentRoot.transform);
   Cube("Lost_Worker_Satchel",Ground(70,133,.2f),new Vector3(.55f,.4f,.4f),wood,contentRoot.transform);
   Cube("Shrine_Stone",new Vector3(-3,.4f,-27),new Vector3(.6f,.8f,.5f),stone,contentRoot.transform);
   // A former timber work platform, high above the walking path, previews a return.
   Cube("Preview_High_Shelf",Ground(-103,112,7),new Vector3(3,.18f,2),wood,contentRoot.transform);
   Cube("Preview_Shelf_Support",Ground(-103,112,3.5f),new Vector3(.22f,7,.22f),wood,contentRoot.transform);
   var content=ScriptableObject.CreateInstance<PrologueContentSO>();content.StartPosition=new Vector3(0,1.1f,-29);content.MainPath=Main;content.BranchPath=Branch;
   content.Points=new[]{
    Point("MineStart",PrologueInteractionKind.Rest,new Vector3(-2,0,-27),"성황당 곁에서 숨 고르기","",0),
    Point("BlastEvidence",PrologueInteractionKind.Evidence,new Vector3(2,0,-14),"부서진 지지목 조사","돌이 안쪽으로 흩어졌다. 지지목에는 불탄 줄의 흔적이 남아 있다.",0),
    Point("WorkerSatchel",PrologueInteractionKind.Currency,Ground(70,133),"작업자의 소지품 살피기","해진 주머니에 조선통보가 남아 있다.",24),
    Point("SideEvidence",PrologueInteractionKind.Evidence,Ground(75,117),"바위 틈의 흔적 조사","같은 매듭의 끈이 여기에도 걸려 있다. 누군가 이 길로 광산을 드나들었다.",0),
    Point("InnRest",PrologueInteractionKind.Rest,new Vector3(-71,0,47),"금표 주막에서 쉬기","",0),
    Point("Logger",PrologueInteractionKind.Conversation,new Vector3(-78,0,47),"벌목꾼과 이야기","벌목꾼: 산 너머 벌목 마을이 비었소. 금표 비석을 지나면 길목 역참이 나올 게요.",0),
    Point("Herbalist",PrologueInteractionKind.Conversation,new Vector3(-65,0,44),"약초꾼과 이야기","약초꾼: 광산 쪽에서 왔소? 오늘은 더 깊이 들어가지 마시오. 숲의 뿌리가 길을 삼키고 있소.",0),
    Point("GukPreview",PrologueInteractionKind.Preview,Ground(-100,123),"절벽 위 살피기","갈라진 절벽 위로 오래된 길이 이어져 있다. 지금은 닿을 수 없다.",0)};
   content=Asset(content,"Content.asset");
   var session=contentRoot.AddComponent<PrologueSession>();session.Content=content;
   var player=Object.FindFirstObjectByType<PlayerMotor>().transform;session.Player=player;player.position=content.StartPosition;
   var interaction=contentRoot.AddComponent<PrologueInteraction>();interaction.Session=session;
   foreach(var p in content.Points.Where(p=>p.Kind==PrologueInteractionKind.Conversation)){
    var npc=GameObject.CreatePrimitive(PrimitiveType.Capsule);npc.name=p.Id+"_TEMP_NPC";npc.transform.SetParent(contentRoot.transform);npc.transform.position=p.Position+Vector3.up;npc.GetComponent<Renderer>().sharedMaterial=stone;}
   var config=Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatConfigSO>(DevSceneKit.DefaultConfigPath));config.name="Prologue Combat";
   config=Asset(config,"Combat.asset");
   var enemies=new List<PrologueEncounter>();
   for(int i=0;i<2;i++){
    Vector3 pos=i==0?new Vector3(0,1,-8):Ground(24,18,1);
    var enemy=DevSceneKit.CreateEnemy(i==0?"CorruptedBeast_Melee_TEMP":"CorruptedBeast_Fire_TEMP",pos,config,Element.Fire,player,player.GetComponent<PlayerVitals>(),true);
    enemy.transform.SetParent(contentRoot.transform);enemy.layer=2;
    var ec=enemy.GetComponent<EnemyController>();Set(ec,"_attackMode",i==0?EnemyController.AttackMode.MeleeOnly:EnemyController.AttackMode.RangedOnly);Set(ec,"_environmentOcclusion",true);
    var agent=enemy.AddComponent<NavMeshAgent>();agent.radius=.4f;agent.height=2;agent.baseOffset=1;agent.angularSpeed=180;agent.acceleration=8;
    var actor=enemy.AddComponent<PrologueEncounter>();actor.Id="Beast_"+i;actor.Session=session;actor.Player=player;actor.Ranged=i==1;actor.PatrolPoints=new[]{pos,pos+Vector3.forward*3};enemies.Add(actor);
   }
   session.Encounters=enemies.ToArray();var wiring=Object.FindFirstObjectByType<CombatLoopWiring>();session.Wiring=wiring;Set(wiring,"_enemies",enemies.Select(e=>e.GetComponent<EnemyVitals>()).ToArray());
   Set(Object.FindFirstObjectByType<CombatLifetimeScope>(),"_prologue",session);
   SetupSky();SetupArea(content);
   var nav=contentRoot.AddComponent<NavMeshSurface>();nav.collectObjects=CollectObjects.All;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.layerMask=1;nav.overrideVoxelSize=true;nav.voxelSize=.25f;nav.BuildNavMesh();
   if(nav.navMeshData!=null){nav.RemoveData();nav.navMeshData=Asset(Object.Instantiate(nav.navMeshData),"Navigation.asset");nav.AddData();}
   EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
   var scenes=EditorBuildSettings.scenes.ToList();if(!scenes.Any(s=>s.path==ScenePath)){scenes.Add(new EditorBuildSettingsScene(ScenePath,true));EditorBuildSettings.scenes=scenes.ToArray();}
   var length=Main.Zip(Main.Skip(1),(a,b)=>Vector3.Distance(a,b)).Sum();
   File.WriteAllText(Output+"/layout.json",JsonUtility.ToJson(content,true));
   return "Built "+ScenePath+"; outdoor path="+length.ToString("F1")+"m, "+(length/4.5f).ToString("F1")+"s; "+ground.triangles.Length/3+" ground triangles; nav="+(nav.navMeshData!=null);
  }
  static PrologueContentSO.Point Point(string id,PrologueInteractionKind kind,Vector3 p,string prompt,string text,int reward)=>new PrologueContentSO.Point{Id=id,Kind=kind,Position=p,Prompt=prompt,Text=text,Currency=reward};
  static GameObject Cube(string name,Vector3 p,Vector3 size,Material mat,Transform parent){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent);go.transform.position=p;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;}
  public static Mesh BuildGround()
  {
   const int n=224;var v=new Vector3[(n+1)*(n+1)];var uv=new Vector2[v.Length];var tri=new int[n*n*6];int k=0;
   for(int z=0;z<=n;z++)for(int x=0;x<=n;x++){float px=-140+x*280f/n,pz=-35+z*290f/n;int i=z*(n+1)+x;v[i]=Ground(px,pz);uv[i]=new Vector2(px,pz)*.1f;
    if(x<n&&z<n){tri[k++]=i;tri[k++]=i+n+1;tri[k++]=i+1;tri[k++]=i+1;tri[k++]=i+n+1;tri[k++]=i+n+2;}}
   var mesh=new Mesh{name="Prologue shared surface",indexFormat=IndexFormat.UInt32};mesh.vertices=v;mesh.uv=uv;mesh.triangles=tri;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  static Texture2D BuildMask(){const int n=512;var t=new Texture2D(n,n,TextureFormat.R8,false,true);var bytes=new byte[n*n];for(int y=0;y<n;y++)for(int x=0;x<n;x++){var q=new Vector2(-140+x*280f/(n-1),-35+y*290f/(n-1));float d=Mathf.Min(Distance(q,Main,out _),Distance(q,Branch,out _));float edge=.35f*Mathf.PerlinNoise(q.x*.3f,q.y*.3f);bytes[y*n+x]=(byte)(255*(1-Mathf.SmoothStep(0,1,(d-2.2f-edge)/1.2f)));}t.SetPixelData(bytes,0);t.Apply();t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;return t;}
  static void SetupSky()
  {
   var profile=Asset(ScriptableObject.CreateInstance<InkSkyProfile>(),"SkyProfile.asset");var sky=new Material(Shader.Find("Oheangbu/Ink Cloud Sky"));profile.Apply(sky);sky=Asset(sky,"Sky.mat");
   var driver=Object.FindFirstObjectByType<WorldLookDriver>();Set(driver,"_useSkybox",true);Set(driver,"_skyboxMaterial",sky);Set(driver,"_inkSkyProfile",profile);driver.Apply();RenderSettings.fog=false;
   var vp=ScriptableObject.CreateInstance<VolumeProfile>();vp=Asset(vp,"Post.asset");
   var bloom=vp.Add<Bloom>(true);bloom.intensity.Override(.035f);bloom.threshold.Override(1.2f);AssetDatabase.AddObjectToAsset(bloom,vp);
   var grading=vp.Add<ColorAdjustments>(true);grading.postExposure.Override(0);grading.contrast.Override(2);grading.saturation.Override(0);AssetDatabase.AddObjectToAsset(grading,vp);
   foreach(var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))if(volume.name.Contains("Codex_NoBloom")){volume.name="Prologue_Subtle_Post";volume.sharedProfile=vp;}
   var camera=Camera.main;var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=AntialiasingQuality.High;
   EditorUtility.SetDirty(vp);
  }
  static void SetupArea(PrologueContentSO content)
  {
   var sheet=ScriptableObject.CreateInstance<AreaSheetSO>();Set(sheet,"areaId","Cheongrim_Prologue");Set(sheet,"scenePath",ScenePath);
   Set(sheet,"paths",new[]{new AreaSheetSO.PathSpec{id="Main",knots=Main},new AreaSheetSO.PathSpec{id="ExplorationLoop",knots=Branch,rewardSlot="WorkerSatchel"}});
   Set(sheet,"pois",content.Points.Select(p=>new AreaSheetSO.PoiSpec{id=p.Id,position=p.Position}).ToArray());
   Set(sheet,"checkpoints",content.Points.Where(p=>p.Kind==PrologueInteractionKind.Rest).Select(p=>new AreaSheetSO.CheckpointSpec{id=p.Id,poiId=p.Id}).ToArray());
   var pois=sheet.Pois.ToList();pois.Add(new AreaSheetSO.PoiSpec{id="MineExit",kind=PoiKind.MineExit,position=Ground(0,10),yaw=70});
   pois.Add(new AreaSheetSO.PoiSpec{id="InnReveal",kind=PoiKind.Vista,position=Ground(-100,65),yaw=125});
   pois.Add(new AreaSheetSO.PoiSpec{id="Next_GeumpyoRoad_RESERVED",kind=PoiKind.Junction,position=Ground(-88,35),yaw=260});Set(sheet,"pois",pois.ToArray());
   Set(sheet,"gates",new[]{new AreaSheetSO.GateSpec{id="Guk_CliffTop",position=Ground(-103,112,7),stepHeight=7,mode=GateMode.Preview}});
   Set(sheet,"walkTargets",new[]{new AreaSheetSO.WalkTarget{from="MineExit",to="InnRest",minutes=2.5f,tolerance=.5f}});
   Set(sheet,"attraction",new[]{new AreaSheetSO.AttractionLink{fromPoi="MineStart",toPoi="BlastEvidence",source=AttractionSource.Vein,layer=AttractionLayer.Near},new AreaSheetSO.AttractionLink{fromPoi="MineExit",toPoi="InnReveal",source=AttractionSource.Silhouette,layer=AttractionLayer.Far},new AreaSheetSO.AttractionLink{fromPoi="InnReveal",toPoi="InnRest",source=AttractionSource.Lantern,layer=AttractionLayer.Near}});
   Set(sheet,"sightlines",new[]{new AreaSheetSO.SightlineSpec{fromPoi="InnReveal",target="Thatched_Inn",yaw=125,minAngularDeg=5},new AreaSheetSO.SightlineSpec{fromPoi="GukPreview",target="Preview_High_Shelf",yaw=195,minAngularDeg=1}});
   Asset(sheet,"AreaSheet.asset");
  }
 }
}
