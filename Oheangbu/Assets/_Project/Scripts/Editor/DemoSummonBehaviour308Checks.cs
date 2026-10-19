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
    /// <summary>#308 D308-115 summon behaviours (곰 root mark · 놈 judge · 몸 beat · 솜 hunt · 옴 ranged ward) in an isolated
    /// local-physics/NavMesh fixture, through the real manager, enemy damage and player strike doorway.</summary>
    public static class DemoSummonBehaviour308Checks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed=new List<string>(),failed=new List<string>();
            public string[] unverified={"Play in the main world (native input, real enemies, campaign NavMesh)",
                "Legacy EnemyController attack edge and authored AttackPresentationEnded (fixture enemies have no controller)",
                "Riposte301 instigator path into the beat", "Root/sin ink marks and the ward splash as seen on screen",
                "Beat side spot beside a lock-on target", "Cheongryong / SouthGate strikes (TBD)"};
        }

        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Edit-mode API diagnostic only");
            var report=new Report();Fixture f=null;
            void Check(bool pass,string name)=>(pass?report.passed:report.failed).Add(name);
            try
            {
                // Pure rules.
                bool threw=false;try{new WardCharges308(0,1);}catch(ArgumentOutOfRangeException){threw=true;}
                bool threwRecharge=false;try{new WardCharges308(2,0);}catch(ArgumentOutOfRangeException){threwRecharge=true;}
                Check(threw&&threwRecharge,"Ward charges refuse zero charges or a non-positive recharge");
                var ward=new WardCharges308(2,8);
                bool s1=ward.TrySpend(0),c1=ward.Charges==1&&Near(ward.NextAt,8),s2=ward.TrySpend(1),s3=ward.TrySpend(2);
                Check(s1&&c1&&s2&&!s3&&ward.Charges==0,"Ward spends one charge per block and refuses when empty; recharge counts from the first spend");
                ward.Tick(8);bool back1=ward.Charges==1&&Near(ward.NextAt,16);ward.Tick(16);
                Check(back1&&ward.Charges==2&&float.IsPositiveInfinity(ward.NextAt),"Ward recharges one charge per period back to full");
                Check(SummonHuntRule308.Better(.1f,100,.2f,1)&&SummonHuntRule308.Better(.2f,1,.2f,4)&&!SummonHuntRule308.Better(.2f,5,.2f,4)&&
                    !SummonHuntRule308.Better(.3f,0,.2f,4),"Hunt prefers the lowest HP fraction, nearer on a tie");
                Check(SummonBeatRule308.CanQueue(false,3,3)&&!SummonBeatRule308.CanQueue(true,9,3)&&!SummonBeatRule308.CanQueue(false,2,3),
                    "Beat holds one pending strike and waits out its cooldown");
                var mark=new SummonMark308(5,3);
                Check(mark.Live(4.9f,3)&&!mark.Live(5,3)&&!mark.Live(1,4),"A mark ends at its time or with the enemy's life");

                f=new Fixture();
                var fresh=f.Profile;
                Check(!fresh.RootMark308Enabled&&!fresh.JudgeEnabled&&!fresh.BeatEnabled&&!fresh.HuntEnabled&&!fresh.WardEnabled,
                    "Every D308-115 behaviour is off by default");

                // 곰 root mark.
                f.Configure("곰",Element.Wood,p=>p.RootMark308Enabled=true);
                Check(f.Profile.TryValidate(out string rootError),"Root mark profile validates "+rootError);
                f.Profile.Letter="놈";f.Profile.Element=Element.Fire;f.Profile.FlameAttackEnabled=true;
                Check(!f.Profile.TryValidate(out _),"Root mark is exclusive to 곰");
                f.Configure("곰",Element.Wood,p=>p.RootMark308Enabled=true);
                f.Cast('곰',Element.Wood);f.Advance(.3f);var actor=f.Actor;
                Check(f.Manager.BehaviourSubscriptions308==f.Enemies.Length,"Root mark watches every registered target");
                f.PlaceFromPlayer(f.B,-15,0);
                Call(f.Manager,"MarkRoot308",actor,f.B);
                Check(f.Manager.RootMarksSet==1&&(f.Manager.Mark308(f.B)&128)!=0&&(f.Manager.Mark308(f.B)&256)==0,"MarkRoot308 sets the root bit 128 only");
                f.B.TakeDamage(5,AttackProvenance.Create(f.Manager.Active,DamageSource.Summon,Element.Wood));f.Advance(.05f);
                Check(f.Manager.RootBursts==0&&(f.Manager.Mark308(f.B)&128)!=0,"A summon hit never bursts the root mark");
                int before=f.SummonHits(f.B);
                f.B.TakeDamage(5,AttackProvenance.Create(f.PlayerObject,DamageSource.PlayerDirect,Element.Fire));
                Check(f.Manager.RootBursts==0,"The burst is queued, not applied inside the damage event");
                f.Advance(.05f);
                float basePower=(float)Field(actor,"BasePower");
                Check(f.Manager.RootBursts==1&&f.Manager.Mark308(f.B)==0&&f.SummonHits(f.B)==before+1&&
                    NearRatio(f.LastSummonDamage(f.B),basePower*f.Profile.RootMarkBurstMultiplier),"A player hit on a marked target bursts once for base power x 0.6 and spends the mark");
                f.B.TakeDamage(5,AttackProvenance.Create(f.PlayerObject,DamageSource.PlayerDirect,Element.Fire));f.Advance(.05f);
                Check(f.Manager.RootBursts==1,"An unmarked target never bursts");
                Call(f.Manager,"MarkRoot308",actor,f.B);f.Advance(f.Profile.RootMarkSeconds+.1f);
                Check(f.Manager.Mark308(f.B)==0&&f.Manager.BehaviourMarks308==0,"The root mark expires after its lifetime");
                Call(f.Manager,"MarkRoot308",actor,f.B);f.B.TakeDamage(f.B.Hp);f.B.Restore();
                Check(f.Manager.Mark308(f.B)==0,"A respawned enemy does not keep the previous life's mark");
                f.PlaceFromSummon(f.A,1.6f,0);int marks=f.Manager.RootMarksSet,bursts=f.Manager.RootBursts;
                f.Until(()=>f.Manager.RootMarksSet>marks,6);
                Check(f.Manager.RootMarksSet>marks&&(f.Manager.Mark308(f.A)&128)!=0&&f.Manager.RootBursts==bursts,"The summon's own confirmed hit marks its target");
                f.Manager.Clear();f.ExpectReleased(Check,"root");

                // 놈 judge.
                f.Configure("놈",Element.Fire,p=>{p.FlameAttackEnabled=true;p.JudgeEnabled=true;});
                Check(f.Profile.TryValidate(out string judgeError),"Judge profile validates "+judgeError);
                f.Profile.FlameAttackEnabled=false;Check(!f.Profile.TryValidate(out _),"Judge needs the flame fan");f.Profile.FlameAttackEnabled=true;
                f.Cast('놈',Element.Fire);f.Advance(.3f);actor=f.Actor;
                f.PlaceFromSummon(f.A,3.2f,-.6f);f.PlaceFromSummon(f.B,3.2f,.6f);
                f.Advance(3);
                Check(f.Manager.JudgeShots==0&&f.SummonHits(f.A)==0&&f.SummonHits(f.B)==0,"Without a sin mark the judge never burns");
                Call(f.Manager,"OnAttackEnded308",AttackProvenance.Create(f.A,DamageSource.Enemy,null),true);f.Advance(.05f);
                Check(f.Manager.SinMarksSet==0,"A cancelled enemy attack sets no sin mark");
                Call(f.Manager,"OnAttackEnded308",AttackProvenance.Create(f.PlayerObject,DamageSource.Enemy,null),false);f.Advance(.05f);
                Check(f.Manager.SinMarksSet==0,"An attack without an enemy instigator sets no sin mark");
                Call(f.Manager,"OnAttackEnded308",AttackProvenance.Create(f.A,DamageSource.Enemy,null),false);
                Check(f.Manager.SinMarksSet==0,"The sin mark is queued, not set inside the event");
                f.Advance(.05f);
                Check(f.Manager.SinMarksSet==1&&(f.Manager.Mark308(f.A)&256)!=0&&(f.Manager.Mark308(f.A)&128)==0&&f.Manager.Mark308(f.B)==0,
                    "A finished enemy attack sets the sin bit 256 on that enemy only");
                SetField(actor,"Target",f.B);bool allowB=(bool)Call(f.Manager,"JudgeAllows308",actor);
                SetField(actor,"Target",f.A);bool allowA=(bool)Call(f.Manager,"JudgeAllows308",actor);
                float next=(float)Field(actor,"JudgeNextAt");SetField(actor,"JudgeNextAt",f.Manager.ActiveClock.Elapsed+5);
                bool allowWait=(bool)Call(f.Manager,"JudgeAllows308",actor);SetField(actor,"JudgeNextAt",next);
                Check(!allowB&&allowA&&!allowWait,"The judge fires only at a marked target and once per interval");
                f.Until(()=>f.SummonHits(f.A)>0,6);f.Advance(.6f);
                Check(f.Manager.JudgeShots==1&&f.SummonHits(f.A)>=1&&f.SummonHits(f.B)==0&&(f.Manager.Mark308(f.A)&256)==0,
                    "The judge's flame burns the marked enemy, spares the unmarked neighbour and spends the mark");
                f.Manager.Clear();f.ExpectReleased(Check,"judge");

                // 몸 beat.
                f.Configure("몸",Element.Earth,p=>{p.ClubAttackEnabled=true;p.BeatEnabled=true;});
                Check(f.Profile.TryValidate(out string beatError),"Beat profile validates "+beatError);
                float delay=f.Profile.BeatDelay;f.Profile.BeatDelay=f.Profile.BeatCooldown;Check(!f.Profile.TryValidate(out _),"Beat delay must stay below its cooldown");f.Profile.BeatDelay=delay;
                f.Cast('몸',Element.Earth);f.Advance(.3f);actor=f.Actor;
                Check(Get(f.Manager,"parryHooked308")!=null,"Beat listens to parry counters while active");
                f.PlaceFromSummon(f.A,1.6f,0);
                f.A.TakeDamage(5,AttackProvenance.Create(f.Manager.Active,DamageSource.Summon,Element.Earth));f.Advance(.05f);
                Check(f.Manager.BeatTriggers==0,"A summon hit never triggers the beat");
                Call(f.Manager,"OnParryCountered308",f.A,new ParryImpactResult(ParryOutcome.Half,Element.Earth,f.A.transform.position,
                    AttackProvenance.Create(f.A,DamageSource.Enemy,null)),0f,false);f.Advance(.05f);
                Check(f.Manager.BeatTriggers==0,"A half parry does not trigger the beat");
                Call(f.Manager,"OnParryCountered308",f.A,new ParryImpactResult(ParryOutcome.Success,Element.Earth,f.A.transform.position,
                    AttackProvenance.Create(f.A,DamageSource.Enemy,null)),.5f,false);
                Check(f.Manager.BeatTriggers==0,"The parry beat is queued, not struck inside the event");
                f.Advance(.05f);before=f.SummonHits(f.A);float due=(float)Field(actor,"BeatDueAt");
                Check(f.Manager.BeatTriggers==1&&f.Manager.BeatPending308&&f.Manager.BeatHits==0,"A successful parry queues one beat");
                f.AdvanceTo(due-.06f);Check(f.Manager.BeatHits==0,"The beat waits for its delay");
                f.AdvanceTo(due+.06f);float clubPower=(float)Field(actor,"ClubPower");
                Check(f.Manager.BeatHits==1&&!f.Manager.BeatPending308&&f.SummonHits(f.A)>before&&
                    f.HasSummonDamage(f.A,clubPower*f.Profile.BeatMultiplier),"After the delay the beat lands once for club power x 1.8");
                f.A.TakeDamage(5,AttackProvenance.Create(f.PlayerObject,DamageSource.Retaliation,null));f.Advance(.05f);
                Check(f.Manager.BeatTriggers==1,"A retaliation inside the cooldown does not queue another beat");
                f.Advance(f.Profile.BeatCooldown);
                f.A.TakeDamage(5,AttackProvenance.Create(f.PlayerObject,DamageSource.Retaliation,null));f.Advance(.05f);
                Check(f.Manager.BeatTriggers==2&&f.Manager.BeatPending308,"After the cooldown a retaliation hit queues the next beat");
                f.PlaceFromSummon(f.A,8,0);f.Advance(f.Profile.BeatDelay+.06f);
                Check(f.Manager.BeatMisses==1&&f.Manager.BeatHits==1,"A target that left club range is a miss, not a hit");
                f.Manager.Clear();f.ExpectReleased(Check,"beat");
                Check(Get(f.Manager,"parryHooked308")==null,"Clear unhooks the parry counter");

                // 솜 hunt.
                f.Configure("솜",Element.Metal,p=>{p.TigerAttackEnabled=true;p.HuntEnabled=true;});
                Check(f.Profile.TryValidate(out string huntError),"Hunt profile validates "+huntError);
                f.Cast('솜',Element.Metal);f.Advance(.3f);actor=f.Actor;
                f.PlaceFromSummon(f.A,2,0);f.PlaceFromSummon(f.B,5,2);
                var args=new object[]{actor,null};bool decided=(bool)Call(f.Manager,"TryBehaviourTarget308",args);
                Check(!decided&&f.Manager.HuntPicks==0,"With nobody weakened the hunt leaves targeting to the normal rule");
                f.B.TakeDamage(f.B.MaxHp*.8f);
                args=new object[]{actor,null};decided=(bool)Call(f.Manager,"TryBehaviourTarget308",args);
                Check(decided&&ReferenceEquals(args[1],f.B)&&f.Manager.HuntPicks==1,"The hunt picks the weakened enemy over the nearer healthy one");
                Check(Near((float)Call(f.Manager,"HuntScale308",actor,f.B),f.Profile.HuntMultiplier)&&Near((float)Call(f.Manager,"HuntScale308",actor,f.A),1),
                    "Only a weakened target takes the hunt multiplier");
                f.Until(()=>f.FirstSummonTarget()!=null,8);
                Check(f.FirstSummonTarget()==f.B&&f.Manager.HuntBoostedHits>=1,"The tiger's first claw goes to the weakened enemy, boosted");
                f.Manager.Clear();f.ExpectReleased(Check,"hunt");

                // 옴 ranged ward.
                f.Configure("옴",Element.Water,p=>{p.WaterAttackEnabled=true;p.WardEnabled=true;});
                Check(f.Profile.TryValidate(out string wardError),"Ward profile validates "+wardError);
                f.Profile.Letter="몸";f.Profile.Element=Element.Earth;f.Profile.WaterAttackEnabled=false;f.Profile.ClubAttackEnabled=true;
                Check(!f.Profile.TryValidate(out _),"The ward is exclusive to 옴");
                f.Configure("옴",Element.Water,p=>{p.WaterAttackEnabled=true;p.WardEnabled=true;});
                Check(!f.Player.HasRangedWard308,"No ward is bound before the summon");
                f.Cast('옴',Element.Water);
                Check(f.Player.HasRangedWard308&&f.Manager.WardBound308&&f.Manager.WardCharges308==f.Profile.WardCharges,"The water summon binds the player's ranged ward, full");
                f.PlaceFromPlayer(f.A,0,10);float hp=f.PlayerHp;
                bool landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Ranged);
                Check(landed&&f.PlayerHp<hp&&f.Manager.WardBlocks==0,"During formation the ward does not block yet");
                f.Advance(.3f);f.Profile.WardEnabled=false;hp=f.PlayerHp;
                landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Ranged);
                Check(!landed&&Near(f.PlayerHp,hp)&&f.Manager.WardBlocks==1&&f.Manager.WardCharges308==f.Profile.WardCharges-1,
                    "A ranged strike is stopped and spends one charge (profile edits after acceptance change nothing)");
                f.Profile.WardEnabled=true;hp=f.PlayerHp;
                landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Melee);
                Check(landed&&f.PlayerHp<hp&&f.Manager.WardBlocks==1,"A melee strike passes the ward");
                hp=f.PlayerHp;landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.ElementalRanged);
                Check(!landed&&Near(f.PlayerHp,hp)&&f.Manager.WardBlocks==2&&f.Manager.WardCharges308==0,"An elemental ranged strike is stopped too");
                hp=f.PlayerHp;landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Ranged);
                Check(landed&&f.PlayerHp<hp&&f.Manager.WardBlocks==2,"With no charge left the ranged strike lands");
                f.Advance(f.Profile.WardRechargeSeconds+.1f);hp=f.PlayerHp;
                landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Ranged);
                Check(!landed&&Near(f.PlayerHp,hp)&&f.Manager.WardBlocks==3,"One charge returns after the recharge time");
                f.Manager.Clear();f.ExpectReleased(Check,"ward");
                hp=f.PlayerHp;landed=EnemyStrike308.Deliver(f.Player,f.A,10,IncomingDamageKind.Ranged);
                Check(landed&&f.PlayerHp<hp,"After the summon is gone ranged strikes land again");
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
            public PlayerVitals Player;
            public GameObject PlayerObject;
            public InkPool Ink;
            public EnemyVitals A,B;
            public EnemyVitals[] Enemies;
            public Vector3 Origin=new Vector3(7900,7900,7900);
            public readonly List<EnemyDamageResult> Hits=new List<EnemyDamageResult>();
            NavMeshData data;NavMeshDataInstance installed;
            public Fixture()
            {
                try
                {
                    Scene=EditorSceneManager.NewPreviewScene();Config=ScriptableObject.CreateInstance<CombatConfigSO>();Set(Config,"_enemyMaxHp",1000f);
                    Profile=ScriptableObject.CreateInstance<SummonCombatProfile>();
                    var floor=Obj("Behaviour ground",Origin+Vector3.down*.1f);var box=floor.AddComponent<BoxCollider>();box.size=new Vector3(60,.2f,60);
                    var settings=NavMesh.GetSettingsByIndex(0);settings.agentRadius=.4f;settings.agentHeight=2;settings.agentClimb=.3f;settings.agentSlope=35;
                    settings.overrideVoxelSize=true;settings.voxelSize=.15f;
                    data=NavMeshBuilder.BuildNavMeshData(settings,new List<NavMeshBuildSource>{new NavMeshBuildSource{
                        shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(floor.transform.position,Quaternion.identity,Vector3.one),size=box.size,area=0}},
                        new Bounds(Origin,new Vector3(64,8,64)),Vector3.zero,Quaternion.identity);
                    if(data==null)throw new InvalidOperationException("Behaviour fixture NavMesh creation failed");
                    installed=NavMesh.AddNavMeshData(data);if(!installed.valid)throw new InvalidOperationException("Behaviour fixture NavMesh install failed");
                    var template=Obj("Empty diagnostic behaviour summon",Vector3.zero);template.SetActive(false);
                    PlayerObject=Obj("Behaviour player feet",Origin);Player=PlayerObject.AddComponent<PlayerVitals>();Player.Restore();
                    Wiring=Obj("Behaviour wiring",Origin).AddComponent<CombatLoopWiring>();Set(Wiring,"_config",Config);Set(Wiring,"_playerTransform",PlayerObject.transform);
                    Ink=new InkPool(Config,null);Wiring.Construct(null,null,null,Ink);Set(Wiring,"_contactVfx",null);
                    Manager=Wiring.gameObject.AddComponent<DemoSummonCombatManager>();Manager.Wiring=Wiring;Manager.PlayerVitals=Player;Manager.Profiles=new[]{Profile};
                    Profile.PresentationPrefab=template;
                    Call(Manager,"Bind");
                    A=Enemy("Behaviour target A");B=Enemy("Behaviour target B");Enemies=new[]{A,B};
                    Set(Wiring,"_enemies",Enemies);Call(Wiring,"CollectControllers");
                    foreach(var enemy in Enemies)enemy.DamageResolved+=hit=>Hits.Add(hit);
                    Sync();
                }
                catch{Dispose();throw;}
            }
            EnemyVitals Enemy(string name){var enemy=Obj(name,Origin+Vector3.right*25).AddComponent<EnemyVitals>();Set(enemy,"_config",Config);enemy.Restore();return enemy;}
            GameObject Obj(string name,Vector3 position){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,Scene);go.transform.position=position;return go;}
            public object Actor=>Get(Manager,"active");
            public float PlayerHp=>Player.Hp01*Player.MaxHp;
            // A fresh profile state per behaviour: one letter, one attack path, one behaviour switch.
            public void Configure(string letter,Element element,Action<SummonCombatProfile> set)
            {
                Manager.Clear();
                var template=Profile.PresentationPrefab;var old=Profile;
                Profile=ScriptableObject.CreateInstance<SummonCombatProfile>();UnityEngine.Object.DestroyImmediate(old);
                Profile.PresentationPrefab=template;Profile.FormationSeconds=.2f;Profile.DissolveSeconds=.2f;Profile.CooldownSeconds=10;Profile.BodyHeight=1.6f;
                Profile.Letter=letter;Profile.Element=element;set(Profile);Manager.Profiles=new[]{Profile};
            }
            public void Cast(char letter,Element element)
            {
                Manager.Clear();PlayerObject.transform.SetPositionAndRotation(Origin,Quaternion.identity);
                foreach(var enemy in Enemies){enemy.Restore();enemy.gameObject.SetActive(true);enemy.transform.position=Origin+Vector3.right*25;}
                Hits.Clear();Ink.Restore();Player.Restore();Sync();
                Call(Wiring,"ResolveSummon",new SpellCast(letter,SpellKind.Summon,element,10,default,1));
                if(Manager.Active==null)throw new InvalidOperationException("Behaviour summon preparation failed: "+Manager.LastFailure);
            }
            public void PlaceFromSummon(EnemyVitals enemy,float forward,float right)
            {
                var root=Manager.Active.transform;
                enemy.transform.position=root.position+root.forward*forward+root.right*right+Vector3.up*.875f;Sync();
            }
            public void PlaceFromPlayer(EnemyVitals enemy,float right,float forward)
            {enemy.transform.position=Origin+Vector3.right*right+Vector3.forward*forward+Vector3.up*.875f;Sync();}
            public void Advance(float seconds){while(seconds>0&&Manager.Active!=null){float dt=Mathf.Min(.025f,seconds);Manager.Tick(dt);seconds-=dt;}}
            public void AdvanceTo(float absolute){if(Manager.ActiveClock!=null)Advance(Mathf.Max(0,absolute-Manager.ActiveClock.Elapsed));}
            public void Until(Func<bool> done,float max){while(max>0&&Manager.Active!=null&&!done()){Manager.Tick(.025f);max-=.025f;}}
            public int SummonHits(EnemyVitals enemy){int n=0;foreach(var hit in Hits)if(hit.Target==enemy&&hit.Attack.Source==DamageSource.Summon&&hit.AppliedDamage>0)n++;return n;}
            public float LastSummonDamage(EnemyVitals enemy){for(int i=Hits.Count-1;i>=0;i--)if(Hits[i].Target==enemy&&Hits[i].Attack.Source==DamageSource.Summon)return Hits[i].AppliedDamage;return -1;}
            public bool HasSummonDamage(EnemyVitals enemy,float amount){foreach(var hit in Hits)if(hit.Target==enemy&&hit.Attack.Source==DamageSource.Summon&&NearRatio(hit.AppliedDamage,amount))return true;return false;}
            public EnemyVitals FirstSummonTarget(){foreach(var hit in Hits)if(hit.Attack.Source==DamageSource.Summon&&hit.AppliedDamage>0)return hit.Target;return null;}
            public void ExpectReleased(Action<bool,string> check,string name)
            {
                check(Manager.Active==null&&Manager.BehaviourSubscriptions308==0&&Manager.BehaviourMarks308==0&&!Manager.WardBound308&&
                    !Manager.BeatPending308&&!Player.HasRangedWard308,"Clear releases every "+name+" subscription, mark and ward binding");
            }
            public void Sync(){Physics.SyncTransforms();var physics=Scene.GetPhysicsScene();if(physics.IsValid()&&!physics.Equals(Physics.defaultPhysicsScene))physics.Simulate(.001f);}
            public void Dispose()
            {
                if(Manager!=null)Manager.Clear();if(installed.valid)installed.Remove();if(data!=null)UnityEngine.Object.DestroyImmediate(data);
                if(Scene.IsValid())EditorSceneManager.ClosePreviewScene(Scene);if(Profile!=null)UnityEngine.Object.DestroyImmediate(Profile);if(Config!=null)UnityEngine.Object.DestroyImmediate(Config);
            }
        }
        static bool Near(float a,float b)=>Math.Abs(a-b)<.004f;
        static bool NearRatio(float a,float b)=>b>0&&Math.Abs(a-b)<=Math.Max(.004f,b*.01f);
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static object Get(object target,string name)=>target.GetType().GetField(name,Any).GetValue(target);
        static object Field(object target,string name)=>target.GetType().GetField(name,Any).GetValue(target);
        static void SetField(object target,string name,object value)=>target.GetType().GetField(name,Any).SetValue(target,value);
        static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Any).Invoke(target,args);
    }
}
