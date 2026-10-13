using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
using Oheangbu.App.Demo;
namespace Oheangbu.App.Prologue
{
    [RequireComponent(typeof(EnemyController),typeof(NavMeshAgent))]
    public sealed partial class PrologueEncounter : MonoBehaviour
    {
        public enum Behaviour { Patrol, Chase, Return, Dead }
        public string Id; public Transform Player; public PrologueSession Session;
        public event System.Action<string> Defeated;
        // Presentation observes a committed perception transition; it never changes detection rules.
        public event System.Action PlayerDetected;
        public Vector3[] PatrolPoints; public bool Ranged;
        public float DetectionRange=16,Leash=28,Speed=2.6f;
        public float PreferredDistance;
        // #306 #11 (SPEC-PLAYTEST-306): a tutorial sequencer may hold the actor in place; perception and attack gating keep running
        [System.NonSerialized] public bool HoldPosition;
        // D308-66: on patrol an actor walks (Speed x PatrolSpeedScale) and stands a moment at each point (PatrolPauseMin..Max seconds, the idle
        // clip plays). 1 / 0 / 0 = the old patrol (chase speed, never stopping). Chase and return are not changed.
        [Range(.1f,1f)] public float PatrolSpeedScale=1f; [Min(0)] public float PatrolPauseMin,PatrolPauseMax;
        float patrolPauseUntil;
        [Min(0)] public float DeathVisualSeconds;
        bool[] authoredVisibility;float hideDeadAt=float.NegativeInfinity;
        public Behaviour Current {get;private set;}
        NavMeshAgent agent;EnemyController attacks;EnemyVitals vitals;Renderer[] renderers;Collider[] bodyColliders;
        CheongryongCombatController boss;
        SouthGateGeneralController general;
        MineBossController tutorialBoss;
        CheongryongGrowthController growth;
        Vector3 home;int patrol;float next,lastSeen;
        void Awake(){agent=GetComponent<NavMeshAgent>();attacks=GetComponent<EnemyController>();boss=GetComponent<CheongryongCombatController>();general=GetComponent<SouthGateGeneralController>();tutorialBoss=GetComponent<MineBossController>();vitals=GetComponent<EnemyVitals>();renderers=GetComponentsInChildren<Renderer>();authoredVisibility=new bool[renderers.Length];for(int i=0;i<renderers.Length;i++)authoredVisibility[i]=renderers[i].enabled;bodyColliders=GetComponentsInChildren<Collider>();home=transform.position;}
        void OnEnable(){if(vitals!=null){vitals.Died+=Died;vitals.DamageResolved+=Damaged308;}growth=GetComponent<CheongryongGrowthController>();if(growth!=null)growth.StateChanged+=GrowthChanged;}
        void OnDisable(){LeaveBrain308();if(vitals!=null){vitals.Died-=Died;vitals.DamageResolved-=Damaged308;}if(growth!=null)growth.StateChanged-=GrowthChanged;}
        void GrowthChanged(CheongryongGrowthState state)
        {
            if(state!=CheongryongGrowthState.Windup)return;
            attacks.AttackEnabled=false;attacks.StopAttack();
            if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;
        }
        void Died(){Current=Behaviour.Dead;LeaveBrain308();if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;attacks.AttackEnabled=false;hideDeadAt=Time.time+(System.Array.Exists(renderers,r=>r.enabled)?DeathVisualSeconds:0);Session?.EnemyDefeated(Id);Defeated?.Invoke(Id);if(DeathVisualSeconds<=0)foreach(var r in renderers)r.enabled=false;foreach(var c in bodyColliders)c.enabled=false;}
        public void SetPresentationVisibility(bool allowed)
        {
            if(renderers==null||vitals==null)return;
            bool visible=allowed&&(vitals.IsAlive||Time.time<hideDeadAt);
            for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].enabled=visible&&authoredVisibility[i];
        }
        public void ResetEncounter()
        {
            Current=Behaviour.Patrol;agent.Warp(home);if(agent.isOnNavMesh){agent.ResetPath();agent.isStopped=false;}
            hideDeadAt=float.NegativeInfinity;for(int i=0;i<renderers.Length;i++)renderers[i].enabled=authoredVisibility[i];
            foreach(var c in bodyColliders)c.enabled=true;
            attacks.ResetEncounter();attacks.AttackEnabled=false;patrol=0;next=0;patrolPauseUntil=0;ResetBrain308();
            if(boss!=null)boss.ResetEncounter();
            if(general!=null)general.ResetEncounter();
            if(tutorialBoss!=null)tutorialBoss.ResetEncounter();
        }
        void Update()
        {
            if(Current==Behaviour.Dead){if(!float.IsNegativeInfinity(hideDeadAt)&&Time.time>=hideDeadAt){foreach(var r in renderers)r.enabled=false;hideDeadAt=float.NegativeInfinity;}return;}
            if(Player==null || !vitals.IsAlive || !agent.isOnNavMesh)return;
            if(vitals.Control.BlocksActions(Time.time)){attacks.AttackEnabled=false;agent.isStopped=true;return;}
            if(growth!=null&&growth.IsWindingUp){attacks.AttackEnabled=false;agent.isStopped=true;return;}
            Slip308();if(BrainActive308)BrainFrame308();   // #308 D308-70: per frame (the look-around turn, the side-slip record)
            if(Time.time<next)return;next=Time.time+.12f;
            float distance=Vector3.Distance(transform.position,Player.position);
            if(BrainActive308){TickBrain308(distance);return;}   // #308 D308-70 (SPEC-ENEMY-BRAIN-308): ordinary actors with a filled-in profile; everything below is the behaviour before
            var previous=Current;
            bool seen=distance<DetectionRange && attacks.HasLineOfSight();if(seen)lastSeen=Time.time;
            if(Current!=Behaviour.Return && seen)Current=Behaviour.Chase;
            if(Current==Behaviour.Chase && (Vector3.Distance(Player.position,home)>Leash || Time.time-lastSeen>4)) {Current=Behaviour.Return;attacks.StopAttack();}
            if(previous!=Behaviour.Chase && Current==Behaviour.Chase)PlayerDetected?.Invoke();
            attacks.AttackEnabled=boss==null && general==null && Current==Behaviour.Chase && seen;
            if(boss!=null){boss.AttackEnabled=Current==Behaviour.Chase&&seen;if(Current==Behaviour.Return)boss.StopAttack();}
            if(general!=null){general.AttackEnabled=Current==Behaviour.Chase&&seen;if(Current==Behaviour.Return)general.StopAttack();}
            // #308 D308-70: EnemyController used to turn the body to the player every frame, also on patrol and on the way home, so the actor slid
            // sideways under a forward walk. Now it faces the player only while it chases (or a tutorial wait holds it); otherwise the agent turns it
            // along its path. The dragon and the general turn themselves (their EnemyController is off) and are left alone.
            if(boss==null&&general==null){bool face=Current==Behaviour.Chase||HoldPosition;attacks.FaceTarget=face;agent.updateRotation=!face;}
            if(HoldPosition){if(agent.isOnNavMesh)agent.isStopped=true;return;}   // #306 #11: a tutorial wait stands still, still watching
            agent.speed=Speed*vitals.Control.MovementScale(Time.time)*(Current==Behaviour.Patrol?PatrolSpeedScale:1f);agent.isStopped=boss!=null?Current==Behaviour.Chase&&!boss.CanNavigate:general!=null?Current==Behaviour.Chase&&!general.CanNavigate:attacks.AttackInProgress;
            if(agent.isStopped)return;
            if(Current==Behaviour.Return)StepReturn308();
            else if(Current==Behaviour.Chase){agent.stoppingDistance=PreferredDistance>0?PreferredDistance:Ranged?9:1.7f;agent.SetDestination(Player.position);}
            else StepPatrol();
        }
        // #308 D308-70: the way home stops at home. The stopping distance of the chase (1.7 m, 2.2 m, 9 m for a ranged actor) used to stay on the agent,
        // so it stood that far from home, never came within the 1.2 m that ends Return, and ignored the player from then on.
        void StepReturn308(){agent.stoppingDistance=.3f;agent.SetDestination(home);if(Vector3.Distance(transform.position,home)<1.2f)Current=Behaviour.Patrol;}
        void StepPatrol()
        {
            if(PatrolPoints==null || PatrolPoints.Length==0)return;
            if(Time.time<patrolPauseUntil){agent.isStopped=true;return;}
            agent.stoppingDistance=.3f;agent.SetDestination(PatrolPoints[patrol]);
            if(!agent.pathPending && agent.remainingDistance<.6f){patrol=(patrol+1)%PatrolPoints.Length;if(PatrolPauseMax>0&&PatrolPoints.Length>1)patrolPauseUntil=Time.time+Random.Range(PatrolPauseMin,PatrolPauseMax);}
        }
    }
}
