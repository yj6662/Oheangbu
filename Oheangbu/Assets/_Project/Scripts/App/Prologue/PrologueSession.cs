using System;
using System.IO;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using UnityEngine;
using VContainer;
namespace Oheangbu.App.Prologue
{
    public sealed class PrologueSession : MonoBehaviour
    {
        public PrologueContentSO Content; public Transform Player; public PrologueEncounter[] Encounters;
        public CombatLoopWiring Wiring;
        public PrologueProgress Progress {get;private set;}
        public string TestSaveSuffix="";
        public event Action<string> Feedback;
        InkPool ink;GroggyMeter groggy;ParryJudge parry;PrologueProgressStore store;PlayerVitals vitals;CharacterController cc;
        Vector3 lastGrounded;float nextSave;bool ready,respawning;GameObject drop;Material dropMaterial;
        [Inject] public void Construct(InkPool ink,GroggyMeter groggy,ParryJudge parry){this.ink=ink;this.groggy=groggy;this.parry=parry;}
        void Start()
        {
            if(Content==null||Player==null) {enabled=false;return;}
            vitals=Player.GetComponent<PlayerVitals>();cc=Player.GetComponent<CharacterController>();
            store=new PrologueProgressStore(Path.Combine(Application.persistentDataPath,Content.SaveSlot+TestSaveSuffix+".json"));
            Progress=store.Load()??new PrologueProgress{checkpointPosition=Content.StartPosition,position=Content.StartPosition,yaw=Content.StartYaw};
            var position=Progress.hasPosition?Progress.position:Progress.checkpointPosition;
            if(!IsSafe(position))position=Progress.checkpointPosition;
            Teleport(position,Progress.yaw);vitals.Restore(Progress.hp>0?Progress.hp:1);ink?.Restore(Progress.ink);
            vitals.Died+=OnDeath;ready=true;nextSave=Time.time+Content.AutosaveSeconds;RefreshDrop();
        }
        bool IsSafe(Vector3 p)
        {
            if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))return false;
            foreach(var hit in Physics.RaycastAll(p+Vector3.up*2,Vector3.down,5,1,QueryTriggerInteraction.Ignore))
                if(!hit.transform.IsChildOf(Player)&&hit.normal.y>.5f)return true;
            return false;
        }
        void Update()
        {
            if(!ready||respawning)return;
            if(cc.isGrounded){if(lastGrounded.y-Player.position.y>=Content.FallDeathHeight){vitals.ApplyFatalFall();return;}lastGrounded=Player.position;}
            else if(lastGrounded.y-Player.position.y>=Content.FallDeathHeight){vitals.ApplyFatalFall();return;}
            if(Time.time>=nextSave && cc.isGrounded){Save();nextSave=Time.time+Content.AutosaveSeconds;}
        }
        void OnDestroy(){if(vitals!=null)vitals.Died-=OnDeath;if(drop!=null)Destroy(drop);if(dropMaterial!=null)Destroy(dropMaterial);}
        void OnApplicationQuit(){if(ready)Save();}
        void OnDisable(){if(ready&&Player!=null&&vitals!=null)Save();}
        public void Save()
        {
            if(!ready)return;
            Progress.position=lastGrounded;Progress.yaw=Player.eulerAngles.y;Progress.hp=vitals.Hp01;Progress.ink=ink!=null?ink.Value:1;Progress.hasPosition=true;store.Save(Progress);
        }
        public void Interact(string id)
        {
            if(!ready)return;
            if(id=="CurrencyDrop") {if(Vector3.Distance(Player.position,Progress.dropPosition)>2.5f)return;int sum=PrologueProgressStore.Retrieve(Progress);RefreshDrop();Feedback?.Invoke("조선통보 "+sum+" 회수");Save();return;}
            var p=Array.Find(Content.Points,x=>x.Id==id);if(p==null)return;
            if(Vector3.Distance(Player.position,p.Position)>p.Radius+1f)return;
            if(p.Kind==PrologueInteractionKind.Rest){Rest(p);return;}
            bool first=PrologueProgressStore.Complete(Progress,id,p.Currency);
            Feedback?.Invoke(p.Kind==PrologueInteractionKind.Currency&&!first?"이미 챙긴 물품이다.":p.Text+(first&&p.Currency>0?"\n조선통보 +"+p.Currency:""));
            Save();
        }
        void Rest(PrologueContentSO.Point p)
        {
            Progress.checkpoint=p.Id;Progress.checkpointPosition=p.Position+Vector3.up;
            ResetCombat();vitals.Restore();ink?.Restore();PrologueProgressStore.Complete(Progress,p.Id,0);
            Feedback?.Invoke("잠시 숨을 고른다.\n체력과 먹을 회복했다.  조선통보 "+Progress.currency);Save();
        }
        public void EnemyDefeated(string id){if(!ready)return;Progress.currency+=Content.EnemyReward;Save();}
        void ResetCombat()
        {
            var wiring=Wiring;if(wiring!=null){wiring.enabled=false;wiring.enabled=true;}
            var lockOn=Player.GetComponent<LockOn>();if(lockOn!=null&&lockOn.IsLocked)lockOn.Toggle();
            groggy?.Reset();parry?.ClearGuard();foreach(var actor in Encounters)if(actor!=null)actor.ResetEncounter();
        }
        void OnDeath()
        {
            if(!ready||respawning)return;respawning=true;
            PrologueProgressStore.Drop(Progress,lastGrounded);ResetCombat();Teleport(Progress.checkpointPosition,0);
            vitals.Restore();ink?.Restore();RefreshDrop();respawning=false;Feedback?.Invoke("성황당 곁에서 다시 눈을 떴다. 남긴 통보를 되찾을 수 있다.");Save();
        }
        public void Teleport(Vector3 p,float yaw)
        {
            if(cc==null)cc=Player.GetComponent<CharacterController>();
            bool was=cc.enabled;cc.enabled=false;Player.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));cc.enabled=was;lastGrounded=p;
            Player.GetComponent<PlayerMotor>()?.ResetMotion();
        }
        void RefreshDrop()
        {
            if(drop!=null)Destroy(drop);if(dropMaterial!=null)Destroy(dropMaterial);if(Progress.dropCurrency<=0)return;
            drop=GameObject.CreatePrimitive(PrimitiveType.Sphere);drop.name="LostTongbo";drop.transform.position=Progress.dropPosition-Vector3.up*.7f;drop.transform.localScale=new Vector3(.3f,.15f,.3f);
            Destroy(drop.GetComponent<Collider>());var r=drop.GetComponent<Renderer>();dropMaterial=r.material;dropMaterial.color=new Color(.3f,.25f,.16f);
        }
    }
}
