using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.App.SpellVFX120;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpAreaRiftBuild
 {
  public const string Folder="Assets/_Project/Art/SpellVFX120/AreaRift";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||glyph==null||glyph.Length!=1||!"고노소모".Contains(glyph))throw new Exception("Stopped editor and selected glyph required");
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"AreaRift");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   foreach(string g in new[]{"고","노","소","모","오"})
   {var src=catalog.Entries.Single(x=>x.Glyph==g).Profile;string path=Folder+"/Baseline_"+g+".asset";if(!File.Exists(path)&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(src),path))throw new Exception("Backup failed");}
   if(!File.Exists(Folder+"/BaselineSpellBook.asset")&&!AssetDatabase.CopyAsset("Assets/_Project/Data/Configs/SpellBook_Proto.asset",Folder+"/BaselineSpellBook.asset"))throw new Exception("Book backup failed");
   var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;p.AreaReadabilityRevision=true;
   if(glyph=="고")
   {
    p.Duration=3;p.AreaCastSeconds=2.65f;string path=Folder+"/Cast_고.prefab";
    if(!File.Exists(path))
    {
     var host=Object.Instantiate(p.NativeCastPrefab);
     try{foreach(var ps in host.GetComponentsInChildren<ParticleSystem>(true)){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.duration=2.65f;m.startLifetime=2.65f;}PrefabUtility.SaveAsPrefabAsset(host,path);}finally{Object.DestroyImmediate(host);}
    }
    p.NativeCastPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   }
   else
   {
    if(glyph=="노")p.LaunchViewport=new Vector2(.65f,.12f);
    if(glyph=="모"){p.EarthRift=true;p.CameraOffsetLaunch=false;}
    string path=Folder+"/Body_"+glyph+".prefab";
    if(!File.Exists(path))
    {
     var host=Object.Instantiate(p.NativeBodyPrefab);
     try
     {
      if(glyph=="소")
      {
       host.transform.localScale=new Vector3(2.8f,2.8f,1.15f);
       var renderer=host.GetComponent<MeshRenderer>();var material=new Material(renderer.sharedMaterial);
       material.SetColor("_BaseColor",new Color(.82f,.9f,.98f));material.SetFloat("_Metallic",.65f);material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(.45f,.67f,.9f)*1.6f);
       AssetDatabase.CreateAsset(material,Folder+"/SilverNeedle.mat");renderer.sharedMaterial=material;
       var line=host.GetComponent<LineRenderer>();line.startWidth=.05f;line.endWidth=.006f;line.SetPositions(new[]{new Vector3(0,0,-.2f),new Vector3(0,0,-2f)});line.startColor=new Color(.8f,.92f,1,1);line.endColor=new Color(.35f,.55f,.75f,0);
      }
      foreach(var ps in host.GetComponentsInChildren<ParticleSystem>())
      {
       ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;var em=ps.emission;
       if(glyph=="노"){bool core=ps.name=="FlameCore";m.startColor=core?new Color(1.8f,.8f,.16f,.95f):new Color(.18f,.16f,.14f,.1f);em.rateOverTime=core?420:10;}
       if(glyph=="모"){bool grain=ps.name=="Grains";m.maxParticles=grain?96:100;m.startLifetime=grain?.6f:.5f;m.startColor=grain?new Color(.34f,.22f,.1f,1):new Color(.48f,.32f,.17f,.55f);em.rateOverTime=grain?180:160;}
      }
      if(glyph=="모")
      {
       var material=AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot+"/KtpEmphasis/DebrisUnlit.mat");
       for(int lane=0;lane<7;lane++)
       {
        var go=new GameObject("Fissure_"+lane);go.transform.SetParent(host.transform,false);var line=go.AddComponent<LineRenderer>();line.sharedMaterial=material;line.useWorldSpace=true;line.positionCount=25;line.startWidth=.13f;line.endWidth=.065f;line.numCornerVertices=1;line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
       }
      }
      PrefabUtility.SaveAsPrefabAsset(host,path);
     }finally{Object.DestroyImmediate(host);}
    }
    p.NativeBodyPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
   }
   if(glyph=="소"){var material=p.NativeBodyPrefab.GetComponent<MeshRenderer>().sharedMaterial;material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;material.EnableKeyword("_EMISSION");EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);}
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   string output=Path.Combine(Vfx120Editor.Output,"AreaRift",glyph);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));return "AREA_RIFT_BUILT "+glyph;
  }
 }
}
