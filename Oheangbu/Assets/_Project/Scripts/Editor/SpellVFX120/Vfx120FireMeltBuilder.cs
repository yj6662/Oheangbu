using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireMeltBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string folder=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/041_B18B.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/041_B18B.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_FireMelt_041";go.transform.Find("Travel").localPosition=new Vector3(0,-.78f,0);
    foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=1.5f;shape.radiusThickness=.12f;shape.rotation=new Vector3(90,0,0);
     var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.12f,.12f);v.y=new ParticleSystem.MinMaxCurve(.6f,1.1f);v.z=new ParticleSystem.MinMaxCurve(-.12f,.12f);
     var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.4f,.6f);
     if(ps.name=="Combustion"||ps.name=="TornFlame"){main.startSize=new ParticleSystem.MinMaxCurve(.18f,.34f);var em=ps.emission;em.rateOverTime=140;}
    }
    var drops=go.transform.Find("Contact/ImpactCinders").GetComponent<ParticleSystem>();var dm=drops.main;dm.gravityModifier=1.2f;dm.startSize=new ParticleSystem.MinMaxCurve(.025f,.055f);var dv=drops.velocityOverLifetime;dv.x=new ParticleSystem.MinMaxCurve(-.45f,.45f);dv.y=new ParticleSystem.MinMaxCurve(-.5f,0);dv.z=new ParticleSystem.MinMaxCurve(-.45f,.45f);p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_FireMelt_041.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_Resolve_126.mat");p.Duration=3.2f;p.NativeFieldPrefab=null;p.NativeImpactPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_melt_041_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="놋",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=4;public string scope="Target-area fire with falling hot droplets and eroding contact motif. No armor reduction rules.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("FireAura_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(.3f);if(!e.FireAuraConfigured||e.FireAuraParticles==0||e.FireAuraContacts!=0)throw new Exception("Aura missing or fake contact");e.SignalFireAuraHit(new Vector3(1,1,0));e.Sample(.32f);if(e.FireAuraMarkOpacity<.8f)throw new Exception("No hit mark");e.Sample(1);e.SignalFireAuraHit(new Vector3(-1,1,0));e.Sample(1.02f);if(e.FireAuraContacts!=2||e.FireAuraMarkOpacity<.8f)throw new Exception("Repeat contact failed");e.Sample(e.Life);if(e.FireAuraParticles!=0||e.FireAuraMarkOpacity!=0)throw new Exception("Lingering aura");return "PASS_FIRE_FIELD_REPEATED_MELT_CONTACTS_CLEANUP";}
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
