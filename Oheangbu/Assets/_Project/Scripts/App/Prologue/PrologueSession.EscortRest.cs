using System;
using UnityEngine;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
namespace Oheangbu.App.Prologue {
 public sealed partial class PrologueSession {
  bool PrepareJourneyEscortRest(PrologueProgress candidate,PrologueContentSO.Point point){
   if(!EscortActive){candidate.hasEscortCheckpoint=false;return true;}
   if(!EscortPresent||JourneySeated||escortExitPending||Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding||
      JourneySeat.Vehicle.Speed>.5f||JourneySeat.Vehicle.GroundedWheelCount<3||
      Vector3.Distance(Player.position,JourneySeat.Vehicle.transform.position)>DemoEscortRules.MaximumCompanionDistance||
      Vector3.Distance(Player.position,EscortCargo.position)>DemoEscortRules.MaximumCompanionDistance||
      !PlanJourneyEscortExit(out var person,out var cargo))return false;
   var proof=JourneyEscortProof(DemoEscortCommand.SaveCheckpoint);proof.CheckpointId=point.Id;proof.CheckpointFeet=person;
   proof.CheckpointYaw=JourneySeat.Vehicle.transform.eulerAngles.y;proof.CheckpointVerified=GroundForEscort(person,out _);
   var status=DemoEscortRules.TryPrepare(Progress.escort,proof.Command,proof,out var next,out _);
   if(status!=DemoEscortStatus.Prepared&&status!=DemoEscortStatus.Duplicate)return false;
   candidate.escort=next??Progress.escort.Copy();candidate.hasEscortCheckpoint=true;
   candidate.escortCheckpointCargo=cargo;candidate.escortCheckpointVehicle=JourneySeat.Vehicle.Body.position;candidate.escortCheckpointVehicleRotation=JourneySeat.Vehicle.Body.rotation;
   return true;
  }
 }
}
