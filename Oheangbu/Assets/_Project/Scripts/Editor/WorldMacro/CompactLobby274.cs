using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.UI;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string LobbyImage274="Assets/_Project/Art/UI/Lobby274/cheongrim-inn.png";
  const string LobbyOutput274="../Art/UI/Lobby274";
  public static string Lobby274(string command)
  {
   if(command=="apply")
   {
    var candidate=FrontageScene249();if(candidate.isDirty)throw new Exception("Unsaved candidate edits");
    Directory.CreateDirectory(LobbyOutput274+"/Recovery");
    foreach(var path in new[]{candidate.path,CompactLoadingStartup270.Lobby}){string backup=LobbyOutput274+"/Recovery/"+Path.GetFileName(path);if(!File.Exists(backup))File.Copy(path,backup);}
    AssetDatabase.ImportAsset(LobbyImage274);var importer=(TextureImporter)AssetImporter.GetAtPath(LobbyImage274);
    importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=false;importer.streamingMipmaps=false;importer.isReadable=false;importer.npotScale=TextureImporterNPOTScale.None;importer.maxTextureSize=2048;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
    var image=AssetDatabase.LoadAssetAtPath<Texture2D>(LobbyImage274);
    var lobby=EditorSceneManager.OpenScene(CompactLoadingStartup270.Lobby,OpenSceneMode.Additive);
    try{foreach(var scene in new[]{lobby,candidate}){var ui=scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();ui.LobbyIllustration=image;EditorUtility.SetDirty(ui);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}}
    finally{EditorSceneManager.CloseScene(lobby,true);SceneManager.SetActiveScene(candidate);}
    return "Generated illustration bound to lobby and candidate return path; original compact theme/title preserved.";
   }
   if(command=="check")return CheckLobby274();
   throw new ArgumentException(command);
  }
  static string CheckLobby274()
  {
   var candidate=FrontageScene249();var records=new List<string>();void Check(bool ok,string msg){records.Add((ok?"PASS ":"FAIL ")+msg);}
   var lobby=EditorSceneManager.OpenScene(CompactLoadingStartup270.Lobby,OpenSceneMode.Additive);
   var binding=lobby.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   var candidateUi=candidate.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   Check(binding.LobbyIllustration!=null&&binding.LobbyIllustration==candidateUi.LobbyIllustration,"lobby and direct-world return share the new illustration");
   Check(binding.Theme.TitleBackdrop!=binding.LobbyIllustration,"shared original title art preserved");
   Check(binding.PlaySceneName==candidate.name&&binding.Content==candidateUi.Content&&binding.LoadingProfile==candidateUi.LoadingProfile,"play/save/loading bindings unchanged");
   var preview=EditorSceneManager.NewPreviewScene();var cursor=Cursor.lockState;bool cursorVisible=Cursor.visible;
   try
   {
    foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(1280,720)})
    {
     var host=new GameObject("Lobby visual fixture");SceneManager.MoveGameObjectToScene(host,preview);var ui=host.AddComponent<PlaytestUiRoot>();ui.enabled=false;
     ui.Theme=binding.Theme;ui.Content=binding.Content;ui.LobbyIllustration=binding.LobbyIllustration;
     var settings=host.AddComponent<UserSettingsService>();settings.enabled=false;
     typeof(PlaytestUiRoot).GetProperty("Settings").SetValue(ui,settings);
     var go=new GameObject("Canvas",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(host.transform,false);var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
     var scaler=go.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
     var cameraGo=new GameObject("Offscreen UI camera");SceneManager.MoveGameObjectToScene(cameraGo,preview);var camera=cameraGo.AddComponent<Camera>();camera.scene=preview;camera.enabled=false;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=540;canvas.worldCamera=camera;canvas.planeDistance=1;
     var rect=(RectTransform)go.transform;var baseLayer=PlaytestUiView.Stretch("Background",rect);var modal=PlaytestUiView.Stretch("Menu",rect);
     void Set(string name,object value)=>typeof(PlaytestUiRoot).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(ui,value);
     Set("canvasRect",rect);Set("baseLayer",baseLayer);Set("modalLayer",modal);
     var rt=new RenderTexture(size.x,size.y,24);var tex=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);var previous=RenderTexture.active;
     try
     {
      camera.targetTexture=rt;camera.aspect=size.x/(float)size.y;
      // Screen-size scaling in an offscreen edit fixture needs the target dimensions explicitly.
      canvas.scaleFactor=Mathf.Sqrt(size.x/1920f*size.y/1080f);
      typeof(PlaytestUiRoot).GetMethod("BuildTitle",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,new object[]{false});
      Canvas.ForceUpdateCanvases();typeof(PlaytestUiRoot).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,null);Canvas.ForceUpdateCanvases();
      var image=baseLayer.Find("LobbyIllustration").GetComponent<RawImage>();var fit=image.GetComponent<AspectRatioFitter>();
      Check(image.texture==binding.LobbyIllustration&&!image.raycastTarget&&fit.aspectMode==AspectRatioFitter.AspectMode.EnvelopeParent,size+" generated full-cover background does not intercept input");
      Check(Mathf.Abs(image.rectTransform.rect.width/image.rectTransform.rect.height-fit.aspectRatio)<.002f,size+" artwork aspect ratio preserved");
      var panel=baseLayer.Find("TitleCard") as RectTransform;var corners=new Vector3[4];panel.GetWorldCorners(corners);Check(corners.All(v=>{var p=camera.WorldToViewportPoint(v);return p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1;}),size+" complete panel fits viewport");
      Check(panel.GetComponentsInChildren<Button>().Length==5,size+" five existing menu actions retained");
      Check(panel.Find("Paper")==null&&panel.Find("Fiber")==null,size+" menu has no backing panel");
      Check(baseLayer.Find("MenuMist").GetComponent<CanvasRenderer>()!=null,size+" continuous legibility wash has a renderer");
      var buttons=panel.GetComponentsInChildren<LobbyMenuButton274>();
      Check(buttons.Length==5&&buttons.All(b=>b.transform.Find("FocusFrame").GetComponent<CanvasGroup>().alpha==0),size+" frames invisible at rest");
      void Capture(string suffix){camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,size.x,size.y),0,0);tex.Apply();File.WriteAllBytes(LobbyOutput274+"/lobby-"+size.x+"x"+size.y+suffix+".png",tex.EncodeToPNG());}
      Capture("");
      var option=buttons.Single(b=>b.name=="Options");var frame=option.transform.Find("FocusFrame").GetComponent<CanvasGroup>();
      option.OnPointerEnter(null);Check(frame.alpha==1&&buttons.Where(b=>b!=option).All(b=>b.transform.Find("FocusFrame").GetComponent<CanvasGroup>().alpha==0),size+" hover reveals only the pointed item frame");
      Capture("-hover");option.OnPointerExit(null);Check(frame.alpha==0,size+" pointer exit hides frame");
      option.OnSelect(null);Check(frame.alpha==1,size+" keyboard selection reveals frame");
      option.OnDeselect(null);Check(frame.alpha==0,size+" keyboard deselection hides frame");
      option.interactable=false;option.OnPointerEnter(null);Check(frame.alpha==0,size+" disabled item does not highlight");
     }finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(host);Object.DestroyImmediate(cameraGo);}
    }
    Check(Cursor.lockState==cursor&&Cursor.visible==cursorVisible,"offscreen review leaves cursor state unchanged");
   }finally{EditorSceneManager.ClosePreviewScene(preview);EditorSceneManager.CloseScene(lobby,true);SceneManager.SetActiveScene(candidate);}
   File.WriteAllLines(LobbyOutput274+"/checks.txt",records);return string.Join("\n",records);
  }
 }
}
