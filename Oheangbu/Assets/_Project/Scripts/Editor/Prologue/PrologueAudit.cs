using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.Prologue
{
 public static class PrologueAudit
 {
  public static string FinalizeScene()
  {
   if(EditorApplication.isPlaying)throw new Exception("Edit mode required");
   if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=PrologueBuilder.ScenePath)throw new Exception("Production scene required");
   var session=Object.FindFirstObjectByType<PrologueSession>();session.TestSaveSuffix="";
   PrologueBuilder.Set(session.Wiring,"_environmentOcclusion",true);
   var driver=Object.FindFirstObjectByType<WorldLookDriver>();PrologueBuilder.Set(driver,"_inkSkyProfile",AssetDatabase.LoadAssetAtPath<InkSkyProfile>(PrologueBuilder.Folder+"/SkyProfile.asset"));driver.Apply();
   // Scene-copy rocks that were under the old inn must not hover over the new trail.
   var list=new List<string>();foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){
    if(!r.name.Contains("Bedrock")&&!r.name.Contains("Rock"))continue;
    if(r.bounds.size.x>40||r.bounds.size.z>40)continue;
    var p=r.bounds.center;float gap=r.bounds.min.y-PrologueBuilder.Height(p.x,p.z);
    if(gap>2)list.Add(r.name+" parent="+r.transform.parent?.name+" gap="+gap.ToString("F1")+" position="+r.transform.position);
   }
   UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
   return "Saved targeted settings. Potential floating rocks (review, no blind move):\n"+string.Join("\n",list);
  }
  [StructLayout(LayoutKind.Sequential)]struct Performance{public uint cb;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;public uint Handles,Processes,Threads;}
  [DllImport("psapi.dll")]static extern bool GetPerformanceInfo(out Performance info,uint size);
  public static float CommitRatio(){Performance p;return GetPerformanceInfo(out p,(uint)Marshal.SizeOf<Performance>())?(float)((double)p.CommitTotal.ToUInt64()/p.CommitLimit.ToUInt64()):1;}
  public static string Audit()
  {
   var checks=new List<string>();Action<bool,string> check=(ok,label)=>checks.Add((ok?"PASS ":"FAIL ")+label);
   check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path==PrologueBuilder.ScenePath,"production scene");
   var session=Object.FindFirstObjectByType<PrologueSession>();check(session!=null && session.Content!=null && session.Player!=null && session.Wiring!=null,"content/runtime wiring");
   check(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c=>c.CompareTag("MainCamera"))==1,"one main camera");
   check(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length==1,"one audio listener");
   int missing=0,badShaders=0;long tris=0;foreach(var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)){
    foreach(var mat in r.sharedMaterials){if(mat==null)missing++;else if(mat.shader==null||ShaderUtil.ShaderHasError(mat.shader))badShaders++;}
    var f=r.GetComponent<MeshFilter>();if(f!=null&&f.sharedMesh!=null)tris+=f.sharedMesh.GetIndexCount(0)/3;
   }
   check(missing==0,"missing materials="+missing);check(badShaders==0,"shader errors="+badShaders);
   check(RenderSettings.skybox!=null && !ShaderUtil.ShaderHasError(RenderSettings.skybox.shader),"sky shader");
   check(!RenderSettings.fog,"no duplicate fog");
   PrologueBuilder.Layout();Physics.SyncTransforms();int misses=0;float slope=0;
   foreach(var p in PrologueBuilder.Main.Concat(PrologueBuilder.Branch)){
    var origin=new Vector3(p.x,PrologueBuilder.Height(p.x,p.z)+2,p.z);
    if(!Physics.Raycast(origin,Vector3.down,out var hit,4,1,QueryTriggerInteraction.Ignore))misses++;
            else {float s=Vector3.Angle(hit.normal,Vector3.up);slope=Mathf.Max(slope,s);if(s>44 && checks.Count<35)checks.Add("DETAIL steep "+p+" hit="+hit.collider.name+" at="+hit.point+" slope="+s);}}
   check(misses==0,"path ground misses="+misses);check(slope<45,"path maximum sampled slope="+slope.ToString("F2"));
   var path=new NavMeshPath();bool nav=NavMesh.CalculatePath(new Vector3(0,0,-25),new Vector3(-72,0,47),NavMesh.AllAreas,path);check(nav&&path.status==NavMeshPathStatus.PathComplete,"mine to inn NavMesh");
   string test=PrologueBuilder.Output+"/save_test.json";var store=new PrologueProgressStore(test);var state=new PrologueProgress();
   check(PrologueProgressStore.Complete(state,"loot",24)&&!PrologueProgressStore.Complete(state,"loot",24)&&state.currency==24,"one-off reward idempotency");
   PrologueProgressStore.Drop(state,new Vector3(1,2,3));check(state.currency==0&&state.dropCurrency==24,"death currency drop");
   check(PrologueProgressStore.Retrieve(state)==24&&PrologueProgressStore.Retrieve(state)==0&&state.currency==24,"retrieve exactly once");
   PrologueProgressStore.Drop(state,Vector3.one);PrologueProgressStore.Drop(state,Vector3.zero);check(state.dropCurrency==0,"second death destroys previous drop");
   state.currency=37;store.Save(state);state.currency=41;store.Save(state);var load=store.Load();check(load.currency==41&&load.completed.Contains("loot"),"disk reload latest progression");
   File.WriteAllText(test,"corrupt");check(store.Load().currency==37,"corrupt primary backup recovery");
   checks.Add("INFO all visible mesh instances (all LOD levels counted) tris="+tris);checks.Add("INFO system commit="+CommitRatio().ToString("P1"));
   string result=string.Join("\n",checks);File.WriteAllText(PrologueBuilder.Output+"/technical_static.txt",result);return result;
  }
  public static string Capture()
  {
   if(CommitRatio()>=.85f)throw new Exception("Capture stopped: system commit >=85%");
   var positions=new[]{new Vector3(0,1.8f,8),new Vector3(29,4,18),new Vector3(93,PrologueBuilder.Height(93,120)+1.8f,120),new Vector3(-92,5,62),new Vector3(80,12,132)};
   var targets=new[]{new Vector3(38,8,22),new Vector3(72,13,70),new Vector3(65,16,180),new Vector3(-72,2,38),new Vector3(71,9.5f,132)};
   var cam=NewCamera();var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);
   var prior=RenderTexture.active;var volume=Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).First(v=>v.name=="Prologue_Subtle_Post");bool enabled=volume.enabled;
   try{cam.targetTexture=rt;for(int i=0;i<positions.Length;i++)foreach(bool before in new[]{true,false}){
    if(CommitRatio()>=.85f)throw new Exception("Capture stopped: system commit >=85%");
    cam.transform.SetPositionAndRotation(positions[i],Quaternion.LookRotation(targets[i]-positions[i]));cam.clearFlags=before?CameraClearFlags.SolidColor:CameraClearFlags.Skybox;volume.enabled=!before;
    cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(PrologueBuilder.Output+"/view_"+i+(before?"_before.png":"_after.png"),tex.EncodeToPNG());
   }}finally{volume.enabled=enabled;cam.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(cam.gameObject);}
   return "10 comparison stills 1920x1080, sky/post OFF vs ON, same new geometry. No art approval inferred.";
  }
  public static Camera NewCamera(){var go=new GameObject("Prologue_Review_Camera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;return camera;}
 }
}
