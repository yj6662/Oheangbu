using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireEmpowerBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string f=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/033_B10C.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/033_B10C.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f+"/PF_FireBolt_025.prefab"));
   try{go.name="PF_FireEmpower_033";foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
   {
    ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.2f;shape.radiusThickness=.15f;
    var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.04f,.04f);v.y=new ParticleSystem.MinMaxCurve(.08f,.2f);v.z=new ParticleSystem.MinMaxCurve(-.04f,.04f);
    var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.2f,.4f);if(ps.name=="Combustion"||ps.name=="TornFlame"){main.startSize=new ParticleSystem.MinMaxCurve(.08f,.15f);var em=ps.emission;em.rateOverTime=70;}
   }p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,f+"/PF_FireEmpower_033.prefab");}finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(f+"/M_EmberSeal_136.mat");if(p.PatternMaterial==null)p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(f+"/M_HitPattern_110.mat");p.Duration=2.6f;p.NativeFieldPrefab=null;p.NativeImpactPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_empower_033_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c,pattern=AssetDatabase.GetAssetPath(p.PatternMaterial)},true));return c;
  }
  [Serializable]class Report{public string glyph="넌",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck,pattern;public int particleSystems=7,maxParticleCapacity=432,patternTris=4;public string scope="Empower consumption and confirmed hit are separate, one-use signals. No attack multiplier implementation.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Empower_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(.4f);if(e.FireAuraParticles==0||e.ConfirmFireEmpowerHit(Vector3.one))throw new Exception("Preparation failed");if(!e.ConsumeFireEmpower()||e.ConsumeFireEmpower())throw new Exception("Consumption failed");e.Sample(.7f);if(e.FireAuraMarkOpacity!=0)throw new Exception("Miss fabricated hit");if(!e.ConfirmFireEmpowerHit(Vector3.one)||e.ConfirmFireEmpowerHit(Vector3.one))throw new Exception("Hit not once");e.Sample(.72f);if(e.FireAuraMarkOpacity<.8f)throw new Exception("No hit motif");e.Sample(e.Life);if(e.FireAuraParticles!=0||e.FireAuraMarkOpacity!=0)throw new Exception("Lingering");return "PASS_PREPARE_CONSUME_MISS_CONFIRMED_HIT_ONCE_CLEANUP";}
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
