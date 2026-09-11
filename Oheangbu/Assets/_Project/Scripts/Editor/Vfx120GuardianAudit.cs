using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.SpellVFX120;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Read actual instantiated transforms/meshes. This is not an art, animation or
    // gameplay PASS, and its own fixture never sends hit/damage/AI events.
    public static class Vfx120GuardianAudit
    {
        const string AssetRoot = "Assets/_Project/Art/SpellVFX120/";
        [Serializable] sealed class Report
        {
            public string status = "DIAGNOSTIC_ONLY", capturedUtc, unityVersion, appMvid, output, frameProbeOutput;
            public string overlapMethod = "UNVERIFIED: triangle intersection intentionally deferred. This first audit only identifies detached parts, actual joint pivots and fist contact error.";
            public string targetMethod = "TargetPoint equals ReceivedTarget.position+(0,1.1,0). Contact is transformed by actual Body_6, not recomputed through motion solver.";
            public string limitations = "No render, gameplay, CPU budget or production animation PASS. noDemo intentionally has no scheduled/actual hit cue. Ground sampling remains the existing effect's Begin implementation.";
            public bool temporarySceneClosed, loadedSceneDirtyFlagsUnchanged;
            public List<Sample> samples = new List<Sample>();
            public List<string> errors = new List<string>();
        }
        [Serializable] sealed class Sample
        {
            public string scenario, phase, targetName, targetGeometryStatus;
            public bool demo, finite, contactExpected;
            public float age, life, hitAt, targetPointErrorMeters, rightArmAboveHeadMeters, rightFistAboveHeadMeters;
            public int signalCount;
            public Vector3 targetPoint, actualRightFistContact, actualRightShoulder, actualRightElbow;
            public List<PartData> parts = new List<PartData>();
            public List<int> bodyIndicesAboveHead = new List<int>();
        }
        [Serializable] sealed class PartData
        {
            public int index, vertices, triangles, nativeBuffers;
            public string anatomicalPart, objectName, meshPath;
            public bool rendererEnabled;
            public Vector3 worldPosition, lossyScale, rendererBoundsCenter, rendererBoundsSize;
            public Quaternion worldRotation;
            public Vector3 actualVertexBoundsCenter, actualVertexBoundsSize;
            public float parentPivotDistance, authoredParentPivotDistance, minimumVertexY;
        }
        [Serializable] sealed class ManifestPart { public int index; public string name; public float[] pivot_unity; }
        [Serializable] sealed class Manifest { public ManifestPart[] parts; }
        [Serializable] sealed class FrameReport
        {
            public string status = "READ_ONLY_FRAME_DIAGNOSTIC", capturedUtc, fbxPath, manifestPath;
            public string matrixOrder = "Row-major localToWorldMatrix, 16 floats";
            public List<FrameNode> nodes = new List<FrameNode>();
        }
        [Serializable] sealed class FrameNode
        {
            public string name, meshName;
            public int partIndex, vertices, triangles;
            public Vector3 manifestPivot, actualNodeOrigin, localPosition, localScale, worldScale;
            public Quaternion localRotation, worldRotation;
            public float determinant, originToManifestError, originToXMirroredManifestError;
            public float[] localToWorld;
            public Vector3 meshLocalBoundsCenter, meshLocalBoundsSize, transformedVertexBoundsCenter, transformedVertexBoundsSize;
        }

        public static string FrameProbe()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Edit mode only.");
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            string output = Path.Combine(root, "Art/SpellVFX120/guardian_frame_probe.json");
            var report = new FrameReport { capturedUtc = DateTime.UtcNow.ToString("o"),
                fbxPath = AssetRoot + "Blender/StoneGuardian_Jangseung_Parts_B2.fbx",
                manifestPath = Path.Combine(root, "Art/SpellVFX120/Blender/GuardianParts/guardian_parts_manifest.json") };
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(report.manifestPath));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(report.fbxPath);
            if (model == null) throw new InvalidOperationException("Source FBX unavailable for frame probe.");
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                ManifestPart part = null;
                foreach (var candidate in manifest.parts)
                    if (filter.name == "GuardianPart_" + candidate.name) { part = candidate; break; }
                if (part == null) throw new InvalidOperationException("Unmapped source FBX node: " + filter.name);
                var transform = filter.transform; var matrix = transform.localToWorldMatrix; var mesh = filter.sharedMesh;
                var pivot = new Vector3(part.pivot_unity[0], part.pivot_unity[1], part.pivot_unity[2]);
                var nodeOrigin = matrix.MultiplyPoint3x4(Vector3.zero);
                var vertices = mesh.vertices;
                var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
                foreach (var vertex in vertices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertex));
                var node = new FrameNode { name = filter.name, partIndex = part.index, meshName = mesh.name,
                    vertices = mesh.vertexCount, triangles = mesh.triangles.Length / 3,
                    manifestPivot = pivot, actualNodeOrigin = nodeOrigin, localPosition = transform.localPosition,
                    localScale = transform.localScale, worldScale = transform.lossyScale,
                    localRotation = transform.localRotation, worldRotation = transform.rotation,
                    determinant = matrix.determinant, localToWorld = new float[16],
                    originToManifestError = Vector3.Distance(nodeOrigin, pivot),
                    originToXMirroredManifestError = Vector3.Distance(nodeOrigin, new Vector3(-pivot.x, pivot.y, pivot.z)),
                    meshLocalBoundsCenter = mesh.bounds.center, meshLocalBoundsSize = mesh.bounds.size,
                    transformedVertexBoundsCenter = bounds.center, transformedVertexBoundsSize = bounds.size };
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) node.localToWorld[r * 4 + c] = matrix[r, c];
                report.nodes.Add(node);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            return output;
        }
        public static string Run()
        {
            var report = new Report { capturedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                appMvid = typeof(Vfx120Effect).Assembly.ManifestModule.ModuleVersionId.ToString() };
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            report.output = Path.Combine(root, "Art/SpellVFX120/guardian_anatomy_audit.json");
            var scenes = new List<Scene>(); var dirty = new List<bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); scenes.Add(s); dirty.Add(s.isDirty); }
            Scene preview = default;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Edit mode only.");
                report.frameProbeOutput = FrameProbe();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "Prefabs/064_BAB8.prefab");
                if (prefab == null) throw new InvalidOperationException("Guardian prefab missing.");
                Transform primary = GameObject.Find("VFX Target")?.transform;
                Transform secondary = GameObject.Find("VFX Secondary Target")?.transform;
                preview = EditorSceneManager.NewPreviewScene();
                RunScenario(prefab, preview, primary, new Vector3(0, 0, 4), true, "primary_demo", report);
                RunScenario(prefab, preview, secondary, new Vector3(2.3f, 0, 5.1f), true, "secondary_p2_demo", report);
                RunScenario(prefab, preview, primary, new Vector3(0, 0, 4), false, "primary_noDemo", report);
            }
            catch (Exception error) { report.status = "INCOMPLETE"; report.errors.Add(error.ToString()); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                report.temporarySceneClosed = !preview.IsValid() || !preview.isLoaded;
                report.loadedSceneDirtyFlagsUnchanged = true;
                for (int i = 0; i < scenes.Count; i++)
                    if (!scenes[i].IsValid() || scenes[i].isDirty != dirty[i]) report.loadedSceneDirtyFlagsUnchanged = false;
                if (!report.loadedSceneDirtyFlagsUnchanged) { report.status = "INCOMPLETE"; report.errors.Add("Loaded scene dirty flags changed; the audit did not clear or save them."); }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(report.output));
            File.WriteAllText(report.output, JsonUtility.ToJson(report, true));
            return report.output;
        }

        static void RunScenario(GameObject prefab, Scene scene, Transform sourceTarget,
            Vector3 fallback, bool demo, string label, Report report)
        {
            GameObject instance = null, target = null;
            try
            {
                target = new GameObject("GuardianAudit_Target") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(target, scene);
                target.transform.position = sourceTarget != null ? sourceTarget.position : fallback;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.hideFlags = HideFlags.HideAndDontSave;
                var fx = instance.GetComponent<Vfx120Effect>();
                if (fx == null) throw new InvalidOperationException("No actual Vfx120Effect on prefab.");
                fx.PreviewControlled = true; fx.DemonstrationCues = demo;
                fx.Begin(new Vector3(0, 1, 0), target.transform, new Vector3(0, 1, 4), Color.white);
                if (fx.Profile.GuardianMeshes == null || fx.Profile.GuardianMeshes.Length != 11)
                    throw new InvalidOperationException("Guardian rig not assigned.");
                float peak = .55f * fx.Life;
                fx.Sample(peak);
                report.samples.Add(Inspect(fx, sourceTarget, label, "exact_055_life", demo));
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (target != null) Object.DestroyImmediate(target);
            }
        }

        static Sample Inspect(Vfx120Effect fx, Transform sourceTarget, string scenario, string phase, bool demo)
        {
            var row = new Sample { scenario = scenario, phase = phase, demo = demo, age = fx.Age, life = fx.Life,
                targetName = sourceTarget != null ? sourceTarget.name : "Fallback transform only", targetGeometryStatus = "SURFACE_TEST_DEFERRED",
                contactExpected = demo, signalCount = fx.SignalCount, hitAt = fx.HitAt, finite = true };
            row.targetPoint = fx.ReceivedTarget.position + Vector3.up * 1.1f;
            var transforms = new Transform[11]; var bounds = new Bounds[11];
            for (int i = 0; i < 11; i++)
            {
                Transform part = fx.transform.Find("Body_" + i);
                if (part == null) throw new InvalidOperationException("Missing actual Body_" + i);
                transforms[i] = part;
                var filter = part.GetComponent<MeshFilter>(); var renderer = part.GetComponent<Renderer>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    throw new InvalidOperationException("Unreadable Body_" + i);
                Vector3[] vertices = filter.sharedMesh.vertices;
                bounds[i] = new Bounds(part.TransformPoint(vertices[0]), Vector3.zero);
                foreach (var vertex in vertices) bounds[i].Encapsulate(part.TransformPoint(vertex));
                int parent = Vfx120GuardianMotion.Parent(i);
                var item = new PartData { index = i, anatomicalPart = ((Vfx120GuardianMotion.Part)i).ToString(), objectName = part.name,
                    meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh), vertices = filter.sharedMesh.vertexCount,
                    triangles = filter.sharedMesh.triangles.Length / 3, nativeBuffers = filter.sharedMesh.vertexBufferCount,
                    rendererEnabled = renderer.enabled, worldPosition = part.position, worldRotation = part.rotation, lossyScale = part.lossyScale,
                    rendererBoundsCenter = renderer.bounds.center, rendererBoundsSize = renderer.bounds.size,
                    actualVertexBoundsCenter = bounds[i].center, actualVertexBoundsSize = bounds[i].size,
                    minimumVertexY = bounds[i].min.y,
                    parentPivotDistance = parent < 0 ? 0 : Vector3.Distance(part.position, transforms[parent].position),
                    authoredParentPivotDistance = parent < 0 ? 0 : Vector3.Distance(fx.Profile.GuardianPivots[i], fx.Profile.GuardianPivots[parent]) * fx.Profile.PartScale.y };
                row.parts.Add(item);
                row.finite &= Finite(part.position) && Finite(part.lossyScale);
            }
            int fistIndex = (int)Vfx120GuardianMotion.Part.RightFist;
            row.actualRightFistContact = transforms[fistIndex].TransformPoint(fx.Profile.GuardianFistContact - fx.Profile.GuardianPivots[fistIndex]);
            row.targetPointErrorMeters = Vector3.Distance(row.actualRightFistContact, row.targetPoint);
            row.actualRightShoulder = transforms[5].position;
            row.actualRightElbow = transforms[6].position;
            row.rightArmAboveHeadMeters = Mathf.Max(0, bounds[5].max.y - bounds[2].max.y);
            row.rightFistAboveHeadMeters = Mathf.Max(0, bounds[6].max.y - bounds[2].max.y);
            for (int i = 0; i < 11; i++) if (i != 2 && bounds[i].max.y > bounds[2].max.y + .001f) row.bodyIndicesAboveHead.Add(i);
            return row;
        }

        static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
            || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));
    }
}
