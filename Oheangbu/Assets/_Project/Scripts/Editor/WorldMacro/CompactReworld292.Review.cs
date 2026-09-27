using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Check292()
  {
   var s=Session292();var rows=new List<string>();
   void Check(bool pass,string label)=>rows.Add((pass?"PASS ":"FAIL ")+label);
   Check(s.Content.SaveSlot=="world-compact-reworld-292","isolated save slot");
   Check(s.MountainLayout.FinalSurface!=null,"final surface connected");
   var source=ScriptableObject.CreateInstance<WorldMacroPlaytestSO>();JsonUtility.FromJsonOverwrite(File.ReadAllText(O292+"/source-content.json"),source);
   Check(source.Points.All(p=>s.Content.Points.Any(q=>q.Id==p.Id)),"stable interaction IDs preserved");Object.DestroyImmediate(source);
   var roots=s.gameObject.scene.GetRootGameObjects();
   Check(roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"no missing scene scripts");
   var materials=roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>()).SelectMany(r=>r.sharedMaterials)
    .Concat(roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Where(a=>a.Sheet!=null).SelectMany(a=>a.Sheet.Prototypes).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material)).ToArray();
   Check(materials.All(m=>m!=null&&m.shader!=null),"active material references");
   foreach(var shader in materials.Where(m=>m!=null).Select(m=>m.shader).Distinct())Check(shader!=null&&shader.isSupported&&!ShaderUtil.ShaderHasError(shader),"shader "+(shader!=null?shader.name:"MISSING"));
   foreach(var m in s.MountainLayout.Mountains)
   {
    float length=0,maxGrade=0;
    for(int i=1;i<m.MainPath.Length;i++){var d=m.MainPath[i]-m.MainPath[i-1];length+=d.magnitude;maxGrade=Mathf.Max(maxGrade,Mathf.Abs(d.y)/Mathf.Max(.01f,new Vector2(d.x,d.z).magnitude));}
    rows.Add($"MEASURE {m.Id} route={length:F1}m max grade={maxGrade:F3}");
    Check(maxGrade<.70f,m.Id+" terrain route slope below 35 degrees (not player verification)");
   }
   rows.Add("Manual play, CPU/GPU timings and Korean/ink art acceptance: NOT VERIFIED");
   string result=string.Join("\n",rows);File.WriteAllText(O292+"/checks.txt",result);return result;
  }
  static string QuarantineNavigation292()
  {
   var s=Session292();int count=0;
   foreach(var nav in s.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>(true)))
   {if(nav.gameObject.name=="Reworld292_Navigation")continue;nav.RemoveData();nav.enabled=false;count++;}
   Save292();File.WriteAllText(O292+"/navigation.txt",count+" obsolete navigation surfaces retained disabled. New route geometry and navigation are NOT VALIDATED. Candidate is environment authoring only.");
   return count+" obsolete navigation surfaces retained disabled in candidate only";
  }
  static string Capture292(int index)
  {
   var s=Session292();var height=new CompactWorldSurface(s.MountainLayout);
   var source=Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene==s.gameObject.scene&&c.cameraType==CameraType.Game).OrderByDescending(c=>c.CompareTag("MainCamera")).First();
   var go=new GameObject("Reworld292_Capture");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;
   EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var arts=Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None);var observers=arts.Select(a=>a.Observer).ToArray();
   var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var oldRT=RenderTexture.active;bool async=ShaderUtil.allowAsyncCompilation;
   Vector3 eye,target;
   if(index==0){eye=new Vector3(2000,5300,300);target=new Vector3(2000,100,3100);}
   else{var m=s.MountainLayout.Mountains[(index-1)%5];if(index<=5){eye=m.Foot+new Vector3(-180,160,-220);target=m.Summit;}else{eye=index<=10?m.Temple+new Vector3(0,2,-6):m.TemplePath[Mathf.RoundToInt((m.TemplePath.Length-1)*.75f)];eye.y=height.Sample(eye.x,eye.z)+2;target=m.Temple+new Vector3(0,3,10);}}
   var fog=AssetDatabase.LoadAssetAtPath<Material>(A292+"/Materials/Fog292.mat");var fogRange=fog!=null?fog.GetVector("_FogRange"):Vector4.zero;bool oldFog=RenderSettings.fog;
   bool mist293=fog!=null&&fog.HasProperty("_Mist293");var mist=mist293?fog.GetVector("_Mist293"):Vector4.zero;
   if(eye293.HasValue){eye=eye293.Value;target=target293.Value;}
   try
   {
    if(index==0||raw293){RenderSettings.fog=false;if(fog!=null)fog.SetVector("_FogRange",new Vector4(15000,20000,0,0));if(mist293)fog.SetVector("_Mist293",new Vector4(mist.x,mist.y,0,mist.w));camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;}
    foreach(var a in arts)a.Observer=camera;ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=index==0?65:60;camera.farClipPlane=10000;
    camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.Render();camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(output293??O292+"/view-"+index+".png",image.EncodeToPNG());
   }
   finally{RenderSettings.fog=oldFog;if(fog!=null)fog.SetVector("_FogRange",fogRange);if(mist293)fog.SetVector("_Mist293",mist);for(int i=0;i<arts.Length;i++)arts[i].Observer=observers[i];ShaderUtil.allowAsyncCompilation=async;RenderTexture.active=oldRT;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);}
   return O292+"/view-"+index+".png";
  }
  static string Navigation292()
  {
   var s=Session292();var old=s.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NavMeshSurface>(true)).Where(v=>v.isActiveAndEnabled).ToArray();
   var basis=old.First();var root=new GameObject("Reworld292_Navigation");var nav=root.AddComponent<NavMeshSurface>();nav.agentTypeID=basis.agentTypeID;nav.collectObjects=CollectObjects.Volume;nav.useGeometry=NavMeshCollectGeometry.PhysicsColliders;
   nav.center=new Vector3(2000,300,3000);nav.size=new Vector3(4000,800,6000);nav.overrideVoxelSize=true;nav.voxelSize=.4f;nav.overrideTileSize=true;nav.tileSize=256;
   try{nav.BuildNavMesh();if(nav.navMeshData==null)throw new Exception("Navigation build returned no data");
    string path=A292+"/Navigation292.asset";if(AssetDatabase.LoadAssetAtPath<NavMeshData>(path)!=null)throw new Exception("Retain existing navigation; revise explicitly");
    AssetDatabase.CreateAsset(nav.navMeshData,path);foreach(var n in old){n.RemoveData();n.enabled=false;}Save292();
    File.WriteAllText(O292+"/navigation.txt","New navigation baked from candidate physics, voxel 0.4m. Agent traversal NOT VERIFIED.");return "Navigation built; route validation pending";
   }catch{var data=nav.navMeshData;Object.DestroyImmediate(root);if(data!=null&&!AssetDatabase.Contains(data))Object.DestroyImmediate(data);throw;}
  }
 }
}
