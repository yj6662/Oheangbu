using System;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEngine;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        void OnDemoShopInteraction(PrologueInteractionKind kind,Vector3 point)
        {
            if(kind==PrologueInteractionKind.Rest&&Session!=null){
                if(Session.RestPresentationActive)return;
                if(Theme.Icons!=null){Session.Walker.Motor.TryBeginRestPose();return;}
                if(Session.AtDemoShop)OpenPage("정비");
            }
        }
        void BuildDemoShop()
        {
            if(Session?.DemoEconomy==null)return;
            // 재화는 하단 상태줄 상시 표시. 정비명만.
            subheading.text=Session.DemoShopDisplayName+" 정비";
            string[] labels={"목 마석","화 마석","토 마석","금 마석","수 마석","체력 보강","먹 용량 보강"};
            for(int i=0;i<labels.Length;i++)
            {
                var track=(DemoUpgradeTrack)i;
                if(!Session.DemoEconomy.TryQuote(track,out var quote,out var error)){ShowNotice(error);continue;}
                float y=i*74;
                if(Theme.Icons==null)V.Text(contentRoot,"UpgradeLabel_"+track,labels[i],Theme.Font,26,Theme.Ink,0,y,220,56);
                else if(i<5)V.Text(contentRoot,"ElementGlyph","木火土金水"[i].ToString(),Theme.Font,40,Theme.Ink,60,y,64,56);
                else {var icon=V.Image(V.Rect("Resource",contentRoot,64,y,40,48),i==5?Theme.Icons.Health:Theme.Icons.Ink,i==5?Theme.Icons.Heart:Theme.Icons.InkBottle);icon.preserveAspect=true;}
                string percent=Mathf.RoundToInt(quote.CurrentTotalBonus*100)+"%";
                V.Text(contentRoot,"UpgradeLevel_"+track,quote.CurrentLevel+" / "+quote.MaximumLevel+"  ·  +"+percent,Theme.Font,22,Theme.Muted,250,y,320,56);
                var intent=Guid.NewGuid().ToString("N");
                var button=V.Button(contentRoot,"Buy_"+track,quote.AtMaximum?"강화 완료":"+"+Mathf.RoundToInt(quote.NextTotalBonus*100)+"%  ·  "+quote.Cost+"통보",Theme,654,y,440,58,()=>
                {
                    // Keep this quote/intent stable for a double click or delayed callback.
                    bool bought=Session.TryBuyDemoUpgrade(track,quote.CurrentLevel,intent,out string message);
                    ShowNotice(message,5);
                    if(bought)OpenPage("정비");
                });
                if(Theme.Icons!=null){var symbol=button.transform.Find("ControlSymbol");if(symbol!=null)symbol.localPosition+=Vector3.left*155;
                    V.Text(button.transform,"Price",quote.AtMaximum?"—":quote.Cost.ToString("N0"),Theme.Font,24,Theme.Ink,114,0,280,58,UnityEngine.TextAnchor.MiddleCenter);}
                button.interactable=quote.CanAfford;
            }
            V.Text(contentRoot,"UpgradeHint","마석 · 속성 위력    보강 · 최대 체력·먹",Theme.Font,19,Theme.Muted,0,536,1080,64);
        }
    }
}
