using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Spellcraft;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Issued-plan presentation contract, exercised on real catalog prefab clones.
    // No input/recognition/caster, damage, live Update, or authored-art pass is inferred.
    public static class Vfx120ElementWashAudit
    {
        [Serializable] private sealed class Report
        {
            public string status, capturedUtc, unityVersion, appMvid, runtimeSourceSha256;
            public string authority = "EDIT_PREVIEW_DIAGNOSTIC_AREA_PLANS_NOT_GAMEPLAY";
            public string scope = "Actual native carrier endpoints/width and Path position versus issued Delay/Speed/Length; live preview particles, expiry, reverse seek, re-Begin, fallbacks and source immutability.";
            public string limits = "Carrier anchors are not a bound on every particle/billboard. Rendered coverage/art, real Update/Destroy, performance and actual damage timing remain UNVERIFIED. NaN tests invoke only the new body-build rejection branch; legacy whole-effect invalid-input safety is NOT claimed.";
            public int passed, failed, errors;
            public bool cleanupObserved, sourcesUnchanged;
            public List<Row> rows = new List<Row>();
            public List<Source> sources = new List<Source>();
            public List<string> messages = new List<string>();
        }
        [Serializable] private sealed class Row
        {
            public string name, glyph, status, detail;
            public float life, maximumPositionError, maximumExtentError;
            public int maximumLiveParticles, maximumCapacity, maximumSystems;
            public bool nativeConfigured, oldBodyAbsent, noGameplaySignals, planUnchanged, profileUnchanged;
            public List<Sample> samples = new List<Sample>();
        }
        [Serializable] private sealed class Sample
        {
            public float age;
            public Vector3 carrierWorldPosition, carrierScale;
            public int liveParticles, systems, capacity;
        }
        [Serializable] private sealed class Source
        {
            public string path, diskBefore, diskAfter, dependencyBefore, dependencyAfter;
            public bool unchanged;
            [NonSerialized] public Object asset;
            [NonSerialized] public bool dirtyBefore;
            [NonSerialized] public string memoryBefore;
        }
        // Away from ordinary C2 geometry, without 30 km float quantization.
        private static readonly Vector3 Origin = new Vector3(1000, 1002, 1000);
        private const float SpatialTolerance = .002f;
        private const string CatalogPath = "Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return "BLOCKED: idle Edit mode required; this audit does not change Play mode.";
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                appMvid = typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),
                runtimeSourceSha256 = FileHash(Absolute("Assets/_Project/Scripts/App/SpellVFX120/Vfx120Effect.ElementWash.cs")) };
            var scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<Object>();
            var sources = new HashSet<string>();
            Application.LogCallback logged = (message, trace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                report.errors++; if (report.messages.Count < 16) report.messages.Add(message);
            };
            Application.logMessageReceived += logged;
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                Need(catalog != null, "Catalog missing.");
                foreach (string glyph in new[] { "노", "모" })
                {
                    var entry = catalog.Entries.FirstOrDefault(e => e.Glyph == glyph);
                    var profile = entry.Profile;
                    if (profile == null || entry.Prefab == null)
                    { report.rows.Add(new Row { name = "catalog", glyph = glyph, status = "FAIL", detail = "Required real profile/prefab missing." }); continue; }
                    Remember(entry.Prefab, sources, report.sources);
                    Remember(profile, sources, report.sources);
                    Vfx120Effect Spawn(AreaImpactPlan plan, Vfx120Profile alternate = null)
                    {
                        var host = Object.Instantiate(entry.Prefab); temporary.Add(host);
                        host.hideFlags = HideFlags.HideAndDontSave;
                        SceneManager.MoveGameObjectToScene(host, scene);
                        var effect = host.GetComponent<Vfx120Effect>();
                        Need(effect != null, "Catalog prefab has no Vfx120Effect.");
                        effect.Profile = alternate != null ? alternate : profile;
                        effect.PreviewControlled = true; effect.DemonstrationCues = false;
                        effect.SetAreaPlan(plan);
                        effect.Begin(Origin, null, Origin + new Vector3(-4, -.9f, 8), Color.white);
                        return effect;
                    }
                    void Test(string name, Action<Row> run)
                    {
                        var row = new Row { name = name, glyph = glyph }; report.rows.Add(row);
                        try { run(row); row.status = "PASS_EDIT_CONTRACT_ONLY"; }
                        catch (Exception error) { row.status = "FAIL"; row.detail = error.GetBaseException().Message; }
                        finally { DestroyTemporary(temporary); }
                    }
                    for (int variant = 0; variant < (glyph == "노" ? 3 : 2); variant++)
                    {
                        int parameterSet = variant;
                        Test("spatial_clock_lifetime_reverse_rebegin_" + variant, row =>
                        {
                            var plan = Plan(glyph, parameterSet);
                            string planBefore = JsonUtility.ToJson(plan), profileBefore = EditorJsonUtility.ToJson(profile);
                            Need(profile.NativeBodyPrefab != null, "Native source prefab not assigned: run the real builder first.");
                            Need(profile.NativeBodyMotion == (glyph == "노" ? Vfx120NativeBodyMotion.FlameCone : Vfx120NativeBodyMotion.SandFront), "Wrong native body motion on catalog profile.");
                            var effect = Spawn(plan); row.life = effect.Life;
                            row.nativeConfigured = effect.NativeBodyConfigured && effect.NativeBody != null;
                            Need(row.nativeConfigured, "Native configure failed: " + effect.NativeBodyDiagnostic);
                            row.oldBodyAbsent = effect.PartCount == 0 && effect.AccentCount == 0
                                && !effect.GetComponentsInChildren<Vfx120Atmosphere>(true).Any()
                                && !effect.GetComponentsInChildren<LineRenderer>(true).Any();
                            Need(row.oldBodyAbsent, "Old procedural plates/atmosphere/ribbons remain alongside the native body.");
                            Need(effect.NativeBody.SourceSystemCount == profile.NativeBodyPrefab.GetComponentsInChildren<ParticleSystem>(true).Length
                                && effect.NativeBody.InstanceCount == 1 && effect.NativeBody.ContentTransform != profile.NativeBodyPrefab.transform,
                                "Configured body is not one isolated source instance.");

                            // Compare the existing no-native path's lifetime, rather than
                            // duplicating Begin's lifetime formula in the test.
                            var oldProfile = Object.Instantiate(profile); temporary.Add(oldProfile);
                            oldProfile.hideFlags = HideFlags.HideAndDontSave;
                            oldProfile.NativeBodyPrefab = null; oldProfile.NativeBodyMotion = Vfx120NativeBodyMotion.None;
                            var control = Spawn(plan, oldProfile);
                            Need(Mathf.Abs(control.Life - effect.Life) < .00001f, "Native animation changed the pre-existing root lifetime.");
                            Need(control.PartCount > 0 && !control.NativeBodyConfigured, "No-native control did not retain the old fallback.");

                            float middle = plan.Shape == AreaShape.Path ? plan.Delay + plan.Length / plan.Speed * .5f : plan.Delay + .2f;
                            float travelEnd = plan.Shape == AreaShape.Path ? plan.Delay + plan.Length / plan.Speed : plan.Delay + .5f;
                            float[] times = { 0, Mathf.Max(0, plan.Delay - .22f), Mathf.Max(0, plan.Delay - .11f), plan.Delay, middle, travelEnd };
                            float previousReach = -1;
                            foreach (float age in times.Distinct().OrderBy(t => t))
                            {
                                Observe(effect, row, age);
                                VerifySpatial(effect, plan, row, age, ref previousReach);
                            }
                            Need(row.maximumLiveParticles > 0, "Vacuous shape test: native body emitted no visible-count particles at any sample.");
                            // Save a real state, expire all roles, then seek back. Equality
                            // of particle counts alone is insufficient: placement is rechecked.
                            Observe(effect, row, middle);
                            int particlesBefore = effect.NativeBody.LiveParticleCount;
                            Vector3 positionBefore = effect.NativeBody.transform.position;
                            Need(particlesBefore > 0, "Reverse-seek reference sample has no native body particles.");
                            Observe(effect, row, effect.Life);
                            Need(effect.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), "Particles remain at the existing root lifetime boundary.");
                            Observe(effect, row, effect.Life + .1f);
                            Need(effect.NativeBody.IsComplete && effect.NativeBody.LiveParticleCount == 0, "Body is not complete after Life.");
                            Observe(effect, row, middle);
                            Need(!effect.NativeBody.IsComplete && effect.NativeBody.ContentTransform.gameObject.activeInHierarchy
                                && effect.NativeBody.LiveParticleCount == particlesBefore
                                && Vector3.Distance(positionBefore, effect.NativeBody.transform.position) < .005f,
                                "Backward seek did not recover the emitted body and its previous position.");

                            int systemsBefore = effect.GetComponentsInChildren<ParticleSystem>(true).Length;
                            for (int repeat = 0; repeat < 2; repeat++)
                            {
                                var previousBody = effect.NativeBody;
                                effect.Begin(Origin, null, Origin + new Vector3(-4, -.9f, 8), Color.white);
                                Observe(effect, row, middle);
                                Need(previousBody == null, "Re-Begin left the previous native host alive in Edit mode.");
                                Need(effect.NativeBodyConfigured && effect.PartCount == 0 && effect.AccentCount == 0
                                    && effect.GetComponentsInChildren<ParticleSystem>(true).Length == systemsBefore
                                    && effect.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Native_ElementBody") == 1,
                                    "Re-Begin accumulated generated bodies or particle systems.");
                            }
                            Observe(effect, row, effect.Life + .1f);
                            Need(effect.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), "Repeated body leaves end particles.");
                            row.noGameplaySignals = effect.SignalCount == 0 && control.SignalCount == 0;
                            row.planUnchanged = ReferenceEquals(effect.ReceivedAreaPlan, plan) && JsonUtility.ToJson(plan) == planBefore;
                            row.profileUnchanged = EditorJsonUtility.ToJson(profile) == profileBefore;
                            Need(row.noGameplaySignals && row.planUnchanged && row.profileUnchanged,
                                "Presentation changed a supplied plan/profile or invented a cue signal.");
                        });
                    }
                    foreach (bool missing in new[] { true, false })
                    Test(missing ? "missing_plan_fallback" : "wrong_shape_fallback", row =>
                    {
                        Need(profile.NativeBodyPrefab != null, "Native source missing; rejection test would be vacuous.");
                        var plan = missing ? null : Plan(glyph, 0);
                        if (plan != null) plan.Shape = glyph == "노" ? AreaShape.Path : AreaShape.Cone;
                        string before = plan == null ? null : JsonUtility.ToJson(plan);
                        var effect = Spawn(plan); effect.Sample(.5f);
                        Need(!effect.NativeBodyConfigured && effect.NativeBody == null && effect.PartCount > 0,
                            "Missing/wrong plan did not retain existing body fallback.");
                        Need(effect.NativeBodyDiagnostic == "COMPATIBLE_SPATIAL_PLAN_REQUIRED", "Rejection diagnostic missing.");
                        Need(effect.GetComponentsInChildren<Transform>(true).All(Finite), "Finite wrong-shape input produced non-finite fallback transforms.");
                        row.planUnchanged = plan == null || JsonUtility.ToJson(plan) == before;
                        Need(row.planUnchanged, "Rejected caller plan was modified.");
                    });
                    Test("nan_new_body_rejection_only", row =>
                    {
                        Need(profile.NativeBodyPrefab != null, "Native source missing; NaN rejection would be vacuous.");
                        MethodInfo build = typeof(Vfx120Effect).GetMethod("BuildElementWash", Private);
                        Need(build != null, "New body-build branch missing.");
                        string[] fields = glyph == "노" ? new[] { "Point", "Direction", "Length", "Delay", "Angle" }
                            : new[] { "Point", "Direction", "Length", "Delay", "Radius", "Speed" };
                        foreach (string field in fields)
                        {
                            var plan = Plan(glyph, 0); var info = typeof(AreaImpactPlan).GetField(field);
                            info.SetValue(plan, info.FieldType == typeof(Vector3) ? (object)new Vector3(float.NaN, 0, 0) : float.NaN);
                            var host = new GameObject("ElementWash_InvalidBranch") { hideFlags = HideFlags.HideAndDontSave };
                            temporary.Add(host); SceneManager.MoveGameObjectToScene(host, scene);
                            var effect = host.AddComponent<Vfx120Effect>(); effect.Profile = profile;
                            effect.PreviewControlled = true; effect.SetAreaPlan(plan);
                            // Deliberately do NOT call legacy Begin with malformed data:
                            // only the new private branch has a NaN rejection contract.
                            build.Invoke(effect, null);
                            Need(!effect.NativeBodyConfigured && effect.NativeBody == null && host.transform.childCount == 0
                                && effect.NativeBodyDiagnostic == "COMPATIBLE_SPATIAL_PLAN_REQUIRED", "New branch did not reject NaN " + field);
                        }
                        row.detail = "Rejected new native body only: " + string.Join(",", fields) + "; legacy malformed Begin was not exercised.";
                    });
                }
            }
            catch (Exception error) { report.errors++; report.messages.Add(error.ToString()); }
            finally
            {
                DestroyTemporary(temporary);
                EditorSceneManager.ClosePreviewScene(scene);
                report.cleanupObserved = !scene.IsValid();
                Application.logMessageReceived -= logged;
            }
            report.sourcesUnchanged = true;
            foreach (var source in report.sources)
            {
                source.diskAfter = FileHash(Absolute(source.path));
                source.dependencyAfter = AssetDatabase.GetAssetDependencyHash(source.path).ToString();
                source.unchanged = source.diskBefore == source.diskAfter && source.dependencyBefore == source.dependencyAfter
                    && source.memoryBefore == MemoryHash(source.path, source.asset) && EditorUtility.IsDirty(source.asset) == source.dirtyBefore;
                report.sourcesUnchanged &= source.unchanged;
            }
            report.passed = report.rows.Count(r => r.status.StartsWith("PASS"));
            report.failed = report.rows.Count - report.passed;
            report.status = report.rows.Count == 11 && report.failed == 0 && report.errors == 0 && report.sourcesUnchanged && report.cleanupObserved
                ? "PASS_EDIT_SPATIAL_NATIVE_BODY_CONTRACT_ONLY" : "FAIL_OR_INCOMPLETE";
            string json = JsonUtility.ToJson(report, true);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX120/element_wash_audit.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, json);
            return json;
        }

        private static AreaImpactPlan Plan(string glyph, int variant) => new AreaImpactPlan
        {
            Shape = glyph == "노" ? AreaShape.Cone : AreaShape.Path,
            Point = Origin + new Vector3(3, -1.1f, -2),
            Direction = Quaternion.Euler(0, variant == 0 ? 37 : -62, 0) * Vector3.forward,
            Delay = variant == 2 ? 0 : variant == 0 ? .4f : .75f, Length = variant == 0 ? 7 : 3,
            Angle = variant == 0 ? 27 : 16, Radius = variant == 0 ? 1.6f : .65f,
            Speed = variant == 0 ? 3 : 1.3f
        };
        private static void VerifySpatial(Vfx120Effect effect, AreaImpactPlan plan, Row row, float age, ref float previousReach)
        {
            Transform body = effect.NativeBody.transform;
            Vector3 forward = plan.Direction.normalized, side = Vector3.Cross(Vector3.up, forward);
            if (plan.Shape == AreaShape.Path)
            {
                float distance = Mathf.Clamp((age - plan.Delay) * plan.Speed, 0, plan.Length);
                Vector3 expected = plan.Point + forward * distance + Vector3.up * .02f;
                float error = Vector3.Distance(body.position, expected);
                row.maximumPositionError = Mathf.Max(row.maximumPositionError, error);
                Need(error < SpatialTolerance, "Path front differs from issued Delay/Speed/Length at age " + age + ": " + error);
                float width = Vector3.Distance(body.TransformPoint(Vector3.right * .5f), body.TransformPoint(Vector3.left * .5f));
                row.maximumExtentError = Mathf.Max(row.maximumExtentError, Mathf.Abs(width - plan.Radius * 2));
                Need(Mathf.Abs(width - plan.Radius * 2) < SpatialTolerance, "Path carrier width does not follow supplied diameter.");
            }
            else
            {
                Vector3 apex = body.TransformPoint(Vector3.zero), end = body.TransformPoint(Vector3.forward);
                float length = Vector3.Dot(end - apex, forward);
                float halfWidth = Mathf.Abs(Vector3.Dot(body.TransformPoint(Vector3.forward + Vector3.right) - end, side));
                row.maximumPositionError = Mathf.Max(row.maximumPositionError, Vector3.Distance(apex, plan.Point));
                Need(Vector3.Distance(apex, plan.Point) < SpatialTolerance, "Cone emission apex moved away from issued point.");
                Need(length + SpatialTolerance >= previousReach && length <= plan.Length + SpatialTolerance, "Cone retreats/overshoots during its unfolding.");
                previousReach = length;
                Need(halfWidth <= Mathf.Tan(plan.Angle * Mathf.Deg2Rad) * length + SpatialTolerance, "Cone carrier exceeds the issued half angle.");
                if (age < plan.Delay - .05f) Need(length < plan.Length * .99f, "Cone has no delayed range unfolding.");
                if (age >= plan.Delay)
                {
                    float error = Vector3.Distance(end, plan.Point + forward * plan.Length);
                    float widthError = Mathf.Abs(halfWidth - plan.Length * Mathf.Tan(plan.Angle * Mathf.Deg2Rad));
                    row.maximumExtentError = Mathf.Max(row.maximumExtentError, Mathf.Max(error, widthError));
                    Need(error < SpatialTolerance && widthError < SpatialTolerance, "Cone full reach/width is late or differs from issued range at Delay.");
                }
            }
            Need(Vector3.Angle(body.forward, forward) < .05f, "Carrier faces a direction different from the issued plan.");
        }
        private static void Observe(Vfx120Effect effect, Row row, float age)
        {
            effect.Sample(age);
            var body = effect.NativeBody; Need(body != null && effect.NativeBodyConfigured, "Body disappeared during a preview seek.");
            var systems = body.GetComponentsInChildren<ParticleSystem>(true);
            int capacity = systems.Sum(p => p.main.maxParticles), live = systems.Sum(p => p.particleCount);
            row.maximumSystems = Mathf.Max(row.maximumSystems, systems.Length);
            row.maximumCapacity = Mathf.Max(row.maximumCapacity, capacity);
            row.maximumLiveParticles = Mathf.Max(row.maximumLiveParticles, live);
            row.samples.Add(new Sample { age = age, carrierWorldPosition = body.transform.position,
                carrierScale = body.transform.lossyScale, liveParticles = live, systems = systems.Length, capacity = capacity });
            Need(systems.Length > 0 && systems.Length <= 12 && body.ActiveSystemCount <= 12
                && body.ParticleBudget > 0 && body.ParticleBudget <= 240 && live <= capacity && capacity <= 240,
                "Native body exceeds its per-body 12-system/240-particle budget or has no systems.");
            Need(body.GetComponentsInChildren<Transform>(true).All(Finite), "A valid issued plan produced non-finite transforms.");
            foreach (var ps in systems)
            {
                var particles = new ParticleSystem.Particle[ps.main.maxParticles];
                int count = ps.GetParticles(particles);
                for (int i = 0; i < count; i++)
                    Need(Finite(particles[i].position) && Finite(particles[i].velocity) && Finite(particles[i].remainingLifetime)
                        && Finite(particles[i].GetCurrentSize3D(ps)), "Native particle contains non-finite state.");
            }
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Transform t) => Finite(t.position) && Finite(t.lossyScale)
            && Finite(t.rotation.x) && Finite(t.rotation.y) && Finite(t.rotation.z) && Finite(t.rotation.w);
        private static void Need(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
        private static void DestroyTemporary(List<Object> objects)
        { for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear(); }
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static void Remember(Object asset, HashSet<string> paths, List<Source> sources)
        {
            string root = AssetDatabase.GetAssetPath(asset); if (string.IsNullOrEmpty(root)) return;
            foreach (string path in AssetDatabase.GetDependencies(root, true))
            {
                if (!paths.Add(path) || !File.Exists(Absolute(path))) continue;
                var value = AssetDatabase.LoadMainAssetAtPath(path);
                sources.Add(new Source { path = path, asset = value, diskBefore = FileHash(Absolute(path)),
                    dependencyBefore = AssetDatabase.GetAssetDependencyHash(path).ToString(), dirtyBefore = EditorUtility.IsDirty(value),
                    memoryBefore = MemoryHash(path, value) });
            }
        }
        private static string MemoryHash(string path, Object asset)
        {
            var text = new StringBuilder();
            if (asset != null) text.Append(EditorJsonUtility.ToJson(asset));
            if (asset is GameObject go)
                foreach (var component in go.GetComponentsInChildren<Component>(true))
                    if (component != null) text.Append(EditorJsonUtility.ToJson(component));
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
                if (sub != asset && (sub is Mesh || sub is Material)) text.Append(EditorJsonUtility.ToJson(sub));
            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : "MISSING";
        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
