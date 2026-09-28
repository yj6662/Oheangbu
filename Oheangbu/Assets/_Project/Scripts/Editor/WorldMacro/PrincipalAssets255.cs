using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class PrincipalAssets255
 {
  public static string Import(string unused)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
   var report=new List<string>{"Meshy 7.1 Wangso / Jeongdam review assets. Existing scene actors unchanged.","Native walking review: arms retain raised/bent pose; not accepted for gameplay replacement or retargeting."};
   foreach(string id in new[]{"wangso","jeongdam"})
   {
    string folder="Assets/_Project/Art/Characters/Principal255/"+id;DevSceneKit.EnsureFolder(folder);
    string source="../Art/Characters/Principal255/"+id;
    foreach(var pair in new[]{("Body","result_rigged_character_fbx_url.fbx"),("Walk","result_basic_animations_walking_fbx_url.fbx"),("Run","result_basic_animations_running_fbx_url.fbx")})
    {
     string path=folder+"/"+pair.Item1+".fbx";File.Copy(source+"/rig/"+pair.Item2,path,true);AssetDatabase.ImportAsset(path);
     var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.None;
     importer.animationType=ModelImporterAnimationType.Generic;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
     importer.importAnimation=pair.Item1!="Body";importer.optimizeGameObjects=false;importer.isReadable=true;
     importer.animationCompression=ModelImporterAnimationCompression.Off;importer.motionNodeName="Hips";importer.SaveAndReimport();
    }
    foreach(var kind in new[]{"base_color","normal"})
    {
     string path=folder+"/"+kind+".png";File.Copy(source+"/refine/texture_urls_0_"+kind+".png",path,true);AssetDatabase.ImportAsset(path);
     var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=kind=="normal"?TextureImporterType.NormalMap:TextureImporterType.Default;
     importer.maxTextureSize=2048;importer.SaveAndReimport();
    }
    string matPath=folder+"/Surface.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,matPath);}
    material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/base_color.png"));
    material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/normal.png"));material.EnableKeyword("_NORMALMAP");material.SetFloat("_Smoothness",.2f);material.SetFloat("_BumpScale",.6f);EditorUtility.SetDirty(material);
    var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/Body.fbx"));
    try
    {
     root.name=id+"_ReviewOnly";var animator=root.GetComponent<Animator>();
     if(animator==null||animator.avatar==null||!animator.avatar.isValid)throw new Exception(id+" avatar invalid");
     animator.applyRootMotion=false;
     foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
     {
      if(skin.sharedMesh==null||skin.bones.Any(b=>b==null))throw new Exception(id+" skin binding missing");
      skin.sharedMaterials=Enumerable.Repeat(material,skin.sharedMaterials.Length).ToArray();
      report.Add(id+" skin="+skin.name+" vertices="+skin.sharedMesh.vertexCount+" bones="+skin.bones.Length);
     }
     foreach(string mode in new[]{"Walk","Run"})
     {var clip=AssetDatabase.LoadAllAssetsAtPath(folder+"/"+mode+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));report.Add(id+" "+mode+"="+clip.length+"s, generic="+!clip.humanMotion);}
     PrefabUtility.SaveAsPrefabAsset(root,folder+"/ReviewOnly.prefab");
    }
    finally{Object.DestroyImmediate(root);}
   }
   AssetDatabase.SaveAssets();string result=string.Join("\n",report);File.WriteAllText("../Art/Characters/Principal255/unity-import.txt",result);return result;
  }
 }
}
