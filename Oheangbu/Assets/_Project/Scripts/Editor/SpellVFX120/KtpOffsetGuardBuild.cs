using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpOffsetGuardBuild
 {
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||!new[]{"가","나","거","너"}.Contains(glyph))throw new Exception("Stopped editor and selected glyph required");
   string folder=Vfx120Editor.AssetRoot+"/KtpEmphasis";
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   var p=catalog.Entries.Single(x=>x.Glyph==glyph).Profile;
   string baseline=folder+"/BaselineV4_"+glyph+".asset";
   if(AssetDatabase.LoadAssetAtPath<Vfx120Profile>(baseline)==null)
   {
    if(p.CameraOffsetLaunch||p.GuardContactPrefab!=null)throw new Exception("Missing unmodified V4 baseline");
    if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p),baseline))throw new Exception("Baseline copy failed");
   }
   bool guard=glyph=="거"||glyph=="너";
   if(!guard){p.CameraOffsetLaunch=true;p.LaunchViewport=new Vector2(2f/3f,1f/3f);p.LaunchDepth=1.6f;p.KtpContactMultiplier=3.5f;p.KtpContactBrightness=3.5f;p.KtpContactSurfaceOffset=.4f;}
   else
   {
    p.NativeCastPrefab=null;p.KtpQuickCast=false;p.KtpPatternShield=true;
    p.NativeFieldPrefab=KtpQuickCastBuild.Pattern(folder,glyph=="거"?0:1,true);p.NativeFieldRole=Vfx120TraditionalMotif.Role.Shield;
    p.KtpFieldMultiplier=glyph=="거"?1.8f:1.1f;p.KtpFieldOffset=Vector3.zero;
    p.GuardContactPrefab=Contact(folder,glyph=="너");
   }
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssetIfDirty(p);
   string output=Path.Combine(Vfx120Editor.Output,"Emphasis4",glyph+"_v5");Directory.CreateDirectory(output);
   File.WriteAllText(Path.Combine(output,"settings.json"),EditorJsonUtility.ToJson(p,true));
   return "OFFSET_GUARD_BUILT "+glyph;
  }
  static GameObject Contact(string folder,bool fire)
  {
   string path=folder+"/"+(fire?"FireToAshContact":"WoodSplinterContact")+".prefab";
   var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
   var host=new GameObject(fire?"FireToAshContact":"WoodSplinterContact");
   try
   {
    var root=host.AddComponent<ParticleSystem>();root.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=root.main;main.loop=false;main.duration=.35f;main.startLifetime=.1f;
    var emission=root.emission;emission.enabled=false;host.GetComponent<ParticleSystemRenderer>().enabled=false;
    if(fire)
    {
     var src=AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/FireBolt/PF_FireGuard_031.prefab");
     var contact=Object.Instantiate(src.transform.Find("Contact").gameObject,host.transform,false);contact.transform.localPosition=Vector3.zero;
     foreach(var ps in contact.GetComponentsInChildren<ParticleSystem>())
     {
      ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var m=ps.main;m.loop=false;m.duration=.2f;m.startDelay=0;m.startLifetime=ps.name.Contains("Smoke")?.45f:.28f;m.maxParticles=32;
      var em=ps.emission;em.enabled=true;em.rateOverTime=0;em.rateOverDistance=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,(short)(ps.name.Contains("Smoke")?3:ps.name.Contains("Fire")?14:18))});
     }
     Debris(host,folder,"AshFlakes",28,new Color(.12f,.105f,.09f),.14f,.75f,new Vector3(.065f,.09f,.025f),.18f);
    }
    else
    {
     Debris(host,folder,"BambooSplinters",24,new Color(.38f,.27f,.12f),0,.65f,new Vector3(.055f,.38f,.04f),.8f);
     Debris(host,folder,"LeafFragments",14,new Color(.17f,.32f,.12f),.025f,.85f,new Vector3(.18f,.35f,.035f),.3f);
    }
    return PrefabUtility.SaveAsPrefabAsset(host,path);
   }
   finally{Object.DestroyImmediate(host);}
  }
  public static void Debris(GameObject parent,string folder,string name,int count,Color color,float delay,float life,Vector3 size,float gravity)
  {
   string materialPath=folder+"/DebrisUnlit.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
   if(mat==null){var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");if(shader==null)throw new Exception("Particle shader missing");mat=new Material(shader);mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Cull",0);AssetDatabase.CreateAsset(mat,materialPath);}
   string meshPath=folder+"/DebrisShard.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
   if(mesh==null){mesh=new Mesh{name="DebrisShard"};mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.4f,0),new Vector3(.2f,.5f,0),new Vector3(-.3f,.4f,0)};mesh.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);}
   var go=new GameObject(name);go.transform.SetParent(parent.transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   var m=ps.main;m.loop=false;m.duration=.3f;m.startDelay=delay;m.startLifetime=new ParticleSystem.MinMaxCurve(life*.65f,life);m.startSpeed=new ParticleSystem.MinMaxCurve(1.5f,4f);m.startColor=color;m.maxParticles=count;m.gravityModifier=gravity;m.startSize3D=true;m.startSizeX=size.x;m.startSizeY=size.y;m.startSizeZ=size.z;m.startRotation3D=true;m.startRotationX=new ParticleSystem.MinMaxCurve(0,6.28f);m.startRotationY=new ParticleSystem.MinMaxCurve(0,6.28f);m.startRotationZ=new ParticleSystem.MinMaxCurve(0,6.28f);m.scalingMode=ParticleSystemScalingMode.Hierarchy;
   var em=ps.emission;em.enabled=true;em.rateOverTime=0;em.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
   var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.12f;
   var rotation=ps.rotationOverLifetime;rotation.enabled=true;rotation.z=new ParticleSystem.MinMaxCurve(-5,5);
   var sizeLife=ps.sizeOverLifetime;sizeLife.enabled=true;sizeLife.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(.5f,1),new Keyframe(1,0)));
   var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;renderer.sharedMaterial=mat;
  }
 }
}
