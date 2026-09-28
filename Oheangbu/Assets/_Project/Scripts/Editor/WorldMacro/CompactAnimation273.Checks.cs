using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static Vector3[] WorldSkin273(SkinnedMeshRenderer skin)
  {
   var mesh=skin.sharedMesh;var v=mesh.vertices;var w=mesh.boneWeights;var poses=mesh.bindposes;
   var matrices=skin.bones.Select((b,i)=>b.localToWorldMatrix*poses[i]).ToArray();var output=new Vector3[v.Length];
   for(int i=0;i<v.Length;i++){var b=w[i];output[i]=matrices[b.boneIndex0].MultiplyPoint3x4(v[i])*b.weight0+matrices[b.boneIndex1].MultiplyPoint3x4(v[i])*b.weight1+matrices[b.boneIndex2].MultiplyPoint3x4(v[i])*b.weight2+matrices[b.boneIndex3].MultiplyPoint3x4(v[i])*b.weight3;}return output;
  }
  static string CheckAnimation273()
  {
   var scene=FrontageScene249();var report=new List<string>();void C(bool ok,string name)=>report.Add((ok?"PASS ":"FAIL ")+name);
   var preview=EditorSceneManager.NewPreviewScene();
   void CheckClips(GameObject source,AnimationClip[] clips,string label,bool support=false)
   {
    var model=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(model,preview);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
    foreach(var m in model.GetComponentsInChildren<MonoBehaviour>(true))m.enabled=false;
    var transforms=model.GetComponentsInChildren<Transform>();var positions=transforms.Select(t=>t.localPosition).ToArray();var rotations=transforms.Select(t=>t.localRotation).ToArray();var scales=transforms.Select(t=>t.localScale).ToArray();
    void Reset(){for(int i=0;i<transforms.Length;i++){transforms[i].localPosition=positions[i];transforms[i].localRotation=rotations[i];transforms[i].localScale=scales[i];}}
    var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();var baked=new Mesh();
    try
    {
     C(clips.All(c=>c!=null&&!c.empty),label+" nonempty imported clips");
     C(clips.All(c=>AnimationUtility.GetCurveBindings(c).All(b=>b.path==""||model.transform.Find(b.path)!=null)),label+" every clip transform path exists");
     C(skins.All(s=>s.bones.All(b=>b!=null)),label+" complete skin bones");
     foreach(var clip in clips)
     {
      Reset();clip.SampleAnimation(model,0);var baseline=skins.Select(WorldSkin273).ToArray();
      var anchor=support?transforms.Single(t=>t.name=="SupportHand_R"):null;var origin=anchor!=null?anchor.position:Vector3.zero;float movement=0,drift=0;bool finite=true;
      foreach(float u in new[]{.25f,.5f,.65f,.85f,1f})
      {
       clip.SampleAnimation(model,u*clip.length);if(anchor!=null)drift=Mathf.Max(drift,Vector3.Distance(origin,anchor.position));
       for(int k=0;k<skins.Length;k++){var v=WorldSkin273(skins[k]);for(int j=0;j<v.Length;j++){finite&=float.IsFinite(v[j].x)&&float.IsFinite(v[j].y)&&float.IsFinite(v[j].z);movement=Mathf.Max(movement,Vector3.Distance(v[j],baseline[k][j]));}}
      }
      C(finite&&movement>.0001f&&movement<(support?15:4),label+" "+clip.name+" finite bounded world deformation; max="+movement.ToString("F4")+"m");
      if(anchor!=null&&!clip.name.EndsWith("SupportRelease"))C(drift<.001f,label+" "+clip.name+" support wrist fixed; drift="+drift.ToString("F6"));
     }
    }finally{Object.DestroyImmediate(baked);Object.DestroyImmediate(model);}
   }
   try
   {
    var manager=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<DemoSummonCombatManager>(true)).Single();
    C(manager.Profiles.Length==5&&manager.Profiles.All(p=>AssetDatabase.GetAssetPath(p).Contains("/Animation273/")),"five isolated candidate summon profiles");
    foreach(var p in manager.Profiles)
    {
     CheckClips(p.PresentationPrefab,new[]{p.FormationClip,p.DissolveClip},p.Letter);
     var go=new GameObject("Summon clock fixture");SceneManager.MoveGameObjectToScene(go,preview);go.SetActive(false);var presentation=go.AddComponent<DemoSummonPresentation>();presentation.Configure(p);
     var clock=new SummonCombatClock(1.2f,10,1);clock.Advance(.6f);presentation.Sample(clock,0,0);C(presentation.HasAnimationGraph,p.Letter+" real formation graph evaluates");
     var before=go.GetComponentsInChildren<Transform>(true).Select(t=>t.localRotation).ToArray();clock.Advance(0);presentation.Sample(clock,0,0);C(before.SequenceEqual(go.GetComponentsInChildren<Transform>(true).Select(t=>t.localRotation)),p.Letter+" paused clock holds pose");
     clock.Advance(.7f);clock.BeginDissolve();clock.Advance(.5f);presentation.Sample(clock,0,0);C(clock.Phase==SummonPhase.Dissolving,p.Letter+" real dissolve phase samples");Object.DestroyImmediate(go);
    }
    var enemies=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<EnemyRigMotion273>(true)).ToArray();C(enemies.Length==5,"five active ordinary enemies wired");
    foreach(var family in enemies.GroupBy(e=>e.Attack))
    {var e=family.First();CheckClips(e.Animator.gameObject,new[]{e.Attack,e.Hit,e.Stun,e.Death},e.name);}
    foreach(var source in enemies)
    {
     var clone=Object.Instantiate(source.gameObject);SceneManager.MoveGameObjectToScene(clone,preview);clone.transform.position=Vector3.zero;
     foreach(var c in clone.GetComponents<MonoBehaviour>())c.enabled=false;
     var driver=clone.GetComponent<EnemyRigMotion273>();var encounter=clone.GetComponent<Oheangbu.App.Prologue.PrologueEncounter>();
     void Call(object o,string n)=>o.GetType().GetMethod(n,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null);
     var state=typeof(EnemyController).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic);
     try
     {
      Call(encounter,"Awake");driver.Vitals.Restore();Call(driver,"OnEnable");Call(encounter,"OnEnable");
      state.SetValue(driver.Enemy,Enum.Parse(state.FieldType,"Stunned"));driver.Evaluate(Time.time,.016f);C(driver.CurrentPose=="Stun",source.name+" actual enemy stun selects authored clip");
      state.SetValue(driver.Enemy,Enum.Parse(state.FieldType,"Idle"));driver.Vitals.TakeDamage(1);driver.Evaluate(Time.time,.016f);C(driver.CurrentPose=="Hit",source.name+" actual damage triggers hit clip");
      driver.Vitals.TakeDamage(100000);driver.Evaluate(Time.time+.5f,.016f);encounter.SetPresentationVisibility(true);
      C(driver.CurrentPose=="Death"&&clone.GetComponentsInChildren<Renderer>().Any(r=>r.enabled)&&clone.GetComponentsInChildren<Collider>().All(c=>!c.enabled),source.name+" death motion remains visible with combat colliders disabled");
      typeof(Oheangbu.App.Prologue.PrologueEncounter).GetField("hideDeadAt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(encounter,Time.time-1);Call(encounter,"Update");encounter.SetPresentationVisibility(true);C(clone.GetComponentsInChildren<Renderer>().All(r=>!r.enabled),source.name+" completed death stays hidden after update and repeated cull");
      driver.Vitals.Restore();driver.Evaluate(Time.time+2,.016f);encounter.SetPresentationVisibility(true);C(driver.CurrentPose=="Idle"&&clone.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),source.name+" life revision restores idle and authored visibility");
     }finally{Call(driver,"OnDisable");Call(encounter,"OnDisable");Object.DestroyImmediate(clone);}
    }
    C(EnemyRigMotion273.AttackPhase(true,0,false,0)==0&&EnemyRigMotion273.AttackPhase(true,1,false,0)==.5f&&EnemyRigMotion273.AttackPhase(false,0,true,1)==1,"enemy anticipation/contact/recovery phase boundaries");
    var actor=VillageSession().Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);var sr=actor.GetComponent<SinmokRigAnimation>();
    C(sr.FacingVisual!=null&&sr.Animator.avatar!=null&&sr.Animator.avatar.isValid,"generated Sinmok avatar and facing adapter");
    C(actor.GetComponentsInChildren<Renderer>().Count(r=>r.enabled)==4,"only four generated Sinmok surfaces visible");
    CheckClips(sr.FacingVisual.gameObject,new[]{sr.Idle}.Concat(sr.Attacks).Concat(new[]{sr.SupportRelease}).ToArray(),"Sinmok",true);
    C(VillageSession().Content.SaveSlot=="world-demo-compact-cave-v4"&&VillageSession().Content.Campaign.IsValid,"campaign and save slot retained");
   }catch(Exception e){report.Add("FAIL "+e);}finally{EditorSceneManager.ClosePreviewScene(preview);}
   File.WriteAllLines("../Art/Characters/Animation273/unity-checks.txt",report);return string.Join("\n",report);
  }
  static string CaptureAnimation273()
  {
   FrontageScene249();var source=VillageSession().Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId).GetComponent<SinmokRigAnimation>();
   var preview=EditorSceneManager.NewPreviewScene();var model=Object.Instantiate(source.FacingVisual.gameObject);SceneManager.MoveGameObjectToScene(model,preview);model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
   var go=new GameObject("Offscreen model camera");SceneManager.MoveGameObjectToScene(go,preview);var cam=go.AddComponent<Camera>();cam.scene=preview;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.19f,.20f,.18f);cam.orthographic=true;cam.orthographicSize=10;cam.transform.position=new Vector3(4,8,32);cam.transform.LookAt(new Vector3(0,6,0));
   foreach(var a in model.GetComponentsInChildren<Animator>())a.enabled=false;
   var light=new GameObject("Preview light");SceneManager.MoveGameObjectToScene(light,preview);var sun=light.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2f;light.transform.rotation=Quaternion.Euler(35,155,0);
   var skins=model.GetComponentsInChildren<SkinnedMeshRenderer>();var snapshots=new List<Mesh>();
   foreach(var skin in skins){skin.enabled=false;var snapshot=new GameObject("Snapshot "+skin.name);SceneManager.MoveGameObjectToScene(snapshot,preview);var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.vertices=WorldSkin273(skin);mesh.triangles=skin.sharedMesh.triangles;mesh.uv=skin.sharedMesh.uv;mesh.RecalculateNormals();mesh.RecalculateTangents();snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;snapshots.Add(mesh);}
   var rt=new RenderTexture(1920,1080,24);var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;Directory.CreateDirectory("../Art/Characters/Animation273/UnityCaptures");
   try
   {
    cam.targetTexture=rt;cam.aspect=16f/9;
    foreach(var clip in new[]{source.Idle}.Concat(source.Attacks).Concat(new[]{source.SupportRelease}))foreach(float u in new[]{0f,.5f,.65f,.85f})
    {
     clip.SampleAnimation(model,u*clip.length);for(int i=0;i<skins.Length;i++){snapshots[i].vertices=WorldSkin273(skins[i]);snapshots[i].RecalculateNormals();snapshots[i].RecalculateTangents();snapshots[i].RecalculateBounds();}cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();File.WriteAllBytes("../Art/Characters/Animation273/UnityCaptures/"+clip.name.Replace('|','_')+"_"+Mathf.RoundToInt(u*100)+".png",image.EncodeToPNG());
    }
   }finally{cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(model);foreach(var mesh in snapshots)Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(preview);}
   return "24 Unity offscreen generated Sinmok pose captures, candidate and save untouched";
  }
  static string CaptureScene273()
  {
   var scene=FrontageScene249();var session=VillageSession();var actor=session.Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);
   var art=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var observer=art.Observer;
   var go=new GameObject("Sinmok273 offscreen scene camera"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(session.Walker.ViewCamera);camera.enabled=false;
   EditorUtility.CopySerialized(session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   go.AddComponent<Skybox>().material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prior=RenderTexture.active;
   try
   {
    art.Observer=camera;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=49;camera.clearFlags=CameraClearFlags.Skybox;
    var target=actor.transform.position+Vector3.up*6;camera.transform.position=actor.transform.position+new Vector3(14,8,29);camera.transform.LookAt(target);
    camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes("../Art/Characters/Animation273/candidate-scene.png",tex.EncodeToPNG());
   }finally{art.Observer=observer;camera.targetTexture=null;RenderTexture.active=prior;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   return "Actual saved candidate offscreen capture, no Play or player input";
  }
 }
}
