using Oheangbu.Combat;
using UnityEngine;
using UnityEngine.AI;
namespace Oheangbu.App.Prologue
{
    [RequireComponent(typeof(EnemyController),typeof(NavMeshAgent))]
    public sealed class PrologueEncounter : MonoBehaviour
    {
        public enum Behaviour { Patrol, Chase, Return, Dead }
        public string Id; public Transform Player; public PrologueSession Session;
        public event System.Action<string> Defeated;
        public Vector3[] PatrolPoints; public bool Ranged;
        public float DetectionRange=16,Leash=28,Speed=2.6f;
        public Behaviour Current {get;private set;}
        NavMeshAgent agent;EnemyController attacks;EnemyVitals vitals;Renderer[] renderers;Collider[] bodyColliders;
        Vector3 home;int patrol;float next,lastSeen;
        void Awake(){agent=GetComponent<NavMeshAgent>();attacks=GetComponent<EnemyController>();vitals=GetComponent<EnemyVitals>();renderers=GetComponentsInChildren<Renderer>();bodyColliders=GetComponentsInChildren<Collider>();home=transform.position;}
        void OnEnable(){if(vitals!=null)vitals.Died+=Died;}
        void OnDisable(){if(vitals!=null)vitals.Died-=Died;}
        void Died(){Current=Behaviour.Dead;if(agent.enabled&&agent.isOnNavMesh)agent.isStopped=true;attacks.AttackEnabled=false;Session?.EnemyDefeated(Id);Defeated?.Invoke(Id);foreach(var r in renderers)r.enabled=false;foreach(var c in bodyColliders)c.enabled=false;}
        public void ResetEncounter()
        {
            Current=Behaviour.Patrol;agent.Warp(home);if(agent.isOnNavMesh){agent.ResetPath();agent.isStopped=false;}
            foreach(var r in renderers)r.enabled=true;
            foreach(var c in bodyColliders)c.enabled=true;
            attacks.ResetEncounter();attacks.AttackEnabled=false;patrol=0;next=0;
        }
        void Update()
        {
            if(Player==null || !vitals.IsAlive || !agent.isOnNavMesh)return;
            if(Time.time<next)return;next=Time.time+.12f;
            float distance=Vector3.Distance(transform.position,Player.position);
            bool seen=distance<DetectionRange && attacks.HasLineOfSight();if(seen)lastSeen=Time.time;
            if(Current!=Behaviour.Return && seen)Current=Behaviour.Chase;
            if(Current==Behaviour.Chase && (Vector3.Distance(Player.position,home)>Leash || Time.time-lastSeen>4)) {Current=Behaviour.Return;attacks.StopAttack();}
            attacks.AttackEnabled=Current==Behaviour.Chase && seen;
            agent.speed=Speed;agent.isStopped=attacks.AttackInProgress;
            if(agent.isStopped)return;
            if(Current==Behaviour.Return){agent.SetDestination(home);if(Vector3.Distance(transform.position,home)<1.2f)Current=Behaviour.Patrol;}
            else if(Current==Behaviour.Chase){agent.stoppingDistance=Ranged?9:1.7f;agent.SetDestination(Player.position);}
            else if(PatrolPoints!=null && PatrolPoints.Length>0){agent.stoppingDistance=.3f;agent.SetDestination(PatrolPoints[patrol]);if(!agent.pathPending && agent.remainingDistance<.6f)patrol=(patrol+1)%PatrolPoints.Length;}
        }
    }
}
