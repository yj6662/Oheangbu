using System;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.Prologue {
 public sealed partial class PrologueSession {
  public JourneyEscortStop[] EscortStops=Array.Empty<JourneyEscortStop>();
  bool TryInteractJourneyEscortStop(PrologueContentSO.Point point){
   DemoEscortCommand command;
   switch(point.Id){case "checkpoint_1":command=DemoEscortCommand.FirstInspection;break;case "checkpoint_2":command=DemoEscortCommand.SecondInspection;break;case "cargo_delivery":command=DemoEscortCommand.Deliver;break;default:return false;}
   var stop=EscortStops.FirstOrDefault(s=>s!=null&&s.PointId==point.Id);
   if(stop==null||!stop.isActiveAndEnabled||stop.Session!=this||stop.gameObject.scene!=gameObject.scene||!EscortReferences||escortExitPending||
     Vector3.Distance(Player.position,stop.transform.position)>DemoEscortRules.MaximumInteractionDistance||
     Vector3.Distance(EscortCargo.position,stop.transform.position)>DemoEscortRules.MaximumCompanionDistance){Resolve(InteractionResult.Waiting);return true;}
   var proof=JourneyEscortProof(command);proof.InteractionId=point.Id;proof.PointFeet=stop.transform.position;
   var status=DemoEscortRules.TryPrepare(Progress.escort,command,proof,out var next,out _);
   if(status!=DemoEscortStatus.Prepared){Resolve(InteractionResult.Waiting);return true;}
   var candidate=CopyProgress();candidate.escort=next;
   PrologueProgressStore.Complete(candidate,point.Id,point.Currency);
   if(!TryCommit(candidate)){Resolve(InteractionResult.SaveFailed);return true;}
   if(command==DemoEscortCommand.Deliver){escortPerson.Place(EscortCompanion.position);escortBox.Place(EscortCargo.position);}
   Resolve(InteractionResult.Success);return true;
  }
 }
}
