using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 [InitializeOnLoad]public static class FireHaetaeReview
 {
  [Serializable]public class Clip{public string view,folder;public int frames;public bool ended;public float commitRatio,footError;}
  [Serializable]public class Report{public string status,mvid,limitation="Static exterior / summon VFX only. No rig, gameplay summon, AI or combat.";public List<Clip> clips=new List<Clip>();public List<string> errors=new List<string>();}
  static Report report;static Clip clip;static GameObject host,cameraGo;static Vfx120Effect fx;static Camera camera;static RenderTexture rt;static Texture2D image;static Vector3 origin,forward;static int frame,view;static double next;
  static FireHaetaeReview(){EditorApplication.update+=Tick;}
  static void Save(){Directory.CreateDirectory(FireHaetaeBuild.Output);File.WriteAllText(Path.Combine(FireHaetaeBuild.Output,"capture.json"),JsonUtility.ToJson(report,true));}
  public static string Start(bool externalOnly=false)
  {
   if(report!=null||EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");if(SceneManager.GetActiveScene().path!="Assets/_Project/Scenes/Dev/C2_CodexWorld.unity")throw new Exception("C2 required");if(FixedWardReview.Memory()>=.85f)throw new Exception("Memory >=85%");
   Report previous=externalOnly?JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(FireHaetaeBuild.Output,"capture.json"))):null;
   if(externalOnly&&(previous==null||!previous.clips.Any(c=>c.view=="play"&&c.frames==132&&c.ended)))throw new Exception("Completed current play clip required");
   origin=Camera.main.transform.position;forward=Vector3.ProjectOnPlane(Camera.main.transform.forward,Vector3.up).normalized;view=externalOnly?1:0;report=new Report{status="RUNNING",mvid=typeof(FireHaetaeVfx).Module.ModuleVersionId.ToString()};
   if(externalOnly)report.clips.Add(previous.clips.First(c=>c.view=="play"));Save();return "FIRE_HAETAE_CAPTURE_STARTED";
  }
  static void BeginClip()
  {
   if(FixedWardReview.Memory()>=.85f)throw new Exception("Memory >=85%; next clip stopped");host=new GameObject("FireHaetaeReview");fx=host.AddComponent<Vfx120Effect>();fx.Profile=FireHaetaeBuild.Profile;fx.PreviewControlled=true;fx.Begin(origin,null,origin+forward*10,Color.white);
   if(!fx.FireHaetae.Grounded)throw new Exception("Placement ground missing or >15cm deviation");
   cameraGo=new GameObject("FireHaetaeReviewCamera");camera=cameraGo.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.targetTexture=null;camera.aspect=16f/9;camera.fieldOfView=65;camera.nearClipPlane=.08f;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
   rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
   string name=view==0?"play":"external";clip=new Clip{view=name,folder=Path.Combine(FireHaetaeBuild.Output,name),commitRatio=FixedWardReview.Memory(),footError=fx.FireHaetae.FootError};Directory.CreateDirectory(clip.folder);report.clips.Add(clip);frame=0;Save();
  }
  static void Tick()
  {
   if(report==null||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.045;
   try
   {
    if(clip==null){if(view==2){report.status="PASS_DIAGNOSTIC_VISUAL_PENDING";Save();report=null;return;}BeginClip();return;}
    float memory=FixedWardReview.Memory();clip.commitRatio=Mathf.Max(clip.commitRatio,memory);if(memory>=.85f)throw new Exception("Memory >=85%; stopped with partial frames preserved");
    float age=frame/24f;fx.Sample(age);var center=fx.FireHaetae.Centre;
    if(view==0){camera.transform.position=origin+Vector3.Cross(Vector3.up,forward)*(.4f*Mathf.Sin(age*.65f));camera.transform.rotation=Quaternion.LookRotation(forward);}
    else{camera.transform.position=center+forward*4.5f+Vector3.Cross(Vector3.up,forward)*4+Vector3.up*2.5f;camera.transform.LookAt(center+Vector3.up*1.25f);}
    var old=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(Path.Combine(clip.folder,frame.ToString("D4")+".jpg"),image.EncodeToJPG(88));}finally{camera.targetTexture=null;RenderTexture.active=old;}
    frame++;clip.frames=frame;if(frame%12==0){GC.Collect();Save();}
    if(frame<132)return;
    clip.ended=fx.FireHaetae.Ended&&fx.FireHaetae.LiveParticles==0&&fx.FireHaetae.VisibleRenderers==0;if(!clip.ended)throw new Exception("End residue");Cleanup();clip=null;view++;Save();GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();
   }catch(Exception e){report.status="INCOMPLETE";report.errors.Add(e.ToString());Cleanup();Save();report=null;clip=null;}
  }
  static void Cleanup(){if(host!=null)Object.DestroyImmediate(host);if(cameraGo!=null)Object.DestroyImmediate(cameraGo);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);host=cameraGo=null;fx=null;camera=null;rt=null;image=null;GC.Collect();}
  [Serializable]class AuditResult{public string status;public List<string> passed=new List<string>(),errors=new List<string>();public float maxFootGap;}
  public static string Audit()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||report!=null)throw new Exception("Stopped editor required");var result=new AuditResult();var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.name="FireHaetaeDiagnosticGround";ground.layer=6;ground.transform.position=new Vector3(7000,-.5f,7000);ground.transform.localScale=new Vector3(30,1,30);Physics.SyncTransforms();
   try
   {
    foreach(int fps in new[]{30,60,120})foreach(float rate in new[]{1f,.2f})
    {
     var go=new GameObject("HaetaeAudit");try{
      var effect=go.AddComponent<Vfx120Effect>();effect.Profile=FireHaetaeBuild.Profile;effect.PreviewControlled=true;effect.Begin(new Vector3(7000,1.6f,7000),null,new Vector3(7000,1.6f,7010),Color.white);var haetae=effect.FireHaetae;
      if(!haetae.Grounded)throw new Exception("Ground placement");result.passed.Add("ground "+fps+"/"+rate);
      if(go.GetComponentsInChildren<Collider>().Length!=0||go.GetComponentsInChildren<Animator>().Length!=0)throw new Exception("Unexpected gameplay or rig");result.passed.Add("no collider/rig "+fps+"/"+rate);
      var original=haetae.Centre;var filters=go.GetComponentsInChildren<MeshFilter>().Where(x=>x.name.Contains("FireHaetae_Body")).ToArray();var originalVerts=filters.Select(x=>x.sharedMesh.vertices).ToArray();
      foreach(var foot in haetae.FeetWorld())
      {
       var vertices=filters.SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).Where(v=>Vector2.Distance(new Vector2(v.x,v.z),new Vector2(foot.x,foot.z))<.12f).ToArray();
       float bottom=vertices.Min(v=>v.y);if(Mathf.Abs(bottom)>.025f)throw new Exception("Actual paw mesh gap >2.5cm: "+bottom);
      }
      result.passed.Add("actual paw mesh ground <2.5cm "+fps+"/"+rate);
      for(float t=0;t<4.7f;t+=rate/fps){effect.Sample(t);if(haetae.Centre!=original)throw new Exception("Drifting summon");}
      result.passed.Add("clock life "+fps+"/"+rate);if(!haetae.Ended||haetae.LiveParticles!=0||haetae.VisibleRenderers!=0)throw new Exception("End residue");result.passed.Add("clean end "+fps+"/"+rate);
      for(int i=0;i<filters.Length;i++)if(!filters[i].sharedMesh.vertices.SequenceEqual(originalVerts[i]))throw new Exception("Body deformed over time");result.passed.Add("no scaling or gait "+fps+"/"+rate);
      go.transform.position+=Vector3.right*4;effect.Sample(4.8f);if(Vector3.Distance(go.transform.Find("FireHaetaePresentation").position,original)>.001f)throw new Exception("Follows host");result.passed.Add("world anchoring "+fps+"/"+rate);
      foreach(var point in haetae.FeetWorld()){if(!ground.GetComponent<Collider>().Raycast(new Ray(point+Vector3.up,Vector3.down),out var hit,2))throw new Exception("Missing paw ground");float gap=Mathf.Abs(point.y-hit.point.y);result.maxFootGap=Mathf.Max(result.maxFootGap,gap);if(gap>.02f)throw new Exception("Hoof gap >2cm");}result.passed.Add("paw anchors <2cm "+fps+"/"+rate);
     }finally{Object.DestroyImmediate(go);}
    }
    result.status="PASS";
   }catch(Exception e){result.status="FAIL";result.errors.Add(e.ToString());}
   finally{Object.DestroyImmediate(ground);}
   File.WriteAllText(Path.Combine(FireHaetaeBuild.Output,"audit.json"),JsonUtility.ToJson(result,true));return result.status+" "+result.passed.Count+" checks "+string.Join(";",result.errors);
  }
 }
}
