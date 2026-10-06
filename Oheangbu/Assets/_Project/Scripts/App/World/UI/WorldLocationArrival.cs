using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
namespace Oheangbu.App.World.UI
{
 // #304 도착 카드 (DESIGN §5.11 / §7.1, IMPLEMENTATION §7.1): the arrival name is a vertical 화제 on the LEFT, not a centred
 // banner. An ink spine (stroke_spine 150x500 α.9 at (52,150), TopLeft anchored) is drawn top -> bottom (InkReveal wipe,
 // ArrivalInMs), carries the realm in Region72 paper and the local name in Serif700_22 mist, both set vertically
 // (UiText304.Vertical: one glyph per line, a space = an empty line), holds ArrivalHoldMs and bleeds out (ArrivalOutMs).
 // A local name longer than ArrivalSpec304.MaxVerticalChars falls back to one horizontal Serif700_22 line under the spine.
 // Reduced motion: drawn at once, alpha fades of Motion.ReducedMs. Discovery / save / LocationArrivalAllowed are unchanged;
 // "LocationNameCanvas" keeps sortingOrder 55. CreateName / CreateDetail / SetNames (legacy Text, editor reviews) are kept as is.
 [DisallowMultipleComponent]
 public sealed class WorldLocationArrival:MonoBehaviour
 {
  public WorldLocationCatalog Catalog;public WorldMacroPlaytestSession Session;public PlaytestUiThemeSO Theme;
  WorldLocationTracker tracker;CanvasGroup group,textGroup;RectTransform card;Image spine;InkRevealEffect spineFx;TMP_Text region,place,placeFlat;
  UiStyle304SO style;bool spineAcross,previewing;float age=10,nextSave,builtScale=-1;string displayed;
  readonly Queue<WorldLocationCatalog.Entry> visits=new Queue<WorldLocationCatalog.Entry>();readonly HashSet<string> queued=new HashSet<string>();
  /// <summary>Unscaled time of the last card shown (-1000 = none) and how many were shown: the bearing line's D23 emphasis.</summary>
  public float LastAnnouncedTime{get;private set;}=-1000;
  public int AnnouncedCount{get;private set;}
  public bool CardVisible=>group!=null&&group.alpha>0&&age<TotalSeconds;
  public string ShownRegion=>region!=null?region.text:null;
  public string ShownPlace=>place!=null&&place.gameObject.activeSelf?place.text:placeFlat!=null&&placeFlat.gameObject.activeSelf?placeFlat.text:"";

  // ------------------------------------------------------------------ legacy (Text) builders, still used by editor reviews
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

  // ------------------------------------------------------------------ #304 card (TMP), reusable by editor captures
  /// <summary>Builds the vertical arrival card "ArrivalCard304" (1920x1080 TopLeft frame) under a 1920x1080-scaled canvas:
  /// Spine (Image + InkRevealEffect), ArrivalText (CanvasGroup) with RegionName (vertical Region72), LocalName (vertical
  /// Serif700_22) and LocalNameFlat (horizontal fallback). spineAcross: true when the spine is the pre-rotated HUD sprite
  /// (Wipe draws it top -> bottom), false when it is stroke_spine (Bleed).</summary>
  public static RectTransform CreateCard304(UiStyle304SO s,Transform canvas,out Image spine,out InkRevealEffect spineFx,out CanvasGroup text,
   out TMP_Text region,out TMP_Text place,out TMP_Text placeFlat,out bool spineAcross)
  {
   s=s!=null?s:UiStyle304SO.Fallback;var a=s.Arrival;var k=HudTokens304.Load();
   var card=PlaytestUiView.RectAnchored("ArrivalCard304",canvas,0,0,UiPageFit304.Width,UiPageFit304.Height,a.Anchor);
   spineAcross=k.SpineAcross!=null;
   var tint=UiStyle304SO.A(s.Ink,k.InkAlphaFor(s.Materials.InkReveal,a.SpineAlpha));   // QA1/QA2: α.9 at its sRGB weight - UI/InkReveal remaps it (after2/arrival.png spine 37 on a 195 sky)
   if(spineAcross)
   {
    // the sprite is stroke_spine turned 90° CCW; the rect is turned 90° CW, so rect-local left -> right = screen top -> bottom
    var r=a.Spine;float cx=r.x+r.width*.5f,cy=r.y+r.height*.5f;
    spine=PlaytestUiView.SpriteImage(card,"Spine",k.SpineAcross,tint,cx-r.height*.5f,cy-r.width*.5f,r.height,r.width,90f);
    spineFx=InkRevealEffect.On(spine,s,InkRevealMode.Wipe,0f);
   }
   else
   {
    spine=PlaytestUiView.SpriteImage(card,"Spine",s.Sprites.Spine,tint,a.Spine.x,a.Spine.y,a.Spine.width,a.Spine.height);
    spineFx=InkRevealEffect.On(spine,s,InkRevealMode.Bleed,0f);
   }
   spine.raycastTarget=false;
   if(spine.sprite==null)spine.enabled=false;   // style without sprites (setup not run): no white box, the words still show
   var textRoot=PlaytestUiView.Rect("ArrivalText",card,0,0,UiPageFit304.Width,UiPageFit304.Height);
   text=textRoot.gameObject.AddComponent<CanvasGroup>();text.blocksRaycasts=false;text.interactable=false;text.alpha=0;
   region=PlaytestUiView.Label(s,textRoot,"RegionName","",UiType304.Region72,s.Paper,a.RegionPos.x,a.RegionPos.y,a.RegionWidth,0,TextAlignmentOptions.Top);
   place=PlaytestUiView.Label(s,textRoot,"LocalName","",UiType304.Serif700_22,s.Mist,a.PlacePos.x,a.PlacePos.y,a.PlaceWidth,0,TextAlignmentOptions.Top);
   placeFlat=PlaytestUiView.Label(s,textRoot,"LocalNameFlat","",UiType304.Serif700_22,s.Mist,a.Spine.x+12,a.Spine.y+a.Spine.height+k.ArrivalFallbackGap,0,0,TextAlignmentOptions.TopLeft);
   placeFlat.gameObject.SetActive(false);
   return card;
  }

