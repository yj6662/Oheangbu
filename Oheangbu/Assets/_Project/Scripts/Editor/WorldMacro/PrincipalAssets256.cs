using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.AI;
using Oheangbu.App.World.UI;
using Oheangbu.App.World;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class PrincipalAssets256
 {
  const string Folder="Assets/_Project/Art/Characters/Principal256";
  const string Output="../Art/Characters/Principal256";
  const string Candidate="Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity";
  static readonly string[] Ids={"wangso","jeongdam"};
  public static string Inspect(string unused)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var scene=SceneManager.GetActiveScene();if(scene.isDirty)throw new Exception("Unsaved scene changes");
   if(scene.path!=Candidate)scene=EditorSceneManager.OpenScene(Candidate);
   var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true));
   var lines=new List<string>();
   foreach(var id in new[]{"wangso_w1","jeongdam_j1"})
   foreach(var p in all.Where(x=>x.Id==id))
   {lines.Add(id+" root="+p.transform.name+" active="+p.gameObject.activeInHierarchy+" pos="+p.transform.position+" scale="+p.transform.lossyScale+" visual="+p.Visual);foreach(var t in p.GetComponentsInChildren<Transform>(true))lines.Add("  "+AnimationUtility.CalculateTransformPath(t,p.transform)+" components="+string.Join(",",t.GetComponents<Component>().Select(c=>c==null?"MISSING":c.GetType().Name)));}
   return string.Join("\n",lines);
  }
  public static string Import(string unused)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var report=new List<string>{"Principal256 imported for technical review; no scene installation implied."};
   foreach(string id in Ids)
   {
    string folder=Folder+"/"+id;DevSceneKit.EnsureFolder(folder);string source=Output+"/"+id;
    foreach(var pair in new[]{("Body","result_rigged_character_fbx_url.fbx"),("Walk","result_basic_animations_walking_fbx_url.fbx"),("Run","result_basic_animations_running_fbx_url.fbx")})
    {
     string path=folder+"/"+pair.Item1+".fbx";File.Copy(source+"/prepared/"+pair.Item1+".fbx",path,true);AssetDatabase.ImportAsset(path);
     var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.animationType=ModelImporterAnimationType.Generic;
     importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=pair.Item1!="Body";importer.optimizeGameObjects=false;importer.isReadable=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.motionNodeName="Hips";importer.importNormals=ModelImporterNormals.Calculate;importer.normalCalculationMode=ModelImporterNormalCalculationMode.AreaAndAngleWeighted;importer.normalSmoothingAngle=70;
     var clips=importer.defaultClipAnimations;foreach(var clip in clips){clip.loopTime=true;clip.lockRootPositionXZ=true;clip.lockRootHeightY=true;clip.lockRootRotation=true;}if(clips.Length>0)importer.clipAnimations=clips;importer.SaveAndReimport();
    }
    foreach(string kind in new[]{"base_color","normal"})
    {
     string path=folder+"/"+kind+".png";File.Copy(source+"/texture/texture_urls_0_"+kind+".png",path,true);AssetDatabase.ImportAsset(path);
     var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=kind=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;importer.maxTextureSize=2048;importer.SaveAndReimport();
    }
    string matPath=folder+"/Surface.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,matPath);}
    material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png"));material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/normal.png"));material.EnableKeyword("_NORMALMAP");material.SetFloat("_Smoothness",.12f);material.SetFloat("_BumpScale",.5f);EditorUtility.SetDirty(material);
    var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Body.fbx"));
    try
    {
     root.name=id+"_Principal256";var animator=root.GetComponent<Animator>();if(animator==null||animator.avatar==null||!animator.avatar.isValid)throw new Exception(id+" invalid avatar");animator.applyRootMotion=false;
     foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){if(skin.sharedMesh==null||skin.bones.Any(b=>b==null))throw new Exception(id+" skin binding missing");skin.sharedMaterials=Enumerable.Repeat(material,skin.sharedMaterials.Length).ToArray();report.Add(id+" vertices="+skin.sharedMesh.vertexCount+" bones="+skin.bones.Length+" bounds="+skin.bounds);}
     report.Add(id+" bones="+string.Join(",",root.GetComponentsInChildren<Transform>().Select(t=>t.name)));
     foreach(string mode in new[]{"Walk","Run"}){var clip=Clip(id,mode);report.Add(id+" "+mode+" duration="+clip.length+" bindings="+AnimationUtility.GetCurveBindings(clip).Length);}
     PrefabUtility.SaveAsPrefabAsset(root,folder+"/Review.prefab");
    }finally{Object.DestroyImmediate(root);}
   }
   AssetDatabase.SaveAssets();string result=string.Join("\n",report);File.WriteAllText(Output+"/unity-import.txt",result);return result;
  }
  static AnimationClip Clip(string id,string mode)=>AssetDatabase.LoadAllAssetsAtPath(Folder+"/"+id+"/"+mode+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
  static Bounds VisualBounds(GameObject go)
  {
   Bounds bounds=default;bool first=true;var mesh=new Mesh();
   try{foreach(var skin in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)){skin.BakeMesh(mesh);foreach(var v in mesh.vertices){var p=skin.transform.TransformPoint(v);if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}}}
   finally{Object.DestroyImmediate(mesh);}if(first)throw new Exception("Empty skinned geometry");return bounds;
  }
  static RuntimeAnimatorController Controller(string id,GameObject root)
  {
   string folder=Folder+"/"+id;var bones=root.GetComponentsInChildren<Transform>(true);
   var positions=bones.Select(t=>t.localPosition).ToArray();var rotations=bones.Select(t=>t.localRotation).ToArray();var scales=bones.Select(t=>t.localScale).ToArray();
   var walk=Clip(id,"Walk");walk.SampleAnimation(root,0);var a=bones.Select(t=>t.localRotation).ToArray();walk.SampleAnimation(root,walk.length*.5f);
   for(int i=0;i<bones.Length;i++)
   {
    bool arm=bones[i].name.Contains("Arm")&&!bones[i].name.Contains("Armature")||bones[i].name.Contains("Hand")||bones[i].name.Contains("Shoulder");
    var idle=arm?Quaternion.Slerp(a[i],bones[i].localRotation,.5f):rotations[i];
    bones[i].localPosition=positions[i];bones[i].localScale=scales[i];bones[i].localRotation=idle;
   }
   string clipPath=folder+"/Idle.anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,clipPath);}clip.ClearCurves();clip.frameRate=30;
   foreach(var t in bones)
   {
    string path=AnimationUtility.CalculateTransformPath(t,root.transform);var p=t.localPosition;var q=t.localRotation;
    void C(string n,float value)=>AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),n),AnimationCurve.Constant(0,1.2f,value));
    C("m_LocalPosition.x",p.x);C("m_LocalPosition.y",p.y);C("m_LocalPosition.z",p.z);C("m_LocalRotation.x",q.x);C("m_LocalRotation.y",q.y);C("m_LocalRotation.z",q.z);C("m_LocalRotation.w",q.w);
   }
   var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;AnimationUtility.SetAnimationClipSettings(clip,settings);EditorUtility.SetDirty(clip);
   string controllerPath=folder+"/Locomotion.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);if(controller==null)controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
   var sm=controller.layers[0].stateMachine;foreach(var state in sm.states)sm.RemoveState(state.state);
   sm.defaultState=sm.AddState("Idle");sm.defaultState.motion=clip;sm.AddState("Walk").motion=walk;sm.AddState("Run").motion=Clip(id,"Run");EditorUtility.SetDirty(controller);return controller;
  }
  public static string Prepare(string unused)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");var report=new List<string>();
   foreach(string id in Ids)
   {
    var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+id+"/Review.prefab"));
    try {var animator=go.GetComponent<Animator>();animator.runtimeAnimatorController=Controller(id,go);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var motion=go.GetComponent<PrincipalNpcMotion256>();if(motion==null)motion=go.AddComponent<PrincipalNpcMotion256>();motion.Animator=animator;motion.IsWangso=id=="wangso";PrefabUtility.SaveAsPrefabAsset(go,Folder+"/"+id+"/Character.prefab");report.Add(id+" idle/walk/run ready");}
    finally{Object.DestroyImmediate(go);}
   }
   AssetDatabase.SaveAssets();return string.Join("\n",report);
  }
  public static string Install(string unused)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=Candidate||SceneManager.GetActiveScene().isDirty)throw new Exception("Clean candidate Edit scene required");
   if(!File.Exists(Output+"/motion-accepted.json"))throw new Exception("Motion review required before scene installation");
   var scene=SceneManager.GetActiveScene();var session=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var report=new List<string>();
   foreach(string id in Ids)
   {
    string pointId=id=="wangso"?"wangso_w1":"jeongdam_j1";
    var point=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).Single(p=>p.Id==pointId&&p.gameObject.activeInHierarchy);var actor=point.transform;Vector3 position=actor.position;
    if(id=="wangso"&&session.DemoEscortCompanion!=actor)throw new Exception("Unexpected escort root");
    var old=actor.Find("PrincipalVisual256");if(old!=null)Object.DestroyImmediate(old.gameObject);
    var legacy=actor.Find("Replaceable_Innkeeper_Visual");if(legacy==null)throw new Exception("Expected replaceable visual missing");legacy.gameObject.SetActive(false);
    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+id+"/Character.prefab"),scene);visual.name="PrincipalVisual256";visual.transform.SetParent(actor,false);
    var rr=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var bounds=VisualBounds(visual);
    visual.transform.localScale*= (id=="wangso"?1.68f:1.78f)/bounds.size.y;
    bounds=VisualBounds(visual);visual.transform.position+=Vector3.up*(position.y-bounds.min.y);
    var motion=visual.GetComponent<PrincipalNpcMotion256>();motion.MotionRoot=actor;motion.Session=session;
    point.Visual=visual.transform;foreach(var binding in session.InteractionVisuals.Where(v=>v.Id==pointId))binding.Renderers=rr.Cast<Renderer>().ToArray();
    foreach(var skin in rr){skin.updateWhenOffscreen=true;if(skin.bones.Any(b=>b==null)||skin.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported))throw new Exception("Invalid skin or material");}
    if(actor.position!=position)throw new Exception("Gameplay root moved");EditorUtility.SetDirty(point);report.Add(pointId+" visual replaced, gameplay root retained, height="+(id=="wangso"?1.68f:1.78f));
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText(Output+"/installation.txt",string.Join("\n",report));return string.Join("\n",report);
  }
  public static string Capture(string unused)
  {
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Candidate||scene.isDirty)throw new Exception("Clean candidate Edit scene required");
   var s=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var camGo=new GameObject("Temporary256Camera");var cam=camGo.AddComponent<Camera>();cam.CopyFrom(s.Walker.ViewCamera);cam.enabled=false;cam.useOcclusionCulling=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
   var rt=RenderTexture.GetTemporary(1200,1200,24);var texture=new Texture2D(1200,1200,TextureFormat.RGB24,false);var previous=RenderTexture.active;var lines=new List<string>();
   try
   {
    cam.targetTexture=rt;cam.aspect=1;cam.fieldOfView=35;cam.nearClipPlane=.05f;
    foreach(string id in Ids)
    {
     string pid=id=="wangso"?"wangso_w1":"jeongdam_j1";var actor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).Single(p=>p.Id==pid&&p.gameObject.activeInHierarchy).transform;
     var original=actor.Find("PrincipalVisual256");if(original==null)throw new Exception("Installed model missing");
     var clone=Object.Instantiate(original.gameObject,actor);clone.name="Temporary256Pose";original.gameObject.SetActive(false);
     try
     {
      clone.GetComponent<PrincipalNpcMotion256>().enabled=false;clone.GetComponent<Animator>().enabled=false;
      foreach(string mode in new[]{"Idle","Walk","Run"})
      {
       var clip=mode=="Idle"?AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/"+id+"/Idle.anim"):Clip(id,mode);clip.SampleAnimation(clone,mode=="Idle"?0:clip.length*.25f);
       var b=VisualBounds(clone);lines.Add(id+" "+mode+" baked bounds="+b);
       Vector3 focus=actor.position+Vector3.up*.9f;cam.transform.position=focus+actor.forward*3.45f+actor.right*.3f+Vector3.up*.1f;cam.transform.LookAt(focus);
       cam.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,1200),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+id+"/unity_"+mode+".png",texture.EncodeToPNG());
      }
     }finally{original.gameObject.SetActive(true);Object.DestroyImmediate(clone);}
    }
   }finally{RenderTexture.active=previous;cam.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(camGo);EditorSceneManager.OpenScene(Candidate);}
   File.WriteAllText(Output+"/unity-pose-checks.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
  public static string Runtime(string command)
  {
   if(!EditorApplication.isPlaying)throw new Exception("Private Play required");
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();if(s==null||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Normal save is protected");
   var ui=PlaytestUiRoot.Instance;var lines=new List<string>();
   foreach(string id in new[]{"wangso_w1","jeongdam_j1","wangso_w1"})
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();var point=s.Content.Points.Single(p=>p.Id==id);bool approach=false;
    for(int i=0;i<16;i++){float a=i*Mathf.PI/8;var p=point.Position+new Vector3(Mathf.Sin(a)*2,0,Mathf.Cos(a)*2);if(!NavMesh.SamplePosition(p,out var hit,2,NavMesh.AllAreas))continue;s.Teleport(hit.position+Vector3.up*.08f,Quaternion.LookRotation(point.Position-hit.position).eulerAngles.y);Physics.SyncTransforms();if(s.CanInteract(id)){approach=true;break;}}
    if(!approach||!s.Interact(id))throw new Exception(id+" interaction failed");
    var binding=s.InteractionVisuals.Single(v=>v.Id==id);if(binding.Renderers.Length==0||binding.Renderers.Any(r=>r==null||!r.gameObject.activeInHierarchy||!(r is SkinnedMeshRenderer)))throw new Exception(id+" outline renderer binding invalid");
    var motion=binding.Renderers[0].GetComponentInParent<PrincipalNpcMotion256>();if(motion==null||motion.Animator==null||motion.Animator.runtimeAnimatorController==null||motion.AllowRun)throw new Exception(id+" animation observer invalid");
    lines.Add("PASS "+id+" actual session interaction, active new skinned renderer bindings and idle/walk controller. Teleport approach fixture, not manual navigation.");
   }
   if(!s.Progress.campaign.Facts.Contains("met:jeongdam")||!s.Progress.campaign.Completed.Contains("cargo_contract"))throw new Exception("Conversation facts/contract missing");
   lines.Add("PASS Jeongdam contact and Wangso contract committed in private diagnostic save; existing quest order retained.");ui.CloseMenu();ui.Gate.ReleaseImmediately();File.WriteAllText(Output+"/runtime-checks.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
