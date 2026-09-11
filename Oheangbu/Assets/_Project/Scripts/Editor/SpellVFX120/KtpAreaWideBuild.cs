using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaWideBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/AreaWide";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!"고노소모오".Contains(glyph)||glyph.Length!=1)throw new Exception("Stopped editor and selected glyph required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaWide");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string baseline=Folder+"/Baseline_"+glyph+".asset";if(!File.Exists(baseline)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),baseline))throw new Exception("Backup failed");
   const string bookPath="Assets/_Project/Data/Configs/SpellBook_Proto.asset";
   if(!File.Exists(Folder+"/BaselineSpellBook.asset")&&!AssetDatabase.CopyAsset(bookPath,Folder+"/BaselineSpellBook.asset"))throw new Exception("Book backup failed");
   p.WideAreaRevision=true;
   if(glyph=="고"||glyph=="노")
   {
    string path=Folder+"/Cast_"+glyph+".prefab";
    if(!File.Exists(path))
    {
     var host=Object.Instantiate(p.NativeCastPrefab);
     try{foreach(var ps in host.GetComponentsInChildren<ParticleSystem>(true))
     {
      ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.duration=1.25f;main.startLifetime=1.25f;
      var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.85f,.07f),new GradientAlphaKey(.8f,.7f),new GradientAlphaKey(0,1)});var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
     }PrefabUtility.SaveAsPrefabAsset(host,path);}finally{Object.DestroyImmediate(host);}
    }
    p.NativeCastPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);p.AreaCastSeconds=1.25f;
   }
   if(glyph=="모")
   {
    string path=Folder+"/SandVortex.prefab";
    if(!File.Exists(path))
    {
     var host=Object.Instantiate(p.NativeBodyPrefab);
     try{foreach(var ps in host.GetComponentsInChildren<ParticleSystem>())
     {ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.startLifetime=ps.name=="Grains"?.8f:1.05f;m.maxParticles=ps.name=="Grains"?100:240;m.startSize=ps.name=="Grains"?.08f:1.3f;var em=ps.emission;em.rateOverTime=ps.name=="Grains"?100:220;}
      PrefabUtility.SaveAsPrefabAsset(host,path);}finally{Object.DestroyImmediate(host);}
    }
    p.NativeBodyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>(bookPath);var so=new SerializedObject(book);var entries=so.FindProperty("_entries");
   for(int i=0;i<entries.arraySize;i++)
   {
    var e=entries.GetArrayElementAtIndex(i);if(e.FindPropertyRelative("Letter").stringValue!=glyph)continue;
    if(glyph=="모"||glyph=="오"){e.FindPropertyRelative("AreaRadius").floatValue=6;e.FindPropertyRelative("BasePower").floatValue=5;}
    if(glyph=="소"){e.FindPropertyRelative("AreaAngle").floatValue=45;e.FindPropertyRelative("VolleyShots").intValue=24;e.FindPropertyRelative("VolleyInterval").floatValue=.045f;e.FindPropertyRelative("ScatterVolley").boolValue=true;}
   }
   so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(book);
   string output=Path.Combine(Vfx120Editor.Output,"AreaWide",glyph);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "AREA_WIDE_BUILT "+glyph;
  }
 }
}
