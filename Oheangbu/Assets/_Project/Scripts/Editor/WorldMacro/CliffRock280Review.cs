using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  public static string Rock280(string command){
   const string output="../Art/World/Compact/Rebuild/Ascent280";
   Directory.CreateDirectory(output);
   if(command=="prepare"){
    var importer=(TextureImporter)AssetImporter.GetAtPath(A278+"/Textures/Granite280.png");
    if(importer==null)throw new Exception("Import generated granite first");
    importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;
    importer.anisoLevel=8;importer.mipmapEnabled=true;importer.sRGBTexture=true;
    importer.maxTextureSize=2048;importer.npotScale=TextureImporterNPOTScale.None;
    importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
    return "Granite280: repeat, trilinear, mipmaps, anisotropy8, sRGB; source preserved";
   }
   if(command!="close")throw new Exception("Use prepare or close");
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=Scene278)throw new Exception("Open prototype in Edit mode");
   var source=UnityEngine.Object.FindFirstObjectByType<CliffAscentExplorer278>().View;
   var go=new GameObject("Rock280_CloseCapture");var camera=go.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.useOcclusionCulling=false;
   EditorUtility.CopySerialized(source.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var rt=new RenderTexture(1920,1080,24);var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;bool previousAsync=ShaderUtil.allowAsyncCompilation;
   try{
    ShaderUtil.allowAsyncCompilation=false;camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=60;
    for(int i=0;i<2;i++){
     var p=RouteAt278(i==0?175:65);camera.transform.SetPositionAndRotation(p+new Vector3(1.2f,1.7f,0),Quaternion.LookRotation(new Vector3(5,.3f,2.4f)));
     camera.Render();camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();File.WriteAllBytes(output+"/close-"+i+".png",texture.EncodeToPNG());
    }
   }finally{ShaderUtil.allowAsyncCompilation=previousAsync;camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(go);}
   return "Two near-wall 1080p Unity captures; original camera and scene unchanged";
  }
 }
}
