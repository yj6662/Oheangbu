using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>Copies existing motions; corrects only their source T-pose reference. Never modifies V1 sources.</summary>
    public static class DosaV2MotionRetargetBuilder
    {
        public const string Folder = "Assets/_Project/Art/Characters/DosaV2/Animations";
        public const string AvatarPath = Folder + "/AV_DosaV1_TPoseSource.asset";
        public const string OverridePath = Folder + "/AOC_DosaV2_ExistingMotions.overrideController";
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private sealed class Pose
        {
            public string clip;
            public Vector3 leftShoulder, leftElbow, leftHand, hips, leftKnee, leftFoot;
            public bool humanMotion;
            public int transformCurves;
        }
        [Serializable] private sealed class Report
        {
            public string status, error, backupDirectory, sourceRoot;
            public string scope = "Existing motion import reference correction only; no new animation authoring. Trial copies only Idle/RunForward and does not assign the playable profile. Full method copies 17 original motions and assigns a separate AnimatorOverrideController to the V2 playable profile.";
            public bool sourcePoseValid, sourceAvatarValid, originalFilesUnchanged, applied;
            public float sourcePoseErrorBefore, sourcePoseErrorAfter;
            public Quaternion sourceRootRotation;
            public List<string> copied = new List<string>();
            public List<Pose> samples = new List<Pose>();
        }

        public static string PrepareTwoClipTrial() => Build(false);
        public static string CompleteAndApply() => Build(true);

        private static string Build(bool full)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "NOT_RUN: Edit Mode required";
            var report = new Report();
            string[] names = full ? DosaPlayerBuilder.MotionNames : new[] { "Idle", "RunForward" };
            var originals = new[] { DosaPlayerBuilder.WorldModel, DosaPlayerBuilder.ControllerPath }
                .Concat(DosaPlayerBuilder.MotionNames.Select(DosaPlayerBuilder.ClipPath))
                .SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).ToDictionary(p => p, Sha);
            report.backupDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Backups/Retarget_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
            Directory.CreateDirectory(report.backupDirectory);
            try
            {
                DevSceneKit.EnsureFolder(Folder);
                Backup(report, AvatarPath); Backup(report, OverridePath);
                foreach (string name in names) Backup(report, TargetPath(name));
                if (full) Backup(report, DosaV2PlayableDraftBuilder.VisualProfilePath);
                var avatar = BuildSourceAvatar(report);
                foreach (string name in names)
                {
                    string original = DosaPlayerBuilder.ClipPath(name), target = TargetPath(name);
                    if (!File.Exists(original)) throw new FileNotFoundException(original);
                    if (!File.Exists(target))
                    {
                        if (!AssetDatabase.CopyAsset(original, target)) throw new IOException("Copy failed: " + target);
                    }
                    else { File.Copy(original, target, true); AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport); }
                    var sourceImporter = (ModelImporter)AssetImporter.GetAtPath(original);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(target);
                    importer.animationType = ModelImporterAnimationType.Human;
                    importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                    importer.sourceAvatar = avatar;
                    importer.importAnimation = true; importer.optimizeGameObjects = false;
                    importer.clipAnimations = sourceImporter.clipAnimations;
                    importer.SaveAndReimport();
                    var clip = Clip(target);
                    if (!clip.isHumanMotion) throw new InvalidOperationException("Copied clip did not import as Humanoid: " + name);
                    report.copied.Add(target);
                }
                foreach (string name in new[] { "Idle", "RunForward" }) report.samples.Add(SampleV2(name));
                if (full)
                {
                    var originalController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DosaPlayerBuilder.ControllerPath);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
                    if (controller == null)
                    {
                        controller = new AnimatorOverrideController(originalController) { name = "AOC_DosaV2_ExistingMotions" };
                        AssetDatabase.CreateAsset(controller, OverridePath);
                    }
                    else controller.runtimeAnimatorController = originalController;
                    var replacements = DosaPlayerBuilder.MotionNames.ToDictionary(n => Clip(DosaPlayerBuilder.ClipPath(n)), n => Clip(TargetPath(n)));
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); controller.GetOverrides(overrides);
                    for (int i = 0; i < overrides.Count; i++)
                    {
                        if (!replacements.TryGetValue(overrides[i].Key, out var replacement)) throw new InvalidOperationException("Unknown original controller clip: " + overrides[i].Key.name);
                        overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, replacement);
                    }
                    controller.ApplyOverrides(overrides); EditorUtility.SetDirty(controller);
                    var profile = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(DosaV2PlayableDraftBuilder.VisualProfilePath);
                    if (profile == null) throw new InvalidOperationException("Install the V2 playable draft before assigning its override controller.");
                    profile.Controller = controller; EditorUtility.SetDirty(profile); report.applied = true;
                }
                AssetDatabase.SaveAssets();
                report.status = full ? "EXISTING_MOTIONS_RETARGETED_DRAFT" : "TWO_CLIP_TRIAL_READY_NOT_APPLIED";
            }
            catch (Exception e) { report.status = "RETARGET_PREPARATION_FAILED"; report.error = e.GetBaseException().ToString(); }
            finally
            {
                report.originalFilesUnchanged = originals.All(kv => File.Exists(kv.Key) && Sha(kv.Key) == kv.Value);
                if (!report.originalFilesUnchanged) { report.status = "SOURCE_MUTATION_DETECTED"; report.error += " Original V1 file hash changed."; }
            }
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(report.backupDirectory, "report.json"), json);
            string outDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2")); Directory.CreateDirectory(outDirectory);
            File.WriteAllText(Path.Combine(outDirectory, full ? "motion-retarget-apply.json" : "motion-retarget-trial.json"), json);
            return json;
        }

        private static Avatar BuildSourceAvatar(Report report)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject model = null;
            try
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaPlayerBuilder.WorldModel), scene);
                model.transform.position = Vector3.zero;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = null;
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform skeletonRoot = hips.parent;
                if (skeletonRoot == null || skeletonRoot == model.transform) throw new InvalidOperationException("Expected V1 armature parent matching animation FBX root.");
                report.sourceRoot = skeletonRoot.name; report.sourceRootRotation = skeletonRoot.localRotation;
                var description = DosaV2PlayerBuilder.HumanDescriptionFor(skeletonRoot.gameObject);
                var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AvatarSetupTool", true);
                var mapped = description.human.ToDictionary(h => h.humanName, h => h.boneName);
                var transforms = skeletonRoot.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => true);
                var get = type.GetMethod("GetHumanBones", Static, null, new[] { typeof(Dictionary<string, string>), typeof(Dictionary<Transform, bool>) }, null);
                if (get == null) throw new MissingMethodException(type.FullName, "GetHumanBones");
                object wrappers = get.Invoke(null, new object[] { mapped, transforms });
                MethodInfo Find(string name) => type.GetMethod(name, Static, null, new[] { wrappers.GetType() }, null)
                    ?? throw new MissingMethodException(type.FullName, name);
                var error = Find("GetPoseError"); report.sourcePoseErrorBefore = (float)error.Invoke(null, new[] { wrappers });
                Find("MakePoseValid").Invoke(null, new[] { wrappers });
                report.sourcePoseErrorAfter = (float)error.Invoke(null, new[] { wrappers });
                report.sourcePoseValid = (bool)Find("IsPoseValid").Invoke(null, new[] { wrappers });
                if (!report.sourcePoseValid) throw new InvalidOperationException("Unity Enforce T-Pose did not produce a valid source pose.");
                // Preserve the actual armature hierarchy/root-axis conversion. Only the clone's pose was aligned by Unity.
                description = DosaV2PlayerBuilder.HumanDescriptionFor(skeletonRoot.gameObject);
                var generated = AvatarBuilder.BuildHumanAvatar(skeletonRoot.gameObject, description);
                report.sourceAvatarValid = generated != null && generated.isValid && generated.isHuman;
                if (!report.sourceAvatarValid) { if (generated != null) Object.DestroyImmediate(generated); throw new InvalidOperationException("Aligned source Avatar is invalid."); }
                generated.name = "AV_DosaV1_TPoseSource";
                var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
                if (existing == null) { AssetDatabase.CreateAsset(generated, AvatarPath); existing = generated; }
                else { EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); }
                AssetDatabase.SaveAssets(); return existing;
            }
            finally { if (model != null) Object.DestroyImmediate(model); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static Pose SampleV2(string name)
        {
            var scene = EditorSceneManager.NewPreviewScene(); GameObject model = null; PlayableGraph graph = default;
            try
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.WorldModel), scene);
                model.transform.position = Vector3.zero;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                var animator = model.GetComponent<Animator>(); animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var clip = Clip(TargetPath(name)); graph = PlayableGraph.Create("V2 corrected source trial"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, name, animator); output.SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(.25); graph.Evaluate(0f);
                Vector3 Position(HumanBodyBones bone) { var t = animator.GetBoneTransform(bone); return t != null ? animator.transform.InverseTransformPoint(t.position) : Vector3.zero; }
                return new Pose { clip = name, humanMotion = clip.isHumanMotion,
                    transformCurves = AnimationUtility.GetCurveBindings(clip).Count(b => b.type == typeof(Transform)),
                    leftShoulder = Position(HumanBodyBones.LeftUpperArm), leftElbow = Position(HumanBodyBones.LeftLowerArm), leftHand = Position(HumanBodyBones.LeftHand),
                    hips = Position(HumanBodyBones.Hips), leftKnee = Position(HumanBodyBones.LeftLowerLeg), leftFoot = Position(HumanBodyBones.LeftFoot) };
            }
            finally { if (graph.IsValid()) graph.Destroy(); if (model != null) Object.DestroyImmediate(model); EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static string TargetPath(string name) => Folder + "/A_DosaCourier_" + name + ".fbx";
        private static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)) ?? throw new InvalidOperationException("Missing clip: " + path);
        private static void Backup(Report report, string path)
        {
            foreach (string file in new[] { path, path + ".meta" })
            {
                if (!File.Exists(file)) continue; string target = Path.Combine(report.backupDirectory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, false);
            }
        }
        private static string Sha(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
    }
}
