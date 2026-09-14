using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>임시 시각 프리팹만 평가한다. 컨트롤러/Avatar/씬 플레이어를 수정하지 않는다.</summary>
    public static class DosaAnimatorAudit
    {
        [Serializable] private sealed class BoneSample
        {
            public string bone;
            public Vector3 modelPosition, hipsRelativePosition;
            public Quaternion localRotation;
        }
        [Serializable] private sealed class PoseSample
        {
            public string mode;
            public float sampleTime, humanScale, baseNormalizedTime, drawingNormalizedTime;
            public int baseStateHash, drawingStateHash;
            public float[] layerWeights;
            public string[] currentClipNames;
            public List<BoneSample> bones = new List<BoneSample>();
        }
        [Serializable] private sealed class Comparison
        {
            public string a, b;
            public float sampleTime, maxArmPositionDelta;
        }
        [Serializable] private sealed class Report
        {
            public string status;
            public string note = "Diagnostic, not an art approval. Every mode uses a fresh temporary PF_DosaVisual clone; presentation behaviours are disabled and no production object or asset is changed. Positions are model-local Unity XYZ. Compare layer weights before attributing differences to the empty drawing layer. Single-clip Playables and SampleAnimation still use imported Unity animation/Avatar data; the prefab bind pose and Blender reference supply the separate source comparison.";
            public string sourceReference = "Art/Player/idle-locomotion-hand-position-diagnostic.json (Idle frame 1): left elbow (-.267031,1.112051,.064318), left hand (-.280912,.903953,.230841), right elbow (.213382,1.109196,-.130742), right hand (.287479,.868580,-.062104).";
            public List<PoseSample> poses = new List<PoseSample>();
            public List<Comparison> comparisons = new List<Comparison>();
            public List<string> errors = new List<string>();
        }

        private static readonly HumanBodyBones[] AuditedBones = { HumanBodyBones.Hips, HumanBodyBones.Spine,
            HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };

        public static string Run()
        {
            if (!Application.isPlaying) return "NOT_RUN: Play Mode required";
            var report = new Report();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.VisualPrefabPath);
            var idle = AssetDatabase.LoadAllAssetsAtPath(DosaPlayerBuilder.ClipPath("Idle")).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (prefab == null || idle == null) return "NOT_RUN: missing visual prefab or imported Idle clip";
            var randomState = UnityEngine.Random.state;
            try
            {
                foreach (string mode in new[] { "prefab_bind_pose", "controller_default", "controller_layer1_zero",
                    "single_idle_playable", "sample_animation", "controller_after_draw_release" })
                {
                    try { Evaluate(prefab, idle, mode, report); }
                    catch (Exception ex) { report.errors.Add(mode + ": " + ex.GetBaseException().Message); }
                }
                foreach (float time in new[] { 0f, 1f })
                foreach (string b in new[] { "controller_layer1_zero", "single_idle_playable", "sample_animation", "controller_after_draw_release" })
                {
                    PoseSample aPose = report.poses.FirstOrDefault(p => p.mode == "controller_default" && p.sampleTime == time);
                    PoseSample bPose = report.poses.FirstOrDefault(p => p.mode == b && p.sampleTime == time);
                    if (aPose == null || bPose == null) continue;
                    float max = 0f;
                    foreach (var bone in aPose.bones.Where(p => p.bone.Contains("Arm") || p.bone.Contains("Hand")))
                    {
                        var other = bPose.bones.FirstOrDefault(p => p.bone == bone.bone);
                        if (other != null) max = Mathf.Max(max, Vector3.Distance(bone.modelPosition, other.modelPosition));
                    }
                    report.comparisons.Add(new Comparison { a = aPose.mode, b = b, sampleTime = time, maxArmPositionDelta = max });
                }
            }
            finally { UnityEngine.Random.state = randomState; }
            report.status = report.errors.Count == 0 ? "DIAGNOSTIC_COMPLETE" : "DIAGNOSTIC_ERRORS";
            string directory = Path.GetFullPath("Screenshots/PlayerDosa"); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "animator-idle-audit.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return report.status + ": poses=" + report.poses.Count + "; "
                + string.Join(" | ", report.comparisons.Select(c => c.b + "@" + c.sampleTime + "s delta=" + c.maxArmPositionDelta.ToString("F5") + "m"))
                + "; errors=" + string.Join(" | ", report.errors) + "; report=" + path;
        }

        private static void Evaluate(GameObject prefab, AnimationClip idle, string mode, Report report)
        {
            GameObject clone = null;
            PlayableGraph graph = default;
            try
            {
                clone = Object.Instantiate(prefab);
                clone.name = "DosaAnimatorAudit_" + mode; clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetPositionAndRotation(new Vector3(1600f, 0f, 1600f), Quaternion.identity);
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var world = clone.transform.Find("WorldBody").GetComponent<Animator>();
                foreach (var animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = animator == world;
                world.applyRootMotion = false; world.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (mode == "prefab_bind_pose") { report.poses.Add(Snapshot(world, mode, 0f)); return; }
                world.Rebind();
                if (mode == "single_idle_playable")
                {
                    world.runtimeAnimatorController = null;
                    graph = PlayableGraph.Create("DosaIdleAudit_SingleClip");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var clip = AnimationClipPlayable.Create(graph, idle);
                    clip.SetApplyFootIK(false); clip.SetApplyPlayableIK(false);
                    var output = AnimationPlayableOutput.Create(graph, "Idle", world); output.SetSourcePlayable(clip);
                    graph.Play(); clip.SetTime(0); graph.Evaluate(0f);
                    report.poses.Add(Snapshot(world, mode, 0f));
                    for (int i = 0; i < 60; i++) graph.Evaluate(1f / 60f);
                    report.poses.Add(Snapshot(world, mode, 1f));
                    return;
                }
                if (mode == "sample_animation")
                {
                    world.runtimeAnimatorController = null; world.enabled = false;
                    idle.SampleAnimation(world.gameObject, 0f); report.poses.Add(Snapshot(world, mode, 0f));
                    idle.SampleAnimation(world.gameObject, 1f); report.poses.Add(Snapshot(world, mode, 1f));
                    return;
                }
                ResetParameters(world);
                if (mode == "controller_layer1_zero" && world.layerCount > 1) world.SetLayerWeight(1, 0f);
                world.Play("Locomotion", 0, 0f); world.Update(0f);
                if (mode == "controller_after_draw_release")
                {
                    world.SetBool("Combat", true); world.SetBool("Drawing", true);
                    for (int i = 0; i < 60; i++) world.Update(1f / 60f);
                    world.SetBool("Combat", false); world.SetBool("Drawing", false);
                    for (int i = 0; i < 120; i++) world.Update(1f / 60f);
                    world.Play("Locomotion", 0, 0f); world.Update(0f);
                }
                report.poses.Add(Snapshot(world, mode, 0f));
                for (int i = 0; i < 60; i++) world.Update(1f / 60f);
                report.poses.Add(Snapshot(world, mode, 1f));
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        private static void ResetParameters(Animator animator)
        {
            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool) animator.SetBool(parameter.nameHash, false);
                else if (parameter.type == AnimatorControllerParameterType.Float)
                    animator.SetFloat(parameter.nameHash, parameter.name.EndsWith("PlaybackRate", StringComparison.Ordinal) ? 1f : 0f);
                else if (parameter.type == AnimatorControllerParameterType.Int) animator.SetInteger(parameter.nameHash, 0);
                else animator.ResetTrigger(parameter.nameHash);
            }
        }

        private static PoseSample Snapshot(Animator animator, string mode, float time)
        {
            var sample = new PoseSample { mode = mode, sampleTime = time, humanScale = animator.humanScale,
                layerWeights = new float[animator.runtimeAnimatorController != null ? animator.layerCount : 0] };
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            foreach (var bone in AuditedBones)
            {
                Transform transform = animator.GetBoneTransform(bone);
                if (transform == null) continue;
                sample.bones.Add(new BoneSample { bone = bone.ToString(), modelPosition = animator.transform.InverseTransformPoint(transform.position),
                    hipsRelativePosition = animator.transform.InverseTransformVector(transform.position - hips.position), localRotation = transform.localRotation });
            }
            for (int i = 0; i < sample.layerWeights.Length; i++) sample.layerWeights[i] = animator.GetLayerWeight(i);
            if (sample.layerWeights.Length > 0)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0); sample.baseStateHash = state.fullPathHash; sample.baseNormalizedTime = state.normalizedTime;
                sample.currentClipNames = animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name + ":" + c.weight.ToString("F3")).ToArray();
            }
            if (sample.layerWeights.Length > 1)
            {
                var state = animator.GetCurrentAnimatorStateInfo(1); sample.drawingStateHash = state.fullPathHash; sample.drawingNormalizedTime = state.normalizedTime;
            }
            return sample;
        }
    }
}
