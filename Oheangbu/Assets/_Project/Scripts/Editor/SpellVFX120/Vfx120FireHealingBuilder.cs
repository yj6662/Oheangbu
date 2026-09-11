using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireHealingBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string folder=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/038_B179.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/038_B179.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_FireHealing_038";go.transform.Find("Travel").localPosition=Vector3.zero;
    foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Cone;shape.radius=.12f;shape.angle=10;
     var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.55f,.55f);v.y=new ParticleSystem.MinMaxCurve(.1f,.65f);v.z=new ParticleSystem.MinMaxCurve(4.5f,5.5f);
     var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.7f);
     if(ps.name=="Combustion"||ps.name=="TornFlame"){main.startSize=new ParticleSystem.MinMaxCurve(.25f,.5f);var em=ps.emission;em.rateOverTime=150;}
    }
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_FireHealing_038.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_HeavyHit_156.mat");p.Duration=3.4f;p.NativeFieldPrefab=null;p.NativeImpactPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_healing_038_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="녹",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=6;public string scope="Healing floor requires explicit confirmed target. Cast clock alone cannot create healing. No health recovery logic added.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("FireAura_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(.3f);if(!e.FireAuraConfigured||e.FireAuraParticles==0||e.FireAuraContacts!=0)throw new Exception("Aura missing or fake contact");e.Sample(.62f);if(e.FireHealingOpacity!=0||e.ConfirmFireHealingHit(null,Vector3.one,Vector3.zero))throw new Exception("Air cast healed");e.ConfirmFireHealingHit(go.transform,Vector3.one,Vector3.zero);e.Sample(.64f);if(e.FireAuraMarkOpacity<.8f)throw new Exception("No hit mark");e.Sample(1);if(e.FireAuraContacts!=1||e.FireAuraParticles==0||e.FireHealingOpacity<.8f)throw new Exception("Jet/clock failed");e.Sample(e.Life);if(e.FireAuraParticles!=0||e.FireAuraMarkOpacity!=0)throw new Exception("Lingering aura");return "PASS_AIR_NO_HEAL_CONFIRMED_TARGET_HEAL_AND_CLEANUP";}
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
