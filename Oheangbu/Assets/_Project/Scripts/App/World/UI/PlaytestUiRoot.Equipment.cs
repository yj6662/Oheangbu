using System;
using System.Linq;
using Oheangbu.Data.Demo;
using UnityEngine;
using UnityEngine.UI;
using V=Oheangbu.App.World.UI.PlaytestUiView;
namespace Oheangbu.App.World.UI {
 public sealed partial class PlaytestUiRoot {
  string gearSelection="",gearError="";bool fragmentTab;
  void OpenEquipmentService(string id){gearSelection="";gearError="";OpenPage(id=="village_shop"?"장비 상점":"장비 강화");}
  void GearSymbol(Transform parent,EquipmentSlot slot,Color color,float x=12,float y=12,float size=64){
   var art=Session.Content.EquipmentUiArt;var sprite=art!=null?art.ForSlot(slot):null;
   // Keep the illustration on paper even when its button is selected in red.
   if(sprite!=null&&color==Theme.Paper){var paper=V.Image(V.Stretch("SelectedGearPaper",parent,5),Theme.Paper);paper.color=new Color(Theme.Paper.r,Theme.Paper.g,Theme.Paper.b,.92f);paper.raycastTarget=false;}
   var g=V.Rect("GearSymbol",parent,x,y,size,size).gameObject.AddComponent<EquipmentInkGraphic>();
   g.Symbol=(int)slot;g.Artwork=sprite;g.color=sprite!=null?new Color(1,1,1,color.a):color;g.raycastTarget=false;
  }
  void GearAction(EquipmentAction action,string id,EquipmentSlot slot,long revision,int expectedPrice=-1){
   bool accepted=Session.TryEquipment(action,id,slot,revision,Guid.NewGuid().ToString("N"),out gearError,expectedPrice);
   string cue=!accepted?"ui_error":action==EquipmentAction.Buy?"purchase":action==EquipmentAction.Upgrade?"upgrade":action==EquipmentAction.Unequip?"unequip":"equip";
   if(!PlayNamedSound(cue,.65f))PlayUi(accepted?Theme.ConfirmSound:Theme.BackSound,.4f);
   suppressNextPageSound255=Theme.SoundPalette!=null;
   OpenPage(Page);
  }
  void BuildEquipmentInventory(){
   var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;
   if(!Session.EquipmentReady){V.Text(contentRoot,"GearError","장비 저장을 확인해야 한다.",Theme.Font,24,Theme.Ink,30,40,850,70);return;}
   var silhouette=V.Rect("InkPlayerSilhouette",contentRoot,52,5,324,565).gameObject.AddComponent<EquipmentInkGraphic>();
   silhouette.Artwork=Session.Content.EquipmentUiArt!=null?Session.Content.EquipmentUiArt.Portrait:null;
   silhouette.color=silhouette.Artwork!=null?new Color(1,1,1,.68f):new Color(Theme.Ink.r,Theme.Ink.g,Theme.Ink.b,.22f);silhouette.raycastTarget=false;
   var xy=new[]{new Vector2(8,240),new Vector2(336,16),new Vector2(336,192),new Vector2(8,128),new Vector2(336,444),new Vector2(8,416)};
   for(int i=0;i<6;i++){
    var slot=(EquipmentSlot)i;string worn=state.Equipped[i];
    var button=V.Button(contentRoot,"Equipped_"+slot,"",Theme,xy[i].x,xy[i].y,88,88,()=>{gearSelection=worn??"";gearError="";OpenPage("소지품");},!string.IsNullOrEmpty(worn)&&gearSelection==worn);
    var drop=button.gameObject.AddComponent<EquipmentDropSlot>();drop.Slot=slot;
    GearSymbol(button.transform,slot,string.IsNullOrEmpty(worn)?new Color(Theme.Ink.r,Theme.Ink.g,Theme.Ink.b,.22f):gearSelection==worn?Theme.Paper:Theme.Ink);

   }
   V.Button(contentRoot,"EquipmentTab","장비",Theme,476,0,130,40,()=>{fragmentTab=false;OpenPage("소지품");},!fragmentTab,true);
   V.Button(contentRoot,"FragmentsTab","석경",Theme,616,0,130,40,()=>{fragmentTab=true;OpenPage("소지품");},fragmentTab,true);
   var grid=V.Scroll(contentRoot,"EquipmentOwnedGrid",476,60,642,244,Mathf.Ceil((fragmentTab?Session.Progress.ui.items.Count:state.Owned.Count)/5f)*120);
   if(fragmentTab){int i=0;foreach(var f in WorldMacroCollectionCatalog.AllFragments.Where(x=>Session.Progress.ui.GetItemCount(x.ItemId)>0)){
    var item=f;var b=V.Button(grid,"Fragment_"+f.Id,f.Letter,Theme,i%5*128,i/5*120,112,104,()=>{ShowDetail(item.Letter+"의 석경",item.Letter+"의 파편을 "+Session.Progress.ui.GetItemCount(item.ItemId)+"개 지니고 있다.");},false,true);i++;
   }}else for(int i=0;i<state.Owned.Count;i++){
    var item=state.Owned[i];var d=catalog.Find(item.Id);var b=V.Button(grid,"Gear_"+item.Id,"",Theme,i%5*128,i/5*120,112,104,()=>{gearSelection=item.Id;gearError="";OpenPage("소지품");},gearSelection==item.Id);
    GearSymbol(b.transform,d.Slot,gearSelection==item.Id?Theme.Paper:Theme.Ink,20,16,72);
    V.Text(b.transform,"GearLevel",(state.Equipped.Contains(item.Id)?"•":""),Theme.Font,16,gearSelection==item.Id?Theme.Seal:Theme.Muted,90,82,14,16,TextAnchor.MiddleCenter);
    var drag=b.gameObject.AddComponent<EquipmentDragItem>();drag.ItemId=item.Id;drag.Revision=state.Revision;drag.Dropped=(id,slot,rev)=>GearAction(EquipmentAction.Equip,id,slot,rev);
   }
   DrawGearDetails(false,false);
  }
  static string EffectName(EquipmentEffect effect)=>effect==EquipmentEffect.MaxHealth?"최대 체력":effect==EquipmentEffect.MaxInk?"최대 먹":effect==EquipmentEffect.WoodDamage?"목 속성 위력":"모든 속성 위력";
  void DrawGearDetails(bool shop,bool forge){
   var catalog=Session.Content.EquipmentCatalog;var state=Session.Progress.equipment;var d=catalog.Find(gearSelection);if(d==null)return;
   float bonus=catalog.Bonus(state,d.Id);var old=state.Equipped[(int)d.Slot];float delta=bonus-(string.IsNullOrEmpty(old)?0:catalog.Bonus(state,old));
   V.Rule(contentRoot,Theme,476,330,624);
   V.Text(contentRoot,"SelectedGearTitle",d.Name+"  +"+state.Level(d.Id),Theme.Font,29,Theme.Ink,476,348,624,46);
   string info=EffectName(d.Effect)+" +"+(bonus*100).ToString("0")+"%";
   if(forge&&state.Level(d.Id)<3)info+=" → +"+((bonus+catalog.UpgradeStep)*100).ToString("0")+"%";
   else if(!shop)info+="   (교체 "+(delta>=0?"+":"")+(delta*100).ToString("0")+"%p)";
   V.Text(contentRoot,"SelectedGearEffect",info,Theme.Font,21,Theme.Muted,476,397,624,48);
   if(!string.IsNullOrEmpty(gearError))V.Text(contentRoot,"GearTransactionError",gearError,Theme.Font,18,Theme.Seal,476,512,624,64);
   long revision=state.Revision;int quotedPrice=shop?d.Price:forge&&state.Level(d.Id)<3?catalog.UpgradeCosts[state.Level(d.Id)]:0;
   if(shop){bool owned=state.Has(d.Id);var b=V.Button(contentRoot,"GearBuy",owned?"보유 중":d.Price+"통보 · 구매",Theme,476,452,248,48,()=>GearAction(EquipmentAction.Buy,d.Id,d.Slot,revision,quotedPrice),false,true);b.interactable=!owned;}
   else if(forge){int level=state.Level(d.Id);var b=V.Button(contentRoot,"GearUpgrade",level==3?"강화 완료":catalog.UpgradeCosts[level]+"통보 · 강화",Theme,476,452,248,48,()=>GearAction(EquipmentAction.Upgrade,d.Id,d.Slot,revision,quotedPrice),false,true);b.interactable=level<3;}
   else {bool worn=old==d.Id;V.Button(contentRoot,"GearEquip",worn?"해제":"착용",Theme,476,452,180,48,()=>GearAction(worn?EquipmentAction.Unequip:EquipmentAction.Equip,d.Id,d.Slot,revision),false,true);}
  }
 }
}
