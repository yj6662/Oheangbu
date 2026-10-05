using System;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using TMPro;
using UnityEngine;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 정비 (DESIGN §7.9, IMPLEMENTATION §7.15): reachable only beside a shelter (the rail shows it only then).
    /// Rows = 오행 형상 도장 + 이름 + 단계 방점 + 값 → 다음 값 + 비용 + [Enter] (filled while focused). Results go to the notice channel
    /// (UiNoticeChannelSO: 정비 = Service, rejection = Error) instead of ShowNotice. Names kept: Buy_&lt;track&gt;.</summary>
    public sealed partial class PlaytestUiRoot
    {
        void OnDemoShopInteraction(PrologueInteractionKind kind,Vector3 point)
        {
            if(kind==PrologueInteractionKind.Rest&&Session!=null){
                if(Session.RestPresentationActive)return;
                // Gameplay branch, not label hiding: the live (icon) theme rests with the seated rest pose and reaches 정비
                // through the rail; the text theme opens 정비 directly. Kept as is (#304 changes no rest behaviour).
                if(Theme.Icons!=null){Session.Walker.Motor.TryBeginRestPose();return;}
                if(Session.AtDemoShop)OpenPage("정비");
            }
        }

        void BuildDemoShop()
        {
            if(Session?.DemoEconomy==null)return;
            var s=Content304Style;
            V.Label(s,contentRoot,"ShopPlace",Session.DemoShopDisplayName,UiType304.Serif700_24,s.Mist,64,164);
            Content304Currency(contentRoot,"Currency",1856,160,Session.Progress.ledger.currency);
            V.Label(s,contentRoot,"GroupStones","마석 · 속성 위력",UiType304.Meta20,s.Mist,96,214);
            V.Label(s,contentRoot,"GroupBody","보강",UiType304.Meta20,s.Mist,96,704);   // D308-27 (정비 ③): the rows under it name the two
            string[] labels={"목 마석","화 마석","토 마석","금 마석","수 마석","체력 보강","먹 용량 보강"};
            GameObject first=null;
            for(int i=0;i<labels.Length;i++)
            {
                var track=(DemoUpgradeTrack)i;
                if(!Session.DemoEconomy.TryQuote(track,out var quote,out var error)){Content304Notice(UiNoticeKind304.Error,error);continue;}
                float y=i<5?246+i*88:736+(i-5)*88;
                var intent=Guid.NewGuid().ToString("N");
                var row=V.FocusRow(s,contentRoot,"Buy_"+track,labels[i],64,y,1200,80,()=>
                {
                    // Keep this quote/intent stable for a double click or delayed callback.
                    bool bought=Session.TryBuyDemoUpgrade(track,quote.CurrentLevel,intent,out string message);
                    Content304Notice(bought?UiNoticeKind304.Service:UiNoticeKind304.Error,message,bought?null:Session.DemoShopDisplayName);
                    if(bought)OpenPage("정비");
                },new FocusRowSpec304
                {
                    Role=UiType304.Label28,LabelX=120,DabGap=80,UnderlayX=0,ContentRight=1100,
                    Key="Enter",KeyMode=FocusKeyMode304.FilledWhenFocused,KeyX=1010,KeySmall=false,
                    Disabled=!quote.CanAfford,DisabledReason=quote.AtMaximum?"강화 완료":"통보가 모자라다",MetaX=1010,
                    SoundTheme=Theme,
                });
                Content304Note(row.Button);
                // 오행 형상 도장 (마석) or the resource pictogram (보강), recoloured with the focus state
                if(i<5)
                {
                    var stamp=V.Element(s,row.Rect,i,48,14,52,s.Paper);
                    var hanja=V.Label(s,stamp.transform,"Hanja",Content304Hanja((Oheangbu.Core.Domain.Element)i),UiType304.Serif900_22,s.Paper,0,0,52,52,TextAlignmentOptions.Center);
                    row.Visual.AddTint(stamp,s.Paper,s.Ink);row.Visual.AddTint(hanja,s.Paper,s.Ink);
                }
                else if(Theme.Icons!=null)
                {
                    var pict=V.SpriteImage(row.Rect,"Pictogram",i==5?Theme.Icons.Heart:Theme.Icons.InkBottle,s.Paper,54,20,40,40);
                    pict.preserveAspect=true;row.Visual.AddTint(pict,s.Paper,s.Ink);
                }
                // 단계 방점 (achieved = paper, remaining = mist .35) + 값 → 다음 값 + 비용. D308-27 (정비 ①②): no word per row - the
                // arrow says now / next and the page head carries 조선통보 with the balance. The next column closed up (690 -> 648)
                for(int k=0;k<quote.MaximumLevel;k++)
                {
                    bool on=k<quote.CurrentLevel;
                    // remaining steps pre-composited on their surface (Mist α.35 on the veil, Ash α.45 on the focus paper): a linear
                    // blend lifts both mid-tones (#63 for #42 on the veil)
                    var dab=V.SpriteImage(row.Rect,"LevelDab",s.Sprites.Dab,on?s.Paper:Content304Precomposite(s.Veil,s.Mist,.35f),330+k*36,30,28,21,s.DabRotation);
                    row.Visual.AddTint(dab,dab.color,on?s.Ink:Content304Precomposite(s.Paper,s.Ash,.45f));
                }
                string now="+"+Mathf.RoundToInt(quote.CurrentTotalBonus*100)+"%";
                var nowLabel=V.Label(s,row.Rect,"UpgradeLevel_"+track,now,UiType304.Meta20,s.Mist,560,27);
                row.Visual.AddTint(nowLabel,s.Mist,s.Ash);
                if(!quote.AtMaximum)
                {
                    var next=V.Label(s,row.Rect,"UpgradeNext_"+track,"→ +"+Mathf.RoundToInt(quote.NextTotalBonus*100)+"%",UiType304.Body24,s.Paper,648,22);
                    var price=V.Label(s,row.Rect,"UpgradePrice_"+track,quote.Cost.ToString("N0"),UiType304.Meta20,quote.CanAfford?s.Mist:s.Off,648+next.rectTransform.sizeDelta.x+18,27);
                    row.Visual.AddTint(next,s.Paper,s.Ink);row.Visual.AddTint(price,price.color,s.Ash);
                }
                if(first==null&&quote.CanAfford)first=row.Button.gameObject;
            }
            Content304Restore(contentRoot,first);
        }
    }
}
