using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.World
{
    // World changes are derived only from the session's durably committed progress.
    public sealed class CompactMountainGate : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public string RequiredCompleted;
        public Transform Panel;
        public Vector3 OpenOffset=new Vector3(0,-3,0);
        public Collider[] Blockers;
        public NavMeshObstacle Obstacle;
        Vector3 closed;float amount;bool initialized;
        void Start(){closed=Panel!=null?Panel.localPosition:Vector3.zero;}
        void Update()
        {
            if(Session?.Progress==null||Panel==null)return;
            bool opened=Session.Progress.ledger.completed.Contains(RequiredCompleted);
            if(!initialized){amount=opened?1:0;initialized=true;}else amount=Mathf.MoveTowards(amount,opened?1:0,Time.deltaTime*.7f);
            Panel.localPosition=closed+OpenOffset*Mathf.SmoothStep(0,1,amount);
            bool blocked=amount<.99f;
            if(Blockers!=null)foreach(var collider in Blockers)if(collider!=null)collider.enabled=blocked;
            if(Obstacle!=null)Obstacle.enabled=blocked;
        }
    }
}
