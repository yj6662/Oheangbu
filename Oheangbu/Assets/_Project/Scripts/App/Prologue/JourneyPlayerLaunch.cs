using System;
using System.IO;
using System.Linq;
using UnityEngine.AI;
using UnityEngine;
using UnityEngine.Rendering;
namespace Oheangbu.App.Prologue {
 // Added to the build copy only. Ordinary launches use the existing textless pause menu.
 public sealed class JourneyPlayerLaunch : MonoBehaviour {
  public PrologueSession Session;
  public ProloguePauseMenu Menu;
  string reportPath,slotPath;
  bool smoke,continuation,finished,capture;
  int capturePhase;float captureAt;
  string menuCapture,playCapture,offscreenMenuCapture;
  bool offscreenMenuNonblank;
  float started;
  PrologueProgress prior;
  [Serializable] sealed class Result { public bool passed,continuation,loaded,paused,resumed,saved,navigationReady,capturesNonblank,offscreenSceneNonblank,offscreenMenuNonblank; public string scene,error,slot,menuCapture,playCapture,offscreenSceneCapture,offscreenMenuCapture; public int width,height; }
  void Awake(){
   var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-journey-smoke-report");smoke=i>=0&&i+1<args.Length;
   if(smoke)reportPath=Path.GetFullPath(args[i+1]);continuation=Array.IndexOf(args,"-journey-smoke-continue")>=0;
   capture=smoke&&Array.IndexOf(args,"-journey-smoke-capture")>=0;
   if(capture){Application.runInBackground=true;menuCapture=reportPath+".menu.png";playCapture=reportPath+".play.png";Directory.CreateDirectory(Path.GetDirectoryName(reportPath));}
   Session.TestSaveSuffix=smoke?"-standalone-smoke":"-standalone";
   slotPath=Path.Combine(Application.persistentDataPath,Session.Content.SaveSlot+Session.TestSaveSuffix+".json");
   if(smoke)prior=new PrologueProgressStore(slotPath).Load();started=Time.realtimeSinceStartup;
  }
  void Update(){
   if(finished)return;
   if(smoke&&Time.realtimeSinceStartup-started>45){Finish(new Result{error="Startup or capture timeout"});return;}
   if(Session.Progress==null){if(smoke&&Time.realtimeSinceStartup-started>45)Finish(new Result{error="Session initialization timeout"});return;}
   if(!smoke){Menu.SetOpen(true);finished=true;enabled=false;return;}
   if(Time.realtimeSinceStartup-started<2)return;
   if(capture){
    if(capturePhase==0){Menu.SetOpen(true);captureAt=Time.realtimeSinceStartup+1;capturePhase=1;return;}
    if(capturePhase==1){if(Time.realtimeSinceStartup<captureAt)return;try{offscreenMenuCapture=reportPath+".menu-scene.png";Canvas.ForceUpdateCanvases();offscreenMenuNonblank=CaptureScene(offscreenMenuCapture);ScreenCapture.CaptureScreenshot(menuCapture);}catch(Exception e){Finish(new Result{error=e.ToString()});return;}capturePhase=2;return;}
    if(capturePhase==2){if(!File.Exists(menuCapture))return;Menu.SetOpen(false);captureAt=Time.realtimeSinceStartup+1;capturePhase=3;return;}
    if(capturePhase==3){if(Time.realtimeSinceStartup<captureAt)return;ScreenCapture.CaptureScreenshot(playCapture);capturePhase=4;return;}
    if(!File.Exists(playCapture))return;
   }
   var r=new Result{continuation=continuation,scene=gameObject.scene.path,slot=slotPath,menuCapture=menuCapture,playCapture=playCapture,width=Screen.width,height=Screen.height,offscreenMenuCapture=offscreenMenuCapture,offscreenMenuNonblank=offscreenMenuNonblank};
   try{
    r.loaded=!continuation||prior!=null&&prior.hasPosition&&Session.Progress.checkpoint==prior.checkpoint&&Session.Progress.currency==prior.currency&&Vector3.Distance(Session.Player.position,prior.position)<1;
    Menu.SetOpen(true);r.paused=Menu.IsOpen&&Time.timeScale==0;Menu.SetOpen(false);r.resumed=!Menu.IsOpen&&Time.timeScale>0;
    r.navigationReady=Session.Encounters.Where(a=>a!=null&&a.gameObject.activeInHierarchy).All(a=>a.GetComponent<NavMeshAgent>()!=null&&a.GetComponent<NavMeshAgent>().isOnNavMesh);
    r.saved=Menu.SaveProgress()&&new PrologueProgressStore(slotPath).Load()?.hasPosition==true;
    r.capturesNonblank=!capture||(NonblankFile(menuCapture)&&NonblankFile(playCapture));
    if(capture){r.offscreenSceneCapture=reportPath+".scene.png";r.offscreenSceneNonblank=CaptureScene(r.offscreenSceneCapture);}
    r.passed=r.capturesNonblank&&r.loaded&&r.paused&&r.resumed&&r.saved&&r.navigationReady&&Menu.Textless&&Session.Player!=null;
   }catch(Exception e){r.error=e.ToString();}Finish(r);
  }
  static bool Nonblank(Color32[] pixels){byte lo=255,hi=0;for(int i=0;i<pixels.Length;i+=13){var p=pixels[i];lo=Math.Min(lo,Math.Min(p.r,Math.Min(p.g,p.b)));hi=Math.Max(hi,Math.Max(p.r,Math.Max(p.g,p.b)));}return hi-lo>16;}
  static bool NonblankFile(string path){var t=new Texture2D(2,2,TextureFormat.RGBA32,false);try{return ImageConversion.LoadImage(t,File.ReadAllBytes(path))&&Nonblank(t.GetPixels32());}finally{Destroy(t);}}
  static bool CaptureScene(string path){
   var camera=Camera.main;if(camera==null)return false;
   var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);var active=RenderTexture.active;float aspect=camera.aspect;
   try{target.Create();camera.aspect=1920f/1080;for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());return Nonblank(texture.GetPixels32());}
   finally{camera.aspect=aspect;RenderTexture.active=active;target.Release();Destroy(target);Destroy(texture);}
  }
  void Finish(Result result){finished=true;Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,JsonUtility.ToJson(result,true));Application.Quit(result.passed?0:1);}
 }
}
