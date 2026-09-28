using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  const string RoadChecksReport="../Art/World/PineRest/road_interaction_checks.txt";
  static string RoadExit(){var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");return "Safe exit="+s.JourneySeat.TryExit();}
  static string RoadInteractionCheck(){
   var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-")||s.JourneySeated||s.Progress.escort.Stage!=DemoEscortStage.Escorting)throw new Exception("Isolated escort on foot required");
   int startCurrency=s.Progress.currency;var checks=new List<string>();void Check(bool value,string label){checks.Add((value?"PASS ":"FAIL ")+label);}
   JourneyEscortStop Stop(string id)=>s.EscortStops.Single(x=>x.PointId==id);
   void At(string id){var stop=Stop(id);s.EscortCompanion.position=stop.transform.position+stop.transform.forward*2;s.EscortCargo.position=s.EscortCompanion.position+Vector3.up*.7f;s.Teleport(stop.transform.position+stop.transform.right*1.8f+Vector3.up*1.1f,0);Physics.SyncTransforms();}
   At("checkpoint_2");s.Interact("checkpoint_2");Check(s.Progress.escort.Stage==DemoEscortStage.Escorting,"second inspection cannot precede first");
   At("cargo_delivery");s.Interact("cargo_delivery");Check(!s.Progress.escort.DeliveryRewardRecorded,"delivery cannot skip both inspections");
   At("checkpoint_1");var officer=Stop("checkpoint_1");officer.enabled=false;try{s.Interact("checkpoint_1");Check(s.Progress.escort.Stage==DemoEscortStage.Escorting,"inactive physical inspector rejects proof");}finally{officer.enabled=true;}
   s.EscortCargo.gameObject.SetActive(false);try{s.Interact("checkpoint_1");Check(s.Progress.escort.Stage==DemoEscortStage.Escorting,"missing cargo rejects inspection");}finally{s.EscortCargo.gameObject.SetActive(true);}
   var npc=s.EscortCompanion.position;s.EscortCompanion.position+=Vector3.right*30;try{s.Interact("checkpoint_1");Check(s.Progress.escort.Stage==DemoEscortStage.Escorting,"distant companion rejects inspection");}finally{s.EscortCompanion.position=npc;}
   At("checkpoint_1");
   var field=typeof(PrologueSession).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic);var repository=field.GetValue(s);string blocker=Path.GetFullPath("../Art/World/PineRest/road_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit");
   try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));s.Interact("checkpoint_1");Check(s.Progress.escort.Stage==DemoEscortStage.Escorting&&!s.Progress.completed.Contains("checkpoint_1"),"inspection IOException publishes neither stage nor completion");}finally{field.SetValue(s,repository);File.Delete(blocker);}
   s.Interact("checkpoint_1");Check(s.Progress.escort.Stage==DemoEscortStage.FirstInspectionCleared&&s.Progress.completed.Contains("checkpoint_1")&&s.Progress.currency==startCurrency+60,"first inspection commits nearby unloaded companion/cargo and campaign reward60");
   long revision=s.Progress.escort.Revision;s.Interact("checkpoint_1");Check(s.Progress.escort.Revision==revision&&s.Progress.currency==startCurrency+60,"repeat inspection duplicates neither revision nor currency");
   At("checkpoint_2");s.Interact("checkpoint_2");Check(s.Progress.escort.Stage==DemoEscortStage.SecondInspectionCleared&&s.Progress.currency==startCurrency+120,"second inspection commits in order and campaign reward60");
   At("cargo_delivery");int balance=s.Progress.currency;File.WriteAllText(blocker,"audit");
   try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));s.Interact("cargo_delivery");Check(s.Progress.escort.Stage==DemoEscortStage.SecondInspectionCleared&&s.Progress.currency==balance&&!s.Progress.completed.Contains("cargo_delivery"),"delivery IOException awards nothing");}finally{field.SetValue(s,repository);File.Delete(blocker);}
   s.Interact("cargo_delivery");Check(s.Progress.escort.Stage==DemoEscortStage.Delivered&&s.Progress.escort.DeliveryRewardRecorded&&s.Progress.currency==balance+220,"delivery awards existing campaign reward once");
   revision=s.Progress.escort.Revision;s.Interact("cargo_delivery");Check(s.Progress.escort.Revision==revision&&s.Progress.currency==balance+220,"repeat delivery cannot duplicate currency");
   var disk=((PrologueProgressStore)repository).Load();Check(disk.escort.Stage==DemoEscortStage.Delivered&&disk.currency==s.Progress.currency&&Vector3.Distance(disk.escort.CompanionFeet,s.EscortCompanion.position)<.1f,"delivery and receiver-side companion position persist together");
   s.Save();checks.Add("Direct interactions, actor audit teleport and real IOException; not driving the road, manual F or charpae presentation verification.");var report=string.Join("\n",checks);File.WriteAllText(RoadChecksReport,report);return report;
  }
  static string RoadReloadCheck(){var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");var depot=s.EscortStops.Single(x=>x.PointId=="cargo_delivery");bool pass=s.Progress.escort.Stage==DemoEscortStage.Delivered&&Vector3.Distance(s.EscortCompanion.position,depot.transform.position)<8&&Vector3.Distance(s.EscortCargo.position,depot.transform.position)<8&&s.EscortStops.Where(x=>x.Barrier!=null).All(x=>Quaternion.Angle(x.Barrier.localRotation,Quaternion.identity)>80);string result=(pass?"PASS ":"FAIL ")+"new Play restores delivered cargo/NPC at depot and opens both inspected barriers";File.WriteAllText("../Art/World/PineRest/road_reload.txt",result);return result;}
 }
}
