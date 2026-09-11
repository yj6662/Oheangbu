using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120PiercingFlameBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   string folder=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/030_B0AD.asset");
   string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals/030_B0AD.asset.txt");if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(p),backup);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_PiercingFlame_030";
    foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     var shape=ps.shape;shape.radius=.025f;
     if(ps.name=="Combustion"||ps.name=="TornFlame")
     {
      var m=ps.main;m.startSize=new ParticleSystem.MinMaxCurve(.09f,.18f);m.startLifetime=new ParticleSystem.MinMaxCurve(.16f,.25f);
      var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.04f,.04f);v.y=new ParticleSystem.MinMaxCurve(.02f,.08f);v.z=new ParticleSystem.MinMaxCurve(-3f,-1.8f);
      var noise=ps.noise;noise.strength=.045f;var em=ps.emission;em.rateOverTime=220;
     }
    }
    var fire=go.transform.Find("Contact/ImpactFire").GetComponent<ParticleSystem>();var main=fire.main;main.startSize=new ParticleSystem.MinMaxCurve(.18f,.35f);
    var emission=fire.emission;emission.SetBursts(new[]{new ParticleSystem.Burst(0,20)});
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_PiercingFlame_030.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_HitPattern_110.mat");p.Duration=1.8f;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Count=1;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.23f;p.NativeImpactScale=.55f;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"piercing_flame_030_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="낭",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=2;public string scope="Narrow fire jet continues beyond supplied contact; stationary hit motif. No armor/damage rules changed.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Piercing_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try
   {
    var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);
    e.Sample(.3f);if(e.FirePenetrationDistance!=0||e.FireHitPatternOpacity!=0)throw new Exception("Early contact");
    e.Sample(.85f);if(e.FirePenetrationDistance<1.4f||e.FireHitPatternOpacity<.9f||e.FireBoltParticles==0)throw new Exception("Missing penetration/motif");
    e.Sample(e.Life);if(e.FireBoltParticles!=0||e.FireHitPatternOpacity!=0)throw new Exception("Lingering effect");
    e.SetImpactClock(0);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(1);if(e.FireBoltImpacted||e.FirePenetrationDistance!=0)throw new Exception("Unconfirmed contact");
    return "PASS_CONFIRMED_CONTACT_PENETRATION_MOTIF_AND_CLEANUP";
   }
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
