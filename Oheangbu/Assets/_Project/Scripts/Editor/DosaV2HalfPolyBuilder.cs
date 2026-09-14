using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class DosaV2HalfPolyBuilder
    {
        const string Folder = "Assets/_Project/Art/Characters/DosaV2/HalfPoly";
        public const string CandidatePath = "Assets/_Project/Prefabs/Player/PF_DosaV2_HalfPolyCandidate.prefab";
        [Serializable] public sealed class Side
        {
            public string name;
            public int beforeTriangles, afterTriangles, beforeRenderers, afterRenderers, retainedCloth, handBlendShapes;
            public float maximumBoneRestDifference, maximumWeightSumError;
            public int invalidWeights;
            public DosaV2RenderBatchBuilder.Report batching = new DosaV2RenderBatchBuilder.Report();
        }
        [Serializable] public sealed class Report
        {
            public string status, error, backup;
            public int animatorsBefore, animatorsAfter, transformsBefore, transformsAfter;
            public List<Side> sides = new List<Side>();
            public string scope = "Reduced render derivatives on the existing skeleton. Original world Cloth and source-index physics/attachment meshes remain intact. Near render-only sleeves and both hand render meshes are reduced; grip corrective shape names retained. No new motion/Avatar or paid generation.";
        }
        static string Name(string value) => DosaV2LodBuilder.NormalizeName(value);
        static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        static int Triangles(Renderer r) => MeshOf(r) == null ? 0 : Enumerable.Range(0, MeshOf(r).subMeshCount).Sum(i => (int)MeshOf(r).GetIndexCount(i) / 3);

        public static string BuildCandidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var report = new Report { status = "BUILDING" };
            DevSceneKit.EnsureFolder(Folder);
            foreach (string side in new[] { "World", "Arms" }) Import(side);
            var root = PrefabUtility.LoadPrefabContents(DosaV2PlayableDraftBuilder.VisualPrefabPath);
            try
            {
                if (root.GetComponentsInChildren<Renderer>(true).Any(r => r.name.StartsWith("HalfDisplay_"))) throw new InvalidOperationException("Half-poly derivative is already installed.");
                report.animatorsBefore = root.GetComponentsInChildren<Animator>(true).Length;
                report.transformsBefore = root.GetComponentsInChildren<Transform>(true).Length;
                report.sides.Add(BuildSide(root.transform.Find("WorldBody"), "World"));
                report.sides.Add(BuildSide(root.transform.Find("NearArms"), "Arms"));
                report.animatorsAfter = root.GetComponentsInChildren<Animator>(true).Length;
                report.transformsAfter = root.GetComponentsInChildren<Transform>(true).Length;
                if (report.animatorsBefore != report.animatorsAfter) throw new InvalidOperationException("Extra Animator added.");
                if (report.sides[0].afterTriangles > 37800 || report.sides[1].afterTriangles > 13500) throw new InvalidOperationException("Half-poly budget exceeded.");
                if (PrefabUtility.SaveAsPrefabAsset(root, CandidatePath) == null) throw new IOException("Candidate save failed.");
                AssetDatabase.SaveAssets(); report.status = "CANDIDATE_BINDING_PASS_PENDING_PLAY";
            }
            catch (Exception e) { report.status = "FAILED"; report.error = e.ToString(); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            string json = JsonUtility.ToJson(report, true); File.WriteAllText("Screenshots/PlayerDosaV2/half-poly-build.json", json); return json;
        }
        static void Import(string side)
        {
            string name = "SM_DosaV2_Half_" + side + ".fbx", destination = Folder + "/" + name;
            File.Copy(Path.GetFullPath("../Art/PlayerV2/HalfPoly/" + name), destination, true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(destination);
            importer.globalScale = 1; importer.useFileScale = true; importer.importCameras = false; importer.importLights = false;
            importer.importVisibility = false; importer.importAnimation = false; importer.optimizeGameObjects = false; importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off; importer.importBlendShapes = true;
            importer.importNormals = ModelImporterNormals.Import; importer.importBlendShapeNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.SaveAndReimport();
        }
        static Side BuildSide(Transform world, string label)
        {
            if (world == null) throw new InvalidOperationException("Missing representation " + label);
            var report = new Side { name = label };
            var existingBatch = world.GetComponent<PlayerModelRenderBatch>();
            var previous = existingBatch != null ? existingBatch.ActiveRenderers.ToArray() : world.GetComponentsInChildren<Renderer>(true);
            report.beforeRenderers = previous.Length; report.beforeTriangles = previous.Sum(Triangles);
            var cloth = previous.Where(r => r.GetComponent<Cloth>() != null).ToArray(); report.retainedCloth = cloth.Length;
            var authored = world.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("DosaV2_") || r.name.StartsWith("DosaPackV2_")).ToDictionary(r => Name(r.name));
            var boneMap = world.GetComponentsInChildren<Transform>(true).GroupBy(t => Name(t.name)).ToDictionary(g => g.Key, g => g.ToArray());
            var preview = EditorSceneManager.NewPreviewScene();
            var sourceRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/SM_DosaV2_Half_" + label + ".fbx"), preview);
            var templates = new List<Renderer>();
            try
            {
                Transform Resolve(Transform source)
                {
                    if (source == null || !boneMap.TryGetValue(Name(source.name), out var matches) || matches.Length != 1) throw new InvalidOperationException("Missing/ambiguous bone " + source?.name);
                    float difference = DosaV2LodBuilder.MatrixDifference(sourceRoot.transform.worldToLocalMatrix * source.localToWorldMatrix, world.worldToLocalMatrix * matches[0].localToWorldMatrix);
                    report.maximumBoneRestDifference = Mathf.Max(report.maximumBoneRestDifference, difference);
                    if (difference > .0002f) throw new InvalidOperationException(source.name + ": rest basis changed " + difference);
                    return matches[0];
                }
                foreach (var source in sourceRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (!authored.TryGetValue(Name(source.name), out var baseline)) throw new InvalidOperationException("Unknown reduced part " + source.name);
                    if (baseline.GetComponent<Cloth>() != null) continue;
                    Mesh mesh = MeshOf(source);
                    if (mesh == null || !mesh.isReadable || mesh.subMeshCount != baseline.sharedMaterials.Length) throw new InvalidOperationException(source.name + ": mesh/material mismatch.");
                    var go = new GameObject("HalfDisplay_" + label + "_" + source.name); go.layer = world.gameObject.layer; go.transform.SetParent(world, false);
                    Matrix4x4 local = sourceRoot.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
                    go.transform.localPosition = local.GetColumn(3); go.transform.localRotation = local.rotation; go.transform.localScale = local.lossyScale;
                    var destination = go.AddComponent<SkinnedMeshRenderer>(); destination.sharedMaterials = baseline.sharedMaterials;
                    if (source is SkinnedMeshRenderer skin)
                    {
                        destination.bones = skin.bones.Select(Resolve).ToArray(); destination.rootBone = Resolve(skin.rootBone); destination.sharedMesh = mesh;
                    }
                    else
                    {
                        mesh = Object.Instantiate(mesh); mesh.name = "SM_HalfRigid_" + label + "_" + source.name;
                        mesh.bindposes = new[] { baseline.transform.worldToLocalMatrix * go.transform.localToWorldMatrix };
                        mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, mesh.vertexCount).ToArray();
                        string path = Folder + "/" + mesh.name + ".asset"; var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (old != null) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); mesh = old; } else AssetDatabase.CreateAsset(mesh, path);
                        destination.bones = new[] { baseline.transform }; destination.rootBone = world; destination.sharedMesh = mesh;
                    }
                    destination.quality = SkinQuality.Bone4; destination.updateWhenOffscreen = true; destination.forceMatrixRecalculationPerRender = true;
                    var bounds = mesh.bounds; bounds.Expand(1f); destination.localBounds = bounds;
                    destination.shadowCastingMode = baseline.shadowCastingMode; destination.receiveShadows = baseline.receiveShadows;
                    destination.renderingLayerMask = baseline.renderingLayerMask; destination.enabled = false;
                    foreach (var w in mesh.boneWeights)
                    {
                        float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                        report.maximumWeightSumError = Mathf.Max(report.maximumWeightSumError, Mathf.Abs(sum - 1f));
                        if (!float.IsFinite(sum) || Mathf.Abs(sum - 1f) > .0001f || w.weight0 < 0 || w.weight1 < 0 || w.weight2 < 0 || w.weight3 < 0) report.invalidWeights++;
                    }
                    if (source.name.EndsWith("DosaV2_Hands"))
                    {
                        report.handBlendShapes = mesh.blendShapeCount;
                        if (report.handBlendShapes < 2) throw new InvalidOperationException("Grip corrective shapes missing.");
                    }
                    templates.Add(destination);
                }
                if (report.invalidWeights > 0) throw new InvalidOperationException("Invalid reduced skin weights.");
                var display = DosaV2RenderBatchBuilder.CombineReducedSources(world, templates.ToArray(), Folder + "/RenderMeshes", "HalfDisplay_" + label + "_", report.batching);
                var batch = existingBatch != null ? existingBatch : world.gameObject.AddComponent<PlayerModelRenderBatch>();
                batch.Configure(previous, previous.Except(cloth).ToArray(), display);
                report.afterRenderers = batch.ActiveRenderers.Length; report.afterTriangles = batch.ActiveRenderers.Sum(Triangles);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            return report;
        }

        public static string ApplyCandidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var report = JsonUtility.FromJson<Report>(File.ReadAllText("Screenshots/PlayerDosaV2/half-poly-build.json"));
            if (report.status != "CANDIDATE_BINDING_PASS_PENDING_PLAY") throw new InvalidOperationException("Half-poly candidate binding not passed.");
            string backup = Path.GetFullPath("../Art/PlayerV2/Backups/HalfPoly_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(backup);
            File.Copy(DosaV2PlayableDraftBuilder.VisualPrefabPath, Path.Combine(backup, "PF_DosaV2_PlayableDraft.prefab"));
            var root = PrefabUtility.LoadPrefabContents(CandidatePath);
            try { if (PrefabUtility.SaveAsPrefabAsset(root, DosaV2PlayableDraftBuilder.VisualPrefabPath) == null) throw new IOException("Half-poly apply failed."); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets(); report.backup = backup; report.status = "APPLIED_PENDING_PLAY";
            string json = JsonUtility.ToJson(report, true); File.WriteAllText("Screenshots/PlayerDosaV2/half-poly-apply.json", json); return json;
        }
    }
}
