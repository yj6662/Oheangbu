using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaFlowBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/AreaFlow";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||glyph==null||glyph.Length!=1||!"고노소오".Contains(glyph))throw new Exception("Stopped editor and selected glyph required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaFlow");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   // Back up all five before any mutation, including the unchanged sand candidate.
   foreach(string g in new[]{"고","노","소","모","오"})
   {var source=catalog.Entries.Single(x=>x.Glyph==g).Profile;string path=Folder+"/Baseline_"+g+".asset";if(!File.Exists(path)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source),path))throw new Exception("Profile backup failed");}
   if(!File.Exists(Folder+"/BaselineSpellBook.asset")&&!AssetDatabase.CopyAsset("Assets/_Project/Data/Configs/SpellBook_Proto.asset",Folder+"/BaselineSpellBook.asset"))throw new Exception("Book backup failed");
   var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;p.AreaFlowRevision=true;
   if(glyph=="고")p.Duration=4.6f;
   if(glyph=="노"||glyph=="소")
   {p.CameraOffsetLaunch=true;p.LaunchViewport=glyph=="노"?new Vector2(.65f,.32f):new Vector2(.5f,1.2f);p.LaunchDepth=glyph=="노"?1.6f:3.2f;}
   if(glyph=="고"||glyph=="노")
   {
    float lifetime=glyph=="고"?4.1f:1.8f;string path=Folder+"/Cast_"+glyph+".prefab";
    if(!File.Exists(path))
    {
     var host=Object.Instantiate(p.NativeCastPrefab);
     try{foreach(var ps in host.GetComponentsInChildren<ParticleSystem>(true))
     {
      ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.duration=lifetime;main.startLifetime=lifetime;
      var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(glyph=="고"?.85f:.55f,.03f),new GradientAlphaKey(glyph=="고"?.7f:.4f,.85f),new GradientAlphaKey(0,1)});var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
     }PrefabUtility.SaveAsPrefabAsset(host,path);}finally{Object.DestroyImmediate(host);}
    }
    p.NativeCastPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);p.AreaCastSeconds=lifetime;
   }
   if(glyph=="노"||glyph=="오")
   {
    string path=Folder+"/Body_"+glyph+".prefab";
    if(!File.Exists(path))
    {
     // Restore the original rolling water material/mesh, keep the current twelve-meter plan.
     var source=glyph=="오"?AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/AreaFive/Body_오.prefab"):p.NativeBodyPrefab;
     var host=Object.Instantiate(source);
     try
     {
      foreach(var ps in host.GetComponentsInChildren<ParticleSystem>())
      {
       ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
       if(glyph=="노"){var m=ps.main;m.startLifetime=ps.name=="FlameCore"?.28f:.4f;var em=ps.emission;em.rateOverTime=ps.name=="FlameCore"?350:20;}
      }
      if(glyph=="오")
      {
       var shore=Object.Instantiate(host.transform.Find("Foam").gameObject,host.transform);shore.name="ShoreFoam";
       shore.transform.localPosition=Vector3.zero;shore.transform.localScale=Vector3.one;
       var ps=shore.GetComponent<ParticleSystem>();var m=ps.main;m.maxParticles=128;m.startLifetime=.55f;m.startSize=.25f;m.startColor=new Color(.82f,.92f,.92f,.8f);var em=ps.emission;em.rateOverTime=180;
      }
      PrefabUtility.SaveAsPrefabAsset(host,path);
     }finally{Object.DestroyImmediate(host);}
    }
    p.NativeBodyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   string output=Path.Combine(Vfx120Editor.Output,"AreaFlow",glyph);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "AREA_FLOW_BUILT "+glyph;
  }
 }
}
