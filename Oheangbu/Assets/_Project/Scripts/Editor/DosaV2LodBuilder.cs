using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Builds renderer-only derivatives in a disposable V2 world instance. Never promotes a rig or saves a scene/prefab.</summary>
    public static class DosaV2LodBuilder
    {
        private const string ModelFolder = "Assets/_Project/Art/Characters/DosaV2/Models/";
        [Serializable] public sealed class RendererAudit
        {
            public string source, target, sharedParent, rootBone;
            public int vertices, triangles, mappedBones, maximumInfluences;
            public float maximumWeightSumError, maximumRestMatrixError;
            public bool skinned;
        }
        [Serializable] public sealed class LevelAudit
        {
            public int level, triangles, budget;
            public string sourcePath, sourceSha256, sourceMetaSha256;
            public List<RendererAudit> renderers = new List<RendererAudit>();
        }
        [Serializable] public sealed class Report
        {
            public string status = "WAIT", error, utc;
            public string scope = "Static renderer/skeleton binding only. Native LOD transitions, Cloth return-to-skin reset and visible geometry still require actual Play/visual measurements. This is not RIG_PASS.";
            public string cameraContract = "Explicit injected gameplay camera; only this character's owned LODGroup is forced. Camera transforms/settings and global QualitySettings are read-only. Reinject camera on gameplay instantiation.";
            public float[] thresholds = { .35f, .12f, 0f };
            public string thresholdStatus = "TEST: serialized initial transition settings; not measured distance/performance acceptance.";
            public int animatorsBefore, animatorsAfter, clothBefore, clothAfter, addedSkeletonBones;
            public List<LevelAudit> levels = new List<LevelAudit>();
        }

        public static string Bind(GameObject world, Camera gameplayCamera)
        {
            var report = new Report { utc = DateTime.UtcNow.ToString("o") };
            var created = new List<Object>(); Scene preview = default;
            try
            {
                Need(!Application.isPlaying, "Bind requires Edit mode.");
                Need(world != null && !EditorUtility.IsPersistent(world), "Provide a fresh nonpersistent validation world instance.");
                Need(world.scene.name != "C2_CodexWorld", "Pre-gate LOD binding is restricted to an isolated validation instance.");
                Need(gameplayCamera != null, "An explicit gameplay/reference camera is required.");
                Need(world.GetComponentInChildren<LODGroup>(true) == null && world.GetComponentInChildren<PlayerLodClothController>(true) == null, "Fresh world required: an existing LOD controller/group must not be duplicated.");
                var secondary = world.GetComponent<PlayerSecondaryMotionRig>();
                Need(secondary != null && secondary.IsConfigured, "Bind the world secondary rig before LOD renderers.");
                var original = world.GetComponentsInChildren<Renderer>(true);
                Need(original.Length > 0 && original.All(r => r is MeshRenderer || r is SkinnedMeshRenderer), "Only inspected mesh renderers are supported.");
                var baseByName = Unique(original, r => NormalizeName(r.name), "LOD0 renderer");
                var bonesByName = world.GetComponentsInChildren<Transform>(true)
                    .GroupBy(t => NormalizeName(t.name)).ToDictionary(g => g.Key, g => g.ToArray());
                var sharedSkinBones = new HashSet<Transform>(original.OfType<SkinnedMeshRenderer>().SelectMany(r => r.bones).Where(t => t != null));
                report.animatorsBefore = world.GetComponentsInChildren<Animator>(true).Length;
                var cloth = world.GetComponentsInChildren<Cloth>(true);
                report.clothBefore = cloth.Length;
                Need(report.animatorsBefore == 1 && cloth.Length > 0, "One existing world Animator and authored LOD0 Cloth are required.");
                var baseAudit = new LevelAudit { level = 0, budget = 75000, sourcePath = ModelFolder + "SM_DosaV2_Rigged.fbx" };
                HashSource(baseAudit);
                foreach (var renderer in original)
                {
                    Mesh mesh = MeshOf(renderer);
                    Need(mesh != null, renderer.name + ": LOD0 mesh is missing.");
                    int triangles = TriangleCount(mesh);
                    baseAudit.triangles += triangles;
                    baseAudit.renderers.Add(new RendererAudit { source = renderer.name, target = renderer.name, triangles = triangles, vertices = mesh.vertexCount, skinned = renderer is SkinnedMeshRenderer });
                }
                Need(baseAudit.triangles <= baseAudit.budget, "Actual LOD0 triangles exceed 75,000.");
                report.levels.Add(baseAudit);
                preview = EditorSceneManager.NewPreviewScene();
                var levels = new List<Renderer[]> { original };
                for (int level = 1; level <= 2; level++)
                {
                    var audit = new LevelAudit { level = level, budget = level == 1 ? 35000 : 18000, sourcePath = ModelFolder + "SM_DosaV2_LOD" + level + ".fbx" };
                    HashSource(audit);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(audit.sourcePath);
                    Need(asset != null, audit.sourcePath + " is not imported.");
                    Need(!AssetDatabase.LoadAllAssetsAtPath(audit.sourcePath).OfType<AnimationClip>().Any(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)), "LOD model unexpectedly contains authored clips.");
                    var sourceRoot = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview);
                    try
                    {
                        Need(sourceRoot.GetComponentsInChildren<Cloth>(true).Length == 0, "Only LOD0 may own native Cloth.");
                        var sourceRenderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
                        var sourceByName = Unique(sourceRenderers, r => NormalizeName(r.name), "LOD" + level + " renderer");
                        Need(new HashSet<string>(sourceByName.Keys).SetEquals(baseByName.Keys), "LOD" + level + " part names differ from the current LOD0: regenerate from the same final source.");
                        var container = new GameObject("DosaV2_LOD" + level + "_SkinnedRenderers");
                        container.transform.SetParent(world.transform, false); created.Add(container);
                        var targetRenderers = new List<Renderer>();
                        foreach (var source in sourceRenderers)
                        {
                            Renderer baseline = baseByName[NormalizeName(source.name)];
                            Mesh mesh = MeshOf(source);
                            Need(mesh != null && mesh.isReadable, source.name + ": readable actual mesh is required.");
                            Need(mesh.subMeshCount == baseline.sharedMaterials.Length, source.name + ": current material-slot count differs.");
                            Need(baseline.sharedMaterials.All(m => m != null && m.shader != null), source.name + ": current material reference is missing.");
                            var target = new GameObject("LOD" + level + "__" + source.name);
                            created.Add(target);
                            target.layer = baseline.gameObject.layer;
                            var item = new RendererAudit { source = source.name, target = target.name, vertices = mesh.vertexCount, triangles = TriangleCount(mesh) };
                            Renderer result;
                            if (source is SkinnedMeshRenderer skin)
                            {
                                Need(baseline is SkinnedMeshRenderer, source.name + ": skin/rigid role changed.");
                                Need(skin.bones.Length == mesh.bindposes.Length && skin.bones.Length > 0, source.name + ": bind-pose/bone count mismatch.");
                                var mapped = skin.bones.Select(b => Resolve(b, bonesByName)).ToArray();
                                Need(mapped.All(b => sharedSkinBones.Contains(b)), source.name + ": source references a bone outside the existing skeleton.");
                                Transform root = Resolve(skin.rootBone, bonesByName);
                                item.maximumRestMatrixError = CheckRest(skin.rootBone, root, sourceRoot.transform, world.transform);
                                for (int i = 0; i < mapped.Length; i++)
                                    item.maximumRestMatrixError = Mathf.Max(item.maximumRestMatrixError, CheckRest(skin.bones[i], mapped[i], sourceRoot.transform, world.transform));
                                target.transform.SetParent(container.transform, false);
                                SetRelativeTransform(target.transform, sourceRoot.transform.worldToLocalMatrix * source.transform.localToWorldMatrix);
                                var destination = target.AddComponent<SkinnedMeshRenderer>();
                                destination.sharedMesh = mesh; destination.bones = mapped; destination.rootBone = root;
                                destination.localBounds = skin.localBounds; destination.quality = SkinQuality.Bone4;
                                destination.updateWhenOffscreen = true; destination.forceMatrixRecalculationPerRender = true;
                                for (int i = 0; i < mesh.blendShapeCount; i++) destination.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                                ValidateWeights(mesh, mapped.Length, item);
                                item.skinned = true; item.mappedBones = mapped.Length; item.rootBone = root.name; result = destination;
                            }
                            else
                            {
                                Need(source is MeshRenderer && baseline is MeshRenderer, source.name + ": unsupported renderer role.");
                                Transform parent = Resolve(source.transform.parent, bonesByName);
                                Need(parent == baseline.transform.parent, source.name + ": rigid prop is not parented to its original shared pivot.");
                                item.maximumRestMatrixError = CheckRest(source.transform.parent, parent, sourceRoot.transform, world.transform);
                                target.transform.SetParent(parent, false);
                                target.transform.localPosition = source.transform.localPosition;
                                target.transform.localRotation = source.transform.localRotation;
                                target.transform.localScale = source.transform.localScale;
                                target.AddComponent<MeshFilter>().sharedMesh = mesh;
                                result = target.AddComponent<MeshRenderer>(); item.sharedParent = parent.name;
                            }
                            result.sharedMaterials = baseline.sharedMaterials;
                            result.shadowCastingMode = baseline.shadowCastingMode; result.receiveShadows = baseline.receiveShadows;
                            result.lightProbeUsage = baseline.lightProbeUsage; result.reflectionProbeUsage = baseline.reflectionProbeUsage;
                            result.enabled = baseline.enabled;
                            var block = new MaterialPropertyBlock(); baseline.GetPropertyBlock(block); result.SetPropertyBlock(block);
                            targetRenderers.Add(result); audit.triangles += item.triangles; audit.renderers.Add(item);
                        }
                        Need(audit.triangles <= audit.budget, "LOD" + level + " exceeds its triangle budget.");
                        report.levels.Add(audit); levels.Add(targetRenderers.ToArray());
                    }
                    finally { if (sourceRoot != null) Object.DestroyImmediate(sourceRoot); }
                }
                var group = world.AddComponent<LODGroup>(); created.Add(group);
                group.fadeMode = LODFadeMode.None; group.animateCrossFading = false;
                group.SetLODs(levels.Select((renderers, i) => new LOD(report.thresholds[i], renderers)).ToArray());
                group.RecalculateBounds();
                var controller = world.AddComponent<PlayerLodClothController>(); created.Add(controller);
                controller.Configure(group, gameplayCamera, secondary, cloth);
                report.animatorsAfter = world.GetComponentsInChildren<Animator>(true).Length;
                report.clothAfter = world.GetComponentsInChildren<Cloth>(true).Length;
                report.addedSkeletonBones = world.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => !sharedSkinBones.Contains(b)).Distinct().Count();
                Need(report.animatorsBefore == report.animatorsAfter && report.clothBefore == report.clothAfter && report.addedSkeletonBones == 0, "LOD binding introduced an Animator, Cloth or skeleton.");
                foreach (var obj in created) if (obj != null) { EditorUtility.SetDirty(obj); if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
                EditorUtility.SetDirty(world);
                report.status = "BOUND_STATIC_PENDING_PLAY";
            }
            catch (Exception exception)
            {
                report.error = exception.Message;
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
            string json = JsonUtility.ToJson(report, true);
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots/PlayerDosaV2/lod-binding.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json);
            return json;
        }

        private static Dictionary<string, T> Unique<T>(IEnumerable<T> values, Func<T, string> key, string role)
        {
            var groups = values.GroupBy(key).ToArray();
            Need(groups.All(g => g.Count() == 1), role + " contains ambiguous duplicate names.");
            return groups.ToDictionary(g => g.Key, g => g.Single());
        }
        public static string NormalizeName(string name)
        {
            if (name == null) return null;
            int split = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            return name.Substring(split + 1);
        }
        private static Transform Resolve(Transform source, Dictionary<string, Transform[]> targets)
        {
            Need(source != null, "Source bone/rootBone/rigid pivot is missing.");
            Need(targets.TryGetValue(NormalizeName(source.name), out var matches) && matches.Length == 1, source.name + ": shared skeleton name is missing or ambiguous.");
            return matches[0];
        }
        private static float CheckRest(Transform source, Transform target, Transform sourceRoot, Transform targetRoot)
        {
            float error = MatrixDifference(sourceRoot.worldToLocalMatrix * source.localToWorldMatrix, targetRoot.worldToLocalMatrix * target.localToWorldMatrix);
            Need(error <= .0002f, source.name + ": LOD rest basis differs from the current world skeleton (" + error.ToString("G9") + ").");
            return error;
        }
        public static float MatrixDifference(Matrix4x4 a, Matrix4x4 b)
        {
            float maximum = 0f;
            for (int i = 0; i < 16; i++)
            {
                float difference = Math.Abs(a[i] - b[i]);
                if (float.IsNaN(difference) || float.IsInfinity(difference)) return float.PositiveInfinity;
                maximum = Math.Max(maximum, difference);
            }
            return maximum;
        }
        private static void SetRelativeTransform(Transform target, Matrix4x4 matrix)
        {
            Need(matrix.determinant > 0f, "Renderer rest transform is singular or mirrored.");
            target.localPosition = matrix.GetColumn(3);
            target.localRotation = matrix.rotation; target.localScale = matrix.lossyScale;
            Need(MatrixDifference(matrix, Matrix4x4.TRS(target.localPosition, target.localRotation, target.localScale)) <= .0002f, "Renderer rest transform contains unsupported shear.");
        }
        private static Mesh MeshOf(Renderer renderer)
        {
            return renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }
        private static int TriangleCount(Mesh mesh)
        {
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) { Need(mesh.GetTopology(i) == MeshTopology.Triangles, mesh.name + ": non-triangle submesh."); count += checked((int)mesh.GetIndexCount(i) / 3); }
            return count;
        }
        private static void ValidateWeights(Mesh mesh, int boneCount, RendererAudit report)
        {
            using (var counts = mesh.GetBonesPerVertex())
            using (var weights = mesh.GetAllBoneWeights())
            {
                Need(counts.Length == mesh.vertexCount, mesh.name + ": missing complete per-vertex weights.");
                int cursor = 0;
                foreach (byte count in counts)
                {
                    Need(count >= 1 && count <= 4, mesh.name + ": requires 1..4 bone influences.");
                    report.maximumInfluences = Math.Max(report.maximumInfluences, count);
                    float sum = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        Need(cursor < weights.Length, mesh.name + ": truncated weight stream.");
                        var weight = weights[cursor++];
                        Need(weight.boneIndex >= 0 && weight.boneIndex < boneCount && weight.weight >= 0f && !float.IsNaN(weight.weight) && !float.IsInfinity(weight.weight), mesh.name + ": invalid skin influence.");
                        sum += weight.weight;
                    }
                    report.maximumWeightSumError = Math.Max(report.maximumWeightSumError, Math.Abs(1f - sum));
                }
                Need(cursor == weights.Length && report.maximumWeightSumError <= .0001f, mesh.name + ": extra weights or nonnormalized skin.");
            }
        }
        private static void HashSource(LevelAudit audit)
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, audit.sourcePath);
            Need(File.Exists(path) && File.Exists(path + ".meta"), audit.sourcePath + ": source file/meta is missing.");
            audit.sourceSha256 = Hash(path); audit.sourceMetaSha256 = Hash(path + ".meta");
        }
        private static string Hash(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
