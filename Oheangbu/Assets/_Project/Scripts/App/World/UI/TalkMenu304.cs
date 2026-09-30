using System;
using System.Collections.Generic;
using UnityEngine;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#306 §2-3 대화 메뉴 (DialogueView304): a MenuWidth column of RowHeight rows stacked UP from `bottom`, centred on
    /// CentreX, on one ink band stroke α MenuBandAlpha. Row = Serif700_28 Paper; focused = paper wet_s + the canvas's one 방점
    /// (FocusMark304) + Title30 Ink and a small filled [F]; Leave is the last row with a hollow [Esc]. A disabled service shows as
    /// an Off row. Harness names: "TalkMenu304", rows "Choice_i" (i = entry index, Leave last).</summary>
    public static class TalkMenu304
    {
        public static FocusRow304[] Build(UiStyle304SO s,DialogueViewSpec304 k,RectTransform parent,IReadOnlyList<DialogueService306> entries,float bottom,
            Action<int> picked,PlaytestUiThemeSO soundTheme,out RectTransform root)
        {
            int n=entries!=null?entries.Count:0;
            float h=n*k.RowHeight,x=Mathf.Round(k.CentreX-k.MenuWidth*.5f),y=bottom-h;
            root=V.Rect("TalkMenu304",parent,0,0,UiPageFit304.Width,UiPageFit304.Height);
            root.gameObject.AddComponent<CanvasGroup>();
            var rows=new FocusRow304[n];
            if(n==0)return rows;
            V.Brush(s,root,"TalkMenuBand",StrokeClass304.Band,s.Ink,x-k.MenuPadX,y-k.MenuPadY,k.MenuWidth+2f*k.MenuPadX,h+2f*k.MenuPadY,k.MenuBandAlpha);
            for(int i=0;i<n;i++)
            {
                int index=i;var e=entries[i];bool leave=e.Kind==DialogueServiceKind306.Leave;
                float keyW=V.KeycapWidth(s,null,leave?"Esc":"F",true);
                rows[i]=V.FocusRow(s,root,"Choice_"+i,k.LabelOf(e),x,y+i*k.RowHeight,k.MenuWidth,k.RowHeight,()=>picked?.Invoke(index),new FocusRowSpec304
                {
                    Role=UiType304.Serif700_28,FocusRole=UiType304.Title30,LabelX=k.RowLabelX,
                    Underlay=StrokeClass304.WetS,UnderlayH=k.FocusUnderlayH,ContentRight=k.MenuWidth-k.KeyRight,
                    Key=leave?"Esc":null,KeyMode=FocusKeyMode304.Hollow,FocusKey=leave?null:"F",KeySmall=true,KeyX=k.MenuWidth-k.KeyRight-keyW,
                    Disabled=!leave&&!e.Enabled,SoundTheme=soundTheme,
                });
            }
            return rows;
        }
    }
}
