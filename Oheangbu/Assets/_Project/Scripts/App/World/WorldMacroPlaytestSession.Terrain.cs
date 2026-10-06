using Oheangbu.App.Demo;
using Oheangbu.Combat;
using UnityEngine;
namespace Oheangbu.App.World
{
    public sealed partial class WorldMacroPlaytestSession
    {
        public WorldTerrainQuery Traversal;
        public float CurrentImmersion {get;private set;}
        public bool IsDrowning=>drowningRemaining>0;
        float recentGroundHeight,vehicleGroundHeight,drowningRemaining,nextVehicleAnchor;
        bool groundHeightKnown,vehicleHeightKnown;
        WorldMacroProgress pendingEnvironmentRecovery;
        float nextRecoveryAttempt;
        void ResetTraversal(Vector3 feet)
        {
            recentGroundHeight=feet.y;groundHeightKnown=true;vehicleHeightKnown=false;drowningRemaining=0;
            CurrentImmersion=0;
            if(Walker?.Motor!=null){Walker.Motor.EnvironmentalInputBlocked=false;Walker.Motor.TerrainMovementScale=1;}
            if(Walker?.Drawing!=null)Walker.Drawing.enabled=true;
            if(demoField!=null)demoField.enabled=true;
        }
        bool TickTraversal()
        {
            if(pendingEnvironmentRecovery!=null)
            {if(Time.unscaledTime>=nextRecoveryAttempt)TryFinishEnvironmentRecovery();return true;}
            if(Traversal==null||Traversal.Rules==null)return false;
            if(vitals.Hp01<=0)return true;
            var feet=FieldSpellService.Feet(Walker.Body);
            if(Walker.Seated&&DemoEscortSeat?.Vehicle!=null)
            {
                var car=DemoEscortSeat.Vehicle;
                feet=car.Hull!=null?new Vector3(car.transform.position.x,car.Hull.bounds.min.y,car.transform.position.z):car.transform.position;
                if(!vehicleHeightKnown){vehicleGroundHeight=feet.y;vehicleHeightKnown=true;}
                if(vehicleGroundHeight-feet.y>=Traversal.Rules.FatalFallHeight){vitals.ApplyFatalFall();return true;}
                if(car.GroundedWheelCount>0)
                {
                    vehicleGroundHeight=feet.y;
                    if(Time.time>=nextVehicleAnchor&&!Traversal.IsDeep(feet))
                    {
                        nextVehicleAnchor=Time.time+.3f;
                        float side=car.Hull!=null?car.Hull.size.x*.5f+.9f:2.5f;
                        var center=new Vector3(car.transform.position.x,feet.y,car.transform.position.z);
                        if(TrySafeFeet(center+car.transform.right*side,out var roadside)||TrySafeFeet(center-car.transform.right*side,out roadside))lastSafe=roadside;
                    }
                }
            }
            else
            {
                // Safe seat exit starts a new walking interval. The last walking
                // height may be kilometres behind and must not count as a fall.
                if(vehicleHeightKnown){recentGroundHeight=feet.y;groundHeightKnown=true;}
                vehicleHeightKnown=false;
                if(groundHeightKnown&&recentGroundHeight-feet.y>=Traversal.Rules.FatalFallHeight){vitals.ApplyFatalFall();return true;}
                // #308 D308-18 (3): the reference follows the feet only on walkable support (WorldMacroPlaytestSession.Fall308.cs)
                if(FallReferenceRefreshes308()){recentGroundHeight=feet.y;groundHeightKnown=true;}
            }
            CurrentImmersion=Traversal.Immersion(feet);
            Walker.Motor.TerrainMovementScale=Traversal.MovementScale(feet);
            if(drowningRemaining>0)
            {
                if(DemoEscortSeat!=null&&DemoEscortSeat.Occupied)DemoEscortSeat.Vehicle.StopDriverInputForUi();
                drowningRemaining-=Time.deltaTime;
                if(drowningRemaining<=0){vitals.ApplyEnvironmentalDeath(EnvironmentDeathCause.DeepWater);return true;}
                return true;
            }
            if(CurrentImmersion>Traversal.Rules.MaximumWadingDepth)
            {
                drowningRemaining=Traversal.Rules.DrowningSeconds;Walker.Motor.EnvironmentalInputBlocked=true;
                Walker.Drawing.enabled=false;
                if(demoField!=null)demoField.enabled=false;
                Show("깊은 물에 잠겼다.");return true;
            }
            if(!Walker.Seated&&Walker.Motor.IsLocomotionGrounded&&TrySafeFeet(feet,out var safe))lastSafe=safe;
            return false;
        }
        void BeginEnvironmentRecovery()
        {
            var feet=Checkpoint();float yaw=CheckpointYaw();
            var proposal=WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
            Oheangbu.App.Prologue.PrologueProgressStore.Drop(proposal.ledger,lastSafe);
            proposal.ledger.position=feet;proposal.ledger.checkpointPosition=feet;proposal.ledger.hasPosition=true;
            proposal.ledger.yaw=yaw;proposal.ledger.hp=1;proposal.ledger.ink=1;
            foreach(var actor in Content.Encounters)if(actor.RespawnOnRest)proposal.defeated.Remove(actor.Id);
            pendingEnvironmentRecovery=proposal;TryFinishEnvironmentRecovery();
        }
        void TryFinishEnvironmentRecovery()
        {
            nextRecoveryAttempt=Time.unscaledTime+.5f;
            if(!TryCommitInteraction(pendingEnvironmentRecovery,out _,true))return;
            var feet=Progress.ledger.position;float yaw=Progress.ledger.yaw;pendingEnvironmentRecovery=null;respawning=true;
            if(DemoEscortSeat!=null&&DemoEscortSeat.Occupied)DemoEscortSeat.ForceExitForRecovery(feet,yaw);
            DemoEscortSummon?.RecallAfterRecovery();
            ResetCombat();Teleport(feet,yaw);vitals.Restore();ink.Restore();RefreshDrop();respawning=false;
            Show("마지막 쉼터에서 눈을 떴다. 통보는 마지막 안전한 마른 지면에 남아 있다.");
        }
    }
}
