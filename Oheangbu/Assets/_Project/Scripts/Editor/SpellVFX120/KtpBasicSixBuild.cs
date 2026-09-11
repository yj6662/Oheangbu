using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpBasicSixBuild
 {
  static string Folder=>Vfx120Editor.AssetRoot+"/KtpEmphasis";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"사","마","아","서","머","어"}.Contains(glyph))throw new Exception("Stopped editor and one of the six basic spells required");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string baseline=Folder+"/BaselineBasicSix_"+glyph+".asset";
   if(AssetDatabase.LoadAssetAtPath<Vfx120Profile>(baseline)==null&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),baseline))throw new Exception("Baseline copy failed");
   int family=glyph=="사"||glyph=="서"?3:glyph=="마"||glyph=="머"?2:4;
   bool guard=p.Behavior==Vfx120Behavior.Shield;
   var quick=KtpQuickCastBuild.Pattern(Folder,family,false);
   p.UseOriginalKtp=true;p.KtpEmphasis=true;p.KtpBrightness=2.2f;
   p.KtpImpactMultiplier=2;p.KtpContactMultiplier=guard?2:3.5f;
   if(guard)
   {
    KtpQuickCastBuild.Pattern(Folder,family,true);
    p.KtpQuickCast=false;p.KtpPatternShield=true;p.KtpRectShield=true;p.NativeCastPrefab=null;
    p.NativeFieldPrefab=KtpRectShieldBuild.Shield(family);p.NativeFieldRole=Vfx120TraditionalMotif.Role.Shield;
    p.NativeReplaceBody=true;p.KtpFieldMultiplier=1/p.NativeScale;p.KtpFieldOffset=Vector3.zero;
    p.GuardContactPrefab=DebrisContact(family);p.GuardContactPrefab=KtpRectShieldBuild.Contact(p,family);
   }
   else
   {
    p.CameraOffsetLaunch=true;p.LaunchViewport=new Vector2(.60f,.40f);p.LaunchDepth=1.6f;
    p.NativeCastPrefab=quick;p.KtpQuickCast=true;p.KtpCastMultiplier=.264f/p.NativeScale;p.KtpCastOffset=new Vector3(0,0,-.65f);
    p.KtpContactBrightness=3.5f;p.KtpContactSurfaceOffset=.4f;
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   var cp=AssetDatabase.LoadAssetAtPath<KtpContactProfile>(Vfx120Editor.AssetRoot+"/Resources/KTP_ContactProfile.asset");
   cp.SpellProfiles=(cp.SpellProfiles??Array.Empty<Vfx120Profile>()).Where(x=>x!=null&&x.Glyph!=glyph).Append(p).ToArray();
   EditorUtility.SetDirty(cp);AssetDatabase.SaveAssetIfDirty(cp);
   string output=Path.Combine(Vfx120Editor.Output,"BasicSix",glyph);Directory.CreateDirectory(output);
   File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "BASIC_SIX_BUILT "+glyph+"; original flight="+p.Flight+"; contactOverrides="+cp.SpellProfiles.Length;
  }
  static GameObject DebrisContact(int family)
  {
   string path=Folder+"/ElementDebrisContact_"+family+".prefab";
   var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
   var host=new GameObject("ElementDebrisContact");
   try
   {
    var root=host.AddComponent<ParticleSystem>();root.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    var main=root.main;main.loop=false;main.duration=.35f;main.startLifetime=.1f;
    var em=root.emission;em.enabled=false;host.GetComponent<ParticleSystemRenderer>().enabled=false;
    if(family==3)
    {
     KtpOffsetGuardBuild.Debris(host,Folder,"MetalChips",18,new Color(.65f,.73f,.8f),0,.65f,new Vector3(.1f,.16f,.04f),1);
     KtpOffsetGuardBuild.Debris(host,Folder,"MetalSparks",20,new Color(2.5f,1.75f,.65f),0,.25f,new Vector3(.025f,.22f,.025f),.25f);
    }
    else if(family==2)
    {
     KtpOffsetGuardBuild.Debris(host,Folder,"StoneChunks",20,new Color(.45f,.32f,.19f),0,.7f,new Vector3(.14f,.18f,.1f),1.25f);
     KtpOffsetGuardBuild.Debris(host,Folder,"StoneDust",14,new Color(.63f,.5f,.33f),.02f,.55f,new Vector3(.055f,.065f,.04f),.18f);
     RoundParticles(host.transform.Find("StoneChunks"),false);
    }
    else
    {
     KtpOffsetGuardBuild.Debris(host,Folder,"WaterDrops",28,new Color(.34f,.75f,1f),0,.65f,new Vector3(.055f,.16f,.055f),.65f);
     KtpOffsetGuardBuild.Debris(host,Folder,"WaterSpray",16,new Color(.64f,.86f,1f),.02f,.3f,new Vector3(.025f,.05f,.025f),.18f);
     RoundParticles(host.transform.Find("WaterDrops"),true);RoundParticles(host.transform.Find("WaterSpray"),true);
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }
   finally{Object.DestroyImmediate(host);}
  }
  static void RoundParticles(Transform t,bool water)
  {
   // A built-in low-resolution sphere keeps water round and stones volumetric;
   // metallic flakes retain the angular shard mesh.
   var primitive=GameObject.CreatePrimitive(water?PrimitiveType.Sphere:PrimitiveType.Cube);
   t.GetComponent<ParticleSystemRenderer>().mesh=primitive.GetComponent<MeshFilter>().sharedMesh;
   Object.DestroyImmediate(primitive);
  }
 }
}
