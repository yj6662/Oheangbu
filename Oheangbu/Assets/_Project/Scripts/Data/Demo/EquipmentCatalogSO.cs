using System;
using System.Collections.Generic;
using UnityEngine;
namespace Oheangbu.Data.Demo {
 public enum EquipmentSlot { Brush, Head, Body, Hands, Feet, Accessory }
 public enum EquipmentEffect { AllDamage, MaxInk, MaxHealth, WoodDamage }
 [Serializable] public sealed class EquipmentDefinition {
  public string Id,Name; public EquipmentSlot Slot; public EquipmentEffect Effect;
  public float Bonus; public int Price; public bool Starter;
 }
 [CreateAssetMenu(menuName="Oheangbu/Compact/Equipment TEST Catalog")]
 public sealed class EquipmentCatalogSO:ScriptableObject {
  public string CatalogId="compact-village-test-v1";
  public int[] UpgradeCosts={40,80,140}; public float UpgradeStep=.02f;
  public EquipmentDefinition[] Items={
   new EquipmentDefinition{Id="old_brush",Name="닳은 붓",Slot=EquipmentSlot.Brush,Effect=EquipmentEffect.AllDamage,Starter=true},
   new EquipmentDefinition{Id="old_robe",Name="기운 도포",Slot=EquipmentSlot.Body,Effect=EquipmentEffect.MaxHealth,Starter=true},
   new EquipmentDefinition{Id="pine_brush",Name="송연필",Slot=EquipmentSlot.Brush,Effect=EquipmentEffect.AllDamage,Bonus=.04f,Price=120},
   new EquipmentDefinition{Id="linen_head",Name="삼베 두건",Slot=EquipmentSlot.Head,Effect=EquipmentEffect.MaxInk,Bonus=.05f,Price=60},
   new EquipmentDefinition{Id="work_robe",Name="누빈 도포",Slot=EquipmentSlot.Body,Effect=EquipmentEffect.MaxHealth,Bonus=.06f,Price=100},
   new EquipmentDefinition{Id="work_wrap",Name="작업 손싸개",Slot=EquipmentSlot.Hands,Effect=EquipmentEffect.AllDamage,Bonus=.02f,Price=80},
   new EquipmentDefinition{Id="straw_boot",Name="가죽 덧신",Slot=EquipmentSlot.Feet,Effect=EquipmentEffect.MaxHealth,Bonus=.03f,Price=60},
   new EquipmentDefinition{Id="wood_bead",Name="회양목 염주",Slot=EquipmentSlot.Accessory,Effect=EquipmentEffect.WoodDamage,Bonus=.06f,Price=80}};
  public EquipmentDefinition Find(string id)=>Array.Find(Items,x=>x.Id==id);
  public float Bonus(EquipmentState state,string id){var d=Find(id);return d==null?0:d.Bonus+state.Level(id)*UpgradeStep;}
  public bool Valid(){
   if(string.IsNullOrWhiteSpace(CatalogId)||UpgradeCosts==null||UpgradeCosts.Length!=3||!float.IsFinite(UpgradeStep)||UpgradeStep<0||Items==null)return false;
   var ids=new HashSet<string>();foreach(var c in UpgradeCosts)if(c<0)return false;
   foreach(var d in Items)if(d==null||string.IsNullOrWhiteSpace(d.Id)||!ids.Add(d.Id)||d.Price<0||!Enum.IsDefined(typeof(EquipmentSlot),d.Slot)||!Enum.IsDefined(typeof(EquipmentEffect),d.Effect)||!float.IsFinite(d.Bonus)||d.Bonus<0)return false;
   return true;
  }
 }
 [Serializable] public sealed class OwnedEquipment {public string Id;public int Level;}
 [Serializable] public sealed class EquipmentState {
  public string CatalogId="";public long Revision;public List<OwnedEquipment> Owned=new List<OwnedEquipment>();
  public string[] Equipped=new string[6];public List<string> Requests=new List<string>();
  public bool Has(string id)=>Owned.Exists(x=>x.Id==id);
  public int Level(string id)=>Owned.Find(x=>x.Id==id)?.Level??0;
  public bool IsValid(){
   if(CatalogId==null||Revision<0||Owned==null||Equipped==null||Equipped.Length!=6||Requests==null)return false;
   var ids=new HashSet<string>();foreach(var x in Owned)if(x==null||string.IsNullOrWhiteSpace(x.Id)||!ids.Add(x.Id)||x.Level<0||x.Level>3)return false;
   var worn=new HashSet<string>();foreach(var id in Equipped)if(!string.IsNullOrEmpty(id)&&(!ids.Contains(id)||!worn.Add(id)))return false;
   return Requests.TrueForAll(x=>!string.IsNullOrWhiteSpace(x))&&new HashSet<string>(Requests).Count==Requests.Count;
  }
  public EquipmentState Copy(){var c=new EquipmentState{CatalogId=CatalogId,Revision=Revision,Equipped=(string[])Equipped.Clone(),Requests=new List<string>(Requests)};foreach(var x in Owned)c.Owned.Add(new OwnedEquipment{Id=x.Id,Level=x.Level});return c;}
  public bool Matches(EquipmentCatalogSO catalog){if(!IsValid()||catalog==null||!catalog.Valid()||CatalogId!=catalog.CatalogId)return false;foreach(var item in Owned)if(catalog.Find(item.Id)==null)return false;for(int i=0;i<6;i++)if(!string.IsNullOrEmpty(Equipped[i])&&(int)catalog.Find(Equipped[i]).Slot!=i)return false;return true;}
  public static EquipmentState Initial(EquipmentCatalogSO catalog){var s=new EquipmentState{CatalogId=catalog.CatalogId};foreach(var d in catalog.Items)if(d.Starter){s.Owned.Add(new OwnedEquipment{Id=d.Id});s.Equipped[(int)d.Slot]=d.Id;}return s;}
 }
 public enum EquipmentAction { Buy,Upgrade,Equip,Unequip }
 // Expected revision + stable intent ID makes stale quotes and duplicate callbacks harmless.
 public static class EquipmentTransactions {
  public static bool Propose(EquipmentCatalogSO catalog,EquipmentState source,int coins,EquipmentAction action,string id,EquipmentSlot slot,long expected,string request,
   out EquipmentState next,out int balance,out string error,int expectedPrice=-1){
   next=null;balance=coins;error=null;
   if(source==null||!source.Matches(catalog)||coins<0||!Enum.IsDefined(typeof(EquipmentSlot),slot)||!Enum.IsDefined(typeof(EquipmentAction),action)||string.IsNullOrWhiteSpace(request)){error="장비 데이터를 확인할 수 없다.";return false;}
   if(source.Requests.Contains(request)){error="이미 처리한 요청이다.";return false;}
   if(source.Revision!=expected){error="장비가 바뀌었다. 다시 선택한다.";return false;}
   var d=catalog.Find(id);int price=0;
   if(action==EquipmentAction.Unequip){if(string.IsNullOrEmpty(source.Equipped[(int)slot])){error="빈 장착 칸이다.";return false;}}
   else if(d==null){error="없는 장비다.";return false;}
   else if(action==EquipmentAction.Buy){if(d.Starter||source.Has(id)){error="이미 가진 장비다.";return false;}price=d.Price;}
   else if(!source.Has(id)){error="소유하지 않은 장비다.";return false;}
   else if(action==EquipmentAction.Upgrade){int level=source.Level(id);if(level>=3){error="더 강화할 수 없다.";return false;}price=catalog.UpgradeCosts[level];}
   else if(d.Slot!=slot){error="이 부위에는 착용할 수 없다.";return false;}
   if(expectedPrice>=0&&expectedPrice!=price){error="가격이 바뀌었다. 다시 선택한다.";return false;}
   if(coins<price){error="통보가 부족하다.";return false;}
   if(source.Revision==long.MaxValue){error="장비 기록을 확인해야 한다.";return false;}
   next=source.Copy();balance=coins-price;
   switch(action){case EquipmentAction.Buy:next.Owned.Add(new OwnedEquipment{Id=id});break;case EquipmentAction.Upgrade:next.Owned.Find(x=>x.Id==id).Level++;break;case EquipmentAction.Equip:next.Equipped[(int)slot]=id;break;case EquipmentAction.Unequip:next.Equipped[(int)slot]=null;break;}
   next.Revision++;next.Requests.Add(request);if(next.Requests.Count>64)next.Requests.RemoveAt(0);return true;
  }
 }
}
