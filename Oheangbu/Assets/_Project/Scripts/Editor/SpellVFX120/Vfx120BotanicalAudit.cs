using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // Read-only source inventory and Edit preview tests on actual catalog clones.
    // Never starts Play, changes a scene asset, or issues a recognized letter/damage.
    public static class Vfx120BotanicalAudit
    {
        private const string CatalogPath = "Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset";
        private static readonly string[] Kinds = { "RootBind", "BambooFront", "PlantedTree" };
        private static readonly Vector3 Origin = new Vector3(1000, 1002, 1000);
        private const float Tolerance = .002f;

        [Serializable] private sealed class Inventory
        {
            public string status = "INVENTORY_ONLY_NOT_BUILT_OR_TESTED";
            public string scope = "Read actual imported source meshes/materials. Build() does not generate or change assets.";
            public List<AssetRow> rows = new List<AssetRow>();
            public string error;
        }
        [Serializable] private sealed class AssetRow
        {
            public string glyph, kind, profile, source;
            public int directChildren, meshRenderers, triangles, colliders, scripts;
        }
        [Serializable] private sealed class Report
        {
            public string status, capturedUtc, unityVersion, appMvid, botanicalRuntimeSourceSha256;
            public string authority = "EDIT_PREVIEW_SUPPLIED_PLANS_AND_CUES_NOT_GAMEPLAY";
            public string scope = "Three real botanical catalog entries: target tracking, Path center/time and authored stem pivot spacing, planted-tree release, repeat/reverse/expiry/re-Begin and source preservation.";
            public string limits = "No art, foliage coverage/hit geometry, real Update/Destroy, gameplay, rendering, shader clipping correctness, runtime allocation rate or performance PASS. Below-ground emergence at frame zero is intentional. Material-clone checks cover the botanical subtree only.";
            public Inventory inventory;
            public int passed, failed, errors;
            public bool sourcesUnchanged, cleanupObserved;
            public List<Row> rows = new List<Row>();
            public List<Source> sources = new List<Source>();
            public List<string> messages = new List<string>();
        }
        [Serializable] private sealed class Row
        {
            public string name, glyph, kind, status, detail;
            public float life, maximumCenterError, maximumPivotError;
            public bool planUnchanged, profileUnchanged;
            public List<SampleRow> samples = new List<SampleRow>();
        }
        [Serializable] private sealed class SampleRow
        {
            public float age, opacity;
            public Vector3 hostWorldPosition;
            public int enabledRenderers;
        }
        [Serializable] private sealed class Source
        {
            public string path, diskBefore, diskAfter, dependencyBefore, dependencyAfter;
            public bool unchanged;
            [NonSerialized] public Object asset;
            [NonSerialized] public bool dirtyBefore;
            [NonSerialized] public string memoryBefore;
        }

        // Compatibility with the requested Build -> Audit workflow: the actual
        // botanical builder is owned elsewhere; this method is inventory only.
        public static string Build()
        {
            if (EditorApplication.isCompiling) return "BLOCKED: compilation pending.";
            try { return JsonUtility.ToJson(ReadInventory(), true); }
            catch (Exception e) { return JsonUtility.ToJson(new Inventory { error = e.GetBaseException().Message }, true); }
        }
        public static string Run() => Audit();
        public static string Audit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return "BLOCKED: idle Edit mode required. This audit never switches Play mode.";
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                appMvid = typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),
                botanicalRuntimeSourceSha256 = FileHash(Absolute("Assets/_Project/Scripts/App/SpellVFX120/Vfx120Effect.Botanical.cs")) };
            var temporary = new List<Object>();
            var remembered = new HashSet<string>();
            var scene = EditorSceneManager.NewPreviewScene();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                report.errors++; if (report.messages.Count < 16) report.messages.Add(message);
            };
            Application.logMessageReceived += log;
            try
            {
                report.inventory = ReadInventory();
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
                Need(catalog != null, "Catalog missing.");
                foreach (string kind in Kinds)
                {
                    var matches = catalog.Entries.Where(e => e.Profile != null && e.Profile.BotanicalKind.ToString() == kind).ToArray();
                    if (matches.Length != 1)
                    {
                        report.rows.Add(new Row { kind = kind, name = "catalog_selection", status = "FAIL",
                            detail = "Expected one representative profile of this kind; found " + matches.Length });
                        continue;
                    }
                    var entry = matches[0]; var profile = entry.Profile;
                    Remember(entry.Prefab, remembered, report.sources); Remember(profile, remembered, report.sources);
                    void Test(string name, Action<Row> run)
                    {
                        var row = new Row { name = name, glyph = entry.Glyph, kind = kind }; report.rows.Add(row);
                        try { run(row); row.status = "PASS_EDIT_CONTRACT_ONLY"; }
                        catch (Exception e) { row.status = "FAIL"; row.detail = e.GetBaseException().Message; }
                        finally { Clear(temporary); }
                    }
                    Transform Target()
                    {
                        var go = new GameObject("BotanicalAudit_Target_NoCollider") { hideFlags = HideFlags.HideAndDontSave };
                        temporary.Add(go); SceneManager.MoveGameObjectToScene(go, scene);
                        go.transform.position = Origin + new Vector3(-2, -1, 5); return go.transform;
                    }
                    Vfx120Effect Spawn(AreaImpactPlan plan, Transform target, Vfx120Profile alternate = null)
                    {
                        Need(entry.Prefab != null, "Actual catalog prefab missing.");
                        var go = Object.Instantiate(entry.Prefab); temporary.Add(go);
                        go.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(go, scene);
                        var effect = go.GetComponent<Vfx120Effect>(); Need(effect != null, "Actual prefab has no Vfx120Effect.");
                        effect.Profile = alternate != null ? alternate : profile;
                        effect.PreviewControlled = true; effect.DemonstrationCues = false;
                        effect.SetImpactClock(.35f); effect.SetAreaPlan(plan);
                        effect.Begin(Origin, target, Origin + new Vector3(0, -1, 6), Color.white);
                        return effect;
                    }
                    int variants = kind == "BambooFront" || kind == "PlantedTree" ? 2 : 1;
                    for (int v = 0; v < variants; v++)
                    {
                        int variant = v;
                        Test("placement_repeat_reverse_expiry_rebegin_" + v, row =>
                        {
                            Need(profile.BotanicalPrefab != null, "Botanical source missing: run the actual builder before this audit.");
                            var target = Target();
                            var plan = kind == "BambooFront" ? PathPlan(variant) : kind == "PlantedTree" && variant == 1 ? CirclePlan() : null;
                            string planBefore = plan == null ? null : JsonUtility.ToJson(plan), profileBefore = EditorJsonUtility.ToJson(profile);
                            var effect = Spawn(plan, target); row.life = effect.Life;
                            Need(effect.BotanicalConfigured && effect.BotanicalInstance != null, "Configure failed: " + effect.BotanicalDiagnostic);
                            var noBotanical = Object.Instantiate(profile); temporary.Add(noBotanical);
                            noBotanical.hideFlags = HideFlags.HideAndDontSave; noBotanical.BotanicalPrefab = null;
                            var control = Spawn(plan, target, noBotanical);
                            Need(!control.BotanicalConfigured && control.PartCount > 0, "Unassigned botanical control did not retain old body.");
                            Need(Mathf.Abs(control.Life - effect.Life) < .00001f, "Botanical expression changed the existing Life clock.");
                            float delay = plan == null ? .35f : plan.Delay;
                            float middle = kind == "BambooFront" ? delay + plan.Length / plan.Speed * .5f : delay + .6f;
                            float end = kind == "BambooFront" ? delay + plan.Length / plan.Speed : delay + .9f;
                            float[] times = { 0, delay * .5f, delay, middle, end };
                            foreach (float age in times.Distinct().OrderBy(x => x))
                            {
                                Observe(effect, profile, row, age);
                                CheckPlacement(effect, profile, plan, target, kind, age, row);
                                string first = State(effect); Observe(effect, profile, row, age);
                                Need(first == State(effect), "Same-frame repeated Sample changed botanical transforms/opacity/renderers.");
                            }
                            Observe(effect, profile, row, middle);
                            Need(effect.BotanicalOpacity > .05f && Enabled(effect.BotanicalInstance) > 0, "No visible-count botanical body at the reference sample.");
                            string reference = State(effect);
                            if (kind == "RootBind")
                            {
                                Vector3 before = effect.BotanicalInstance.transform.position;
                                Vector3 move = new Vector3(1.1f, 0, -.8f); target.position += move;
                                Observe(effect, profile, row, middle);
                                Need(Vector3.Distance(effect.BotanicalInstance.transform.position - before, move) < Tolerance,
                                    "Root bind did not follow the actual target's horizontal translation.");
                                CheckPlacement(effect, profile, plan, target, kind, middle, row);
                                target.position -= move; Observe(effect, profile, row, middle);
                                reference = State(effect);
                            }
                            Observe(effect, profile, row, effect.Life);
                            Need(Enabled(effect.BotanicalInstance) == 0 && effect.BotanicalOpacity <= .00001f, "Botanical remains enabled at Life.");
                            Observe(effect, profile, row, effect.Life + .1f);
                            Need(Enabled(effect.BotanicalInstance) == 0, "Botanical reappeared after Life.");
                            Observe(effect, profile, row, middle);
                            Need(State(effect) == reference, "Backward seek did not recover the same botanical state.");
                            Need(effect.SignalCount == 0, "Ordinary sampling invented a gameplay/presentation cue.");
                            if (kind == "PlantedTree")
                            {
                                float releaseAt = middle;
                                effect.Signal(Vfx120Effect.Cue.Hit);
                                effect.Signal(Vfx120Effect.Cue.Release);
                                Observe(effect, profile, row, releaseAt + .5f);
                                Need(releaseAt + .5f < effect.Life, "Release sample overlaps natural expiry; cannot prove early release.");
                                Need(effect.BotanicalOpacity <= .00001f && Enabled(effect.BotanicalInstance) == 0,
                                    "Tree did not fade out within .5s of the supplied Release cue.");
                            }
                            int hierarchyCount = effect.BotanicalInstance.GetComponentsInChildren<Transform>(true).Length;
                            for (int repeat = 0; repeat < 2; repeat++)
                            {
                                var old = effect.BotanicalInstance;
                                effect.Begin(Origin, target, Origin + new Vector3(0, -1, 6), Color.white);
                                Observe(effect, profile, row, middle);
                                Need(old == null && effect.BotanicalConfigured && effect.SignalCount == 0 && effect.ReleaseAt < 0,
                                    "Re-Begin retained the old host or a release cue.");
                                Need(effect.BotanicalInstance.GetComponentsInChildren<Transform>(true).Length == hierarchyCount
                                    && Enabled(effect.BotanicalInstance) > 0 && effect.BotanicalOpacity > .05f,
                                    "Re-Begin accumulated geometry or failed to restore the body.");
                            }
                            row.planUnchanged = ReferenceEquals(effect.ReceivedAreaPlan, plan) && (plan == null || JsonUtility.ToJson(plan) == planBefore);
                            row.profileUnchanged = EditorJsonUtility.ToJson(profile) == profileBefore;
                            Need(row.planUnchanged && row.profileUnchanged, "Supplied plan/profile was mutated.");
                        });
                    }
                    if (kind == "BambooFront") foreach (bool missing in new[] { true, false })
                    Test(missing ? "missing_path_fallback" : "incompatible_plan_fallback", row =>
                    {
                        Need(profile.BotanicalPrefab != null, "Missing source makes fallback test vacuous.");
                        var plan = missing ? null : CirclePlan(); string before = plan == null ? null : JsonUtility.ToJson(plan);
                        var effect = Spawn(plan, Target()); effect.Sample(.5f);
                        Need(!effect.BotanicalConfigured && effect.BotanicalInstance == null && effect.PartCount > 0,
                            "Missing/incompatible Path did not retain the original procedural body.");
                        Need(!string.IsNullOrWhiteSpace(effect.BotanicalDiagnostic), "Rejected plan has no diagnostic.");
                        Need(effect.GetComponentsInChildren<Transform>(true).All(Finite), "Finite fallback plan generated non-finite transforms.");
                        Need(plan == null || before == JsonUtility.ToJson(plan), "Rejected plan was modified.");
                    });
                }
            }
            catch (Exception e) { report.errors++; report.messages.Add(e.ToString()); }
            finally
            {
                Clear(temporary); EditorSceneManager.ClosePreviewScene(scene);
                report.cleanupObserved = !scene.IsValid(); Application.logMessageReceived -= log;
            }
            report.sourcesUnchanged = true;
            foreach (var source in report.sources)
            {
                source.diskAfter = FileHash(Absolute(source.path));
                source.dependencyAfter = AssetDatabase.GetAssetDependencyHash(source.path).ToString();
                source.unchanged = source.diskBefore == source.diskAfter && source.dependencyBefore == source.dependencyAfter
                    && source.memoryBefore == Memory(source.path, source.asset) && EditorUtility.IsDirty(source.asset) == source.dirtyBefore;
                report.sourcesUnchanged &= source.unchanged;
            }
            report.passed = report.rows.Count(r => r.status == "PASS_EDIT_CONTRACT_ONLY"); report.failed = report.rows.Count - report.passed;
            report.status = report.rows.Count == 7 && report.failed == 0 && report.errors == 0 && report.cleanupObserved && report.sourcesUnchanged
                ? "PASS_BOTANICAL_EDIT_CONTRACTS_ONLY" : "FAIL_OR_INCOMPLETE";
            string json = JsonUtility.ToJson(report, true);
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX120/botanical_audit.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllText(output, json); return json;
        }

        private static AreaImpactPlan PathPlan(int v) => new AreaImpactPlan { Shape = AreaShape.Path,
            Point = Origin + new Vector3(2, -1, -2), Direction = Quaternion.Euler(0, v == 0 ? 31 : -57, 0) * Vector3.forward,
            Delay = v == 0 ? .4f : .7f, Speed = v == 0 ? 2.5f : 1.8f, Length = v == 0 ? 6 : 3.5f, Radius = v == 0 ? 1.3f : .65f };
        private static AreaImpactPlan CirclePlan() => new AreaImpactPlan { Shape = AreaShape.Circle,
            Point = Origin + new Vector3(3, -.8f, 4), Direction = Vector3.forward, Delay = .35f, Radius = 1.4f };

        private static void CheckPlacement(Vfx120Effect effect, Vfx120Profile profile, AreaImpactPlan plan, Transform target, string kind, float age, Row row)
        {
            Transform host = effect.BotanicalInstance.transform;
            if (kind == "BambooFront")
            {
                Vector3 direction = new Vector3(plan.Direction.x, 0, plan.Direction.z).normalized;
                Vector3 expected = plan.Point + direction * Mathf.Clamp((age - plan.Delay) * plan.Speed, 0, plan.Length);
                float error = Vector3.Distance(host.position, expected);
                row.maximumCenterError = Mathf.Max(row.maximumCenterError, error);
                Need(error < Tolerance && Vector3.Angle(host.forward, direction) < .05f, "Bamboo center/direction differs from the issued Path clock.");
                Need(Mathf.Abs(effect.BotanicalNominalHalfWidth - plan.Radius) < .00001f, "Bamboo reported halfwidth differs from supplied Radius.");
                Need(host.childCount == profile.BotanicalPrefab.transform.childCount && host.childCount == 7, "Expected seven authored stem pivots.");
                for (int i = 0; i < host.childCount; i++)
                {
                    var stem = host.GetChild(i); var source = profile.BotanicalPrefab.transform.GetChild(i);
                    float pivotError = Mathf.Abs(stem.localPosition.x - source.localPosition.x * plan.Radius);
                    row.maximumPivotError = Mathf.Max(row.maximumPivotError, pivotError);
                    Need(pivotError < Tolerance, "Stem pivot spacing does not follow the authored 2m width and supplied halfwidth.");
                    Need(Vector3.Distance(stem.localScale, source.localScale) < .00001f, "Bamboo shaft geometry was stretched to fake width.");
                }
            }
            else
            {
                Vector3 expected = kind == "PlantedTree" && plan != null ? plan.Point : new Vector3(target.position.x, Origin.y - 1, target.position.z);
                float error = Vector3.Distance(host.position, expected);
                row.maximumCenterError = Mathf.Max(row.maximumCenterError, error);
                Need(error < Tolerance, "Root/tree host is not anchored to the intended target ground or Circle point.");
            }
        }
        private static void Observe(Vfx120Effect effect, Vfx120Profile profile, Row row, float age)
        {
            effect.Sample(age); var body = effect.BotanicalInstance;
            Need(effect.BotanicalConfigured && body != null, "Botanical host unavailable during Sample.");
            Need(effect.PartCount == 0 && effect.AccentCount == 0, "Procedural body/accent remains alongside replacement.");
            Need(body.GetComponentsInChildren<Transform>(true).All(Finite) && Finite(effect.BotanicalOpacity), "Non-finite botanical transform or opacity.");
            Need(body.GetComponentsInChildren<Collider>(true).Length == 0 && body.GetComponentsInChildren<MonoBehaviour>(true).Length == 0
                && body.GetComponentsInChildren<Rigidbody>(true).Length == 0 && body.GetComponentsInChildren<Animator>(true).Length == 0
                && body.GetComponentsInChildren<ParticleSystem>(true).Length == 0,
                "Botanical content introduces a collider/script/rigidbody/Animator/particle system.");
            var renderers = body.GetComponentsInChildren<MeshRenderer>(true);
            var sourceRenderers = profile.BotanicalPrefab.GetComponentsInChildren<MeshRenderer>(true);
            Need(renderers.Length > 0 && renderers.Length == sourceRenderers.Length, "Botanical renderer count differs from actual source.");
            for (int i = 0; i < renderers.Length; i++)
            {
                var filter = renderers[i].GetComponent<MeshFilter>(); var source = sourceRenderers[i].GetComponent<MeshFilter>();
                Need(filter != null && source != null && filter.sharedMesh != null && filter.sharedMesh == source.sharedMesh,
                    "Botanical geometry was copied/replaced rather than sharing the source mesh.");
                var materials = renderers[i].sharedMaterials; var original = sourceRenderers[i].sharedMaterials;
                Need(materials.Length == original.Length && materials.Length > 0, "Botanical material slots differ from source.");
                for (int j = 0; j < materials.Length; j++)
                    Need(materials[j] != null && materials[j] == original[j] && AssetDatabase.Contains(materials[j]),
                        "Botanical subtree created/replaced a material instance.");
                var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block);
                Need(Finite(block.GetFloat("_Visibility")) && Finite(block.GetFloat("_GroundY"))
                    && Mathf.Abs(block.GetFloat("_Visibility") - effect.BotanicalOpacity) < .00001f,
                    "Actual renderer visibility/ground property block is non-finite or differs from reported opacity.");
            }
            row.samples.Add(new SampleRow { age = age, opacity = effect.BotanicalOpacity,
                hostWorldPosition = body.transform.position, enabledRenderers = Enabled(body) });
        }
        private static string State(Vfx120Effect effect)
        {
            var b = new StringBuilder(); b.Append(effect.BotanicalOpacity.ToString("R"));
            foreach (var t in effect.BotanicalInstance.GetComponentsInChildren<Transform>(true))
                b.Append(t.name).Append(t.localPosition.ToString("F6")).Append(t.localRotation.ToString("F6")).Append(t.localScale.ToString("F6"));
            foreach (var r in effect.BotanicalInstance.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock(); r.GetPropertyBlock(block);
                b.Append(r.enabled).Append(r.gameObject.activeInHierarchy)
                    .Append(block.GetFloat("_Visibility").ToString("R")).Append(block.GetFloat("_GroundY").ToString("R"));
            }
            return b.ToString();
        }
        private static int Enabled(GameObject go) => go == null ? 0 : go.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy);
        private static Inventory ReadInventory()
        {
            var result = new Inventory(); var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(CatalogPath);
            Need(catalog != null, "Catalog missing.");
            foreach (var e in catalog.Entries.Where(e => e.Profile != null && Kinds.Contains(e.Profile.BotanicalKind.ToString())))
            {
                var source = e.Profile.BotanicalPrefab; var row = new AssetRow { glyph = e.Glyph,
                    kind = e.Profile.BotanicalKind.ToString(), profile = AssetDatabase.GetAssetPath(e.Profile), source = AssetDatabase.GetAssetPath(source) };
                if (source != null)
                {
                    row.directChildren = source.transform.childCount; row.meshRenderers = source.GetComponentsInChildren<MeshRenderer>(true).Length;
                    row.colliders = source.GetComponentsInChildren<Collider>(true).Length; row.scripts = source.GetComponentsInChildren<MonoBehaviour>(true).Length;
                    foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null)
                        for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                            if (mf.sharedMesh.GetTopology(s) == MeshTopology.Triangles) row.triangles += checked((int)mf.sharedMesh.GetIndexCount(s) / 3);
                }
                result.rows.Add(row);
            }
            return result;
        }
        private static void Remember(Object asset, HashSet<string> seen, List<Source> result)
        {
            string root = AssetDatabase.GetAssetPath(asset); if (string.IsNullOrEmpty(root)) return;
            foreach (string path in AssetDatabase.GetDependencies(root, true))
            {
                if (!seen.Add(path) || !File.Exists(Absolute(path))) continue;
                var value = AssetDatabase.LoadMainAssetAtPath(path);
                result.Add(new Source { path = path, asset = value, diskBefore = FileHash(Absolute(path)),
                    dependencyBefore = AssetDatabase.GetAssetDependencyHash(path).ToString(), dirtyBefore = EditorUtility.IsDirty(value), memoryBefore = Memory(path, value) });
            }
        }
        private static string Memory(string path, Object asset)
        {
            var b = new StringBuilder(); if (asset != null) b.Append(EditorJsonUtility.ToJson(asset));
            if (asset is GameObject go) foreach (var c in go.GetComponentsInChildren<Component>(true)) if (c != null) b.Append(EditorJsonUtility.ToJson(c));
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path)) if (sub != asset && (sub is Material || sub is Mesh)) b.Append(EditorJsonUtility.ToJson(sub));
            return Hash(Encoding.UTF8.GetBytes(b.ToString()));
        }
        private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Transform t) => Finite(t.position) && Finite(t.lossyScale) && Finite(t.rotation.x) && Finite(t.rotation.y) && Finite(t.rotation.z) && Finite(t.rotation.w);
        private static void Clear(List<Object> temporary) { for (int i = temporary.Count - 1; i >= 0; i--) if (temporary[i] != null) Object.DestroyImmediate(temporary[i]); temporary.Clear(); }
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : "MISSING";
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    }
}
