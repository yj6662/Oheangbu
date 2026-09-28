using System;
using Oheangbu.Data.Demo;
using UnityEngine;
namespace Oheangbu.App.World {
 public sealed partial class WorldMacroPlaytestSession {
  public bool EquipmentEnabled=>Content!=null&&Content.EquipmentCatalog!=null;
  public bool EquipmentReady=>EquipmentEnabled&&Progress?.equipment!=null&&Progress.equipment.Matches(Content.EquipmentCatalog);
  public event Action<string> EquipmentServiceRequested;
  bool equipmentBusy;string equipmentService;
  void InitializeEquipment(){
   if(!EquipmentEnabled||!ready||Progress.equipment.CatalogId!="")return;
   var p=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress));p.equipment=EquipmentState.Initial(Content.EquipmentCatalog);
   if(TryCommitInteraction(p,out _))ApplyDemoStats();
  }
  public bool NearEquipmentService(string id){
   if(!EquipmentReady||Walker==null||Walker.Seated||vitals==null||vitals.Hp01<=0)return false;
   var p=Array.Find(Content.Points,x=>x.Id==id);return p!=null&&Vector3.Distance(Walker.Body.transform.position,p.Position)<=p.Radius;
  }
  bool TryVillageService(string id){
   if(!EquipmentEnabled||(id!="village_shop"&&id!="village_artisan"))return false;
   if(!EquipmentReady){InitializeEquipment();if(!EquipmentReady)return true;}
   equipmentService=id;EquipmentServiceRequested?.Invoke(id);return true;
  }
  public bool DeliverVillageToolbox(out string error){
   error=null;if(!NearEquipmentService("village_artisan")){error="장인에게 직접 전해야 한다.";return false;}
   if(!DemoCampaignProgression.TryAdvance(Content.Campaign,Progress.campaign,DemoEventKind.Interaction,"village_tool_report",out var next,out int reward,Progress.defeated)){error=Progress.campaign.Completed.Contains("village_tool_report")?"이미 건넨 공구함이다.":"계곡 곁 수레 자리를 살펴보자.";return false;}
   if(reward>int.MaxValue-Progress.ledger.currency){error="통보 보유량을 확인해야 한다.";return false;}
   var p=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress));p.campaign=next;p.ledger.currency+=reward;
   return TryCommitInteraction(p,out error);
  }
  public bool TryEquipment(EquipmentAction action,string id,EquipmentSlot slot,long revision,string request,out string error,int expectedPrice=-1){
   error=null;if(equipmentBusy||!ready||!EquipmentReady||vitals.Hp01<=0||HasPendingDefeats){error="장비를 준비하는 중이다.";return false;}
   if(action==EquipmentAction.Buy&&(equipmentService!="village_shop"||!NearEquipmentService(equipmentService))||action==EquipmentAction.Upgrade&&(equipmentService!="village_artisan"||!NearEquipmentService(equipmentService))){error="작업장이나 상점에서 이용한다.";return false;}
   equipmentBusy=true;
   try{
    if(!EquipmentTransactions.Propose(Content.EquipmentCatalog,Progress.equipment,Progress.ledger.currency,action,id,slot,revision,request,out var next,out int coins,out error,expectedPrice))return false;
    var p=JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress));p.equipment=next;p.ledger.currency=coins;
    if(!TryCommitInteraction(p,out error))return false;ApplyDemoStats();return true;
   }finally{equipmentBusy=false;}
  }
 }
}
