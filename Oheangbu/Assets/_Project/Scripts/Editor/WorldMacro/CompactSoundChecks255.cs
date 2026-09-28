using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static class CompactSoundChecks255
 {
  const string Output="../Art/Audio/Compact255/runtime-checks.txt";
  static void Check(bool ok,string text){File.AppendAllText(Output,(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  static int Count(WorldMacroUiAudioVoices voices,string id)=>voices.Played.TryGetValue(id,out int n)?n:0;
  static void Gear(PlaytestUiRoot ui,EquipmentAction action,string id,EquipmentSlot slot,long revision,int price=-1)
   =>typeof(PlaytestUiRoot).GetMethod("GearAction",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,new object[]{action,id,slot,revision,price});
  static WorldMacroPlaytestSession walking;static float walkUntil;static int stepsBefore;static string walkResult;static bool captureMuted;
  static void TickWalk()
  {
   if(walking==null||!EditorApplication.isPlaying){EditorApplication.update-=TickWalk;return;}
   if(Time.time>=walkUntil)
   {
    var audio=walking.GetComponent<CompactSoundscape255>();int after=audio.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value);
    walkResult=(after>stepsBefore?"PASS":"FAIL")+" actual CharacterController fixture movement emits footsteps: "+(after-stepsBefore)+"; not native input play";
    File.AppendAllText(Output,walkResult+"\n");EditorApplication.update-=TickWalk;walking=null;return;
   }
   var body=walking.Walker.Body;if(body.enabled)body.Move(body.transform.forward*(1.7f*Mathf.Min(Time.deltaTime,.05f))+Vector3.down*.015f);
  }
  public static string Run(string command)
  {
   var s=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();var ui=PlaytestUiRoot.Instance;
   if(!EditorApplication.isPlaying||s==null||!s.TestSaveSuffix.StartsWith("_compact_slice_"))throw new Exception("Private candidate Play required");
   var bridge=s.GetComponent<CompactSoundscape255>();var voices=Object.FindFirstObjectByType<WorldMacroUiAudioVoices>();
   string save=Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json");
   if(command=="mute-set"){var setting=Object.FindFirstObjectByType<UserSettingsService>();var value=setting.Current;value.MasterVolume=0;setting.Preview(value);return "Unsaved master-mute preview; automatic rollback remains enabled";}
   if(command=="companion")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();var npc=s.DemoEscortCompanion;if(npc==null)throw new Exception("Companion missing");
    var home=npc.position;var player=s.Walker.Body.transform.position;float yaw=s.Walker.Body.transform.eulerAngles.y;
    var presentation=Object.FindFirstObjectByType<Oheangbu.App.Demo.DemoEscortPresentation>();var oldState=presentation.State;
    var snapshot=typeof(CompactSoundscape255).GetMethod("Snapshot",BindingFlags.NonPublic|BindingFlags.Instance);var update=typeof(CompactSoundscape255).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
    try
    {
     s.Teleport(home+Vector3.right*2,0);snapshot.Invoke(bridge,null);int before=bridge.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value);
     npc.position+=Vector3.forward*.9f;update.Invoke(bridge,null);npc.position+=Vector3.forward*.9f;update.Invoke(bridge,null);
     Check(bridge.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value)==before+1,"companion displacement observer emits one footstep; transform fixture, not escort navigation");
     typeof(Oheangbu.App.Demo.DemoEscortPresentation).GetProperty("State").SetValue(presentation,Oheangbu.App.Demo.DemoEscortPresentationState.Riding);
     snapshot.Invoke(bridge,null);before=bridge.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value);
     npc.position+=Vector3.forward*.9f;update.Invoke(bridge,null);npc.position+=Vector3.forward*.9f;update.Invoke(bridge,null);
     Check(bridge.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value)==before,"riding companion does not make walking sounds");
    }
    finally{npc.position=home;typeof(Oheangbu.App.Demo.DemoEscortPresentation).GetProperty("State").SetValue(presentation,oldState);s.Teleport(player,yaw);snapshot.Invoke(bridge,null);}
    return File.ReadAllText(Output);
   }
   if(command=="walk-start"){ui.CloseMenu();ui.Gate.ReleaseImmediately();s.Teleport(s.Content.StartFeet,0);walking=s;walkUntil=Time.time+6;stepsBefore=bridge.Counts.Where(p=>p.Key.StartsWith("step_")).Sum(p=>p.Value);walkResult="pending";EditorApplication.update+=TickWalk;return "Six-second physics walk SFX fixture started";}
   if(command=="walk-result")return walkResult;
   if(command=="ui-success")
   {
    File.WriteAllText(Output,"Automated private-save SFX event fixtures; not native/manual play or user listening approval.\n");
    Check(Application.isFocused,"Game View focused");
    foreach(var enemy in s.Actors)enemy.gameObject.SetActive(false);
    s.Progress.ledger.currency=1000;s.SaveNow(out _);
    Check(CompactRebuildAuthoring.VillageRuntime("shop").Contains("interact=True"),"physical shop interaction opens service");
    var item=s.Content.EquipmentCatalog.Items.Single(i=>i.Id=="pine_brush");
    int before=Count(voices,"purchase"), confirm=Count(voices,"ui_confirm");
    Gear(ui,EquipmentAction.Buy,item.Id,item.Slot,s.Progress.equipment.Revision,item.Price);
    Check(s.Progress.equipment.Has(item.Id)&&Count(voices,"purchase")==before+1&&Count(voices,"ui_confirm")==confirm,"saved purchase emits exactly one purchase cue, no page-confirm duplicate");
    before=Count(voices,"equip");Gear(ui,EquipmentAction.Equip,item.Id,item.Slot,s.Progress.equipment.Revision);
    Check(s.Progress.equipment.Equipped[0]==item.Id&&Count(voices,"equip")==before+1,"saved equip emits equipment cue");
    Check(CompactRebuildAuthoring.VillageRuntime("forge").Contains("interact=True"),"physical artisan interaction opens forge");
    before=Count(voices,"upgrade");Gear(ui,EquipmentAction.Upgrade,item.Id,item.Slot,s.Progress.equipment.Revision,40);
    Check(s.Progress.equipment.Level(item.Id)==1&&Count(voices,"upgrade")==before+1,"saved upgrade emits artisan cue");
    var disk=JsonUtility.FromJson<WorldMacroProgress>(File.ReadAllText(save));Check(disk.equipment.Level(item.Id)==1&&disk.ledger.currency==840,"audio follows persisted purchase and upgrade");
   }
   else if(command=="ui-error")
   {
    ui.OpenPage("소지품");var drag=ui.GetComponentsInChildren<EquipmentDragItem>().Single(d=>d.ItemId=="pine_brush");
    var slot=ui.GetComponentsInChildren<EquipmentDropSlot>().Single(d=>d.Slot==EquipmentSlot.Head);
    string snapshot=JsonUtility.ToJson(s.Progress);int before=Count(voices,"ui_error"),equip=Count(voices,"equip");
    var e=new PointerEventData(EventSystem.current){pointerDrag=drag.gameObject,position=Vector2.one*400};
    drag.OnBeginDrag(e);slot.OnDrop(e);drag.OnEndDrag(e);
    Check(snapshot==JsonUtility.ToJson(s.Progress)&&Count(voices,"ui_error")==before+1&&Count(voices,"equip")==equip,"wrong-slot actual drop handler emits rejection only and preserves state");
    Check(Count(voices,"ui_drag")>0&&Count(voices,"ui_drop")>0,"equipment drag and release foley attached");
    Check(ui.GetComponentsInChildren<CompactUiSound255>().All(x=>!(x is IBeginDragHandler)),"ordinary UI sound relay does not intercept ScrollRect dragging");
   }
   else if(command=="save-error")
   {
    string snapshot=JsonUtility.ToJson(s.Progress);byte[] disk=File.ReadAllBytes(save);int error=Count(voices,"ui_error"),accepted=Count(voices,"unequip");
    using(var locked=new FileStream(save+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None))
     Gear(ui,EquipmentAction.Unequip,"pine_brush",EquipmentSlot.Brush,s.Progress.equipment.Revision);
    Check(snapshot==JsonUtility.ToJson(s.Progress)&&disk.SequenceEqual(File.ReadAllBytes(save)),"locked save preserves currency, gear and disk");
    Check(Count(voices,"ui_error")==error+1&&Count(voices,"unequip")==accepted,"failed save plays rejection, never success sound");
   }
   else if(command=="world")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();
    int before=bridge.Counts.TryGetValue("misfire",out int n)?n:0;
    Object.FindFirstObjectByType<BrushStrokeFeedAdapter>().NotifyCastFailed();
    Check(bridge.Counts.TryGetValue("misfire",out n)&&n==before+1,"rejected-cast presentation event reaches world audio");
    float old=Time.timeScale;try{Time.timeScale=0;Check(!bridge.Emit("land",s.Walker.Body.transform.position),"paused world rejects new cues");int c=Count(voices,"ui_select");ui.PlayNamedSound("ui_select");Check(Count(voices,"ui_select")==c+1,"menu cue remains available while paused");}finally{Time.timeScale=old;}
    Check(Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a=>a.name.StartsWith("CompactSound255_"))==16,"world voices remain bounded at 16");
   }
   else if(command=="probe-start"||command=="probe-mute")
   {
    ui.CloseMenu();ui.Gate.ReleaseImmediately();var listener=s.Walker.ViewCamera.GetComponent<AudioListener>();
    Check(listener!=null&&listener.enabled,"player camera owns active listener");
    var probe=listener.GetComponent<WorldMacroAudioOutputProbe>();if(probe==null)probe=listener.gameObject.AddComponent<WorldMacroAudioOutputProbe>();
    if(command=="probe-mute"){var setting=Object.FindFirstObjectByType<UserSettingsService>();var value=setting.Current;value.MasterVolume=0;setting.Preview(value);}
    captureMuted=Object.FindFirstObjectByType<UserSettingsService>().Current.MasterVolume==0;Check(command!="probe-mute"||captureMuted,"requested capture mute state is active");probe.Arm(AudioSettings.outputSampleRate,3);bridge.Emit("land",s.Walker.Body.transform.position,.7f);ui.PlayNamedSound("ui_confirm",.5f);return "Post-mix output capture armed for 3 seconds";
   }
   else if(command=="probe-end")
   {
    var probe=Object.FindFirstObjectByType<WorldMacroAudioOutputProbe>();if(probe==null||!probe.StopAndCopy(out var samples))throw new Exception("Probe unavailable; retry after callback");
    float peak=samples.Length>0?samples.Max(x=>Mathf.Abs(x)):0;double rms=samples.Length>0?Math.Sqrt(samples.Sum(x=>(double)x*x)/samples.Length):0;
    if(captureMuted)Object.FindFirstObjectByType<UserSettingsService>().Revert();
    Check(samples.Length>0&&(captureMuted?peak<=Mathf.Pow(10,WorldMacroAudioMixProfileSO.VolumeDb(0)/20f):peak>0&&peak<.95),(captureMuted?"master mute stays below existing -80dB mixer floor":"post-mix contains signal without clipping")+"; peak="+peak+" rms="+rms+" samples="+samples.Length);
    Object.Destroy(probe);
   }
   else throw new ArgumentException(command);
   return File.ReadAllText(Output);
  }
 }
}
