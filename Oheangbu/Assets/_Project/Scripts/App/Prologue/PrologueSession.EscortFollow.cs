using System;
using System.Linq;
using Oheangbu.App.Demo;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.Prologue
{
    public sealed partial class PrologueSession
    {
        public JourneyEscortFollowProfileSO EscortFollowProfile;
        Transform escortCarrySocket;
        NavMeshPath escortWalkPath;
        Animator escortWalkAnimator;
        float escortNextPath,escortNextRejoin;int escortPathCorner;
        public float EscortWalkMetres {get;private set;}
        public float EscortLargestStep {get;private set;}
        public string EscortWalkIssue {get;private set;}
        public bool EscortCarryAttached=>escortCarrySocket!=null&&EscortCargo!=null&&EscortCargo.IsChildOf(escortCarrySocket);

        bool CargoStepClear(Vector3 target,Quaternion rotation)
        {
            var box=EscortCargo.GetComponent<BoxCollider>();if(box==null)return false;
            var current=box.transform.localToWorldMatrix;
            var future=Matrix4x4.TRS(target,rotation,EscortCompanion.lossyScale)*EscortCompanion.worldToLocalMatrix*current;
            var swept=new Bounds(current.MultiplyPoint3x4(box.center),Vector3.zero);
            for(int i=0;i<8;i++){var p=box.center+Vector3.Scale(box.size*.5f,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));swept.Encapsulate(current.MultiplyPoint3x4(p));swept.Encapsulate(future.MultiplyPoint3x4(p));}
            float reach=Vector3.Distance(swept.center,EscortCompanion.position)+swept.extents.magnitude;
            float arcMargin=reach*Quaternion.Angle(EscortCompanion.rotation,rotation)*Mathf.Deg2Rad*.5f;
            return !Physics.OverlapBox(swept.center,swept.extents+Vector3.one*(.025f+arcMargin),Quaternion.identity,1,QueryTriggerInteraction.Ignore).Any(c=>!IgnoreEscortCollider(c));
        }

        void TickJourneyEscortFollow()
        {
            if(EscortFollowProfile==null||!EscortReferences)return;
            if(escortWalkAnimator==null)escortWalkAnimator=EscortCompanion.GetComponentInChildren<Animator>();
            if(escortWalkAnimator!=null)escortWalkAnimator.SetBool("JourneyWalking",false);
            if(!EscortPresent||!EscortActive||JourneySeated||escortExitPending||respawning||vitals.Hp01<=0||Time.timeScale<=0||JourneySeat.RuntimeState!=null&&JourneySeat.RuntimeState.InputBlocked)return;
            float distance=Vector3.Distance(Player.position,EscortCompanion.position);
            if(Progress.escort.CompanionMode==DemoEscortCompanionMode.Waiting)
            {
                if(distance>DemoEscortRules.MaximumCompanionDistance||Time.unscaledTime<escortNextRejoin||Vector3.Distance(EscortCompanion.position,EscortCargo.position)>3)return;
                escortNextRejoin=Time.unscaledTime+1;
                var proof=JourneyEscortProof(DemoEscortCommand.Rejoin);
                if(DemoEscortRules.TryPrepare(Progress.escort,proof.Command,proof,out var next,out _)!=DemoEscortStatus.Prepared)return;
                var candidate=CopyProgress();candidate.escort=next;
                if(!TryCommit(candidate)){EscortWalkIssue="Rejoin save pending";return;}
            }
            if(Progress.escort.CompanionMode!=DemoEscortCompanionMode.Following)return;
            if(escortCarrySocket==null){escortCarrySocket=new GameObject("Journey carried sealed cargo").transform;escortCarrySocket.SetParent(EscortCompanion,false);escortCarrySocket.localPosition=EscortFollowProfile.CargoOffset;}
            if(!EscortCarryAttached)escortBox.Attach(escortCarrySocket);
            if(distance<=EscortFollowProfile.StopDistance){EscortWalkIssue=null;return;}
            if(distance>EscortFollowProfile.MaximumDistance){EscortWalkIssue="Player beyond following range";return;}
            escortWalkPath??=new NavMeshPath();
            if(Time.time>=escortNextPath)
            {
                escortNextPath=Time.time+EscortFollowProfile.PathInterval;
                if(!NavMesh.SamplePosition(EscortCompanion.position,out var from,.5f,NavMesh.AllAreas)||
                    !NavMesh.SamplePosition(Player.position,out var to,2,NavMesh.AllAreas)||
                    !NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,escortWalkPath)||escortWalkPath.status!=NavMeshPathStatus.PathComplete)
                {escortWalkPath.ClearCorners();EscortWalkIssue="No complete walking path";return;}
                escortPathCorner=1;
            }
            var corners=escortWalkPath.corners;
            if(corners.Length<2)return;
            Vector3 position=EscortCompanion.position;
            while(escortPathCorner<corners.Length-1&&Vector3.ProjectOnPlane(corners[escortPathCorner]-position,Vector3.up).magnitude<.2f)escortPathCorner++;
            Vector3 target=corners[Mathf.Clamp(escortPathCorner,1,corners.Length-1)];
            float step=Mathf.Min(EscortFollowProfile.MaximumStep,EscortFollowProfile.Speed*Time.deltaTime);
            target=Vector3.MoveTowards(position,target,step);
            if(!GroundForEscort(target,out var support)){EscortWalkIssue="Missing ground support";return;}
            target.y=support.y+.04f;Vector3 delta=target-position;
            if(delta.magnitude>EscortFollowProfile.MaximumStep+.001f){EscortWalkIssue="Ground step exceeds limit";return;}
            float radius=EscortFollowProfile.ClearanceRadius;
            Vector3 bottom=position+Vector3.up*(radius+.05f),top=position+Vector3.up*(Mathf.Max(radius,EscortFollowProfile.ClearanceHeight-radius)+.05f);
            bool Blocks(Collider c)=>c!=null&&!IgnoreEscortCollider(c);
            if(delta.sqrMagnitude>.000001f&&Physics.CapsuleCastAll(bottom,top,radius,delta.normalized,delta.magnitude+.025f,1,QueryTriggerInteraction.Ignore).Any(h=>Blocks(h.collider)&&h.normal.y<.65f)||
                Physics.OverlapCapsule(bottom+delta,top+delta,radius,1,QueryTriggerInteraction.Ignore).Any(Blocks))
            {EscortWalkIssue="Walking or cargo clearance obstructed";return;}
            Vector3 facing=Vector3.ProjectOnPlane(delta,Vector3.up);
            var rotation=facing.sqrMagnitude>.000001f?Quaternion.RotateTowards(EscortCompanion.rotation,Quaternion.LookRotation(facing),360*Time.deltaTime):EscortCompanion.rotation;
            if(!CargoStepClear(target,rotation)){EscortWalkIssue="Cargo sweep obstructed";return;}
            EscortCompanion.SetPositionAndRotation(target,rotation);
            EscortWalkMetres+=delta.magnitude;EscortLargestStep=Mathf.Max(EscortLargestStep,delta.magnitude);EscortWalkIssue=null;
            if(escortWalkAnimator!=null)escortWalkAnimator.SetBool("JourneyWalking",delta.sqrMagnitude>.000001f);
        }
    }
}
