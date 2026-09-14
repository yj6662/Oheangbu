using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
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
        [Serializable] private sealed class AuthoredSupportFile { public string fbxSha256; public AuthoredSupportClip[] clips; }
        [Serializable] private sealed class AuthoredSupportClip { public string name; public float duration; public AuthoredSupportSample[] samples; }
        [Serializable] private sealed class AuthoredSupportSample { public float time, left, right; public bool leftSupport, rightSupport; }
        [Serializable] private sealed class HeightCorrection
        {
            public string clip, method = "Actual authored C02 weighted-sole heights versus Unity Humanoid conversion. Only copied RootT.y is adjusted; limb rotations/lengths and gameplay root motion are unchanged.";
            public string status;
            public float beforeMinimum, afterMinimum, maximumCorrection, maximumResidual;
            public float[] before, desired, after;
        }
        private static AuthoredSupportClip AuthoredSupportFor(string name)
        {
            string path = Folder + "/AuthoredMotionSupport.json";
            if (!File.Exists(path)) throw new FileNotFoundException("Export actual authored sole samples before preparing motion.", path);
            var data = JsonUtility.FromJson<AuthoredSupportFile>(File.ReadAllText(path));
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(Actions))
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (actual != data.fbxSha256) throw new InvalidDataException("Authored foot metadata does not match the current animation FBX.");
            }
            return data.clips.FirstOrDefault(c => c.name == name);
        }
        private static void CorrectAuthoredSoleHeight(GameObject source, float scale, AnimationClip clip)
        {
            var authored = AuthoredSupportFor(clip.name);
            if (authored == null || authored.samples.Length != 121) throw new InvalidDataException("Missing authored sole trajectory: " + clip.name);
            var report = new HeightCorrection { clip = clip.name, desired = authored.samples.Select(s => Mathf.Max(.003f, Mathf.Min(s.left, s.right) * scale)).ToArray() };
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            var original = AnimationUtility.GetEditorCurve(clip, binding);
            if (original == null) throw new InvalidDataException("Copied Humanoid clip has no Body RootT.y curve: " + clip.name);
            report.before = HeightSamples(source, scale, clip, out float response);
            report.beforeMinimum = report.before.Min(); float[] current = report.before;
            if (response < .1f || response > 4f) throw new InvalidDataException("Invalid avatar height response: " + response);
            for (int pass = 0; pass < 3; pass++)
            {
                var existing = AnimationUtility.GetEditorCurve(clip, binding); var corrected = new AnimationCurve();
                for (int i = 0; i < current.Length; i++)
                {
                    float time = clip.length * i / (current.Length - 1);
                    float delta = report.desired[i] - current[i];
                    report.maximumCorrection = Mathf.Max(report.maximumCorrection, Mathf.Abs(report.desired[i] - report.before[i]));
                    corrected.AddKey(time, existing.Evaluate(time) + delta / response);
                }
                if (report.maximumCorrection > .45f) throw new InvalidDataException("Humanoid conversion height error exceeds the bounded correction: " + clip.name);
                AnimationUtility.SetEditorCurve(clip, binding, corrected);
                current = HeightSamples(source, scale, clip, out response);
                report.maximumResidual = Enumerable.Range(0, current.Length).Max(i => Mathf.Abs(report.desired[i] - current[i]));
                if (report.maximumResidual < .003f) break;
            }
            report.after = current; report.afterMinimum = current.Min(); report.status = report.maximumResidual < .003f ? "CONVERSION_HEIGHT_PASS" : "FAIL";
            Directory.CreateDirectory(ReportFolder); File.WriteAllText(Path.Combine(ReportFolder, "height_conversion_" + clip.name + ".json"), JsonUtility.ToJson(report, true));
            if (report.status == "FAIL") throw new InvalidDataException("Humanoid copied height did not match authored sole motion: " + clip.name + " residual " + report.maximumResidual);
            EditorUtility.SetDirty(clip);
        }
        private static float[] HeightSamples(GameObject source, float scale, AnimationClip clip, out float response)
        {
            var preview = EditorSceneManager.NewPreviewScene(); GameObject instance = null; PlayableGraph graph = default;
            response = 0f;
            try
            {
                instance = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(instance, preview);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); instance.transform.localScale = Vector3.one * scale;
                var animator = instance.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind();
                response = animator.humanScale * scale;
                var left = CalibrationSole(instance, animator.GetBoneTransform(HumanBodyBones.LeftFoot));
                var right = CalibrationSole(instance, animator.GetBoneTransform(HumanBodyBones.RightFoot));
                graph = PlayableGraph.Create("Copied_Humanoid_Height"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetSpeed(0);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(playable); graph.Play();
                var samples = new float[121];
                for (int i = 0; i < samples.Length; i++)
                {
                    playable.SetTime(clip.length * i / (samples.Length - 1)); graph.Evaluate(0);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    SampleSole(left, out float l); SampleSole(right, out float r); samples[i] = Mathf.Min(l, r);
                }
                return samples;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy(); if (instance != null) Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
