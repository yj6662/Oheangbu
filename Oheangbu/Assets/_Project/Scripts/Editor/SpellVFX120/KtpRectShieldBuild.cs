using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpRectShieldBuild
 {
  static string Folder=>Vfx120Editor.AssetRoot+"/KtpEmphasis";
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"가","나","거","너"}.Contains(glyph))throw new Exception("Stopped editor and selected glyph required");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string backup=Folder+"/BaselineV5_"+glyph+".asset";
   if(AssetDatabase.LoadAssetAtPath<Vfx120Profile>(backup)==null&&!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),backup))throw new Exception("Baseline copy failed");
   bool guard=glyph=="거"||glyph=="너";
   if(!guard)p.LaunchViewport=new Vector2(.60f,.40f);
   else
   {
    int family=glyph=="너"?1:0;
    p.KtpRectShield=true;p.NativeCastPrefab=null;
    p.NativeFieldPrefab=Shield(family);p.KtpFieldMultiplier=1/p.NativeScale;
    p.GuardContactPrefab=Contact(p,family);
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   string output=Path.Combine(Vfx120Editor.Output,"Emphasis4",glyph+"_v6");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "RECT_SHIELD_BUILT "+glyph;
  }
  public static GameObject Shield(int family)
  {
   string path=Folder+"/RectShield_"+family+".prefab";var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset!=null)return TuneCenter(path);
   var host=new GameObject("RectShield");
   try
   {
    var pattern=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/TransparentShield_"+family+".prefab"),host.transform,false);pattern.name="CenterPattern";pattern.transform.localPosition=new Vector3(0,0,-.02f);
    foreach(var ps in pattern.GetComponentsInChildren<ParticleSystem>()){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.startSize3D=false;m.startSize=1.05f;ps.transform.localScale=Vector3.one;}
    var panel=new GameObject("ShieldPanel");panel.transform.SetParent(host.transform,false);var system=panel.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    var main=system.main;main.loop=false;main.duration=.05f;main.startLifetime=30;main.startSpeed=0;main.startColor=Color.white;main.startSize3D=true;main.startSizeX=1.8f;main.startSizeY=2.2f;main.startSizeZ=1;main.maxParticles=1;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
    var shape=system.shape;shape.enabled=false;var em=system.emission;em.rateOverTime=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,1)});
    var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.004f),new GradientAlphaKey(1,1)});var color=system.colorOverLifetime;color.enabled=true;color.color=gradient;
    string mp=Folder+"/RectShieldPanel.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);if(mat==null){var shader=Shader.Find("Oheangbu/VFX/RectShield");if(shader==null)throw new Exception("Shield shader missing");mat=new Material(shader);AssetDatabase.CreateAsset(mat,mp);}
    string meshPath=Folder+"/ShieldQuad.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
    if(mesh==null){mesh=new Mesh{name="ShieldQuad"};mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);}
    var renderer=panel.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;renderer.sharedMaterial=mat;
    PrefabUtility.SaveAsPrefabAsset(host,path);return TuneCenter(path);
   }
   finally{Object.DestroyImmediate(host);}
  }
  static GameObject TuneCenter(string path)
  {
   var root=PrefabUtility.LoadPrefabContents(path);
   try
   {
    foreach(var ps in root.transform.Find("CenterPattern").GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
     // The vendor shader unfolds through normalized custom-data curves. A long-lived
     // shield must hold the unfolded phase instead of taking seconds to reveal its center.
     var custom=ps.customData;
     foreach(var data in new[]{ParticleSystemCustomData.Custom1,ParticleSystemCustomData.Custom2})
      if(custom.GetMode(data)==ParticleSystemCustomDataMode.Vector)
       for(int i=0;i<custom.GetVectorComponentCount(data);i++){var curve=custom.GetVector(data,i);custom.SetVector(data,i,new ParticleSystem.MinMaxCurve(curve.Evaluate(.35f,.5f)));}
     var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.8f,.002f),new GradientAlphaKey(.8f,1)});var color=ps.colorOverLifetime;color.enabled=true;color.color=gradient;
    }
    return PrefabUtility.SaveAsPrefabAsset(root,path);
   }
   finally{PrefabUtility.UnloadPrefabContents(root);}
  }
  public static GameObject Contact(Vfx120Profile p,int family)
  {
   string path=Folder+"/SmallPatternContact_"+family+".prefab";var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(asset!=null)return asset;
   var host=Object.Instantiate(p.GuardContactPrefab);host.name="SmallPatternAndDebris";
   try
   {
    var pattern=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/QuickCast_"+family+".prefab"),host.transform,false);pattern.name="ContactPattern";
    foreach(var ps in pattern.GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);ps.name="ContactPattern";ps.transform.localScale=Vector3.one;
     var main=ps.main;main.startSize3D=false;main.startSize=1.45f;main.startLifetime=.3f;main.duration=.3f;
     var color=ps.colorOverLifetime;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.1f),new GradientAlphaKey(.9f,.4f),new GradientAlphaKey(0,1)});color.color=gradient;
     var renderer=ps.GetComponent<ParticleSystemRenderer>();string matPath=Folder+"/SmallPattern_"+family+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
     if(mat==null)
     {
      mat=new Material(renderer.sharedMaterial);var shader=mat.shader;
      for(int i=0;i<shader.GetPropertyCount();i++)if(shader.GetPropertyType(i)==UnityEngine.Rendering.ShaderPropertyType.Color){int id=shader.GetPropertyNameId(i);var c=mat.GetColor(id);mat.SetColor(id,new Color(p.Pigment.r*4,p.Pigment.g*4,p.Pigment.b*4,c.a));}
      if(mat.HasProperty("_Emission"))mat.SetFloat("_Emission",1);AssetDatabase.CreateAsset(mat,matPath);
     }
     renderer.sharedMaterial=mat;
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }
   finally{Object.DestroyImmediate(host);}
  }
 }
}
