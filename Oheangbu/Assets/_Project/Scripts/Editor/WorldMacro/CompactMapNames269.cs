using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  public static string MapNames269(string command)
  {
   if(command!="check")throw new ArgumentException(command);
   var scene=FrontageScene249();
   var source=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlaytestUiRoot>(true)).Single();
   const string output=Output+"/MapNames269";Directory.CreateDirectory(output);
   var preview=EditorSceneManager.NewPreviewScene();
   GameObject New(string name,params Type[] types){var g=new GameObject(name,types);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g,preview);return g;}
   const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
   var oldInstance=PlaytestUiRoot.Instance;
   var instance=typeof(PlaytestUiRoot).GetProperty("Instance");
   var cam=New("Map preview camera",typeof(Camera)).GetComponent<Camera>();cam.enabled=false;cam.scene=preview;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.14f,.14f,.13f);cam.cullingMask=1<<31;
   var rt=new RenderTexture(1920,1080,24);cam.targetTexture=rt;cam.aspect=16f/9;
   var panel=New("Map preview canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=cam;canvas.planeDistance=1;
   var sessionHost=New("Isolated map state");sessionHost.SetActive(false);var session=sessionHost.AddComponent<WorldMacroPlaytestSession>();
   var content=Object.Instantiate(source.Content);content.StartFeet=new Vector3(3100,100,2240);session.Content=content;
   var progress=WorldMacroProgress.CreateNew("269-preview",content.StartFeet,0);
   progress.ui.discoveredMarkers=source.MapData.Markers.Select(m=>m.Id).ToList();
   typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session,progress);
   var presenter=panel.AddComponent<WorldMapPresenter>();presenter.enabled=false;
   object Get(string name)=>typeof(WorldMapPresenter).GetField(name,flags).GetValue(presenter);
   void Set(string name,object value)=>typeof(WorldMapPresenter).GetField(name,flags).SetValue(presenter,value);
   void Call(string name,params object[] args)=>typeof(WorldMapPresenter).GetMethod(name,flags).Invoke(presenter,args);
   var checks=new List<string>();
   void Check(bool pass,string label){checks.Add((pass?"PASS ":"FAIL ")+label);if(!pass)throw new Exception(label);}
   void Capture(string name){Canvas.ForceUpdateCanvases();cam.Render();var previous=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(output+"/"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);RenderTexture.active=previous;}
   try
   {
    instance.SetValue(null,source);
    var theme=source.Theme;
    presenter.Initialize((RectTransform)panel.transform,session,source.WorldSheet,new WorldMapUiDependencies{BakedData=source.MapData,Icons=theme.Icons,Font=theme.Font,PaperTexture=theme.PaperTexture,Ink=theme.Ink,Paper=theme.Paper,Muted=theme.Muted,Seal=theme.Seal,ReducedMotion=true});
    foreach(var t in panel.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
    Canvas.ForceUpdateCanvases();Call("RefreshFullLayout",true);presenter.SetExpanded(true);presenter.ShowWholeWorld();Canvas.ForceUpdateCanvases();
    var labels=(List<Text>)Get("regionLabels");
    Check(labels.Count==5&&labels.All(t=>t.enabled&&t.gameObject.activeInHierarchy&&t.alignment==TextAnchor.MiddleCenter&&t.rectTransform.pivot==new Vector2(.5f,.5f)),"five centered realm labels enabled in icon map");
    Capture("whole");
    var realm=source.MapData.Locations.Entries.Single(e=>e.Id=="realm_cheongrim");var centre=realm.Polygon.Aggregate(Vector2.zero,(a,b)=>a+b)/realm.Polygon.Length;
    var mapSize=source.MapData.BoundsMax-source.MapData.BoundsMin;var uv=(centre-source.MapData.BoundsMin);uv=new Vector2(uv.x/mapSize.x,uv.y/mapSize.y);
    var rect=(RectTransform)Get("foldMap");var screen=RectTransformUtility.WorldToScreenPoint(cam,rect.TransformPoint(new Vector3((uv.x-.5f)*rect.rect.width,(uv.y-.5f)*rect.rect.height,0)));
    float initialSize=labels.Single(t=>t.text=="청림").rectTransform.rect.height;
    for(int i=0;i<10;i++)Call("Zoom",1f,screen,cam);
    Check(labels.Single(t=>t.text=="청림").fontSize==24&&labels.Single(t=>t.text=="청림").rectTransform.rect.height==initialSize,"zoom preserves realm type size");
    Check(labels.Single(t=>t.text=="청림").gameObject.activeSelf,"realm name stays visible at close zoom when its centre is in view");Capture("zoom");
    presenter.FocusCurrent();Call("ApplyUvAndMarkers",content.StartFeet);Canvas.ForceUpdateCanvases();
    var markers=(RectTransform)Get("fullMarkers");var spec=source.MapData.Markers.First(m=>m.Label.Contains("금표"));
    var icon=(RectTransform)markers.Find("Marker_"+spec.Id);Check(icon!=null&&icon.gameObject.activeInHierarchy,"discovered inn icon visible: "+spec.Id+" at "+spec.WorldXZ+" icon="+(icon==null?"missing":icon.gameObject.activeSelf.ToString())+" uv="+Get("fullUv")+" known="+session.Progress.ui.discoveredMarkers.Contains(spec.Id)+" data="+((WorldMapBakedDataSO)Get("data")).name);
    screen=RectTransformUtility.WorldToScreenPoint(cam,icon.position);
    var input=rect.Find("MapInput").gameObject;var evt=new PointerEventData(null){position=screen,pointerCurrentRaycast=new RaycastResult{module=panel.GetComponent<GraphicRaycaster>()}};
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);
    var hover=(Text)Get("mapHover");Check(hover.gameObject.activeSelf&&hover.text==spec.Label&&!hover.raycastTarget,"pointer hover names actual visible location without raycast interception");
    RectTransformUtility.ScreenPointToLocalPointInRectangle(presenter.FullRoot,screen,cam,out var local);
    Check(hover.rectTransform.anchoredPosition.x>local.x&&hover.rectTransform.anchoredPosition.y>local.y,"name sits upper right of pointer");Capture("hover");
    File.WriteAllText(output+"/hover-layout.txt","enabled="+hover.enabled+" active="+hover.gameObject.activeInHierarchy+" color="+hover.color+" rect="+hover.rectTransform.rect+" position="+hover.rectTransform.anchoredPosition+" screen="+RectTransformUtility.WorldToScreenPoint(cam,hover.transform.position)+" vertices="+hover.cachedTextGenerator.vertexCount+" cull="+hover.canvasRenderer.cull+" alpha="+hover.canvasRenderer.GetAlpha());
    var hoverScreen=RectTransformUtility.WorldToScreenPoint(cam,hover.transform.position);
    Check(Vector2.Distance(hoverScreen,screen+new Vector2(18,20))<2,"rendered tooltip origin is actually upper right in screen coordinates");
    var bounds=presenter.FullRoot.rect;
    Check(hover.rectTransform.anchoredPosition.x+hover.rectTransform.rect.width<=bounds.xMax-12&&hover.rectTransform.anchoredPosition.y+32<=bounds.yMax-12,"tooltip fits viewport margins");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerDownHandler);Check(!hover.gameObject.activeSelf,"pointer down hides name for dragging");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerUpHandler);Check(hover.gameObject.activeSelf,"pointer up restores hover");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerExitHandler);Check(!hover.gameObject.activeSelf,"pointer exit hides name");
    progress.ui.discoveredMarkers.Remove(spec.Id);Call("ApplyUvAndMarkers",content.StartFeet);ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);
    Check(!icon.gameObject.activeSelf,"undiscovered icon remains hidden");Check(!hover.gameObject.activeSelf||hover.text!=spec.Label,"hover does not leak undiscovered name");
    progress.ui.discoveredMarkers.Add(spec.Id);Call("ApplyUvAndMarkers",content.StartFeet);ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);presenter.SetExpanded(false);Check(!hover.gameObject.activeSelf,"closing map clears hover immediately");
    File.WriteAllLines(output+"/checks.txt",checks);return string.Join("\n",checks)+"\nIsolated edit-mode fixture and offscreen renders only; no gameplay input or save writes.";
   }
   finally
   {
    instance.SetValue(null,oldInstance);
    // Runtime presenter normally disposes at end-of-frame; the edit-mode harness owns its transient objects.
    foreach(var name in new[]{"paperMaterial","miniDiscoveryMaterial","miniPaperMaterial","fogTexture","caveMapMaterial","ownedFont"}){var field=typeof(WorldMapPresenter).GetField(name,flags);if(field?.GetValue(presenter) is Object obj)Object.DestroyImmediate(obj);field?.SetValue(presenter,null);}
    foreach(var name in new[]{"paperInk","macroInk"}){var ink=Get(name);if(ink!=null)Object.DestroyImmediate((Object)ink.GetType().GetProperty("Texture").GetValue(ink));Set(name,null);}
    Set("<MiniRoot>k__BackingField",null);Set("<FullRoot>k__BackingField",null);
    Object.DestroyImmediate(panel);Object.DestroyImmediate(content);cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);EditorSceneManager.ClosePreviewScene(preview);
   }
  }
 }
}
