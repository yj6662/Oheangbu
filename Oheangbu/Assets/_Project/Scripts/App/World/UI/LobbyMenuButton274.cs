using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    // The hit area stays present while only the focused item's brush frame is visible.
    public sealed class LobbyMenuButton274 : Button
    {
        CanvasGroup frame;
        Text label;
        Color ink;
        float frameTarget;

        public static LobbyMenuButton274 Create(Transform parent,string name,string text,PlaytestUiThemeSO theme,
            float x,float y,float width,float height,UnityAction action)
        {
            var rect=PlaytestUiView.Rect(name,parent,x,y,width,height);
            var hit=PlaytestUiView.Image(rect,Color.clear,null,true);
            var button=rect.gameObject.AddComponent<LobbyMenuButton274>();
            button.transition=Transition.None;button.targetGraphic=hit;button.ink=theme.Ink;
            var border=PlaytestUiView.Stretch("FocusFrame",rect);
            button.frame=border.gameObject.AddComponent<CanvasGroup>();
            button.frame.alpha=0;button.frame.interactable=false;button.frame.blocksRaycasts=false;
            var color=new Color(theme.Ink.r,theme.Ink.g,theme.Ink.b,.62f);
            PlaytestUiView.Image(PlaytestUiView.Rect("Top",border,0,0,width,2),color,theme.BrushStroke);
            PlaytestUiView.Image(PlaytestUiView.Rect("Bottom",border,0,height-2,width,2),color,theme.BrushStroke);
            PlaytestUiView.Image(PlaytestUiView.Rect("Left",border,0,1,2,height-2),color,theme.BrushStroke);
            PlaytestUiView.Image(PlaytestUiView.Rect("Right",border,width-2,1,2,height-2),color,theme.BrushStroke);
            button.label=PlaytestUiView.Text(rect,"Label",text,theme.Font,26,theme.Ink,20,0,width-40,height,TextAnchor.MiddleLeft);
            if(theme.SoundPalette!=null)rect.gameObject.AddComponent<CompactUiSound255>().Theme=theme;
            button.onClick.AddListener(action);
            button.DoStateTransition(button.currentSelectionState,true);
            return button;
        }
        protected override void DoStateTransition(SelectionState state,bool instant)
        {
            frameTarget=state==SelectionState.Highlighted||state==SelectionState.Selected||state==SelectionState.Pressed?1:0;
            if(frame!=null&&(instant||!Application.isPlaying))frame.alpha=frameTarget;
            if(label!=null)label.color=new Color(ink.r,ink.g,ink.b,state==SelectionState.Disabled?.32f:1f);
        }
        void Update()
        {
            if(frame!=null)frame.alpha=Mathf.MoveTowards(frame.alpha,frameTarget,Time.unscaledDeltaTime*9f);
        }
    }
}
