using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Data;
using Oheangbu.Drawing;
using Oheangbu.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    /// <summary>User-authorized playable draft: reuse the existing gameplay and motion controller.</summary>
    public static class DosaV2PlayableDraftBuilder
    {
        public const string VisualPrefabPath = "Assets/_Project/Prefabs/Player/PF_DosaV2_PlayableDraft.prefab";
        public const string VisualProfilePath = "Assets/_Project/Data/PlayerV2/PlayerVisual_DosaV2_PlayableDraft.asset";
        private const int NearLayer = 31;
        private const string VisualName = "DosaVisual";
        [Serializable] private sealed class FileRecord { public string path, sha256, backup; }
        [Serializable] private sealed class InstallRecord
        {
            public string owner, gameplayBefore, gameplayAfter;
            public bool gameplayUnchanged, reusedVisual;
            public int worldCloth, secondaryRigs, bodyCapsules;
        }
        [Serializable] private sealed class Report
        {
            public string status, error, backupDirectory, controller, activeScene;
            public string authorization = "The user explicitly requested installing the current V2 model as the player. This is a playable draft using the existing controller; it is not RIG_PASS or approval of newly authored production animation.";
            public string scope = "Separate V2 playable visual/profile, shared PlayerRig and canonical C2 only. Gameplay input, movement speed, camera pose/FOV and drawing recognition remain unchanged. Existing Humanoid clips are reused at runtime; no clip/model generation or importer retarget mutation.";
            public List<FileRecord> sources = new List<FileRecord>(), backups = new List<FileRecord>();
            public List<InstallRecord> installations = new List<InstallRecord>();
            public string[] limitations = { "Existing V1 motion retarget on the V2 Avatar is a draft and requires actual Play inspection.",
                "Cloth, decoration, wrist lining and overall visual quality are not certified by installation.",
                "Latest authoring review bindings are copied; this helper does not rebuild or weaken their collision/attachment constraints." };
        }

        [MenuItem("Oheangbu/Player V2/Install playable draft in C2 and shared rig")]
        private static void InstallMenu() => Debug.Log(InstallDraft());

        public static string InstallDraft()
        {
            RequireEditAndSavedScenes();
            var report = new Report { status = "INSTALLING_PLAYABLE_DRAFT", controller = DosaPlayerBuilder.ControllerPath };
            PrepareBackup(report, new[] { DevSceneKit.RigPrefabPath, CodexWorldSceneBuilder.ScenePath, VisualPrefabPath,
                VisualProfilePath, "ProjectSettings/TagManager.asset" });
            try
            {
                BuildVisualAsset(report);
                var shared = PrefabUtility.LoadPrefabContents(DevSceneKit.RigPrefabPath);
                try
                {
                    report.installations.Add(InstallInRig(shared, "shared PlayerRig"));
                    if (PrefabUtility.SaveAsPrefabAsset(shared, DevSceneKit.RigPrefabPath) == null)
                        throw new IOException("Could not save the shared PlayerRig draft.");
                }
                finally { PrefabUtility.UnloadPrefabContents(shared); }
                AssetDatabase.SaveAssets();
                var scene = EditorSceneManager.OpenScene(CodexWorldSceneBuilder.ScenePath, OpenSceneMode.Single);
                var motors = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<PlayerMotor>(true)).ToArray();
                Need(motors.Length == 1, "Canonical C2 must contain exactly one PlayerMotor.");
                report.installations.Add(InstallInRig(motors[0].transform.root.gameObject, "canonical C2"));
                EditorSceneManager.MarkSceneDirty(scene);
                Need(EditorSceneManager.SaveScene(scene), "Could not save canonical C2.");
                AssetDatabase.SaveAssets();
                report.activeScene = scene.path; report.status = "PLAYABLE_DRAFT_INSTALLED_NOT_RIG_PASS";
            }
            catch (Exception e) { report.status = "PLAYABLE_DRAFT_INSTALL_FAILED"; report.error = e.GetBaseException().Message; }
            return SaveReport(report);
        }

        /// <summary>Optional asset-only preparation. InstallDraft already calls this internally.</summary>
        public static string BuildPlayableDraftAsset()
        {
            RequireEditAndSavedScenes();
            var report = new Report { status = "BUILDING_PLAYABLE_DRAFT", controller = DosaPlayerBuilder.ControllerPath };
            PrepareBackup(report, new[] { VisualPrefabPath, VisualProfilePath });
            try { BuildVisualAsset(report); report.status = "PLAYABLE_DRAFT_ASSET_READY_NOT_RIG_PASS"; }
            catch (Exception e) { report.status = "PLAYABLE_DRAFT_BUILD_FAILED"; report.error = e.GetBaseException().Message; }
            return SaveReport(report);
        }

        private static void BuildVisualAsset(Report report)
        {
            var review = AssetDatabase.LoadAssetAtPath<GameObject>(DosaV2PlayerBuilder.ValidationPrefab);
            var sourceVisual = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(DosaV2PlayerBuilder.VisualProfilePath);
            var drawing = AssetDatabase.LoadAssetAtPath<DrawingPoseProfileSO>(DosaV2PlayerBuilder.DrawingProfilePath);
            var controller = SelectMotionController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DosaPlayerBuilder.ControllerPath));
            Need(review != null && sourceVisual != null && drawing != null && controller != null,
                "Build/import the current V2 review prefab and keep the existing Dosa controller available first.");
            report.controller = AssetDatabase.GetAssetPath(controller);
            foreach (string path in new[] { DosaV2PlayerBuilder.ValidationPrefab, DosaV2PlayerBuilder.WorldModel,
                DosaV2PlayerBuilder.ArmsModel, DosaV2PlayerBuilder.BrushModel, DosaV2PlayerBuilder.VisualProfilePath,
                DosaV2PlayerBuilder.DrawingProfilePath, DosaPlayerBuilder.ControllerPath }) AddSource(report, path);
            DevSceneKit.EnsureFolder(Path.GetDirectoryName(VisualProfilePath).Replace('\\', '/'));
            var visual = AssetDatabase.LoadAssetAtPath<PlayerVisualProfileSO>(VisualProfilePath);
            if (visual == null)
            {
                visual = Object.Instantiate(sourceVisual); visual.name = "PlayerVisual_DosaV2_PlayableDraft";
                AssetDatabase.CreateAsset(visual, VisualProfilePath);
            }
            else EditorUtility.CopySerialized(sourceVisual, visual);
            // Keep isolated authoring profiles/controller-free review assets unchanged.
            visual.name = "PlayerVisual_DosaV2_PlayableDraft"; visual.Controller = controller;
            visual.RelaxedBrushCarry = true;
            EditorUtility.SetDirty(visual);
            var root = PrefabUtility.LoadPrefabContents(DosaV2PlayerBuilder.ValidationPrefab);
            try
            {
                var world = DirectChild(root, "DosaV2_World_Rest");
                var near = DirectChild(root, "DosaV2_NearArms_Rest");
                var brush = DirectChild(root, "DosaV2_Brush_Rest");
                var worldAnimator = world.GetComponent<Animator>(); var nearAnimator = near.GetComponent<Animator>();
                Need(new[] { worldAnimator, nearAnimator }.All(a => a != null && a.avatar != null && a.avatar.isValid && a.isHuman),
                    "Current world and near imports must expose valid Humanoid Avatars for existing controller reuse.");
                Need(world.GetComponent<PlayerSecondaryMotionRig>() != null && near.GetComponent<PlayerSecondaryMotionRig>() != null,
                    "Current review prefab must contain its explicit world/near secondary bindings.");
                Need(brush.GetComponent<BrushBristleRig>() != null, "Current brush deformation binding is missing.");
                root.name = VisualName;
                world.name = "WorldBody"; near.name = "NearArms"; brush.name = "WorldBrush";
                foreach (var part in new[] { world, near, brush })
                { part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity; }
                // Late IK and the camera-relative arms must update skin bounds even when the imported rest bounds are outside the view.
                foreach (var skin in world.GetComponentsInChildren<SkinnedMeshRenderer>(true).Concat(near.GetComponentsInChildren<SkinnedMeshRenderer>(true)))
                    skin.updateWhenOffscreen = true;
                var nearBrush = Object.Instantiate(brush, root.transform, false); nearBrush.name = "NearBrush";
                foreach (var transform in near.GetComponentsInChildren<Transform>(true).Concat(nearBrush.GetComponentsInChildren<Transform>(true)))
                    transform.gameObject.layer = NearLayer;
                foreach (var renderer in near.GetComponentsInChildren<Renderer>(true).Concat(nearBrush.GetComponentsInChildren<Renderer>(true)))
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                // Existing world brush proxy references keep pointing at the same world brush object.
                DosaV2PerformanceBuilder.ConfigurePlayable(root);
                var rig = root.GetComponent<PlayerVisualRig>() ?? root.AddComponent<PlayerVisualRig>();
                rig.Configure(worldAnimator, nearAnimator, brush.transform, nearBrush.transform, visual, drawing);
                foreach (var animator in new[] { worldAnimator, nearAnimator })
                {
                    animator.runtimeAnimatorController = controller; animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
                }
                DevSceneKit.EnsureFolder(Path.GetDirectoryName(VisualPrefabPath).Replace('\\', '/'));
                Need(PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath) != null, "Could not save playable visual draft.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static RuntimeAnimatorController SelectMotionController(RuntimeAnimatorController original)
        {
            const string folder = "Assets/_Project/Art/Characters/DosaV2/Animations";
            var candidate = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(folder + "/AOC_DosaV2_ExistingMotions.overrideController");
            if (original == null || candidate == null || candidate.runtimeAnimatorController != original) return original;
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(); candidate.GetOverrides(overrides);
            if (overrides.Count != DosaPlayerBuilder.MotionNames.Length) return original;
            foreach (string name in DosaPlayerBuilder.MotionNames)
            {
                var source = AssetDatabase.LoadAllAssetsAtPath(DosaPlayerBuilder.ClipPath(name)).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
                if (source == null) return original;
                var matches = overrides.Where(pair => pair.Key == source).ToArray();
                if (matches.Length != 1 || matches[0].Value == null || !matches[0].Value.isHumanMotion
                    || AssetDatabase.GetAssetPath(matches[0].Value) != folder + "/A_DosaCourier_" + name + ".fbx") return original;
            }
            return candidate;
        }

        public static string InstallJourneyVisual(GameObject rig)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || rig==null || rig.scene.path!=PineRestGameBuilder.Scene)throw new InvalidOperationException("Journey edit rig required");
            InstallInRig(rig,rig.scene.path);
            return "Existing Dosa V2 playable visual installed in Journey instance";
        }

        private static InstallRecord InstallInRig(GameObject playerRoot, string owner)
        {
            var motor = playerRoot.GetComponentInChildren<PlayerMotor>(true);
            Need(motor != null, "PlayerMotor missing in " + owner);
            var controller = motor.GetComponent<CharacterController>(); Need(controller != null, "CharacterController missing.");
            string before = GameplaySignature(playerRoot);
            var choices = motor.GetComponentsInChildren<PlayerVisualRig>(true).Where(v => v.transform.parent == motor.transform && v.name == VisualName).ToArray();
            PlayerVisualRig visual = choices.FirstOrDefault(v => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(v.gameObject) == VisualPrefabPath);
            foreach (var old in choices) if (old != visual) Object.DestroyImmediate(old.gameObject);
            bool reused = visual != null;
            if (visual == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath), motor.transform);
                visual = instance.GetComponent<PlayerVisualRig>();
            }
            Need(visual != null, "Playable visual binding is missing.");
            visual.name = VisualName;
            visual.transform.localPosition = Vector3.up * (controller.center.y - controller.height * .5f - controller.skinWidth);
            visual.transform.localRotation = Quaternion.identity; Record(visual.transform);
            foreach (string name in new[] { "Body", "BrushProp" })
            { var legacy = motor.transform.Find(name); if (legacy != null) { legacy.gameObject.SetActive(false); Record(legacy.gameObject); } }
            var main = playerRoot.GetComponentsInChildren<Camera>(true).SingleOrDefault(c => c.CompareTag("MainCamera"));
            Need(main != null, "Exactly one existing MainCamera is required.");
            EnsureOverlay(main, visual);
            var nearRoot = visual.transform.Find("NearArms");
            Need(nearRoot != null, "Playable near representation root is missing.");
            foreach (var secondary in visual.GetComponentsInChildren<PlayerSecondaryMotionRig>(true))
            {
                // World inertia/gravity remain in world space; only the viewmodel follows the camera basis.
                secondary.SetCameraReference(secondary.transform == nearRoot || secondary.transform.IsChildOf(nearRoot) ? main : null);
                Record(secondary);
            }
            foreach (var lod in visual.GetComponentsInChildren<PlayerLodClothController>(true))
            { lod.SetViewCamera(main); Record(lod); }
            // A scene can retain an old added-driver override after the shared asset gains the new visual.
            var drivers = motor.GetComponents<PlayerVisualDriver>();
            if (drivers.Length > 1)
                foreach (var driver in drivers.Where(PrefabUtility.IsAddedComponentOverride)) Object.DestroyImmediate(driver);
            drivers = motor.GetComponents<PlayerVisualDriver>(); Need(drivers.Length <= 1, "Ambiguous player visual drivers.");
            var activeDriver = drivers.FirstOrDefault() ?? motor.gameObject.AddComponent<PlayerVisualDriver>();
            activeDriver.Configure(visual, controller, motor, playerRoot.GetComponentInChildren<DodgeAction>(true), playerRoot.GetComponentInChildren<LockOn>(true),
                playerRoot.GetComponentInChildren<DrawingInputController>(true), playerRoot.GetComponentInChildren<CameraRigController>(true), playerRoot.GetComponentInChildren<BrushStrokeFeedAdapter>(true));
            Record(activeDriver);
            foreach (var effect in playerRoot.GetComponentsInChildren<HarvestInkStreamEffect>(true))
            {
                var serialized = new SerializedObject(effect); var sink = serialized.FindProperty("_sinkAnchor");
                Need(sink != null, "Harvest effect sink field changed."); sink.objectReferenceValue = visual.EffectTip;
                serialized.ApplyModifiedPropertiesWithoutUndo(); Record(effect);
            }
            string after = GameplaySignature(playerRoot); Need(before == after, "Unexpected gameplay/component/camera-pose mutation in " + owner);
            return new InstallRecord { owner = owner, gameplayBefore = before, gameplayAfter = after, gameplayUnchanged = true, reusedVisual = reused,
                worldCloth = visual.GetComponentsInChildren<Cloth>(true).Length, secondaryRigs = visual.GetComponentsInChildren<PlayerSecondaryMotionRig>(true).Length,
                bodyCapsules = visual.GetComponentsInChildren<CapsuleCollider>(true).Length };
        }

        private static void EnsureOverlay(Camera main, PlayerVisualRig rig)
        {
            var tag = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var entry = tag.FindProperty("layers").GetArrayElementAtIndex(NearLayer);
            Need(string.IsNullOrEmpty(entry.stringValue) || entry.stringValue == "DosaViewmodel", "Layer31 belongs to another purpose.");
            entry.stringValue = "DosaViewmodel"; tag.ApplyModifiedPropertiesWithoutUndo();
            var cameras = main.GetComponentsInChildren<Camera>(true).Where(c => c != main && c.name == "DosaArmsCamera").ToArray();
            var overlay = cameras.FirstOrDefault(c => !PrefabUtility.IsAddedGameObjectOverride(c.gameObject)) ?? cameras.FirstOrDefault();
            foreach (var old in cameras) if (old != overlay) Object.DestroyImmediate(old.gameObject);
            if (overlay == null)
            {
                var obj = new GameObject("DosaArmsCamera"); obj.transform.SetParent(main.transform, false); overlay = obj.AddComponent<Camera>();
            }
            overlay.transform.localPosition = Vector3.zero; overlay.transform.localRotation = Quaternion.identity;
            overlay.cullingMask = 1 << NearLayer; overlay.clearFlags = CameraClearFlags.Depth;
            overlay.allowHDR = main.allowHDR; overlay.allowMSAA = main.allowMSAA;
            var overlayData = overlay.GetUniversalAdditionalCameraData(); overlayData.renderType = CameraRenderType.Overlay;
            var serialized = new SerializedObject(overlayData); serialized.FindProperty("m_ClearDepth").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
            overlayData.renderPostProcessing = false; overlayData.renderShadows = false;
            main.cullingMask &= ~(1 << NearLayer); var data = main.GetUniversalAdditionalCameraData();
            data.cameraStack.RemoveAll(c => c == null); if (!data.cameraStack.Contains(overlay)) data.cameraStack.Add(overlay);
            rig.ConfigureViewCamera(overlay);
            foreach (Object value in new Object[] { main, data, overlay, overlay.transform, overlayData, rig }) Record(value);
        }

        private static string GameplaySignature(GameObject root)
        {
            Type[] types = { typeof(PlayerMotor), typeof(CharacterController), typeof(DodgeAction), typeof(LockOn),
                typeof(DrawingInputController), typeof(BrushStrokeFeedAdapter), typeof(CameraRigController) };
            var parts = root.GetComponentsInChildren<Component>(true).Where(c => c != null && types.Contains(c.GetType()))
                .Select(c => c.GetType().FullName + ":" + EditorJsonUtility.ToJson(c)).ToList();
            foreach (var camera in root.GetComponentsInChildren<Camera>(true).Where(c => c.CompareTag("MainCamera")))
                parts.Add(camera.transform.localPosition.ToString("R") + camera.transform.localRotation.ToString("R") + camera.fieldOfView.ToString("R"));
            return ShaText(string.Join("\n", parts));
        }
        private static GameObject DirectChild(GameObject root, string name)
        { var child = root.transform.Find(name); Need(child != null, "Latest review prefab child missing: " + name); return child.gameObject; }
        private static void Record(Object value)
        { EditorUtility.SetDirty(value); if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
        private static void RequireEditAndSavedScenes()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode before installing the draft.");
            for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Save the current edited scene before installation; unsaved work is not discarded.");
        }
        private static void PrepareBackup(Report report, IEnumerable<string> paths)
        {
            report.backupDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Backups/PlayableDraft_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
            Directory.CreateDirectory(report.backupDirectory);
            foreach (string path in paths.Distinct()) foreach (string file in new[] { path, path + ".meta" })
            {
                if (!File.Exists(file)) continue; string target = Path.Combine(report.backupDirectory, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target, false);
                report.backups.Add(new FileRecord { path = file, sha256 = ShaFile(file), backup = target });
            }
        }
        private static void AddSource(Report report, string path)
        { Need(File.Exists(path), "Source file missing: " + path); report.sources.Add(new FileRecord { path = path, sha256 = ShaFile(path) }); }
        private static string SaveReport(Report report)
        {
            string json = JsonUtility.ToJson(report, true); string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/PlayerV2/Inspect/playable-draft-install.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json);
            if (!string.IsNullOrEmpty(report.backupDirectory)) File.WriteAllText(Path.Combine(report.backupDirectory, "installation.json"), json);
            return json;
        }
        private static string ShaFile(string path) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(File.ReadAllBytes(path))); }
        private static string ShaText(string text) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))); }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
