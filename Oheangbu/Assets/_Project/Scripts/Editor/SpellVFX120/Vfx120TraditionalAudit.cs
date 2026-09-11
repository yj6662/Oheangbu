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
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Edit-preview native PS seeks only. Does not exercise live Update/Animator clocks,
    // input, recognition, real combat hits, scene wiring, performance, or art quality.
    public static class Vfx120TraditionalAudit
    {
        [Serializable] private sealed class Report
        {
            public string stage = "KTP_EDIT_PREVIEW_CONTRACT", authority = "DIAGNOSTIC_SUPPLIED_CLOCK_NOT_GAMEPLAY";
            public string capturedUtc, unityVersion, appMvid, sourceSha256, meshySourceSha256, status, scope;
            public string runtimeUpdate = "UNVERIFIED", animatorRuntime = "UNVERIFIED", art = "UNVERIFIED", performance = "UNVERIFIED";
            public int profileCount, passed, failed, unassigned, errorCount, catalogEntryCount, catalogUniqueGlyphs;
            public bool sourceUnchanged, cleanupObserved;
            public string sourceMemoryPolicy = "Unchanged hash algorithm: concatenated per-main/subasset JSON hashes in LoadAllAssetsAtPath order; GameObject component JSON remains in hierarchy traversal order. Ordering or lazy non-dirty serialized state can affect this hash. Mismatches are preserved for diagnosis, never canonicalized or ignored.";
            public List<Row> rows = new List<Row>();
            public List<Source> sources = new List<Source>();
            public List<string> errors = new List<string>();
        }
        [Serializable] private sealed class Row
        {
            public string glyph, status, diagnostic, modelDiagnostic = "NOT_ASSIGNED";
            public float suppliedImpactClock, life;
            public bool configuredRoles, noFallback, supportedShaderFlags, sourceColorSlots,
                budgets, replacement, endZero, reverseImpactHidden, finalZero;
            public int proceduralBodyCount, nativeRoles, unsupportedShaderCount;
            public bool expectsCast, expectsImpact, expectsField, expectsModel, expectsBotanical;
            public string botanicalScope;
            public bool botanicalReferences = true, botanicalEndHidden = true;
            public bool modelConfigured = true, modelReferences = true, modelFixedTransforms = true,
                modelGround = true, modelEndHidden = true, modelReverseVisible;
            public float modelCircleCenterError, modelPivotHeightError, modelBoundsFloorDelta;
            public string modelGroundScope = "STATIC_TRANSFORM_AND_MESH_BOUNDS_ONLY; no physics contact test";
            [NonSerialized] public TransformRecord[] ModelTransforms;
            [NonSerialized] public Mesh[] ModelSourceMeshes;
            [NonSerialized] public Material[][] ModelSourceMaterials;
            public List<SampleRow> samples = new List<SampleRow>();
        }
        [Serializable] private sealed class SampleRow
        {
            public string direction;
            public float age;
            public int liveParticles, particleCapacity, systems, impactParticles;
            public bool withinBudget, impactHidden, allCompleteOrWaiting;
            public int visibleModelRenderers;
            public float modelAlpha;
        }
        private struct TransformRecord
        {
            public Transform Transform;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }
        [Serializable] private sealed class Source
        {
            public string path, diskShaBefore, diskShaAfter, dependencyHashBefore, dependencyHashAfter;
            public string serializeMemoryBefore, serializeMemoryAfter, memoryDiagnosticBefore, memoryDiagnosticAfter;
            public bool dirtyBefore, dirtyAfter;
            public bool memoryUnchanged;
            [NonSerialized] public UnityEngine.Object Asset;
            [NonSerialized] public string MemoryBefore;
            [NonSerialized] public string MemoryBeforeText;
        }

        public static string Run() => Execute(false);
        public static string RunCatalog() => Execute(true);

        private static string Execute(bool wholeCatalog)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "REJECTED: Edit mode required; this audit never stops or changes Play mode.";
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                appMvid = typeof(Vfx120Effect).Module.ModuleVersionId.ToString(),
                sourceSha256 = FileHash(Path.Combine(Application.dataPath, "_Project/Scripts/App/SpellVFX120/Vfx120TraditionalMotif.cs")),
                meshySourceSha256 = FileHash(Path.Combine(Application.dataPath, "_Project/Scripts/App/SpellVFX120/Vfx120MeshySummon.cs")),
                scope = wholeCatalog ? "CATALOG_SCAN_120; ASSIGNED_NATIVE_CONTRACTS_ONLY" : "THREE_PROFILES_가_곰_구" };
            var scene = EditorSceneManager.NewPreviewScene();
            var roots = new List<GameObject>();
            var sourceAssets = new HashSet<string>();
            var dependencyRoots = new HashSet<string>();
            var selected = new List<Vfx120Profile>();
            Application.LogCallback log = (message, trace, type) =>
            {
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                report.errorCount++; if (report.errors.Count < 16) report.errors.Add(message);
            };
            Application.logMessageReceived += log;
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
                if (catalog == null) throw new InvalidOperationException("Catalog missing.");
                report.catalogEntryCount = catalog.Entries.Length;
                report.catalogUniqueGlyphs = catalog.Entries.Select(e => e.Glyph).Distinct().Count();
                var entries = wholeCatalog ? catalog.Entries : new[] { "가", "곰", "구" }
                    .Select(glyph => catalog.Entries.FirstOrDefault(e => e.Glyph == glyph)).ToArray();
                foreach (var entry in entries)
                {
                    var profile = entry.Profile;
                    if (profile == null)
                    { report.rows.Add(new Row { glyph = entry.Glyph, status = "FAIL_MISSING_PROFILE" }); continue; }
                    selected.Add(profile);
                    Remember(profile, sourceAssets, dependencyRoots, report.sources);
                    Remember(profile.NativeCastPrefab, sourceAssets, dependencyRoots, report.sources);
                    Remember(profile.NativeImpactPrefab, sourceAssets, dependencyRoots, report.sources);
                    Remember(profile.NativeFieldPrefab, sourceAssets, dependencyRoots, report.sources);
                    Remember(profile.SummonPrefab, sourceAssets, dependencyRoots, report.sources);
                }
                foreach (var profile in selected)
                {
                    var row = new Row { glyph = profile.Glyph, budgets = true, endZero = true,
                        reverseImpactHidden = true, supportedShaderFlags = true, sourceColorSlots = true,
                        expectsCast = profile.NativeCastPrefab != null, expectsImpact = profile.NativeImpactPrefab != null,
                        expectsField = profile.NativeFieldPrefab != null, expectsModel = profile.SummonPrefab != null,
                        expectsBotanical = profile.BotanicalPrefab != null && (profile.BotanicalKind == Vfx120BotanicalKind.RootBind
                            || profile.BotanicalKind == Vfx120BotanicalKind.PlantedTree),
                        botanicalScope = "No spatial plan supplied here. Root/tree replacement checked; Bamboo Path and motion require botanical_audit.json." };
                    report.rows.Add(row);
                    if (wholeCatalog && !row.expectsCast && !row.expectsImpact && !row.expectsField && !row.expectsModel)
                    { row.status = "UNASSIGNED_NOT_TESTED"; continue; }
                    var host = new GameObject("KTP_Audit_" + profile.Glyph) { hideFlags = HideFlags.HideAndDontSave };
                    roots.Add(host); SceneManager.MoveGameObjectToScene(host, scene);
                    var effect = host.AddComponent<Vfx120Effect>();
                    effect.Profile = profile; effect.PreviewControlled = true; effect.DemonstrationCues = false;
                    // A diagnostic issued clock lets this isolated test reach the normal
                    // presentation branch. It is not evidence of a real combat event.
                    if (row.expectsImpact) { row.suppliedImpactClock = .6f; effect.SetImpactClock(.6f); }
                    var origin = new Vector3(30000, 30000, 30000);
                    effect.Begin(origin, null, origin + new Vector3(0, .1f, 4), Color.white);
                    row.life = effect.Life; row.proceduralBodyCount = effect.PartCount;
                    CaptureModelContract(effect, row);
                    foreach (float age in new[] { 0f, .2f, .6f, 1.2f, effect.Life, effect.Life + .1f })
                        Observe(effect, row, age, "forward");
                    foreach (float age in new[] { effect.Life * .5f, 1.2f, .6f, .2f, 0f }) Observe(effect, row, age, "reverse");
                    row.configuredRoles = MatchesRole(row.expectsCast, effect.NativeCast, Vfx120TraditionalMotif.Role.Cast)
                        && MatchesRole(row.expectsImpact, effect.NativeImpact, Vfx120TraditionalMotif.Role.Impact)
                        && MatchesRole(row.expectsField, effect.NativeField, profile.NativeFieldRole)
                        && (!row.expectsField || effect.NativeFieldConfigured);
                    row.nativeRoles = Motifs(effect).Count();
                    row.diagnostic = effect.NativeDiagnostic;
                    row.modelDiagnostic = effect.MeshyDiagnostic;
                    row.noFallback = row.configuredRoles && row.modelConfigured && !row.diagnostic.Contains("FALLBACK")
                        && !row.diagnostic.Contains("fallback") && !row.modelDiagnostic.Contains("FALLBACK")
                        && !row.modelDiagnostic.Contains("fallback");
                    bool sealHidden = host.GetComponentsInChildren<Transform>(true)
                        .Where(t => t.name == "TraditionalMotif").All(t => !t.gameObject.activeInHierarchy);
                    if (row.expectsBotanical)
                    {
                        row.botanicalReferences = effect.BotanicalConfigured && effect.BotanicalInstance != null;
                        if (row.botanicalReferences)
                        {
                            var meshes = effect.BotanicalInstance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh);
                            var materials = effect.BotanicalInstance.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials);
                            row.botanicalReferences &= meshes.SequenceEqual(profile.BotanicalPrefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh))
                                && materials.SequenceEqual(profile.BotanicalPrefab.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials));
                        }
                    }
                    bool expectsReplacement = row.expectsModel || row.expectsBotanical || (row.expectsField && profile.NativeReplaceBody);
                    row.replacement = expectsReplacement
                        ? (effect.NativeReplacesProcedural || effect.MeshyConfigured || (row.expectsBotanical && row.botanicalReferences)) && effect.PartCount == 0 && effect.AccentCount == 0
                            && !host.GetComponentsInChildren<LineRenderer>(true).Any()
                            && !host.GetComponentsInChildren<Vfx120Atmosphere>(true).Any()
                        : !effect.NativeReplacesProcedural && !effect.MeshyConfigured && effect.PartCount > 0
                            && (!row.expectsField || sealHidden);
                    foreach (var motif in Motifs(effect))
                    {
                        row.sourceColorSlots &= motif.UntintedMaterialSlots == 0;
                        foreach (var renderer in motif.GetComponentsInChildren<Renderer>(true))
                            foreach (var material in renderer.sharedMaterials)
                            {
                                bool supported = material != null && material.shader != null && material.shader.isSupported
                                    && material.shader.name != "Hidden/InternalErrorShader";
                                row.supportedShaderFlags &= supported;
                                if (!supported) row.unsupportedShaderCount++;
                            }
                    }
                    effect.Sample(effect.Life + .1f);
                    row.finalZero = host.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0);
                    if (row.expectsModel) row.modelEndHidden &= VisibleModelRenderers(effect) == 0;
                    if (row.expectsBotanical) row.botanicalEndHidden = effect.BotanicalInstance != null
                        && effect.BotanicalInstance.GetComponentsInChildren<MeshRenderer>(true).All(r => !r.enabled);
                    row.status = row.configuredRoles && row.noFallback && row.supportedShaderFlags && row.sourceColorSlots
                        && row.budgets && row.replacement && row.endZero && row.reverseImpactHidden && row.finalZero
                        && row.modelConfigured && row.modelReferences && row.modelFixedTransforms && row.modelGround && row.modelEndHidden
                        && (!row.expectsModel || row.modelReverseVisible) && row.botanicalReferences && row.botanicalEndHidden
                        ? "PASS_EDIT_PREVIEW_CONTRACT_ONLY" : "FAIL";
                    // Bound peak allocations during the catalog run. No 120-model pile-up.
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
            catch (Exception error) { report.errorCount++; report.errors.Add(error.ToString()); }
            finally
            {
                foreach (var root in roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
                report.cleanupObserved = roots.All(root => root == null);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                Application.logMessageReceived -= log;
            }
            report.sourceUnchanged = true;
            string memoryDiagnostics = null;
            foreach (var source in report.sources)
            {
                source.diskShaAfter = FileHash(AbsoluteAsset(source.path));
                source.dependencyHashAfter = AssetDatabase.GetAssetDependencyHash(source.path).ToString();
                source.dirtyAfter = EditorUtility.IsDirty(source.Asset);
                source.serializeMemoryAfter = SourceMemoryHash(source.path, source.Asset, out var memoryAfterText);
                source.memoryUnchanged = source.MemoryBefore == source.serializeMemoryAfter;
                report.sourceUnchanged &= source.diskShaBefore == source.diskShaAfter
                    && source.dependencyHashBefore == source.dependencyHashAfter
                    && source.dirtyBefore == source.dirtyAfter
                    && source.memoryUnchanged;
                if (!source.memoryUnchanged)
                {
                    // Only mismatches reach disk. Full before/after text stays out of
                    // the ordinary JSON report; this does not relax any PASS condition.
                    if (memoryDiagnostics == null)
                    {
                        memoryDiagnostics = Path.Combine(Vfx120Editor.Output, "SourceMemoryDiagnostics18",
                            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                        Directory.CreateDirectory(memoryDiagnostics);
                    }
                    string name = Path.GetFileNameWithoutExtension(source.path) + "_" + Hash(Encoding.UTF8.GetBytes(source.path)).Substring(0, 12);
                    source.memoryDiagnosticBefore = Path.GetFullPath(Path.Combine(memoryDiagnostics, name + "_before.txt"));
                    source.memoryDiagnosticAfter = Path.GetFullPath(Path.Combine(memoryDiagnostics, name + "_after.txt"));
                    try
                    {
                        File.WriteAllText(source.memoryDiagnosticBefore, source.MemoryBeforeText, new UTF8Encoding(false));
                        File.WriteAllText(source.memoryDiagnosticAfter, memoryAfterText, new UTF8Encoding(false));
                    }
                    catch (Exception error)
                    {
                        report.errorCount++;
                        report.errors.Add("Memory mismatch diagnostic write failed for " + source.path + ": " + error.Message);
                    }
                }
            }
            report.profileCount = report.rows.Count; report.passed = report.rows.Count(r => r.status == "PASS_EDIT_PREVIEW_CONTRACT_ONLY");
            report.unassigned = report.rows.Count(r => r.status == "UNASSIGNED_NOT_TESTED");
            report.failed = report.profileCount - report.passed - report.unassigned;
            bool completeScope = wholeCatalog
                ? report.profileCount == 120 && report.catalogEntryCount == 120 && report.catalogUniqueGlyphs == 120
                : report.profileCount == 3;
            report.status = completeScope && report.passed > 0 && report.failed == 0 && report.errorCount == 0
                && report.sourceUnchanged && report.cleanupObserved
                ? wholeCatalog ? "PASS_ASSIGNED_EDIT_PREVIEW_CONTRACTS_ONLY" : "PASS_EDIT_PREVIEW_CONTRACT_ONLY" : "FAIL";
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory(Vfx120Editor.Output);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, wholeCatalog ? "traditional_catalog_audit.json" : "traditional_audit.json"), json, new UTF8Encoding(false));
            return json;
        }

        private static void Observe(Vfx120Effect effect, Row row, float age, string direction)
        {
            effect.Sample(age);
            var sample = new SampleRow { age = age, direction = direction, withinBudget = true, allCompleteOrWaiting = true };
            foreach (var motif in Motifs(effect))
            {
                var systems = motif.GetComponentsInChildren<ParticleSystem>(true);
                int allocated = systems.Sum(p => p.main.maxParticles);
                sample.systems += systems.Length; sample.particleCapacity += allocated; sample.liveParticles += motif.LiveParticleCount;
                sample.withinBudget &= motif.SourceSystemCount <= 32 && systems.Length <= 32
                    && allocated == motif.ParticleBudget && allocated <= 400 && motif.LiveParticleCount <= allocated
                    && systems.All(p => p.particleCount <= p.main.maxParticles);
                sample.allCompleteOrWaiting &= motif.IsComplete || motif.Status == "PREVIEW_WAITING_FOR_START";
            }
            var impact = effect.NativeImpact;
            sample.impactParticles = impact != null ? impact.LiveParticleCount : 0;
            sample.impactHidden = impact == null || (impact.LiveParticleCount == 0
                && (impact.ContentTransform == null || !impact.ContentTransform.gameObject.activeInHierarchy));
            row.budgets &= sample.withinBudget;
            if (age >= effect.Life) row.endZero &= sample.liveParticles == 0;
            if (direction == "reverse" && row.suppliedImpactClock > 0 && age < row.suppliedImpactClock)
                row.reverseImpactHidden &= sample.impactHidden;
            if (row.expectsModel)
            {
                var model = effect.MeshyModel;
                sample.visibleModelRenderers = VisibleModelRenderers(effect);
                sample.modelAlpha = model != null ? model.Alpha : 0;
                if (age >= effect.Life) row.modelEndHidden &= sample.visibleModelRenderers == 0 && sample.modelAlpha == 0;
                if (direction == "reverse" && age > 0 && age < effect.Life)
                    row.modelReverseVisible |= sample.visibleModelRenderers > 0 && sample.modelAlpha > 0;
                row.modelReferences &= ModelReferences(effect, row);
                if (row.ModelTransforms != null) foreach (var saved in row.ModelTransforms)
                {
                    var t = saved.Transform;
                    row.modelFixedTransforms &= t != null && Finite(t.localPosition) && Finite(t.localScale)
                        && (t.localPosition - saved.Position).sqrMagnitude <= 1e-10f
                        && (t.localScale - saved.Scale).sqrMagnitude <= 1e-10f
                        && Quaternion.Angle(t.localRotation, saved.Rotation) <= .001f;
                }
            }
            row.samples.Add(sample);
        }

        private static bool MatchesRole(bool expected, Vfx120TraditionalMotif actual, Vfx120TraditionalMotif.Role role)
            => expected ? actual != null && actual.MotifRole == role : actual == null;

        private static void CaptureModelContract(Vfx120Effect effect, Row row)
        {
            if (!row.expectsModel)
            { row.modelConfigured = !effect.MeshyConfigured && effect.MeshyModel == null; return; }
            var model = effect.MeshyModel;
            row.modelConfigured = effect.MeshyConfigured && model != null && model.InstanceCount == 1;
            if (!row.modelConfigured) { row.modelReferences = row.modelGround = false; return; }
            row.ModelTransforms = model.GetComponentsInChildren<Transform>(true).Select(t => new TransformRecord
                { Transform = t, Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale }).ToArray();
            row.ModelSourceMeshes = effect.Profile.SummonPrefab.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
            row.ModelSourceMaterials = effect.Profile.SummonPrefab.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterials).ToArray();
            row.modelReferences = ModelReferences(effect, row);
            var point = model.transform.localPosition;
            if (effect.NativeField != null)
            {
                var circle = effect.NativeField.transform.localPosition;
                row.modelCircleCenterError = new Vector2(point.x - circle.x, point.z - circle.z).magnitude;
                row.modelPivotHeightError = Mathf.Abs(point.y - (circle.y - .05f));
            }
            else
            {
                // No collider is created; the remote diagnostic origin uses the existing
                // GroundHeight fallback (-1 local), then the model's +.01 offset.
                row.modelPivotHeightError = Mathf.Abs(point.y + .99f);
            }
            float minY = float.PositiveInfinity;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                var matrix = effect.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    minY = Mathf.Min(minY, matrix.MultiplyPoint3x4(corner).y);
                }
            }
            row.modelBoundsFloorDelta = minY - point.y;
            row.modelGround = row.modelCircleCenterError <= .001f && row.modelPivotHeightError <= .0025f
                && !float.IsInfinity(minY) && !float.IsNaN(minY) && Mathf.Abs(row.modelBoundsFloorDelta) <= .02f;
        }

        private static bool ModelReferences(Vfx120Effect effect, Row row)
        {
            var model = effect.MeshyModel;
            if (model == null || row.ModelSourceMeshes == null || row.ModelSourceMaterials == null) return false;
            var meshes = model.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
            var materials = model.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterials).ToArray();
            if (!meshes.SequenceEqual(row.ModelSourceMeshes) || materials.Length != row.ModelSourceMaterials.Length
                || model.MaterialCloneCount != 0) return false;
            for (int i = 0; i < materials.Length; i++) if (!materials[i].SequenceEqual(row.ModelSourceMaterials[i])) return false;
            return true;
        }
        private static int VisibleModelRenderers(Vfx120Effect effect) => effect.MeshyModel == null ? 0
            : effect.MeshyModel.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy);
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        private static IEnumerable<Vfx120TraditionalMotif> Motifs(Vfx120Effect effect)
        {
            if (effect.NativeCast != null) yield return effect.NativeCast;
            if (effect.NativeImpact != null) yield return effect.NativeImpact;
            if (effect.NativeField != null) yield return effect.NativeField;
        }
        private static void Remember(UnityEngine.Object asset, HashSet<string> paths, HashSet<string> dependencyRoots, List<Source> sources)
        {
            if (asset == null) return;
            string rootPath = AssetDatabase.GetAssetPath(asset);
            if (!dependencyRoots.Add(rootPath)) return;
            foreach (string path in AssetDatabase.GetDependencies(rootPath, true))
            {
                // Hash all dependency files through Unity's dependency hash, and directly
                // inspect serialized prefab/profile/material/mesh sources for in-memory edits.
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".prefab" && ext != ".asset" && ext != ".mat"
                    && ext != ".fbx" && ext != ".glb" && ext != ".gltf") continue;
                if (!paths.Add(path)) continue;
                var value = AssetDatabase.LoadMainAssetAtPath(path);
                var source = new Source { path = path, Asset = value, diskShaBefore = FileHash(AbsoluteAsset(path)),
                    dependencyHashBefore = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    dirtyBefore = EditorUtility.IsDirty(value) };
                string memoryBefore = SourceMemoryHash(path, value, out var memoryBeforeText);
                source.MemoryBefore = source.serializeMemoryBefore = memoryBefore;
                source.MemoryBeforeText = memoryBeforeText;
                sources.Add(source);
            }
        }
        private static string AbsoluteAsset(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        private static string SourceMemoryHash(string path, UnityEngine.Object main, out string diagnosticText)
        {
            var mainText = MemoryText(main);
            var content = new StringBuilder(MemoryHash(main, mainText));
            var diagnostic = new StringBuilder("SOURCE: " + path + "\nORDER: MAIN, THEN LOAD_ALL_ASSETS (MESH/MATERIAL ONLY)\n");
            AppendMemoryDiagnostic(diagnostic, 0, main, mainText);
            int index = 1;
            // Imported model meshes/materials are sub-assets, not the prefab root.
            // Hash each once per unique source path, without changing readability/import flags.
            // Do not sort: the previous algorithm includes the actual returned order.
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset != main && (asset is Mesh || asset is Material))
                {
                    var text = MemoryText(asset);
                    content.Append(MemoryHash(asset, text));
                    AppendMemoryDiagnostic(diagnostic, index++, asset, text);
                }
            diagnosticText = diagnostic.ToString();
            return Hash(Encoding.UTF8.GetBytes(content.ToString()));
        }
        private static string MemoryText(UnityEngine.Object asset)
        {
            if (asset == null) return "MISSING";
            string serialized = EditorJsonUtility.ToJson(asset);
            if (asset is GameObject go)
                foreach (var component in go.GetComponentsInChildren<Component>(true))
                    if (component != null) serialized += EditorJsonUtility.ToJson(component);
            return serialized;
        }
        private static string MemoryHash(UnityEngine.Object asset, string text) => asset == null ? "MISSING" : Hash(Encoding.UTF8.GetBytes(text));
        private static void AppendMemoryDiagnostic(StringBuilder text, int index, UnityEngine.Object asset, string serialized)
        {
            text.Append("\n=== ENTRY ").Append(index).Append(" | ");
            if (asset == null) text.Append("MISSING");
            else
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                text.Append(asset.GetType().FullName).Append(" | ").Append(asset.name)
                    .Append(" | guid=").Append(guid).Append(" localId=").Append(localId);
            }
            text.Append(" ===\n").Append(serialized).Append('\n');
        }
        private static string FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : "MISSING";
        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
