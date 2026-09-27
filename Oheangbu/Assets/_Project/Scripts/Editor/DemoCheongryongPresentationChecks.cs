using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    // Real source prefabs/materials, real controller callbacks and actor-local physics. Edit mode only.
    // No scene/asset writes, refresh, Play mode, screenshot or build.
    public static class DemoCheongryongPresentationChecks
    {
        const string WarningPath = "Assets/_Project/Art/SpellVFX120/AreaRift/Cast_고.prefab";
        const string SpikePath = "Assets/_Project/Art/SpellVFX120/AreaFive/Body_고.prefab";
        const string ImpactPath = "Assets/_Project/Art/SpellVFX120/AreaFive/AreaContact_0.prefab";
        const string MeshPath = "Assets/_Project/Art/SpellVFX120/BambooBolt/VFX120_BambooBolt_001.asset";
        const string MaterialPath = "Assets/_Project/Art/SpellVFX120/BambooBolt/M_BambooBolt_001.mat";

        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public Vector3 projectileMeshSize;
            public string[] unverified = { "Manual rendered root/projectile readability, size, color and occlusion",
                "Particle playback and natural remnant expiry during real paused/slowed Play-mode frames",
                "Global CombatLoopWiring parry burst visuals, audio, performance and actual input timing" };
        }

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run presentation checks in Edit mode.");
            var report = new Report();
            void Check(bool pass, string name) => (pass ? report.passed : report.failed).Add(name);
            void Group(string name, Action action) { try { action(); } catch (Exception e) { report.failed.Add(name + ": " + e); } }
            Sources sources = null;
            Group("Load real sources", () =>
            {
                sources = new Sources(); report.projectileMeshSize = sources.Mesh.bounds.size;
                var size = sources.Mesh.bounds.size;
                Check(size.z > size.x * 3f && size.z > size.y * 3f && sources.Mesh.bounds.max.z <= .02f,
                    "Actual bamboo mesh extends behind its local-Z tip; LookRotation aligns its long axis with flight");
            });
            if (sources != null)
            {
                Group("Root and frozen geometry", () => RootAndFrozen(sources, Check));
                Group("Projectile geometry", () => Projectile(sources, Check));
                Group("Contact ownership", () => Contacts(sources, Check));
                Group("Cancellation and lifecycle", () => Lifecycle(sources, Check));
                Group("Growth warning", () => Growth(sources, Check));
                Group("Expired remnant cleanup", () => Expiry(sources, Check));
                Check(sources.Unchanged(), "Source prefab components, shared meshes and shared materials remain unchanged in memory");
            }
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        static void RootAndFrozen(Sources sources, Action<bool, string> check)
        {
            using (var f = new Fixture(sources))
            {
                var plan = f.Begin(CheongryongAttackKind.RootEruption);
                var warning = f.Visual("warning"); var spike = f.Visual("spike");
                check(f.Presentation.HasAllSources && warning != null && spike != null && f.Presentation.LiveVisualCount == 2 && f.OwnedCount == 2,
                    "Root windup instantiates the actual warning and spike sources in its local scene");
                check(warning != null && Near(warning.transform.position, plan.TargetPoint + Vector3.up * .025f) &&
                    warning.scene == f.Scene && spike.scene == f.Scene && !AnyEnabledRenderer(spike),
                    "Ground warning is at the locked target and the spike remains hidden during early windup");
                Vector3 warningPosition = warning.transform.position, spikePosition = spike.transform.position;
                for (int i = 0; i < 12; i++) f.Tick(f.Now);
                check(Near(warning.transform.position, warningPosition) && Near(spike.transform.position, spikePosition) &&
                    Near(plan.SampleTime, plan.StartedAt) && f.Presentation.LiveVisualCount == 2,
                    "Repeated frozen scaled-clock samples preserve both root geometry and ownership count");
                bool scaled = true; int particles = 0;
                foreach (var ps in warning.GetComponentsInChildren<ParticleSystem>(true))
                { particles++; scaled &= !ps.main.useUnscaledTime && !ps.main.loop && ps.main.stopAction == ParticleSystemStopAction.None; }
                check(particles > 0 && scaled, "Real warning particles are finite, use scaled time and cannot destroy source-owned objects themselves");
                bool disabledColliders = true;
                foreach (var root in new[] { warning, spike })
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true)) disabledColliders &= !collider.enabled;
                check(disabledColliders, "Spawned warning/root colliders cannot obstruct combat or player movement");
                f.Player.transform.position += Vector3.right * 5; f.Sync(); f.Tick(plan.ReleaseAt + .01f);
                check(f.Visual("warning") == null && spike != null && Near(spike.transform.position, plan.TargetPoint) && AnyEnabledRenderer(spike),
                    "Root emerges at the originally telegraphed ground point after the player leaves");
                check(f.CountNamed(sources.Impact.name) == 0 && Near(f.Player.Hp01, 1f), "Missed ground eruption creates no hit burst and causes no real damage");
                f.Combat.StopAttack();
                check(f.Presentation.LiveVisualCount == 0 && f.OwnedCount == 0 && f.Scene.GetRootGameObjects().Length == f.BaselineRoots,
                    "Cancelling a missed root removes actual owned GameObjects immediately in Edit mode");
            }
        }

        static void Projectile(Sources sources, Action<bool, string> check)
        {
            using (var f = new Fixture(sources))
            {
                var plan = f.Begin(CheongryongAttackKind.WoodProjectile);
                var bolt = f.Visual("projectile");
                check(bolt != null && !bolt.activeSelf && Near(bolt.transform.position, plan.Origin) &&
                    bolt.GetComponent<MeshFilter>().sharedMesh == sources.Mesh && bolt.GetComponent<MeshRenderer>().sharedMaterial == sources.Material,
                    "Windup prepares an invisible mouth bolt using the actual shared mesh/material");
                f.Tick(plan.ReleaseAt + .1f);
                check(bolt != null && bolt.activeSelf && Near(bolt.transform.position, plan.ProjectilePosition) &&
                    Vector3.Dot(bolt.transform.forward, plan.Direction) > .9999f && bolt.scene == f.Scene,
                    "Released bolt samples the controller's exact fixed-ray position and orientation");
                Vector3 frozen = bolt.transform.position;
                for (int i = 0; i < 12; i++) f.Tick(f.Now);
                check(Near(bolt.transform.position, frozen) && f.Presentation.LiveVisualCount == 1 && f.OwnedCount == 1,
                    "Frozen scaled time holds projectile position without spawning duplicate visuals");
                f.Player.transform.position += Vector3.right * 5; f.Sync();
                f.Until(plan.ActiveEndAt + .01f);
                check(f.Player.Hp01 == 1f && f.CountNamed(sources.Impact.name) == 0 && f.Visual("projectile") == null && f.OwnedCount == 0,
                    "Fixed-ray miss ends the projectile with zero damage, burst or residual owned objects");
            }
        }

        static void Contacts(Sources sources, Action<bool, string> check)
        {
            foreach (var outcome in new[] { ParryOutcome.None, ParryOutcome.Success, ParryOutcome.Half })
                using (var f = new Fixture(sources))
                {
                    var plan = f.Begin(CheongryongAttackKind.RootEruption);
                    if (outcome != ParryOutcome.None) f.Judge.RaiseGuard(outcome == ParryOutcome.Success ? Element.Metal : Element.Wood, plan.ReleaseAt - .1f);
                    int ownedOutcomes = 0; f.Judge.OwnedImpactResolved += _ => ownedOutcomes++;
                    f.Tick(plan.ReleaseAt + .01f);
                    int expected = outcome == ParryOutcome.None ? 1 : 0;
                    check(f.CountNamed(sources.Impact.name) == expected &&
                        ownedOutcomes == (outcome == ParryOutcome.None ? 0 : 1),
                        outcome + ": real damage creates one impact burst; owned Success/Half add no second presentation burst");
                    for (int i = 0; i < 10; i++) f.Tick(f.Now);
                    check(f.CountNamed(sources.Impact.name) == expected && ownedOutcomes == (outcome == ParryOutcome.None ? 0 : 1),
                        outcome + ": repeated LateUpdate samples cannot repeat the contact burst or owned parry event");
                }
        }

        static void Lifecycle(Sources sources, Action<bool, string> check)
        {
            foreach (string state in new[] { "root windup", "projectile flight", "root after impact" })
                foreach (string reason in new[] { "cancel", "death", "disable" })
                    using (var f = new Fixture(sources))
                    {
                        var plan = f.Begin(state == "projectile flight" ? CheongryongAttackKind.WoodProjectile : CheongryongAttackKind.RootEruption);
                        if (state == "projectile flight") f.Tick(plan.ReleaseAt + .1f);
                        if (state == "root after impact") f.Tick(plan.ReleaseAt + .01f);
                        if (reason == "cancel") f.Combat.StopAttack();
                        else if (reason == "death") f.Enemy.TakeDamage(float.MaxValue);
                        else { f.Presentation.enabled = false; Call(f.Presentation, "OnDisable"); }
                        Call(f.Presentation, "LateUpdate");
                        check(f.Presentation.LiveVisualCount == 0 && f.OwnedCount == 0 && f.Scene.GetRootGameObjects().Length == f.BaselineRoots,
                            state + " / " + reason + ": zero actual owned objects, no deferred-Destroy false pass (live=" +
                            f.Presentation.LiveVisualCount + ", roots=" + f.OwnedCount + ")");
                    }
        }

        static void Growth(Sources sources, Action<bool, string> check)
        {
            using (var f = new Fixture(sources))
            {
                var growth = f.Enemy.gameObject.AddComponent<CheongryongGrowthController>();
                Set(growth, "_clock", (Func<double>)(() => f.Now)); Call(growth, "OnEnable");
                f.Combat.Configure(f.Player.transform, f.Player, f.Judge);
                f.Presentation.Configure(f.Combat, sources.Warning, sources.Spike, sources.Mesh, sources.Material, sources.Impact);
                var plan = f.Begin(CheongryongAttackKind.WoodProjectile);
                f.Enemy.TakeDamage(50, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Wood));
                var warning = f.Visual("growthWarning");
                check(growth.IsWindingUp && plan.IsCancelled && warning != null && f.Visual("projectile") == null && f.Presentation.LiveVisualCount == 1,
                    "Real threshold growth replaces attack presentation with one four-second growth warning");
                f.Now += 1f; f.Enemy.TakeDamage(1, AttackProvenance.Create(f.Player, DamageSource.PlayerDirect, Element.Metal));
                check(growth.WasInterrupted && f.Presentation.LiveVisualCount == 0 && f.OwnedCount == 0,
                    "Real Metal growth interruption clears its warning without leaving owned objects");
            }
        }

        static void Expiry(Sources sources, Action<bool, string> check)
        {
            using (var f = new Fixture(sources))
            {
                var plan = f.Begin(CheongryongAttackKind.RootEruption); f.Tick(plan.ReleaseAt + .01f);
                var remnants = (IList)Get(f.Presentation, "remnants");
                check(remnants.Count == 1 && f.CountNamed(sources.Impact.name) == 1, "Successful direct damage owns exactly one temporary impact remnant");
                // Move the diagnostic deadline into the past without running or sleeping the Editor clock.
                // This checks actual removal; natural Play-mode passage of time remains explicitly unverified.
                foreach (var remnant in remnants)
                    remnant.GetType().GetField("Until", BindingFlags.Instance | BindingFlags.Public).SetValue(remnant, Time.time - .01f);
                Call(f.Presentation, "LateUpdate");
                check(remnants.Count == 0 && f.CountNamed(sources.Impact.name) == 0, "An expired deadline destroys the actual impact remnant, not just its bookkeeping entry");
                f.Combat.StopAttack();
                check(f.Presentation.LiveVisualCount == 0 && f.OwnedCount == 0, "Expiry followed by cancellation leaves no owned presentation objects");
            }
        }

        sealed class Sources
        {
            public readonly GameObject Warning, Spike, Impact;
            public readonly Mesh Mesh;
            public readonly Material Material;
            readonly Dictionary<UnityEngine.Object, string> snapshots = new Dictionary<UnityEngine.Object, string>();
            public Sources()
            {
                Warning = Require<GameObject>(WarningPath); Spike = Require<GameObject>(SpikePath); Impact = Require<GameObject>(ImpactPath);
                Mesh = Require<Mesh>(MeshPath); Material = Require<Material>(MaterialPath);
                Capture(Mesh); Capture(Material);
                foreach (var prefab in new[] { Warning, Spike, Impact })
                {
                    Capture(prefab);
                    foreach (var component in prefab.GetComponentsInChildren<Component>(true)) if (component != null) Capture(component);
                    foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials) if (material != null) Capture(material);
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh != null) Capture(filter.sharedMesh);
                }
            }
            void Capture(UnityEngine.Object obj) { if (!snapshots.ContainsKey(obj)) snapshots.Add(obj, EditorJsonUtility.ToJson(obj)); }
            public bool Unchanged()
            { foreach (var pair in snapshots) if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value) return false; return true; }
            static T Require<T>(string path) where T : UnityEngine.Object
            { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset == null) throw new InvalidOperationException("Missing real source: " + path); return asset; }
        }

        sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly CombatConfigSO Config;
            public readonly CheongryongCombatProfile Profile;
            public readonly EnemyVitals Enemy;
            public readonly PlayerVitals Player;
            public readonly CheongryongCombatController Combat;
            public readonly CheongryongAttackPresentation Presentation;
            public readonly ParryJudge Judge;
            public readonly int BaselineRoots;
            public float Now = Time.time + 100f;
            public Fixture(Sources sources)
            {
                Scene = EditorSceneManager.NewPreviewScene(); Config = ScriptableObject.CreateInstance<CombatConfigSO>();
                Profile = ScriptableObject.CreateInstance<CheongryongCombatProfile>(); Set(Config, "_enemyMaxHp", 100f);
                var ground = Object("Diagnostic floor", new Vector3(0, -.5f, 0)); ground.AddComponent<BoxCollider>().size = new Vector3(100, 1, 100);
                Enemy = Object("Presentation diagnostic boss", Vector3.zero).AddComponent<EnemyVitals>(); Set(Enemy, "_config", Config); Enemy.Restore();
                Player = Object("Presentation diagnostic player", new Vector3(0, 0, 8)).AddComponent<PlayerVitals>(); Set(Player, "_config", Config); Player.Restore();
                Judge = new ParryJudge(Config);
                Combat = Enemy.gameObject.AddComponent<CheongryongCombatController>(); Set(Combat, "_clock", (Func<float>)(() => Now));
                Combat.ConfigureProfile(Profile, Config); Combat.Configure(Player.transform, Player, Judge);
                Presentation = Enemy.gameObject.AddComponent<CheongryongAttackPresentation>();
                Presentation.Configure(Combat, sources.Warning, sources.Spike, sources.Mesh, sources.Material, sources.Impact);
                BaselineRoots = Scene.GetRootGameObjects().Length; Sync();
            }
            GameObject Object(string name, Vector3 position)
            { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, Scene); obj.transform.position = position; return obj; }
            public GameObject Visual(string field) => (GameObject)Get(Presentation, field);
            public int OwnedCount
            { get { int count = 0; foreach (var root in Scene.GetRootGameObjects()) if (root.name.StartsWith("Cheongryong_", StringComparison.Ordinal)) count++; return count; } }
            public int CountNamed(string sourceName)
            { int count = 0; foreach (var root in Scene.GetRootGameObjects()) if (root.name == "Cheongryong_" + sourceName) count++; return count; }
            public CheongryongAttackPlan Begin(CheongryongAttackKind kind)
            {
                Now += Profile.InitialDelay + Profile.CancelRecoverySeconds + .1f; Sync();
                if (!Combat.TryBeginAttack(kind)) throw new InvalidOperationException("Could not start actual " + kind);
                Call(Presentation, "LateUpdate"); return Combat.CurrentPlan;
            }
            public void Tick(float now) { Now = now; Call(Combat, "Update"); Call(Presentation, "LateUpdate"); }
            public void Until(float end)
            { int count = 0; while (Now < end && count++ < 10000) Tick(Mathf.Min(end, Now + .02f)); if (count >= 10000) throw new InvalidOperationException("Unbounded timeline"); }
            public void Sync()
            { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("Local physics scene required"); physics.Simulate(.001f); }
            public void Dispose()
            {
                if (Presentation != null) { Presentation.enabled = false; Call(Presentation, "OnDisable"); }
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
                UnityEngine.Object.DestroyImmediate(Profile); UnityEngine.Object.DestroyImmediate(Config);
            }
        }

        static bool AnyEnabledRenderer(GameObject obj)
        { if (obj == null) return false; foreach (var renderer in obj.GetComponentsInChildren<Renderer>(true)) if (renderer.enabled) return true; return false; }
        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .001f;
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
        static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
