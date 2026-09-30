using Oheangbu.Combat;
using UnityEngine;
namespace Oheangbu.App.World
{
    /// <summary>Appearance only: never translates a gameplay root or resolves an attack.</summary>
    public sealed class OwnedActorMotion:MonoBehaviour
    {
        public Animator Animator;
        public Transform MotionRoot;
        public EnemyController Enemy;
        public bool HasWalk,HasAttack;
        // #306: an NpcJobActor owns this animator; it disables this adapter and the flag keeps a re-enable from crossfading over it
        [System.NonSerialized] public bool JobDriven;
        Vector3 previous;bool attacking;string state;
        void OnEnable(){previous=MotionRoot!=null?MotionRoot.position:transform.position;state=null;attacking=false;if(Enemy!=null){Enemy.AttackTelegraphed+=Telegraph;Enemy.AttackPresentationEnded+=EndAttack;}}
        void OnDisable(){if(Enemy!=null){Enemy.AttackTelegraphed-=Telegraph;Enemy.AttackPresentationEnded-=EndAttack;}attacking=false;}
        void Telegraph(EnemyAttackCue cue){attacking=HasAttack;state=null;}
        void EndAttack(AttackProvenance provenance,bool cancelled){attacking=false;state=null;}
        void LateUpdate()
        {
            if(JobDriven||Animator==null||MotionRoot==null)return;Vector3 p=MotionRoot.position;float speed=Time.deltaTime>0?Vector3.ProjectOnPlane(p-previous,Vector3.up).magnitude/Time.deltaTime:0;previous=p;
            if(Time.deltaTime<=0)return;
            string next=attacking?"Attack":HasWalk&&speed>.12f&&speed<12?"Walk":"Idle";
            if(state==next)return;Animator.CrossFadeInFixedTime(next,.16f);state=next;
        }
    }
}
