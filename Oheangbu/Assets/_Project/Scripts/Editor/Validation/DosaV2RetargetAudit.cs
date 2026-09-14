using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Two bounded read-only diagnostics: reference-pose alignment and the two existing imported clips.</summary>
    public static class DosaV2RetargetAudit
    {
        [Serializable] private sealed class Bone
        {
            public string name;
            public Vector3 modelLocal, world;
            public Quaternion localRotation;
        }
        [Serializable] private sealed class Pose
        {
            public string model, mode, clip;
            public bool avatarValid, human;
            public float time, humanScale;
            public Vector3 rootWorld, bodyPosition;
            public Quaternion rootWorldRotation, bodyRotation;
            public string[] muscleNames;
            public float[] muscles;
            public List<Bone> bones = new List<Bone>();
        }
        [Serializable] private sealed class Curves
        {
            public string path, name, avatarName;
            public bool isHumanMotion, legacy, avatarValid;
            public float length;
            public int total, animator, transform, other;
            public string[] animatorProperties, transformProperties, otherProperties;
        }
        [Serializable] private sealed class Report
        {
            public string status, error;
            public string scope = "READ_ONLY_RETARGET_DIAGNOSTIC: four imported Avatars at all-zero muscles, then existing Idle/RunForward at exactly .25 seconds on V1/V2. Separate PreviewScene clones; no production object, importer or asset mutation. HumanPose body position is Avatar-scaled; bone modelLocal/world positions are meters.";
            public List<Curves> clips = new List<Curves>();
            public List<Pose> poses = new List<Pose>();
        }
        private static readonly HumanBodyBones[] Bones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "NOT_RUN: Edit Mode required";
            var report = new Report();
            Scene preview = default;
            try
            {
                string idlePath = DosaPlayerBuilder.ClipPath("Idle"), runPath = DosaPlayerBuilder.ClipPath("RunForward");
                var clips = new[] { GetClip(idlePath), GetClip(runPath) };
                report.clips.Add(CurveInfo(idlePath, clips[0])); report.clips.Add(CurveInfo(runPath, clips[1]));
                preview = EditorSceneManager.NewPreviewScene();
                foreach (string path in new[] { DosaPlayerBuilder.WorldModel, DosaV2PlayerBuilder.WorldModel, idlePath, runPath })
                    Evaluate(preview, path, null, true, report);
                foreach (string path in new[] { DosaPlayerBuilder.WorldModel, DosaV2PlayerBuilder.WorldModel })
                    foreach (var clip in clips) Evaluate(preview, path, clip, false, report);
                report.status = "RETARGET_DIAGNOSTIC_COMPLETE";
            }
            catch (Exception e) { report.status = "RETARGET_DIAGNOSTIC_FAILED"; report.error = e.GetBaseException().ToString(); }
            finally { if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview); }
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2"));
            Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, "retarget-reference-audit.json"), json);
            return json;
        }

        private static AnimationClip GetClip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) throw new InvalidOperationException("Missing imported clip: " + path);
            return clip;
        }
        private static Curves CurveInfo(string path, AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            string Describe(EditorCurveBinding b) => b.path + " :: " + b.propertyName;
            var animator = bindings.Where(b => b.type == typeof(Animator)).ToArray();
            var transform = bindings.Where(b => b.type == typeof(Transform)).ToArray();
            var other = bindings.Where(b => b.type != typeof(Animator) && b.type != typeof(Transform)).ToArray();
            return new Curves { path = path, name = clip.name, isHumanMotion = clip.isHumanMotion, legacy = clip.legacy,
                length = clip.length, avatarName = avatar != null ? avatar.name : null, avatarValid = avatar != null && avatar.isValid,
                total = bindings.Length, animator = animator.Length, transform = transform.Length, other = other.Length,
                animatorProperties = animator.Select(Describe).ToArray(), transformProperties = transform.Select(Describe).ToArray(),
                otherProperties = other.Select(Describe).ToArray() };
        }
        private static void Evaluate(Scene scene, string path, AnimationClip clip, bool zero, Report report)
        {
            GameObject instance = null; PlayableGraph graph = default; HumanPoseHandler handler = null;
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Missing model: " + path);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.transform.position = new Vector3(1000f, 0f, 1000f);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var animator = instance.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.isHuman)
                    throw new InvalidOperationException("Invalid Humanoid: " + path);
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                if (zero)
                {
                    report.poses.Add(Snapshot(animator, path, "imported_reference_before_zero", null, 0f, pose));
                    Array.Clear(pose.muscles, 0, pose.muscles.Length);
                    handler.SetHumanPose(ref pose); handler.GetHumanPose(ref pose);
                    report.poses.Add(Snapshot(animator, path, "all_zero_muscles", null, 0f, pose));
                }
                else
                {
                    graph = PlayableGraph.Create("DosaV2ReadOnlyRetarget"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                    var output = AnimationPlayableOutput.Create(graph, "Actual imported clip", animator); output.SetSourcePlayable(playable);
                    graph.Play(); playable.SetTime(.25); graph.Evaluate(0f);
                    handler.GetHumanPose(ref pose);
                    report.poses.Add(Snapshot(animator, path, "actual_imported_clip_playable", clip.name, .25f, pose));
                }
            }
            finally
            {
                handler?.Dispose(); if (graph.IsValid()) graph.Destroy();
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }
        private static Pose Snapshot(Animator animator, string path, string mode, string clip, float time, HumanPose human)
        {
            var result = new Pose { model = path, mode = mode, clip = clip, time = time,
                avatarValid = animator.avatar.isValid, human = animator.isHuman, humanScale = animator.humanScale,
                rootWorld = animator.transform.position, rootWorldRotation = animator.transform.rotation,
                bodyPosition = human.bodyPosition, bodyRotation = human.bodyRotation,
                muscleNames = HumanTrait.MuscleName, muscles = (float[])human.muscles.Clone() };
            foreach (var name in Bones)
            {
                var bone = animator.GetBoneTransform(name); if (bone == null) continue;
                result.bones.Add(new Bone { name = name.ToString(), modelLocal = animator.transform.InverseTransformPoint(bone.position),
                    world = bone.position, localRotation = bone.localRotation });
            }
            return result;
        }
    }
}
