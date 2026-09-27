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
    /// <summary>Explicitly invoked edit-mode diagnostics; never opens or modifies campaign scenes.</summary>
    public static class DemoSummonWaterChecks
    {
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Native combat input and final mouth/stream alignment", "Campaign navigation and performance",
                "Continuous target motion between physical steps", "Visual approval" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit-mode diagnostic only");
            var report = new Report(); Fixture f = null;
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            try
            {
                f = new Fixture(); Check(!f.Profile.WaterAttackEnabled, "Water combat is opt-in");
                f.Profile.Letter = "옴"; f.Profile.Element = Element.Water; f.Profile.WaterAttackEnabled = true;
                Check(f.Profile.TryValidate(out _), "Water profile validates");
                f.Profile.ClubAttackEnabled = true; Check(!f.Profile.TryValidate(out _), "Water rejects mixed attack modes"); f.Profile.ClubAttackEnabled = false;
                f.Profile.WaterTravelSpeed = 1; Check(!f.Profile.TryValidate(out _), "Travel front must reach range inside stream time"); f.Profile.WaterTravelSpeed = 12;
                f.Profile.WaterRadius = float.NaN; Check(!f.Profile.TryValidate(out _), "Non-finite water geometry fails"); f.Profile.WaterRadius = .22f;
                var geometry = new SummonWaterAttackPlan(new Vector3(4, 2, 7), Vector3.forward, 6, .22f, 12, 10, .6f, .7f, .5f);
                var origin = geometry.Origin;
                Check(geometry.Contains(origin + Vector3.forward * 4 + Vector3.right * .2f) &&
                    !geometry.Contains(origin + Vector3.forward * 4 + Vector3.right * .23f) &&
                    !geometry.Contains(origin + Vector3.forward * 6.01f) && !geometry.Contains(origin + Vector3.back) &&
                    !geometry.Contains(origin + Vector3.forward * 2 + Vector3.up * .23f), "Finite water corridor bounds width, height, near and far ends");
                geometry.AdvanceTo(10.5f); Check(!geometry.IsStreaming && Near(geometry.FrontDistance, 0), "Preparation contains no traveling water");
                geometry.AdvanceTo(10.85f); Check(Near(geometry.FrontDistance, 3), "Water front advances at twelve metres per scaled second");
                geometry.AdvanceTo(11.4f); Check(!geometry.IsStreaming && !geometry.IsFinished, "Stream ends before recovery ends");
                Check(geometry.IsVisible && Near(geometry.TailDistance, 1.2f), "Trailing water continues draining through recovery");
                bool rewindRejected = false; try { geometry.AdvanceTo(11); } catch (ArgumentOutOfRangeException) { rewindRejected = true; }
                Check(rewindRejected, "Plan rejects clock rewind");
                geometry.AdvanceTo(11.81f); Check(!geometry.IsVisible && geometry.IsFinished, "No water remains after trailing edge reaches bounded end");

                f.Reset(); f.UntilAttack(); var plan = f.Manager.ActiveWaterPlan;
                Check(plan != null && Near(plan.ReleaseAt - plan.StartedAt, .6f) && Near(plan.StreamEndAt - plan.ReleaseAt, .7f) && Near(plan.EndAt - plan.StreamEndAt, .5f),
                    "Committed cast separates windup, stream and recovery");
                var fixedOrigin = plan.Origin; var fixedDirection = plan.Direction;
                f.AdvanceTo(plan.ReleaseAt - .03f); Check(f.Hits.Count == 0, "No windup damage");
                float age = f.Manager.ActiveClock.Elapsed; var pose = f.Manager.Active.transform.position;
                for (int i = 0; i < 30; i++) f.Manager.Tick(0);
                Check(Near(age, f.Manager.ActiveClock.Elapsed) && pose == f.Manager.Active.transform.position && f.Hits.Count == 0, "Pause preserves fixed pose and attack clock");
                f.AdvanceTo((plan.ContactAt(f.A.transform.position) + plan.ContactAt(f.B.transform.position)) * .5f);
                Check(f.Count(f.A) == 1 && f.Count(f.B) == 0 && f.Count(f.C) == 0, "Leading edge reaches near target before far targets");
                f.AdvanceTo(plan.EndAt + .05f);
                Check(f.Count(f.A) == 1 && f.Count(f.B) == 1 && f.Count(f.C) == 1 && f.Hits.Count == 3, "Each captured target receives one hit and outsiders receive none");
                Check(f.Hits.Count == 3 && f.Hits[0].Attack.AttackId == f.Hits[2].Attack.AttackId && f.Contacts == 3,
                    "One attack provenance uses the shared contact/damage pathway without empty-space impacts");
                bool provenance = true; foreach (var hit in f.Hits) provenance &= hit.Attack.Source == DamageSource.Summon && hit.Attack.Element == Element.Water;
                Check(provenance && Near(f.Manager.WaterDamageSnapshot, 10), "Water snapshots one times cast damage with summon source");
                Check(plan.Origin == fixedOrigin && plan.Direction == fixedDirection, "Committed mouth and direction remain fixed");

                foreach (int fps in new[] { 30, 60, 120 }) foreach (float scale in new[] { 1f, .25f })
                {
                    f.Reset(); f.UntilAttack(); float elapsed = 0, dt = scale / fps;
                    while (elapsed < 1.9f) { f.Manager.Tick(dt); elapsed += dt; }
                    Check(f.Hits.Count == 3 && f.Count(f.A) == 1 && f.Count(f.C) == 1, fps + " fps / " + scale + " time scale preserves one hit per target");
                }
                f.Reset(); f.UntilAttack(); f.Manager.Tick(1.9f); Check(f.Hits.Count == 3, "Large delta substeps traveling front");
                f.Reset(); f.UntilAttack(); f.A.TakeDamage(f.A.Hp); f.Advance(1.9f);
                Check(f.Count(f.A) == 0 && f.Count(f.B) == 1 && f.Count(f.C) == 1, "Dead primary cannot receive damage and does not erase other fixed candidates");
                f.Reset(); f.UntilAttack(); f.B.TakeDamage(f.B.Hp); f.B.Restore(); f.Advance(1.9f);
                Check(f.Count(f.B) == 0 && f.Count(f.A) == 1, "Respawned life cannot inherit captured contact");
                f.Reset(); f.UntilAttack(); f.Side.transform.position = f.C.transform.position; f.Sync(); f.Advance(1.9f);
                Check(f.Count(f.Side) == 0 && f.Count(f.C) == 1, "Late corridor entrant is never captured");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; var oldB = f.B.transform.position;
                f.B.transform.position += Vector3.right * 2; f.Sync(); f.AdvanceTo(plan.ContactAt(oldB) + .06f);
                f.B.transform.position = oldB; f.Sync(); f.AdvanceTo(plan.EndAt + .05f);
                Check(f.Count(f.B) == 0, "Dodge and re-entry cannot reschedule missed leading edge");
                f.Reset(); f.UntilAttack(); f.C.transform.position += Vector3.forward * .6f; f.Sync(); f.Advance(1.9f);
                Check(f.Count(f.C) == 0, "Moving further down stream cannot reschedule captured contact");
                f.Reset(); f.A.transform.position += Vector3.up * .8f; f.Sync(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Advance(1.9f);
                Check(plan.Direction.y > .1f && f.Count(f.A) == 1 && f.Count(f.B) == 0 && f.Count(f.C) == 0,
                    "Low mouth aims in three dimensions at commit without widening the stream for height");
                f.Reset(); var capsule = f.B.gameObject.AddComponent<CapsuleCollider>(); capsule.height = 1.75f; capsule.radius = .28f;
                f.B.transform.position -= Vector3.up * .875f; capsule.center = Vector3.up * .875f; f.Sync(); f.UntilAttack(); f.Advance(1.9f);
                Check(f.Count(f.B) == 1, "Foot-rooted enemy uses actual collider centre"); UnityEngine.Object.DestroyImmediate(capsule);
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Wall(1.4f, 0); f.Advance(.05f);
                Check(plan.ClearDistance < 1.4f && plan.VisibleEnd == plan.Origin, "Inserted wall clips full corridor before stream releases");
                f.Advance(1.85f); Check(f.Hits.Count == 0 && f.Contacts == 0, "Owner-scene wall blocks water and empty space produces no generic impact");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Wall(1.4f, .25f); f.Advance(1.9f);
                Check(f.Hits.Count == 0 && plan.ClearDistance < 1.4f, "Off-axis wall clips stream radius even though centre ray is clear");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Wall(1.4f, 0); f.Advance(.05f); float clipped = plan.ClearDistance;
                f.RemoveWalls(); f.Advance(1.85f); Check(f.Hits.Count == 0 && Near(clipped, plan.ClearDistance), "Removing wall cannot reopen a committed blocked stream");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Wiring.SummonPlayer.position += Vector3.right * 40; f.Advance(.1f);
                Check(plan.IsCancelled && f.Hits.Count == 0, "Leash exit cancels unreleased water");
                f.Reset(); f.UntilAttack(); f.Profile.WaterDamageMultiplier = 9; f.Advance(1.9f);
                Check(Near(f.Manager.WaterDamageSnapshot, 10) && f.Hits.Count == 3, "Profile edit cannot mutate accepted damage snapshot"); f.Profile.WaterDamageMultiplier = 1;
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Manager.ActiveClock.BeginDissolve(); f.Manager.Tick(.025f);
                Check(plan.IsCancelled && f.Hits.Count == 0, "Dissolve cancels pending water");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; f.Manager.Clear(); f.Manager.Tick(2);
                Check(plan.IsCancelled && f.Manager.Active == null && f.Hits.Count == 0, "Clear cancels the fixed stream and actor");
                f.Reset(); f.UntilAttack(); plan = f.Manager.ActiveWaterPlan; var actor = f.Manager.Active; f.Ink.Restore(0);
                Call(f.Wiring, "ResolveSummon", new SpellCast('옴', SpellKind.Summon, Element.Water, 10, default, 1));
                Check(f.Manager.Active == actor && !plan.IsCancelled, "Failed replacement preserves original stream");
                f.Ink.Restore(); Call(f.Wiring, "ResolveSummon", new SpellCast('옴', SpellKind.Summon, Element.Water, 10, default, 1));
                Check(f.Manager.Active != null && f.Manager.Active != actor && plan.IsCancelled, "Accepted replacement cancels previous stream");
                f.Advance(21); Check(f.Manager.Active == null, "Activity expiry removes the water summon");
            }
            catch (Exception exception) { report.failed.Add(exception.ToString()); }
            finally { f?.Dispose(); }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL"; return JsonUtility.ToJson(report, true);
        }

        sealed class Fixture : IDisposable
        {
            public Scene Scene;
            public CombatConfigSO Config;
            public SummonCombatProfile Profile;
            public CombatLoopWiring Wiring;
            public DemoSummonCombatManager Manager;
            public InkPool Ink;
            public EnemyVitals A, B, C, Side;
            public Vector3 Origin = new Vector3(8300, 8300, 8300);
            public readonly List<EnemyDamageResult> Hits = new List<EnemyDamageResult>();
            public int Contacts;
            NavMeshData data; NavMeshDataInstance installed;
            readonly List<GameObject> walls = new List<GameObject>();
            public Fixture()
            {
                try
                {
                    Scene = EditorSceneManager.NewPreviewScene(); Config = ScriptableObject.CreateInstance<CombatConfigSO>(); Set(Config, "_enemyMaxHp", 1000f);
                    Profile = ScriptableObject.CreateInstance<SummonCombatProfile>();
                    var floor = Obj("Water ground", Origin + Vector3.down * .1f); var box = floor.AddComponent<BoxCollider>(); box.size = new Vector3(60, .2f, 60);
                    var settings = NavMesh.GetSettingsByIndex(0); settings.agentRadius = .4f; settings.agentHeight = 2; settings.agentClimb = .3f; settings.agentSlope = 35;
                    settings.overrideVoxelSize = true; settings.voxelSize = .15f;
                    data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { new NavMeshBuildSource {
                        shape = NavMeshBuildSourceShape.Box, transform = Matrix4x4.TRS(floor.transform.position, Quaternion.identity, Vector3.one), size = box.size, area = 0 } },
                        new Bounds(Origin, new Vector3(64, 8, 64)), Vector3.zero, Quaternion.identity);
                    if (data == null) throw new InvalidOperationException("Water fixture NavMesh creation failed");
                    installed = NavMesh.AddNavMeshData(data); if (!installed.valid) throw new InvalidOperationException("Water fixture NavMesh install failed");
                    var template = Obj("Empty diagnostic water summon", Vector3.zero); template.SetActive(false); Profile.PresentationPrefab = template;
                    Profile.FormationSeconds = .2f; Profile.DissolveSeconds = .2f; Profile.CooldownSeconds = 10; Profile.BodyHeight = 1.6f;
                    Profile.WaterOriginOffset = new Vector3(0, .875f, 1);
                    var player = Obj("Water player feet", Origin);
                    Wiring = Obj("Water wiring", Origin).AddComponent<CombatLoopWiring>(); Set(Wiring, "_config", Config); Set(Wiring, "_playerTransform", player.transform);
                    Ink = new InkPool(Config, null); Wiring.Construct(null, null, null, Ink); Set(Wiring, "_contactVfx", null);
                    Manager = Wiring.gameObject.AddComponent<DemoSummonCombatManager>(); Manager.Wiring = Wiring; Manager.Profiles = new[] { Profile }; Call(Manager, "Bind");
                    A = Enemy("Water near primary"); B = Enemy("Water middle"); C = Enemy("Water far"); Side = Enemy("Water outsider");
                    Set(Wiring, "_enemies", new[] { A, B, C, Side }); Call(Wiring, "CollectControllers");
                    Wiring.EnemyDamageResolved += hit => Hits.Add(hit); Wiring.EnemyHitResolved += (_, __) => Contacts++; Sync();
                }
                catch { Dispose(); throw; }
            }
            EnemyVitals Enemy(string name) { var enemy = Obj(name, Origin + Vector3.right * 25).AddComponent<EnemyVitals>(); Set(enemy, "_config", Config); enemy.Restore(); return enemy; }
            GameObject Obj(string name, Vector3 position) { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, Scene); go.transform.position = position; return go; }
            public void Reset()
            {
                Manager.Clear(); RemoveWalls(); Wiring.SummonPlayer.position = Origin;
                foreach (var enemy in new[] { A, B, C, Side }) { enemy.Restore(); enemy.gameObject.SetActive(true); enemy.transform.position = Origin + Vector3.right * 25; }
                Hits.Clear(); Contacts = 0; Ink.Restore(); Sync();
                Call(Wiring, "ResolveSummon", new SpellCast('옴', SpellKind.Summon, Element.Water, 10, default, 1));
                if (Manager.Active == null) throw new InvalidOperationException("Water preparation failed: " + Manager.LastFailure);
                Vector3 mouth = Manager.Active.transform.TransformPoint(Profile.WaterOriginOffset), forward = Manager.Active.transform.forward;
                A.transform.position = mouth + forward * 3.2f; B.transform.position = mouth + forward * 4.2f; C.transform.position = mouth + forward * 5.2f;
                Side.transform.position = mouth + forward * 5.2f + Vector3.right; Sync();
            }
            public void UntilAttack()
            { int n = 0; while (Manager.Active != null && Manager.ActiveWaterPlan == null && n++ < 160) Manager.Tick(.025f); if (Manager.ActiveWaterPlan == null) throw new InvalidOperationException("Water plan did not start"); }
            public void Advance(float seconds) { while (seconds > 0) { float dt = Mathf.Min(.025f, seconds); Manager.Tick(dt); seconds -= dt; } }
            public void AdvanceTo(float absolute) { if (Manager.ActiveClock != null) Advance(Mathf.Max(0, absolute - Manager.ActiveClock.Elapsed)); }
            public int Count(EnemyVitals enemy) { int result = 0; foreach (var hit in Hits) if (hit.Target == enemy && hit.AppliedDamage > 0) result++; return result; }
            public void Wall(float distance, float lateral)
            {
                var plan = Manager.ActiveWaterPlan; var wall = Obj("Water local wall", plan.Origin + plan.Direction * distance + Vector3.right * lateral);
                wall.transform.rotation = Quaternion.LookRotation(plan.Direction); wall.AddComponent<BoxCollider>().size = new Vector3(.1f, 4, .15f); walls.Add(wall); Sync();
            }
            public void RemoveWalls() { foreach (var wall in walls) if (wall != null) UnityEngine.Object.DestroyImmediate(wall); walls.Clear(); Sync(); }
            public void Sync() { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene)) physics.Simulate(.001f); }
            public void Dispose()
            {
                if (Manager != null) Manager.Clear(); if (installed.valid) installed.Remove(); if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); if (Profile != null) UnityEngine.Object.DestroyImmediate(Profile); if (Config != null) UnityEngine.Object.DestroyImmediate(Config);
            }
        }
        static bool Near(float a, float b) => Math.Abs(a - b) < .004f;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
