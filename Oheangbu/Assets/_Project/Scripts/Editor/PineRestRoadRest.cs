using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static string RoadRestAuthor(){
   var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
   var s=Object.FindFirstObjectByType<PrologueSession>();var ground=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();var points=s.Content.Points.ToList();
   foreach(var stop in s.EscortStops.Where(x=>x.Barrier!=null)){
    string id=stop.PointId+"_rest";if(points.Any(x=>x.Id==id))continue;var site=stop.transform.parent;var p=site.TransformPoint(new Vector3(3,0,-14));
    if(!ground.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out var hit,200))throw new Exception("Rest support absent");p=hit.point;
    points.Add(new PrologueContentSO.Point{Id=id,Kind=PrologueInteractionKind.Rest,Position=p,Radius=2.5f,RequiredCompleted=new[]{stop.PointId}});
    var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);marker.name="Road rest stone "+id;marker.transform.SetParent(site);marker.transform.position=p+site.right*1.1f+Vector3.up*.2f;marker.transform.localScale=new Vector3(.55f,.2f,.55f);marker.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/CodexWorld/Materials/PaleStone.mat");
   }
   s.Content.Points=points.ToArray();EditorUtility.SetDirty(s.Content);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Two inspected-road rest points authored; cargo and parked vehicle required during escort";
  }
  static PrologueSession RoadRestSession(){var s=Object.FindFirstObjectByType<PrologueSession>();if(!EditorApplication.isPlaying||s==null||!s.TestSaveSuffix.StartsWith("-audit-"))throw new Exception("Isolated Play required");return s;}
  static string RoadRestPrepare(){
   var s=RoadRestSession();if(s.JourneySeated||s.Progress.escort.Stage!=DemoEscortStage.Escorting)throw new Exception("Active unloaded escort required");
   if(s.Progress.dropCurrency>0){s.Teleport(s.Progress.dropPosition,0);s.Interact("CurrencyDrop");}
   var stop=s.EscortStops.Single(x=>x.PointId=="checkpoint_1");var rest=s.Content.Points.Single(x=>x.Id=="checkpoint_1_rest");
   s.Teleport(rest.Position+Vector3.up*1.1f,0);string previous=s.Progress.checkpoint;s.Interact(rest.Id);if(s.Progress.checkpoint!=previous)throw new Exception("Rest skipped inspection gate");
   s.EscortCompanion.position=stop.transform.position+stop.transform.forward*2;s.EscortCargo.position=s.EscortCompanion.position+Vector3.up*.7f;s.Teleport(stop.transform.position+stop.transform.right*1.8f+Vector3.up*1.1f,0);s.Interact(stop.PointId);
   if(s.Progress.escort.Stage!=DemoEscortStage.FirstInspectionCleared)throw new Exception("Inspection preparation failed");
   var p=rest.Position-stop.transform.parent.right*4;var ground=GameObject.Find("Capital road ground").GetComponent<MeshCollider>();ground.Raycast(new Ray(p+Vector3.up*100,Vector3.down),out var hit,200);
   typeof(PrologueSession).GetMethod("SetJourneyVehiclePose",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(s,new object[]{hit.point+Vector3.up*.15f,stop.transform.parent.rotation});
   s.EscortCompanion.position=rest.Position+Vector3.right*2;s.EscortCargo.position=s.EscortCompanion.position+Vector3.up*.6f;s.Teleport(rest.Position+Vector3.up*1.1f,0);Physics.SyncTransforms();return "Audit actors/vehicle placed at first inspected rest; wait for real wheel contacts";
  }
  static string RoadRestCheck(){
   var s=RoadRestSession();var rest=s.Content.Points.Single(x=>x.Id=="checkpoint_1_rest");var checks=new List<string>();void Check(bool pass,string label)=>checks.Add((pass?"PASS ":"FAIL ")+label);
   Check(s.JourneySeat.Vehicle.GroundedWheelCount>=3,"parked checkpoint vehicle settles on real wheel contacts");
   string old=s.Progress.checkpoint;var cargo=s.EscortCargo.position;s.EscortCargo.position+=Vector3.right*30;try{s.Interact(rest.Id);Check(s.Progress.checkpoint==old&&!s.Progress.hasEscortCheckpoint,"distant cargo cannot split player and escort checkpoint");}finally{s.EscortCargo.position=cargo;}
   var field=typeof(PrologueSession).GetField("store",BindingFlags.NonPublic|BindingFlags.Instance);var repository=(PrologueProgressStore)field.GetValue(s);string blocker=Path.GetFullPath("../Art/World/PineRest/rest_block_"+Guid.NewGuid().ToString("N"));File.WriteAllText(blocker,"audit");
   try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));s.Interact(rest.Id);Check(s.Progress.checkpoint==old&&!s.Progress.hasEscortCheckpoint,"rest IOException publishes no player or escort checkpoint");}finally{field.SetValue(s,repository);File.Delete(blocker);}
   s.Interact(rest.Id);Check(s.Progress.checkpoint==rest.Id&&s.Progress.hasEscortCheckpoint&&s.Progress.escort.CheckpointId==rest.Id,"rest atomically commits matching player and escort checkpoint IDs");
   if(!s.Progress.hasEscortCheckpoint)throw new Exception("Rest rejected; cannot verify death recovery");
   var vehicle=s.Progress.escortCheckpointVehicle;var person=s.Progress.escort.CheckpointFeet;var box=s.Progress.escortCheckpointCargo;int balance=s.Progress.currency;
   Check(Vector3.Distance(vehicle,s.JourneySeat.Vehicle.Body.position)<.1f&&Vector3.Distance(person,rest.Position)<5,"safe companion and actual parked vehicle poses recorded");
   s.Teleport(rest.Position+Vector3.forward*14+Vector3.up*1.1f,0);var death=s.Player.position;
   File.WriteAllText(blocker,"audit");try{field.SetValue(s,new PrologueProgressStore(Path.Combine(blocker,"state.json")));s.Player.GetComponent<Oheangbu.Combat.PlayerVitals>().ApplyFatalFall();Check(Vector3.Distance(s.Player.position,death)<.1f&&s.Progress.currency==balance,"death IOException defers respawn and currency publication");}finally{field.SetValue(s,repository);File.Delete(blocker);}
   s.Save();
   Check(Vector3.Distance(s.Player.position,s.Progress.checkpointPosition)<.1f&&Vector3.Distance(s.EscortCompanion.position,person)<.1f&&Vector3.Distance(s.EscortCargo.position,box)<.1f&&Vector3.Distance(s.JourneySeat.Vehicle.Body.position,vehicle)<.1f,"death retry restores player NPC cargo and vehicle together at road rest");
   Check(s.Progress.escort.Stage==DemoEscortStage.FirstInspectionCleared&&s.Progress.currency==0&&s.Progress.dropCurrency==balance&&balance>0&&Vector3.Distance(s.Progress.dropPosition,death)<.1f,"recovery retains inspection and leaves currency at death site");
   var disk=repository.Load();Check(disk.hasEscortCheckpoint&&disk.checkpoint==rest.Id&&disk.escort.CheckpointId==rest.Id,"joint recovery survives disk load");
   checks.Add("Actual wheel contacts and IOException; audit actor/vehicle placement and direct rest/death calls, not a driven route or manual input.");string result=string.Join("\n",checks);File.WriteAllText("../Art/World/PineRest/road_rest_checks.txt",result);return result;
  }
  static string RoadRestReload(){var s=RoadRestSession();bool pass=s.Progress.hasEscortCheckpoint&&s.Progress.checkpoint=="checkpoint_1_rest"&&Vector3.Distance(s.Player.position,s.Progress.checkpointPosition)<2&&Vector3.Distance(s.EscortCompanion.position,s.Player.position)<8&&Vector3.Distance(s.JourneySeat.Vehicle.transform.position,s.Progress.escortCheckpointVehicle)<.3f;string report=(pass?"PASS ":"FAIL ")+"new Play restores first road checkpoint and nearby escort vehicle";File.WriteAllText("../Art/World/PineRest/road_rest_reload.txt",report);return report;}
 }
}
