using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoSummonFlameChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Native input", "Final haetae rig", "Campaign performance", "Moving campaign enemies" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode fixture only");
            var report = new Report(); Fixture f = null;
            int ownedMaterialsBefore=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.EndsWith("_OwnedCone",StringComparison.Ordinal));
            void Check(bool result,string name) => (result?report.passed:report.failed).Add(name);
            try
            {
                f = new Fixture();
                Check(!f.Profile.FlameAttackEnabled,"Flame attack is opt-in and existing profiles retain their attack path");
                f.Profile.Letter="놈";f.Profile.Element=Element.Fire;f.Profile.FlameAttackEnabled=true;
                f.Profile.FlamePrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/SpellVFX120/Traditional/Bodies/PF_KTP_FlameCone.prefab");
                f.Profile.FlameConstrainVisual=true;
                f.Profile.FlameAdditiveShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAdditive.shadergraph");
                f.Profile.FlameAlphaShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Shaders/DemoFlameAlpha.shadergraph");
                Check(f.Profile.FlamePrefab!=null,"Existing KTP flame prefab is available");
                Check(f.Profile.TryValidate(out _),"Authored fire profile is valid");
                float minimum=f.Profile.FlameMinimumDistance;
                f.Profile.FlameMinimumDistance=f.Profile.FlameRange+1;
                Check(!f.Profile.TryValidate(out _),"Invalid minimum distance outside attack range is rejected");f.Profile.FlameMinimumDistance=minimum;
                f.Reset();f.UntilAttack();
                var plan=f.Manager.ActiveFlamePlan;
                Check(plan!=null&&Near(plan.ReleaseAt-plan.StartedAt,.45f)&&Near(plan.SprayEndAt-plan.ReleaseAt,.65f),"Committed cone separates windup and finite spray time");
                var origin=plan.Origin;var direction=plan.Direction;
                f.Advance(.3f);Check(f.Hits.Count==0,"No fire damage before release");
                float age=f.Manager.ActiveClock.Elapsed;
                for(int i=0;i<30;i++)f.Manager.Tick(0);
                Check(Near(age,f.Manager.ActiveClock.Elapsed)&&f.Hits.Count==0,"Pause preserves windup and does not apply damage");
                f.Advance(.3f);
                Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.Side)==0,"One released cone hits each inside target once and excludes side target");
                Check(f.Contacts==2&&f.Hits[0].Attack.AttackId==f.Hits[1].Attack.AttackId,"One attack ID reaches the common contact path for both targets");
                bool correct=true;foreach(var hit in f.Hits)correct &= hit.Attack.Element==Element.Fire&&hit.Attack.Source==DamageSource.Summon&&hit.Attack.Instigator==f.Manager.Active;
                Check(correct,"Fire summon provenance is retained in production damage");
                var visual=f.Manager.Active.GetComponent<DemoSummonFlamePresentation>();
                Check(visual!=null&&visual.ParticleSystems==10&&visual.LiveParticles>0,"Existing ten-system flame asset emits through the shared attack clock");
                float simulation=visual==null?0:visual.SimulatedTime;
                for(int i=0;i<30;i++)f.Manager.Tick(0);
                Check(visual!=null&&Near(simulation,visual.SimulatedTime),"Pause freezes flame simulation");
                f.Advance(1.2f);Check(f.Count(f.A)==1&&f.Count(f.B)==1,"Spray and recovery cannot repeat the released damage");
                Check(plan.Origin==origin&&plan.Direction==direction,"Cast origin and direction remain the original snapshot");
                Check(visual!=null&&visual.LiveParticles==0,"Particles are cleared at the attack end");
                foreach(int fps in new[]{30,60,120})foreach(float scale in new[]{1f,.25f})
                {
                    f.Reset();f.UntilAttack();float elapsed=0,step=scale/fps;
                    while(elapsed<1.7f){f.Manager.Tick(step);elapsed+=step;}
                    Check(f.Count(f.A)==1&&f.Count(f.B)==1&&f.Count(f.Side)==0,fps+"fps / "+scale+" scaled dt retains one hit per cone target");
                }
                f.Reset();f.UntilAttack();f.A.TakeDamage(f.A.Hp);f.Advance(1.7f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1,"Primary death leaves other captured living cone target eligible");
                f.Reset();f.UntilAttack();f.A.TakeDamage(f.A.Hp);f.A.Restore();f.Advance(1.7f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1,"Respawn cannot inherit the captured previous life hit");
                f.Reset();f.UntilAttack();f.A.transform.position+=Vector3.right*5;f.Sync();f.Advance(1.7f);
                Check(f.Count(f.A)==0&&f.Count(f.B)==1,"Moving outside the fixed cone evades the hit");
                f.Reset();f.UntilAttack();f.Side.transform.position=f.B.transform.position;f.Sync();f.Advance(1.7f);
                Check(f.Count(f.Side)==0,"Late entrants are not added to the captured target set");
                f.Reset();f.UntilAttack();f.Wall(2);f.Advance(1.7f);
                Check(f.Hits.Count==0,"Wall appearing before release blocks actual fire contacts");
                f.Reset();f.UntilAttack();plan=f.Manager.ActiveFlamePlan;f.Manager.Clear();f.Manager.Tick(2);
                Check(plan.IsCancelled&&f.Hits.Count==0&&f.Manager.Active==null,"Clear cancels unreleased flame and destroys its actor");
                f.Reset();f.A.transform.position=f.Manager.Active.transform.position+Vector3.forward*1.5f+Vector3.up*.875f;
                f.B.gameObject.SetActive(false);f.Side.gameObject.SetActive(false);f.Sync();var before=f.Manager.Active.transform.position;
                f.Advance(.8f);
                Check(f.Manager.Active.transform.position.z<before.z-.3f,"Too-close target produces real backward movement");
                Check(Vector3.Dot(f.Manager.Active.transform.forward,Vector3.forward)>.8f,"Retreat retains facing toward the enemy");
                f.Reset();f.A.transform.position=f.Manager.Active.transform.position+Vector3.forward*1.5f+Vector3.up*.875f;
                f.B.gameObject.SetActive(false);f.Side.gameObject.SetActive(false);f.Wall(-1.15f);before=f.Manager.Active.transform.position;f.Sync();f.Advance(1.8f);
                Check(f.Manager.Active.transform.position.z>=before.z-.35f,"Blocked retreat does not teleport or force its footprint through a wall");
                Check(f.Count(f.A)>0,"Blocked retreat can still attack a visible close enemy from its safe position");
                f.Reset();f.UntilAttack();var fireActor=f.Manager.Active;plan=f.Manager.ActiveFlamePlan;
                var wood=UnityEngine.Object.Instantiate(f.Profile);wood.Letter="곰";wood.Element=Element.Wood;wood.FlameAttackEnabled=false;
                try
                {
                    f.Manager.Profiles=new[]{f.Profile,wood};f.Ink.Restore(0);
                    Call(f.Wiring,"ResolveSummon",new SpellCast('곰',SpellKind.Summon,Element.Wood,10,default,1));
                    Check(f.Manager.Active==fireActor&&!plan.IsCancelled,"Failed wood replacement preserves current fire actor and pending cast");
                    f.Ink.Restore();Call(f.Wiring,"ResolveSummon",new SpellCast('곰',SpellKind.Summon,Element.Wood,10,default,1));
                    Check(f.Manager.Active!=null&&f.Manager.Active!=fireActor&&plan.IsCancelled&&f.Manager.ActiveFlamePlan==null,"Successful wood replacement clears fire ownership and unreleased cone");
                    var woodActor=f.Manager.Active;f.Ink.Restore();Call(f.Wiring,"ResolveSummon",new SpellCast('놈',SpellKind.Summon,Element.Fire,10,default,1));
                    Check(f.Manager.Active!=null&&f.Manager.Active!=woodActor,"Fire replaces wood using the same accepted-cast transaction");
                }
                finally{f.Manager.Clear();f.Manager.Profiles=new[]{f.Profile};UnityEngine.Object.DestroyImmediate(wood);}
            }
            catch(Exception e){report.failed.Add(e.ToString());}
            finally{f?.Dispose();}
            Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.EndsWith("_OwnedCone",StringComparison.Ordinal))==ownedMaterialsBefore,
                "Repeated attacks, replacements and fixture disposal release all owned cone materials");
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
            public EnemyVitals A, B, Side;
            public BoxCollider Floor;
            public Vector3 Origin = new Vector3(6200, 6200, 6200);
            public readonly List<EnemyDamageResult> Hits = new List<EnemyDamageResult>();
            public int Contacts;
            NavMeshData data; NavMeshDataInstance installed;
            readonly List<GameObject> temporary = new List<GameObject>();

            public Fixture()
            {
                try
                {
                Scene = EditorSceneManager.NewPreviewScene();
                Config = ScriptableObject.CreateInstance<CombatConfigSO>(); Set(Config, "_enemyMaxHp", 1000f);
                Profile = ScriptableObject.CreateInstance<SummonCombatProfile>();
                var ground = Obj("Flame fixture ground", Origin + Vector3.down * .1f);
                Floor = ground.AddComponent<BoxCollider>(); Floor.size = new Vector3(60, .2f, 60);
                var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = .4f; settings.agentHeight = 2;
                settings.agentClimb = .3f; settings.agentSlope = 35; settings.overrideVoxelSize = true; settings.voxelSize = .15f;
                var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(ground.transform.position, Quaternion.identity, Vector3.one), size = Floor.size, area = 0 } };
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Origin, new Vector3(64, 8, 64)), Vector3.zero, Quaternion.identity);
                if (data == null) throw new InvalidOperationException("Flame fixture NavMesh build failed.");
                installed = NavMesh.AddNavMeshData(data);
                if (!installed.valid) throw new InvalidOperationException("Flame fixture NavMesh installation failed.");
                var template = Obj("Diagnostic empty appearance", Vector3.zero); template.SetActive(false);
                Profile.PresentationPrefab = template; Profile.FormationSeconds = .2f; Profile.DissolveSeconds = .2f;
                Profile.CooldownSeconds = 10; Profile.FollowSpeed = 1.8f;
                var player = Obj("Flame fixture player feet", Origin);
                Wiring = Obj("Flame fixture wiring", Origin).AddComponent<CombatLoopWiring>();
                Set(Wiring, "_config", Config); Set(Wiring, "_playerTransform", player.transform);
                Ink = new InkPool(Config, null); Wiring.Construct(null, null, null, Ink); Set(Wiring, "_contactVfx", null);
                Manager = Wiring.gameObject.AddComponent<DemoSummonCombatManager>(); Manager.Wiring = Wiring; Manager.Profiles = new[] { Profile };
                Call(Manager, "Bind");
                A = Enemy("Flame target A"); B = Enemy("Flame target B"); Side = Enemy("Flame excluded side target");
                Set(Wiring, "_enemies", new[] { A, B, Side }); Call(Wiring, "CollectControllers");
                Wiring.EnemyDamageResolved += hit => Hits.Add(hit); Wiring.EnemyHitResolved += (_, __) => Contacts++;
                Sync();
                }
                catch { Dispose(); throw; }
            }
            EnemyVitals Enemy(string name)
            {
                var enemy = Obj(name, Origin + Vector3.right * 25).AddComponent<EnemyVitals>();
                Set(enemy, "_config", Config); enemy.Restore(); return enemy;
            }
            public GameObject Obj(string name, Vector3 point)
            { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = point; return go; }
            public void Reset()
            {
                Manager.Clear(); foreach (var go in temporary) if (go != null) UnityEngine.Object.DestroyImmediate(go); temporary.Clear();
                Floor.enabled = true; A.Restore(); B.Restore(); Side.Restore(); A.gameObject.SetActive(true); B.gameObject.SetActive(true); Side.gameObject.SetActive(true);
                foreach (var enemy in new[] { A, B, Side }) enemy.transform.position = Origin + Vector3.right * 25;
                Hits.Clear(); Contacts = 0; Ink.Restore(); Sync();
                Call(Wiring, "ResolveSummon", new SpellCast('놈', SpellKind.Summon, Element.Fire, 10, default, 1));
                if (Manager.Active == null) throw new InvalidOperationException("Flame fixture summon preparation failed: " + Manager.LastFailure);
                Vector3 start = Manager.Active.transform.position;
                A.transform.position = start + Vector3.forward * 3.5f + Vector3.up * .875f;
                B.transform.position = start + Vector3.forward * 3.5f + Vector3.right + Vector3.up * .875f;
                Side.transform.position = start + Vector3.forward * 3.5f + Vector3.right * 3f + Vector3.up * .875f;
                Sync();
            }
            public void UntilAttack()
            {
                int guard = 0;
                while (Manager.Active != null && Manager.ActiveClock.PendingAttackId == 0 && guard++ < 140) Manager.Tick(.025f);
                if (Manager.Active == null || Manager.ActiveClock.PendingAttackId == 0) throw new InvalidOperationException("Flame fixture attack did not start.");
            }
            public void Advance(float time) { while (time > 0) { float step = Mathf.Min(.025f, time); Manager.Tick(step); time -= step; } }
            public int Count(EnemyVitals target) { int count = 0; foreach (var hit in Hits) if (hit.Target == target && hit.AppliedDamage > 0) count++; return count; }
            public void Wall(float forwardDistance)
            {
                var wall = Obj("Root path solid wall", Manager.Active.transform.position + Vector3.forward * forwardDistance + Vector3.up);
                wall.AddComponent<BoxCollider>().size = new Vector3(4, 4, .25f); temporary.Add(wall); Sync();
            }
            public void GroundGap(float forwardDistance, float length)
            {
                Floor.enabled = false; float gapStart = Manager.Active.transform.position.z + forwardDistance;
                foreach (var span in new[] { new Vector2(Origin.z - 30, gapStart), new Vector2(gapStart + length, Origin.z + 30) })
                {
                    var ground = Obj("Root ground gap bank", new Vector3(Origin.x, Origin.y - .1f, (span.x + span.y) * .5f));
                    ground.AddComponent<BoxCollider>().size = new Vector3(60, .2f, span.y - span.x); temporary.Add(ground);
                }
                Sync();
            }
            public void Sync() { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f); }
            public void Dispose()
            {
                if (Manager != null) Manager.Clear(); if (installed.valid) installed.Remove(); if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
                if (Profile != null) UnityEngine.Object.DestroyImmediate(Profile); if (Config != null) UnityEngine.Object.DestroyImmediate(Config);
            }
        }

        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static bool Near(float a, float b) => Math.Abs(a - b) < .002f;
        static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
