using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Uses the existing rig's extraction/calibration helpers, but all output paths are new.
    public static partial class WorldMacroPlayerReRigAuthoring
    {
        static bool _recoveryHand;
        public static string GripAFolder => _recoveryHand ? "Assets/_Project/Art/Characters/PlaytestRecoveryGripA" : "Assets/_Project/Art/Characters/PlaytestPolish/GripA";
        public static string GripAModel => GripAFolder + "/Player_C02_GripA.fbx";
        public static string GripAProfile => GripAFolder + "/PlayerGesture_GripA.asset";
        public static string GripAPrefab => GripAFolder + "/PF_Player_C02_GripA.prefab";
        static string GripASource => Path.GetFullPath(Path.Combine(Application.dataPath, _recoveryHand?"../../Art/PlaytestRecovery/Hands":"../../Art/PlaytestPolish/Hands"));
        public static string ExecuteRecoveryGrip(string command)
        {
            _recoveryHand=true;
            try
            {
                if(command=="apply"||command=="import")
                {
                    string result=ExecuteGripA(command);var profile=AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GripAProfile);
                    profile.PreserveAnimatedCarry=true;profile.AnatomicalNearReach=true;profile.MaximumNearShoulderCorrection=.05f;
                    profile.NearMinimumDepth=.7f;profile.NearMaximumDepth=1.55f;profile.NearTipDepth=1.25f;profile.DrawingBrushRoll=-162;
                    EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();return result;
                }
                return ExecuteGripARuntime(command);
            }
            finally{_recoveryHand=false;}
        }
        [Serializable] private sealed class BoundSkinSource
        { public string renderer, assetPath, sha256; public bool nearArm; public int vertices, triangles; }
        private static BoundSkinSource[] ReadBoundSkinSources(WorldMacroPlayerGestureRig rig)
        {
            Transform near = Find(rig.transform, "C02_NearArm");
            return rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).Select(s =>
            {
                string path = AssetDatabase.GetAssetPath(s.sharedMesh);
                return new BoundSkinSource { renderer = s.name, assetPath = path, sha256 = File.Exists(path) ? Sha(path) : "RUNTIME_MESH_NO_ASSET_HASH",
                    nearArm = near != null && s.transform.IsChildOf(near), vertices = s.sharedMesh.vertexCount, triangles = s.sharedMesh.triangles.Length / 3 };
            }).ToArray();
        }
        private static string ActiveSkinSourcePath(WorldMacroPlayerGestureRig rig)
            => ReadBoundSkinSources(rig).FirstOrDefault(s => s.renderer == "C02_Mesh_0" && !s.nearArm)?.assetPath ?? "UNVERIFIED_ACTIVE_MODEL";
        private static string ActiveSkinSourceHash(WorldMacroPlayerGestureRig rig)
        { string path = ActiveSkinSourcePath(rig); return File.Exists(path) ? Sha(path) : "UNVERIFIED_ACTIVE_MODEL"; }

        public static string ExecuteGripARuntime(string command)
        {
            Need(EditorApplication.isPlaying && !EditorApplication.isCompiling, "A runtime audit requires existing Play mode.");
            var rig = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Need(rig != null && rig.IsPresentationReady && ActiveSkinSourcePath(rig) == GripAModel && AssetDatabase.GetAssetPath(rig.Profile) == GripAProfile,
                "The current actor must actually use the selected A body mesh and A profile.");
            var surface = JsonUtility.FromJson<GripASurface>(File.ReadAllText(Path.Combine(GripASource, "Validation/hand_surface_sampling.json")));
            Need(surface.sourceFbxSha256 == ActiveSkinSourceHash(rig), "Current A mesh no longer matches its own static report; rerun the relevant source checks.");
            string output = Path.Combine(GripASource, "Validation/Unity");
            string result;
            if (command == "contact") result = ValidateCurrentSkinContact(output);
            else if (command == "check") result = RuntimeGestureQa(null, output, true);
            else if (command.StartsWith("capture:", StringComparison.Ordinal)) result = RuntimeGestureQa(command.Substring(8), output, true);
            else throw new ArgumentException("Expected check, contact or capture:center/top-left/top-right/bottom-left/bottom-right/sit-carry.");
            if (command == "check" || command == "contact") WriteGripAEvidenceStamp(rig, command);
            return result;
        }
        [Serializable] sealed class GripASurface
        { public string status, sourceFbxSha256; public float maximum_sampled_penetration_m; public int samples, ray_misses; }
        [Serializable] sealed class GripAResult
        {
            public string status, model, profile, prefab, sourceSha256;
            public bool humanoid, sceneApplied, existingControllerPreserved;
            public int fingers, bodyTriangles, nearTriangles, samples;
            public float sampledStaticPenetrationMeters;
            public string[] unverified = { "Current Unity skin/contact and stroke endpoint accuracy require fresh runtime checks.",
                "Selected A image is an appearance reference, not proof of exact image reproduction.", "Sitting terrain/cloth contact and native user input remain unverified." };
        }

        public static string ExecuteGripA(string command)
        {
            Need(!EditorApplication.isPlaying && !EditorApplication.isCompiling, "Grip A import/apply requires idle Edit mode.");
            if (command == "syncprefab") return SyncGripAVisualPrefab();
            string input = Path.Combine(GripASource, "Exports/Player_C02_GripA.fbx");
            var surface = JsonUtility.FromJson<GripASurface>(File.ReadAllText(Path.Combine(GripASource, "Validation/hand_surface_sampling.json")));
            Need(surface.status == "PASS_SAMPLED_STATIC_SURFACE" && surface.maximum_sampled_penetration_m <= .0005f && surface.ray_misses == 0,
                "The NEW A mesh requires its own sampled triangle contact result.");
            Need(surface.sourceFbxSha256 == Sha(input), "Selected A surface report is stale for the exported FBX.");
            EnsureFolder(GripAFolder); EnsureFolder(GripAFolder + "/Near");
            bool apply = string.Equals(command, "apply", StringComparison.OrdinalIgnoreCase);
            Need(apply || string.Equals(command, "import", StringComparison.OrdinalIgnoreCase), "Expected import or apply.");
            if (!File.Exists(GripAModel)) Need(AssetDatabase.CopyAsset(ModelPath, GripAModel), "Could not copy the existing verified importer settings.");
            File.Copy(input, GripAModel, true);
            AssetDatabase.ImportAsset(GripAModel, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(GripAModel);
            var importerReference = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var restSource = AssetDatabase.LoadAssetAtPath<GameObject>(GripAModel);
            var human = importerReference.humanDescription;
            human.skeleton = restSource.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
                { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            importer.humanDescription = human; importer.SaveAndReimport();
            var imported = AssetDatabase.LoadAssetAtPath<GameObject>(GripAModel);
            Need(imported != null, "Grip A model import failed.");
            var candidate = Object.Instantiate(imported); candidate.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = candidate.GetComponent<Animator>();
                Need(animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman, "The copied Humanoid importer must remain valid.");
                animator.enabled = false; animator.runtimeAnimatorController = null;
                var baseline = AssetDatabase.LoadAssetAtPath<WorldMacroPlayerGestureProfile>(GestureProfilePath);
                var profile = CopyAsset(baseline, GripAProfile); profile.name = "PlayerGesture_GripA";
                var poses = new List<WorldMacroPlayerGestureProfile.FingerPose>();
                foreach (var old in baseline.Fingers)
                {
                    Transform bone = Find(candidate.transform, old.BoneName), grip = Find(candidate.transform, "POSE_Grip_" + old.BoneName), relax = Find(candidate.transform, "POSE_Relax_" + old.BoneName);
                    Need(bone != null && grip != null && relax != null && grip.parent == bone, "Missing neutral A calibration marker: " + old.BoneName);
                    bool right = old.BoneName.StartsWith("Right", StringComparison.Ordinal);
                    poses.Add(new WorldMacroPlayerGestureProfile.FingerPose { BoneName = old.BoneName, UseExplicitRestPose = true,
                        RestLocalRotation = bone.localRotation, CarryOffset = right ? grip.localRotation : relax.localRotation,
                        DrawingOffset = right ? grip.localRotation : relax.localRotation, HarvestOffset = right ? grip.localRotation : relax.localRotation,
                        ReleasedOffset = relax.localRotation });
                }
                Need(poses.Count == 30, "All 30 finger segments must be retained.");
                profile.Fingers = poses.ToArray();
                Transform hand = Find(candidate.transform, "RightHand"), socket = Find(candidate.transform, "GripReference_R");
                Need(hand != null && socket != null, "A shaft calibration is missing.");
                profile.RightHandGripPosition = hand.InverseTransformPoint(socket.position);
                profile.RightHandGripRotation = Quaternion.Inverse(hand.rotation) * socket.rotation;
                profile.DrawingBrushRoll = -72f; profile.UseSeatedCarry = true;
                EditorUtility.SetDirty(profile);
                var sources = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(s => s.name);
                var nearMeshes = new Dictionary<string, Mesh>();
                foreach (var pair in sources)
                {
                    var mesh = ExtractRightArm(pair.Value); if (mesh == null) continue;
                    string path = GripAFolder + "/Near/SM_" + pair.Key + "_GripA.asset";
                    var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                    else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
                    nearMeshes.Add(pair.Key, mesh);
                }
                var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    ApplyGripATo(prefabRoot, sources, nearMeshes, profile);
                    Need(PrefabUtility.SaveAsPrefabAsset(prefabRoot, GripAPrefab) != null, "Saving A derivative prefab failed.");
                }
                finally { PrefabUtility.UnloadPrefabContents(prefabRoot); }
                if (apply)
                {
                    Need(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == WorldMacroPlaytestAuthoring.ScenePath, "Apply only targets the existing playtest scene.");
                    var appearances = Object.FindObjectsByType<WorldMacroPlayerAppearance>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Where(a => a.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene() && a.GetComponent<WorldMacroPlayerGestureRig>() != null).ToArray();
                    Need(appearances.Length == 1, "Expected one current player appearance; do not replace or duplicate actors.");
                    Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Apply selected A hand derivative");
                    try
                    {
                        ApplyGripATo(appearances[0].gameObject, sources, nearMeshes, profile);
                        EditorSceneManager.MarkSceneDirty(appearances[0].gameObject.scene);
                        Need(EditorSceneManager.SaveScene(appearances[0].gameObject.scene), "Could not save hand-only scene update.");
                        Undo.CollapseUndoOperations(undo);
                    }
                    catch { Undo.RevertAllDownToGroup(undo); throw; }
                }
                AssetDatabase.SaveAssets();
                var result = new GripAResult { status = apply ? "A_HAND_APPLIED_RUNTIME_QA_PENDING" : "A_HAND_IMPORTED_RUNTIME_QA_PENDING", model = GripAModel,
                    profile = GripAProfile, prefab = GripAPrefab, sourceSha256 = Sha(input), humanoid = true, fingers = poses.Count,
                    bodyTriangles = sources.Values.Sum(s => s.sharedMesh.triangles.Length / 3), nearTriangles = nearMeshes.Values.Sum(m => m.triangles.Length / 3),
                    sceneApplied = apply, existingControllerPreserved = true, samples = surface.samples, sampledStaticPenetrationMeters = surface.maximum_sampled_penetration_m };
                string json = JsonUtility.ToJson(result, true); File.WriteAllText(Path.Combine(GripASource, "Validation/unity_" + command + ".json"), json); return json;
            }
            finally { Object.DestroyImmediate(candidate); }
        }

        static void ApplyGripATo(GameObject root, Dictionary<string, SkinnedMeshRenderer> sources, Dictionary<string, Mesh> nearMeshes, WorldMacroPlayerGestureProfile profile)
        {
            var animator = root.GetComponent<Animator>(); var controller = animator.runtimeAnimatorController;
            Transform near = Find(root.transform, "C02_NearArm");
            foreach (var target in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!sources.TryGetValue(target.name, out var source)) continue; // Actual brush remains unchanged.
                bool isNear = near != null && target.transform.IsChildOf(near); Transform skeleton = isNear ? near : root.transform;
                Mesh mesh = isNear ? nearMeshes.GetValueOrDefault(source.name) : source.sharedMesh;
                if (mesh == null) continue;
                Transform[] bones = source.bones.Select(b => Find(skeleton, b.name)).ToArray();
                Need(bones.All(b => b != null), "A mesh cannot map to the fixed current skeleton.");
                for (int i = 0; i < bones.Length; i++)
                    Need(Vector3.Distance(bones[i].localPosition, source.bones[i].localPosition) < .0001f && Quaternion.Angle(bones[i].localRotation, source.bones[i].localRotation) < .01f,
                        "Current skeleton is not in the same neutral bind pose: " + bones[i].name);
                Undo.RecordObject(target, "Apply A hand skin"); target.sharedMesh = mesh; target.bones = bones;
                target.rootBone = Find(skeleton, source.rootBone.name); EditorUtility.SetDirty(target);
            }
            var gesture = root.GetComponent<WorldMacroPlayerGestureRig>(); Need(gesture != null, "Current player has no gesture rig.");
            Undo.RecordObject(gesture, "Apply selected A calibration");
            var serialized = new SerializedObject(gesture); serialized.FindProperty("_profile").objectReferenceValue = profile; serialized.ApplyModifiedProperties();
            Need(gesture.TryBind(), "A hand presentation failed to bind: " + gesture.BindingError); EditorUtility.SetDirty(gesture);
            Need(animator.runtimeAnimatorController == controller, "Hand-only apply unexpectedly changed the motion controller.");
        }
    }
}
