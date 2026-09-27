using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using Oheangbu.Data.Demo;
using Oheangbu.Core.Domain;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  static WorldMacroPlaytestSession VillageSession()=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).Single();
  public static string VillageChecks(){
   var s=VillageSession();var cat=s.Content.EquipmentCatalog;var results=new List<string>();
   void Check(bool ok,string name){results.Add((ok?"PASS ":"FAIL ")+name);File.WriteAllText(VillageOutput+"/rules.txt",string.Join("\n",results));if(!ok)throw new Exception(name);}
   Check(cat!=null&&cat.Valid(),"candidate catalog valid");
   var gear=EquipmentState.Initial(cat);Check(gear.Owned.Count==2&&gear.Equipped.Count(x=>!string.IsNullOrEmpty(x))==2,"exactly two zero-bonus starter items");
   var economy=new DemoEconomyState();var rules=DemoEconomyRules.ApprovedDefaults;
   var stats=new RuntimePlayerStats(economy,rules,gear,cat);Check(stats.DamageMultiplier(Element.Wood)==1&&stats.MaxHpMultiplier==1&&stats.MaxInkMultiplier==1,"starter preserves baseline");
   int coins=10000;
   bool Act(EquipmentAction a,string id,EquipmentSlot slot,long revision,string request){bool ok=EquipmentTransactions.Propose(cat,gear,coins,a,id,slot,revision,request,out var next,out var balance,out _);if(ok){gear=next;coins=balance;}return ok;}
   foreach(var d in cat.Items.Where(x=>!x.Starter)){
    long revision=gear.Revision;string request="buy_"+d.Id;int money=coins;
    Check(Act(EquipmentAction.Buy,d.Id,d.Slot,revision,request)&&coins==money-d.Price,"buy "+d.Id);
    Check(!Act(EquipmentAction.Buy,d.Id,d.Slot,gear.Revision,request)&&!Act(EquipmentAction.Buy,d.Id,d.Slot,gear.Revision,"duplicate_"+d.Id),"reject double callback and duplicate ownership "+d.Id);
    Check(!Act(EquipmentAction.Equip,d.Id,d.Slot,revision,"stale_"+d.Id),"reject stale revision "+d.Id);
    Check(!Act(EquipmentAction.Equip,d.Id,(EquipmentSlot)(((int)d.Slot+1)%6),gear.Revision,"wrong_"+d.Id),"reject wrong slot "+d.Id);
    Check(Act(EquipmentAction.Equip,d.Id,d.Slot,gear.Revision,"equip_"+d.Id)&&gear.Equipped[(int)d.Slot]==d.Id,"equip "+d.Slot);
    for(int level=0;level<3;level++){money=coins;Check(Act(EquipmentAction.Upgrade,d.Id,d.Slot,gear.Revision,"upgrade_"+d.Id+level)&&coins==money-cat.UpgradeCosts[level],"upgrade "+d.Id+" to "+(level+1));}
    Check(!Act(EquipmentAction.Upgrade,d.Id,d.Slot,gear.Revision,"max_"+d.Id),"max +3 rejects "+d.Id);
    Check(Act(EquipmentAction.Unequip,null,d.Slot,gear.Revision,"remove_"+d.Id)&&gear.Has(d.Id),"unequip retains ownership "+d.Slot);
    Check(Act(EquipmentAction.Equip,d.Id,d.Slot,gear.Revision,"reequip_"+d.Id),"reequip "+d.Slot);
   }
   Check(gear.Owned.Count==8&&gear.Has("old_brush")&&gear.Has("old_robe"),"replacement retains original items without duplication");
   var empty=EquipmentState.Initial(cat);Check(!EquipmentTransactions.Propose(cat,empty,500,EquipmentAction.Buy,"pine_brush",EquipmentSlot.Brush,0,"old-price",out _,out _,out _,119),"stale price quote rejected");Check(!EquipmentTransactions.Propose(cat,empty,0,EquipmentAction.Buy,"pine_brush",EquipmentSlot.Brush,0,"poor",out _,out int unchanged,out _)&&unchanged==0,"insufficient funds unchanged");
   stats=new RuntimePlayerStats(economy,rules,gear,cat);Check(Mathf.Abs(stats.DamageMultiplier(Element.Wood)-1.30f)<.0001f&&Mathf.Abs(stats.DamageMultiplier(Element.Fire)-1.18f)<.0001f&&Mathf.Abs(stats.MaxHpMultiplier-1.21f)<.0001f&&Mathf.Abs(stats.MaxInkMultiplier-1.11f)<.0001f,"all six +3 stat totals applied once");
   economy.levels[(int)DemoUpgradeTrack.Health]=1;economy.levels[(int)DemoUpgradeTrack.Wood]=1;stats=new RuntimePlayerStats(economy,rules,gear,cat);
   Check(Mathf.Abs(stats.MaxHpMultiplier-1.31f)<.0001f&&Mathf.Abs(stats.DamageMultiplier(Element.Wood)-1.40f)<.0001f,"legacy + equipment additive against base");
   var old=WorldMacroProgress.CreateNew("fixture",Vector3.one,17);old.version=7;old.ledger.currency=123;old.campaign.Facts.Add("test:found");old.economy=economy;old.equipment=null;old.ui.discoveredMarkers.Add("fixture-place");
   var migrated=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(old)));
   Check(migrated.version==8&&migrated.ledger.currency==123&&migrated.campaign.Facts.Contains("test:found")&&migrated.economy.levels[(int)DemoUpgradeTrack.Health]==1&&migrated.equipment.Owned.Count==0,"v7 migration preserves progress; catalog initializes separately");
   migrated.equipment=gear;var reload=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(migrated)));
   Check(reload.equipment.Owned.Count==8&&reload.equipment.Level("pine_brush")==3&&reload.equipment.Equipped[(int)EquipmentSlot.Feet]=="straw_boot","v8 roundtrip preserves ownership/equip/levels");
   var campaign=s.Content.Campaign;var state=new DemoCampaignState{CampaignId=campaign.CampaignId};
   Check(!DemoCampaignProgression.TryAdvance(campaign,state,DemoEventKind.Interaction,"wangso_w1",out _,out _),"contract needs Jeongdam contact");
   Check(DemoCampaignProgression.TryAdvance(campaign,state,DemoEventKind.Interaction,"jeongdam_j1",out state,out _)&&DemoCampaignProgression.TryAdvance(campaign,state,DemoEventKind.Interaction,"wangso_w1",out state,out _),"Jeongdam then contract; no optional quest gate");
   foreach(var pair in new[]{new[]{"village_toolbox","village_tool_report"},new[]{"village_cut_trace","village_resident"}}){
    var q=new DemoCampaignState{CampaignId=campaign.CampaignId};Check(!DemoCampaignProgression.TryAdvance(campaign,q,DemoEventKind.Interaction,pair[1],out _,out _),"early report cannot manufacture discovery "+pair[0]);
    Check(DemoCampaignProgression.TryAdvance(campaign,q,DemoEventKind.Interaction,pair[0],out q,out int foundReward)&&foundReward==0,"discovery before acceptance "+pair[0]);
    Check(!q.Completed.Contains(pair[1]),"discovery cannot auto-report "+pair[0]);
    Check(DemoCampaignProgression.TryAdvance(campaign,q,DemoEventKind.Interaction,pair[1],out q,out int reward)&&reward==80,"live late report pays 80 "+pair[0]);
    Check(!DemoCampaignProgression.TryAdvance(campaign,q,DemoEventKind.Interaction,pair[1],out _,out _),"repeat report cannot repay "+pair[0]);
   }
   Check(AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>("Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset").EquipmentCatalog==null,"canonical catalog remains disabled");
   return string.Join("\n",results);
  }
  public static string VillageCapture(){
   var s=VillageSession();var ledger=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(VillageOutput+"/ledger.json"));var ground=FinalSurface(SceneManager.GetActiveScene());
   var go=new GameObject("Village245ReviewCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(s.Walker.ViewCamera);camera.enabled=false;EditorUtility.CopySerialized(s.Walker.ViewCamera.GetUniversalAdditionalCameraData(),camera.GetUniversalAdditionalCameraData());
   var art=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CompactRebuildArtRenderer>()).Single();var previousObserver=art.Observer;art.Observer=camera;
   var rt=new RenderTexture(1920,1080,24);var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);var prev=RenderTexture.active;
   Vector3 Ground(float x,float z,float y)=>ground(x,z).point+Vector3.up*y;
   var eye=new[]{Ground(2780,2240,2.5f),Ground(2710,2199,1.9f),Ground(2642,2220,30),Ground(2852,2410,1.9f)};
   var target=new[]{Ground(2700,2180,3),Ground(2693,2160,2),Ground(2700,2180,0),Ground(2840,2390,1.6f)};
   try{camera.targetTexture=rt;camera.aspect=16f/9;camera.fieldOfView=62;for(int i=0;i<eye.Length;i++){camera.transform.SetPositionAndRotation(eye[i],Quaternion.LookRotation(target[i]-eye[i]));camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();File.WriteAllBytes(VillageOutput+"/village_"+i+".png",tex.EncodeToPNG());}}
   finally{art.Observer=previousObserver;camera.targetTexture=null;RenderTexture.active=prev;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}return "Four village route/court/overview/relay captures";
  }
 }
}
