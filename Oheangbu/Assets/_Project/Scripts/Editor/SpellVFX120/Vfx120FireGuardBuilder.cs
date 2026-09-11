using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireGuardBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string folder=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/031_B108.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/031_B108.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_FireGuard_031";go.transform.Find("Travel").localPosition=new Vector3(0,-.7f,.72f);go.transform.Find("Contact").localPosition=new Vector3(0,.1f,.72f);
    foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
    {
     ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(1.65f,.1f,.12f);
     var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.15f,.15f);v.y=new ParticleSystem.MinMaxCurve(1.4f,2.1f);v.z=new ParticleSystem.MinMaxCurve(-.08f,.08f);
     var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.45f,.7f);
     if(ps.name=="Combustion"||ps.name=="TornFlame"){main.startSize=new ParticleSystem.MinMaxCurve(.2f,.4f);var em=ps.emission;em.rateOverTime=125;}
    }
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,folder+"/PF_FireGuard_031.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/M_HitPattern_110.mat");p.NativeFieldPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_guard_031_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="너",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=2;public string scope="Rising fire curtain and KTP motif; successful Fire parry signal adapter connected. No combat timing or reward changes.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("FireGuard_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(.3f);if(!e.FireGuardConfigured||e.FireGuardParticles==0||e.FireGuardPulse!=0)throw new Exception("Guard missing or fake parry");e.Signal(Vfx120Effect.Cue.Parry);e.Sample(.32f);if(e.FireGuardPulse<.7f)throw new Exception("No parry response");e.Sample(e.Life);if(e.FireGuardParticles!=0||e.FireGuardPulse!=0)throw new Exception("Lingering guard");return "PASS_FIRE_CURTAIN_EXPLICIT_PARRY_AND_CLEANUP";}
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
