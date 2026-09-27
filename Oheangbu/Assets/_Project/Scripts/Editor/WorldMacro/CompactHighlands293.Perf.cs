using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Oheangbu.App.World;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  // Play-mode frame timing on one Cheongrim section (plan verification). Game cameras are switched off so only the
  // probe's 1920x1080 camera renders, with the realm vegetation/grass observer pointed at it. The actual player collider
  // walks the section both ways (MountainTrailProbe285). Editor numbers only: not a standalone 120fps approval.
  static MountainTrailProbe285 perfProbe293;
  static double perfStart293;

  static string Perf293(int section,bool noArt=false)
  {
   RequireClean292();
   SessionState.SetBool("Highlands293.Perf",true);SessionState.SetInt("Highlands293.PerfSection",section);SessionState.SetBool("Highlands293.PerfNoArt",noArt);
   CompactLoadingStartup270.UseCurrent();EditorApplication.isPlaying=true;
   return "Play frame-timing probe scheduled on "+SectionIds293()[section]+" (results: Highlands293/Perf)";
  }
  [InitializeOnLoadMethod] static void RegisterPerf293(){EditorApplication.playModeStateChanged-=ModePerf293;EditorApplication.playModeStateChanged+=ModePerf293;}
  static void ModePerf293(PlayModeStateChange state)
  {
   // the probe starts Play in the current scene; the default lobby start is restored afterwards
   if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool("Highlands293.PerfRestore",false)){SessionState.SetBool("Highlands293.PerfRestore",false);CompactLoadingStartup270.UseLobby();}
   if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool("Highlands293.Perf",false))return;
   SessionState.SetBool("Highlands293.Perf",false);SessionState.SetBool("Highlands293.PerfRestore",true);
   perfProbe293=null;perfStart293=EditorApplication.timeSinceStartup;EditorApplication.update-=TickPerf293;EditorApplication.update+=TickPerf293;
  }
  static void TickPerf293()
  {
   if(!EditorApplication.isPlaying){EditorApplication.update-=TickPerf293;return;}
   string folder=O293+"/Perf";
   if(perfProbe293==null)
   {
    if(EditorApplication.timeSinceStartup-perfStart293<5)return;  // let the session settle its first frames
    string id=SectionIds293()[SessionState.GetInt("Highlands293.PerfSection",1)];var profile=SectionProfile293(id);
    var source=Camera.main;var cameraGo=new GameObject("Highlands293_perf_camera");var camera=cameraGo.AddComponent<Camera>();
    if(source!=null){camera.CopyFrom(source);EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());}
    foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))if(c!=camera)c.enabled=false;
    camera.enabled=true;camera.farClipPlane=Mathf.Max(camera.farClipPlane,3000);
    foreach(var art in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))art.Observer=camera;
    if(SessionState.GetBool("Highlands293.PerfNoArt",false))  // diagnosis: realm vegetation and grass renderers off
    {foreach(var art in Object.FindObjectsByType<CompactRebuildArtRenderer>(FindObjectsSortMode.None))art.enabled=false;foreach(var grass in Object.FindObjectsByType<CompactGrassRenderer266>(FindObjectsSortMode.None))grass.enabled=false;}
    var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var body=session!=null&&session.Walker!=null?session.Walker.Body:null;
    var probeGo=new GameObject("Highlands293_perf_probe");var cc=probeGo.AddComponent<CharacterController>();cc.enabled=false;
    if(body!=null){cc.height=body.height;cc.center=body.center;cc.radius=body.radius;cc.stepOffset=body.stepOffset;cc.skinWidth=body.skinWidth;cc.slopeLimit=body.slopeLimit;}
    else{cc.height=1.75f;cc.center=Vector3.up*.875f;cc.radius=.28f;cc.stepOffset=.3f;cc.slopeLimit=45;}
    probeGo.transform.position=profile.At(0)+Vector3.up*.15f;
    perfProbe293=probeGo.AddComponent<MountainTrailProbe285>();perfProbe293.Profile=profile;perfProbe293.View=camera;perfProbe293.Output=Path.GetFullPath(folder);
    cc.enabled=true;Application.runInBackground=true;return;
   }
   if(perfProbe293.Result==null)
   {
    if(EditorApplication.timeSinceStartup-perfStart293>420){Directory.CreateDirectory(folder);File.WriteAllText(folder+"/walk-play.txt","TIMEOUT after 420s");EditorApplication.update-=TickPerf293;EditorApplication.isPlaying=false;}
    return;
   }
   Directory.CreateDirectory(folder);File.WriteAllText(folder+"/walk-play.txt",perfProbe293.Result);EditorApplication.update-=TickPerf293;perfProbe293=null;EditorApplication.isPlaying=false;
  }
 }
}
