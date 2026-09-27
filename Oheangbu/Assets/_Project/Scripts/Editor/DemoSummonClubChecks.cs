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
    /// <summary>Real manager/common-damage API diagnostics in an isolated local-physics/NavMesh fixture.</summary>
    public static class DemoSummonClubChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public string[] unverified={"Native combat input", "Final club rig/mesh radius and visual approval",
                "Campaign NavMesh/performance", "Continuous moving-target collision between sampled physical steps"};
        }
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit-mode API diagnostic only");
            var report=new Report();Fixture f=null;
            void Check(bool pass,string name)=>(pass?report.passed:report.failed).Add(name);
            try
            {
                f=new Fixture();Check(!f.Profile.ClubAttackEnabled,"Club combat is opt-in");
                f.Profile.Letter="몸";f.Profile.Element=Element.Earth;f.Profile.ClubAttackEnabled=true;
                Check(f.Profile.TryValidate(out _),"Earth club profile validates");
                f.Profile.TigerAttackEnabled=true;Check(!f.Profile.TryValidate(out _),"Club profile cannot select tiger attacks");f.Profile.TigerAttackEnabled=false;
                float range=f.Profile.ClubRange;f.Profile.ClubRange=-1;Check(!f.Profile.TryValidate(out _),"Negative club range fails validation");f.Profile.ClubRange=range;

                var geometry=new SummonClubAttackPlan(new Vector3(4,2,7),Vector3.forward,2.8f,70,1.5f,10,.8f,.3f,.6f);
                var center=new Vector3(4,2,7);
                Check(geometry.Contains(center+Vector3.forward*2)&&!geometry.Contains(center+Vector3.forward*3)&&
                    !geometry.Contains(center+Vector3.back)&&!geometry.Contains(center+Vector3.forward+Vector3.up*1.6f),
                    "Plan contains radial/angle/vertical bounds without expanding the sector");
                float left=geometry.ContactAt(center+Quaternion.AngleAxis(-45,Vector3.up)*Vector3.forward*2);
                float middle=geometry.ContactAt(center+Vector3.forward*2);
                float right=geometry.ContactAt(center+Quaternion.AngleAxis(45,Vector3.up)*Vector3.forward*2);
                Check(left<middle&&middle<right&&Near(middle,10.95f),"Plan schedules left-to-right contact times, not one simultaneous explosion");

                f.Reset();f.UntilAttack();var plan=f.Manager.ActiveClubPlan;
                Check(plan!=null&&Near(plan.SweepAt-plan.StartedAt,.8f)&&Near(plan.SweepEndAt-plan.SweepAt,.3f)&&Near(plan.EndAt-plan.SweepEndAt,.6f),
                    "Committed sweep separates preparation, sweep, and recovery");
                Vector3 origin=plan.Origin,direction=plan.Direction;
                float aTime=plan.ContactAt(Aim(f.A)),bTime=plan.ContactAt(Aim(f.B)),cTime=plan.ContactAt(Aim(f.C));
                Check(aTime<bTime&&bTime<cTime,"Captured left, middle, and right targets have ordered contacts");
                f.AdvanceTo(plan.SweepAt-.04f);Check(f.Hits.Count==0,"No club damage during preparation");
                float age=f.Manager.ActiveClock.Elapsed;var pose=f.Manager.Active.transform.position;
                for(int i=0;i<30;i++)f.Manager.Tick(0);
                Check(Near(age,f.Manager.ActiveClock.Elapsed)&&pose==f.Manager.Active.transform.position&&f.Hits.Count==0,"Pause preserves pose, clock, and unreleased sweep");
                f.AdvanceTo((aTime+bTime)*.5f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==0&&f.Count(f.C)==0,"Left contact resolves before middle/right contacts");
                f.AdvanceTo((bTime+cTime)*.5f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.C)==0,"Middle contact resolves before the right target");
                f.AdvanceTo(plan.SweepEndAt+.08f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.C)==1&&f.Count(f.Side)==0&&f.Count(f.Far)==0&&f.Count(f.High)==0,
                    "One sweep hits each captured eligible target once and excludes angle/range/height outsiders");
                Check(f.Contacts==3&&f.Hits.Count==3&&f.Hits[0].Attack.AttackId==f.Hits[1].Attack.AttackId&&f.Hits[1].Attack.AttackId==f.Hits[2].Attack.AttackId,
                    "One sweep provenance reaches the shared contact path for its three targets");
                bool provenance=true;foreach(var hit in f.Hits)provenance&=hit.Attack.Source==DamageSource.Summon&&hit.Attack.Element==Element.Earth&&hit.Attack.Instigator==f.Manager.Active;
                Check(provenance&&Near(f.Manager.ClubDamageSnapshot,12),"Sweep power snapshots 1.2 times cast power and retains earth summon provenance");
                Check(f.HitAt(f.A)>=aTime-.003f&&f.HitAt(f.A)<=aTime+.055f&&f.HitAt(f.B)>=bTime-.003f&&f.HitAt(f.B)<=bTime+.055f&&
                    f.HitAt(f.C)>=cTime-.003f&&f.HitAt(f.C)<=cTime+.055f,"Observed common-hit times match scheduled angles within a physical step");
                f.AdvanceTo(plan.EndAt+.15f);Check(f.Hits.Count==3,"Recovery never repeats completed sweep contacts");
                Check(plan.Origin==origin&&plan.Direction==direction,"Origin/direction remain a fixed attack snapshot");

                foreach(int fps in new[]{30,60,120})foreach(float scale in new[]{1f,.25f})
                {
                    f.Reset();f.UntilAttack();float elapsed=0,dt=scale/fps;
                    while(elapsed<1.9f){f.Manager.Tick(dt);elapsed+=dt;}
                    Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.C)==1&&f.Hits.Count==3,
                        fps+" fps / "+scale+" scaled time retains each angular contact once");
                }
                f.Reset();f.UntilAttack();f.Manager.Tick(1.9f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.C)==1,"Large frame delta substeps all scheduled contacts without omission");
                f.Reset();f.UntilAttack();f.A.TakeDamage(f.A.Hp);f.Advance(1.9f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1&&f.Count(f.C)==1,"Captured target death excludes that target and preserves other fixed candidates");
                f.Reset();f.UntilAttack();f.A.TakeDamage(f.A.Hp);f.A.Restore();f.Advance(1.9f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1&&f.Count(f.C)==1,"Same-object respawn cannot inherit an earlier life contact");
                f.Reset();f.UntilAttack();f.B.TakeDamage(f.B.Hp);f.B.Restore();f.Advance(1.9f);
                Check(f.Count(f.B)==0&&f.Count(f.A)==1&&f.Count(f.C)==1,"Primary target respawn does not replace the other captured sweep candidates");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;f.MoveAtAngle(f.A,35);f.Advance(1.9f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1&&f.Count(f.C)==1,"Dodging into a future angle cannot reschedule the original left contact");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;f.AdvanceTo(plan.SweepAt+.12f);f.MoveAtAngle(f.C,-45);f.AdvanceTo(plan.EndAt+.1f);
                Check(f.Count(f.C)==0&&f.Count(f.A)==1&&f.Count(f.B)==1,"Moving behind the already passed sweep angle cannot receive a late contact");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;aTime=plan.ContactAt(Aim(f.A));var aPosition=f.A.transform.position;
                f.A.transform.position+=Vector3.right*5;f.Sync();f.AdvanceTo(aTime+.055f);f.A.transform.position=aPosition;f.Sync();f.AdvanceTo(plan.EndAt+.1f);
                Check(f.Count(f.A)==0,"Leaving during the scheduled contact and re-entering cannot cause a ghost hit");
                f.Reset();f.UntilAttack();f.Side.transform.position=f.C.transform.position;f.Sync();f.Advance(1.9f);
                Check(f.Count(f.Side)==0&&f.Count(f.C)==1,"A late entrant is never added to the captured target list");
                f.Reset();f.UntilAttack();f.C.transform.position+=Vector3.up*3;f.Sync();f.Advance(1.9f);
                Check(f.Count(f.C)==0&&f.Count(f.A)==1&&f.Count(f.B)==1,"Current vertical separation is checked again at contact");
                f.Reset();f.UntilAttack();f.C.transform.position+=Vector3.forward*5;f.Sync();f.Advance(1.9f);
                Check(f.Count(f.C)==0&&f.Count(f.A)==1&&f.Count(f.B)==1,"Current radial separation is checked again at contact");
                f.Reset();f.UntilAttack();f.WallAtAngle(-45);f.Advance(1.9f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1&&f.Count(f.C)==1,"A new local wall blocks only its occluded target through shared damage checks");
                f.Reset();f.UntilAttack();f.WallAcrossFront();f.Advance(1.9f);
                Check(f.Hits.Count==0,"A wall inserted before the sweep prevents all occluded damage");
                f.Reset();f.UntilAttack();f.Wiring.SummonPlayer.position+=Vector3.right*40;f.Advance(1.9f);
                Check(f.Hits.Count==0,"Player leash exit prevents captured contacts");

                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;f.Profile.ClubDamageMultiplier=9;f.Advance(1.9f);
                Check(f.Hits.Count==3&&Near(f.Manager.ClubDamageSnapshot,12),"Changing the profile after summon acceptance cannot mutate the damage snapshot");
                f.Profile.ClubDamageMultiplier=1.2f;
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;f.Manager.ActiveClock.BeginDissolve();f.Manager.Tick(.025f);
                Check(plan.IsCancelled&&f.Hits.Count==0,"Dissolve cancels a captured unreleased sweep");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;f.Manager.Clear();f.Manager.Tick(2);
                Check(plan.IsCancelled&&f.Hits.Count==0&&f.Manager.Active==null,"Clear cancels the sweep and removes its actor");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveClubPlan;var actor=f.Manager.Active;f.Ink.Restore(0);
                Call(f.Wiring,"ResolveSummon",new SpellCast('몸',SpellKind.Summon,Element.Earth,10,default,1));
                Check(f.Manager.Active==actor&&!plan.IsCancelled,"Insufficient ink preserves the original actor and its fixed sweep");
                f.Ink.Restore();Call(f.Wiring,"ResolveSummon",new SpellCast('몸',SpellKind.Summon,Element.Earth,10,default,1));
                Check(f.Manager.Active!=null&&f.Manager.Active!=actor&&plan.IsCancelled,"Accepted replacement cancels the previous sweep and owns one actor");
                f.Advance(21);Check(f.Manager.Active==null,"Twenty-second activity terminates after formation/dissolve");
                f.Profile.ClubRange=1.3f;f.Profile.ClubOriginHeight=.850525f;f.Profile.ClubHeightTolerance=.65f;
                f.Reset();f.MoveAtAngle(f.A,-45,1.15f);f.MoveAtAngle(f.B,0,1.1f);f.MoveAtAngle(f.C,45,1.15f);
                f.UntilAttack();f.Advance(1.9f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.C)==1,
                    "Measured 1.3m club with 0.65m height tolerance hits centre-rooted demo targets");
                f.Reset();f.MoveAtAngle(f.A,-45,1.15f);f.MoveAtAngle(f.B,0,1.1f);f.MoveAtAngle(f.C,45,1.15f);
                var capsule=f.B.gameObject.AddComponent<CapsuleCollider>();capsule.height=1.75f;capsule.radius=.28f;
                f.B.transform.position-=Vector3.up*.875f;capsule.center=Vector3.up*.875f;f.Sync();
                f.UntilAttack();f.Advance(1.9f);
                Check(f.Count(f.B)==1,"Foot-rooted collider uses its actual body centre without enlarging the measured swing");
                UnityEngine.Object.DestroyImmediate(capsule);
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
            public EnemyVitals A,B,C,Side,Far,High;
            public Vector3 Origin=new Vector3(7900,7900,7900);
            public readonly List<EnemyDamageResult> Hits=new List<EnemyDamageResult>();
            public readonly List<float> HitTimes=new List<float>();
            public int Contacts;
            NavMeshData data;NavMeshDataInstance installed;
            readonly List<GameObject> temporary=new List<GameObject>();
            public Fixture()
            {
                try
                {
                    Scene=EditorSceneManager.NewPreviewScene();Config=ScriptableObject.CreateInstance<CombatConfigSO>();Set(Config,"_enemyMaxHp",1000f);
                    Profile=ScriptableObject.CreateInstance<SummonCombatProfile>();
                    var floor=Obj("Club ground",Origin+Vector3.down*.1f);var box=floor.AddComponent<BoxCollider>();box.size=new Vector3(60,.2f,60);
                    var settings=NavMesh.GetSettingsByIndex(0);settings.agentRadius=.4f;settings.agentHeight=2;settings.agentClimb=.3f;settings.agentSlope=35;
                    settings.overrideVoxelSize=true;settings.voxelSize=.15f;
                    data=NavMeshBuilder.BuildNavMeshData(settings,new List<NavMeshBuildSource>{new NavMeshBuildSource{
                        shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(floor.transform.position,Quaternion.identity,Vector3.one),size=box.size,area=0}},
                        new Bounds(Origin,new Vector3(64,8,64)),Vector3.zero,Quaternion.identity);
                    if(data==null)throw new InvalidOperationException("Club fixture NavMesh creation failed");
                    installed=NavMesh.AddNavMeshData(data);if(!installed.valid)throw new InvalidOperationException("Club fixture NavMesh install failed");
                    var template=Obj("Empty diagnostic club summon",Vector3.zero);template.SetActive(false);Profile.PresentationPrefab=template;
                    Profile.FormationSeconds=.2f;Profile.DissolveSeconds=.2f;Profile.CooldownSeconds=10;Profile.BodyHeight=1.6f;
                    var player=Obj("Club player feet",Origin);
                    Wiring=Obj("Club wiring",Origin).AddComponent<CombatLoopWiring>();Set(Wiring,"_config",Config);Set(Wiring,"_playerTransform",player.transform);
                    Ink=new InkPool(Config,null);Wiring.Construct(null,null,null,Ink);Set(Wiring,"_contactVfx",null);
                    Manager=Wiring.gameObject.AddComponent<DemoSummonCombatManager>();Manager.Wiring=Wiring;Manager.Profiles=new[]{Profile};Call(Manager,"Bind");
                    A=Enemy("Club left target");B=Enemy("Club middle primary");C=Enemy("Club right target");
                    Side=Enemy("Club angle outsider");Far=Enemy("Club range outsider");High=Enemy("Club height outsider");
                    Set(Wiring,"_enemies",new[]{A,B,C,Side,Far,High});Call(Wiring,"CollectControllers");
                    Wiring.EnemyDamageResolved+=hit=>{Hits.Add(hit);HitTimes.Add(Manager.ActiveClock?.Elapsed??-1);};
                    Wiring.EnemyHitResolved+=(_,__)=>Contacts++;Sync();
                }
                catch{Dispose();throw;}
            }
            EnemyVitals Enemy(string name){var enemy=Obj(name,Origin+Vector3.right*25).AddComponent<EnemyVitals>();Set(enemy,"_config",Config);enemy.Restore();return enemy;}
            GameObject Obj(string name,Vector3 position){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,Scene);go.transform.position=position;return go;}
            public void Reset()
            {
                Manager.Clear();foreach(var go in temporary)if(go!=null)UnityEngine.Object.DestroyImmediate(go);temporary.Clear();Wiring.SummonPlayer.position=Origin;
                foreach(var enemy in new[]{A,B,C,Side,Far,High}){enemy.Restore();enemy.gameObject.SetActive(true);enemy.transform.position=Origin+Vector3.right*25;}
                Hits.Clear();HitTimes.Clear();Contacts=0;Ink.Restore();Sync();
                Call(Wiring,"ResolveSummon",new SpellCast('몸',SpellKind.Summon,Element.Earth,10,default,1));
                if(Manager.Active==null)throw new InvalidOperationException("Club preparation failed: "+Manager.LastFailure);
                MoveAtAngle(A,-45,2.2f);MoveAtAngle(B,0,1.75f);MoveAtAngle(C,45,2.2f);
                MoveAtAngle(Side,110,3.2f);MoveAtAngle(Far,15,4.5f);MoveAtAngle(High,-15,2.2f);High.transform.position+=Vector3.up*3;Sync();
            }
            public void MoveAtAngle(EnemyVitals enemy,float degrees,float radius=2.2f)
            {
                Vector3 direction=Manager.ActiveClubPlan?.Direction??Manager.Active.transform.forward;
                Vector3 center=Manager.ActiveClubPlan?.Origin??Manager.Active.transform.position;
                center.y=Manager.Active.transform.position.y;
                enemy.transform.position=center+Quaternion.AngleAxis(degrees,Vector3.up)*direction*radius+Vector3.up*.875f;Sync();
            }
            public void UntilAttack()
            {int n=0;while(Manager.Active!=null&&Manager.ActiveClubPlan==null&&n++<160)Manager.Tick(.025f);if(Manager.ActiveClubPlan==null)throw new InvalidOperationException("Club plan did not start");}
            public void Advance(float seconds){while(seconds>0){float dt=Mathf.Min(.025f,seconds);Manager.Tick(dt);seconds-=dt;}}
            public void AdvanceTo(float absolute){if(Manager.ActiveClock!=null)Advance(Mathf.Max(0,absolute-Manager.ActiveClock.Elapsed));}
            public int Count(EnemyVitals enemy){int result=0;foreach(var hit in Hits)if(hit.Target==enemy&&hit.AppliedDamage>0)result++;return result;}
            public float HitAt(EnemyVitals enemy){for(int i=0;i<Hits.Count;i++)if(Hits[i].Target==enemy&&Hits[i].AppliedDamage>0)return HitTimes[i];return -1;}
            public void WallAtAngle(float degrees)
            {
                var plan=Manager.ActiveClubPlan;Vector3 ray=Quaternion.AngleAxis(degrees,Vector3.up)*plan.Direction;
                var wall=Obj("Club local occluder",plan.Origin+ray*1.6f);wall.transform.rotation=Quaternion.LookRotation(ray);
                wall.AddComponent<BoxCollider>().size=new Vector3(.4f,4,.15f);temporary.Add(wall);Sync();
            }
            public void WallAcrossFront()
            {var plan=Manager.ActiveClubPlan;var wall=Obj("Club full wall",plan.Origin+plan.Direction*1.1f);wall.transform.rotation=Quaternion.LookRotation(plan.Direction);
                wall.AddComponent<BoxCollider>().size=new Vector3(6,4,.15f);temporary.Add(wall);Sync();}
            public void Sync(){Physics.SyncTransforms();var physics=Scene.GetPhysicsScene();if(physics.IsValid()&&!physics.Equals(Physics.defaultPhysicsScene))physics.Simulate(.001f);}
            public void Dispose()
            {
                if(Manager!=null)Manager.Clear();if(installed.valid)installed.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);
                if(Scene.IsValid())EditorSceneManager.ClosePreviewScene(Scene);if(Profile!=null)UnityEngine.Object.DestroyImmediate(Profile);if(Config!=null)UnityEngine.Object.DestroyImmediate(Config);
            }
        }
        static Vector3 Aim(EnemyVitals enemy)=>enemy.transform.position;
        static bool Near(float a,float b)=>Math.Abs(a-b)<.004f;
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    }
}
