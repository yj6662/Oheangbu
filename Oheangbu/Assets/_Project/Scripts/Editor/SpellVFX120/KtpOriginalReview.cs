using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpOriginalReview
 {
  [Serializable] class Row{public int role,systems,capacity,peak;public float end;public string source,status;}
  [Serializable] class Report{public string glyph,status,mvid;public Row[] roles;}
  public static string Capture(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   if(!p.UseOriginalKtp)throw new Exception("Build original first");
   int initial=(glyph[0]-0xAC00)/588;int family=initial==0?0:initial==2?1:initial==6?2:initial==9?3:4;
   var scene=EditorSceneManager.NewPreviewScene();var camGo=new GameObject("OriginalReviewCamera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo,scene);var cam=camGo.AddComponent<Camera>();cam.scene=scene;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.055f,.052f,.048f);cam.nearClipPlane=.01f;cam.fieldOfView=50;cam.transform.position=new Vector3(0,1.4f,-4);cam.transform.LookAt(new Vector3(0,.4f,0));
   var rt=new RenderTexture(960,540,24);rt.Create();var tex=new Texture2D(960,540,TextureFormat.RGB24,false);var old=RenderTexture.active;cam.targetTexture=rt;
   var rows=new Row[4];string folder=Path.Combine(Vfx120Editor.Output,"OriginalReview",glyph);Directory.CreateDirectory(folder);
   try
   {
    for(int role=0;role<4;role++)
    {
     string path=Vfx120Editor.AssetRoot+"/KtpOriginal/Original_"+family+"_"+role+".prefab";var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
     var host=new GameObject("OriginalReview");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);host.transform.localScale=Vector3.one*.32f;if(role<2)host.transform.position=Vector3.up*.5f;
     var fx=host.AddComponent<Vfx120TraditionalMotif>();var settings=Vfx120TraditionalMotif.Settings.DefaultFor((Vfx120TraditionalMotif.Role)role);settings.PreserveAuthored=true;settings.PreviewControlled=true;settings.HierarchyScaling=true;settings.Sustain=false;
     try
     {
      if(!fx.Configure(source,p.Pigment,p.Ink,(Vfx120TraditionalMotif.Role)role,settings))throw new Exception(fx.Diagnostic);
      var originals=source.GetComponentsInChildren<ParticleSystem>(true);var clones=fx.ContentTransform.GetComponentsInChildren<ParticleSystem>(true);
      if(originals.Length!=clones.Length)throw new Exception("Hierarchy differs");
      for(int i=0;i<clones.Length;i++)if(originals[i].main.maxParticles!=clones[i].main.maxParticles||originals[i].main.duration!=clones[i].main.duration||originals[i].main.loop!=clones[i].main.loop||!originals[i].GetComponent<Renderer>().sharedMaterials.SequenceEqual(clones[i].GetComponent<Renderer>().sharedMaterials))throw new Exception("Original settings changed");
      var row=new Row{role=role,source=path,systems=originals.Length,capacity=originals.Sum(x=>x.main.maxParticles),end=fx.EndsAt};rows[role]=row;
      string frames=Path.Combine(folder,"role_"+role);Directory.CreateDirectory(frames);float duration=Mathf.Min(12,fx.EndsAt+.2f);int n=Mathf.CeilToInt(duration*24);
      for(int f=0;f<n;f++){fx.Sample(f/24f);row.peak=Mathf.Max(row.peak,clones.Sum(x=>x.particleCount));cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,540),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(frames,f.ToString("D4")+".png"),tex.EncodeToPNG());}
      fx.Sample(fx.EndsAt);if(!fx.IsComplete||row.peak==0)throw new Exception("No particles or end");row.status="PASS_SETTINGS_AND_PREVIEW_ONLY";
     }
     finally{UnityEngine.Object.DestroyImmediate(host);}
    }
    string report=JsonUtility.ToJson(new Report{glyph=glyph,status="PASS_PREVIEW_ONLY_USER_VISUAL_PENDING",mvid=typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),roles=rows},true);File.WriteAllText(Path.Combine(folder,"report.json"),report);return report;
   }
   finally{RenderTexture.active=old;cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(tex);EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
