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
  const string A283="Assets/_Project/Art/World/TerrainAscent283";
  const string O283="../Art/World/Compact/Rebuild/Terrain283";
  public const string Scene283=A283+"/W_Cheongrim_CliffAscent_Terrain.unity";
  static readonly Vector3 Origin283=new Vector3(-320,-155,-70), Size283=new Vector3(768,400,512);
  static readonly float[] CrossX283={-320,-160,-80,-40,-15,-7,-3.8f,3.8f,7,12,25,50,85,120,180,300};
  static readonly float[] CrossY283={-145,-130,-105,-60,-24,-6,0,0,8,30,45,70,80,65,28,-10};
  static Vector3 At283(Vector3[] route,float z){int i=Mathf.Clamp(Mathf.FloorToInt(z),0,route.Length-2);return Vector3.Lerp(route[i],route[i+1],Mathf.Clamp01(z-i));}
  static float Height283(Vector3[] route,float x,float z)
  {
   var p=At283(route,z);float d=x-p.x, h=CrossY283[CrossY283.Length-1];
   for(int k=1;k<CrossX283.Length;k++)if(d<CrossX283[k]){h=Mathf.Lerp(CrossY283[k-1],CrossY283[k],Mathf.InverseLerp(CrossX283[k-1],CrossX283[k],d));break;}
   float mask=Mathf.SmoothStep(0,1,Mathf.InverseLerp(5,32,Mathf.Abs(d)));
   float broad=(Mathf.PerlinNoise(x*.014f+37,z*.013f+11)-.5f)*29;
   float detail=(1-Mathf.Abs(Mathf.PerlinNoise(x*.075f+21,z*.09f+7)*2-1))*3;
   float height=p.y+h+mask*(broad+detail);
   var inn=At283(route,10)+Vector3.left*32;
   float terrace=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(11,24,Vector2.Distance(new Vector2(x,z),new Vector2(inn.x,inn.z))));
   // A continuous approach connects the inn terrace back to the start of the ascent.
   float approach=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,9,Mathf.Abs(z-10))))*(x>inn.x&&x<p.x?1:0);
   float keepTrail=Mathf.SmoothStep(0,1,Mathf.InverseLerp(4.5f,10,Mathf.Abs(d)));
   return Mathf.Lerp(height,inn.y,Mathf.Max(terrace,approach)*keepTrail);
  }
  [MenuItem("Oheangbu/별도 맵/Terrain 산길 283 열기 (이 씬에서 Play)")]
  public static void OpenTerrain283()=>Terrain283("open");
  public static string Terrain283(string command)
  {
   Directory.CreateDirectory(O283);
   if(command=="capture")return CaptureAscent278(Scene283,O283);
   if(command=="check")return CheckTerrain283();
   if(command=="walk-play"){
    if(SceneManager.GetActiveScene().path!=Scene283||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean Terrain283 Edit scene required");
    SessionState.SetBool("Terrain283.Probe",true);CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;return "Terrain CharacterController Play probe scheduled";
   }
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Save current changes first; clean Edit required");
   if(command=="open"){EditorSceneManager.OpenScene(Scene283);CompactLoadingStartup270.UseCurrent();return Scene283;}
   if(command!="build")throw new ArgumentException(command);
   DevSceneKit.EnsureFolder(A283);
   EditorSceneManager.OpenScene(Scene278);
   EditorSceneManager.SaveScene(SceneManager.GetActiveScene(),Scene283,true);
   EditorSceneManager.OpenScene(Scene283);
   var root=GameObject.Find("Cliff_Ascent_278");
   foreach(Transform child in root.transform.Cast<Transform>().ToArray())
    if(child.name.StartsWith("Cliff_")||child.name=="Trail"||child.name.StartsWith("Groundcover_packet_"))Object.DestroyImmediate(child.gameObject);
   var route=RouteData278().points;
   var data=AssetDatabase.LoadAssetAtPath<TerrainData>(A283+"/Mountain.asset");
   if(data==null){data=new TerrainData();AssetDatabase.CreateAsset(data,A283+"/Mountain.asset");}
   data.heightmapResolution=1025;data.size=Size283;data.alphamapResolution=1024;data.baseMapResolution=1024;
   var heights=new float[1025,1025];
   for(int z=0;z<1025;z++)for(int x=0;x<1025;x++)heights[z,x]=Mathf.Clamp01((Height283(route,Origin283.x+x/1024f*Size283.x,Origin283.z+z/1024f*Size283.z)-Origin283.y)/Size283.y);
   data.SetHeights(0,0,heights);
   data.terrainLayers=new[]{Layer283("Rock",A278+"/Textures/Granite280.png","T_RockGround_1_N.png",2.5f),Layer283("Dirt","Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/T_Dirt_1_BC.png","T_Dirt_1_N.png",3)};
   var splat=new float[1024,1024,2];
   for(int z=0;z<1024;z++)for(int x=0;x<1024;x++){
    float u=x/1023f,v=z/1023f,wx=Origin283.x+u*Size283.x,wz=Origin283.z+v*Size283.z;
    float d=Mathf.Abs(wx-At283(route,wz).x);
    float road=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(2.7f,4.1f,d)))*(wz>=-2&&wz<=334?1:0);
    float soil=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(16,42,data.GetSteepness(u,v))))*.7f;
    var inn=At283(route,10)+Vector3.left*32;
    float yard=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(10,18,Vector2.Distance(new Vector2(wx,wz),new Vector2(inn.x,inn.z))));
    float dirt=Mathf.Max(road,soil,yard);splat[z,x,0]=1-dirt;splat[z,x,1]=dirt;
   }
   data.SetAlphamaps(0,0,splat);EditorUtility.SetDirty(data);
   var go=UnityEngine.Terrain.CreateTerrainGameObject(data);go.name="Cliff_Terrain283";go.transform.SetParent(root.transform);go.transform.position=Origin283;
   var terrain=go.GetComponent<Terrain>();terrain.drawInstanced=true;terrain.heightmapPixelError=3;terrain.basemapDistance=1600;
   var material=new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")){name="NativeTerrain283"};terrain.materialTemplate=SurveyMaterial(material,A283+"/Terrain.mat");
   foreach(var group in root.GetComponentsInChildren<LODGroup>()){
    var p=group.transform.position;p.y=terrain.SampleHeight(p)+Origin283.y-.08f;group.transform.position=p;
   }
   var scree=root.transform.Find("Scree_and_retaining_stones");if(scree!=null)foreach(Transform t in scree){var p=t.position;p.y=terrain.SampleHeight(p)+Origin283.y-t.lossyScale.y*.25f;t.position=p;}
   RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.65f,.67f,.65f);RenderSettings.fogStartDistance=160;RenderSettings.fogEndDistance=1250;
   var navObject=new GameObject("Terrain283_Navigation");navObject.transform.SetParent(root.transform);
   var nav=navObject.AddComponent<NavMeshSurface>();nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;nav.center=new Vector3(40,80,165);nav.size=new Vector3(250,260,350);nav.overrideVoxelSize=true;nav.voxelSize=.2f;
   Physics.SyncTransforms();nav.BuildNavMesh();
   if(nav.navMeshData!=null){var copy=Object.Instantiate(nav.navMeshData);nav.RemoveData();var old=AssetDatabase.LoadAssetAtPath<NavMeshData>(A283+"/Navigation.asset");if(old==null)AssetDatabase.CreateAsset(copy,A283+"/Navigation.asset");else{EditorUtility.CopySerialized(copy,old);Object.DestroyImmediate(copy);copy=old;EditorUtility.SetDirty(old);}nav.navMeshData=copy;nav.AddData();}
   var explorer=Object.FindFirstObjectByType<CliffAscentExplorer278>();explorer.StartFeet=At283(route,4);explorer.transform.position=explorer.StartFeet+Vector3.up*.12f;
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());CompactLoadingStartup270.UseCurrent();
   return "Independent native Terrain scene saved; 1025 heightmap, 1024 splat, road painted directly on Terrain";
  }
  static TerrainLayer Layer283(string name,string diffuse,string normal,float scale)
  {
   var path=A283+"/"+name+".terrainlayer";var layer=AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
   if(layer==null){layer=new TerrainLayer();AssetDatabase.CreateAsset(layer,path);}
   // Terrain Lit treats an opaque diffuse alpha channel as polished smoothness.
   // Copy import settings locally, leaving the shared source and its pixels untouched.
   string local=A283+"/"+name+"Diffuse"+Path.GetExtension(diffuse);
   if(AssetDatabase.LoadAssetAtPath<Texture2D>(local)==null)AssetDatabase.CopyAsset(diffuse,local);
   var importer=(TextureImporter)AssetImporter.GetAtPath(local);
   if(importer.alphaSource!=TextureImporterAlphaSource.None){importer.alphaSource=TextureImporterAlphaSource.None;importer.SaveAndReimport();}
   layer.diffuseTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(local);layer.normalMapTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/World/WorldCompact/NaturalSurface/Textures/"+normal);layer.tileSize=Vector2.one*scale;layer.normalScale=.3f;layer.metallic=0;layer.smoothness=0;EditorUtility.SetDirty(layer);return layer;
  }
  static string CheckTerrain283()
  {
   var lines=new List<string>();void C(bool ok,string text){lines.Add((ok?"PASS ":"FAIL ")+text);}
   C(SceneManager.GetActiveScene().path==Scene283,"independent Terrain scene");
   var terrain=Object.FindFirstObjectByType<Terrain>();if(terrain==null)throw new Exception("Terrain missing");var data=terrain.terrainData;
   C(Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).Length==1,"one native Terrain");
   C(terrain.GetComponent<TerrainCollider>().terrainData==data&&AssetDatabase.GetAssetPath(data)==A283+"/Mountain.asset","persistent TerrainData and TerrainCollider match");
   C(data.heightmapResolution==1025&&data.alphamapResolution==1024,"1025 height samples and 1024 splat map");
   C(data.terrainLayers.Length==2&&data.terrainLayers.All(l=>l.diffuseTexture!=null&&l.normalMapTexture!=null),"two textured Terrain layers with normals");
   C(terrain.materialTemplate.shader.isSupported&&!ShaderUtil.ShaderHasError(terrain.materialTemplate.shader),"native URP Terrain shader supported, no errors");
   C(GameObject.Find("Trail")==null&&GameObject.Find("Cliff_Meshy_282")==null&&GameObject.Find("Cliff_0")==null,"main cliff and trail are Terrain, prior main meshes removed from clone");
   Physics.SyncTransforms();var route=RouteData278().points;float error=0,slope=0;int blocked=0,hits=0;
   for(int i=3;i<330;i++){
    var p=route[i];float y=terrain.SampleHeight(p)+Origin283.y;error=Mathf.Max(error,Mathf.Abs(y-p.y));
    if(terrain.GetComponent<TerrainCollider>().Raycast(new Ray(p+Vector3.up*10,Vector3.down),out var hit,20))hits++;
    slope=Mathf.Max(slope,data.GetSteepness((p.x-Origin283.x)/Size283.x,(p.z-Origin283.z)/Size283.z));
    var a=new Vector3(p.x,y+.38f,p.z);if(Physics.OverlapCapsule(a,a+Vector3.up*1.1f,.27f).Any(c=>!(c is CharacterController)&&c!=terrain.GetComponent<TerrainCollider>()))blocked++;
   }
   C(hits==327,"TerrainCollider ground rays "+hits+"/327");C(error<.2f,"route maximum ground height error="+error);C(slope<35,"route maximum slope="+slope);C(blocked==0,"capsule obstruction samples="+blocked);
   var path=new NavMeshPath();bool complete=NavMesh.SamplePosition(route[4],out var start,2,NavMesh.AllAreas)&&NavMesh.SamplePosition(route[325],out var end,2,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
   C(complete,"fresh baked NavMesh connects lower/upper path, corners="+path.corners.Length);
   C(Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).All(b=>b==null||(!b.GetType().Name.Contains("Campaign")&&!b.GetType().Name.Contains("Save"))),"no campaign/save components");
   File.WriteAllLines(O283+"/unity-checks.txt",lines);return string.Join("\n",lines);
  }
  static CliffAscentContactProbe278 probe283;
  [InitializeOnLoadMethod] static void Register283(){EditorApplication.playModeStateChanged-=Mode283;EditorApplication.playModeStateChanged+=Mode283;}
  static void Mode283(PlayModeStateChange state)
  {
   if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool("Terrain283.Probe",false);EditorApplication.update-=Tick283;}
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Terrain283.Probe",false))return;
   var explorer=Object.FindFirstObjectByType<CliffAscentExplorer278>();explorer.enabled=false;explorer.GetComponent<CharacterController>().enabled=false;
   var go=new GameObject("Terrain283_physical_probe");var cc=go.AddComponent<CharacterController>();cc.enabled=false;cc.height=1.8f;cc.center=Vector3.up*.9f;cc.radius=.28f;cc.slopeLimit=42;cc.stepOffset=.28f;cc.skinWidth=.035f;
   probe283=go.AddComponent<CliffAscentContactProbe278>();probe283.Points=RouteData278().points;go.transform.position=probe283.Points[3]+Vector3.up*.12f;cc.enabled=true;
   Time.timeScale=4;Application.runInBackground=true;File.WriteAllText(O283+"/walk-check.txt","RUNNING");EditorApplication.update+=Tick283;
  }
  static void Tick283()
  {
   if(probe283==null||probe283.Result==null)return;
   File.WriteAllText(O283+"/walk-check.txt",probe283.Result+" Play CharacterController: target="+probe283.Target+"/327; moves="+probe283.Moves+"; contacts="+string.Join(";",probe283.HitNames)+". Automated physics only, not manual play or performance measurement.");
   EditorApplication.update-=Tick283;Time.timeScale=1;EditorApplication.isPlaying=false;
  }
 }
}

