using System.Linq;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
namespace Oheangbu.App.Demo
{
    [DefaultExecutionOrder(260)]
    public sealed class DemoEscortSeamWalker:MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public DemoEscortPresentation Presentation;
        public string LastIssue {get;private set;}
        public int CompletedTraversals {get;private set;}
        public float TraversedMetres {get;private set;}
        public float LargestWorldStep {get;private set;}
        NavMeshAgent agent;PlayerVitals vitals;
        void Awake(){agent=GetComponent<NavMeshAgent>();}
        void LateUpdate()
        {
            if(agent==null||!agent.enabled||!agent.isOnNavMesh||!agent.isOnOffMeshLink||agent.isStopped||Session==null||Session.DemoEscortCompanion!=transform)return;
            if(Presentation==null||!Presentation.isActiveAndEnabled||Presentation.Session!=Session)return;
            if(vitals==null&&Session.Walker?.Body!=null)vitals=Session.Walker.Body.GetComponent<PlayerVitals>();
            if(!Session.DemoEscortReady||Session.GameplayInputBlocked||vitals==null||vitals.Hp01<=0)return;
            if(Time.deltaTime<=0)return;
            var link=agent.currentOffMeshLinkData;
            if(!DemoEscortNavigationSeam.Active.Any(s=>s!=null&&s.gameObject.scene==gameObject.scene&&s.Matches(link.startPos,link.endPos)))
            {LastIssue="Unowned off-mesh connection; traversal refused.";return;}
            Vector3 end=link.endPos,target=transform.position,delta=Vector3.zero;
            bool Ignore(Collider c)=>c==null||c.gameObject.scene!=gameObject.scene||c.transform.IsChildOf(transform)||Session.DemoEscortCargo!=null&&c.transform.IsChildOf(Session.DemoEscortCargo);
            // A floor correction changes the final 3D step. Reduce travel and re-query real support,
            // rather than clamping Y and leaving the feet unsupported above the ground.
            float travel=Mathf.Min(.35f,agent.speed*Time.deltaTime);bool accepted=false;
            for(int attempt=0;attempt<6;attempt++,travel*=.5f)
            {
                target=Vector3.MoveTowards(transform.position,end,travel);bool grounded=false;
                foreach(var hit in Physics.RaycastAll(target+Vector3.up*.45f,Vector3.down,.9f,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
                    if(!Ignore(hit.collider)&&hit.collider.attachedRigidbody==null&&hit.normal.y>=.85f&&Mathf.Abs(hit.point.y-target.y)<.25f)
                    {target.y=hit.point.y;grounded=true;break;}
                if(!grounded){LastIssue="Ground seam support changed; waiting.";return;}
                delta=target-transform.position;
                if(delta.magnitude<=.35f){accepted=true;break;}
            }
            if(!accepted){LastIssue="Ground correction exceeds bounded supported step; waiting.";return;}
            if(delta.sqrMagnitude>.000001f&&Physics.CapsuleCastAll(transform.position+Vector3.up*.3f,transform.position+Vector3.up*1.4f,.27f,delta.normalized,delta.magnitude+.02f,~0,QueryTriggerInteraction.Ignore)
                .Any(h=>!Ignore(h.collider)&&h.normal.y<.65f)){LastIssue="Ground seam obstructed; waiting.";return;}
            // No Warp and no instantaneous link completion: walk every centimetre over real collision support.
            transform.position=target;LastIssue=null;TraversedMetres+=delta.magnitude;LargestWorldStep=Mathf.Max(LargestWorldStep,delta.magnitude);
            Vector3 flat=Vector3.ProjectOnPlane(delta,Vector3.up);if(flat.sqrMagnitude>.0001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(flat),360*Time.deltaTime);
            if(Vector3.ProjectOnPlane(transform.position-end,Vector3.up).magnitude<.04f&&Mathf.Abs(transform.position.y-end.y)<.2f)
            {agent.CompleteOffMeshLink();agent.nextPosition=transform.position;CompletedTraversals++;}
        }
    }
}
