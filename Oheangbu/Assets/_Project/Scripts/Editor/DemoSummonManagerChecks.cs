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
    // Production ResolveSummon + manager API in a disposable world. This is not an input/playthrough test.
    public static class DemoSummonManagerChecks
    {
        [Serializable] sealed class Report
        {
            public string status, mode="Isolated Editor API diagnostic; no actual player input";
            public List<string> passed=new List<string>(),failed=new List<string>();
            public int accepted,preparationFailures,attacks,hits,pathQueries;
            public string[] unverified={"Actual player recognition/input and moving campaign navigation",
                "Approved deer animation and visual quality", "Vehicle boarding and player-death event integration in campaign",
                "Frame performance and manual combat feel"};
        }
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Run only in isolated Edit mode.");
            var report=new Report();var scene=EditorSceneManager.NewPreviewScene();
            var config=ScriptableObject.CreateInstance<CombatConfigSO>();
            var profile=ScriptableObject.CreateInstance<SummonCombatProfile>();
            NavMeshData data=null;NavMeshDataInstance installed=default;DemoSummonCombatManager manager=null;
            void Check(bool pass,string name)=>(pass?report.passed:report.failed).Add(name);
            try
            {
                Vector3 origin=new Vector3(5000,5000,5000);
                var ground=Obj("Owned ground",scene,origin+Vector3.down*.1f);
                var groundCollider=ground.AddComponent<BoxCollider>();groundCollider.size=new Vector3(60,.2f,60);
                var settings=NavMesh.GetSettingsByIndex(0);settings.agentRadius=.4f;settings.agentHeight=2.7f;
                settings.agentClimb=.3f;settings.agentSlope=35;settings.overrideVoxelSize=true;settings.voxelSize=.15f;
                var sources=new List<NavMeshBuildSource>{new NavMeshBuildSource
                {shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(ground.transform.position,Quaternion.identity,Vector3.one),size=groundCollider.size,area=0}};
                data=NavMeshBuilder.BuildNavMeshData(settings,sources,new Bounds(origin,new Vector3(64,8,64)),Vector3.zero,Quaternion.identity);
                if(data==null)throw new InvalidOperationException("Fixture NavMesh build failed.");
                installed=NavMesh.AddNavMeshData(data);
                Check(installed.valid&&NavMesh.SamplePosition(origin,out _,1,NavMesh.AllAreas),"Disposable NavMesh has a connected ground surface");

                var template=Obj("Diagnostic model (not a new approved appearance)",scene,Vector3.zero);template.SetActive(false);
                var visual=GameObject.CreatePrimitive(PrimitiveType.Cube);visual.transform.SetParent(template.transform,false);
                visual.transform.localPosition=Vector3.up*.75f;visual.transform.localScale=new Vector3(.8f,1.5f,1.8f);
                profile.PresentationPrefab=template;profile.FormationSeconds=.2f;profile.DissolveSeconds=.2f;
                profile.WindupSeconds=.2f;profile.CooldownSeconds=.3f;profile.BodyHeight=1.7f;
                string profileBefore=JsonUtility.ToJson(profile),configBefore=JsonUtility.ToJson(config);
                var player=Obj("Player feet",scene,origin);
                var wiring=Obj("Summon wiring",scene,origin).AddComponent<CombatLoopWiring>();
                Set(wiring,"_config",config);Set(wiring,"_playerTransform",player.transform);Set(wiring,"_contactVfx",null);
                var ink=new InkPool(config,null);wiring.Construct(null,null,null,ink);
                manager=wiring.gameObject.AddComponent<DemoSummonCombatManager>();manager.Wiring=wiring;manager.Profiles=new[]{profile};
                Call(manager,"Bind");Sync(scene);
                var cast=new SpellCast('곰',SpellKind.Summon,Element.Wood,10,default,1);
                void Cast()=>Call(wiring,"ResolveSummon",cast);
                float cost=config.SpellInkCost*2;
                int signals=0;wiring.SummonAccepted+=(_,__,___)=>signals++;
                float upgrade=1.1f;wiring.PlayerDamageScale=_=>upgrade;
                ink.Restore();Cast();
                Check(manager.Active!=null&&!manager.HasPrepared&&manager.AcceptedSummons==1&&signals==1&&Near(ink.Value,1-cost),
                    "Actual ResolveSummon prepares then spends exactly two spell costs and accepts one actor");
                if(manager.Active==null)throw new InvalidOperationException("First summon failed: "+manager.LastFailure);
                Check(Near(manager.DamageSnapshot,8.8f),"Accepted summon snapshots cast power times 10-percent upgrade times profile multiplier");
                Check(manager.Active.GetComponentsInChildren<Collider>(true).Length==0&&manager.Active.GetComponentsInChildren<NavMeshAgent>(true).Length==0,
                    "Spawned actor contains no traffic collider or active NavMeshAgent");
                var old=manager.Active;float oldElapsed=manager.ActiveClock.Elapsed;
                manager.Tick(0);Check(manager.Active==old&&Near(manager.ActiveClock.Elapsed,oldElapsed),"Tick zero preserves paused actor, clock and pending lifetime");
                upgrade=1.2f;Check(Near(manager.DamageSnapshot,8.8f),"Changing upgrade after acceptance does not retroactively strengthen active actor");

                ink.Restore(cost*.5f);float balance=ink.Value;Cast();
                Check(manager.Active==old&&!manager.HasPrepared&&Near(ink.Value,balance)&&signals==1,
                    "Insufficient ink destroys only prepared candidate and preserves prior summon, balance and acceptance count");
                var obstruction=Obj("Solid denied placement region",scene,origin+Vector3.up*2);
                var block=obstruction.AddComponent<BoxCollider>();block.size=new Vector3(20,4,20);Sync(scene);
                ink.Restore();Cast();
                Check(manager.Active==old&&!manager.HasPrepared&&Near(ink.Value,1)&&signals==1&&manager.PrepareFailures>0,
                    "Blocked spawn space fails before spending and preserves existing summon");
                UnityEngine.Object.DestroyImmediate(obstruction);Sync(scene);
                ink.Restore();Cast();
                Check(old==null&&manager.Active!=null&&manager.AcceptedSummons==2&&manager.ReleasedSummons==1&&signals==2&&Near(manager.DamageSnapshot,9.6f),
                    "Successful replacement destroys exactly the old actor and takes the new upgrade snapshot");
                for(int i=0;i<3;i++){ink.Restore();Cast();}
                int actorRoots=0;foreach(var root in scene.GetRootGameObjects())if(root.name.StartsWith("DemoSummon_",StringComparison.Ordinal))actorRoots++;
                Check(actorRoots==1&&manager.AcceptedSummons-manager.ReleasedSummons==1&&!manager.HasPrepared,
                    "Repeated successful casts retain exactly one actor and no prepared roots");

                var target=Obj("Registered enemy",scene,manager.Active.transform.position+Vector3.forward*1.7f).AddComponent<EnemyVitals>();
                Set(target,"_config",config);target.Restore();Set(wiring,"_enemies",new[]{target});Call(wiring,"CollectControllers");
                float hp=target.Hp;int contacts=0;wiring.EnemyHitResolved+=(_,__)=>contacts++;
                manager.Tick(profile.FormationSeconds+.05f);
                Check(manager.AttackStarts==1&&Near(target.Hp,hp),"Formation finishes before a real manager windup starts; telegraph does not deal early damage");
                Vector3 attackPosition=manager.Active.transform.position;
                manager.Tick(profile.WindupSeconds+.01f);
                Check(manager.ConfirmedHits==1&&Near(hp-target.Hp,9.6f)&&contacts==1,
                    "Manager impact calls the shared production hit/contact path once with snapshot power");
                manager.Tick(.1f);
                Check(Vector3.Distance(attackPosition,manager.Active.transform.position)<.001f&&manager.ConfirmedHits==1,
                    "Post-impact recovery stays stationary and cannot duplicate the same hit");
                // Block after windup begins; a due failed hit is consumed rather than deferred behind the wall.
                int guard=0;while(manager.ActiveClock.PendingAttackId==0&&guard++<30)manager.Tick(.05f);
                var wall=Obj("Mid-windup wall",scene,(manager.Active.transform.position+target.transform.position)*.5f+Vector3.up);
                var wallCollider=wall.AddComponent<BoxCollider>();wallCollider.size=new Vector3(3,3,.2f);Sync(scene);
                hp=target.Hp;int consumed=manager.ConsumedStrikes;manager.Tick(profile.WindupSeconds+.05f);
                Check(Near(target.Hp,hp)&&manager.ActiveClock.PendingAttackId==0&&manager.ConsumedStrikes==consumed+1,
                    "A physical wall introduced during windup consumes the strike without through-wall damage");
                UnityEngine.Object.DestroyImmediate(wall);Sync(scene);
                manager.Tick(.05f);Check(Near(target.Hp,hp),"Removing wall cannot revive the already-consumed strike");

                manager.Clear();Check(manager.Active==null&&!manager.HasPrepared,"Explicit rest/teardown clear removes both active and prepared ownership");

                // Real encounter roots are capsule centres, .875m above their feet. They still need a
                // path to terrain, not a tiny NavMesh sample around the elevated transform pivot.
                ink.Restore();Cast();target.Restore();
                target.transform.position=manager.Active.transform.position+Vector3.forward*8+Vector3.up*.875f;
                var enemyCollider=target.gameObject.AddComponent<CapsuleCollider>();enemyCollider.height=1.75f;enemyCollider.radius=.3f;
                Sync(scene);int initialHits=manager.ConfirmedHits,initialQueries=manager.PathQueries,initialFailures=manager.PathFailures;
                Vector3 approachStart=manager.Active.transform.position;hp=target.Hp;
                guard=0;while(manager.ConfirmedHits==initialHits&&manager.Active!=null&&guard++<100)manager.Tick(.05f);
                Check(manager.Active!=null&&Vector3.Distance(approachStart,manager.Active.transform.position)>2&&
                    manager.ConfirmedHits==initialHits+1&&target.Hp<hp&&manager.PathQueries>initialQueries&&manager.PathFailures==initialFailures,
                    "Elevated .875m encounter root with capsule collider projects to owned ground, approaches and receives actual hit");
                manager.Clear();

                // Endpoint boxes are both clear, but the long body sweeps across a post halfway through a turn.
                Vector3 turnFeet=origin+Vector3.right*12+Vector3.up*.035f;
                var post=Obj("Angular sweep obstruction",scene,turnFeet+new Vector3(.64f,1,.64f));
                var postCollider=post.AddComponent<BoxCollider>();postCollider.size=new Vector3(.12f,1.5f,.12f);Sync(scene);
                bool zeroClear=(bool)Call(manager,"VolumeClear",profile,turnFeet,Quaternion.identity,true,0f,0f);
                bool ninetyClear=(bool)Call(manager,"VolumeClear",profile,turnFeet,Quaternion.Euler(0,90,0),true,0f,0f);
                bool swept=(bool)Call(manager,"SweptPoseClear",profile,turnFeet,Quaternion.identity,turnFeet,Quaternion.Euler(0,90,0));
                Check(zeroClear&&ninetyClear&&!swept,"Angular swept footprint catches a post missed by both clear endpoint poses");
                UnityEngine.Object.DestroyImmediate(post);Sync(scene);
                Check((bool)Call(manager,"SweptPoseClear",profile,turnFeet,Quaternion.identity,turnFeet,Quaternion.Euler(0,90,0)),
                    "The same ninety-degree turn succeeds after physical obstruction removal");
                Set(wiring,"_enemies",Array.Empty<EnemyVitals>());Call(wiring,"CollectControllers");
                ink.Restore();Cast();manager.Tick(profile.FormationSeconds+profile.ActivitySeconds+profile.DissolveSeconds+.5f);
                Check(manager.Active==null&&!manager.HasPrepared&&manager.AcceptedSummons==manager.ReleasedSummons,
                    "Finite formation plus 20-second activity and dissolve expire without actor residue");
                ink.Restore();Cast();wiring.enabled=false;Call(wiring,"OnDisable");
                Check(manager.Active==null&&!manager.HasPrepared,"Production Wiring disable clears summon synchronously for rest/scene teardown");
                Check(JsonUtility.ToJson(config)==configBefore&&JsonUtility.ToJson(profile)==profileBefore,
                    "Diagnostic transactions do not mutate config or summon profile");
            }
            catch(Exception exception){report.failed.Add(exception.ToString());}
            finally
            {
                if(manager!=null){report.accepted=manager.AcceptedSummons;report.preparationFailures=manager.PrepareFailures;
                    report.attacks=manager.AttackStarts;report.hits=manager.ConfirmedHits;report.pathQueries=manager.PathQueries;manager.Clear();}
                if(installed.valid)installed.Remove();
                if(data!=null)UnityEngine.Object.DestroyImmediate(data);
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(profile);UnityEngine.Object.DestroyImmediate(config);
            }
            report.status=report.failed.Count==0?"PASS":"FAIL";return JsonUtility.ToJson(report,true);
        }
        static void Sync(Scene scene)
        {Physics.SyncTransforms();var physics=scene.GetPhysicsScene();if(physics.IsValid()&&!physics.Equals(Physics.defaultPhysicsScene))physics.Simulate(.001f);}
        static GameObject Obj(string name,Scene scene,Vector3 position)
        {var result=new GameObject(name);SceneManager.MoveGameObjectToScene(result,scene);result.transform.position=position;return result;}
        static bool Near(float a,float b)=>Mathf.Abs(a-b)<.001f;
        static void Set(object target,string field,object value)=>target.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    }
}
