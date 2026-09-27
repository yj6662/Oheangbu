using System;
using System.Linq;
using Oheangbu.App.Demo;
using UnityEngine;

namespace Oheangbu.App.Prologue
{
    public sealed partial class PrologueSession
    {
        public Transform EscortPassengerSocket, EscortCargoSocket, EscortDepartureAnchor;
        EscortActor escortPerson, escortBox;
        Vector3 escortHomePerson, escortHomeCargo, vehicleHome;
        Quaternion vehicleHomeRotation;
        bool escortExitPending, escortRecovering;
        float escortRetryAt;

        sealed class EscortActor
        {
            readonly Transform actor, parent;
            readonly Collider[] colliders;
            readonly bool[] collision;
            Vector3 rollbackPosition;
            Quaternion rollbackRotation;
            Transform rollbackParent;bool[] rollbackCollision;
            public EscortActor(Transform actor)
            {
                this.actor=actor; parent=actor.parent;
                colliders=actor.GetComponentsInChildren<Collider>(true);
                collision=colliders.Select(c=>c.enabled).ToArray();
            }
            public void Attach(Transform socket)
            {
                rollbackPosition=actor.position; rollbackRotation=actor.rotation;rollbackParent=actor.parent;rollbackCollision=colliders.Select(c=>c!=null&&c.enabled).ToArray();
                foreach(var c in colliders)if(c!=null)c.enabled=false;
                var bounds=VisibleBounds(actor);
                var localFeet=actor.InverseTransformPoint(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
                actor.SetParent(socket,true); actor.localRotation=Quaternion.identity;
                actor.position=socket.position-actor.TransformVector(localFeet);
            }
            static Bounds VisibleBounds(Transform root)
            {
                bool first=true;var bounds=new Bounds(root.position,Vector3.zero);
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    Mesh mesh=null;bool temporary=false;
                    if(renderer is SkinnedMeshRenderer skin){mesh=new Mesh();skin.BakeMesh(mesh);temporary=true;}
                    else mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null)continue;var b=mesh.bounds;
                    for(int i=0;i<8;i++){var point=renderer.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);}
                    if(temporary)UnityEngine.Object.Destroy(mesh);
                }
                return bounds;
            }
            public void Place(Vector3 point)
            {
                actor.SetParent(parent,true); actor.position=point;
                for(int i=0;i<colliders.Length;i++)if(colliders[i]!=null)colliders[i].enabled=collision[i];
            }
            public void Rollback(){actor.SetParent(rollbackParent,true);actor.SetPositionAndRotation(rollbackPosition,rollbackRotation);for(int i=0;i<colliders.Length;i++)if(colliders[i]!=null)colliders[i].enabled=rollbackCollision[i];}
        }

        bool EscortActive => Progress?.escort!=null && Progress.escort.Stage>=DemoEscortStage.Escorting && Progress.escort.Stage<DemoEscortStage.Delivered;
        bool EscortReferences => JourneySeat!=null && EscortCompanion!=null && EscortCargo!=null && EscortPassengerSocket!=null && EscortCargoSocket!=null && EscortDepartureAnchor!=null &&
            EscortPassengerSocket.IsChildOf(JourneySeat.Vehicle.transform) && EscortCargoSocket.IsChildOf(JourneySeat.Vehicle.transform) &&
            EscortCompanion.gameObject.scene==gameObject.scene && EscortCargo.gameObject.scene==gameObject.scene;
        bool EscortPresent => EscortReferences && EscortCompanion.gameObject.activeInHierarchy && EscortCargo.gameObject.activeInHierarchy;

        void BindJourneyEscortLoad()
        {
            if(!EscortReferences)return;
            escortPerson=new EscortActor(EscortCompanion); escortBox=new EscortActor(EscortCargo);
            escortHomePerson=EscortCompanion.position;escortHomeCargo=EscortCargo.position;
            vehicleHome=JourneySeat.Vehicle.transform.position;vehicleHomeRotation=JourneySeat.Vehicle.transform.rotation;
            if(Progress.hasVehicle)SetJourneyVehiclePose(Progress.vehiclePosition,Progress.vehicleRotation);
            JourneySeat.ConfirmBoarding=ConfirmJourneyEscortBoarding;
            if(Progress.escort?.Stage==DemoEscortStage.Delivered){escortPerson.Place(Progress.escort.CompanionFeet);escortBox.Place(Progress.escortCargoPosition);return;}
            if(!EscortActive)return;
            if(Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding)
            {
                escortPerson.Attach(EscortPassengerSocket);escortBox.Attach(EscortCargoSocket);
                escortExitPending=true;
            }
            else {escortPerson.Place(Progress.escort.CompanionFeet);escortBox.Place(Progress.escortCargoPosition);}
        }
        void SetJourneyVehiclePose(Vector3 position,Quaternion rotation)
        {
            var vehicle=JourneySeat.Vehicle;vehicle.SetDriverInput(0,0,true);
            vehicle.transform.SetPositionAndRotation(position,rotation);
            vehicle.Body.position=position;vehicle.Body.rotation=rotation;
            vehicle.Body.linearVelocity=vehicle.Body.angularVelocity=Vector3.zero;
            Physics.SyncTransforms();
        }
        bool CanLoadJourneyEscort()
        {
            if(!EscortPresent||escortPerson==null||escortExitPending||Progress?.escort==null||JourneySeat.Vehicle.Speed>.5f)return false;
            var state=Progress.escort;
            if(state.Stage!=DemoEscortStage.Contracted&&!EscortActive || state.CompanionMode==DemoEscortCompanionMode.Riding)return false;
            return Vector3.Distance(EscortCompanion.position,JourneySeat.SeatSocket.position)<=DemoEscortRules.MaximumCompanionDistance &&
                Vector3.Distance(EscortCargo.position,JourneySeat.SeatSocket.position)<=DemoEscortRules.MaximumCompanionDistance &&
                (state.Stage!=DemoEscortStage.Contracted || Vector3.Distance(JourneySeat.SeatSocket.position,EscortDepartureAnchor.position)<=DemoEscortRules.MaximumInteractionDistance);
        }
        DemoEscortEvidence JourneyEscortProof(DemoEscortCommand command) => new DemoEscortEvidence
        {
            Command=command,ExpectedRevision=Progress.escort.Revision,EventId=Guid.NewGuid().ToString("N"),SessionVerified=true,
            PlayerFeet=JourneySeated?JourneySeat.SeatSocket.position:Player.position,
            CompanionFeet=EscortCompanion.position,CompanionPresent=EscortPresent,CargoPresent=EscortPresent,
            PlayerInVehicle=JourneySeated,VehiclePresent=JourneySeat.Vehicle.IsConfigured,
            VehicleSpeed=JourneySeat.Vehicle.Speed,
            CompanionLoaded=EscortCompanion.IsChildOf(EscortPassengerSocket),CargoLoaded=EscortCargo.IsChildOf(EscortCargoSocket)
        };
        bool ConfirmJourneyEscortBoarding()
        {
            if(!JourneySeated||!CanLoadJourneyEscort())return false;
            escortPerson.Attach(EscortPassengerSocket);escortBox.Attach(EscortCargoSocket);
            bool committed=false;
            try
            {
                var command=Progress.escort.Stage==DemoEscortStage.Contracted?DemoEscortCommand.StartEscort:DemoEscortCommand.Board;
                var proof=JourneyEscortProof(command);
                proof.InteractionId="escort_start";proof.PointFeet=EscortDepartureAnchor.position;
                proof.CheckpointId="escort_start";proof.CheckpointFeet=escortHomePerson;proof.CheckpointYaw=0;
                proof.CheckpointVerified=GroundForEscort(escortHomePerson,out var support)&&Vector3.Distance(support,escortHomePerson)<.3f;
                if(DemoEscortRules.TryPrepare(Progress.escort,command,proof,out var next,out _)!=DemoEscortStatus.Prepared)return false;
                var candidate=CopyProgress();candidate.escort=next;
                if(command==DemoEscortCommand.StartEscort){
                    if(!JourneySeat.TryGetSafeExit(out var exit))return false;
                    PrologueProgressStore.Complete(candidate,"escort_start",0);candidate.checkpoint="escort_start";candidate.checkpointPosition=exit;candidate.hasEscortCheckpoint=false;
                }
                committed=TryCommit(candidate);Resolve(committed?InteractionResult.Success:InteractionResult.SaveFailed);
                return committed;
            }
            finally{if(!committed){escortPerson.Rollback();escortBox.Rollback();}}
        }
        void RequestJourneyEscortExit()
        {
            if(escortRecovering||!EscortActive)return;
            escortExitPending=Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding;
            escortRetryAt=0;TickJourneyEscort();
        }
        void TickJourneyEscort()
        {
            if(!escortExitPending||JourneySeated||respawning||Time.timeScale<=0||Time.unscaledTime<escortRetryAt)return;
            escortRetryAt=Time.unscaledTime+.5f;
            if(!EscortPresent||!EscortActive||Progress.escort.CompanionMode!=DemoEscortCompanionMode.Riding)return;
            if(!PlanJourneyEscortExit(out var person,out var cargo))return;
            var proof=JourneyEscortProof(DemoEscortCommand.Disembark);proof.SafeExitVerified=true;proof.SafeExitFeet=person;
            if(DemoEscortRules.TryPrepare(Progress.escort,proof.Command,proof,out var next,out _)!=DemoEscortStatus.Prepared)return;
            var candidate=CopyProgress();Snapshot(candidate);candidate.escort=next;candidate.escortCargoPosition=cargo;
            if(!TryCommit(candidate,false)){Resolve(InteractionResult.SaveFailed);return;}
            escortPerson.Place(person);escortBox.Place(cargo);escortExitPending=false;
        }
        bool IgnoreEscortCollider(Collider c) => c==null || c.transform.IsChildOf(Player) || c.transform.IsChildOf(EscortCompanion) || c.transform.IsChildOf(EscortCargo);
        bool GroundForEscort(Vector3 origin,out Vector3 point)
        {
            point=default;
            foreach(var hit in Physics.RaycastAll(origin+Vector3.up*3,Vector3.down,7,1,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            {
                if(IgnoreEscortCollider(hit.collider)||hit.transform.IsChildOf(JourneySeat.Vehicle.transform)||hit.normal.y<.85f)continue;
                point=hit.point;return true;
            }
            return false;
        }
        bool PlanJourneyEscortExit(out Vector3 person,out Vector3 cargo)
        {
            person=cargo=default;
            var origin=Player.position;
            if(Vector3.Distance(origin,JourneySeat.Vehicle.transform.position)>DemoEscortRules.MaximumCompanionDistance && !JourneySeat.TryGetSafeExit(out origin))return false;
            foreach(float range in new[]{1.5f,2.5f,3.5f,4f})
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back,new Vector3(1,0,1).normalized,new Vector3(-1,0,1).normalized,new Vector3(1,0,-1).normalized,new Vector3(-1,0,-1).normalized})
            {
                if(!GroundForEscort(origin+direction*range,out var p))continue;
                p+=Vector3.up*.04f;
                float radius=EscortFollowProfile!=null?EscortFollowProfile.ClearanceRadius:.3f;
                float height=EscortFollowProfile!=null?EscortFollowProfile.ClearanceHeight:1.75f;
                var obstacle=JourneySeat.Vehicle.GetComponent<UnityEngine.AI.NavMeshObstacle>();
                if(obstacle!=null&&obstacle.enabled){var local=obstacle.transform.InverseTransformPoint(p)-obstacle.center;if(Mathf.Abs(local.x)<obstacle.size.x*.5f+radius&&Mathf.Abs(local.z)<obstacle.size.z*.5f+radius)continue;}
                if(Physics.OverlapCapsule(p+Vector3.up*(radius+.05f),p+Vector3.up*(Mathf.Max(radius,height-radius)+.05f),radius,1,QueryTriggerInteraction.Ignore).Any(c=>!IgnoreEscortCollider(c)))continue;
                if(EscortFollowProfile!=null&&(!UnityEngine.AI.NavMesh.SamplePosition(p,out var nav,radius*.5f,UnityEngine.AI.NavMesh.AllAreas)||Vector3.ProjectOnPlane(nav.position-p,Vector3.up).magnitude>radius*.5f))continue;
                foreach(var side in new[]{Vector3.right*1.25f,Vector3.left*1.25f,Vector3.forward*1.25f,Vector3.back*1.25f})
                {
                    if(!GroundForEscort(p+side,out var box))continue;
                    box+=Vector3.up*.05f;
                    if(Vector3.Distance(box,Player.position)<1.2f||Physics.OverlapBox(box+Vector3.up*.5f,new Vector3(.5f,.45f,.5f),Quaternion.identity,1,QueryTriggerInteraction.Ignore).Any(c=>!IgnoreEscortCollider(c)))continue;
                    person=p;cargo=box;return true;
                }
            }
            return false;
        }
        void SnapshotJourneyTransport(PrologueProgress candidate)
        {
            if(!EscortReferences||!JourneySeat.Vehicle.IsConfigured)return;
            if(JourneySeated || !escortExitPending && EscortActive && Progress.escort.CompanionMode==DemoEscortCompanionMode.Riding)
            {
                candidate.position=Progress.position;candidate.yaw=Progress.yaw;
                if(JourneySeat.Vehicle.GroundedWheelCount<3||!JourneySeat.TryGetSafeExit(out var exit))return;
                candidate.position=exit;candidate.yaw=JourneySeat.Vehicle.transform.eulerAngles.y;
            }
            candidate.hasVehicle=true;candidate.vehiclePosition=JourneySeat.Vehicle.Body.position;candidate.vehicleRotation=JourneySeat.Vehicle.Body.rotation;
            candidate.escortCargoPosition=EscortCargo.position;
            if(candidate.escort!=null&&candidate.escort.CompanionMode==DemoEscortCompanionMode.Following)candidate.escort.CompanionFeet=EscortCompanion.position;
        }
        void PrepareJourneyEscortDeath(PrologueProgress candidate)
        {
            if(!EscortActive||!EscortReferences)return;
            var proof=JourneyEscortProof(DemoEscortCommand.RecoverAfterDeath);
            bool shared=Progress.hasEscortCheckpoint;
            var person=shared?Progress.escort.CheckpointFeet:escortHomePerson;
            var cargo=shared?Progress.escortCheckpointCargo:escortHomeCargo;
            var vehicle=shared?Progress.escortCheckpointVehicle:vehicleHome;
            var rotation=shared?Progress.escortCheckpointVehicleRotation:vehicleHomeRotation;
            proof.DeathRecoveryVerified=vitals.Hp01<=0;proof.CheckpointVerified=GroundForEscort(person,out _)&&GroundForEscort(cargo,out _);
            proof.CheckpointId=Progress.escort.CheckpointId;proof.CheckpointFeet=person;proof.CheckpointYaw=rotation.eulerAngles.y;
            if(DemoEscortRules.TryPrepare(Progress.escort,proof.Command,proof,out var next,out _)!=DemoEscortStatus.Prepared)throw new InvalidOperationException("Escort recovery point unavailable");
            candidate.escort=next;candidate.escortCargoPosition=cargo;
            candidate.hasVehicle=true;candidate.vehiclePosition=vehicle;candidate.vehicleRotation=rotation;
        }
        void RestoreJourneyEscortAfterDeath()
        {
            if(!EscortActive||!EscortReferences)return;
            escortExitPending=false;SetJourneyVehiclePose(Progress.vehiclePosition,Progress.vehicleRotation);
            escortPerson.Place(Progress.escort.CompanionFeet);escortBox.Place(Progress.escortCargoPosition);
        }
    }
}
