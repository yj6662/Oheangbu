using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using Oheangbu.Core.Domain;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro {
 public static partial class CompactRebuildAuthoring {
  static readonly List<string> villageRuntimeResults=new List<string>();
  static void VCheck(bool ok,string text){villageRuntimeResults.Add((ok?"PASS ":"FAIL ")+text);File.WriteAllText(VillageOutput+"/runtime.txt",string.Join("\n",villageRuntimeResults));if(!ok)throw new Exception(text);}
  static bool ApproachVillage(string id){var s=VillageSession();var ui=PlaytestUiRoot.Instance;ui.CloseMenu();ui.Gate.ReleaseImmediately();var p=s.Content.Points.Single(x=>x.Id==id);
   float approach=Mathf.Clamp(p.Radius*.75f,.6f,2f);
   for(int i=0;i<16;i++){float a=i*Mathf.PI/8;var q=p.Position+new Vector3(Mathf.Sin(a)*approach,0,Mathf.Cos(a)*approach);if(!NavMesh.SamplePosition(q,out var hit,2,NavMesh.AllAreas))continue;s.Teleport(hit.position+Vector3.up*.08f,Quaternion.LookRotation(p.Position-hit.position).eulerAngles.y);Physics.SyncTransforms();if(s.CanInteract(id))return true;}return false;}
  public static string VillageRuntime(string command){
   var s=VillageSession();if(!EditorApplication.isPlaying||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private slice-start required");
   var ui=PlaytestUiRoot.Instance;string file=System.IO.Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   if(command=="ui-debug")return "page="+ui.Page+" focus="+Application.isFocused+" screen="+Screen.width+"x"+Screen.height+" cameraRT="+s.Walker.ViewCamera.targetTexture+"\n"+string.Join("\n",Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(c=>c.name+" enabled="+c.enabled+" active="+c.gameObject.activeInHierarchy+" mode="+c.renderMode+" display="+c.targetDisplay+" rect="+((RectTransform)c.transform).rect+" scale="+c.transform.lossyScale))+"\n"+string.Join("\n",ui.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(t=>t.name=="SelectedGearTitle"||t.name=="ServiceSpeaker").Select(t=>t.text+" active="+t.gameObject.activeInHierarchy));
   if(command.StartsWith("capture:")){string label=command.Substring(8);ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(VillageOutput+"/"+label+".png"));return "Capture queued "+label;}
   if(command=="inventory"){ui.OpenPage("소지품");Canvas.ForceUpdateCanvases();return "Inventory open";}
   if(command=="select-service"){
    ui.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="ServiceGear_pine_brush").onClick.Invoke();Canvas.ForceUpdateCanvases();return "Selected saved pine brush";
   }
   if(command=="shop"||command=="forge"||command=="rest"){
    string id=command=="shop"?"village_shop":command=="forge"?"village_artisan":"village_rest";bool near=ApproachVillage(id);bool ok=near&&s.Interact(id);return "approach="+near+" interact="+ok+" focus="+Application.isFocused+" rest="+s.RestPresentationActive;
   }
   if(command=="ui-check"){
    ui.OpenPage("소지품");Canvas.ForceUpdateCanvases();
    VCheck(ui.GetComponentsInChildren<EquipmentInkGraphic>().All(g=>g.canvasRenderer!=null),"all gear graphics own CanvasRenderer");
    var button=ui.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="Gear_pine_brush");button.onClick.Invoke();Canvas.ForceUpdateCanvases();
    VCheck(ui.GetComponentsInChildren<TMPro.TMP_Text>().Any(t=>t.name=="SelectedGearTitle"&&t.text.Contains("송연필")),"click selects item and displays its actual saved upgrade");
    var drag=ui.GetComponentsInChildren<EquipmentDragItem>().Single(d=>d.ItemId=="pine_brush");var drop=ui.GetComponentsInChildren<EquipmentDropSlot>().Single(d=>d.Slot==EquipmentSlot.Head);
    string equipmentBefore=JsonUtility.ToJson(s.Progress.equipment);var ev=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){pointerDrag=drag.gameObject,position=new Vector2(500,500)};
    drag.OnBeginDrag(ev);drop.OnDrop(ev);drag.OnEndDrag(ev);VCheck(equipmentBefore==JsonUtility.ToJson(s.Progress.equipment),"wrong-slot drag through UI returns without mutation");
    var equip=ui.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name=="GearEquip");equip.onClick.Invoke();VCheck(string.IsNullOrEmpty(s.Progress.equipment.Equipped[0]),"selected equipped item button unequips");
    drag=ui.GetComponentsInChildren<EquipmentDragItem>().Single(d=>d.ItemId=="pine_brush");drop=ui.GetComponentsInChildren<EquipmentDropSlot>().Single(d=>d.Slot==EquipmentSlot.Brush);ev.pointerDrag=drag.gameObject;drag.OnBeginDrag(ev);drop.OnDrop(ev);drag.OnEndDrag(ev);
    VCheck(s.Progress.equipment.Equipped[0]=="pine_brush"&&s.Progress.equipment.Owned.Count==8,"valid UI drag equips without duplicating item");Canvas.ForceUpdateCanvases();return string.Join("\n",villageRuntimeResults);
   }
   if(command=="combat-check"){
    ui.CloseMenu();ui.Gate.ReleaseImmediately();var foot=FinalSurface(SceneManager.GetActiveScene())(2700,2180).point;s.Teleport(foot,0);
    var enemy=new GameObject("Temporary245DamageTarget");enemy.transform.position=foot+Vector3.forward*2;var collider=enemy.AddComponent<CapsuleCollider>();collider.center=Vector3.up*.9f;collider.height=1.8f;collider.radius=.25f;var target=enemy.AddComponent<EnemyVitals>();target.Restore();Physics.SyncTransforms();
    try{var wiring=s.Walker.Wiring;var type=wiring.GetType();var plan=new Oheangbu.App.CastPlan{Cast=new Oheangbu.Spellcraft.SpellCast('ㄱ',(Oheangbu.Spellcraft.SpellKind)0,Element.Wood,10,default,1)};
     var hit=(Oheangbu.App.PlannedHit)type.GetMethod("Schedule",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(wiring,new object[]{plan,target,Time.time-.1f,10f});
     VCheck(Mathf.Abs(hit.Power-11.4f)<.0001f,"real cast scheduler snapshots 10 x 1.14 once");
     VCheck(s.TryEquipment(EquipmentAction.Unequip,null,EquipmentSlot.Brush,s.Progress.equipment.Revision,"snapshot-remove",out _),"equipment can change after scheduling");
     float hp=target.Hp;type.GetMethod("TickPendingCasts",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(wiring,null);
     VCheck(Mathf.Abs((hp-target.Hp)-11.4f)<.0001f,"actual enemy HP loses saved 11.4 after brush unequip");
     VCheck(s.TryEquipment(EquipmentAction.Equip,"pine_brush",EquipmentSlot.Brush,s.Progress.equipment.Revision,"snapshot-restore",out _),"brush restored after snapshot test");
    }finally{Object.Destroy(enemy);}return string.Join("\n",villageRuntimeResults);
   }
   if(command=="rest-verify"){
    var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file));VCheck(!s.RestPresentationActive&&s.VillageRestPresentation.CompletedCount>0&&s.Progress.ledger.checkpoint=="village_rest","guesthouse rest completes and returns control");
    VCheck(s.Progress.equipment.Owned.Count==8&&s.Progress.equipment.Level("pine_brush")==1&&disk.equipment.Level("pine_brush")==1,"guesthouse rest preserves equipped gear and upgrades on disk");return string.Join("\n",villageRuntimeResults);
   }
   if(command=="rest-state")return "focus="+Application.isFocused+" elapsed="+s.VillageRestPresentation.Elapsed+" active="+s.RestPresentationActive+" completed="+s.VillageRestPresentation.CompletedCount+" page="+ui.Page+" checkpoint="+s.Progress.ledger.checkpoint;
   if(command=="durability"){
    ui.CloseMenu();ui.Gate.ReleaseImmediately();ApproachVillage("village_artisan");s.Interact("village_artisan");
    foreach(var action in new[]{EquipmentAction.Upgrade,EquipmentAction.Unequip}){
     string snapshot=JsonUtility.ToJson(s.Progress);byte[] primary=File.ReadAllBytes(file);float damage=s.Walker.Wiring.PlayerDamageScale(Element.Wood);
     using(var locked=new FileStream(file+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){
      VCheck(!s.TryEquipment(action,"pine_brush",EquipmentSlot.Brush,s.Progress.equipment.Revision,"failed-"+action,out _),"locked save rejects "+action);
      VCheck(snapshot==JsonUtility.ToJson(s.Progress)&&primary.SequenceEqual(File.ReadAllBytes(file))&&damage==s.Walker.Wiring.PlayerDamageScale(Element.Wood),"failed "+action+" preserves live/disk/stats");
     }
    }
    ui.CloseMenu();ui.Gate.ReleaseImmediately();string gear=JsonUtility.ToJson(s.Progress.equipment);var facts=s.Progress.campaign.Facts.ToArray();
    s.Walker.Body.GetComponent<PlayerVitals>().TakeDamage(float.MaxValue);
    VCheck(gear==JsonUtility.ToJson(s.Progress.equipment)&&facts.All(f=>s.Progress.campaign.Facts.Contains(f)),"lethal damage and checkpoint recovery retain all gear and facts");
    VCheck(s.Walker.Body.GetComponent<PlayerVitals>().Hp01>0&&Vector3.Distance(s.Walker.Body.transform.position,s.Content.Checkpoints.Single(c=>c.Id=="village_rest").Feet)<2,"death returns alive to saved guesthouse checkpoint");
    var saved=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file));VCheck(gear==JsonUtility.ToJson(saved.equipment),"death recovery stores unchanged equipment");
    return string.Join("\n",villageRuntimeResults);
   }
   if(command=="reload"){
    var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(file));VCheck(s.LoadStatus=="primary"&&JsonUtility.ToJson(s.Progress.equipment)==JsonUtility.ToJson(disk.equipment),"restart loads accepted equipment revision/levels/slots");VCheck(s.Progress.campaign.Facts.Contains("delivered:village_toolbox")&&s.Progress.campaign.Facts.Contains("reported:village_cut_trace"),"discovery and direct-report facts survive restart");return string.Join("\n",villageRuntimeResults);
   }
   if(command!="check")throw new ArgumentException(command);
   villageRuntimeResults.Clear();VCheck(s.EquipmentReady&&s.Progress.version==8,"fresh candidate starts with durable v8 equipment");
   foreach(var actor in s.Actors)actor.gameObject.SetActive(false);
   VCheck(ApproachVillage("wangso_w1")&&s.Interact("wangso_w1")&&!s.Progress.campaign.Completed.Contains("cargo_contract"),"Wangso first greeting works without contract");
   VCheck(ApproachVillage("village_toolbox")&&s.Interact("village_toolbox")&&s.Progress.campaign.Facts.Contains("evidence:village_toolbox"),"pre-acceptance toolbox discovery commits");
   VCheck(ApproachVillage("village_cut_trace")&&s.Interact("village_cut_trace"),"pre-acceptance cut trace commits without clearing enemies");
   int before=s.Progress.ledger.currency;VCheck(ApproachVillage("village_resident")&&s.Interact("village_resident")&&s.Progress.ledger.currency==before+80,"resident late report pays exactly 80");
   before=s.Progress.ledger.currency;ApproachVillage("village_resident");s.Interact("village_resident");VCheck(s.Progress.ledger.currency==before,"resident repeat does not repay");
   VCheck(ApproachVillage("jeongdam_j1")&&s.Interact("jeongdam_j1")&&s.Progress.campaign.Facts.Contains("met:jeongdam"),"relay Jeongdam contact");
   VCheck(ApproachVillage("wangso_w1")&&s.Interact("wangso_w1")&&s.Progress.campaign.Completed.Contains("cargo_contract"),"return to Wangso physically accepts contract");
   VCheck(ApproachVillage("village_artisan")&&s.Interact("village_artisan")&&ui.Page=="장비 강화","artisan F opens gear forge");
   before=s.Progress.ledger.currency;VCheck(s.DeliverVillageToolbox(out _)&&s.Progress.ledger.currency==before+80,"direct toolbox handover pays exactly 80");VCheck(!s.DeliverVillageToolbox(out _)&&s.Progress.ledger.currency==before+80,"toolbox cannot pay twice");
   // Only this isolated diagnostic slot receives test funds.
   s.Progress.ledger.currency=10000;VCheck(s.SaveNow(out _),"fixture funds persisted in private slot");
   VCheck(ApproachVillage("village_shop")&&s.Interact("village_shop")&&ui.Page=="장비 상점","shop F opens separate merchant page");
   var vitals=s.Walker.Body.GetComponent<PlayerVitals>();var ink=(InkPool)typeof(WorldMacroPlaytestSession).GetField("ink",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);vitals.Restore(.37f);ink.Restore(.29f);
   var d=s.Content.EquipmentCatalog.Find("pine_brush");long revision=s.Progress.equipment.Revision;string bytes=JsonUtility.ToJson(s.Progress);var diskBytes=File.ReadAllBytes(file);
   using(var locked=new FileStream(file+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){
    VCheck(!s.TryEquipment(EquipmentAction.Buy,d.Id,d.Slot,revision,"locked-buy",out _),"locked full-save rejects purchase");
    VCheck(JsonUtility.ToJson(s.Progress)==bytes&&File.ReadAllBytes(file).SequenceEqual(diskBytes),"failed purchase changes neither live state nor primary bytes");
   }
   VCheck(s.TryEquipment(EquipmentAction.Buy,d.Id,d.Slot,revision,"locked-buy",out _),"same failed request retries once after storage recovers");
   before=s.Progress.ledger.currency;VCheck(!s.TryEquipment(EquipmentAction.Buy,d.Id,d.Slot,revision,"locked-buy",out _)&&s.Progress.ledger.currency==before,"duplicate request cannot charge twice");
   foreach(var item in s.Content.EquipmentCatalog.Items.Where(x=>!x.Starter)){
    if(!s.Progress.equipment.Has(item.Id))VCheck(s.TryEquipment(EquipmentAction.Buy,item.Id,item.Slot,s.Progress.equipment.Revision,"runtime-buy-"+item.Id,out _),"runtime purchase "+item.Slot);
    VCheck(s.TryEquipment(EquipmentAction.Equip,item.Id,item.Slot,s.Progress.equipment.Revision,"runtime-equip-"+item.Id,out _),"runtime equip "+item.Slot);
   }
   VCheck(Mathf.Abs(vitals.Hp01-.37f)<.0001f&&Mathf.Abs(ink.Value-.29f)<.0001f,"HP/ink ratios unchanged by maximum increases");
   VCheck(Mathf.Abs(s.Walker.Wiring.PlayerDamageScale(Element.Wood)-1.12f)<.0001f&&Mathf.Abs(s.Walker.Wiring.PlayerDamageScale(Element.Fire)-1.06f)<.0001f,"live combat scaling once: wood 1.12 / fire 1.06");
   VCheck(ApproachVillage("village_artisan")&&s.Interact("village_artisan"),"reach forge after shopping");
   VCheck(s.TryEquipment(EquipmentAction.Upgrade,"pine_brush",EquipmentSlot.Brush,s.Progress.equipment.Revision,"runtime-upgrade",out _),"runtime gear-specific upgrade commits");
   VCheck(Mathf.Abs(s.Walker.Wiring.PlayerDamageScale(Element.Wood)-1.14f)<.0001f,"upgraded live damage scaling 1.14");
   VCheck(s.Progress.ledger.currency==10000-500-40,"six purchases and one upgrade charge 540 total");
   ui.CloseMenu();ui.Gate.ReleaseImmediately();s.SaveNow(out _);return string.Join("\n",villageRuntimeResults);
  }
  public static string VillageWalk(){
   if(EditorApplication.isPlaying)throw new Exception("Edit required");var s=VillageSession();var source=s.Walker.Body;var ledger=JsonUtility.FromJson<VillageLedger245>(File.ReadAllText(VillageOutput+"/ledger.json"));
   var scene=SceneManager.GetActiveScene();var ground=FinalSurface(scene);var disabled=s.Actors.Select(x=>x.gameObject).Concat(new[]{s.Walker.gameObject}).Where(x=>x.activeSelf).ToArray();foreach(var g in disabled)g.SetActive(false);
   var results=new List<string>();var probe=new GameObject("VillageWalkProbe");var cc=probe.AddComponent<CharacterController>();cc.height=source.height;cc.radius=source.radius;cc.center=source.center;cc.slopeLimit=source.slopeLimit;cc.stepOffset=source.stepOffset;cc.skinWidth=source.skinWidth;cc.minMoveDistance=0;
   try{
    var cave=JsonUtility.FromJson<CaveLayoutData>(File.ReadAllText(Output+"/CaveV4/geometry.json"));
    var continuous=new List<Vector3>{s.Content.StartFeet};
    foreach(var visit in new[]{cave.evidence+Vector3.left*1.6f,cave.recess.Last()+Vector3.left*1.8f,cave.main[5],cave.satchel+Vector3.right*1.6f,cave.main[6],cave.main.Last(),s.Content.InnCheckpointFeet,ledger.main[0]}){
     if(!NavMesh.SamplePosition(continuous.Last(),out var from,3,NavMesh.AllAreas)||!NavMesh.SamplePosition(visit,out var to,3,NavMesh.AllAreas))throw new Exception("Continuous route endpoint missing");
     var nav=new NavMeshPath();if(!NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,nav)||nav.status!=NavMeshPathStatus.PathComplete)throw new Exception("Continuous route disconnected");continuous.AddRange(nav.corners.Skip(1));
    }
    continuous.AddRange(ledger.main.Skip(1));var complete=continuous.ToArray();
    var restPoint=s.Content.Points.Single(p=>p.Id=="village_rest");var restCp=s.Content.Checkpoints.Single(c=>c.Id=="village_rest");var toward=restCp.Feet-restPoint.Position;toward.y=0;
    NavMesh.SamplePosition(restPoint.Position+toward.normalized*2.7f,out var restHit,2,NavMesh.AllAreas);var restPath=new NavMeshPath();
    if(!NavMesh.CalculatePath(ledger.main.Last(),restHit.position,NavMesh.AllAreas,restPath)||restPath.status!=NavMeshPathStatus.PathComplete)throw new Exception("Rest doorstep disconnected");
    foreach(var route in new[]{ledger.main,ledger.branch,ledger.forest,complete,restPath.corners}){
     cc.enabled=false;probe.transform.position=route[0]+Vector3.up*.12f;cc.enabled=true;Physics.SyncTransforms();int target=1,steps=0,stalled=0;float vertical=0,distance=0;
     while(target<route.Length&&steps++<40000){var old=probe.transform.position;var delta=route[target]-old;delta.y=0;if(delta.magnitude<.32f){target++;continue;}vertical=cc.isGrounded?-1:Mathf.Max(-30,vertical-.1962f);cc.Move(Vector3.ClampMagnitude(delta,.09f)+Vector3.up*(vertical*.02f));float moved=Vector3.Distance(old,probe.transform.position);distance+=moved;stalled=moved<.0001f?stalled+1:0;if(stalled>300)break;}
     bool reached=target>=route.Length;results.Add((reached?"PASS":"FAIL")+" controller route "+(route==ledger.main?"main":route==ledger.branch?"evidence":route==ledger.forest?"forest":route==complete?"cave_inn_relay_village":"guesthouse_door")+" distance="+distance+" simulated="+steps*.02f+" end="+probe.transform.position+" target="+route[Math.Min(target,route.Length-1)]);
    }
    foreach(var p in s.Content.Points.Where(p=>p.Id.StartsWith("village_")||p.Id=="jeongdam_j1"||p.Id=="wangso_w1")){
     Vector3 approach=p.Position;
     if(p.Kind==PrologueInteractionKind.Rest){var cp=s.Content.Checkpoints.Single(c=>c.Id==p.Id);var d=cp.Feet-p.Position;d.y=0;approach=p.Position+d.normalized*2.7f;approach.y=ground(approach.x,approach.z).point.y;}
     bool found=NavMesh.SamplePosition(approach,out var hit,1.5f,NavMesh.AllAreas);var path=new NavMeshPath();bool connected=found&&NavMesh.CalculatePath(ledger.main.Last(),hit.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete;
     results.Add((connected?"PASS":"FAIL")+" village navigation to "+p.Id+" approach="+approach+" found="+found+" hit="+hit.position+" path="+path.status);
    }
   }finally{Object.DestroyImmediate(probe);foreach(var g in disabled)g.SetActive(true);Physics.SyncTransforms();}
   File.WriteAllText(VillageOutput+"/walk.txt",string.Join("\n",results));return string.Join("\n",results);
  }
 }
}
