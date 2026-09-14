using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>Small uGUI vocabulary shared by the playtest folio and title. All text is real Korean text.</summary>
    public static class PlaytestUiView
    {
        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(width,height);
            return rect;
        }
        public static RectTransform Stretch(string name, Transform parent, float inset=0)
        {
            var rect = Rect(name,parent,0,0,0,0);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=Vector2.one*inset;rect.offsetMax=-Vector2.one*inset;return rect;
        }
        public static Image Image(RectTransform rect, Color color, Sprite sprite=null, bool hit=false)
        {
            var image=rect.gameObject.AddComponent<Image>();image.color=color;image.sprite=sprite;image.raycastTarget=hit;return image;
        }
        public static RawImage Raw(RectTransform rect, Texture texture, Color color, bool hit=false)
        {
            var image=rect.gameObject.AddComponent<RawImage>();image.texture=texture;image.color=color;image.raycastTarget=hit;return image;
        }
        public static Text Text(Transform parent,string name,string value,Font font,int size,Color color,
            float x,float y,float width,float height,TextAnchor alignment=TextAnchor.UpperLeft)
        {
            var rect=Rect(name,parent,x,y,width,height);var text=rect.gameObject.AddComponent<Text>();
            text.font=font;text.fontSize=size;text.text=value??"";text.color=color;text.alignment=alignment;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.supportRichText=true;text.lineSpacing=1.13f;text.raycastTarget=false;return text;
        }
        public static Button Button(Transform parent,string name,string value,PlaytestUiThemeSO theme,
            float x,float y,float width,float height,UnityAction clicked,bool primary=false)
        {
            var rect=Rect(name,parent,x,y,width,height);
            var image=Image(rect,primary?new Color(theme.Seal.r,theme.Seal.g,theme.Seal.b,.94f):new Color(.2f,.18f,.14f,.045f),null,true);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            ColorBlock colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.89f,.85f,.75f,1);
            colors.selectedColor=colors.highlightedColor;colors.pressedColor=new Color(.70f,.65f,.53f,1);
            colors.disabledColor=new Color(1,1,1,.30f);colors.fadeDuration=.08f;button.colors=colors;
            var text=Text(rect,"Label",value,theme.Font,22,primary?theme.Paper:theme.Ink,18,0,width-36,height,TextAnchor.MiddleLeft);
            button.onClick.AddListener(clicked);
            if(!primary)Image(Rect("FootRule",rect,0,height-1,width,1),new Color(theme.Ink.r,theme.Ink.g,theme.Ink.b,.18f));
            return button;
        }
        public static void Rule(Transform parent,PlaytestUiThemeSO theme,float x,float y,float width)
        {
            Image(Rect("InkRule",parent,x,y,width,2),new Color(theme.Ink.r,theme.Ink.g,theme.Ink.b,.22f),theme.BrushStroke);
        }
        public static RectTransform Scroll(Transform parent,string name,float x,float y,float width,float height,float contentHeight)
        {
            var root=Rect(name,parent,x,y,width,height);var scroll=root.gameObject.AddComponent<ScrollRect>();
            var viewport=Stretch("Viewport",root);Image(viewport,new Color(0,0,0,.001f),null,true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect("Content",viewport,0,0,width-18,Mathf.Max(height,contentHeight));
            scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=34;scroll.inertia=true;
            return content;
        }
        public static Slider Slider(Transform parent,string name,float x,float y,float width,float min,float max,float value,UnityAction<float> changed,PlaytestUiThemeSO theme)
        {
            var root=Rect(name,parent,x,y,width,36);Image(root,new Color(0,0,0,.001f),null,true);
            var slider=root.gameObject.AddComponent<Slider>();slider.minValue=min;slider.maxValue=max;
            Image(Rect("Track",root,0,16,width,3),new Color(.2f,.18f,.14f,.18f));
            var fillArea=Rect("FillArea",root,0,14,width,7);var fill=Stretch("Fill",fillArea);Image(fill,theme.Seal);
            var handleArea=Rect("HandleArea",root,0,0,width,36);var handle=Rect("Handle",handleArea,0,6,15,24);
            var handleImage=Image(handle,theme.Seal,null,true);
            slider.fillRect=fill;slider.handleRect=handle;slider.targetGraphic=handleImage;
            slider.SetValueWithoutNotify(value);slider.onValueChanged.AddListener(changed);return slider;
        }
        public static void Clear(Transform parent)
        {
            for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i);child.gameObject.SetActive(false);UnityEngine.Object.Destroy(child.gameObject);}
        }
    }
}
