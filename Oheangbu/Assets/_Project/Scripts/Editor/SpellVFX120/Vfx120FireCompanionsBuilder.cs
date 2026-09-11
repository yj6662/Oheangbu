using System;using System.IO;using UnityEditor;using UnityEngine;using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireCompanionsBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string f=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/036_B11D.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/036_B11D.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   var go=new GameObject("PF_FireCompanions_036");try{for(int i=0;i<3;i++){var pellet=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f+"/PF_FireBolt_025.prefab"),go.transform);pellet.name="Pellet_"+i;foreach(var ps in pellet.GetComponentsInChildren<ParticleSystem>()){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.maxParticles=16;main.startSizeMultiplier*=.4f;var em=ps.emission;if(em.rateOverTime.constantMax>0)em.rateOverTime=30;}}p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,f+"/PF_FireCompanions_036.prefab");}finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(f+"/M_EmberSeal_136.mat");p.Duration=2.7f;p.NativeFieldPrefab=null;p.NativeImpactPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_companions_036_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="넝",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=21,maxParticleCapacity=336,patternTris=6;}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Companions_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vector3.forward,Color.white);e.Sample(.3f);if(e.FirePelletParticles==0||e.ConfirmFirePelletHit(0,Vector3.one))throw new Exception("Idle/fake contact");for(int i=0;i<3;i++){if(!e.LaunchFirePellet(i,Vector3.one)||e.LaunchFirePellet(i,Vector3.one))throw new Exception("Duplicate launch");}e.Sample(.8f);if(e.FirePelletContacts!=0)throw new Exception("Automatic impact");for(int i=0;i<3;i++)if(!e.ConfirmFirePelletHit(i,Vector3.one)||e.ConfirmFirePelletHit(i,Vector3.one))throw new Exception("Duplicate contact");e.Sample(1);if(e.FirePelletContacts!=3||e.FirePelletParticles==0||e.FirePelletParticles>336)throw new Exception("Contact particles");e.Sample(e.Life);if(e.FirePelletParticles!=0)throw new Exception("Lingering");return "PASS_THREE_INDEPENDENT_LAUNCHES_CONFIRMED_CONTACTS_CLEANUP";}finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
