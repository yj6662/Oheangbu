using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoSummonTigerChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public string[] unverified={"Native combat input", "Final tiger rig and visual approval", "Campaign NavMesh and frame time"};
        }
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit-mode API diagnostic only");
            var report=new Report();Fixture f=null;
            void Check(bool pass,string label)=>(pass?report.passed:report.failed).Add(label);
            try
            {
                f=new Fixture();Check(!f.Profile.TigerAttackEnabled,"Tiger combat is opt-in");
                f.Profile.Letter="솜";f.Profile.Element=Element.Metal;f.Profile.TigerAttackEnabled=true;
                Check(f.Profile.TryValidate(out _),"Metal tiger profile validates");
                f.Profile.FlameAttackEnabled=true;Check(!f.Profile.TryValidate(out _),"A tiger cannot select the fire-fan path");f.Profile.FlameAttackEnabled=false;
                float old=f.Profile.TigerLandingStandOff;f.Profile.TigerLandingStandOff=6;
                Check(!f.Profile.TryValidate(out _),"Landing beyond claw range is rejected");f.Profile.TigerLandingStandOff=old;

                f.Reset();f.UntilAttack();var plan=f.Manager.ActiveTigerPlan;var start=f.Manager.Active.transform.position;
                Check(plan!=null&&plan.UsesLeap&&Near(plan.TakeoffAt-plan.StartedAt,.2f)&&Near(plan.LandAt-plan.StartedAt,.8f),
                    "A mid-range target starts the authored preparation and leap schedule");
                Check(Near(plan.LeapEndAt-plan.StartedAt,1.2f)&&Near(plan.LeftContactAt-plan.LeftStartedAt,.3f)&&
                    Near(plan.RightStartedAt-plan.LeftStartedAt,.8f),"Landing recovery and alternating claws use the rig clip timings");
                f.Advance(.15f);Check(Horizontal(start,f.Manager.Active.transform.position)<.01f&&f.Hits.Count==0,"Preparation remains stationary and cannot damage");
                f.Advance(.4f);Check(Horizontal(start,f.Manager.Active.transform.position)>.4f&&f.Hits.Count==0,"Leap travels on ground without leap contact damage");
                var mid=f.Manager.Active.transform.position;float age=f.Manager.ActiveClock.Elapsed;
                for(int i=0;i<30;i++)f.Manager.Tick(0);
                Check(f.Manager.Active.transform.position==mid&&Near(age,f.Manager.ActiveClock.Elapsed),"Pause freezes leap position and attack clock");
                f.Advance(.35f);Check(Horizontal(plan.End,f.Manager.Active.transform.position)<.03f&&f.Hits.Count==0,"Landing is the fixed stand-off endpoint with no damage");
                f.Advance(.7f);Check(f.Count(f.A)==1&&f.Count(f.B)==0,"Only the locked target receives the left claw at contact");
                f.Advance(.8f);Check(f.Count(f.A)==2&&f.Count(f.B)==0,"Right claw hits the same captured target once");
                Check(f.Contacts==2&&f.Hits[0].Attack.AttackId!=f.Hits[1].Attack.AttackId,"Claws have distinct provenance IDs through the common contact path");
                bool provenance=true;foreach(var hit in f.Hits)provenance&=hit.Attack.Source==DamageSource.Summon&&hit.Attack.Element==Element.Metal&&hit.Attack.Instigator==f.Manager.Active;
                Check(provenance&&Near(f.Manager.TigerDamageSnapshot,5),"Each claw snapshots half of the ten-power metal summon cast");
                f.Advance(.5f);Check(f.Count(f.A)==2,"Recovery cannot repeat a claw contact");

                foreach(int fps in new[]{30,60,120})foreach(float scale in new[]{1f,.25f})
                {
                    f.Reset();f.UntilAttack();float elapsed=0,dt=scale/fps;
                    while(elapsed<2.95f){f.Manager.Tick(dt);elapsed+=dt;}
                    Check(f.Count(f.A)==2&&f.Count(f.B)==0,fps+" fps / "+scale+" scaled time preserves two unique contacts");
                }
                f.Reset();f.UntilAttack();f.Manager.Tick(3);
                Check(f.Count(f.A)==2,"Large delta is physically substepped and cannot skip or duplicate claws");
                f.Reset(1.9f);f.UntilAttack();plan=f.Manager.ActiveTigerPlan;start=f.Manager.Active.transform.position;f.Advance(1.7f);
                Check(!plan.UsesLeap&&f.Count(f.A)==2&&Horizontal(start,f.Manager.Active.transform.position)<.01f,"Close target uses the two claws without a fake leap or root translation");
                f.Reset(8);start=f.Manager.Active.transform.position;f.Advance(.6f);
                Check(Horizontal(start,f.Manager.Active.transform.position)>1f,"Out-of-range tiger approaches using configured fast navigation");

                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.A.TakeDamage(f.A.Hp);f.Advance(3);
                Check(plan.IsCancelled&&f.Count(f.B)==0&&f.Hits.Count==0,"Death during windup cancels and never retargets to the nearby enemy");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.A.TakeDamage(f.A.Hp);f.A.Restore();f.Advance(3);
                Check(plan.IsCancelled&&f.Count(f.A)==0,"A revived target cannot inherit a prior life combo");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;var endpoint=plan.End;
                f.A.transform.position+=Vector3.right*4;f.Sync();f.Advance(3);
                Check(plan.End==endpoint&&f.Count(f.A)==0,"Target movement cannot home the leap endpoint or cause an out-of-range hit");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Advance(1.6f);f.A.transform.position+=Vector3.right*4;f.Sync();f.Advance(.4f);
                f.A.transform.position=plan.End+plan.Direction*2+Vector3.up*.875f;f.Sync();f.Advance(.6f);
                Check(f.Count(f.A)==1&&plan.IsCancelled,"Evading after the left claw cannot re-enter for a late ghost right claw");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Wiring.SummonPlayer.position+=Vector3.right*40;f.Advance(3);
                Check(plan.IsCancelled&&f.Hits.Count==0,"Player leash exit cancels the fixed target combo");
                f.Wiring.SummonPlayer.position=f.Origin;

                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;start=f.Manager.Active.transform.position;
                f.Wall(1.3f);f.Advance(3);
                Check(plan.IsCancelled&&f.Hits.Count==0&&f.Manager.Active.transform.position.z<start.z+.4f,"A new thin wall cancels leap at its last safe pose");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;start=f.Manager.Active.transform.position;
                f.GroundGap(1.1f,.6f);f.Advance(3);
                Check(plan.IsCancelled&&f.Hits.Count==0&&f.Manager.Active.transform.position.z<start.z+.5f,"A newly missing ground span cancels the leap rather than crossing void");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Ceiling();f.Advance(3);
                Check(plan.IsCancelled&&f.Hits.Count==0,"The baked raised pelvis envelope cannot clip through a new low ceiling");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Advance(1.6f);f.Wall(1.0f);f.Advance(1.4f);
                Check(f.Count(f.A)==1&&plan.IsCancelled,"A wall between claws prevents the second common-path hit");

                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Manager.ActiveClock.BeginDissolve();f.Manager.Tick(.025f);
                Check(plan.IsCancelled&&f.Hits.Count==0,"Dissolve immediately cancels a pending leap and claws");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;f.Manager.Clear();f.Manager.Tick(3);
                Check(plan.IsCancelled&&f.Manager.Active==null&&f.Hits.Count==0,"Clear destroys the actor and cancels the immutable attack snapshot");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveTigerPlan;var originalActor=f.Manager.Active;f.Ink.Restore(0);
                Call(f.Wiring,"ResolveSummon",new SpellCast('솜',SpellKind.Summon,Element.Metal,10,default,1));
                Check(f.Manager.Active==originalActor&&!plan.IsCancelled,"Insufficient ink preserves the active leap rather than replacing it");
                f.Ink.Restore();Call(f.Wiring,"ResolveSummon",new SpellCast('솜',SpellKind.Summon,Element.Metal,10,default,1));
                Check(f.Manager.Active!=originalActor&&plan.IsCancelled,"Accepted replacement cancels the old plan and leaves only one active actor");
                f.Advance(21);Check(f.Manager.Active==null,"Twenty-second activity plus formation and dissolve terminates the summon");
            }
            catch(Exception exception){report.failed.Add(exception.ToString());}
            finally{f?.Dispose();}
            report.status=report.failed.Count==0?"PASS":"FAIL";return JsonUtility.ToJson(report,true);
        }

        sealed class Fixture : IDisposable
        {
            public Scene Scene;
            public CombatConfigSO Config;
            public SummonCombatProfile Profile;
            public CombatLoopWiring Wiring;
            public DemoSummonCombatManager Manager;
            public InkPool Ink;
            public EnemyVitals A,B;
            public BoxCollider Floor;
            public Vector3 Origin=new Vector3(7100,7100,7100);
            public readonly List<EnemyDamageResult> Hits=new List<EnemyDamageResult>();
            public int Contacts;
            NavMeshData data;NavMeshDataInstance installed;
            readonly List<GameObject> temporary=new List<GameObject>();
            public Fixture()
            {
                try
                {
                    Scene=EditorSceneManager.NewPreviewScene();
                    Config=ScriptableObject.CreateInstance<CombatConfigSO>();Set(Config,"_enemyMaxHp",1000f);
                    Profile=ScriptableObject.CreateInstance<SummonCombatProfile>();
                    var floor=Obj("Tiger ground",Origin+Vector3.down*.1f);Floor=floor.AddComponent<BoxCollider>();Floor.size=new Vector3(60,.2f,60);
                    var settings=NavMesh.GetSettingsByIndex(0);settings.agentRadius=.4f;settings.agentHeight=2;
                    settings.agentClimb=.3f;settings.agentSlope=35;settings.overrideVoxelSize=true;settings.voxelSize=.15f;
                    data=NavMeshBuilder.BuildNavMeshData(settings,new List<NavMeshBuildSource>{new NavMeshBuildSource{
                        shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(floor.transform.position,Quaternion.identity,Vector3.one),size=Floor.size,area=0}},
                        new Bounds(Origin,new Vector3(64,8,64)),Vector3.zero,Quaternion.identity);
                    if(data==null)throw new InvalidOperationException("Tiger NavMesh creation failed");
                    installed=NavMesh.AddNavMeshData(data);if(!installed.valid)throw new InvalidOperationException("Tiger NavMesh install failed");
                    var template=Obj("Empty diagnostic tiger",Vector3.zero);template.SetActive(false);Profile.PresentationPrefab=template;
                    Profile.FormationSeconds=.2f;Profile.DissolveSeconds=.2f;Profile.CooldownSeconds=10;Profile.BodyHeight=1.3f;
                    var player=Obj("Tiger player feet",Origin);
                    Wiring=Obj("Tiger wiring",Origin).AddComponent<CombatLoopWiring>();Set(Wiring,"_config",Config);Set(Wiring,"_playerTransform",player.transform);
                    Ink=new InkPool(Config,null);Wiring.Construct(null,null,null,Ink);Set(Wiring,"_contactVfx",null);
                    Manager=Wiring.gameObject.AddComponent<DemoSummonCombatManager>();Manager.Wiring=Wiring;Manager.Profiles=new[]{Profile};Call(Manager,"Bind");
                    A=Enemy("Tiger target A");B=Enemy("Tiger nearby non-target B");Set(Wiring,"_enemies",new[]{A,B});Call(Wiring,"CollectControllers");
                    Wiring.EnemyDamageResolved+=hit=>Hits.Add(hit);Wiring.EnemyHitResolved+=(_,__)=>Contacts++;Sync();
                }
                catch{Dispose();throw;}
            }
            EnemyVitals Enemy(string label)
            {var enemy=Obj(label,Origin+Vector3.right*25).AddComponent<EnemyVitals>();Set(enemy,"_config",Config);enemy.Restore();return enemy;}
            GameObject Obj(string label,Vector3 point)
            {var go=new GameObject(label);SceneManager.MoveGameObjectToScene(go,Scene);go.transform.position=point;return go;}
            public void Reset(float distance=4)
            {
                Manager.Clear();foreach(var go in temporary)if(go!=null)UnityEngine.Object.DestroyImmediate(go);temporary.Clear();
                Floor.enabled=true;Wiring.SummonPlayer.position=Origin;A.Restore();B.Restore();A.gameObject.SetActive(true);B.gameObject.SetActive(true);
                A.transform.position=B.transform.position=Origin+Vector3.right*25;Hits.Clear();Contacts=0;Ink.Restore();Sync();
                Call(Wiring,"ResolveSummon",new SpellCast('솜',SpellKind.Summon,Element.Metal,10,default,1));
                if(Manager.Active==null)throw new InvalidOperationException("Tiger preparation failed: "+Manager.LastFailure);
                Vector3 start=Manager.Active.transform.position;
                A.transform.position=start+Vector3.forward*distance+Vector3.up*.875f;
                B.transform.position=A.transform.position+Vector3.right*.8f;Sync();
            }
            public void UntilAttack()
            {
                int guard=0;while(Manager.Active!=null&&Manager.ActiveTigerPlan==null&&guard++<180)Manager.Tick(.025f);
                if(Manager.ActiveTigerPlan==null)throw new InvalidOperationException("Tiger did not start a planned attack");
            }
            public void Advance(float seconds){while(seconds>0){float dt=Mathf.Min(.025f,seconds);Manager.Tick(dt);seconds-=dt;}}
            public int Count(EnemyVitals target){int count=0;foreach(var hit in Hits)if(hit.Target==target&&hit.AppliedDamage>0)count++;return count;}
            public void Wall(float distance)
            {var wall=Obj("Tiger new wall",Manager.Active.transform.position+Vector3.forward*distance+Vector3.up);wall.AddComponent<BoxCollider>().size=new Vector3(4,4,.15f);temporary.Add(wall);Sync();}
            public void Ceiling()
            {var ceiling=Obj("Tiger raised-pelvis ceiling",Manager.Active.transform.position+Vector3.forward+Vector3.up*(Profile.BodyHeight+.18f));ceiling.AddComponent<BoxCollider>().size=new Vector3(3,.1f,3);temporary.Add(ceiling);Sync();}
            public void GroundGap(float distance,float width)
            {
                Floor.enabled=false;float gap=Manager.Active.transform.position.z+distance;
                foreach(var span in new[]{new Vector2(Origin.z-30,gap),new Vector2(gap+width,Origin.z+30)})
                {var ground=Obj("Tiger gap bank",new Vector3(Origin.x,Origin.y-.1f,(span.x+span.y)*.5f));ground.AddComponent<BoxCollider>().size=new Vector3(60,.2f,span.y-span.x);temporary.Add(ground);}
                Sync();
            }
            public void Sync(){Physics.SyncTransforms();var physics=Scene.GetPhysicsScene();if(physics.IsValid()&&!physics.Equals(Physics.defaultPhysicsScene))physics.Simulate(.001f);}
            public void Dispose()
            {
                if(Manager!=null)Manager.Clear();if(installed.valid)installed.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);
                if(Scene.IsValid())EditorSceneManager.ClosePreviewScene(Scene);
                if(Profile!=null)UnityEngine.Object.DestroyImmediate(Profile);if(Config!=null)UnityEngine.Object.DestroyImmediate(Config);
            }
        }
        static float Horizontal(Vector3 a,Vector3 b)=>Vector3.ProjectOnPlane(a-b,Vector3.up).magnitude;
        static bool Near(float a,float b)=>Math.Abs(a-b)<.004f;
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    }
}
