using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FlameCrescentBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string f=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/029_B0AB.asset");string backup=Path.Combine(Vfx120Editor.Output,"FireOriginals/029_B0AB.asset.txt");if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(p),backup);
   var mesh=Arc();string mp=f+"/M_FlameArcEmitter.asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(mp);if(old==null)AssetDatabase.CreateAsset(mesh,mp);else{EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(old);}
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f+"/PF_FireBolt_025.prefab"));
   try
   {
    go.name="PF_FlameCrescent_029";
    for(int i=0;i<2;i++)
    {
     var ps=UnityEngine.Object.Instantiate(go.transform.Find("Travel/Combustion").gameObject,go.transform).GetComponent<ParticleSystem>();ps.name=i==0?"SplitLeft":"SplitRight";ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
     var main=ps.main;main.maxParticles=80;main.startSize=new ParticleSystem.MinMaxCurve(.09f,.2f);main.startLifetime=new ParticleSystem.MinMaxCurve(.12f,.24f);
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Mesh;shape.mesh=mesh;shape.meshShapeType=ParticleSystemMeshShapeType.Triangle;
     var em=ps.emission;em.rateOverTime=240;var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(0f,0f);v.y=new ParticleSystem.MinMaxCurve(.1f,.3f);v.z=new ParticleSystem.MinMaxCurve(-.25f,-.05f);
    }
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,f+"/PF_FlameCrescent_029.prefab");
   }
   finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(f+"/M_HitPattern_110.mat");p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.NativeReplaceBody=false;p.Duration=1.8f;p.Count=1;p.RibbonCount=0;p.UseMist=false;p.NativeScale=.25f;p.NativeImpactScale=.7f;EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
   string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"flame_crescent_029_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="낫",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=9,maxParticleCapacity=592,patternTris=2,emitterMeshTris=48;public string scope="Two crescent emitters diverge after supplied impact; no secondary damage or collision queries.";}
  static Mesh Arc()
  {
   var v=new List<Vector3>();var uv=new List<Vector2>();var t=new List<int>();
   for(int i=0;i<=24;i++){float u=i/24f,a=Mathf.Lerp(-110,110,u)*Mathf.Deg2Rad;float w=.025f+.08f*Mathf.Sin(u*Mathf.PI);foreach(float r in new[]{.5f-w,.5f}){v.Add(new Vector3(Mathf.Cos(a)*r-.15f,Mathf.Sin(a)*r,0));uv.Add(new Vector2(u,r));}if(i>0){int b=(i-1)*2;t.AddRange(new[]{b,b+1,b+2,b+1,b+3,b+2});}}
   var mesh=new Mesh{name="FlameCrescentEmitter"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
  }
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("Crescent_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.SetImpactClock(.6f);e.Begin(Vector3.up,null,new Vector3(0,1,4),Color.white);e.Sample(.4f);if(e.FireSplitSeparation!=0)throw new Exception("Early split");e.Sample(1.1f);if(e.FireSplitSeparation<1.3f||e.FireBoltParticles==0)throw new Exception("Missing fan split");e.Sample(e.Life);if(e.FireBoltParticles!=0||e.FireHitPatternOpacity!=0)throw new Exception("Lingering effect");return "PASS_CONFIRMED_IMPACT_TWO_CRESCENTS_AND_CLEANUP";}
   finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
