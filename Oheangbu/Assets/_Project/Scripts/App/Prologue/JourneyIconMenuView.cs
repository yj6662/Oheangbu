using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
namespace Oheangbu.App.Prologue {
 public sealed class JourneyIconMenuView : MonoBehaviour {
  GameObject root;
  Image result;
  EventSystem events;
  GameObject ownedEvents;
  Button first;
  bool wasOpen;
  public void Initialize(ProloguePauseMenu owner){
   root=new GameObject("Journey Pause Icons",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.layer=5;root.transform.SetParent(transform,false);
   var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=Camera.main;if(canvas.worldCamera!=null)canvas.planeDistance=Mathf.Max(1,canvas.worldCamera.nearClipPlane+.1f);else canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1000;
   var scale=root.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;
   var dim=Create<Image>("Pause shade",root.transform);dim.color=new Color(0,0,0,.48f);dim.rectTransform.anchorMin=Vector2.zero;dim.rectTransform.anchorMax=Vector2.one;dim.rectTransform.offsetMin=dim.rectTransform.offsetMax=Vector2.zero;
   var buttons=new Button[3];string[] names={"Continue","Save","Save and exit"};
   for(int i=0;i<3;i++){
    var background=Create<Image>(names[i],root.transform);background.rectTransform.sizeDelta=new Vector2(88,88);background.rectTransform.anchoredPosition=new Vector2((i-1)*112,0);
    var b=background.gameObject.AddComponent<Button>();b.targetGraphic=background;var colors=b.colors;colors.normalColor=new Color(.15f,.15f,.13f,.15f);colors.highlightedColor=colors.selectedColor=new Color(.42f,.40f,.34f,.65f);colors.pressedColor=new Color(.65f,.62f,.53f,.8f);colors.fadeDuration=.12f;b.colors=colors;
    var icon=Create<RawImage>("Recraft icon",background.transform);icon.texture=owner.IconAt(i);icon.rectTransform.sizeDelta=new Vector2(56,56);icon.color=new Color(.92f,.91f,.86f);icon.raycastTarget=false;buttons[i]=b;
   }
   buttons[0].onClick.AddListener(()=>owner.SetOpen(false));buttons[1].onClick.AddListener(()=>owner.SaveProgress());buttons[2].onClick.AddListener(owner.SaveAndExit);
   for(int i=0;i<3;i++){var nav=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=buttons[(i+2)%3],selectOnRight=buttons[(i+1)%3]};buttons[i].navigation=nav;}
   result=Create<Image>("Save result",root.transform);result.rectTransform.sizeDelta=new Vector2(7,7);result.rectTransform.anchoredPosition=new Vector2(0,-70);result.raycastTarget=false;result.gameObject.SetActive(false);first=buttons[0];
   events=FindFirstObjectByType<EventSystem>();if(events==null){var go=new GameObject("Journey menu events",typeof(EventSystem),typeof(InputSystemUIInputModule));go.transform.SetParent(transform,false);ownedEvents=go;events=go.GetComponent<EventSystem>();go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();}
   root.SetActive(false);
  }
  static T Create<T>(string name,Transform parent) where T:Graphic {var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer));go.layer=5;go.transform.SetParent(parent,false);var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);return go.AddComponent<T>();}
  public void Refresh(bool open,bool? saved){if(root==null)return;root.SetActive(open);result.gameObject.SetActive(open&&saved.HasValue);if(saved.HasValue)result.color=saved.Value?new Color(.45f,.65f,.45f):new Color(.8f,.3f,.2f);if(open&&!wasOpen)events.SetSelectedGameObject(first.gameObject);else if(!open&&wasOpen&&events.currentSelectedGameObject!=null&&events.currentSelectedGameObject.transform.IsChildOf(root.transform))events.SetSelectedGameObject(null);wasOpen=open;}
  void OnDestroy(){if(root!=null)Destroy(root);if(ownedEvents!=null)Destroy(ownedEvents);}
 }
}
