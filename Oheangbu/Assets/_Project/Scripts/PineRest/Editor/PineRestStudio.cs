using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Oheangbu.PineRest.Editor
{
 [InitializeOnLoad] public static class PineRestStudio
 {
  const string Compact="Assets/_Project/Scenes/World/W_Demo_Compact.unity";
  const string Source="Assets/_Project/Scenes/Dev/C2_CodexWorld.unity";
  const string Study="Assets/_Project/Scenes/World/W_ArtStudy_PineRest.unity";
  const string Assets="Assets/_Project/Art/World/PineRest";
  static string Output=>Path.GetFullPath("../Art/World/PineRest");
  [Serializable] class Request{public string id,method,argument;}
  [Serializable] class Response{public string status,result,error;}
  static PineRestStudio(){EditorApplication.update+=Tick;}
  static void Tick(){if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;string path=Output+"/command.json";if(!File.Exists(path))return;Request r;try{r=JsonUtility.FromJson<Request>(File.ReadAllText(path));}catch(IOException){return;}File.Move(path,Output+"/request_"+r.id+".json");var reply=new Response();try{reply.result=Execute(r.method,r.argument);reply.status="COMPLETE";}catch(Exception e){reply.status="FAILED";reply.error=e.ToString();Debug.LogException(e);}File.WriteAllText(Output+"/response_"+r.id+".json",JsonUtility.ToJson(reply,true));}
  static string Execute(string method,string arg){switch(method){case "Game":return (string)Type.GetType("Oheangbu.EditorTools.PineRestGameBuilder, Oheangbu.EditorTools",true).GetMethod("Run").Invoke(null,new object[]{arg});case "ClearWall":return ClearWall();case "Finish":return Finish();case "Polish":return Polish();case "Ground":return Ground();case "Dress":return Dress();case "Audit":return Audit();case "Build":return Build();case "Capture":return Capture(arg);case "Reset":return Reset();case "Inspect":return Inspect();case "Reserialize":AssetDatabase.ForceReserializeAssets(new[]{"Assets/_Project/Data/World/AreaSheet_GeumpyoRoad.asset"});return "Serialized original area sheet";default:throw new ArgumentException(method);}}
  static string Reset(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.isDirty&&scene.path!="Assets/_Project/Scenes/World/W_Cheongrim_GeumpyoRoad.unity")throw new Exception("Unrelated unsaved scene");EditorSceneManager.OpenScene(Compact);Shader.SetGlobalFloat("_OhWorldPost",0);
   foreach(string path in new[]{"Assets/Settings/PC_RPAsset.asset","Assets/Settings/Mobile_RPAsset.asset"}){var rp=AssetDatabase.LoadMainAssetAtPath(path);var so=new SerializedObject(rp);var list=so.FindProperty("m_RendererDataList");for(int i=list.arraySize-1;i>=0;i--){if(AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(i).objectReferenceValue).Contains("/InkLandscape/")){int size=list.arraySize;list.DeleteArrayElementAtIndex(i);if(list.arraySize==size)list.DeleteArrayElementAtIndex(i);}}so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(rp);}
   var sheet=AssetDatabase.LoadMainAssetAtPath("Assets/_Project/Data/World/AreaSheet_GeumpyoRoad.asset");var serialized=new SerializedObject(sheet);var flag=serialized.FindProperty("artReviewOnly");if(flag!=null){flag.boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(sheet);}
   string[] rejected={"Assets/_Project/Scenes/World/W_Cheongrim_GeumpyoRoad.unity","Assets/_Project/Art/World/InkLandscape","Assets/_Project/Shaders/InkLandscape","Assets/_Project/Scripts/InkLandscape"};var log=new StringBuilder();foreach(string path in rejected){if(AssetDatabase.IsValidFolder(path)||AssetDatabase.LoadMainAssetAtPath(path)!=null){if(!AssetDatabase.DeleteAsset(path))throw new Exception("Delete failed "+path);log.AppendLine("Deleted "+path);}}
   var builds=EditorBuildSettings.scenes.Where(x=>!x.path.Contains("W_Cheongrim_GeumpyoRoad")).ToList();builds=builds.OrderBy(x=>x.path.EndsWith("W_Demo_Compact_Title.unity")?0:x.path==Compact?1:2).ToList();EditorBuildSettings.scenes=builds.ToArray();AssetDatabase.SaveAssets();File.WriteAllText(Output+"/reset.txt",log.ToString());return log.ToString();}
  static string Inspect(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)throw new Exception("Unsaved scene");EditorSceneManager.OpenScene(Source);var s=new StringBuilder();foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()){s.AppendLine("ROOT "+root.name+" pos="+root.transform.position);foreach(Transform child in root.transform){s.AppendLine("  "+child.name+" pos="+child.position+" scale="+child.localScale);}}
   foreach(var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(b!=null)s.AppendLine("SCRIPT "+b.GetType().FullName+" on "+b.name);
   foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))s.AppendLine("CAM "+c.name+" "+c.transform.position+" "+c.transform.eulerAngles+" fov="+c.fieldOfView);
   foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.name.Contains("Pine")||r.name.Contains("Inn")||r.name.Contains("Mountain")).Take(20))s.AppendLine("RENDER "+r.name+" bounds="+r.bounds+" materials="+string.Join(",",r.sharedMaterials.Where(m=>m!=null).Select(AssetDatabase.GetAssetPath)));
   File.WriteAllText(Output+"/source_inventory.txt",s.ToString());return s.ToString();}

  static string Build(){
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty && UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Study)throw new Exception("Unrelated unsaved scene");
   if(AssetDatabase.LoadMainAssetAtPath(Study)!=null){EditorSceneManager.OpenScene(Study);return "Opened existing study without rebuilding";}
   if(!AssetDatabase.CopyAsset(Source,Study))throw new Exception("Copy failed");
   var scene=EditorSceneManager.OpenScene(Study);
   var original=Camera.main;var cam=new GameObject("PineRest_ReviewCamera").AddComponent<Camera>();EditorUtility.CopySerialized(original,cam);cam.tag="MainCamera";var data=cam.GetUniversalAdditionalCameraData();EditorUtility.CopySerialized(original.GetUniversalAdditionalCameraData(),data);
   foreach(var root in scene.GetRootGameObjects())if(root.name=="PlayerRig")Object.DestroyImmediate(root);
   var transforms=Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None);
   foreach(var t in transforms.Where(t=>t.name=="00_Abandoned_Mine").ToArray())Object.DestroyImmediate(t.gameObject);
   var pines=transforms.First(t=>t!=null && t.name=="03_Windswept_Pines");
   var notes=new StringBuilder();foreach(Transform t in pines)notes.AppendLine(t.name+" "+t.position+" "+t.localScale);
   var inn=transforms.First(t=>t!=null && t.name=="04_Mountain_Inn");foreach(Transform t in inn)notes.AppendLine("INN "+t.name+" "+t.position+" "+t.localScale);
   File.WriteAllText(Output+"/composition_inventory.txt",notes.ToString());
   cam.transform.position=new Vector3(6,4.2f,9);cam.transform.LookAt(new Vector3(-9,5.8f,43));cam.fieldOfView=54;cam.farClipPlane=1600;cam.GetUniversalAdditionalCameraData().SetRenderer(0);
   var driver=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(b=>b.GetType().Name=="WorldLookDriver");
   var so=new SerializedObject(driver);so.FindProperty("_skyCamera").objectReferenceValue=cam;so.ApplyModifiedPropertiesWithoutUndo();driver.GetType().GetMethod("Apply").Invoke(driver,null);
   var marker=new GameObject("ART STUDY ONLY - Pine Rest - no gameplay");
   EditorSceneManager.SaveScene(scene);return "Created "+Study;
  }
  static string Capture(string arg){
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=Study)throw new Exception("Open study first");
   var cam=Camera.main;
   if(!string.IsNullOrEmpty(arg)&&arg.Contains(",")){var v=arg.Split(',').Select(float.Parse).ToArray();cam.transform.position=new Vector3(v[0],v[1],v[2]);cam.transform.LookAt(new Vector3(v[3],v[4],v[5]));cam.fieldOfView=v[6];EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());}
   var driver=Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).First(b=>b.GetType().Name=="WorldLookDriver");driver.GetType().GetMethod("Apply").Invoke(driver,null);
   var prev=cam.targetTexture;var active=RenderTexture.active;bool srgb=GL.sRGBWrite,async=ShaderUtil.allowAsyncCompilation;
   var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);rt.Create();
   var encoded=new RenderTexture(1920,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);encoded.Create();var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false,false);
   try{ShaderUtil.allowAsyncCompilation=false;cam.targetTexture=rt;cam.Render();GL.sRGBWrite=true;Graphics.Blit(rt,encoded);RenderTexture.active=encoded;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();string path=Output+"/"+(string.IsNullOrEmpty(arg)||arg.Contains(",")?"review":arg)+".png";File.WriteAllBytes(path,tex.EncodeToPNG());return path;}
   finally{cam.targetTexture=prev;RenderTexture.active=active;GL.sRGBWrite=srgb;ShaderUtil.allowAsyncCompilation=async;rt.Release();encoded.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(encoded);Object.DestroyImmediate(tex);}
  }

  static string Dress(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");
   var old=GameObject.Find("PineRest_Composition");if(old!=null)Object.DestroyImmediate(old);var holder=new GameObject("PineRest_Composition").transform;
   var all=Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
   var tree=all.First(t=>t.name=="Korean_Pine_1");
   Clone(tree,new Vector3(-12.5f,1.5f,18),new Vector3(.83f,.83f,.83f),48,holder,"Left frame pine");
   Clone(tree,new Vector3(-25,2.8f,32),new Vector3(.74f,.74f,.74f),132,holder,"Inn shelter pine");
   var rock=all.First(t=>t.name=="Talus_0_0");
   Vector3[] rocks={new Vector3(-11,1.3f,18),new Vector3(-9,1.1f,19.3f),new Vector3(-14,1.5f,20),new Vector3(8,1.7f,23),new Vector3(9,1.6f,25)};
   for(int i=0;i<rocks.Length;i++)Clone(rock,rocks[i],Vector3.one*(i==0?3.2f:1.7f),i*61,holder,"Path shoulder stone "+i);
   // Two broken dry-stone edges contain a resting courtyard without closing the approach.
   for(int side=0;side<2;side++)for(int i=0;i<8;i++){
    float x=side==0?-23.5f:-7.2f;float z=30+i*.85f;
    Clone(rock,new Vector3(x,1.6f+(i%3)*.06f,z),new Vector3(.9f,.58f,1.15f),i*47+side*19,holder,"Courtyard wall "+side+" "+i);
   }
   var lantern=all.First(t=>t.name=="Warm_Porch_Lantern");
   var lamp=Clone(lantern,new Vector3(-5.8f,3.3f,27),Vector3.one*.85f,0,holder,"Approach lantern");
   var wood=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/Timber.mat");
   Block("Lantern post",new Vector3(-5.3f,2.35f,27),new Vector3(.13f,2.9f,.13f),wood,holder);
   Block("Lantern arm",new Vector3(-5.55f,3.75f,27),new Vector3(.7f,.10f,.13f),wood,holder);
   Block("Resting bench",new Vector3(-21,2.0f,33),new Vector3(2.8f,.14f,.6f),wood,holder);
   foreach(float x in new[]{-22f,-20f})Block("Bench leg",new Vector3(x,1.75f,33),new Vector3(.2f,.5f,.45f),wood,holder);
   var cam=Camera.main;cam.transform.position=new Vector3(3.5f,3.6f,14);cam.transform.LookAt(new Vector3(-10,4.3f,38));cam.fieldOfView=55;
   EditorSceneManager.SaveScene(scene);return "Composed pines, stone courtyard, resting bench, approach lantern";
  }
  static Transform Clone(Transform source,Vector3 position,Vector3 scale,float yaw,Transform parent,string name){var t=Object.Instantiate(source.gameObject,parent).transform;t.name=name;t.position=position;t.localScale=scale;t.rotation=Quaternion.Euler(0,yaw,0);return t;}
  static void Block(string name,Vector3 p,Vector3 scale,Material mat,Transform parent){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent);g.transform.position=p;g.transform.localScale=scale;g.GetComponent<Renderer>().sharedMaterial=mat;}
  static string Audit(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");var report=new StringBuilder();int missing=0;foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
   var renderers=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);var mats=renderers.SelectMany(r=>r.sharedMaterials).ToArray();int nulls=mats.Count(m=>m==null);var shaders=mats.Where(m=>m!=null).Select(m=>m.shader).Distinct().ToArray();int errors=shaders.Count(s=>s==null||ShaderUtil.ShaderHasError(s));
   report.AppendLine("Missing scripts="+missing+"; missing materials="+nulls+"; shader errors="+errors+"; renderers="+renderers.Length);report.AppendLine("Gameplay rig="+(GameObject.Find("PlayerRig")!=null));report.AppendLine("Built-in cameras="+Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length);
   report.AppendLine("Study in build="+EditorBuildSettings.scenes.Any(s=>s.path==Study));foreach(var s in shaders)report.AppendLine("Shader "+s.name);File.WriteAllText(Output+"/audit.txt",report.ToString());return report.ToString();
  }

  static string Ground(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");
   var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).First(c=>c.name=="Valley");var holder=GameObject.Find("PineRest_Composition").transform;Physics.SyncTransforms();
   foreach(Transform t in holder){if(t.name.StartsWith("Courtyard wall")||t.name.Contains("stone")||t.name.Contains("pine")){
    if(ground.Raycast(new Ray(t.position+Vector3.up*100,Vector3.down),out var hit,200)){
     if(t.name.Contains("pine"))t.position=new Vector3(t.position.x,hit.point.y-.12f,t.position.z);
     else{var bounds=t.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(t.position,Vector3.zero),(b,r)=>{b.Encapsulate(r.bounds);return b;});t.position+=Vector3.up*(hit.point.y-bounds.min.y-.15f);}
    }
   }}
   foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r=>r.name.StartsWith("Inn_Approach_"))){if(ground.Raycast(new Ray(r.bounds.center+Vector3.up*100,Vector3.down),out var h,200))r.transform.position+=Vector3.up*(h.point.y-r.bounds.min.y-.02f);}
   var bench=holder.Find("Resting bench");if(ground.Raycast(new Ray(bench.position+Vector3.up*100,Vector3.down),out var bh,200)){float dy=bh.point.y+.55f-bench.position.y;foreach(Transform t in holder)if(t.name.StartsWith("Bench")||t.name=="Resting bench")t.position+=Vector3.up*dy;}
   foreach(var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(l.type==LightType.Directional){l.shadowBias=.025f;l.shadowNormalBias=.1f;}
   EditorSceneManager.SaveScene(scene);return "Ground contact adjusted for added props and existing approach stones; shadow bias reduced in study";
  }

  [MenuItem("Oheangbu/Art Study/Pine Rest/Open")] static void OpenMenu(){EditorSceneManager.OpenScene(Study);}
  [MenuItem("Oheangbu/Art Study/Pine Rest/Capture current view")] static void CaptureMenu(){Debug.Log(Capture("review"));}
  [MenuItem("Oheangbu/Art Study/Pine Rest/Audit")] static void AuditMenu(){Debug.Log(Audit());}
  static string Polish(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");
   if(!AssetDatabase.IsValidFolder(Assets))AssetDatabase.CreateFolder("Assets/_Project/Art/World","PineRest");
   var volume=Object.FindFirstObjectByType<Volume>();string profilePath=Assets+"/PineRestVolume.asset";
   var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);if(profile==null){AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile),profilePath);profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);}volume.sharedProfile=profile;
   if(!profile.TryGet<ColorAdjustments>(out var color)){color=profile.Add<ColorAdjustments>();AssetDatabase.AddObjectToAsset(color,profile);}
   color.postExposure.Override(.35f);color.contrast.Override(12);color.saturation.Override(-8);EditorUtility.SetDirty(color);EditorUtility.SetDirty(profile);Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing=true;
   var holder=GameObject.Find("PineRest_Composition").transform;var pine=holder.Find("Left frame pine");pine.position=new Vector3(-3,1.2f,16.5f);pine.localScale=Vector3.one*.8f;
   AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Isolated volume: exposure +0.35, contrast 12; foreground pine framing";
  }

  static string Finish(){
   var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");
   var holder=GameObject.Find("PineRest_Composition").transform;
   foreach(Transform t in holder)if(t.name.StartsWith("Courtyard wall")){t.position+=Vector3.up*.5f;foreach(var lod in t.GetComponentsInChildren<LODGroup>()){lod.RecalculateBounds();lod.ForceLOD(0);}}
   // Source approach stones exaggerate the slope in this close composition; use the existing ground path.
   foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Inn_Approach_")).ToArray())Object.DestroyImmediate(t.gameObject);
   EditorSceneManager.SaveScene(scene);return "Raised low wall crowns; removed raised approach stepping-stones in study";
  }

  static string ClearWall(){var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();if(scene.path!=Study)throw new Exception("Study only");foreach(var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.name.StartsWith("Courtyard wall")).ToArray())Object.DestroyImmediate(t.gameObject);EditorSceneManager.SaveScene(scene);return "Removed unsuccessful low-wall trial";}
 }
}
