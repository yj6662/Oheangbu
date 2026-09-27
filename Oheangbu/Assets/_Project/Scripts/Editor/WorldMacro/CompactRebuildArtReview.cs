using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static WorldMacroPlaytestSession ArtSession()
  {
   if(!EditorApplication.isPlaying||string.IsNullOrEmpty(SessionState.GetString("CompactSlice.CheckSuffix","")))throw new Exception("Private slice diagnostic Play required");
   return SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  }
  static Camera ArtCamera()
  {
   var session=ArtSession();foreach(var actor in session.Actors)actor.gameObject.SetActive(false);
   if(!session.Walker.Seated)session.Walker.Suspend();return session.Walker.ViewCamera;
  }
  static string ArtView(int index)
  {
   var camera=ArtCamera();var trail=JsonUtility.FromJson<WalkReceipt>(File.ReadAllText(Output+"/migration_walk.json")).trail;
   int at=index==0?12:index==1?44:index==2?90:trail.Length-8;
   at=Mathf.Clamp(at,0,trail.Length-2);var eye=trail[at]+Vector3.up*1.7f;
   var target=trail[Mathf.Min(trail.Length-1,at+9)]+Vector3.up*1.5f;
   if(index==3){eye=ArtSession().Content.InnCheckpointFeet+new Vector3(-9,2.2f,-12);target=ArtSession().Content.InnCheckpointFeet+new Vector3(0,3.5f,5);}
   camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));camera.fieldOfView=62;
   File.WriteAllText(Output+"/art_view_"+index+".json",JsonUtility.ToJson(new ArtPose{position=eye,rotation=camera.transform.eulerAngles,fieldOfView=camera.fieldOfView},true));
   return "Art view "+index+" at "+eye+"; render a frame before capture";
  }
  [Serializable] class ArtPose {public Vector3 position,rotation;public float fieldOfView;}
  static string ArtCapture(int index)
  {
   var camera=ArtCamera();var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;
   var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();Directory.CreateDirectory(Output+"/ArtCaptures");File.WriteAllBytes(Output+"/ArtCaptures/"+index+".png",image.EncodeToPNG());return "Captured actual Unity camera with active post: "+index;}
   finally{camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
  }
  static string ArtAudit()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var receipt=JsonUtility.FromJson<MigrationReceipt>(File.ReadAllText(Output+"/migration_slice.json"));var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(receipt.scene,OpenSceneMode.Additive);
   try{
    var roots=scene.GetRootGameObjects();var art=roots.SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();var sheet=art.Sheet;var lines=new List<string>();
    void Check(bool ok,string label){lines.Add((ok?"PASS ":"FAIL ")+label);}
    Check(art.Observer!=null&&art.Observer.gameObject.scene==scene,"explicit candidate camera binding");
    Check(roots.Count(g=>g.name=="Rebuild_EnvironmentArt")==1,"one environment root");
    Check(sheet.FixedPlacements.Select(p=>p.Id).Distinct().Count()==sheet.FixedPlacements.Length,"unique placement IDs");
    Check(sheet.FixedPlacements.All(p=>sheet.Prototypes.Any(t=>t.Id==p.PrototypeId)),"all placement prototypes resolve");
    var materials=sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material).Concat(roots.SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).SelectMany(r=>r.sharedMaterials)).Where(m=>m!=null).Distinct().ToArray();
    Check(sheet.Prototypes.All(p=>p.Lods.Length>=2&&p.Lods.All(l=>l.Parts.Length>0&&l.Parts.All(m=>m.Mesh!=null&&m.Material!=null))),"all prototype meshes/materials and LODs exist");
    var foliage=sheet.Prototypes.Where(p=>p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Tree||p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Grass||p.Category==Oheangbu.Data.World.WorldMacroDressingSheetSO.Kind.Shrub).SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).Select(p=>p.Material).Distinct().ToArray();
    Check(foliage.All(m=>m.FindPass("ShadowCaster")>=0&&m.FindPass("DepthOnly")>=0&&m.FindPass("DepthNormals")>=0),"vegetation ShadowCaster, DepthOnly, DepthNormals passes");
    Check(materials.All(m=>m.shader!=null&&m.shader.isSupported&&!ShaderUtil.GetShaderMessages(m.shader).Any(e=>e.severity.ToString()=="Error")),"supported shaders; no compiled shader errors");
    // Atmosphere238 also owns private materials under the same candidate root.
    Check(sheet.Prototypes.SelectMany(p=>p.Lods).SelectMany(l=>l.Parts).All(p=>AssetDatabase.GetAssetPath(p.Material).StartsWith(Path.GetDirectoryName(receipt.scene).Replace('\\','/')+"/",StringComparison.Ordinal)),"all dressing materials private to candidate");
    var soil=roots.SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Single(m=>m.name=="Worn path");Check(soil.sharedMesh.bounds.max.y<71,"worn soil restricted below mountain shoulder");
    int missing=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));Check(missing==0,"no missing scene scripts");
    foreach(var p in sheet.Prototypes)lines.Add(p.Id+" LOD triangles: "+string.Join(" / ",p.Lods.Select(l=>l.Parts.Sum(m=>(long)m.Mesh.GetIndexCount(m.Submesh)/3))));
    lines.Add("Placements="+sheet.FixedPlacements.Length+"; art colliders="+art.GetComponentsInChildren<Collider>(true).Length+". Render evidence and traversal are separate reports; no artistic approval implied.");
    var report=string.Join("\n",lines);File.WriteAllText(Output+"/art_audit.txt",report);return report;
   }finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
  }
  static EditorApplication.CallbackFunction artMotionTick;
  static string ArtMotion()
  {
   if(artMotionTick!=null||artMeasureTick!=null)throw new Exception("Finish current camera diagnostic first");
   var camera=ArtCamera();var art=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();
   var tree=art.Sheet.FixedPlacements.Single(p=>p.Id=="inn_meshy_0");var target=tree.Position+Vector3.up*4.8f;
   string folder=Output+"/Motion";Directory.CreateDirectory(folder);int frame=0;int last=-1;
   File.WriteAllText(folder+"/status.txt","RUNNING: scripted camera retreat 12–200m across Meshy pine LOD transitions; actual Unity renders, no gameplay input.");
   artMotionTick=()=>{
    if(!EditorApplication.isPlaying||camera==null){EditorApplication.update-=artMotionTick;artMotionTick=null;File.WriteAllText(folder+"/status.txt","INTERRUPTED");return;}
    if(last==Time.renderedFrameCount)return;last=Time.renderedFrameCount;
    float distance=Mathf.Lerp(12,200,frame/119f);camera.transform.position=target+new Vector3(-.22f,.04f,-1).normalized*distance;camera.transform.LookAt(target);
    var previous=camera.targetTexture;var active=RenderTexture.active;float aspect=camera.aspect;var rt=new RenderTexture(960,540,24);var image=new Texture2D(960,540,TextureFormat.RGB24,false);
    try{camera.targetTexture=rt;camera.aspect=16f/9;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(folder+"/"+frame.ToString("D3")+".jpg",image.EncodeToJPG(90));}
    finally{camera.targetTexture=previous;camera.aspect=aspect;RenderTexture.active=active;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);}
    if(++frame>=120){EditorApplication.update-=artMotionTick;artMotionTick=null;File.WriteAllText(folder+"/status.txt","CAPTURED 120 actual Unity frames, camera retreat 12–200m across 32–52m mesh transition and 130–170m card transition. Playback 30fps is a diagnostic montage, not measured real-time playback. Human temporal art approval pending.");}
   };EditorApplication.update+=artMotionTick;return "Recording isolated LOD camera dolly";
  }
  [Serializable] class ArtPerformance
  {public string status,scope,device,unityVersion;public int width,height;public int frames,gpuSamples,cpuTimingSamples;public double frameMedianMs,frameP95Ms,mainThreadMedianMs,cpuTimingMedianMs,gpuMedianMs;public int maximumDrawCalls,maximumVisibleInstances;public long maximumSubmittedTriangles;}
  static EditorApplication.CallbackFunction artMeasureTick;
  public static string VillagePerformance()=>ArtMeasure(true);
  static string ArtMeasure(bool village=false)
  {
   if(artMeasureTick!=null)throw new Exception("Art measurement already running");
   if(village)Oheangbu.App.World.UI.PlaytestUiRoot.Instance.CloseMenu();
   var owner=ArtSession();var returnFeet=owner.Walker.Body.transform.position;float returnYaw=owner.Walker.Body.transform.eulerAngles.y;
   string measurementFile=village?VillageOutput+"/performance.json":Output+"/art_performance.json";
   var camera=ArtCamera();var trail=village?JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(VillageOutput+"/ledger.json")).main:JsonUtility.FromJson<WalkReceipt>(File.ReadAllText(Output+"/migration_walk.json")).trail;
   var art=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>(true)).Single();
   var times=new List<double>();var cpu=new List<double>();var main=new List<double>();var gpu=new List<double>();var timing=new FrameTiming[1];var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
   int previous=-1,frames=0;ulong timingTimestamp=0;var result=new ArtPerformance{device=SystemInfo.graphicsDeviceName,unityVersion=Application.unityVersion,width=camera.pixelWidth,height=camera.pixelHeight,status="RUNNING",scope="Unity Editor Game camera, 600 rendered frames, scripted route sweep (not walking speed). Editor/UI overhead included. Enemy actors disabled; player controller suspended for art camera, excludes active combat. Zero GPU samples means unavailable. No standalone 120fps acceptance."};
   File.WriteAllText(measurementFile,JsonUtility.ToJson(result,true));
   double Median(List<double> values,float percentile=.5f){if(values.Count==0)return -1;values.Sort();return values[Mathf.Clamp((int)(values.Count*percentile),0,values.Count-1)];}
   void Finish(string status){result.status=status;result.frames=times.Count;result.frameMedianMs=Median(times);result.frameP95Ms=Median(times,.95f);result.mainThreadMedianMs=Median(main);result.cpuTimingSamples=cpu.Count;result.cpuTimingMedianMs=Median(cpu);result.gpuSamples=gpu.Count;result.gpuMedianMs=Median(gpu);File.WriteAllText(measurementFile,JsonUtility.ToJson(result,true));recorder.Dispose();EditorApplication.update-=artMeasureTick;artMeasureTick=null;if(village&&EditorApplication.isPlaying&&owner!=null)owner.Walker.Resume(returnFeet,returnYaw);}
   artMeasureTick=()=>{
    if(!EditorApplication.isPlaying||camera==null){Finish("INTERRUPTED");return;}
    if(Time.renderedFrameCount==previous)return;previous=Time.renderedFrameCount;
    if(frames++>=30){times.Add(Time.unscaledDeltaTime*1000);if(recorder.Valid&&recorder.LastValue>0)main.Add(recorder.LastValue/1000000d);FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].frameStartTimestamp!=timingTimestamp){timingTimestamp=timing[0].frameStartTimestamp;if(timing[0].cpuFrameTime>0)cpu.Add(timing[0].cpuFrameTime);if(timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);}result.maximumDrawCalls=Math.Max(result.maximumDrawCalls,art.DrawCalls);result.maximumVisibleInstances=Math.Max(result.maximumVisibleInstances,art.VisibleInstances);result.maximumSubmittedTriangles=Math.Max(result.maximumSubmittedTriangles,art.SubmittedTriangles);}
    float t=Mathf.Clamp01((frames-30)/600f)*(trail.Length-12);int at=Mathf.Clamp((int)t+6,0,trail.Length-2);var eye=Vector3.Lerp(trail[at],trail[at+1],t-Mathf.Floor(t))+Vector3.up*1.7f;var target=trail[Mathf.Min(at+7,trail.Length-1)]+Vector3.up*1.5f;camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(target-eye));
    if(times.Count>=600)Finish("MEASURED_EDITOR_ONLY");
   };EditorApplication.update+=artMeasureTick;return "Camera route measurement started; read art_performance.json after completion";
  }
 }
}
