using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App.Prologue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace Oheangbu.EditorTools {
 public static partial class PineRestGameBuilder {
  static Keyboard interactionKeyboard;
  static double interactionNext,interactionDeadline;
  static int interactionStep,interactionBefore;
  static readonly List<string> interactionChecks=new List<string>();
  const string InteractionReport="../Art/World/PineRest/interaction_input_check.txt";
  static string InteractionCheck(){
   var session=UnityEngine.Object.FindFirstObjectByType<PrologueSession>();
   if(!EditorApplication.isPlaying||session?.Progress==null||!session.TestSaveSuffix.StartsWith("-audit-")||interactionKeyboard!=null)throw new Exception("Fresh isolated Play required");
   if(session.Progress.completed.Contains("WorkerSatchel")||session.Progress.completed.Contains("j1:accepted"))throw new Exception("Fresh audit save required");
   interactionChecks.Clear();interactionStep=0;interactionNext=EditorApplication.timeSinceStartup;interactionDeadline=interactionNext+30;
   interactionKeyboard=InputSystem.AddDevice<Keyboard>("JourneyInteractionAudit");EditorApplication.update+=InteractionTick;File.WriteAllText(InteractionReport,"RUNNING");return "Virtual F input audit running";
  }
  static void InteractionTick(){
   try{
    if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup>interactionDeadline)throw new Exception("Stopped or timed out");
    if(EditorApplication.timeSinceStartup<interactionNext)return;interactionNext=EditorApplication.timeSinceStartup+.35;
    var s=UnityEngine.Object.FindFirstObjectByType<PrologueSession>();var ui=UnityEngine.Object.FindFirstObjectByType<PrologueInteraction>();
    void Check(bool value,string label){interactionChecks.Add((value?"PASS ":"FAIL ")+label);}
    switch(interactionStep++){
     case 0: s.Teleport(s.Content.Points.Single(p=>p.Id=="LoggingEvidence").Position+Vector3.up,0);break;
     case 1: Check(ui.FocusedId=="LoggingEvidence"&&ui.HideText&&ui.InteractionIcon!=null,"nearby textless icon target");interactionBefore=ui.ResultCount;InputSystem.QueueStateEvent(interactionKeyboard,new KeyboardState(Key.F));break;
     case 2: InputSystem.QueueStateEvent(interactionKeyboard,new KeyboardState());Check(ui.ResultCount>interactionBefore&&ui.LastResult==PrologueSession.InteractionResult.Waiting&&!s.Progress.completed.Contains("LoggingEvidence"),"F rejects locked evidence and reports waiting");s.Teleport(s.Content.Points.Single(p=>p.Id=="WorkerSatchel").Position+Vector3.up,0);break;
     case 3: Check(ui.FocusedId=="WorkerSatchel","new target acquired");interactionBefore=ui.ResultCount;InputSystem.QueueStateEvent(interactionKeyboard,new KeyboardState(Key.F));break;
     case 4: InputSystem.QueueStateEvent(interactionKeyboard,new KeyboardState());Check(ui.ResultCount>interactionBefore&&ui.LastResult==PrologueSession.InteractionResult.Success&&s.Progress.completed.Contains("WorkerSatchel"),"F collects reward and reports success");var loaded=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,s.Content.SaveSlot+s.TestSaveSuffix+".json")).Load();Check(loaded.completed.Contains("WorkerSatchel"),"F interaction persisted to disk");UnityEngine.Object.FindFirstObjectByType<ProloguePauseMenu>().SetOpen(true);break;
     case 5: Check(ui.FocusedId==null,"pause clears focus");UnityEngine.Object.FindFirstObjectByType<ProloguePauseMenu>().SetOpen(false);FinishInteractionCheck();break;
    }
   }catch(Exception e){interactionChecks.Add("FAIL "+e.Message);FinishInteractionCheck();}
  }
  static void FinishInteractionCheck(){
   EditorApplication.update-=InteractionTick;if(interactionKeyboard!=null){InputSystem.RemoveDevice(interactionKeyboard);interactionKeyboard=null;}
   interactionChecks.Add("Virtual keyboard F through runtime input. Setup teleports only. Not full walking/combat or art approval.");File.WriteAllText(InteractionReport,string.Join("\n",interactionChecks));
  }
 }
}
