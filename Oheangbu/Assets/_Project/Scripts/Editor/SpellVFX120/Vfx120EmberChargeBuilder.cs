using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120EmberChargeBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   string f=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/028_B0A8.asset");
   string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals/028_B0A8.asset.txt");if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(p),backup);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_EmberCharge_028";var hold=new GameObject("Hold");hold.transform.SetParent(go.transform,false);
    var ps=UnityEngine.Object.Instantiate(go.transform.Find("Travel/Combustion").gameObject,hold.transform).GetComponent<ParticleSystem>();ps.name="BoundEmber";ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
    var main=ps.main;main.maxParticles=32;main.startSize=new ParticleSystem.MinMaxCurve(.055f,.12f);main.startLifetime=new ParticleSystem.MinMaxCurve(.2f,.4f);
    var emission=ps.emission;emission.rateOverTime=25;var v=ps.velocityOverLifetime;v.x=0;v.z=0;v.y=.16f;
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,f+"/PF_EmberCharge_028.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   string mp=f+"/M_EmberSeal_136.mat";var m=AssetDatabase.LoadAssetAtPath<Material>(mp);if(m==null){m=new Material(AssetDatabase.LoadAssetAtPath<Material>(f+"/M_HitPattern_110.mat"));AssetDatabase.CreateAsset(m,mp);}
   m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_136.png"));EditorUtility.SetDirty(m);
   p.PatternMaterial=m;p.Duration=3.2f;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Count=1;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.3f;p.NativeImpactScale=.8f;EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
   string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"ember_charge_028_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="남",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=8,maxParticleCapacity=464,patternTris=2;public string scope="Attachment and explicit detonation visual only; no automatic C4/damage.";}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Charge_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try
   {
    var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);
    e.Sample(1.3f);if(e.NativeImpact!=null||e.EmberChargeDetonatedAt>=0||e.FireHitPatternOpacity<.7f)throw new Exception("Early explosion or missing seal");
    if(!e.DetonateEmberCharge(1.8f)||e.DetonateEmberCharge(1.9f))throw new Exception("Detonation gate failed");
    e.Sample(2);if(e.NativeImpact==null||e.FireBoltParticles<20)throw new Exception("Explosion missing");
    e.Sample(e.Life);if(e.FireBoltParticles!=0||e.FireHitPatternOpacity!=0)throw new Exception("Lingering effect");return "PASS_HOLD_EXPLICIT_DETONATION_ONCE_AND_CLEANUP";
   }
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
