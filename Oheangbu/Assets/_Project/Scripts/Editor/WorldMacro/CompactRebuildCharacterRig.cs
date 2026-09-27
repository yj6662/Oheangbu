using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string CaptureLoggerRig()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||AnimationMode.InAnimationMode())throw new Exception("Neutral edit mode required");
   string folder=Folder+"/Characters236/logger/native", output=Output+"/Characters/Rig236";
   var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Logger_Rigged.prefab"));
   var stage=new GameObject("Temporary_LoggerReview");root.transform.SetParent(stage.transform,false);
   stage.transform.position=new Vector3(0,5000,0);
   foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
   var cameraObject=new GameObject("ReviewCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
   camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.2f,.23f,.22f);camera.fieldOfView=36;
   var lightObject=new GameObject("ReviewLight");var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(35,-25,0);
   var rt=new RenderTexture(1280,960,24);var previous=RenderTexture.active;var image=new Texture2D(1280,960,TextureFormat.RGB24,false);
   var report=new List<string>();var skin=root.GetComponentInChildren<SkinnedMeshRenderer>();var snapshots=new List<Vector3>();
   try
   {
    AnimationMode.StartAnimationMode();
    foreach(var motion in new[]{"Walk","Run"})
    {
     var clip=AssetDatabase.LoadAllAssetsAtPath(folder+"/"+motion+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
     for(int i=0;i<3;i++)
     {
      AnimationMode.BeginSampling();AnimationMode.SampleAnimationClip(root,clip,clip.length*(i+.15f)/3);AnimationMode.EndSampling();
      snapshots.Add(root.transform.InverseTransformPoint(root.GetComponentsInChildren<Transform>().Single(t=>t.name=="LeftFoot").position));
      var baked=new Mesh();skin.BakeMesh(baked);var visible=new GameObject("BakedReviewSkin");visible.layer=31;visible.transform.SetParent(skin.transform,false);visible.AddComponent<MeshFilter>().sharedMesh=baked;var renderer=visible.AddComponent<MeshRenderer>();renderer.sharedMaterials=skin.sharedMaterials;skin.enabled=false;
      try
      {
       var bounds=renderer.bounds;camera.transform.position=bounds.center+new Vector3(1.1f,.2f,-3.5f);camera.transform.LookAt(bounds.center);
       camera.targetTexture=rt;camera.aspect=1280f/960;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,960),0,0);image.Apply();
       File.WriteAllBytes(output+"/Native_"+motion+i+".png",image.EncodeToPNG());
       report.Add(motion+" sample="+i+" bounds="+bounds.size+" foot="+snapshots.Last());
      }
      finally{skin.enabled=true;Object.DestroyImmediate(visible);Object.DestroyImmediate(baked);}
     }
    }
    report.Add("Isolated sampled native generic clips; scene replacement, locomotion controller, idle, combat and foot sliding not verified.");
    string result=string.Join("\n",report);File.WriteAllText(output+"/native_sampled_motion.txt",result);return result;
   }
   finally
   {
    AnimationMode.StopAnimationMode();RenderTexture.active=previous;camera.targetTexture=null;rt.Release();
    Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(lightObject);Object.DestroyImmediate(stage);
   }
  }

  static string ImportLoggerRig()
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit required");
   string folder=Folder+"/Characters236/logger/native";DevSceneKit.EnsureFolder(folder);
   string source=Output+"/Characters/Rig236/logger/rig/";
   var paths=new Dictionary<string,string>();
   foreach(var item in new[]{("Body","result_rigged_character_fbx_url.fbx"),("Walk","result_basic_animations_walking_fbx_url.fbx"),("Run","result_basic_animations_running_fbx_url.fbx")})
   {
    string path=folder+"/"+item.Item1+".fbx";File.Copy(source+item.Item2,path,true);AssetDatabase.ImportAsset(path);
    var importer=(ModelImporter)AssetImporter.GetAtPath(path);
    importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.animationType=ModelImporterAnimationType.Generic;
    importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.importAnimation=item.Item1!="Body";
    importer.optimizeGameObjects=false;importer.isReadable=true;importer.animationCompression=ModelImporterAnimationCompression.Off;
    importer.motionNodeName="Hips";importer.SaveAndReimport();paths[item.Item1]=path;
   }
   var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths["Body"]));
   try
   {
    root.name="Logger_Rig236_Review";var animator=root.GetComponent<Animator>();
    if(animator==null||animator.avatar==null||!animator.avatar.isValid)throw new Exception("Invalid generic avatar");
    animator.applyRootMotion=false;
    var mat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Characters234/logger/Surface.mat");
    var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    foreach(var skin in skins){skin.sharedMaterials=Enumerable.Repeat(mat,skin.sharedMaterials.Length).ToArray();skin.updateWhenOffscreen=true;}
    var lines=new List<string>{"PASS native generic avatar; original #234 geometry and texture reused; 5 Meshy rig credits", "Finger bones absent in Meshy result; no hand articulation claimed. Review prefab only; scene NPC unchanged."};
    foreach(var mode in new[]{"Walk","Run"})
    {
     var clip=AssetDatabase.LoadAllAssetsAtPath(paths[mode]).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
     if(clip.humanMotion||clip.length<=0)throw new Exception("Invalid "+mode+" clip");
     lines.Add("PASS "+mode+" native generic clip length="+clip.length+" rate="+clip.frameRate);
    }
    foreach(var skin in skins)
    {
     if(skin.bones.Any(b=>b==null)||skin.sharedMesh==null)throw new Exception("Missing skin binding");
     lines.Add("PASS skin "+skin.name+" bones="+skin.bones.Length+" vertices="+skin.sharedMesh.vertexCount);
    }
    PrefabUtility.SaveAsPrefabAsset(root,folder+"/Logger_Rigged.prefab");AssetDatabase.SaveAssets();
    string report=string.Join("\n",lines);File.WriteAllText(Output+"/Characters/Rig236/native_unity_import.txt",report);return report;
   }
   finally{Object.DestroyImmediate(root);}
  }
 }
}
