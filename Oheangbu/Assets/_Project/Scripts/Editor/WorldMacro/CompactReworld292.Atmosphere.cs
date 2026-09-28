using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string Atmosphere292()
  {
   var s=Session292();var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
   var po=new SerializedObject(pipeline);var entries=po.FindProperty("m_RendererDataList");
   var camera=s.Walker.ViewCamera.GetUniversalAdditionalCameraData();int old=new SerializedObject(camera).FindProperty("m_RendererIndex").intValue;if(old<0)old=po.FindProperty("m_DefaultRendererIndex").intValue;
   var original=entries.GetArrayElementAtIndex(old).objectReferenceValue;string path=A292+"/Renderer292.asset";
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
   if(renderer==null){if(!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),path))throw new Exception("Cannot isolate renderer");renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);}
   var fog=Asset292("Materials/Fog292.mat",()=>new Material(Shader.Find("Oheangbu/Compact/RebuildFog")));fog.SetColor("_FogColor",new Color(.76f,.77f,.72f));fog.SetVector("_FogRange",new Vector4(450,2800,0,0));EditorUtility.SetDirty(fog);
   foreach(var feature in renderer.rendererFeatures)
   {if(feature is FullScreenPassRendererFeature pass&&feature.name=="CompactMist238"){pass.passMaterial=fog;EditorUtility.SetDirty(pass);}else if(feature.name.Contains("InkWorldPost"))feature.SetActive(false);}
   var pipelines=Enumerable.Range(0,QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).OfType<UniversalRenderPipelineAsset>().Concat(new[]{pipeline}).Distinct().ToArray();
   int index=-1;for(int i=0;i<entries.arraySize;i++)if(entries.GetArrayElementAtIndex(i).objectReferenceValue==renderer)index=i;
   if(index<0)index=pipelines.Max(p=>new SerializedObject(p).FindProperty("m_RendererDataList").arraySize);
   foreach(var pipe in pipelines)
   {
    var so=new SerializedObject(pipe);var list=so.FindProperty("m_RendererDataList");if(index<list.arraySize&&list.GetArrayElementAtIndex(index).objectReferenceValue!=renderer)throw new Exception("Renderer index collision");
    string backup=O292+"/"+pipe.name+"_pipeline_before.asset";if(!File.Exists(backup))File.Copy(AssetDatabase.GetAssetPath(pipe),backup);
    while(list.arraySize<=index){int n=list.arraySize;list.InsertArrayElementAtIndex(n);list.GetArrayElementAtIndex(n).objectReferenceValue=n==index?renderer:list.GetArrayElementAtIndex(0).objectReferenceValue;}
    so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipe);
   }
   foreach(var c in s.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)))c.GetUniversalAdditionalCameraData().SetRenderer(index);
   renderer.SetDirty();EditorUtility.SetDirty(renderer);Save292();return "Private 292 camera renderer index "+index+". Existing entries/defaults retained.";
  }
 }
}
