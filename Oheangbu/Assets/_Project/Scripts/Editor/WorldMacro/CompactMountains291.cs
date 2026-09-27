using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  const string A291=A290+"/InkHermitage291",O291=O290+"/InkHermitage291";
  public static string Mountain291(string command){
   if(SceneManager.GetActiveScene().path!=Scene290||EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean mountain candidate Edit scene required");
   Directory.CreateDirectory(O291);
   if(command=="inspect")return Inspect291();
   if(command=="apply")return Apply291();
   if(command=="capture-before")return Capture291("before");
   if(command=="capture")return Capture291("after");
   if(command=="check")return Check291();
   if(command=="repeat-check"){
    var before=OwnedSignature291();string result=Apply291();var after=OwnedSignature291();
    string repeat=(before==after?"PASS":"FAIL")+" repeat application preserves authored transforms, meshes and material bindings";
    File.WriteAllText(O291+"/repeat.txt",repeat);return repeat+"\n"+result;
   }
   throw new Exception(command);
  }
  static string Apply291(){
   var scene=SceneManager.GetActiveScene();DevSceneKit.EnsureFolder(A291);DevSceneKit.EnsureFolder(A291+"/Materials");
   if(!File.Exists(A291+"/BeforeInkHermitage.unity")){EditorSceneManager.SaveScene(scene,A291+"/BeforeInkHermitage.unity",true);var s0=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();File.WriteAllText(O291+"/content-before.json",JsonUtility.ToJson(s0.Content,true));}
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var mountain=session.MountainLayout.Mountains.Single(m=>m.Realm=="cheongrim");
   BuildHermitage291(session,mountain);
   DressHermitage291(session,mountain);
   string changes=ApplyInkMaterials291();
   AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
   File.WriteAllText(O291+"/apply.txt",changes);return changes;
  }
  static void BuildHermitage291(WorldMacroPlaytestSession session,CompactWorldLayoutSO.Mountain mountain){
   var parent=GameObject.Find("Compact_MountainContent_290").transform;var t=mountain.Temple;
   foreach(Transform child in parent)if(new[]{"ArchiveFloor","WestWall","EastWallNorth","EastWallSouth","RearWall","ArchivePartition","GalleryPost","BrokenGalleryBeam","ArchiveRoof","AssetTempleRoof","TempleStoneAndTimber"}.Contains(child.name))child.gameObject.SetActive(false);
   var old=parent.Find("ThatchedHermitage291");if(old!=null)Object.DestroyImmediate(old.gameObject);
   var root=new GameObject("ThatchedHermitage291").transform;root.SetParent(parent,false);
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab");
   var house=(GameObject)PrefabUtility.InstantiatePrefab(prefab,session.gameObject.scene);house.name="L_Shaped_Thatched_Hermitage";house.transform.SetParent(root,false);house.transform.SetPositionAndRotation(t+new Vector3(0,-.17f,27),Quaternion.Euler(0,180,0));house.transform.localScale=Vector3.one;
   foreach(var c in house.GetComponentsInChildren<Transform>(true))if(c.name=="house_Re_Roof_ACC")c.gameObject.SetActive(false);
   foreach(var light in house.GetComponentsInChildren<Light>())light.enabled=false;
   var materialCopies=new Dictionary<Material,Material>();
   foreach(var r in house.GetComponentsInChildren<Renderer>(true))r.sharedMaterials=r.sharedMaterials.Select(mat=>{
    if(mat==null)return null;if(materialCopies.TryGetValue(mat,out var existing))return existing;
    var path=A291+"/Materials/Hermitage_"+mat.name+".mat";var copy=AssetDatabase.LoadAssetAtPath<Material>(path);if(copy==null){copy=new Material(mat);AssetDatabase.CreateAsset(copy,path);}else EditorUtility.CopySerialized(mat,copy);
    if(copy.HasProperty("_AssetSaturation"))copy.SetFloat("_AssetSaturation",.21f);if(copy.HasProperty("_AssetValue"))copy.SetFloat("_AssetValue",.86f);if(copy.HasProperty("_Smoothness"))copy.SetFloat("_Smoothness",.06f);copy.enableInstancing=true;EditorUtility.SetDirty(copy);materialCopies.Add(mat,copy);return copy;
   }).ToArray();
   // A gate in a low timber enclosure precedes the open porch. Reward remains a save-gated interaction.
   var wood=MountainMaterial290("cheongrim","Pier289_poles");var planks=MountainMaterial290("cheongrim","Pier289_planks");
   void Rail(Vector3 a,Vector3 b,float radius){var go=MeshObject278("WeatheredRail",Import285("Pier289_poles_0"),wood,root,false);go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);go.transform.localScale=new Vector3(radius,(b-a).magnitude,radius);}
   foreach(int side in new[]{-1,1}){
    for(int i=0;i<5;i++){var p=t+new Vector3(side*(2.2f+i*1.7f),0,13);Rail(p-Vector3.up*.12f,p+Vector3.up*(1.3f+(i%2)*.08f),.14f);if(i>0){var q=p+Vector3.left*side*1.7f;Rail(p+Vector3.up*.6f,q+Vector3.up*.58f,.10f);Rail(p+Vector3.up*1.13f,q+Vector3.up*1.17f,.09f);}}
   }
   var gate=parent.Find("ArchiveDoor");gate.position=t+new Vector3(0,.85f,13);gate.localScale=Vector3.one;gate.GetComponent<Renderer>().enabled=false;
   gate.GetComponent<BoxCollider>().size=new Vector3(3.8f,1.7f,.3f);gate.GetComponent<UnityEngine.AI.NavMeshObstacle>().size=new Vector3(3.8f,1.7f,.3f);
   var oldBoards=gate.Find("ReclaimedGate291");if(oldBoards!=null)Object.DestroyImmediate(oldBoards.gameObject);
   var boards=new GameObject("ReclaimedGate291").transform;boards.SetParent(gate,false);
   for(int i=0;i<15;i++){var board=MeshObject278("SplitTimber",Import285("Pier289_planks_"+(i%10)),planks,boards,false);board.transform.localPosition=new Vector3(-1.73f+i*.246f,0,0);board.transform.localRotation=Quaternion.Euler(0,0,90+(i%3-1)*1.7f);board.transform.localScale=new Vector3(1.58f+(i%4)*.04f,.16f,.19f);}
   // The original record position would fall inside the closed wall of the imported house.
   // Move its authored content and visual together to the forecourt; retain the permanent ID and prerequisites.
   string record=mountain.TempleRewardId;var point=session.Content.Points.Single(p=>p.Id==record);point.Position=t+new Vector3(0,0,19);
   var visual=parent.Find(record);visual.position=point.Position;
   EditorUtility.SetDirty(session.Content);EditorUtility.SetDirty(session);
  }
  static string Capture291(string phase){
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var m=session.MountainLayout.Mountains[0];var artRoot=GameObject.Find("Cheongrim_AssetPass_290").transform;var profile=Profile285;
   var source=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene==session.gameObject.scene&&c.cameraType==CameraType.Game).OrderByDescending(c=>c.CompareTag("MainCamera")).First();
   var go=new GameObject("InkHermitage291_review");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=Object.FindFirstObjectByType<CompactRebuildArtRenderer>();var previous=art.Observer;art.Observer=camera;
   var rt=new RenderTexture(1600,900,24);var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);var prior=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
   var eyes=new[]{artRoot.TransformPoint(profile.At(7))+Vector3.up*1.7f,m.Temple+new Vector3(8,6,-10),m.Foot+new Vector3(85,80,-100),m.Foot+new Vector3(0,1.7f,-20),m.Temple+new Vector3(4,2.0f,14)};
   var targets=new[]{artRoot.TransformPoint(profile.At(16))+Vector3.up*1.3f,m.Temple+new Vector3(0,2,26),m.Foot+new Vector3(-60,50,170),m.MainPath[18]+Vector3.up,m.Temple+new Vector3(0,2.1f,27)};
   try{ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=60;
    for(int i=0;i<eyes.Length;i++){camera.transform.SetPositionAndRotation(eyes[i],Quaternion.LookRotation(targets[i]-eyes[i]));camera.Render();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();File.WriteAllBytes(O291+"/"+phase+"-"+i+".png",tex.EncodeToPNG());}
   }finally{art.Observer=previous;ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=prior;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Five matching candidate views including original-map/mountain approach. Source camera="+source.name;
  }
  static string Check291(){
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var m=session.MountainLayout.Mountains[0];var root=GameObject.Find("ThatchedHermitage291");var rows=new List<string>();
   void Check(bool ok,string text)=>rows.Add((ok?"PASS ":"FAIL ")+text);
   Check(root!=null,"thatched hermitage present");var parent=GameObject.Find("Compact_MountainContent_290").transform;
   Check(!parent.Find("AssetTempleRoof").gameObject.activeInHierarchy&&!parent.Find("ArchiveRoof").gameObject.activeInHierarchy,"both previous tiled roofs inactive");
   Check(root.GetComponentsInChildren<MeshFilter>().Any(f=>f.name=="house_Re_Roof_01"),"original thatch mesh used");
   var record=session.Content.Points.Single(p=>p.Id==m.TempleRewardId);Check(Vector3.Distance(parent.Find(record.Id).position,record.Position)<.01f,"authored record position and visual agree");
   Check(m.Actions.Single(a=>a.Id==m.TempleRewardId).Reward==160,"temple reward remains 160, original ID retained");
   Check(session.Content.SaveSlot=="world-compact-mountains-290","candidate save slot retained");
   var owned=SceneManager.GetActiveScene().GetRootGameObjects().Where(g=>new[]{"Compact_Mountains_290","Cheongrim_AssetPass_290","Compact_MountainContent_290"}.Contains(g.name)).ToArray();
   var renderers=owned.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).Where(r=>r.enabled).ToArray();
   var mats=renderers.SelectMany(r=>r.sharedMaterials).ToArray();
   Check(mats.All(mat=>mat!=null&&mat.shader!=null),"active owned renderers have material and shader references");
   Check(mats.Where(mat=>mat!=null).Select(mat=>mat.shader).Distinct().All(shader=>shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader)),"active owned shaders supported without shader compiler errors");
   Check(owned.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).All(g=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g.gameObject)==0),"owned hierarchy has no missing scripts");
   Physics.SyncTransforms();foreach(var p in session.Content.Points.Where(p=>p.Id.StartsWith(m.Id))){
    bool access=false;for(int i=0;i<16;i++){float a=i*Mathf.PI/8;var feet=p.Position+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.4f;
     if(Physics.Raycast(feet+Vector3.up*.8f,Vector3.down,out var ground,1.6f)&&ground.normal.y>.65f&&!Physics.CheckCapsule(ground.point+Vector3.up*.4f,ground.point+Vector3.up*1.5f,.28f,~0,QueryTriggerInteraction.Ignore)){access=true;break;}}
    Check(access,"standing interaction access "+p.Id);
   }
   string result=string.Join("\n",rows);File.WriteAllText(O291+"/checks.txt",result);return result;
  }
  static string OwnedSignature291(){
   var root=GameObject.Find("Compact_MountainContent_290");
   return string.Join("\n",root.GetComponentsInChildren<Transform>(true).Select(t=>t.name+":"+t.localPosition.ToString("F4")+":"+t.localRotation.ToString("F4")+":"+t.localScale.ToString("F4")+":"+t.gameObject.activeSelf+":"+(t.TryGetComponent<Renderer>(out var r)?string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath)):"")+":"+(t.TryGetComponent<MeshFilter>(out var f)?AssetDatabase.GetAssetPath(f.sharedMesh):"")).OrderBy(v=>v));
  }
  static string Inspect291(){
   var scene=SceneManager.GetActiveScene();var lines=new List<string>();
   foreach(var root in scene.GetRootGameObjects().Where(g=>g.name.Contains("Terrain")||g.name.Contains("Art")||g.name.Contains("Mountains"))){
    lines.Add("ROOT "+root.name);
    foreach(var mat in root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m!=null).Distinct().Take(24)){
     lines.Add("MAT "+AssetDatabase.GetAssetPath(mat)+" shader="+mat.shader.name);
     foreach(var key in new[]{"_CIEnabled","_CIDarkNear","_CIPainterly","_PaintedInkEnabled","_PaintedFormEnabled","_PaintedWashEnabled","_RebuildScreenFog","_RebuildGravelStrength","_StrataStrength276"})if(mat.HasProperty(key))lines.Add(key+"="+mat.GetFloat(key));
    }
   }
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab");
   foreach(var f in prefab.GetComponentsInChildren<MeshFilter>(true))lines.Add("HOUSE "+f.name+" "+AssetDatabase.GetAssetPath(f.sharedMesh)+" bounds="+f.sharedMesh.bounds+" materials="+string.Join(",",f.GetComponent<Renderer>().sharedMaterials.Select(m=>m!=null?AssetDatabase.GetAssetPath(m):"MISSING")));
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();foreach(var p in s.MountainLayout.Places.Where(p=>new[]{"herb_path","inn","geumpyo","merchant"}.Contains(p.Id)))lines.Add("POI "+p.Id+" "+p.XZ);
   lines.Add("FOG "+RenderSettings.fog+" "+RenderSettings.fogMode+" "+RenderSettings.fogStartDistance+" "+RenderSettings.fogEndDistance+" "+RenderSettings.fogColor);
   File.WriteAllText(O291+"/inspection.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
