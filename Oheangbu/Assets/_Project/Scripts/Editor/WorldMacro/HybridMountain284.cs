using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string A284="Assets/_Project/Art/World/HybridMountain284";
  const string O284="../Art/World/Compact/Rebuild/Hybrid284";
  public const string Scene284=A284+"/W_Cheongrim_HybridMountain.unity";
  static readonly Vector3 Origin284=new Vector3(-320,-155,-70), Size284=new Vector3(768,400,512);
  static readonly float[] CrossX284={-320,-160,-80,-40,-15,-7,-3.8f,3.8f,7,12,25,50,85,120,180,300};
  static readonly float[] CrossY284={-145,-130,-105,-60,-24,-6,0,0,8,30,45,70,80,65,28,-10};
  static Vector3 At284(Vector3[] route,float z){int i=Mathf.Clamp(Mathf.FloorToInt(z),0,route.Length-2);return Vector3.Lerp(route[i],route[i+1],Mathf.Clamp01(z-i));}
  static float NaturalHeight284(float x,float z)
  {
   float axis=102+.17f*z+18*Mathf.Sin(z*.013f+.6f);
   float summit=165+70*Mathf.Exp(-Mathf.Pow((z-35)/68,2))+128*Mathf.Exp(-Mathf.Pow((z-224)/76,2))+95*Mathf.Exp(-Mathf.Pow((z-343)/42,2));
   float mountain=-100+summit*Mathf.Exp(-Mathf.Pow(Mathf.Abs((x-axis)/155),1.6f));
   float drainage=0;
   foreach(float az in new[]{38f,131f,243f,353f}){
    float line=az+(x-axis)*(.17f+.08f*Mathf.Sin(az));
    drainage+=Mathf.Exp(-Mathf.Pow((z-line)/(7+Mathf.Abs(x-axis)*.024f),2));
   }
   mountain-=drainage*22*Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,55,Mathf.Abs(x-axis)));
   mountain+=(Mathf.PerlinNoise(x*.021f+32,z*.014f+11)-.5f)*8;
   mountain+=(Mathf.PerlinNoise(x*.086f+4,z*.075f+27)-.5f)*2.4f;
   return mountain;
  }
  static Vector3[] RouteData284()
  {
   var points=new Vector3[331];
   for(int z=0;z<points.Length;z++){
    float y=22+.265f*z+2*Mathf.Sin(z*.04f);
    float lo=-240,hi=102+.17f*z+18*Mathf.Sin(z*.013f+.6f);
    for(int j=0;j<28;j++){float mid=(lo+hi)*.5f;if(NaturalHeight284(mid,z)<y)lo=mid;else hi=mid;}
    points[z]=new Vector3((lo+hi)*.5f,y,z);
   }
   // Remove tiny noise-driven bends; follow terrain contours rather than an arbitrary zigzag.
   var smooth=(Vector3[])points.Clone();
   for(int i=0;i<points.Length;i++){float sum=0;for(int k=-5;k<=5;k++)sum+=points[Mathf.Clamp(i+k,0,330)].x;smooth[i].x=sum/11;}
   return smooth;
  }
  static float Height284(Vector3[] route,float x,float z)
  {
   var p=At284(route,z);float d=x-p.x;
   float natural=NaturalHeight284(x,z);
   float road=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(3.4f,9.5f,Mathf.Abs(d)));
   road*=1-Mathf.SmoothStep(0,1,Mathf.Max(-z,z-330)/14);
   float height=Mathf.Lerp(natural,p.y,road);
   var inn=At284(route,10)+Vector3.left*26;
   float terrace=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(17,28,Vector2.Distance(new Vector2(x,z),new Vector2(inn.x,inn.z))));
   float keepTrail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(4.5f,10,Mathf.Abs(d)));
   return Mathf.Lerp(height,inn.y,terrace*keepTrail);
  }
  [MenuItem("Oheangbu/별도 맵/Terrain 산길 284 열기 (이 씬에서 Play)")]
  public static void OpenTerrain284()=>Terrain284("open");
  public static string Terrain284(string command)
  {
   Directory.CreateDirectory(O284);
   if(command=="tidy"){
    if(SceneManager.GetActiveScene().path!=Scene284||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Hybrid scene in Edit required");
    TidyHybrid284();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());return "Woodland LOD thresholds and inn footing contacts adjusted";
   }
   if(command=="capture")return CaptureAscent278(Scene284,O284,RouteData284());
   if(command=="check")return CheckTerrain284();
   if(command=="walk-play"){
    if(SceneManager.GetActiveScene().path!=Scene284||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Terrain284 Edit scene required");
    SessionState.SetBool("Terrain284.Probe",true);CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "Terrain CharacterController Play probe scheduled";
   }
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Save current changes first; clean Edit required");
   if(command=="open"){EditorSceneManager.OpenScene(Scene284);CompactLoadingStartup270.UseCurrent();return Scene284;}
   if(command!="build")throw new ArgumentException(command);
   DevSceneKit.EnsureFolder(A284);
   EditorSceneManager.OpenScene(Scene283);
   EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Scene284,true);
   EditorSceneManager.OpenScene(Scene284);
   var root=GameObject.Find("Cliff_Ascent_278");
   foreach(Transform child in root.transform.Cast<Transform>().ToArray())
    if(child.name.StartsWith("Cliff_")||child.name=="Trail"||child.name.StartsWith("Groundcover_packet_")||child.name=="Terrain283_Navigation")Object.DestroyImmediate(child.gameObject);
   var route=RouteData284();
   float routeLength=0;for(int i=1;i<route.Length;i++)routeLength+=Vector3.Distance(route[i-1],route[i]);
   File.WriteAllText(O284+"/route.json",JsonUtility.ToJson(new Route278{points=route,length=routeLength,rise=route[330].y-route[0].y},true));
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(A284+"/Mountain.asset");
   if(data==null){data=new TerrainData();AssetDatabase.CreateAsset(data,A284+"/Mountain.asset");}
   data.heightmapResolution=1025;data.size=Size284;data.alphamapResolution=1024;data.baseMapResolution=1024;
   var heights=new float[1025,1025];
   for(int z=0;z<1025;z++)for(int x=0;x<1025;x++)heights[z,x]=Mathf.Clamp01((Height284(route,Origin284.x+x/1024f*Size284.x,Origin284.z+z/1024f*Size284.z)-Origin284.y)/Size284.y);
   data.SetHeights(0,0,heights);
   data.terrainLayers=new[]{Layer284("Rock",A278+"/Textures/Granite280.png","T_RockGround_1_N.png",2.5f),Layer284("Dirt","Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/T_Dirt_1_BC.png","T_Dirt_1_N.png",3)};
   var splat=new float[1024,1024,2];
   for(int z=0;z<1024;z++)for(int x=0;x<1024;x++){
    float u=x/1023f,v=z/1023f,wx=Origin284.x+u*Size284.x,wz=Origin284.z+v*Size284.z;
    float d=Mathf.Abs(wx-At284(route,wz).x);
    float road=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.7f,4.1f,d)))*(wz>=-2&&wz<=334?1:0);
    float soil=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(16,42,data.GetSteepness(u,v))))*.7f;
    var inn=At284(route,10)+Vector3.left*26;
    float yard=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,18,Vector2.Distance(new Vector2(wx,wz),new Vector2(inn.x,inn.z))));
    float dirt=Mathf.Max(road,soil,yard);splat[z,x,0]=1-dirt;splat[z,x,1]=dirt;
   }
   data.SetAlphamaps(0,0,splat);EditorUtility.SetDirty(data);
   var go=UnityEngine.Terrain.CreateTerrainGameObject(data);go.name="Cliff_Terrain284";go.transform.SetParent(root.transform);go.transform.position=Origin284;
   var terrain=go.GetComponent<Terrain>();terrain.drawInstanced=true;terrain.heightmapPixelError=3;terrain.basemapDistance=5000;
   var material=new Material(Shader.Find("Oheangbu/Prototype/HybridTerrain284")){name="NativeTerrain284"};terrain.materialTemplate=SurveyMaterial(material,A284+"/Terrain.mat");
   foreach(var group in root.GetComponentsInChildren<LODGroup>()){
    var p=group.transform.position;p.y=terrain.SampleHeight(p)+Origin284.y-.08f;group.transform.position=p;
   }
   var scree=root.transform.Find("Scree_and_retaining_stones");if(scree!=null)foreach(Transform t in scree){var p=t.position;p.y=terrain.SampleHeight(p)+Origin284.y-t.lossyScale.y*.25f;t.position=p;}
   var oldInnFeet=RouteAt278(10)+Vector3.left*32;var newInnFeet=At284(route,10)+Vector3.left*26;var innShift=newInnFeet-oldInnFeet;
   foreach(Transform child in root.transform.Cast<Transform>().ToArray()){
    if(child.name=="Lower_terrace_inn"||child.name.StartsWith("Inn_footing_")||child.name=="Warm_inn_light")child.position+=innShift;
    if(child.name=="Rail_post"||child.name=="Handrail")Object.DestroyImmediate(child.gameObject);
   }
   var railMat=new Material(AssetDatabase.LoadAssetAtPath<Material>(A278+"/Materials/WeatheredWood.mat"));railMat=SurveyMaterial(railMat,A284+"/Rail.mat");
   foreach(var span in new[]{new Vector2(42,64),new Vector2(137,160),new Vector2(245,267)})for(float z=span.x;z<span.y;z+=3.2f){
    var a=At284(route,z)+Vector3.left*3;var b=At284(route,Mathf.Min(z+3.2f,span.y))+Vector3.left*3;
    Log278("Rail_post",a-Vector3.up*.1f,a+Vector3.up*1.05f,.10f,railMat,root.transform);
    Log278("Handrail",a+Vector3.up*.85f,b+Vector3.up*.85f,.075f,railMat,root.transform);
   }
   DressHybrid284(root.transform,terrain,route);
   TidyHybrid284();
   RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.65f,.67f,.65f);RenderSettings.fogStartDistance=160;RenderSettings.fogEndDistance=1250;
   var navObject=new GameObject("Terrain284_Navigation");navObject.transform.SetParent(root.transform);
   var nav=navObject.AddComponent<NavMeshSurface>();nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.center=new Vector3(40,80,165);nav.size=new Vector3(250,260,350);nav.overrideVoxelSize=true;nav.voxelSize=.2f;
   Physics.SyncTransforms();nav.BuildNavMesh();
   if(nav.navMeshData!=null){var copy=Object.Instantiate(nav.navMeshData);nav.RemoveData();var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(A284+"/Navigation.asset");if(old==null)AssetDatabase.CreateAsset(copy,A284+"/Navigation.asset");else{EditorUtility.CopySerialized(copy,old);Object.DestroyImmediate(copy);copy=old;EditorUtility.SetDirty(old);}nav.navMeshData=copy;nav.AddData();}
   var explorer=Object.FindFirstObjectByType<CliffAscentExplorer278>();explorer.StartFeet=At284(route,4);explorer.transform.position=explorer.StartFeet+Vector3.up*.12f;
   LocalizeShared284(root.transform);foreach(var guid in AssetDatabase.FindAssets("",new[]{A284})){var asset=AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));if(asset!=null)AssetDatabase.SaveAssetIfDirty(asset);}EditorSceneManager.SaveScene(SceneManager.GetActiveScene());CompactLoadingStartup270.UseCurrent();
   return "Independent native Terrain scene saved; 1025 heightmap, 1024 splat, road painted directly on Terrain";
  }
  static TerrainLayer Layer284(string name,string diffuse,string normal,float scale)
  {
   var path=A284+"/"+name+".terrainlayer";var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
   if(layer==null){layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,path);}
   // Terrain Lit treats an opaque diffuse alpha channel as polished smoothness.
   // Copy import settings locally, leaving the shared source and its pixels untouched.
   string local=A284+"/"+name+"Diffuse"+Path.GetExtension(diffuse);
   if(AssetDatabase.LoadAssetAtPath<Texture2D>(local)==null)AssetDatabase.CopyAsset(diffuse,local);
   var importer=(TextureImporter)AssetImporter.GetAtPath(local);
   if(importer.alphaSource!=TextureImporterAlphaSource.None){importer.alphaSource=TextureImporterAlphaSource.None;importer.SaveAndReimport();}
   layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(local);layer.normalMapTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/"+normal);layer.tileSize=Vector2.one*scale;layer.normalScale=.3f;layer.metallic=0;layer.smoothness=0;EditorUtility.SetDirty(layer);return layer;
  }
  static string CheckTerrain284()
  {
   var lines=new List<string>();void C(bool ok,string text){lines.Add((ok?"PASS ":"FAIL ")+text);}
   C(SceneManager.GetActiveScene().path==Scene284,"independent Terrain scene");
   var terrain=Object.FindFirstObjectByType<Terrain>();if(terrain==null)throw new Exception("Terrain missing");var data=terrain.terrainData;
   C(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length==1,"one native Terrain");
   C(terrain.GetComponent<TerrainCollider>().terrainData==data&&AssetDatabase.GetAssetPath(data)==A284+"/Mountain.asset","persistent TerrainData and TerrainCollider match");
   C(data.heightmapResolution==1025&&data.alphamapResolution==1024,"1025 height samples and 1024 splat map");
   C(data.terrainLayers.Length==2&&data.terrainLayers.All(l=>l.diffuseTexture!=null&&l.normalMapTexture!=null),"two textured Terrain layers with normals");
   C(terrain.materialTemplate.shader.isSupported&&!ShaderUtil.ShaderHasError(terrain.materialTemplate.shader),"Terrain triplanar shader supported, no errors");
   C(GameObject.Find("Trail")==null&&GameObject.Find("Cliff_Meshy_282")==null&&GameObject.Find("Cliff_0")==null,"main cliff and trail are Terrain, prior main meshes removed from clone");
   Physics.SyncTransforms();var route=RouteData284();float error=0,slope=0;int blocked=0,hits=0;
   for(int i=3;i<330;i++){
    var p=route[i];float y=terrain.SampleHeight(p)+Origin284.y;error=Mathf.Max(error,Mathf.Abs(y-p.y));
    if(terrain.GetComponent<TerrainCollider>().Raycast(new Ray(p+Vector3.up*10,Vector3.down),out var hit,20))hits++;
    slope=Mathf.Max(slope,data.GetSteepness((p.x-Origin284.x)/Size284.x,(p.z-Origin284.z)/Size284.z));
    var a=new Vector3(p.x,y+.38f,p.z);var obstacles=Physics.OverlapCapsule(a,a+Vector3.up*1.1f,.27f).Where(c=>!(c is CharacterController)&&c!=terrain.GetComponent<TerrainCollider>()).ToArray();if(obstacles.Length>0){blocked++;lines.Add("INFO obstruction route="+i+" names="+string.Join(",",obstacles.Select(c=>c.name)));}
   }
   C(hits==327,"TerrainCollider ground rays "+hits+"/327");C(error<.2f,"route maximum ground height error="+error);C(slope<35,"route maximum slope="+slope);C(blocked==0,"capsule obstruction samples="+blocked);
   var path=new NavMeshPath();bool complete=NavMesh.SamplePosition(route[4],out var start,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(route[325],out var end,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
   C(complete,"fresh baked NavMesh connects lower/upper path, corners="+path.corners.Length);
   C(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(b=>b==null||(!b.GetType().Name.Contains("Campaign")&&!b.GetType().Name.Contains("Save"))),"no campaign/save components");
   CheckDressing284(terrain,route,C);
   File.WriteAllLines(O284+"/unity-checks.txt",lines);return string.Join("\n",lines);
  }
  static CliffAscentContactProbe278 probe284;
  [InitializeOnLoadMethod] static void Register284(){EditorApplication.playModeStateChanged-=Mode284;EditorApplication.playModeStateChanged+=Mode284;}
  static void Mode284(PlayModeStateChange state)
  {
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool("Terrain284.Probe",false);EditorApplication.update-=Tick284;}
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Terrain284.Probe",false))return;
   var explorer=Object.FindFirstObjectByType<CliffAscentExplorer278>();explorer.enabled=false;explorer.GetComponent<CharacterController>().enabled=false;
   var go=new GameObject("Terrain284_physical_probe");var cc=go.AddComponent<CharacterController>();cc.enabled=false;cc.height=1.8f;cc.center=Vector3.up*.9f;cc.radius=.28f;cc.slopeLimit=42;cc.stepOffset=.28f;cc.skinWidth=.035f;
   probe284=go.AddComponent<CliffAscentContactProbe278>();probe284.Points=RouteData284();go.transform.position=probe284.Points[3]+Vector3.up*.12f;cc.enabled=true;
   Time.timeScale=4;Application.runInBackground=true;File.WriteAllText(O284+"/walk-check.txt","RUNNING");EditorApplication.update+=Tick284;
  }
  static void Tick284()
  {
   if(probe284==null||probe284.Result==null)return;
   File.WriteAllText(O284+"/walk-check.txt",probe284.Result+" Play CharacterController: target="+probe284.Target+"/327; moves="+probe284.Moves+"; contacts="+string.Join(";",probe284.HitNames)+". Automated physics only, not manual play or performance measurement.");
   EditorApplication.update-=Tick284;Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

