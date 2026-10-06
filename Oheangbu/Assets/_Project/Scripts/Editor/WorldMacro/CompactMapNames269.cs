using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using TMPro;
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
    // #304: realm labels are TMP MapRegion44 (centre alignment, pivot .5). D308-25 (map 5): the name of the pointed mark is that
    // mark's own label on the sheet (MapLabels/Label_<id>, with a plate when the map has a notation bundle), not a tooltip
    var labels=(List<TMP_Text>)Get("regionLabels");
    Check(labels.Count==5&&labels.All(t=>t.enabled&&t.gameObject.activeInHierarchy&&t.alignment==TextAlignmentOptions.Center&&t.rectTransform.pivot==new Vector2(.5f,.5f)),"five centered realm labels enabled in icon map");
    Capture("whole");
    // #304 QA (map.png): the printed map with every cell discovered (the fog hides only undiscovered cells) + paper labels
    // inside the printed window and clear of the vertical title slip. Soft lines: they report, they do not stop the fixture.
    RevealedCapture304(presenter,source.MapData,"whole_revealed",Capture,checks);
    PaperLabels304(presenter,"whole",checks);
    var realm=source.MapData.Locations.Entries.Single(e=>e.Id=="realm_cheongrim");var centre=realm.Polygon.Aggregate(Vector2.zero,(a,b)=>a+b)/realm.Polygon.Length;
    var mapSize=source.MapData.BoundsMax-source.MapData.BoundsMin;var uv=(centre-source.MapData.BoundsMin);uv=new Vector2(uv.x/mapSize.x,uv.y/mapSize.y);
    var rect=(RectTransform)Get("foldMap");var screen=RectTransformUtility.WorldToScreenPoint(cam,rect.TransformPoint(new Vector3((uv.x-.5f)*rect.rect.width,(uv.y-.5f)*rect.rect.height,0)));
    float initialSize=labels.Single(t=>t.text=="청림").rectTransform.rect.height,initialFont=labels.Single(t=>t.text=="청림").fontSize;
    // D308-25: the first notch in from the whole-world view goes back to the wide window (another view under the same screen point),
    // so the realm centre's screen point is taken again before every notch
    Vector2 CentreScreen(){var view=(Rect)Get("fullUv");return RectTransformUtility.WorldToScreenPoint(cam,rect.TransformPoint(new Vector3(((uv.x-view.x)/view.width-.5f)*rect.rect.width,((uv.y-view.y)/view.height-.5f)*rect.rect.height,0)));}
    for(int i=0;i<10;i++)Call("Zoom",1f,CentreScreen(),cam);
    Check(Mathf.Approximately(labels.Single(t=>t.text=="청림").fontSize,initialFont)&&labels.Single(t=>t.text=="청림").rectTransform.rect.height==initialSize,"zoom preserves realm type size");
    Check(labels.Single(t=>t.text=="청림").gameObject.activeSelf,"realm name stays visible at close zoom when its centre is in view");Capture("zoom");
    presenter.FocusCurrent();Call("ApplyUvAndMarkers",content.StartFeet);Canvas.ForceUpdateCanvases();
    PaperLabels304(presenter,"current",checks);RevealedCapture304(presenter,source.MapData,"current_revealed",Capture,checks);
    var markers=(RectTransform)Get("fullMarkers");var spec=source.MapData.Markers.First(m=>m.Label.Contains("금표"));
    var icon=(RectTransform)markers.Find("Marker_"+spec.Id);Check(icon!=null&&icon.gameObject.activeInHierarchy,"discovered inn icon visible: "+spec.Id+" at "+spec.WorldXZ+" icon="+(icon==null?"missing":icon.gameObject.activeSelf.ToString())+" uv="+Get("fullUv")+" known="+session.Progress.ui.discoveredMarkers.Contains(spec.Id)+" data="+((WorldMapBakedDataSO)Get("data")).name);
    screen=RectTransformUtility.WorldToScreenPoint(cam,icon.position);
    var input=rect.Find("MapInput").gameObject;var evt=new PointerEventData(null){position=screen,pointerCurrentRaycast=new RaycastResult{module=panel.GetComponent<GraphicRaycaster>()}};
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);
    var paperLabels=(RectTransform)Get("fullLabels");var hover=paperLabels.Find("Label_"+spec.Id).GetComponent<TMP_Text>();var plate=paperLabels.Find("Plate_"+spec.Id) as RectTransform;
    int Tags()=>paperLabels.GetComponentsInChildren<TMP_Text>(false).Count(t=>t.name.StartsWith("Label_",StringComparison.Ordinal)&&!t.name.StartsWith("Label_Region_",StringComparison.Ordinal)&&t.alpha>0f);
    bool Shown()=>hover.gameObject.activeSelf&&hover.alpha>0f;
    int cut=spec.Label.LastIndexOf(" · ",StringComparison.Ordinal);string shortName=cut>=0&&cut+3<spec.Label.Length?spec.Label.Substring(cut+3):spec.Label;   // #304 ShortLabel: the place part only
    Check(Shown()&&hover.text==shortName&&Tags()==1&&!hover.raycastTarget,"pointer on a mark shows that mark's own name tag on the sheet: one tag, the short name, no raycast interception (tags="+Tags()+" text="+hover.text+")");
    Canvas.ForceUpdateCanvases();
    var tagBox=HarnessUiRules304.RectIn(rect,plate!=null&&plate.gameObject.activeSelf?plate:hover.rectTransform);Vector2 iconAt=rect.InverseTransformPoint(icon.position);
    float tagGap=Mathf.Max(0f,Mathf.Max(Mathf.Max(tagBox.xMin-iconAt.x,iconAt.x-tagBox.xMax),Mathf.Max(tagBox.yMin-iconAt.y,iconAt.y-tagBox.yMax)));
    Check(!tagBox.Contains(iconAt)&&tagGap<=icon.rect.width*.5f*icon.localScale.x+40f,"name tag stands beside its icon (one of the four sides), not on it and not away from it: gap from the icon centre="+tagGap.ToString("0.0")+"px box="+tagBox);Capture("hover");
    File.WriteAllText(output+"/hover-layout.txt","enabled="+hover.enabled+" active="+hover.gameObject.activeInHierarchy+" color="+hover.color+" box="+tagBox+" icon="+iconAt+" plate="+(plate!=null&&plate.gameObject.activeSelf)+" characters="+hover.textInfo.characterCount+" cull="+hover.canvasRenderer.cull+" alpha="+hover.canvasRenderer.GetAlpha());
    var printed=rect.rect;var slip=HarnessUiRules304.Member(presenter,"titleSlip304") as RectTransform;
    Check(tagBox.xMin>=printed.xMin-.5f&&tagBox.xMax<=printed.xMax+.5f&&tagBox.yMin>=printed.yMin-.5f&&tagBox.yMax<=printed.yMax+.5f&&(slip==null||!slip.gameObject.activeInHierarchy||!tagBox.Overlaps(HarnessUiRules304.RectIn(rect,slip))),"name tag whole inside the printed window and clear of the title slip");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerDownHandler);Check(Tags()==0,"pointer down hides the name tag for dragging");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerUpHandler);Check(Shown()&&Tags()==1,"pointer up restores the name tag");
    ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerExitHandler);Check(Tags()==0,"pointer exit hides the name tag");
    progress.ui.discoveredMarkers.Remove(spec.Id);Call("ApplyUvAndMarkers",content.StartFeet);ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);
    Check(!icon.gameObject.activeSelf,"undiscovered icon remains hidden");Check(!Shown()&&paperLabels.GetComponentsInChildren<TMP_Text>(false).All(t=>t.name.StartsWith("Label_Region_",StringComparison.Ordinal)||t.alpha<=0f||t.text!=shortName),"the place of an undiscovered mark shows no name tag (no leak)");
    progress.ui.discoveredMarkers.Add(spec.Id);Call("ApplyUvAndMarkers",content.StartFeet);ExecuteEvents.Execute(input,evt,ExecuteEvents.pointerMoveHandler);Check(Shown(),"the name tag is back once the mark is known again");presenter.SetExpanded(false);Check(Tags()==0,"closing map clears the name tag immediately");
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
  /// <summary>#304 QA: swaps the presenter's discovery grid (private "discovery", by name) for an all-discovered grid, captures,
  /// and puts the original grid back. The fixture's progress has no discovered cells, so "whole.png" is all fog by design.</summary>
  static void RevealedCapture304(WorldMapPresenter presenter,WorldMapBakedDataSO data,string name,Action<string> capture,List<string> checks)
  {
   const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
   var field=typeof(WorldMapPresenter).GetField("discovery",flags);var refresh=typeof(WorldMapPresenter).GetMethod("RefreshFog",flags,null,Type.EmptyTypes,null);
   if(field==null||refresh==null||!(field.GetValue(presenter) is WorldMapDiscoveryGrid grid)){checks.Add("INFO "+name+" skipped: WorldMapPresenter.discovery / RefreshFog not found by name");return;}
   var all=new WorldMapDiscoveryGrid(data.BoundsMin,data.BoundsMax,data.Outline,Enumerable.Repeat((byte)255,grid.ByteCount).ToArray());
   try{field.SetValue(presenter,all);refresh.Invoke(presenter,null);Canvas.ForceUpdateCanvases();capture(name);checks.Add("INFO "+name+".png = every cell discovered (illustrated="+data.HasIllustration+", paintedRelief="+data.PaintedRelief+")");}
   finally{field.SetValue(presenter,grid);refresh.Invoke(presenter,null);}
  }
  /// <summary>#304 QA: every live label on the paper (region names, place names, pin) inside the printed window (foldMap) and not
  /// under the vertical title slip (titleSlip304); both by name. FAIL lines are findings (the fixture keeps running).</summary>
  static void PaperLabels304(WorldMapPresenter presenter,string view,List<string> checks)
  {
   var window=HarnessUiRules304.Member(presenter,"foldMap") as RectTransform;var labels=HarnessUiRules304.Member(presenter,"fullLabels") as RectTransform;var slip=HarnessUiRules304.Member(presenter,"titleSlip304") as RectTransform;
   if(window==null||labels==null){checks.Add("INFO paper labels ("+view+") skipped: foldMap / fullLabels not found by name");return;}
   Canvas.ForceUpdateCanvases();
   var paper=window.rect;var title=slip!=null&&slip.gameObject.activeInHierarchy?HarnessUiRules304.RectIn(window,slip):Rect.zero;var corners=new Vector3[4];int n=0,bad=0;
   foreach(var t in labels.GetComponentsInChildren<TMP_Text>(false))
   {
    if(!HarnessUiRules304.IsLive(t)||!HarnessUiRules304.TextWorldCorners(t,corners))continue;n++;
    float x0=float.MaxValue,y0=float.MaxValue,x1=float.MinValue,y1=float.MinValue;
    foreach(var c in corners){var q=window.InverseTransformPoint(c);x0=Mathf.Min(x0,q.x);y0=Mathf.Min(y0,q.y);x1=Mathf.Max(x1,q.x);y1=Mathf.Max(y1,q.y);}
    var r=Rect.MinMaxRect(x0,y0,x1,y1);bool outside=r.xMin<paper.xMin-2||r.xMax>paper.xMax+2||r.yMin<paper.yMin-2||r.yMax>paper.yMax+2;bool under=title.width>0&&r.Overlaps(title);
    if(outside||under){bad++;checks.Add("FAIL paper label ("+view+") \""+t.text+"\""+(outside?" leaves the printed window":"")+(under?" lies under the title slip":"")+" rect="+r+" window="+paper+(title.width>0?" slip="+title:""));}
   }
   checks.Add((bad==0?"PASS ":"FAIL ")+"paper labels ("+view+") inside the printed window and clear of the title slip: "+(n-bad)+"/"+n+(slip==null?" (title slip not found by name)":""));
  }
 }
}
