using System;
using System.Linq;
using Oheangbu.Core.Domain;
using UnityEngine;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        void BuildInventory()
        {
            if(Session?.Progress?.ui==null)return;
            var ui=Session.Progress.ui;
            subheading.text="조선통보 "+Session.Progress.ledger.currency.ToString("N0")+"  ·  석경 파편 "+ui.items.Sum(x=>x.count)+"개";
            var held=WorldMacroCollectionCatalog.AllFragments.Where(x=>ui.GetItemCount(x.ItemId)>0).ToArray();
            if(held.Length==0)
            {
                EmptyPage("아직 담긴 물품이 없습니다.","폐광의 시작 지점에 남겨진 석경 파편을 살펴보세요.\n발견한 파편은 이곳에 보관되고 술식 도감을 채웁니다.");return;
            }
            if(string.IsNullOrEmpty(selectedItem)||!held.Any(x=>x.ItemId==selectedItem))selectedItem=held[0].ItemId;
            var grid=V.Scroll(contentRoot,"InventoryGrid",0,0,610,592,Mathf.Ceil(held.Length/4f)*140);
            for(int i=0;i<held.Length;i++)
            {
                var fragment=held[i];int index=i;float x=(i%4)*146,y=(i/4)*140;
                var button=V.Button(grid,"Fragment_"+fragment.Id,"",Theme,x,y,134,126,()=>{selectedItem=fragment.ItemId;OpenPage("소지품");},fragment.ItemId==selectedItem);
                if(Theme.FragmentIcon!=null){var icon=V.Image(V.Rect("Stone",button.transform,0,8,70,70),Color.white,Theme.FragmentIcon);icon.preserveAspect=true;}
                V.Text(button.transform,"Glyph",fragment.Letter,Theme.Font,38,fragment.ItemId==selectedItem?Theme.Paper:Theme.Ink,66,12,56,76,TextAnchor.MiddleCenter);
                V.Text(button.transform,"Count","×"+ui.GetItemCount(fragment.ItemId),Theme.Font,17,fragment.ItemId==selectedItem?Theme.Paper:Theme.Muted,12,89,110,35,TextAnchor.MiddleRight);
            }
            var current=held.First(x=>x.ItemId==selectedItem);
            V.Image(V.Rect("ColumnRule",contentRoot,650,0,1,576),new Color(.2f,.18f,.14f,.18f));
            V.Text(contentRoot,"ItemEyebrow","소지품 · 석경",Theme.Font,19,Theme.Seal,700,5,390,40);
            V.Text(contentRoot,"ItemTitle",current.Letter+"의 석경 파편",Theme.Font,32,Theme.Ink,700,64,416,70);
            if(Theme.FragmentIcon!=null){var preview=V.Image(V.Rect("ItemImage",contentRoot,686,150,166,166),Color.white,Theme.FragmentIcon);preview.preserveAspect=true;}
            V.Text(contentRoot,"ItemGlyph",current.Letter,Theme.Font,104,Theme.Ink,858,140,230,166,TextAnchor.MiddleCenter);
            var itemBody=V.Scroll(contentRoot,"ItemBody",700,315,416,200,320);
            V.Text(itemBody,"ItemDescription","부서진 석경에 남아 있던 글자.\n표면의 획을 살피면 술식의 짜임과 쓰임을 읽을 수 있다.\n\n읽은 내용은 술식 도감에 남는다.",Theme.Font,24,Theme.Muted,0,0,396,310);
            V.Button(contentRoot,"ReadCodex","술식 도감에서 읽기",Theme,700,529,416,56,()=>{selectedSpell=current.Letter;OpenPage("술식 도감");},true);
        }
        void EmptyPage(string title,string description)
        {
            V.Text(contentRoot,"EmptyTitle",title,Theme.Font,32,Theme.Ink,40,94,1050,80);
            V.Rule(contentRoot,Theme,40,206,610);
            V.Text(contentRoot,"EmptyBody",description,Theme.Font,24,Theme.Muted,40,248,1000,198);
        }
        void BuildCodex()
        {
            var known=Session.Progress.ui.knownSpellLetters;
            if(!known.Contains(selectedSpell))selectedSpell=known.Count>0?known[0]:"";
            subheading.text="석경에 남은 술식  ·  "+known.Count+" / "+WorldMacroCollectionCatalog.AllSpells.Count;
            var all=WorldMacroCollectionCatalog.AllSpells;
            for(int i=0;i<all.Count;i++)
            {
                var spell=all[i];bool unlocked=known.Contains(spell.Letter);float x=i%5*118,y=i/5*130;
                var b=V.Button(contentRoot,"Codex_"+spell.Id,"",Theme,x,y,106,116,()=>{selectedSpell=spell.Letter;OpenPage("술식 도감");},unlocked&&selectedSpell==spell.Letter);
                b.interactable=unlocked;
                V.Text(b.transform,"Glyph",unlocked?spell.Letter:"·",Theme.Font,unlocked?44:38,unlocked&&selectedSpell==spell.Letter?Theme.Paper:unlocked?Theme.Ink:Theme.Muted,6,6,94,70,TextAnchor.MiddleCenter);
                V.Text(b.transform,"Kind",unlocked?ElementLabel(spell.Element):"미발견",Theme.Font,16,unlocked&&selectedSpell==spell.Letter?Theme.Paper:Theme.Muted,6,80,94,32,TextAnchor.MiddleCenter);
            }
            V.Image(V.Rect("ColumnRule",contentRoot,618,0,1,582),new Color(.2f,.18f,.14f,.18f));
            if(!WorldMacroCollectionCatalog.TryGetSpell(selectedSpell,out var current))
            {
                V.Text(contentRoot,"CodexEmptyTitle","흩어진 석경",Theme.Font,35,Theme.Ink,670,70,438,85);
                V.Text(contentRoot,"CodexEmptyBody","파편을 발견하면 그 글자의\n짜임과 쓰임을 읽을 수 있습니다.\n\n먼저 폐광의 안전한 시작 지점에서\n아·어의 파편을 살펴보세요.",Theme.Font,24,Theme.Muted,670,196,438,260);return;
            }
            V.Text(contentRoot,"Element",ElementLabel(current.Element)+" · "+current.Label,Theme.Font,20,Theme.Seal,668,0,445,45);
            V.Text(contentRoot,"SpellGlyph",current.Letter,Theme.Font,96,Theme.Ink,668,50,170,145,TextAnchor.MiddleCenter);
            var example=V.Rect("StrokeExample",contentRoot,900,58,180,128).gameObject.AddComponent<CodexStrokeExample>();
            example.Initialize(Theme.StrokeTemplates,current.Letter,Theme.Ink);
            V.Text(contentRoot,"ExampleLabel","쓰기 예시",Theme.Font,16,Theme.Muted,900,177,180,32,TextAnchor.MiddleCenter);
            V.Text(contentRoot,"Composition",current.BrushExample,Theme.Font,25,Theme.Ink,668,211,445,72,TextAnchor.MiddleCenter);
            var scroll=V.Scroll(contentRoot,"SpellDescription",668,293,458,213,340);
            V.Text(scroll,"Description",current.Description,Theme.Font,23,Theme.Muted,0,0,426,330);
            V.Text(contentRoot,"DrawHint","Q + 좌클릭: 작도\nQ 놓기: 술식 발동",Theme.Font,20,Theme.Ink,668,518,445,78);
        }
        static string ElementLabel(Element element)
        {
            switch(element){case Element.Wood:return "목 木";case Element.Fire:return "화 火";case Element.Earth:return "토 土";case Element.Metal:return "금 金";case Element.Water:return "수 水";default:return "";}
        }
        void BuildChapae()
        {
            subheading.text="오행부의 차패 · 소유자의 이력";
            var card=V.Rect("Identity",contentRoot,22,18,420,552);
            V.Image(V.Stretch("Backing",card),new Color(.25f,.20f,.13f,.06f));
            V.Text(card,"Title","差 牌",Theme.Font,66,Theme.Ink,35,46,350,98,TextAnchor.MiddleCenter);
            V.Rule(card,Theme,54,174,312);
            V.Text(card,"IdentityLabel","오행부 소속\n전직 집행관",Theme.Font,30,Theme.Ink,50,222,320,110,TextAnchor.MiddleCenter);
            V.Text(card,"IdentityDescription","오행부 소속을 증명하는 차패.\n얻은 덕은 이곳에 새겨진다.",Theme.Font,22,Theme.Muted,45,354,330,100,TextAnchor.MiddleCenter);
            V.Text(card,"SummonPalanquinHint",Session.OpeningCommissionReceived
                ? "G · 오행부로 자동차 부르기\n하차 후 30m 떨어지면 자동 회수"
                : "아전에게 의뢰를 확인한 뒤\nG로 자동차를 부를 수 있습니다.",
                Theme.Font,20,Theme.Seal,34,474,352,72,TextAnchor.MiddleCenter);
            string[] virtueIds={"仁","禮","義","智","信"};string[] names={"인 · 목","예 · 화","의 · 금","지 · 수","신 · 토"};
            for(int i=0;i<5;i++)
            {
                bool owned=Session.Progress.ui.knownVirtues.Contains(virtueIds[i]);float y=16+i*106;
                V.Text(contentRoot,"Virtue_"+i,virtueIds[i],Theme.Font,42,owned?Theme.Seal:Theme.Muted,510,y,80,76,TextAnchor.MiddleCenter);
                V.Text(contentRoot,"VirtueName_"+i,names[i],Theme.Font,27,Theme.Ink,618,y+2,400,52);
                V.Text(contentRoot,"VirtueState_"+i,owned?"차패에 새겨짐":"아직 새겨지지 않음",Theme.Font,20,Theme.Muted,618,y+55,420,39);
                V.Rule(contentRoot,Theme,510,y+94,590);
            }
        }
    }
}
