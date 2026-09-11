using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 [InitializeOnLoad] public static class FixedWardReview
 {
  [StructLayout(LayoutKind.Sequential)] struct Perf{public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;public uint Handles,Processes,Threads;}
  [DllImport("psapi.dll")]static extern bool GetPerformanceInfo(out Perf p,uint size);
  public static float Memory(){if(!GetPerformanceInfo(out var p,(uint)Marshal.SizeOf<Perf>()))throw new Exception("Memory measurement unavailable");return (float)((double)p.CommitTotal.ToUInt64()/p.CommitLimit.ToUInt64());}
  [Serializable] public class Row{public string view,folder,status;public int frames,contacts,peakParticles;public bool ended;public float commitRatio;public Vector3 centre,camera;}
  [Serializable] public class Report{public string glyph,status,scene,mvid,limitation="VFX diagnostic only. Scripted projectile contacts, no gameplay blocking, damage reduction or parry. Editor-time capture, not a performance benchmark.";public List<Row> clips=new List<Row>();public List<string> errors=new List<string>();}
  static Report report;static Row row;static Vfx120Effect fx;static GameObject host,cameraGo,projectile;static Camera camera;static RenderTexture rt;static Texture2D image;static Material projectileMaterial;static int viewIndex,frame;static bool[] fired;static Vector3 centre,forward,eye;static Quaternion rotation;
  static FixedWardReview(){EditorApplication.update+=Tick;}
  public static string Start(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||report!=null)throw new Exception("Stopped editor with no active capture required");
   if(SceneManager.GetActiveScene().path!="Assets/_Project/Scenes/Dev/C2_CodexWorld.unity")throw new Exception("C2 required");
   if(Memory()>=.85f)throw new Exception("Commit >=85%; new capture stopped");
   var source=Camera.main;if(source==null)throw new Exception("Main camera missing");forward=Vector3.ProjectOnPlane(source.transform.forward,Vector3.up).normalized;rotation=Quaternion.LookRotation(forward);centre=source.transform.position+forward*18;
   if(Physics.Raycast(centre+Vector3.up*10,Vector3.down,out var ground,40,LayerMask.GetMask("Default","WorldGround","WorldRibbon","WorldRidge"),QueryTriggerInteraction.Ignore))centre=ground.point;
   eye=centre+Vector3.up*1.65f;
   report=new Report{glyph=glyph,status="RUNNING",scene=SceneManager.GetActiveScene().path,mvid=typeof(FixedWardVfx).Module.ModuleVersionId.ToString()};viewIndex=0;Save();return "WARD_CAPTURE_STARTED "+glyph;
  }
  static void BeginClip()
  {
   if(Memory()>=.85f)throw new Exception("Commit >=85%; next clip stopped");
   string view=viewIndex==0?"play":"external";string folder=Path.Combine(FixedWardBuild.Output,report.glyph,view);Directory.CreateDirectory(folder);
   row=new Row{view=view,folder=folder,status="RUNNING",centre=centre,commitRatio=Memory()};report.clips.Add(row);frame=0;fired=new bool[3];
   host=new GameObject("WardReviewEffect");fx=host.AddComponent<Vfx120Effect>();fx.Profile=FixedWardBuild.Profile(report.glyph);fx.PreviewControlled=true;fx.Begin(centre,null,centre+forward*8,Color.white);
   cameraGo=new GameObject("WardReviewCamera");camera=cameraGo.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.targetTexture=null;camera.aspect=16f/9;camera.fieldOfView=65;camera.nearClipPlane=.05f;
   var data=camera.GetUniversalAdditionalCameraData();data.requiresColorTexture=true;data.requiresDepthTexture=true;data.renderPostProcessing=true;
   rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);rt.Create();image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
   projectile=GameObject.CreatePrimitive(PrimitiveType.Sphere);projectile.name="DiagnosticProjectile";Object.DestroyImmediate(projectile.GetComponent<Collider>());projectile.transform.localScale=Vector3.one*.18f;projectileMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));projectileMaterial.color=new Color(.84f,.65f,.35f);projectile.GetComponent<Renderer>().sharedMaterial=projectileMaterial;
   Save();
  }
  static void Tick()
  {
   if(report==null||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
   try
   {
    if(row==null){if(viewIndex==2){report.status="PASS_VFX_DIAGNOSTIC_VISUAL_PENDING";Save();report=null;return;}BeginClip();return;}
    if(Memory()>=.85f)throw new Exception("Commit >=85%; capture stopped and resources released");
    float t=frame/24f;fx.Sample(t);var ward=fx.FixedWard;
    projectile.SetActive(false);
    for(int i=0;i<3;i++)
    {
     float hit=1.1f+i*.9f;int panel=i==0?0:i==1?ward.PanelCount-1:2;var point=ward.ContactPoint(panel);var normal=ward.ContactNormal(panel);
     if(t>=hit-.5f&&t<hit){projectile.SetActive(true);projectile.transform.position=Vector3.Lerp(point+normal*2.7f,point,Mathf.InverseLerp(hit-.5f,hit,t));}
     if(t>=hit&&!fired[i]){if(!ward.ContactAt(100+i,point,normal))throw new Exception("Diagnostic contact rejected");fired[i]=true;}
    }
    fx.Sample(t);row.contacts=ward.AcceptedContacts;row.peakParticles=Mathf.Max(row.peakParticles,ward.LiveParticles);
    if(viewIndex==0)
    {
     // Exit and re-enter the stationary ward to reveal camera-side opacity and anchoring.
     float move=t<2.4f?Mathf.Sin(t*1.1f)*.35f:t<3.25f?Mathf.Lerp(0,3.5f,Mathf.InverseLerp(2.4f,3.25f,t)):Mathf.Lerp(3.5f,0,Mathf.InverseLerp(3.25f,3.65f,t));
     camera.transform.SetPositionAndRotation(eye+forward*move,rotation);
    }
    else{camera.transform.position=centre-forward*7+Vector3.Cross(Vector3.up,forward)*5+Vector3.up*4.6f;camera.transform.LookAt(centre+Vector3.up*.7f);}
    row.camera=camera.transform.position;Capture(Path.Combine(row.folder,frame.ToString("D4")+".jpg"));frame++;row.frames=frame;
    if(frame<144)return;
    row.ended=ward.Ended&&ward.VisiblePanels==0&&ward.LiveParticles==0&&ward.LiveContacts==0;
    if(!row.ended||row.contacts!=3)throw new Exception("Contact/end verification failed");
    row.status="PASS_VFX_DIAGNOSTIC_VISUAL_PENDING";Cleanup();row=null;viewIndex++;Save();GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();
   }catch(Exception e){report.errors.Add(e.ToString());report.status="FAIL";Cleanup();Save();report=null;row=null;}
  }
  static void Capture(string path)
  {
   var old=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToJPG(90));}finally{camera.targetTexture=null;RenderTexture.active=old;}
  }
  static void Save(){var path=Path.Combine(FixedWardBuild.Output,report.glyph);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"report.json"),JsonUtility.ToJson(report,true));}
  static void Cleanup(){if(host!=null)Object.DestroyImmediate(host);if(cameraGo!=null)Object.DestroyImmediate(cameraGo);if(projectile!=null)Object.DestroyImmediate(projectile);if(projectileMaterial!=null)Object.DestroyImmediate(projectileMaterial);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}if(image!=null)Object.DestroyImmediate(image);host=cameraGo=projectile=null;fx=null;camera=null;rt=null;image=null;}
  [Serializable]class AuditReport{public string status,glyph;public List<string> checks=new List<string>(),errors=new List<string>();public int maxParticles,groundQueries;public float groundError;}
  static void Need(AuditReport r,bool pass,string label){if(!pass)throw new Exception(label);r.checks.Add(label);}
  public static string Audit(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||report!=null)throw new Exception("Stopped editor required");var result=new AuditReport{glyph=glyph};GameObject hill=null;Mesh mesh=null;
   try
   {
    if(Memory()>=.85f)throw new Exception("Commit >=85%");
    hill=new GameObject("WardAuditSlope");hill.layer=6;hill.transform.position=new Vector3(5000,0,5000);
    mesh=new Mesh();var vertices=new Vector3[17*17];var triangles=new List<int>();
    for(int z=0;z<17;z++)for(int x=0;x<17;x++){float xx=x*.6f-4.8f,zz=z*.6f-4.8f;int ix=z*17+x;vertices[ix]=new Vector3(xx,zz*.15f+.13f*Mathf.Sin(xx),zz);if(x<16&&z<16)triangles.AddRange(new[]{ix,ix+17,ix+1,ix+1,ix+17,ix+18});}
    mesh.vertices=vertices;mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();hill.AddComponent<MeshCollider>().sharedMesh=mesh;Physics.SyncTransforms();
    foreach(int fps in new[]{30,60,120})foreach(float speed in new[]{1f,.2f})
    {
     var go=new GameObject("WardAudit");try
     {
      var effect=go.AddComponent<Vfx120Effect>();effect.Profile=FixedWardBuild.Profile(glyph);effect.PreviewControlled=true;effect.Begin(hill.transform.position,null,hill.transform.position+Vector3.forward*8,Color.white);var w=effect.FixedWard;Vector3 fixedCentre=w.Centre;int queries=w.GroundQueries;
      Need(result,w.MissingGround==0,"all slope ground samples "+fps+"/"+speed);
      Need(result,w.PanelCount==(glyph=="구"?6:8),"panel count "+fps+"/"+speed);
      Need(result,!w.ContactAt(1,w.ContactPoint(0),w.ContactNormal(0)),"no contact before formation "+fps+"/"+speed);
      for(float t=0;t<1;t+=speed/fps)effect.Sample(t);effect.Sample(1);
      float worst=0;foreach(var filter in go.GetComponentsInChildren<MeshFilter>().Where(x=>x.name=="GroundFollowingMembrane"))
      {
       var v=filter.sharedMesh.vertices;var uv=filter.sharedMesh.uv;
       // The first 17 columns are the exterior contact edge; the inward thickness is a solid wall volume.
       for(int k=0;k<17;k++){var point=filter.transform.TransformPoint(v[k]);if(!hill.GetComponent<MeshCollider>().Raycast(new Ray(point+Vector3.up*10,Vector3.down),out var groundHit,20))throw new Exception("Missing audit ground");worst=Mathf.Max(worst,Mathf.Abs(point.y-groundHit.point.y-.012f));}
      }
      result.groundError=Mathf.Max(result.groundError,worst);Need(result,worst<.02f,"membrane ground edge within2cm "+fps+"/"+speed);
      Need(result,w.ContactAt(1,w.ContactPoint(0),w.ContactNormal(0)),"first accepted contact "+fps+"/"+speed);
      Need(result,!w.ContactAt(1,w.ContactPoint(0),w.ContactNormal(0)),"duplicate rejected "+fps+"/"+speed);
      Need(result,!w.ContactAt(3,w.Centre,Vector3.up),"out of surface rejected "+fps+"/"+speed);
      effect.Sample(1.1f);Need(result,w.ContactAt(2,w.ContactPoint(2),w.ContactNormal(2)),"different side consecutive hit "+fps+"/"+speed);
      effect.Sample(1.2f);result.maxParticles=Math.Max(result.maxParticles,w.LiveParticles);go.transform.position+=Vector3.right*4;effect.Sample(1.3f);
      Need(result,w.Centre==fixedCentre&&Vector3.Distance(go.transform.Find("FixedWard_"+glyph).position,fixedCentre)<.001f,"world anchoring "+fps+"/"+speed);
      Need(result,w.InternalOpacity(fixedCentre+Vector3.up*1.6f)<.25f&&w.InternalOpacity(fixedCentre+Vector3.right*4)>.99f,"inside outside opacity "+fps+"/"+speed);
      Need(result,go.GetComponentsInChildren<Collider>().Length==0,"no gameplay collider "+fps+"/"+speed);
      Need(result,w.GroundQueries==queries,"ground queries cached "+fps+"/"+speed);result.groundQueries=queries;
      effect.Sample(4.7f);Need(result,w.Ended&&w.VisiblePanels==0&&w.LiveParticles==0&&w.LiveContacts==0,"complete release "+fps+"/"+speed);
      Need(result,!w.ContactAt(4,w.ContactPoint(0),w.ContactNormal(0)),"late contact rejected "+fps+"/"+speed);
     }finally{Object.DestroyImmediate(go);}
    }
    hill.SetActive(false);Physics.SyncTransforms();var missing=new GameObject("WardMissingGroundAudit");
    try{var effect=missing.AddComponent<Vfx120Effect>();effect.Profile=FixedWardBuild.Profile(glyph);effect.PreviewControlled=true;effect.Begin(hill.transform.position,null,hill.transform.position+Vector3.forward*8,Color.white);effect.Sample(1);Need(result,effect.FixedWard.MissingGround>0&&effect.FixedWard.VisiblePanels==0,"missing ground not bridged");}finally{Object.DestroyImmediate(missing);}
    result.status="PASS";
   }catch(Exception e){result.status="FAIL";result.errors.Add(e.ToString());}
   finally{if(hill!=null)Object.DestroyImmediate(hill);if(mesh!=null)Object.DestroyImmediate(mesh);}
   Directory.CreateDirectory(Path.Combine(FixedWardBuild.Output,glyph));File.WriteAllText(Path.Combine(FixedWardBuild.Output,glyph,"audit.json"),JsonUtility.ToJson(result,true));return result.status+" "+result.checks.Count+" checks "+string.Join(";",result.errors);
  }
 }
}