  /// <summary>Writes the realm / local names into the #304 card: vertical when the local name has at most
  /// ArrivalSpec304.MaxVerticalChars glyphs, otherwise the horizontal fallback line. Sizes the labels to their text.</summary>
  public static void SetNames304(UiStyle304SO s,WorldLocationCatalog catalog,WorldLocationCatalog.Entry entry,TMP_Text region,TMP_Text place,TMP_Text placeFlat)
  {
   catalog.ArrivalNames(entry,out var above,out var below);
   SetNames304(s,above,below,region,place,placeFlat);
  }

  public static void SetNames304(UiStyle304SO s,string above,string below,TMP_Text region,TMP_Text place,TMP_Text placeFlat)
  {
   s=s!=null?s:UiStyle304SO.Fallback;var a=s.Arrival;
   Fit(region,UiText304.Vertical(above),s.Role(UiType304.Region72),a.RegionPos.x,a.RegionPos.y,a.RegionWidth);
   int glyphs=0;foreach(char c in below??"")if(!char.IsWhiteSpace(c))glyphs++;
   bool flat=glyphs>a.MaxVerticalChars;
   place.gameObject.SetActive(!flat&&glyphs>0);placeFlat.gameObject.SetActive(flat);
   if(flat)Fit(placeFlat,below,s.Role(UiType304.Serif700_22),a.Spine.x+12,a.Spine.y+a.Spine.height+HudTokens304.Load().ArrivalFallbackGap,0);
   else Fit(place,UiText304.Vertical(below),s.Role(UiType304.Serif700_22),a.PlacePos.x,a.PlacePos.y,a.PlaceWidth);
  }

  // column (x, width): centred column of the mockup; a glyph wider than the column (본문 크기 > 1) widens it about its centre.
  // width 0 = left-aligned line sized to the text.
  static void Fit(TMP_Text t,string value,TypeRole role,float x,float top,float width)
  {
   t.text=value??"";
   var pref=t.GetPreferredValues(t.text);
   float w=width>0?Mathf.Max(width,Mathf.Ceil(pref.x)):Mathf.Max(1,Mathf.Ceil(pref.x));
   float left=width>0?x+(width-w)*.5f:x;
   var r=t.rectTransform;r.sizeDelta=new Vector2(w,Mathf.Max(1,Mathf.Ceil(pref.y)));
   // mockup "top" is the CSS line box: move by the half leading so the first glyph sits where the mockup puts it
   r.anchoredPosition=new Vector2(left,-(top+role.CssTopOffset(t.font)));
  }

