using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpQuickCastBuild
 {
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"가","나","거","너"}.Contains(glyph))throw new Exception("Stopped editor and selected glyph required");
   string folder=Vfx120Editor.AssetRoot+"/KtpEmphasis";
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string baseline=folder+"/BaselineV3_"+glyph+".asset";
   if(AssetDatabase.LoadAssetAtPath<Vfx120Profile>(baseline)==null)
   {
    if(p.KtpQuickCast)throw new Exception("Missing unmodified V3 baseline");
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),baseline))throw new Exception("Baseline copy failed");
   }
   int family=glyph=="나"||glyph=="너"?1:0;
   p.NativeCastPrefab=Pattern(folder,family,false);
   p.KtpQuickCast=true;p.KtpCastMultiplier=1.1f;p.KtpCastOffset=new Vector3(0,0,-.65f);
   if(glyph=="너")
   {
    p.KtpPatternShield=true;p.NativeFieldPrefab=Pattern(folder,1,true);p.NativeFieldRole=Vfx120TraditionalMotif.Role.Shield;
    p.KtpFieldMultiplier=1.1f;p.KtpFieldOffset=Vector3.zero;
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   var output=Path.Combine(Vfx120Editor.Output,"Emphasis4",glyph+"_v4");Directory.CreateDirectory(output);
   File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "QUICK_CAST_BUILT "+glyph+"; cast=0.32s, alphaPeak=0.35, multiplier=1.1, offsetZ=-0.65; patternShield="+p.KtpPatternShield;
  }
  public static GameObject Pattern(string folder,int family,bool shield)
  {
   string path=folder+"/"+(shield?"TransparentShield":"QuickCast")+"_"+family+".prefab";
   var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
   var source=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/KtpOriginal/Original_"+family+"_3.prefab");
   var patterns=source.GetComponentsInChildren<ParticleSystem>(true).Where(p=>p.name.IndexOf("pattern",StringComparison.OrdinalIgnoreCase)>=0).ToArray();
   if(patterns.Length==0)throw new Exception("Bottom pattern particle not found");
   var host=new GameObject(shield?"KTP_Bottom_TransparentShield":"KTP_Bottom_QuickCast");
   try
   {
    var upright=new GameObject("Upright");upright.transform.SetParent(host.transform,false);upright.transform.localRotation=Quaternion.Euler(90,0,0);
    foreach(var original in patterns)
    {
     var copy=Object.Instantiate(original.gameObject,upright.transform,false);copy.SetActive(true);
     foreach(var child in copy.GetComponentsInChildren<Transform>(true).Where(t=>t!=copy.transform).OrderByDescending(t=>t.GetSiblingIndex()).ToArray())if(child!=null)Object.DestroyImmediate(child.gameObject);
     var ps=copy.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
     copy.transform.localPosition=Vector3.zero;
     var main=ps.main;main.loop=false;main.duration=shield?.05f:.32f;main.startDelay=0;main.startLifetime=shield?30f:.32f;
     main.startSpeed=0;main.startColor=Color.white;main.maxParticles=1;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
     var em=ps.emission;em.enabled=true;em.rateOverTime=0;em.rateOverDistance=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
     var shape=ps.shape;shape.enabled=false;
     var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},shield
      ?new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.26f,.004f),new GradientAlphaKey(.26f,1)}
      :new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.35f,.12f),new GradientAlphaKey(.3f,.4f),new GradientAlphaKey(0,1)});
     var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
     var size=ps.sizeOverLifetime;size.enabled=true;size.separateAxes=false;
     size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.8f),new Keyframe(shield?.004f:.15f,1),new Keyframe(1,shield?1:.94f)));
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }
   finally{Object.DestroyImmediate(host);}
  }
 }
}
