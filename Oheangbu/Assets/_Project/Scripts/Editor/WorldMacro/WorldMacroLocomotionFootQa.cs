using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.App.World;
using Oheangbu.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroLocomotionAuthoring
    {
        private static string FootQaModel => _assetFolderOverride==RecoveryFolder ? "Assets/_Project/Art/Characters/PlaytestRecoveryGripA/Player_C02_GripA.fbx" : Model;
        [Serializable] private sealed class FootCase
        {
            public string clip, status, exception, clipAsset, clipSha256, clipMetaSha256;
            public int fps, frames, stancePairs;
            public float scaledTime, slopeDegrees, maxPenetrationBefore, maxPenetrationAfter, maxLegLocalLengthError;
            public float maxLift, maxPelvisDrop, medianStanceSpeedBefore, medianStanceSpeedAfter;
            public float maxStanceSlipBefore, maxStanceSlipAfter;
            public float maxPelvisRise, clipDuration, sourceDuration, playbackRate, prescribedSpeed, scaledStep, evaluatedSeconds, completeCycles;
            public float maxBindLengthErrorBefore, maxBindLengthErrorAfter, maxBoneScaleError;
            public float maxFootSkinPenetrationBefore, maxFootSkinPenetrationAfter;
            public int leftStanceSamples, rightStanceSamples, leftStanceWindows, rightStanceWindows, leftStancePairs, rightStancePairs;
            public int leftSoleVertices, rightSoleVertices, leftFootSkinVertices, rightFootSkinVertices;
            public int worstFootSkinFrame = -1, worstFootSkinVertex = -1;
            public string worstFootSkinRenderer;
            public Vector3 worstFootSkinWorld;
            public bool finite = true, resourcesReleased;
            public List<FootThreshold> thresholds = new List<FootThreshold>();
            public List<string> failedThresholds = new List<string>();
        }
        [Serializable] private sealed class FootThreshold
        { public string name, comparison; public float measured, limit; public bool passed; }
        [Serializable] private sealed class FootReport
        {
            public string status, utc, exception, modelSha256, actionsSha256, controllerSha256, profileSha256, supportSha256, fixtureSha256, solverSha256;
            public string scope = "SYNTHETIC SINGLE-CLIP POSE: isolated current C02 avatar, prescribed actor translation and diagnostic ground planes at explicit scaled steps. This bypasses the live blend tree and actual slope/collision velocity. Exact all-influence LBS is sampled before/after the production solver. Skin penetration additionally checks all Foot+Toes-weighted vertices (>=0.5 summed weight, no lowest-rest-height filter); renderer and worst vertex identify possible weighted garment findings. No native input, terrain raycast, live-player movement, route play or build.";
            public List<FootCase> cases = new List<FootCase>();
            public bool resourcesReleased;
        }
        private sealed class FootQaVertex
        { public CalibrationVertex skin; public string renderer; public int vertex; }
        private sealed class FootQaLimb
        { public Transform from, to; public float length; }
        public static string RunFootChecks()
        {
            EditOnly(); var report = new FootReport { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                report.modelSha256 = FootQaSha(FootQaModel); report.actionsSha256 = FootQaSha(Actions);
                report.controllerSha256 = FootQaSha(ControllerPath); report.profileSha256 = FootQaSha(ProfilePath);
                report.supportSha256 = FootQaSha(Folder + "/AuthoredMotionSupport.json");
                report.fixtureSha256 = FootQaSha("Assets/_Project/Scripts/Editor/WorldMacro/WorldMacroLocomotionFootQa.cs");
                report.solverSha256 = FootQaSha("Assets/_Project/Scripts/App/World/WorldMacroPlayerFootPlacement.cs");
                var profile = Need<WorldMacroPlayerAppearanceProfile>(ProfilePath);
                var controller = Need<AnimatorController>(ControllerPath);
                var tree = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state.motion as BlendTree;
                foreach (int fps in new[] { 30, 60, 120 }) foreach (float timeScale in new[] { 1f, .2f })
                    foreach (float slope in new[] { 0f, 12f })
                        foreach (var clip in profile.NaturalLocomotion ? (fps==60&&timeScale==1f&&slope==0f ? profile.DirectionalWalk.Concat(profile.DirectionalRun).ToArray():new[]{profile.Walk,profile.Run}) : profile.RecoveryMotions ? (fps==60&&timeScale==1f&&slope==0f ? new[]{profile.Walk,profile.Run}.Concat(profile.DirectionalCrouch).ToArray():new[]{profile.Walk,profile.Run,profile.DirectionalCrouch[0]}) : fps == 60 && timeScale == 1f && slope == 0f
                            ? profile.DirectionalWalk.Concat(profile.DirectionalRun).ToArray()
                            : new[] { profile.DirectionalWalk[0], profile.DirectionalRun[0] })
                        {
                            try
                            {
                                var selectedTree=clip.name.StartsWith("Crouch") ? controller.layers[0].stateMachine.states.First(s=>s.state.name=="Crouch").state.motion as BlendTree : tree;
                                var motion = selectedTree.children.First(c => c.motion == clip);
                                report.cases.Add(MeasureFeet(profile, clip, motion.timeScale, clip.name.StartsWith("Crouch")?1.2f:clip.name.StartsWith("Walk", StringComparison.Ordinal) ? 2.2f : 5.5f, fps, timeScale, slope));
                            }
                            catch (Exception e)
                            { report.cases.Add(new FootCase { clip = clip != null ? clip.name : "MISSING", fps = fps, scaledTime = timeScale, slopeDegrees = slope, status = "FAIL_EXCEPTION", exception = e.ToString(), finite = false, resourcesReleased = true }); }
                        }
            }
            catch (Exception e) { report.exception = e.ToString(); }
            report.status = string.IsNullOrEmpty(report.exception) && report.cases.Count > 0 && report.cases.All(c => c.status == "PASS_LIMITS")
                ? "SYNTHETIC_FOOT_LIMITS_PASS_VISUAL_UNVERIFIED" : "FAILED_LIMITS";
            report.resourcesReleased = report.cases.All(c => c.resourcesReleased);
            Directory.CreateDirectory(ReportFolder);
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string history = Path.Combine(ReportFolder, "FootQaHistory"); Directory.CreateDirectory(history);
            string latest = Path.Combine(ReportFolder, "unity_synthetic_feet.json");
            if (File.Exists(latest)) File.Copy(latest, Path.Combine(history, run + "_previous.json"), false);
            var json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(history, run + ".json"), json); File.WriteAllText(latest, json); return json;
        }
        private static FootCase MeasureFeet(WorldMacroPlayerAppearanceProfile profile, AnimationClip clip, float rate,
            float speed, int fps, float timeScale, float slope)
        {
            var result = new FootCase { clip = clip.name, fps = fps, scaledTime = timeScale, slopeDegrees = slope,
                playbackRate = rate, prescribedSpeed = speed, clipDuration = clip.length, clipAsset = AssetDatabase.GetAssetPath(clip) };
            var preview = EditorSceneManager.NewPreviewScene(); GameObject actor = null; PlayableGraph graph = default;
            PlayerLocomotionProfileSO config = null;
            try
            {
                if (!FootQaFinite(rate) || rate <= 0f || !FootQaFinite(clip.length) || clip.length <= 0f)
                    throw new InvalidDataException("Finite positive duration and playback rate are required.");
                result.clipSha256 = FootQaSha(result.clipAsset); result.clipMetaSha256 = FootQaSha(result.clipAsset + ".meta");
                actor = new GameObject("Isolated_Foot_Pose_Only"); SceneManager.MoveGameObjectToScene(actor, preview);
                var capsule = actor.AddComponent<CharacterController>(); capsule.height = 1.75f; capsule.radius = .28f;
                capsule.center = Vector3.up * .875f; capsule.skinWidth = .03f;
                var motor = actor.AddComponent<PlayerMotor>(); motor.enabled = false;
                config = ScriptableObject.CreateInstance<PlayerLocomotionProfileSO>();
                config.PreserveAuthoredFootRoll = profile.NaturalLocomotion;
                typeof(PlayerMotor).GetField("_locomotion", MotorFlags).SetValue(motor, config);
                Vector3 axis = clip.name.EndsWith("Back", StringComparison.Ordinal) ? Vector3.back : clip.name.EndsWith("Left", StringComparison.Ordinal)
                    ? Vector3.left : clip.name.EndsWith("Right", StringComparison.Ordinal) ? Vector3.right : Vector3.forward;
                typeof(PlayerMotor).GetField("_actualLocalVelocity", MotorFlags).SetValue(motor, axis * speed);
                var model = Object.Instantiate(Need<GameObject>(FootQaModel), actor.transform);
                model.transform.localScale = Vector3.one * profile.UniformScale;
                model.transform.localPosition = profile.LocalOffset; model.transform.localRotation = Quaternion.Euler(0, profile.FacingYaw, 0);
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                var foot = model.AddComponent<WorldMacroPlayerFootPlacement>(); foot.enabled = false;
                foot.Configure(animator, capsule, null, model.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                if (!foot.IsBound) throw new InvalidDataException(foot.BindingError);
                var left = CalibrationSole(model, animator.GetBoneTransform(HumanBodyBones.LeftFoot));
                var right = CalibrationSole(model, animator.GetBoneTransform(HumanBodyBones.RightFoot));
                var leftSkin = FootQaAllSkin(model, animator.GetBoneTransform(HumanBodyBones.LeftFoot));
                var rightSkin = FootQaAllSkin(model, animator.GetBoneTransform(HumanBodyBones.RightFoot));
                result.leftSoleVertices = left.Length; result.rightSoleVertices = right.Length;
                result.leftFootSkinVertices = leftSkin.Length; result.rightFootSkinVertices = rightSkin.Length;
                var limbs = FootQaBindLimbs(animator);
                var scales = new Dictionary<Transform, Vector3>();
                foreach (var limb in limbs)
                    for (Transform t = limb.to; t != null && t != actor.transform; t = t.parent) scales[t] = t.localScale;
                graph = PlayableGraph.Create("Isolated_Foot_Pose"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable); graph.Play();
                float dt = timeScale / fps;
                // At least two complete source cycles plus one explicit step; never hide a seam in a fixed short run.
                int frames = Mathf.CeilToInt(Mathf.Max(1.4f, 2f * clip.length / rate + dt) / dt);
                if (frames > 12000) throw new InvalidDataException("Unreasonable rate requires more than 12000 diagnostic steps.");
                result.frames = frames; result.scaledStep = dt; result.evaluatedSeconds = frames * dt;
                result.completeCycles = result.evaluatedSeconds * rate / clip.length;
                Vector3 normal = Quaternion.Euler(-slope, 0, 0) * Vector3.up;
                Vector3 travel = Vector3.ProjectOnPlane(axis, normal).normalized;
                var support = AuthoredSupportFor(clip.name);
                if (support == null || support.samples == null || support.samples.Length != 121)
                    throw new InvalidDataException("121 matching authored support samples are required.");
                result.sourceDuration = support.duration;
                if (!FootQaFinite(support.duration) || Mathf.Abs(support.duration - clip.length) > .002f)
                    throw new InvalidDataException("Authored duration and imported clip duration differ.");
                for (int i = 0; i < support.samples.Length; i++)
                    if (support.samples[i] == null || !FootQaFinite(support.samples[i].time)
                        || Mathf.Abs(support.samples[i].time - support.duration * i / 120f) > .002f)
                        throw new InvalidDataException("Authored support timing is not uniform or is invalid at " + i);
                var beforeSpeeds = new List<float>(); var afterSpeeds = new List<float>();
                Vector3 previousBeforeL = Vector3.zero, previousBeforeR = Vector3.zero, previousAfterL = Vector3.zero, previousAfterR = Vector3.zero;
                bool previousL = false, previousR = false;
                Vector3 originBeforeL = Vector3.zero, originBeforeR = Vector3.zero, originAfterL = Vector3.zero, originAfterR = Vector3.zero;
                for (int i = 0; i <= frames; i++)
                {
                    foot.RestoreAnimatedPose(); float elapsed = i * dt;
                    actor.transform.position = travel * speed * elapsed; Physics.SyncTransforms();
                    playable.SetTime(elapsed * rate % clip.length); graph.Evaluate(0);
                    model.transform.localPosition = profile.LocalOffset; model.transform.localRotation = Quaternion.Euler(0, profile.FacingYaw, 0);
                    Vector3 beforeL = PlaneSole(left, normal, out float lowL), beforeR = PlaneSole(right, normal, out float lowR);
                    result.maxPenetrationBefore = Mathf.Max(result.maxPenetrationBefore, -Mathf.Min(lowL, lowR));
                    FootQaCheckSkeleton(limbs, scales, result, false);
                    FootQaSkinMinimum(leftSkin, rightSkin, normal, result, i, false);
                    foot.EvaluatePoseAgainstPlaneAtStep(Vector3.zero, normal, true, false, false, dt);
                    Vector3 afterL = PlaneSole(left, normal, out lowL), afterR = PlaneSole(right, normal, out lowR);
                    FootQaCheckSkeleton(limbs, scales, result, true);
                    FootQaSkinMinimum(leftSkin, rightSkin, normal, result, i, true);
                    var diagnostic = foot.Diagnostics;
                    float phase = elapsed * rate % clip.length / clip.length;
                    var window = support.samples[Mathf.Clamp(Mathf.RoundToInt(phase * 120f), 0, 120)];
                    bool stanceL = window.leftSupport, stanceR = window.rightSupport;
                    if (stanceL) result.leftStanceSamples++; if (stanceR) result.rightStanceSamples++;
                    if (stanceL && !previousL) result.leftStanceWindows++; if (stanceR && !previousR) result.rightStanceWindows++;
                    result.maxPenetrationAfter = Mathf.Max(result.maxPenetrationAfter, -Mathf.Min(lowL, lowR));
                    result.maxLegLocalLengthError = Mathf.Max(result.maxLegLocalLengthError, diagnostic.MaximumLocalLengthError);
                    result.maxLift = Mathf.Max(result.maxLift, Mathf.Max(diagnostic.LeftAppliedLift, diagnostic.RightAppliedLift));
                    result.maxPelvisDrop = Mathf.Max(result.maxPelvisDrop, -diagnostic.PelvisOffset);
                    result.maxPelvisRise = Mathf.Max(result.maxPelvisRise, diagnostic.PelvisOffset);
                    if (stanceL && !previousL) { originBeforeL = beforeL; originAfterL = afterL; }
                    if (stanceR && !previousR) { originBeforeR = beforeR; originAfterR = afterR; }
                    if (stanceL) { result.maxStanceSlipBefore = Mathf.Max(result.maxStanceSlipBefore, Vector3.ProjectOnPlane(beforeL - originBeforeL, normal).magnitude); result.maxStanceSlipAfter = Mathf.Max(result.maxStanceSlipAfter, Vector3.ProjectOnPlane(afterL - originAfterL, normal).magnitude); }
                    if (stanceR) { result.maxStanceSlipBefore = Mathf.Max(result.maxStanceSlipBefore, Vector3.ProjectOnPlane(beforeR - originBeforeR, normal).magnitude); result.maxStanceSlipAfter = Mathf.Max(result.maxStanceSlipAfter, Vector3.ProjectOnPlane(afterR - originAfterR, normal).magnitude); }
                    if (i > 2 && previousL && stanceL)
                    { result.leftStancePairs++; beforeSpeeds.Add(Vector3.ProjectOnPlane(beforeL - previousBeforeL, normal).magnitude / dt); afterSpeeds.Add(Vector3.ProjectOnPlane(afterL - previousAfterL, normal).magnitude / dt); }
                    if (i > 2 && previousR && stanceR)
                    { result.rightStancePairs++; beforeSpeeds.Add(Vector3.ProjectOnPlane(beforeR - previousBeforeR, normal).magnitude / dt); afterSpeeds.Add(Vector3.ProjectOnPlane(afterR - previousAfterR, normal).magnitude / dt); }
                    previousL = stanceL; previousR = stanceR;
                    previousBeforeL = beforeL; previousBeforeR = beforeR; previousAfterL = afterL; previousAfterR = afterR;
                }
                beforeSpeeds.Sort(); afterSpeeds.Sort(); result.stancePairs = beforeSpeeds.Count;
                result.medianStanceSpeedBefore = beforeSpeeds.Count > 0 ? beforeSpeeds[beforeSpeeds.Count / 2] : -1;
                result.medianStanceSpeedAfter = afterSpeeds.Count > 0 ? afterSpeeds[afterSpeeds.Count / 2] : -1;
                FootQaLimit(result, "finite samples", result.finite ? 1f : 0f, 1f, ">=", result.finite);
                FootQaLimit(result, "complete source cycles", result.completeCycles, 2f, ">=", result.completeCycles >= 2f);
                FootQaLimit(result, "left stance pairs", result.leftStancePairs, 3f, ">", result.leftStancePairs > 3);
                FootQaLimit(result, "right stance pairs", result.rightStancePairs, 3f, ">", result.rightStancePairs > 3);
                FootQaLimit(result, "left stance windows", result.leftStanceWindows, 2f, ">=", result.leftStanceWindows >= 2);
                FootQaLimit(result, "right stance windows", result.rightStanceWindows, 2f, ">=", result.rightStanceWindows >= 2);
                FootQaLimit(result, "solver local length delta m", result.maxLegLocalLengthError, .0001f, "<", result.maxLegLocalLengthError < .0001f);
                FootQaLimit(result, "bind world length before IK m", result.maxBindLengthErrorBefore, .0001f, "<", result.maxBindLengthErrorBefore < .0001f);
                FootQaLimit(result, "bind world length after IK m", result.maxBindLengthErrorAfter, .0001f, "<", result.maxBindLengthErrorAfter < .0001f);
                FootQaLimit(result, "bone local scale component delta", result.maxBoneScaleError, .00001f, "<", result.maxBoneScaleError < .00001f);
                FootQaLimit(result, "ankle lift m", result.maxLift, .076f, "<=", result.maxLift <= .076f);
                FootQaLimit(result, "pelvis drop m", result.maxPelvisDrop, .0351f, "<=", result.maxPelvisDrop <= .0351f);
                FootQaLimit(result, "pelvis rise m", result.maxPelvisRise, .0401f, "<=", result.maxPelvisRise <= .0401f);
                FootQaLimit(result, "selected sole penetration m", result.maxPenetrationAfter, .015f, "<", result.maxPenetrationAfter < .015f);
                FootQaLimit(result, "independent Foot+Toes skin penetration m", result.maxFootSkinPenetrationAfter, .015f, "<", result.maxFootSkinPenetrationAfter < .015f);
                FootQaLimit(result, "stance centroid displacement m", result.maxStanceSlipAfter, .10f, "<=", result.maxStanceSlipAfter <= .10f);
                result.status = result.failedThresholds.Count == 0 ? "PASS_LIMITS" : "FAIL_LIMITS";
            }
            catch (Exception e) { result.status = "FAIL_EXCEPTION"; result.exception = e.ToString(); result.finite = false; result.failedThresholds.Add("Diagnostic execution failed; remaining thresholds unverified."); }
            finally
            {
                if (graph.IsValid()) graph.Destroy(); if (actor != null) Object.DestroyImmediate(actor);
                if (config != null) Object.DestroyImmediate(config); EditorSceneManager.ClosePreviewScene(preview);
                result.resourcesReleased = true;
            }
            return result;
        }
        private static Vector3 PlaneSole(CalibrationVertex[] samples, Vector3 normal, out float minimum)
        {
            if (samples == null || samples.Length == 0) throw new InvalidDataException("No sole skin samples.");
            Vector3 center = Vector3.zero; minimum = float.PositiveInfinity;
            foreach (var sample in samples)
            {
                Vector3 p = Vector3.zero;
                for (int i = 0; i < sample.weights.Length; i++) p += sample.bones[i].TransformPoint(sample.bindPoints[i]) * sample.weights[i];
                if (!Finite(p)) throw new InvalidDataException("Nonfinite skinned sole vertex.");
                minimum = Mathf.Min(minimum, Vector3.Dot(p, normal)); center += p;
            }
            return center / samples.Length;
        }
        private static FootQaVertex[] FootQaAllSkin(GameObject model, Transform foot)
        {
            if (foot == null) throw new InvalidDataException("Missing Humanoid foot.");
            var rows = new List<FootQaVertex>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skin.sharedMesh; if (mesh == null) continue;
                var counts = mesh.GetBonesPerVertex(); var weights = mesh.GetAllBoneWeights();
                var vertices = mesh.vertices; var bones = skin.bones; var bind = mesh.bindposes;
                try
                {
                    int offset = 0;
                    for (int vertex = 0; vertex < counts.Length; vertex++)
                    {
                        int first = offset; float total = 0f, maskWeight = 0f;
                        for (int j = 0; j < counts[vertex]; j++)
                        {
                            var w = weights[offset++];
                            if (w.boneIndex < 0 || w.boneIndex >= bones.Length || w.boneIndex >= bind.Length || bones[w.boneIndex] == null)
                                throw new InvalidDataException("Invalid skin influence: " + skin.name + " vertex " + vertex);
                            if (!FootQaFinite(w.weight)) throw new InvalidDataException("Nonfinite skin weight.");
                            total += w.weight;
                            if (bones[w.boneIndex] == foot || bones[w.boneIndex].IsChildOf(foot)) maskWeight += w.weight;
                        }
                        if (maskWeight < .5f) continue;
                        if (Mathf.Abs(total - 1f) > .001f) throw new InvalidDataException("Foot skin weights are not normalized.");
                        var sample = new CalibrationVertex { bones = new Transform[counts[vertex]], bindPoints = new Vector3[counts[vertex]], weights = new float[counts[vertex]] };
                        for (int j = 0; j < counts[vertex]; j++)
                        {
                            var w = weights[first + j]; sample.bones[j] = bones[w.boneIndex]; sample.weights[j] = w.weight;
                            sample.bindPoints[j] = bind[w.boneIndex].MultiplyPoint3x4(vertices[vertex]);
                        }
                        rows.Add(new FootQaVertex { skin = sample, renderer = skin.name, vertex = vertex });
                    }
                }
                finally { counts.Dispose(); weights.Dispose(); }
            }
            if (rows.Count < 4) throw new InvalidDataException("Insufficient independent foot+toe skin for " + foot.name);
            return rows.ToArray();
        }
        private static void FootQaSkinMinimum(FootQaVertex[] left, FootQaVertex[] right, Vector3 normal, FootCase result, int frame, bool after)
        {
            foreach (var side in new[] { left, right }) foreach (var row in side)
            {
                Vector3 point = Vector3.zero;
                for (int j = 0; j < row.skin.weights.Length; j++)
                    point += row.skin.bones[j].TransformPoint(row.skin.bindPoints[j]) * row.skin.weights[j];
                if (!Finite(point)) throw new InvalidDataException("Nonfinite independent foot skin sample.");
                float penetration = Mathf.Max(0f, -Vector3.Dot(point, normal));
                if (!after) result.maxFootSkinPenetrationBefore = Mathf.Max(result.maxFootSkinPenetrationBefore, penetration);
                else if (penetration > result.maxFootSkinPenetrationAfter)
                {
                    result.maxFootSkinPenetrationAfter = penetration; result.worstFootSkinFrame = frame;
                    result.worstFootSkinVertex = row.vertex; result.worstFootSkinRenderer = row.renderer; result.worstFootSkinWorld = point;
                }
            }
        }
        private static FootQaLimb[] FootQaBindLimbs(Animator animator)
        {
            var pairs = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            var rows = new List<FootQaLimb>();
            for (int side = 0; side < 2; side++) for (int segment = 0; segment < 2; segment++)
            {
                var from = animator.GetBoneTransform(pairs[side * 3 + segment]);
                var to = animator.GetBoneTransform(pairs[side * 3 + segment + 1]);
                if (from == null || to == null) throw new InvalidDataException("Missing Humanoid leg segment.");
                float length = Vector3.Distance(from.position, to.position);
                if (!FootQaFinite(length) || length < .05f) throw new InvalidDataException("Invalid neutral limb length.");
                rows.Add(new FootQaLimb { from = from, to = to, length = length });
            }
            return rows.ToArray();
        }
        private static void FootQaCheckSkeleton(FootQaLimb[] limbs, Dictionary<Transform, Vector3> scales, FootCase result, bool after)
        {
            foreach (var row in limbs)
            {
                float length = Vector3.Distance(row.from.position, row.to.position);
                if (!FootQaFinite(length)) throw new InvalidDataException("Nonfinite leg length.");
                float error = Mathf.Abs(length - row.length);
                if (after) result.maxBindLengthErrorAfter = Mathf.Max(result.maxBindLengthErrorAfter, error);
                else result.maxBindLengthErrorBefore = Mathf.Max(result.maxBindLengthErrorBefore, error);
            }
            foreach (var row in scales)
            {
                Vector3 current = row.Key.localScale;
                if (!Finite(current)) throw new InvalidDataException("Nonfinite local bone scale.");
                Vector3 delta = current - row.Value;
                result.maxBoneScaleError = Mathf.Max(result.maxBoneScaleError, Mathf.Max(Mathf.Abs(delta.x), Mathf.Max(Mathf.Abs(delta.y), Mathf.Abs(delta.z))));
            }
        }
        private static void FootQaLimit(FootCase result, string name, float value, float limit, string comparison, bool passed)
        {
            passed &= FootQaFinite(value);
            result.thresholds.Add(new FootThreshold { name = name, measured = value, limit = limit, comparison = comparison, passed = passed });
            if (!passed) result.failedThresholds.Add(name + ": " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " requires " + comparison + " " + limit.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        private static bool FootQaFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static string FootQaSha(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new FileNotFoundException("Foot QA source asset is missing.", path);
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
