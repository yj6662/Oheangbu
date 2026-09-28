using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
namespace Oheangbu.App.World.UI
{
 [DisallowMultipleComponent]
 public sealed class WorldLocationArrival:MonoBehaviour
 {
  public WorldLocationCatalog Catalog;public WorldMacroPlaytestSession Session;public PlaytestUiThemeSO Theme;
  WorldLocationTracker tracker;CanvasGroup group;Text title,detail;RectTransform label;float age=10,nextSave;string displayed;
  readonly Queue<WorldLocationCatalog.Entry> visits=new Queue<WorldLocationCatalog.Entry>();readonly HashSet<string> queued=new HashSet<string>();
  public static Text CreateName(Transform parent,Font font)
  {
   var text=PlaytestUiView.Text(parent,"RegionName","",font,42,new Color(.95f,.925f,.84f),0,0,1000,76,TextAnchor.MiddleCenter);
   text.supportRichText=false;text.resizeTextForBestFit=true;text.resizeTextMinSize=24;text.resizeTextMaxSize=42;
   text.horizontalOverflow=HorizontalWrapMode.Overflow;text.raycastTarget=false;
   var r=text.rectTransform;r.anchorMin=new Vector2(.15f,1);r.anchorMax=new Vector2(.85f,1);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(0,76);r.anchoredPosition=new Vector2(0,-64);
   var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.015f,.02f,.015f,.8f);shadow.effectDistance=new Vector2(1.5f,-2);shadow.useGraphicAlpha=true;
   return text;
  }
  public static Text CreateDetail(Text realm,Font font)
  {
   var text=PlaytestUiView.Text(realm.transform,"LocalName","",font,25,new Color(.91f,.89f,.83f),0,69,1000,44,TextAnchor.MiddleCenter);
   var r=text.rectTransform;r.anchorMin=new Vector2(0,1);r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(.5f,1);r.sizeDelta=new Vector2(0,44);r.anchoredPosition=new Vector2(0,-69);
   text.supportRichText=false;text.resizeTextForBestFit=true;text.resizeTextMinSize=19;text.resizeTextMaxSize=25;
   var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.015f,.02f,.015f,.8f);shadow.effectDistance=new Vector2(1,-1.5f);
   return text;
  }
  public static void SetNames(WorldLocationCatalog catalog,WorldLocationCatalog.Entry place,Text realm,Text local)
  {catalog.ArrivalNames(place,out var above,out var below);realm.text=above;local.text=below;}
  void Start()
  {
   if(Catalog==null||Session==null||Theme==null||Theme.Font==null){enabled=false;return;}
   tracker=new WorldLocationTracker(Catalog);
   var go=new GameObject("LocationNameCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(CanvasGroup));go.transform.SetParent(transform,false);
   var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=55;
   var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
   group=go.GetComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;group.alpha=0;
   title=CreateName(go.transform,Theme.Font);detail=CreateDetail(title,Theme.Font);label=title.rectTransform;
  }
  void Update()
  {
   if(tracker==null||Session.Progress==null||Session.Walker==null)return;
   float now=Time.unscaledTime;tracker.Step(Session.Walker.Body.transform.position,now);
   var ui=PlaytestUiRoot.Instance;bool allowed=Session.LocationArrivalAllowed&&(ui==null||!ui.IsMenuOpen);
   var current=tracker.Current;
   if(current?.Id!=displayed){displayed=null;age=10;}
   if(Session.LocationDiscoveryAllowed&&current!=null&&!Session.Progress.campaign.Facts.Contains("location:"+current.Id)&&queued.Add(current.Id))visits.Enqueue(current);
   // Visits survive leaving during combat or a failed save. Only the visual notice
   // drops stale places; a physically reached place must not lose its discovery.
   if(Session.LocationDiscoveryAllowed&&visits.Count>0&&now>=nextSave)
   {nextSave=now+5;if(Session.TryRecordLocationVisit(visits.Peek())){queued.Remove(visits.Dequeue().Id);nextSave=now+.25f;}}
   var arrival=tracker.TakeAnnouncement(now,allowed);
   if(arrival!=null){SetNames(Catalog,arrival,title,detail);displayed=arrival.Id;age=0;}
   if(allowed)age+=Time.unscaledDeltaTime;
   group.alpha=allowed?WorldLocationTracker.Opacity(age):0;
   float inset=Screen.height>0?(Screen.height-Screen.safeArea.yMax)*1080f/Screen.height:0;
   label.anchoredPosition=new Vector2(0,-64-inset);
  }
  void OnDisable(){if(group!=null)group.alpha=0;}
 }
}