  void Start()
  {
   if(Catalog==null||Session==null||Theme==null){enabled=false;return;}
   tracker=new WorldLocationTracker(Catalog);
   style=PlaytestUiView.Style(Theme);
   var go=new GameObject("LocationNameCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(CanvasGroup));go.transform.SetParent(transform,false);
   var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=55;
   var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
   PlaytestUiView.EnsureCanvasChannels(canvas);
   group=go.GetComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;group.alpha=0;
   card=CreateCard304(style,go.transform,out spine,out spineFx,out textGroup,out region,out place,out placeFlat,out spineAcross);
   builtScale=UiText304.TextScale;
  }

  float InSeconds=>style.Motion.ArrivalInMs/1000f;
  float HoldSeconds=>style.ArrivalSeconds;
  float OutSeconds=>style.Motion.ArrivalOutMs/1000f;
  float TotalSeconds=>style!=null?InSeconds+HoldSeconds+OutSeconds:0;

  object factFor;string fact;
  void Update()
  {
   if(tracker==null||Session.Progress==null||Session.Walker==null)return;
   float now=Time.unscaledTime;tracker.Step(Session.Walker.Body.transform.position,now);
   var ui=PlaytestUiRoot.Instance;bool allowed=Session.LocationArrivalAllowed&&(ui==null||!ui.IsMenuOpen);
   var current=tracker.Current;
   if(!previewing&&current?.Id!=displayed){displayed=null;age=10;}
   if(current!=factFor){factFor=current;fact=current!=null?"location:"+current.Id:null;}   // #307: no per-frame string while inside a place
   if(Session.LocationDiscoveryAllowed&&current!=null&&!Session.Progress.campaign.Facts.Contains(fact)&&queued.Add(current.Id))visits.Enqueue(current);
   // Visits survive leaving during combat or a failed save. Only the visual notice
   // drops stale places; a physically reached place must not lose its discovery.
   if(Session.LocationDiscoveryAllowed&&visits.Count>0&&now>=nextSave)
   {nextSave=now+5;if(Session.TryRecordLocationVisit(visits.Peek())){queued.Remove(visits.Dequeue().Id);nextSave=now+.25f;}}
   if(previewing){allowed=true;if(age>=TotalSeconds)previewing=false;}
   var arrival=previewing?null:tracker.TakeAnnouncement(now,allowed);
   if(arrival!=null)
   {
    RefreshScale();
    SetNames304(style,Catalog,arrival,region,place,placeFlat);displayed=arrival.Id;age=0;LastAnnouncedTime=now;AnnouncedCount++;
   }
   if(allowed)age+=Time.unscaledDeltaTime;
   group.alpha=allowed&&age<TotalSeconds?1:0;
   Present(age);
   float inset=Screen.height>0?(Screen.height-Screen.safeArea.yMax)*1080f/Screen.height:0;
   card.anchoredPosition=new Vector2(card.anchoredPosition.x,-inset);
  }

  void RefreshScale()
  {
   if(Mathf.Approximately(builtScale,UiText304.TextScale))return;
   foreach(var t in new[]{region,place,placeFlat})UiText304.ApplyTextScale(t,UiText304.TextScale);builtScale=UiText304.TextScale;
  }

  /// <summary>Capture / tour preview (Art/UI304/screens/harness.md `arrival:`): shows the card with these names at once,
  /// skipping the tracker and LocationArrivalAllowed (menus still hide the HUD canvases, not this one). No discovery is recorded.</summary>
  public void Preview304(string regionName,string placeName)
  {
   if(card==null||style==null)return;
   RefreshScale();
   SetNames304(style,regionName??"",placeName??"",region,place,placeFlat);
   previewing=true;age=0;LastAnnouncedTime=Time.unscaledTime;AnnouncedCount++;
  }

  public void Preview304(string regionName)=>Preview304(regionName,"");

  /// <summary>Preview with the current (or first realm) names of the catalog.</summary>
  public void Preview304()
  {
   if(Catalog==null)return;
   WorldLocationCatalog.Entry entry=tracker!=null?tracker.Current:null;
   if(entry==null)foreach(var e in Catalog.Entries)if(e!=null&&e.Priority==0){entry=e;break;}
   if(entry==null)return;
   Catalog.ArrivalNames(entry,out var above,out var below);Preview304(above,below);
  }

  // in: spine reveal 0 -> 1 (ease.stroke) with the words; hold; out: bleed (ease.lift). Reduced motion: alpha fades only.
  void Present(float t)
  {
   float tin=InSeconds,hold=HoldSeconds,tout=OutSeconds;
   bool reduced=UiTween304.ReducedMotion;float r=style.Motion.ReducedMs/1000f;
   float reveal,text;
   if(t>=tin+hold+tout){reveal=0;text=0;}
   else if(reduced)
   {
    float a=t<r?t/r:t<tin+hold?1:Mathf.Clamp01(1-(t-tin-hold)/Mathf.Max(.001f,Mathf.Min(r,tout)));
    reveal=1;text=a;SetSpineAlpha(a);
   }
   else
   {
    if(t<tin){reveal=UiEase304.Eval(style.Motion.Stroke,t/Mathf.Max(.001f,tin));text=reveal;}
    else if(t<tin+hold){reveal=1;text=1;}
    else{float k=UiEase304.Eval(style.Motion.Lift,(t-tin-hold)/Mathf.Max(.001f,tout));reveal=1-k;text=1-k;}
    SetSpineAlpha(1);
   }
   if(spineFx!=null)
   {
    // the out phase is a bleed (번짐) on either sprite; the in phase keeps the sprite's own mode
    var mode=!reduced&&t>=tin+hold?InkRevealMode.Bleed:spineAcross?InkRevealMode.Wipe:InkRevealMode.Bleed;
    if(spineFx.Mode!=mode)spineFx.Mode=mode;
    spineFx.Reveal=reveal;
   }
   textGroup.alpha=text;
  }

  void SetSpineAlpha(float k)
  {
   // QA1 (after_states/arrival.png): the spine read mid grey (76..93 on a ~198 sky, mockup ink α.9 ≈ 38) under linear blending
   if(spine==null)return;var c=spine.color;float a=HudTokens304.Load().InkAlphaFor(spine.material,style.Arrival.SpineAlpha)*k;if(!Mathf.Approximately(c.a,a)){c.a=a;spine.color=c;}
  }

  void OnDisable(){if(group!=null)group.alpha=0;}
 }
}
