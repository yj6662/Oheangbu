using System;using System.IO;using UnityEditor;using UnityEngine;using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class Vfx120FireSwordBuilder
 {
  public static string Build()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");string f=Vfx120Editor.AssetRoot+"/FireBolt";
   var p=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(Vfx120Editor.AssetRoot+"/Profiles/035_B11B.asset");string b=Path.Combine(Vfx120Editor.Output,"FireOriginals/035_B11B.asset.txt");if(!File.Exists(b))File.Copy(AssetDatabase.GetAssetPath(p),b);
   string mp=f+"/M_FireSwordCore.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,mp);}mat.SetColor("_BaseColor",new Color(.15f,.035f,.012f));mat.SetFloat("_Metallic",.7f);mat.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(mat);
   string meshPath=f+"/SM_FireSwordCore.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(mesh==null){mesh=new Mesh{name="FireSwordCore"};AssetDatabase.CreateAsset(mesh,meshPath);}mesh.Clear();mesh.vertices=new[]{new Vector3(0,.05f,0),new Vector3(-.055f,.2f,0),new Vector3(0,.2f,.025f),new Vector3(.055f,.2f,0),new Vector3(0,.2f,-.025f),new Vector3(0,1.25f,0)};mesh.triangles=new[]{0,2,1,0,3,2,0,4,3,0,1,4,1,2,5,2,3,5,3,4,5,4,1,5};mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(f+"/PF_FireBolt_025.prefab"));
   try{go.name="PF_FireSword_035";var blade=new GameObject("BladeCore");blade.transform.SetParent(go.transform,false);blade.AddComponent<MeshFilter>().sharedMesh=mesh;blade.AddComponent<MeshRenderer>().sharedMaterial=mat;
    go.transform.Find("Travel").localPosition=new Vector3(0,.65f,0);
    foreach(var ps in go.transform.Find("Travel").GetComponentsInChildren<ParticleSystem>())
    {ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.07f,1.05f,.04f);var v=ps.velocityOverLifetime;v.x=new ParticleSystem.MinMaxCurve(-.12f,.12f);v.y=new ParticleSystem.MinMaxCurve(.25f,.5f);v.z=new ParticleSystem.MinMaxCurve(-.1f,.1f);var main=ps.main;main.startLifetime=new ParticleSystem.MinMaxCurve(.15f,.3f);if(ps.name=="Combustion"||ps.name=="TornFlame"){main.startSize=new ParticleSystem.MinMaxCurve(.1f,.19f);var em=ps.emission;em.rateOverTime=180;}}
    p.NativeBodyPrefab=PrefabUtility.SaveAsPrefabAsset(go,f+"/PF_FireSword_035.prefab");
   }finally{UnityEngine.Object.DestroyImmediate(go);}
   p.PatternMaterial=AssetDatabase.LoadAssetAtPath<Material>(f+"/M_HitPattern_110.mat");p.Duration=2.4f;p.NativeFieldPrefab=null;p.NativeImpactPrefab=null;p.NativeReplaceBody=false;p.NativeBodyMotion=Vfx120NativeBodyMotion.None;p.Count=1;p.RibbonCount=0;p.UseMist=false;EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();string c=Check(p);File.WriteAllText(Path.Combine(Vfx120Editor.Output,"fire_sword_035_build.json"),JsonUtility.ToJson(new Report{technicalCheck=c},true));return c;
  }
  [Serializable]class Report{public string glyph="넛",status="AUTHORED_AWAITING_USER_VISUAL_REVIEW",technicalCheck;public int particleSystems=7,maxParticleCapacity=432,patternTris=4,bladeTris=8;}
  static string Check(Vfx120Profile p)
  {
   var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();var go=new GameObject("FireSword_Check");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);
   try{var e=go.AddComponent<Vfx120Effect>();e.Profile=p;e.PreviewControlled=true;e.Begin(Vector3.up,null,Vector3.forward,Color.white);e.Sample(.3f);if(e.FireAuraParticles==0)throw new Exception("No blade fire");Vector3 grip=new Vector3(1,1,0);e.SetFireSwordGrip(grip,Quaternion.Euler(0,0,-70));e.Sample(.6f);if(e.FireSwordGripError(grip)>.001f||e.FireAuraContacts!=0)throw new Exception("Grip or fake contact");e.SignalFireAuraHit(Vector3.one);e.Sample(.62f);if(e.FireAuraMarkOpacity<.8f)throw new Exception("No contact motif");e.Sample(e.Life);if(e.FireAuraParticles!=0||e.FireAuraMarkOpacity!=0)throw new Exception("Lingering");return "PASS_GRIP_FIRE_CONFIRMED_MOTIF_AND_CLEANUP";}finally{UnityEngine.Object.DestroyImmediate(go);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
