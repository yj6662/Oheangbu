using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        [Serializable] private sealed class GaitSample
        {
            public float clipTime, meshMinimumY, meshMaximumY, leftSoleMinimumY, rightSoleMinimumY;
            public Vector3 leftSoleCentroid, rightSoleCentroid, modelRoot;
            public bool allVerticesFinite, allHandTransformsFinite;
            public float correctedLeftSoleMinimumY, correctedRightSoleMinimumY, correctedWholeMeshMinimumY;
            public float leftAppliedLift, rightAppliedLift, correctionLocalBoneLengthError;
        }
        [Serializable] private sealed class GaitClipReport
        {
            public string clip, path, supportSpeedStatus;
            public float clipLength, configuredSpeedMps, configuredTimeScale;
            public float estimatedNativeSupportSpeedMps, estimatedSupportSpeedAtConfiguredTimeScaleMps, suggestedTimeScaleForConfiguredSpeed;
            public float minimumMeshY, maximumMeshY;
            public float minimumLeftSoleY, minimumRightSoleY, correctedMinimumLeftSoleY, correctedMinimumRightSoleY;
            public bool correctedSolesWithinTwoCentimeters;
            public int leftSupportSamples, rightSupportSamples;
            public GaitSample[] samples;
        }
        [Serializable] private sealed class GaitReport
        {
            public string status, utc, sourceFbxSha256, error, skinWeightsBefore, skinWeightsAfter;
            public string scope = "Isolated preview scene, existing imported Idle/Walk/Run clips sampled directly with a manual AnimationClipPlayable. 120 poses per clip, no live-player movement, gameplay input, capture, clip editing or build. No gesture override in this gait-only probe.";
            public string speedProvenance = "Original Walk1.553752797 and Run5.362285040 were measured in native Blender world metres in Motion/baseline_motion_diagnostic.json; old .7040447 Unity display scaling was applied later. This command measures the current imported Humanoid at the corrected display scale.";
            public string soleMethod = "Rest lowest15% of vertices with >=.7 matching Foot weight. Support candidate: sole minimumY within.025m of that foot's sampled cycle minimum and centroid vertical velocity below.55m/s. Central-difference signed forward (-localZ) velocity; 120 samples per clip. This is an inferred support window, not actual ground-force contact.";
            public string[] limitations = { "Foot heights and support-speed estimates are diagnostic data, not an artistic locomotion or gameplay-foot-slip pass.",
                "Directional walking, dodging, slopes, stairs, whole-clothing contact and live movement remain unverified by this command." };
            public float displayScale, neutralHeightMeters;
            public int leftSoleVertices, rightSoleVertices;
            public bool originalLivePlayerPreserved, resourcesReleased, qualityRestored;
            public bool flatPlaneFootClearanceTest;
            public GaitClipReport[] clips;
        }
        private sealed class GaitMesh
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Baked;
            public int[] Left, Right;
        }

        private static string ValidateIsolatedGait(bool validateFootPlacement = false)
        {
            Need(!EditorApplication.isCompiling, "Wait for editor compilation before the isolated gait probe.");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerAppearanceProfile>(ProfilePath);
            Need(model != null && profile != null && profile.Idle != null && profile.Walk != null && profile.Run != null,
                "Prepared derivative model/profile with existing gait clips required.");
            var live = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Transform[] liveTransforms = live != null ? live.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            Vector3[] livePositions = liveTransforms.Select(t => t.localPosition).ToArray(); Quaternion[] liveRotations = liveTransforms.Select(t => t.localRotation).ToArray();
            SkinWeights oldQuality = QualitySettings.skinWeights;
            var report = new GaitReport { utc = DateTime.UtcNow.ToString("o"), sourceFbxSha256 = Sha(ModelPath), displayScale = profile.UniformScale,
                skinWeightsBefore = oldQuality.ToString(), flatPlaneFootClearanceTest = validateFootPlacement };
            var preview = EditorSceneManager.NewPreviewScene(); GameObject instance = null; PlayableGraph graph = default;
            var meshes = new List<GaitMesh>(); var results = new List<GaitClipReport>();
            try
            {
                QualitySettings.skinWeights = SkinWeights.Unlimited;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, preview);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.SetPositionAndRotation(profile.LocalOffset, Quaternion.identity); instance.transform.localScale = Vector3.one * profile.UniformScale;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var animator = instance.GetComponent<Animator>();
                Need(animator != null && animator.avatar != null && animator.isHuman, "The isolated model needs a valid Humanoid Animator.");
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Bounds neutral = default; bool initialized = false;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.quality = SkinQuality.Auto;
                    Vector3[] rest = skin.sharedMesh.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                    foreach (var point in rest) { if (!initialized) { neutral = new Bounds(point, Vector3.zero); initialized = true; } else neutral.Encapsulate(point); }
                    var record = new GaitMesh { Renderer = skin, Baked = new Mesh { name = "Temporary_Gait_" + skin.name, hideFlags = HideFlags.HideAndDontSave },
                        Left = GaitSoleIndices(skin, rest, "LeftFoot"), Right = GaitSoleIndices(skin, rest, "RightFoot") };
                    report.leftSoleVertices += record.Left.Length; report.rightSoleVertices += record.Right.Length; meshes.Add(record);
                }
                Need(report.leftSoleVertices > 0 && report.rightSoleVertices > 0, "Could not identify both actual foot-skin probe sets.");
                report.neutralHeightMeters = neutral.size.y;
                WorldMacroPlayerFootPlacement footPlacement = null;
                if (validateFootPlacement)
                {
                    footPlacement = instance.AddComponent<WorldMacroPlayerFootPlacement>();
                    footPlacement.Configure(animator, null, null, meshes.Select(m => m.Renderer).ToArray());
                    footPlacement.enabled = false;
                    Need(footPlacement.IsBound, "Isolated foot clearance binding failed: " + footPlacement.BindingError);
                    report.scope += " Optional foot clearance is sampled against an explicit flat y=0 diagnostic plane, without gameplay Physics queries. Whole-mesh minima may include the robe; sole minima are reported separately.";
                }
                var clips = new[] { profile.Idle, profile.Walk, profile.Run };
                for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                {
                    AnimationClip clip = clips[clipIndex]; footPlacement?.RestoreAnimatedPose(); animator.Rebind();
                    graph = PlayableGraph.Create("C02 isolated gait numeric probe"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false); playable.SetSpeed(0);
                    var output = AnimationPlayableOutput.Create(graph, "Existing gait clip", animator); output.SetSourcePlayable(playable); graph.Play();
                    var samples = new List<GaitSample>(120);
                    for (int sampleIndex = 0; sampleIndex < 120; sampleIndex++)
                    {
                        float time = clip.length * sampleIndex / 120f; footPlacement?.RestoreAnimatedPose(); playable.SetTime(time); graph.Evaluate(0f);
                        var sample = new GaitSample { clipTime = time, meshMinimumY = float.PositiveInfinity, meshMaximumY = float.NegativeInfinity,
                            leftSoleMinimumY = float.PositiveInfinity, rightSoleMinimumY = float.PositiveInfinity, allVerticesFinite = true,
                            allHandTransformsFinite = instance.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("LeftHand", StringComparison.Ordinal) || t.name.StartsWith("RightHand", StringComparison.Ordinal))
                                .All(t => Finite(t.position) && WorldMacroPlayerGestureProfile.IsUsableRotation(t.rotation)), modelRoot = instance.transform.position };
                        foreach (var mesh in meshes)
                        {
                            // Compensate scale here because TransformPoint below applies the complete renderer transform.
                            mesh.Renderer.BakeMesh(mesh.Baked, true); Vector3[] vertices = mesh.Baked.vertices;
                            for (int i = 0; i < vertices.Length; i++)
                            {
                                Vector3 point = mesh.Renderer.transform.TransformPoint(vertices[i]); sample.allVerticesFinite &= Finite(point);
                                sample.meshMinimumY = Mathf.Min(sample.meshMinimumY, point.y); sample.meshMaximumY = Mathf.Max(sample.meshMaximumY, point.y);
                            }
                            foreach (int i in mesh.Left) { Vector3 p = mesh.Renderer.transform.TransformPoint(vertices[i]); sample.leftSoleCentroid += p; sample.leftSoleMinimumY = Mathf.Min(sample.leftSoleMinimumY, p.y); }
                            foreach (int i in mesh.Right) { Vector3 p = mesh.Renderer.transform.TransformPoint(vertices[i]); sample.rightSoleCentroid += p; sample.rightSoleMinimumY = Mathf.Min(sample.rightSoleMinimumY, p.y); }
                        }
                        sample.leftSoleCentroid /= report.leftSoleVertices; sample.rightSoleCentroid /= report.rightSoleVertices;
                        sample.correctedLeftSoleMinimumY = sample.leftSoleMinimumY; sample.correctedRightSoleMinimumY = sample.rightSoleMinimumY;
                        sample.correctedWholeMeshMinimumY = sample.meshMinimumY;
                        if (footPlacement != null)
                        {
                            footPlacement.EvaluatePoseAgainstPlane(Vector3.zero, Vector3.up, true, false, false);
                            sample.leftAppliedLift = footPlacement.Diagnostics.LeftAppliedLift; sample.rightAppliedLift = footPlacement.Diagnostics.RightAppliedLift;
                            sample.correctionLocalBoneLengthError = footPlacement.Diagnostics.MaximumLocalLengthError;
                            sample.correctedLeftSoleMinimumY = sample.correctedRightSoleMinimumY = sample.correctedWholeMeshMinimumY = float.PositiveInfinity;
                            foreach (var mesh in meshes)
                            {
                                mesh.Renderer.BakeMesh(mesh.Baked, true); Vector3[] corrected = mesh.Baked.vertices;
                                foreach (Vector3 vertex in corrected)
                                {
                                    Vector3 point = mesh.Renderer.transform.TransformPoint(vertex); sample.allVerticesFinite &= Finite(point);
                                    sample.correctedWholeMeshMinimumY = Mathf.Min(sample.correctedWholeMeshMinimumY, point.y);
                                }
                                foreach (int i in mesh.Left) sample.correctedLeftSoleMinimumY = Mathf.Min(sample.correctedLeftSoleMinimumY, mesh.Renderer.transform.TransformPoint(corrected[i]).y);
                                foreach (int i in mesh.Right) sample.correctedRightSoleMinimumY = Mathf.Min(sample.correctedRightSoleMinimumY, mesh.Renderer.transform.TransformPoint(corrected[i]).y);
                            }
                        }
                        samples.Add(sample);
                    }
                    var row = new GaitClipReport { clip = clip.name, path = AssetDatabase.GetAssetPath(clip), clipLength = clip.length, samples = samples.ToArray(),
                        configuredSpeedMps = clipIndex == 1 ? profile.WalkSpeed : clipIndex == 2 ? profile.RunSpeed : 0f,
                        configuredTimeScale = clipIndex == 2 ? profile.RunSpeed / Mathf.Max(.001f, profile.NativeRunSpeed) : 1f,
                        minimumMeshY = samples.Min(s => s.meshMinimumY), maximumMeshY = samples.Max(s => s.meshMaximumY),
                        minimumLeftSoleY = samples.Min(s => s.leftSoleMinimumY), minimumRightSoleY = samples.Min(s => s.rightSoleMinimumY),
                        correctedMinimumLeftSoleY = samples.Min(s => s.correctedLeftSoleMinimumY), correctedMinimumRightSoleY = samples.Min(s => s.correctedRightSoleMinimumY) };
                    row.correctedSolesWithinTwoCentimeters = validateFootPlacement && row.correctedMinimumLeftSoleY >= -.02f && row.correctedMinimumRightSoleY >= -.02f
                        && samples.All(s => s.correctionLocalBoneLengthError <= .0002f && s.allVerticesFinite);
                    float left = GaitSupportSpeed(row.samples, true, out int leftSamples), right = GaitSupportSpeed(row.samples, false, out int rightSamples);
                    row.leftSupportSamples = leftSamples; row.rightSupportSamples = rightSamples;
                    row.supportSpeedStatus = leftSamples >= 8 && rightSamples >= 8 ? "BOTH_FEET_SUPPORT_ESTIMATE" : "UNVERIFIED_TOO_FEW_SUPPORT_SAMPLES";
                    row.estimatedNativeSupportSpeedMps = leftSamples > 0 && rightSamples > 0 ? (left + right) * .5f : leftSamples > 0 ? left : right;
                    row.estimatedSupportSpeedAtConfiguredTimeScaleMps = row.estimatedNativeSupportSpeedMps * row.configuredTimeScale;
                    row.suggestedTimeScaleForConfiguredSpeed = clipIndex > 0 && row.estimatedNativeSupportSpeedMps > .01f ? row.configuredSpeedMps / row.estimatedNativeSupportSpeedMps : 0f;
                    results.Add(row); graph.Destroy(); graph = default;
                }
                report.clips = results.ToArray();
                report.status = results.SelectMany(r => r.samples).All(s => s.allVerticesFinite && s.allHandTransformsFinite)
                    ? "MEASURED_ISOLATED_GAIT_NOT_ART_APPROVAL" : "FAIL_NONFINITE_POSE";
            }
            catch (Exception exception) { report.status = "UNVERIFIED_GAIT_ERROR"; report.error = exception.ToString(); report.clips = results.ToArray(); }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                foreach (var mesh in meshes) if (mesh.Baked != null) Object.DestroyImmediate(mesh.Baked);
                if (instance != null) Object.DestroyImmediate(instance); EditorSceneManager.ClosePreviewScene(preview);
                QualitySettings.skinWeights = oldQuality; report.skinWeightsAfter = QualitySettings.skinWeights.ToString(); report.qualityRestored = QualitySettings.skinWeights == oldQuality;
                report.resourcesReleased = instance == null && !graph.IsValid() && meshes.All(m => m.Baked == null);
                report.originalLivePlayerPreserved = liveTransforms.Select((t, i) => t != null && t.localPosition == livePositions[i] && t.localRotation.Equals(liveRotations[i])).All(v => v);
            }
            Directory.CreateDirectory(Output); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(Output, validateFootPlacement ? "isolated_gait_foot_clearance.json" : "isolated_gait.json"), json); return json;
        }

        private static int[] GaitSoleIndices(SkinnedMeshRenderer skin, Vector3[] rest, string boneName)
        {
            var counts = skin.sharedMesh.GetBonesPerVertex(); var weights = skin.sharedMesh.GetAllBoneWeights();
            try
            {
                var ids = new List<int>(); int offset = 0; var bones = skin.bones;
                for (int i = 0; i < counts.Length; i++)
                {
                    float influence = 0f;
                    for (int j = 0; j < counts[i]; j++) { var w = weights[offset++]; if (w.boneIndex < bones.Length && bones[w.boneIndex] != null && bones[w.boneIndex].name == boneName) influence += w.weight; }
                    if (influence >= .7f) ids.Add(i);
                }
                if (ids.Count == 0) return Array.Empty<int>();
                ids.Sort((a, b) => rest[a].y.CompareTo(rest[b].y));
                float cutoff = rest[ids[Mathf.Min(ids.Count - 1, Mathf.CeilToInt(ids.Count * .15f))]].y;
                return ids.Where(i => rest[i].y <= cutoff).ToArray();
            }
            finally { counts.Dispose(); weights.Dispose(); }
        }

        private static float GaitSupportSpeed(GaitSample[] samples, bool left, out int count)
        {
            float low = samples.Min(s => left ? s.leftSoleMinimumY : s.rightSoleMinimumY); var speeds = new List<float>();
            for (int i = 0; i < samples.Length; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(samples.Length - 1, i + 1);
                float dt = samples[b].clipTime - samples[a].clipTime; if (dt <= .00001f) continue;
                Vector3 va = left ? samples[a].leftSoleCentroid : samples[a].rightSoleCentroid;
                Vector3 vb = left ? samples[b].leftSoleCentroid : samples[b].rightSoleCentroid;
                Vector3 velocity = (vb - va) / dt; float height = left ? samples[i].leftSoleMinimumY : samples[i].rightSoleMinimumY;
                if (height <= low + .025f && Mathf.Abs(velocity.y) < .55f) speeds.Add(-velocity.z);
            }
            count = speeds.Count; if (count == 0) return 0f; speeds.Sort();
            return count % 2 == 0 ? (speeds[count / 2 - 1] + speeds[count / 2]) * .5f : speeds[count / 2];
        }
    }
}
