using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string ModelFolder273="Assets/_Project/Art/Characters/Sinmok273";
  public static string Sinmok273(string command)
  {
   if(command=="apply")return ApplySinmok273();
   if(command=="check")return CheckAnimation273();
   if(command=="capture")return CaptureAnimation273();
   if(command=="combat")return CombatChecks263(true);
   if(command=="scene")return CaptureScene273();
   throw new ArgumentException(command);
  }
  static string ApplySinmok273()
  {
   var scene=FrontageScene249();if(scene.isDirty)throw new Exception("Candidate has unsaved changes");
   Directory.CreateDirectory("../Art/Characters/Sinmok272/rig/Recovery");string back="../Art/Characters/Sinmok272/rig/Recovery/Candidate.unity";if(!File.Exists(back))File.Copy(scene.path,back);
   string path=ModelFolder273+"/Sinmok273.fbx";var imp=(ModelImporter)AssetImporter.GetAtPath(path);
   imp.animationType=ModelImporterAnimationType.Generic;imp.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;imp.importAnimation=true;imp.materialImportMode=ModelImporterMaterialImportMode.None;imp.isReadable=true;imp.animationCompression=ModelImporterAnimationCompression.Off;
   var definitions=imp.defaultClipAnimations;foreach(var d in definitions){d.loopTime=d.name.EndsWith("Idle");d.lockRootPositionXZ=true;d.lockRootHeightY=true;d.lockRootRotation=true;}imp.clipAnimations=definitions;imp.SaveAndReimport();
   var normal=(TextureImporter)AssetImporter.GetAtPath(ModelFolder273+"/texture_urls_0_normal.png");normal.textureType=TextureImporterType.NormalMap;normal.maxTextureSize=4096;normal.SaveAndReimport();
   var color=(TextureImporter)AssetImporter.GetAtPath(ModelFolder273+"/texture_urls_0_base_color.png");color.maxTextureSize=4096;color.SaveAndReimport();
   string materialPath=ModelFolder273+"/OldGinkgo.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
   if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,materialPath);}
   mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder273+"/texture_urls_0_base_color.png"));mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder273+"/texture_urls_0_normal.png"));mat.EnableKeyword("_NORMALMAP");mat.SetFloat("_BumpScale",.8f);mat.SetFloat("_Smoothness",.12f);mat.SetFloat("_Metallic",0);mat.SetColor("_BaseColor",new Color(.72f,.72f,.69f,1));EditorUtility.SetDirty(mat);
   var actor=VillageSession().Actors.Single(a=>a.Id==WorldMacroPlaytestSession.SinmokId);var old=actor.transform.Find("GeneratedSinmok273");if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
   foreach(var r in actor.GetComponentsInChildren<Renderer>(true))r.enabled=false;
   var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),actor.transform);model.name="GeneratedSinmok273";model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
   foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()){r.sharedMaterial=mat;r.updateWhenOffscreen=false;var b=r.localBounds;b.Expand(8);r.localBounds=b;}
   var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")).ToArray();AnimationClip Clip(string n)=>clips.Single(c=>c.name.EndsWith("Sinmok_"+n));
   var rig=actor.GetComponent<SinmokRigAnimation>();rig.Animator=model.GetComponent<Animator>();rig.Animator.applyRootMotion=false;rig.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   rig.Idle=Clip("Idle");rig.Attacks=new[]{Clip("LeftSlam"),Clip("LeftSweep"),Clip("RootPulse"),Clip("BoughCast")};rig.SupportRelease=Clip("SupportRelease");rig.FacingVisual=model.transform;rig.TurningTrunk=model.GetComponentsInChildren<Transform>().Single(t=>t.name=="TrunkUpper");
   var ground=FinalSurface(scene);var low=model.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(WorldSkin273).Where(v=>v.y<actor.transform.position.y+.45f).ToArray();
   var offsets=low.Where((v,i)=>i%Mathf.Max(1,low.Length/32)==0).Select(v=>ground(v.x,v.z).point.y-v.y).OrderBy(v=>v).ToArray();
   if(offsets.Length>0){float offset=offsets[Mathf.Min(offsets.Length-1,offsets.Length/5)]-.08f;if(Mathf.Abs(offset)>2.5f)throw new Exception("Unexpected generated model grounding offset: "+offset);model.transform.localPosition+=Vector3.up*offset;File.WriteAllText("../Art/Characters/Animation273/grounding.txt","Generated visual local Y offset="+offset+"m; low-root terrain samples="+offsets.Length+". Gameplay actor/terrain unchanged.");}
   var originalAnimator=actor.GetComponent<Animator>();if(originalAnimator!=null)originalAnimator.enabled=false;
   EditorUtility.SetDirty(rig);AssetDatabase.SaveAssets();UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
   return "Generated Sinmok: 4 surface parts, 22 Blender bones, idle + four combat clips + support replant. Existing encounter, reward, save and collider unchanged.";
  }
 }
}
