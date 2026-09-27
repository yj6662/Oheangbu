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
 public static class PrincipalAssets257
 {
  const string Folder="Assets/_Project/Art/Characters/Principal257";
  const string Output="../Art/Characters/Principal257";
  const string Candidate="Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity";
  static readonly string[] Ids={"wangso"};
  static EditorApplication.CallbackFunction motionTick;
  static string motionResult="Not started";
  public static string MotionCheck(string command)
  {
   if(command=="status")return motionResult;
   if(motionTick!=null)throw new Exception("Motion check already active");
   var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
   if(!EditorApplication.isPlaying||session==null||!session.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private diagnostic Play required");
   var observer=Object.FindFirstObjectByType<PrincipalNpcMotion257>();
   if(observer==null||!observer.AllowRun)throw new Exception("Verified run-enabled Wangso required");
   var actor=observer.MotionRoot;var start=actor.position;var rotation=actor.rotation;
   var agent=actor.GetComponent<NavMeshAgent>();bool agentEnabled=agent!=null&&agent.enabled;
   if(agentEnabled)agent.enabled=false;
   bool background=Application.runInBackground;Application.runInBackground=true;
   double begin=EditorApplication.timeSinceStartup,last=begin;
   var states=new HashSet<string>();var hands=new Dictionary<string,List<Vector3>>();
   var hand=observer.GetComponentsInChildren<Transform>().First(t=>t.name.Contains("RightHand"));
   motionResult="RUNNING";
   void Finish(string result)
   {
    EditorApplication.update-=motionTick;motionTick=null;
    if(actor!=null){actor.position=start;actor.rotation=rotation;}
    if(agent!=null)agent.enabled=agentEnabled;
    Application.runInBackground=background;
    motionResult=result;File.WriteAllText(Output+"/motion-runtime.txt",result);
   }
   motionTick=()=>
   {
    try
    {
     if(!EditorApplication.isPlaying||actor==null){Finish("FAIL Play interrupted");return;}
     double now=EditorApplication.timeSinceStartup;float elapsed=(float)(now-begin);float dt=Mathf.Min((float)(now-last),.1f);last=now;
     float speed=elapsed<1.5f?0:elapsed<3.5f?.8f:elapsed<5.5f?4:0;
     // Controlled root motion exercises the live observer and Animator, without a save mutation.
     actor.position+=actor.right*(Mathf.Sin(elapsed*3)>0?1:-1)*speed*dt;
     string state=observer.CurrentState;
     if(!string.IsNullOrEmpty(state))
     {
      states.Add(state);if(!hands.TryGetValue(state,out var samples)){samples=new List<Vector3>();hands[state]=samples;}
      samples.Add(actor.InverseTransformPoint(hand.position));
     }
     if(elapsed<7)return;
     var lines=new List<string>{"Private Play: live root movement -> observer -> Animator. Controlled movement fixture; not continuous manual escort."};
     foreach(string expected in new[]{"Idle","Walk","Run"})
     {
      if(!states.Contains(expected)||!hands.TryGetValue(expected,out var samples)||samples.Count<3)throw new Exception(expected+" missing");
      var bounds=new Bounds(samples[0],Vector3.zero);foreach(var p in samples)bounds.Encapsulate(p);
      if(expected!="Idle"&&bounds.size.magnitude<.005f)throw new Exception(expected+" hand did not animate");
      lines.Add("PASS "+expected+" samples="+samples.Count+" hand local excursion="+bounds.size);
     }
     Finish(string.Join("\n",lines));
    }catch(Exception error){Finish("FAIL "+error.Message);}
   };
   EditorApplication.update+=motionTick;return motionResult;
  }
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
   var report=new List<string>{"Principal257 imported for technical review; no scene installation implied."};
   foreach(string id in Ids)
   {
    string folder=Folder+"/"+id;DevSceneKit.EnsureFolder(folder);string source=Output+"/"+id;
    foreach(var pair in new[]{("Body","result_rigged_character_fbx_url.fbx"),("Walk","result_basic_animations_walking_fbx_url.fbx"),("Run","result_basic_animations_running_fbx_url.fbx")})
    {
     string path=folder+"/"+pair.Item1+".fbx";File.Copy(source+"/prepared/"+pair.Item1+".fbx",path,true);AssetDatabase.ImportAsset(path);
     var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.animationType=ModelImporterAnimationType.Generic;
     importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=pair.Item1!="Body";importer.optimizeGameObjects=false;importer.isReadable=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.motionNodeName="Hips";importer.importNormals=ModelImporterNormals.Import;importer.normalCalculationMode=ModelImporterNormalCalculationMode.AreaAndAngleWeighted;importer.normalSmoothingAngle=70;
     var clips=importer.defaultClipAnimations;foreach(var clip in clips){clip.loopTime=true;clip.lockRootPositionXZ=true;clip.lockRootHeightY=true;clip.lockRootRotation=true;}if(clips.Length>0)importer.clipAnimations=clips;importer.SaveAndReimport();
    }
    foreach(string kind in new[]{"base_color"})
    {
     string path=folder+"/"+kind+".png";File.Copy(source+"/prepared/"+kind+".png",path,true);AssetDatabase.ImportAsset(path);
     var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=kind=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;importer.maxTextureSize=4096;importer.SaveAndReimport();
    }
    string matPath=folder+"/Surface.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,matPath);}
    material.shader=Shader.Find("Oheangbu/DesaturatedAssetLit");material.SetFloat("_AssetSaturation",.8f);material.SetFloat("_AssetAmbient",.4f);material.SetFloat("_AssetValue",1f);
    material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png"));material.SetTexture("_BumpMap",null);material.DisableKeyword("_NORMALMAP");material.SetFloat("_Smoothness",.12f);material.SetFloat("_BumpScale",.22f);EditorUtility.SetDirty(material);
    string hairPath=folder+"/HairTie.mat";var hairMaterial=AssetDatabase.LoadAssetAtPath<Material>(hairPath);if(hairMaterial==null){hairMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(hairMaterial,hairPath);}hairMaterial.SetColor("_BaseColor",new Color(.08f,.075f,.065f));hairMaterial.SetFloat("_Smoothness",.08f);EditorUtility.SetDirty(hairMaterial);
    var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Body.fbx"));
    try
    {
     root.name=id+"_Principal257";var animator=root.GetComponent<Animator>();if(animator==null||animator.avatar==null||!animator.avatar.isValid)throw new Exception(id+" invalid avatar");animator.applyRootMotion=false;
     foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){if(skin.sharedMesh==null||skin.bones.Any(b=>b==null))throw new Exception(id+" skin binding missing");skin.sharedMaterials=Enumerable.Range(0,skin.sharedMesh.subMeshCount).Select(i=>i==0?material:hairMaterial).ToArray();report.Add(id+" vertices="+skin.sharedMesh.vertexCount+" bones="+skin.bones.Length+" bounds="+skin.bounds);}
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
    void C(string n,float value)=>AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),n),AnimationCurve.Constant(0,4f,value));
    C("m_LocalPosition.x",p.x);C("m_LocalPosition.y",p.y);C("m_LocalPosition.z",p.z);C("m_LocalRotation.x",q.x);C("m_LocalRotation.y",q.y);C("m_LocalRotation.z",q.z);C("m_LocalRotation.w",q.w);
   }
   // Subtle breathing and head settling; no root translation or gameplay displacement.
   foreach(var t in bones.Where(t=>t.name=="Spine"||t.name=="Spine1"||t.name=="Head"))
   {
    string path=AnimationUtility.CalculateTransformPath(t,root.transform);Quaternion basis=t.localRotation;
    var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
    for(int i=0;i<=8;i++){float time=i*.5f;float wave=Mathf.Sin(time*Mathf.PI*.5f);var q=basis*Quaternion.Euler(wave*(t.name=="Head"?.45f:.35f),0,0);float[] v={q.x,q.y,q.z,q.w};for(int k=0;k<4;k++)curves[k].AddKey(time,v[k]);}
    string[] components={"x","y","z","w"};for(int k=0;k<4;k++){for(int j=0;j<curves[k].length;j++)curves[k].SmoothTangents(j,0);AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+components[k]),curves[k]);}
   }
   clip.EnsureQuaternionContinuity();
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
    try {var animator=go.GetComponent<Animator>();animator.runtimeAnimatorController=Controller(id,go);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var motion=go.GetComponent<PrincipalNpcMotion257>();if(motion==null)motion=go.AddComponent<PrincipalNpcMotion257>();motion.Animator=animator;motion.IsWangso=id=="wangso";motion.AllowSeatedPose=false;motion.AllowRun=true;PrefabUtility.SaveAsPrefabAsset(go,Folder+"/"+id+"/Character.prefab");report.Add(id+" idle/walk/run ready");}
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
    var old=actor.Find("PrincipalVisual257");if(old!=null)Object.DestroyImmediate(old.gameObject);
    var previous=actor.Find("PrincipalVisual256");if(previous!=null)previous.gameObject.SetActive(false);
    var legacy=actor.Find("Replaceable_Innkeeper_Visual");if(legacy==null)throw new Exception("Expected replaceable visual missing");legacy.gameObject.SetActive(false);
    var visual=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+id+"/Character.prefab"),scene);visual.name="PrincipalVisual257";visual.transform.SetParent(actor,false);
    var rr=visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);var bounds=VisualBounds(visual);
    visual.transform.localScale*= (id=="wangso"?1.68f:1.78f)/bounds.size.y;
    bounds=VisualBounds(visual);visual.transform.position+=Vector3.up*(position.y-bounds.min.y);
    var motion=visual.GetComponent<PrincipalNpcMotion257>();motion.MotionRoot=actor;motion.Session=session;
    point.Visual=visual.transform;foreach(var binding in session.InteractionVisuals.Where(v=>v.Id==pointId))binding.Renderers=rr.Cast<Renderer>().ToArray();
    foreach(var skin in rr){skin.updateWhenOffscreen=true;if(skin.bones.Any(b=>b==null)||skin.sharedMaterials.Any(m=>m==null||m.shader==null||!m.shader.isSupported))throw new Exception("Invalid skin or material");}
    if(actor.position!=position)throw new Exception("Gameplay root moved");EditorUtility.SetDirty(point);report.Add(pointId+" visual replaced, gameplay root retained, height="+(id=="wangso"?1.68f:1.78f));
   }
   EditorUtility.SetDirty(session);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText(Output+"/installation.txt",string.Join("\n",report));return string.Join("\n",report);
  }
  public static string Capture(string unused)
  {
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Candidate||scene.isDirty)throw new Exception("Clean candidate Edit scene required");
   var s=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();var camGo=new GameObject("Temporary257Camera");var cam=camGo.AddComponent<Camera>();cam.CopyFrom(s.Walker.ViewCamera);cam.enabled=false;cam.useOcclusionCulling=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());
   var rt=RenderTexture.GetTemporary(1200,1200,24);var texture=new Texture2D(1200,1200,TextureFormat.RGB24,false);var previous=RenderTexture.active;var lines=new List<string>();bool asyncCompilation=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
   try
   {
    cam.targetTexture=rt;cam.aspect=1;cam.fieldOfView=35;cam.nearClipPlane=.05f;
    foreach(string id in Ids)
    {
     string pid=id=="wangso"?"wangso_w1":"jeongdam_j1";var actor=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true)).Single(p=>p.Id==pid&&p.gameObject.activeInHierarchy).transform;
     var original=actor.Find("PrincipalVisual257");if(original==null)throw new Exception("Installed model missing");
     var clone=Object.Instantiate(original.gameObject,actor);clone.name="Temporary257Pose";original.gameObject.SetActive(false);
     try
     {
      clone.GetComponent<PrincipalNpcMotion257>().enabled=false;clone.GetComponent<Animator>().enabled=false;
      foreach(string mode in new[]{"Idle","Walk","Run"})
      {
       var clip=mode=="Idle"?AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder+"/"+id+"/Idle.anim"):Clip(id,mode);clip.SampleAnimation(clone,mode=="Idle"?0:clip.length*.25f);
       var b=VisualBounds(clone);lines.Add(id+" "+mode+" baked bounds="+b);
       Vector3 focus=actor.position+Vector3.up*.9f;cam.transform.position=focus+actor.forward*3.45f+actor.right*.3f+Vector3.up*.1f;cam.transform.LookAt(focus);
       cam.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,1200),0,0);texture.Apply();File.WriteAllBytes(Output+"/"+id+"/unity_"+mode+".png",texture.EncodeToPNG());
       if(mode=="Idle")
       {
        foreach(float angle in new[]{0f,35f,90f})
        {
         focus=actor.position+Vector3.up*1.535f;
         cam.transform.position=focus+Quaternion.AngleAxis(angle,Vector3.up)*actor.forward*.66f;
         cam.transform.LookAt(focus);cam.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1200,1200),0,0);texture.Apply();
         File.WriteAllBytes(Output+"/"+id+"/unity_head_"+angle+".png",texture.EncodeToPNG());
        }
       }
      }
     }finally{original.gameObject.SetActive(true);Object.DestroyImmediate(clone);}
    }
   }finally{ShaderUtil.allowAsyncCompilation=asyncCompilation;RenderTexture.active=previous;cam.targetTexture=null;RenderTexture.ReleaseTemporary(rt);Object.DestroyImmediate(texture);Object.DestroyImmediate(camGo);EditorSceneManager.OpenScene(Candidate);}
   File.WriteAllText(Output+"/unity-pose-checks.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
  public static string Verify(string unused)
  {
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=Candidate||scene.isDirty)throw new Exception("Clean candidate Edit required");
   var actors=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<WorldMacroContentPoint>(true));
   var actor=actors.Single(p=>p.Id=="wangso_w1"&&p.gameObject.activeInHierarchy);
   var visual=actor.transform.Find("PrincipalVisual257");
   if(visual==null||!visual.gameObject.activeInHierarchy||actor.Visual!=visual)throw new Exception("Wangso visual binding");
   if(actor.GetComponentsInChildren<PrincipalNpcMotion257>(true).Length!=1)throw new Exception("Duplicate observer");
   if(actor.transform.Find("PrincipalVisual256").gameObject.activeSelf)throw new Exception("Legacy model active");
   var animator=visual.GetComponent<Animator>();if(animator==null||animator.runtimeAnimatorController==null||animator.applyRootMotion)throw new Exception("Animator configuration");
   foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
   {
    if(skin.bones.Any(b=>b==null)||skin.sharedMesh==null)throw new Exception("Skin missing reference");
    foreach(var material in skin.sharedMaterials)
     if(material==null||material.shader==null||!material.shader.isSupported||ShaderUtil.ShaderHasError(material.shader))throw new Exception("Material/shader error");
   }
   var jeongdam=actors.Single(p=>p.Id=="jeongdam_j1"&&p.gameObject.activeInHierarchy);
   if(jeongdam.Visual==null||jeongdam.Visual.name!="PrincipalVisual256")throw new Exception("Jeongdam changed");
   string result="PASS clean candidate Edit; one Wangso257 observer, old256 disabled, visual/skin/controller/material/shader bindings valid; Jeongdam256 retained. This is asset integrity, not manual escort or GPU performance.";
   File.WriteAllText(Output+"/final-integrity.txt",result);return result;
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
    if(id=="wangso_w1"){var motion=binding.Renderers[0].GetComponentInParent<PrincipalNpcMotion257>();if(motion==null||motion.Animator==null||motion.Animator.runtimeAnimatorController==null)throw new Exception(id+" animation observer invalid");}
    lines.Add("PASS "+id+" actual session interaction, active new skinned renderer bindings and idle/walk controller. Teleport approach fixture, not manual navigation.");
   }
   if(!s.Progress.campaign.Facts.Contains("met:jeongdam")||!s.Progress.campaign.Completed.Contains("cargo_contract"))throw new Exception("Conversation facts/contract missing");
   lines.Add("PASS Jeongdam contact and Wangso contract committed in private diagnostic save; existing quest order retained.");ui.CloseMenu();ui.Gate.ReleaseImmediately();File.WriteAllText(Output+"/runtime-checks.txt",string.Join("\n",lines));return string.Join("\n",lines);
  }
 }
}
